// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Editing;
using KM.Api.BagHook;
using KM.Api.Behavior;
using KM.Api.CatchCap;
using KM.Api.DynamaxAdventures;
using KM.Api.Encounters;
using KM.Api.ExeFs;
using KM.Api.FairyGymBoosts;
using KM.Api.FashionUnlock;
using KM.Api.Flagwork;
using KM.Api.Gifts;
using KM.Api.GymUniformRemoval;
using KM.Api.HyperTraining;
using KM.Api.Items;
using KM.Api.IvScreen;
using KM.Api.ModMerger;
using KM.Api.NpcItemGift;
using KM.Api.Placement;
using KM.Api.Moves;
using KM.Api.Pokemon;
using KM.Api.Projects;
using KM.Api.Rentals;
using KM.Api.Shops;
using KM.Api.Raids;
using KM.Api.RoyalCandy;
using KM.Api.StartingItems;
using KM.Api.ShinyRate;
using KM.Api.StaticEncounters;
using KM.Api.Text;
using KM.Api.SpreadsheetImport;
using KM.Api.Trainers;
using KM.Api.Trades;
using KM.Api.TypeChart;
using KM.Api.Workflows;
using KM.Core.Projects;
using KM.SwSh.Behavior;
using KM.SwSh.BagHook;
using KM.SwSh.CatchCap;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Encounters;
using KM.SwSh.ExeFs;
using KM.SwSh.FairyGymBoosts;
using KM.SwSh.FashionUnlock;
using KM.SwSh.Flagwork;
using KM.SwSh.Gifts;
using KM.SwSh.GymUniformRemoval;
using KM.SwSh.HyperTraining;
using KM.SwSh.IvScreen;
using KM.SwSh.ModMerger;
using KM.SwSh.NpcItemGift;
using KM.SwSh.Placement;
using KM.SwSh.Moves;
using KM.SwSh.Pokemon;
using KM.SwSh.Rentals;
using KM.SwSh.Shops;
using KM.SwSh.Raids;
using KM.SwSh.RoyalCandy;
using KM.SwSh.StartingItems;
using KM.SwSh.ShinyRate;
using KM.SwSh.StaticEncounters;
using KM.SwSh.Text;
using KM.SwSh.SpreadsheetImport;
using KM.SwSh.Trainers;
using KM.SwSh.Trades;
using KM.SwSh.TypeChart;
using KM.SwSh.Workflows;
using KM.SwSh.Items;

namespace KM.Tools.Bridge;

public static class SwShBridgeMapper
{
    public static ListWorkflowsResponse ToDto(SwShWorkflowList workflowList)
    {
        ArgumentNullException.ThrowIfNull(workflowList);

        return new ListWorkflowsResponse(workflowList.Workflows.Select(ToDto).ToArray());
    }

    public static LoadItemsWorkflowResponse ToDto(SwShItemsWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadItemsWorkflowResponse(ToItemsWorkflowDto(workflow));
    }

    public static LoadPokemonWorkflowResponse ToDto(SwShPokemonWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadPokemonWorkflowResponse(ToPokemonWorkflowDto(workflow));
    }

    public static UpdatePokemonFieldResponse ToDto(SwShPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePokemonFieldResponse(
            ToPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdatePokemonFieldsResponse ToPokemonFieldsDto(SwShPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePokemonFieldsResponse(
            ToPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdatePokemonLearnsetResponse ToDtoLearnsetUpdate(SwShPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePokemonLearnsetResponse(
            ToPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdatePokemonEvolutionResponse ToDtoEvolutionUpdate(SwShPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePokemonEvolutionResponse(
            ToPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadMovesWorkflowResponse ToDto(SwShMovesWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadMovesWorkflowResponse(ToMovesWorkflowDto(workflow));
    }

    public static UpdateMoveFieldResponse ToDto(SwShMovesEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateMoveFieldResponse(
            ToMovesWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateMoveFieldsResponse ToMoveFieldsDto(SwShMovesEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateMoveFieldsResponse(
            ToMovesWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadTextWorkflowResponse ToDto(SwShTextWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadTextWorkflowResponse(ToTextWorkflowDto(workflow));
    }

    public static LoadTrainersWorkflowResponse ToDto(SwShTrainersWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadTrainersWorkflowResponse(ToTrainersWorkflowDto(workflow));
    }

    public static LoadShopsWorkflowResponse ToDto(SwShShopsWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadShopsWorkflowResponse(ToShopsWorkflowDto(workflow));
    }

    public static LoadGiftPokemonWorkflowResponse ToDto(SwShGiftPokemonWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadGiftPokemonWorkflowResponse(ToGiftPokemonWorkflowDto(workflow));
    }

    public static LoadTradePokemonWorkflowResponse ToDto(SwShTradePokemonWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadTradePokemonWorkflowResponse(ToTradePokemonWorkflowDto(workflow));
    }

    public static LoadStaticEncountersWorkflowResponse ToDto(SwShStaticEncountersWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadStaticEncountersWorkflowResponse(ToStaticEncountersWorkflowDto(workflow));
    }

    public static LoadRentalPokemonWorkflowResponse ToDto(SwShRentalPokemonWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadRentalPokemonWorkflowResponse(ToRentalPokemonWorkflowDto(workflow));
    }

    public static LoadDynamaxAdventuresWorkflowResponse ToDto(SwShDynamaxAdventuresWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadDynamaxAdventuresWorkflowResponse(ToDynamaxAdventuresWorkflowDto(workflow));
    }

    public static PlanDynamaxAdventureSeedResponse ToDto(SwShDynamaxAdventureSeedPlanResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new PlanDynamaxAdventureSeedResponse(ToDynamaxAdventureSeedPlanDto(result));
    }

    public static SearchDynamaxAdventureSeedResponse ToDto(SwShDynamaxAdventureSeedSearchPlanResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new SearchDynamaxAdventureSeedResponse(ToDynamaxAdventureSeedSearchDto(result));
    }

    public static SetDynamaxAdventureSaveSeedResponse ToDto(SwShDynamaxAdventureSaveSeedResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new SetDynamaxAdventureSaveSeedResponse(
            new DynamaxAdventureSaveSeedDto(
                result.SaveFilePath,
                result.BackupFilePath,
                result.OldSeed,
                result.NewSeed,
                result.WasChanged,
                result.ChecksumsValid,
                result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray()));
    }

    public static PreviewDynamaxAdventureDefaultsResponse ToDto(SwShDynamaxAdventureDefaultPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return new PreviewDynamaxAdventureDefaultsResponse(
            preview.Changes.Select(ToDto).ToArray(),
            preview.AbilityOptions.Select(ToDto).ToArray(),
            preview.GigantamaxOptions.Select(ToDto).ToArray(),
            preview.MoveOptions.Select(ToDto).ToArray(),
            preview.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadEncountersWorkflowResponse ToDto(SwShEncountersWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadEncountersWorkflowResponse(ToEncountersWorkflowDto(workflow));
    }

    public static LoadRaidRewardsWorkflowResponse ToDto(SwShRaidRewardsWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadRaidRewardsWorkflowResponse(ToRaidRewardsWorkflowDto(workflow));
    }

    public static LoadRaidBonusRewardsWorkflowResponse ToBonusDto(SwShRaidRewardsWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadRaidBonusRewardsWorkflowResponse(ToRaidRewardsWorkflowDto(workflow));
    }

    public static LoadRaidBattlesWorkflowResponse ToDto(SwShRaidBattlesWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadRaidBattlesWorkflowResponse(ToRaidBattlesWorkflowDto(workflow));
    }

    public static LoadPlacementWorkflowResponse ToDto(SwShPlacementWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadPlacementWorkflowResponse(ToPlacementWorkflowDto(workflow));
    }

    public static LoadBehaviorWorkflowResponse ToDto(SwShBehaviorWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadBehaviorWorkflowResponse(ToBehaviorWorkflowDto(workflow));
    }

    public static LoadFlagworkSaveWorkflowResponse ToDto(SwShFlagworkSaveWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadFlagworkSaveWorkflowResponse(ToFlagworkSaveWorkflowDto(workflow));
    }

    public static LoadExeFsPatchWorkflowResponse ToDto(SwShExeFsPatchWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadExeFsPatchWorkflowResponse(ToExeFsPatchWorkflowDto(workflow));
    }

    public static StageExeFsPatchResponse ToDto(SwShExeFsPatchEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageExeFsPatchResponse(
            ToExeFsPatchWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadBagHookWorkflowResponse ToDto(SwShBagHookWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadBagHookWorkflowResponse(ToBagHookWorkflowDto(workflow));
    }

    public static StageBagHookInstallResponse ToDto(SwShBagHookEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageBagHookInstallResponse(
            ToBagHookWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static StageBagHookUninstallResponse ToBagHookUninstallDto(SwShBagHookEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageBagHookUninstallResponse(
            ToBagHookWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadCatchCapWorkflowResponse ToDto(SwShCatchCapWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadCatchCapWorkflowResponse(ToCatchCapWorkflowDto(workflow));
    }

    public static StageCatchCapResponse ToDto(SwShCatchCapEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageCatchCapResponse(
            ToCatchCapWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static StageCatchCapUninstallResponse ToCatchCapUninstallDto(SwShCatchCapEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageCatchCapUninstallResponse(
            ToCatchCapWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadHyperTrainingWorkflowResponse ToDto(SwShHyperTrainingWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadHyperTrainingWorkflowResponse(ToHyperTrainingWorkflowDto(workflow));
    }

    public static StageHyperTrainingResponse ToDto(SwShHyperTrainingEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageHyperTrainingResponse(
            ToHyperTrainingWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadShinyRateWorkflowResponse ToDto(SwShShinyRateWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadShinyRateWorkflowResponse(ToShinyRateWorkflowDto(workflow));
    }

    public static StageShinyRateResponse ToDto(SwShShinyRateEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageShinyRateResponse(
            ToShinyRateWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadTypeChartWorkflowResponse ToDto(SwShTypeChartWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadTypeChartWorkflowResponse(ToTypeChartWorkflowDto(workflow));
    }

    public static StageTypeChartResponse ToDto(SwShTypeChartEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageTypeChartResponse(
            ToTypeChartWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadFairyGymBoostsWorkflowResponse ToDto(SwShFairyGymBoostsWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadFairyGymBoostsWorkflowResponse(ToFairyGymBoostsWorkflowDto(workflow));
    }

    public static StageFairyGymBoostsResponse ToDto(SwShFairyGymBoostsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageFairyGymBoostsResponse(
            ToFairyGymBoostsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadFashionUnlockWorkflowResponse ToDto(SwShFashionUnlockWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadFashionUnlockWorkflowResponse(ToFashionUnlockWorkflowDto(workflow));
    }

    public static StageFashionUnlockInstallResponse ToDto(SwShFashionUnlockEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageFashionUnlockInstallResponse(
            ToFashionUnlockWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static StageFashionUnlockUninstallResponse ToFashionUnlockUninstallDto(SwShFashionUnlockEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageFashionUnlockUninstallResponse(
            ToFashionUnlockWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadIvScreenWorkflowResponse ToDto(SwShIvScreenWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadIvScreenWorkflowResponse(ToIvScreenWorkflowDto(workflow));
    }

    public static LoadGymUniformRemovalWorkflowResponse ToDto(SwShGymUniformRemovalWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadGymUniformRemovalWorkflowResponse(ToGymUniformRemovalWorkflowDto(workflow));
    }

    public static StageIvScreenInstallResponse ToDto(SwShIvScreenEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageIvScreenInstallResponse(
            ToIvScreenWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static StageGymUniformRemovalInstallResponse ToDto(SwShGymUniformRemovalEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageGymUniformRemovalInstallResponse(
            ToGymUniformRemovalWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static StageIvScreenUninstallResponse ToIvScreenUninstallDto(SwShIvScreenEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageIvScreenUninstallResponse(
            ToIvScreenWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static StageGymUniformRemovalUninstallResponse ToGymUniformRemovalUninstallDto(SwShGymUniformRemovalEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageGymUniformRemovalUninstallResponse(
            ToGymUniformRemovalWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadRoyalCandyWorkflowResponse ToDto(SwShRoyalCandyWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadRoyalCandyWorkflowResponse(ToRoyalCandyWorkflowDto(workflow));
    }

    public static StageRoyalCandyWorkflowResponse ToDto(SwShRoyalCandyEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageRoyalCandyWorkflowResponse(
            ToRoyalCandyWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadStartingItemsWorkflowResponse ToDto(SwShStartingItemsWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadStartingItemsWorkflowResponse(ToStartingItemsWorkflowDto(workflow));
    }

    public static StageStartingItemsResponse ToDto(SwShStartingItemsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageStartingItemsResponse(
            ToStartingItemsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadNpcItemGiftWorkflowResponse ToDto(SwShNpcItemGiftWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadNpcItemGiftWorkflowResponse(ToNpcItemGiftWorkflowDto(workflow));
    }

    public static StageNpcItemGiftResponse ToDto(SwShNpcItemGiftEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageNpcItemGiftResponse(
            ToNpcItemGiftWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadSpreadsheetImportWorkflowResponse ToDto(SwShSpreadsheetImportWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadSpreadsheetImportWorkflowResponse(ToSpreadsheetImportWorkflowDto(workflow));
    }

    public static PreviewSpreadsheetImportResponse ToDto(SwShSpreadsheetImportExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new PreviewSpreadsheetImportResponse(
            ToSpreadsheetImportWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            ToDto(result.Preview),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static LoadModMergerWorkflowResponse ToDto(SwShModMergerWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadModMergerWorkflowResponse(ToModMergerWorkflowDto(workflow));
    }

    public static StageModMergeResponse ToDto(SwShModMergerStageResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageModMergeResponse(
            ToModMergerWorkflowDto(result.Workflow),
            ToDto(result.Preview),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static ApplyModMergeResponse ToDto(SwShModMergerApplyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ApplyModMergeResponse(
            ToModMergerWorkflowDto(result.Workflow),
            ToDto(result.Preview),
            result.WrittenFiles,
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateItemFieldResponse ToDto(SwShItemsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateItemFieldResponse(
            ToItemsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateItemFieldsResponse ToItemFieldsDto(SwShItemsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateItemFieldsResponse(
            ToItemsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateTextEntryResponse ToDto(SwShTextEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateTextEntryResponse(
            ToTextWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateTrainerFieldResponse ToDto(SwShTrainersEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateTrainerFieldResponse(
            ToTrainersWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateShopInventoryItemResponse ToDto(SwShShopsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateShopInventoryItemResponse(
            ToShopsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateGiftPokemonFieldResponse ToDto(SwShGiftPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateGiftPokemonFieldResponse(
            ToGiftPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateGiftPokemonFieldsResponse ToGiftPokemonFieldsDto(SwShGiftPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateGiftPokemonFieldsResponse(
            ToGiftPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateTradePokemonFieldResponse ToDto(SwShTradePokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateTradePokemonFieldResponse(
            ToTradePokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateTradePokemonFieldsResponse ToTradePokemonFieldsDto(SwShTradePokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateTradePokemonFieldsResponse(
            ToTradePokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateStaticEncounterFieldResponse ToDto(SwShStaticEncountersEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateStaticEncounterFieldResponse(
            ToStaticEncountersWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateRentalPokemonFieldResponse ToDto(SwShRentalPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateRentalPokemonFieldResponse(
            ToRentalPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateRentalPokemonFieldsResponse ToRentalPokemonFieldsDto(SwShRentalPokemonEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateRentalPokemonFieldsResponse(
            ToRentalPokemonWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateDynamaxAdventureFieldResponse ToDto(SwShDynamaxAdventuresEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateDynamaxAdventureFieldResponse(
            ToDynamaxAdventuresWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateEncounterSlotFieldResponse ToDto(SwShEncountersEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateEncounterSlotFieldResponse(
            ToEncountersWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateEncounterSlotFieldsResponse ToEncounterSlotFieldsDto(SwShEncountersEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateEncounterSlotFieldsResponse(
            ToEncountersWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateRaidRewardFieldResponse ToDto(SwShRaidRewardsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateRaidRewardFieldResponse(
            ToRaidRewardsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateRaidRewardFieldsResponse ToFieldsDto(SwShRaidRewardsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateRaidRewardFieldsResponse(
            ToRaidRewardsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateRaidBonusRewardFieldResponse ToBonusDto(SwShRaidRewardsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateRaidBonusRewardFieldResponse(
            ToRaidRewardsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateRaidBonusRewardFieldsResponse ToBonusFieldsDto(SwShRaidRewardsEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateRaidBonusRewardFieldsResponse(
            ToRaidRewardsWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateRaidBattleSlotFieldResponse ToDto(SwShRaidBattlesEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateRaidBattleSlotFieldResponse(
            ToRaidBattlesWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateRaidBattleSlotFieldsResponse ToFieldsDto(SwShRaidBattlesEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateRaidBattleSlotFieldsResponse(
            ToRaidBattlesWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdatePlacementObjectFieldResponse ToDto(SwShPlacementEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePlacementObjectFieldResponse(
            ToPlacementWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdatePlacementObjectFieldsResponse ToPlacementObjectFieldsDto(SwShPlacementEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdatePlacementObjectFieldsResponse(
            Workflow: null,
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray(),
            result.UpdatedObjects?.Select(ToDto).ToArray() ?? []);
    }

    public static UpdateBehaviorEntryFieldResponse ToDto(SwShBehaviorEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateBehaviorEntryFieldResponse(
            ToBehaviorWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static UpdateBehaviorEntryFieldsResponse ToBehaviorEntryFieldsDto(SwShBehaviorEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new UpdateBehaviorEntryFieldsResponse(
            ToBehaviorWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static ValidateEditSessionResponse ToDto(SwShEditSessionValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);

        return new ValidateEditSessionResponse(
            EditSessionBridgeMapper.ToDto(validation.Session),
            validation.IsValid,
            validation.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ItemsWorkflowDto ToItemsWorkflowDto(SwShItemsWorkflow workflow)
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

    private static PokemonWorkflowDto ToPokemonWorkflowDto(SwShPokemonWorkflow workflow)
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

    private static MovesWorkflowDto ToMovesWorkflowDto(SwShMovesWorkflow workflow)
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

    private static TextWorkflowDto ToTextWorkflowDto(SwShTextWorkflow workflow)
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

    private static GiftPokemonWorkflowDto ToGiftPokemonWorkflowDto(SwShGiftPokemonWorkflow workflow)
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
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static GiftPokemonRecordDto ToDto(SwShGiftPokemonEntry gift)
    {
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
            gift.BallItemId,
            gift.BallItem,
            gift.Ability,
            gift.AbilityLabel,
            gift.Nature,
            gift.NatureLabel,
            gift.Gender,
            gift.GenderLabel,
            gift.ShinyLock,
            gift.ShinyLockLabel,
            gift.DynamaxLevel,
            gift.CanGigantamax,
            gift.SpecialMoveId,
            gift.SpecialMove,
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
            AbilityOptions = gift.AbilityOptions.Select(ToDto).ToArray(),
            GenderOptions = gift.GenderOptions.Select(ToDto).ToArray(),
        };
    }

    private static GiftPokemonEditableFieldDto ToDto(SwShGiftPokemonEditableField field)
    {
        return new GiftPokemonEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static GiftPokemonEditableFieldOptionDto ToDto(SwShGiftPokemonEditableFieldOption option)
    {
        return new GiftPokemonEditableFieldOptionDto(option.Value, option.Label);
    }

    private static TradePokemonWorkflowDto ToTradePokemonWorkflowDto(SwShTradePokemonWorkflow workflow)
    {
        return new TradePokemonWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Trades.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new TradePokemonWorkflowStatsDto(
                workflow.Stats.TotalTradeCount,
                workflow.Stats.FixedIvTradeCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static TradePokemonRecordDto ToDto(SwShTradePokemonEntry trade)
    {
        return new TradePokemonRecordDto(
            trade.TradeIndex,
            trade.Label,
            trade.SpeciesId,
            trade.Species,
            trade.Form,
            trade.Level,
            trade.HeldItemId,
            trade.HeldItem,
            trade.BallItemId,
            trade.BallItem,
            trade.Ability,
            trade.AbilityLabel,
            trade.Nature,
            trade.NatureLabel,
            trade.Gender,
            trade.GenderLabel,
            trade.ShinyLock,
            trade.ShinyLockLabel,
            trade.DynamaxLevel,
            trade.CanGigantamax,
            trade.RequiredSpeciesId,
            trade.RequiredSpecies,
            trade.RequiredForm,
            trade.RequiredNature,
            trade.RequiredNatureLabel,
            trade.UnknownRequirement,
            trade.TrainerId,
            trade.OtGender,
            trade.OtGenderLabel,
            trade.MemoryCode,
            trade.MemoryTextVariable,
            trade.MemoryFeel,
            trade.MemoryIntensity,
            trade.Field03,
            FormatHash(trade.Hash0),
            FormatHash(trade.Hash1),
            FormatHash(trade.Hash2),
            trade.RelearnMoves.Select(ToDto).ToArray(),
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
            AbilityOptions = trade.AbilityOptions.Select(ToDto).ToArray(),
            GenderOptions = trade.GenderOptions.Select(ToDto).ToArray(),
        };
    }

    private static TradePokemonMoveRecordDto ToDto(SwShTradePokemonMoveRecord move)
    {
        return new TradePokemonMoveRecordDto(
            move.Slot,
            move.MoveId,
            move.Move);
    }

    private static TradePokemonEditableFieldDto ToDto(SwShTradePokemonEditableField field)
    {
        return new TradePokemonEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static TradePokemonEditableFieldOptionDto ToDto(SwShTradePokemonEditableFieldOption option)
    {
        return new TradePokemonEditableFieldOptionDto(option.Value, option.Label);
    }

    private static string FormatHash(ulong value)
    {
        return $"0x{value:X16}";
    }

    private static StaticEncountersWorkflowDto ToStaticEncountersWorkflowDto(SwShStaticEncountersWorkflow workflow)
    {
        return new StaticEncountersWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Encounters.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new StaticEncountersWorkflowStatsDto(
                workflow.Stats.TotalEncounterCount,
                workflow.Stats.GigantamaxEncounterCount,
                workflow.Stats.FixedIvEncounterCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static StaticEncounterRecordDto ToDto(SwShStaticEncounterEntry encounter)
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
            encounter.DynamaxLevel,
            encounter.CanGigantamax,
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
            AbilityOptions = encounter.AbilityOptions.Select(ToDto).ToArray(),
            GenderOptions = encounter.GenderOptions.Select(ToDto).ToArray(),
        };
    }

    private static StaticEncounterStatsDto ToDto(SwShStaticEncounterStatsRecord stats)
    {
        return new StaticEncounterStatsDto(
            stats.HP,
            stats.Attack,
            stats.Defense,
            stats.SpecialAttack,
            stats.SpecialDefense,
            stats.Speed);
    }

    private static StaticEncounterMoveDto ToDto(SwShStaticEncounterMoveRecord move)
    {
        return new StaticEncounterMoveDto(
            move.Slot,
            move.MoveId,
            move.Move);
    }

    private static StaticEncounterEditableFieldDto ToDto(SwShStaticEncounterEditableField field)
    {
        return new StaticEncounterEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static StaticEncounterEditableFieldOptionDto ToDto(SwShStaticEncounterEditableFieldOption option)
    {
        return new StaticEncounterEditableFieldOptionDto(option.Value, option.Label);
    }

    private static RentalPokemonWorkflowDto ToRentalPokemonWorkflowDto(SwShRentalPokemonWorkflow workflow)
    {
        return new RentalPokemonWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Rentals.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new RentalPokemonWorkflowStatsDto(
                workflow.Stats.TotalRentalCount,
                workflow.Stats.PerfectIvRentalCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static RentalPokemonRecordDto ToDto(SwShRentalPokemonEntry rental)
    {
        return new RentalPokemonRecordDto(
            rental.RentalIndex,
            rental.Label,
            rental.SpeciesId,
            rental.Species,
            rental.Form,
            rental.Level,
            rental.HeldItemId,
            rental.HeldItem,
            rental.BallItemId,
            rental.BallItem,
            rental.Ability,
            rental.AbilityLabel,
            rental.Nature,
            rental.NatureLabel,
            rental.Gender,
            rental.GenderLabel,
            rental.TrainerId,
            rental.Hash1,
            rental.Hash2,
            rental.Moves.Select(ToDto).ToArray(),
            ToDto(rental.Evs),
            ToDto(rental.Ivs),
            rental.HasPerfectIvs,
            rental.IvSummary,
            new RentalPokemonProvenanceDto(
                rental.Provenance.SourceFile,
                ProjectBridgeMapper.ToDto(rental.Provenance.SourceLayer),
                ProjectBridgeMapper.ToDto(rental.Provenance.FileState)))
        {
            AbilityOptions = rental.AbilityOptions.Select(ToDto).ToArray(),
            GenderOptions = rental.GenderOptions.Select(ToDto).ToArray(),
        };
    }

    private static RentalPokemonStatsDto ToDto(SwShRentalPokemonStatsRecord stats)
    {
        return new RentalPokemonStatsDto(
            stats.HP,
            stats.Attack,
            stats.Defense,
            stats.SpecialAttack,
            stats.SpecialDefense,
            stats.Speed);
    }

    private static RentalPokemonMoveRecordDto ToDto(SwShRentalPokemonMoveRecord move)
    {
        return new RentalPokemonMoveRecordDto(
            move.Slot,
            move.MoveId,
            move.Move);
    }

    private static RentalPokemonEditableFieldDto ToDto(SwShRentalPokemonEditableField field)
    {
        return new RentalPokemonEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static RentalPokemonEditableFieldOptionDto ToDto(SwShRentalPokemonEditableFieldOption option)
    {
        return new RentalPokemonEditableFieldOptionDto(option.Value, option.Label);
    }

    private static DynamaxAdventuresWorkflowDto ToDynamaxAdventuresWorkflowDto(SwShDynamaxAdventuresWorkflow workflow)
    {
        return new DynamaxAdventuresWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Encounters.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            workflow.SafeNormalSpeciesOptions.Select(ToDto).ToArray(),
            new DynamaxAdventuresWorkflowStatsDto(
                workflow.Stats.TotalEncounterCount,
                workflow.Stats.SingleCaptureCount,
                workflow.Stats.StoryGatedCount,
                workflow.Stats.GuaranteedPerfectIvEncounterCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static DynamaxAdventureRecordDto ToDto(SwShDynamaxAdventureEntry encounter)
    {
        return new DynamaxAdventureRecordDto(
            encounter.EntryIndex,
            encounter.IsEditable,
            encounter.Label,
            encounter.AdventureIndex,
            encounter.SpeciesId,
            encounter.Species,
            encounter.Form,
            encounter.BossTargetSpeciesId,
            encounter.BossTargetSpecies,
            encounter.Level,
            encounter.BallItemId,
            encounter.BallItem,
            encounter.Ability,
            encounter.AbilityLabel,
            encounter.GigantamaxState,
            encounter.GigantamaxLabel,
            encounter.Version,
            encounter.VersionLabel,
            encounter.ShinyRoll,
            encounter.ShinyRollLabel,
            encounter.IsSingleCapture,
            encounter.SingleCaptureFlagBlock,
            encounter.IsStoryProgressGated,
            encounter.UiMessageId,
            encounter.OtGender,
            encounter.OtGenderLabel,
            encounter.Moves.Select(ToDto).ToArray(),
            ToDto(encounter.Ivs),
            encounter.GuaranteedPerfectIvs,
            encounter.IvSummary,
            new DynamaxAdventureProvenanceDto(
                encounter.Provenance.SourceFile,
                ProjectBridgeMapper.ToDto(encounter.Provenance.SourceLayer),
                ProjectBridgeMapper.ToDto(encounter.Provenance.FileState)))
        {
            AbilityOptions = encounter.AbilityOptions.Select(ToDto).ToArray(),
            GigantamaxOptions = encounter.GigantamaxOptions.Select(ToDto).ToArray(),
            MoveOptions = encounter.MoveOptions.Select(ToDto).ToArray(),
            BossTargetOptions = encounter.BossTargetOptions.Select(ToDto).ToArray(),
            VanillaPokemon = encounter.VanillaPokemon is null
                ? null
                : ToDto(encounter.VanillaPokemon),
        };
    }

    private static DynamaxAdventureBossTargetOptionDto ToDto(
        SwShDynamaxAdventureBossTargetOption option)
    {
        return new DynamaxAdventureBossTargetOptionDto(
            option.EntryIndex,
            option.AdventureIndex,
            option.SpeciesId,
            option.Species,
            option.Form,
            option.Version,
            option.VersionLabel,
            option.IsStoryProgressGated,
            option.Label);
    }

    private static DynamaxAdventurePokemonSnapshotDto ToDto(
        SwShDynamaxAdventurePokemonSnapshot snapshot)
    {
        return new DynamaxAdventurePokemonSnapshotDto(
            snapshot.SpeciesId,
            snapshot.Species,
            snapshot.Form,
            snapshot.Level,
            snapshot.Ability,
            snapshot.AbilityLabel,
            snapshot.GigantamaxState,
            snapshot.GigantamaxLabel,
            snapshot.Moves.Select(ToDto).ToArray(),
            ToDto(snapshot.Ivs),
            snapshot.GuaranteedPerfectIvs,
            snapshot.IvSummary);
    }

    private static DynamaxAdventureMoveRecordDto ToDto(SwShDynamaxAdventureMoveRecord move)
    {
        return new DynamaxAdventureMoveRecordDto(
            move.Slot,
            move.MoveId,
            move.Move);
    }

    private static DynamaxAdventureIvsDto ToDto(SwShDynamaxAdventureIvsRecord ivs)
    {
        return new DynamaxAdventureIvsDto(
            ivs.Hp,
            ivs.Attack,
            ivs.Defense,
            ivs.Speed,
            ivs.SpecialAttack,
            ivs.SpecialDefense);
    }

    private static DynamaxAdventureEditableFieldDto ToDto(SwShDynamaxAdventureEditableField field)
    {
        return new DynamaxAdventureEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static DynamaxAdventureEditableFieldOptionDto ToDto(SwShDynamaxAdventureEditableFieldOption option)
    {
        return new DynamaxAdventureEditableFieldOptionDto(option.Value, option.Label);
    }

    private static DynamaxAdventureDefaultFieldDto ToDto(SwShDynamaxAdventureDefaultField field)
    {
        return new DynamaxAdventureDefaultFieldDto(field.Field, field.Value);
    }

    private static DynamaxAdventureSeedPlanDto ToDynamaxAdventureSeedPlanDto(
        SwShDynamaxAdventureSeedPlanResult result)
    {
        return new DynamaxAdventureSeedPlanDto(
            FormatSeed(result.Seed),
            result.NpcCount,
            result.Rentals.Select(ToDto).ToArray(),
            result.Encounters.Select(ToDto).ToArray(),
            result.RequiredRowPositions.Select(ToDto).ToArray(),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static DynamaxAdventureSeedSearchDto ToDynamaxAdventureSeedSearchDto(
        SwShDynamaxAdventureSeedSearchPlanResult result)
    {
        return new DynamaxAdventureSeedSearchDto(
            result.NpcCount,
            FormatSeed(result.StartSeed),
            FormatSeed(result.Limit),
            result.MaxResults,
            result.Results.Select(ToDto).ToArray(),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static DynamaxAdventureSeedSearchMatchDto ToDto(
        SwShDynamaxAdventureSeedSearchPlanMatch match)
    {
        return new DynamaxAdventureSeedSearchMatchDto(
            FormatSeed(match.Seed),
            match.Positions.Select(ToDto).ToArray());
    }

    private static DynamaxAdventureSeedTemplateDto ToDto(
        SwShDynamaxAdventureSeedPlanTemplate template)
    {
        return new DynamaxAdventureSeedTemplateDto(
            template.Row,
            template.Species,
            template.Form,
            template.IsBoss);
    }

    private static DynamaxAdventureSeedRowPositionDto ToDto(
        SwShDynamaxAdventureSeedPlanRowPosition position)
    {
        return new DynamaxAdventureSeedRowPositionDto(
            position.Row,
            position.Kind == SwShDynamaxAdventureSeedPlanSlotKind.Rental ? "rental" : "encounter",
            position.Slot);
    }

    private static string FormatSeed(ulong value)
    {
        return $"0x{value:X16}";
    }

    private static TrainersWorkflowDto ToTrainersWorkflowDto(SwShTrainersWorkflow workflow)
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

    private static ShopsWorkflowDto ToShopsWorkflowDto(SwShShopsWorkflow workflow)
    {
        return new ShopsWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Shops.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new ShopsWorkflowStatsDto(
                workflow.Stats.TotalShopCount,
                workflow.Stats.TotalInventoryItemCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static EncountersWorkflowDto ToEncountersWorkflowDto(SwShEncountersWorkflow workflow)
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

    private static RaidBattlesWorkflowDto ToRaidBattlesWorkflowDto(SwShRaidBattlesWorkflow workflow)
    {
        return new RaidBattlesWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Tables.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new RaidBattlesWorkflowStatsDto(
                workflow.Stats.TotalTableCount,
                workflow.Stats.TotalSlotCount,
                workflow.Stats.GigantamaxSlotCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static RaidRewardsWorkflowDto ToRaidRewardsWorkflowDto(SwShRaidRewardsWorkflow workflow)
    {
        return new RaidRewardsWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Tables.Select(ToDto).ToArray(),
            workflow.EditableFields.Select(ToDto).ToArray(),
            new RaidRewardsWorkflowStatsDto(
                workflow.Stats.TotalTableCount,
                workflow.Stats.TotalRewardItemCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static PlacementWorkflowDto ToPlacementWorkflowDto(SwShPlacementWorkflow workflow)
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

    private static BehaviorWorkflowDto ToBehaviorWorkflowDto(SwShBehaviorWorkflow workflow)
    {
        return new BehaviorWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Entries.Select(ToDto).ToArray(),
            workflow.Fields.Select(ToDto).ToArray(),
            new BehaviorWorkflowStatsDto(
                workflow.Stats.TotalEntryCount,
                workflow.Stats.TotalBehaviorCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static FlagworkSaveWorkflowDto ToFlagworkSaveWorkflowDto(SwShFlagworkSaveWorkflow workflow)
    {
        return new FlagworkSaveWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Flags.Select(ToDto).ToArray(),
            workflow.SaveBlocks.Select(ToDto).ToArray(),
            workflow.SaveFile is null ? null : ToDto(workflow.SaveFile),
            new FlagworkSaveWorkflowStatsDto(
                workflow.Stats.TotalFlagCount,
                workflow.Stats.TotalSaveBlockCount,
                workflow.Stats.SourceFileCount,
                workflow.Stats.HasSaveFile),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ExeFsPatchWorkflowDto ToExeFsPatchWorkflowDto(SwShExeFsPatchWorkflow workflow)
    {
        return new ExeFsPatchWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Patches.Select(ToDto).ToArray(),
            workflow.Segments.Select(ToDto).ToArray(),
            workflow.Checks.Select(ToDto).ToArray(),
            new ExeFsPatchWorkflowStatsDto(
                workflow.Stats.TotalPatchCount,
                workflow.Stats.TotalCheckCount,
                workflow.Stats.PassCount,
                workflow.Stats.WarningCount,
                workflow.Stats.FailCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static BagHookWorkflowDto ToBagHookWorkflowDto(SwShBagHookWorkflow workflow)
    {
        return new BagHookWorkflowDto(
            ToDto(workflow.Summary),
            workflow.InstallStatus,
            workflow.InstallMessage,
            workflow.Slots.Select(ToDto).ToArray(),
            new BagHookWorkflowStatsDto(
                workflow.Stats.TotalSlotCount,
                workflow.Stats.OccupiedSlotCount,
                workflow.Stats.EmptySlotCount,
                workflow.Stats.ReservedSlotCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static CatchCapWorkflowDto ToCatchCapWorkflowDto(SwShCatchCapWorkflow workflow)
    {
        return new CatchCapWorkflowDto(
            ToDto(workflow.Summary),
            workflow.InstallStatus,
            workflow.InstallMessage,
            workflow.LogicExpression,
            workflow.CapLogicSha256,
            workflow.BuildId,
            workflow.DetectedGame is null
                ? null
                : ProjectBridgeMapper.ToDto(workflow.DetectedGame.Value),
            workflow.DisplayHookOffsetHex,
            workflow.RuntimeHookOffsetHex,
            workflow.Caps.Select(ToDto).ToArray(),
            ToDto(workflow.Provenance),
            new CatchCapWorkflowStatsDto(
                workflow.Stats.TotalCapCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static HyperTrainingWorkflowDto ToHyperTrainingWorkflowDto(SwShHyperTrainingWorkflow workflow)
    {
        return new HyperTrainingWorkflowDto(
            ToDto(workflow.Summary),
            workflow.InstallStatus,
            workflow.InstallMessage,
            workflow.BuildId,
            ToProjectGameDto(workflow.DetectedGame),
            ToDto(workflow.LevelRule),
            workflow.Sources.Select(ToDto).ToArray(),
            new HyperTrainingWorkflowStatsDto(
                workflow.Stats.SourceFileCount,
                workflow.Stats.OutputFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ShinyRateWorkflowDto ToShinyRateWorkflowDto(SwShShinyRateWorkflow workflow)
    {
        return new ShinyRateWorkflowDto(
            ToDto(workflow.Summary),
            workflow.InstallStatus,
            workflow.InstallMessage,
            workflow.BuildId,
            workflow.FunctionOffsetHex,
            workflow.CompareOffsetHex,
            workflow.BreakOffsetHex,
            ToProjectGameDto(workflow.DetectedGame),
            workflow.Source is null ? null : ToDto(workflow.Source),
            ToDto(workflow.RateRule),
            workflow.Presets.Select(ToDto).ToArray(),
            new ShinyRateWorkflowStatsDto(
                workflow.Stats.SourceFileCount,
                workflow.Stats.OutputFileCount,
                workflow.Stats.PresetCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static TypeChartWorkflowDto ToTypeChartWorkflowDto(SwShTypeChartWorkflow workflow)
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

    private static FairyGymBoostsWorkflowDto ToFairyGymBoostsWorkflowDto(
        SwShFairyGymBoostsWorkflow workflow)
    {
        return new FairyGymBoostsWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Trainers.Select(ToDto).ToArray(),
            workflow.Sources.Select(ToDto).ToArray(),
            new FairyGymBoostsWorkflowStatsDto(
                workflow.Stats.TrainerCount,
                workflow.Stats.BoostCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static FashionUnlockWorkflowDto ToFashionUnlockWorkflowDto(SwShFashionUnlockWorkflow workflow)
    {
        return new FashionUnlockWorkflowDto(
            ToDto(workflow.Summary),
            workflow.InstallStatus,
            workflow.InstallMessage,
            "swsh",
            workflow.BuildId,
            workflow.DirectGetterOffsetHex,
            workflow.MappedGetterOffsetHex,
            string.Empty,
            workflow.StubKind,
            ToProjectGameDto(workflow.DetectedGame),
            workflow.ReservedRegions.Select(ToDto).ToArray(),
            ToDto(workflow.Provenance),
            new FashionUnlockWorkflowStatsDto(
                workflow.Stats.ReservedMainTextRegionCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static IvScreenWorkflowDto ToIvScreenWorkflowDto(SwShIvScreenWorkflow workflow)
    {
        return new IvScreenWorkflowDto(
            ToDto(workflow.Summary),
            workflow.InstallStatus,
            workflow.InstallMessage,
            workflow.Marker,
            workflow.BuildId,
            workflow.DetectedGame is null
                ? null
                : ProjectBridgeMapper.ToDto(workflow.DetectedGame.Value),
            workflow.PrimaryValueSourceOffsetHex,
            workflow.XToggleRefreshOffsetHex,
            workflow.RawIvGetterOffsetHex,
            workflow.HyperTrainingWrapperOffsetHex,
            workflow.CanUninstall,
            workflow.ReservedRegions.Select(ToDto).ToArray(),
            ToDto(workflow.Provenance),
            new IvScreenWorkflowStatsDto(
                workflow.Stats.ReservedMainTextRegionCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static GymUniformRemovalWorkflowDto ToGymUniformRemovalWorkflowDto(SwShGymUniformRemovalWorkflow workflow)
    {
        return new GymUniformRemovalWorkflowDto(
            ToDto(workflow.Summary),
            workflow.InstallStatus,
            workflow.InstallMessage,
            workflow.BuildId,
            workflow.PatchOffsetHex,
            workflow.StubKind,
            workflow.ReservedRegions.Select(ToDto).ToArray(),
            ToDto(workflow.Provenance),
            new GymUniformRemovalWorkflowStatsDto(
                workflow.Stats.ReservedMainTextRegionCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static RoyalCandyWorkflowDto ToRoyalCandyWorkflowDto(SwShRoyalCandyWorkflow workflow)
    {
        return new RoyalCandyWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Workflows.Select(ToDto).ToArray(),
            workflow.Checks.Select(ToDto).ToArray(),
            workflow.Outputs.Select(ToDto).ToArray(),
            new RoyalCandyWorkflowStatsDto(
                workflow.Stats.TotalWorkflowCount,
                workflow.Stats.TotalStepCount,
                workflow.Stats.TotalCheckCount,
                workflow.Stats.PassCount,
                workflow.Stats.WarningCount,
                workflow.Stats.FailCount,
                workflow.Stats.OutputCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static StartingItemsWorkflowDto ToStartingItemsWorkflowDto(SwShStartingItemsWorkflow workflow)
    {
        return new StartingItemsWorkflowDto(
            ToDto(workflow.Summary),
            workflow.InstallStatus,
            workflow.InstallMessage,
            workflow.BlockerKind,
            workflow.Grants.Select(ToDto).ToArray(),
            workflow.ItemOptions.Select(ToDto).ToArray(),
            new StartingItemsWorkflowStatsDto(
                workflow.Stats.TotalGrantSlotCount,
                workflow.Stats.OccupiedGrantSlotCount,
                workflow.Stats.ItemOptionCount,
                workflow.Stats.SourceFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static NpcItemGiftWorkflowDto ToNpcItemGiftWorkflowDto(SwShNpcItemGiftWorkflow workflow)
    {
        return new NpcItemGiftWorkflowDto(
            ToDto(workflow.Summary),
            workflow.Npcs.Select(ToDto).ToArray(),
            workflow.Sources.Select(ToDto).ToArray(),
            workflow.ItemOptions.Select(ToDto).ToArray(),
            new NpcItemGiftWorkflowStatsDto(
                workflow.Stats.NpcCount,
                workflow.Stats.GiftCount,
                workflow.Stats.SourceFileCount,
                workflow.Stats.ItemOptionCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static SpreadsheetImportWorkflowDto ToSpreadsheetImportWorkflowDto(
        SwShSpreadsheetImportWorkflow workflow)
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

    private static ModMergerWorkflowDto ToModMergerWorkflowDto(SwShModMergerWorkflow workflow)
    {
        return new ModMergerWorkflowDto(
            ToDto(workflow.Summary),
            workflow.ModDirectory1,
            workflow.ModDirectory2,
            workflow.OutputRootPath,
            workflow.Directory1Files.Select(ToDto).ToArray(),
            workflow.Directory2Files.Select(ToDto).ToArray(),
            new ModMergerWorkflowStatsDto(
                workflow.Stats.Directory1FileCount,
                workflow.Stats.Directory2FileCount,
                workflow.Stats.MatchingFileCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ModMergerFileRecordDto ToDto(SwShModMergerFileRecord file)
    {
        return new ModMergerFileRecordDto(
            file.RelativePath,
            file.Name,
            file.Size,
            file.SupportKind,
            file.Status);
    }

    private static ModMergerPreviewDto ToDto(SwShModMergerPreview preview)
    {
        return new ModMergerPreviewDto(
            preview.CanApply,
            preview.Status,
            preview.MergeMode,
            preview.ReviewToken,
            preview.SelectedFileCount,
            preview.ReadyFileCount,
            preview.ConflictFileCount,
            preview.UnresolvedConflictCount,
            preview.Files.Select(ToDto).ToArray(),
            preview.Conflicts.Select(ToDto).ToArray(),
            preview.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ModMergerFilePreviewRecordDto ToDto(SwShModMergerFilePreviewRecord file)
    {
        return new ModMergerFilePreviewRecordDto(
            file.RelativePath,
            file.OutputRelativePath,
            file.SupportKind,
            file.Status,
            file.MergeKind,
            file.Summary,
            file.Directory1ChangeCount,
            file.Directory2ChangeCount,
            file.ConflictCount);
    }

    private static ModMergerConflictRecordDto ToDto(SwShModMergerConflictRecord conflict)
    {
        return new ModMergerConflictRecordDto(
            conflict.ConflictId,
            conflict.RelativePath,
            conflict.Label,
            conflict.Description,
            conflict.Directory1Value,
            conflict.Directory2Value,
            conflict.Resolution);
    }

    private static WorkflowSummaryDto ToDto(SwShWorkflowSummary summary)
    {
        return new WorkflowSummaryDto(
            summary.Id,
            summary.Label,
            summary.Description,
            ToDto(summary.Availability),
            summary.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ItemRecordDto ToDto(SwShItemRecord item)
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

    private static ItemDetailGroupDto ToDto(SwShItemDetailGroup group)
    {
        return new ItemDetailGroupDto(
            group.Label,
            group.Details.Select(ToDto).ToArray());
    }

    private static ItemDetailDto ToDto(SwShItemDetail detail)
    {
        return new ItemDetailDto(detail.Label, detail.Value);
    }

    private static ItemEditableFieldDto ToDto(SwShItemEditableField field)
    {
        return new ItemEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(option => new ItemEditableFieldOptionDto(option.Value, option.Label)).ToArray(),
            field.IsReadOnly,
            field.ReadOnlyReason);
    }

    private static PokemonRecordDto ToDto(SwShPokemonRecord pokemon)
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
                pokemon.Personal.CanNotDynamax,
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

    private static PokemonEditableFieldDto ToDto(SwShPokemonEditableField field)
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

    private static PokemonEditableFieldOptionDto ToDto(SwShPokemonEditableFieldOption option)
    {
        return new PokemonEditableFieldOptionDto(option.Value, option.Label);
    }

    private static PokemonEvolutionRecordDto ToDto(SwShPokemonEvolutionRecord evolution)
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

    private static PokemonEvolutionMethodOptionDto ToDto(SwShPokemonEvolutionMethodOption option)
    {
        return new PokemonEvolutionMethodOptionDto(
            option.Value,
            option.Label,
            option.ArgumentKind,
            option.ArgumentLabel,
            option.ArgumentOptions.Select(ToDto).ToArray());
    }

    private static PokemonLearnsetMoveDto ToDto(SwShPokemonLearnsetMove learnsetMove)
    {
        return new PokemonLearnsetMoveDto(
            learnsetMove.Slot,
            learnsetMove.MoveId,
            learnsetMove.MoveName,
            learnsetMove.Level);
    }

    private static PokemonCompatibilityGroupDto ToDto(SwShPokemonCompatibilityGroup group)
    {
        return new PokemonCompatibilityGroupDto(
            group.GroupId,
            group.Label,
            group.EnabledCount,
            group.Entries.Select(ToDto).ToArray());
    }

    private static PokemonCompatibilityEntryDto ToDto(SwShPokemonCompatibilityEntry entry)
    {
        return new PokemonCompatibilityEntryDto(
            entry.Slot,
            entry.MoveId,
            entry.MoveName,
            entry.Label,
            entry.CanLearn);
    }

    private static MoveRecordDto ToDto(SwShMoveRecord move)
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

    private static MoveEditableFieldDto ToDto(SwShMoveEditableField field)
    {
        return new MoveEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static MoveEditableFieldOptionDto ToDto(SwShMoveEditableFieldOption option)
    {
        return new MoveEditableFieldOptionDto(option.Value, option.Label);
    }

    private static MoveStatChangeRecordDto ToDto(SwShMoveStatChangeRecord statChange)
    {
        return new MoveStatChangeRecordDto(
            statChange.Slot,
            statChange.Stat,
            statChange.StatName,
            statChange.Stage,
            statChange.Percent);
    }

    private static MoveFlagRecordDto ToDto(SwShMoveFlagRecord flag)
    {
        return new MoveFlagRecordDto(
            flag.Field,
            flag.Label,
            flag.Enabled);
    }

    private static MoveProvenanceDto ToDto(SwShMoveProvenance provenance)
    {
        return new MoveProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static TextEntryRecordDto ToDto(SwShTextEntryRecord entry)
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

    private static TextEditableFieldDto ToDto(SwShTextEditableField field)
    {
        return new TextEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumLength,
            field.MaximumLength);
    }

    private static DialogueReferenceRecordDto ToDto(SwShDialogueReferenceRecord reference)
    {
        return new DialogueReferenceRecordDto(
            reference.DialogueId,
            reference.Label,
            reference.TextId,
            reference.Context,
            reference.Preview,
            ToDto(reference.Provenance));
    }

    private static TextProvenanceDto ToDto(SwShTextProvenance provenance)
    {
        return new TextProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static TrainerRecordDto ToDto(SwShTrainerRecord trainer)
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
            ToDto(trainer.Provenance));
    }

    private static TrainerPokemonRecordDto ToDto(SwShTrainerPokemonRecord pokemon)
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
            pokemon.DynamaxLevel,
            pokemon.CanGigantamax,
            ToDto(pokemon.Ivs),
            pokemon.Shiny,
            pokemon.CanDynamax,
            null,
            null)
        {
            AbilityOptions = pokemon.AbilityOptions.Select(ToDto).ToArray(),
            BaseStats = pokemon.BaseStats is null ? null : ToDto(pokemon.BaseStats),
            SpriteName = pokemon.SpriteName,
        };
    }

    private static TrainerAiFlagStateDto ToDto(SwShTrainerAiFlagState flag)
    {
        return new TrainerAiFlagStateDto(
            flag.Bit,
            flag.Mask,
            flag.Label,
            flag.Description,
            flag.Enabled);
    }

    private static TrainerPokemonStatsDto ToDto(SwShTrainerPokemonStatsRecord stats)
    {
        return new TrainerPokemonStatsDto(
            stats.HP,
            stats.Attack,
            stats.Defense,
            stats.SpecialAttack,
            stats.SpecialDefense,
            stats.Speed);
    }

    private static TrainerEditableFieldDto ToDto(SwShTrainerEditableField field)
    {
        return new TrainerEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static TrainerEditableFieldOptionDto ToDto(SwShTrainerEditableFieldOption option)
    {
        return new TrainerEditableFieldOptionDto(option.Value, option.Label);
    }

    private static TrainerProvenanceDto ToDto(SwShTrainerProvenance provenance)
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

    private static ShopRecordDto ToDto(SwShShopRecord shop)
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
            shop.Inventory
                .Select(item => ToDto(item) with { CanEditPrice = shop.GlobalPriceField is not null })
                .ToArray(),
            ToDto(shop.Provenance))
        {
            GlobalPriceField = shop.GlobalPriceField,
        };
    }

    private static ShopInventoryRecordDto ToDto(SwShShopInventoryRecord inventoryItem)
    {
        return new ShopInventoryRecordDto(
            inventoryItem.Slot,
            inventoryItem.ItemId,
            inventoryItem.ItemName,
            inventoryItem.Price,
            inventoryItem.IsKnownItem,
            inventoryItem.StockLimit);
    }

    private static ShopProvenanceDto ToDto(SwShShopProvenance provenance)
    {
        return new ShopProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static ShopEditableFieldDto ToDto(SwShShopEditableField field)
    {
        return new ShopEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static ShopEditableFieldOptionDto ToDto(SwShShopEditableFieldOption option)
    {
        return new ShopEditableFieldOptionDto(option.Value, option.Label, option.ItemName, option.Price)
        {
            Prices = option.Prices,
        };
    }

    private static EncounterTableRecordDto ToDto(SwShEncounterTableRecord table)
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
            LocationKey: table.LocationKey);
    }

    private static EncounterSlotRecordDto ToDto(SwShEncounterSlotRecord slot)
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

    private static EncounterEditableFieldDto ToDto(SwShEncounterEditableField field)
    {
        return new EncounterEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static EncounterEditableFieldOptionDto ToDto(SwShEncounterEditableFieldOption option)
    {
        return new EncounterEditableFieldOptionDto(option.Value, option.Label);
    }

    private static EncounterProvenanceDto ToDto(SwShEncounterProvenance provenance)
    {
        return new EncounterProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static RaidBattleTableRecordDto ToDto(SwShRaidBattleTableRecord table)
    {
        return new RaidBattleTableRecordDto(
            table.TableId,
            table.DisplayName,
            table.DenId,
            table.TableIndex,
            table.GameVersion,
            table.SourceTableHash,
            table.Slots.Select(ToDto).ToArray(),
            ToDto(table.Provenance));
    }

    private static RaidBattleSlotRecordDto ToDto(SwShRaidBattleSlotRecord slot)
    {
        return new RaidBattleSlotRecordDto(
            slot.Slot,
            slot.EntryIndex,
            slot.SpeciesId,
            slot.Species,
            slot.Form,
            slot.Ability,
            slot.AbilityLabel,
            slot.IsGigantamax,
            slot.Gender,
            slot.GenderLabel,
            slot.FlawlessIvs,
            slot.Probabilities,
            slot.ProbabilitySummary,
            slot.LevelTableHash,
            slot.DropTableHash,
            slot.BonusTableHash,
            ToDto(slot.DropRewardLink),
            ToDto(slot.BonusRewardLink))
        {
            AbilityOptions = slot.AbilityOptions.Select(ToDto).ToArray(),
            FormOptions = slot.FormOptions.Select(ToDto).ToArray(),
        };
    }

    private static RaidBattleRewardLinkDto ToDto(SwShRaidBattleRewardLinkRecord link)
    {
        return new RaidBattleRewardLinkDto(
            link.RewardKind,
            link.RewardKindLabel,
            link.TableId,
            link.SourceTableHash,
            link.IsMatched,
            link.RewardItemCount,
            link.Preview);
    }

    private static RaidBattleEditableFieldDto ToDto(SwShRaidBattleEditableField field)
    {
        return new RaidBattleEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static RaidBattleEditableFieldOptionDto ToDto(SwShRaidBattleEditableFieldOption option)
    {
        return new RaidBattleEditableFieldOptionDto(option.Value, option.Label);
    }

    private static RaidBattleProvenanceDto ToDto(SwShRaidBattleProvenance provenance)
    {
        return new RaidBattleProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static RaidRewardTableRecordDto ToDto(SwShRaidRewardTableRecord table)
    {
        return new RaidRewardTableRecordDto(
            table.TableId,
            table.DisplayName,
            table.DenId,
            table.Rank,
            table.GameVersion,
            table.RewardKind,
            table.RewardKindLabel,
            table.ArchiveMember,
            table.TableIndex,
            table.SourceTableHash,
            table.Rewards.Select(ToDto).ToArray(),
            ToDto(table.Provenance));
    }

    private static RaidRewardItemRecordDto ToDto(SwShRaidRewardItemRecord reward)
    {
        return new RaidRewardItemRecordDto(
            reward.Slot,
            reward.EntryId,
            reward.ItemId,
            reward.ItemName,
            reward.Quantity,
            reward.Weight,
            reward.Values);
    }

    private static RaidRewardEditableFieldDto ToDto(SwShRaidRewardEditableField field)
    {
        return new RaidRewardEditableFieldDto(
            field.Field,
            field.Label,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.Options.Select(ToDto).ToArray());
    }

    private static RaidRewardEditableFieldOptionDto ToDto(SwShRaidRewardEditableFieldOption option)
    {
        return new RaidRewardEditableFieldOptionDto(option.Value, option.Label);
    }

    private static RaidRewardProvenanceDto ToDto(SwShRaidRewardProvenance provenance)
    {
        return new RaidRewardProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static PlacedObjectRecordDto ToDto(SwShPlacedObjectRecord placedObject)
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
            Fields: placedObject.Fields?.Select(ToDto).ToArray() ?? Array.Empty<PlacementFieldValueDto>());
    }

    private static PlacementFieldValueDto ToDto(SwShPlacementFieldValue field)
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

    private static PlacementCategoryDto ToDto(SwShPlacementCategory category)
    {
        return new PlacementCategoryDto(
            category.Id,
            category.Label,
            category.Description,
            category.ObjectCount);
    }

    private static PlacementEditableFieldDto ToDto(SwShPlacementEditableField field)
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

    private static PlacementEditableFieldOptionDto ToDto(SwShPlacementEditableFieldOption option)
    {
        return new PlacementEditableFieldOptionDto(option.Value, option.Label);
    }

    private static PlacementProvenanceDto ToDto(SwShPlacementProvenance provenance)
    {
        return new PlacementProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static BehaviorEntryRecordDto ToDto(SwShBehaviorEntryRecord entry)
    {
        return new BehaviorEntryRecordDto(
            entry.EntryId,
            entry.Index,
            entry.Label,
            entry.SpeciesId,
            entry.SpeciesName,
            entry.Form,
            entry.Behavior,
            entry.BehaviorLabel,
            entry.ModelPart,
            entry.HitboxRadius,
            entry.GrassShakeRadius,
            entry.Hash1,
            entry.Hash2,
            entry.InternalSpeciesName,
            entry.Fields.Select(ToDto).ToArray(),
            ToDto(entry.Provenance),
            entry.FormOptions.Select(ToDto).ToArray());
    }

    private static BehaviorFieldDto ToDto(SwShBehaviorField field)
    {
        return new BehaviorFieldDto(
            field.Field,
            field.Label,
            field.Group,
            field.ValueKind,
            field.MinimumValue,
            field.MaximumValue,
            field.IsReadOnly,
            field.Description,
            field.Options.Select(ToDto).ToArray());
    }

    private static BehaviorFieldOptionDto ToDto(SwShBehaviorFieldOption option)
    {
        return new BehaviorFieldOptionDto(option.Value, option.Label);
    }

    private static BehaviorFieldValueDto ToDto(SwShBehaviorFieldValue value)
    {
        return new BehaviorFieldValueDto(value.Field, value.Value);
    }

    private static BehaviorProvenanceDto ToDto(SwShBehaviorProvenance provenance)
    {
        return new BehaviorProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static FlagRecordDto ToDto(SwShFlagRecord flag)
    {
        return new FlagRecordDto(
            flag.FlagId,
            flag.Name,
            flag.Category,
            flag.Kind,
            flag.ValueKind,
            flag.DefaultValue,
            flag.Description,
            flag.Table,
            flag.Index,
            flag.Hash,
            flag.Low32Key,
            ToDto(flag.Provenance));
    }

    private static SaveBlockRecordDto ToDto(SwShSaveBlockRecord saveBlock)
    {
        return new SaveBlockRecordDto(
            saveBlock.BlockId,
            saveBlock.Name,
            saveBlock.Key,
            saveBlock.Hash,
            saveBlock.Kind,
            saveBlock.ValueKind,
            saveBlock.Description,
            ToDto(saveBlock.Provenance));
    }

    private static SaveFileRecordDto ToDto(SwShSaveFileRecord saveFile)
    {
        return new SaveFileRecordDto(
            saveFile.FileName,
            saveFile.SizeBytes,
            saveFile.Sha256,
            saveFile.Status,
            saveFile.Description);
    }

    private static FlagworkSaveProvenanceDto ToDto(SwShFlagworkSaveProvenance provenance)
    {
        return new FlagworkSaveProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static ExeFsPatchRecordDto ToDto(SwShExeFsPatchRecord patch)
    {
        return new ExeFsPatchRecordDto(
            patch.PatchId,
            patch.Name,
            patch.TargetFile,
            patch.PatchKind,
            patch.Status,
            patch.Description,
            patch.Details,
            ToDto(patch.Provenance));
    }

    private static ExeFsSegmentRecordDto ToDto(SwShExeFsSegmentRecord segment)
    {
        return new ExeFsSegmentRecordDto(
            segment.SegmentId,
            segment.Name,
            segment.FileOffset,
            segment.MemoryOffset,
            segment.DecompressedSize,
            segment.CompressedSize,
            segment.Sha256,
            segment.HashStatus,
            ToDto(segment.Provenance));
    }

    private static ExeFsPatchCheckRecordDto ToDto(SwShExeFsPatchCheckRecord check)
    {
        return new ExeFsPatchCheckRecordDto(
            check.CheckId,
            check.PatchId,
            check.Status,
            check.Area,
            check.Offset,
            check.Name,
            check.Expected,
            check.Actual,
            check.Notes,
            ToDto(check.Provenance));
    }

    private static ExeFsPatchProvenanceDto ToDto(SwShExeFsPatchProvenance provenance)
    {
        return new ExeFsPatchProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static BagHookSlotRecordDto ToDto(SwShBagHookSlotRecord slot)
    {
        return new BagHookSlotRecordDto(
            slot.Slot,
            slot.Status,
            slot.IsReserved,
            slot.ReservedFor,
            slot.ItemId,
            slot.ItemName,
            slot.Quantity,
            slot.Owner,
            slot.Notes,
            ToDto(slot.Provenance));
    }

    private static BagHookProvenanceDto ToDto(SwShBagHookProvenance provenance)
    {
        return new BagHookProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static CatchCapRecordDto ToDto(SwShCatchCapRecord cap)
    {
        return new CatchCapRecordDto(
            cap.BadgeCount,
            cap.Label,
            cap.LevelCap,
            cap.MinimumLevelCap,
            cap.MaximumLevelCap);
    }

    private static CatchCapProvenanceDto ToDto(SwShCatchCapProvenance provenance)
    {
        return new CatchCapProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static HyperTrainingLevelRuleDto ToDto(SwShHyperTrainingLevelRule rule)
    {
        return new HyperTrainingLevelRuleDto(
            rule.MinimumLevel,
            rule.ScriptMinimumLevel,
            rule.RuntimeMinimumLevel,
            rule.DialogueMinimumLevel,
            rule.LevelsMatch,
            rule.VanillaMinimumLevel,
            rule.MinimumAllowedLevel,
            rule.MaximumAllowedLevel,
            rule.ScriptCell,
            rule.DialogueSummary,
            rule.RuntimeSummary);
    }

    private static HyperTrainingSourceRecordDto ToDto(SwShHyperTrainingSourceRecord source)
    {
        return new HyperTrainingSourceRecordDto(
            source.SourceId,
            source.Label,
            source.RelativePath,
            source.Status,
            ToDto(source.Provenance));
    }

    private static HyperTrainingProvenanceDto ToDto(SwShHyperTrainingProvenance provenance)
    {
        return new HyperTrainingProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static ShinyRateSourceRecordDto ToDto(SwShShinyRateSourceRecord source)
    {
        return new ShinyRateSourceRecordDto(
            source.SourceId,
            source.Label,
            source.RelativePath,
            source.Status,
            ToDto(source.Provenance));
    }

    private static ShinyRateRuleDto ToDto(SwShShinyRateRule rule)
    {
        return new ShinyRateRuleDto(
            rule.Mode,
            rule.RollCount,
            rule.MinimumRollCount,
            rule.MaximumRollCount,
            rule.MinimumCustomDenominator,
            rule.MaximumCustomDenominator,
            rule.OddsDenominator,
            rule.ChancePercent,
            rule.OddsLabel,
            rule.PercentLabel,
            rule.RuntimeSummary);
    }

    private static ShinyRatePresetDto ToDto(SwShShinyRatePreset preset)
    {
        return new ShinyRatePresetDto(
            preset.PresetId,
            preset.Label,
            preset.Mode,
            preset.RollCount,
            preset.TargetDenominator,
            preset.IsEnabled,
            preset.OddsLabel,
            preset.PercentLabel,
            preset.Description);
    }

    private static ShinyRateProvenanceDto ToDto(SwShShinyRateProvenance provenance)
    {
        return new ShinyRateProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static TypeChartSourceRecordDto ToDto(SwShTypeChartSourceRecord source)
    {
        return new TypeChartSourceRecordDto(
            source.SourceId,
            source.Label,
            source.RelativePath,
            source.Status,
            ToDto(source.Provenance));
    }

    private static TypeChartProvenanceDto ToDto(SwShTypeChartProvenance provenance)
    {
        return new TypeChartProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static TypeChartTypeDefinitionDto ToDto(SwShTypeChartTypeDefinition type)
    {
        return new TypeChartTypeDefinitionDto(
            type.TypeIndex,
            type.Label,
            type.ShortLabel,
            type.Color);
    }

    private static TypeChartCellDto ToDto(SwShTypeChartCell cell)
    {
        return new TypeChartCellDto(
            cell.AttackTypeIndex,
            cell.DefenseTypeIndex,
            cell.Effectiveness,
            cell.VanillaEffectiveness);
    }

    private static FairyGymBoostsSourceRecordDto ToDto(SwShFairyGymBoostsSourceRecord source)
    {
        return new FairyGymBoostsSourceRecordDto(
            source.SourceId,
            source.Label,
            source.RelativePath,
            source.Status,
            ToDto(source.Provenance));
    }

    private static FairyGymBoostsProvenanceDto ToDto(SwShFairyGymBoostsProvenance provenance)
    {
        return new FairyGymBoostsProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static FairyGymBoostTrainerDto ToDto(SwShFairyGymBoostTrainer trainer)
    {
        return new FairyGymBoostTrainerDto(
            trainer.TrainerId,
            trainer.NpcName,
            trainer.DisplayOrder,
            trainer.Boosts.Select(ToDto).ToArray());
    }

    private static FairyGymBoostRecordDto ToDto(SwShFairyGymBoostRecord boost)
    {
        return new FairyGymBoostRecordDto(
            boost.BoostId,
            boost.SequenceFile,
            boost.AnswerChoice,
            boost.AnswerText,
            boost.QuestionText,
            boost.DefaultEffectId,
            boost.DefaultResultKind,
            boost.ResultKind,
            boost.EffectId,
            boost.EffectLabel,
            boost.StageAmount,
            boost.AffectedStats);
    }

    private static IvScreenReservedRegionDto ToDto(SwShIvScreenReservedRegion region)
    {
        return new IvScreenReservedRegionDto(
            region.RegionId,
            region.Label,
            region.OffsetLabel,
            region.StartOffset,
            region.Length,
            region.Rule);
    }

    private static IvScreenProvenanceDto ToDto(SwShIvScreenProvenance provenance)
    {
        return new IvScreenProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static GymUniformRemovalReservedRegionDto ToDto(SwShGymUniformRemovalReservedRegion region)
    {
        return new GymUniformRemovalReservedRegionDto(
            region.RegionId,
            region.Label,
            region.OffsetLabel,
            region.StartOffset,
            region.Length,
            region.Rule);
    }

    private static FashionUnlockReservedRegionDto ToDto(SwShFashionUnlockReservedRegion region)
    {
        return new FashionUnlockReservedRegionDto(
            region.RegionId,
            region.Label,
            region.OffsetLabel,
            region.StartOffset,
            region.Length,
            region.Rule);
    }

    private static GymUniformRemovalProvenanceDto ToDto(SwShGymUniformRemovalProvenance provenance)
    {
        return new GymUniformRemovalProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static FashionUnlockProvenanceDto ToDto(SwShFashionUnlockProvenance provenance)
    {
        return new FashionUnlockProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static ProjectGameDto? ToProjectGameDto(ProjectGame? game)
    {
        return game switch
        {
            ProjectGame.Sword => ProjectGameDto.Sword,
            ProjectGame.Shield => ProjectGameDto.Shield,
            ProjectGame.Scarlet => ProjectGameDto.Scarlet,
            ProjectGame.Violet => ProjectGameDto.Violet,
            null => null,
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
        };
    }

    private static RoyalCandyWorkflowRecordDto ToDto(SwShRoyalCandyWorkflowRecord workflow)
    {
        return new RoyalCandyWorkflowRecordDto(
            workflow.WorkflowId,
            workflow.Name,
            workflow.Category,
            workflow.Target,
            workflow.Mode,
            workflow.ItemId,
            workflow.TemplateItemId,
            workflow.Status,
            workflow.Description,
            workflow.LevelCaps.Select(ToDto).ToArray(),
            workflow.Steps.Select(ToDto).ToArray(),
            ToDto(workflow.Provenance));
    }

    private static RoyalCandyLevelCapRecordDto ToDto(SwShRoyalCandyLevelCapRecord levelCap)
    {
        return new RoyalCandyLevelCapRecordDto(
            levelCap.Slot,
            levelCap.MilestoneId,
            levelCap.Label,
            levelCap.LevelCap,
            levelCap.MinimumLevelCap,
            levelCap.MaximumLevelCap,
            levelCap.ProgressKind,
            levelCap.ProgressHash,
            levelCap.WorkMinimum);
    }

    private static RoyalCandyWorkflowCheckRecordDto ToDto(SwShRoyalCandyWorkflowCheckRecord check)
    {
        return new RoyalCandyWorkflowCheckRecordDto(
            check.CheckId,
            check.WorkflowId,
            check.Status,
            check.Area,
            check.Target,
            check.Message,
            ToDto(check.Provenance));
    }

    private static RoyalCandyOutputRecordDto ToDto(SwShRoyalCandyOutputRecord output)
    {
        return new RoyalCandyOutputRecordDto(
            output.OutputId,
            output.WorkflowId,
            output.RelativePath,
            output.SourceFile,
            output.OutputKind,
            output.Status,
            output.Description,
            ToDto(output.Provenance));
    }

    private static RoyalCandyWorkflowStepRecordDto ToDto(SwShRoyalCandyWorkflowStepRecord step)
    {
        return new RoyalCandyWorkflowStepRecordDto(
            step.Step,
            step.Label,
            step.Description);
    }

    private static RoyalCandyProvenanceDto ToDto(SwShRoyalCandyProvenance provenance)
    {
        return new RoyalCandyProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static StartingItemGrantRecordDto ToDto(SwShStartingItemGrantRecord grant)
    {
        return new StartingItemGrantRecordDto(
            grant.Slot,
            grant.ItemId,
            grant.ItemName,
            grant.Quantity,
            grant.IsKeyItem,
            grant.Status,
            grant.Owner,
            ToDto(grant.Provenance));
    }

    private static StartingItemOptionRecordDto ToDto(SwShStartingItemOptionRecord option)
    {
        return new StartingItemOptionRecordDto(
            option.ItemId,
            option.Name,
            option.Category,
            option.IsKeyItem);
    }

    private static StartingItemsProvenanceDto ToDto(SwShStartingItemsProvenance provenance)
    {
        return new StartingItemsProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static NpcItemGiftNpcGroupDto ToDto(SwShNpcItemGiftNpcGroup npc)
    {
        return new NpcItemGiftNpcGroupDto(
            npc.NpcId,
            npc.NpcName,
            npc.DisplayOrder,
            npc.Gifts.Select(ToDto).ToArray());
    }

    private static NpcItemGiftRecordDto ToDto(SwShNpcItemGiftRecord gift)
    {
        return new NpcItemGiftRecordDto(
            gift.GiftId,
            gift.NpcId,
            gift.NpcName,
            gift.Label,
            gift.Location,
            gift.DisplayOrder,
            gift.RelativePath,
            gift.Status,
            gift.Quantity,
            gift.VanillaQuantity,
            gift.QuantityCell,
            gift.CanEditQuantity,
            gift.Items.Select(ToDto).ToArray(),
            ToDto(gift.Provenance));
    }

    private static NpcItemGiftItemSlotRecordDto ToDto(SwShNpcItemGiftItemSlotRecord item)
    {
        return new NpcItemGiftItemSlotRecordDto(
            item.SlotId,
            item.Label,
            item.ItemId,
            item.ItemName,
            item.VanillaItemId,
            item.VanillaItemName,
            item.ItemCell);
    }

    private static NpcItemGiftSourceRecordDto ToDto(SwShNpcItemGiftSourceRecord source)
    {
        return new NpcItemGiftSourceRecordDto(
            source.SourceId,
            source.Label,
            source.RelativePath,
            source.Status,
            ToDto(source.Provenance));
    }

    private static NpcItemGiftItemOptionRecordDto ToDto(SwShNpcItemGiftItemOptionRecord option)
    {
        return new NpcItemGiftItemOptionRecordDto(
            option.ItemId,
            option.Name,
            option.Category,
            option.IsKeyItem);
    }

    private static NpcItemGiftProvenanceDto ToDto(SwShNpcItemGiftProvenance provenance)
    {
        return new NpcItemGiftProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static SpreadsheetImportProfileRecordDto ToDto(
        SwShSpreadsheetImportProfileRecord profile)
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

    private static SpreadsheetImportColumnRecordDto ToDto(
        SwShSpreadsheetImportColumnRecord column)
    {
        return new SpreadsheetImportColumnRecordDto(
            column.Column,
            column.Header,
            column.ValueKind,
            column.IsRequired,
            column.Description);
    }

    private static SpreadsheetImportProvenanceDto ToDto(SwShSpreadsheetImportProvenance provenance)
    {
        return new SpreadsheetImportProvenanceDto(
            provenance.SourceFile,
            ProjectBridgeMapper.ToDto(provenance.SourceLayer),
            ProjectBridgeMapper.ToDto(provenance.FileState));
    }

    private static SpreadsheetImportPreviewDto ToDto(SwShSpreadsheetImportPreview preview)
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

    private static SpreadsheetImportRowPreviewRecordDto ToDto(
        SwShSpreadsheetImportRowPreviewRecord row)
    {
        return new SpreadsheetImportRowPreviewRecordDto(
            row.RowNumber,
            row.RecordId,
            row.Status,
            row.Summary,
            row.Cells.Select(ToDto).ToArray(),
            row.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static SpreadsheetImportCellPreviewRecordDto ToDto(
        SwShSpreadsheetImportCellPreviewRecord cell)
    {
        return new SpreadsheetImportCellPreviewRecordDto(
            cell.Header,
            cell.Field,
            cell.Value,
            cell.Status,
            cell.Message);
    }

    private static WorkflowAvailabilityDto ToDto(SwShWorkflowAvailability availability)
    {
        return availability switch
        {
            SwShWorkflowAvailability.Disabled => WorkflowAvailabilityDto.Disabled,
            SwShWorkflowAvailability.ReadOnly => WorkflowAvailabilityDto.ReadOnly,
            SwShWorkflowAvailability.Available => WorkflowAvailabilityDto.Available,
            _ => throw new ArgumentOutOfRangeException(nameof(availability), availability, null),
        };
    }
}
