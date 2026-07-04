// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.ZA.Generated.Field.PokemonSpawner;
using KM.ZA.Data;
using KM.ZA.Workflows;
using System.Globalization;

namespace KM.ZA.Encounters;

internal sealed class ZaEncountersWorkflowService
{
    public const string SpeciesIdField = "speciesId";
    public const string FormField = "form";
    public const string LevelMinField = "levelMin";
    public const string LevelMaxField = "levelMax";

    private const string WorkflowLabel = "Wild Encounters";
    private const string WorkflowDescription = "Edit Pokemon Legends Z-A wild encounter Pokemon rows.";
    private const string GameVersionLabel = "Pokemon Legends ZA";
    private const string TableIdPrefix = "za-spawner";
    private static readonly string[] EncounterDataIdSuffixes =
    [
        "_Alpha",
    ];

    private readonly ZaWorkflowFileSource fileSource;

    public ZaEncountersWorkflowService(ZaWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new ZaWorkflowFileSource();
    }

    public ZaWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.Encounters,
            WorkflowLabel,
            WorkflowDescription);
    }

    public ZaEncountersWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        ZaWorkflowFile? encounterSource = null;
        ZaWorkflowFile? spawnerSource = null;
        var labels = ZaTextLabelLookup.None();
        var pokemonAvailability = ZaPokemonAvailability.Unfiltered;
        var tables = Array.Empty<ZaEncounterTableRecord>();

        try
        {
            labels = ZaTextLabelLookup.Load(project, fileSource, diagnostics, project.Paths);
            pokemonAvailability = ZaPokemonAvailability.Load(project, fileSource, diagnostics, WorkflowLabel);
            encounterSource = fileSource.Read(project, ZaDataPaths.EncountDataArray);
            spawnerSource = fileSource.Read(project, ZaDataPaths.PokemonSpawnerDataArray);
            tables = LoadTables(spawnerSource, encounterSource, labels).ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Error(
                $"Wild Encounters could not be loaded: {exception.Message}",
                $"romfs/{ZaDataPaths.EncountDataArray}"));
        }

        var summary = ZaWorkflowSupport.CreateSummary(
            project,
            ZaWorkflowIds.Encounters,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        return new ZaEncountersWorkflow(
            summary,
            tables,
            CreateEditableFields(labels, pokemonAvailability),
            new ZaEncountersWorkflowStats(
                tables.Length,
                tables.Sum(table => table.Slots.Count),
                new[] { encounterSource, spawnerSource }.Count(source => source is not null)),
            diagnostics);
    }

    internal static ZaEncounterEditableField? GetEditableField(
        ZaEncountersWorkflow workflow,
        string? field)
    {
        return workflow.EditableFields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
    }

    internal static string CreateSlotRecordId(string tableId, int slot)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{tableId}#{slot}");
    }

    internal static bool TryParseSlotRecordId(string? recordId, out string tableId, out int slot)
    {
        tableId = string.Empty;
        slot = -1;

        var separatorIndex = recordId?.LastIndexOf('#') ?? -1;
        if (separatorIndex <= 0 || separatorIndex >= recordId!.Length - 1)
        {
            return false;
        }

        tableId = recordId[..separatorIndex];
        return int.TryParse(recordId[(separatorIndex + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out slot)
            && slot >= 0;
    }

    internal static string FormatEncounterSpeciesLabel(int speciesId, int form, ZaTextLabelLookup labels)
    {
        return FormatEncounterSpeciesLabel(speciesId, form, labels.Pokemon(speciesId));
    }

    internal static string FormatEncounterSpeciesLabel(int speciesId, int form, string speciesName)
    {
        return ZaLabels.PokemonWithForm(speciesId, form, speciesName);
    }

    private static IEnumerable<ZaEncounterTableRecord> LoadTables(
        ZaWorkflowFile spawnerSource,
        ZaWorkflowFile encounterSource,
        ZaTextLabelLookup labels)
    {
        var pokemonRows = ZaPokemonDataDocument.Parse(encounterSource.Bytes)
            .Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .GroupBy(entry => entry.Id!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var table = PokemonSpawnerDataDBArray.GetRootAsPokemonSpawnerDataDBArray(new ByteBuffer(spawnerSource.Bytes));
        var tableCountsByLocation = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var groupIndex = 0; groupIndex < table.ValuesLength; groupIndex++)
        {
            var db = table.Values(groupIndex);
            if (db is null)
            {
                continue;
            }

            for (var spawnerIndex = 0; spawnerIndex < db.Value.RootLength; spawnerIndex++)
            {
                var spawner = db.Value.Root(spawnerIndex);
                if (spawner is null || spawner.Value.EncountDataInfoListLength == 0)
                {
                    continue;
                }

                var slots = ReadSlots(spawner.Value, pokemonRows, encounterSource, labels).ToArray();
                if (slots.Length == 0)
                {
                    continue;
                }

                var locationKey = FormatLocationKey(spawner.Value);
                var location = FormatLocation(locationKey, labels);
                var tableNumber = NextTableNumber(tableCountsByLocation, locationKey);
                yield return new ZaEncounterTableRecord(
                    CreateTableId(groupIndex, spawnerIndex),
                    location,
                    FormatArea(spawner.Value),
                    FormatEncounterType(spawner.Value),
                    GameVersionLabel,
                    spawnerSource.RelativePath,
                    slots,
                    new ZaEncounterProvenance(
                        spawnerSource.RelativePath,
                        spawnerSource.SourceLayer,
                        spawnerSource.FileState),
                    locationKey,
                    GetLocationSort(locationKey),
                    FormatTableLabel(locationKey, tableNumber, spawner.Value.Id, labels),
                    FormatTableDetails(slots));
            }
        }
    }

    private static IEnumerable<ZaEncounterSlotRecord> ReadSlots(
        PokemonSpawnerData spawner,
        IReadOnlyDictionary<string, ZaPokemonDataEntry> pokemonRows,
        ZaWorkflowFile encounterSource,
        ZaTextLabelLookup labels)
    {
        for (var slot = 0; slot < spawner.EncountDataInfoListLength; slot++)
        {
            var encounter = spawner.EncountDataInfoList(slot);
            if (encounter is null)
            {
                continue;
            }

            var encounterDataId = encounter.Value.EncountDataId ?? string.Empty;
            var isAlpha = IsAlphaEncounter(encounterDataId);
            var pokemon = ResolvePokemonRow(encounterDataId, pokemonRows);
            var speciesId = pokemon?.DevNo ?? 0;
            var form = pokemon?.FormNo ?? 0;
            yield return new ZaEncounterSlotRecord(
                slot,
                pokemon?.SourceIndex ?? -1,
                encounterDataId,
                speciesId,
                pokemon is null
                    ? FormatUnresolvedEncounterData(encounterDataId)
                    : FormatEncounterSpeciesLabel(speciesId, form, labels),
                form,
                pokemon?.MinLevel ?? 0,
                pokemon?.MaxLevel ?? 0,
                encounter.Value.Weight,
                FormatTimeCondition(encounter.Value.AppearedTimeCondition),
                FormatWeatherCondition(encounter.Value.AppearedWeatherCondition, encounter.Value),
                isAlpha,
                isAlpha ? "Alpha" : "Wild",
                new ZaEncounterProvenance(
                    encounterSource.RelativePath,
                    encounterSource.SourceLayer,
                    encounterSource.FileState));
        }
    }

    private static ZaPokemonDataEntry? ResolvePokemonRow(
        string encounterDataId,
        IReadOnlyDictionary<string, ZaPokemonDataEntry> pokemonRows)
    {
        if (string.IsNullOrWhiteSpace(encounterDataId))
        {
            return null;
        }

        if (pokemonRows.TryGetValue(encounterDataId, out var exactRow))
        {
            return exactRow;
        }

        foreach (var suffix in EncounterDataIdSuffixes)
        {
            if (encounterDataId.EndsWith(suffix, StringComparison.Ordinal)
                && pokemonRows.TryGetValue(encounterDataId[..^suffix.Length], out var suffixedRow))
            {
                return suffixedRow;
            }
        }

        return null;
    }

    private static bool IsAlphaEncounter(string encounterDataId)
    {
        return EncounterDataIdSuffixes.Any(suffix =>
            encounterDataId.EndsWith(suffix, StringComparison.Ordinal));
    }

    private static string FormatUnresolvedEncounterData(string encounterDataId)
    {
        return string.IsNullOrWhiteSpace(encounterDataId)
            ? "Unresolved encounter data"
            : $"Unresolved encounter data ({encounterDataId})";
    }

    private static IReadOnlyList<ZaEncounterEditableField> CreateEditableFields(
        ZaTextLabelLookup labels,
        ZaPokemonAvailability pokemonAvailability)
    {
        var speciesOptions = CreateSpeciesOptions(labels, pokemonAvailability);
        var speciesMaximumValue = Math.Max(
            labels.PokemonNameCount - 1,
            speciesOptions.Count > 0 ? speciesOptions.Max(option => option.Value) : 0);
        return
        [
            new(
                SpeciesIdField,
                "Species",
                "integer",
                0,
                speciesMaximumValue,
                speciesOptions),
            new(FormField, "Form", "integer", 0, short.MaxValue, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(LevelMinField, "Min Level", "integer", 0, 100, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(LevelMaxField, "Max Level", "integer", 0, 100, Array.Empty<ZaEncounterEditableFieldOption>()),
        ];
    }

    private static IReadOnlyList<ZaEncounterEditableFieldOption> CreateIndexedOptions(
        int count,
        Func<int, string> resolveName,
        bool includeNone)
    {
        var firstValue = includeNone ? 0 : 1;
        if (count <= firstValue)
        {
            return includeNone ? [new(0, "0 None")] : [];
        }

        return Enumerable
            .Range(firstValue, count - firstValue)
            .Select(value =>
            {
                var label = value == 0 ? "None" : resolveName(value);
                return new ZaEncounterEditableFieldOption(
                    value,
                    $"{value.ToString(CultureInfo.InvariantCulture)} {label}");
            })
            .ToArray();
    }

    private static IReadOnlyList<ZaEncounterEditableFieldOption> CreateSpeciesOptions(
        ZaTextLabelLookup labels,
        ZaPokemonAvailability pokemonAvailability)
    {
        return pokemonAvailability
            .CreateSpeciesOptions(labels.PokemonNameCount, labels.Pokemon, includeNone: true)
            .Select(option => new ZaEncounterEditableFieldOption(option.Value, option.Label))
            .ToArray();
    }

    private static string CreateTableId(int groupIndex, int spawnerIndex)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{TableIdPrefix}:{groupIndex}:{spawnerIndex}");
    }

    private static string FormatLocationKey(PokemonSpawnerData spawner)
    {
        var objectInfo = FirstAppearanceObject(spawner);
        var zoneInfo = objectInfo?.ZoneInfo;
        return ZaLumioseLocationLabels.CreateLocationKey(
            zoneInfo?.ZoneId,
            zoneInfo?.VariationId,
            spawner.Id);
    }

    private static string FormatLocation(string locationKey, ZaTextLabelLookup labels)
    {
        return ZaLumioseLocationLabels.FormatLocation(locationKey, labels.PlaceName, labels.Pokemon);
    }

    private static int NextTableNumber(IDictionary<string, int> tableCountsByLocation, string locationKey)
    {
        tableCountsByLocation.TryGetValue(locationKey, out var current);
        var next = current + 1;
        tableCountsByLocation[locationKey] = next;
        return next;
    }

    private static int? GetLocationSort(string locationKey)
    {
        return ZaLumioseLocationLabels.GetLocationSort(locationKey);
    }

    private static bool IsNumberedWildZone(string locationKey)
    {
        return ZaLumioseLocationLabels.IsNumberedWildZone(locationKey);
    }

    private static string FormatTableLabel(string locationKey, int tableNumber, string? spawnerId, ZaTextLabelLookup labels)
    {
        if (IsNumberedWildZone(locationKey))
        {
            return $"Spawner {tableNumber.ToString(CultureInfo.InvariantCulture)}";
        }

        return string.IsNullOrWhiteSpace(spawnerId)
            ? $"Spawner {tableNumber.ToString(CultureInfo.InvariantCulture)}"
            : ZaLumioseLocationLabels.FormatRawSpawnerId(spawnerId, labels.Pokemon);
    }

    private static string FormatTableDetails(IReadOnlyList<ZaEncounterSlotRecord> slots)
    {
        if (slots.Count == 0)
        {
            return "No slots";
        }

        var species = slots
            .Select(FormatSlotPreviewSpecies)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToArray();
        var speciesLabel = species.Length == 0 ? "No species" : string.Join(", ", species);
        var additionalCount = slots
            .Select(FormatSlotPreviewSpecies)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.Ordinal)
            .Skip(3)
            .Count();
        if (additionalCount > 0)
        {
            speciesLabel = $"{speciesLabel} + {additionalCount.ToString(CultureInfo.InvariantCulture)} more";
        }

        var slotLabel = slots.Count == 1 ? "slot" : "slots";
        var weightTotal = slots.Sum(slot => slot.Weight);
        var alphaCount = slots.Count(slot => slot.IsAlpha);
        var alphaLabel = alphaCount == 0
            ? string.Empty
            : $" - {alphaCount.ToString(CultureInfo.InvariantCulture)} Alpha";
        return $"{speciesLabel} - {slots.Count.ToString(CultureInfo.InvariantCulture)} {slotLabel} - total {weightTotal.ToString(CultureInfo.InvariantCulture)}{alphaLabel}";
    }

    private static string FormatSlotPreviewSpecies(ZaEncounterSlotRecord slot)
    {
        return slot.IsAlpha ? $"{slot.Species} Alpha" : slot.Species;
    }

    private static string FormatArea(PokemonSpawnerData spawner)
    {
        var objectInfo = FirstAppearanceObject(spawner);
        if (!string.IsNullOrWhiteSpace(objectInfo?.DungeonName))
        {
            return objectInfo.Value.DungeonName;
        }

        if (!string.IsNullOrWhiteSpace(objectInfo?.BattleAreaId))
        {
            return objectInfo.Value.BattleAreaId;
        }

        return "Pokemon Spawner";
    }

    private static string FormatEncounterType(PokemonSpawnerData spawner)
    {
        var objectInfo = FirstAppearanceObject(spawner);
        if (objectInfo is null || objectInfo.Value.TagListLength == 0)
        {
            return "Wild Pokemon";
        }

        var tags = Enumerable
            .Range(0, objectInfo.Value.TagListLength)
            .Select(objectInfo.Value.TagList)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return tags.Length == 0 ? "Wild Pokemon" : string.Join(", ", tags);
    }

    private static AppearanceSpawnerObjectInfo? FirstAppearanceObject(PokemonSpawnerData spawner)
    {
        for (var index = 0; index < spawner.AppearanceSpawnerObjectInfoListLength; index++)
        {
            var objectInfo = spawner.AppearanceSpawnerObjectInfoList(index);
            if (objectInfo is not null)
            {
                return objectInfo;
            }
        }

        return null;
    }

    private static string? FormatTimeCondition(int value)
    {
        return value switch
        {
            0 => null,
            1 => "Morning",
            2 => "Day",
            3 => "Evening",
            4 => "Night",
            _ => $"Time condition {value.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    private static string FormatWeatherCondition(int value, EncountDataInfo encounter)
    {
        var weather = value switch
        {
            0 => "Any weather",
            1 => "Clear",
            2 => "Rain",
            3 => "Snow",
            4 => "Fog",
            _ => $"Weather condition {value.ToString(CultureInfo.InvariantCulture)}",
        };

        if (encounter.TagListLength == 0)
        {
            return weather;
        }

        var tags = Enumerable
            .Range(0, encounter.TagListLength)
            .Select(encounter.TagList)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return tags.Length == 0 ? weather : $"{weather}; {string.Join(", ", tags)}";
    }
}
