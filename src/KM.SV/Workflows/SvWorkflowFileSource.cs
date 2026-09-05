// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Output;
using KM.Core.Projects;
using KM.Core.Semantics;
using KM.Formats.SV;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace KM.SV.Workflows;

internal sealed class SvWorkflowFileSource
{
    public const string DescriptorVirtualPath = SvTrinityDescriptorPatcher.DescriptorVirtualPath;
    private const int MaximumBoundedArchiveIndexBytes = 64 * 1024 * 1024;
    private const long MaximumBoundedArchivePackBytes = 128L * 1024L * 1024L;
    private const int MaximumBoundedTableRecords = 50_000;
    private const int MaximumBoundedNestedRecords = 100_000;

    internal static object OutputWriteSyncRoot { get; } = new();
    private static readonly ConcurrentDictionary<string, SvOutputRootLockGate> OutputRootLocks = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    [ThreadStatic]
    private static DeferredOutputBatch? activeDeferredOutputBatch;
    [ThreadStatic]
    private static FreshReadContext? activeFreshReadContext;

    internal static bool HasActiveDeferredOutputBatch => activeDeferredOutputBatch is not null;

    private readonly SvCacheManager cacheManager;
    private readonly bool bypassReusableBaseCache;
    private readonly int? maximumReadBytes;
    private readonly int? maximumReadCount;
    private readonly long? maximumAggregateReadBytes;
    private readonly FreshArchiveSource? freshBaseArchiveSource;
    private readonly FreshArchiveSource? freshOutputArchiveSource;
    private int boundedReadCount;
    private long boundedReadBytes;

    public SvWorkflowFileSource(
        SvCacheManager? cacheManager = null,
        bool bypassReusableBaseCache = false,
        int? maximumReadBytes = null,
        int? maximumReadCount = null,
        long? maximumAggregateReadBytes = null)
        : this(
            cacheManager,
            bypassReusableBaseCache,
            maximumReadBytes,
            maximumReadCount,
            maximumAggregateReadBytes,
            freshBaseArchiveSource: null,
            freshOutputArchiveSource: null)
    {
    }

    private SvWorkflowFileSource(
        SvCacheManager? cacheManager,
        bool bypassReusableBaseCache,
        int? maximumReadBytes,
        int? maximumReadCount,
        long? maximumAggregateReadBytes,
        FreshArchiveSource? freshBaseArchiveSource,
        FreshArchiveSource? freshOutputArchiveSource)
    {
        if (maximumReadBytes is <= 0
            || maximumReadCount is <= 0
            || maximumAggregateReadBytes is <= 0
            || (maximumReadCount is null) != (maximumAggregateReadBytes is null)
            || maximumReadCount is not null && maximumReadBytes is null)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumReadBytes), "The bounded read budget is invalid.");
        }

        this.cacheManager = cacheManager ?? new SvCacheManager();
        this.bypassReusableBaseCache = bypassReusableBaseCache;
        this.maximumReadBytes = maximumReadBytes;
        this.maximumReadCount = maximumReadCount;
        this.maximumAggregateReadBytes = maximumAggregateReadBytes;
        this.freshBaseArchiveSource = freshBaseArchiveSource;
        this.freshOutputArchiveSource = freshOutputArchiveSource;
    }

    internal static FreshSemanticReaderPool CreateFreshSemanticReaderPool(
        SvCacheManager cacheManager,
        ProjectPaths paths,
        int maximumReadBytes,
        int readerCount)
    {
        ArgumentNullException.ThrowIfNull(cacheManager);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumReadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(readerCount);

        var archiveSnapshots = new Dictionary<string, FreshArchiveSnapshot>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var baseSnapshot = CaptureFreshArchiveSnapshot(paths.BaseRomFsPath, archiveSnapshots);
        var outputSnapshot = CaptureFreshArchiveSnapshot(paths.OutputRootPath, archiveSnapshots);
        var baseSources = OpenFreshArchiveSources(
            baseSnapshot,
            paths.ScarletVioletSupportFolderPath,
            readerCount);
        var outputSources = OpenFreshArchiveSources(
            outputSnapshot,
            paths.ScarletVioletSupportFolderPath,
            readerCount);
        var readers = new SvWorkflowFileSource[readerCount];
        for (var index = 0; index < readers.Length; index++)
        {
            readers[index] = new SvWorkflowFileSource(
                cacheManager,
                bypassReusableBaseCache: true,
                maximumReadBytes,
                maximumReadCount: null,
                maximumAggregateReadBytes: null,
                baseSources[index],
                outputSources[index]);
        }

        return new FreshSemanticReaderPool(
            readers,
            baseSources,
            outputSources,
            baseSnapshot.IndexBuildCount,
            outputSnapshot.IndexBuildCount);
    }

    internal int? BoundedTableRecordLimit => maximumReadBytes is null
        ? null
        : MaximumBoundedTableRecords;

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

    public SvWorkflowFile Read(OpenedProject project, string virtualRomFsPath)
    {
        EnsureBoundedReadAvailable();
        SvWorkflowFile result;
        try
        {
            result = ReadCore(project, virtualRomFsPath);
        }
        catch (Exception exception)
        {
            ObserveFailedBoundedRead(exception);
            throw;
        }

        ObserveBoundedRead(result.Bytes.Length);
        return result;
    }

    private SvWorkflowFile ReadCore(OpenedProject project, string virtualRomFsPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);

        var normalizedVirtualPath = NormalizeVirtualPath(virtualRomFsPath);
        var relativePath = ToRelativePath(normalizedVirtualPath);
        var entry = FindEntry(project, relativePath);
        var suppressLayeredOutput = false;
        if (activeDeferredOutputBatch is { IsCommitting: false } deferredBatch
            && deferredBatch.Matches(project.Paths)
            && deferredBatch.TryGetRomFsMutation(normalizedVirtualPath, out var deferredBytes))
        {
            if (deferredBytes is not null)
            {
                return new SvWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    deferredBytes.ToArray(),
                    ProjectFileLayer.Layered,
                    ProjectFileGraphEntryState.LayeredOverride);
            }

            suppressLayeredOutput = true;
        }

        if (!suppressLayeredOutput
            && !string.IsNullOrWhiteSpace(project.Paths.OutputRootPath))
        {
            var trinityModManagerPath = CombineGraphPath(project.Paths.OutputRootPath, normalizedVirtualPath);
            var standalonePath = CombineGraphPath(project.Paths.OutputRootPath, relativePath);
            (string Path, bool IsStandalone)? looseOutput;
            try
            {
                looseOutput = SelectLatestLooseOutput(trinityModManagerPath, standalonePath);
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
                return new SvWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    ReadAllBytes(
                        looseOutput.Value.Path,
                        relativePath,
                        ProjectFileLayer.Layered,
                        state),
                    ProjectFileLayer.Layered,
                    state);
            }

            if (TryReadOutputArchive(
                project.Paths,
                normalizedVirtualPath,
                relativePath,
                out var layeredArchiveBytes))
            {
                return new SvWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    layeredArchiveBytes,
                    ProjectFileLayer.Layered,
                    ProjectFileGraphEntryState.LayeredOverride);
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
                return new SvWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    ReadAllBytes(
                        looseBasePath,
                        relativePath,
                        ProjectFileLayer.Base,
                        entry?.State),
                    ProjectFileLayer.Base,
                    entry?.State ?? ProjectFileGraphEntryState.BaseOnly);
            }

            try
            {
                var archiveBytes = bypassReusableBaseCache || activeFreshReadContext is not null
                    ? ReadBaseBytesFresh(project.Paths, normalizedVirtualPath)
                    : cacheManager.ReadBaseTrinityFile(project.Paths, normalizedVirtualPath);
                return new SvWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    archiveBytes,
                    ProjectFileLayer.Base,
                    entry?.State ?? ProjectFileGraphEntryState.BaseOnly);
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

    public SvWorkflowFile ReadBase(OpenedProject project, string virtualRomFsPath)
    {
        EnsureBoundedReadAvailable();
        SvWorkflowFile result;
        try
        {
            result = ReadBaseCore(project, virtualRomFsPath);
        }
        catch (Exception exception)
        {
            ObserveFailedBoundedRead(exception);
            throw;
        }

        ObserveBoundedRead(result.Bytes.Length);
        return result;
    }

    private SvWorkflowFile ReadBaseCore(OpenedProject project, string virtualRomFsPath)
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
                return new SvWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    ReadAllBytes(
                        looseBasePath,
                        relativePath,
                        ProjectFileLayer.Base,
                        entry?.State),
                    ProjectFileLayer.Base,
                    entry?.State ?? ProjectFileGraphEntryState.BaseOnly);
            }

            try
            {
                var archiveBytes = bypassReusableBaseCache || activeFreshReadContext is not null
                    ? ReadBaseBytesFresh(project.Paths, normalizedVirtualPath)
                    : cacheManager.ReadBaseTrinityFile(project.Paths, normalizedVirtualPath);
                return new SvWorkflowFile(
                    normalizedVirtualPath,
                    relativePath,
                    archiveBytes,
                    ProjectFileLayer.Base,
                    entry?.State ?? ProjectFileGraphEntryState.BaseOnly);
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
        return ReadCurrentSourceFreshCore(
            paths,
            virtualRomFsPath,
            observedBaseBytes: null,
            useObservedBase: false);
    }

    internal (byte[] Bytes, ProjectFileLayer Layer) ReadCurrentSourceFreshUsingObservedBase(
        ProjectPaths paths,
        string virtualRomFsPath,
        byte[]? observedBaseBytes)
    {
        return ReadCurrentSourceFreshCore(
            paths,
            virtualRomFsPath,
            observedBaseBytes,
            useObservedBase: true);
    }

    private (byte[] Bytes, ProjectFileLayer Layer) ReadCurrentSourceFreshCore(
        ProjectPaths paths,
        string virtualRomFsPath,
        byte[]? observedBaseBytes,
        bool useObservedBase)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);

        var normalizedVirtualPath = NormalizeVirtualPath(virtualRomFsPath);
        var relativePath = ToRelativePath(normalizedVirtualPath);
        var suppressLayeredOutput = false;
        if (activeDeferredOutputBatch is { IsCommitting: false } deferredBatch
            && deferredBatch.Matches(paths)
            && deferredBatch.TryGetRomFsMutation(normalizedVirtualPath, out var deferredBytes))
        {
            if (deferredBytes is not null)
            {
                return (deferredBytes.ToArray(), ProjectFileLayer.Layered);
            }

            suppressLayeredOutput = true;
        }

        if (!suppressLayeredOutput && !string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            var trinityModManagerPath = CombineGraphPath(paths.OutputRootPath, normalizedVirtualPath);
            var standalonePath = CombineGraphPath(paths.OutputRootPath, relativePath);
            var looseOutput = SelectLatestLooseOutput(trinityModManagerPath, standalonePath);
            if (looseOutput is not null)
            {
                return (
                    ReadAllBytes(
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

        if (useObservedBase)
        {
            return observedBaseBytes is not null
                ? (observedBaseBytes, ProjectFileLayer.Base)
                : throw CreateReadFailure(
                    relativePath,
                    ProjectFileLayer.Base,
                    state: null,
                    exception: new FileNotFoundException());
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
            return ReadAllBytes(
                looseBasePath,
                relativePath,
                ProjectFileLayer.Base,
                ProjectFileGraphEntryState.BaseOnly);
        }

        try
        {
            if (ResolveFreshBaseArchiveSource(paths) is { } freshArchiveSource)
            {
                freshArchiveSource.Failure?.Throw();
                var retainedArchive = freshArchiveSource.Archive
                    ?? throw new FileNotFoundException();
                return maximumReadBytes is { } retainedLimit
                    ? retainedArchive.ReadFile(normalizedVirtualPath, retainedLimit)
                    : retainedArchive.ReadFile(normalizedVirtualPath);
            }

            using var archive = OpenArchive(
                paths.BaseRomFsPath,
                paths.ScarletVioletSupportFolderPath);
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
            if (ResolveFreshBaseArchiveSource(project.Paths) is { } freshArchiveSource)
            {
                freshArchiveSource.Failure?.Throw();
                return freshArchiveSource.Archive?.ContainsFile(normalizedVirtualPath) == true;
            }

            if (bypassReusableBaseCache)
            {
                using var archive = OpenArchive(
                    project.Paths.BaseRomFsPath,
                    project.Paths.ScarletVioletSupportFolderPath);
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
            if (ResolveFreshBaseArchiveSource(project.Paths) is { } freshArchiveSource)
            {
                freshArchiveSource.Failure?.Throw();
                return freshArchiveSource.Index?.Files
                    .Select(file => file.PackName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                    ?? [];
            }

            if (bypassReusableBaseCache)
            {
                return SvTrinityArchive.BuildIndex(project.Paths.BaseRomFsPath!)
                    .Files
                    .Select(file => file.PackName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            return cacheManager.ListBaseTrinityPackNames(project.Paths);
        }
        catch (Exception exception) when (exception is FileNotFoundException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public SvWorkflowArchiveInventory? GetOutputArchiveInventory(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var outputRootPath = project.Paths.OutputRootPath;
        if (string.IsNullOrWhiteSpace(outputRootPath))
        {
            return null;
        }

        try
        {
            var freshArchiveSource = ResolveFreshOutputArchiveSource(project.Paths);
            SvTrinityArchiveIndex index;
            if (freshArchiveSource is not null)
            {
                freshArchiveSource.Failure?.Throw();
                if (freshArchiveSource.Index is not { } retainedIndex)
                {
                    return null;
                }

                index = retainedIndex;
            }
            else
            {
                if (!HasTrinityArchive(outputRootPath))
                {
                    return null;
                }

                index = SvTrinityArchive.BuildIndex(outputRootPath);
            }

            return new SvWorkflowArchiveInventory(
                index.Files
                    .Select(file => file.PackName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                index.Files.Select(file => file.FileHash).ToHashSet());
        }
        catch (Exception exception) when (IsContextualFileFailure(exception))
        {
            throw CreateReadFailure(
                "romfs/arc/data.trpfd",
                ProjectFileLayer.Layered,
                state: null,
                exception: exception,
                operation: ProjectFileOperation.Inspect);
        }
    }

    public static ProjectFileReference CreateReference(SvWorkflowFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new ProjectFileReference(file.SourceLayer, file.RelativePath);
    }

    public static string ResolveOutputPath(
        ProjectPaths paths,
        string virtualRomFsPath,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRomFsPath);

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException("Set an output root before applying Scarlet/Violet edits.");
        }

        var targetRelativePath = ToOutputRelativePath(NormalizeVirtualPath(virtualRomFsPath), outputMode);
        if (Path.IsPathRooted(targetRelativePath))
        {
            throw new InvalidOperationException($"Scarlet/Violet target path '{targetRelativePath}' must be relative.");
        }

        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(outputRoot, targetRelativePath));
        var pathFromOutputRoot = Path.GetRelativePath(outputRoot, targetPath);
        if (PathContainment.IsOutsideRoot(pathFromOutputRoot))
        {
            throw new InvalidOperationException($"Scarlet/Violet target path '{targetRelativePath}' escapes the output root.");
        }

        return targetPath;
    }

    internal static IDisposable BeginFreshReadScope(ProjectPaths paths)
    {
        return BeginFreshReadScope(paths, requireIndependentSnapshot: false);
    }

    internal static IDisposable BeginIndependentFreshReadScope(ProjectPaths paths)
    {
        return BeginFreshReadScope(paths, requireIndependentSnapshot: true);
    }

    private static IDisposable BeginFreshReadScope(
        ProjectPaths paths,
        bool requireIndependentSnapshot)
    {
        ArgumentNullException.ThrowIfNull(paths);

        // A single review/apply phase may cross several editor-domain guards.
        // Share one immutable archive/index snapshot across compatible nested
        // work, but let the output-boundary check explicitly request a fresh
        // snapshot before any mutation is promoted.
        if (!requireIndependentSnapshot
            && activeFreshReadContext?.Matches(paths) == true)
        {
            activeFreshReadContext.Retain();
            return new FreshReadScope(activeFreshReadContext);
        }

        var context = FreshReadContext.Create(paths, activeFreshReadContext);
        activeFreshReadContext = context;
        return new FreshReadScope(context);
    }

    public static PlannedWriteInfo CreatePlannedWrite(
        ProjectPaths paths,
        string virtualRomFsPath,
        IReadOnlyList<ProjectFileReference> sources,
        SvOutputMode outputMode = SvOutputMode.Standalone)
    {
        var targetRelativePath = ToOutputRelativePath(NormalizeVirtualPath(virtualRomFsPath), outputMode);
        var targetPath = ResolveOutputPath(paths, virtualRomFsPath, outputMode);

        return new PlannedWriteInfo(
            targetRelativePath,
            sources,
            File.Exists(targetPath));
    }

    public static OutputApplyResult? Write(
        ProjectPaths paths,
        string virtualRomFsPath,
        byte[] bytes,
        SvOutputMode outputMode = SvOutputMode.Standalone,
        SvOutputApplyContext? applyContext = null,
        Func<bool>? revalidateReviewedState = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return WriteBatch(
            paths,
            [new SvWorkflowFileWrite(virtualRomFsPath, bytes, applyContext)],
            outputMode,
            applyContext,
            revalidateReviewedState);
    }

    internal static OutputApplyResult? WriteBatch(
        ProjectPaths paths,
        IReadOnlyList<SvWorkflowFileWrite> writes,
        SvOutputMode outputMode = SvOutputMode.Standalone,
        SvOutputApplyContext? applyContext = null,
        Func<bool>? revalidateReviewedState = null)
    {
        return ApplyBatchCore(
            paths,
            writes,
            Array.Empty<string>(),
            Array.Empty<SvStandaloneOutputMutation>(),
            outputMode,
            applyContext,
            revalidateReviewedState);
    }

    internal static byte[] ReadOutputBytesForVerification(
        ProjectPaths paths,
        string virtualRomFsPath,
        SvOutputMode outputMode)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var normalizedVirtualPath = NormalizeVirtualPath(virtualRomFsPath);
        if (activeDeferredOutputBatch is { IsCommitting: false } deferredBatch
            && deferredBatch.Matches(paths)
            && deferredBatch.OutputMode == outputMode
            && deferredBatch.TryGetRomFsMutation(normalizedVirtualPath, out var deferredBytes))
        {
            return deferredBytes?.ToArray()
                ?? throw new FileNotFoundException(
                    "The staged Scarlet/Violet output target is deleted.");
        }

        return File.ReadAllBytes(ResolveOutputPath(paths, normalizedVirtualPath, outputMode));
    }

    internal static bool IsDeferredOutputBatchActive(
        ProjectPaths paths,
        SvOutputMode outputMode)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return activeDeferredOutputBatch is { IsCommitting: false } deferredBatch
            && deferredBatch.Matches(paths)
            && deferredBatch.OutputMode == outputMode;
    }

    internal static OutputApplyResult? ApplyStandaloneOutputBatch(
        ProjectPaths paths,
        IReadOnlyList<SvStandaloneOutputMutation> outputMutations,
        SvOutputApplyContext applyContext,
        Func<bool>? revalidateReviewedState = null)
    {
        ArgumentNullException.ThrowIfNull(applyContext);
        return ApplyBatchCore(
            paths,
            Array.Empty<SvWorkflowFileWrite>(),
            Array.Empty<string>(),
            outputMutations,
            SvOutputMode.Standalone,
            applyContext,
            revalidateReviewedState);
    }

    internal static OutputApplyResult? ApplyStandaloneRomFsReplacementBatch(
        ProjectPaths paths,
        IReadOnlyList<SvWorkflowFileWrite> writes,
        IReadOnlyList<string> deletes,
        SvOutputApplyContext applyContext,
        Func<bool>? revalidateReviewedState = null)
    {
        ArgumentNullException.ThrowIfNull(applyContext);
        return ApplyBatchCore(
            paths,
            writes,
            deletes,
            Array.Empty<SvStandaloneOutputMutation>(),
            SvOutputMode.Standalone,
            applyContext,
            revalidateReviewedState);
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
                "Scarlet/Violet owned output inspection requires an output root.");
        }

        using var outputLock = AcquireOutputLock(paths);
        var coordinator = OutputTransactionCoordinator.ForProject(paths);
        var projectId = ProjectIdentity.FromPaths(paths);
        var inventory = coordinator.GetOwnershipInventoryAsync().GetAwaiter().GetResult();
        const string romFsPrefix = "romfs/";
        return inventory.Files
            .Where(record => record.ProjectId == projectId
                && record.GameFamily == GameFamily.ScarletViolet
                && string.Equals(record.OutputMode, ToOutputModeKey(SvOutputMode.Standalone), StringComparison.Ordinal)
                && record.Path.Value.StartsWith(romFsPrefix, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(record.Path.Value, ToRelativePath(DescriptorVirtualPath), StringComparison.OrdinalIgnoreCase)
                && record.Claims.Any(claim => claim.OwnerId == ownerId))
            .Select(record => NormalizeVirtualPath(record.Path.Value[romFsPrefix.Length..]))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    internal static DeferredOutputBatch BeginDeferredOutputBatch(
        ProjectPaths paths,
        SvOutputMode outputMode,
        ChangePlan reviewedPlan,
        SvOutputApplyContext applyContext)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(reviewedPlan);
        ArgumentNullException.ThrowIfNull(applyContext);
        if (activeDeferredOutputBatch is not null)
        {
            throw new InvalidOperationException(
                "A Scarlet/Violet deferred output batch is already active on this thread.");
        }

        var batch = new DeferredOutputBatch(paths, outputMode, reviewedPlan, applyContext);
        activeDeferredOutputBatch = batch;
        return batch;
    }

    private static OutputApplyResult? ApplyBatchCore(
        ProjectPaths paths,
        IReadOnlyList<SvWorkflowFileWrite> writes,
        IReadOnlyList<string> deletes,
        IReadOnlyList<SvStandaloneOutputMutation> outputMutations,
        SvOutputMode outputMode,
        SvOutputApplyContext? applyContext,
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
            applyContext,
            revalidateReviewedState);
    }

    private static OutputApplyResult? ApplyBatchCoreLocked(
        ProjectPaths paths,
        IReadOnlyList<SvWorkflowFileWrite> writes,
        IReadOnlyList<string> deletes,
        IReadOnlyList<SvStandaloneOutputMutation> outputMutations,
        SvOutputMode outputMode,
        SvOutputApplyContext? applyContext,
        Func<bool>? revalidateReviewedState)
    {
        ArgumentNullException.ThrowIfNull(writes);
        ArgumentNullException.ThrowIfNull(deletes);
        ArgumentNullException.ThrowIfNull(outputMutations);
        if (writes.Count == 0 && deletes.Count == 0 && outputMutations.Count == 0)
        {
            throw new ArgumentException(
                "A Scarlet/Violet output batch must contain at least one mutation.",
                nameof(writes));
        }

        if (outputMode != SvOutputMode.Standalone && outputMutations.Count > 0)
        {
            throw new ArgumentException(
                "Explicit output mutations require standalone output.",
                nameof(outputMutations));
        }

        var normalizedWrites = writes.Select(write =>
        {
            ArgumentNullException.ThrowIfNull(write);
            ArgumentException.ThrowIfNullOrWhiteSpace(write.VirtualPath);
            ArgumentNullException.ThrowIfNull(write.Bytes);
            var virtualPath = NormalizeVirtualPath(write.VirtualPath);
            if (string.Equals(virtualPath, DescriptorVirtualPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Scarlet/Violet workflow batches cannot write the descriptor directly.",
                    nameof(writes));
            }

            return new SvWorkflowFileWrite(
                virtualPath,
                write.Bytes.ToArray(),
                write.ApplyContext);
        }).ToArray();
        var normalizedDeletes = deletes.Select(delete =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(delete);
            var virtualPath = NormalizeVirtualPath(delete);
            if (string.Equals(virtualPath, DescriptorVirtualPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Scarlet/Violet workflow batches cannot delete the descriptor directly.",
                    nameof(deletes));
            }

            return virtualPath;
        }).ToArray();
        var normalizedOutputMutations = outputMutations.Select(mutation =>
        {
            ArgumentNullException.ThrowIfNull(mutation);
            var relativePath = NormalizeStandaloneOutputRelativePath(mutation.RelativePath);
            return new SvStandaloneOutputMutation(
                relativePath,
                mutation.Bytes?.ToArray(),
                mutation.DeleteFallbackBytes?.ToArray(),
                mutation.ApplyContext);
        }).ToArray();
        if (normalizedWrites.Select(write => write.VirtualPath)
            .Concat(normalizedDeletes)
            .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1)
            || normalizedOutputMutations
                .GroupBy(
                    mutation => mutation.RelativePath,
                    OperatingSystem.IsWindows()
                        ? StringComparer.OrdinalIgnoreCase
                        : StringComparer.Ordinal)
                .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "A Scarlet/Violet output batch contains duplicate or conflicting targets.",
                nameof(writes));
        }

        if (activeDeferredOutputBatch is { IsCommitting: false } deferredBatch)
        {
            if (normalizedOutputMutations.Length > 0)
            {
                throw new InvalidOperationException(
                    "Explicit output mutations cannot join a normal Scarlet/Violet deferred batch.");
            }

            deferredBatch.Stage(
                paths,
                outputMode,
                normalizedWrites,
                normalizedDeletes,
                applyContext,
                revalidateReviewedState);
            return null;
        }

        if (revalidateReviewedState is not null
            && !RevalidateReviewedState(paths, revalidateReviewedState))
        {
            throw new OutputReviewStateConflictException();
        }

        var mutations = normalizedWrites.Select(write => new SvWorkflowOutputMutation(
                ResolveOutputPath(paths, write.VirtualPath, outputMode),
                write.Bytes,
                DeleteFallbackBytes: null,
                write.ApplyContext ?? applyContext))
            .ToList();
        mutations.AddRange(normalizedDeletes.Select(delete => new SvWorkflowOutputMutation(
            ResolveOutputPath(paths, delete, outputMode),
            Bytes: null,
            DeleteFallbackBytes: null,
            applyContext)));
        mutations.AddRange(normalizedOutputMutations.Select(mutation => new SvWorkflowOutputMutation(
            ResolveStandaloneOutputPath(paths, mutation.RelativePath),
            mutation.Bytes,
            mutation.DeleteFallbackBytes,
            mutation.ApplyContext ?? applyContext)));

        OutputDirectoryMembershipSnapshot? standaloneRomFsMembership = null;
        if (outputMode == SvOutputMode.Standalone
            && (normalizedWrites.Length > 0 || normalizedDeletes.Length > 0))
        {
            standaloneRomFsMembership = CaptureStandaloneRomFsMembership(paths);
            var existingVirtualPaths = GetLayeredVirtualPaths(standaloneRomFsMembership);
            var deletedVirtualPaths = normalizedDeletes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var effectiveVirtualPaths = existingVirtualPaths
                .Where(path => !deletedVirtualPaths.Contains(path))
                .Concat(normalizedWrites.Select(write => write.VirtualPath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var descriptorBytes = CreateStandaloneDescriptorPreviewFromVirtualPaths(
                paths,
                effectiveVirtualPaths);
            mutations.Add(new SvWorkflowOutputMutation(
                ResolveOutputPath(paths, DescriptorVirtualPath, SvOutputMode.Standalone),
                descriptorBytes,
                DeleteFallbackBytes: null,
                applyContext));
        }

        var targetComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        if (mutations.GroupBy(
                mutation => Path.GetFullPath(mutation.TargetPath),
                targetComparer)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidDataException(
                "A Scarlet/Violet output batch contains duplicate resolved targets.");
        }

        return PromotePreparedMutations(
            paths,
            mutations,
            outputMode,
            applyContext,
            standaloneRomFsMembership is null
                ? null
                : [standaloneRomFsMembership.ToDependency()]);
    }

    private static bool RevalidateReviewedState(
        ProjectPaths paths,
        Func<bool> revalidateReviewedState)
    {
        try
        {
            using var freshReads = BeginIndependentFreshReadScope(paths);
            return revalidateReviewedState();
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
    }

    private static OutputApplyResult? PromotePreparedMutations(
        ProjectPaths paths,
        IReadOnlyList<SvWorkflowOutputMutation> mutations,
        SvOutputMode outputMode,
        SvOutputApplyContext? applyContext,
        IEnumerable<OutputDirectoryMembershipDependency>? directoryMembershipDependencies = null)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException("A Scarlet/Violet output batch requires an output root.");
        }

        if (!IsScarletViolet(paths.SelectedGame))
        {
            throw new InvalidOperationException(
                "Scarlet/Violet output requires a matching project game.");
        }

        var outputRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.OutputRootPath));
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
        var coordinator = OutputTransactionCoordinator.ForProject(paths, coordinatorOptions);
        var projectId = ProjectIdentity.FromPaths(paths);
        var outputModeKey = ToOutputModeKey(outputMode);
        var ownershipSnapshot = coordinator
            .GetOwnershipInventorySnapshotAsync()
            .GetAwaiter()
            .GetResult();
        var inventory = ownershipSnapshot.Inventory;
        var defaultOwnerId = applyContext?.OwnerId ?? new OwnershipOwnerId("workflow.sv.output");
        var defaultPreservationRule = applyContext?.PreservationRule
            ?? new PreservationRuleDescriptor(
                "sv.full-file-rebuild",
                schemaVersion: 1,
                preservesUnownedData: true,
                requiresPreimage: true);
        var outputMutations = new List<OutputMutation>(mutations.Count);
        long plannedWriteBytes = 0;
        long plannedBackupBytes = 0;
        foreach (var mutation in mutations)
        {
            var fullTargetPath = Path.GetFullPath(mutation.TargetPath);
            var relativePathValue = Path.GetRelativePath(outputRoot, fullTargetPath)
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
                    "A Scarlet/Violet output target exceeds the configured write limit.");
            }

            var remainingBackupBytes = coordinatorOptions.MaximumBackupBytesPerApply - plannedBackupBytes;
            var expectedPreimage = CaptureOutputFileState(
                fullTargetPath,
                Math.Min(coordinatorOptions.MaximumFingerprintFileBytes, remainingBackupBytes));
            var context = mutation.ApplyContext ?? applyContext;
            var ownership = new OwnedTarget(
                GameFamily.ScarletViolet,
                new OwnedTargetAddress(relativePath),
                context?.OwnerId ?? defaultOwnerId,
                context?.PreservationRule ?? defaultPreservationRule);
            var ownedRecord = inventory.Files.FirstOrDefault(record => record.Path == relativePath);
            var isComposedExecutable = IsComposedExecutablePath(relativePath);
            var ownershipClaims = new[] { ownership };
            if (isComposedExecutable && ownedRecord is not null)
            {
                ValidateComposedExecutableOwnership(
                    ownedRecord,
                    projectId,
                    GameFamily.ScarletViolet,
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
                var owned = isComposedExecutable
                    ? ownedRecord
                    : inventory.Files.FirstOrDefault(record =>
                        record.Path == relativePath
                        && record.ProjectId == projectId
                        && record.GameFamily == GameFamily.ScarletViolet);
                var remainingClaims = isComposedExecutable && owned is not null
                    ? owned.Claims.Where(claim => claim.OwnerId != ownership.OwnerId).ToArray()
                    : [];
                var activeRemainingClaims = remainingClaims
                    .Where(claim => !OutputCreatorProvenance.IsClaim(claim))
                    .ToArray();
                var canDelete = owned is not null
                    && (!isComposedExecutable || activeRemainingClaims.Length == 0
                        && remainingClaims.Length == 0)
                    && owned.FileDeleteEligible
                    && owned.Claims.Any(claim =>
                        claim.Address.ScopeKind == OwnedTargetScopeKind.File);
                if (canDelete)
                {
                    plannedBackupBytes = checked(plannedBackupBytes + expectedPreimage.LengthBytes);
                    outputMutations.Add(OutputMutation.Delete(
                        relativePath,
                        expectedPreimage,
                        isComposedExecutable ? owned!.Claims : [ownership],
                        outputModeKey));
                    EnsureMutationCountWithinLimit(outputMutations.Count, coordinatorOptions);
                    continue;
                }

                var canDeleteVerifiedBase = isComposedExecutable
                    && owned is not null
                    && activeRemainingClaims.Length == 0
                    && remainingClaims.Length > 0
                    && remainingClaims.All(OutputCreatorProvenance.IsClaim)
                    && owned.FileDeleteEligible
                    && mutation.DeleteFallbackBytes is { } verifiedFallback
                    && verifiedBaseMain is not null
                    && verifiedBaseMainState is not null
                    && verifiedFallback.AsSpan().SequenceEqual(verifiedBaseMain);
                if (canDeleteVerifiedBase)
                {
                    var authority = new OutputVerifiedBaseDeleteAuthority(
                        projectId,
                        GameFamily.ScarletViolet,
                        ownership.OwnerId,
                        outputModeKey,
                        relativePath,
                        expectedPreimage,
                        verifiedBaseMainState!,
                        owned!.Claims);
                    plannedBackupBytes = checked(plannedBackupBytes + expectedPreimage.LengthBytes);
                    outputMutations.Add(OutputMutation.DeleteVerifiedBase(
                        relativePath,
                        expectedPreimage,
                        owned.Claims,
                        authority));
                    EnsureMutationCountWithinLimit(outputMutations.Count, coordinatorOptions);
                    continue;
                }

                bytes = mutation.DeleteFallbackBytes
                    ?? throw new OutputOwnershipConflictException(relativePath);
                if (isComposedExecutable && remainingClaims.Length > 0)
                {
                    ownershipClaims = owned!.FileDeleteEligible
                        && !remainingClaims.Any(claim =>
                            claim.Address.ScopeKind == OwnedTargetScopeKind.File
                            && !OutputCreatorProvenance.IsClaim(claim))
                        ? remainingClaims
                            .Append(OutputCreatorProvenance.Create(
                                GameFamily.ScarletViolet,
                                relativePath))
                            .Distinct()
                            .ToArray()
                        : remainingClaims;
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
            var nextBackupBytes = checked(plannedBackupBytes + expectedPreimage.LengthBytes);
            if (nextWriteBytes > coordinatorOptions.MaximumWriteBytesPerApply)
            {
                throw new OutputLimitExceededException(
                    "The Scarlet/Violet output batch exceeds the configured write limit.");
            }

            if (nextBackupBytes > coordinatorOptions.MaximumBackupBytesPerApply)
            {
                throw new OutputLimitExceededException(
                    "The Scarlet/Violet output batch exceeds the configured backup limit.");
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

        var membershipDependencies = directoryMembershipDependencies?.ToArray() ?? [];
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

        var contextForPlan = applyContext ?? new SvOutputApplyContext(
            OutputReviewFingerprint.FromMutations(outputMutations),
            defaultOwnerId,
            [new OutputApplyOrigin(OutputApplyOriginKind.Workflow, "workflow.sv.output")]);
        var origins = mutations
            .Select(mutation => mutation.ApplyContext)
            .Where(context => context is not null)
            .SelectMany(context => context!.Origins)
            .Concat(contextForPlan.Origins)
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
            GameFamily.ScarletViolet,
            ToOutputModeKey(outputMode),
            contextForPlan.SemanticReviewHash,
            origins,
            outputMutations,
            directoryMembershipDependencies: membershipDependencies,
            ownershipInventoryRevision: ownershipSnapshot.Revision)
        {
            HistoryDetails = contextForPlan.HistoryDetails,
        };
        var result = coordinator.ApplyAsync(plan).GetAwaiter().GetResult();
        if (result.Outcome != OutputApplyOutcome.Committed)
        {
            throw new SvOutputApplyNotCommittedException(result);
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
        IReadOnlyList<SvWorkflowOutputMutation> mutations,
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
                "Composed Scarlet/Violet executable cleanup requires Base ExeFS main.");
        }

        var baseRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.BaseExeFsPath));
        var baseMainPath = Path.GetFullPath(Path.Combine(baseRoot, "main"));
        var relativeBasePath = Path.GetRelativePath(baseRoot, baseMainPath);
        if (PathContainment.IsOutsideRoot(relativeBasePath)
            || !File.Exists(baseMainPath)
            || Directory.Exists(baseMainPath))
        {
            throw new IOException(
                "Composed Scarlet/Violet executable cleanup requires a physical Base ExeFS main.");
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
                "Base ExeFS main exceeds the configured Scarlet/Violet executable limit.");
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

    private static void EnsureMutationCountWithinLimit(
        int mutationCount,
        OutputTransactionCoordinatorOptions options)
    {
        if (mutationCount > options.MaximumMutationsPerApply)
        {
            throw new OutputLimitExceededException(
                "The Scarlet/Violet output batch contains too many targets.");
        }
    }

    private static OutputFileState CaptureOutputFileState(string targetPath, long maximumBytes)
    {
        if (Directory.Exists(targetPath))
        {
            throw new IOException("A Scarlet/Violet output target is a directory.");
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
                "A Scarlet/Violet output preimage exceeds the configured backup limit.");
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
                    "A Scarlet/Violet output preimage changed while it was reviewed.");
            }

            hasher.AppendData(buffer, 0, bytesRead);
            remaining -= bytesRead;
        }

        if (stream.ReadByte() != -1)
        {
            throw new IOException(
                "A Scarlet/Violet output preimage changed while it was reviewed.");
        }

        return OutputFileState.Existing(
            Convert.ToHexStringLower(hasher.GetHashAndReset()),
            length);
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
        catch (Exception exception) when (exception is
            ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            lockKey = $"<invalid>:{outputRoot}";
        }

        var gate = ReserveOutputRootGate(lockKey);
        var lockTaken = false;
        Mutex? processMutex = null;
        try
        {
            Monitor.Enter(gate.SyncRoot, ref lockTaken);
            processMutex = new Mutex(initiallyOwned: false, CreateOutputMutexName(lockKey));
            try
            {
                if (!processMutex.WaitOne(TimeSpan.FromSeconds(30)))
                {
                    throw new IOException(
                        "Another KM Editor process is still writing to this Scarlet/Violet output root.");
                }
            }
            catch (AbandonedMutexException)
            {
            }

            return new SvOutputRootLock(gate, processMutex);
        }
        catch
        {
            processMutex?.Dispose();
            if (lockTaken)
            {
                Monitor.Exit(gate.SyncRoot);
            }

            ReleaseOutputRootGate(gate);
            throw;
        }
    }

    private static SvOutputRootLockGate ReserveOutputRootGate(string lockKey)
    {
        while (true)
        {
            var gate = OutputRootLocks.GetOrAdd(
                lockKey,
                static key => new SvOutputRootLockGate(key));
            if (gate.TryAddReference())
            {
                return gate;
            }

            RemoveOutputRootGate(gate);
            Thread.Yield();
        }
    }

    internal static void ReleaseOutputRootGate(SvOutputRootLockGate gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        if (gate.ReleaseReference())
        {
            RemoveOutputRootGate(gate);
        }
    }

    private static void RemoveOutputRootGate(SvOutputRootLockGate gate)
    {
        ((ICollection<KeyValuePair<string, SvOutputRootLockGate>>)OutputRootLocks).Remove(
            new KeyValuePair<string, SvOutputRootLockGate>(gate.LockKey, gate));
    }

    private static string CreateOutputMutexName(string lockKey)
    {
        var keyBytes = Encoding.UTF8.GetBytes(lockKey);
        return $"KMEditor.SV.Output.{Convert.ToHexString(SHA256.HashData(keyBytes))}";
    }

    private static OutputDirectoryMembershipSnapshot CaptureStandaloneRomFsMembership(
        ProjectPaths paths)
    {
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException(
                "Scarlet/Violet output membership requires an output root.");
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

    private static byte[] CreateStandaloneDescriptorPreviewFromVirtualPaths(
        ProjectPaths paths,
        IEnumerable<string> effectiveVirtualPaths)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            throw new InvalidOperationException(
                "Scarlet/Violet descriptor preview requires a base RomFS path.");
        }

        return SvTrinityDescriptorPatcher.CreateLayeredDescriptorFromVirtualPaths(
            paths.BaseRomFsPath,
            effectiveVirtualPaths);
    }

    internal static string ResolveStandaloneOutputPath(ProjectPaths paths, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException("Set an output root before writing Scarlet/Violet files.");
        }

        var normalized = NormalizeStandaloneOutputRelativePath(relativePath);
        var outputRoot = Path.GetFullPath(paths.OutputRootPath);
        var targetPath = Path.GetFullPath(Path.Combine(
            outputRoot,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (PathContainment.IsOutsideRoot(Path.GetRelativePath(outputRoot, targetPath))
            || !OutputMetadataNamespace.IsSafePayloadDestinationPath(targetPath))
        {
            throw new OutputPathSecurityException();
        }

        return targetPath;
    }

    private static string NormalizeStandaloneOutputRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException(
                "A Scarlet/Violet output path must be relative.",
                nameof(relativePath));
        }

        var normalized = new RelativeOutputPath(relativePath).Value;
        if (OutputMetadataNamespace.ContainsReservedSegment(normalized))
        {
            throw new OutputPathSecurityException();
        }

        return normalized;
    }

    private static string ToOutputModeKey(SvOutputMode outputMode)
    {
        return outputMode switch
        {
            SvOutputMode.Standalone => "sv.standalone",
            SvOutputMode.TrinityModManager => "sv.trinity-mod-manager",
            SvOutputMode.TrinityBypass => "sv.trinity-bypass",
            _ => throw new ArgumentOutOfRangeException(nameof(outputMode), outputMode, null),
        };
    }

    public static PlannedWriteInfo CreateDescriptorPlannedWrite(ProjectPaths paths)
    {
        var sources = new[]
        {
            new ProjectFileReference(ProjectFileLayer.Base, ToRelativePath(DescriptorVirtualPath)),
        };
        return CreatePlannedWrite(paths, DescriptorVirtualPath, sources, SvOutputMode.Standalone);
    }

    internal static byte[] CreateStandaloneDescriptorPreview(
        ProjectPaths paths,
        IEnumerable<string> plannedVirtualPaths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(plannedVirtualPaths);
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath))
        {
            throw new InvalidOperationException(
                "Scarlet/Violet descriptor preview requires a base RomFS path.");
        }

        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            throw new InvalidOperationException(
                "Scarlet/Violet descriptor preview requires an output root.");
        }

        return SvTrinityDescriptorPatcher.CreateLayeredDescriptorIncludingVirtualPaths(
            paths.BaseRomFsPath,
            paths.OutputRootPath,
            plannedVirtualPaths);
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
            Domain: "sv.editor",
            Field: field,
            Expected: expected);
    }

    public static bool IsScarletViolet(ProjectGame? game)
    {
        return game is ProjectGame.Scarlet or ProjectGame.Violet;
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

    private static string ToOutputRelativePath(string normalizedVirtualPath, SvOutputMode outputMode)
    {
        return outputMode switch
        {
            SvOutputMode.Standalone => ToRelativePath(normalizedVirtualPath),
            SvOutputMode.TrinityModManager => normalizedVirtualPath,
            SvOutputMode.TrinityBypass => ToRelativePath(normalizedVirtualPath),
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

        return normalized;
    }

    private static string CombineGraphPath(string rootPath, string relativePath)
    {
        return Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    internal static (string Path, bool IsStandalone)? SelectLatestLooseOutput(
        string trinityModManagerPath,
        string standalonePath)
    {
        var trinityModManagerExists = FileExistsForInspection(trinityModManagerPath);
        var standaloneExists = FileExistsForInspection(standalonePath);
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

            if (ResolveFreshOutputArchiveSource(paths) is { } freshArchiveSource)
            {
                freshArchiveSource.Failure?.Throw();
                if (freshArchiveSource.Archive is not { } retainedArchive)
                {
                    return false;
                }

                return maximumReadBytes is { } retainedLimit
                    ? retainedArchive.TryReadFile(virtualPath, retainedLimit, out bytes)
                    : retainedArchive.TryReadFile(virtualPath, out bytes);
            }

            if (!HasTrinityArchive(outputRootPath))
            {
                return false;
            }

            using var archive = OpenArchive(
                outputRootPath,
                paths.ScarletVioletSupportFolderPath);
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
            if (string.IsNullOrWhiteSpace(outputRootPath))
            {
                return false;
            }

            if (ResolveFreshOutputArchiveSource(paths) is { } freshArchiveSource)
            {
                freshArchiveSource.Failure?.Throw();
                return freshArchiveSource.Archive?.ContainsFile(virtualPath) == true;
            }

            if (!HasTrinityArchive(outputRootPath))
            {
                return false;
            }

            using var archive = OpenArchive(
                outputRootPath,
                paths.ScarletVioletSupportFolderPath);
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

    private byte[] ReadAllBytes(
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

    private SvTrinityArchive OpenArchive(string rootPath, string? supportFolderPath)
    {
        return maximumReadBytes is null
            ? SvTrinityArchive.Open(rootPath, supportFolderPath)
            : SvTrinityArchive.Open(
                rootPath,
                supportFolderPath,
                maximumIndexBytes: MaximumBoundedArchiveIndexBytes,
                maximumPackBytes: MaximumBoundedArchivePackBytes);
    }

    private FreshArchiveSource? ResolveFreshBaseArchiveSource(ProjectPaths paths)
    {
        return freshBaseArchiveSource
            ?? activeFreshReadContext?.GetBaseSource(paths);
    }

    private FreshArchiveSource? ResolveFreshOutputArchiveSource(ProjectPaths paths)
    {
        return freshOutputArchiveSource
            ?? activeFreshReadContext?.GetOutputSource(paths);
    }

    private static FreshArchiveSnapshot CaptureFreshArchiveSnapshot(
        string? rootPath,
        IDictionary<string, FreshArchiveSnapshot> archiveSnapshots,
        int? maximumIndexBytes = MaximumBoundedArchiveIndexBytes)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return FreshArchiveSnapshot.Missing;
        }

        string? archiveRootPath = null;
        var indexBuildAttempted = false;
        try
        {
            archiveRootPath = ResolveFreshArchiveRootPath(rootPath);
            if (archiveRootPath is null)
            {
                return FreshArchiveSnapshot.Missing;
            }

            if (archiveSnapshots.TryGetValue(archiveRootPath, out var existing))
            {
                return existing with { IndexBuildCount = 0 };
            }

            indexBuildAttempted = true;
            var snapshot = new FreshArchiveSnapshot(
                archiveRootPath,
                maximumIndexBytes is { } indexLimit
                    ? SvTrinityArchive.BuildIndex(archiveRootPath, indexLimit)
                    : SvTrinityArchive.BuildIndex(archiveRootPath),
                Failure: null,
                IndexBuildCount: 1);
            archiveSnapshots.Add(archiveRootPath, snapshot);
            return snapshot;
        }
        catch (Exception exception) when (IsContextualFileFailure(exception))
        {
            var snapshot = new FreshArchiveSnapshot(
                archiveRootPath ?? rootPath,
                Index: null,
                ExceptionDispatchInfo.Capture(exception),
                IndexBuildCount: indexBuildAttempted ? 1 : 0);
            if (archiveRootPath is not null)
            {
                archiveSnapshots[archiveRootPath] = snapshot;
            }

            return snapshot;
        }
    }

    private static string? ResolveFreshArchiveRootPath(string rootPath)
    {
        var fullRootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (HasTrinityArchiveAt(fullRootPath))
        {
            return fullRootPath;
        }

        var nestedRomFsPath = Path.Combine(fullRootPath, "romfs");
        return HasTrinityArchiveAt(nestedRomFsPath)
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(nestedRomFsPath))
            : null;
    }

    private static FreshArchiveSource[] OpenFreshArchiveSources(
        FreshArchiveSnapshot snapshot,
        string? supportFolderPath,
        int readerCount,
        int? maximumIndexBytes = MaximumBoundedArchiveIndexBytes,
        long? maximumPackBytes = MaximumBoundedArchivePackBytes)
    {
        if (snapshot.Failure is not null)
        {
            return Enumerable.Repeat(
                    new FreshArchiveSource(Archive: null, Index: null, Failure: snapshot.Failure),
                    readerCount)
                .ToArray();
        }

        if (snapshot.Index is null || string.IsNullOrWhiteSpace(snapshot.RootPath))
        {
            return Enumerable.Repeat(FreshArchiveSource.Missing, readerCount).ToArray();
        }

        var archives = new SvTrinityArchive?[readerCount];
        try
        {
            for (var index = 0; index < archives.Length; index++)
            {
                archives[index] = SvTrinityArchive.Open(
                    snapshot.RootPath,
                    supportFolderPath,
                    index: snapshot.Index,
                    maximumIndexBytes: maximumIndexBytes,
                    maximumPackBytes: maximumPackBytes);
            }

            return archives
                .Select(archive => new FreshArchiveSource(
                    archive ?? throw new InvalidOperationException("A retained Trinity reader was not opened."),
                    snapshot.Index,
                    Failure: null))
                .ToArray();
        }
        catch (Exception exception) when (IsContextualFileFailure(exception))
        {
            foreach (var archive in archives)
            {
                archive?.Dispose();
            }

            var failure = ExceptionDispatchInfo.Capture(exception);
            return Enumerable.Repeat(
                    new FreshArchiveSource(Archive: null, Index: null, Failure: failure),
                    readerCount)
                .ToArray();
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

    private sealed record FreshArchiveSnapshot(
        string? RootPath,
        SvTrinityArchiveIndex? Index,
        ExceptionDispatchInfo? Failure,
        int IndexBuildCount)
    {
        public static FreshArchiveSnapshot Missing { get; } = new(
            RootPath: null,
            Index: null,
            Failure: null,
            IndexBuildCount: 0);
    }

    internal sealed record FreshArchiveSource(
        SvTrinityArchive? Archive,
        SvTrinityArchiveIndex? Index,
        ExceptionDispatchInfo? Failure)
    {
        public static FreshArchiveSource Missing { get; } = new(
            Archive: null,
            Index: null,
            Failure: null);
    }

    private sealed class FreshReadContext : IDisposable
    {
        private readonly ProjectPaths paths;
        private FreshArchiveSource? baseSource;
        private FreshArchiveSource? outputSource;
        private int leaseCount = 1;
        private bool disposed;

        private FreshReadContext(ProjectPaths paths, FreshReadContext? parent)
        {
            this.paths = paths;
            Parent = parent;
        }

        internal FreshReadContext? Parent { get; }

        internal static FreshReadContext Create(ProjectPaths paths, FreshReadContext? parent)
        {
            return new FreshReadContext(paths, parent);
        }

        internal FreshArchiveSource GetBaseSource(ProjectPaths candidatePaths)
        {
            ThrowIfUnavailable(candidatePaths);
            EnsureInitialized();
            return baseSource!;
        }

        internal FreshArchiveSource GetOutputSource(ProjectPaths candidatePaths)
        {
            ThrowIfUnavailable(candidatePaths);
            EnsureInitialized();
            return outputSource!;
        }

        internal bool Matches(ProjectPaths candidatePaths)
        {
            return !disposed && Equals(paths, candidatePaths);
        }

        internal void Retain()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            leaseCount = checked(leaseCount + 1);
        }

        internal bool Release()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (leaseCount <= 0)
            {
                throw new InvalidOperationException(
                    "The Scarlet/Violet fresh-read scope lease is unbalanced.");
            }

            leaseCount--;
            return leaseCount == 0;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            ExceptionDispatchInfo? firstFailure = null;
            try
            {
                baseSource?.Archive?.Dispose();
            }
            catch (Exception exception)
            {
                firstFailure = ExceptionDispatchInfo.Capture(exception);
            }

            if (!ReferenceEquals(outputSource?.Archive, baseSource?.Archive))
            {
                try
                {
                    outputSource?.Archive?.Dispose();
                }
                catch (Exception exception)
                {
                    firstFailure ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            disposed = true;
            leaseCount = 0;
            firstFailure?.Throw();
        }

        private void EnsureInitialized()
        {
            if (baseSource is not null && outputSource is not null)
            {
                return;
            }

            var archiveSnapshots = new Dictionary<string, FreshArchiveSnapshot>(
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);
            var capturedBaseSnapshot = CaptureFreshArchiveSnapshot(
                paths.BaseRomFsPath,
                archiveSnapshots,
                maximumIndexBytes: null);
            var capturedOutputSnapshot = CaptureFreshArchiveSnapshot(
                paths.OutputRootPath,
                archiveSnapshots,
                maximumIndexBytes: null);
            var openedBaseSource = OpenFreshArchiveSources(
                capturedBaseSnapshot,
                paths.ScarletVioletSupportFolderPath,
                readerCount: 1,
                maximumIndexBytes: null,
                maximumPackBytes: null)[0];
            FreshArchiveSource? openedOutputSource = null;
            try
            {
                openedOutputSource = OpenFreshArchiveSources(
                    capturedOutputSnapshot,
                    paths.ScarletVioletSupportFolderPath,
                    readerCount: 1,
                    maximumIndexBytes: null,
                    maximumPackBytes: null)[0];
                baseSource = openedBaseSource;
                outputSource = openedOutputSource;
            }
            catch
            {
                openedBaseSource.Archive?.Dispose();
                openedOutputSource?.Archive?.Dispose();
                throw;
            }
        }

        private void ThrowIfUnavailable(ProjectPaths candidatePaths)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!Equals(paths, candidatePaths))
            {
                throw new InvalidOperationException(
                    "A Scarlet/Violet fresh-read scope cannot cross project source roots.");
            }
        }
    }

    private sealed class FreshReadScope : IDisposable
    {
        private readonly FreshReadContext context;
        private bool disposed;

        internal FreshReadScope(FreshReadContext context)
        {
            this.context = context;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (!ReferenceEquals(activeFreshReadContext, context))
            {
                throw new InvalidOperationException(
                    "The Scarlet/Violet fresh-read scope is unbalanced.");
            }

            if (context.Release())
            {
                activeFreshReadContext = context.Parent;
                context.Dispose();
            }
        }
    }

    internal sealed class FreshSemanticReaderPool : IDisposable
    {
        private readonly IReadOnlyList<FreshArchiveSource> baseSources;
        private readonly IReadOnlyList<FreshArchiveSource> outputSources;
        private bool disposed;

        internal FreshSemanticReaderPool(
            IReadOnlyList<SvWorkflowFileSource> readers,
            IReadOnlyList<FreshArchiveSource> baseSources,
            IReadOnlyList<FreshArchiveSource> outputSources,
            int baseArchiveIndexBuildCount,
            int outputArchiveIndexBuildCount)
        {
            Readers = readers;
            this.baseSources = baseSources;
            this.outputSources = outputSources;
            BaseArchiveIndexBuildCount = baseArchiveIndexBuildCount;
            OutputArchiveIndexBuildCount = outputArchiveIndexBuildCount;
        }

        internal IReadOnlyList<SvWorkflowFileSource> Readers { get; }

        internal int BaseArchiveIndexBuildCount { get; }

        internal int OutputArchiveIndexBuildCount { get; }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            ExceptionDispatchInfo? firstFailure = null;
            foreach (var source in baseSources.Concat(outputSources))
            {
                try
                {
                    source.Archive?.Dispose();
                }
                catch (Exception exception)
                {
                    firstFailure ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            disposed = true;
            firstFailure?.Throw();
        }
    }

    internal sealed class DeferredOutputBatch : IDisposable
    {
        private readonly ProjectPaths paths;
        private readonly SvOutputApplyContext applyContext;
        private readonly Dictionary<string, SvWorkflowFileWrite> writes = new(
            StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> deletes = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> reviewedTargets;
        private readonly IReadOnlyDictionary<string, OutputFileState> reviewedStates;
        private bool disposed;
        private bool committed;

        internal DeferredOutputBatch(
            ProjectPaths paths,
            SvOutputMode outputMode,
            ChangePlan reviewedPlan,
            SvOutputApplyContext applyContext)
        {
            this.paths = paths;
            OutputMode = outputMode;
            this.applyContext = applyContext;
            var comparer = StringComparer.Ordinal;
            var reviewedRelativePaths = reviewedPlan.Writes
                .Select(write => NormalizeStandaloneOutputRelativePath(write.TargetRelativePath))
                .ToArray();
            var resolvedComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            if (reviewedRelativePaths.Distinct(comparer).Count() != reviewedRelativePaths.Length
                || reviewedRelativePaths
                    .Select(relativePath => Path.GetFullPath(
                        ResolveStandaloneOutputPath(paths, relativePath)))
                    .GroupBy(path => path, resolvedComparer)
                    .Any(group => group.Count() > 1))
            {
                throw new OutputReviewStateConflictException();
            }

            reviewedTargets = reviewedRelativePaths.ToHashSet(comparer);
            var options = new OutputTransactionCoordinatorOptions();
            long observedBytes = 0;
            var states = new Dictionary<string, OutputFileState>(comparer);
            foreach (var relativePath in reviewedTargets)
            {
                var targetPath = ResolveStandaloneOutputPath(paths, relativePath);
                var remainingBytes = options.MaximumBackupBytesPerApply - observedBytes;
                var state = CaptureOutputFileState(
                    targetPath,
                    Math.Min(options.MaximumFingerprintFileBytes, remainingBytes));
                observedBytes = checked(observedBytes + state.LengthBytes);
                states.Add(relativePath, state);
            }

            reviewedStates = states;
        }

        internal SvOutputMode OutputMode { get; }

        internal bool IsCommitting { get; private set; }

        internal bool HasPendingMutations => writes.Count > 0 || deletes.Count > 0;

        internal bool Matches(ProjectPaths candidate)
        {
            return candidate == paths;
        }

        internal bool TryGetRomFsMutation(string virtualPath, out byte[]? bytes)
        {
            if (writes.TryGetValue(virtualPath, out var write))
            {
                bytes = write.Bytes;
                return true;
            }

            if (deletes.Contains(virtualPath))
            {
                bytes = null;
                return true;
            }

            bytes = null;
            return false;
        }

        internal void Stage(
            ProjectPaths candidatePaths,
            SvOutputMode outputMode,
            IReadOnlyList<SvWorkflowFileWrite> stagedWrites,
            IReadOnlyList<string> stagedDeletes,
            SvOutputApplyContext? operationContext,
            Func<bool>? revalidateReviewedState)
        {
            ThrowIfUnavailable();
            if (!Matches(candidatePaths) || outputMode != OutputMode)
            {
                throw new InvalidOperationException(
                    "A Scarlet/Violet deferred output batch cannot cross projects or output modes.");
            }

            if (revalidateReviewedState is not null)
            {
                if (!RevalidateReviewedState(paths, revalidateReviewedState))
                {
                    throw new OutputReviewStateConflictException();
                }
            }

            foreach (var write in stagedWrites)
            {
                EnsureReviewedTarget(ToOutputRelativePath(write.VirtualPath, outputMode));
                deletes.Remove(write.VirtualPath);
                writes[write.VirtualPath] = write with
                {
                    ApplyContext = write.ApplyContext ?? operationContext,
                };
            }

            foreach (var delete in stagedDeletes)
            {
                EnsureReviewedTarget(ToOutputRelativePath(delete, outputMode));
                writes.Remove(delete);
                deletes.Add(delete);
            }
        }

        internal OutputApplyResult? Commit(Func<bool>? revalidateReviewedState = null)
        {
            ThrowIfUnavailable();
            IsCommitting = true;
            try
            {
                if (revalidateReviewedState is not null
                    && !RevalidateReviewedState(paths, revalidateReviewedState))
                {
                    throw new OutputReviewStateConflictException();
                }

                var options = new OutputTransactionCoordinatorOptions();
                long observedBytes = 0;
                foreach (var reviewed in reviewedStates)
                {
                    var targetPath = ResolveStandaloneOutputPath(paths, reviewed.Key);
                    var remainingBytes = options.MaximumBackupBytesPerApply - observedBytes;
                    var current = CaptureOutputFileState(
                        targetPath,
                        Math.Min(options.MaximumFingerprintFileBytes, remainingBytes));
                    observedBytes = checked(observedBytes + current.LengthBytes);
                    if (current != reviewed.Value)
                    {
                        throw new OutputPreimageConflictException(
                            new RelativeOutputPath(reviewed.Key));
                    }
                }

                if (OutputMode == SvOutputMode.Standalone
                    && (writes.Count > 0 || deletes.Count > 0))
                {
                    EnsureReviewedTarget(
                        ToOutputRelativePath(DescriptorVirtualPath, SvOutputMode.Standalone));
                }

                var result = ApplyBatchCoreLocked(
                    paths,
                    writes.Values.OrderBy(write => write.VirtualPath, StringComparer.Ordinal).ToArray(),
                    deletes.Order(StringComparer.Ordinal).ToArray(),
                    Array.Empty<SvStandaloneOutputMutation>(),
                    OutputMode,
                    applyContext,
                    revalidateReviewedState: null);
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

            if (!committed)
            {
                writes.Clear();
                deletes.Clear();
            }
        }

        private void EnsureReviewedTarget(string relativePath)
        {
            var normalized = NormalizeStandaloneOutputRelativePath(relativePath);
            if (!reviewedTargets.Contains(normalized))
            {
                throw new OutputReviewStateConflictException();
            }
        }

        private void ThrowIfUnavailable()
        {
            if (disposed || committed || IsCommitting)
            {
                throw new InvalidOperationException(
                    "The Scarlet/Violet deferred output batch is no longer available.");
            }
        }
    }
}

internal sealed record SvWorkflowFile(
    string VirtualPath,
    string RelativePath,
    byte[] Bytes,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

internal sealed record SvWorkflowArchiveInventory(
    IReadOnlyList<string> PackNames,
    IReadOnlySet<ulong> FileHashes);

internal sealed record PlannedWriteInfo(
    string TargetRelativePath,
    IReadOnlyList<ProjectFileReference> Sources,
    bool ReplacesExistingOutput);

internal sealed record SvWorkflowFileWrite(
    string VirtualPath,
    byte[] Bytes,
    SvOutputApplyContext? ApplyContext = null);

internal sealed record SvStandaloneOutputMutation(
    string RelativePath,
    byte[]? Bytes,
    byte[]? DeleteFallbackBytes = null,
    SvOutputApplyContext? ApplyContext = null);

internal sealed record SvWorkflowOutputMutation(
    string TargetPath,
    byte[]? Bytes,
    byte[]? DeleteFallbackBytes,
    SvOutputApplyContext? ApplyContext = null);

internal sealed record SvOutputApplyContext(
    string SemanticReviewHash,
    OwnershipOwnerId OwnerId,
    IReadOnlyList<OutputApplyOrigin> Origins,
    PreservationRuleDescriptor? PreservationRule = null)
{
    public OutputHistoryDetails? HistoryDetails { get; init; }
}

public sealed class SvOutputApplyNotCommittedException : IOException
{
    public SvOutputApplyNotCommittedException(OutputApplyResult result)
        : base(result.Outcome == OutputApplyOutcome.RolledBack
            ? "Scarlet/Violet output was rolled back and no reviewed changes were kept."
            : "Scarlet/Violet output requires recovery before another write can begin.")
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public OutputApplyResult Result { get; }
}

internal sealed class SvOutputRootLock : IDisposable
{
    private SvOutputRootLockGate? gate;
    private Mutex? processMutex;

    public SvOutputRootLock(SvOutputRootLockGate gate, Mutex processMutex)
    {
        this.gate = gate;
        this.processMutex = processMutex;
    }

    public void Dispose()
    {
        var capturedMutex = Interlocked.Exchange(ref processMutex, null);
        try
        {
            capturedMutex?.ReleaseMutex();
        }
        finally
        {
            capturedMutex?.Dispose();
            var capturedGate = Interlocked.Exchange(ref gate, null);
            if (capturedGate is not null)
            {
                try
                {
                    Monitor.Exit(capturedGate.SyncRoot);
                }
                finally
                {
                    SvWorkflowFileSource.ReleaseOutputRootGate(capturedGate);
                }
            }
        }
    }
}

internal sealed class SvOutputRootLockGate
{
    private readonly object referenceSync = new();
    private int referenceCount;
    private bool retired;

    public SvOutputRootLockGate(string lockKey)
    {
        LockKey = lockKey;
    }

    public string LockKey { get; }

    public object SyncRoot { get; } = new();

    public bool TryAddReference()
    {
        lock (referenceSync)
        {
            if (retired)
            {
                return false;
            }

            referenceCount++;
            return true;
        }
    }

    public bool ReleaseReference()
    {
        lock (referenceSync)
        {
            if (referenceCount <= 0)
            {
                throw new InvalidOperationException("The Scarlet/Violet output lock gate was released without an owner.");
            }

            referenceCount--;
            if (referenceCount != 0)
            {
                return false;
            }

            retired = true;
            return true;
        }
    }
}
