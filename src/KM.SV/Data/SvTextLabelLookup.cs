// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SV.Workflows;

namespace KM.SV.Data;

internal sealed class SvTextLabelLookup
{
    private static readonly SvTextLabelLookup Empty = new(
        [],
        [],
        [],
        [],
        [],
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        [],
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        [],
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

    private readonly IReadOnlyList<string> itemNames;
    private readonly IReadOnlyList<string> moveNames;
    private readonly IReadOnlyList<string> pokemonNames;
    private readonly IReadOnlyList<string> abilityNames;
    private readonly IReadOnlyList<string> placeNames;
    private readonly IReadOnlyDictionary<string, int> placeNameIndices;
    private readonly IReadOnlyList<string> trainerNames;
    private readonly IReadOnlyDictionary<string, int> trainerNameIndices;
    private readonly IReadOnlyList<string> trainerTypes;
    private readonly IReadOnlyDictionary<string, int> trainerTypeIndices;

    private SvTextLabelLookup(
        IReadOnlyList<string> itemNames,
        IReadOnlyList<string> moveNames,
        IReadOnlyList<string> pokemonNames,
        IReadOnlyList<string> abilityNames,
        IReadOnlyList<string> placeNames,
        IReadOnlyDictionary<string, int> placeNameIndices,
        IReadOnlyList<string> trainerNames,
        IReadOnlyDictionary<string, int> trainerNameIndices,
        IReadOnlyList<string> trainerTypes,
        IReadOnlyDictionary<string, int> trainerTypeIndices)
    {
        this.itemNames = itemNames;
        this.moveNames = moveNames;
        this.pokemonNames = pokemonNames;
        this.abilityNames = abilityNames;
        this.placeNames = placeNames;
        this.placeNameIndices = placeNameIndices;
        this.trainerNames = trainerNames;
        this.trainerNameIndices = trainerNameIndices;
        this.trainerTypes = trainerTypes;
        this.trainerTypeIndices = trainerTypeIndices;
    }

    public static SvTextLabelLookup Load(
        OpenedProject project,
        SvWorkflowFileSource fileSource,
        ICollection<ValidationDiagnostic> diagnostics,
        ProjectPaths? paths = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(fileSource);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var language = paths is null
            ? SvGameTextLanguage.English
            : SvGameTextLanguage.Resolve(paths);

        return new SvTextLabelLookup(
            LoadIndexedTableWithFallback(project, fileSource, language, SvDataPaths.ItemNames, "item names", diagnostics),
            LoadIndexedTableWithFallback(project, fileSource, language, SvDataPaths.MoveNames, "move names", diagnostics),
            LoadIndexedTableWithFallback(project, fileSource, language, SvDataPaths.PokemonNames, "Pokemon names", diagnostics),
            LoadIndexedTableWithFallback(project, fileSource, language, SvDataPaths.AbilityNames, "ability names", diagnostics),
            LoadIndexedTableWithFallback(project, fileSource, language, SvDataPaths.PlaceNames, "place names", diagnostics),
            LoadKeyIndicesWithFallback(project, fileSource, language, SvDataPaths.PlaceNameKeys, "place name keys", diagnostics),
            LoadIndexedTableWithFallback(project, fileSource, language, SvDataPaths.TrainerNames, "trainer names", diagnostics),
            LoadKeyIndicesWithFallback(project, fileSource, language, SvDataPaths.TrainerNameKeys, "trainer name keys", diagnostics),
            LoadIndexedTableWithFallback(project, fileSource, language, SvDataPaths.TrainerTypes, "trainer class names", diagnostics),
            LoadKeyIndicesWithFallback(project, fileSource, language, SvDataPaths.TrainerTypeKeys, "trainer class keys", diagnostics));
    }

    public static SvTextLabelLookup None() => Empty;

    public int ItemNameCount => itemNames.Count;

    public int MoveNameCount => moveNames.Count;

    public int PokemonNameCount => pokemonNames.Count;

    public int AbilityNameCount => abilityNames.Count;

    public string Item(int itemId) => GetIndexed(itemNames, itemId) ?? SvLabels.Item(itemId);

    public string Move(int moveId) => GetIndexed(moveNames, moveId) ?? SvLabels.Move(moveId);

    public string Pokemon(int speciesId) => GetIndexed(pokemonNames, speciesId) ?? SvLabels.Pokemon(speciesId);

    public string Ability(int abilityId) => GetIndexed(abilityNames, abilityId) ?? SvLabels.Ability(abilityId);

    public string? PlaceName(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return GetKeyed(placeNames, placeNameIndices, key)
            ?? GetKeyed(placeNames, placeNameIndices, $"PLACENAME_{key}")
            ?? GetKeyed(placeNames, placeNameIndices, $"PLACENAME_{key}_01");
    }

    public string TrainerName(string? key, int trainerId)
    {
        return GetKeyed(trainerNames, trainerNameIndices, key)
            ?? (!string.IsNullOrWhiteSpace(key) && !key.StartsWith("TRNAME_", StringComparison.OrdinalIgnoreCase)
                ? key
                : $"Trainer {trainerId}");
    }

    public string TrainerType(string? key)
    {
        return GetKeyed(trainerTypes, trainerTypeIndices, key)
            ?? (string.IsNullOrWhiteSpace(key) ? "Trainer" : SvLabels.FormatRawNameForLookup(key));
    }

    private static IReadOnlyList<string> LoadIndexedTableWithFallback(
        OpenedProject project,
        SvWorkflowFileSource fileSource,
        string language,
        Func<string, string> pathFactory,
        string label,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        return TryLoadIndexedTable(project, fileSource, pathFactory(language), label, diagnostics)
            ?? (string.Equals(language, SvGameTextLanguage.English, StringComparison.OrdinalIgnoreCase)
                ? null
                : TryLoadIndexedTable(project, fileSource, pathFactory(SvGameTextLanguage.English), label, diagnostics))
            ?? [];
    }

    private static IReadOnlyDictionary<string, int> LoadKeyIndicesWithFallback(
        OpenedProject project,
        SvWorkflowFileSource fileSource,
        string language,
        Func<string, string> pathFactory,
        string label,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        return TryLoadKeyIndices(project, fileSource, pathFactory(language), label, diagnostics)
            ?? (string.Equals(language, SvGameTextLanguage.English, StringComparison.OrdinalIgnoreCase)
                ? null
                : TryLoadKeyIndices(project, fileSource, pathFactory(SvGameTextLanguage.English), label, diagnostics))
            ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string>? TryLoadIndexedTable(
        OpenedProject project,
        SvWorkflowFileSource fileSource,
        string path,
        string label,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            return SwShGameTextFile.Parse(fileSource.Read(project, path).Bytes)
                .Lines
                .Select(line => line.Text)
                .ToArray();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Warning(
                $"Scarlet/Violet {label} could not be loaded: {exception.Message}",
                $"romfs/{path}"));
            return null;
        }
    }

    private static IReadOnlyDictionary<string, int>? TryLoadKeyIndices(
        OpenedProject project,
        SvWorkflowFileSource fileSource,
        string path,
        string label,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            return SwShAhtbFile.Parse(fileSource.Read(project, path).Bytes)
                .Entries
                .Select((entry, index) => (entry.Name, index))
                .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().index, StringComparer.OrdinalIgnoreCase);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Warning(
                $"Scarlet/Violet {label} could not be loaded: {exception.Message}",
                $"romfs/{path}"));
            return null;
        }
    }

    private static string? GetIndexed(IReadOnlyList<string> values, int index)
    {
        return index >= 0
            && index < values.Count
            && !string.IsNullOrWhiteSpace(values[index])
                ? values[index]
                : null;
    }

    private static string? GetKeyed(
        IReadOnlyList<string> values,
        IReadOnlyDictionary<string, int> indices,
        string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || !indices.TryGetValue(key, out var index))
        {
            return null;
        }

        return GetIndexed(values, index);
    }
}
