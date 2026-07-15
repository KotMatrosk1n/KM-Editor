// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;

namespace KM.SwSh.Editing;

public sealed class SwShOutputRollbackScope : IDisposable
{
    private readonly string outputRootPath;
    private readonly string snapshotRootPath;
    private readonly IReadOnlyList<OutputSnapshot> snapshots;
    private bool completed;

    private SwShOutputRollbackScope(
        string outputRootPath,
        string snapshotRootPath,
        IReadOnlyList<OutputSnapshot> snapshots)
    {
        this.outputRootPath = outputRootPath;
        this.snapshotRootPath = snapshotRootPath;
        this.snapshots = snapshots;
    }

    public static bool TryCapture(
        ProjectPaths paths,
        IEnumerable<string> targetRelativePaths,
        out SwShOutputRollbackScope? scope,
        out SwShOutputRollbackFailure? failure)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(targetRelativePaths);

        scope = null;
        failure = null;
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            failure = new SwShOutputRollbackFailure(
                string.Empty,
                "Output Root is not configured.");
            return false;
        }

        if (!TryResolveStableOutputPaths(paths, out var stablePaths, out var stableRootFailure))
        {
            failure = new SwShOutputRollbackFailure(
                string.Empty,
                stableRootFailure ?? "Output Root could not be resolved safely.");
            return false;
        }

        var outputRootPath = stablePaths.OutputRootPath!;
        var snapshotRootPath = Path.Combine(
            Path.GetTempPath(),
            "km-editor-swsh-rollback",
            Guid.NewGuid().ToString("N"));
        var snapshots = new List<OutputSnapshot>();
        var currentRelativePath = string.Empty;

        try
        {
            Directory.CreateDirectory(snapshotRootPath);
            var relativePaths = targetRelativePaths
                .Select(NormalizeRelativePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            for (var index = 0; index < relativePaths.Length; index++)
            {
                var relativePath = relativePaths[index];
                currentRelativePath = relativePath;
                var targetPath = ResolvePhysicalContainedPath(outputRootPath, relativePath);
                if (targetPath is null)
                {
                    throw new OutputSnapshotException(
                        relativePath,
                        "Target path is not a physical path inside Output Root.");
                }

                if (File.Exists(targetPath))
                {
                    var snapshotPath = Path.Combine(snapshotRootPath, $"{index:D6}.bin");
                    File.Copy(targetPath, snapshotPath, overwrite: false);
                    snapshots.Add(new OutputSnapshot(
                        relativePath,
                        targetPath,
                        OutputSnapshotKind.File,
                        snapshotPath));
                }
                else if (Directory.Exists(targetPath))
                {
                    snapshots.Add(new OutputSnapshot(
                        relativePath,
                        targetPath,
                        OutputSnapshotKind.Directory,
                        SnapshotPath: null));
                }
                else
                {
                    snapshots.Add(new OutputSnapshot(
                        relativePath,
                        targetPath,
                        OutputSnapshotKind.Missing,
                        SnapshotPath: null));
                }
            }

            scope = new SwShOutputRollbackScope(outputRootPath, snapshotRootPath, snapshots);
            return true;
        }
        catch (OutputSnapshotException exception)
        {
            failure = new SwShOutputRollbackFailure(exception.RelativePath, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failure = new SwShOutputRollbackFailure(
                currentRelativePath,
                exception.Message);
        }

        TryDeleteSnapshotDirectory(snapshotRootPath);
        return false;
    }

    public IReadOnlyList<SwShOutputRollbackFailure> Rollback()
    {
        if (completed)
        {
            return Array.Empty<SwShOutputRollbackFailure>();
        }

        var failures = new List<SwShOutputRollbackFailure>();
        foreach (var snapshot in snapshots.Reverse())
        {
            try
            {
                RestoreSnapshot(snapshot);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add(new SwShOutputRollbackFailure(snapshot.RelativePath, exception.Message));
            }
        }

        completed = true;
        if (failures.Count > 0)
        {
            failures.Add(new SwShOutputRollbackFailure(
                string.Empty,
                $"Temporary rollback snapshots were retained for recovery at '{snapshotRootPath}'."));
        }
        else if (!TryDeleteSnapshotDirectory(snapshotRootPath))
        {
            failures.Add(new SwShOutputRollbackFailure(
                string.Empty,
                "Temporary rollback snapshots could not be deleted."));
        }

        return failures;
    }

    public void Commit()
    {
        if (completed)
        {
            return;
        }

        completed = true;
        TryDeleteSnapshotDirectory(snapshotRootPath);
    }

    public void Dispose()
    {
        if (!completed)
        {
            Rollback();
        }
    }

    private void RestoreSnapshot(OutputSnapshot snapshot)
    {
        var currentTargetPath = ResolvePhysicalContainedPath(outputRootPath, snapshot.RelativePath);
        if (currentTargetPath is null
            || !string.Equals(currentTargetPath, snapshot.TargetPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Rollback target is no longer a physical path inside Output Root.");
        }

        switch (snapshot.Kind)
        {
            case OutputSnapshotKind.File:
                if (Directory.Exists(snapshot.TargetPath))
                {
                    throw new IOException("Rollback target is now a directory and cannot be replaced safely.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(snapshot.TargetPath)!);
                File.Copy(snapshot.SnapshotPath!, snapshot.TargetPath, overwrite: true);
                break;
            case OutputSnapshotKind.Directory:
                if (File.Exists(snapshot.TargetPath))
                {
                    File.Delete(snapshot.TargetPath);
                }

                Directory.CreateDirectory(snapshot.TargetPath);
                break;
            case OutputSnapshotKind.Missing:
                if (Directory.Exists(snapshot.TargetPath))
                {
                    throw new IOException("Rollback target is now a directory and cannot be deleted safely.");
                }

                if (File.Exists(snapshot.TargetPath))
                {
                    File.Delete(snapshot.TargetPath);
                    DeleteEmptyParentDirectories(snapshot.TargetPath);
                }

                break;
            default:
                throw new InvalidOperationException($"Unknown output snapshot kind '{snapshot.Kind}'.");
        }
    }

    private void DeleteEmptyParentDirectories(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        while (!string.IsNullOrWhiteSpace(directory)
            && IsPathInsideRoot(outputRootPath, directory)
            && !string.Equals(
                Path.TrimEndingDirectorySeparator(directory),
                Path.TrimEndingDirectorySeparator(outputRootPath),
                StringComparison.OrdinalIgnoreCase))
        {
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(outputRootPath, directory));
            var currentDirectory = ResolvePhysicalContainedPath(outputRootPath, relativePath);
            if (currentDirectory is null
                || !string.Equals(currentDirectory, directory, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (Directory.EnumerateFileSystemEntries(directory).Any())
            {
                break;
            }

            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }

    internal static bool TryResolveStableOutputPaths(
        ProjectPaths paths,
        out ProjectPaths stablePaths,
        out string? failure)
    {
        ArgumentNullException.ThrowIfNull(paths);

        stablePaths = paths;
        failure = null;
        if (string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return true;
        }

        try
        {
            var fullRoot = Path.GetFullPath(paths.OutputRootPath);
            var root = new DirectoryInfo(fullRoot);
            if (!string.IsNullOrWhiteSpace(root.LinkTarget))
            {
                var resolvedRoot = root.ResolveLinkTarget(returnFinalTarget: true);
                if (resolvedRoot is null)
                {
                    failure = "Configured Output Root link could not be resolved.";
                    return false;
                }

                fullRoot = Path.GetFullPath(resolvedRoot.FullName);
            }

            stablePaths = paths with { OutputRootPath = fullRoot };
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            failure = $"Configured Output Root could not be resolved: {exception.Message}";
            return false;
        }
    }

    internal static string? ResolvePhysicalContainedPath(string? outputRootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(outputRootPath)
            || string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath))
        {
            return null;
        }

        try
        {
            var fullRoot = Path.GetFullPath(outputRootPath);
            var targetPath = Path.GetFullPath(Path.Combine(
                fullRoot,
                relativePath
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar)));
            var relativeToRoot = Path.GetRelativePath(fullRoot, targetPath);
            return PathContainment.IsWithinRoot(relativeToRoot)
                && !TraversesLinkBelowRoot(fullRoot, relativeToRoot)
                    ? targetPath
                    : null;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsPathInsideRoot(string outputRootPath, string targetPath)
    {
        return PathContainment.IsWithinRoot(Path.GetRelativePath(outputRootPath, targetPath));
    }

    private static bool TraversesLinkBelowRoot(string fullRoot, string relativePath)
    {
        var currentPath = fullRoot;
        foreach (var segment in relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            try
            {
                var attributes = File.GetAttributes(currentPath);
                if (!attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                FileSystemInfo entry = attributes.HasFlag(FileAttributes.Directory)
                    ? new DirectoryInfo(currentPath)
                    : new FileInfo(currentPath);
                if (!string.IsNullOrWhiteSpace(entry.LinkTarget))
                {
                    return true;
                }
            }
            catch (FileNotFoundException)
            {
                break;
            }
            catch (DirectoryNotFoundException)
            {
                break;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace('\\', '/');
    }

    private static bool TryDeleteSnapshotDirectory(string snapshotRootPath)
    {
        try
        {
            if (Directory.Exists(snapshotRootPath))
            {
                Directory.Delete(snapshotRootPath, recursive: true);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record OutputSnapshot(
        string RelativePath,
        string TargetPath,
        OutputSnapshotKind Kind,
        string? SnapshotPath);

    private sealed class OutputSnapshotException(string relativePath, string message) : IOException(message)
    {
        public string RelativePath { get; } = relativePath;
    }

    private enum OutputSnapshotKind
    {
        Missing,
        File,
        Directory,
    }
}

public sealed record SwShOutputRollbackFailure(string RelativePath, string Message);
