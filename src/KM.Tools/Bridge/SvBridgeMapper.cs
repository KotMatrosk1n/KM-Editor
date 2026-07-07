// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Editing;
using KM.Api.Encounters;
using KM.Api.FashionUnlock;
using KM.Api.Gifts;
using KM.Api.HyperspaceBypass;
using KM.Api.Items;
using KM.Api.ModMerger;
using KM.Api.Moves;
using KM.Api.Placement;
using KM.Api.Pokemon;
using KM.Api.Projects;
using KM.Api.Raids;
using KM.Api.Shops;
using KM.Api.SpreadsheetImport;
using KM.Api.StaticEncounters;
using KM.Api.SvCache;
using KM.Api.Text;
using KM.Api.Trainers;
using KM.Api.Trades;
using KM.Api.TypeChart;
using KM.Api.Workflows;
using KM.Core.Projects;
using KM.SV.DumpImport;
using KM.SV.Encounters;
using KM.SV.FashionUnlock;
using KM.SV.Gifts;
using KM.SV.HyperspaceBypass;
using KM.SV.Items;
using KM.SV.ModMerger;
using KM.SV.Moves;
using KM.SV.Placement;
using KM.SV.Pokemon;
using KM.SV.Raids;
using KM.SV.Shops;
using KM.SV.StaticEncounters;
using KM.SV.Text;
using KM.SV.Trainers;
using KM.SV.Trades;
using KM.SV.TypeChart;
using KM.SV.Workflows;

namespace KM.Tools.Bridge;

public static class SvBridgeMapper
{
    public static SvCacheMode ToCore(SvCacheModeDto mode)
    {
        return mode switch
        {
            SvCacheModeDto.Minimal => SvCacheMode.Minimal,
            SvCacheModeDto.Balanced => SvCacheMode.Balanced,
            SvCacheModeDto.Performance => SvCacheMode.Performance,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    public static SvCacheStatusResponse ToDto(SvCacheStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new SvCacheStatusResponse(new SvCacheStatusDto(
            new SvCacheSettingsDto(ToDto(status.Settings.Mode), status.Settings.MaxCacheSizeBytes),
            status.CacheSizeBytes,
            status.WarmupCompleted,
            status.WarmupTotal,
            status.ProgressPercent,
            status.Phase,
            status.Message,
            status.IsActiveProjectPreserved));
    }

    private static SvCacheModeDto ToDto(SvCacheMode mode)
    {
        return mode switch
        {
            SvCacheMode.Minimal => SvCacheModeDto.Minimal,
            SvCacheMode.Balanced => SvCacheModeDto.Balanced,
            SvCacheMode.Performance => SvCacheModeDto.Performance,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    public static SvOutputMode ToCore(ChangePlanOutputModeDto? outputMode)
    {
        return outputMode switch
        {
            null => SvOutputMode.Standalone,
            ChangePlanOutputModeDto.Standalone => SvOutputMode.Standalone,
            ChangePlanOutputModeDto.TrinityModManager => SvOutputMode.TrinityModManager,
            ChangePlanOutputModeDto.TrinityBypass => SvOutputMode.TrinityBypass,
            _ => throw new ArgumentOutOfRangeException(nameof(outputMode), outputMode, null),
        };
    }

    public static ListWorkflowsResponse ToDto(SvWorkflowList workflowList)
    {
        ArgumentNullException.ThrowIfNull(workflowList);

        return new ListWorkflowsResponse(workflowList.Workflows.Select(ToDto).ToArray());
    }

    public static LoadItemsWorkflowResponse ToDto(SvItemsWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadItemsWorkflowResponse(ToItemsWorkflowDto(workflow));
    }

    public static UpdateItemFieldResponse ToDto(SvItemsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateItemFieldResponse(
            ToItemsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateItemFieldsResponse ToItemFieldsDto(SvItemsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateItemFieldsResponse(
            ToItemsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadMovesWorkflowResponse ToDto(SvMovesWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadMovesWorkflowResponse(ToMovesWorkflowDto(workflow));
    }

    public static LoadTextWorkflowResponse ToDto(SvTextWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadTextWorkflowResponse(ToTextWorkflowDto(workflow));
    }

    public static UpdateMoveFieldResponse ToDto(SvMovesEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateMoveFieldResponse(
            ToMovesWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateMoveFieldsResponse ToMoveFieldsDto(SvMovesEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateMoveFieldsResponse(
            ToMovesWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateTextEntryResponse ToDto(SvTextEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateTextEntryResponse(
            ToTextWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadPokemonWorkflowResponse ToDto(SvPokemonWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadPokemonWorkflowResponse(ToPokemonWorkflowDto(workflow));
    }

    public static UpdatePokemonFieldResponse ToDto(SvPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePokemonFieldResponse(
            ToPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdatePokemonFieldsResponse ToPokemonFieldsDto(SvPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePokemonFieldsResponse(
            ToPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdatePokemonLearnsetResponse ToDtoLearnsetUpdate(SvPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePokemonLearnsetResponse(
            ToPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdatePokemonEvolutionResponse ToDtoEvolutionUpdate(SvPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePokemonEvolutionResponse(
            ToPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadTrainersWorkflowResponse ToDto(SvTrainersWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadTrainersWorkflowResponse(ToTrainersWorkflowDto(workflow));
    }

    public static UpdateTrainerFieldResponse ToDto(SvTrainersEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateTrainerFieldResponse(
            ToTrainersWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateTrainerFieldsResponse ToTrainerFieldsDto(SvTrainersEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateTrainerFieldsResponse(
            ToTrainersWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadEncountersWorkflowResponse ToDto(SvEncountersWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadEncountersWorkflowResponse(ToEncountersWorkflowDto(workflow));
    }

    public static UpdateEncounterSlotFieldResponse ToDto(SvEncountersEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateEncounterSlotFieldResponse(
            ToEncountersWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateEncounterSlotFieldsResponse ToEncounterSlotFieldsDto(SvEncountersEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateEncounterSlotFieldsResponse(
            ToEncountersWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadTeraRaidsWorkflowResponse ToDto(SvTeraRaidsWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadTeraRaidsWorkflowResponse(ToTeraRaidsWorkflowDto(workflow));
    }

    public static UpdateTeraRaidFieldResponse ToDto(SvTeraRaidsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateTeraRaidFieldResponse(
            ToTeraRaidsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateTeraRaidFieldsResponse ToTeraRaidFieldsDto(SvTeraRaidsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateTeraRaidFieldsResponse(
            ToTeraRaidsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadStaticEncountersWorkflowResponse ToDto(SvStaticEncountersWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadStaticEncountersWorkflowResponse(ToStaticEncountersWorkflowDto(workflow));
    }

    public static LoadShopsWorkflowResponse ToDto(SvShopsWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadShopsWorkflowResponse(ToShopsWorkflowDto(workflow));
    }

    public static UpdateShopInventoryItemResponse ToDto(SvShopsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateShopInventoryItemResponse(
            ToShopsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateStaticEncounterFieldResponse ToDto(SvStaticEncountersEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateStaticEncounterFieldResponse(
            ToStaticEncountersWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadGiftPokemonWorkflowResponse ToDto(SvGiftPokemonWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadGiftPokemonWorkflowResponse(ToGiftPokemonWorkflowDto(workflow));
    }

    public static UpdateGiftPokemonFieldResponse ToDto(SvGiftPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateGiftPokemonFieldResponse(
            ToGiftPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateGiftPokemonFieldsResponse ToGiftPokemonFieldsDto(SvGiftPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateGiftPokemonFieldsResponse(
            ToGiftPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadTradePokemonWorkflowResponse ToDto(SvTradePokemonWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadTradePokemonWorkflowResponse(ToTradePokemonWorkflowDto(workflow));
    }

    public static UpdateTradePokemonFieldResponse ToDto(SvTradePokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateTradePokemonFieldResponse(
            ToTradePokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateTradePokemonFieldsResponse ToTradePokemonFieldsDto(SvTradePokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateTradePokemonFieldsResponse(
            ToTradePokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadPlacementWorkflowResponse ToDto(SvPlacementWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadPlacementWorkflowResponse(ToPlacementWorkflowDto(workflow));
    }

    public static LoadHyperspaceBypassWorkflowResponse ToDto(SvHyperspaceBypassWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadHyperspaceBypassWorkflowResponse(ToHyperspaceBypassWorkflowDto(workflow));
    }

    public static LoadFashionUnlockWorkflowResponse ToDto(SvFashionUnlockWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadFashionUnlockWorkflowResponse(ToFashionUnlockWorkflowDto(workflow));
    }

    public static LoadTypeChartWorkflowResponse ToDto(SvTypeChartWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadTypeChartWorkflowResponse(ToTypeChartWorkflowDto(workflow));
    }

    public static UpdatePlacementObjectFieldResponse ToDto(SvPlacementEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePlacementObjectFieldResponse(
            ToPlacementWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdatePlacementObjectFieldsResponse ToPlacementObjectFieldsDto(SvPlacementEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePlacementObjectFieldsResponse(
            ToPlacementWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static StageHyperspaceBypassInstallResponse ToHyperspaceBypassInstallDto(
        SvHyperspaceBypassEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageHyperspaceBypassInstallResponse(
            ToHyperspaceBypassWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static StageFashionUnlockInstallResponse ToFashionUnlockInstallDto(
        SvFashionUnlockEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageFashionUnlockInstallResponse(
            ToFashionUnlockWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static StageHyperspaceBypassUninstallResponse ToHyperspaceBypassUninstallDto(
        SvHyperspaceBypassEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageHyperspaceBypassUninstallResponse(
            ToHyperspaceBypassWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static StageFashionUnlockUninstallResponse ToFashionUnlockUninstallDto(
        SvFashionUnlockEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageFashionUnlockUninstallResponse(
            ToFashionUnlockWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static StageTypeChartResponse ToDto(SvTypeChartEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageTypeChartResponse(
            ToTypeChartWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static StageTypeChartUninstallResponse ToTypeChartUninstallDto(SvTypeChartEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageTypeChartUninstallResponse(
            ToTypeChartWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadSpreadsheetImportWorkflowResponse ToDto(SvDumpImportWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadSpreadsheetImportWorkflowResponse(ToSpreadsheetImportWorkflowDto(workflow));
    }

    public static PreviewSpreadsheetImportResponse ToDto(SvDumpImportExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new PreviewSpreadsheetImportResponse(
            ToSpreadsheetImportWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            ToDto(result.Preview),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static ValidateEditSessionResponse ToDto(SvEditSessionValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);

        return new ValidateEditSessionResponse(
            EditSessionBridgeMapper.ToDto(validation.Session),
            validation.IsValid,
            validation.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadSvModMergerWorkflowResponse ToDto(SvModMergerWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadSvModMergerWorkflowResponse(ToWorkflowDto(workflow));
    }

    public static StageSvModMergeResponse ToDto(SvModMergerStageResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageSvModMergeResponse(
            ToWorkflowDto(result.Workflow),
            ToDto(result.Preview),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static ApplySvModMergeResponse ToDto(SvModMergerApplyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ApplySvModMergeResponse(
            ToWorkflowDto(result.Workflow),
            ToDto(result.Preview),
            result.WrittenFiles,
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ItemsWorkflowDto ToItemsWorkflowDto(SvItemsWorkflow workflow)
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

    private static MovesWorkflowDto ToMovesWorkflowDto(SvMovesWorkflow workflow)
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

    private static TextWorkflowDto ToTextWorkflowDto(SvTextWorkflow workflow)
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

    private static PokemonWorkflowDto ToPokemonWorkflowDto(SvPokemonWorkflow workflow)
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

    private static TrainersWorkflowDto ToTrainersWorkflowDto(SvTrainersWorkflow workflow)
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

    private static EncountersWorkflowDto ToEncountersWorkflowDto(SvEncountersWorkflow workflow)
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

    private static TeraRaidsWorkflowDto ToTeraRaidsWorkflowDto(SvTeraRaidsWorkflow workflow)
    {
        return new TeraRaidsWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Raids.Select(ToDto).ToArray(),
            workflow.FixedRewardTables.Select(ToDto).ToArray(),
            workflow.LotteryRewardTables.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new TeraRaidsWorkflowStatsDto(
                workflow.Stats.TotalRaidCount,
                workflow.Stats.TotalRewardTableCount,
                workflow.Stats.TotalRewardItemCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ShopsWorkflowDto ToShopsWorkflowDto(SvShopsWorkflow workflow)
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
            EditorFamily = "sv",
        };
    }

    private static GiftPokemonWorkflowDto ToGiftPokemonWorkflowDto(SvGiftPokemonWorkflow workflow)
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
            EditorFamily = "sv",
        };
    }

    private static TradePokemonWorkflowDto ToTradePokemonWorkflowDto(SvTradePokemonWorkflow workflow)
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
            EditorFamily = "sv",
        };
    }

    private static PlacementWorkflowDto ToPlacementWorkflowDto(SvPlacementWorkflow workflow)
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

    private static HyperspaceBypassWorkflowDto ToHyperspaceBypassWorkflowDto(
        SvHyperspaceBypassWorkflow workflow)
    {
        return new HyperspaceBypassWorkflowDto(
            ToDto(workflow.Summary),
            workflow.InstallStatus,
            workflow.InstallMessage,
            workflow.BuildId,
            workflow.PatchOffsetHex,
            workflow.StubKind,
            ToProjectGameDto(workflow.DetectedGame),
            workflow.ReservedRegions.Select(ToDto).ToArray(),
            ToDto(workflow.Provenance),
            new HyperspaceBypassWorkflowStatsDto(
                workflow.Stats.ReservedMainTextRegionCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static FashionUnlockWorkflowDto ToFashionUnlockWorkflowDto(
        SvFashionUnlockWorkflow workflow)
    {
        return new FashionUnlockWorkflowDto(
            ToDto(workflow.Summary),
            workflow.InstallStatus,
            workflow.InstallMessage,
            "sv",
            workflow.BuildId,
            string.Empty,
            string.Empty,
            workflow.OwnershipCheckOffsetHex,
            workflow.StubKind,
            ToProjectGameDto(workflow.DetectedGame),
            workflow.ReservedRegions.Select(ToDto).ToArray(),
            ToDto(workflow.Provenance),
            new FashionUnlockWorkflowStatsDto(
                workflow.Stats.ReservedMainTextRegionCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static TypeChartWorkflowDto ToTypeChartWorkflowDto(SvTypeChartWorkflow workflow)
    {
        return new TypeChartWorkflowDto(
            ToDto(workflow.Summary),
            workflow.InstallStatus,
            workflow.InstallMessage,
            workflow.BuildId,
            workflow.ChartOffsetHex,
            ToProjectGameDto(workflow.DetectedGame),
            workflow.Source is null ? null : ToDto(workflow.Source),
            workflow.Types.Select(ToDto).ToArray(),
            workflow.Cells.Select(ToDto).ToArray(),
            new TypeChartWorkflowStatsDto(
                workflow.Stats.SourceFileCount,
                workflow.Stats.OutputFileCount,
                workflow.Stats.ChartCellCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static SpreadsheetImportWorkflowDto ToSpreadsheetImportWorkflowDto(
        SvDumpImportWorkflow workflow)
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

    private static SvModMergerWorkflowDto ToWorkflowDto(SvModMergerWorkflow workflow)
    {
        return new SvModMergerWorkflowDto(
            ToDto(workflow.Summary),
            workflow.OutputRootPath,
            workflow.Sources.Select(ToDto).ToArray(),
            new SvModMergerWorkflowStatsDto(
                workflow.Stats.SourceCount,
                workflow.Stats.EnabledSourceCount,
                workflow.Stats.SourceFileCount,
                workflow.Stats.OutputFileCount,
                workflow.Stats.OverrideCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static SpreadsheetImportProfileRecordDto ToDto(SvDumpImportProfileRecord profile)
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

    private static SpreadsheetImportColumnRecordDto ToDto(SvDumpImportColumnRecord column)
    {
        return new SpreadsheetImportColumnRecordDto(
            column.Column,
            column.Header,
            column.ValueKind,
            column.IsRequired,
            column.Description);
    }

    private static SpreadsheetImportProvenanceDto ToDto(SvDumpImportProvenance provenance)
    {
        return new SpreadsheetImportProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static SpreadsheetImportPreviewDto ToDto(SvDumpImportPreview preview)
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

    private static SpreadsheetImportRowPreviewRecordDto ToDto(SvDumpImportRowPreviewRecord row)
    {
        return new SpreadsheetImportRowPreviewRecordDto(
            row.RowNumber,
            row.RecordId,
            row.Status,
            row.Summary,
            row.Cells.Select(ToDto).ToArray(),
            row.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static SpreadsheetImportCellPreviewRecordDto ToDto(SvDumpImportCellPreviewRecord cell)
    {
        return new SpreadsheetImportCellPreviewRecordDto(
            cell.Header,
            cell.Field,
            cell.Value,
            cell.Status,
            cell.Message);
    }

    private static WorkflowSummaryDto ToDto(SvWorkflowSummary summary)
    {
        return new WorkflowSummaryDto(
            summary.Id,
            summary.Label,
            summary.Description,
            ToDto(summary.Availability),
            summary.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ItemRecordDto ToDto(SvItemRecord item)
    {
        return new ItemRecordDto(
            item.ItemId,
            item.Name,
            item.Category,
            item.BuyPrice,
            item.SellPrice,
            item.WattsPrice,
            item.AlternatePrice,
            new Dictionary<string, int?>(),
            new ItemMetadataDto(
                item.Metadata.Pouch,
                item.Metadata.PouchFlags,
                item.Metadata.FlingPower,
                item.Metadata.FieldUseType,
                item.Metadata.FieldFlags,
                item.Metadata.CanUseOnPokemon,
                item.Metadata.ItemType,
                item.Metadata.SortIndex,
                item.Metadata.ItemSprite,
                item.Metadata.GroupType,
                item.Metadata.GroupIndex,
                item.Metadata.CureStatusFlags,
                item.Metadata.Boost0,
                item.Metadata.Boost1,
                item.Metadata.Boost2,
                item.Metadata.Boost3,
                item.Metadata.UseFlags1,
                item.Metadata.UseFlags2,
                item.Metadata.EvHp,
                item.Metadata.EvAttack,
                item.Metadata.EvDefense,
                item.Metadata.EvSpeed,
                item.Metadata.EvSpecialAttack,
                item.Metadata.EvSpecialDefense,
                item.Metadata.HealAmount,
                item.Metadata.PpGain,
                item.Metadata.FriendshipGain1,
                item.Metadata.FriendshipGain2,
                item.Metadata.FriendshipGain3,
                item.Metadata.MachineSlot,
                item.Metadata.MachineMoveId,
                item.Metadata.MachineMoveName),
            item.SharedItemIds,
            item.DetailGroups.Select(ToDto).ToArray(),
            new ItemProvenanceDto(
                item.Provenance.SourceFile,
                ProjectBridgeMapper.ToDto(item.Provenance.SourceLayer),
                ProjectBridgeMapper.ToDto(item.Provenance.FileState)));
    }

    private static ItemDetailGroupDto ToDto(SvItemDetailGroup group)
    {
        return new ItemDetailGroupDto(
            group.Label,
            group.Details.Select(ToDto).ToArray());
    }

    private static ItemDetailDto ToDto(SvItemDetail detail)
    {
        return new ItemDetailDto(detail.Label, detail.Value);
    }

    private static ItemEditableFieldDto ToDto(SvItemEditableField field)
    {
        return new ItemEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(option => new ItemEditableFieldOptionDto(option.Value, option.Label)).ToArray());
    }

    private static PokemonRecordDto ToDto(SvPokemonRecord pokemon)
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
                ProjectBridgeMapper.ToDto(pokemon.Provenance.FileState)));
    }

    private static PokemonEditableFieldDto ToDto(SvPokemonEditableField field)
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

    private static PokemonEditableFieldOptionDto ToDto(SvPokemonEditableFieldOption option)
    {
        return new PokemonEditableFieldOptionDto(option.Value, option.Label);
    }

    private static PokemonEvolutionRecordDto ToDto(SvPokemonEvolutionRecord evolution)
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

    private static PokemonEvolutionMethodOptionDto ToDto(SvPokemonEvolutionMethodOption option)
    {
        return new PokemonEvolutionMethodOptionDto(
            option.Value,
            option.Label,
            option.ArgumentKind,
            option.ArgumentLabel,
            option.ArgumentOptions.Select(ToDto).ToArray());
    }

    private static PokemonLearnsetMoveDto ToDto(SvPokemonLearnsetMove learnsetMove)
    {
        return new PokemonLearnsetMoveDto(
            learnsetMove.Slot,
            learnsetMove.MoveId,
            learnsetMove.MoveName,
            learnsetMove.Level,
            learnsetMove.RawLevel == learnsetMove.Level ? null : learnsetMove.RawLevel,
            learnsetMove.LevelLabel);
    }

    private static PokemonCompatibilityGroupDto ToDto(SvPokemonCompatibilityGroup group)
    {
        return new PokemonCompatibilityGroupDto(
            group.GroupId,
            group.Label,
            group.EnabledCount,
            group.Entries.Select(ToDto).ToArray());
    }

    private static PokemonCompatibilityEntryDto ToDto(SvPokemonCompatibilityEntry entry)
    {
        return new PokemonCompatibilityEntryDto(
            entry.Slot,
            entry.MoveId,
            entry.MoveName,
            entry.Label,
            entry.CanLearn);
    }

    private static MoveRecordDto ToDto(SvMoveRecord move)
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

    private static MoveEditableFieldDto ToDto(SvMoveEditableField field)
    {
        return new MoveEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static MoveEditableFieldOptionDto ToDto(SvMoveEditableFieldOption option)
    {
        return new MoveEditableFieldOptionDto(option.Value, option.Label);
    }

    private static MoveStatChangeRecordDto ToDto(SvMoveStatChangeRecord statChange)
    {
        return new MoveStatChangeRecordDto(
            statChange.Slot,
            statChange.Stat,
            statChange.StatName,
            statChange.Stage,
            statChange.Percent);
    }

    private static MoveFlagRecordDto ToDto(SvMoveFlagRecord flag)
    {
        return new MoveFlagRecordDto(
            flag.Field,
            flag.Label,
            flag.Enabled);
    }

    private static MoveProvenanceDto ToDto(SvMoveProvenance provenance)
    {
        return new MoveProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static TextEntryRecordDto ToDto(SvTextEntryRecord entry)
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

    private static TextEditableFieldDto ToDto(SvTextEditableField field)
    {
        return new TextEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumLength,
            field.MaximumLength);
    }

    private static DialogueReferenceRecordDto ToDto(SvDialogueReferenceRecord reference)
    {
        return new DialogueReferenceRecordDto(
            reference.DialogueId,
            reference.Label,
            reference.TextId,
            reference.Context,
            reference.Preview,
            ToDto(reference.Provenance));
    }

    private static TextProvenanceDto ToDto(SvTextProvenance provenance)
    {
        return new TextProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static TrainerRecordDto ToDto(SvTrainerRecord trainer)
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
            trainer.CanTerastallize,
            trainer.TeraTarget,
            trainer.Heal,
            trainer.Money,
            trainer.Gift,
            trainer.ClassBallId,
            trainer.ClassBall,
            trainer.CanEditClassBall,
            trainer.ClassBallScope,
            trainer.Team.Select(ToDto).ToArray(),
            ToDto(trainer.Provenance));
    }

    private static TrainerPokemonRecordDto ToDto(SvTrainerPokemonRecord pokemon)
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
            pokemon.TeraType,
            pokemon.TeraTypeLabel)
        {
            AbilityOptions = pokemon.AbilityOptions.Select(ToDto).ToArray(),
            BaseStats = pokemon.BaseStats is null ? null : ToDto(pokemon.BaseStats),
        };
    }

    private static TrainerAiFlagStateDto ToDto(SvTrainerAiFlagState flag)
    {
        return new TrainerAiFlagStateDto(
            flag.Bit,
            flag.Mask,
            flag.Label,
            flag.Description,
            flag.Enabled);
    }

    private static TrainerPokemonStatsDto ToDto(SvTrainerPokemonStatsRecord stats)
    {
        return new TrainerPokemonStatsDto(
            stats.HP,
            stats.Attack,
            stats.Defense,
            stats.SpecialAttack,
            stats.SpecialDefense,
            stats.Speed);
    }

    private static TrainerEditableFieldDto ToDto(SvTrainerEditableField field)
    {
        return new TrainerEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static TrainerEditableFieldOptionDto ToDto(SvTrainerEditableFieldOption option)
    {
        return new TrainerEditableFieldOptionDto(option.Value, option.Label);
    }

    private static TrainerProvenanceDto ToDto(SvTrainerProvenance provenance)
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

    private static EncounterTableRecordDto ToDto(SvEncounterTableRecord table)
    {
        return new EncounterTableRecordDto(
            table.TableId,
            table.Location,
            table.Area,
            table.EncounterType,
            table.GameVersion,
            table.ArchiveMember,
            table.Slots.Select(ToDto).ToArray(),
            ToDto(table.Provenance));
    }

    private static EncounterSlotRecordDto ToDto(SvEncounterSlotRecord slot)
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
            slot.Weather);
    }

    private static EncounterEditableFieldDto ToDto(SvEncounterEditableField field)
    {
        return new EncounterEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static EncounterEditableFieldOptionDto ToDto(SvEncounterEditableFieldOption option)
    {
        return new EncounterEditableFieldOptionDto(option.Value, option.Label);
    }

    private static TeraRaidRecordDto ToDto(SvTeraRaidEntry raid)
    {
        return new TeraRaidRecordDto(
            raid.RecordId,
            raid.Region,
            raid.StarRank,
            raid.StarLabel,
            raid.EntryIndex,
            raid.RaidNo,
            raid.Version,
            raid.VersionLabel,
            raid.DeliveryGroupId,
            raid.Difficulty,
            raid.SpawnRate,
            raid.CaptureRate,
            raid.CaptureLevel,
            raid.SpeciesId,
            raid.Species,
            raid.Form,
            raid.Level,
            raid.HeldItemId,
            raid.HeldItem,
            raid.BallItemId,
            raid.BallItem,
            raid.Ability,
            raid.AbilityLabel,
            raid.Nature,
            raid.NatureLabel,
            raid.Gender,
            raid.GenderLabel,
            raid.ShinyLock,
            raid.ShinyLockLabel,
            raid.TeraType,
            raid.TeraTypeLabel,
            raid.MoveMode,
            raid.MoveModeLabel,
            raid.Moves.Select(ToDto).ToArray(),
            new TeraRaidIvsDto(
                raid.Ivs.HP,
                raid.Ivs.Attack,
                raid.Ivs.Defense,
                raid.Ivs.SpecialAttack,
                raid.Ivs.SpecialDefense,
                raid.Ivs.Speed),
            raid.FlawlessIvCount,
            raid.IvSummary,
            raid.ScaleMode,
            raid.ScaleModeLabel,
            raid.ScaleValue,
            raid.HeightMode,
            raid.HeightModeLabel,
            raid.HeightValue,
            raid.WeightMode,
            raid.WeightModeLabel,
            raid.WeightValue,
            raid.HpMultiplier,
            raid.ShieldTriggerHp,
            raid.ShieldTriggerTime,
            raid.DoubleActionHp,
            raid.DoubleActionTime,
            raid.DoubleActionRate,
            raid.FixedRewardTableHash,
            raid.LotteryRewardTableHash,
            raid.FixedRewardPreview,
            raid.LotteryRewardPreview,
            ToDto(raid.Provenance))
        {
            AbilityOptions = raid.AbilityOptions.Select(ToDto).ToArray(),
        };
    }

    private static TeraRaidMoveDto ToDto(SvTeraRaidMoveRecord move)
    {
        return new TeraRaidMoveDto(move.Slot, move.MoveId, move.Move, move.PointUps);
    }

    private static TeraRaidRewardTableDto ToDto(SvTeraRaidRewardTableRecord table)
    {
        return new TeraRaidRewardTableDto(
            table.RecordId,
            table.RewardKind,
            table.RewardKindLabel,
            table.TableIndex,
            table.TableHash,
            table.RewardItemCount,
            table.Preview,
            table.Rewards.Select(ToDto).ToArray(),
            ToDto(table.Provenance));
    }

    private static TeraRaidRewardItemDto ToDto(SvTeraRaidRewardItemRecord reward)
    {
        return new TeraRaidRewardItemDto(
            reward.RecordId,
            reward.RewardKind,
            reward.RewardKindLabel,
            reward.TableIndex,
            reward.TableHash,
            reward.Slot,
            reward.Category,
            reward.CategoryLabel,
            reward.SubjectType,
            reward.SubjectTypeLabel,
            reward.ItemId,
            reward.ItemName,
            reward.Count,
            reward.Rate,
            reward.RareItemFlag,
            ToDto(reward.Provenance));
    }

    private static TeraRaidEditableFieldDto ToDto(SvTeraRaidEditableField field)
    {
        return new TeraRaidEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static TeraRaidEditableFieldOptionDto ToDto(SvTeraRaidEditableFieldOption option)
    {
        return new TeraRaidEditableFieldOptionDto(option.Value, option.Label);
    }

    private static TeraRaidProvenanceDto ToDto(SvTeraRaidProvenance provenance)
    {
        return new TeraRaidProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static ShopRecordDto ToDto(SvShopRecord shop)
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
            EditorFamily = "sv",
            CanEditInventoryOrder = shop.CanEditInventoryOrder,
        };
    }

    private static ShopInventoryRecordDto ToDto(SvShopInventoryRecord inventoryItem)
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

    private static ShopProvenanceDto ToDto(SvShopProvenance provenance)
    {
        return new ShopProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static ShopEditableFieldDto ToDto(SvShopEditableField field)
    {
        return new ShopEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static ShopEditableFieldOptionDto ToDto(SvShopEditableFieldOption option)
    {
        return new ShopEditableFieldOptionDto(option.Value, option.Label, option.ItemName, option.Price);
    }

    private static StaticEncountersWorkflowDto ToStaticEncountersWorkflowDto(
        SvStaticEncountersWorkflow workflow)
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
                FixedSymbolCount = workflow.Stats.FixedSymbolCount,
                CoinSymbolCount = workflow.Stats.CoinSymbolCount,
            },
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray())
        {
            EditorFamily = "sv",
        };
    }

    private static StaticEncounterRecordDto ToDto(SvStaticEncounterEntry encounter)
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
            EditorFamily = "sv",
            CategoryId = encounter.CategoryId,
            CategoryLabel = encounter.CategoryLabel,
            SupportedFields = encounter.SupportedFields,
            FieldValues = encounter.FieldValues,
            FieldDisplayValues = encounter.FieldDisplayValues,
            FieldReadOnly = encounter.FieldReadOnly,
            AbilityOptions = encounter.AbilityOptions.Select(ToDto).ToArray(),
        };
    }

    private static StaticEncounterStatsDto ToDto(SvStaticEncounterStatsRecord stats)
    {
        return new StaticEncounterStatsDto(
            stats.HP,
            stats.Attack,
            stats.Defense,
            stats.SpecialAttack,
            stats.SpecialDefense,
            stats.Speed);
    }

    private static StaticEncounterMoveDto ToDto(SvStaticEncounterMoveRecord move)
    {
        return new StaticEncounterMoveDto(move.Slot, move.MoveId, move.Move);
    }

    private static StaticEncounterEditableFieldDto ToDto(SvStaticEncounterEditableField field)
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
        SvStaticEncounterEditableFieldOption option)
    {
        return new StaticEncounterEditableFieldOptionDto(option.Value, option.Label);
    }

    private static GiftPokemonRecordDto ToDto(SvGiftPokemonEntry gift)
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
            gift.BallId,
            gift.Ball,
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
            EditorFamily = "sv",
            AbilityOptions = gift.AbilityOptions.Select(ToDto).ToArray(),
            EventLabel = gift.EventLabel,
            Moves = gift.Moves.Select(ToDto).ToArray(),
            TeraType = gift.TeraType,
            TeraTypeLabel = gift.TeraTypeLabel,
            ScaleMode = gift.ScaleMode,
            ScaleModeLabel = gift.ScaleModeLabel,
            ScaleValue = gift.ScaleValue,
        };
    }

    private static GiftPokemonMoveDto ToDto(SvGiftPokemonMoveRecord move)
    {
        return new GiftPokemonMoveDto(move.Slot, move.MoveId, move.Move, move.PointUps);
    }

    private static GiftPokemonEditableFieldDto ToDto(SvGiftPokemonEditableField field)
    {
        return new GiftPokemonEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static GiftPokemonEditableFieldOptionDto ToDto(SvGiftPokemonEditableFieldOption option)
    {
        return new GiftPokemonEditableFieldOptionDto(option.Value, option.Label);
    }

    private static TradePokemonRecordDto ToDto(SvTradePokemonEntry trade)
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
            trade.BallId,
            trade.Ball,
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
            trade.RequiredSpeciesId,
            trade.RequiredSpecies,
            trade.RequiredForm,
            0,
            "Default",
            0,
            ToTrainerIdDto(trade.TrainerId),
            trade.OtGender,
            trade.OtGenderLabel,
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
            EditorFamily = "sv",
            EventLabel = trade.EventLabel,
            Moves = moves,
            TeraType = trade.TeraType,
            TeraTypeLabel = trade.TeraTypeLabel,
            ScaleMode = trade.ScaleMode,
            ScaleModeLabel = trade.ScaleModeLabel,
            ScaleValue = trade.ScaleValue,
            AbilityOptions = trade.AbilityOptions.Select(ToDto).ToArray(),
        };
    }

    private static TradePokemonMoveRecordDto ToDto(SvTradePokemonMoveRecord move)
    {
        return new TradePokemonMoveRecordDto(
            move.Slot,
            move.MoveId,
            move.Move);
    }

    private static TradePokemonEditableFieldDto ToDto(SvTradePokemonEditableField field)
    {
        return new TradePokemonEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static TradePokemonEditableFieldOptionDto ToDto(SvTradePokemonEditableFieldOption option)
    {
        return new TradePokemonEditableFieldOptionDto(option.Value, option.Label);
    }

    private static int ToTrainerIdDto(long trainerId)
    {
        if (trainerId <= 0)
        {
            return 0;
        }

        return trainerId > int.MaxValue ? int.MaxValue : (int)trainerId;
    }

    private static PlacedObjectRecordDto ToDto(SvPlacedObjectRecord placedObject)
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

    private static PlacementFieldValueDto ToDto(SvPlacementFieldValue value)
    {
        return new PlacementFieldValueDto(
            value.Field,
            value.Label,
            value.Group,
            value.Value,
            value.DisplayValue,
            value.IsReadOnly,
            "text",
            0,
            0,
            string.Empty,
            value.Options?.Select(ToDto).ToArray());
    }

    private static PlacementEditableFieldDto ToDto(SvPlacementEditableField field)
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

    private static PlacementEditableFieldOptionDto ToDto(SvPlacementEditableFieldOption option)
    {
        return new PlacementEditableFieldOptionDto(option.Value, option.Label);
    }

    private static PlacementCategoryDto ToDto(SvPlacementCategory category)
    {
        return new PlacementCategoryDto(
            category.Id,
            category.Label,
            category.Description,
            category.ObjectCount);
    }

    private static HyperspaceBypassReservedRegionDto ToDto(SvHyperspaceBypassReservedRegion region)
    {
        return new HyperspaceBypassReservedRegionDto(
            region.RegionId,
            region.Label,
            region.OffsetLabel,
            region.StartOffset,
            region.Length,
            region.Rule);
    }

    private static FashionUnlockReservedRegionDto ToDto(SvFashionUnlockReservedRegion region)
    {
        return new FashionUnlockReservedRegionDto(
            region.RegionId,
            region.Label,
            region.OffsetLabel,
            region.StartOffset,
            region.Length,
            region.Rule);
    }

    private static TypeChartSourceRecordDto ToDto(SvTypeChartSourceRecord source)
    {
        return new TypeChartSourceRecordDto(
            source.SourceId,
            source.Label,
            source.RelativePath,
            source.Status,
            ToDto(source.Provenance));
    }

    private static TypeChartProvenanceDto ToDto(SvTypeChartProvenance provenance)
    {
        return new TypeChartProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static TypeChartTypeDefinitionDto ToDto(SvTypeChartTypeDefinition type)
    {
        return new TypeChartTypeDefinitionDto(
            type.TypeIndex,
            type.Label,
            type.ShortLabel,
            type.Color);
    }

    private static TypeChartCellDto ToDto(SvTypeChartCell cell)
    {
        return new TypeChartCellDto(
            cell.AttackTypeIndex,
            cell.DefenseTypeIndex,
            cell.Effectiveness,
            cell.VanillaEffectiveness);
    }

    private static PlacementProvenanceDto ToDto(SvPlacementProvenance provenance)
    {
        return new PlacementProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static HyperspaceBypassProvenanceDto ToDto(SvHyperspaceBypassProvenance provenance)
    {
        return new HyperspaceBypassProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static FashionUnlockProvenanceDto ToDto(SvFashionUnlockProvenance provenance)
    {
        return new FashionUnlockProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static EncounterProvenanceDto ToDto(SvEncounterProvenance provenance)
    {
        return new EncounterProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static SvModMergerSourceRecordDto ToDto(SvModMergerSourceRecord source)
    {
        return new SvModMergerSourceRecordDto(
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

    private static SvModMergerPreviewDto ToDto(SvModMergerPreview preview)
    {
        return new SvModMergerPreviewDto(
            preview.CanApply,
            preview.Status,
            preview.SelectedFileCount,
            preview.ReadyFileCount,
            preview.ConflictFileCount,
            preview.UnresolvedConflictCount,
            preview.Files.Select(ToDto).ToArray(),
            preview.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static SvModMergerFilePreviewRecordDto ToDto(SvModMergerFilePreviewRecord file)
    {
        return new SvModMergerFilePreviewRecordDto(
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

    private static WorkflowAvailabilityDto ToDto(SvWorkflowAvailability availability)
    {
        return availability switch
        {
            SvWorkflowAvailability.Disabled => WorkflowAvailabilityDto.Disabled,
            SvWorkflowAvailability.ReadOnly => WorkflowAvailabilityDto.ReadOnly,
            SvWorkflowAvailability.Available => WorkflowAvailabilityDto.Available,
            _ => throw new ArgumentOutOfRangeException(nameof(availability), availability, null),
        };
    }

    private static ProjectGameDto? ToProjectGameDto(ProjectGame? game)
    {
        return game is null ? null : ProjectBridgeMapper.ToDto(game.Value);
    }
}
