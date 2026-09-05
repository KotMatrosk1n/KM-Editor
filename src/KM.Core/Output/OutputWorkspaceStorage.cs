// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.Core.Output;

/// <summary>Durable output metadata, separated by exact game and physical output location.</summary>
public sealed class OutputWorkspaceStorage
{
    public const string MigrationDiagnosticCode = "KM-PROJECT-OUTPUT-MIGRATION-BLOCKED";
    public const string WorkingDirectoryName = ".km-working";
    private const int MaximumEntries = 100_000;
    private const long MaximumBytes = 16L * 1024 * 1024 * 1024;
    private readonly ProjectPaths projectPaths;
    private readonly string appDataRoot;
    private readonly string root;
    private readonly string gameRoot;
    private readonly string legacyRoot;
    private readonly string workspaceKey;

    public OutputWorkspaceStorage(ProjectPaths paths, string? appDataRoot = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.SelectedGame is not { } game || !Enum.IsDefined(game)
            || string.IsNullOrWhiteSpace(paths.OutputRootPath) || !Path.IsPathFullyQualified(paths.OutputRootPath))
            throw new ArgumentException("Output storage requires an exact game and absolute output root.");
        projectPaths = paths;
        OutputRoot = OutputMetadataNamespace.NormalizeOutputRoot(paths.OutputRootPath);
        RequireSafe(OutputRoot);
        var normalized = OperatingSystem.IsWindows() ? OutputRoot.ToUpperInvariant() : OutputRoot;
        var rootKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        appDataRoot ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KM Editor");
        if (!Path.IsPathFullyQualified(appDataRoot)) throw new OutputPathSecurityException();
        this.appDataRoot = appDataRoot;
        root = Path.Combine(appDataRoot, "Workspaces", "Output", rootKey);
        gameRoot = Path.Combine(root, game.ToString());
        workspaceKey = game + ":" + rootKey;
        MetadataRoot = Path.Combine(gameRoot, "store");
        if (Overlaps(MetadataRoot, OutputRoot)) throw new OutputPathSecurityException();
        WorkingRoot = Path.Combine(OutputRoot, WorkingDirectoryName);
        legacyRoot = Path.Combine(OutputRoot, ".km");
    }

    public string OutputRoot { get; }
    public string MetadataRoot { get; }
    public string WorkingRoot { get; }

    internal void ValidatePlanScope(OutputApplyPlan plan)
    {
        if (plan.ProjectId != ProjectIdentity.FromPaths(projectPaths)
            || plan.GameFamily != projectPaths.SelectedGame!.Value.ToGameFamily())
            throw new ArgumentException("The output plan does not belong to this project and game.", nameof(plan));
    }
    public bool HasMaterial => Directory.Exists(MetadataRoot) || File.Exists(MetadataRoot)
        || Directory.Exists(legacyRoot) || File.Exists(legacyRoot) || new DirectoryInfo(legacyRoot).LinkTarget is not null
        || Directory.Exists(WorkingRoot) || File.Exists(WorkingRoot) || new DirectoryInfo(WorkingRoot).LinkTarget is not null
        || File.Exists(Path.Combine(root, "migration.blocked"))
        || Directory.Exists(Path.Combine(gameRoot, "store.pending"));

    public static bool HasStoredOutput(string outputRoot)
    {
        return Enum.GetValues<ProjectGame>().Any(game =>
            new OutputWorkspaceStorage(new ProjectPaths(null, null, outputRoot, null, game)).HasMaterial);
    }

    public async Task<IAsyncDisposable> AcquireAsync(OutputTransactionCoordinatorOptions options,
        CancellationToken cancellationToken = default)
    {
        options.Validate();
        RequireSafe(OutputRoot);
        // This parent is also the default per-user installation directory. A
        // portable/custom install may reach it first, so keep normal inherited
        // permissions here and apply private ACLs only below Workspaces.
        RequireSafe(appDataRoot);
        Directory.CreateDirectory(appDataRoot);
        RequireSafe(appDataRoot);
        OutputPathSafety.CreatePrivateStorageDirectory(root);
        var lockPath = Path.Combine(root, "workspace.lock");
        RequireSafe(lockPath);
        var started = Stopwatch.GetTimestamp();
        FileStream gate;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { gate = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); break; }
            catch (IOException) when (Stopwatch.GetElapsedTime(started) < options.WriterLockTimeout)
            { await Task.Delay(options.WriterLockRetryDelay, cancellationToken).ConfigureAwait(false); }
            catch (IOException) { throw new OutputRootLockTimeoutException(options.WriterLockTimeout); }
        }
        FileStream? legacyGate = null;
        try
        {
            var legacySafety = new OutputPathSafety(OutputRoot, allowPortableMetadata: true);
            // Legacy journals on a volume without ownership cannot establish write authority.
            // Preserve them for resolution instead of importing them as trusted workspace state.
            if (legacySafety.HasPortableMetadata && Directory.Exists(legacyRoot) && HasLegacyData())
                throw new OutputWorkspaceMigrationException();
            legacySafety.EnsureMetadataLayout();
            legacyGate = new FileStream(Path.Combine(legacyRoot, "output.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Delete);
            OutputPathSafety.CreatePrivateStorageDirectory(gameRoot);
            foreach (var game in Enum.GetValues<ProjectGame>())
            {
                var storage = new OutputWorkspaceStorage(projectPaths with { SelectedGame = game }, appDataRoot);
                if (Directory.Exists(storage.gameRoot)) storage.ResumePublication();
            }
            MigrateLegacy(cancellationToken);
            if (File.Exists(Path.Combine(root, "migration.blocked")))
                throw new OutputWorkspaceMigrationException();
            if (File.Exists(MetadataRoot) || File.Exists(WorkingRoot)) throw new OutputPathSecurityException();
            if (Directory.Exists(MetadataRoot))
                new OutputPathSafety(OutputRoot, MetadataRoot, WorkingRoot).EnsureMetadataLayout();
            foreach (var game in Enum.GetValues<ProjectGame>().Where(game => game != projectPaths.SelectedGame))
            {
                var other = new OutputWorkspaceStorage(projectPaths with { SelectedGame = game }, appDataRoot);
                var transactions = Path.Combine(other.MetadataRoot, "transactions");
                RequireSafe(transactions);
                if (Directory.Exists(transactions) && Directory.EnumerateFileSystemEntries(transactions).Any())
                    throw new OutputWorkspaceMigrationException();
                var journals = Path.Combine(other.MetadataRoot, "transaction-journals");
                RequireSafe(journals);
                if (Directory.Exists(journals) && Directory.EnumerateFileSystemEntries(journals).Any())
                    throw new OutputWorkspaceMigrationException();
            }
            PrepareWorkingTransactions(cancellationToken);
            return new Lease(this, gate, legacyGate);
        }
        catch
        {
            try { if (legacyGate is not null) CleanupLegacyLockDirectory(); }
            finally { legacyGate?.Dispose(); gate.Dispose(); }
            throw;
        }
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        if (!HasMaterial) return;
        await using var lease = await AcquireAsync(new OutputTransactionCoordinatorOptions(), cancellationToken).ConfigureAwait(false);
    }

    private void MigrateLegacy(CancellationToken cancellationToken)
    {
        RequireSafe(legacyRoot);
        if (!Directory.Exists(legacyRoot)) return;
        if (!HasLegacyData()) return;
        var detectedGame = DetectLegacyGame();
        if (detectedGame is { } game && game != projectPaths.SelectedGame)
        {
            var destination = new OutputWorkspaceStorage(projectPaths with { SelectedGame = game },
                appDataRoot);
            OutputPathSafety.CreatePrivateStorageDirectory(destination.gameRoot);
            destination.ResumePublication();
            destination.MigrateLegacy(cancellationToken);
            return;
        }
        var imported = Archive(legacyRoot, cancellationToken);
        var existing = Directory.Exists(MetadataRoot) ? Archive(MetadataRoot, cancellationToken) : null;
        var incoming = InspectCandidate(imported);
        var accepted = IsCompatible(imported) && incoming.Valid;
        string? winner = null;
        if (accepted && existing is null) winner = imported;
        else if (accepted && existing is not null)
        {
            if (Path.GetFileName(imported) == Path.GetFileName(existing)) winner = existing;
            else
            {
                var current = InspectCandidate(existing);
                if (!current.Valid || !IsCompatible(existing)) winner = null;
                else if (incoming.Pending && current.Pending) winner = null;
                else if (incoming.Pending) winner = imported;
                else if (current.Pending) winner = existing;
                else if (incoming.Matches && !current.Matches) winner = imported;
                else if (current.Matches && !incoming.Matches) winner = existing;
                else if (incoming.Matches && current.Matches)
                    winner = incoming.Latest > current.Latest ? imported : existing;
            }
        }
        if (winner is null)
            WriteDurable(Path.Combine(root, "migration.blocked"), "Preserved conflicting output metadata requires resolution.\n");
        else if (winner != existing)
        {
            var pending = Path.Combine(gameRoot, "store.pending");
            CopyTree(winner, pending, cancellationToken);
            WriteDurable(Path.Combine(gameRoot, "migration.ready"), "ready\n");
            ResumePublication();
        }
        // Recheck every source byte before deleting anything. Interrupted deletion resumes
        // from its complete immutable import, never from an incomplete remaining tree.
        WriteDurable(Path.Combine(gameRoot, "legacy.retiring"), Path.GetFileName(imported));
        VerifySubset(legacyRoot, imported);
        RetireLegacyContents();
        File.Delete(Path.Combine(gameRoot, "legacy.retiring"));
    }

    private void ResumePublication()
    {
        var retiring = Path.Combine(gameRoot, "legacy.retiring");
        if (File.Exists(retiring))
        {
            var id = File.ReadAllText(retiring);
            if (id.Length != 64 || id.Any(c => !char.IsAsciiHexDigit(c))) throw new OutputPathSecurityException();
            var preserved = Path.Combine(gameRoot, "imports", id);
            RequireSafe(preserved);
            if (!Directory.Exists(preserved)) throw new OutputWorkspaceMigrationException();
            if (Directory.Exists(legacyRoot))
            {
                VerifySubset(legacyRoot, preserved);
                RetireLegacyContents();
            }
            File.Delete(retiring);
        }
        var pending = Path.Combine(gameRoot, "store.pending");
        var ready = Path.Combine(gameRoot, "migration.ready");
        if (!File.Exists(ready))
        {
            if (Directory.Exists(pending)) DeleteTree(pending);
            return;
        }
        if (Directory.Exists(pending))
        {
            if (Directory.Exists(MetadataRoot))
            {
                var archive = Path.Combine(gameRoot, "replaced-" + Guid.NewGuid().ToString("N"));
                OutputFileSystemDurability.MoveDirectory(MetadataRoot, archive);
            }
            OutputFileSystemDurability.MoveDirectory(pending, MetadataRoot);
        }
        if (!Directory.Exists(MetadataRoot)) throw new OutputWorkspaceMigrationException();
        File.Delete(ready);
    }

    private string Archive(string source, CancellationToken cancellationToken)
    {
        var inventory = Inventory(source);
        var signature = Signature(inventory);
        var imports = Path.Combine(gameRoot, "imports");
        OutputPathSafety.CreatePrivateStorageDirectory(imports);
        var archive = Path.Combine(imports, signature);
        if (!Directory.Exists(archive))
        {
            var pending = Path.Combine(imports, "copy-" + Guid.NewGuid().ToString("N"));
            try
            {
                CopyTree(source, pending, cancellationToken);
                if (Signature(Inventory(pending)) != signature || Signature(Inventory(source)) != signature)
                    throw new OutputWorkspaceMigrationException();
                OutputFileSystemDurability.MoveDirectory(pending, archive);
            }
            catch
            {
                // A failed copy never becomes an import. Reclaim its owned staging
                // bytes so cancellation or a full disk does not poison the next retry.
                try { if (Directory.Exists(pending)) DeleteTree(pending); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OutputCoordinatorException) { }
                throw;
            }
        }
        else if (Signature(Inventory(archive)) != signature) throw new OutputWorkspaceMigrationException();
        return archive;
    }

    private ProjectGame? DetectLegacyGame()
    {
        var candidates = Enum.GetValues<ProjectGame>().ToDictionary(
            game => ProjectIdentity.FromPaths(projectPaths with { SelectedGame = game }).Value, game => game);
        var found = new HashSet<ProjectGame>();
        foreach (var file in Inventory(legacyRoot).Keys.Where(name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            var path = Path.Combine(legacyRoot, file);
            if (new FileInfo(path).Length > OutputLimits.MaximumMetadataDocumentBytes) return null;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(path));
                foreach (var property in Properties(document.RootElement))
                    if (property.NameEquals("projectId") && property.Value.ValueKind == JsonValueKind.String)
                    {
                        if (!candidates.TryGetValue(property.Value.GetString()!, out var game)) return null;
                        found.Add(game);
                    }
            }
            catch (JsonException) { return null; }
        }
        return found.Count == 1 ? found.Single() : null;
    }

    private bool IsCompatible(string store)
    {
        if ((File.Exists(Path.Combine(store, "sv-mod-merger-manifest.json")) && projectPaths.SelectedGame is not (ProjectGame.Scarlet or ProjectGame.Violet))
            || (File.Exists(Path.Combine(store, "za-mod-merger-manifest.json")) && projectPaths.SelectedGame != ProjectGame.ZA)) return false;
        var expected = ProjectIdentity.FromPaths(projectPaths).Value;
        var opposite = Enum.GetValues<ProjectGame>().Where(game => game != projectPaths.SelectedGame)
            .Select(game => ProjectIdentity.FromPaths(projectPaths with { SelectedGame = game }).Value).ToHashSet(StringComparer.Ordinal);
        foreach (var file in Inventory(store).Keys.Where(name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            var path = Path.Combine(store, file);
            if (new FileInfo(path).Length > OutputLimits.MaximumMetadataDocumentBytes) return false;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
                foreach (var property in Properties(doc.RootElement))
                {
                    if (property.NameEquals("projectId") && property.Value.ValueKind == JsonValueKind.String)
                    {
                        var id = property.Value.GetString();
                        if (opposite.Contains(id!) || id != expected) return false;
                    }
                    if (property.NameEquals("gameFamily") && property.Value.ValueKind == JsonValueKind.Number
                        && property.Value.GetInt32() != (int)projectPaths.SelectedGame!.Value.ToGameFamily()) return false;
                }
            }
            catch (JsonException) { return false; }
        }
        return true;
    }

    private (bool Valid, bool Matches, bool Pending, DateTimeOffset Latest) InspectCandidate(string store)
    {
        var pending = Path.Combine(store, "transactions");
        var hasPending = Directory.Exists(pending) && Directory.EnumerateFileSystemEntries(pending).Any();
        var privateJournals = Path.Combine(store, "transaction-journals");
        hasPending |= Directory.Exists(privateJournals) && Directory.EnumerateFileSystemEntries(privateJournals).Any();
        var matches = true;
        var valid = true;
        var latest = DateTimeOffset.MinValue;
        var safety = new OutputPathSafety(OutputRoot, store);
        var metadata = new OutputMetadataStore(safety);
        try
        {
            var ownership = metadata.ReadJsonAsync<OutputOwnershipInventory>(Path.Combine(store, "ownership.json"), CancellationToken.None).GetAwaiter().GetResult();
            if (ownership is not null)
                foreach (var file in ownership.Files)
                {
                    safety.ValidateTarget(file.Path);
                    var target = safety.ResolveTarget(file.Path);
                    if (!File.Exists(target) || new FileInfo(target).Length != file.CurrentState.LengthBytes
                        || HashFile(target) != file.CurrentState.Sha256) matches = false;
                    if (file.UpdatedAtUtc > latest) latest = file.UpdatedAtUtc;
                }
            var history = metadata.ReadJsonAsync<OutputApplyHistoryDocument>(Path.Combine(store, "history.json"), CancellationToken.None).GetAwaiter().GetResult();
            if (history is not null)
            {
                if (history.SchemaVersion != OutputApplyHistoryDocument.CurrentSchemaVersion
                    || history.Receipts.IsDefault || history.Receipts.Length > OutputLimits.MaximumHistoryReceipts
                    || history.Receipts.Any(receipt => receipt is null)
                    || history.Receipts.Select(receipt => receipt.TransactionId).Distinct().Count() != history.Receipts.Length)
                    throw new OutputWorkspaceMigrationException();
                foreach (var receipt in history.Receipts)
                {
                    receipt.HistoryDetails?.Validate();
                    if (receipt.CompletedAtUtc > latest) latest = receipt.CompletedAtUtc;
                }
            }
            var checkpoints = Path.Combine(store, "checkpoints");
            if (Directory.Exists(checkpoints))
                foreach (var directory in Directory.EnumerateDirectories(checkpoints))
                {
                    var manifest = metadata.ReadJsonAsync<OutputCheckpointManifest>(Path.Combine(directory, "manifest.json"), CancellationToken.None).GetAwaiter().GetResult();
                    if (manifest?.Summary is { } summary && manifest.SchemaVersion == OutputCheckpointManifest.CurrentSchemaVersion
                        && summary.CreatedAtUtc > latest) latest = summary.CreatedAtUtc;
                }
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or OutputCoordinatorException)
        { matches = false; valid = false; }
        return (valid, matches, hasPending, latest);
    }

    private void PrepareWorkingTransactions(CancellationToken cancellationToken)
    {
        RequireSafe(WorkingRoot);
        var binding = Path.Combine(WorkingRoot, "workspace.id");
        if (Directory.Exists(WorkingRoot))
        {
            RequireSafe(binding);
            if (!File.Exists(binding))
            {
                new OutputPathSafety(OutputRoot, WorkingRoot, allowPortableMetadata: true).EnsureMetadataLayout();
                var unbound = Inventory(WorkingRoot);
                if (unbound.Keys.Any(name => name is not ("output-store.marker" or "transactions" or "checkpoints")))
                    throw new OutputWorkspaceMigrationException();
                WriteDurable(binding, workspaceKey);
            }
            if (File.ReadAllText(binding) != workspaceKey)
            {
                CleanupWorkingLayout();
                if (Directory.Exists(WorkingRoot)) throw new OutputWorkspaceMigrationException();
            }
        }
        var storedTransactions = Path.Combine(MetadataRoot, "transactions");
        if (!Directory.Exists(storedTransactions) || !Directory.EnumerateFileSystemEntries(storedTransactions).Any()) return;
        EnsureWorkingLayout();
        var safety = new OutputPathSafety(OutputRoot, MetadataRoot, WorkingRoot);
        safety.EnsureMetadataLayout();
        var destination = Path.Combine(WorkingRoot, "transactions");
        foreach (var entry in Directory.EnumerateDirectories(storedTransactions))
        {
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if (!Directory.Exists(target))
            {
                var preparing = target + ".importing";
                if (Directory.Exists(preparing)) DeleteTree(preparing);
                CopyTree(entry, preparing, cancellationToken, portableDestination: safety.UsesPrivateWorkspaceJournal);
                OutputFileSystemDurability.MoveDirectory(preparing, target);
            }
            if (safety.UsesPrivateWorkspaceJournal)
            {
                var sourceJournal = Path.Combine(entry, "journal.json");
                RequireSafe(sourceJournal);
                var journal = safety.GetTransactionJournalPath(target);
                safety.ValidateMetadataFile(journal);
                if (!File.Exists(journal))
                {
                    var temporary = safety.GetContainedMetadataPath(Path.GetDirectoryName(journal)!, "." + Path.GetFileName(journal) + ".pending.tmp");
                    safety.ValidateMetadataFile(temporary);
                    File.Delete(temporary);
                    using (var input = new FileStream(sourceJournal, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var output = new OutputMetadataStore(safety).OpenPrivateFile(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileOptions.WriteThrough))
                    {
                        if (input.Length > OutputLimits.MaximumMetadataDocumentBytes) throw new OutputWorkspaceMigrationException();
                        input.CopyTo(output);
                        output.Flush(true);
                    }
                    OutputFileSystemDurability.Move(temporary, journal, overwrite: false);
                }
                if (HashFile(sourceJournal) != HashFile(journal)) throw new OutputWorkspaceMigrationException();
                var workingJournal = Path.Combine(target, "journal.json");
                RequireSafe(workingJournal);
                if (File.Exists(workingJournal))
                {
                    if (HashFile(sourceJournal) != HashFile(workingJournal)) throw new OutputWorkspaceMigrationException();
                    File.Delete(workingJournal);
                    OutputFileSystemDurability.FlushParent(workingJournal);
                }
                var expected = Inventory(entry);
                expected.Remove("journal.json");
                if (Signature(expected) != Signature(Inventory(target))) throw new OutputWorkspaceMigrationException();
            }
            else if (Signature(Inventory(entry)) != Signature(Inventory(target))) throw new OutputWorkspaceMigrationException();
        }
        if (Directory.EnumerateFiles(storedTransactions).Any()) throw new OutputWorkspaceMigrationException();
        var transferred = Path.Combine(MetadataRoot, "transactions.transferred");
        if (Directory.Exists(transferred)) DeleteTree(transferred);
        OutputFileSystemDurability.MoveDirectory(storedTransactions, transferred);
        DeleteTree(transferred);
    }

    internal void EnsureWorkingLayout()
    {
        var safety = new OutputPathSafety(OutputRoot, WorkingRoot, allowPortableMetadata: true);
        safety.EnsureMetadataLayout();
        var binding = Path.Combine(WorkingRoot, "workspace.id");
        if (!File.Exists(binding)) WriteDurable(binding, workspaceKey);
        else if (File.ReadAllText(binding) != workspaceKey) throw new OutputWorkspaceMigrationException();
    }

    private bool HasLegacyData() => Inventory(legacyRoot).Keys.Any(name =>
        name is not ("output-store.marker" or "transactions" or "checkpoints"));

    private void RetireLegacyContents()
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(legacyRoot))
        {
            if (Path.GetFileName(entry) is "output.lock" or "output-store.marker") continue;
            RequireSafe(entry);
            if (Directory.Exists(entry)) DeleteTree(entry); else File.Delete(entry);
        }
    }

    private void CleanupLegacyLockDirectory()
    {
        if (Directory.Exists(legacyRoot) && !HasLegacyData()) DeleteTree(legacyRoot);
    }

    private void CleanupWorkingLayout()
    {
        if (!Directory.Exists(WorkingRoot)) return;
        RequireSafe(WorkingRoot);
        var transactions = Path.Combine(WorkingRoot, "transactions");
        if (File.Exists(transactions)) return;
        if (Directory.Exists(transactions) && Directory.EnumerateFileSystemEntries(transactions).Any()) return;
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "output-store.marker", "workspace.id", "transactions", "checkpoints" };
        if (Directory.EnumerateFileSystemEntries(WorkingRoot).Any(path => !allowed.Contains(Path.GetFileName(path)))) return;
        var checkpoints = Path.Combine(WorkingRoot, "checkpoints");
        if (File.Exists(checkpoints)) return;
        if (Directory.Exists(checkpoints) && Directory.EnumerateFileSystemEntries(checkpoints).Any()) return;
        new OutputPathSafety(OutputRoot, WorkingRoot, allowPortableMetadata: true).EnsureMetadataLayout();
        DeleteTree(WorkingRoot);
    }

    private static IEnumerable<JsonProperty> Properties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                yield return property;
                foreach (var nested in Properties(property.Value)) yield return nested;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                foreach (var nested in Properties(item)) yield return nested;
    }

    private static SortedDictionary<string, string> Inventory(string root)
    {
        RequireSafe(root);
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        long total = 0;
        while (pending.TryPop(out var current))
        {
            if (current.Depth > 32) throw new OutputWorkspaceMigrationException();
            foreach (var path in Directory.EnumerateFileSystemEntries(current.Path))
            {
                RequireSafe(path);
                var relative = Path.GetRelativePath(root, path);
                if (relative == "output.lock") continue;
                if (result.Count >= MaximumEntries) throw new OutputWorkspaceMigrationException();
                if (Directory.Exists(path)) { result.Add(relative, "directory"); pending.Push((path, current.Depth + 1)); }
                else
                {
                    total = checked(total + new FileInfo(path).Length);
                    if (total > MaximumBytes) throw new OutputWorkspaceMigrationException();
                    result.Add(relative, HashFile(path));
                }
            }
        }
        return result;
    }

    private static string Signature(SortedDictionary<string, string> inventory) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(inventory)));

    private static string HashFile(string path)
    {
        RequireSafe(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void CopyTree(string source, string destination, CancellationToken cancellationToken, bool portableDestination = false)
    {
        var inventory = Inventory(source);
        RequireSafe(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new OutputWorkspaceMigrationException();
        void CreateDestinationDirectory(string path)
        {
            if (portableDestination) { RequireSafe(path); Directory.CreateDirectory(path); RequireSafe(path); }
            else OutputPathSafety.CreatePrivateStorageDirectory(path);
        }
        CreateDestinationDirectory(destination);
        foreach (var (relative, fingerprint) in inventory)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, relative);
            if (fingerprint == "directory") { CreateDestinationDirectory(target); continue; }
            var original = Path.Combine(source, relative);
            RequireSafe(original);
            CreateDestinationDirectory(Path.GetDirectoryName(target)!);
            using (var input = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { input.CopyTo(output); output.Flush(true); }
            if (HashFile(target) != fingerprint) throw new OutputWorkspaceMigrationException();
        }
        if (Signature(Inventory(destination)) != Signature(inventory) || Signature(Inventory(source)) != Signature(inventory))
            throw new OutputWorkspaceMigrationException();
    }

    private static void VerifySubset(string source, string archive)
    {
        var saved = Inventory(archive);
        foreach (var (path, hash) in Inventory(source))
            if (!saved.TryGetValue(path, out var expected) || expected != hash) throw new OutputWorkspaceMigrationException();
    }

    private static void DeleteTree(string path)
    {
        _ = Inventory(path);
        // Use validated, bounded traversal rather than following links recursively.
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            RequireSafe(entry);
            if (Directory.Exists(entry)) DeleteTree(entry); else File.Delete(entry);
        }
        Directory.Delete(path);
        OutputFileSystemDurability.FlushParent(path);
    }

    private static void WriteDurable(string path, string content)
    {
        RequireSafe(path);
        var temporary = path + ".pending";
        RequireSafe(temporary);
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { stream.Write(Encoding.UTF8.GetBytes(content)); stream.Flush(true); }
        OutputFileSystemDurability.Move(temporary, path, overwrite: true);
    }

    private static bool Overlaps(string first, string second)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(first, second, comparison)
            || first.StartsWith(second + Path.DirectorySeparatorChar, comparison)
            || second.StartsWith(first + Path.DirectorySeparatorChar, comparison);
    }

    private static void RequireSafe(string path)
    {
        if (!FileSystemPathBoundary.HasSafeExistingAncestorChain(path)) throw new OutputPathSecurityException();
    }

    private sealed class Lease(OutputWorkspaceStorage storage, FileStream gate, FileStream legacyGate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            try { storage.CleanupWorkingLayout(); storage.CleanupLegacyLockDirectory(); }
            finally { legacyGate.Dispose(); gate.Dispose(); }
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class OutputWorkspaceMigrationException : OutputCoordinatorException
{
    public OutputWorkspaceMigrationException() : base("Output metadata migration needs attention. Preserved copies are available in the output workspace; no game files were changed.") { }
}
