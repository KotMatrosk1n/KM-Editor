// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using System.Globalization;

namespace KM.SwSh.Moves;

internal static class SwShMoveAvailability
{
    public static IReadOnlySet<int> LoadUsableMoveIds(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var moveSources = ResolveWorkflowFiles(project, SwShMoveDataFile.MoveDataRelativeDirectory)
            .Where(source => IsMoveDataFile(source.GraphEntry.RelativePath))
            .OrderBy(source => source.GraphEntry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var records = new List<MoveAvailabilityRecord>();
        foreach (var source in moveSources)
        {
            try
            {
                var move = SwShMoveDataFile.Parse(File.ReadAllBytes(source.AbsolutePath)).Record;
                if (move.MoveId <= int.MaxValue)
                {
                    records.Add(new MoveAvailabilityRecord(
                        checked((int)move.MoveId),
                        move.CanUseMove,
                        GetSourceLayer(source.GraphEntry),
                        source.GraphEntry.RelativePath));
                }
            }
            catch (InvalidDataException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return records
            .GroupBy(record => record.MoveId)
            .Select(group => group
                .OrderByDescending(record => record.SourceLayer == ProjectFileLayer.Layered)
                .ThenByDescending(record => IsPreferredMoveDataFile(record.RelativePath))
                .ThenBy(record => record.RelativePath, StringComparer.OrdinalIgnoreCase)
                .First())
            .Where(record => record.CanUseMove)
            .Select(record => record.MoveId)
            .ToHashSet();
    }

    public static IReadOnlyList<TOption> CreateMoveOptions<TOption>(
        IReadOnlyList<string> moveNames,
        IReadOnlySet<int> usableMoveIds,
        Func<int, string, TOption> createOption,
        bool includeNone = false)
    {
        ArgumentNullException.ThrowIfNull(moveNames);
        ArgumentNullException.ThrowIfNull(usableMoveIds);
        ArgumentNullException.ThrowIfNull(createOption);

        var maximumMoveId = Math.Max(
            moveNames.Count - 1,
            usableMoveIds.Count == 0 ? -1 : usableMoveIds.Max());
        if (maximumMoveId < 0)
        {
            return [];
        }

        var shouldFilter = usableMoveIds.Count > 0;
        return Enumerable.Range(0, maximumMoveId + 1)
            .Where(moveId =>
                (includeNone && moveId == 0)
                || (moveId > 0 && (!shouldFilter || usableMoveIds.Contains(moveId))))
            .Select(moveId => createOption(moveId, FormatMoveOptionLabel(moveId, moveNames)))
            .ToArray();
    }

    private static string FormatMoveOptionLabel(int moveId, IReadOnlyList<string> moveNames)
    {
        var label = (uint)moveId < (uint)moveNames.Count && !string.IsNullOrWhiteSpace(moveNames[moveId])
            ? moveNames[moveId]
            : moveId == 0 ? "None" : $"Move {moveId.ToString(CultureInfo.InvariantCulture)}";

        return string.Create(CultureInfo.InvariantCulture, $"{moveId:000} {label}");
    }

    private static bool IsMoveDataFile(string relativePath)
    {
        return relativePath.EndsWith(".wazabin", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith(".bin", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPreferredMoveDataFile(string relativePath)
    {
        return relativePath.EndsWith(".wazabin", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<WorkflowFileSource> ResolveWorkflowFiles(
        OpenedProject project,
        string relativeDirectory)
    {
        var prefix = relativeDirectory.TrimEnd('/') + "/";

        return project.FileGraph.Entries
            .Where(entry => entry.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new
            {
                Entry = entry,
                SourcePath = ResolveSourcePath(project.Paths, entry),
            })
            .Where(source => source.SourcePath is not null && File.Exists(source.SourcePath))
            .Select(source => new WorkflowFileSource(source.Entry, source.SourcePath!));
    }

    private static string? ResolveSourcePath(ProjectPaths paths, ProjectFileGraphEntry entry)
    {
        if (entry.LayeredFile is not null && !string.IsNullOrWhiteSpace(paths.OutputRootPath))
        {
            return CombineGraphPath(paths.OutputRootPath, entry.RelativePath);
        }

        if (entry.BaseFile is not null && entry.RelativePath.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            return CombineGraphPath(paths.BaseRomFsPath, entry.RelativePath["romfs/".Length..]);
        }

        return null;
    }

    private static string? CombineGraphPath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        return Path.Combine(
            rootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static ProjectFileLayer GetSourceLayer(ProjectFileGraphEntry entry)
    {
        return entry.LayeredFile is not null
            ? ProjectFileLayer.Layered
            : ProjectFileLayer.Base;
    }

    private sealed record MoveAvailabilityRecord(
        int MoveId,
        bool CanUseMove,
        ProjectFileLayer SourceLayer,
        string RelativePath);

    private sealed record WorkflowFileSource(ProjectFileGraphEntry GraphEntry, string AbsolutePath);
}
