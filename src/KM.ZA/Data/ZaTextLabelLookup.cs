// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.ZA.Workflows;
using System.Globalization;

namespace KM.ZA.Data;

internal sealed class ZaTextLabelLookup
{
    private static readonly IReadOnlySet<string> GenericTrainerNameLabels =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Boy",
            "Girl",
            "Little Boy",
            "Little Girl",
            "Man",
            "Old Man",
            "Old Woman",
            "Woman",
            "Young Lady",
            "Young Man",
        };

    private static readonly ZaTextLabelLookup Empty = new(
        [],
        [],
        [],
        [],
        [],
        [],
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        [],
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        [],
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<ulong, int>());

    private readonly IReadOnlyList<string> itemNames;
    private readonly IReadOnlyList<string> moveNames;
    private readonly IReadOnlyList<string> moveDescriptions;
    private readonly IReadOnlyList<string> pokemonNames;
    private readonly IReadOnlyList<string> abilityNames;
    private readonly IReadOnlyList<string> placeNames;
    private readonly IReadOnlyDictionary<string, int> placeNameIndices;
    private readonly IReadOnlyList<string> trainerNames;
    private readonly IReadOnlyDictionary<string, int> trainerNameIndices;
    private readonly IReadOnlyList<string> trainerTypes;
    private readonly IReadOnlyDictionary<string, int> trainerTypeIndices;
    private readonly IReadOnlyDictionary<ulong, int> trainerTypeHashIndices;

    private ZaTextLabelLookup(
        IReadOnlyList<string> itemNames,
        IReadOnlyList<string> moveNames,
        IReadOnlyList<string> moveDescriptions,
        IReadOnlyList<string> pokemonNames,
        IReadOnlyList<string> abilityNames,
        IReadOnlyList<string> placeNames,
        IReadOnlyDictionary<string, int> placeNameIndices,
        IReadOnlyList<string> trainerNames,
        IReadOnlyDictionary<string, int> trainerNameIndices,
        IReadOnlyList<string> trainerTypes,
        IReadOnlyDictionary<string, int> trainerTypeIndices,
        IReadOnlyDictionary<ulong, int> trainerTypeHashIndices)
    {
        this.itemNames = itemNames;
        this.moveNames = moveNames;
        this.moveDescriptions = moveDescriptions;
        this.pokemonNames = pokemonNames;
        this.abilityNames = abilityNames;
        this.placeNames = placeNames;
        this.placeNameIndices = placeNameIndices;
        this.trainerNames = trainerNames;
        this.trainerNameIndices = trainerNameIndices;
        this.trainerTypes = trainerTypes;
        this.trainerTypeIndices = trainerTypeIndices;
        this.trainerTypeHashIndices = trainerTypeHashIndices;
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
            LoadIndexedTableWithFallback(project, fileSource, language, ZaDataPaths.MoveDescriptions, "move descriptions", diagnostics),
            LoadIndexedTableWithFallback(project, fileSource, language, ZaDataPaths.PokemonNames, "Pokemon names", diagnostics),
            LoadIndexedTableWithFallback(project, fileSource, language, ZaDataPaths.AbilityNames, "ability names", diagnostics),
            LoadIndexedTableWithFallback(project, fileSource, language, ZaDataPaths.PlaceNames, "place names", diagnostics),
            LoadKeyIndicesWithFallback(project, fileSource, language, ZaDataPaths.PlaceNameKeys, "place name keys", diagnostics),
            LoadIndexedTableWithFallback(project, fileSource, language, ZaDataPaths.TrainerNames, "trainer names", diagnostics),
            LoadKeyIndicesWithFallback(project, fileSource, language, ZaDataPaths.TrainerNameKeys, "trainer name keys", diagnostics),
            LoadIndexedTableWithFallback(project, fileSource, language, ZaDataPaths.TrainerTypes, "trainer class names", diagnostics),
            LoadKeyIndicesWithFallback(project, fileSource, language, ZaDataPaths.TrainerTypeKeys, "trainer class keys", diagnostics),
            LoadKeyHashIndicesWithFallback(project, fileSource, language, ZaDataPaths.TrainerTypeKeys, "trainer class keys", diagnostics));
    }

    public string Item(int itemId) => GetIndexed(itemNames, itemId) ?? ZaLabels.Item(itemId);

    public string Move(int moveId)
    {
        var indexed = GetIndexed(moveNames, moveId);
        var fallback = ZaLabels.Move(moveId);
        return string.Equals(indexed, $"Move {moveId.ToString(System.Globalization.CultureInfo.InvariantCulture)}", StringComparison.Ordinal)
            ? fallback
            : indexed ?? fallback;
    }

    public string? MoveDescription(int moveId) => GetIndexed(moveDescriptions, moveId);

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

    public string TrainerName(string? key, int trainerId, string? trainerClass = null)
    {
        return TrainerNameFromText(key, trainerId)
            ?? (!string.IsNullOrWhiteSpace(key) && !key.StartsWith("TRNAME_", StringComparison.OrdinalIgnoreCase)
                ? ZaLabels.FormatTrainerIdForLookup(key)
                : $"Trainer {trainerId}");
    }

    public string? TrainerNameFromText(string? key, int trainerId)
    {
        var keyed = FirstUsableTrainerName(TrainerNameKeyCandidates(key)
            .Select(candidate => GetKeyed(trainerNames, trainerNameIndices, candidate))
            .ToArray());
        if (!string.IsNullOrWhiteSpace(key) || !string.IsNullOrWhiteSpace(keyed))
        {
            return keyed;
        }

        return FirstUsableTrainerName(GetIndexed(trainerNames, trainerId));
    }

    public string TrainerType(string? key)
    {
        return GetKeyed(trainerTypes, trainerTypeIndices, key)
            ?? (string.IsNullOrWhiteSpace(key) ? "Trainer" : ZaLabels.FormatRawNameForLookup(key));
    }

    public string TrainerNameByIndex(int trainerId)
    {
        return GetIndexed(trainerNames, trainerId)
            ?? $"Trainer {trainerId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    public string? TrainerTypeByIndex(int trainerTypeId)
    {
        return GetIndexed(trainerTypes, trainerTypeId);
    }

    public (int Id, string Name) TrainerTypeByHash(ulong primaryHash, ulong secondaryHash)
    {
        if (TryGetTrainerTypeByHash(primaryHash, out var trainerType))
        {
            return trainerType;
        }

        if (secondaryHash != primaryHash
            && TryGetTrainerTypeByHash(secondaryHash, out trainerType))
        {
            return trainerType;
        }

        if (primaryHash <= int.MaxValue
            && TryGetTrainerTypeByIndex((int)primaryHash, out trainerType))
        {
            return trainerType;
        }

        if (secondaryHash <= int.MaxValue
            && secondaryHash != primaryHash
            && TryGetTrainerTypeByIndex((int)secondaryHash, out trainerType))
        {
            return trainerType;
        }

        return (-1, "Trainer");
    }

    private static IReadOnlyList<string> LoadIndexedTableWithFallback(
        OpenedProject project,
        ZaWorkflowFileSource fileSource,
        string language,
        Func<string, string> pathFactory,
        string label,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        return TryLoadIndexedTable(project, fileSource, CreatePathCandidates(language, pathFactory), label, diagnostics)
            ?? (string.Equals(language, ZaGameTextLanguage.English, StringComparison.OrdinalIgnoreCase)
                ? null
                : TryLoadIndexedTable(project, fileSource, CreatePathCandidates(ZaGameTextLanguage.English, pathFactory), label, diagnostics))
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
        return TryLoadKeyIndices(project, fileSource, CreatePathCandidates(language, pathFactory), label, diagnostics)
            ?? (string.Equals(language, ZaGameTextLanguage.English, StringComparison.OrdinalIgnoreCase)
                ? null
                : TryLoadKeyIndices(project, fileSource, CreatePathCandidates(ZaGameTextLanguage.English, pathFactory), label, diagnostics))
            ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<ulong, int> LoadKeyHashIndicesWithFallback(
        OpenedProject project,
        ZaWorkflowFileSource fileSource,
        string language,
        Func<string, string> pathFactory,
        string label,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        return TryLoadKeyHashIndices(project, fileSource, CreatePathCandidates(language, pathFactory), label, diagnostics)
            ?? (string.Equals(language, ZaGameTextLanguage.English, StringComparison.OrdinalIgnoreCase)
                ? null
                : TryLoadKeyHashIndices(project, fileSource, CreatePathCandidates(ZaGameTextLanguage.English, pathFactory), label, diagnostics))
            ?? new Dictionary<ulong, int>();
    }

    private static IReadOnlyList<string> CreatePathCandidates(string language, Func<string, string> pathFactory)
    {
        var path = pathFactory(language);
        var legacyPath = ZaDataPaths.TryCreateLegacyMessagePath(path);
        return string.IsNullOrWhiteSpace(legacyPath)
            || string.Equals(path, legacyPath, StringComparison.OrdinalIgnoreCase)
            ? [path]
            : [path, legacyPath];
    }

    private static IReadOnlyList<string>? TryLoadIndexedTable(
        OpenedProject project,
        ZaWorkflowFileSource fileSource,
        IReadOnlyList<string> paths,
        string label,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var path in paths)
        {
            var values = TryLoadIndexedTable(project, fileSource, path, label, diagnostics);
            if (values is not null)
            {
                return values;
            }
        }

        return null;
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
        IReadOnlyList<string> paths,
        string label,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var path in paths)
        {
            var values = TryLoadKeyIndices(project, fileSource, path, label, diagnostics);
            if (values is not null)
            {
                return values;
            }
        }

        return null;
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

    private static IReadOnlyDictionary<ulong, int>? TryLoadKeyHashIndices(
        OpenedProject project,
        ZaWorkflowFileSource fileSource,
        IReadOnlyList<string> paths,
        string label,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var path in paths)
        {
            var values = TryLoadKeyHashIndices(project, fileSource, path, label, diagnostics);
            if (values is not null)
            {
                return values;
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<ulong, int>? TryLoadKeyHashIndices(
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
                .Select((entry, index) => (entry.Hash, index))
                .GroupBy(entry => entry.Hash)
                .ToDictionary(group => group.Key, group => group.First().index);
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

    private static IReadOnlyList<string> TrainerNameKeyCandidates(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return [];
        }

        var candidates = new List<string> { key };
        if (!key.StartsWith("TRNAME_", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add($"TRNAME_{key}");
            AddTrimmedTrainerNameCandidate(candidates, key, "TR_");
            AddTrimmedTrainerNameCandidate(candidates, key, "TRAINER_");
        }

        AddTrainerNamePatternCandidates(candidates, key);

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddTrimmedTrainerNameCandidate(ICollection<string> candidates, string key, string prefix)
    {
        if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && key.Length > prefix.Length)
        {
            candidates.Add($"TRNAME_{key[prefix.Length..]}");
        }
    }

    private static void AddTrainerNamePatternCandidates(ICollection<string> candidates, string key)
    {
        AddDimensionTrainerNameCandidate(candidates, key);
        AddRestaurantTrainerNameCandidate(candidates, key);
        AddSubquestTrainerNameCandidates(candidates, key);
    }

    private static void AddDimensionTrainerNameCandidate(ICollection<string> candidates, string key)
    {
        var parts = key.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 5
            && parts[0].Equals("dim", StringComparison.OrdinalIgnoreCase)
            && parts[1].Equals("rank", StringComparison.OrdinalIgnoreCase)
            && IsDigits(parts[2])
            && IsDigits(parts[4]))
        {
            candidates.Add($"dim_rank_{parts[2]}_{parts[4]}");
        }
    }

    private static void AddRestaurantTrainerNameCandidate(ICollection<string> candidates, string key)
    {
        const string prefix = "Ev_sys_";
        if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(key[prefix.Length..]);
        }
    }

    private static void AddSubquestTrainerNameCandidates(ICollection<string> candidates, string key)
    {
        const string prefix = "Ev_sub_";
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var rest = key[prefix.Length..];
        if (rest.Length < 3 || !IsDigits(rest[..3]))
        {
            return;
        }

        var baseKey = $"sub_{rest[..3]}";
        var suffix = rest.Length > 3
            ? rest[3..].TrimStart('_')
            : string.Empty;

        AddSubquestVariantCandidates(candidates, baseKey, suffix);
        candidates.Add(baseKey);
    }

    private static void AddSubquestVariantCandidates(ICollection<string> candidates, string baseKey, string suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return;
        }

        var parts = suffix.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return;
        }

        var last = parts[^1];
        if (last.Equals("manager", StringComparison.OrdinalIgnoreCase))
        {
            AddNumberedTrainerNameCandidate(candidates, baseKey, 1);
            return;
        }

        if (last.Equals("master", StringComparison.OrdinalIgnoreCase))
        {
            AddNumberedTrainerNameCandidate(candidates, baseKey, 2);
            return;
        }

        if (last.Equals("client", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (last.Equals("nageki", StringComparison.OrdinalIgnoreCase))
        {
            AddNumberedTrainerNameCandidate(candidates, baseKey, 1);
            return;
        }

        if (last.Equals("dageki", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (IsDigits(last) && int.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var lastNumber))
        {
            AddNumberedTrainerNameCandidate(candidates, baseKey, lastNumber);
        }

        var first = parts[0];
        if (!IsDigits(first) || !int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var firstNumber))
        {
            return;
        }

        var variant = firstNumber >= 100 && firstNumber % 100 == 10
            ? (firstNumber / 100) + 1
            : firstNumber / 10;
        AddNumberedTrainerNameCandidate(candidates, baseKey, variant);
        if (variant > 1)
        {
            AddNumberedTrainerNameCandidate(candidates, baseKey, variant - 1);
        }
    }

    private static void AddNumberedTrainerNameCandidate(ICollection<string> candidates, string baseKey, int number)
    {
        if (number > 0)
        {
            candidates.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{baseKey}_{number:00}"));
        }
    }

    private static bool IsDigits(string value)
    {
        return value.Length > 0 && value.All(char.IsDigit);
    }

    private bool TryGetTrainerTypeByHash(ulong hash, out (int Id, string Name) trainerType)
    {
        trainerType = default;
        if (!trainerTypeHashIndices.TryGetValue(hash, out var index))
        {
            return false;
        }

        return TryGetTrainerTypeByIndex(index, out trainerType);
    }

    private bool TryGetTrainerTypeByIndex(int index, out (int Id, string Name) trainerType)
    {
        trainerType = default;
        var name = GetIndexed(trainerTypes, index);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        trainerType = (index, name);
        return true;
    }

    private static string? FirstUsableTrainerName(params string?[] values)
    {
        return values.FirstOrDefault(value =>
            !IsPlaceholderLabel(value)
            && !IsGenericTrainerNameLabel(value));
    }

    private static bool IsPlaceholderLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();
        return string.Equals(trimmed, "-", StringComparison.Ordinal)
            || trimmed.All(character => character == '?');
    }

    private static bool IsGenericTrainerNameLabel(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && GenericTrainerNameLabels.Contains(value.Trim());
    }
}
