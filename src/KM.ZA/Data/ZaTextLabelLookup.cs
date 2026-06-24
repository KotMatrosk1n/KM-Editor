// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.ZA.Workflows;

namespace KM.ZA.Data;

internal sealed class ZaTextLabelLookup
{
    private static readonly ZaTextLabelLookup Empty = new(
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

    private ZaTextLabelLookup(
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

    public int ItemNameCount => itemNames.Count;
    public int MoveNameCount => moveNames.Count;
    public int PokemonNameCount => pokemonNames.Count;
    public int AbilityNameCount => abilityNames.Count;

    public static ZaTextLabelLookup None() => Empty;

    public static ZaTextLabelLookup Load(
        OpenedProject project,
        ZaWorkflowFileSource fileSource,
        ICollection<ValidationDiagnostic> diagnostics,
        ProjectPaths? paths = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(fileSource);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var language = paths is null
            ? ZaGameTextLanguage.English
            : ZaGameTextLanguage.Resolve(paths);

        return new ZaTextLabelLookup(
            LoadIndexedTableWithFallback(project, fileSource, language, ZaDataPaths.ItemNames, "item names", diagnostics),
            LoadIndexedTableWithFallback(project, fileSource, language, ZaDataPaths.MoveNames, "move names", diagnostics),
            LoadIndexedTableWithFallback(project, fileSource, language, ZaDataPaths.PokemonNames, "Pokemon names", diagnostics),
            LoadIndexedTableWithFallback(project, fileSource, language, ZaDataPaths.AbilityNames, "ability names", diagnostics),
            LoadIndexedTableWithFallback(project, fileSource, language, ZaDataPaths.PlaceNames, "place names", diagnostics),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            LoadIndexedTableWithFallback(project, fileSource, language, ZaDataPaths.TrainerNames, "trainer names", diagnostics),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            LoadIndexedTableWithFallback(project, fileSource, language, ZaDataPaths.TrainerTypes, "trainer class names", diagnostics),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
    }

    public string Item(int itemId) => GetIndexed(itemNames, itemId) ?? ZaLabels.Item(itemId);

    public string Move(int moveId) => GetIndexed(moveNames, moveId) ?? ZaLabels.Move(moveId);

    public string Pokemon(int speciesId) => GetIndexed(pokemonNames, speciesId) ?? ZaLabels.Pokemon(speciesId);

    public string Ability(int abilityId) => GetIndexed(abilityNames, abilityId) ?? ZaLabels.Ability(abilityId);

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
            ?? (string.IsNullOrWhiteSpace(key) ? "Trainer" : ZaLabels.FormatRawNameForLookup(key));
    }

    private static IReadOnlyList<string> LoadIndexedTableWithFallback(
        OpenedProject project,
        ZaWorkflowFileSource fileSource,
        string language,
        Func<string, string> pathFactory,
        string label,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        return TryLoadIndexedTable(project, fileSource, pathFactory(language), label, diagnostics)
            ?? (string.Equals(language, ZaGameTextLanguage.English, StringComparison.OrdinalIgnoreCase)
                ? null
                : TryLoadIndexedTable(project, fileSource, pathFactory(ZaGameTextLanguage.English), label, diagnostics))
            ?? [];
    }

    private static IReadOnlyDictionary<string, int> LoadKeyIndicesWithFallback(
        OpenedProject project,
        ZaWorkflowFileSource fileSource,
        string language,
        Func<string, string> pathFactory,
        string label,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        return TryLoadKeyIndices(project, fileSource, pathFactory(language), label, diagnostics)
            ?? (string.Equals(language, ZaGameTextLanguage.English, StringComparison.OrdinalIgnoreCase)
                ? null
                : TryLoadKeyIndices(project, fileSource, pathFactory(ZaGameTextLanguage.English), label, diagnostics))
            ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string>? TryLoadIndexedTable(
        OpenedProject project,
        ZaWorkflowFileSource fileSource,
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
            diagnostics.Add(ZaWorkflowSupport.Warning(
                $"Pokemon Legends Z-A {label} could not be loaded: {exception.Message}",
                $"romfs/{path}"));
            return null;
        }
    }

    private static IReadOnlyDictionary<string, int>? TryLoadKeyIndices(
        OpenedProject project,
        ZaWorkflowFileSource fileSource,
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
            diagnostics.Add(ZaWorkflowSupport.Warning(
                $"Pokemon Legends Z-A {label} could not be loaded: {exception.Message}",
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
