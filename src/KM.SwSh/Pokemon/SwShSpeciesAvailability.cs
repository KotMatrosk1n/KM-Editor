// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using System.Globalization;

namespace KM.SwSh.Pokemon;

internal static class SwShSpeciesAvailability
{
    public static IReadOnlySet<int> LoadPresentSpeciesIds(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var source = ResolveWorkflowFile(project, SwShPersonalTable.PersonalDataRelativePath);
        if (source is null)
        {
            return new HashSet<int>();
        }

        try
        {
            return CreatePresentSpeciesIds(SwShPersonalTable.Parse(File.ReadAllBytes(source.AbsolutePath)).Records);
        }
        catch (InvalidDataException)
        {
            return new HashSet<int>();
        }
        catch (IOException)
        {
            return new HashSet<int>();
        }
        catch (UnauthorizedAccessException)
        {
            return new HashSet<int>();
        }
    }

    public static IReadOnlySet<int> CreatePresentSpeciesIds(IReadOnlyList<SwShPersonalRecord> personalRecords)
    {
        ArgumentNullException.ThrowIfNull(personalRecords);

        var speciesIds = new HashSet<int>();
        foreach (var record in personalRecords)
        {
            if (!record.IsPresentInGame)
            {
                continue;
            }

            var speciesId = record.HatchedSpecies > 0 ? record.HatchedSpecies : record.PersonalId;
            if (speciesId > 0)
            {
                speciesIds.Add(speciesId);
            }
        }

        return speciesIds;
    }

    public static IReadOnlyList<TOption> CreateSpeciesOptions<TOption>(
        IReadOnlyList<string> speciesNames,
        IReadOnlySet<int> presentSpeciesIds,
        Func<int, string, TOption> createOption)
    {
        ArgumentNullException.ThrowIfNull(speciesNames);
        ArgumentNullException.ThrowIfNull(presentSpeciesIds);
        ArgumentNullException.ThrowIfNull(createOption);

        if (speciesNames.Count == 0)
        {
            return [];
        }

        var shouldFilter = presentSpeciesIds.Count > 0;
        return speciesNames
            .Select((name, index) => (name, index))
            .Where(entry => !shouldFilter || presentSpeciesIds.Contains(entry.index))
            .Select(entry => createOption(entry.index, FormatSpeciesOptionLabel(entry.index, entry.name)))
            .ToArray();
    }

    public static bool IsPresentSpeciesId(int speciesId, IReadOnlySet<int> presentSpeciesIds)
    {
        return speciesId > 0 && (presentSpeciesIds.Count == 0 || presentSpeciesIds.Contains(speciesId));
    }

    private static string FormatSpeciesOptionLabel(int speciesId, string name)
    {
        var label = string.IsNullOrWhiteSpace(name)
            ? $"Species {speciesId.ToString(CultureInfo.InvariantCulture)}"
            : name;

        return string.Create(CultureInfo.InvariantCulture, $"{speciesId:000} {label}");
    }

    private static WorkflowFileSource? ResolveWorkflowFile(OpenedProject project, string relativePath)
    {
        var graphEntry = project.FileGraph.Entries.FirstOrDefault(entry =>
            string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        if (graphEntry is null)
        {
            return null;
        }

        var sourcePath = ResolveSourcePath(project.Paths, graphEntry);
        return sourcePath is not null && File.Exists(sourcePath)
            ? new WorkflowFileSource(graphEntry.RelativePath, sourcePath)
            : null;
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

    private sealed record WorkflowFileSource(string RelativePath, string AbsolutePath);
}
