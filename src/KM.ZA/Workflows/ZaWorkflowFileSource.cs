// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.ZA;

namespace KM.ZA.Workflows;

internal sealed class ZaWorkflowFileSource
{
    public const string DescriptorVirtualPath = ZaTrinityDescriptorPatcher.DescriptorVirtualPath;

    private static readonly ConcurrentDictionary<string, object> OutputRootLocks = new(
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

    private readonly ZaCacheManager cacheManager;

    public ZaWorkflowFileSource(ZaCacheManager? cacheManager = null)
    {
        this.cacheManager = cacheManager ?? new ZaCacheManager();
    }

    public ZaWorkflowFile Read(OpenedProject project, string virtualRomFsPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);

        var normalizedVirtualPath = NormalizeVirtualPath(virtualRomFsPath);
        var relativePath = ToRelativePath(normalizedVirtualPath);
        var entry = FindEntry(project, relativePath);

        if (!string.IsNullOrWhiteSpace(project.Paths.OutputRootPath))
        {
            var trinityModManagerPath = CombineGraphPath(project.Paths.OutputRootPath, normalizedVirtualPath);
            var standalonePath = CombineGraphPath(project.Paths.OutputRootPath, relativePath);
            var looseOutput = SelectLatestLooseOutput(trinityModManagerPath, standalonePath);
            if (looseOutput is not null)
            {
                return new ZaWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    File.ReadAllBytes(looseOutput.Value.Path),
                    ProjectFileLayer.Layered,
                    looseOutput.Value.IsStandalone
                        ? entry?.State ?? ProjectFileGraphEntryState.LayeredOverride
                        : ProjectFileGraphEntryState.LayeredOverride,
                    looseOutput.Value.IsStandalone
                        ? ZaWorkflowFileOrigin.StandaloneLooseOutput
                        : ZaWorkflowFileOrigin.TrinityModManagerLooseOutput);
            }

            if (TryReadOutputArchive(project.Paths, normalizedVirtualPath, out var layeredArchiveBytes))
            {
                return new ZaWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    layeredArchiveBytes,
                    ProjectFileLayer.Layered,
                    ProjectFileGraphEntryState.LayeredOverride,
                    ZaWorkflowFileOrigin.OutputArchive);
            }
        }

        if (!string.IsNullOrWhiteSpace(project.Paths.BaseRomFsPath))
        {
            var looseBasePath = CombineGraphPath(project.Paths.BaseRomFsPath, normalizedVirtualPath);
            if (File.Exists(looseBasePath))
            {
                return new ZaWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    File.ReadAllBytes(looseBasePath),
                    ProjectFileLayer.Base,
                    entry?.State ?? ProjectFileGraphEntryState.BaseOnly,
                    ZaWorkflowFileOrigin.LooseBase);
            }

            try
            {
                var archiveBytes = cacheManager.ReadBaseTrinityFile(project.Paths, normalizedVirtualPath);
                return new ZaWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    archiveBytes,
                    ProjectFileLayer.Base,
                    entry?.State ?? ProjectFileGraphEntryState.BaseOnly,
                    ZaWorkflowFileOrigin.BaseArchive);
            }
            catch (FileNotFoundException)
            {
            }
        }

        throw new FileNotFoundException($"Pokemon Legends Z-A file '{relativePath}' could not be resolved.");
    }

    public ZaWorkflowFile ReadBase(OpenedProject project, string virtualRomFsPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);

        var normalizedVirtualPath = NormalizeVirtualPath(virtualRomFsPath);
        var relativePath = ToRelativePath(normalizedVirtualPath);
        var entry = FindEntry(project, relativePath);

        if (!string.IsNullOrWhiteSpace(project.Paths.BaseRomFsPath))
        {
            var looseBasePath = CombineGraphPath(project.Paths.BaseRomFsPath, normalizedVirtualPath);
            if (File.Exists(looseBasePath))
            {
                return new ZaWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    File.ReadAllBytes(looseBasePath),
                    ProjectFileLayer.Base,
                    entry?.State ?? ProjectFileGraphEntryState.BaseOnly,
                    ZaWorkflowFileOrigin.LooseBase);
            }

            try
            {
                var archiveBytes = cacheManager.ReadBaseTrinityFile(project.Paths, normalizedVirtualPath);
                return new ZaWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    archiveBytes,
                    ProjectFileLayer.Base,
                    entry?.State ?? ProjectFileGraphEntryState.BaseOnly,
                    ZaWorkflowFileOrigin.BaseArchive);
            }
            catch (FileNotFoundException)
            {
            }
        }

        throw new FileNotFoundException($"Pokemon Legends Z-A base file '{relativePath}' could not be resolved.");
    }

    public bool Exists(OpenedProject project, string virtualRomFsPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);

        var normalizedVirtualPath = NormalizeVirtualPath(virtualRomFsPath);
        var relativePath = ToRelativePath(normalizedVirtualPath);

        if (!string.IsNullOrWhiteSpace(project.Paths.OutputRootPath))
        {
            if (File.Exists(CombineGraphPath(project.Paths.OutputRootPath, normalizedVirtualPath))
                || File.Exists(CombineGraphPath(project.Paths.OutputRootPath, relativePath))
                || TryOutputArchiveContains(project.Paths, normalizedVirtualPath))
            {
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(project.Paths.BaseRomFsPath))
        {
            return false;
        }

        if (File.Exists(CombineGraphPath(project.Paths.BaseRomFsPath, normalizedVirtualPath)))
        {
            return true;
        }

        try
        {
            return cacheManager.ContainsBaseTrinityFile(project.Paths, normalizedVirtualPath);
        }
        catch (Exception exception) when (exception is FileNotFoundException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public IReadOnlyList<string> ListBasePackNames(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        try
        {
            return cacheManager.ListBaseTrinityPackNames(project.Paths);
        }
        catch (Exception exception) when (exception is FileNotFoundException or IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            return Array.Empty<string>();
        }
    }

    public static ProjectFileReference CreateReference(ZaWorkflowFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new ProjectFileReference(file.SourceLayer, file.RelativePath);
    }

    public static string ResolveOutputPath(
        ProjectPaths paths,
        string virtualRomFsPath,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException("Set an output root before applying Pokemon Legends Z-A edits.");
        }

        var targetRelativePath = ToOutputRelativePath(NormalizeVirtualPath(virtualRomFsPath), outputMode);
        if (Path.IsPathRooted(targetRelativePath))
        {
            throw new InvalidOperationException($"Pokemon Legends Z-A target path '{targetRelativePath}' must be relative.");
        }

        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(outputRoot, targetRelativePath));
        var pathFromOutputRoot = Path.GetRelativePath(outputRoot, targetPath);
        if (PathContainment.IsOutsideRoot(pathFromOutputRoot))
        {
            throw new InvalidOperationException($"Pokemon Legends Z-A target path '{targetRelativePath}' escapes the output root.");
        }

        EnsureNoLinkTraversal(outputRoot, targetPath);
        return targetPath;
    }

    public static PlannedWriteInfo CreatePlannedWrite(
        ProjectPaths paths,
        string virtualRomFsPath,
        IReadOnlyList<ProjectFileReference> sources,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        var targetRelativePath = ToOutputRelativePath(NormalizeVirtualPath(virtualRomFsPath), outputMode);
        var targetPath = ResolveOutputPath(paths, virtualRomFsPath, outputMode);

        return new PlannedWriteInfo(
            targetRelativePath,
            sources,
            File.Exists(targetPath));
    }

    internal static bool CanDeleteStandaloneOutput(
        ProjectPaths paths,
        ZaWorkflowFile effectiveFile,
        byte[] vanillaBytes)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(effectiveFile);
        ArgumentNullException.ThrowIfNull(vanillaBytes);

        if (effectiveFile.Origin != ZaWorkflowFileOrigin.StandaloneLooseOutput
            || string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return false;
        }

        try
        {
            var standalonePath = ResolveOutputPath(
                paths,
                effectiveFile.VirtualPath,
                ZaOutputMode.Standalone);
            if (!File.Exists(standalonePath)
                || !File.ReadAllBytes(standalonePath).AsSpan().SequenceEqual(effectiveFile.Bytes))
            {
                return false;
            }

            var trinityModManagerPath = ResolveOutputPath(
                paths,
                effectiveFile.VirtualPath,
                ZaOutputMode.TrinityModManager);
            if (File.Exists(trinityModManagerPath))
            {
                return File.ReadAllBytes(trinityModManagerPath)
                    .AsSpan()
                    .SequenceEqual(vanillaBytes);
            }

            if (!HasTrinityArchive(paths.OutputRootPath))
            {
                return true;
            }

            using var archive = ZaTrinityArchive.Open(
                paths.OutputRootPath,
                paths.PokemonLegendsZASupportFolderPath);
            return !archive.TryReadFile(effectiveFile.VirtualPath, out var archiveBytes)
                || archiveBytes.AsSpan().SequenceEqual(vanillaBytes);
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return false;
        }
    }

    public static void Write(
        ProjectPaths paths,
        string virtualRomFsPath,
        byte[] bytes,
        ZaOutputMode outputMode = ZaOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        WriteBatch(
            paths,
            [new ZaWorkflowFileWrite(virtualRomFsPath, bytes)],
            outputMode);
    }

    public static void WriteBatch(
        ProjectPaths paths,
        IReadOnlyList<ZaWorkflowFileWrite> writes,
        ZaOutputMode outputMode = ZaOutputMode.Standalone,
        byte[]? reviewedStandaloneDescriptorBytes = null)
    {
        ApplyBatch(
            paths,
            writes,
            Array.Empty<string>(),
            outputMode,
            reviewedStandaloneDescriptorBytes);
    }

    public static void ApplyBatch(
        ProjectPaths paths,
        IReadOnlyList<ZaWorkflowFileWrite> writes,
        IReadOnlyList<string> deletes,
        ZaOutputMode outputMode = ZaOutputMode.Standalone,
        byte[]? reviewedStandaloneDescriptorBytes = null,
        bool deleteStandaloneDescriptor = false)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(writes);
        ArgumentNullException.ThrowIfNull(deletes);
        using var outputLock = AcquireOutputLock(paths);
        if (writes.Count == 0 && deletes.Count == 0)
        {
            throw new ArgumentException(
                "Pokemon Legends Z-A output batch must contain at least one data-file write or deletion.",
                nameof(writes));
        }

        var normalizedWrites = writes
            .Select(write =>
            {
                ArgumentNullException.ThrowIfNull(write);
                ArgumentException.ThrowIfNullOrWhiteSpace(write.VirtualPath);
                ArgumentNullException.ThrowIfNull(write.Bytes);
                var virtualPath = NormalizeVirtualPath(write.VirtualPath);
                if (string.Equals(virtualPath, DescriptorVirtualPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        "Pokemon Legends Z-A data batches cannot write the Trinity descriptor directly.",
                        nameof(writes));
                }

                return new ZaWorkflowFileWrite(virtualPath, write.Bytes.ToArray());
            })
            .ToArray();
        var normalizedDeletes = deletes
            .Select(delete =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(delete);
                var virtualPath = NormalizeVirtualPath(delete);
                if (string.Equals(virtualPath, DescriptorVirtualPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        "Pokemon Legends Z-A data batches cannot delete the Trinity descriptor directly.",
                        nameof(deletes));
                }

                return virtualPath;
            })
            .ToArray();
        if (normalizedWrites
            .Select(write => write.VirtualPath)
            .Concat(normalizedDeletes)
            .GroupBy(virtualPath => virtualPath, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Pokemon Legends Z-A output batch contains duplicate or conflicting target files.",
                nameof(writes));
        }

        List<ZaWorkflowOutputMutation> mutations;
        try
        {
            mutations = normalizedWrites
                .Select(write => new ZaWorkflowOutputMutation(
                    ResolveOutputPath(paths, write.VirtualPath, outputMode),
                    write.Bytes))
                .ToList();
            mutations.AddRange(normalizedDeletes.Select(delete =>
                new ZaWorkflowOutputMutation(
                    ResolveOutputPath(paths, delete, outputMode),
                    Bytes: null)));
            if (outputMode == ZaOutputMode.Standalone)
            {
                var descriptorBytes = reviewedStandaloneDescriptorBytes?.ToArray()
                    ?? CreatePatchedDescriptorBytes(
                        paths,
                        normalizedWrites.Select(write => write.VirtualPath),
                        normalizedDeletes);
                if (deleteStandaloneDescriptor
                    && !CanDeleteStandaloneDescriptor(
                        paths,
                        descriptorBytes,
                        normalizedWrites.Select(write => write.VirtualPath),
                        normalizedDeletes))
                {
                    throw new InvalidDataException(
                        "Pokemon Legends Z-A standalone descriptor can only be deleted when its reviewed preview matches the verified base descriptor and no standalone overrides remain.");
                }

                mutations.Add(new ZaWorkflowOutputMutation(
                    ResolveOutputPath(paths, DescriptorVirtualPath, ZaOutputMode.Standalone),
                    deleteStandaloneDescriptor ? null : descriptorBytes));
            }
            else if (reviewedStandaloneDescriptorBytes is not null || deleteStandaloneDescriptor)
            {
                throw new ArgumentException(
                    "Standalone descriptor review and deletion can only be used with standalone output.",
                    reviewedStandaloneDescriptorBytes is not null
                        ? nameof(reviewedStandaloneDescriptorBytes)
                        : nameof(deleteStandaloneDescriptor));
            }
        }
        catch (Exception exception)
        {
            throw new IOException(
                "Pokemon Legends Z-A output batch could not be prepared.",
                exception);
        }

        PromotePreparedMutations(paths, mutations);
    }

    internal static IDisposable AcquireOutputLock(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var outputRoot = paths.OutputRootPath ?? string.Empty;
        string lockKey;
        try
        {
            lockKey = string.IsNullOrWhiteSpace(outputRoot)
                ? "<unset>"
                : Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
            if (Directory.Exists(lockKey))
            {
                var rootInfo = new DirectoryInfo(lockKey);
                if (rootInfo.LinkTarget is not null
                    && rootInfo.ResolveLinkTarget(returnFinalTarget: true) is { } resolvedRoot)
                {
                    lockKey = Path.TrimEndingDirectorySeparator(
                        Path.GetFullPath(resolvedRoot.FullName));
                }
            }

            if (OperatingSystem.IsWindows())
            {
                lockKey = lockKey.ToUpperInvariant();
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            lockKey = $"<invalid>:{outputRoot}";
        }

        var gate = OutputRootLocks.GetOrAdd(lockKey, static _ => new object());
        Monitor.Enter(gate);
        Mutex? processMutex = null;
        try
        {
            var lockName = CreateOutputMutexName(lockKey);
            processMutex = new Mutex(initiallyOwned: false, lockName);
            try
            {
                if (!processMutex.WaitOne(TimeSpan.FromSeconds(30)))
                {
                    throw new IOException(
                        "Another KM Editor process is still writing to this Pokemon Legends Z-A output root.");
                }
            }
            catch (AbandonedMutexException)
            {
                // The prior writer exited without releasing the mutex. Ownership transfers here.
            }

            return new ZaOutputRootLock(gate, processMutex);
        }
        catch
        {
            processMutex?.Dispose();
            Monitor.Exit(gate);
            throw;
        }
    }

    private static string CreateOutputMutexName(string lockKey)
    {
        var keyBytes = Encoding.UTF8.GetBytes(lockKey);
        return $"KMEditor.ZA.Output.{Convert.ToHexString(SHA256.HashData(keyBytes))}";
    }

    public static PlannedWriteInfo CreateDescriptorPlannedWrite(ProjectPaths paths)
    {
        var sources = new[]
        {
            new ProjectFileReference(ProjectFileLayer.Base, ToRelativePath(DescriptorVirtualPath)),
        };
        return CreatePlannedWrite(paths, DescriptorVirtualPath, sources, ZaOutputMode.Standalone);
    }

    internal static byte[] CreateStandaloneDescriptorPreview(
        ProjectPaths paths,
        IEnumerable<string> plannedVirtualPaths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(plannedVirtualPaths);
        return CreatePatchedDescriptorBytes(
            paths,
            plannedVirtualPaths,
            Array.Empty<string>());
    }

    internal static byte[] CreateStandaloneDescriptorPreview(
        ProjectPaths paths,
        IEnumerable<string> plannedWriteVirtualPaths,
        IEnumerable<string> plannedDeleteVirtualPaths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(plannedWriteVirtualPaths);
        ArgumentNullException.ThrowIfNull(plannedDeleteVirtualPaths);
        return CreatePatchedDescriptorBytes(
            paths,
            plannedWriteVirtualPaths,
            plannedDeleteVirtualPaths);
    }

    internal static bool StandaloneDescriptorMatchesBase(
        ProjectPaths paths,
        byte[] descriptorBytes)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(descriptorBytes);
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            return false;
        }

        try
        {
            return descriptorBytes
                .AsSpan()
                .SequenceEqual(ZaTrinityDescriptorPatcher.ReadBaseDescriptor(paths.BaseRomFsPath));
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidDataException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return false;
        }
    }

    internal static bool CanDeleteStandaloneDescriptor(
        ProjectPaths paths,
        byte[] descriptorBytes,
        IEnumerable<string> plannedWriteVirtualPaths,
        IEnumerable<string> plannedDeleteVirtualPaths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(descriptorBytes);
        ArgumentNullException.ThrowIfNull(plannedWriteVirtualPaths);
        ArgumentNullException.ThrowIfNull(plannedDeleteVirtualPaths);
        if (!StandaloneDescriptorMatchesBase(paths, descriptorBytes)
            || string.IsNullOrWhiteSpace(paths.OutputRootPath)
            || plannedWriteVirtualPaths.Any())
        {
            return false;
        }

        try
        {
            return !ZaTrinityDescriptorPatcher.HasLayeredVirtualPaths(
                paths.OutputRootPath,
                plannedDeleteVirtualPaths);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return false;
        }
    }

    private static byte[] CreatePatchedDescriptorBytes(
        ProjectPaths paths,
        IEnumerable<string> plannedWriteVirtualPaths,
        IEnumerable<string> plannedDeleteVirtualPaths)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            throw new InvalidOperationException("Pokemon Legends Z-A descriptor patching requires a base RomFS path.");
        }

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException("Pokemon Legends Z-A descriptor patching requires an output root.");
        }

        var descriptorBytes = ZaTrinityDescriptorPatcher.CreateLayeredDescriptor(
            paths.BaseRomFsPath,
            paths.OutputRootPath,
            plannedDeleteVirtualPaths);
        var plannedHashes = plannedWriteVirtualPaths
            .Select(NormalizeVirtualPath)
            .Select(ZaTrinityPathHasher.HashPath)
            .ToHashSet();
        return plannedHashes.Count == 0
            ? descriptorBytes
            : ZaTrinityDescriptorPatcher.RemoveFileHashes(descriptorBytes, plannedHashes);
    }

    private static void PromotePreparedMutations(
        ProjectPaths paths,
        IReadOnlyList<ZaWorkflowOutputMutation> mutations)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException("Pokemon Legends Z-A output batch requires an output root.");
        }

        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var transactionRoot = Path.GetFullPath(Path.Combine(
            outputRoot,
            ".km",
            "transactions",
            $"za-output-{Guid.NewGuid():N}"));
        var relativeTransactionPath = Path.GetRelativePath(outputRoot, transactionRoot);
        if (PathContainment.IsOutsideRoot(relativeTransactionPath))
        {
            throw new InvalidOperationException("Pokemon Legends Z-A transaction path escapes the output root.");
        }

        EnsureNoLinkTraversal(outputRoot, transactionRoot);
        var prepared = new List<ZaPreparedWorkflowMutation>(mutations.Count);
        try
        {
            Directory.CreateDirectory(transactionRoot);
            for (var index = 0; index < mutations.Count; index++)
            {
                var mutation = mutations[index];
                string? stagedPath = null;
                var backupPath = Path.Combine(transactionRoot, $"{index:D4}.bak");
                if (mutation.Bytes is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(mutation.TargetPath)!);
                    stagedPath = Path.Combine(transactionRoot, $"{index:D4}.new");
                    File.WriteAllBytes(stagedPath, mutation.Bytes);
                    if (!File.ReadAllBytes(stagedPath).AsSpan().SequenceEqual(mutation.Bytes))
                    {
                        throw new IOException("Prepared Pokemon Legends Z-A output did not verify.");
                    }
                }
                else if (Directory.Exists(mutation.TargetPath))
                {
                    throw new IOException(
                        "Pokemon Legends Z-A output deletion target is a directory.");
                }

                prepared.Add(new ZaPreparedWorkflowMutation(
                    mutation.TargetPath,
                    stagedPath,
                    backupPath,
                    mutation.Bytes));
            }
        }
        catch (Exception exception)
        {
            TryDeleteTransactionDirectory(outputRoot, transactionRoot);
            throw new IOException(
                "Pokemon Legends Z-A output batch could not be staged.",
                exception);
        }

        var committed = false;
        var rollbackComplete = false;
        try
        {
            foreach (var mutation in prepared)
            {
                if (File.Exists(mutation.TargetPath))
                {
                    File.Move(mutation.TargetPath, mutation.BackupPath);
                    mutation.OriginalMoved = true;
                }

                if (mutation.ExpectedBytes is null)
                {
                    if (File.Exists(mutation.TargetPath))
                    {
                        throw new IOException("Deleted Pokemon Legends Z-A output still exists.");
                    }

                    continue;
                }

                File.Move(mutation.StagedPath!, mutation.TargetPath);
                mutation.StagedPromoted = true;
                if (!File.ReadAllBytes(mutation.TargetPath).AsSpan().SequenceEqual(mutation.ExpectedBytes))
                {
                    throw new IOException("Promoted Pokemon Legends Z-A output did not verify.");
                }
            }

            committed = true;
        }
        catch (Exception exception)
        {
            var rollbackErrors = RollBackPreparedMutations(prepared);
            rollbackComplete = rollbackErrors.Count == 0;
            if (rollbackComplete)
            {
                throw new IOException(
                    "Pokemon Legends Z-A output promotion failed; prior output files were restored.",
                    exception);
            }

            throw new IOException(
                "Pokemon Legends Z-A output promotion failed and rollback could not be completed; recovery files were retained.",
                new AggregateException([exception, .. rollbackErrors]));
        }
        finally
        {
            if (committed || rollbackComplete)
            {
                TryDeleteTransactionDirectory(outputRoot, transactionRoot);
            }
        }
    }

    private static IReadOnlyList<Exception> RollBackPreparedMutations(
        IReadOnlyList<ZaPreparedWorkflowMutation> prepared)
    {
        var errors = new List<Exception>();
        for (var index = prepared.Count - 1; index >= 0; index--)
        {
            var mutation = prepared[index];
            try
            {
                if (mutation.StagedPromoted && File.Exists(mutation.TargetPath))
                {
                    if (mutation.ExpectedBytes is null
                        || !File.ReadAllBytes(mutation.TargetPath).AsSpan().SequenceEqual(mutation.ExpectedBytes))
                    {
                        throw new IOException(
                            "A promoted Pokemon Legends Z-A output was replaced by another writer; "
                            + "rollback left that replacement and the recovery backup untouched.");
                    }

                    File.Delete(mutation.TargetPath);
                }

                if (mutation.OriginalMoved && File.Exists(mutation.BackupPath))
                {
                    if (File.Exists(mutation.TargetPath))
                    {
                        throw new IOException(
                            "A Pokemon Legends Z-A output target changed during rollback; "
                            + "the recovery backup was retained without overwriting it.");
                    }

                    File.Move(mutation.BackupPath, mutation.TargetPath);
                }
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
        }

        return errors;
    }

    private static bool TryDeleteTransactionDirectory(
        string outputRoot,
        string transactionRoot)
    {
        try
        {
            var resolvedOutputRoot = Path.GetFullPath(outputRoot);
            var resolvedTransactionRoot = Path.GetFullPath(transactionRoot);
            if (PathContainment.IsOutsideRoot(
                    Path.GetRelativePath(resolvedOutputRoot, resolvedTransactionRoot)))
            {
                return false;
            }

            EnsureNoLinkTraversal(resolvedOutputRoot, resolvedTransactionRoot);
            if (Directory.Exists(resolvedTransactionRoot))
            {
                Directory.Delete(resolvedTransactionRoot, recursive: true);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static ValidationDiagnostic CreateDiagnostic(
        DiagnosticSeverity severity,
        string message,
        string? field = null,
        string? expected = null,
        string? actual = null)
    {
        return new ValidationDiagnostic(
            severity,
            message,
            File: actual,
            Domain: "za.editor",
            Field: field,
            Expected: expected);
    }

    public static bool IsPokemonLegendsZA(ProjectGame? game)
    {
        return game is ProjectGame.ZA;
    }

    private static ProjectFileGraphEntry? FindEntry(OpenedProject project, string relativePath)
    {
        return project.FileGraph.Entries.FirstOrDefault(
            entry => string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
    }

    private static string ToRelativePath(string virtualRomFsPath)
    {
        return $"romfs/{virtualRomFsPath}";
    }

    private static string ToOutputRelativePath(string normalizedVirtualPath, ZaOutputMode outputMode)
    {
        return outputMode switch
        {
            ZaOutputMode.Standalone => ToRelativePath(normalizedVirtualPath),
            ZaOutputMode.TrinityModManager => normalizedVirtualPath,
            ZaOutputMode.TrinityBypass => ToRelativePath(normalizedVirtualPath),
            _ => throw new ArgumentOutOfRangeException(nameof(outputMode), outputMode, null),
        };
    }

    private static string NormalizeVirtualPath(string virtualRomFsPath)
    {
        var normalized = virtualRomFsPath.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["romfs/".Length..];
        }

        var segments = normalized.Split('/');
        if (segments.Length == 0
            || segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."))
        {
            throw new ArgumentException(
                $"Pokemon Legends Z-A virtual path '{virtualRomFsPath}' is not canonical.",
                nameof(virtualRomFsPath));
        }

        return normalized;
    }

    private static void EnsureNoLinkTraversal(string rootPath, string targetPath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var target = Path.GetFullPath(targetPath);
        var relativePath = Path.GetRelativePath(root, target);
        if (PathContainment.IsOutsideRoot(relativePath))
        {
            throw new InvalidOperationException("Pokemon Legends Z-A output path escapes its configured root.");
        }

        var current = root;
        foreach (var segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                continue;
            }

            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) == 0)
            {
                continue;
            }

            FileSystemInfo info = (attributes & FileAttributes.Directory) != 0
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            if (info.LinkTarget is not null)
            {
                throw new InvalidOperationException(
                    $"Pokemon Legends Z-A output path '{relativePath}' traverses a linked file or directory.");
            }
        }
    }

    private static string CombineGraphPath(string rootPath, string relativePath)
    {
        return Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static (string Path, bool IsStandalone)? SelectLatestLooseOutput(
        string trinityModManagerPath,
        string standalonePath)
    {
        var trinityModManagerExists = File.Exists(trinityModManagerPath);
        var standaloneExists = File.Exists(standalonePath);
        if (!trinityModManagerExists)
        {
            return standaloneExists ? (standalonePath, true) : null;
        }

        if (!standaloneExists)
        {
            return (trinityModManagerPath, false);
        }

        return File.GetLastWriteTimeUtc(standalonePath) > File.GetLastWriteTimeUtc(trinityModManagerPath)
            ? (standalonePath, true)
            : (trinityModManagerPath, false);
    }

    private static bool TryReadOutputArchive(ProjectPaths paths, string virtualPath, out byte[] bytes)
    {
        bytes = [];
        try
        {
            var outputRootPath = paths.OutputRootPath;
            if (string.IsNullOrWhiteSpace(outputRootPath))
            {
                return false;
            }

            if (!HasTrinityArchive(outputRootPath))
            {
                return false;
            }

            using var archive = ZaTrinityArchive.Open(
                outputRootPath,
                paths.PokemonLegendsZASupportFolderPath);
            return archive.TryReadFile(virtualPath, out bytes);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            bytes = [];
            return false;
        }
    }

    private static bool TryOutputArchiveContains(ProjectPaths paths, string virtualPath)
    {
        try
        {
            var outputRootPath = paths.OutputRootPath;
            if (string.IsNullOrWhiteSpace(outputRootPath) || !HasTrinityArchive(outputRootPath))
            {
                return false;
            }

            using var archive = ZaTrinityArchive.Open(
                outputRootPath,
                paths.PokemonLegendsZASupportFolderPath);
            return archive.ContainsFile(virtualPath);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasTrinityArchive(string rootPath)
    {
        return HasTrinityArchiveAt(rootPath)
            || HasTrinityArchiveAt(Path.Combine(rootPath, "romfs"));
    }

    private static bool HasTrinityArchiveAt(string romFsRoot)
    {
        return File.Exists(Path.Combine(romFsRoot, "arc", "data.trpfd"))
            && File.Exists(Path.Combine(romFsRoot, "arc", "data.trpfs"));
    }
}

internal sealed record ZaWorkflowFile(
    string VirtualPath,
    string RelativePath,
    byte[] Bytes,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState,
    ZaWorkflowFileOrigin Origin);

internal enum ZaWorkflowFileOrigin
{
    StandaloneLooseOutput,
    TrinityModManagerLooseOutput,
    OutputArchive,
    LooseBase,
    BaseArchive,
}

internal sealed record PlannedWriteInfo(
    string TargetRelativePath,
    IReadOnlyList<ProjectFileReference> Sources,
    bool ReplacesExistingOutput);

internal sealed record ZaWorkflowFileWrite(
    string VirtualPath,
    byte[] Bytes);

internal sealed record ZaWorkflowOutputMutation(
    string TargetPath,
    byte[]? Bytes);

internal sealed class ZaPreparedWorkflowMutation
{
    public ZaPreparedWorkflowMutation(
        string targetPath,
        string? stagedPath,
        string backupPath,
        byte[]? expectedBytes)
    {
        TargetPath = targetPath;
        StagedPath = stagedPath;
        BackupPath = backupPath;
        ExpectedBytes = expectedBytes;
    }

    public string TargetPath { get; }

    public string? StagedPath { get; }

    public string BackupPath { get; }

    public byte[]? ExpectedBytes { get; }

    public bool OriginalMoved { get; set; }

    public bool StagedPromoted { get; set; }
}

internal sealed class ZaOutputRootLock : IDisposable
{
    private object? gate;
    private Mutex? processMutex;

    public ZaOutputRootLock(object gate, Mutex processMutex)
    {
        this.gate = gate;
        this.processMutex = processMutex;
    }

    public void Dispose()
    {
        var capturedMutex = Interlocked.Exchange(ref processMutex, null);
        try
        {
            if (capturedMutex is not null)
            {
                capturedMutex.ReleaseMutex();
            }
        }
        finally
        {
            capturedMutex?.Dispose();
            var capturedGate = Interlocked.Exchange(ref gate, null);
            if (capturedGate is not null)
            {
                Monitor.Exit(capturedGate);
            }
        }
    }
}
