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
        return LoadPresentSpeciesIds(
            project,
            File.ReadAllBytes,
            maximumRecordCount: null);
    }

    internal static IReadOnlySet<int> LoadPresentSpeciesIds(
        OpenedProject project,
        Func<string, byte[]> readAllBytes,
        int? maximumRecordCount)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(readAllBytes);

        if (maximumRecordCount is null)
            return SwShEffectivePokemonCatalog.LoadForBrowsing(project.Paths).SpeciesFor(SwShPokemonCapability.Assignment);

        var source = ResolveWorkflowFile(project, SwShPersonalTable.PersonalDataRelativePath);
        if (source is null)
        {
            return new HashSet<int>();
        }

        try
        {
            return CreatePresentSpeciesIds(SwShPersonalTable.Parse(
                readAllBytes(source.AbsolutePath),
                maximumRecordCount).Records);
        }
        catch (InvalidDataException) when (maximumRecordCount is null)
        {
            return new HashSet<int>();
        }
        catch (IOException) when (maximumRecordCount is null)
        {
            return new HashSet<int>();
        }
        catch (UnauthorizedAccessException) when (maximumRecordCount is null)
        {
            return new HashSet<int>();
        }
    }

    public static IReadOnlySet<int> CreatePresentSpeciesIds(IReadOnlyList<SwShPersonalRecord> personalRecords)
    {
        ArgumentNullException.ThrowIfNull(personalRecords);

        return SwShEffectivePokemonCatalog.Identities(personalRecords)
            .Where(entry => entry.Personal.IsPresentInGame)
            .Select(entry => entry.Identity.Species)
            .ToHashSet();
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

        return speciesNames
            .Select((name, index) => (name, index))
            .Where(entry => presentSpeciesIds.Contains(entry.index))
            .Select(entry => createOption(entry.index, FormatSpeciesOptionLabel(entry.index, entry.name)))
            .ToArray();
    }

    public static bool IsPresentSpeciesId(int speciesId, IReadOnlySet<int> presentSpeciesIds)
    {
        return speciesId > 0 && presentSpeciesIds.Contains(speciesId);
    }

    internal static IEnumerable<int> PresentForms(IReadOnlyList<SwShPersonalRecord> records, int species, int maximumForm)
    {
        if (!KM.SwSh.Restoration.SwShRestorationSpecies.IsSpecies(species)) yield break;
        var baseRow = KM.SwSh.Restoration.SwShRestorationSpecies.BaseRow(species);
        if (baseRow >= records.Count) yield break;
        var main = records[baseRow];
        for (var form = 0; form < Math.Max(1, main.FormCount) && form <= maximumForm; form++)
        {
            var row = form == 0 || main.FormStatsIndex == 0 ? baseRow : main.FormStatsIndex + form - 1;
            if (row >= 0 && row < records.Count && records[row].IsPresentInGame) yield return form;
        }
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
