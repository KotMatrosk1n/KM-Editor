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
        ZaWorkflowFile? pokemonSource = null;
        ZaWorkflowFile? spawnerSource = null;
        var labels = ZaTextLabelLookup.None();
        var tables = Array.Empty<ZaEncounterTableRecord>();

        try
        {
            labels = ZaTextLabelLookup.Load(project, fileSource, diagnostics, project.Paths);
            pokemonSource = fileSource.Read(project, ZaDataPaths.PokemonDataArray);
            spawnerSource = fileSource.Read(project, ZaDataPaths.PokemonSpawnerDataArray);
            tables = LoadTables(spawnerSource, pokemonSource, labels).ToArray();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(ZaWorkflowSupport.Error(
                $"Wild Encounters could not be loaded: {exception.Message}",
                $"romfs/{ZaDataPaths.PokemonSpawnerDataArray}"));
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
            CreateEditableFields(labels),
            new ZaEncountersWorkflowStats(
                tables.Length,
                tables.Sum(table => table.Slots.Count),
                new[] { pokemonSource, spawnerSource }.Count(source => source is not null)),
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
        if (speciesId == 0)
        {
            return "Empty";
        }

        return form == 0
            ? speciesName
            : string.Create(CultureInfo.InvariantCulture, $"{speciesName} (Form {form})");
    }

    private static IEnumerable<ZaEncounterTableRecord> LoadTables(
        ZaWorkflowFile spawnerSource,
        ZaWorkflowFile pokemonSource,
        ZaTextLabelLookup labels)
    {
        var pokemonRows = ZaPokemonDataDocument.Parse(pokemonSource.Bytes)
            .Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .GroupBy(entry => entry.Id!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var table = PokemonSpawnerDataDBArray.GetRootAsPokemonSpawnerDataDBArray(new ByteBuffer(spawnerSource.Bytes));
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

                var slots = ReadSlots(spawner.Value, pokemonRows, pokemonSource, labels).ToArray();
                if (slots.Length == 0)
                {
                    continue;
                }

                yield return new ZaEncounterTableRecord(
                    CreateTableId(groupIndex, spawnerIndex),
                    FormatLocation(spawner.Value),
                    FormatArea(spawner.Value),
                    FormatEncounterType(spawner.Value),
                    GameVersionLabel,
                    spawnerSource.RelativePath,
                    slots,
                    new ZaEncounterProvenance(
                        spawnerSource.RelativePath,
                        spawnerSource.SourceLayer,
                        spawnerSource.FileState));
            }
        }
    }

    private static IEnumerable<ZaEncounterSlotRecord> ReadSlots(
        PokemonSpawnerData spawner,
        IReadOnlyDictionary<string, ZaPokemonDataEntry> pokemonRows,
        ZaWorkflowFile pokemonSource,
        ZaTextLabelLookup labels)
    {
        for (var slot = 0; slot < spawner.EncountDataInfoListLength; slot++)
        {
            var encounter = spawner.EncountDataInfoList(slot);
            if (encounter is null)
            {
                continue;
            }

            pokemonRows.TryGetValue(encounter.Value.EncountDataId ?? string.Empty, out var pokemon);
            var speciesId = pokemon?.DevNo ?? 0;
            var form = pokemon?.FormNo ?? 0;
            yield return new ZaEncounterSlotRecord(
                slot,
                pokemon?.SourceIndex ?? -1,
                encounter.Value.EncountDataId ?? string.Empty,
                speciesId,
                pokemon is null
                    ? $"Missing data: {encounter.Value.EncountDataId ?? "unknown"}"
                    : FormatEncounterSpeciesLabel(speciesId, form, labels),
                form,
                pokemon?.MinLevel ?? 0,
                pokemon?.MaxLevel ?? 0,
                encounter.Value.Weight,
                FormatTimeCondition(encounter.Value.AppearedTimeCondition),
                FormatWeatherCondition(encounter.Value.AppearedWeatherCondition, encounter.Value),
                new ZaEncounterProvenance(
                    pokemonSource.RelativePath,
                    pokemonSource.SourceLayer,
                    pokemonSource.FileState));
        }
    }

    private static IReadOnlyList<ZaEncounterEditableField> CreateEditableFields(ZaTextLabelLookup labels)
    {
        var speciesOptions = CreateIndexedOptions(labels.PokemonNameCount, labels.Pokemon, includeNone: true);
        return
        [
            new(
                SpeciesIdField,
                "Species",
                "integer",
                0,
                speciesOptions.Count > 0 ? speciesOptions.Max(option => option.Value) : ushort.MaxValue,
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

    private static string CreateTableId(int groupIndex, int spawnerIndex)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{TableIdPrefix}:{groupIndex}:{spawnerIndex}");
    }

    private static string FormatLocation(PokemonSpawnerData spawner)
    {
        var objectInfo = FirstAppearanceObject(spawner);
        var zoneInfo = objectInfo?.ZoneInfo;
        var zoneId = zoneInfo?.ZoneId;
        var variationId = zoneInfo?.VariationId;
        if (!string.IsNullOrWhiteSpace(zoneId) && !string.IsNullOrWhiteSpace(variationId))
        {
            return $"{zoneId} {variationId}";
        }

        if (!string.IsNullOrWhiteSpace(zoneId))
        {
            return zoneId;
        }

        return string.IsNullOrWhiteSpace(spawner.Id) ? "Unknown Z-A Area" : spawner.Id;
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
