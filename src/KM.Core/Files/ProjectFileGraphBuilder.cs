// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;

namespace KM.Core.Files;

public sealed class ProjectFileGraphBuilder
{
    private static readonly EnumerationOptions RecursiveEnumeration = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        ReturnSpecialDirectories = false,
    };

    public ProjectFileGraph Build(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var entries = new Dictionary<string, FileGraphAccumulator>(StringComparer.OrdinalIgnoreCase);

        // Prefix base roots with their LayeredFS target folder so provenance and write plans share one path space.
        AddBaseRoot(entries, paths.BaseRomFsPath, "romfs");
        AddBaseRoot(entries, paths.BaseExeFsPath, "exefs");
        AddLayeredRoot(entries, paths.OutputRootPath);

        return new ProjectFileGraph(
            entries
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => entry.Value.ToEntry(entry.Key))
                .ToArray());
    }

    private static void AddBaseRoot(
        IDictionary<string, FileGraphAccumulator> entries,
        string? rootPath,
        string rootPrefix)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(rootPath, "*", RecursiveEnumeration))
        {
            var relativePath = NormalizeRelativePath(rootPath, filePath, rootPrefix);
            var accumulator = GetOrAdd(entries, relativePath);
            accumulator.BaseFile = new ProjectFileReference(ProjectFileLayer.Base, relativePath);
        }
    }

    private static void AddLayeredRoot(IDictionary<string, FileGraphAccumulator> entries, string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(rootPath, "*", RecursiveEnumeration))
        {
            var relativePath = NormalizeRelativePath(rootPath, filePath, rootPrefix: null);
            var accumulator = GetOrAdd(entries, relativePath);
            accumulator.LayeredFile = new ProjectFileReference(ProjectFileLayer.Layered, relativePath);
        }
    }

    private static FileGraphAccumulator GetOrAdd(
        IDictionary<string, FileGraphAccumulator> entries,
        string relativePath)
    {
        if (!entries.TryGetValue(relativePath, out var accumulator))
        {
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
