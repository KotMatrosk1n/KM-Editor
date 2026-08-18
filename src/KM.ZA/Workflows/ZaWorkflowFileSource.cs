// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.Formats.ZA;

namespace KM.ZA.Workflows;

internal sealed class ZaWorkflowFileSource
{
    public const string DescriptorVirtualPath = ZaTrinityDescriptorPatcher.DescriptorVirtualPath;
    public const string TrinityModManagerRomFsDirectory = "trinity-mod-manager-romfs";

    private static readonly ConcurrentDictionary<string, object> OutputRootLocks = new(
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
    // All top-level RomFS roots emitted by current Z-A workflows, plus the Trinity descriptor root.
    private static readonly string[] KnownBareTrinityModManagerRootDirectories =
    [
        "arc",
        "avalon",
        "ik_event",
        "ik_message",
        "message",
        "param_ai",
        "world",
    ];

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
            var isolatedTrinityModManagerPath = CombineGraphPath(
                project.Paths.OutputRootPath,
                $"{TrinityModManagerRomFsDirectory}/{normalizedVirtualPath}");
            (string Path, bool IsStandalone)? looseOutput;
            try
            {
                looseOutput = SelectLatestLooseOutput(
                    trinityModManagerPath,
                    isolatedTrinityModManagerPath,
                    standalonePath);
            }
            catch (Exception exception) when (IsContextualFileFailure(exception))
            {
                throw CreateReadFailure(
                    relativePath,
                    ProjectFileLayer.Layered,
                    entry?.State,
                    exception,
                    ProjectFileOperation.Inspect);
            }

            if (looseOutput is not null)
            {
                var state = looseOutput.Value.IsStandalone
                    ? entry?.State ?? ProjectFileGraphEntryState.LayeredOverride
                    : ProjectFileGraphEntryState.LayeredOverride;
                return new ZaWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    ReadAllBytesWithContext(
                        looseOutput.Value.Path,
                        relativePath,
                        ProjectFileLayer.Layered,
                        state),
                    ProjectFileLayer.Layered,
                    state,
                    looseOutput.Value.IsStandalone
                        ? ZaWorkflowFileOrigin.StandaloneLooseOutput
                        : ZaWorkflowFileOrigin.TrinityModManagerLooseOutput);
            }

            if (TryReadOutputArchive(
                project.Paths,
                normalizedVirtualPath,
                relativePath,
                out var layeredArchiveBytes))
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
            if (FileExistsWithContext(
                looseBasePath,
                relativePath,
                ProjectFileLayer.Base,
                entry?.State))
            {
                return new ZaWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    ReadAllBytesWithContext(
                        looseBasePath,
                        relativePath,
                        ProjectFileLayer.Base,
                        entry?.State),
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
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
            }
            catch (Exception exception) when (IsContextualFileFailure(exception))
            {
                throw CreateReadFailure(
                    relativePath,
                    ProjectFileLayer.Base,
                    entry?.State,
                    exception,
                    ProjectFileOperation.Inspect);
            }
        }

        throw CreateReadFailure(
            relativePath,
            layer: null,
            state: entry?.State,
            exception: new FileNotFoundException());
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
            if (FileExistsWithContext(
                looseBasePath,
                relativePath,
                ProjectFileLayer.Base,
                entry?.State))
            {
                return new ZaWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    ReadAllBytesWithContext(
                        looseBasePath,
                        relativePath,
                        ProjectFileLayer.Base,
                        entry?.State),
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
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
            }
            catch (Exception exception) when (IsContextualFileFailure(exception))
            {
                throw CreateReadFailure(
                    relativePath,
                    ProjectFileLayer.Base,
                    entry?.State,
                    exception,
                    ProjectFileOperation.Inspect);
            }
        }

        throw CreateReadFailure(
            relativePath,
            layer: null,
            state: entry?.State,
            exception: new FileNotFoundException());
    }

    public bool Exists(OpenedProject project, string virtualRomFsPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);

        var normalizedVirtualPath = NormalizeVirtualPath(virtualRomFsPath);
        var relativePath = ToRelativePath(normalizedVirtualPath);

        if (!string.IsNullOrWhiteSpace(project.Paths.OutputRootPath))
        {
            if (FileExistsWithContext(
                    CombineGraphPath(project.Paths.OutputRootPath, normalizedVirtualPath),
                    relativePath,
                    ProjectFileLayer.Layered,
                    state: null)
                || FileExistsWithContext(
                    CombineGraphPath(
                    project.Paths.OutputRootPath,
                    $"{TrinityModManagerRomFsDirectory}/{normalizedVirtualPath}"),
                    relativePath,
                    ProjectFileLayer.Layered,
                    state: null)
                || FileExistsWithContext(
                    CombineGraphPath(project.Paths.OutputRootPath, relativePath),
                    relativePath,
                    ProjectFileLayer.Layered,
                    state: null)
                || TryOutputArchiveContains(project.Paths, normalizedVirtualPath, relativePath))
            {
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(project.Paths.BaseRomFsPath))
        {
            return false;
        }

        if (FileExistsWithContext(
            CombineGraphPath(project.Paths.BaseRomFsPath, normalizedVirtualPath),
            relativePath,
            ProjectFileLayer.Base,
            state: null))
        {
            return true;
        }

        try
        {
            return cacheManager.ContainsBaseTrinityFile(project.Paths, normalizedVirtualPath);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception exception) when (IsContextualFileFailure(exception))
        {
            throw CreateReadFailure(
                relativePath,
                ProjectFileLayer.Base,
                state: null,
                exception: exception,
                operation: ProjectFileOperation.Inspect);
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

    internal bool TryFindLegacyBareTrinityModManagerOutput(
        OpenedProject project,
        IEnumerable<string> plannedVirtualPaths,
        out string? relativePath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(plannedVirtualPaths);

        relativePath = null;
        if (string.IsNullOrWhiteSpace(project.Paths.OutputRootPath))
        {
            return false;
        }

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var virtualRootNames = plannedVirtualPaths
            .Concat(KnownBareTrinityModManagerRootDirectories)
            .Select(NormalizeVirtualPath)
            .Select(GetFirstPathSegment)
            .ToHashSet(comparer);
        if (virtualRootNames.Count == 0)
        {
            return false;
        }

        var outputRoot = Path.GetFullPath(project.Paths.OutputRootPath);
        if (!Directory.Exists(outputRoot))
        {
            return false;
        }

        foreach (var entryPath in Directory.EnumerateFileSystemEntries(
                     outputRoot,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            var entryName = Path.GetFileName(
                Path.TrimEndingDirectorySeparator(entryPath));
            if (virtualRootNames.Contains(entryName))
            {
                relativePath = entryName;
                return true;
            }
        }

        return false;
    }

    public static ProjectFileReference CreateReference(ZaWorkflowFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new ProjectFileReference(file.SourceLayer, file.RelativePath);
    }

    public static string ResolveOutputPath(
        ProjectPaths paths,
        string virtualRomFsPath,
        ZaOutputMode outputMode = ZaOutputMode.Standalone,
        bool isolateTrinityModManagerRomFs = false)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException("Set an output root before applying Pokemon Legends Z-A edits.");
        }

        var targetRelativePath = ToOutputRelativePath(
            NormalizeVirtualPath(virtualRomFsPath),
            outputMode,
            isolateTrinityModManagerRomFs);
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
        ZaOutputMode outputMode = ZaOutputMode.Standalone,
        bool isolateTrinityModManagerRomFs = false)
    {
        var targetRelativePath = ToOutputRelativePath(
            NormalizeVirtualPath(virtualRomFsPath),
            outputMode,
            isolateTrinityModManagerRomFs);
        var targetPath = ResolveOutputPath(
            paths,
            virtualRomFsPath,
            outputMode,
            isolateTrinityModManagerRomFs);

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
            if (File.Exists(trinityModManagerPath)
                && !File.ReadAllBytes(trinityModManagerPath)
                    .AsSpan()
                    .SequenceEqual(vanillaBytes))
            {
                return false;
            }

            var isolatedTrinityModManagerPath = ResolveOutputPath(
                paths,
                effectiveFile.VirtualPath,
                ZaOutputMode.TrinityModManager,
                isolateTrinityModManagerRomFs: true);
            if (File.Exists(isolatedTrinityModManagerPath)
                && !File.ReadAllBytes(isolatedTrinityModManagerPath)
                    .AsSpan()
                    .SequenceEqual(vanillaBytes))
            {
                return false;
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

    public static OutputApplyResult? Write(
        ProjectPaths paths,
        string virtualRomFsPath,
        byte[] bytes,
        ZaOutputMode outputMode = ZaOutputMode.Standalone,
        Func<bool>? revalidateReviewedState = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return WriteBatch(
            paths,
            [new ZaWorkflowFileWrite(virtualRomFsPath, bytes)],
            outputMode,
            revalidateReviewedState: revalidateReviewedState);
    }

    public static OutputApplyResult? WriteBatch(
        ProjectPaths paths,
        IReadOnlyList<ZaWorkflowFileWrite> writes,
        ZaOutputMode outputMode = ZaOutputMode.Standalone,
        byte[]? reviewedStandaloneDescriptorBytes = null,
        ZaOutputApplyContext? applyContext = null,
        Func<bool>? revalidateReviewedState = null)
    {
        return ApplyBatch(
            paths,
            writes,
            Array.Empty<string>(),
            outputMode,
            reviewedStandaloneDescriptorBytes,
            applyContext: applyContext,
            revalidateReviewedState: revalidateReviewedState);
    }

    public static OutputApplyResult? ApplyBatch(
        ProjectPaths paths,
        IReadOnlyList<ZaWorkflowFileWrite> writes,
        IReadOnlyList<string> deletes,
        ZaOutputMode outputMode = ZaOutputMode.Standalone,
        byte[]? reviewedStandaloneDescriptorBytes = null,
        bool deleteStandaloneDescriptor = false,
        ZaOutputApplyContext? applyContext = null,
        Func<bool>? revalidateReviewedState = null)
    {
        return ApplyBatchCore(
            paths,
            writes,
            deletes,
            Array.Empty<ZaStandaloneOutputMutation>(),
            outputMode,
            reviewedStandaloneDescriptorBytes,
            deleteStandaloneDescriptor,
            allowHybridExeFsOutput: false,
            isolateTrinityModManagerRomFs: false,
            applyContext: applyContext,
            revalidateReviewedState: revalidateReviewedState);
    }

    internal static OutputApplyResult? ApplyStandaloneMixedBatch(
        ProjectPaths paths,
        IReadOnlyList<ZaWorkflowFileWrite> romFsWrites,
        IReadOnlyList<string> romFsDeletes,
        IReadOnlyList<ZaStandaloneOutputMutation> outputMutations,
        byte[]? reviewedStandaloneDescriptorBytes = null,
        bool deleteStandaloneDescriptor = false,
        ZaOutputApplyContext? applyContext = null,
        Func<bool>? revalidateReviewedState = null)
    {
        return ApplyBatchCore(
            paths,
            romFsWrites,
            romFsDeletes,
            outputMutations,
            ZaOutputMode.Standalone,
            reviewedStandaloneDescriptorBytes,
            deleteStandaloneDescriptor,
            allowHybridExeFsOutput: false,
            isolateTrinityModManagerRomFs: false,
            applyContext: applyContext,
            revalidateReviewedState: revalidateReviewedState);
    }

    internal static OutputApplyResult? ApplyStandaloneMixedBatch(
        ProjectPaths paths,
        Func<ZaStandaloneMixedBatch> prepareBatch,
        ZaOutputApplyContext? applyContext = null,
        Func<bool>? revalidateReviewedState = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(prepareBatch);

        using var outputLock = AcquireOutputLock(paths);
        var batch = prepareBatch()
            ?? throw new InvalidOperationException(
                "Pokemon Legends Z-A standalone output preparation returned no batch.");
        return ApplyBatchCoreLocked(
            paths,
            batch.RomFsWrites,
            batch.RomFsDeletes,
            batch.OutputMutations,
            ZaOutputMode.Standalone,
            batch.ReviewedStandaloneDescriptorBytes,
            batch.DeleteStandaloneDescriptor,
            allowHybridExeFsOutput: false,
            isolateTrinityModManagerRomFs: false,
            applyContext: applyContext,
            revalidateReviewedState: revalidateReviewedState);
    }

    internal static OutputApplyResult? ApplyHybridMixedBatch(
        ProjectPaths paths,
        ZaOutputMode outputMode,
        bool isolateTrinityModManagerRomFs,
        Func<ZaStandaloneMixedBatch> prepareBatch,
        ZaOutputApplyContext? applyContext = null,
        Func<bool>? revalidateReviewedState = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(prepareBatch);
        if (isolateTrinityModManagerRomFs
            && outputMode != ZaOutputMode.TrinityModManager)
        {
            throw new ArgumentException(
                "Isolated Trinity Mod Manager RomFS routing requires Trinity Mod Manager output.",
                nameof(isolateTrinityModManagerRomFs));
        }

        using var outputLock = AcquireOutputLock(paths);
        var batch = prepareBatch()
            ?? throw new InvalidOperationException(
                "Pokemon Legends Z-A hybrid output preparation returned no batch.");
        return ApplyBatchCoreLocked(
            paths,
            batch.RomFsWrites,
            batch.RomFsDeletes,
            batch.OutputMutations,
            outputMode,
            batch.ReviewedStandaloneDescriptorBytes,
            batch.DeleteStandaloneDescriptor,
            allowHybridExeFsOutput: true,
            isolateTrinityModManagerRomFs,
            applyContext: applyContext,
            revalidateReviewedState: revalidateReviewedState);
    }

    private static OutputApplyResult? ApplyBatchCore(
        ProjectPaths paths,
        IReadOnlyList<ZaWorkflowFileWrite> writes,
        IReadOnlyList<string> deletes,
        IReadOnlyList<ZaStandaloneOutputMutation> outputMutations,
        ZaOutputMode outputMode,
        byte[]? reviewedStandaloneDescriptorBytes,
        bool deleteStandaloneDescriptor,
        bool allowHybridExeFsOutput,
        bool isolateTrinityModManagerRomFs,
        ZaOutputApplyContext? applyContext,
        Func<bool>? revalidateReviewedState)
    {
        ArgumentNullException.ThrowIfNull(paths);
        using var outputLock = AcquireOutputLock(paths);
        return ApplyBatchCoreLocked(
            paths,
            writes,
            deletes,
            outputMutations,
            outputMode,
            reviewedStandaloneDescriptorBytes,
            deleteStandaloneDescriptor,
            allowHybridExeFsOutput,
            isolateTrinityModManagerRomFs,
            applyContext,
            revalidateReviewedState);
    }

    private static OutputApplyResult? ApplyBatchCoreLocked(
        ProjectPaths paths,
        IReadOnlyList<ZaWorkflowFileWrite> writes,
        IReadOnlyList<string> deletes,
        IReadOnlyList<ZaStandaloneOutputMutation> outputMutations,
        ZaOutputMode outputMode,
        byte[]? reviewedStandaloneDescriptorBytes,
        bool deleteStandaloneDescriptor,
        bool allowHybridExeFsOutput,
        bool isolateTrinityModManagerRomFs,
        ZaOutputApplyContext? applyContext,
        Func<bool>? revalidateReviewedState)
    {
        ArgumentNullException.ThrowIfNull(writes);
        ArgumentNullException.ThrowIfNull(deletes);
        ArgumentNullException.ThrowIfNull(outputMutations);
        if (writes.Count == 0 && deletes.Count == 0 && outputMutations.Count == 0)
        {
            throw new ArgumentException(
                "Pokemon Legends Z-A output batch must contain at least one file write or deletion.",
                nameof(writes));
        }

        if (outputMode != ZaOutputMode.Standalone
            && outputMutations.Count > 0
            && !allowHybridExeFsOutput)
        {
            throw new ArgumentException(
                "Explicit ExeFS output mutations require standalone output.",
                nameof(outputMutations));
        }

        if (isolateTrinityModManagerRomFs
            && outputMode != ZaOutputMode.TrinityModManager)
        {
            throw new ArgumentException(
                "Isolated Trinity Mod Manager RomFS routing requires Trinity Mod Manager output.",
                nameof(isolateTrinityModManagerRomFs));
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
        var normalizedOutputMutations = outputMutations
            .Select(mutation =>
            {
                ArgumentNullException.ThrowIfNull(mutation);
                ArgumentException.ThrowIfNullOrWhiteSpace(mutation.RelativePath);
                var relativePath = NormalizeStandaloneOutputRelativePath(mutation.RelativePath);
                if (!relativePath.StartsWith("exefs/", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        "Explicit standalone output mutations are limited to ExeFS paths.",
                        nameof(outputMutations));
                }

                return new ZaStandaloneOutputMutation(relativePath, mutation.Bytes?.ToArray());
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

        if (normalizedOutputMutations
            .GroupBy(
                mutation => mutation.RelativePath,
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Pokemon Legends Z-A output batch contains duplicate or conflicting ExeFS targets.",
                nameof(outputMutations));
        }

        List<ZaWorkflowOutputMutation> mutations;
        OutputDirectoryMembershipSnapshot? standaloneRomFsMembership = null;
        try
        {
            mutations = normalizedWrites
                .Select(write => new ZaWorkflowOutputMutation(
                    ResolveOutputPath(
                        paths,
                        write.VirtualPath,
                        outputMode,
                        isolateTrinityModManagerRomFs),
                    write.Bytes))
                .ToList();
            mutations.AddRange(normalizedDeletes.Select(delete =>
                new ZaWorkflowOutputMutation(
                    ResolveOutputPath(
                        paths,
                        delete,
                        outputMode,
                        isolateTrinityModManagerRomFs),
                    Bytes: null)));
            mutations.AddRange(normalizedOutputMutations.Select(mutation =>
                new ZaWorkflowOutputMutation(
                    ResolveStandaloneOutputPath(paths, mutation.RelativePath),
                    mutation.Bytes)));
            var hasRomFsMutations = normalizedWrites.Length > 0 || normalizedDeletes.Length > 0;
            if (outputMode == ZaOutputMode.Standalone && hasRomFsMutations)
            {
                standaloneRomFsMembership = CaptureStandaloneRomFsMembership(paths);
                var layeredVirtualPaths = GetLayeredVirtualPaths(standaloneRomFsMembership);
                var currentDescriptorBytes = CreatePatchedDescriptorBytes(
                    paths,
                    normalizedWrites.Select(write => write.VirtualPath),
                    normalizedDeletes,
                    layeredVirtualPaths);
                if (reviewedStandaloneDescriptorBytes is not null
                    && !reviewedStandaloneDescriptorBytes.AsSpan().SequenceEqual(currentDescriptorBytes))
                {
                    throw new OutputReviewStateConflictException();
                }

                var descriptorBytes = reviewedStandaloneDescriptorBytes?.ToArray()
                    ?? currentDescriptorBytes;
                if (deleteStandaloneDescriptor
                    && !CanDeleteStandaloneDescriptorFromVirtualPaths(
                        paths,
                        descriptorBytes,
                        normalizedWrites.Select(write => write.VirtualPath),
                        normalizedDeletes,
                        layeredVirtualPaths))
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
                    "Standalone descriptor review and deletion require standalone RomFS mutations.",
                    reviewedStandaloneDescriptorBytes is not null
                        ? nameof(reviewedStandaloneDescriptorBytes)
                        : nameof(deleteStandaloneDescriptor));
            }

            var targetComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            if (mutations
                .GroupBy(
                    mutation => Path.GetFullPath(mutation.TargetPath),
                    targetComparer)
                .Any(group => group.Count() > 1))
            {
                throw new InvalidDataException(
                    "Pokemon Legends Z-A output batch contains duplicate or conflicting resolved targets.");
            }
        }
        catch (OutputCoordinatorException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            InvalidOperationException or
            ArgumentException or
            NotSupportedException or
            OverflowException)
        {
            throw new IOException(
                "Pokemon Legends Z-A output batch could not be prepared.",
                exception);
        }

        return PromotePreparedMutations(
            paths,
            mutations,
            outputMode,
            applyContext,
            standaloneRomFsMembership is null
                ? null
                : [standaloneRomFsMembership.ToDependency()],
            revalidateReviewedState);
    }

    internal static string ResolveStandaloneOutputPath(
        ProjectPaths paths,
        string outputRelativePath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRelativePath);
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException(
                "Set an output root before applying Pokemon Legends Z-A standalone edits.");
        }

        var normalizedRelativePath = NormalizeStandaloneOutputRelativePath(outputRelativePath);
        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(
            outputRoot,
            normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (PathContainment.IsOutsideRoot(Path.GetRelativePath(outputRoot, targetPath)))
        {
            throw new InvalidOperationException(
                $"Pokemon Legends Z-A target path '{normalizedRelativePath}' escapes the output root.");
        }

        EnsureNoLinkTraversal(outputRoot, targetPath);
        return targetPath;
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

    private static bool CanDeleteStandaloneDescriptorFromVirtualPaths(
        ProjectPaths paths,
        byte[] descriptorBytes,
        IEnumerable<string> plannedWriteVirtualPaths,
        IEnumerable<string> plannedDeleteVirtualPaths,
        IEnumerable<string> layeredVirtualPaths)
    {
        if (!StandaloneDescriptorMatchesBase(paths, descriptorBytes)
            || plannedWriteVirtualPaths.Any())
        {
            return false;
        }

        return !ZaTrinityDescriptorPatcher.HasLayeredVirtualPaths(
            layeredVirtualPaths,
            plannedDeleteVirtualPaths);
    }

    private static OutputDirectoryMembershipSnapshot CaptureStandaloneRomFsMembership(
        ProjectPaths paths)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException(
                "Pokemon Legends Z-A descriptor review requires an output root.");
        }

        var coordinator = new OutputTransactionCoordinator(paths.OutputRootPath);
        return coordinator
            .CaptureDirectoryMembershipAsync(new RelativeOutputPath("romfs"))
            .GetAwaiter()
            .GetResult();
    }

    private static string[] GetLayeredVirtualPaths(OutputDirectoryMembershipSnapshot membership)
    {
        const string prefix = "romfs/";
        return membership.Entries
            .Where(entry => !entry.IsDirectory)
            .Select(entry => entry.Path.Value)
            .Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(path => path[prefix.Length..])
            .ToArray();
    }

    private static byte[] CreatePatchedDescriptorBytes(
        ProjectPaths paths,
        IEnumerable<string> plannedWriteVirtualPaths,
        IEnumerable<string> plannedDeleteVirtualPaths,
        IEnumerable<string>? layeredVirtualPaths = null)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            throw new InvalidOperationException("Pokemon Legends Z-A descriptor patching requires a base RomFS path.");
        }

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException("Pokemon Legends Z-A descriptor patching requires an output root.");
        }

        var descriptorBytes = layeredVirtualPaths is null
            ? ZaTrinityDescriptorPatcher.CreateLayeredDescriptor(
                paths.BaseRomFsPath,
                paths.OutputRootPath,
                plannedDeleteVirtualPaths)
            : ZaTrinityDescriptorPatcher.CreateLayeredDescriptorFromVirtualPaths(
                paths.BaseRomFsPath,
                layeredVirtualPaths,
                plannedDeleteVirtualPaths);
        var plannedHashes = plannedWriteVirtualPaths
            .Select(NormalizeVirtualPath)
            .Select(ZaTrinityPathHasher.HashPath)
            .ToHashSet();
        return plannedHashes.Count == 0
            ? descriptorBytes
            : ZaTrinityDescriptorPatcher.RemoveFileHashes(descriptorBytes, plannedHashes);
    }

    private static OutputApplyResult? PromotePreparedMutations(
        ProjectPaths paths,
        IReadOnlyList<ZaWorkflowOutputMutation> mutations,
        ZaOutputMode outputMode,
        ZaOutputApplyContext? applyContext,
        IEnumerable<OutputDirectoryMembershipDependency>? directoryMembershipDependencies = null,
        Func<bool>? revalidateReviewedState = null)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException("Pokemon Legends Z-A output batch requires an output root.");
        }

        if (paths.SelectedGame is not ProjectGame.ZA)
        {
            throw new InvalidOperationException("Pokemon Legends Z-A output requires a matching project game.");
        }

        var outputRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.OutputRootPath));
        var ownerId = applyContext?.OwnerId ?? new OwnershipOwnerId("workflow.za.output");
        var preservationRule = new PreservationRuleDescriptor(
            "za.full-file-rebuild",
            schemaVersion: 1,
            preservesUnownedData: true,
            requiresPreimage: true);
        var coordinatorOptions = new OutputTransactionCoordinatorOptions();
        var membershipDependencies = directoryMembershipDependencies?.ToArray()
            ?? [];
        var reviewedPreimages = revalidateReviewedState is null
            ? null
            : CapturePreparedPreimages(mutations, coordinatorOptions);
        var reviewStateIsCurrent = true;
        if (revalidateReviewedState is not null)
        {
            try
            {
                reviewStateIsCurrent = revalidateReviewedState();
            }
            catch (OutputCoordinatorException)
            {
                throw;
            }
            catch (Exception exception) when (exception is
                IOException or
                UnauthorizedAccessException or
                SecurityException or
                InvalidOperationException or
                ArgumentException or
                NotSupportedException or
                OverflowException)
            {
                throw new OutputReviewStateConflictException(exception);
            }

            if (!reviewStateIsCurrent)
            {
                throw new OutputReviewStateConflictException();
            }
        }

        var outputMutations = new List<OutputMutation>(mutations.Count);
        long plannedWriteBytes = 0;
        long plannedBackupBytes = 0;
        foreach (var mutation in mutations)
        {
            var relativePathValue = Path.GetRelativePath(outputRoot, Path.GetFullPath(mutation.TargetPath))
                .Replace(Path.DirectorySeparatorChar, '/');
            if (PathContainment.IsOutsideRoot(relativePathValue))
            {
                throw new OutputPathSecurityException();
            }

            var relativePath = new RelativeOutputPath(relativePathValue);
            if (mutation.Bytes?.LongLength > coordinatorOptions.MaximumWriteBytesPerMutation)
            {
                throw new OutputLimitExceededException(
                    "A Pokemon Legends Z-A output target exceeds the configured write limit.");
            }

            var remainingBackupBytes = coordinatorOptions.MaximumBackupBytesPerApply - plannedBackupBytes;
            var expectedPreimage = CaptureOutputFileState(
                mutation.TargetPath,
                Math.Min(coordinatorOptions.MaximumFingerprintFileBytes, remainingBackupBytes));
            if (reviewedPreimages is not null
                && (!reviewedPreimages.TryGetValue(
                        Path.GetFullPath(mutation.TargetPath),
                        out var reviewedPreimage)
                    || reviewedPreimage != expectedPreimage))
            {
                throw new OutputPreimageConflictException(relativePath);
            }
            var ownership = new OwnedTarget(
                GameFamily.LegendsZA,
                new OwnedTargetAddress(relativePath),
                ownerId,
                preservationRule);
            if (mutation.Bytes is null)
            {
                if (expectedPreimage.Exists)
                {
                    plannedBackupBytes = checked(plannedBackupBytes + expectedPreimage.LengthBytes);
                    outputMutations.Add(OutputMutation.Delete(
                        relativePath,
                        expectedPreimage,
                        [ownership]));
                    EnsureMutationCountWithinLimit(outputMutations.Count, coordinatorOptions);
                }

                continue;
            }

            var plannedHash = Convert.ToHexStringLower(SHA256.HashData(mutation.Bytes));
            if (expectedPreimage.Exists
                && expectedPreimage.LengthBytes == mutation.Bytes.LongLength
                && string.Equals(expectedPreimage.Sha256, plannedHash, StringComparison.Ordinal))
            {
                continue;
            }

            var nextWriteBytes = checked(plannedWriteBytes + mutation.Bytes.LongLength);
            if (nextWriteBytes > coordinatorOptions.MaximumWriteBytesPerApply)
            {
                throw new OutputLimitExceededException(
                    "The Pokemon Legends Z-A output batch exceeds the configured write limit.");
            }

            var nextBackupBytes = checked(plannedBackupBytes + expectedPreimage.LengthBytes);
            if (nextBackupBytes > coordinatorOptions.MaximumBackupBytesPerApply)
            {
                throw new OutputLimitExceededException(
                    "The Pokemon Legends Z-A output batch exceeds the configured backup limit.");
            }

            plannedWriteBytes = nextWriteBytes;
            plannedBackupBytes = nextBackupBytes;
            outputMutations.Add(OutputMutation.Write(
                relativePath,
                mutation.Bytes,
                expectedPreimage,
                [ownership]));
            EnsureMutationCountWithinLimit(outputMutations.Count, coordinatorOptions);
        }

        if (outputMutations.Count == 0)
        {
            var validationCoordinator = new OutputTransactionCoordinator(outputRoot, coordinatorOptions);
            foreach (var dependency in membershipDependencies)
            {
                var current = validationCoordinator
                    .CaptureDirectoryMembershipAsync(dependency.Directory)
                    .GetAwaiter()
                    .GetResult();
                if (current.Revision != dependency.ExpectedRevision)
                {
                    throw new OutputStateRevisionConflictException(
                        dependency.ExpectedRevision,
                        current.Revision);
                }
            }

            return null;
        }

        var context = applyContext ?? new ZaOutputApplyContext(
            OutputReviewFingerprint.FromMutations(outputMutations),
            ownerId,
            [new OutputApplyOrigin(OutputApplyOriginKind.Workflow, "workflow.za.output")]);
        var plan = new OutputApplyPlan(
            ProjectIdentity.FromPaths(paths),
            GameFamily.LegendsZA,
            ToOutputModeKey(outputMode),
            context.SemanticReviewHash,
            context.Origins,
            outputMutations,
            directoryMembershipDependencies: membershipDependencies);
        var coordinator = new OutputTransactionCoordinator(outputRoot, coordinatorOptions);
        var result = coordinator.ApplyAsync(plan).GetAwaiter().GetResult();
        if (result.Outcome != OutputApplyOutcome.Committed)
        {
            throw new ZaOutputApplyNotCommittedException(result);
        }

        return result;
    }

    private static IReadOnlyDictionary<string, OutputFileState> CapturePreparedPreimages(
        IReadOnlyList<ZaWorkflowOutputMutation> mutations,
        OutputTransactionCoordinatorOptions options)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var states = new Dictionary<string, OutputFileState>(comparer);
        long fingerprintedBytes = 0;
        foreach (var mutation in mutations)
        {
            if (mutation.Bytes?.LongLength > options.MaximumWriteBytesPerMutation)
            {
                throw new OutputLimitExceededException(
                    "A Pokemon Legends Z-A output target exceeds the configured write limit.");
            }

            var remainingBytes = options.MaximumBackupBytesPerApply - fingerprintedBytes;
            var state = CaptureOutputFileState(
                mutation.TargetPath,
                Math.Min(options.MaximumFingerprintFileBytes, remainingBytes));
            fingerprintedBytes = checked(fingerprintedBytes + state.LengthBytes);
            if (!states.TryAdd(Path.GetFullPath(mutation.TargetPath), state))
            {
                throw new InvalidDataException(
                    "Pokemon Legends Z-A output preparation contains duplicate targets.");
            }
        }

        return states;
    }

    private static void EnsureMutationCountWithinLimit(
        int mutationCount,
        OutputTransactionCoordinatorOptions options)
    {
        if (mutationCount > options.MaximumMutationsPerApply)
        {
            throw new OutputLimitExceededException(
                "The Pokemon Legends Z-A output batch contains too many targets.");
        }
    }

    private static OutputFileState CaptureOutputFileState(string targetPath, long maximumBytes)
    {
        if (Directory.Exists(targetPath))
        {
            throw new IOException("A Pokemon Legends Z-A output target is a directory.");
        }

        if (!File.Exists(targetPath))
        {
            return OutputFileState.Missing;
        }

        using var stream = new FileStream(
            targetPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 128 * 1024,
            FileOptions.SequentialScan);
        var length = stream.Length;
        if (maximumBytes < 0 || length > maximumBytes)
        {
            throw new OutputLimitExceededException(
                "A Pokemon Legends Z-A output preimage exceeds the configured backup limit.");
        }

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        var remaining = length;
        while (remaining > 0)
        {
            var bytesRead = stream.Read(buffer, 0, (int)Math.Min(buffer.LongLength, remaining));
            if (bytesRead == 0)
            {
                throw new IOException(
                    "A Pokemon Legends Z-A output preimage changed while it was being reviewed.");
            }

            hasher.AppendData(buffer, 0, bytesRead);
            remaining -= bytesRead;
        }

        if (stream.ReadByte() != -1)
        {
            throw new IOException(
                "A Pokemon Legends Z-A output preimage changed while it was being reviewed.");
        }

        var fingerprint = Convert.ToHexStringLower(hasher.GetHashAndReset());
        return OutputFileState.Existing(fingerprint, length);
    }

    private static string ToOutputModeKey(ZaOutputMode outputMode)
    {
        return outputMode switch
        {
            ZaOutputMode.Standalone => "za.standalone",
            ZaOutputMode.TrinityModManager => "za.trinity-mod-manager",
            ZaOutputMode.TrinityBypass => "za.trinity-bypass",
            _ => throw new ArgumentOutOfRangeException(nameof(outputMode), outputMode, null),
        };
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

    private static string ToOutputRelativePath(
        string normalizedVirtualPath,
        ZaOutputMode outputMode,
        bool isolateTrinityModManagerRomFs = false)
    {
        if (isolateTrinityModManagerRomFs
            && outputMode != ZaOutputMode.TrinityModManager)
        {
            throw new ArgumentException(
                "Isolated Trinity Mod Manager RomFS routing requires Trinity Mod Manager output.",
                nameof(isolateTrinityModManagerRomFs));
        }

        return outputMode switch
        {
            ZaOutputMode.Standalone => ToRelativePath(normalizedVirtualPath),
            ZaOutputMode.TrinityModManager when isolateTrinityModManagerRomFs =>
                $"{TrinityModManagerRomFsDirectory}/{normalizedVirtualPath}",
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

    private static string GetFirstPathSegment(string normalizedVirtualPath)
    {
        var separatorIndex = normalizedVirtualPath.IndexOf('/');
        return separatorIndex < 0
            ? normalizedVirtualPath
            : normalizedVirtualPath[..separatorIndex];
    }

    private static string NormalizeStandaloneOutputRelativePath(string outputRelativePath)
    {
        if (Path.IsPathRooted(outputRelativePath)
            || outputRelativePath.StartsWith('/')
            || outputRelativePath.StartsWith('\\'))
        {
            throw new ArgumentException(
                $"Pokemon Legends Z-A standalone output path '{outputRelativePath}' must be relative.",
                nameof(outputRelativePath));
        }

        var normalized = outputRelativePath.Replace('\\', '/');
        var invalidFileNameCharacters = Path.GetInvalidFileNameChars();
        var segments = normalized.Split('/');
        if (segments.Length == 0
            || segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."
                || segment.IndexOfAny(invalidFileNameCharacters) >= 0))
        {
            throw new ArgumentException(
                $"Pokemon Legends Z-A standalone output path '{outputRelativePath}' is not canonical.",
                nameof(outputRelativePath));
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
        string isolatedTrinityModManagerPath,
        string standalonePath)
    {
        var candidates = new[]
        {
            new LooseOutputCandidate(trinityModManagerPath, IsStandalone: false, Priority: 2),
            new LooseOutputCandidate(isolatedTrinityModManagerPath, IsStandalone: false, Priority: 3),
            new LooseOutputCandidate(standalonePath, IsStandalone: true, Priority: 1),
        };
        var selected = candidates
            .Where(candidate => FileExistsForInspection(candidate.Path))
            .OrderByDescending(candidate => File.GetLastWriteTimeUtc(candidate.Path))
            .ThenByDescending(candidate => candidate.Priority)
            .FirstOrDefault();
        if (selected is null)
        {
            return null;
        }

        return (selected.Path, selected.IsStandalone);
    }

    private static bool TryReadOutputArchive(
        ProjectPaths paths,
        string virtualPath,
        string relativePath,
        out byte[] bytes)
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
        catch (Exception exception) when (IsContextualFileFailure(exception))
        {
            // An existing output archive participates in the game's effective data. Falling back
            // to base after an inspection failure would hide that source and could stage edits
            // against bytes the game does not use.
            throw CreateReadFailure(
                relativePath,
                ProjectFileLayer.Layered,
                state: null,
                exception: exception,
                operation: ProjectFileOperation.Inspect);
        }
    }

    private static bool TryOutputArchiveContains(
        ProjectPaths paths,
        string virtualPath,
        string relativePath)
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
        catch (Exception exception) when (IsContextualFileFailure(exception))
        {
            // Treat an unreadable output archive as an unknown candidate, not as an absent file.
            throw CreateReadFailure(
                relativePath,
                ProjectFileLayer.Layered,
                state: null,
                exception: exception,
                operation: ProjectFileOperation.Inspect);
        }
    }

    private static byte[] ReadAllBytesWithContext(
        string path,
        string relativePath,
        ProjectFileLayer layer,
        ProjectFileGraphEntryState? state)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception exception) when (IsContextualFileFailure(exception))
        {
            throw CreateReadFailure(relativePath, layer, state, exception);
        }
    }

    private static bool FileExistsWithContext(
        string path,
        string relativePath,
        ProjectFileLayer layer,
        ProjectFileGraphEntryState? state)
    {
        try
        {
            return FileExistsForInspection(path);
        }
        catch (Exception exception) when (IsContextualFileFailure(exception))
        {
            throw CreateReadFailure(
                relativePath,
                layer,
                state,
                exception,
                ProjectFileOperation.Inspect);
        }
    }

    private static bool FileExistsForInspection(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.Directory) == 0;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static ProjectFileOperationException CreateReadFailure(
        string relativePath,
        ProjectFileLayer? layer,
        ProjectFileGraphEntryState? state,
        Exception exception,
        ProjectFileOperation operation = ProjectFileOperation.Read)
    {
        return exception as ProjectFileOperationException
            ?? new ProjectFileOperationException(
                operation,
                relativePath,
                layer,
                state,
                exception);
    }

    private static bool IsContextualFileFailure(Exception exception)
    {
        return exception is IOException
            or InvalidDataException
            or UnauthorizedAccessException
            or SecurityException
            or DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException;
    }

    private static bool HasTrinityArchive(string rootPath)
    {
        return HasTrinityArchiveAt(rootPath)
            || HasTrinityArchiveAt(Path.Combine(rootPath, "romfs"));
    }

    private static bool HasTrinityArchiveAt(string romFsRoot)
    {
        return FileExistsForInspection(Path.Combine(romFsRoot, "arc", "data.trpfd"))
            && FileExistsForInspection(Path.Combine(romFsRoot, "arc", "data.trpfs"));
    }

    private sealed record LooseOutputCandidate(
        string Path,
        bool IsStandalone,
        int Priority);
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

internal sealed record ZaStandaloneOutputMutation(
    string RelativePath,
    byte[]? Bytes);

internal sealed record ZaStandaloneMixedBatch(
    IReadOnlyList<ZaWorkflowFileWrite> RomFsWrites,
    IReadOnlyList<string> RomFsDeletes,
    IReadOnlyList<ZaStandaloneOutputMutation> OutputMutations,
    byte[]? ReviewedStandaloneDescriptorBytes = null,
    bool DeleteStandaloneDescriptor = false);

internal sealed record ZaWorkflowOutputMutation(
    string TargetPath,
    byte[]? Bytes);

internal sealed record ZaOutputApplyContext(
    string SemanticReviewHash,
    OwnershipOwnerId OwnerId,
    IReadOnlyList<OutputApplyOrigin> Origins);

public sealed class ZaOutputApplyNotCommittedException : IOException
{
    public ZaOutputApplyNotCommittedException(OutputApplyResult result)
        : base(result.Outcome == OutputApplyOutcome.RolledBack
            ? "Pokemon Legends Z-A output was rolled back and no reviewed changes were kept."
            : "Pokemon Legends Z-A output requires recovery before another write can begin.")
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public OutputApplyResult Result { get; }
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
