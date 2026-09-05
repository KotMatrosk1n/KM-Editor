// SPDX-License-Identifier: GPL-3.0-only

using System.Security;
using KM.Core.Projects;
using KM.Core.Output;

namespace KM.Core.Files;

public sealed class ProjectFileGraphBuilder
{


    private static readonly EnumerationOptions ShallowEnumeration = new()
    {
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    private readonly ProjectFileGraphBuilderOptions options;

    public ProjectFileGraphBuilder(ProjectFileGraphBuilderOptions? options = null)
    {
        options ??= new ProjectFileGraphBuilderOptions();
        options.Validate();
        this.options = options;
    }

    public ProjectFileGraph Build(ProjectPaths paths)
    {
        return Build(paths, CancellationToken.None);
    }

    public ProjectFileGraph Build(ProjectPaths paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        cancellationToken.ThrowIfCancellationRequested();

        var entries = new Dictionary<string, FileGraphAccumulator>(StringComparer.OrdinalIgnoreCase);
        var budget = new TraversalBudget(options);

        // Prefix base roots with their LayeredFS target folder so provenance and write plans share one path space.
        AddBaseRoot(entries, paths.BaseRomFsPath, "romfs", budget, cancellationToken);
        AddScarletVioletVirtualRomFs(entries, paths, budget, cancellationToken);
        AddBaseRoot(entries, paths.BaseExeFsPath, "exefs", budget, cancellationToken);
        AddLayeredRoot(entries, paths.OutputRootPath, budget, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        return new ProjectFileGraph(
            entries
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return entry.Value.ToEntry(entry.Key);
                })
                .ToArray());
    }

    private static void AddScarletVioletVirtualRomFs(
        IDictionary<string, FileGraphAccumulator> entries,
        ProjectPaths paths,
        TraversalBudget budget,
        CancellationToken cancellationToken)
    {
        if (!ShouldExposeScarletVioletVirtualFiles(paths))
        {
            return;
        }

        foreach (var virtualPath in ScarletVioletKnownRomFsFiles.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = $"romfs/{virtualPath}";
            var accumulator = GetOrAdd(entries, relativePath, budget);
            accumulator.BaseFile ??= new ProjectFileReference(ProjectFileLayer.Base, relativePath);
        }
    }

    private static bool ShouldExposeScarletVioletVirtualFiles(ProjectPaths paths)
    {
        if (string.IsNullOrWhiteSpace(paths.BaseRomFsPath)
            || !Directory.Exists(paths.BaseRomFsPath)
            || !HasTraversableTrinityArchive(paths.BaseRomFsPath))
        {
            return false;
        }

        return ProjectGameMetadata.IsScarletViolet(paths.SelectedGame);
    }

    private static bool HasTraversableTrinityArchive(string baseRomFsPath)
    {
        if (!TryCreateTraversalRoot(baseRomFsPath, out var root))
        {
            return false;
        }

        return IsTraversableFile(root, Path.Combine("arc", "data.trpfd"))
            && IsTraversableFile(root, Path.Combine("arc", "data.trpfs"));
    }

    private static bool IsTraversableFile(DirectoryInfo root, string relativePath)
    {
        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        var currentPath = root.FullName;
        for (var index = 0; index < segments.Length; index++)
        {
            currentPath = Path.Combine(currentPath, segments[index]);
            if (!TryNormalizeContainedPath(root.FullName, currentPath, out var containedPath))
            {
                return false;
            }

            FileSystemInfo entry = index == segments.Length - 1
                ? new FileInfo(containedPath)
                : new DirectoryInfo(containedPath);
            if (!TryGetAttributes(entry, out var attributes)
                || !IsTraversableEntry(entry, attributes))
            {
                return false;
            }

            var shouldBeDirectory = index < segments.Length - 1;
            if (shouldBeDirectory != attributes.HasFlag(FileAttributes.Directory))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddBaseRoot(
        IDictionary<string, FileGraphAccumulator> entries,
        string? rootPath,
        string rootPrefix,
        TraversalBudget budget,
        CancellationToken cancellationToken)
    {
        TraverseFiles(
            rootPath,
            excludeOutputMetadata: false,
            budget,
            cancellationToken,
            (root, filePath) =>
            {
                var relativePath = NormalizeRelativePath(root, filePath, rootPrefix);
                var accumulator = GetOrAdd(entries, relativePath, budget);
                accumulator.BaseFile = new ProjectFileReference(ProjectFileLayer.Base, relativePath);
            });
    }

    private static void AddLayeredRoot(
        IDictionary<string, FileGraphAccumulator> entries,
        string? rootPath,
        TraversalBudget budget,
        CancellationToken cancellationToken)
    {
        TraverseFiles(
            rootPath,
            excludeOutputMetadata: true,
            budget,
            cancellationToken,
            (root, filePath) =>
            {
                var relativePath = NormalizeRelativePath(root, filePath, rootPrefix: null);
                var accumulator = GetOrAdd(entries, relativePath, budget);
                accumulator.LayeredFile = new ProjectFileReference(ProjectFileLayer.Layered, relativePath);
            });
    }

    private static void TraverseFiles(
        string? rootPath,
        bool excludeOutputMetadata,
        TraversalBudget budget,
        CancellationToken cancellationToken,
        Action<string, string> addFile)
    {
        if (string.IsNullOrWhiteSpace(rootPath)
            || !Directory.Exists(rootPath)
            || !TryCreateTraversalRoot(rootPath, out var root))
        {
            return;
        }

        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var visitedDirectories = new HashSet<string>(pathComparer);
        var pendingDirectories = new Stack<PendingDirectory>();
        pendingDirectories.Push(new PendingDirectory(root, Depth: 0));

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pending = pendingDirectories.Pop();
            if (!TryGetFullName(pending.Directory, out var pendingPath)
                || !TryNormalizeContainedOrRootPath(root.FullName, pendingPath, out var directoryPath)
                || !visitedDirectories.Add(directoryPath))
            {
                continue;
            }

            var directory = new DirectoryInfo(directoryPath);
            if (!TryGetAttributes(directory, out var directoryAttributes)
                || !directoryAttributes.HasFlag(FileAttributes.Directory)
                || !IsTraversableEntry(directory, directoryAttributes))
            {
                continue;
            }

            budget.RegisterDirectory(pending.Depth);
            var enumerator = TryCreateEnumerator(directory);
            if (enumerator is null)
            {
                continue;
            }

            try
            {
                while (TryMoveNext(enumerator, out var entry))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    budget.RegisterFileSystemEntry();

                    if (excludeOutputMetadata
                        && pending.Depth == 0
                        && OutputMetadataNamespace.ContainsReservedSegment(entry.Name))
                    {
                        continue;
                    }

                    if (!TryGetAttributes(entry, out var attributes)
                        || !IsTraversableEntry(entry, attributes)
                        || !TryGetFullName(entry, out var discoveredPath)
                        || !TryNormalizeContainedPath(root.FullName, discoveredPath, out var entryPath))
                    {
                        continue;
                    }

                    if (attributes.HasFlag(FileAttributes.Directory))
                    {
                        var childDepth = checked(pending.Depth + 1);
                        budget.ValidateDepth(childDepth);
                        pendingDirectories.Push(new PendingDirectory(new DirectoryInfo(entryPath), childDepth));
                        continue;
                    }

                    addFile(root.FullName, entryPath);
                }
            }
            finally
            {
                TryDispose(enumerator);
            }
        }
    }

    private static bool TryCreateTraversalRoot(string rootPath, out DirectoryInfo root)
    {
        try
        {
            root = new DirectoryInfo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath)));
            return TryGetAttributes(root, out var attributes)
                && attributes.HasFlag(FileAttributes.Directory)
                && IsTraversableEntry(root, attributes)
                && FileSystemPathBoundary.HasSafeExistingChain(root.FullName, isDirectory: true);
        }
        catch (Exception exception) when (IsSkippableFileSystemException(exception))
        {
            root = null!;
            return false;
        }
    }

    private static IEnumerator<FileSystemInfo>? TryCreateEnumerator(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateFileSystemInfos("*", ShallowEnumeration).GetEnumerator();
        }
        catch (Exception exception) when (IsSkippableFileSystemException(exception))
        {
            return null;
        }
    }

    private static bool TryMoveNext(
        IEnumerator<FileSystemInfo> enumerator,
        out FileSystemInfo entry)
    {
        try
        {
            if (enumerator.MoveNext())
            {
                entry = enumerator.Current;
                return true;
            }
        }
        catch (Exception exception) when (IsSkippableFileSystemException(exception))
        {
            // The current directory may have disappeared or become unreadable. Other queued
            // directories remain discoverable and are processed normally.
        }

        entry = null!;
        return false;
    }

    private static void TryDispose(IDisposable disposable)
    {
        try
        {
            disposable.Dispose();
        }
        catch (Exception exception) when (IsSkippableFileSystemException(exception))
        {
            // Enumeration cleanup cannot make a previously discovered path safe or unsafe.
        }
    }

    private static bool TryGetAttributes(FileSystemInfo entry, out FileAttributes attributes)
    {
        try
        {
            attributes = entry.Attributes;
            return true;
        }
        catch (Exception exception) when (IsSkippableFileSystemException(exception))
        {
            attributes = default;
            return false;
        }
    }

    private static bool TryGetFullName(FileSystemInfo entry, out string fullName)
    {
        try
        {
            fullName = entry.FullName;
            return true;
        }
        catch (Exception exception) when (IsSkippableFileSystemException(exception))
        {
            fullName = string.Empty;
            return false;
        }
    }

    private static bool IsTraversableEntry(FileSystemInfo entry, FileAttributes attributes)
    {
        if (!attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return true;
        }

        try
        {
            // Cloud Files placeholders are reparse entries without a link target. Actual
            // symbolic links and junctions expose a target and are never traversed.
            return string.IsNullOrEmpty(entry.LinkTarget);
        }
        catch (Exception exception) when (IsSkippableFileSystemException(exception))
        {
            return false;
        }
    }

    private static bool TryNormalizeContainedOrRootPath(
        string rootPath,
        string candidatePath,
        out string normalizedPath)
    {
        if (!TryNormalizePath(candidatePath, out normalizedPath))
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(rootPath, normalizedPath, comparison)
            || IsContainedRelativePath(rootPath, normalizedPath);
    }

    private static bool TryNormalizeContainedPath(
        string rootPath,
        string candidatePath,
        out string normalizedPath)
    {
        return TryNormalizePath(candidatePath, out normalizedPath)
            && IsContainedRelativePath(rootPath, normalizedPath);
    }

    private static bool TryNormalizePath(string path, out string normalizedPath)
    {
        try
        {
            normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return true;
        }
        catch (Exception exception) when (IsSkippableFileSystemException(exception))
        {
            normalizedPath = string.Empty;
            return false;
        }
    }

    private static bool IsContainedRelativePath(string rootPath, string candidatePath)
    {
        string relativePath;
        try
        {
            relativePath = Path.GetRelativePath(rootPath, candidatePath);
        }
        catch (Exception exception) when (IsSkippableFileSystemException(exception))
        {
            return false;
        }

        return relativePath.Length > 0
            && !string.Equals(relativePath, ".", StringComparison.Ordinal)
            && !string.Equals(relativePath, "..", StringComparison.Ordinal)
            && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathRooted(relativePath);
    }

    private static bool IsSkippableFileSystemException(Exception exception)
    {
        return exception is
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            ArgumentException or
            NotSupportedException;
    }

    private static FileGraphAccumulator GetOrAdd(
        IDictionary<string, FileGraphAccumulator> entries,
        string relativePath,
        TraversalBudget budget)
    {
        if (!entries.TryGetValue(relativePath, out var accumulator))
        {
            budget.RegisterGraphEntry();
            accumulator = new FileGraphAccumulator();
            entries.Add(relativePath, accumulator);
        }

        return accumulator;
    }

    private static string NormalizeRelativePath(string rootPath, string filePath, string? rootPrefix)
    {
        var relativePath = Path.GetRelativePath(rootPath, filePath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

        return rootPrefix is null ? relativePath : $"{rootPrefix}/{relativePath}";
    }

    private sealed record PendingDirectory(DirectoryInfo Directory, int Depth);

    private sealed class TraversalBudget(ProjectFileGraphBuilderOptions options)
    {
        private int fileSystemEntryCount;
        private int directoryCount;
        private int graphEntryCount;

        public void RegisterFileSystemEntry()
        {
            fileSystemEntryCount = RegisterCount(
                fileSystemEntryCount,
                options.MaximumFileSystemEntries,
                ProjectFileGraphDiscoveryLimit.FileSystemEntries);
        }

        public void RegisterDirectory(int depth)
        {
            ValidateDepth(depth);
            directoryCount = RegisterCount(
                directoryCount,
                options.MaximumDirectories,
                ProjectFileGraphDiscoveryLimit.Directories);
        }

        public void ValidateDepth(int depth)
        {
            if (depth > options.MaximumTraversalDepth)
            {
                throw new ProjectFileGraphDiscoveryException(
                    ProjectFileGraphDiscoveryLimit.TraversalDepth,
                    options.MaximumTraversalDepth);
            }
        }

        public void RegisterGraphEntry()
        {
            graphEntryCount = RegisterCount(
                graphEntryCount,
                options.MaximumGraphEntries,
                ProjectFileGraphDiscoveryLimit.GraphEntries);
        }

        private static int RegisterCount(
            int currentCount,
            int limit,
            ProjectFileGraphDiscoveryLimit limitKind)
        {
            if (currentCount >= limit)
            {
                throw new ProjectFileGraphDiscoveryException(limitKind, limit);
            }

            return currentCount + 1;
        }
    }

    private sealed class FileGraphAccumulator
    {
        public ProjectFileReference? BaseFile { get; set; }

        public ProjectFileReference? LayeredFile { get; set; }

        public ProjectFileGraphEntry ToEntry(string relativePath)
        {
            var state = (BaseFile, LayeredFile) switch
            {
                ({ }, { }) => ProjectFileGraphEntryState.LayeredOverride,
                (null, { }) => ProjectFileGraphEntryState.LayeredOnly,
                _ => ProjectFileGraphEntryState.BaseOnly,
            };

            return new ProjectFileGraphEntry(relativePath, BaseFile, LayeredFile, state);
        }
    }
}
