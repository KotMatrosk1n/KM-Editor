// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using System.Text.Json;
using System.Buffers.Binary;
using KM.Formats.Audio;
using KM.Formats.Models;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Editing;
using KM.SwSh.Items;
using KM.SwSh.Pokemon;

namespace KM.SwSh.Restoration;

public sealed record SwShRestorationIntent(string DefinitionId, string PreparedHash, bool Enabled, string? ExpectedStateHash)
{
    public string? RemovalId { get; init; }
    public bool UpdateAssets { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool AdoptExternal { get; init; }
    public IReadOnlyList<string> AdditionalPreparedHashes { get; init; } = [];
}
public sealed record SwShRestorationStageResult(EditSession Session, IReadOnlyList<ValidationDiagnostic> Diagnostics);
public enum SwShRestorationRepairState { NotApplicable, Available, Installed, Staged, Blocked }
public sealed record SwShRestorationRepairStatus(string Id, SwShRestorationRepairState State, string? Detail = null);
public sealed record SwShRestorationStageProgress(string Phase, int? Completed = null, int? Total = null);
public sealed record SwShRestorationStageSelection(string DefinitionId, string PreparedHash, bool Enabled,
    bool RemoveAssets = false, string? RemovalId = null, IReadOnlyCollection<string>? PreparedAssets = null,
    bool UpdateAssets = false, bool AdoptExternal = false);
public sealed record SwShRestorationPreview(IReadOnlyDictionary<string, byte[]?> Files, string? StateHash, string? PreviousStateHash,
    SwShRestorationState State, IReadOnlyList<ProjectFileReference> Sources, IReadOnlyList<int> RemovedSpecies)
{
    public IReadOnlyList<SwShPokemonIdentity> RemovedIdentities { get; init; } = RemovedSpecies.Select(s => new SwShPokemonIdentity(s, 0)).ToArray();
    public IReadOnlySet<string> AdoptedWholeFiles { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

public sealed partial class SwShRestorationService
{
    public const string Domain = SwShRestorationDefinitions.Domain;
    private const string AudioRepairSummary = "Repair restored Pokemon audio.";
    private const string PokedexRepairSummary = "Repair shared Pokédex and Town Map memory.";
    private const string BattleRepairSummary = "Repair restored Pokemon battle heaps.";
    private const string AudioRepairId = "audio-repair";
    private const string PokedexRepairId = "pokedex-repair";
    private const string BattleRepairId = "battle-repair";

    public static bool IsAudioRepair(PendingEdit edit) => edit.Domain == Domain && edit.RecordId == AudioRepairId;
    public static bool IsPokedexRepair(PendingEdit edit) => edit.Domain == Domain && edit.RecordId == PokedexRepairId;
    private static bool IsBattleRepair(PendingEdit edit) => edit.Domain == Domain && edit.RecordId == BattleRepairId;
    public static bool IsExecutableRepair(PendingEdit edit) => IsAudioRepair(edit) || IsPokedexRepair(edit) || IsBattleRepair(edit);

    public static string ExecutableRepairSummary(IReadOnlyList<PendingEdit> edits)
    {
        var repairs = edits.Where(IsExecutableRepair).ToArray();
        if (repairs.Length == 1) return RepairSummary(repairs[0].RecordId);
        if (!repairs.Any(IsBattleRepair)) return "Repair restored Pokemon audio and shared Pokédex and Town Map memory.";
        return "Repair restored Pokemon executable support.";
    }

    private static string RepairSummary(string? id) => id switch
    {
        AudioRepairId => AudioRepairSummary,
        PokedexRepairId => PokedexRepairSummary,
        BattleRepairId => BattleRepairSummary,
        _ => throw new InvalidDataException("Unknown restoration executable repair."),
    };

    private static PendingEdit RepairEdit(string id) => new(Domain, RepairSummary(id), [], id, "repair", "true", Domain);

    public static IReadOnlyList<SwShRestorationRepairStatus> InspectIntegrity(ProjectPaths paths, IReadOnlyList<PendingEdit> pending,
        bool hasRestorations)
    {
        if (paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield)
            || string.IsNullOrWhiteSpace(paths.OutputRootPath) || string.IsNullOrWhiteSpace(paths.BaseRomFsPath)) return [];
        var changesAssets = pending.Any(edit => edit.Domain == Domain && !IsExecutableRepair(edit)
            && (Parse(edit) is { Enabled: true } or { UpdateAssets: true }));
        var game = paths.SelectedGame.Value;
        var baseline = new Lazy<byte[]>(() => ReadBattleBase(paths));
        var model = new Lazy<(byte[]? Executable, bool NeedsPokedexRepair)>(() =>
        {
            var output = Output(paths, SwShRestorationModelMemoryPatcher.MainPath, includeExecutable: true);
            if (!System.IO.File.Exists(output))
            {
                if (hasRestorations)
                    throw new InvalidDataException("The restoration executable is missing from the output folder.");
                return (null, false);
            }
            var executable = BoundedRead(output);
            var memory = SwShRestorationModelMemoryPatcher.Read(executable, game);
            if (!memory.Installed)
            {
                if (hasRestorations)
                    throw new InvalidDataException("The current executable is missing restoration model memory support.");
                return (null, false);
            }
            return (executable, memory.NeedsPokedexRepair);
        });
        SwShRestorationRepairStatus Inspect(string id, Func<SwShRestorationRepairState> check)
        {
            try
            {
                var status = check();
                if (status != SwShRestorationRepairState.Installed
                    && (changesAssets && id is BattleRepairId or PokedexRepairId
                        || pending.Any(edit => IsExecutableRepair(edit) && edit.RecordId == id)))
                    status = SwShRestorationRepairState.Staged;
                return new(id, status);
            }
            catch (Exception exception) when (SourceError(exception))
            {
                return new(id, SwShRestorationRepairState.Blocked, exception.Message);
            }
        }

        SwShRestorationRepairState CheckModel(bool battle)
        {
            var (executable, needsPokedexRepair) = model.Value;
            if (executable is null) return SwShRestorationRepairState.NotApplicable;
            if (!battle)
            {
                if (!needsPokedexRepair) return SwShRestorationRepairState.Installed;
                _ = SwShRestorationModelMemoryPatcher.Apply(baseline.Value, executable, game);
                return SwShRestorationRepairState.Available;
            }
            return SwShRestorationBattleHeapPatcher.Check(baseline.Value, executable, game)
                ? SwShRestorationRepairState.Installed : SwShRestorationRepairState.Available;
        }

        var repairs = new List<SwShRestorationRepairStatus>();
        if (paths.SelectedGame == ProjectGame.Sword)
        {
            repairs.Add(Inspect(AudioRepairId, () =>
            {
                var bankBytes = EffectiveVoiceBankBytes(paths);
                if (!SwShRestorationSoundMemoryPatcher.NeedsLargeBankBudget(bankBytes))
                    return SwShRestorationRepairState.NotApplicable;
                var outputs = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
                AddSoundMemory(paths, outputs, new HashSet<ProjectFileReference>(), bankBytes);
                return outputs.Count == 0 ? SwShRestorationRepairState.Installed : SwShRestorationRepairState.Available;
            }));
        }
        repairs.Add(Inspect(BattleRepairId, () => CheckModel(battle: true)));
        repairs.Add(Inspect(PokedexRepairId, () => CheckModel(battle: false)));
        return repairs;
    }

    public SwShRestorationStageResult StageIntegrity(ProjectPaths paths, EditSession? session, bool hasRestorations)
    {
        var current = session ?? EditSession.Start();
        try
        {
            var available = InspectIntegrity(paths, current.PendingEdits, hasRestorations)
                .Where(repair => repair.State == SwShRestorationRepairState.Available)
                .Select(repair => repair.Id).ToArray();
            if (available.Length == 0) return new(current, []);
            var next = current with { PendingEdits = [.. current.PendingEdits, .. available.Select(RepairEdit)] };
            _ = PreviewExecutableRepairs(paths, next.PendingEdits.Any(IsAudioRepair),
                next.PendingEdits.Any(IsPokedexRepair), next.PendingEdits.Any(IsBattleRepair));
            return new(next, []);
        }
        catch (Exception exception) when (SourceError(exception)) { return new(current, [Error(SessionCode, exception.Message)]); }
    }

    public static bool CanRepairPokedex(ProjectPaths paths)
    {
        if (paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield)
            || string.IsNullOrWhiteSpace(paths.BaseExeFsPath) || string.IsNullOrWhiteSpace(paths.BaseRomFsPath)
            || string.IsNullOrWhiteSpace(paths.OutputRootPath)) return false;
        try
        {
            var output = Output(paths, SwShRestorationModelMemoryPatcher.MainPath, includeExecutable: true);
            return System.IO.File.Exists(output)
                && SwShRestorationModelMemoryPatcher.Read(BoundedRead(output), paths.SelectedGame.Value).NeedsPokedexRepair;
        }
        catch (Exception exception) when (SourceError(exception)) { return false; }
    }

    public static bool CanRepairAudio(ProjectPaths paths) => paths.SelectedGame == ProjectGame.Sword
        && !string.IsNullOrWhiteSpace(paths.BaseExeFsPath) && !string.IsNullOrWhiteSpace(paths.BaseRomFsPath)
        && !string.IsNullOrWhiteSpace(paths.OutputRootPath)
        && SwShRestorationSoundMemoryPatcher.NeedsLargeBankBudget(EffectiveVoiceBankBytes(paths));

    public SwShRestorationStageResult StageAudioRepair(ProjectPaths paths, EditSession? session, out bool upToDate)
        => StageExecutableRepair(paths, session, audio: true, out upToDate);

    private static SwShRestorationStageResult StageExecutableRepair(ProjectPaths paths, EditSession? session, bool audio, out bool upToDate)
    {
        var current = session ?? EditSession.Start();
        upToDate = false;
        try
        {
            var preview = PreviewExecutableRepairs(paths, audio, pokedex: !audio);
            upToDate = preview.Files.Count == 0;
            var repairId = audio ? AudioRepairId : PokedexRepairId;
            var remaining = current.PendingEdits.Where(edit => edit.Domain != Domain || edit.RecordId != repairId);
            var edits = upToDate ? remaining : remaining.Append(RepairEdit(repairId));
            return new(current with { PendingEdits = edits.ToArray() }, []);
        }
        catch (Exception exception) when (SourceError(exception)) { return new(current, [Error(SessionCode, exception.Message)]); }
    }

    private static SwShRestorationPreview PreviewExecutableRepairs(ProjectPaths paths, bool audio, bool pokedex, bool battle = false)
    {
        var snapshot = SwShRestorationStateStore.Read(paths);
        var outputs = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        var sources = new HashSet<ProjectFileReference>();
        if (audio)
        {
            if (!CanRepairAudio(paths))
                throw new InvalidDataException("Select a Sword 1.3.2 project with Base ExeFS and a large restored Common.bnk.");
            var bankBytes = EffectiveVoiceBankBytes(paths, sources);
            if (SwShRestorationSoundMemoryPatcher.NeedsLargeBankBudget(bankBytes))
                AddSoundMemory(paths, outputs, sources, bankBytes);
        }
        if (pokedex)
        {
            if (paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield)
                || string.IsNullOrWhiteSpace(paths.BaseExeFsPath) || string.IsNullOrWhiteSpace(paths.BaseRomFsPath)
                || string.IsNullOrWhiteSpace(paths.OutputRootPath))
                throw new InvalidDataException("Select a Sword or Shield 1.3.2 project with Base RomFS, Base ExeFS and an output folder.");
            AddModelMemory(paths, outputs, sources, repairOnly: true);
        }
        if (battle) AddBattleHeap(paths, outputs, sources);
        return new(outputs, null, snapshot.Hash, snapshot.State, sources.ToArray(), []);
    }

    public static void BindOutputState(ProjectPaths paths, SwShRestorationPreview preview)
    {
        if (preview.StateHash is { } stateHash)
            SwShChangePlanSourceGuard.BindRestorationState(paths, preview.PreviousStateHash, stateHash, preview.AdoptedWholeFiles);
    }

    public const string SourceCode = "KM-SWSH-RESTORATION-SOURCE-UNAVAILABLE";
    public const string ConflictCode = "KM-SWSH-RESTORATION-CONFLICT";
    public const string SessionCode = "KM-SWSH-RESTORATION-SESSION-INVALID";
    public const string ReferenceCode = "KM-SWSH-RESTORATION-REFERENCE";
    public const string StaleCode = "KM-SWSH-RESTORATION-PREPARED-STALE";

    public SwShRestorationStageResult Stage(ProjectPaths paths, string definitionId, string preparedHash, bool enabled, EditSession? session, bool removeAssets = false, string? removalId = null, IReadOnlyCollection<string>? preparedAssets = null, bool updateAssets = false, bool adoptExternal = false)
        => StageBatch(paths, [new(definitionId, preparedHash, enabled, removeAssets, removalId, preparedAssets, updateAssets, adoptExternal)], session);

    public SwShRestorationStageResult StageBatch(ProjectPaths paths, IReadOnlyList<SwShRestorationStageSelection> selections, EditSession? session,
        Action<SwShRestorationStageProgress>? reportProgress = null)
    {
        if (selections.Count == 0) return StageExecutableRepair(paths, session, audio: false, out _);
        var current = session ?? EditSession.Start();
        try
        {
            if (selections.Count > SwShRestorationDefinitions.All.Count
                || selections.Any(s => s is null) || selections.Select(s => s.DefinitionId).Distinct(StringComparer.Ordinal).Count() != selections.Count)
                throw new InvalidDataException("Restoration selections are missing, duplicated, or excessive.");
            current = UpgradeSession(paths, current);
            var stateHash = SwShRestorationStateStore.Read(paths).Hash;
            var next = current;
            foreach (var selection in selections)
                next = StageSelection(paths, selection, next, stateHash);
            _ = Preview(paths, next, reportProgress);
            return new(next, []);
        }
        catch (Exception exception) when (SourceError(exception)) { return new(current, [Error(SessionCode, exception.Message)]); }
    }

    private static EditSession StageSelection(ProjectPaths paths, SwShRestorationStageSelection selection, EditSession session, string? stateHash)
    {
        var (definitionId, preparedHash, enabled, removeAssets, removalId, preparedAssets, updateAssets, adoptExternal) = selection;
        if (enabled && removeAssets) throw new InvalidDataException("Asset removal cannot enable a restoration.");
        if (updateAssets && removeAssets) throw new InvalidDataException("Asset updates cannot remove a restoration.");
        if (adoptExternal && (!enabled || updateAssets || removeAssets || definitionId == SwShRestorationDefinitions.Piplup.Id))
            throw new InvalidDataException("An external installation must be staged as a supported restoration.");
        if (definitionId == SwShRestorationDefinitions.Piplup.Id)
        {
            foreach (var definition in SwShRestorationDefinitions.PiplupSpecies)
                session = StageSelection(paths, new(definition.Id, SwShRestorationCompatibility.Split(paths, preparedHash, definition.Id),
                    enabled, removeAssets, UpdateAssets: updateAssets), session, stateHash);
            return session;
        }
        var intent = new SwShRestorationIntent(definitionId, preparedHash, enabled, stateHash)
        {
            UpdateAssets = updateAssets,
            AdoptExternal = adoptExternal,
            RemovalId = removeAssets ? removalId ?? Guid.NewGuid().ToString("N") : null,
            AdditionalPreparedHashes = removeAssets ? (preparedAssets ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() : [],
        };
        var edit = CreateEdit(paths, intent);
        var removalStems = removeAssets ? SwShRestorationCustomizations.ModelStems(
            SwShRestorationStateStore.ReadPrepared(paths, preparedHash)).ToArray() : [];
        bool Keep(PendingEdit pending)
        {
            if (pending.Domain == Domain && pending.RecordId == definitionId) return false;
            if (!removeAssets || pending.Domain != ModelTextureIntent.Domain) return true;
            var model = ModelTextureIntent.Parse(pending.NewValue);
            return model.Game != paths.SelectedGame.ToString() || !removalStems.Any(stem =>
                model.Model.StartsWith($"bin/pokemon/{stem}/", StringComparison.Ordinal));
        }
        return session with { PendingEdits = session.PendingEdits.Where(Keep).Append(edit).ToArray() };
    }

    public SwShEditSessionValidation Validate(ProjectPaths paths, EditSession session)
    {
        var plan = CreateChangePlan(paths, session);
        return new(session, plan.CanApply, plan.Diagnostics);
    }

    public ChangePlan CreateChangePlan(ProjectPaths paths, EditSession session)
    {
        try
        {
            var preview = Preview(paths, session);
            var references = CheckReferences(paths, preview);
            if (references.References.Count > 0) return new(session.Id, [], ReferenceDiagnostics(references));
            var sources = preview.Sources.Concat(references.Sources).ToHashSet();
            var executableOnly = preview.StateHash is null;
            if (!executableOnly)
                SwShRestorationFormDependencies.ValidateNative(preview.State, path =>
                    preview.Files.TryGetValue(path, out var value) ? value : ReadEffective(paths, path, sources));
            var writes = preview.Files.Keys.Order(StringComparer.Ordinal).Select(path => new PlannedFileWrite(path,
                path == (executableOnly ? SwShRestorationModelMemoryPatcher.MainPath : SwShRestorationComposer.PersonalPath) ? sources.ToArray() : [],
                System.IO.File.Exists(Output(paths, path, includeExecutable: true)),
                executableOnly ? ExecutableRepairSummary(session.PendingEdits) : "Update restored Pokemon while retaining unrelated resources and project customizations.")).ToArray();
            return SwShChangePlanSourceGuard.Capture(paths, new(session.Id, writes, []) { EffectivePendingEdits = session.PendingEdits }, preserveExplicitSourceLayers: true);
        }
        catch (Exception exception) when (SourceError(exception)) { return new(session.Id, [], [Error(SourceCode, exception.Message)]); }
    }

    public static IReadOnlyList<ValidationDiagnostic> ReferenceDiagnostics(SwShPokemonReferences references) => references.References.Select(r =>
        new ValidationDiagnostic(DiagnosticSeverity.Error,
            $"Pokemon {r.Species}, form {r.Form}, is still used by {r.Editor}, record {r.Record}. Change that assignment before removal.",
            File: r.File, Domain: "workflow." + r.Editor, Field: r.Record + ":" + r.Field,
            Expected: $"Pokemon {r.Species}, form {r.Form}") { Code = ReferenceCode }).ToArray();

    private static SwShPokemonReferences CheckReferences(ProjectPaths paths, SwShRestorationPreview preview)
    {
        if (preview.RemovedIdentities.Count == 0) return new([], []);
        using var view = new SwShRestorationView(paths);
        WritePreview(view.Paths, preview);
        var references = SwShPokemonReferenceIndex.FindIdentities(view.Paths, preview.RemovedIdentities.ToHashSet());
        return references with { Sources = references.Sources.Select(s => new ProjectFileReference(
            System.IO.File.Exists(Output(paths, s.RelativePath)) ? ProjectFileLayer.Layered : ProjectFileLayer.Base, s.RelativePath)).ToArray() };
    }

    public ApplyResult ApplyChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewed)
    {
        try
        {
            var plan = CreateChangePlan(paths, session);
            if (!plan.CanApply) return Result(plan, [], plan.Diagnostics);
            if (!ChangePlanReview.Matches(reviewed, plan)) return Result(plan, [], [Error(StaleCode, "The restoration plan changed. Review it again.")]);
            var failures = SwShChangePlanSourceGuard.Validate(paths, reviewed);
            if (failures.Any(d => d.Severity == DiagnosticSeverity.Error)) return Result(plan, [], failures);
            if (!SwShChangePlanSourceGuard.TryAcquireApplyScope(paths, plan, out var scope, out failures, preserveExplicitSourceLayers: true)) return Result(plan, [], failures);
            using var verified = scope!;
            if (!verified.TryPrepareSnapshotPlan(CreateChangePlan(verified.ApplyPaths, session), out var prepared))
                return Result(plan, [], [Error(StaleCode, "Restoration sources changed while preparing the verified output.")]);
            var preview = Preview(verified.ApplyPaths, session);
            BindOutputState(verified.ApplyPaths, preview);
            var written = WritePreview(verified.ApplyPaths, preview);
            return verified.Commit(Result(prepared, written, []));
        }
        catch (Exception exception) when (SourceError(exception)) { return Result(reviewed, [], [Error(ConflictCode, exception.Message)]); }
    }

    public SwShRestorationPreview Preview(ProjectPaths paths, EditSession session) => Preview(paths, session, null);

    private SwShRestorationPreview Preview(ProjectPaths paths, EditSession session, Action<SwShRestorationStageProgress>? reportProgress)
    {
        if (paths.SelectedGame is not (ProjectGame.Sword or ProjectGame.Shield) || string.IsNullOrWhiteSpace(paths.OutputRootPath)
            || string.IsNullOrWhiteSpace(paths.BaseRomFsPath)) throw new InvalidDataException("Select a Sword or Shield project with a base dump and output folder.");
        var staged = UpgradeSession(paths, session).PendingEdits.Where(e => e.Domain == Domain).ToArray();
        if (staged.Length == 0 || staged.Select(e => e.RecordId).Distinct(StringComparer.Ordinal).Count() != staged.Length)
            throw new InvalidDataException("Restoration selections are missing, duplicated, or excessive.");
        var audioRepair = staged.Any(IsAudioRepair);
        var pokedexRepair = staged.Any(IsPokedexRepair);
        var battleRepair = staged.Any(IsBattleRepair);
        foreach (var repair in staged.Where(IsExecutableRepair))
            if ((repair.Summary != RepairSummary(repair.RecordId)
                    && !(IsPokedexRepair(repair) && repair.Summary == "Repair the Pokédex model memory."))
                || repair.Owner != Domain || repair.Field != "repair" || repair.NewValue != "true" || repair.Sources.Count != 0
                || IsAudioRepair(repair) && paths.SelectedGame != ProjectGame.Sword)
                throw new InvalidDataException("Staged restoration executable repair is invalid.");
        var edits = staged.Where(e => !IsExecutableRepair(e)).ToArray();
        if (edits.Length > SwShRestorationDefinitions.All.Count)
            throw new InvalidDataException("Restoration selections are excessive.");
        if (edits.Length == 0) return PreviewExecutableRepairs(paths, audioRepair, pokedexRepair, battleRepair);
        var completed = 0;
        reportProgress?.Invoke(new("staging", completed, edits.Length));
        void ReportSelection() => reportProgress?.Invoke(new("staging", ++completed, edits.Length));

        var snapshot = SwShRestorationStateStore.Read(paths); var selections = snapshot.State.Selections.ToDictionary(s => s.DefinitionId, StringComparer.Ordinal);
        var owned = SwShRestorationOwnership.ExactFiles(paths);
        var outputs = new Dictionary<string, byte[]?>(StringComparer.Ordinal); var sources = new HashSet<ProjectFileReference>(); var removed = new HashSet<SwShPokemonIdentity>();
        var targetFingerprints = new Dictionary<string, SwShRestorationSourceFingerprint>(StringComparer.Ordinal);
        // Only retain resource identities here. Payloads remain under the existing
        // artifact read limits, and each selection still uses its current hash.
        var includeAllKeys = edits.Any(e => Parse(e).AdoptExternal);
        var protectedKeys = new Dictionary<string, HashSet<(string Path, SwShRestorationWriter Writer, string Key)>>(StringComparer.Ordinal);
        HashSet<(string Path, SwShRestorationWriter Writer, string Key)> ProtectedKeys(string hash)
        {
            if (protectedKeys.TryGetValue(hash, out var keys)) return keys;
            keys = SwShRestorationStateStore.ReadPrepared(paths, hash).Contributions
                .Where(c => includeAllKeys || IsShared((c.Path, c.Writer, c.Key)))
                .Select(c => (c.Path, c.Writer, c.Key)).ToHashSet();
            protectedKeys.Add(hash, keys);
            return keys;
        }
        static bool IsShared((string Path, SwShRestorationWriter Writer, string Key) key) =>
            SwShRestorationMegaItems.IsItemContribution(key.Path, key.Writer)
            || key.Writer is SwShRestorationWriter.SpeciesText or SwShRestorationWriter.AbilityText
            || key.Writer == SwShRestorationWriter.Camp && key.Key.Contains("/common/", StringComparison.Ordinal);
        var externalDefinitions = new Lazy<HashSet<string>>(() => SwShRestorationCatalog.Discover(paths, [])
            .Entries.Where(e => e.Installation == SwShRestorationInstallation.External
                && e.Support != SwShRestorationSupport.NotSupported && e.DefinitionId is not null)
            .Select(e => e.DefinitionId!).ToHashSet(StringComparer.Ordinal));
        var adoptedWholeFiles = new HashSet<string>(StringComparer.Ordinal);
        var needsSoundMemory = false;
        var allowLargeBankRepair = audioRepair;
        byte[]? Current(string path)
        {
            if (outputs.TryGetValue(path, out var value)) return value;
            return ReadEffective(paths, path, sources);
        }
        var pendingVoices = new List<SwShRestorationContribution>();
        var pendingVoiceKeys = new HashSet<string>(StringComparer.Ordinal);
        void FlushVoices()
        {
            if (pendingVoices.Count == 0) return;
            foreach (var file in SwShRestorationComposer.Compose(pendingVoices, Current, true).Files)
                outputs[file.Key] = file.Value;
            pendingVoices.Clear();
            pendingVoiceKeys.Clear();
        }
        var assetsOnly = edits.All(e => Parse(e).UpdateAssets);
        var hasForms = !assetsOnly && (SwShRestorationForms.EnabledForms(snapshot.State).Any()
            || edits.Any(e => SwShRestorationDefinitions.Get(Parse(e).DefinitionId).Identities.Any(i => i.Form != 0)));
        byte[]? NativeFormFile(string path)
        {
            var physical = Path.Combine(paths.BaseRomFsPath, path[6..]);
            if (!File.Exists(physical)) return null;
            sources.Add(new(ProjectFileLayer.Base, path));
            return BoundedRead(physical);
        }
        var normalizedFormStorage = hasForms
            ? SwShRestorationForms.Storage(NativeFormFile, Current, snapshot.State, normalize: true)
            : null;
        if (normalizedFormStorage is not null)
            foreach (var file in normalizedFormStorage) outputs[file.Key] = file.Value;
        foreach (var edit in edits.OrderBy(e => e.RecordId, StringComparer.Ordinal))
        {
            var intent = Parse(edit);
            if (intent.ExpectedStateHash != snapshot.Hash) throw new InvalidDataException("Restoration selection changed after staging. Stage it again.");
            var expected = CreateEdit(paths, intent);
            if (edit.Summary != expected.Summary || edit.Owner != expected.Owner || edit.Field != expected.Field || !edit.Sources.SequenceEqual(expected.Sources))
                throw new InvalidDataException("Staged restoration metadata does not match its prepared definition.");
            foreach (var source in expected.Sources) sources.Add(source);
            var bundle = SwShRestorationStateStore.ReadPrepared(paths, intent.PreparedHash);
            if (bundle.DefinitionId != intent.DefinitionId) throw new InvalidDataException("Prepared restoration definition does not match the selection.");
            needsSoundMemory |= intent.Enabled && bundle.Contributions.Any(c => c.Writer == SwShRestorationWriter.Voices);
            allowLargeBankRepair |= intent.Enabled || intent.UpdateAssets;
            selections.TryGetValue(intent.DefinitionId, out var installed);
            if (installed?.RemovalId is not null && intent.Enabled) installed = null;
            var freshInstall = intent.Enabled && installed is null && !intent.AdoptExternal && !intent.UpdateAssets;
            if (!freshInstall) FlushVoices();
            if (intent.AdoptExternal && (installed?.Enabled == true || !externalDefinitions.Value.Contains(intent.DefinitionId)))
                throw new InvalidDataException("This Pokemon is no longer an external installation. Reload the restoration list and stage again.");
            if (intent.AdoptExternal) installed = null;
            if (intent.Enabled && installed is null && !intent.AdoptExternal && SwShRestorationModelResources.UsesLegacyLayout(bundle))
                throw new InvalidDataException("Prepare this restoration again to use its separate model files.");
            if (intent.RemovalId is not null && installed?.Enabled != true)
            {
                selections[intent.DefinitionId] = RemovedSelection(intent, installed);
                ReportSelection();
                continue;
            }
            if (!intent.Enabled && !intent.UpdateAssets && (installed is null || !installed.Enabled)) throw new InvalidDataException("Only an installed, owned restoration can be removed.");
            VerifyTargetSources(paths, bundle, targetFingerprints, intent.UpdateAssets);
            var effective = installed?.CustomizationHash is { } customHash ? SwShRestorationStateStore.ReadPrepared(paths, customHash) : bundle;
            SwShRestorationContribution[] defaultUpdates = [];
            var customization = installed?.CustomizationHash;
            if (intent.UpdateAssets)
            {
                if (installed is null || installed.RemovalId is not null || installed.Enabled != intent.Enabled)
                    throw new InvalidDataException("Update assets only for an existing owned restoration without changing its enabled state.");
                var previous = SwShRestorationStateStore.ReadPrepared(paths, installed.PreparedHash);
                var update = SwShRestorationAssetUpdate.Create(paths, previous, bundle,
                    customization is null ? previous : effective, snapshot.State, installed.Enabled, owned, Current);
                var retained = SwShRestorationStateStore.Store(paths, update.Customized);
                using (retained.Lease) customization = retained.Hash == intent.PreparedHash ? null : retained.Hash;
                foreach (var file in update.Files) outputs[file.Key] = file.Value;
                selections[intent.DefinitionId] = installed with { PreparedHash = intent.PreparedHash, CustomizationHash = customization, AssetRevision = bundle.AssetRevision,
                    AssetHashes = installed.AssetHashes.Concat(new[] { intent.PreparedHash, installed.PreparedHash, installed.CustomizationHash, customization }
                        .OfType<string>()).Distinct(StringComparer.Ordinal).ToArray() };
                ReportSelection();
                continue;
            }
            if (installed is not null && installed.PreparedHash != intent.PreparedHash)
            {
                if (!intent.Enabled) throw new InvalidDataException("Remove a restoration using its installed definition.");
                var previous = SwShRestorationStateStore.ReadPrepared(paths, installed.PreparedHash);
                effective = SwShRestorationIconUpdate.Rebase(previous, bundle, customization is null ? previous : effective, out defaultUpdates);
                if (customization is not null)
                {
                    var rebased = SwShRestorationStateStore.Store(paths, effective);
                    using (rebased.Lease) customization = rebased.Hash;
                }
            }
            if (effective.DefinitionId != bundle.DefinitionId) throw new InvalidDataException("Retained customizations belong to another definition.");
            var preparedHash = intent.PreparedHash;
            if (intent.Enabled && installed is { Enabled: false } && SwShRestorationModelResources.UsesLegacyLayout(bundle))
            {
                var migration = SwShRestorationLegacyModels.For(bundle);
                var relocated = migration.Move(bundle, bundle);
                effective = customization is null ? relocated : migration.Move(effective, bundle, relocated);
                foreach (var item in effective.Contributions.Where(c => c.Writer == SwShRestorationWriter.File && migration.IsDestination(c.Path)))
                    if (Current(item.Path) is not null)
                        throw new InvalidDataException($"Restoration cannot replace an existing file while relocating a retained model: {item.Path}.");
                var stored = SwShRestorationStateStore.Store(paths, relocated);
                using (stored.Lease) preparedHash = stored.Hash;
                if (customization is not null)
                {
                    var retained = SwShRestorationStateStore.Store(paths, effective);
                    using (retained.Lease) customization = retained.Hash == preparedHash ? null : retained.Hash;
                }
            }
            if (!intent.Enabled) effective = SwShRestorationCustomizations.IncludeLooseModels(paths, effective, owned);
            if (!intent.Enabled) VerifyVoiceRemoval(paths, effective, Current(SwShRestorationComposer.VoicesPath)!);
            var peerKeys = selections.Values.Where(s => s.Enabled && s.DefinitionId != intent.DefinitionId)
                .SelectMany(s => ProtectedKeys(s.CustomizationHash ?? s.PreparedHash)).ToHashSet();
            var shared = includeAllKeys ? peerKeys.Where(IsShared).ToHashSet() : peerKeys;
            foreach (var item in effective.Contributions.Where(c => c.Writer == SwShRestorationWriter.File))
            {
                // Native parent tables own only the added regional route.
                if (SwShRestorationEvolutionPolicy.IsNativeParentFile(SwShRestorationDefinitions.Get(intent.DefinitionId), item)) continue;
                if (!System.IO.File.Exists(Output(paths, item.Path))) continue;
                var nativeFallback = intent.Enabled && item.Before is not null
                    && (BoundedRead(Output(paths, item.Path)).AsSpan().SequenceEqual(item.Before)
                        || (owned.Contains(item.Path) && normalizedFormStorage is not null
                            && normalizedFormStorage.TryGetValue(item.Path, out var normalized)
                            && normalized is not null && normalized.AsSpan().SequenceEqual(item.Before)));
                var sharedOwned = owned.Contains(item.Path) && shared.Contains((item.Path, item.Writer, item.Key));
                if (!nativeFallback && !sharedOwned && !intent.AdoptExternal && (installed is null || !owned.Contains(item.Path)))
                    throw new InvalidDataException($"Restoration cannot replace or remove an externally supplied file: {item.Path}.");
            }
            // Keep shared camp resources and held items until their last owner is removed.
            var contributions = effective.Contributions;
            if (!intent.Enabled)
            {
                contributions = contributions.Where(c => !shared.Contains((c.Path, c.Writer, c.Key))).ToArray();
            }
            else
            {
                if (installed is { Enabled: false } && customization is not null)
                    contributions = contributions.Select(SwShRestorationForms.NormalizeRetainedEgg).ToArray();
                contributions = RetainSharedAbilityText(contributions, shared, owned, Current);
            }
            var adoption = intent.AdoptExternal
                ? contributions.Select(c => (c.Path, c.Writer, c.Key)).Except(peerKeys).ToHashSet()
                : null;
            if (adoption is not null)
                foreach (var (path, writer, _) in adoption)
                    if (writer == SwShRestorationWriter.File) adoptedWholeFiles.Add(path);
            if (freshInstall)
            {
                var addedVoices = contributions.Where(c => c.Writer == SwShRestorationWriter.Voices).ToArray();
                if (pendingVoices.Count + addedVoices.Length > SwShRestorationComposer.MaximumContributions
                    || addedVoices.Any(c => pendingVoiceKeys.Contains(c.Key)))
                    FlushVoices();
                pendingVoices.AddRange(addedVoices);
                pendingVoiceKeys.UnionWith(addedVoices.Select(c => c.Key));
                contributions = contributions.Where(c => c.Writer != SwShRestorationWriter.Voices).ToArray();
            }
            var composition = contributions.Count == 0 ? new SwShRestorationComposition(new Dictionary<string, byte[]?>(), [])
                : SwShRestorationComposer.Compose(contributions, Current, intent.Enabled,
                allowOwnedCustomizations: installed?.Enabled == true, ownedPaths: owned, adoption: adoption);
            if (intent.AdoptExternal && composition.RetainedCustomizations.Count > 0)
            {
                var retained = composition.RetainedCustomizations.ToDictionary(c => (c.Path, c.Writer, c.Key));
                var stored = SwShRestorationStateStore.Store(paths, effective with { Contributions = effective.Contributions
                    .Select(c => retained.GetValueOrDefault((c.Path, c.Writer, c.Key), c)).ToArray() });
                using (stored.Lease) customization = stored.Hash;
            }
            foreach (var file in composition.Files) outputs[file.Key] = file.Value;
            if (installed?.Enabled == true && defaultUpdates.Length > 0)
            {
                var refreshed = SwShRestorationComposer.Compose(defaultUpdates, Current, true, allowOwnedCustomizations: true, ownedPaths: owned);
                foreach (var file in refreshed.Files) outputs[file.Key] = file.Value;
            }
            if (!intent.Enabled)
            {
                if (intent.RemovalId is null)
                {
                var retained = composition.RetainedCustomizations.ToDictionary(c => (c.Path, c.Key));
                var customized = effective with { Contributions = effective.Contributions.Select(c =>
                    SwShRestorationDependencies.Retain(c, retained.GetValueOrDefault((c.Path, c.Key), c), snapshot.State)).ToArray() };
                var stored = SwShRestorationStateStore.Store(paths, customized); using (stored.Lease) customization = stored.Hash;
                }
                foreach (var identity in SwShRestorationDefinitions.Get(intent.DefinitionId).Identities) removed.Add(identity);
            }
            selections[intent.DefinitionId] = intent.RemovalId is not null ? RemovedSelection(intent, installed)
                : new(intent.DefinitionId, preparedHash, intent.Enabled, customization)
                { AssetRevision = installed?.AssetRevision ?? bundle.AssetRevision,
                    AssetHashes = (installed?.AssetHashes ?? []).Concat(new[] { preparedHash, intent.PreparedHash, installed?.PreparedHash, installed?.CustomizationHash, customization }.OfType<string>()).Distinct(StringComparer.Ordinal).ToArray() };
            ReportSelection();
        }
        reportProgress?.Invoke(new("validating"));
        FlushVoices();
        if (outputs.TryGetValue(SwShRestorationComposer.VoicesPath, out var voices)
            && voices is not null && voices.Length > SwShRestorationSourceBudget.MaximumVoiceBankBytes)
        {
            throw new InvalidDataException(
                $"The shared sound bank Common.bnk would be {voices.Length:N0} bytes. The editor can save up to {SwShRestorationSourceBudget.MaximumVoiceBankBytes:N0} bytes in that file.");
        }

        var state = new SwShRestorationState(1, selections.Values.OrderBy(s => s.DefinitionId, StringComparer.Ordinal).ToArray());
        if (!assetsOnly)
            foreach (var file in SwShRestorationDependencies.Reconcile(paths, snapshot.State, state, Current)) outputs[file.Key] = file.Value;
        if (hasForms)
            foreach (var file in SwShRestorationForms.Storage(NativeFormFile, Current, state, normalize: false)) outputs[file.Key] = file.Value;
        if (!assetsOnly && (outputs.ContainsKey(SwShRestorationComposer.PersonalPath) || outputs.ContainsKey(SwShRestorationComposer.LearnsetPath)))
        {
            var personal = Current(SwShRestorationComposer.PersonalPath)!;
            var learnset = Current(SwShRestorationComposer.LearnsetPath)!;
            if (personal.Length > SwShRestorationSpecies.NativeRecordCount * 176 || learnset.Length > SwShRestorationSpecies.NativeRecordCount * 260)
            {
                var aligned = SwShRestorationSpecies.AlignTables(personal, learnset);
                if (!aligned.Personal.AsSpan().SequenceEqual(personal)) outputs[SwShRestorationComposer.PersonalPath] = aligned.Personal;
                if (!aligned.Learnset.AsSpan().SequenceEqual(learnset)) outputs[SwShRestorationComposer.LearnsetPath] = aligned.Learnset;
            }
        }
        var writesVoices = outputs.ContainsKey(SwShRestorationComposer.VoicesPath);
        var bankBytes = voices?.LongLength ?? 0;
        if (!writesVoices && allowLargeBankRepair && paths.SelectedGame == ProjectGame.Sword)
            bankBytes = EffectiveVoiceBankBytes(paths, sources);
        if (needsSoundMemory && (!assetsOnly || writesVoices)
            || allowLargeBankRepair && paths.SelectedGame == ProjectGame.Sword
                && SwShRestorationSoundMemoryPatcher.NeedsLargeBankBudget(bankBytes))
            AddSoundMemory(paths, outputs, sources, bankBytes);
        var updatingAssets = edits.Any(edit => Parse(edit).UpdateAssets);
        var installingAssets = edits.Any(edit => Parse(edit).Enabled);
        var needsModelMemory = assetsOnly ? installingAssets || updatingAssets : SwShRestorationDependencies.Enabled(state).Count != 0;
        if (!assetsOnly)
        {
            AddMegaItemProtection(paths, state, outputs, sources);
            AddSpeciesSupport(paths, state, outputs, sources);
            AddAbilitySupport(paths, state, outputs, sources, owned);
            if (needsModelMemory)
            {
                AddTableMemory(paths, outputs, sources);
                AddModelMemory(paths, outputs, sources);
            }
            AddCryRouting(paths, state, outputs, sources);
        }
        else if (needsModelMemory)
            AddModelMemory(paths, outputs, sources);
        if (!needsModelMemory && (pokedexRepair || updatingAssets))
            AddModelMemory(paths, outputs, sources, repairOnly: true);
        if (battleRepair || installingAssets || updatingAssets)
            AddBattleHeap(paths, outputs, sources);
        AddSamplerSupport(paths, state, outputs, sources);
        var storedState = SwShRestorationStateStore.Store(paths, state); using (storedState.Lease) { }
        state = SwShRestorationStateStore.ReadState(paths, storedState.Hash).State;
        if (!outputs.ContainsKey(SwShRestorationComposer.PersonalPath)) outputs.Add(SwShRestorationComposer.PersonalPath, Current(SwShRestorationComposer.PersonalPath)
            ?? throw new InvalidDataException("Restoration state requires a personal data anchor."));
        return new(outputs, storedState.Hash, snapshot.Hash, state, sources.OrderBy(s => s.RelativePath, StringComparer.Ordinal).ThenBy(s => s.Layer).ToArray(),
            removed.Select(i => i.Species).Distinct().Order().ToArray())
            { RemovedIdentities = removed.OrderBy(i => i.Species).ThenBy(i => i.Form).ToArray(), AdoptedWholeFiles = adoptedWholeFiles };
    }

    private static void AddCryRouting(ProjectPaths paths, SwShRestorationState state,
        IDictionary<string, byte[]?> outputs, ISet<ProjectFileReference> sources)
    {
        var enabled = SwShRestorationDependencies.Enabled(state).ToArray();
        var wanted = enabled.Any(identity => KM.Formats.SwSh.SwShVoiceRouting.EventForm(identity.Species, identity.Form) != identity.Form);
        const string relative = "exefs/main";
        var output = Output(paths, relative, includeExecutable: true);
        byte[]? current = outputs.TryGetValue(relative, out var pending) ? pending : null;
        if (current is null && System.IO.File.Exists(output))
        {
            current = BoundedRead(output);
            sources.Add(new(ProjectFileLayer.Layered, relative));
        }
        if (!wanted && (current is null || !SwShRestorationCryPatcher.HasInstalledPatch(current))) return;
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
            throw new InvalidDataException("Select the game's Base ExeFS folder to support independent restored form cries.");
        var baseline = BoundedRead(Path.Combine(paths.BaseExeFsPath, "main"));
        sources.Add(new(ProjectFileLayer.Base, relative));
        current ??= baseline;
        var patched = SwShRestorationCryPatcher.Apply(baseline, current, paths.SelectedGame!.Value, enabled);
        if (!patched.AsSpan().SequenceEqual(current)) outputs[relative] = patched;
    }

    private static void AddSpeciesSupport(ProjectPaths paths, SwShRestorationState state,
        IDictionary<string, byte[]?> outputs, ISet<ProjectFileReference> sources)
    {
        var enabled = SwShRestorationDependencies.Enabled(state);
        var newSpecies = enabled.Any(identity => identity.Species > SwShEffectivePokemonCatalog.MaximumSpecies);
        if (!newSpecies && !enabled.Any(identity => identity.Form != 0)) return;
        var personal = outputs.TryGetValue(SwShRestorationComposer.PersonalPath, out var pendingPersonal) && pendingPersonal is not null
            ? pendingPersonal : SwShEffectivePokemonCatalog.Read(paths, SwShRestorationComposer.PersonalPath, sources: sources);
        var independentForms = enabled.Any(identity => identity.Form != 0
            && (personal[SwShRestorationSpecies.BaseRow(identity.Species) * KM.Formats.SwSh.SwShPersonalTable.RecordSize + 33] & 64) == 0);
        if (!independentForms && !newSpecies) return;
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
            throw new InvalidDataException("Select the game's Base ExeFS folder to support the restored Pokemon identity.");
        if (newSpecies)
        {
            var nativePersonal = BoundedRead(Path.Combine(paths.BaseRomFsPath!, SwShRestorationComposer.PersonalPath[6..]));
            sources.Add(new(ProjectFileLayer.Base, SwShRestorationComposer.PersonalPath));
            SwShRestorationSpecies.ValidateReservation(nativePersonal, personal);
        }
        const string relative = "exefs/main";
        var baseline = BoundedRead(Path.Combine(paths.BaseExeFsPath, "main")); sources.Add(new(ProjectFileLayer.Base, relative));
        var current = baseline; var output = Output(paths, relative, includeExecutable: true);
        if (outputs.TryGetValue(relative, out var pending) && pending is not null) current = pending;
        else if (System.IO.File.Exists(output)) { current = BoundedRead(output); sources.Add(new(ProjectFileLayer.Layered, relative)); }
        var patched = independentForms ? SwShRestorationSpeciesPatcher.ApplyIndependentForms(baseline, current, paths.SelectedGame!.Value)
            : SwShRestorationSpeciesPatcher.Apply(baseline, current, paths.SelectedGame!.Value);
        if (!patched.AsSpan().SequenceEqual(current)) outputs[relative] = patched;
    }

    private static void AddMegaItemProtection(ProjectPaths paths, SwShRestorationState state,
        IDictionary<string, byte[]?> outputs, ISet<ProjectFileReference> sources)
    {
        static bool HasItems(ProjectPaths paths, SwShRestorationState state) => state.Selections.Where(s => s.Enabled)
            .Any(s => SwShRestorationStateStore.ReadPrepared(paths, s.CustomizationHash ?? s.PreparedHash)
                .Contributions.Any(c => c.Writer == SwShRestorationWriter.Items));
        if (!HasItems(paths, state)) return;
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
            throw new InvalidDataException("Select the game's Base ExeFS folder to protect restored Mega Stones in battle.");
        const string relative = "exefs/main";
        var baseline = BoundedRead(Path.Combine(paths.BaseExeFsPath, "main"));
        sources.Add(new(ProjectFileLayer.Base, relative));
        var current = baseline;
        var output = Output(paths, relative, includeExecutable: true);
        if (outputs.TryGetValue(relative, out var pending) && pending is not null) current = pending;
        else if (System.IO.File.Exists(output)) { current = BoundedRead(output); sources.Add(new(ProjectFileLayer.Layered, relative)); }
        // Category-based protection is harmless with no active stone rows and may
        // also serve stones installed by another feature; leave it installed.
        var patched = SwShRestorationMegaItemProtection.Apply(baseline, current, paths.SelectedGame!.Value);
        if (!patched.AsSpan().SequenceEqual(current)) outputs[relative] = patched;
    }

    private static void AddTableMemory(ProjectPaths paths, IDictionary<string, byte[]?> outputs, ISet<ProjectFileReference> sources)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
            throw new InvalidDataException("Select the game's Base ExeFS folder to apply the memory required by restored Pokemon tables.");
        const string relative = SwShRestorationTableMemoryPatcher.MainPath;
        var baseline = BoundedRead(Path.Combine(paths.BaseExeFsPath, "main")); sources.Add(new(ProjectFileLayer.Base, relative));
        var current = baseline; var output = Output(paths, relative, includeExecutable: true);
        if (outputs.TryGetValue(relative, out var pending) && pending is not null) current = pending;
        else if (System.IO.File.Exists(output)) { current = BoundedRead(output); sources.Add(new(ProjectFileLayer.Layered, relative)); }
        var patched = SwShRestorationTableMemoryPatcher.Apply(baseline, current, paths.SelectedGame!.Value);
        if (!patched.AsSpan().SequenceEqual(current)) outputs[relative] = patched;
    }

    private static long EffectiveVoiceBankBytes(ProjectPaths paths, ISet<ProjectFileReference>? sources = null)
    {
        const string relative = SwShRestorationComposer.VoicesPath;
        var effectiveSources = new HashSet<ProjectFileReference>();
        var bank = FindEffectiveFile(paths, relative, effectiveSources);
        var bytes = bank is null ? 0 : new FileInfo(bank).Length;
        if (sources is not null && SwShRestorationSoundMemoryPatcher.NeedsLargeBankBudget(bytes))
            sources.UnionWith(effectiveSources);
        return bytes;
    }

    private static void AddSoundMemory(ProjectPaths paths, IDictionary<string, byte[]?> outputs, ISet<ProjectFileReference> sources, long bankBytes)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
            throw new InvalidDataException("Select the game's Base ExeFS folder to apply the sound memory required by restored Pokemon.");
        const string relative = SwShRestorationSoundMemoryPatcher.MainPath;
        var baseline = BoundedRead(Path.Combine(paths.BaseExeFsPath, "main"));
        sources.Add(new(ProjectFileLayer.Base, relative));
        var output = Output(paths, relative, includeExecutable: true);
        var current = baseline;
        if (outputs.TryGetValue(relative, out var pending) && pending is not null) current = pending;
        else if (System.IO.File.Exists(output)) { current = BoundedRead(output); sources.Add(new(ProjectFileLayer.Layered, relative)); }
        var patched = SwShRestorationSoundMemoryPatcher.Apply(baseline, current, paths.SelectedGame!.Value, bankBytes);
        if (!patched.AsSpan().SequenceEqual(current)) outputs[relative] = patched;
    }

    private static byte[] ReadBattleBase(ProjectPaths paths)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
            throw new InvalidDataException("Select the game's original Base ExeFS folder to repair battle memory.");
        return BoundedRead(Path.Combine(paths.BaseExeFsPath, "main"));
    }

    private static void AddBattleHeap(ProjectPaths paths, IDictionary<string, byte[]?> outputs, ISet<ProjectFileReference> sources)
    {
        const string relative = SwShRestorationModelMemoryPatcher.MainPath;
        var baseline = ReadBattleBase(paths);
        sources.Add(new(ProjectFileLayer.Base, relative));
        var output = Output(paths, relative, includeExecutable: true);
        if (!outputs.TryGetValue(relative, out var current) || current is null)
        {
            if (!System.IO.File.Exists(output))
                throw new InvalidDataException("The restoration executable is missing from the output folder.");
            current = BoundedRead(output);
            sources.Add(new(ProjectFileLayer.Layered, relative));
        }
        var patched = SwShRestorationBattleHeapPatcher.Apply(baseline, current, paths.SelectedGame!.Value);
        if (!ReferenceEquals(patched, current)) outputs[relative] = patched;
    }

    private static void AddModelMemory(ProjectPaths paths, IDictionary<string, byte[]?> outputs, ISet<ProjectFileReference> sources,
        bool repairOnly = false)
    {
        const string relative = SwShRestorationModelMemoryPatcher.MainPath;
        var output = Output(paths, relative, includeExecutable: true);
        outputs.TryGetValue(relative, out var current);
        if (current is null && System.IO.File.Exists(output))
        {
            current = BoundedRead(output);
            sources.Add(new(ProjectFileLayer.Layered, relative));
        }
        var game = paths.SelectedGame!.Value;
        if (repairOnly && (current is null || !SwShRestorationModelMemoryPatcher.Read(current, game).NeedsPokedexRepair)) return;
        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
            throw new InvalidDataException("Select the game's Base ExeFS folder to apply the memory required by restored Pokemon models.");
        var baseline = BoundedRead(Path.Combine(paths.BaseExeFsPath, "main"));
        sources.Add(new(ProjectFileLayer.Base, relative));
        current ??= baseline;
        var patched = SwShRestorationModelMemoryPatcher.Apply(baseline, current, game);
        if (!patched.AsSpan().SequenceEqual(current)) outputs[relative] = patched;
    }

    public static IReadOnlyList<ProjectFileReference> WritePreview(ProjectPaths paths, SwShRestorationPreview preview)
    {
        var written = new List<ProjectFileReference>();
        foreach (var (path, bytes) in preview.Files)
        {
            var target = Output(paths, path, includeExecutable: true);
            if (bytes is null) { if (System.IO.File.Exists(target)) System.IO.File.Delete(target); }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); System.IO.File.WriteAllBytes(target, bytes);
                if (!System.IO.File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) throw new IOException("Restoration output failed its readback check.");
            }
            written.Add(new(ProjectFileLayer.Generated, path));
        }
        return written;
    }

    private static void VerifyVoiceRemoval(ProjectPaths paths, SwShPreparedRestoration prepared, byte[] current)
    {
        var items = prepared.Contributions.Where(c => c.Writer == SwShRestorationWriter.Voices).ToArray();
        var objectIds = items.Where(c => !c.Key.StartsWith("media:", StringComparison.Ordinal)).Select(c => uint.Parse(c.Key.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
        var mediaIds = items.Where(c => c.Key.StartsWith("media:", StringComparison.Ordinal)).Select(c => uint.Parse(c.Key[6..], System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
        var native = new SoundBankDocument(BoundedRead(Path.Combine(paths.BaseRomFsPath!, SwShRestorationComposer.VoicesPath[6..])));
        var bank = new SoundBankDocument(current);
        if (bank.Version != 128 || native.Version != 128) throw new InvalidDataException("Voice removal requires a supported native bank layout.");
        var nativeObjects = native.Objects.ToLookup(o => (o.Type, o.Id));
        foreach (var node in bank.Objects.Where(o => !objectIds.Contains(o.Id)))
        {
            if (nativeObjects[(node.Type, node.Id)].Any(o => o.Payload.AsSpan().SequenceEqual(node.Payload))) continue;
            var payload = node.Payload;
            uint Read(int at)
            {
                if (at < 0 || at > payload.Length - 4) throw new InvalidDataException("An external voice reference is truncated.");
                return BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(at));
            }
            var referenced = node.Type switch
            {
                2 when payload.Length >= 25 => objectIds.Contains(Read(21)) || mediaIds.Contains(Read(5)),
                3 when payload.Length >= 6 => objectIds.Contains(Read(2)),
                4 when payload.Length >= 1 && payload[0] <= 64 && payload.Length >= 1 + payload[0] * 4
                    => Enumerable.Range(0, payload[0]).Any(i => objectIds.Contains(Read(1 + i * 4))),
                7 when SoundBankActorMixer.TryReadReferences(bank.Version, payload, out var references)
                    => references.Any(objectIds.Contains),
                _ => throw new InvalidDataException($"External voice object {node.Type}:{node.Id} has an unsupported reference layout. Removal cannot safely inspect its dependencies."),
            };
            if (referenced) throw new InvalidDataException($"External voice object {node.Type}:{node.Id} still references this restoration. Remove that reference first.");
        }
    }
    private static SwShRestorationSelection RemovedSelection(SwShRestorationIntent intent, SwShRestorationSelection? installed) =>
        new(intent.DefinitionId, intent.PreparedHash, false)
        {
            RemovalId = intent.RemovalId,
            RemovedHashes = new[] { intent.PreparedHash, installed?.PreparedHash, installed?.CustomizationHash }
                .OfType<string>().Concat(intent.AdditionalPreparedHashes).Concat(installed?.RemovedHashes ?? []).Concat(installed?.AssetHashes ?? []).Distinct(StringComparer.Ordinal).ToArray()
        };

    private static PendingEdit CreateEdit(ProjectPaths paths, SwShRestorationIntent intent)
    {
        paths = SwShChangePlanSourceGuard.OriginalPaths(paths);
        var bundle = SwShRestorationStateStore.ReadPrepared(paths, intent.PreparedHash);
        if (bundle.DefinitionId != intent.DefinitionId) throw new InvalidDataException("Prepared restoration does not match its selection.");
        if (intent.AdditionalPreparedHashes is null || intent.AdditionalPreparedHashes.Count > 16) throw new InvalidDataException("Prepared removal set is invalid.");
        foreach (var hash in intent.AdditionalPreparedHashes)
            if (SwShRestorationStateStore.ReadPrepared(paths, hash).DefinitionId != intent.DefinitionId)
                throw new InvalidDataException("Prepared removal data belongs to another Pokemon.");
        var sources = new HashSet<ProjectFileReference>();
        var contributions = intent.UpdateAssets ? SwShRestorationAssetUpdate.Inputs(bundle) : bundle.Contributions;
        if (intent.UpdateAssets)
        {
            var installed = SwShRestorationStateStore.Read(paths).State.Selections.SingleOrDefault(s => s.DefinitionId == intent.DefinitionId);
            if (installed is not null)
                contributions = contributions.Concat(SwShRestorationAssetUpdate.Inputs(
                    SwShRestorationStateStore.ReadPrepared(paths, installed.CustomizationHash ?? installed.PreparedHash)));
        }
        foreach (var path in contributions.Select(c => c.Path).Distinct(StringComparer.Ordinal))
        {
            _ = FindEffectiveFile(paths, path, sources);
            // Retired assets can restore native bytes hidden beneath owned output.
            // Bind that base input as well as the effective overlay before review.
            if (intent.UpdateAssets && SwShRestorationAssetUpdate.IsAssetPath(path)
                && File.Exists(Path.Combine(paths.BaseRomFsPath!, path[6..])))
                sources.Add(new(ProjectFileLayer.Base, path));
        }
        foreach (var source in bundle.Sources.Where(s => s.Role == "target"
            && (!intent.UpdateAssets || SwShRestorationAssetUpdate.IsInputPath(bundle, "romfs/" + s.RelativePath))))
            sources.Add(new(ProjectFileLayer.Base, "romfs/" + source.RelativePath));
        if (intent.UpdateAssets) sources.Add(new(ProjectFileLayer.Base, SwShRestorationComposer.PersonalPath));
        return new(Domain, (intent.AdoptExternal ? "Replace external installation for " : intent.UpdateAssets ? "Update assets for " : intent.RemovalId is not null ? "Remove assets for " : intent.Enabled ? "Restore " : "Remove restored ") + intent.DefinitionId, sources.OrderBy(s => s.RelativePath, StringComparer.Ordinal).ThenBy(s => s.Layer).ToArray(),
            intent.DefinitionId, "enabled", JsonSerializer.Serialize(intent), Domain);
    }
    public static EditSession UpgradeSession(ProjectPaths paths, EditSession session)
    {
        if (!session.PendingEdits.Any(e => e.Domain == Domain && e.RecordId == SwShRestorationDefinitions.Piplup.Id)) return session;
        var edits = new List<PendingEdit>();
        foreach (var edit in session.PendingEdits)
        {
            if (edit.Domain != Domain || edit.RecordId != SwShRestorationDefinitions.Piplup.Id) { edits.Add(edit); continue; }
            var intent = Parse(edit); var expected = CreateEdit(paths, intent);
            if (edit.Summary != expected.Summary || edit.Owner != expected.Owner || edit.Field != expected.Field || !edit.Sources.SequenceEqual(expected.Sources))
                throw new InvalidDataException("Staged family restoration metadata is invalid.");
            foreach (var definition in SwShRestorationDefinitions.PiplupSpecies)
                edits.Add(CreateEdit(paths, intent with { DefinitionId = definition.Id,
                    PreparedHash = SwShRestorationCompatibility.Split(paths, intent.PreparedHash, definition.Id) }));
        }
        return session with { PendingEdits = edits };
    }
    private static SwShRestorationIntent Parse(PendingEdit edit)
    {
        if (edit.NewValue is null || edit.NewValue.Length > 4096) throw new InvalidDataException("Staged restoration intent is missing or oversized.");
        var intent = JsonSerializer.Deserialize<SwShRestorationIntent>(edit.NewValue) ?? throw new InvalidDataException("Staged restoration intent is empty.");
        if (intent.DefinitionId != edit.RecordId) throw new InvalidDataException("Staged restoration identity is inconsistent.");
        if (intent.RemovalId is not null && (intent.Enabled || intent.UpdateAssets || !Guid.TryParseExact(intent.RemovalId, "N", out _)))
            throw new InvalidDataException("Staged asset removal is invalid.");
        if (intent.AdoptExternal && (!intent.Enabled || intent.UpdateAssets || intent.RemovalId is not null))
            throw new InvalidDataException("Staged external asset update is invalid.");
        _ = SwShRestorationDefinitions.Get(intent.DefinitionId); return intent;
    }
    private static void VerifyTargetSources(ProjectPaths paths, SwShPreparedRestoration prepared,
        Dictionary<string, SwShRestorationSourceFingerprint> fingerprints, bool assetsOnly)
    {
        foreach (var source in prepared.Sources.Where(s => s.Role == "target"
            && (!assetsOnly || SwShRestorationAssetUpdate.IsInputPath(prepared, "romfs/" + s.RelativePath))))
        {
            if (!fingerprints.TryGetValue(source.RelativePath, out var fingerprint))
            {
                _ = new KM.Core.Semantics.RelativeOutputPath(source.RelativePath);
                var path = Path.Combine(paths.BaseRomFsPath!, source.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                fingerprint = source with { Length = stream.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(stream)) };
                fingerprints.Add(source.RelativePath, fingerprint);
            }
            if (fingerprint.Length != source.Length || fingerprint.Sha256 != source.Sha256)
                throw new InvalidDataException("The target dump changed since restoration preparation. Prepare the Pokemon again.");
        }
    }
    private static byte[]? ReadEffective(ProjectPaths paths, string relative, ISet<ProjectFileReference> sources)
    {
        var path = FindEffectiveFile(paths, relative, sources);
        return path is null ? null : BoundedRead(path, SwShRestorationSourceBudget.MaximumBytes(relative));
    }
    private static string? FindEffectiveFile(ProjectPaths paths, string relative, ISet<ProjectFileReference> sources)
    {
        var output = Output(paths, relative);
        var native = Path.Combine(paths.BaseRomFsPath!, relative[6..].Replace('/', Path.DirectorySeparatorChar));
        if (System.IO.File.Exists(output)) { sources.Add(new(ProjectFileLayer.Layered, relative)); return output; }
        if (System.IO.File.Exists(native)) { sources.Add(new(ProjectFileLayer.Base, relative)); return native; }
        return null;
    }
    private static byte[] BoundedRead(string path, int maximumBytes = 64 * 1024 * 1024)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximumBytes) throw new InvalidDataException("Restoration source exceeds its file budget.");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
    }
    private static string Output(ProjectPaths paths, string relative, bool includeExecutable = false)
    {
        _ = new KM.Core.Semantics.RelativeOutputPath(relative);
        if (!(relative.StartsWith("romfs/", StringComparison.Ordinal) || includeExecutable && relative.StartsWith("exefs/", StringComparison.Ordinal))
            || string.IsNullOrWhiteSpace(paths.OutputRootPath)) throw new InvalidDataException("Restoration output path is invalid.");
        return Path.Combine(paths.OutputRootPath, relative.Replace('/', Path.DirectorySeparatorChar));
    }
    internal static bool SourceError(Exception exception) => exception is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException
        or JsonException or OverflowException or KM.Core.Output.OutputCoordinatorException;
    internal static ValidationDiagnostic Error(string code, string message) => new(DiagnosticSeverity.Error, message, Domain: Domain) { Code = code };
    private static ApplyResult Result(ChangePlan plan, IReadOnlyList<ProjectFileReference> written, IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        var id = Guid.NewGuid().ToString("N"); var now = DateTimeOffset.UtcNow;
        return new(id, now, written, new(id, now, plan.Writes), diagnostics);
    }
}
