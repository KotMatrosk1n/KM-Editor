// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
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
    private const int MaximumBoundedArchiveIndexBytes = 64 * 1024 * 1024;
    private const long MaximumBoundedArchivePackBytes = 128L * 1024L * 1024L;
    private const int MaximumBoundedTableRecords = 50_000;
    private const int MaximumBoundedNestedRecords = 100_000;

    private static readonly ConcurrentDictionary<string, object> OutputRootLocks = new(
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
    [ThreadStatic]
    private static DeferredOutputBatch? activeDeferredOutputBatch;
    [ThreadStatic]
    private static int freshReadScopeDepth;
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
    private readonly bool bypassReusableBaseCache;
    private readonly int? maximumReadBytes;
    private readonly int? maximumReadCount;
    private readonly long? maximumAggregateReadBytes;
    private readonly Dictionary<BoundedReadMemoKey, BoundedReadMemoEntry> boundedReadMemo = [];
    private int boundedReadCount;
    private long boundedReadBytes;

    public ZaWorkflowFileSource(
        ZaCacheManager? cacheManager = null,
        bool bypassReusableBaseCache = false,
        int? maximumReadBytes = null,
        int? maximumReadCount = null,
        long? maximumAggregateReadBytes = null)
    {
        if (maximumReadBytes is <= 0
            || maximumReadCount is <= 0
            || maximumAggregateReadBytes is <= 0
            || (maximumReadCount is null) != (maximumAggregateReadBytes is null)
            || maximumReadCount is not null && maximumReadBytes is null)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumReadBytes), "The bounded read budget is invalid.");
        }

        this.cacheManager = cacheManager ?? new ZaCacheManager();
        this.bypassReusableBaseCache = bypassReusableBaseCache;
        this.maximumReadBytes = maximumReadBytes;
        this.maximumReadCount = maximumReadCount;
        this.maximumAggregateReadBytes = maximumAggregateReadBytes;
    }

    internal int? BoundedTableRecordLimit => maximumReadBytes is null
        ? null
        : MaximumBoundedTableRecords;

    internal int? BoundedNestedRecordLimit => maximumReadBytes is null
        ? null
        : MaximumBoundedNestedRecords;

    internal bool IsBoundedSemanticLimit(Exception exception)
    {
        if (maximumReadBytes is null)
        {
            return false;
        }

        Exception? candidate = exception;
        for (var depth = 0; candidate is not null && depth < 8; depth++)
        {
            if (candidate is InvalidDataException
                && candidate.Message.Contains("bounded", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            candidate = candidate.InnerException;
        }

        return false;
    }

    internal void EnsureBoundedTableCount(int count, string label)
    {
        EnsureBoundedCount(count, MaximumBoundedTableRecords, label);
    }

    internal void EnsureBoundedNestedCount(int count, string label)
    {
        EnsureBoundedCount(count, MaximumBoundedNestedRecords, label);
    }

    private void EnsureBoundedCount(int count, int maximum, string label)
    {
        if (maximumReadBytes is not null && (count < 0 || count > maximum))
        {
            throw new InvalidDataException($"{label} exceeds the bounded semantic record limit.");
        }
    }

    private void EnsureBoundedReadAvailable()
    {
        if (maximumReadCount is null || maximumAggregateReadBytes is null || maximumReadBytes is null)
        {
            return;
        }

        if (boundedReadCount >= maximumReadCount.Value
            || boundedReadBytes > maximumAggregateReadBytes.Value - maximumReadBytes.Value)
        {
            throw new InvalidDataException("The workflow exceeds its bounded fresh source-read budget.");
        }

        boundedReadCount = checked(boundedReadCount + 1);
    }

    private void ObserveBoundedRead(int byteCount)
    {
        if (maximumReadCount is null || maximumAggregateReadBytes is null)
        {
            return;
        }

        var nextBytes = checked(boundedReadBytes + byteCount);
        if (nextBytes > maximumAggregateReadBytes.Value)
        {
            throw new InvalidDataException("The workflow exceeds its bounded fresh source-byte budget.");
        }

        boundedReadBytes = nextBytes;
    }

    private void ObserveFailedBoundedRead(Exception exception)
    {
        if (maximumReadBytes is not null && !IsDefinitelyMissing(exception))
        {
            ObserveBoundedRead(maximumReadBytes.Value);
        }
    }

    private static bool IsDefinitelyMissing(Exception exception)
    {
        Exception? candidate = exception;
        for (var depth = 0; candidate is not null && depth < 8; depth++)
        {
            if (candidate is FileNotFoundException or DirectoryNotFoundException)
            {
                return true;
            }

            candidate = candidate.InnerException;
        }

        return false;
    }

    public ZaWorkflowFile Read(OpenedProject project, string virtualRomFsPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);
        var memoKey = new BoundedReadMemoKey(
            project.Id,
            BaseOnly: false,
            NormalizeVirtualPath(virtualRomFsPath));
        if (TryGetBoundedReadMemo(memoKey, out var memoized))
        {
            return memoized;
        }

        EnsureBoundedReadAvailable();
        ZaWorkflowFile result;
        try
        {
            result = ReadCore(project, virtualRomFsPath);
        }
        catch (Exception exception)
        {
            ObserveFailedBoundedRead(exception);
            StoreBoundedReadFailure(memoKey, exception);
            throw;
        }

        ObserveBoundedRead(result.Bytes.Length);
        StoreBoundedReadResult(memoKey, result);
        return result;
    }

    private ZaWorkflowFile ReadCore(OpenedProject project, string virtualRomFsPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);

        var normalizedVirtualPath = NormalizeVirtualPath(virtualRomFsPath);
        var relativePath = ToRelativePath(normalizedVirtualPath);
        var entry = FindEntry(project, relativePath);
        var suppressLayeredOutput = false;
        if (activeDeferredOutputBatch is { IsCommitting: false } deferredBatch
            && deferredBatch.Matches(project.Paths)
            && deferredBatch.TryGetRomFsMutation(normalizedVirtualPath, out var stagedBytes))
        {
            if (stagedBytes is not null)
            {
                return new ZaWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    stagedBytes.ToArray(),
                    ProjectFileLayer.Layered,
                    entry?.State ?? ProjectFileGraphEntryState.LayeredOverride,
                    deferredBatch.OutputMode == ZaOutputMode.TrinityModManager
                        ? ZaWorkflowFileOrigin.TrinityModManagerLooseOutput
                        : ZaWorkflowFileOrigin.StandaloneLooseOutput);
            }

            suppressLayeredOutput = true;
        }

        if (!suppressLayeredOutput
            && !string.IsNullOrWhiteSpace(project.Paths.OutputRootPath))
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
                var archiveBytes = bypassReusableBaseCache || freshReadScopeDepth > 0
                    ? ReadBaseBytesFresh(project.Paths, normalizedVirtualPath)
                    : cacheManager.ReadBaseTrinityFile(project.Paths, normalizedVirtualPath);
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
        var memoKey = new BoundedReadMemoKey(
            project.Id,
            BaseOnly: true,
            NormalizeVirtualPath(virtualRomFsPath));
        if (TryGetBoundedReadMemo(memoKey, out var memoized))
        {
            return memoized;
        }

        EnsureBoundedReadAvailable();
        ZaWorkflowFile result;
        try
        {
            result = ReadBaseCore(project, virtualRomFsPath);
        }
        catch (Exception exception)
        {
            ObserveFailedBoundedRead(exception);
            StoreBoundedReadFailure(memoKey, exception);
            throw;
        }

        ObserveBoundedRead(result.Bytes.Length);
        StoreBoundedReadResult(memoKey, result);
        return result;
    }

    private bool TryGetBoundedReadMemo(BoundedReadMemoKey key, out ZaWorkflowFile result)
    {
        result = null!;
        if (maximumReadCount is null || !boundedReadMemo.TryGetValue(key, out var memo))
        {
            return false;
        }

        if (memo.Failure is not null)
        {
            memo.Failure.Throw();
        }

        result = memo.Result!;
        return true;
    }

    private void StoreBoundedReadResult(BoundedReadMemoKey key, ZaWorkflowFile result)
    {
        if (maximumReadCount is not null)
        {
            boundedReadMemo.Add(key, new BoundedReadMemoEntry(result, Failure: null));
        }
    }

    private void StoreBoundedReadFailure(BoundedReadMemoKey key, Exception exception)
    {
        if (maximumReadCount is not null)
        {
            boundedReadMemo.Add(
                key,
                new BoundedReadMemoEntry(
                    Result: null,
                    ExceptionDispatchInfo.Capture(exception)));
        }
    }

    private ZaWorkflowFile ReadBaseCore(OpenedProject project, string virtualRomFsPath)
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
                var archiveBytes = bypassReusableBaseCache || freshReadScopeDepth > 0
                    ? ReadBaseBytesFresh(project.Paths, normalizedVirtualPath)
                    : cacheManager.ReadBaseTrinityFile(project.Paths, normalizedVirtualPath);
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

    internal byte[] ReadCurrentBytesFresh(ProjectPaths paths, string virtualRomFsPath)
    {
        return ReadCurrentSourceFresh(paths, virtualRomFsPath).Bytes;
    }

    internal (byte[] Bytes, ProjectFileLayer Layer) ReadCurrentSourceFresh(
        ProjectPaths paths,
        string virtualRomFsPath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);

        var normalizedVirtualPath = NormalizeVirtualPath(virtualRomFsPath);
        var relativePath = ToRelativePath(normalizedVirtualPath);
        if (!string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            var trinityModManagerPath = CombineGraphPath(paths.OutputRootPath, normalizedVirtualPath);
            var isolatedPath = CombineGraphPath(
                paths.OutputRootPath,
                $"{TrinityModManagerRomFsDirectory}/{normalizedVirtualPath}");
            var standalonePath = CombineGraphPath(paths.OutputRootPath, relativePath);
            var looseOutput = SelectLatestLooseOutput(
                trinityModManagerPath,
                isolatedPath,
                standalonePath);
            if (looseOutput is not null)
            {
                return (
                    ReadAllBytesWithContext(
                        looseOutput.Value.Path,
                        relativePath,
                        ProjectFileLayer.Layered,
                        ProjectFileGraphEntryState.LayeredOverride),
                    ProjectFileLayer.Layered);
            }

            if (TryReadOutputArchive(paths, normalizedVirtualPath, relativePath, out var outputBytes))
            {
                return (outputBytes, ProjectFileLayer.Layered);
            }
        }

        return (ReadBaseBytesFresh(paths, normalizedVirtualPath), ProjectFileLayer.Base);
    }

    internal byte[] ReadBaseBytesFresh(ProjectPaths paths, string virtualRomFsPath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);

        var normalizedVirtualPath = NormalizeVirtualPath(virtualRomFsPath);
        var relativePath = ToRelativePath(normalizedVirtualPath);
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            throw CreateReadFailure(
                relativePath,
                ProjectFileLayer.Base,
                state: null,
                exception: new DirectoryNotFoundException());
        }

        var looseBasePath = CombineGraphPath(paths.BaseRomFsPath, normalizedVirtualPath);
        if (FileExistsWithContext(
            looseBasePath,
            relativePath,
            ProjectFileLayer.Base,
            state: null))
        {
            return ReadAllBytesWithContext(
                looseBasePath,
                relativePath,
                ProjectFileLayer.Base,
                ProjectFileGraphEntryState.BaseOnly);
        }

        try
        {
            using var archive = OpenArchive(
                paths.BaseRomFsPath,
                paths.PokemonLegendsZASupportFolderPath);
            return maximumReadBytes is { } limit
                ? archive.ReadFile(normalizedVirtualPath, limit)
                : archive.ReadFile(normalizedVirtualPath);
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
            if (bypassReusableBaseCache || freshReadScopeDepth > 0)
            {
                using var archive = OpenArchive(
                    project.Paths.BaseRomFsPath,
                    project.Paths.PokemonLegendsZASupportFolderPath);
                return archive.ContainsFile(normalizedVirtualPath);
            }

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
            if (bypassReusableBaseCache || freshReadScopeDepth > 0)
            {
                return ZaTrinityArchive.BuildIndex(project.Paths.BaseRomFsPath!)
                    .Files
                    .Select(file => file.PackName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

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

    internal static IDisposable BeginFreshReadScope()
    {
        // Safety-critical plan/apply paths must consume the same current archive
        // bytes that their source-binding guard records, even when cache stamps
        // (length and last-write time) are unchanged.
        freshReadScopeDepth = checked(freshReadScopeDepth + 1);
        return new FreshReadScope();
    }

    internal static string ResolveReviewedOutputPath(
        ProjectPaths paths,
        string targetRelativePath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRelativePath);
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException(
                "Set an output root before reviewing Pokemon Legends Z-A edits.");
        }

        var relativePath = new RelativeOutputPath(targetRelativePath).Value;
        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(
            outputRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (PathContainment.IsOutsideRoot(Path.GetRelativePath(outputRoot, targetPath)))
        {
            throw new InvalidOperationException(
                "Pokemon Legends Z-A reviewed output path escapes its configured root.");
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
        var replacesExistingOutput = activeDeferredOutputBatch is { IsCommitting: false } deferredBatch
            && deferredBatch.Matches(paths, outputMode)
                ? deferredBatch.TargetExists(
                    NormalizeVirtualPath(virtualRomFsPath),
                    isolateTrinityModManagerRomFs,
                    targetPath)
                : File.Exists(targetPath);

        return new PlannedWriteInfo(
            targetRelativePath,
            sources,
            replacesExistingOutput);
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

    internal static IReadOnlyList<string> GetOwnedStandaloneRomFsVirtualPaths(
        ProjectPaths paths,
        OwnershipOwnerId ownerId)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(ownerId);
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException(
                "Pokemon Legends Z-A owned output inspection requires an output root.");
        }

        using var outputLock = AcquireOutputLock(paths);
        var coordinator = new OutputTransactionCoordinator(paths.OutputRootPath);
        var projectId = ProjectIdentity.FromPaths(paths);
        var inventory = coordinator.GetOwnershipInventoryAsync().GetAwaiter().GetResult();
        const string romFsPrefix = "romfs/";
        return inventory.Files
            .Where(record => record.ProjectId == projectId
                && record.GameFamily == GameFamily.LegendsZA
                && string.Equals(record.OutputMode, ToOutputModeKey(ZaOutputMode.Standalone), StringComparison.Ordinal)
                && record.Path.Value.StartsWith(romFsPrefix, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(record.Path.Value, $"romfs/{DescriptorVirtualPath}", StringComparison.OrdinalIgnoreCase)
                && record.Claims.Any(claim => claim.OwnerId == ownerId))
            .Select(record => NormalizeVirtualPath(record.Path.Value[romFsPrefix.Length..]))
            .Order(StringComparer.Ordinal)
            .ToArray();
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

                return new ZaWorkflowFileWrite(
                    virtualPath,
                    write.Bytes.ToArray(),
                    write.ApplyContext);
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

                return new ZaStandaloneOutputMutation(
                    relativePath,
                    mutation.Bytes?.ToArray(),
                    mutation.DeleteFallbackBytes?.ToArray(),
                    mutation.ApplyContext);
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

        if (activeDeferredOutputBatch is { IsCommitting: false } deferredBatch)
        {
            deferredBatch.Stage(
                paths,
                outputMode,
                normalizedWrites,
                normalizedDeletes,
                normalizedOutputMutations,
                deleteStandaloneDescriptor,
                allowHybridExeFsOutput,
                isolateTrinityModManagerRomFs,
                applyContext,
                revalidateReviewedState);
            return null;
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
                    write.Bytes,
                    DeleteFallbackBytes: null,
                    write.ApplyContext ?? applyContext))
                .ToList();
            mutations.AddRange(normalizedDeletes.Select(delete =>
                new ZaWorkflowOutputMutation(
                    ResolveOutputPath(
                        paths,
                        delete,
                        outputMode,
                        isolateTrinityModManagerRomFs),
                    Bytes: null,
                    DeleteFallbackBytes: null,
                    applyContext)));
            mutations.AddRange(normalizedOutputMutations.Select(mutation =>
                new ZaWorkflowOutputMutation(
                    ResolveStandaloneOutputPath(paths, mutation.RelativePath),
                    mutation.Bytes,
                    mutation.DeleteFallbackBytes,
                    mutation.ApplyContext ?? applyContext)));
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
                    deleteStandaloneDescriptor ? null : descriptorBytes,
                    DeleteFallbackBytes: null,
                    applyContext));
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

    internal static DeferredOutputBatch BeginDeferredOutputBatch(
        ProjectPaths paths,
        ZaOutputMode outputMode,
        ChangePlan reviewedPlan,
        ZaOutputApplyContext applyContext)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(reviewedPlan);
        ArgumentNullException.ThrowIfNull(applyContext);
        if (activeDeferredOutputBatch is not null)
        {
            throw new InvalidOperationException(
                "A Pokemon Legends Z-A deferred output batch is already active on this thread.");
        }

        var batch = new DeferredOutputBatch(paths, outputMode, reviewedPlan, applyContext);
        activeDeferredOutputBatch = batch;
        return batch;
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
        var membership = CaptureStandaloneRomFsMembership(paths);
        return CreatePatchedDescriptorBytes(
            paths,
            plannedVirtualPaths,
            Array.Empty<string>(),
            GetLayeredVirtualPaths(membership));
    }

    internal static byte[] CreateStandaloneDescriptorPreview(
        ProjectPaths paths,
        IEnumerable<string> plannedWriteVirtualPaths,
        IEnumerable<string> plannedDeleteVirtualPaths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(plannedWriteVirtualPaths);
        ArgumentNullException.ThrowIfNull(plannedDeleteVirtualPaths);
        var membership = CaptureStandaloneRomFsMembership(paths);
        return CreatePatchedDescriptorBytes(
            paths,
            plannedWriteVirtualPaths,
            plannedDeleteVirtualPaths,
            GetLayeredVirtualPaths(membership));
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

        return ReadOnlyOutputDirectoryMembership.Capture(
            paths.OutputRootPath,
            new RelativeOutputPath("romfs"));
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
        var defaultOwnerId = applyContext?.OwnerId ?? new OwnershipOwnerId("workflow.za.output");
        var preservationRule = new PreservationRuleDescriptor(
            "za.full-file-rebuild",
            schemaVersion: 1,
            preservesUnownedData: true,
            requiresPreimage: true);
        var coordinatorOptions = new OutputTransactionCoordinatorOptions();
        using var verifiedBaseMainLease = OpenVerifiedBaseMainLeaseIfRequired(
            paths,
            outputRoot,
            mutations,
            coordinatorOptions.MaximumWriteBytesPerMutation);
        var verifiedBaseMain = verifiedBaseMainLease is null
            ? null
            : ReadVerifiedBaseMain(verifiedBaseMainLease);
        var verifiedBaseMainState = verifiedBaseMain is null
            ? null
            : OutputFileState.Existing(
                Convert.ToHexStringLower(SHA256.HashData(verifiedBaseMain)),
                verifiedBaseMain.LongLength);
        var coordinator = new OutputTransactionCoordinator(outputRoot, coordinatorOptions);
        var projectId = ProjectIdentity.FromPaths(paths);
        var outputModeKey = ToOutputModeKey(outputMode);
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

        var ownershipSnapshot = coordinator
            .GetOwnershipInventorySnapshotAsync()
            .GetAwaiter()
            .GetResult();
        var inventory = ownershipSnapshot.Inventory;

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
            if (mutation.Bytes?.LongLength > coordinatorOptions.MaximumWriteBytesPerMutation
                || mutation.DeleteFallbackBytes?.LongLength
                    > coordinatorOptions.MaximumWriteBytesPerMutation)
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
                mutation.ApplyContext?.OwnerId ?? defaultOwnerId,
                preservationRule);
            var ownedRecord = inventory.Files.FirstOrDefault(record => record.Path == relativePath);
            var isComposedExecutable = IsComposedExecutablePath(relativePath);
            var ownershipClaims = new[] { ownership };
            if (isComposedExecutable && ownedRecord is not null)
            {
                ValidateComposedExecutableOwnership(
                    ownedRecord,
                    projectId,
                    GameFamily.LegendsZA,
                    outputModeKey,
                    expectedPreimage,
                    relativePath);
                ownershipClaims = ownedRecord.Claims
                    .Where(claim => claim.OwnerId != ownership.OwnerId)
                    .Append(ownership)
                    .Distinct()
                    .ToArray();
            }

            var bytes = mutation.Bytes;
            if (bytes is null && expectedPreimage.Exists)
            {
                if (isComposedExecutable)
                {
                    var remainingClaims = ownedRecord is null
                        ? []
                        : ownedRecord.Claims
                            .Where(claim => claim.OwnerId != ownership.OwnerId)
                            .ToArray();
                    var activeRemainingClaims = remainingClaims
                        .Where(claim => !OutputCreatorProvenance.IsClaim(claim))
                        .ToArray();
                    var canDelete = ownedRecord is not null
                        && activeRemainingClaims.Length == 0
                        && remainingClaims.Length == 0
                        && ownedRecord.FileDeleteEligible
                        && ownedRecord.Claims.Any(claim =>
                            claim.Address.ScopeKind == OwnedTargetScopeKind.File);
                    if (canDelete)
                    {
                        plannedBackupBytes = checked(
                            plannedBackupBytes + expectedPreimage.LengthBytes);
                        outputMutations.Add(OutputMutation.Delete(
                            relativePath,
                            expectedPreimage,
                            ownedRecord!.Claims,
                            outputModeKey));
                        EnsureMutationCountWithinLimit(outputMutations.Count, coordinatorOptions);
                        continue;
                    }

                    var canDeleteVerifiedBase = ownedRecord is not null
                        && activeRemainingClaims.Length == 0
                        && remainingClaims.Length > 0
                        && remainingClaims.All(OutputCreatorProvenance.IsClaim)
                        && ownedRecord.FileDeleteEligible
                        && mutation.DeleteFallbackBytes is { } verifiedFallback
                        && verifiedBaseMain is not null
                        && verifiedBaseMainState is not null
                        && verifiedFallback.AsSpan().SequenceEqual(verifiedBaseMain);
                    if (canDeleteVerifiedBase)
                    {
                        var authority = new OutputVerifiedBaseDeleteAuthority(
                            projectId,
                            GameFamily.LegendsZA,
                            ownership.OwnerId,
                            outputModeKey,
                            relativePath,
                            expectedPreimage,
                            verifiedBaseMainState!,
                            ownedRecord!.Claims);
                        plannedBackupBytes = checked(
                            plannedBackupBytes + expectedPreimage.LengthBytes);
                        outputMutations.Add(OutputMutation.DeleteVerifiedBase(
                            relativePath,
                            expectedPreimage,
                            ownedRecord.Claims,
                            authority));
                        EnsureMutationCountWithinLimit(outputMutations.Count, coordinatorOptions);
                        continue;
                    }

                    bytes = mutation.DeleteFallbackBytes
                        ?? throw new OutputOwnershipConflictException(relativePath);
                    if (remainingClaims.Length > 0)
                    {
                        ownershipClaims = ownedRecord!.FileDeleteEligible
                            && !remainingClaims.Any(claim =>
                                claim.Address.ScopeKind == OwnedTargetScopeKind.File
                                && !OutputCreatorProvenance.IsClaim(claim))
                            ? remainingClaims
                                .Append(OutputCreatorProvenance.Create(
                                    GameFamily.LegendsZA,
                                    relativePath))
                                .Distinct()
                                .ToArray()
                            : remainingClaims;
                    }
                }
                else
                {
                    plannedBackupBytes = checked(plannedBackupBytes + expectedPreimage.LengthBytes);
                    outputMutations.Add(OutputMutation.Delete(
                        relativePath,
                        expectedPreimage,
                        [ownership],
                        outputModeKey));
                    EnsureMutationCountWithinLimit(outputMutations.Count, coordinatorOptions);
                    continue;
                }
            }
            else if (bytes is null)
            {
                continue;
            }

            var plannedHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (expectedPreimage.Exists
                && expectedPreimage.LengthBytes == bytes.LongLength
                && string.Equals(expectedPreimage.Sha256, plannedHash, StringComparison.Ordinal))
            {
                continue;
            }

            var nextWriteBytes = checked(plannedWriteBytes + bytes.LongLength);
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
                bytes,
                expectedPreimage,
                ownershipClaims,
                outputModeKey,
                ownershipActor: isComposedExecutable ? ownership.OwnerId : null));
            EnsureMutationCountWithinLimit(outputMutations.Count, coordinatorOptions);
        }

        if (outputMutations.Count == 0)
        {
            foreach (var dependency in membershipDependencies)
            {
                var current = coordinator
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
            defaultOwnerId,
            [new OutputApplyOrigin(OutputApplyOriginKind.Workflow, "workflow.za.output")]);
        var origins = mutations
            .Select(mutation => mutation.ApplyContext)
            .Where(mutationContext => mutationContext is not null)
            .SelectMany(mutationContext => mutationContext!.Origins)
            .Concat(context.Origins)
            .Concat(outputMutations
                .Select(mutation => mutation.OwnershipActor)
                .Where(actor => actor is not null)
                .Select(actor => new OutputApplyOrigin(
                    OutputApplyOriginKind.Workflow,
                    actor!.Value)))
            .Concat(outputMutations
                .Select(mutation => mutation.VerifiedBaseDeleteAuthority?.ActingOwnerId)
                .Where(actor => actor is not null)
                .Select(actor => new OutputApplyOrigin(
                    OutputApplyOriginKind.Workflow,
                    actor!.Value)))
            .Distinct()
            .ToArray();
        var plan = new OutputApplyPlan(
            projectId,
            GameFamily.LegendsZA,
            outputModeKey,
            context.SemanticReviewHash,
            origins,
            outputMutations,
            directoryMembershipDependencies: membershipDependencies,
            ownershipInventoryRevision: ownershipSnapshot.Revision);
        var result = coordinator.ApplyAsync(plan).GetAwaiter().GetResult();
        if (result.Outcome != OutputApplyOutcome.Committed)
        {
            throw new ZaOutputApplyNotCommittedException(result);
        }

        return result;
    }

    private static bool IsComposedExecutablePath(RelativeOutputPath path)
    {
        return string.Equals(path.CanonicalKey, "EXEFS/MAIN", StringComparison.Ordinal);
    }

    private static FileStream? OpenVerifiedBaseMainLeaseIfRequired(
        ProjectPaths paths,
        string outputRoot,
        IReadOnlyList<ZaWorkflowOutputMutation> mutations,
        long maximumBytes)
    {
        var requiresBase = mutations.Any(mutation =>
        {
            if (mutation.Bytes is not null || mutation.DeleteFallbackBytes is null)
            {
                return false;
            }

            var relative = Path.GetRelativePath(outputRoot, Path.GetFullPath(mutation.TargetPath))
                .Replace(Path.DirectorySeparatorChar, '/');
            return !PathContainment.IsOutsideRoot(relative)
                && IsComposedExecutablePath(new RelativeOutputPath(relative));
        });
        if (!requiresBase)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
        {
            throw new InvalidOperationException(
                "Composed Pokemon Legends Z-A executable cleanup requires Base ExeFS main.");
        }

        var baseRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.BaseExeFsPath));
        var baseMainPath = Path.GetFullPath(Path.Combine(baseRoot, "main"));
        var relativeBasePath = Path.GetRelativePath(baseRoot, baseMainPath);
        if (PathContainment.IsOutsideRoot(relativeBasePath)
            || !File.Exists(baseMainPath)
            || Directory.Exists(baseMainPath))
        {
            throw new IOException(
                "Composed Pokemon Legends Z-A executable cleanup requires a physical Base ExeFS main.");
        }

        var stream = new FileStream(
            baseMainPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.SequentialScan);
        if (stream.Length <= 0 || stream.Length > maximumBytes)
        {
            stream.Dispose();
            throw new OutputLimitExceededException(
                "Base ExeFS main exceeds the configured Pokemon Legends Z-A executable limit.");
        }

        return stream;
    }

    private static byte[] ReadVerifiedBaseMain(FileStream stream)
    {
        var bytes = new byte[checked((int)stream.Length)];
        stream.Position = 0;
        stream.ReadExactly(bytes);
        stream.Position = 0;
        return bytes;
    }

    private static void ValidateComposedExecutableOwnership(
        OutputOwnershipRecord owned,
        ProjectId projectId,
        GameFamily gameFamily,
        string outputMode,
        OutputFileState expectedPreimage,
        RelativeOutputPath relativePath)
    {
        if (owned.ProjectId != projectId
            || owned.GameFamily != gameFamily
            || owned.CurrentState != expectedPreimage
            || !string.Equals(owned.OutputMode, outputMode, StringComparison.Ordinal))
        {
            throw new OutputOwnershipConflictException(relativePath);
        }
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

    private bool TryReadOutputArchive(
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

            using var archive = OpenArchive(
                outputRootPath,
                paths.PokemonLegendsZASupportFolderPath);
            return maximumReadBytes is { } limit
                ? archive.TryReadFile(virtualPath, limit, out bytes)
                : archive.TryReadFile(virtualPath, out bytes);
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

    private bool TryOutputArchiveContains(
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

            using var archive = OpenArchive(
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

    private byte[] ReadAllBytesWithContext(
        string path,
        string relativePath,
        ProjectFileLayer layer,
        ProjectFileGraphEntryState? state)
    {
        try
        {
            if (maximumReadBytes is not { } limit)
            {
                return File.ReadAllBytes(path);
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1_024,
                FileOptions.SequentialScan);
            if (stream.Length < 0 || stream.Length > limit)
            {
                throw new InvalidDataException("The semantic source file exceeds its bounded limit.");
            }

            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1)
            {
                throw new InvalidDataException("The semantic source file changed while it was read.");
            }

            return bytes;
        }
        catch (Exception exception) when (IsContextualFileFailure(exception))
        {
            throw CreateReadFailure(relativePath, layer, state, exception);
        }
    }

    private ZaTrinityArchive OpenArchive(string rootPath, string? supportFolderPath)
    {
        return maximumReadBytes is null
            ? ZaTrinityArchive.Open(rootPath, supportFolderPath)
            : ZaTrinityArchive.Open(
                rootPath,
                supportFolderPath,
                maximumIndexBytes: MaximumBoundedArchiveIndexBytes,
                maximumPackBytes: MaximumBoundedArchivePackBytes);
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

    internal sealed class DeferredOutputBatch : IDisposable
    {
        private readonly ProjectPaths paths;
        private readonly ZaOutputApplyContext applyContext;
        private readonly Dictionary<string, DeferredMutation> romFsMutations = new(
            StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DeferredMutation> standaloneOutputMutations;
        private readonly Dictionary<string, OutputFileState> reviewedTargetStates;
        private readonly HashSet<string> reviewedTargetRelativePaths;
        private readonly OutputDirectoryMembershipSnapshot? reviewedStandaloneMembership;
        private readonly OutputTransactionCoordinatorOptions coordinatorOptions = new();
        private bool deleteStandaloneDescriptorRequested;
        private bool disposed;
        private bool committed;

        internal DeferredOutputBatch(
            ProjectPaths paths,
            ZaOutputMode outputMode,
            ChangePlan reviewedPlan,
            ZaOutputApplyContext applyContext)
        {
            this.paths = paths;
            OutputMode = outputMode;
            this.applyContext = applyContext;
            var pathComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            standaloneOutputMutations = new Dictionary<string, DeferredMutation>(pathComparer);
            reviewedTargetStates = new Dictionary<string, OutputFileState>(pathComparer);
            reviewedTargetRelativePaths = new HashSet<string>(pathComparer);

            long capturedBytes = 0;
            foreach (var write in reviewedPlan.Writes)
            {
                var relativePath = NormalizeStandaloneOutputRelativePath(write.TargetRelativePath);
                if (!reviewedTargetRelativePaths.Add(relativePath))
                {
                    throw new InvalidDataException(
                        "The reviewed Pokemon Legends Z-A mixed plan contains duplicate output targets.");
                }

                var targetPath = ResolveStandaloneOutputPath(paths, relativePath);
                var remainingBytes = coordinatorOptions.MaximumBackupBytesPerApply - capturedBytes;
                var state = CaptureOutputFileState(
                    targetPath,
                    Math.Min(coordinatorOptions.MaximumFingerprintFileBytes, remainingBytes));
                capturedBytes = checked(capturedBytes + state.LengthBytes);
                reviewedTargetStates.Add(targetPath, state);
            }

            reviewedStandaloneMembership = outputMode == ZaOutputMode.Standalone
                ? CaptureStandaloneRomFsMembership(paths)
                : null;
        }

        internal ZaOutputMode OutputMode { get; }

        internal bool IsCommitting { get; private set; }

        internal bool Matches(ProjectPaths candidate)
        {
            return candidate == paths;
        }

        internal bool Matches(ProjectPaths candidate, ZaOutputMode outputMode)
        {
            return outputMode == OutputMode && Matches(candidate);
        }

        internal bool TryGetRomFsMutation(string normalizedVirtualPath, out byte[]? bytes)
        {
            if (romFsMutations.TryGetValue(normalizedVirtualPath, out var mutation))
            {
                bytes = mutation.Bytes;
                return true;
            }

            bytes = null;
            return false;
        }

        internal bool TargetExists(
            string normalizedVirtualPath,
            bool isolateTrinityModManagerRomFs,
            string resolvedTargetPath)
        {
            if (!isolateTrinityModManagerRomFs
                && romFsMutations.TryGetValue(normalizedVirtualPath, out var mutation))
            {
                return mutation.Bytes is not null;
            }

            if (!isolateTrinityModManagerRomFs
                && OutputMode == ZaOutputMode.Standalone
                && string.Equals(
                    normalizedVirtualPath,
                    DescriptorVirtualPath,
                    StringComparison.OrdinalIgnoreCase)
                && romFsMutations.Count > 0)
            {
                return !deleteStandaloneDescriptorRequested;
            }

            return File.Exists(resolvedTargetPath);
        }

        internal void Stage(
            ProjectPaths candidatePaths,
            ZaOutputMode outputMode,
            IReadOnlyList<ZaWorkflowFileWrite> writes,
            IReadOnlyList<string> deletes,
            IReadOnlyList<ZaStandaloneOutputMutation> outputMutations,
            bool deleteStandaloneDescriptor,
            bool allowHybridExeFsOutput,
            bool isolateTrinityModManagerRomFs,
            ZaOutputApplyContext? operationContext,
            Func<bool>? revalidateReviewedState)
        {
            ThrowIfUnavailable();
            if (!Matches(candidatePaths, outputMode))
            {
                throw new InvalidOperationException(
                    "A Pokemon Legends Z-A deferred output batch cannot cross projects or output modes.");
            }

            if ((allowHybridExeFsOutput && outputMutations.Count > 0)
                || isolateTrinityModManagerRomFs)
            {
                throw new InvalidOperationException(
                    "Hybrid or isolated Pokemon Legends Z-A outputs cannot join a normal mixed-domain batch.");
            }

            if (revalidateReviewedState is not null && !revalidateReviewedState())
            {
                throw new OutputReviewStateConflictException();
            }

            foreach (var write in writes)
            {
                EnsureReviewedTarget(ToOutputRelativePath(write.VirtualPath, outputMode));
                romFsMutations[write.VirtualPath] = new DeferredMutation(
                    write.Bytes.ToArray(),
                    DeleteFallbackBytes: null,
                    write.ApplyContext ?? operationContext);
            }

            foreach (var delete in deletes)
            {
                EnsureReviewedTarget(ToOutputRelativePath(delete, outputMode));
                romFsMutations[delete] = new DeferredMutation(
                    Bytes: null,
                    DeleteFallbackBytes: null,
                    operationContext);
            }

            foreach (var mutation in outputMutations)
            {
                EnsureReviewedTarget(mutation.RelativePath);
                standaloneOutputMutations[mutation.RelativePath] = new DeferredMutation(
                    mutation.Bytes?.ToArray(),
                    mutation.DeleteFallbackBytes?.ToArray(),
                    mutation.ApplyContext ?? operationContext);
            }

            if (outputMode == ZaOutputMode.Standalone && (writes.Count > 0 || deletes.Count > 0))
            {
                EnsureReviewedTarget(ToOutputRelativePath(
                    DescriptorVirtualPath,
                    ZaOutputMode.Standalone));
            }

            deleteStandaloneDescriptorRequested |= deleteStandaloneDescriptor;
        }

        internal OutputApplyResult? Commit()
        {
            ThrowIfUnavailable();
            if (romFsMutations.Count == 0 && standaloneOutputMutations.Count == 0)
            {
                throw new InvalidOperationException(
                    "A Pokemon Legends Z-A deferred output batch has no prepared mutations.");
            }

            IsCommitting = true;
            try
            {
                var writes = romFsMutations
                    .Where(entry => entry.Value.Bytes is not null)
                    .Select(entry => new ZaWorkflowFileWrite(
                        entry.Key,
                        entry.Value.Bytes!,
                        entry.Value.ApplyContext))
                    .ToArray();
                var deletes = romFsMutations
                    .Where(entry => entry.Value.Bytes is null)
                    .Select(entry => entry.Key)
                    .ToArray();
                var outputMutations = standaloneOutputMutations
                    .Select(entry => new ZaStandaloneOutputMutation(
                        entry.Key,
                        entry.Value.Bytes,
                        entry.Value.DeleteFallbackBytes,
                        entry.Value.ApplyContext))
                    .ToArray();
                var result = ApplyBatchCoreLocked(
                    paths,
                    writes,
                    deletes,
                    outputMutations,
                    OutputMode,
                    reviewedStandaloneDescriptorBytes: null,
                    deleteStandaloneDescriptor: deleteStandaloneDescriptorRequested
                        && writes.Length == 0,
                    allowHybridExeFsOutput: outputMutations.Length > 0,
                    isolateTrinityModManagerRomFs: false,
                    applyContext: applyContext,
                    revalidateReviewedState: RevalidateReviewedOutputState);
                committed = true;
                return result;
            }
            finally
            {
                IsCommitting = false;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (ReferenceEquals(activeDeferredOutputBatch, this))
            {
                activeDeferredOutputBatch = null;
            }
        }

        private bool RevalidateReviewedOutputState()
        {
            long capturedBytes = 0;
            foreach (var (path, reviewedState) in reviewedTargetStates)
            {
                var remainingBytes = coordinatorOptions.MaximumBackupBytesPerApply - capturedBytes;
                var currentState = CaptureOutputFileState(
                    path,
                    Math.Min(coordinatorOptions.MaximumFingerprintFileBytes, remainingBytes));
                capturedBytes = checked(capturedBytes + currentState.LengthBytes);
                if (currentState != reviewedState)
                {
                    return false;
                }
            }

            return reviewedStandaloneMembership is null
                || CaptureStandaloneRomFsMembership(paths).Revision
                    == reviewedStandaloneMembership.Revision;
        }

        private void EnsureReviewedTarget(string relativePath)
        {
            var normalized = NormalizeStandaloneOutputRelativePath(relativePath);
            if (!reviewedTargetRelativePaths.Contains(normalized))
            {
                throw new OutputReviewStateConflictException();
            }
        }

        private void ThrowIfUnavailable()
        {
            if (disposed || committed || !ReferenceEquals(activeDeferredOutputBatch, this))
            {
                throw new InvalidOperationException(
                    "The Pokemon Legends Z-A deferred output batch is no longer active.");
            }
        }

        private sealed record DeferredMutation(
            byte[]? Bytes,
            byte[]? DeleteFallbackBytes,
            ZaOutputApplyContext? ApplyContext);
    }

    private sealed class FreshReadScope : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (freshReadScopeDepth <= 0)
            {
                throw new InvalidOperationException("The Z-A fresh-read scope is unbalanced.");
            }

            freshReadScopeDepth--;
        }
    }

    private sealed record LooseOutputCandidate(
        string Path,
        bool IsStandalone,
        int Priority);

    private readonly record struct BoundedReadMemoKey(
        ProjectId ProjectId,
        bool BaseOnly,
        string VirtualPath);

    private sealed record BoundedReadMemoEntry(
        ZaWorkflowFile? Result,
        ExceptionDispatchInfo? Failure);
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
    byte[] Bytes,
    ZaOutputApplyContext? ApplyContext = null);

internal sealed record ZaStandaloneOutputMutation(
    string RelativePath,
    byte[]? Bytes,
    byte[]? DeleteFallbackBytes = null,
    ZaOutputApplyContext? ApplyContext = null);

internal sealed record ZaStandaloneMixedBatch(
    IReadOnlyList<ZaWorkflowFileWrite> RomFsWrites,
    IReadOnlyList<string> RomFsDeletes,
    IReadOnlyList<ZaStandaloneOutputMutation> OutputMutations,
    byte[]? ReviewedStandaloneDescriptorBytes = null,
    bool DeleteStandaloneDescriptor = false);

internal sealed record ZaWorkflowOutputMutation(
    string TargetPath,
    byte[]? Bytes,
    byte[]? DeleteFallbackBytes,
    ZaOutputApplyContext? ApplyContext = null);

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
