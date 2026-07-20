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
    public const string AlphaChancePercentField = "alphaChancePercent";
    public const string AlphaLevelBonusField = "alphaLevelBonus";
    public const string WeightField = "weight";
    public const string SlotMaxCountField = "slotMaxCount";
    public const string AppearanceMinCountField = "appearanceMinCount";
    public const string AppearanceMaxCountField = "appearanceMaxCount";

    private const string WorkflowLabel = "Wild Encounters";
    private const string WorkflowDescription = "Edit Pokemon Legends Z-A wild encounter Pokemon rows, slot weights, and spawn counts.";
    private const string GameVersionLabel = "Pokemon Legends ZA";
    private const string TableIdPrefix = "za-spawner";
    private const string PokemonDataRecordIdPrefix = "encount-data:";
    private const string AppearanceRecordIdSuffix = "#appearance";

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
            tables = LoadTables(
                spawnerSource,
                encounterSource,
                labels,
                pokemonAvailability,
                diagnostics).ToArray();
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
            diagnostics)
        {
            PokemonAvailability = pokemonAvailability,
        };
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

    internal static string CreatePokemonDataRecordId(int sourceIndex)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{PokemonDataRecordIdPrefix}{sourceIndex}");
    }

    internal static string CreateAppearanceRecordId(string tableId)
    {
        return $"{tableId}{AppearanceRecordIdSuffix}";
    }

    internal static bool TryParsePokemonDataRecordId(string? recordId, out int sourceIndex)
    {
        sourceIndex = -1;
        return recordId?.StartsWith(PokemonDataRecordIdPrefix, StringComparison.Ordinal) == true
            && int.TryParse(
                recordId[PokemonDataRecordIdPrefix.Length..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out sourceIndex)
            && sourceIndex >= 0;
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

    internal static bool TryParseAppearanceRecordId(string? recordId, out string tableId)
    {
        tableId = string.Empty;
        if (recordId?.EndsWith(AppearanceRecordIdSuffix, StringComparison.Ordinal) != true
            || recordId.Length == AppearanceRecordIdSuffix.Length)
        {
            return false;
        }

        tableId = recordId[..^AppearanceRecordIdSuffix.Length];
        return true;
    }

    internal static bool TryParseTableId(string? tableId, out int groupIndex, out int spawnerIndex)
    {
        groupIndex = -1;
        spawnerIndex = -1;

        var prefix = $"{TableIdPrefix}:";
        if (tableId?.StartsWith(prefix, StringComparison.Ordinal) != true)
        {
            return false;
        }

        var parts = tableId[prefix.Length..].Split(':');
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out groupIndex)
            && groupIndex >= 0
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out spawnerIndex)
            && spawnerIndex >= 0;
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
        ZaTextLabelLookup labels,
        ZaPokemonAvailability pokemonAvailability,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        var pokemonRows = ZaEncounterDataDocument.Parse(encounterSource.Bytes)
            .Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .GroupBy(entry => entry.Id!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var table = PokemonSpawnerDataDBArray.GetRootAsPokemonSpawnerDataDBArray(new ByteBuffer(spawnerSource.Bytes));
        var displayOrder = ZaPokemonSpawnerDisplayOrder.Create(table);
        var scalarSpawners = ZaPokemonSpawnerDataDocument.Parse(spawnerSource.Bytes)
            .Entries
            .ToDictionary(
                entry => (entry.GroupIndex, entry.SpawnerIndex),
                entry => entry);
        var reportedInvalidAlphaChanceSources = new HashSet<int>();
        var reportedInvalidAlphaLevelBonusSources = new HashSet<int>();
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

                var displayPosition = displayOrder[(groupIndex, spawnerIndex)];
                var locationKey = displayPosition.LocationKey;
                scalarSpawners.TryGetValue(
                    (groupIndex, spawnerIndex),
                    out var scalarSpawner);
                if (scalarSpawner is not null
                    && !string.Equals(scalarSpawner.Id, spawner.Value.Id, StringComparison.Ordinal))
                {
                    diagnostics.Add(ZaWorkflowSupport.Warning(
                        $"Spawner '{spawner.Value.Id}' could not be matched safely to its exact-byte scalar storage. "
                        + "Weight and population fields will remain read-only and be preserved.",
                        spawnerSource.RelativePath,
                        WeightField,
                        "Matching generated and exact-byte spawner identities"));
                    scalarSpawner = null;
                }

                var appearanceCounts = ReadAppearanceCounts(spawner.Value, scalarSpawner);
                if (appearanceCounts.ObjectCount > 0
                    && !appearanceCounts.HasUniformReadableValues)
                {
                    diagnostics.Add(ZaWorkflowSupport.Warning(
                        $"Spawner '{spawner.Value.Id}' has missing or mixed appearance count values. "
                        + "Overall minimum and maximum counts will remain read-only and be preserved.",
                        spawnerSource.RelativePath,
                        AppearanceMinCountField,
                        "Matching count values on every appearance object"));
                }
                else if (appearanceCounts.ObjectCount > 0
                    && !appearanceCounts.CanEdit)
                {
                    diagnostics.Add(ZaWorkflowSupport.Warning(
                        $"Spawner '{spawner.Value.Id}' stores at least one appearance count as an omitted default. "
                        + "The current uniform count range will remain visible but read-only.",
                        spawnerSource.RelativePath,
                        AppearanceMinCountField,
                        "Materialized minimum and maximum counts on every appearance object"));
                }

                var slots = ReadSlots(
                    spawner.Value,
                    scalarSpawner,
                    pokemonRows,
                    encounterSource,
                    labels,
                    pokemonAvailability,
                    IsNumberedWildZone(locationKey),
                    appearanceCounts,
                    diagnostics,
                    reportedInvalidAlphaChanceSources,
                    reportedInvalidAlphaLevelBonusSources).ToArray();
                if (slots.Length == 0)
                {
                    continue;
                }

                var location = FormatLocation(locationKey, labels);
                yield return new ZaEncounterTableRecord(
                    CreateTableId(groupIndex, spawnerIndex),
                    location,
                    FormatArea(spawner.Value, labels),
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
                    FormatTableLabel(locationKey, displayPosition.Ordinal, spawner.Value.Id, labels),
                    FormatTableDetails(slots),
                    ZaLumioseLocationLabels.GetMissionDetails(locationKey))
                {
                    SpawnerCategory = GetSpawnerCategory(locationKey, spawner.Value.Id),
                };
            }
        }
    }

    private static IEnumerable<ZaEncounterSlotRecord> ReadSlots(
        PokemonSpawnerData spawner,
        ZaPokemonSpawnerDataEntry? scalarSpawner,
        IReadOnlyDictionary<string, ZaPokemonDataEntry> pokemonRows,
        ZaWorkflowFile encounterSource,
        ZaTextLabelLookup labels,
        ZaPokemonAvailability pokemonAvailability,
        bool isNumberedWildZone,
        AppearanceCountSummary appearanceCounts,
        ICollection<ValidationDiagnostic> diagnostics,
        ISet<int> reportedInvalidAlphaChanceSources,
        ISet<int> reportedInvalidAlphaLevelBonusSources)
    {
        for (var slot = 0; slot < spawner.EncountDataInfoListLength; slot++)
        {
            var encounter = spawner.EncountDataInfoList(slot);
            if (encounter is null)
            {
                continue;
            }

            var scalarSlot = scalarSpawner is not null
                && slot < scalarSpawner.EncountDataInfoList.Count
                ? scalarSpawner.EncountDataInfoList[slot]
                : null;
            var encounterDataId = encounter.Value.EncountDataId ?? string.Empty;
            var hasMatchingScalarSlot = scalarSlot is not null
                && string.Equals(
                    scalarSlot.EncountDataId ?? string.Empty,
                    encounterDataId,
                    StringComparison.Ordinal)
                && scalarSlot.Weight == encounter.Value.Weight
                && scalarSlot.MaxCount == encounter.Value.MaxCount;
            var hasStructuralAlphaReference = HasStructuralAlphaReference(encounterDataId);
            var pokemon = ResolvePokemonRow(encounterDataId, pokemonRows);
            var speciesId = pokemon?.DevNo ?? 0;
            var form = pokemon?.FormNo ?? 0;
            var alphaChancePercent = pokemon is not null
                && TryReadAlphaChancePercent(pokemon.OyabunProbability, out var wholeAlphaChancePercent)
                    ? wholeAlphaChancePercent
                    : (int?)null;
            if (pokemon is not null
                && alphaChancePercent is null
                && reportedInvalidAlphaChanceSources.Add(pokemon.SourceIndex))
            {
                diagnostics.Add(ZaWorkflowSupport.Warning(
                    $"Shared Alpha chance for encounter row '{pokemon.Id}' is {pokemon.OyabunProbability.ToString(CultureInfo.InvariantCulture)} percent. "
                    + "Only whole-number percentages from 0 through 100 are editable; this value will remain read-only and be preserved.",
                    encounterSource.RelativePath,
                    AlphaChancePercentField,
                    "Whole-number shared Alpha chance from 0 through 100"));
            }

            var alphaLevelBonus = pokemon is not null
                && pokemon.OyabunAdditionalLevel is >= 0 and <= 100
                    ? pokemon.OyabunAdditionalLevel
                    : (int?)null;
            if (pokemon is not null
                && alphaLevelBonus is null
                && reportedInvalidAlphaLevelBonusSources.Add(pokemon.SourceIndex))
            {
                diagnostics.Add(ZaWorkflowSupport.Warning(
                    $"Shared Alpha level bonus for encounter row '{pokemon.Id}' is {pokemon.OyabunAdditionalLevel.ToString(CultureInfo.InvariantCulture)}. "
                    + "Only values from 0 through 100 are editable; this value will remain read-only and be preserved.",
                    encounterSource.RelativePath,
                    AlphaLevelBonusField,
                    "Shared Alpha level bonus from 0 through 100"));
            }

            yield return new ZaEncounterSlotRecord(
                slot,
                pokemon?.SourceIndex ?? -1,
                pokemon is null ? null : CreatePokemonDataRecordId(pokemon.SourceIndex),
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
                FormatWeatherCondition(encounter.Value.AppearedWeatherCondition),
                hasStructuralAlphaReference,
                FormatEncounterKind(pokemon?.OyabunProbability),
                new ZaEncounterProvenance(
                    encounterSource.RelativePath,
                    encounterSource.SourceLayer,
                    encounterSource.FileState),
                isNumberedWildZone ? encounter.Value.ShowMapIcon == 0 : null,
                alphaChancePercent,
                alphaLevelBonus,
                pokemon?.OyabunProbability > 0)
            {
                SlotMaxCount = encounter.Value.MaxCount,
                CanEditWeight = hasMatchingScalarSlot && scalarSlot!.CanEditWeight,
                CanEditSlotMaxCount = hasMatchingScalarSlot && scalarSlot!.CanEditMaxCount,
                AppearanceMinCount = appearanceCounts.Minimum,
                AppearanceMaxCount = appearanceCounts.Maximum,
                AppearanceObjectCount = appearanceCounts.ObjectCount,
                CanEditAppearanceCounts = appearanceCounts.CanEdit,
                FormOptions = CreateFormOptions(
                    speciesId,
                    labels.Pokemon(speciesId),
                    pokemonAvailability),
            };
        }
    }

    private static bool TryReadAlphaChancePercent(float value, out int wholePercent)
    {
        if (float.IsFinite(value)
            && value >= 0
            && value <= 100
            && value == MathF.Truncate(value))
        {
            wholePercent = checked((int)value);
            return true;
        }

        wholePercent = 0;
        return false;
    }

    private static string FormatEncounterKind(float? alphaChancePercent)
    {
        return alphaChancePercent switch
        {
            100 => "Guaranteed Alpha",
            > 0 and < 100 => "Alpha Chance",
            0 => "Wild",
            null => "Unresolved",
            _ => "Invalid Alpha Chance",
        };
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

        var normalizedId = ZaEncounterDataIds.NormalizeSpawnerEncounterDataId(encounterDataId);
        if (!string.Equals(normalizedId, encounterDataId, StringComparison.Ordinal)
            && pokemonRows.TryGetValue(normalizedId, out var suffixedRow))
        {
            return suffixedRow;
        }

        return null;
    }

    private static bool HasStructuralAlphaReference(string encounterDataId)
    {
        return ZaEncounterDataIds.IsAlphaSpawnerEncounterDataId(encounterDataId);
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
            new(AlphaChancePercentField, "Alpha Chance (%)", "integer", 0, 100, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(AlphaLevelBonusField, "Alpha Level Bonus", "integer", 0, 100, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(WeightField, "Weight", "integer", 0, int.MaxValue, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(SlotMaxCountField, "Slot Max Count", "integer", 0, int.MaxValue, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(AppearanceMinCountField, "Overall Min Count", "integer", 0, int.MaxValue, Array.Empty<ZaEncounterEditableFieldOption>()),
            new(AppearanceMaxCountField, "Overall Max Count", "integer", 0, int.MaxValue, Array.Empty<ZaEncounterEditableFieldOption>()),
        ];
    }

    private static AppearanceCountSummary ReadAppearanceCounts(
        PokemonSpawnerData spawner,
        ZaPokemonSpawnerDataEntry? scalarSpawner)
    {
        var objectCount = spawner.AppearanceSpawnerObjectInfoListLength;
        if (objectCount == 0)
        {
            return new AppearanceCountSummary(0, null, null, false);
        }

        int? minimum = null;
        int? maximum = null;
        var canEdit = scalarSpawner is not null
            && scalarSpawner.AppearanceSpawnerObjectInfoList.Count == objectCount
            && scalarSpawner.CanEditAppearanceCounts;
        for (var index = 0; index < objectCount; index++)
        {
            var objectInfo = spawner.AppearanceSpawnerObjectInfoList(index);
            var appearanceInfo = objectInfo?.AppearanceInfo;
            if (appearanceInfo is null)
            {
                return new AppearanceCountSummary(objectCount, null, null, false);
            }

            var scalarObjectInfo = scalarSpawner is not null
                && index < scalarSpawner.AppearanceSpawnerObjectInfoList.Count
                ? scalarSpawner.AppearanceSpawnerObjectInfoList[index]
                : null;
            var scalarAppearanceInfo = scalarObjectInfo?.AppearanceInfo;
            if (scalarAppearanceInfo is null
                || !string.Equals(
                    scalarObjectInfo!.ObjectName ?? string.Empty,
                    objectInfo!.Value.ObjectName ?? string.Empty,
                    StringComparison.Ordinal)
                || scalarAppearanceInfo.MinCount != appearanceInfo.Value.MinCount
                || scalarAppearanceInfo.MaxCount != appearanceInfo.Value.MaxCount)
            {
                canEdit = false;
            }

            if (minimum is null)
            {
                minimum = appearanceInfo.Value.MinCount;
                maximum = appearanceInfo.Value.MaxCount;
                continue;
            }

            if (minimum.Value != appearanceInfo.Value.MinCount
                || maximum!.Value != appearanceInfo.Value.MaxCount)
            {
                return new AppearanceCountSummary(objectCount, null, null, false);
            }
        }

        return new AppearanceCountSummary(objectCount, minimum, maximum, canEdit);
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
            .Select(option => new ZaEncounterEditableFieldOption(option.Value, option.Label)
            {
                FormOptions = CreateSpeciesFormOptions(
                    option.Value,
                    labels,
                    pokemonAvailability),
            })
            .ToArray();
    }

    private static IReadOnlyList<ZaEncounterEditableFieldOption>? CreateSpeciesFormOptions(
        int speciesId,
        ZaTextLabelLookup labels,
        ZaPokemonAvailability pokemonAvailability)
    {
        if (speciesId == 0)
        {
            return [new ZaEncounterEditableFieldOption(0, ZaLabels.PokemonFormLabel(0, 0, "None"))];
        }

        if (!pokemonAvailability.HasKnownAvailability)
        {
            return null;
        }

        return CreateFormOptions(speciesId, labels.Pokemon(speciesId), pokemonAvailability);
    }

    internal static IReadOnlyList<ZaEncounterEditableFieldOption> CreateFormOptions(
        int speciesId,
        string speciesName,
        ZaPokemonAvailability pokemonAvailability)
    {
        return pokemonAvailability.CreateFormOptions(
            speciesId,
            form => new ZaEncounterEditableFieldOption(
                form,
                ZaLabels.PokemonFormLabel(speciesId, form, speciesName)));
    }

    private static string CreateTableId(int groupIndex, int spawnerIndex)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{TableIdPrefix}:{groupIndex}:{spawnerIndex}");
    }

    private static string FormatLocation(string locationKey, ZaTextLabelLookup labels)
    {
        return ZaLumioseLocationLabels.FormatLocation(
            locationKey,
            labels.PlaceName,
            labels.Pokemon,
            labels.MissionTitle);
    }

    private static int? GetLocationSort(string locationKey)
    {
        return ZaLumioseLocationLabels.GetLocationSort(locationKey);
    }

    private static string? GetSpawnerCategory(string locationKey, string? spawnerId)
    {
        if (!locationKey.StartsWith("outzone_", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(spawnerId)
            ? ZaLumioseLocationLabels.OtherSpawnerCategory
            : ZaLumioseLocationLabels.ClassifyRawSpawnerId(spawnerId)
                ?? ZaLumioseLocationLabels.OtherSpawnerCategory;
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
            : ZaLumioseLocationLabels.FormatRawSpawnerId(
                spawnerId,
                labels.Pokemon,
                labels.MissionTitle);
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
        var weightTotal = slots.Sum(slot => (long)slot.Weight);
        var alphaCount = slots.Count(slot => slot.IsAlpha);
        var alphaLabel = alphaCount == 0
            ? string.Empty
            : $" - {alphaCount.ToString(CultureInfo.InvariantCulture)} Alpha";
        return $"{speciesLabel} - {slots.Count.ToString(CultureInfo.InvariantCulture)} {slotLabel} - total weight {weightTotal.ToString(CultureInfo.InvariantCulture)}{alphaLabel}";
    }

    private static string FormatSlotPreviewSpecies(ZaEncounterSlotRecord slot)
    {
        return slot.IsAlpha ? $"{slot.Species} Alpha" : slot.Species;
    }

    private static string FormatArea(PokemonSpawnerData spawner, ZaTextLabelLookup labels)
    {
        var objectInfo = FirstAppearanceObject(spawner);
        if (!string.IsNullOrWhiteSpace(objectInfo?.DungeonName))
        {
            return ZaLumioseLocationLabels.FormatLocation(
                objectInfo.Value.DungeonName,
                labels.PlaceName,
                labels.Pokemon,
                labels.MissionTitle);
        }

        if (!string.IsNullOrWhiteSpace(objectInfo?.BattleAreaId))
        {
            return ZaLumioseLocationLabels.FormatLocation(
                objectInfo.Value.BattleAreaId,
                labels.PlaceName,
                labels.Pokemon,
                labels.MissionTitle);
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

    private static string FormatWeatherCondition(int value)
    {
        return value switch
        {
            0 => "Any weather",
            1 => "Clear",
            2 => "Rain",
            3 => "Snow",
            4 => "Fog",
            _ => $"Weather condition {value.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    private readonly record struct AppearanceCountSummary(
        int ObjectCount,
        int? Minimum,
        int? Maximum,
        bool CanEdit)
    {
        public bool HasUniformReadableValues => Minimum is not null && Maximum is not null;
    }
}
