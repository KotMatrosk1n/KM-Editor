// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Editing;
using KM.Api.AngeFight;
using KM.Api.Encounters;
using KM.Api.Gifts;
using KM.Api.Items;
using KM.Api.ModMerger;
using KM.Api.Moves;
using KM.Api.Placement;
using KM.Api.Pokemon;
using KM.Api.Shops;
using KM.Api.SpreadsheetImport;
using KM.Api.StaticEncounters;
using KM.Api.Text;
using KM.Api.TypeChart;
using KM.Api.Trainers;
using KM.Api.Trades;
using KM.Api.Workflows;
using KM.Api.ZaCache;
using KM.ZA.Encounters;
using KM.ZA.AngeFight;
using KM.ZA.Gifts;
using KM.ZA.Items;
using KM.ZA.ModMerger;
using KM.ZA.Moves;
using KM.ZA.Placement;
using KM.ZA.Pokemon;
using KM.ZA.Shops;
using KM.ZA.StaticEncounters;
using KM.ZA.Text;
using KM.ZA.TypeChart;
using KM.ZA.Trainers;
using KM.ZA.Trades;
using KM.ZA.DumpImport;
using KM.ZA.Workflows;

namespace KM.Tools.Bridge;

public static class ZaBridgeMapper
{
    public static ZaCacheMode ToCore(ZaCacheModeDto mode)
    {
        return mode switch
        {
            ZaCacheModeDto.Minimal => ZaCacheMode.Minimal,
            ZaCacheModeDto.Balanced => ZaCacheMode.Balanced,
            ZaCacheModeDto.Performance => ZaCacheMode.Performance,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    public static ZaCacheStatusResponse ToDto(ZaCacheStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new ZaCacheStatusResponse(new ZaCacheStatusDto(
            new ZaCacheSettingsDto(ToDto(status.Settings.Mode), status.Settings.MaxCacheSizeBytes),
            status.CacheSizeBytes,
            status.WarmupCompleted,
            status.WarmupTotal,
            status.ProgressPercent,
            status.Phase,
            status.Message,
            status.IsActiveProjectPreserved));
    }

    public static ZaOutputMode ToCore(ChangePlanOutputModeDto? outputMode)
    {
        return outputMode switch
        {
            null => ZaOutputMode.Standalone,
            ChangePlanOutputModeDto.Standalone => ZaOutputMode.Standalone,
            ChangePlanOutputModeDto.TrinityModManager => ZaOutputMode.TrinityModManager,
            ChangePlanOutputModeDto.TrinityBypass => ZaOutputMode.TrinityBypass,
            _ => throw new ArgumentOutOfRangeException(nameof(outputMode), outputMode, null),
        };
    }

    public static ListWorkflowsResponse ToDto(ZaWorkflowList workflowList)
    {
        ArgumentNullException.ThrowIfNull(workflowList);

        return new ListWorkflowsResponse(workflowList.Workflows.Select(ToDto).ToArray());
    }

    public static LoadPokemonWorkflowResponse ToDto(ZaPokemonWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadPokemonWorkflowResponse(ToPokemonWorkflowDto(workflow));
    }

    public static LoadItemsWorkflowResponse ToDto(ZaItemsWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadItemsWorkflowResponse(ToItemsWorkflowDto(workflow));
    }

    public static LoadSpreadsheetImportWorkflowResponse ToDto(ZaDumpImportWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadSpreadsheetImportWorkflowResponse(ToSpreadsheetImportWorkflowDto(workflow));
    }

    public static PreviewSpreadsheetImportResponse ToDto(ZaDumpImportExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new PreviewSpreadsheetImportResponse(
            ToSpreadsheetImportWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            ToDto(result.Preview),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadMovesWorkflowResponse ToDto(ZaMovesWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadMovesWorkflowResponse(ToMovesWorkflowDto(workflow));
    }

    public static LoadTextWorkflowResponse ToDto(ZaTextWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadTextWorkflowResponse(ToTextWorkflowDto(workflow));
    }

    public static LoadShopsWorkflowResponse ToDto(ZaShopsWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadShopsWorkflowResponse(ToShopsWorkflowDto(workflow));
    }

    public static LoadTrainersWorkflowResponse ToDto(ZaTrainersWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadTrainersWorkflowResponse(ToTrainersWorkflowDto(workflow));
    }

    public static LoadPlacementWorkflowResponse ToDto(ZaPlacementWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadPlacementWorkflowResponse(ToPlacementWorkflowDto(workflow));
    }

    public static LoadGiftPokemonWorkflowResponse ToDto(ZaGiftPokemonWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadGiftPokemonWorkflowResponse(ToGiftPokemonWorkflowDto(workflow));
    }

    public static LoadEncountersWorkflowResponse ToDto(ZaEncountersWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadEncountersWorkflowResponse(ToEncountersWorkflowDto(workflow));
    }

    public static LoadStaticEncountersWorkflowResponse ToDto(ZaStaticEncountersWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadStaticEncountersWorkflowResponse(ToStaticEncountersWorkflowDto(workflow));
    }

    public static LoadTypeChartWorkflowResponse ToDto(ZaTypeChartWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadTypeChartWorkflowResponse(ToTypeChartWorkflowDto(workflow));
    }

    public static LoadAngeFightWorkflowResponse ToDto(ZaAngeFightWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadAngeFightWorkflowResponse(ToAngeFightWorkflowDto(workflow));
    }

    public static LoadTradePokemonWorkflowResponse ToDto(ZaTradePokemonWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadTradePokemonWorkflowResponse(ToTradePokemonWorkflowDto(workflow));
    }

    public static LoadZaModMergerWorkflowResponse ToDto(ZaModMergerWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadZaModMergerWorkflowResponse(ToWorkflowDto(workflow));
    }

    public static UpdatePokemonFieldResponse ToDto(ZaPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePokemonFieldResponse(
            ToPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateItemFieldResponse ToDto(ZaItemsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateItemFieldResponse(
            ToItemsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateItemFieldsResponse ToItemFieldsDto(ZaItemsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateItemFieldsResponse(
            ToItemsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateMoveFieldResponse ToDto(ZaMovesEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateMoveFieldResponse(
            ToMovesWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateMoveFieldsResponse ToMoveFieldsDto(ZaMovesEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateMoveFieldsResponse(
            ToMovesWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateTextEntryResponse ToDto(ZaTextEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateTextEntryResponse(
            ToTextWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateShopInventoryItemResponse ToDto(ZaShopsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateShopInventoryItemResponse(
            ToShopsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateTrainerFieldResponse ToDto(ZaTrainersEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateTrainerFieldResponse(
            ToTrainersWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateTrainerFieldsResponse ToTrainerFieldsDto(ZaTrainersEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateTrainerFieldsResponse(
            ToTrainersWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdatePlacementObjectFieldResponse ToDto(ZaPlacementEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePlacementObjectFieldResponse(
            ToPlacementWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdatePlacementObjectFieldsResponse ToPlacementObjectFieldsDto(ZaPlacementEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePlacementObjectFieldsResponse(
            ToPlacementWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateGiftPokemonFieldResponse ToDto(ZaGiftPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateGiftPokemonFieldResponse(
            ToGiftPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateEncounterSlotFieldResponse ToDto(ZaEncountersEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateEncounterSlotFieldResponse(
            ToEncountersWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateEncounterSlotFieldsResponse ToEncounterSlotFieldsDto(ZaEncountersEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateEncounterSlotFieldsResponse(
            ToEncountersWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateStaticEncounterFieldResponse ToDto(ZaStaticEncountersEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateStaticEncounterFieldResponse(
            ToStaticEncountersWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static StageTypeChartResponse ToDto(ZaTypeChartEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageTypeChartResponse(
            ToTypeChartWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static StageTypeChartUninstallResponse ToTypeChartUninstallDto(ZaTypeChartEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageTypeChartUninstallResponse(
            ToTypeChartWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static StageAngeFightResponse ToDto(ZaAngeFightEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageAngeFightResponse(
            ToAngeFightWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static StageAngeFightUninstallResponse ToAngeFightUninstallDto(
        ZaAngeFightEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageAngeFightUninstallResponse(
            ToAngeFightWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateGiftPokemonFieldsResponse ToGiftPokemonFieldsDto(ZaGiftPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateGiftPokemonFieldsResponse(
            ToGiftPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateTradePokemonFieldResponse ToDto(ZaTradePokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateTradePokemonFieldResponse(
            ToTradePokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateTradePokemonFieldsResponse ToTradePokemonFieldsDto(ZaTradePokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateTradePokemonFieldsResponse(
            ToTradePokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static StageZaModMergeResponse ToDto(ZaModMergerStageResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageZaModMergeResponse(
            ToWorkflowDto(result.Workflow),
            ToDto(result.Preview),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static ApplyZaModMergeResponse ToDto(ZaModMergerApplyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ApplyZaModMergeResponse(
            ToWorkflowDto(result.Workflow),
            ToDto(result.Preview),
            result.WrittenFiles,
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdatePokemonFieldsResponse ToPokemonFieldsDto(ZaPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePokemonFieldsResponse(
            ToPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdatePokemonLearnsetResponse ToDtoLearnsetUpdate(ZaPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePokemonLearnsetResponse(
            ToPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdatePokemonEvolutionResponse ToDtoEvolutionUpdate(ZaPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePokemonEvolutionResponse(
            ToPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static ValidateEditSessionResponse ToDto(ZaEditSessionValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);

        return new ValidateEditSessionResponse(
            EditSessionBridgeMapper.ToDto(validation.Session),
            validation.IsValid,
            validation.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static WorkflowSummaryDto ToDto(ZaWorkflowSummary summary)
    {
        return new WorkflowSummaryDto(
            summary.Id,
            summary.Label,
            summary.Description,
            ToDto(summary.Availability),
            summary.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static PokemonWorkflowDto ToPokemonWorkflowDto(ZaPokemonWorkflow workflow)
    {
        return new PokemonWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Pokemon.Select(ToDto).ToArray(),
            new PokemonWorkflowStatsDto(
                workflow.Stats.TotalPokemonCount,
                workflow.Stats.PresentPokemonCount,
                workflow.Stats.TotalEvolutionCount,
                workflow.Stats.TotalLearnsetMoveCount,
                workflow.Stats.SourceFileCount),
            workflow.EvolutionMethodOptions.Select(ToDto).ToArray(),
            workflow.LearnsetMoveOptions.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ItemsWorkflowDto ToItemsWorkflowDto(ZaItemsWorkflow workflow)
    {
        return new ItemsWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Items.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new ItemsWorkflowStatsDto(
                workflow.Stats.TotalItemCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static MovesWorkflowDto ToMovesWorkflowDto(ZaMovesWorkflow workflow)
    {
        return new MovesWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Moves.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new MovesWorkflowStatsDto(
                workflow.Stats.TotalMoveCount,
                workflow.Stats.EnabledMoveCount,
                workflow.Stats.SourceFileCount,
                workflow.Stats.ActiveFlagCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static TextWorkflowDto ToTextWorkflowDto(ZaTextWorkflow workflow)
    {
        return new TextWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Entries.Select(ToDto).ToArray(),
            workflow.DialogueReferences.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new TextWorkflowStatsDto(
                workflow.Stats.TotalTextEntryCount,
                workflow.Stats.DialogueReferenceCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ShopsWorkflowDto ToShopsWorkflowDto(ZaShopsWorkflow workflow)
    {
        return new ShopsWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Shops.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new ShopsWorkflowStatsDto(
                workflow.Stats.TotalShopCount,
                workflow.Stats.TotalInventoryItemCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray())
        {
            EditorFamily = "za",
        };
    }

    private static TrainersWorkflowDto ToTrainersWorkflowDto(ZaTrainersWorkflow workflow)
    {
        return new TrainersWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Trainers.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new TrainersWorkflowStatsDto(
                workflow.Stats.TotalTrainerCount,
                workflow.Stats.TotalPokemonCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static PlacementWorkflowDto ToPlacementWorkflowDto(ZaPlacementWorkflow workflow)
    {
        return new PlacementWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Objects.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new PlacementWorkflowStatsDto(
                workflow.Stats.TotalObjectCount,
                workflow.Stats.TotalAreaCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray(),
            workflow.Categories.Select(ToDto).ToArray());
    }

    private static GiftPokemonWorkflowDto ToGiftPokemonWorkflowDto(ZaGiftPokemonWorkflow workflow)
    {
        return new GiftPokemonWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Gifts.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new GiftPokemonWorkflowStatsDto(
                workflow.Stats.TotalGiftCount,
                workflow.Stats.EggGiftCount,
                workflow.Stats.FixedIvGiftCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray())
        {
            EditorFamily = "za",
        };
    }

    private static TradePokemonWorkflowDto ToTradePokemonWorkflowDto(ZaTradePokemonWorkflow workflow)
    {
        return new TradePokemonWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Trades.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new TradePokemonWorkflowStatsDto(
                workflow.Stats.TotalTradeCount,
                workflow.Stats.FixedIvTradeCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray())
        {
            EditorFamily = "za",
        };
    }

    private static EncountersWorkflowDto ToEncountersWorkflowDto(ZaEncountersWorkflow workflow)
    {
        return new EncountersWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Tables.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new EncountersWorkflowStatsDto(
                workflow.Stats.TotalTableCount,
                workflow.Stats.TotalSlotCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static EncounterTableRecordDto ToDto(ZaEncounterTableRecord table)
    {
        return new EncounterTableRecordDto(
            table.TableId,
            table.Location,
            table.Area,
            table.EncounterType,
            table.GameVersion,
            table.ArchiveMember,
            table.Slots.Select(ToDto).ToArray(),
            ToDto(table.Provenance),
            table.LocationKey,
            table.LocationSort,
            table.TableLabel,
            table.TableDetails)
        {
            LocationDetails = table.LocationDetails,
            SpawnerCategory = table.SpawnerCategory,
        };
    }

    private static EncounterSlotRecordDto ToDto(ZaEncounterSlotRecord slot)
    {
        return new EncounterSlotRecordDto(
            slot.Slot,
            slot.SpeciesId,
            slot.Species,
            slot.Form,
            slot.LevelMin,
            slot.LevelMax,
            slot.Weight,
            slot.TimeOfDay,
            slot.Weather,
            slot.EncounterDataId,
            slot.EncounterKind,
            slot.IsAlpha,
            slot.EncounterRecordId,
            slot.ContributesToWildZoneCompletion,
            slot.AlphaChancePercent,
            slot.AlphaLevelBonus,
            slot.SlotMaxCount,
            slot.AppearanceMinCount,
            slot.AppearanceMaxCount,
            slot.AppearanceObjectCount,
            slot.CanEditWeight,
            slot.CanEditSlotMaxCount,
            slot.CanEditAppearanceCounts);
    }

    private static EncounterProvenanceDto ToDto(ZaEncounterProvenance provenance)
    {
        return new EncounterProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static EncounterEditableFieldDto ToDto(ZaEncounterEditableField field)
    {
        return new EncounterEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static EncounterEditableFieldOptionDto ToDto(ZaEncounterEditableFieldOption option)
    {
        return new EncounterEditableFieldOptionDto(option.Value, option.Label);
    }

    private static StaticEncountersWorkflowDto ToStaticEncountersWorkflowDto(
        ZaStaticEncountersWorkflow workflow)
    {
        return new StaticEncountersWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Encounters.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new StaticEncountersWorkflowStatsDto(
                workflow.Stats.TotalEncounterCount,
                null,
                workflow.Stats.FixedIvEncounterCount,
                workflow.Stats.SourceFileCount)
            {
                FixedSymbolCount = workflow.Stats.PokemonDataEncounterCount,
                CoinSymbolCount = 0,
            },
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray())
        {
            EditorFamily = "za",
        };
    }

    private static StaticEncounterRecordDto ToDto(ZaStaticEncounterEntry encounter)
    {
        return new StaticEncounterRecordDto(
            encounter.EncounterIndex,
            encounter.Label,
            encounter.EncounterId,
            encounter.SpeciesId,
            encounter.Species,
            encounter.Form,
            encounter.Level,
            encounter.HeldItemId,
            encounter.HeldItem,
            encounter.Ability,
            encounter.AbilityLabel,
            encounter.Nature,
            encounter.NatureLabel,
            encounter.Gender,
            encounter.GenderLabel,
            encounter.ShinyLock,
            encounter.ShinyLockLabel,
            encounter.EncounterScenario,
            encounter.EncounterScenarioLabel,
            null,
            null,
            ToDto(encounter.Evs),
            ToDto(encounter.Ivs),
            encounter.FlawlessIvCount,
            encounter.IvSummary,
            encounter.Moves.Select(ToDto).ToArray(),
            new StaticEncounterProvenanceDto(
                encounter.Provenance.SourceFile,
                ProjectBridgeMapper.ToDto(encounter.Provenance.SourceLayer),
                ProjectBridgeMapper.ToDto(encounter.Provenance.FileState)))
        {
            EditorFamily = "za",
            CategoryId = encounter.CategoryId,
            CategoryLabel = encounter.CategoryLabel,
            ScenarioDetails = encounter.ScenarioDetails,
            SupportedFields = encounter.SupportedFields,
            FieldValues = encounter.FieldValues,
            FieldDisplayValues = encounter.FieldDisplayValues,
            FieldReadOnly = encounter.FieldReadOnly,
            AbilityOptions = encounter.AbilityOptions.Select(ToDto).ToArray(),
        };
    }

    private static StaticEncounterStatsDto ToDto(ZaStaticEncounterStatsRecord stats)
    {
        return new StaticEncounterStatsDto(
            stats.HP,
            stats.Attack,
            stats.Defense,
            stats.SpecialAttack,
            stats.SpecialDefense,
            stats.Speed);
    }

    private static StaticEncounterMoveDto ToDto(ZaStaticEncounterMoveRecord move)
    {
        return new StaticEncounterMoveDto(move.Slot, move.MoveId, move.Move);
    }

    private static StaticEncounterEditableFieldDto ToDto(ZaStaticEncounterEditableField field)
    {
        return new StaticEncounterEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray())
        {
            Group = field.Group,
            IsReadOnly = field.IsReadOnly,
            Description = field.Description,
        };
    }

    private static StaticEncounterEditableFieldOptionDto ToDto(
        ZaStaticEncounterEditableFieldOption option)
    {
        return new StaticEncounterEditableFieldOptionDto(option.Value, option.Label);
    }

    private static TypeChartWorkflowDto ToTypeChartWorkflowDto(ZaTypeChartWorkflow workflow)
    {
        return new TypeChartWorkflowDto(
            ToDto(workflow.Summary),
            workflow.InstallStatus,
            workflow.InstallMessage,
            workflow.BuildId,
            workflow.ChartOffsetHex,
            workflow.DetectedGame is null ? null : ProjectBridgeMapper.ToDto(workflow.DetectedGame.Value),
            workflow.Source is null ? null : ToDto(workflow.Source),
            workflow.Types.Select(ToDto).ToArray(),
            workflow.Cells.Select(ToDto).ToArray(),
            new TypeChartWorkflowStatsDto(
                workflow.Stats.SourceFileCount,
                workflow.Stats.OutputFileCount,
                workflow.Stats.ChartCellCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static TypeChartSourceRecordDto ToDto(ZaTypeChartSourceRecord source)
    {
        return new TypeChartSourceRecordDto(
            source.SourceId,
            source.Label,
            source.RelativePath,
            source.Status,
            ToDto(source.Provenance));
    }

    private static TypeChartProvenanceDto ToDto(ZaTypeChartProvenance provenance)
    {
        return new TypeChartProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static TypeChartTypeDefinitionDto ToDto(ZaTypeChartTypeDefinition type)
    {
        return new TypeChartTypeDefinitionDto(
            type.TypeIndex,
            type.Label,
            type.ShortLabel,
            type.Color);
    }

    private static TypeChartCellDto ToDto(ZaTypeChartCell cell)
    {
        return new TypeChartCellDto(
            cell.AttackTypeIndex,
            cell.DefenseTypeIndex,
            cell.Effectiveness,
            cell.VanillaEffectiveness);
    }

    private static AngeFightWorkflowDto ToAngeFightWorkflowDto(
        ZaAngeFightWorkflow workflow)
    {
        return new AngeFightWorkflowDto(
            ToDto(workflow.Summary),
            workflow.InstallStatus,
            workflow.InstallMessage,
            workflow.CanUninstall,
            workflow.UninstallMessage,
            workflow.Sources.Select(ToDto).ToArray(),
            workflow.Flowers.Select(ToDto).ToArray(),
            workflow.Attacks.Select(ToDto).ToArray(),
            new AngeFightWorkflowStatsDto(
                workflow.Stats.SourceFileCount,
                workflow.Stats.FlowerCount,
                workflow.Stats.AttackCount,
                workflow.Stats.EditableValueCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static AngeFightSourceRecordDto ToDto(ZaAngeFightSourceRecord source)
    {
        return new AngeFightSourceRecordDto(
            source.Id,
            source.Label,
            source.RelativePath,
            source.Status,
            source.EffectiveSha256,
            source.VanillaSha256,
            new AngeFightProvenanceDto(
                source.Provenance.RelativePath,
                ProjectBridgeMapper.ToDto(source.Provenance.SourceLayer),
                ProjectBridgeMapper.ToDto(source.Provenance.State)));
    }

    private static AngeFightFlowerRecordDto ToDto(ZaAngeFightFlowerRecord flower)
    {
        return new AngeFightFlowerRecordDto(
            flower.FlowerId,
            flower.Label,
            flower.Hp,
            flower.VanillaHp);
    }

    private static AngeFightAttackRecordDto ToDto(ZaAngeFightAttackRecord attack)
    {
        return new AngeFightAttackRecordDto(
            attack.MoveId,
            attack.Label,
            attack.Usage,
            attack.BulletId,
            attack.AttackId,
            attack.DamageToPokemon,
            attack.DamageToPlayer,
            attack.VanillaDamageToPokemon,
            attack.VanillaDamageToPlayer,
            attack.HitIntervalSeconds,
            attack.SharedByMultipleActions,
            attack.CanRepeatHit);
    }

    private static SpreadsheetImportWorkflowDto ToSpreadsheetImportWorkflowDto(
        ZaDumpImportWorkflow workflow)
    {
        return new SpreadsheetImportWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Profiles.Select(ToDto).ToArray(),
            new SpreadsheetImportWorkflowStatsDto(
                workflow.Stats.TotalProfileCount,
                workflow.Stats.TotalColumnCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static SpreadsheetImportProfileRecordDto ToDto(ZaDumpImportProfileRecord profile)
    {
        return new SpreadsheetImportProfileRecordDto(
            profile.ProfileId,
            profile.Name,
            profile.SourceKind,
            profile.TargetWorkflow,
            profile.Status,
            profile.Description,
            profile.Columns.Select(ToDto).ToArray(),
            ToDto(profile.Provenance));
    }

    private static SpreadsheetImportColumnRecordDto ToDto(ZaDumpImportColumnRecord column)
    {
        return new SpreadsheetImportColumnRecordDto(
            column.Column,
            column.Header,
            column.ValueKind,
            column.IsRequired,
            column.Description);
    }

    private static SpreadsheetImportProvenanceDto ToDto(ZaDumpImportProvenance provenance)
    {
        return new SpreadsheetImportProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static SpreadsheetImportPreviewDto ToDto(ZaDumpImportPreview preview)
    {
        return new SpreadsheetImportPreviewDto(
            preview.ProfileId,
            preview.SourcePath,
            preview.TotalRowCount,
            preview.AcceptedRowCount,
            preview.RejectedRowCount,
            preview.SkippedRowCount,
            preview.Rows.Select(ToDto).ToArray());
    }

    private static SpreadsheetImportRowPreviewRecordDto ToDto(ZaDumpImportRowPreviewRecord row)
    {
        return new SpreadsheetImportRowPreviewRecordDto(
            row.RowNumber,
            row.RecordId,
            row.Status,
            row.Summary,
            row.Cells.Select(ToDto).ToArray(),
            row.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static SpreadsheetImportCellPreviewRecordDto ToDto(ZaDumpImportCellPreviewRecord cell)
    {
        return new SpreadsheetImportCellPreviewRecordDto(
            cell.Header,
            cell.Field,
            cell.Value,
            cell.Status,
            cell.Message);
    }

    private static ZaModMergerWorkflowDto ToWorkflowDto(ZaModMergerWorkflow workflow)
    {
        return new ZaModMergerWorkflowDto(
            ToDto(workflow.Summary),
            workflow.OutputRootPath,
            workflow.Sources.Select(ToDto).ToArray(),
            new ZaModMergerWorkflowStatsDto(
                workflow.Stats.SourceCount,
                workflow.Stats.EnabledSourceCount,
                workflow.Stats.SourceFileCount,
                workflow.Stats.OutputFileCount,
                workflow.Stats.OverrideCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static PokemonRecordDto ToDto(ZaPokemonRecord pokemon)
    {
        return new PokemonRecordDto(
            pokemon.PersonalId,
            pokemon.SpeciesId,
            pokemon.Form,
            pokemon.Name,
            pokemon.FormLabel,
            pokemon.Type1,
            pokemon.Type2,
            new PokemonBaseStatsDto(
                pokemon.BaseStats.HP,
                pokemon.BaseStats.Attack,
                pokemon.BaseStats.Defense,
                pokemon.BaseStats.SpecialAttack,
                pokemon.BaseStats.SpecialDefense,
                pokemon.BaseStats.Speed,
                pokemon.BaseStats.Total),
            new PokemonAbilitySetDto(
                pokemon.Abilities.Ability1,
                pokemon.Abilities.Ability1Label,
                pokemon.Abilities.Ability2,
                pokemon.Abilities.Ability2Label,
                pokemon.Abilities.HiddenAbility,
                pokemon.Abilities.HiddenAbilityLabel),
            new PokemonDexPresenceDto(
                pokemon.DexPresence.IsPresentInGame,
                pokemon.DexPresence.IsInAnyDex,
                pokemon.DexPresence.RegionalDexIndex,
                pokemon.DexPresence.ArmorDexIndex,
                pokemon.DexPresence.CrownDexIndex),
            new PokemonPersonalDetailsDto(
                pokemon.Personal.Type1,
                pokemon.Personal.Type2,
                pokemon.Personal.CatchRate,
                pokemon.Personal.EvolutionStage,
                pokemon.Personal.EVYieldHP,
                pokemon.Personal.EVYieldAttack,
                pokemon.Personal.EVYieldDefense,
                pokemon.Personal.EVYieldSpecialAttack,
                pokemon.Personal.EVYieldSpecialDefense,
                pokemon.Personal.EVYieldSpeed,
                pokemon.Personal.HeldItem1,
                pokemon.Personal.HeldItem2,
                pokemon.Personal.HeldItem3,
                pokemon.Personal.GenderRatio,
                pokemon.Personal.HatchCycles,
                pokemon.Personal.BaseFriendship,
                pokemon.Personal.ExpGrowth,
                pokemon.Personal.EggGroup1,
                pokemon.Personal.EggGroup2,
                pokemon.Personal.FormStatsIndex,
                pokemon.Personal.FormCount,
                pokemon.Personal.Color,
                pokemon.Personal.IsPresentInGame,
                pokemon.Personal.HasSpriteForm,
                pokemon.Personal.ModelId,
                pokemon.Personal.HatchedSpecies,
                pokemon.Personal.LocalFormIndex,
                pokemon.Personal.IsRegionalForm,
                null,
                pokemon.Personal.Form),
            pokemon.CatchRate,
            pokemon.EvolutionStage,
            pokemon.GenderRatio,
            pokemon.GenderRatioLabel,
            pokemon.BaseExperience,
            pokemon.Height,
            pokemon.Weight,
            pokemon.Evolutions.Select(ToDto).ToArray(),
            pokemon.Learnset.Select(ToDto).ToArray(),
            pokemon.Compatibility.Select(ToDto).ToArray(),
            new PokemonProvenanceDto(
                pokemon.Provenance.SourceFile,
                ProjectBridgeMapper.ToDto(pokemon.Provenance.SourceLayer),
                ProjectBridgeMapper.ToDto(pokemon.Provenance.FileState)),
            pokemon.SpriteName);
    }

    private static ItemRecordDto ToDto(ZaItemRecord item)
    {
        return new ItemRecordDto(
            item.ItemId,
            item.Name,
            item.Category,
            item.BuyPrice,
            item.SellPrice,
            item.WattsPrice,
            item.AlternatePrice,
            item.FieldValues,
            ToDto(item.Metadata),
            item.SharedItemIds.ToArray(),
            item.DetailGroups.Select(ToDto).ToArray(),
            ToDto(item.Provenance));
    }

    private static ItemMetadataDto ToDto(ZaItemMetadata metadata)
    {
        return new ItemMetadataDto(
            metadata.Pouch,
            metadata.PouchFlags,
            metadata.FlingPower,
            metadata.FieldUseType,
            metadata.FieldFlags,
            metadata.CanUseOnPokemon,
            metadata.ItemType,
            metadata.SortIndex,
            metadata.ItemSprite,
            metadata.GroupType,
            metadata.GroupIndex,
            metadata.CureStatusFlags,
            metadata.Boost0,
            metadata.Boost1,
            metadata.Boost2,
            metadata.Boost3,
            metadata.UseFlags1,
            metadata.UseFlags2,
            metadata.EvHp,
            metadata.EvAttack,
            metadata.EvDefense,
            metadata.EvSpeed,
            metadata.EvSpecialAttack,
            metadata.EvSpecialDefense,
            metadata.HealAmount,
            metadata.PpGain,
            metadata.FriendshipGain1,
            metadata.FriendshipGain2,
            metadata.FriendshipGain3,
            metadata.MachineSlot,
            metadata.MachineMoveId,
            metadata.MachineMoveName);
    }

    private static ItemDetailGroupDto ToDto(ZaItemDetailGroup group)
    {
        return new ItemDetailGroupDto(
            group.Label,
            group.Details.Select(ToDto).ToArray());
    }

    private static ItemDetailDto ToDto(ZaItemDetail detail)
    {
        return new ItemDetailDto(detail.Label, detail.Value);
    }

    private static ItemProvenanceDto ToDto(ZaItemProvenance provenance)
    {
        return new ItemProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static ItemEditableFieldDto ToDto(ZaItemEditableField field)
    {
        return new ItemEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static ItemEditableFieldOptionDto ToDto(ZaItemEditableFieldOption option)
    {
        return new ItemEditableFieldOptionDto(option.Value, option.Label);
    }

    private static PokemonEditableFieldDto ToDto(ZaPokemonEditableField field)
    {
        return new PokemonEditableFieldDto(
            field.Field,
            field.Label,
            field.Group,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static PokemonEditableFieldOptionDto ToDto(ZaPokemonEditableFieldOption option)
    {
        return new PokemonEditableFieldOptionDto(option.Value, option.Label);
    }

    private static PokemonEvolutionRecordDto ToDto(ZaPokemonEvolutionRecord evolution)
    {
        return new PokemonEvolutionRecordDto(
            evolution.Slot,
            evolution.Method,
            evolution.Argument,
            evolution.Species,
            evolution.Form,
            evolution.Level,
            evolution.MethodName,
            evolution.ArgumentKind,
            evolution.ArgumentLabel,
            evolution.ArgumentValue);
    }

    private static PokemonEvolutionMethodOptionDto ToDto(ZaPokemonEvolutionMethodOption option)
    {
        return new PokemonEvolutionMethodOptionDto(
            option.Value,
            option.Label,
            option.ArgumentKind,
            option.ArgumentLabel,
            option.ArgumentOptions.Select(ToDto).ToArray());
    }

    private static PokemonLearnsetMoveDto ToDto(ZaPokemonLearnsetMove learnsetMove)
    {
        return new PokemonLearnsetMoveDto(
            learnsetMove.Slot,
            learnsetMove.MoveId,
            learnsetMove.MoveName,
            learnsetMove.Level,
            learnsetMove.RawLevel == learnsetMove.Level ? null : learnsetMove.RawLevel,
            learnsetMove.LevelLabel);
    }

    private static PokemonCompatibilityGroupDto ToDto(ZaPokemonCompatibilityGroup group)
    {
        return new PokemonCompatibilityGroupDto(
            group.GroupId,
            group.Label,
            group.EnabledCount,
            group.Entries.Select(ToDto).ToArray());
    }

    private static PokemonCompatibilityEntryDto ToDto(ZaPokemonCompatibilityEntry entry)
    {
        return new PokemonCompatibilityEntryDto(
            entry.Slot,
            entry.MoveId,
            entry.MoveName,
            entry.Label,
            entry.CanLearn);
    }

    private static MoveRecordDto ToDto(ZaMoveRecord move)
    {
        return new MoveRecordDto(
            move.MoveId,
            move.Name,
            move.Description,
            move.Version,
            move.CanUseMove,
            move.Type,
            move.TypeName,
            move.Quality,
            move.Category,
            move.CategoryName,
            move.Power,
            move.Accuracy,
            move.PP,
            move.Priority,
            move.CritStage,
            move.MaxMovePower,
            move.Target,
            move.TargetName,
            move.HitMin,
            move.HitMax,
            move.TurnMin,
            move.TurnMax,
            move.Inflict,
            move.InflictName,
            move.InflictPercent,
            move.RawInflictCount,
            move.Flinch,
            move.EffectSequence,
            move.Recoil,
            move.RawHealing,
            move.StatChanges.Select(ToDto).ToArray(),
            move.Flags.Select(ToDto).ToArray(),
            ToDto(move.Provenance));
    }

    private static MoveEditableFieldDto ToDto(ZaMoveEditableField field)
    {
        return new MoveEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static MoveEditableFieldOptionDto ToDto(ZaMoveEditableFieldOption option)
    {
        return new MoveEditableFieldOptionDto(option.Value, option.Label);
    }

    private static MoveStatChangeRecordDto ToDto(ZaMoveStatChangeRecord statChange)
    {
        return new MoveStatChangeRecordDto(
            statChange.Slot,
            statChange.Stat,
            statChange.StatName,
            statChange.Stage,
            statChange.Percent);
    }

    private static MoveFlagRecordDto ToDto(ZaMoveFlagRecord flag)
    {
        return new MoveFlagRecordDto(
            flag.Field,
            flag.Label,
            flag.Enabled);
    }

    private static MoveProvenanceDto ToDto(ZaMoveProvenance provenance)
    {
        return new MoveProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static TextEntryRecordDto ToDto(ZaTextEntryRecord entry)
    {
        return new TextEntryRecordDto(
            entry.TextId,
            entry.TextKey,
            entry.Label,
            entry.Language,
            entry.SourceFile,
            entry.LineIndex,
            entry.Value,
            entry.CanEdit,
            entry.EditBlockedReason,
            ToDto(entry.Provenance));
    }

    private static TextEditableFieldDto ToDto(ZaTextEditableField field)
    {
        return new TextEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumLength,
            field.MaximumLength);
    }

    private static DialogueReferenceRecordDto ToDto(ZaDialogueReferenceRecord reference)
    {
        return new DialogueReferenceRecordDto(
            reference.DialogueId,
            reference.Label,
            reference.TextId,
            reference.Context,
            reference.Preview,
            ToDto(reference.Provenance));
    }

    private static TextProvenanceDto ToDto(ZaTextProvenance provenance)
    {
        return new TextProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static ShopRecordDto ToDto(ZaShopRecord shop)
    {
        return new ShopRecordDto(
            shop.ShopId,
            shop.Name,
            shop.Kind,
            shop.InventoryLabel,
            shop.InventoryIndex,
            shop.InventoryCount,
            shop.SourceHash,
            shop.InventorySummary,
            shop.Location,
            shop.Currency,
            shop.Inventory.Select(ToDto).ToArray(),
            ToDto(shop.Provenance))
        {
            EditorFamily = "za",
            CanEditInventoryOrder = shop.CanEditInventoryOrder,
        };
    }

    private static ShopInventoryRecordDto ToDto(ZaShopInventoryRecord inventoryItem)
    {
        return new ShopInventoryRecordDto(
            inventoryItem.Slot,
            inventoryItem.ItemId,
            inventoryItem.ItemName,
            inventoryItem.Price,
            inventoryItem.IsKnownItem,
            inventoryItem.StockLimit)
        {
            FieldValues = inventoryItem.FieldValues,
            FieldDisplayValues = inventoryItem.FieldDisplayValues,
            SupportedFields = inventoryItem.SupportedFields,
            PriceField = inventoryItem.PriceField,
            CanEditPrice = inventoryItem.CanEditPrice,
        };
    }

    private static ShopProvenanceDto ToDto(ZaShopProvenance provenance)
    {
        return new ShopProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static ShopEditableFieldDto ToDto(ZaShopEditableField field)
    {
        return new ShopEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static ShopEditableFieldOptionDto ToDto(ZaShopEditableFieldOption option)
    {
        return new ShopEditableFieldOptionDto(
            option.Value,
            option.Label,
            option.ItemName,
            option.Price);
    }

    private static TrainerRecordDto ToDto(ZaTrainerRecord trainer)
    {
        return new TrainerRecordDto(
            trainer.TrainerId,
            trainer.Name,
            trainer.TrainerClassId,
            trainer.TrainerClass,
            trainer.Location,
            trainer.BattleTypeValue,
            trainer.BattleType,
            trainer.ItemIds,
            trainer.Items,
            trainer.AiFlags,
            trainer.AiFlagStates.Select(ToDto).ToArray(),
            null,
            null,
            trainer.Heal,
            trainer.Money,
            trainer.Gift,
            trainer.ClassBallId,
            trainer.ClassBall,
            trainer.CanEditClassBall,
            trainer.ClassBallScope,
            trainer.Team.Select(ToDto).ToArray(),
            ToDto(trainer.Provenance))
        {
            ZaRank = trainer.Rank,
            ZaMegaEvolution = trainer.MegaEvolution,
            ZaLastHand = trainer.LastHand,
        };
    }

    private static GiftPokemonRecordDto ToDto(ZaGiftPokemonEntry gift)
    {
        var firstMove = gift.Moves.FirstOrDefault(move => move.MoveId != 0)
            ?? gift.Moves.FirstOrDefault();

        return new GiftPokemonRecordDto(
            gift.GiftIndex,
            gift.Label,
            gift.SpeciesId,
            gift.Species,
            gift.Form,
            gift.Level,
            gift.IsEgg,
            gift.HeldItemId,
            gift.HeldItem,
            0,
            "None",
            gift.Ability,
            gift.AbilityLabel,
            gift.Nature,
            gift.NatureLabel,
            gift.Gender,
            gift.GenderLabel,
            gift.ShinyLock,
            gift.ShinyLockLabel,
            null,
            null,
            firstMove?.MoveId ?? 0,
            firstMove?.Move,
            new GiftPokemonIvsDto(
                gift.Ivs.HP,
                gift.Ivs.Attack,
                gift.Ivs.Defense,
                gift.Ivs.SpecialAttack,
                gift.Ivs.SpecialDefense,
                gift.Ivs.Speed),
            gift.FlawlessIvCount,
            gift.IvSummary,
            new GiftPokemonProvenanceDto(
                gift.Provenance.SourceFile,
                ProjectBridgeMapper.ToDto(gift.Provenance.SourceLayer),
                ProjectBridgeMapper.ToDto(gift.Provenance.FileState)))
        {
            EditorFamily = "za",
            AbilityOptions = gift.AbilityOptions.Select(ToDto).ToArray(),
            EventLabel = gift.EventLabel,
            Moves = gift.Moves.Select(ToDto).ToArray(),
        };
    }

    private static GiftPokemonMoveDto ToDto(ZaGiftPokemonMoveRecord move)
    {
        return new GiftPokemonMoveDto(move.Slot, move.MoveId, move.Move, move.PointUps);
    }

    private static GiftPokemonEditableFieldDto ToDto(ZaGiftPokemonEditableField field)
    {
        return new GiftPokemonEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static GiftPokemonEditableFieldOptionDto ToDto(ZaGiftPokemonEditableFieldOption option)
    {
        return new GiftPokemonEditableFieldOptionDto(option.Value, option.Label);
    }

    private static TradePokemonRecordDto ToDto(ZaTradePokemonEntry trade)
    {
        var moves = trade.Moves.Select(ToDto).ToArray();

        return new TradePokemonRecordDto(
            trade.TradeIndex,
            trade.Label,
            trade.SpeciesId,
            trade.Species,
            trade.Form,
            trade.Level,
            trade.HeldItemId,
            trade.HeldItem,
            0,
            "None",
            trade.Ability,
            trade.AbilityLabel,
            trade.Nature,
            trade.NatureLabel,
            trade.Gender,
            trade.GenderLabel,
            trade.ShinyLock,
            trade.ShinyLockLabel,
            null,
            null,
            0,
            "Handled by trade event",
            0,
            0,
            "Default",
            0,
            0,
            0,
            "Default",
            0,
            0,
            0,
            0,
            0,
            "0x0000000000000000",
            "0x0000000000000000",
            "0x0000000000000000",
            moves,
            new TradePokemonIvsDto(
                trade.Ivs.HP,
                trade.Ivs.Attack,
                trade.Ivs.Defense,
                trade.Ivs.SpecialAttack,
                trade.Ivs.SpecialDefense,
                trade.Ivs.Speed),
            trade.FlawlessIvCount,
            trade.IvSummary,
            new TradePokemonProvenanceDto(
                trade.Provenance.SourceFile,
                ProjectBridgeMapper.ToDto(trade.Provenance.SourceLayer),
                ProjectBridgeMapper.ToDto(trade.Provenance.FileState)))
        {
            EditorFamily = "za",
            EventLabel = trade.EventLabel,
            Moves = moves,
            AbilityOptions = trade.AbilityOptions.Select(ToDto).ToArray(),
        };
    }

    private static TradePokemonMoveRecordDto ToDto(ZaTradePokemonMoveRecord move)
    {
        return new TradePokemonMoveRecordDto(move.Slot, move.MoveId, move.Move);
    }

    private static TradePokemonEditableFieldDto ToDto(ZaTradePokemonEditableField field)
    {
        return new TradePokemonEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static TradePokemonEditableFieldOptionDto ToDto(ZaTradePokemonEditableFieldOption option)
    {
        return new TradePokemonEditableFieldOptionDto(option.Value, option.Label);
    }

    private static ZaModMergerSourceRecordDto ToDto(ZaModMergerSourceRecord source)
    {
        return new ZaModMergerSourceRecordDto(
            source.SourceIndex,
            source.Path,
            source.Name,
            source.Kind,
            source.IsEnabled,
            source.Status,
            source.FileCount,
            source.OverrideCount,
            source.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ZaModMergerPreviewDto ToDto(ZaModMergerPreview preview)
    {
        return new ZaModMergerPreviewDto(
            preview.CanApply,
            preview.Status,
            preview.SelectedFileCount,
            preview.ReadyFileCount,
            preview.ConflictFileCount,
            preview.UnresolvedConflictCount,
            preview.Files.Select(ToDto).ToArray(),
            preview.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ZaModMergerFilePreviewRecordDto ToDto(ZaModMergerFilePreviewRecord file)
    {
        return new ZaModMergerFilePreviewRecordDto(
            file.RelativePath,
            file.OutputRelativePath,
            file.SupportKind,
            file.Status,
            file.MergeKind,
            file.Summary,
            file.SourceIndex,
            file.SourceName,
            file.OverrideCount);
    }

    private static TrainerPokemonRecordDto ToDto(ZaTrainerPokemonRecord pokemon)
    {
        return new TrainerPokemonRecordDto(
            pokemon.Slot,
            pokemon.SpeciesId,
            pokemon.Species,
            pokemon.Form,
            pokemon.Level,
            pokemon.HeldItemId,
            pokemon.HeldItem,
            pokemon.MoveIds,
            pokemon.Moves,
            pokemon.Gender,
            pokemon.GenderLabel,
            pokemon.Ability,
            pokemon.AbilityLabel,
            pokemon.Nature,
            pokemon.NatureLabel,
            ToDto(pokemon.Evs),
            null,
            null,
            ToDto(pokemon.Ivs),
            pokemon.Shiny,
            null,
            null,
            null)
        {
            AbilityOptions = pokemon.AbilityOptions.Select(ToDto).ToArray(),
            BaseStats = pokemon.BaseStats is null ? null : ToDto(pokemon.BaseStats),
            SpriteName = pokemon.SpriteName,
        };
    }

    private static PlacedObjectRecordDto ToDto(ZaPlacedObjectRecord placedObject)
    {
        return new PlacedObjectRecordDto(
            placedObject.ObjectId,
            placedObject.ObjectType,
            placedObject.Label,
            placedObject.Map,
            placedObject.ArchiveMember,
            placedObject.ZoneIndex,
            placedObject.ObjectIndex,
            placedObject.ChanceIndex,
            placedObject.ItemId,
            placedObject.ItemName,
            placedObject.ItemHash,
            placedObject.Quantity,
            placedObject.Chance,
            placedObject.X,
            placedObject.Y,
            placedObject.Z,
            placedObject.RotationY,
            placedObject.ScriptId,
            ToDto(placedObject.Provenance),
            placedObject.CategoryId,
            placedObject.CategoryLabel,
            placedObject.Fields.Select(ToDto).ToArray());
    }

    private static PlacementFieldValueDto ToDto(ZaPlacementFieldValue field)
    {
        return new PlacementFieldValueDto(
            field.Field,
            field.Label,
            field.Group,
            field.Value,
            field.DisplayValue,
            field.IsReadOnly,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Description,
            field.Options?.Select(ToDto).ToArray());
    }

    private static PlacementEditableFieldDto ToDto(ZaPlacementEditableField field)
    {
        return new PlacementEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray(),
            field.Group,
            field.IsReadOnly,
            field.Description);
    }

    private static PlacementEditableFieldOptionDto ToDto(ZaPlacementEditableFieldOption option)
    {
        return new PlacementEditableFieldOptionDto(option.Value, option.Label);
    }

    private static PlacementCategoryDto ToDto(ZaPlacementCategory category)
    {
        return new PlacementCategoryDto(
            category.Id,
            category.Label,
            category.Description,
            category.ObjectCount);
    }

    private static PlacementProvenanceDto ToDto(ZaPlacementProvenance provenance)
    {
        return new PlacementProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static TrainerAiFlagStateDto ToDto(ZaTrainerAiFlagState flag)
    {
        return new TrainerAiFlagStateDto(
            flag.Bit,
            flag.Mask,
            flag.Label,
            flag.Description,
            flag.Enabled);
    }

    private static TrainerPokemonStatsDto ToDto(ZaTrainerPokemonStatsRecord stats)
    {
        return new TrainerPokemonStatsDto(
            stats.HP,
            stats.Attack,
            stats.Defense,
            stats.SpecialAttack,
            stats.SpecialDefense,
            stats.Speed);
    }

    private static TrainerEditableFieldDto ToDto(ZaTrainerEditableField field)
    {
        return new TrainerEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static TrainerEditableFieldOptionDto ToDto(ZaTrainerEditableFieldOption option)
    {
        return new TrainerEditableFieldOptionDto(option.Value, option.Label);
    }

    private static TrainerProvenanceDto ToDto(ZaTrainerProvenance provenance)
    {
        return new TrainerProvenanceDto(
            provenance.SourceFile,
            provenance.TeamSourceFile,
            provenance.ClassSourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.TeamSourceLayer),
            provenance.ClassSourceLayer is null ? null : ProjectBridgeMapper.ToDto(provenance.ClassSourceLayer.Value),
            ProjectBridgeMapper.ToDto(provenance.FileState),
            ProjectBridgeMapper.ToDto(provenance.TeamFileState),
            provenance.ClassFileState is null ? null : ProjectBridgeMapper.ToDto(provenance.ClassFileState.Value));
    }

    private static ZaCacheModeDto ToDto(ZaCacheMode mode)
    {
        return mode switch
        {
            ZaCacheMode.Minimal => ZaCacheModeDto.Minimal,
            ZaCacheMode.Balanced => ZaCacheModeDto.Balanced,
            ZaCacheMode.Performance => ZaCacheModeDto.Performance,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    private static WorkflowAvailabilityDto ToDto(ZaWorkflowAvailability availability)
    {
        return availability switch
        {
            ZaWorkflowAvailability.Disabled => WorkflowAvailabilityDto.Disabled,
            ZaWorkflowAvailability.ReadOnly => WorkflowAvailabilityDto.ReadOnly,
            ZaWorkflowAvailability.Available => WorkflowAvailabilityDto.Available,
            _ => throw new ArgumentOutOfRangeException(nameof(availability), availability, null),
        };
    }
}
