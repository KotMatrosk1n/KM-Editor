// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SV.Placement;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.StaticEncounters;

internal sealed class SvStaticEncountersWorkflowService
{
    private const int AlcremieSpeciesId = (int)global::pml.common.DevID.DEV_MAHOIPPU;

    public const string StaticEncountersEditDomain = "workflow.staticEncounters";

    public const string SpeciesField = "species";
    public const string FormField = "form";
    public const string LevelField = "level";
    public const string HeldItemIdField = "heldItemId";
    public const string AbilityField = "ability";
    public const string NatureField = "nature";
    public const string GenderField = "gender";
    public const string ShinyLockField = "shinyLock";
    public const string Move0Field = "move0Id";
    public const string Move1Field = "move1Id";
    public const string Move2Field = "move2Id";
    public const string Move3Field = "move3Id";
    public const string IvHpField = "ivHp";
    public const string IvAttackField = "ivAttack";
    public const string IvDefenseField = "ivDefense";
    public const string IvSpecialAttackField = "ivSpecialAttack";
    public const string IvSpecialDefenseField = "ivSpecialDefense";
    public const string IvSpeedField = "ivSpeed";
    public const string FlawlessIvCountField = "flawlessIvCount";
    public const string IvModeField = "ivMode";
    public const string MoveModeField = "moveMode";
    public const string TeraTypeField = "teraType";
    public const string ScaleModeField = "scaleMode";
    public const string ScaleValueField = "scaleValue";
    public const string AlcremieSweetField = "alcremieSweet";
    public const string DisableBattleOutField = "disableBattleOut";
    public const string EventEncounterField = "eventEncounter";
    public const string DropItemIdField = "dropItemId";
    public const string DropCountField = "dropCount";
    public const string NatureBoostField = "natureBoost";
    public const string RibbonField = "ribbon";
    public const string AiActionField = "aiAction";
    public const string AiHungerField = "aiHunger";
    public const string AiFatigueField = "aiFatigue";
    public const string AiSleepinessField = "aiSleepiness";
    public const string AiPriorityField = "aiPriority";
    public const string AiTriggerActionField = "aiTriggerAction";
    public const string AiFrequencyField = "aiFrequency";
    public const string SpawnMinDistanceField = "spawnMinDistance";
    public const string SpawnMaxDistanceField = "spawnMaxDistance";
    public const string DespawnMinDistanceField = "despawnMinDistance";
    public const string DespawnMaxDistanceField = "despawnMaxDistance";
    public const string SpawnModeField = "spawnMode";
    public const string SpawnOnLoadField = "spawnOnLoad";
    public const string RespawnChanceField = "respawnChance";

    private const string WorkflowLabel = "Static Encounters";
    private const string WorkflowDescription = "Static overworld Pokemon symbol tables and linked scene-only point data.";

    private static readonly IReadOnlyDictionary<string, string> FixedFieldMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SpeciesField] = SvPlacementWorkflowService.FixedSpeciesIdField,
            [FormField] = SvPlacementWorkflowService.FixedFormField,
            [LevelField] = SvPlacementWorkflowService.FixedLevelField,
            [GenderField] = SvPlacementWorkflowService.FixedGenderField,
            [ShinyLockField] = SvPlacementWorkflowService.FixedShinyField,
            [IvModeField] = SvPlacementWorkflowService.FixedIvModeField,
            [IvHpField] = SvPlacementWorkflowService.FixedIvHpField,
            [IvAttackField] = SvPlacementWorkflowService.FixedIvAttackField,
            [IvDefenseField] = SvPlacementWorkflowService.FixedIvDefenseField,
            [IvSpecialAttackField] = SvPlacementWorkflowService.FixedIvSpecialAttackField,
            [IvSpecialDefenseField] = SvPlacementWorkflowService.FixedIvSpecialDefenseField,
            [IvSpeedField] = SvPlacementWorkflowService.FixedIvSpeedField,
            [FlawlessIvCountField] = SvPlacementWorkflowService.FixedGuaranteedPerfectIvsField,
            [MoveModeField] = SvPlacementWorkflowService.FixedMoveModeField,
            [Move0Field] = SvPlacementWorkflowService.FixedMove1Field,
            [Move1Field] = SvPlacementWorkflowService.FixedMove2Field,
            [Move2Field] = SvPlacementWorkflowService.FixedMove3Field,
            [Move3Field] = SvPlacementWorkflowService.FixedMove4Field,
            [AbilityField] = SvPlacementWorkflowService.FixedAbilityModeField,
            [ScaleModeField] = SvPlacementWorkflowService.FixedScaleModeField,
            [ScaleValueField] = SvPlacementWorkflowService.FixedScaleValueField,
            [TeraTypeField] = SvPlacementWorkflowService.FixedTeraTypeField,
            [AlcremieSweetField] = SvPlacementWorkflowService.FixedAlcremieSweetField,
            [AiActionField] = SvPlacementWorkflowService.FixedAiActionField,
            [AiHungerField] = SvPlacementWorkflowService.FixedAiHungerField,
            [AiFatigueField] = SvPlacementWorkflowService.FixedAiFatigueField,
            [AiSleepinessField] = SvPlacementWorkflowService.FixedAiSleepinessField,
            [AiPriorityField] = SvPlacementWorkflowService.FixedAiPriorityField,
            [AiTriggerActionField] = SvPlacementWorkflowService.FixedAiTriggerActionField,
            [AiFrequencyField] = SvPlacementWorkflowService.FixedAiFrequencyField,
            [SpawnMinDistanceField] = SvPlacementWorkflowService.FixedSpawnMinDistanceField,
            [SpawnMaxDistanceField] = SvPlacementWorkflowService.FixedSpawnMaxDistanceField,
            [DespawnMinDistanceField] = SvPlacementWorkflowService.FixedDespawnMinDistanceField,
            [DespawnMaxDistanceField] = SvPlacementWorkflowService.FixedDespawnMaxDistanceField,
            [SpawnModeField] = SvPlacementWorkflowService.FixedSpawnModeField,
            [SpawnOnLoadField] = SvPlacementWorkflowService.FixedSpawnOnLoadField,
            [RespawnChanceField] = SvPlacementWorkflowService.FixedRespawnChanceField,
        };

    private static readonly IReadOnlyDictionary<string, string> CoinFieldMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DisableBattleOutField] = SvPlacementWorkflowService.CoinDisableBattleOutField,
            [EventEncounterField] = SvPlacementWorkflowService.CoinEventEncounterField,
            [SpeciesField] = SvPlacementWorkflowService.CoinSpeciesIdField,
            [FormField] = SvPlacementWorkflowService.CoinFormField,
            [LevelField] = SvPlacementWorkflowService.CoinLevelField,
            [GenderField] = SvPlacementWorkflowService.CoinGenderField,
            [ShinyLockField] = SvPlacementWorkflowService.CoinShinyField,
            [IvModeField] = SvPlacementWorkflowService.CoinIvModeField,
            [FlawlessIvCountField] = SvPlacementWorkflowService.CoinGuaranteedPerfectIvsField,
            [HeldItemIdField] = SvPlacementWorkflowService.CoinHeldItemField,
            [DropItemIdField] = SvPlacementWorkflowService.CoinDropItemField,
            [DropCountField] = SvPlacementWorkflowService.CoinDropCountField,
            [NatureField] = SvPlacementWorkflowService.CoinNatureField,
            [NatureBoostField] = SvPlacementWorkflowService.CoinNatureBoostField,
            [AbilityField] = SvPlacementWorkflowService.CoinAbilityModeField,
            [MoveModeField] = SvPlacementWorkflowService.CoinMoveModeField,
            [Move0Field] = SvPlacementWorkflowService.CoinMove1Field,
            [Move1Field] = SvPlacementWorkflowService.CoinMove2Field,
            [Move2Field] = SvPlacementWorkflowService.CoinMove3Field,
            [Move3Field] = SvPlacementWorkflowService.CoinMove4Field,
            [TeraTypeField] = SvPlacementWorkflowService.CoinTeraTypeField,
            [ScaleModeField] = SvPlacementWorkflowService.CoinScaleModeField,
            [ScaleValueField] = SvPlacementWorkflowService.CoinScaleValueField,
            [RibbonField] = SvPlacementWorkflowService.CoinRibbonField,
        };

    private readonly SvPlacementWorkflowService placementWorkflowService;

    public SvStaticEncountersWorkflowService(SvPlacementWorkflowService? placementWorkflowService = null)
    {
        this.placementWorkflowService = placementWorkflowService ?? new SvPlacementWorkflowService();
    }

    public SvWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.StaticEncounters,
            WorkflowLabel,
            WorkflowDescription);
    }

    public SvStaticEncountersWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var placementWorkflow = placementWorkflowService.LoadStaticEncounterObjects(project);
        var sourceFiles = placementWorkflow.Objects
            .Select(placedObject => placedObject.Provenance.SourceFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var encounters = placementWorkflow.Objects
            .Where(IsStaticEncounterObject)
            .Select((placedObject, index) => ToEntry(index, placedObject))
            .ToArray();
        var diagnostics = placementWorkflow.Diagnostics
            .Select(diagnostic => diagnostic with { Domain = StaticEncountersEditDomain })
            .ToArray();
        var summary = SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.StaticEncounters,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Length == 0 ? null : diagnostics);

        return new SvStaticEncountersWorkflow(
            summary,
            encounters,
            CreateEditableFields(placementWorkflow.EditableFields),
            new SvStaticEncountersWorkflowStats(
                encounters.Length,
                encounters.Count(entry => entry.FlawlessIvCount is not null and not 0),
                sourceFiles,
                encounters.Count(entry => entry.CategoryId == SvPlacementWorkflowService.FixedSymbolsCategory),
                encounters.Count(entry => entry.CategoryId == SvPlacementWorkflowService.CoinSymbolsCategory)),
            diagnostics);
    }

    public static bool TryMapField(string categoryId, string field, out string placementField)
    {
        var map = categoryId switch
        {
            SvPlacementWorkflowService.FixedSymbolsCategory => FixedFieldMap,
            SvPlacementWorkflowService.CoinSymbolsCategory => CoinFieldMap,
            _ => null,
        };

        if (map is not null && map.TryGetValue(field, out placementField!))
        {
            return true;
        }

        placementField = string.Empty;
        return false;
    }

    public static string CreateRecordId(int encounterIndex)
    {
        return $"static:{encounterIndex.ToString(CultureInfo.InvariantCulture)}";
    }

    public static bool TryParseRecordId(string? recordId, out int encounterIndex)
    {
        encounterIndex = -1;
        const string prefix = "static:";
        return !string.IsNullOrWhiteSpace(recordId)
            && recordId.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(recordId[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out encounterIndex)
            && encounterIndex >= 0;
    }

    private static bool IsStaticEncounterObject(SvPlacedObjectRecord placedObject)
    {
        return placedObject.CategoryId is
            SvPlacementWorkflowService.FixedSymbolsCategory or
            SvPlacementWorkflowService.CoinSymbolsCategory;
    }

    private static SvStaticEncounterEntry ToEntry(int encounterIndex, SvPlacedObjectRecord placedObject)
    {
        var map = placedObject.CategoryId == SvPlacementWorkflowService.FixedSymbolsCategory
            ? FixedFieldMap
            : CoinFieldMap;
        var fieldValues = map
            .Where(pair => FindField(placedObject, pair.Value) is not null)
            .ToDictionary(pair => pair.Key, pair => FindField(placedObject, pair.Value)!.Value, StringComparer.Ordinal);
        var displayValues = map
            .Where(pair => FindField(placedObject, pair.Value) is not null)
            .ToDictionary(pair => pair.Key, pair => FindField(placedObject, pair.Value)!.DisplayValue, StringComparer.Ordinal);
        var fieldReadOnly = map
            .Where(pair => FindField(placedObject, pair.Value) is not null)
            .ToDictionary(pair => pair.Key, pair => FindField(placedObject, pair.Value)!.IsReadOnly, StringComparer.Ordinal);
        var supportedFields = fieldValues.Keys.ToArray();
        var moves = new[]
        {
            ToMove(0, fieldValues, displayValues),
            ToMove(1, fieldValues, displayValues),
            ToMove(2, fieldValues, displayValues),
            ToMove(3, fieldValues, displayValues),
        };
        var ivs = new SvStaticEncounterStatsRecord(
            ReadInt(fieldValues, IvHpField),
            ReadInt(fieldValues, IvAttackField),
            ReadInt(fieldValues, IvDefenseField),
            ReadInt(fieldValues, IvSpecialAttackField),
            ReadInt(fieldValues, IvSpecialDefenseField),
            ReadInt(fieldValues, IvSpeedField));
        int? flawlessIvCount = fieldValues.ContainsKey(FlawlessIvCountField)
            ? ReadInt(fieldValues, FlawlessIvCountField)
            : null;
        var speciesId = ReadInt(fieldValues, SpeciesField);
        var species = StripLeadingValue(displayValues.GetValueOrDefault(SpeciesField, placedObject.ItemName));
        var form = ReadInt(fieldValues, FormField);
        var level = ReadInt(fieldValues, LevelField);
        var heldItemId = ReadInt(fieldValues, HeldItemIdField);
        var heldItem = heldItemId == 0 ? null : StripLeadingValue(displayValues.GetValueOrDefault(HeldItemIdField, string.Empty));
        var categoryOrdinal = placedObject.CategoryId == SvPlacementWorkflowService.FixedSymbolsCategory ? 0 : 1;
        var categoryLabel = placedObject.CategoryLabel;

        return new SvStaticEncounterEntry(
            encounterIndex,
            placedObject.ObjectId,
            placedObject.CategoryId,
            categoryLabel,
            FormatLabel(encounterIndex, categoryLabel, species, speciesId, form, level, moves),
            placedObject.ObjectId,
            speciesId,
            species,
            form,
            level,
            heldItemId,
            heldItem,
            ReadInt(fieldValues, AbilityField),
            displayValues.GetValueOrDefault(AbilityField, "Default"),
            ReadInt(fieldValues, NatureField),
            displayValues.GetValueOrDefault(NatureField, "Default"),
            ReadInt(fieldValues, GenderField),
            displayValues.GetValueOrDefault(GenderField, "Default"),
            ReadInt(fieldValues, ShinyLockField),
            displayValues.GetValueOrDefault(ShinyLockField, "Default"),
            categoryOrdinal,
            categoryLabel,
            new SvStaticEncounterStatsRecord(0, 0, 0, 0, 0, 0),
            ivs,
            flawlessIvCount,
            FormatIvSummary(ivs, flawlessIvCount),
            moves,
            new SvStaticEncounterProvenance(
                placedObject.Provenance.SourceFile,
                placedObject.Provenance.SourceLayer,
                placedObject.Provenance.FileState),
            supportedFields,
            fieldValues,
            displayValues,
            fieldReadOnly,
            FindField(placedObject, map.GetValueOrDefault(AbilityField) ?? string.Empty)?.Options?
                .Select(option => new SvStaticEncounterEditableFieldOption(option.Value, option.Label))
                .ToArray() ?? []);
    }

    private static SvStaticEncounterMoveRecord ToMove(
        int slot,
        IReadOnlyDictionary<string, string> fieldValues,
        IReadOnlyDictionary<string, string> displayValues)
    {
        var field = slot switch
        {
            0 => Move0Field,
            1 => Move1Field,
            2 => Move2Field,
            3 => Move3Field,
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };
        var moveId = ReadInt(fieldValues, field);
        return new SvStaticEncounterMoveRecord(
            slot,
            moveId,
            moveId == 0 ? null : StripLeadingValue(displayValues.GetValueOrDefault(field, string.Empty)));
    }

    private static IReadOnlyList<SvStaticEncounterEditableField> CreateEditableFields(
        IReadOnlyList<SvPlacementEditableField> placementFields)
    {
        var fields = new List<SvStaticEncounterEditableField>();
        AddField(fields, placementFields, SpeciesField, "Species", "Pokemon", SvPlacementWorkflowService.FixedSpeciesIdField);
        AddField(fields, placementFields, FormField, "Form", "Pokemon", SvPlacementWorkflowService.FixedFormField);
        AddField(fields, placementFields, LevelField, "Level", "Pokemon", SvPlacementWorkflowService.FixedLevelField);
        AddField(fields, placementFields, GenderField, "Gender", "Pokemon", SvPlacementWorkflowService.FixedGenderField);
        AddField(fields, placementFields, ShinyLockField, "Shiny setting", "Pokemon", SvPlacementWorkflowService.FixedShinyField);
        AddField(fields, placementFields, AbilityField, "Ability mode", "Pokemon", SvPlacementWorkflowService.FixedAbilityModeField);
        AddField(fields, placementFields, HeldItemIdField, "Held item", "Pokemon", SvPlacementWorkflowService.CoinHeldItemField);
        AddField(fields, placementFields, NatureField, "Nature", "Pokemon", SvPlacementWorkflowService.CoinNatureField);
        AddField(fields, placementFields, NatureBoostField, "Nature mint override", "Pokemon", SvPlacementWorkflowService.CoinNatureBoostField);
        AddField(fields, placementFields, TeraTypeField, "Tera type", "Pokemon", SvPlacementWorkflowService.FixedTeraTypeField);
        AddField(fields, placementFields, ScaleModeField, "Scale mode", "Pokemon", SvPlacementWorkflowService.FixedScaleModeField);
        AddField(fields, placementFields, ScaleValueField, "Scale value", "Pokemon", SvPlacementWorkflowService.FixedScaleValueField);
        AddField(fields, placementFields, AlcremieSweetField, "Alcremie sweet", "Pokemon", SvPlacementWorkflowService.FixedAlcremieSweetField);
        AddField(fields, placementFields, IvModeField, "IV mode", "Stats", SvPlacementWorkflowService.FixedIvModeField);
        AddField(fields, placementFields, FlawlessIvCountField, "Guaranteed perfect IVs", "Stats", SvPlacementWorkflowService.FixedGuaranteedPerfectIvsField);
        AddField(fields, placementFields, IvHpField, "HP IV", "Stats", SvPlacementWorkflowService.FixedIvHpField);
        AddField(fields, placementFields, IvAttackField, "Attack IV", "Stats", SvPlacementWorkflowService.FixedIvAttackField);
        AddField(fields, placementFields, IvDefenseField, "Defense IV", "Stats", SvPlacementWorkflowService.FixedIvDefenseField);
        AddField(fields, placementFields, IvSpecialAttackField, "Sp. Atk IV", "Stats", SvPlacementWorkflowService.FixedIvSpecialAttackField);
        AddField(fields, placementFields, IvSpecialDefenseField, "Sp. Def IV", "Stats", SvPlacementWorkflowService.FixedIvSpecialDefenseField);
        AddField(fields, placementFields, IvSpeedField, "Speed IV", "Stats", SvPlacementWorkflowService.FixedIvSpeedField);
        AddField(fields, placementFields, MoveModeField, "Move selection mode", "Moves", SvPlacementWorkflowService.FixedMoveModeField);
        AddField(fields, placementFields, Move0Field, "Move 1", "Moves", SvPlacementWorkflowService.FixedMove1Field);
        AddField(fields, placementFields, Move1Field, "Move 2", "Moves", SvPlacementWorkflowService.FixedMove2Field);
        AddField(fields, placementFields, Move2Field, "Move 3", "Moves", SvPlacementWorkflowService.FixedMove3Field);
        AddField(fields, placementFields, Move3Field, "Move 4", "Moves", SvPlacementWorkflowService.FixedMove4Field);
        AddField(fields, placementFields, DropItemIdField, "Drop item", "Drops", SvPlacementWorkflowService.CoinDropItemField);
        AddField(fields, placementFields, DropCountField, "Drop count", "Drops", SvPlacementWorkflowService.CoinDropCountField);
        AddField(fields, placementFields, RibbonField, "Ribbon", "Pokemon", SvPlacementWorkflowService.CoinRibbonField);
        AddField(fields, placementFields, DisableBattleOutField, "Disable battle out", "Battle", SvPlacementWorkflowService.CoinDisableBattleOutField);
        AddField(fields, placementFields, EventEncounterField, "Event encounter", "Battle", SvPlacementWorkflowService.CoinEventEncounterField);
        AddField(fields, placementFields, AiActionField, "AI action", "AI", SvPlacementWorkflowService.FixedAiActionField);
        AddField(fields, placementFields, AiHungerField, "Hunger", "AI", SvPlacementWorkflowService.FixedAiHungerField);
        AddField(fields, placementFields, AiFatigueField, "Fatigue", "AI", SvPlacementWorkflowService.FixedAiFatigueField);
        AddField(fields, placementFields, AiSleepinessField, "Sleepiness", "AI", SvPlacementWorkflowService.FixedAiSleepinessField);
        AddField(fields, placementFields, AiPriorityField, "AI priority", "AI", SvPlacementWorkflowService.FixedAiPriorityField);
        AddField(fields, placementFields, AiTriggerActionField, "Trigger action", "AI", SvPlacementWorkflowService.FixedAiTriggerActionField);
        AddField(fields, placementFields, AiFrequencyField, "Behavior frequency", "AI", SvPlacementWorkflowService.FixedAiFrequencyField);
        AddField(fields, placementFields, SpawnMinDistanceField, "Spawn distance min", "Spawning", SvPlacementWorkflowService.FixedSpawnMinDistanceField);
        AddField(fields, placementFields, SpawnMaxDistanceField, "Spawn distance max", "Spawning", SvPlacementWorkflowService.FixedSpawnMaxDistanceField);
        AddField(fields, placementFields, DespawnMinDistanceField, "Despawn distance min", "Spawning", SvPlacementWorkflowService.FixedDespawnMinDistanceField);
        AddField(fields, placementFields, DespawnMaxDistanceField, "Despawn distance max", "Spawning", SvPlacementWorkflowService.FixedDespawnMaxDistanceField);
        AddField(fields, placementFields, SpawnModeField, "Spawn mode", "Spawning", SvPlacementWorkflowService.FixedSpawnModeField);
        AddField(fields, placementFields, SpawnOnLoadField, "Spawn on load", "Spawning", SvPlacementWorkflowService.FixedSpawnOnLoadField);
        AddField(fields, placementFields, RespawnChanceField, "Respawn chance", "Spawning", SvPlacementWorkflowService.FixedRespawnChanceField);
        return fields;
    }

    private static void AddField(
        ICollection<SvStaticEncounterEditableField> fields,
        IReadOnlyList<SvPlacementEditableField> placementFields,
        string field,
        string label,
        string group,
        string placementField)
    {
        var source = placementFields.FirstOrDefault(candidate => candidate.Field == placementField);
        if (source is null)
        {
            return;
        }

        fields.Add(new SvStaticEncounterEditableField(
            field,
            label,
            source.ValueKind,
            ToIntBound(source.MinimumValue),
            ToIntBound(source.MaximumValue),
            source.Options.Select(option => new SvStaticEncounterEditableFieldOption(option.Value, option.Label)).ToArray(),
            group,
            source.IsReadOnly,
            source.Description));
    }

    private static int? ToIntBound(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return null;
        }

        if (value <= int.MinValue)
        {
            return int.MinValue;
        }

        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)value;
    }

    private static SvPlacementFieldValue? FindField(SvPlacedObjectRecord placedObject, string field)
    {
        return placedObject.Fields.FirstOrDefault(candidate =>
            string.Equals(candidate.Field, field, StringComparison.Ordinal));
    }

    private static int ReadInt(IReadOnlyDictionary<string, string> values, string field)
    {
        return values.TryGetValue(field, out var raw)
            && int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static string StripLeadingValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var trimmed = value.Trim();
        var separator = trimmed.IndexOf(' ', StringComparison.Ordinal);
        return separator > 0
            && int.TryParse(trimmed[..separator], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)
            ? trimmed[(separator + 1)..]
            : trimmed;
    }

    private static string FormatLabel(
        int encounterIndex,
        string category,
        string species,
        int speciesId,
        int form,
        int level,
        IReadOnlyList<SvStaticEncounterMoveRecord> moves)
    {
        var speciesLabel = speciesId == 0
            ? "None"
            : form == 0 ? species : $"{species} (Form {form.ToString(CultureInfo.InvariantCulture)})";
        var moveText = string.Join(", ", moves
            .Where(move => move.MoveId > 0 && !string.IsNullOrWhiteSpace(move.Move))
            .Take(2)
            .Select(move => move.Move));

        return moveText.Length == 0
            ? $"Static {(encounterIndex + 1).ToString("000", CultureInfo.InvariantCulture)}: {speciesLabel} Lv. {level} | {category}"
            : $"Static {(encounterIndex + 1).ToString("000", CultureInfo.InvariantCulture)}: {speciesLabel} Lv. {level} | {category} | {moveText}";
    }

    private static string FormatIvSummary(SvStaticEncounterStatsRecord ivs, int? flawlessIvCount)
    {
        return flawlessIvCount is > 0
            ? $"{flawlessIvCount.Value.ToString(CultureInfo.InvariantCulture)} guaranteed perfect IVs"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"HP {ivs.HP} / Atk {ivs.Attack} / Def {ivs.Defense} / SpA {ivs.SpecialAttack} / SpD {ivs.SpecialDefense} / Spe {ivs.Speed}");
    }
}
