// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.SV.Placement;
using KM.SV.Data;
using KM.SV.Workflows;
using System.Globalization;

namespace KM.SV.Placement;

internal sealed class SvPlacementWorkflowService
{
    private const string WorkflowLabel = "Placement";
    private const string WorkflowDescription = "Edit Scarlet/Violet placement tables and inspect scene-only placement fields.";
    private const int AlcremieSpeciesId = (int)global::pml.common.DevID.DEV_MAHOIPPU;

    public const string FixedSymbolsCategory = "fixedSymbols";
    public const string CoinSymbolsCategory = "coinSymbols";
    public const string VisibleItemsCategory = "visibleItems";
    public const string HiddenItemsCategory = "hiddenItems";
    public const string RummagingPointsCategory = "rummagingPoints";

    public const string PositionXField = "point.positionX";
    public const string PositionYField = "point.positionY";
    public const string PositionZField = "point.positionZ";
    public const string RotationPitchField = "point.rotationPitch";
    public const string RotationYawField = "point.rotationYaw";
    public const string RotationRollField = "point.rotationRoll";
    public const string PointNameField = "point.name";
    public const string PointTableKeyField = "point.tableKey";
    public const string PointLotteryKeyField = "point.lotteryKey";
    public const string PointUseTeraAuraField = "point.useTeraAura";
    public const string PointRainbowAuraField = "point.rainbowAura";

    public const string FixedTableKeyField = "fixed.tableKey";
    public const string FixedSpeciesIdField = "fixed.speciesId";
    public const string FixedFormField = "fixed.form";
    public const string FixedLevelField = "fixed.level";
    public const string FixedGenderField = "fixed.gender";
    public const string FixedShinyField = "fixed.shiny";
    public const string FixedIvModeField = "fixed.ivMode";
    public const string FixedIvHpField = "fixed.ivHp";
    public const string FixedIvAttackField = "fixed.ivAttack";
    public const string FixedIvDefenseField = "fixed.ivDefense";
    public const string FixedIvSpecialAttackField = "fixed.ivSpecialAttack";
    public const string FixedIvSpecialDefenseField = "fixed.ivSpecialDefense";
    public const string FixedIvSpeedField = "fixed.ivSpeed";
    public const string FixedGuaranteedPerfectIvsField = "fixed.guaranteedPerfectIvs";
    public const string FixedMoveModeField = "fixed.moveMode";
    public const string FixedMove1Field = "fixed.move1";
    public const string FixedMove2Field = "fixed.move2";
    public const string FixedMove3Field = "fixed.move3";
    public const string FixedMove4Field = "fixed.move4";
    public const string FixedAbilityModeField = "fixed.abilityMode";
    public const string FixedScaleModeField = "fixed.scaleMode";
    public const string FixedScaleValueField = "fixed.scaleValue";
    public const string FixedTeraTypeField = "fixed.teraType";
    public const string FixedAlcremieSweetField = "fixed.alcremieSweet";
    public const string FixedAiActionField = "fixed.ai.action";
    public const string FixedAiHungerField = "fixed.ai.hunger";
    public const string FixedAiFatigueField = "fixed.ai.fatigue";
    public const string FixedAiSleepinessField = "fixed.ai.sleepiness";
    public const string FixedAiPriorityField = "fixed.ai.priority";
    public const string FixedAiTriggerActionField = "fixed.ai.triggerAction";
    public const string FixedAiFrequencyField = "fixed.ai.frequency";
    public const string FixedSpawnMinDistanceField = "fixed.spawn.minDistance";
    public const string FixedSpawnMaxDistanceField = "fixed.spawn.maxDistance";
    public const string FixedDespawnMinDistanceField = "fixed.spawn.despawnMinDistance";
    public const string FixedDespawnMaxDistanceField = "fixed.spawn.despawnMaxDistance";
    public const string FixedSpawnModeField = "fixed.spawn.mode";
    public const string FixedSpawnOnLoadField = "fixed.spawn.onLoad";
    public const string FixedRespawnChanceField = "fixed.spawn.respawnChance";
    public const string FixedRequiredStoryFlagField = "fixed.spawn.requiredStoryFlag";

    public const string CoinLabelField = "coin.label";
    public const string CoinFirstNumberField = "coin.firstCoinNumber";
    public const string CoinDisableBattleOutField = "coin.disableBattleOut";
    public const string CoinEventEncounterField = "coin.eventEncounter";
    public const string CoinSpeciesIdField = "coin.speciesId";
    public const string CoinFormField = "coin.form";
    public const string CoinLevelField = "coin.level";
    public const string CoinGenderField = "coin.gender";
    public const string CoinShinyField = "coin.shiny";
    public const string CoinIvModeField = "coin.ivMode";
    public const string CoinGuaranteedPerfectIvsField = "coin.guaranteedPerfectIvs";
    public const string CoinHeldItemField = "coin.heldItem";
    public const string CoinDropItemField = "coin.dropItem";
    public const string CoinDropCountField = "coin.dropCount";
    public const string CoinNatureField = "coin.nature";
    public const string CoinNatureBoostField = "coin.natureBoost";
    public const string CoinAbilityModeField = "coin.abilityMode";
    public const string CoinMoveModeField = "coin.moveMode";
    public const string CoinMove1Field = "coin.move1";
    public const string CoinMove2Field = "coin.move2";
    public const string CoinMove3Field = "coin.move3";
    public const string CoinMove4Field = "coin.move4";
    public const string CoinTeraTypeField = "coin.teraType";
    public const string CoinScaleModeField = "coin.scaleMode";
    public const string CoinScaleValueField = "coin.scaleValue";
    public const string CoinRibbonField = "coin.ribbon";

    public const string RummagingCategoryField = "rummaging.category";
    public const string RummagingPatternField = "rummaging.pattern";

    private static readonly IReadOnlyList<CategorySeed> CategorySeeds =
    [
        new(FixedSymbolsCategory, "Fixed Symbols", "Static overworld Pokemon symbol tables and linked scene-only point data."),
        new(CoinSymbolsCategory, "Coin Symbols", "Gimmighoul coin symbol battle rows and linked scene-only point data."),
        new(VisibleItemsCategory, "Visible Items", "Visible overworld item scene placements. Scene values are read-only until TRSCN writing is supported."),
        new(HiddenItemsCategory, "Hidden Items", "Hidden item pool tables used by hidden item points."),
        new(RummagingPointsCategory, "Rummaging Points", "Rummaging point item-pool category and pattern tables."),
    ];

    private static readonly IReadOnlyList<string> HiddenItemTablePaths =
    [
        SvDataPaths.HiddenItemDataTableArray,
        SvDataPaths.HiddenItemDataTableSu1Array,
        SvDataPaths.HiddenItemDataTableSu2Array,
        SvDataPaths.HiddenItemDataTableLcArray,
    ];

    private readonly SvWorkflowFileSource fileSource;

    public SvPlacementWorkflowService(SvWorkflowFileSource? fileSource = null)
    {
        this.fileSource = fileSource ?? new SvWorkflowFileSource();
    }

    public SvWorkflowSummary CreateSummary(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.Placement,
            WorkflowLabel,
            WorkflowDescription);
    }

    public SvPlacementWorkflow Load(OpenedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var diagnostics = new List<ValidationDiagnostic>();
        var objects = new List<SvPlacedObjectRecord>();
        var sourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var labels = SvTextLabelLookup.None();

        try
        {
            labels = SvTextLabelLookup.Load(project, fileSource, diagnostics);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Error(
                $"Placement labels could not be loaded: {exception.Message}",
                "romfs/message/dat/English"));
        }

        var abilityResolver = SvPlacementAbilityResolver.Load(project, fileSource, labels, diagnostics);
        var moveResolver = SvDefaultMoveResolver.Load(project, fileSource, diagnostics);

        TryLoadFixedSymbols(project, labels, abilityResolver, moveResolver, objects, sourceFiles, diagnostics);
        TryLoadCoinSymbols(project, labels, abilityResolver, moveResolver, objects, sourceFiles, diagnostics);
        TryLoadHiddenItems(project, labels, objects, sourceFiles, diagnostics);
        TryLoadRummaging(project, labels, objects, sourceFiles, diagnostics);

        var categories = CategorySeeds
            .Select(seed => new SvPlacementCategory(
                seed.Id,
                seed.Label,
                seed.Description,
                objects.Count(placedObject => string.Equals(placedObject.CategoryId, seed.Id, StringComparison.Ordinal))))
            .ToArray();

        var summary = SvWorkflowSupport.CreateSummary(
            project,
            SvWorkflowIds.Placement,
            WorkflowLabel,
            WorkflowDescription,
            diagnostics.Count == 0 ? null : diagnostics);

        return new SvPlacementWorkflow(
            summary,
            objects,
            CreateEditableFields(labels),
            categories,
            new SvPlacementWorkflowStats(objects.Count, categories.Count(category => category.ObjectCount > 0), sourceFiles.Count),
            diagnostics);
    }

    private void TryLoadFixedSymbols(
        OpenedProject project,
        SvTextLabelLookup labels,
        SvPlacementAbilityResolver abilityResolver,
        SvDefaultMoveResolver moveResolver,
        ICollection<SvPlacedObjectRecord> objects,
        ISet<string> sourceFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            var source = fileSource.Read(project, SvDataPaths.FixedSymbolTableArray);
            sourceFiles.Add(source.RelativePath);
            var table = FixedSymbolTableArray.GetRootAsFixedSymbolTableArray(new ByteBuffer(source.Bytes));
            for (var index = 0; index < table.ValuesLength; index++)
            {
                var row = table.Values(index);
                if (row is null)
                {
                    continue;
                }

                objects.Add(ToFixedSymbolObject(index, row.Value, labels, abilityResolver, moveResolver, source));
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Warning(
                $"Fixed Symbols could not be loaded: {exception.Message}",
                $"romfs/{SvDataPaths.FixedSymbolTableArray}"));
        }
    }

    private void TryLoadCoinSymbols(
        OpenedProject project,
        SvTextLabelLookup labels,
        SvPlacementAbilityResolver abilityResolver,
        SvDefaultMoveResolver moveResolver,
        ICollection<SvPlacedObjectRecord> objects,
        ISet<string> sourceFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            var source = fileSource.Read(project, SvDataPaths.EventBattlePokemonArray);
            sourceFiles.Add(source.RelativePath);
            var table = EventBattlePokemonArray.GetRootAsEventBattlePokemonArray(new ByteBuffer(source.Bytes));
            for (var index = 0; index < table.ValuesLength; index++)
            {
                var row = table.Values(index);
                if (row is null)
                {
                    continue;
                }

                objects.Add(ToCoinSymbolObject(index, row.Value, labels, abilityResolver, moveResolver, source));
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Warning(
                $"Coin Symbols could not be loaded: {exception.Message}",
                $"romfs/{SvDataPaths.EventBattlePokemonArray}"));
        }
    }

    private void TryLoadHiddenItems(
        OpenedProject project,
        SvTextLabelLookup labels,
        ICollection<SvPlacedObjectRecord> objects,
        ISet<string> sourceFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        foreach (var path in HiddenItemTablePaths)
        {
            try
            {
                var source = fileSource.Read(project, path);
                sourceFiles.Add(source.RelativePath);
                var table = HiddenItemDataTableArray.GetRootAsHiddenItemDataTableArray(new ByteBuffer(source.Bytes));
                for (var index = 0; index < table.ValuesLength; index++)
                {
                    var row = table.Values(index);
                    if (row is null)
                    {
                        continue;
                    }

                    objects.Add(ToHiddenItemObject(index, row.Value, labels, source, path));
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
            {
                diagnostics.Add(SvWorkflowSupport.Warning(
                    $"Hidden Items table '{path}' could not be loaded: {exception.Message}",
                    $"romfs/{path}"));
            }
        }
    }

    private void TryLoadRummaging(
        OpenedProject project,
        SvTextLabelLookup labels,
        ICollection<SvPlacedObjectRecord> objects,
        ISet<string> sourceFiles,
        ICollection<ValidationDiagnostic> diagnostics)
    {
        try
        {
            var source = fileSource.Read(project, SvDataPaths.RummagingItemDataTableArray);
            sourceFiles.Add(source.RelativePath);
            var table = RummagingItemDataTableArray.GetRootAsRummagingItemDataTableArray(new ByteBuffer(source.Bytes));
            for (var index = 0; index < table.ValuesLength; index++)
            {
                var row = table.Values(index);
                if (row is null)
                {
                    continue;
                }

                objects.Add(ToRummagingObject(index, row.Value, labels, source));
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            diagnostics.Add(SvWorkflowSupport.Warning(
                $"Rummaging Points could not be loaded: {exception.Message}",
                $"romfs/{SvDataPaths.RummagingItemDataTableArray}"));
        }
    }

    private static SvPlacedObjectRecord ToFixedSymbolObject(
        int index,
        FixedSymbolTable row,
        SvTextLabelLookup labels,
        SvPlacementAbilityResolver abilityResolver,
        SvDefaultMoveResolver moveResolver,
        SvWorkflowFile source)
    {
        var pokeData = row.PokeDataSymbol;
        var speciesId = pokeData is null ? 0 : (int)pokeData.Value.DevId;
        var species = speciesId == 0 ? "None" : labels.Pokemon(speciesId);
        var tableKey = row.TableKey ?? string.Empty;
        var fields = new List<SvPlacementFieldValue>
        {
            Field(PointNameField, string.Empty, "Scene point name", isReadOnly: true),
            Field(PointTableKeyField, tableKey, tableKey, isReadOnly: true),
            Field(PointLotteryKeyField, string.Empty, "Scene-only", isReadOnly: true),
            Field(PointUseTeraAuraField, string.Empty, "Scene-only", isReadOnly: true),
            Field(PointRainbowAuraField, string.Empty, "Scene-only", isReadOnly: true),
            SceneOnlyField(PositionXField),
            SceneOnlyField(PositionYField),
            SceneOnlyField(PositionZField),
            SceneOnlyField(RotationPitchField),
            SceneOnlyField(RotationYawField),
            SceneOnlyField(RotationRollField),
            Field(FixedTableKeyField, tableKey, string.IsNullOrWhiteSpace(tableKey) ? "(blank)" : tableKey, isReadOnly: true),
        };

        if (pokeData is not null)
        {
            AddPokeDataSymbolFields(fields, pokeData.Value, labels, abilityResolver, moveResolver);
        }

        if (row.PokeAI is { } ai)
        {
            AddFixedAiFields(fields, ai);
        }

        if (row.PokeGeneration is { } generation)
        {
            AddFixedGenerationFields(fields, generation);
        }

        return new SvPlacedObjectRecord(
            CreateRecordId(FixedSymbolsCategory, source.VirtualPath, index),
            FixedSymbolsCategory,
            "Fixed Symbols",
            "FixedSymbol",
            string.IsNullOrWhiteSpace(tableKey) ? $"Fixed symbol {index}" : tableKey,
            "Fixed Symbol Table",
            source.RelativePath,
            0,
            index,
            null,
            null,
            species,
            tableKey,
            0,
            null,
            0,
            0,
            0,
            0,
            tableKey,
            fields,
            ToProvenance(source));
    }

    private static SvPlacedObjectRecord ToCoinSymbolObject(
        int index,
        EventBattlePokemon row,
        SvTextLabelLookup labels,
        SvPlacementAbilityResolver abilityResolver,
        SvDefaultMoveResolver moveResolver,
        SvWorkflowFile source)
    {
        var pokeData = row.PokeData;
        var speciesId = pokeData is null ? 0 : (int)pokeData.Value.DevId;
        var species = speciesId == 0 ? "None" : labels.Pokemon(speciesId);
        var label = row.Label ?? $"eventBattlePokemon[{index}]";
        var fields = new List<SvPlacementFieldValue>
        {
            Field(PointNameField, string.Empty, "Scene point name", isReadOnly: true),
            Field(CoinLabelField, label, label, isReadOnly: true),
            Field(CoinFirstNumberField, string.Empty, "Scene-only", isReadOnly: true),
            SceneOnlyField(PositionXField),
            SceneOnlyField(PositionYField),
            SceneOnlyField(PositionZField),
            SceneOnlyField(RotationPitchField),
            SceneOnlyField(RotationYawField),
            SceneOnlyField(RotationRollField),
            Field(CoinDisableBattleOutField, BoolValue(row.DisableBattleOut), SvLabels.Bool(row.DisableBattleOut)),
            Field(CoinEventEncounterField, BoolValue(row.EventEncount), SvLabels.Bool(row.EventEncount)),
        };

        if (pokeData is not null)
        {
            AddEventBattlePokemonFields(fields, pokeData.Value, labels, abilityResolver, moveResolver);
        }

        return new SvPlacedObjectRecord(
            CreateRecordId(CoinSymbolsCategory, source.VirtualPath, index),
            CoinSymbolsCategory,
            "Coin Symbols",
            "CoinSymbol",
            label,
            "Coin Symbol Table",
            source.RelativePath,
            0,
            index,
            null,
            null,
            species,
            label,
            0,
            null,
            0,
            0,
            0,
            0,
            label,
            fields,
            ToProvenance(source));
    }

    private static SvPlacedObjectRecord ToHiddenItemObject(
        int index,
        HiddenItemDataTable row,
        SvTextLabelLookup labels,
        SvWorkflowFile source,
        string virtualPath)
    {
        var tableId = row.TableId ?? $"hiddenItemDataTable[{index}]";
        var fields = new List<SvPlacementFieldValue>
        {
            Field(PointNameField, string.Empty, "Scene point name", isReadOnly: true),
            Field(PointTableKeyField, tableId, tableId, isReadOnly: true),
            SceneOnlyField(PositionXField),
            SceneOnlyField(PositionYField),
            SceneOnlyField(PositionZField),
            SceneOnlyField(RotationYawField),
        };

        for (var slot = 0; slot < 10; slot++)
        {
            var item = row.Item(slot);
            var itemId = item?.ItemId ?? 0;
            fields.Add(Field(
                HiddenItemField(slot, HiddenItemSlotField.ItemId),
                itemId.ToString(CultureInfo.InvariantCulture),
                itemId == 0 ? "None" : $"{itemId} {labels.Item(itemId)}"));
            fields.Add(Field(
                HiddenItemField(slot, HiddenItemSlotField.Chance),
                (item?.EmergePercent ?? 0).ToString(CultureInfo.InvariantCulture),
                (item?.EmergePercent ?? 0).ToString(CultureInfo.InvariantCulture)));
            fields.Add(Field(
                HiddenItemField(slot, HiddenItemSlotField.Count),
                (item?.DropCount ?? 0).ToString(CultureInfo.InvariantCulture),
                (item?.DropCount ?? 0).ToString(CultureInfo.InvariantCulture)));
        }

        return new SvPlacedObjectRecord(
            CreateRecordId(HiddenItemsCategory, virtualPath, index),
            HiddenItemsCategory,
            "Hidden Items",
            "HiddenItemPool",
            tableId,
            FormatHiddenItemRegion(virtualPath),
            source.RelativePath,
            0,
            index,
            null,
            null,
            tableId,
            tableId,
            0,
            null,
            0,
            0,
            0,
            0,
            tableId,
            fields,
            ToProvenance(source));
    }

    private static SvPlacedObjectRecord ToRummagingObject(
        int index,
        RummagingItemDataTable row,
        SvTextLabelLookup labels,
        SvWorkflowFile source)
    {
        var categoryLabel = FormatEnumLabel(row.Category);
        var patternLabel = FormatEnumLabel(row.Pattern);
        var fields = new List<SvPlacementFieldValue>
        {
            Field(PointNameField, string.Empty, "Scene point name", isReadOnly: true),
            SceneOnlyField(PositionXField),
            SceneOnlyField(PositionYField),
            SceneOnlyField(PositionZField),
            SceneOnlyField(RotationYawField),
            Field(RummagingCategoryField, ((int)row.Category).ToString(CultureInfo.InvariantCulture), categoryLabel),
            Field(RummagingPatternField, ((int)row.Pattern).ToString(CultureInfo.InvariantCulture), patternLabel),
        };

        for (var slot = 0; slot < 5; slot++)
        {
            var itemId = row.Item(slot);
            fields.Add(Field(
                RummagingItemField(slot),
                itemId.ToString(CultureInfo.InvariantCulture),
                itemId == 0 ? "None" : $"{itemId} {labels.Item(itemId)}"));
        }

        var label = $"{categoryLabel} / {patternLabel}";
        return new SvPlacedObjectRecord(
            CreateRecordId(RummagingPointsCategory, source.VirtualPath, index),
            RummagingPointsCategory,
            "Rummaging Points",
            "RummagingItemPool",
            label,
            "Rummaging Item Table",
            source.RelativePath,
            0,
            index,
            null,
            null,
            label,
            string.Empty,
            0,
            null,
            0,
            0,
            0,
            0,
            $"{(int)row.Category}:{(int)row.Pattern}",
            fields,
            ToProvenance(source));
    }

    private static void AddPokeDataSymbolFields(
        ICollection<SvPlacementFieldValue> fields,
        global::PokeDataSymbol data,
        SvTextLabelLookup labels,
        SvPlacementAbilityResolver abilityResolver,
        SvDefaultMoveResolver moveResolver)
    {
        var speciesId = (int)data.DevId;
        var abilities = abilityResolver.Resolve(speciesId, data.FormId);
        fields.Add(Field(FixedSpeciesIdField, speciesId, speciesId == 0 ? "None" : $"{speciesId} {labels.Pokemon(speciesId)}"));
        fields.Add(Field(FixedFormField, data.FormId));
        fields.Add(Field(FixedLevelField, data.Level));
        fields.Add(Field(FixedGenderField, (int)data.Sex, SvLabels.EnumName(data.Sex)));
        fields.Add(Field(FixedShinyField, (int)data.RareType, FormatRareType(data.RareType)));
        fields.Add(Field(FixedIvModeField, (int)data.TalentType, SvLabels.EnumName(data.TalentType)));
        AddParamSetFields(fields, data.TalentValue, FixedIvHpField, FixedIvAttackField, FixedIvDefenseField, FixedIvSpecialAttackField, FixedIvSpecialDefenseField, FixedIvSpeedField);
        fields.Add(Field(FixedGuaranteedPerfectIvsField, data.TalentVNum));
        var moveIds = ResolveMoveIds(speciesId, data.FormId, data.Level, moveResolver, data.Waza1, data.Waza2, data.Waza3, data.Waza4);
        fields.Add(Field(FixedMoveModeField, (int)data.WazaType, SvLabels.EnumName(data.WazaType)));
        fields.Add(Field(FixedMove1Field, moveIds[0], MoveLabel(moveIds[0], labels)));
        fields.Add(Field(FixedMove2Field, moveIds[1], MoveLabel(moveIds[1], labels)));
        fields.Add(Field(FixedMove3Field, moveIds[2], MoveLabel(moveIds[2], labels)));
        fields.Add(Field(FixedMove4Field, moveIds[3], MoveLabel(moveIds[3], labels)));
        fields.Add(Field(
            FixedAbilityModeField,
            (int)data.TokuseiIndex,
            FormatAbilityMode(data.TokuseiIndex, abilities),
            options: CreateAbilityModeOptions(abilities)));
        fields.Add(Field(FixedScaleModeField, (int)data.ScaleType, SvLabels.EnumName(data.ScaleType)));
        fields.Add(Field(FixedScaleValueField, data.ScaleValue));
        fields.Add(Field(FixedTeraTypeField, (int)data.GemType, SvLabels.EnumName(data.GemType)));
        fields.Add(Field(
            FixedAlcremieSweetField,
            (int)data.MahoippuViewId,
            SvLabels.EnumName(data.MahoippuViewId),
            isReadOnly: speciesId != AlcremieSpeciesId));
    }

    private static void AddEventBattlePokemonFields(
        ICollection<SvPlacementFieldValue> fields,
        global::PokeDataEventBattle data,
        SvTextLabelLookup labels,
        SvPlacementAbilityResolver abilityResolver,
        SvDefaultMoveResolver moveResolver)
    {
        var speciesId = (int)data.DevId;
        var abilities = abilityResolver.Resolve(speciesId, data.FormId);
        fields.Add(Field(CoinSpeciesIdField, speciesId, speciesId == 0 ? "None" : $"{speciesId} {labels.Pokemon(speciesId)}"));
        fields.Add(Field(CoinFormField, data.FormId));
        fields.Add(Field(CoinLevelField, data.Level));
        fields.Add(Field(CoinGenderField, (int)data.Sex, SvLabels.EnumName(data.Sex)));
        fields.Add(Field(CoinShinyField, (int)data.RareType, FormatRareType(data.RareType)));
        fields.Add(Field(CoinIvModeField, (int)data.TalentType, SvLabels.EnumName(data.TalentType)));
        fields.Add(Field(CoinGuaranteedPerfectIvsField, data.TalentVnum));
        fields.Add(Field(CoinHeldItemField, (int)data.Item, ItemLabel((int)data.Item, labels)));
        fields.Add(Field(CoinDropItemField, (int)data.DropItem, ItemLabel((int)data.DropItem, labels)));
        fields.Add(Field(CoinDropCountField, data.DropItemNum));
        fields.Add(Field(CoinNatureField, (int)data.Seikaku, SvLabels.EnumName(data.Seikaku)));
        fields.Add(Field(CoinNatureBoostField, (int)data.SeikakuHosei, SvLabels.EnumName(data.SeikakuHosei)));
        fields.Add(Field(
            CoinAbilityModeField,
            (int)data.Tokusei,
            FormatAbilityMode(data.Tokusei, abilities),
            options: CreateAbilityModeOptions(abilities)));
        var moveIds = ResolveMoveIds(speciesId, data.FormId, data.Level, moveResolver, data.Waza1, data.Waza2, data.Waza3, data.Waza4);
        fields.Add(Field(CoinMoveModeField, (int)data.WazaType, SvLabels.EnumName(data.WazaType)));
        fields.Add(Field(CoinMove1Field, moveIds[0], MoveLabel(moveIds[0], labels)));
        fields.Add(Field(CoinMove2Field, moveIds[1], MoveLabel(moveIds[1], labels)));
        fields.Add(Field(CoinMove3Field, moveIds[2], MoveLabel(moveIds[2], labels)));
        fields.Add(Field(CoinMove4Field, moveIds[3], MoveLabel(moveIds[3], labels)));
        fields.Add(Field(CoinTeraTypeField, (int)data.GemType, SvLabels.EnumName(data.GemType)));
        fields.Add(Field(CoinScaleModeField, (int)data.ScaleType, SvLabels.EnumName(data.ScaleType)));
        fields.Add(Field(CoinScaleValueField, data.ScaleValue));
        fields.Add(Field(CoinRibbonField, (int)data.SetRibbon, SvLabels.EnumName(data.SetRibbon)));
    }

    private static void AddFixedAiFields(ICollection<SvPlacementFieldValue> fields, FixedSymbolAI ai)
    {
        fields.Add(Field(FixedAiActionField, ai.ActionId));
        fields.Add(Field(FixedAiHungerField, ai.Hunger));
        fields.Add(Field(FixedAiFatigueField, ai.Fatigue));
        fields.Add(Field(FixedAiSleepinessField, ai.Sleepiness));
        fields.Add(Field(FixedAiPriorityField, ai.Priority));
        fields.Add(Field(FixedAiTriggerActionField, ai.TriggerActionId));
        fields.Add(Field(FixedAiFrequencyField, (int)ai.OverrideFrequency, FormatEnumLabel(ai.OverrideFrequency)));
    }

    private static void AddFixedGenerationFields(ICollection<SvPlacementFieldValue> fields, FixedSymbolGeneration generation)
    {
        fields.Add(Field(FixedSpawnMinDistanceField, generation.MinCreateDistance));
        fields.Add(Field(FixedSpawnMaxDistanceField, generation.MaxCreateDistance));
        fields.Add(Field(FixedDespawnMinDistanceField, generation.MinDestroyDistance));
        fields.Add(Field(FixedDespawnMaxDistanceField, generation.MaxDestroyDistance));
        fields.Add(Field(FixedSpawnModeField, (int)generation.GenerationPattern, FormatEnumLabel(generation.GenerationPattern)));
        fields.Add(Field(FixedSpawnOnLoadField, BoolValue(generation.FirstGenerate), SvLabels.Bool(generation.FirstGenerate)));
        fields.Add(Field(FixedRespawnChanceField, generation.RepopProbability));
        fields.Add(Field(FixedRequiredStoryFlagField, generation.RequireScenarioId ?? string.Empty, string.IsNullOrWhiteSpace(generation.RequireScenarioId) ? "None" : generation.RequireScenarioId, isReadOnly: true));
    }

    private static void AddParamSetFields(
        ICollection<SvPlacementFieldValue> fields,
        global::ParamSet? value,
        string hpField,
        string attackField,
        string defenseField,
        string specialAttackField,
        string specialDefenseField,
        string speedField)
    {
        fields.Add(Field(hpField, value?.Hp ?? 0));
        fields.Add(Field(attackField, value?.Atk ?? 0));
        fields.Add(Field(defenseField, value?.Def ?? 0));
        fields.Add(Field(specialAttackField, value?.SpAtk ?? 0));
        fields.Add(Field(specialDefenseField, value?.SpDef ?? 0));
        fields.Add(Field(speedField, value?.Agi ?? 0));
    }

    private static IReadOnlyList<SvPlacementEditableField> CreateEditableFields(SvTextLabelLookup labels)
    {
        var speciesOptions = CreateIndexedOptions(labels.PokemonNameCount, labels.Pokemon, includeNone: true);
        var itemOptions = CreateIndexedOptions(labels.ItemNameCount, labels.Item, includeNone: true);
        var moveOptions = CreateIndexedOptions(labels.MoveNameCount, labels.Move, includeNone: true);
        return
        [
            ReadOnly(PositionXField, "Position X", "Scene Placement", "Scene-only TRSCN coordinate."),
            ReadOnly(PositionYField, "Position Y", "Scene Placement", "Scene-only TRSCN coordinate."),
            ReadOnly(PositionZField, "Position Z", "Scene Placement", "Scene-only TRSCN coordinate."),
            ReadOnly(RotationPitchField, "Rotation Pitch", "Scene Placement", "Scene-only TRSCN rotation."),
            ReadOnly(RotationYawField, "Rotation Yaw", "Scene Placement", "Scene-only TRSCN rotation."),
            ReadOnly(RotationRollField, "Rotation Roll", "Scene Placement", "Scene-only TRSCN rotation."),
            ReadOnly(PointNameField, "Point name", "Scene Placement", "Scene-only point name."),
            ReadOnly(PointTableKeyField, "Pokemon data key", "Scene Placement", "Scene links a point to fixed-symbol table data."),
            ReadOnly(PointLotteryKeyField, "Tera lottery key", "Scene Placement", "Scene-only lottery link."),
            ReadOnly(PointUseTeraAuraField, "Use Tera aura", "Scene Placement", "Scene-only aura flag."),
            ReadOnly(PointRainbowAuraField, "Rainbow aura", "Scene Placement", "Scene-only aura flag."),
            ReadOnly(FixedTableKeyField, "Pokemon data key", "Fixed Symbol Pokemon", "Primary fixed-symbol table key."),
            Integer(FixedSpeciesIdField, "Species", "Fixed Symbol Pokemon", 0, ushort.MaxValue, speciesOptions),
            Integer(FixedFormField, "Form", "Fixed Symbol Pokemon", short.MinValue, short.MaxValue),
            Integer(FixedLevelField, "Level", "Fixed Symbol Pokemon", 0, 100),
            Integer(FixedGenderField, "Gender", "Fixed Symbol Pokemon", int.MinValue, int.MaxValue, CreateEnumOptions<global::SexType>()),
            Integer(FixedShinyField, "Shiny setting", "Fixed Symbol Pokemon", 0, 2, CreateRareTypeOptions()),
            Integer(FixedIvModeField, "IV mode", "Fixed Symbol IVs", int.MinValue, int.MaxValue, CreateEnumOptions<global::TalentType>()),
            Integer(FixedIvHpField, "HP IV", "Fixed Symbol IVs", 0, 31),
            Integer(FixedIvAttackField, "Attack IV", "Fixed Symbol IVs", 0, 31),
            Integer(FixedIvDefenseField, "Defense IV", "Fixed Symbol IVs", 0, 31),
            Integer(FixedIvSpecialAttackField, "Sp. Atk IV", "Fixed Symbol IVs", 0, 31),
            Integer(FixedIvSpecialDefenseField, "Sp. Def IV", "Fixed Symbol IVs", 0, 31),
            Integer(FixedIvSpeedField, "Speed IV", "Fixed Symbol IVs", 0, 31),
            Integer(FixedGuaranteedPerfectIvsField, "Guaranteed perfect IVs", "Fixed Symbol IVs", sbyte.MinValue, sbyte.MaxValue),
            Integer(FixedMoveModeField, "Move selection mode", "Fixed Symbol Moves", int.MinValue, int.MaxValue, CreateEnumOptions<global::WazaType>()),
            Integer(FixedMove1Field, "Move 1", "Fixed Symbol Moves", 0, ushort.MaxValue, moveOptions),
            Integer(FixedMove2Field, "Move 2", "Fixed Symbol Moves", 0, ushort.MaxValue, moveOptions),
            Integer(FixedMove3Field, "Move 3", "Fixed Symbol Moves", 0, ushort.MaxValue, moveOptions),
            Integer(FixedMove4Field, "Move 4", "Fixed Symbol Moves", 0, ushort.MaxValue, moveOptions),
            Integer(FixedAbilityModeField, "Ability mode", "Fixed Symbol Pokemon", 0, 4, CreateAbilityModeOptions(SvPlacementAbilitySet.Empty)),
            Integer(FixedScaleModeField, "Scale mode", "Fixed Symbol Pokemon", int.MinValue, int.MaxValue, CreateEnumOptions<global::SizeType>()),
            Integer(FixedScaleValueField, "Scale value", "Fixed Symbol Pokemon", short.MinValue, short.MaxValue),
            Integer(FixedTeraTypeField, "Tera type", "Fixed Symbol Pokemon", int.MinValue, int.MaxValue, CreateEnumOptions<global::GemType>()),
            Integer(
                FixedAlcremieSweetField,
                "Alcremie sweet",
                "Fixed Symbol Pokemon",
                byte.MinValue,
                byte.MaxValue,
                CreateEnumOptions<global::MahoippuViewID>(),
                "Only editable when the fixed symbol species is Alcremie."),
            Integer(FixedAiActionField, "AI action", "Fixed Symbol AI", int.MinValue, int.MaxValue),
            Number(FixedAiHungerField, "Hunger", "Fixed Symbol AI", float.MinValue, float.MaxValue),
            Number(FixedAiFatigueField, "Fatigue", "Fixed Symbol AI", float.MinValue, float.MaxValue),
            Number(FixedAiSleepinessField, "Sleepiness", "Fixed Symbol AI", float.MinValue, float.MaxValue),
            Integer(FixedAiPriorityField, "AI priority", "Fixed Symbol AI", int.MinValue, int.MaxValue),
            Integer(FixedAiTriggerActionField, "Trigger action", "Fixed Symbol AI", int.MinValue, int.MaxValue),
            Integer(FixedAiFrequencyField, "Behavior frequency", "Fixed Symbol AI", -1, 10, CreateEnumOptions<BehaviorFrequency>()),
            Number(FixedSpawnMinDistanceField, "Spawn distance min", "Fixed Symbol Spawning", 0, float.MaxValue),
            Number(FixedSpawnMaxDistanceField, "Spawn distance max", "Fixed Symbol Spawning", 0, float.MaxValue),
            Number(FixedDespawnMinDistanceField, "Despawn distance min", "Fixed Symbol Spawning", 0, float.MaxValue),
            Number(FixedDespawnMaxDistanceField, "Despawn distance max", "Fixed Symbol Spawning", 0, float.MaxValue),
            Integer(FixedSpawnModeField, "Spawn mode", "Fixed Symbol Spawning", 0, 1, CreateEnumOptions<GenerationPattern>()),
            Integer(FixedSpawnOnLoadField, "Spawn on load", "Fixed Symbol Spawning", 0, 1, BoolOptions()),
            Integer(FixedRespawnChanceField, "Respawn chance", "Fixed Symbol Spawning", 0, 100),
            ReadOnly(FixedRequiredStoryFlagField, "Required story flag", "Fixed Symbol Spawning", "String story flag gating spawn."),
            ReadOnly(CoinLabelField, "Battle label", "Coin Symbol Battle", "Event battle label linked by coin scene points."),
            ReadOnly(CoinFirstNumberField, "First coin number", "Coin Symbol Scene", "Scene-only first coin count."),
            Integer(CoinDisableBattleOutField, "Disable battle out", "Coin Symbol Battle", 0, 1, BoolOptions()),
            Integer(CoinEventEncounterField, "Event encounter", "Coin Symbol Battle", 0, 1, BoolOptions()),
            Integer(CoinSpeciesIdField, "Species", "Coin Symbol Pokemon", 0, ushort.MaxValue, speciesOptions),
            Integer(CoinFormField, "Form", "Coin Symbol Pokemon", short.MinValue, short.MaxValue),
            Integer(CoinLevelField, "Level", "Coin Symbol Pokemon", 0, 100),
            Integer(CoinGenderField, "Gender", "Coin Symbol Pokemon", int.MinValue, int.MaxValue, CreateEnumOptions<global::SexType>()),
            Integer(CoinShinyField, "Shiny setting", "Coin Symbol Pokemon", 0, 2, CreateRareTypeOptions()),
            Integer(CoinIvModeField, "IV mode", "Coin Symbol IVs", int.MinValue, int.MaxValue, CreateEnumOptions<global::TalentType>()),
            Integer(CoinGuaranteedPerfectIvsField, "Guaranteed perfect IVs", "Coin Symbol IVs", sbyte.MinValue, sbyte.MaxValue),
            Integer(CoinHeldItemField, "Held item", "Coin Symbol Pokemon", 0, int.MaxValue, itemOptions),
            Integer(CoinDropItemField, "Drop item", "Coin Symbol Drops", 0, int.MaxValue, itemOptions),
            Integer(CoinDropCountField, "Drop count", "Coin Symbol Drops", sbyte.MinValue, sbyte.MaxValue),
            Integer(CoinNatureField, "Nature", "Coin Symbol Pokemon", int.MinValue, int.MaxValue, CreateEnumOptions<global::SeikakuType>()),
            Integer(CoinNatureBoostField, "Nature mint override", "Coin Symbol Pokemon", int.MinValue, int.MaxValue, CreateEnumOptions<global::SeikakuType>()),
            Integer(CoinAbilityModeField, "Ability mode", "Coin Symbol Pokemon", 0, 4, CreateAbilityModeOptions(SvPlacementAbilitySet.Empty)),
            Integer(CoinMoveModeField, "Move selection mode", "Coin Symbol Moves", int.MinValue, int.MaxValue, CreateEnumOptions<global::WazaType>()),
            Integer(CoinMove1Field, "Move 1", "Coin Symbol Moves", 0, ushort.MaxValue, moveOptions),
            Integer(CoinMove2Field, "Move 2", "Coin Symbol Moves", 0, ushort.MaxValue, moveOptions),
            Integer(CoinMove3Field, "Move 3", "Coin Symbol Moves", 0, ushort.MaxValue, moveOptions),
            Integer(CoinMove4Field, "Move 4", "Coin Symbol Moves", 0, ushort.MaxValue, moveOptions),
            Integer(CoinTeraTypeField, "Tera type", "Coin Symbol Pokemon", int.MinValue, int.MaxValue, CreateEnumOptions<global::GemType>()),
            Integer(CoinScaleModeField, "Scale mode", "Coin Symbol Pokemon", int.MinValue, int.MaxValue, CreateEnumOptions<global::SizeType>()),
            Integer(CoinScaleValueField, "Scale value", "Coin Symbol Pokemon", short.MinValue, short.MaxValue),
            Integer(CoinRibbonField, "Ribbon", "Coin Symbol Pokemon", int.MinValue, int.MaxValue, CreateEnumOptions<global::RibbonType>()),
            Integer(RummagingCategoryField, "Rummaging category", "Rummaging Pool", 0, 5, CreateEnumOptions<RummagingCategory>()),
            Integer(RummagingPatternField, "Rummaging pattern", "Rummaging Pool", 0, 3, CreateEnumOptions<RummagingPattern>()),
            Integer(RummagingItemField(0), "Item 1", "Rummaging Pool", 0, int.MaxValue, itemOptions),
            Integer(RummagingItemField(1), "Item 2", "Rummaging Pool", 0, int.MaxValue, itemOptions),
            Integer(RummagingItemField(2), "Item 3", "Rummaging Pool", 0, int.MaxValue, itemOptions),
            Integer(RummagingItemField(3), "Item 4", "Rummaging Pool", 0, int.MaxValue, itemOptions),
            Integer(RummagingItemField(4), "Item 5", "Rummaging Pool", 0, int.MaxValue, itemOptions),
            .. CreateHiddenItemFields(itemOptions),
        ];
    }

    private static IReadOnlyList<SvPlacementEditableField> CreateHiddenItemFields(
        IReadOnlyList<SvPlacementEditableFieldOption> itemOptions)
    {
        var fields = new List<SvPlacementEditableField>();
        for (var slot = 0; slot < 10; slot++)
        {
            var group = $"Hidden Item Slot {slot + 1}";
            fields.Add(Integer(HiddenItemField(slot, HiddenItemSlotField.ItemId), $"Item {slot + 1}", group, 0, int.MaxValue, itemOptions));
            fields.Add(Integer(HiddenItemField(slot, HiddenItemSlotField.Chance), $"Emerge value {slot + 1}", group, 0, int.MaxValue));
            fields.Add(Integer(HiddenItemField(slot, HiddenItemSlotField.Count), $"Drop count {slot + 1}", group, 0, int.MaxValue));
        }

        return fields;
    }

    private static SvPlacementEditableField Integer(
        string field,
        string label,
        string group,
        double minimum,
        double maximum,
        IReadOnlyList<SvPlacementEditableFieldOption>? options = null,
        string description = "")
    {
        return new SvPlacementEditableField(
            field,
            label,
            group,
            "integer",
            minimum,
            maximum,
            false,
            description,
            options ?? Array.Empty<SvPlacementEditableFieldOption>());
    }

    private static SvPlacementEditableField Number(
        string field,
        string label,
        string group,
        double minimum,
        double maximum)
    {
        return new SvPlacementEditableField(field, label, group, "number", minimum, maximum);
    }

    private static SvPlacementEditableField ReadOnly(
        string field,
        string label,
        string group,
        string description)
    {
        return new SvPlacementEditableField(field, label, group, "text", double.MinValue, double.MaxValue, true, description);
    }

    private static SvPlacementFieldValue SceneOnlyField(string field)
    {
        return Field(field, string.Empty, "Scene-only", isReadOnly: true);
    }

    private static SvPlacementFieldValue Field(
        string field,
        int value,
        string? displayValue = null,
        bool isReadOnly = false,
        IReadOnlyList<SvPlacementEditableFieldOption>? options = null)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        return Field(field, text, displayValue ?? text, isReadOnly, options);
    }

    private static SvPlacementFieldValue Field(
        string field,
        float value,
        string? displayValue = null,
        bool isReadOnly = false,
        IReadOnlyList<SvPlacementEditableFieldOption>? options = null)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return Field(field, text, displayValue ?? text, isReadOnly, options);
    }

    private static SvPlacementFieldValue Field(
        string field,
        string value,
        string displayValue,
        bool isReadOnly = false,
        IReadOnlyList<SvPlacementEditableFieldOption>? options = null)
    {
        var definition = CreateEditableFields(SvTextLabelLookup.None()).FirstOrDefault(candidate => candidate.Field == field);
        return new SvPlacementFieldValue(
            field,
            definition?.Label ?? field,
            definition?.Group ?? "Placement Data",
            value,
            displayValue,
            isReadOnly || definition?.IsReadOnly == true,
            options);
    }

    private static IReadOnlyList<SvPlacementEditableFieldOption> CreateIndexedOptions(
        int count,
        Func<int, string> resolveName,
        bool includeNone)
    {
        var firstValue = includeNone ? 0 : 1;
        if (count <= firstValue)
        {
            return includeNone ? [new SvPlacementEditableFieldOption(0, "0 None")] : [];
        }

        return Enumerable
            .Range(firstValue, count - firstValue)
            .Select(value =>
            {
                var label = value == 0 ? "None" : resolveName(value);
                return new SvPlacementEditableFieldOption(value, $"{value.ToString(CultureInfo.InvariantCulture)} {label}");
            })
            .ToArray();
    }

    private static IReadOnlyList<SvPlacementEditableFieldOption> CreateEnumOptions<TEnum>()
        where TEnum : struct, Enum
    {
        return Enum
            .GetValues<TEnum>()
            .Select(value => new SvPlacementEditableFieldOption(
                Convert.ToInt32(value, CultureInfo.InvariantCulture),
                $"{Convert.ToInt32(value, CultureInfo.InvariantCulture)} {SvLabels.EnumName(value)}"))
            .OrderBy(option => option.Value)
            .ToArray();
    }

    private static IReadOnlyList<SvPlacementEditableFieldOption> CreateRareTypeOptions()
    {
        return
        [
            new((int)global::RareType.DEFAULT, "0 Default"),
            new((int)global::RareType.NO_RARE, "1 Not Shiny"),
            new((int)global::RareType.RARE, "2 Shiny"),
        ];
    }

    private static string FormatRareType(global::RareType value)
    {
        return value switch
        {
            global::RareType.DEFAULT => "Default",
            global::RareType.NO_RARE => "Not Shiny",
            global::RareType.RARE => "Shiny",
            _ => SvLabels.EnumName(value),
        };
    }

    private static IReadOnlyList<SvPlacementEditableFieldOption> CreateAbilityModeOptions(SvPlacementAbilitySet abilities)
    {
        return
        [
            new((int)global::TokuseiType.RANDOM_12, "0 Random Ability 1 or 2"),
            new((int)global::TokuseiType.RANDOM_123, "1 Random Ability 1, 2, or Hidden"),
            new((int)global::TokuseiType.SET_1, $"2 {FormatAbilitySlot(abilities.Ability1, "Ability 1")}"),
            new((int)global::TokuseiType.SET_2, $"3 {FormatAbilitySlot(abilities.Ability2, "Ability 2")}"),
            new((int)global::TokuseiType.SET_3, $"4 {FormatAbilitySlot(abilities.HiddenAbility, "Hidden Ability")}"),
        ];
    }

    private static string FormatAbilityMode(global::TokuseiType value, SvPlacementAbilitySet abilities)
    {
        return value switch
        {
            global::TokuseiType.RANDOM_12 => "Random Ability 1 or 2",
            global::TokuseiType.RANDOM_123 => "Random Ability 1, 2, or Hidden",
            global::TokuseiType.SET_1 => FormatAbilitySlot(abilities.Ability1, "Ability 1"),
            global::TokuseiType.SET_2 => FormatAbilitySlot(abilities.Ability2, "Ability 2"),
            global::TokuseiType.SET_3 => FormatAbilitySlot(abilities.HiddenAbility, "Hidden Ability"),
            _ => SvLabels.EnumName(value),
        };
    }

    private static string FormatAbilitySlot(string ability, string slot)
    {
        return string.Equals(ability, slot, StringComparison.Ordinal) ? slot : $"{ability} ({slot})";
    }

    private static IReadOnlyList<SvPlacementEditableFieldOption> BoolOptions()
    {
        return
        [
            new(0, "0 No"),
            new(1, "1 Yes"),
        ];
    }

    private static string BoolValue(bool value)
    {
        return value ? "1" : "0";
    }

    private static IReadOnlyList<int> ResolveMoveIds(
        int speciesId,
        int form,
        int level,
        SvDefaultMoveResolver moveResolver,
        params global::WazaSet?[] moves)
    {
        var moveIds = moves.Select(WazaId).ToArray();
        return moveIds.All(move => move == 0)
            ? moveResolver.Resolve(speciesId, form, level)
            : moveIds;
    }

    private static string MoveLabel(int moveId, SvTextLabelLookup labels)
    {
        return moveId == 0 ? "None" : $"{moveId} {labels.Move(moveId)}";
    }

    private static int WazaId(global::WazaSet? move)
    {
        return move is null ? 0 : (int)move.Value.WazaId;
    }

    private static string ItemLabel(int itemId, SvTextLabelLookup labels)
    {
        return itemId == 0 ? "None" : $"{itemId} {labels.Item(itemId)}";
    }

    private static string FormatEnumLabel<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return SvLabels.EnumName(value);
    }

    private static string FormatHiddenItemRegion(string virtualPath)
    {
        if (virtualPath.Contains("_su1", StringComparison.OrdinalIgnoreCase))
        {
            return "Hidden Items - The Teal Mask";
        }

        if (virtualPath.Contains("_su2", StringComparison.OrdinalIgnoreCase))
        {
            return "Hidden Items - The Indigo Disk";
        }

        if (virtualPath.Contains("_lc", StringComparison.OrdinalIgnoreCase))
        {
            return "Hidden Items - Link Club";
        }

        return "Hidden Items - Paldea";
    }

    private static SvPlacementProvenance ToProvenance(SvWorkflowFile source)
    {
        return new SvPlacementProvenance(source.RelativePath, source.SourceLayer, source.FileState);
    }

    private static string CreateRecordId(string category, string sourcePath, int index)
    {
        return $"{category}:{sourcePath}:{index.ToString(CultureInfo.InvariantCulture)}";
    }

    public static bool TryParseRecordId(string? objectId, out string category, out string sourcePath, out int index)
    {
        category = string.Empty;
        sourcePath = string.Empty;
        index = -1;

        var first = objectId?.IndexOf(':') ?? -1;
        var last = objectId?.LastIndexOf(':') ?? -1;
        if (string.IsNullOrWhiteSpace(objectId) || first <= 0 || last <= first)
        {
            return false;
        }

        category = objectId[..first];
        sourcePath = objectId[(first + 1)..last];
        return int.TryParse(objectId[(last + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }

    public static string HiddenItemField(int slot, HiddenItemSlotField field)
    {
        return field switch
        {
            HiddenItemSlotField.ItemId => $"hidden.item{slot + 1}.itemId",
            HiddenItemSlotField.Chance => $"hidden.item{slot + 1}.chance",
            HiddenItemSlotField.Count => $"hidden.item{slot + 1}.count",
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };
    }

    public static bool TryParseHiddenItemField(string field, out int slot, out HiddenItemSlotField slotField)
    {
        slot = -1;
        slotField = HiddenItemSlotField.ItemId;

        const string prefix = "hidden.item";
        if (!field.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = field[prefix.Length..];
        var separator = rest.IndexOf('.', StringComparison.Ordinal);
        if (separator <= 0
            || !int.TryParse(rest[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var oneBasedSlot))
        {
            return false;
        }

        slot = oneBasedSlot - 1;
        slotField = rest[(separator + 1)..] switch
        {
            "itemId" => HiddenItemSlotField.ItemId,
            "chance" => HiddenItemSlotField.Chance,
            "count" => HiddenItemSlotField.Count,
            _ => HiddenItemSlotField.Unknown,
        };

        return slot is >= 0 and < 10 && slotField != HiddenItemSlotField.Unknown;
    }

    public static string RummagingItemField(int slot)
    {
        return $"rummaging.item{slot + 1}";
    }

    public static bool TryParseRummagingItemField(string field, out int slot)
    {
        slot = -1;
        const string prefix = "rummaging.item";
        return field.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(field[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var oneBasedSlot)
            && (slot = oneBasedSlot - 1) is >= 0 and < 5;
    }

    public enum HiddenItemSlotField
    {
        Unknown,
        ItemId,
        Chance,
        Count,
    }

    private sealed class SvPlacementAbilityResolver
    {
        private readonly IReadOnlyDictionary<string, SvPlacementAbilitySet> abilitiesBySpeciesForm;

        private SvPlacementAbilityResolver(IReadOnlyDictionary<string, SvPlacementAbilitySet> abilitiesBySpeciesForm)
        {
            this.abilitiesBySpeciesForm = abilitiesBySpeciesForm;
        }

        public static SvPlacementAbilityResolver Empty { get; } = new(
            new Dictionary<string, SvPlacementAbilitySet>(StringComparer.Ordinal));

        public static SvPlacementAbilityResolver Load(
            OpenedProject project,
            SvWorkflowFileSource fileSource,
            SvTextLabelLookup labels,
            ICollection<ValidationDiagnostic> diagnostics)
        {
            try
            {
                var source = fileSource.Read(project, SvDataPaths.PersonalArray);
                var table = global::personal_table.GetRootAspersonal_table(new ByteBuffer(source.Bytes));
                var lookup = new Dictionary<string, SvPlacementAbilitySet>(StringComparer.Ordinal);
                for (var index = 0; index < table.EntryLength; index++)
                {
                    var row = table.Entry(index);
                    if (row?.Species is not { } species || !row.Value.IsPresent)
                    {
                        continue;
                    }

                    lookup.TryAdd(
                        CreateKey(species.Species, species.Form),
                        new SvPlacementAbilitySet(
                            labels.Ability(row.Value.Ability1),
                            labels.Ability(row.Value.Ability2),
                            labels.Ability(row.Value.AbilityHidden)));
                }

                return new SvPlacementAbilityResolver(lookup);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
            {
                diagnostics.Add(SvWorkflowSupport.Warning(
                    $"Placement ability names could not be resolved from Pokemon Data: {exception.Message}",
                    $"romfs/{SvDataPaths.PersonalArray}"));
                return Empty;
            }
        }

        public SvPlacementAbilitySet Resolve(int species, int form)
        {
            return abilitiesBySpeciesForm.TryGetValue(CreateKey(species, form), out var exact)
                ? exact
                : abilitiesBySpeciesForm.TryGetValue(CreateKey(species, 0), out var baseForm)
                    ? baseForm
                    : SvPlacementAbilitySet.Empty;
        }

        private static string CreateKey(int species, int form)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{species}:{form}");
        }
    }

    private sealed record SvPlacementAbilitySet(
        string Ability1,
        string Ability2,
        string HiddenAbility)
    {
        public static SvPlacementAbilitySet Empty { get; } = new("Ability 1", "Ability 2", "Hidden Ability");
    }

    private sealed record CategorySeed(string Id, string Label, string Description);
}
