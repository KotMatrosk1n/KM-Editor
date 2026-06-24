// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Bridge;
using KM.Api.BagHook;
using KM.Api.Behavior;
using KM.Api.CatchCap;
using KM.Api.DynamaxAdventures;
using KM.Api.Editing;
using KM.Api.Encounters;
using KM.Api.ExeFs;
using KM.Api.FairyGymBoosts;
using KM.Api.FashionUnlock;
using KM.Api.Flagwork;
using KM.Api.FpsPatch;
using KM.Api.GameDump;
using KM.Api.Gifts;
using KM.Api.GymUniformRemoval;
using KM.Api.HyperspaceBypass;
using KM.Api.HyperTraining;
using KM.Api.Items;
using KM.Api.IvScreen;
using KM.Api.ModMerger;
using KM.Api.Moves;
using KM.Api.NpcItemGift;
using KM.Api.Placement;
using KM.Api.Pokemon;
using KM.Api.Projects;
using KM.Api.Raids;
using KM.Api.Randomizer;
using KM.Api.Rentals;
using KM.Api.RoyalCandy;
using KM.Api.Shops;
using KM.Api.ShinyRate;
using KM.Api.SpreadsheetImport;
using KM.Api.StartingItems;
using KM.Api.StaticEncounters;
using KM.Api.SvCache;
using KM.Api.Text;
using KM.Api.Trainers;
using KM.Api.Trades;
using KM.Api.TypeChart;
using KM.Api.Workflows;
using KM.Api.ZaCache;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.GameDump;
using KM.Core.Projects;
using KM.SwSh.Behavior;
using KM.SwSh.BagHook;
using KM.SwSh.CatchCap;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Encounters;
using KM.SwSh.ExeFs;
using KM.SwSh.FairyGymBoosts;
using KM.SwSh.FashionUnlock;
using KM.SwSh.Gifts;
using KM.SwSh.FpsPatch;
using KM.SwSh.GameDump;
using KM.SwSh.GymUniformRemoval;
using KM.SwSh.HyperTraining;
using KM.SwSh.Items;
using KM.SwSh.IvScreen;
using KM.SwSh.ModMerger;
using KM.SwSh.Moves;
using KM.SwSh.NpcItemGift;
using KM.SwSh.Placement;
using KM.SwSh.Pokemon;
using KM.SwSh.Raids;
using KM.SwSh.Randomizer;
using KM.SwSh.Rentals;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Shops;
using KM.SwSh.ShinyRate;
using KM.SwSh.SpreadsheetImport;
using KM.SwSh.StartingItems;
using KM.SwSh.StaticEncounters;
using KM.SwSh.Text;
using KM.SwSh.Trainers;
using KM.SwSh.Trades;
using KM.SwSh.TypeChart;
using KM.SwSh.Workflows;
using KM.SV.ModMerger;
using KM.SV.GameDump;
using KM.ZA.GameDump;
using KM.SV.Workflows;
using KM.ZA.Workflows;
using System.Globalization;
using System.Text.Json;

namespace KM.Tools.Bridge;

public sealed class ProjectBridgeDispatcher
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShDynamaxAdventuresEditSessionService dynamaxAdventuresEditSessionService;
    private readonly SwShDynamaxAdventureSeedPlanningService dynamaxAdventureSeedPlanningService;
    private readonly SwShDynamaxAdventureSaveSeedService dynamaxAdventureSaveSeedService;
    private readonly SwShEncountersEditSessionService encountersEditSessionService;
    private readonly SwShExeFsPatchEditSessionService exeFsPatchEditSessionService;
    private readonly SwShBagHookEditSessionService bagHookEditSessionService;
    private readonly SwShCatchCapEditSessionService catchCapEditSessionService;
    private readonly SwShHyperTrainingEditSessionService hyperTrainingEditSessionService;
    private readonly SwShShinyRateEditSessionService shinyRateEditSessionService;
    private readonly SwShFashionUnlockEditSessionService fashionUnlockEditSessionService;
    private readonly SwShFairyGymBoostsEditSessionService fairyGymBoostsEditSessionService;
    private readonly SwShGymUniformRemovalEditSessionService gymUniformRemovalEditSessionService;
    private readonly SwShIvScreenEditSessionService ivScreenEditSessionService;
    private readonly SwShTypeChartEditSessionService typeChartEditSessionService;
    private readonly SwShGiftPokemonEditSessionService giftPokemonEditSessionService;
    private readonly SwShItemsEditSessionService itemsEditSessionService;
    private readonly SwShMovesEditSessionService movesEditSessionService;
    private readonly SwShPlacementEditSessionService placementEditSessionService;
    private readonly SwShBehaviorEditSessionService behaviorEditSessionService;
    private readonly SwShPokemonEditSessionService pokemonEditSessionService;
    private readonly SwShRaidBattlesEditSessionService raidBattlesEditSessionService;
    private readonly SwShRaidRewardsEditSessionService raidRewardsEditSessionService;
    private readonly SwShRentalPokemonEditSessionService rentalPokemonEditSessionService;
    private readonly SwShRoyalCandyEditSessionService royalCandyEditSessionService;
    private readonly SwShStartingItemsEditSessionService startingItemsEditSessionService;
    private readonly SwShNpcItemGiftEditSessionService npcItemGiftEditSessionService;
    private readonly SwShShopsEditSessionService shopsEditSessionService;
    private readonly SwShSpreadsheetImportExecutionService spreadsheetImportExecutionService;
    private readonly SwShModMergerWorkflowService modMergerWorkflowService;
    private readonly SwShFpsPatchService fpsPatchService;
    private readonly SwShRandomizerService randomizerService;
    private readonly SwShGameDumpService swShGameDumpService;
    private readonly SvGameDumpService svGameDumpService;
    private readonly ZaGameDumpService zaGameDumpService;
    private readonly SwShStaticEncountersEditSessionService staticEncountersEditSessionService;
    private readonly SwShTextEditSessionService textEditSessionService;
    private readonly SwShTrainersEditSessionService trainersEditSessionService;
    private readonly SwShTradePokemonEditSessionService tradePokemonEditSessionService;
    private readonly SwShWorkflowService swShWorkflowService;
    private readonly SvWorkflowService svWorkflowService;
    private readonly ZaWorkflowService zaWorkflowService;

    public ProjectBridgeDispatcher(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShDynamaxAdventuresEditSessionService? dynamaxAdventuresEditSessionService = null,
        SwShDynamaxAdventureSeedPlanningService? dynamaxAdventureSeedPlanningService = null,
        SwShDynamaxAdventureSaveSeedService? dynamaxAdventureSaveSeedService = null,
        SwShEncountersEditSessionService? encountersEditSessionService = null,
        SwShExeFsPatchEditSessionService? exeFsPatchEditSessionService = null,
        SwShBagHookEditSessionService? bagHookEditSessionService = null,
        SwShCatchCapEditSessionService? catchCapEditSessionService = null,
        SwShHyperTrainingEditSessionService? hyperTrainingEditSessionService = null,
        SwShShinyRateEditSessionService? shinyRateEditSessionService = null,
        SwShFashionUnlockEditSessionService? fashionUnlockEditSessionService = null,
        SwShFairyGymBoostsEditSessionService? fairyGymBoostsEditSessionService = null,
        SwShGymUniformRemovalEditSessionService? gymUniformRemovalEditSessionService = null,
        SwShIvScreenEditSessionService? ivScreenEditSessionService = null,
        SwShTypeChartEditSessionService? typeChartEditSessionService = null,
        SwShGiftPokemonEditSessionService? giftPokemonEditSessionService = null,
        SwShItemsEditSessionService? itemsEditSessionService = null,
        SwShMovesEditSessionService? movesEditSessionService = null,
        SwShPlacementEditSessionService? placementEditSessionService = null,
        SwShBehaviorEditSessionService? behaviorEditSessionService = null,
        SwShPokemonEditSessionService? pokemonEditSessionService = null,
        SwShRaidBattlesEditSessionService? raidBattlesEditSessionService = null,
        SwShRaidRewardsEditSessionService? raidRewardsEditSessionService = null,
        SwShRentalPokemonEditSessionService? rentalPokemonEditSessionService = null,
        SwShRoyalCandyEditSessionService? royalCandyEditSessionService = null,
        SwShStartingItemsEditSessionService? startingItemsEditSessionService = null,
        SwShNpcItemGiftEditSessionService? npcItemGiftEditSessionService = null,
        SwShShopsEditSessionService? shopsEditSessionService = null,
        SwShSpreadsheetImportExecutionService? spreadsheetImportExecutionService = null,
        SwShModMergerWorkflowService? modMergerWorkflowService = null,
        SwShFpsPatchService? fpsPatchService = null,
        SwShRandomizerService? randomizerService = null,
        SwShGameDumpService? swShGameDumpService = null,
        SvGameDumpService? svGameDumpService = null,
        ZaGameDumpService? zaGameDumpService = null,
        SwShStaticEncountersEditSessionService? staticEncountersEditSessionService = null,
        SwShTextEditSessionService? textEditSessionService = null,
        SwShTrainersEditSessionService? trainersEditSessionService = null,
        SwShTradePokemonEditSessionService? tradePokemonEditSessionService = null,
        SwShWorkflowService? swShWorkflowService = null,
        SvWorkflowService? svWorkflowService = null,
        ZaWorkflowService? zaWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.dynamaxAdventuresEditSessionService = dynamaxAdventuresEditSessionService ?? new SwShDynamaxAdventuresEditSessionService(this.projectWorkspaceService);
        this.dynamaxAdventureSeedPlanningService = dynamaxAdventureSeedPlanningService ?? new SwShDynamaxAdventureSeedPlanningService(this.projectWorkspaceService);
        this.dynamaxAdventureSaveSeedService = dynamaxAdventureSaveSeedService ?? new SwShDynamaxAdventureSaveSeedService();
        this.encountersEditSessionService = encountersEditSessionService ?? new SwShEncountersEditSessionService(this.projectWorkspaceService);
        this.exeFsPatchEditSessionService = exeFsPatchEditSessionService ?? new SwShExeFsPatchEditSessionService(this.projectWorkspaceService);
        this.bagHookEditSessionService = bagHookEditSessionService ?? new SwShBagHookEditSessionService(this.projectWorkspaceService);
        this.catchCapEditSessionService = catchCapEditSessionService ?? new SwShCatchCapEditSessionService(this.projectWorkspaceService);
        this.hyperTrainingEditSessionService = hyperTrainingEditSessionService ?? new SwShHyperTrainingEditSessionService(this.projectWorkspaceService);
        this.shinyRateEditSessionService = shinyRateEditSessionService ?? new SwShShinyRateEditSessionService(this.projectWorkspaceService);
        this.fashionUnlockEditSessionService = fashionUnlockEditSessionService ?? new SwShFashionUnlockEditSessionService(this.projectWorkspaceService);
        this.fairyGymBoostsEditSessionService = fairyGymBoostsEditSessionService ?? new SwShFairyGymBoostsEditSessionService(this.projectWorkspaceService);
        this.gymUniformRemovalEditSessionService = gymUniformRemovalEditSessionService ?? new SwShGymUniformRemovalEditSessionService(this.projectWorkspaceService);
        this.ivScreenEditSessionService = ivScreenEditSessionService ?? new SwShIvScreenEditSessionService(this.projectWorkspaceService);
        this.typeChartEditSessionService = typeChartEditSessionService ?? new SwShTypeChartEditSessionService(this.projectWorkspaceService);
        this.giftPokemonEditSessionService = giftPokemonEditSessionService ?? new SwShGiftPokemonEditSessionService(this.projectWorkspaceService);
        this.itemsEditSessionService = itemsEditSessionService ?? new SwShItemsEditSessionService(this.projectWorkspaceService);
        this.movesEditSessionService = movesEditSessionService ?? new SwShMovesEditSessionService(this.projectWorkspaceService);
        this.placementEditSessionService = placementEditSessionService ?? new SwShPlacementEditSessionService(this.projectWorkspaceService);
        this.behaviorEditSessionService = behaviorEditSessionService ?? new SwShBehaviorEditSessionService(this.projectWorkspaceService);
        this.pokemonEditSessionService = pokemonEditSessionService ?? new SwShPokemonEditSessionService(this.projectWorkspaceService);
        this.raidBattlesEditSessionService = raidBattlesEditSessionService ?? new SwShRaidBattlesEditSessionService(this.projectWorkspaceService);
        this.raidRewardsEditSessionService = raidRewardsEditSessionService ?? new SwShRaidRewardsEditSessionService(this.projectWorkspaceService);
        this.rentalPokemonEditSessionService = rentalPokemonEditSessionService ?? new SwShRentalPokemonEditSessionService(this.projectWorkspaceService);
        this.royalCandyEditSessionService = royalCandyEditSessionService ?? new SwShRoyalCandyEditSessionService(this.projectWorkspaceService);
        this.startingItemsEditSessionService = startingItemsEditSessionService ?? new SwShStartingItemsEditSessionService(this.projectWorkspaceService);
        this.npcItemGiftEditSessionService = npcItemGiftEditSessionService ?? new SwShNpcItemGiftEditSessionService(this.projectWorkspaceService);
        this.shopsEditSessionService = shopsEditSessionService ?? new SwShShopsEditSessionService(this.projectWorkspaceService);
        this.spreadsheetImportExecutionService = spreadsheetImportExecutionService ?? new SwShSpreadsheetImportExecutionService(this.projectWorkspaceService);
        this.modMergerWorkflowService = modMergerWorkflowService ?? new SwShModMergerWorkflowService(this.projectWorkspaceService);
        this.fpsPatchService = fpsPatchService ?? new SwShFpsPatchService(this.projectWorkspaceService);
        this.randomizerService = randomizerService ?? new SwShRandomizerService(this.projectWorkspaceService);
        this.staticEncountersEditSessionService = staticEncountersEditSessionService ?? new SwShStaticEncountersEditSessionService(this.projectWorkspaceService);
        this.textEditSessionService = textEditSessionService ?? new SwShTextEditSessionService(this.projectWorkspaceService);
        this.trainersEditSessionService = trainersEditSessionService ?? new SwShTrainersEditSessionService(this.projectWorkspaceService);
        this.tradePokemonEditSessionService = tradePokemonEditSessionService ?? new SwShTradePokemonEditSessionService(this.projectWorkspaceService);
        this.swShWorkflowService = swShWorkflowService ?? new SwShWorkflowService(
            this.projectWorkspaceService,
            modMergerWorkflowService: this.modMergerWorkflowService);
        this.svWorkflowService = svWorkflowService ?? new SvWorkflowService(this.projectWorkspaceService);
        this.zaWorkflowService = zaWorkflowService ?? new ZaWorkflowService(this.projectWorkspaceService);
        this.swShGameDumpService = swShGameDumpService ?? new SwShGameDumpService(this.swShWorkflowService);
        this.svGameDumpService = svGameDumpService ?? new SvGameDumpService(this.svWorkflowService);
        this.zaGameDumpService = zaGameDumpService ?? new ZaGameDumpService(this.zaWorkflowService);
    }

    public string Dispatch(string requestJson)
    {
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return SerializeFailure("bridge.emptyRequest", "Bridge request JSON cannot be empty.", requestId: null);
        }

        try
        {
            // Read the minimal envelope first so the payload can be deserialized into the command-specific DTO.
            var envelope = JsonSerializer.Deserialize<BridgeCommandEnvelope>(requestJson, BridgeJson.SerializerOptions);
            var gameScopeFailure = ValidateCommandGameScope(envelope, requestJson);
            if (gameScopeFailure is not null)
            {
                return gameScopeFailure;
            }

            return envelope?.Command switch
            {
                KmCommandNames.OpenProject => DispatchOpenProject(requestJson),
                KmCommandNames.ValidateProject => DispatchValidateProject(requestJson),
                KmCommandNames.RefreshFileGraph => DispatchRefreshFileGraph(requestJson),
                KmCommandNames.ListWorkflows => DispatchListWorkflows(requestJson),
                KmCommandNames.LoadItemsWorkflow => DispatchLoadItemsWorkflow(requestJson),
                KmCommandNames.UpdateItemField => DispatchUpdateItemField(requestJson),
                KmCommandNames.UpdateItemFields => DispatchUpdateItemFields(requestJson),
                KmCommandNames.LoadPokemonWorkflow => DispatchLoadPokemonWorkflow(requestJson),
                KmCommandNames.UpdatePokemonField => DispatchUpdatePokemonField(requestJson),
                KmCommandNames.UpdatePokemonFields => DispatchUpdatePokemonFields(requestJson),
                KmCommandNames.UpdatePokemonLearnset => DispatchUpdatePokemonLearnset(requestJson),
                KmCommandNames.UpdatePokemonEvolution => DispatchUpdatePokemonEvolution(requestJson),
                KmCommandNames.LoadMovesWorkflow => DispatchLoadMovesWorkflow(requestJson),
                KmCommandNames.UpdateMoveField => DispatchUpdateMoveField(requestJson),
                KmCommandNames.UpdateMoveFields => DispatchUpdateMoveFields(requestJson),
                KmCommandNames.LoadTextWorkflow => DispatchLoadTextWorkflow(requestJson),
                KmCommandNames.UpdateTextEntry => DispatchUpdateTextEntry(requestJson),
                KmCommandNames.LoadTrainersWorkflow => DispatchLoadTrainersWorkflow(requestJson),
                KmCommandNames.UpdateTrainerField => DispatchUpdateTrainerField(requestJson),
                KmCommandNames.UpdateTrainerFields => DispatchUpdateTrainerFields(requestJson),
                KmCommandNames.LoadGiftPokemonWorkflow => DispatchLoadGiftPokemonWorkflow(requestJson),
                KmCommandNames.UpdateGiftPokemonField => DispatchUpdateGiftPokemonField(requestJson),
                KmCommandNames.UpdateGiftPokemonFields => DispatchUpdateGiftPokemonFields(requestJson),
                KmCommandNames.LoadTradePokemonWorkflow => DispatchLoadTradePokemonWorkflow(requestJson),
                KmCommandNames.UpdateTradePokemonField => DispatchUpdateTradePokemonField(requestJson),
                KmCommandNames.UpdateTradePokemonFields => DispatchUpdateTradePokemonFields(requestJson),
                KmCommandNames.LoadStaticEncountersWorkflow => DispatchLoadStaticEncountersWorkflow(requestJson),
                KmCommandNames.UpdateStaticEncounterField => DispatchUpdateStaticEncounterField(requestJson),
                KmCommandNames.LoadRentalPokemonWorkflow => DispatchLoadRentalPokemonWorkflow(requestJson),
                KmCommandNames.UpdateRentalPokemonField => DispatchUpdateRentalPokemonField(requestJson),
                KmCommandNames.LoadDynamaxAdventuresWorkflow => DispatchLoadDynamaxAdventuresWorkflow(requestJson),
                KmCommandNames.UpdateDynamaxAdventureField => DispatchUpdateDynamaxAdventureField(requestJson),
                KmCommandNames.PreviewDynamaxAdventureDefaults => DispatchPreviewDynamaxAdventureDefaults(requestJson),
                KmCommandNames.PlanDynamaxAdventureSeed => DispatchPlanDynamaxAdventureSeed(requestJson),
                KmCommandNames.SearchDynamaxAdventureSeed => DispatchSearchDynamaxAdventureSeed(requestJson),
                KmCommandNames.SetDynamaxAdventureSaveSeed => DispatchSetDynamaxAdventureSaveSeed(requestJson),
                KmCommandNames.LoadShopsWorkflow => DispatchLoadShopsWorkflow(requestJson),
                KmCommandNames.UpdateShopInventoryItem => DispatchUpdateShopInventoryItem(requestJson),
                KmCommandNames.LoadEncountersWorkflow => DispatchLoadEncountersWorkflow(requestJson),
                KmCommandNames.UpdateEncounterSlotField => DispatchUpdateEncounterSlotField(requestJson),
                KmCommandNames.UpdateEncounterSlotFields => DispatchUpdateEncounterSlotFields(requestJson),
                KmCommandNames.LoadRaidBattlesWorkflow => DispatchLoadRaidBattlesWorkflow(requestJson),
                KmCommandNames.UpdateRaidBattleSlotField => DispatchUpdateRaidBattleSlotField(requestJson),
                KmCommandNames.LoadTeraRaidsWorkflow => DispatchLoadTeraRaidsWorkflow(requestJson),
                KmCommandNames.UpdateTeraRaidField => DispatchUpdateTeraRaidField(requestJson),
                KmCommandNames.UpdateTeraRaidFields => DispatchUpdateTeraRaidFields(requestJson),
                KmCommandNames.LoadRaidRewardsWorkflow => DispatchLoadRaidRewardsWorkflow(requestJson),
                KmCommandNames.UpdateRaidRewardField => DispatchUpdateRaidRewardField(requestJson),
                KmCommandNames.LoadRaidBonusRewardsWorkflow => DispatchLoadRaidBonusRewardsWorkflow(requestJson),
                KmCommandNames.UpdateRaidBonusRewardField => DispatchUpdateRaidBonusRewardField(requestJson),
                KmCommandNames.LoadPlacementWorkflow => DispatchLoadPlacementWorkflow(requestJson),
                KmCommandNames.UpdatePlacementObjectField => DispatchUpdatePlacementObjectField(requestJson),
                KmCommandNames.UpdatePlacementObjectFields => DispatchUpdatePlacementObjectFields(requestJson),
                KmCommandNames.LoadBehaviorWorkflow => DispatchLoadBehaviorWorkflow(requestJson),
                KmCommandNames.UpdateBehaviorEntryField => DispatchUpdateBehaviorEntryField(requestJson),
                KmCommandNames.LoadFlagworkSaveWorkflow => DispatchLoadFlagworkSaveWorkflow(requestJson),
                KmCommandNames.LoadBagHookWorkflow => DispatchLoadBagHookWorkflow(requestJson),
                KmCommandNames.StageBagHookInstall => DispatchStageBagHookInstall(requestJson),
                KmCommandNames.StageBagHookUninstall => DispatchStageBagHookUninstall(requestJson),
                KmCommandNames.LoadCatchCapWorkflow => DispatchLoadCatchCapWorkflow(requestJson),
                KmCommandNames.StageCatchCap => DispatchStageCatchCap(requestJson),
                KmCommandNames.StageCatchCapUninstall => DispatchStageCatchCapUninstall(requestJson),
                KmCommandNames.LoadHyperTrainingWorkflow => DispatchLoadHyperTrainingWorkflow(requestJson),
                KmCommandNames.StageHyperTraining => DispatchStageHyperTraining(requestJson),
                KmCommandNames.LoadShinyRateWorkflow => DispatchLoadShinyRateWorkflow(requestJson),
                KmCommandNames.StageShinyRate => DispatchStageShinyRate(requestJson),
                KmCommandNames.LoadTypeChartWorkflow => DispatchLoadTypeChartWorkflow(requestJson),
                KmCommandNames.StageTypeChart => DispatchStageTypeChart(requestJson),
                KmCommandNames.StageTypeChartUninstall => DispatchStageTypeChartUninstall(requestJson),
                KmCommandNames.LoadFairyGymBoostsWorkflow => DispatchLoadFairyGymBoostsWorkflow(requestJson),
                KmCommandNames.StageFairyGymBoosts => DispatchStageFairyGymBoosts(requestJson),
                KmCommandNames.LoadFashionUnlockWorkflow => DispatchLoadFashionUnlockWorkflow(requestJson),
                KmCommandNames.StageFashionUnlockInstall => DispatchStageFashionUnlockInstall(requestJson),
                KmCommandNames.StageFashionUnlockUninstall => DispatchStageFashionUnlockUninstall(requestJson),
                KmCommandNames.LoadGymUniformRemovalWorkflow => DispatchLoadGymUniformRemovalWorkflow(requestJson),
                KmCommandNames.StageGymUniformRemovalInstall => DispatchStageGymUniformRemovalInstall(requestJson),
                KmCommandNames.StageGymUniformRemovalUninstall => DispatchStageGymUniformRemovalUninstall(requestJson),
                KmCommandNames.LoadHyperspaceBypassWorkflow => DispatchLoadHyperspaceBypassWorkflow(requestJson),
                KmCommandNames.StageHyperspaceBypassInstall => DispatchStageHyperspaceBypassInstall(requestJson),
                KmCommandNames.StageHyperspaceBypassUninstall => DispatchStageHyperspaceBypassUninstall(requestJson),
                KmCommandNames.LoadIvScreenWorkflow => DispatchLoadIvScreenWorkflow(requestJson),
                KmCommandNames.StageIvScreenInstall => DispatchStageIvScreenInstall(requestJson),
                KmCommandNames.StageIvScreenUninstall => DispatchStageIvScreenUninstall(requestJson),
                KmCommandNames.LoadExeFsPatchWorkflow => DispatchLoadExeFsPatchWorkflow(requestJson),
                KmCommandNames.StageExeFsPatch => DispatchStageExeFsPatch(requestJson),
                KmCommandNames.LoadRoyalCandyWorkflow => DispatchLoadRoyalCandyWorkflow(requestJson),
                KmCommandNames.StageRoyalCandyWorkflow => DispatchStageRoyalCandyWorkflow(requestJson),
                KmCommandNames.LoadStartingItemsWorkflow => DispatchLoadStartingItemsWorkflow(requestJson),
                KmCommandNames.StageStartingItems => DispatchStageStartingItems(requestJson),
                KmCommandNames.LoadNpcItemGiftWorkflow => DispatchLoadNpcItemGiftWorkflow(requestJson),
                KmCommandNames.StageNpcItemGift => DispatchStageNpcItemGift(requestJson),
                KmCommandNames.LoadSpreadsheetImportWorkflow => DispatchLoadSpreadsheetImportWorkflow(requestJson),
                KmCommandNames.PreviewSpreadsheetImport => DispatchPreviewSpreadsheetImport(requestJson),
                KmCommandNames.LoadModMergerWorkflow => DispatchLoadModMergerWorkflow(requestJson),
                KmCommandNames.StageModMerge => DispatchStageModMerge(requestJson),
                KmCommandNames.ApplyModMerge => DispatchApplyModMerge(requestJson),
                KmCommandNames.LoadSvModMergerWorkflow => DispatchLoadSvModMergerWorkflow(requestJson),
                KmCommandNames.StageSvModMerge => DispatchStageSvModMerge(requestJson),
                KmCommandNames.ApplySvModMerge => DispatchApplySvModMerge(requestJson),
                KmCommandNames.GetSvCacheStatus => DispatchGetSvCacheStatus(requestJson),
                KmCommandNames.UpdateSvCacheSettings => DispatchUpdateSvCacheSettings(requestJson),
                KmCommandNames.ClearSvCache => DispatchClearSvCache(requestJson),
                KmCommandNames.WarmupSvCacheStep => DispatchWarmupSvCacheStep(requestJson),
                KmCommandNames.GetZaCacheStatus => DispatchGetZaCacheStatus(requestJson),
                KmCommandNames.UpdateZaCacheSettings => DispatchUpdateZaCacheSettings(requestJson),
                KmCommandNames.ClearZaCache => DispatchClearZaCache(requestJson),
                KmCommandNames.WarmupZaCacheStep => DispatchWarmupZaCacheStep(requestJson),
                KmCommandNames.LoadFpsPatch => DispatchLoadFpsPatch(requestJson),
                KmCommandNames.ApplyFpsPatch => DispatchApplyFpsPatch(requestJson),
                KmCommandNames.RestoreFpsPatch => DispatchRestoreFpsPatch(requestJson),
                KmCommandNames.ImportRandomizerSeed => DispatchImportRandomizerSeed(requestJson),
                KmCommandNames.ApplyRandomizer => DispatchApplyRandomizer(requestJson),
                KmCommandNames.RestoreRandomizer => DispatchRestoreRandomizer(requestJson),
                KmCommandNames.LoadGameDumpWorkflow => DispatchLoadGameDumpWorkflow(requestJson),
                KmCommandNames.RunGameDump => DispatchRunGameDump(requestJson),
                KmCommandNames.StartEditSession => DispatchStartEditSession(requestJson),
                KmCommandNames.ValidateEditSession => DispatchValidateEditSession(requestJson),
                KmCommandNames.CreateChangePlan => DispatchCreateChangePlan(requestJson),
                KmCommandNames.ApplyChangePlan => DispatchApplyChangePlan(requestJson),
                null => SerializeFailure("bridge.missingCommand", "Bridge request is missing a command.", envelope?.RequestId),
                _ => SerializeFailure(
                    "bridge.unsupportedCommand",
                    $"Bridge command '{envelope.Command}' is not supported.",
                    envelope.RequestId),
            };
        }
        catch (JsonException exception)
        {
            return SerializeFailure("bridge.invalidJson", $"Bridge request JSON is invalid: {exception.Message}", requestId: null);
        }
    }

    private string DispatchOpenProject(string requestJson)
    {
        var request = DeserializeRequest<OpenProjectRequest>(requestJson);
        var openedProject = projectWorkspaceService.Open(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = new OpenProjectResponse(
            openedProject.Id.ToString(),
            ProjectBridgeMapper.ToDto(openedProject.Health),
            ProjectBridgeMapper.ToDto(openedProject.FileGraph));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchValidateProject(string requestJson)
    {
        var request = DeserializeRequest<ValidateProjectRequest>(requestJson);
        var health = projectWorkspaceService.Validate(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = new ValidateProjectResponse(ProjectBridgeMapper.ToDto(health));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchRefreshFileGraph(string requestJson)
    {
        var request = DeserializeRequest<RefreshFileGraphRequest>(requestJson);
        var fileGraph = projectWorkspaceService.RefreshFileGraph(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = new RefreshFileGraphResponse(ProjectBridgeMapper.ToDto(fileGraph));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchListWorkflows(string requestJson)
    {
        var request = DeserializeRequest<ListWorkflowsRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = paths.SelectedGame switch
        {
            ProjectGame.Scarlet or ProjectGame.Violet => SvBridgeMapper.ToDto(svWorkflowService.List(paths)),
            ProjectGame.ZA => ZaBridgeMapper.ToDto(zaWorkflowService.List(paths)),
            _ => SwShBridgeMapper.ToDto(swShWorkflowService.List(paths)),
        };

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadGameDumpWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadGameDumpWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var workflow = IsPokemonLegendsZA(paths)
            ? zaGameDumpService.Load(paths)
            : IsScarletViolet(paths)
            ? svGameDumpService.Load(paths)
            : swShGameDumpService.Load(paths);
        var response = new LoadGameDumpWorkflowResponse(ProjectBridgeMapper.ToDto(workflow));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchRunGameDump(string requestJson)
    {
        var request = DeserializeRequest<RunGameDumpRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var selections = ProjectBridgeMapper.ToCore(request.Payload.Selections);
        var result = IsPokemonLegendsZA(paths)
            ? zaGameDumpService.Run(paths, request.Payload.DestinationFolder, selections)
            : IsScarletViolet(paths)
            ? svGameDumpService.Run(paths, request.Payload.DestinationFolder, selections)
            : swShGameDumpService.Run(paths, request.Payload.DestinationFolder, selections);
        var response = new RunGameDumpResponse(ProjectBridgeMapper.ToDto(result));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadItemsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadItemsWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.LoadItems(paths))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.LoadItems(paths))
            : SwShBridgeMapper.ToDto(swShWorkflowService.LoadItems(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadPokemonWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadPokemonWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.LoadPokemon(paths))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.LoadPokemon(paths))
            : SwShBridgeMapper.ToDto(swShWorkflowService.LoadPokemon(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdatePokemonField(string requestJson)
    {
        var request = DeserializeRequest<UpdatePokemonFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.UpdatePokemonField(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Field,
                request.Payload.Value))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.UpdatePokemonField(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Field,
                request.Payload.Value))
            : SwShBridgeMapper.ToDto(pokemonEditSessionService.UpdateField(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Field,
                request.Payload.Value));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdatePokemonFields(string requestJson)
    {
        var request = DeserializeRequest<UpdatePokemonFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (IsPokemonLegendsZA(paths))
        {
            var zaResponse = ZaBridgeMapper.ToPokemonFieldsDto(zaWorkflowService.UpdatePokemonFields(
                paths,
                session,
                request.Payload.Updates
                    .Select(update => new KM.ZA.Pokemon.ZaPokemonFieldUpdate(update.PersonalId, update.Field, update.Value))
                    .ToArray()));

            return SerializeSuccess(zaResponse, request.RequestId);
        }

        if (!IsScarletViolet(paths))
        {
            return SerializeFailure(
                "bridge.unsupportedCommand",
                "Batch Pokemon field updates are supported for Scarlet/Violet and Pokemon Legends Z-A projects.",
                request.RequestId);
        }

        var response = SvBridgeMapper.ToPokemonFieldsDto(
            svWorkflowService.UpdatePokemonFields(
                paths,
                session,
                request.Payload.Updates
                    .Select(update => new SvPokemonFieldUpdate(update.PersonalId, update.Field, update.Value))
                    .ToArray()));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdatePokemonLearnset(string requestJson)
    {
        var request = DeserializeRequest<UpdatePokemonLearnsetRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDtoLearnsetUpdate(zaWorkflowService.UpdatePokemonLearnset(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Action,
                request.Payload.Slot,
                request.Payload.MoveId,
                request.Payload.Level))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDtoLearnsetUpdate(svWorkflowService.UpdatePokemonLearnset(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Action,
                request.Payload.Slot,
                request.Payload.MoveId,
                request.Payload.Level))
            : SwShBridgeMapper.ToDtoLearnsetUpdate(pokemonEditSessionService.UpdateLearnset(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Action,
                request.Payload.Slot,
                request.Payload.MoveId,
                request.Payload.Level));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdatePokemonEvolution(string requestJson)
    {
        var request = DeserializeRequest<UpdatePokemonEvolutionRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDtoEvolutionUpdate(zaWorkflowService.UpdatePokemonEvolution(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Action,
                request.Payload.Slot,
                request.Payload.Method,
                request.Payload.Argument,
                request.Payload.Species,
                request.Payload.Form,
                request.Payload.Level))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDtoEvolutionUpdate(svWorkflowService.UpdatePokemonEvolution(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Action,
                request.Payload.Slot,
                request.Payload.Method,
                request.Payload.Argument,
                request.Payload.Species,
                request.Payload.Form,
                request.Payload.Level))
            : SwShBridgeMapper.ToDtoEvolutionUpdate(pokemonEditSessionService.UpdateEvolution(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Action,
                request.Payload.Slot,
                request.Payload.Method,
                request.Payload.Argument,
                request.Payload.Species,
                request.Payload.Form,
                request.Payload.Level));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadMovesWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadMovesWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.LoadMoves(paths))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.LoadMoves(paths))
            : SwShBridgeMapper.ToDto(swShWorkflowService.LoadMoves(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateMoveField(string requestJson)
    {
        var request = DeserializeRequest<UpdateMoveFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.UpdateMoveField(
                paths,
                session,
                request.Payload.MoveId,
                request.Payload.Field,
                request.Payload.Value))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.UpdateMoveField(
                paths,
                session,
                request.Payload.MoveId,
                request.Payload.Field,
                request.Payload.Value))
            : SwShBridgeMapper.ToDto(movesEditSessionService.UpdateField(
                paths,
                session,
                request.Payload.MoveId,
                request.Payload.Field,
                request.Payload.Value));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateMoveFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateMoveFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (IsPokemonLegendsZA(paths))
        {
            var updates = request.Payload.Updates
                .Select(update => new ZaMoveFieldUpdate(update.MoveId, update.Field, update.Value))
                .ToArray();
            var zaResponse = ZaBridgeMapper.ToMoveFieldsDto(
                zaWorkflowService.UpdateMoveFields(paths, session, updates));

            return SerializeSuccess(zaResponse, request.RequestId);
        }

        if (!IsScarletViolet(paths))
        {
            return SerializeFailure(
                "bridge.gameMismatch",
                "Bridge command 'moves.fields.update' is only available for Scarlet/Violet or Pokemon Legends Z-A projects.",
                request.RequestId);
        }

        var svUpdates = request.Payload.Updates
            .Select(update => new SvMoveFieldUpdate(update.MoveId, update.Field, update.Value))
            .ToArray();
        var response = SvBridgeMapper.ToMoveFieldsDto(
            svWorkflowService.UpdateMoveFields(paths, session, svUpdates));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadTextWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadTextWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.LoadText(paths))
            : SwShBridgeMapper.ToDto(swShWorkflowService.LoadText(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateTextEntry(string requestJson)
    {
        var request = DeserializeRequest<UpdateTextEntryRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.UpdateTextEntry(
                paths,
                session,
                request.Payload.TextKey,
                request.Payload.Value))
            : SwShBridgeMapper.ToDto(textEditSessionService.UpdateEntry(
                paths,
                session,
                request.Payload.TextKey,
                request.Payload.Value));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadTrainersWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadTrainersWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.LoadTrainers(paths))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.LoadTrainers(paths))
            : SwShBridgeMapper.ToDto(swShWorkflowService.LoadTrainers(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateTrainerField(string requestJson)
    {
        var request = DeserializeRequest<UpdateTrainerFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.UpdateTrainerField(
                paths,
                session,
                request.Payload.TrainerId,
                request.Payload.Slot,
                request.Payload.Field,
                request.Payload.Value))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.UpdateTrainerField(
                paths,
                session,
                request.Payload.TrainerId,
                request.Payload.Slot,
                request.Payload.Field,
                request.Payload.Value))
            : SwShBridgeMapper.ToDto(trainersEditSessionService.UpdateField(
                paths,
                session,
                request.Payload.TrainerId,
                request.Payload.Slot,
                request.Payload.Field,
                request.Payload.Value));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateTrainerFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateTrainerFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (IsPokemonLegendsZA(paths))
        {
            var zaUpdates = request.Payload.Updates
                .Select(update => new KM.ZA.Trainers.ZaTrainerFieldUpdate(update.TrainerId, update.Slot, update.Field, update.Value))
                .ToArray();
            var zaResponse = ZaBridgeMapper.ToTrainerFieldsDto(
                zaWorkflowService.UpdateTrainerFields(paths, session, zaUpdates));

            return SerializeSuccess(zaResponse, request.RequestId);
        }

        if (!IsScarletViolet(paths))
        {
            return SerializeFailure(
                "bridge.gameMismatch",
                "Bridge command 'trainers.fields.update' is only available for Scarlet/Violet or Pokemon Legends Z-A projects.",
                request.RequestId);
        }

        var updates = request.Payload.Updates
            .Select(update => new SvTrainerFieldUpdate(update.TrainerId, update.Slot, update.Field, update.Value))
            .ToArray();
        var response = SvBridgeMapper.ToTrainerFieldsDto(
            svWorkflowService.UpdateTrainerFields(paths, session, updates));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadShopsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadShopsWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        LoadShopsWorkflowResponse response;
        if (IsPokemonLegendsZA(paths))
        {
            response = ZaBridgeMapper.ToDto(zaWorkflowService.LoadShops(paths));
        }
        else if (IsScarletViolet(paths))
        {
            response = SvBridgeMapper.ToDto(svWorkflowService.LoadShops(paths));
        }
        else
        {
            response = SwShBridgeMapper.ToDto(swShWorkflowService.LoadShops(paths));
        }

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadGiftPokemonWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadGiftPokemonWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.LoadGiftPokemon(paths))
            : SwShBridgeMapper.ToDto(swShWorkflowService.LoadGiftPokemon(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateGiftPokemonField(string requestJson)
    {
        var request = DeserializeRequest<UpdateGiftPokemonFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.UpdateGiftPokemonField(
                paths,
                session,
                request.Payload.GiftIndex,
                request.Payload.Field,
                request.Payload.Value))
            : SwShBridgeMapper.ToDto(giftPokemonEditSessionService.UpdateField(
                paths,
                session,
                request.Payload.GiftIndex,
                request.Payload.Field,
                request.Payload.Value));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateGiftPokemonFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateGiftPokemonFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var updates = request.Payload.Updates
            .Select(update => new SvGiftPokemonFieldUpdate(update.GiftIndex, update.Field, update.Value))
            .ToArray();
        var response = SvBridgeMapper.ToGiftPokemonFieldsDto(
            svWorkflowService.UpdateGiftPokemonFields(paths, session, updates));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadTradePokemonWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadTradePokemonWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.LoadTradePokemon(paths))
            : SwShBridgeMapper.ToDto(swShWorkflowService.LoadTradePokemon(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateTradePokemonField(string requestJson)
    {
        var request = DeserializeRequest<UpdateTradePokemonFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.UpdateTradePokemonField(
                paths,
                session,
                request.Payload.TradeIndex,
                request.Payload.Field,
                request.Payload.Value))
            : SwShBridgeMapper.ToDto(tradePokemonEditSessionService.UpdateField(
                paths,
                session,
                request.Payload.TradeIndex,
                request.Payload.Field,
                request.Payload.Value));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateTradePokemonFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateTradePokemonFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var updates = request.Payload.Updates
            .Select(update => new SvTradePokemonFieldUpdate(update.TradeIndex, update.Field, update.Value))
            .ToArray();
        var response = SvBridgeMapper.ToTradePokemonFieldsDto(
            svWorkflowService.UpdateTradePokemonFields(paths, session, updates));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadStaticEncountersWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadStaticEncountersWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.LoadStaticEncounters(paths))
            : SwShBridgeMapper.ToDto(swShWorkflowService.LoadStaticEncounters(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateStaticEncounterField(string requestJson)
    {
        var request = DeserializeRequest<UpdateStaticEncounterFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.UpdateStaticEncounterField(
                paths,
                session,
                request.Payload.EncounterIndex,
                request.Payload.Field,
                request.Payload.Value))
            : SwShBridgeMapper.ToDto(staticEncountersEditSessionService.UpdateField(
                paths,
                session,
                request.Payload.EncounterIndex,
                request.Payload.Field,
                request.Payload.Value));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadRentalPokemonWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadRentalPokemonWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadRentalPokemon(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateRentalPokemonField(string requestJson)
    {
        var request = DeserializeRequest<UpdateRentalPokemonFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = rentalPokemonEditSessionService.UpdateField(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.RentalIndex,
            request.Payload.Field,
            request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadDynamaxAdventuresWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadDynamaxAdventuresWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadDynamaxAdventures(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateDynamaxAdventureField(string requestJson)
    {
        var request = DeserializeRequest<UpdateDynamaxAdventureFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = dynamaxAdventuresEditSessionService.UpdateField(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.EntryIndex,
            request.Payload.Field,
            request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchPreviewDynamaxAdventureDefaults(string requestJson)
    {
        var request = DeserializeRequest<PreviewDynamaxAdventureDefaultsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var preview = dynamaxAdventuresEditSessionService.PreviewDefaults(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.EntryIndex,
            request.Payload.Species,
            request.Payload.Form,
            request.Payload.Level);
        var response = SwShBridgeMapper.ToDto(preview);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchPlanDynamaxAdventureSeed(string requestJson)
    {
        var request = DeserializeRequest<PlanDynamaxAdventureSeedRequest>(requestJson);
        if (!TryParseSeed(request.Payload.Seed, out var seed))
        {
            return SerializeFailure(
                "dynamaxAdventures.seed.invalid",
                $"Dynamax Adventures seed '{request.Payload.Seed}' is not a valid 64-bit seed.",
                request.RequestId);
        }

        var result = dynamaxAdventureSeedPlanningService.Predict(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            seed,
            request.Payload.NpcCount,
            request.Payload.RequiredRows);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchSearchDynamaxAdventureSeed(string requestJson)
    {
        var request = DeserializeRequest<SearchDynamaxAdventureSeedRequest>(requestJson);
        if (!TryParseSeed(request.Payload.StartSeed, out var startSeed))
        {
            return SerializeFailure(
                "dynamaxAdventures.seed.invalidStart",
                $"Dynamax Adventures start seed '{request.Payload.StartSeed}' is not a valid 64-bit seed.",
                request.RequestId);
        }

        if (!TryParseSeed(request.Payload.Limit, out var limit))
        {
            return SerializeFailure(
                "dynamaxAdventures.seed.invalidLimit",
                $"Dynamax Adventures seed search limit '{request.Payload.Limit}' is not a valid 64-bit value.",
                request.RequestId);
        }

        var result = dynamaxAdventureSeedPlanningService.SearchRows(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.RequiredRows,
            request.Payload.NpcCount,
            startSeed,
            limit,
            request.Payload.MaxResults);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchSetDynamaxAdventureSaveSeed(string requestJson)
    {
        var request = DeserializeRequest<SetDynamaxAdventureSaveSeedRequest>(requestJson);
        if (!TryParseSeed(request.Payload.Seed, out var seed))
        {
            return SerializeFailure(
                "dynamaxAdventures.seed.invalid",
                $"Dynamax Adventures seed '{request.Payload.Seed}' is not a valid 64-bit seed.",
                request.RequestId);
        }

        var result = dynamaxAdventureSaveSeedService.SetSeed(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            seed);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateShopInventoryItem(string requestJson)
    {
        var request = DeserializeRequest<UpdateShopInventoryItemRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        UpdateShopInventoryItemResponse response;
        if (IsPokemonLegendsZA(paths))
        {
            response = ZaBridgeMapper.ToDto(zaWorkflowService.UpdateShopInventoryItem(
                paths,
                session,
                request.Payload.ShopId,
                request.Payload.Slot,
                request.Payload.Field,
                request.Payload.Value));
        }
        else if (IsScarletViolet(paths))
        {
            response = SvBridgeMapper.ToDto(svWorkflowService.UpdateShopInventoryItem(
                paths,
                session,
                request.Payload.ShopId,
                request.Payload.Slot,
                request.Payload.Field,
                request.Payload.Value));
        }
        else
        {
            response = SwShBridgeMapper.ToDto(shopsEditSessionService.UpdateInventoryItem(
                paths,
                session,
                request.Payload.ShopId,
                request.Payload.Slot,
                request.Payload.Field,
                request.Payload.Value));
        }

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadEncountersWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadEncountersWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.LoadEncounters(paths))
            : SwShBridgeMapper.ToDto(swShWorkflowService.LoadEncounters(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateEncounterSlotField(string requestJson)
    {
        var request = DeserializeRequest<UpdateEncounterSlotFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.UpdateEncounterSlotField(
                paths,
                session,
                request.Payload.TableId,
                request.Payload.Slot,
                request.Payload.Field,
                request.Payload.Value))
            : SwShBridgeMapper.ToDto(encountersEditSessionService.UpdateSlotField(
                paths,
                session,
                request.Payload.TableId,
                request.Payload.Slot,
                request.Payload.Field,
                request.Payload.Value));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateEncounterSlotFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateEncounterSlotFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var updates = request.Payload.Updates
            .Select(update => new SvEncounterSlotFieldUpdate(
                update.TableId,
                update.Slot,
                update.Field,
                update.Value))
            .ToArray();
        var response = SvBridgeMapper.ToEncounterSlotFieldsDto(
            svWorkflowService.UpdateEncounterSlotFields(paths, session, updates));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadRaidBattlesWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadRaidBattlesWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadRaidBattles(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateRaidBattleSlotField(string requestJson)
    {
        var request = DeserializeRequest<UpdateRaidBattleSlotFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = raidBattlesEditSessionService.UpdateSlotField(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.TableId,
            request.Payload.Slot,
            request.Payload.Field,
            request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadTeraRaidsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadTeraRaidsWorkflowRequest>(requestJson);
        var workflow = svWorkflowService.LoadTeraRaids(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SvBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateTeraRaidField(string requestJson)
    {
        var request = DeserializeRequest<UpdateTeraRaidFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = svWorkflowService.UpdateTeraRaidField(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.RecordId,
            request.Payload.Field,
            request.Payload.Value);
        var response = SvBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateTeraRaidFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateTeraRaidFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var updates = request.Payload.Updates
            .Select(update => new SvTeraRaidFieldUpdate(update.RecordId, update.Field, update.Value))
            .ToArray();
        var response = SvBridgeMapper.ToTeraRaidFieldsDto(
            svWorkflowService.UpdateTeraRaidFields(
                ProjectBridgeMapper.ToCore(request.Payload.Paths),
                session,
                updates));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadRaidRewardsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadRaidRewardsWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadRaidRewards(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateRaidRewardField(string requestJson)
    {
        var request = DeserializeRequest<UpdateRaidRewardFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = raidRewardsEditSessionService.UpdateRewardField(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.TableId,
            request.Payload.Slot,
            request.Payload.Field,
            request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadRaidBonusRewardsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadRaidBonusRewardsWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadRaidBonusRewards(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToBonusDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateRaidBonusRewardField(string requestJson)
    {
        var request = DeserializeRequest<UpdateRaidBonusRewardFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = raidRewardsEditSessionService.UpdateBonusRewardField(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.TableId,
            request.Payload.Slot,
            request.Payload.Field,
            request.Payload.Value);
        var response = SwShBridgeMapper.ToBonusDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadPlacementWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadPlacementWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (IsScarletViolet(paths))
        {
            var svWorkflow = svWorkflowService.LoadPlacement(paths);
            var svResponse = SvBridgeMapper.ToDto(svWorkflow);

            return SerializeSuccess(svResponse, request.RequestId);
        }

        var workflow = swShWorkflowService.LoadPlacement(paths);
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdatePlacementObjectField(string requestJson)
    {
        var request = DeserializeRequest<UpdatePlacementObjectFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (IsScarletViolet(paths))
        {
            var svResult = svWorkflowService.UpdatePlacementObjectField(
                paths,
                session,
                request.Payload.ObjectId,
                request.Payload.Field,
                request.Payload.Value);
            var svResponse = SvBridgeMapper.ToDto(svResult);

            return SerializeSuccess(svResponse, request.RequestId);
        }

        var result = placementEditSessionService.UpdateObjectField(
            paths,
            session,
            request.Payload.ObjectId,
            request.Payload.Field,
            request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdatePlacementObjectFields(string requestJson)
    {
        var request = DeserializeRequest<UpdatePlacementObjectFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var updates = request.Payload.Updates
            .Select(update => new SvPlacementObjectFieldUpdate(update.ObjectId, update.Field, update.Value))
            .ToArray();
        var response = SvBridgeMapper.ToPlacementObjectFieldsDto(
            svWorkflowService.UpdatePlacementObjectFields(paths, session, updates));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadBehaviorWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadBehaviorWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadBehavior(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateBehaviorEntryField(string requestJson)
    {
        var request = DeserializeRequest<UpdateBehaviorEntryFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = behaviorEditSessionService.UpdateEntryField(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.EntryId,
            request.Payload.Field,
            request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadFlagworkSaveWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadFlagworkSaveWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadFlagworkSave(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadBagHookWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadBagHookWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadBagHook(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageBagHookInstall(string requestJson)
    {
        var request = DeserializeRequest<StageBagHookInstallRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = bagHookEditSessionService.StageInstall(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageBagHookUninstall(string requestJson)
    {
        var request = DeserializeRequest<StageBagHookUninstallRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = bagHookEditSessionService.StageUninstall(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session);
        var response = SwShBridgeMapper.ToBagHookUninstallDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadCatchCapWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadCatchCapWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadCatchCap(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageCatchCap(string requestJson)
    {
        var request = DeserializeRequest<StageCatchCapRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = catchCapEditSessionService.StageCaps(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.Caps.Select(selection => new SwShCatchCapSelection(
                selection.BadgeCount,
                selection.LevelCap)).ToArray(),
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageCatchCapUninstall(string requestJson)
    {
        var request = DeserializeRequest<StageCatchCapUninstallRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = catchCapEditSessionService.StageUninstall(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session);
        var response = SwShBridgeMapper.ToCatchCapUninstallDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadHyperTrainingWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadHyperTrainingWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadHyperTraining(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageHyperTraining(string requestJson)
    {
        var request = DeserializeRequest<StageHyperTrainingRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = hyperTrainingEditSessionService.StageMinimumLevel(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.MinimumLevel,
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadShinyRateWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadShinyRateWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadShinyRate(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageShinyRate(string requestJson)
    {
        var request = DeserializeRequest<StageShinyRateRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = shinyRateEditSessionService.StageRate(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.Mode,
            request.Payload.RollCount,
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadTypeChartWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadTypeChartWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (IsScarletViolet(paths))
        {
            var svWorkflow = svWorkflowService.LoadTypeChart(paths);
            var svResponse = SvBridgeMapper.ToDto(svWorkflow);

            return SerializeSuccess(svResponse, request.RequestId);
        }

        var workflow = swShWorkflowService.LoadTypeChart(paths);
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadFairyGymBoostsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadFairyGymBoostsWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadFairyGymBoosts(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageFairyGymBoosts(string requestJson)
    {
        var request = DeserializeRequest<StageFairyGymBoostsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var selections = request.Payload.Selections
            .Select(selection => new SwShFairyGymBoostSelection(
                selection.BoostId,
                selection.EffectId,
                selection.ResultKind))
            .ToArray();
        var result = fairyGymBoostsEditSessionService.StageBoosts(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            selections,
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageTypeChart(string requestJson)
    {
        var request = DeserializeRequest<StageTypeChartRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        if (IsScarletViolet(paths))
        {
            var svResult = svWorkflowService.StageTypeChart(
                paths,
                request.Payload.Values,
                session);
            var svResponse = SvBridgeMapper.ToDto(svResult);

            return SerializeSuccess(svResponse, request.RequestId);
        }

        var result = typeChartEditSessionService.StageChart(
            paths,
            request.Payload.Values,
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageTypeChartUninstall(string requestJson)
    {
        var request = DeserializeRequest<StageTypeChartUninstallRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = svWorkflowService.StageTypeChartUninstall(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session);
        var response = SvBridgeMapper.ToTypeChartUninstallDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadFashionUnlockWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadFashionUnlockWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.LoadFashionUnlock(paths))
            : SwShBridgeMapper.ToDto(swShWorkflowService.LoadFashionUnlock(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageFashionUnlockInstall(string requestJson)
    {
        var request = DeserializeRequest<StageFashionUnlockInstallRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var response = IsScarletViolet(paths)
            ? SvBridgeMapper.ToFashionUnlockInstallDto(svWorkflowService.StageFashionUnlockInstall(paths, session))
            : SwShBridgeMapper.ToDto(fashionUnlockEditSessionService.StageInstall(paths, session));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageFashionUnlockUninstall(string requestJson)
    {
        var request = DeserializeRequest<StageFashionUnlockUninstallRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var response = IsScarletViolet(paths)
            ? SvBridgeMapper.ToFashionUnlockUninstallDto(svWorkflowService.StageFashionUnlockUninstall(paths, session))
            : SwShBridgeMapper.ToFashionUnlockUninstallDto(fashionUnlockEditSessionService.StageUninstall(paths, session));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadGymUniformRemovalWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadGymUniformRemovalWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadGymUniformRemoval(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageGymUniformRemovalInstall(string requestJson)
    {
        var request = DeserializeRequest<StageGymUniformRemovalInstallRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = gymUniformRemovalEditSessionService.StageInstall(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageGymUniformRemovalUninstall(string requestJson)
    {
        var request = DeserializeRequest<StageGymUniformRemovalUninstallRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = gymUniformRemovalEditSessionService.StageUninstall(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session);
        var response = SwShBridgeMapper.ToGymUniformRemovalUninstallDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadHyperspaceBypassWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadHyperspaceBypassWorkflowRequest>(requestJson);
        var workflow = svWorkflowService.LoadHyperspaceBypass(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SvBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageHyperspaceBypassInstall(string requestJson)
    {
        var request = DeserializeRequest<StageHyperspaceBypassInstallRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = svWorkflowService.StageHyperspaceBypassInstall(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session);
        var response = SvBridgeMapper.ToHyperspaceBypassInstallDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageHyperspaceBypassUninstall(string requestJson)
    {
        var request = DeserializeRequest<StageHyperspaceBypassUninstallRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = svWorkflowService.StageHyperspaceBypassUninstall(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session);
        var response = SvBridgeMapper.ToHyperspaceBypassUninstallDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadIvScreenWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadIvScreenWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadIvScreen(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageIvScreenInstall(string requestJson)
    {
        var request = DeserializeRequest<StageIvScreenInstallRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = ivScreenEditSessionService.StageInstall(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageIvScreenUninstall(string requestJson)
    {
        var request = DeserializeRequest<StageIvScreenUninstallRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = ivScreenEditSessionService.StageUninstall(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session);
        var response = SwShBridgeMapper.ToIvScreenUninstallDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadExeFsPatchWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadExeFsPatchWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadExeFsPatches(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageExeFsPatch(string requestJson)
    {
        var request = DeserializeRequest<StageExeFsPatchRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = exeFsPatchEditSessionService.StagePatch(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.PatchId,
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadRoyalCandyWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadRoyalCandyWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadRoyalCandy(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageRoyalCandyWorkflow(string requestJson)
    {
        var request = DeserializeRequest<StageRoyalCandyWorkflowRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = royalCandyEditSessionService.StageWorkflow(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.WorkflowId,
            request.Payload.LevelCaps?.Select(selection => new SwShRoyalCandyLevelCapSelection(
                selection.Slot,
                selection.LevelCap)).ToArray(),
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadStartingItemsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadStartingItemsWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadStartingItems(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageStartingItems(string requestJson)
    {
        var request = DeserializeRequest<StageStartingItemsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = startingItemsEditSessionService.StageGrants(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.Grants.Select(selection => new SwShStartingItemGrantSelection(
                selection.Slot,
                selection.ItemId,
                selection.Quantity)).ToArray(),
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadNpcItemGiftWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadNpcItemGiftWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadNpcItemGift(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageNpcItemGift(string requestJson)
    {
        var request = DeserializeRequest<StageNpcItemGiftRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = npcItemGiftEditSessionService.StageGifts(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.Gifts.Select(selection => new SwShNpcItemGiftSelection(
                selection.GiftId,
                selection.Quantity,
                selection.Items.Select(item => new SwShNpcItemGiftItemSelection(
                    item.SlotId,
                    item.ItemId)).ToArray())).ToArray(),
            session);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadSpreadsheetImportWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadSpreadsheetImportWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.LoadDumpImport(paths))
            : SwShBridgeMapper.ToDto(swShWorkflowService.LoadSpreadsheetImport(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchPreviewSpreadsheetImport(string requestJson)
    {
        var request = DeserializeRequest<PreviewSpreadsheetImportRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.PreviewDumpImport(
                paths,
                request.Payload.ProfileId,
                request.Payload.SourcePath,
                session))
            : SwShBridgeMapper.ToDto(spreadsheetImportExecutionService.Preview(
                paths,
                request.Payload.ProfileId,
                request.Payload.SourcePath,
                session));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadModMergerWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadModMergerWorkflowRequest>(requestJson);
        var workflow = modMergerWorkflowService.Load(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.ModDirectory1,
            request.Payload.ModDirectory2);
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageModMerge(string requestJson)
    {
        var request = DeserializeRequest<StageModMergeRequest>(requestJson);
        var result = modMergerWorkflowService.Stage(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.ModDirectory1,
            request.Payload.ModDirectory2,
            request.Payload.SelectedDirectory1Files,
            request.Payload.SelectedDirectory2Files,
            request.Payload.Resolutions.Select(resolution => new SwShModMergerConflictResolution(
                resolution.ConflictId,
                resolution.Source)).ToArray(),
            request.Payload.MergeMode);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchApplyModMerge(string requestJson)
    {
        var request = DeserializeRequest<ApplyModMergeRequest>(requestJson);
        var result = modMergerWorkflowService.Apply(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.ModDirectory1,
            request.Payload.ModDirectory2,
            request.Payload.SelectedDirectory1Files,
            request.Payload.SelectedDirectory2Files,
            request.Payload.Resolutions.Select(resolution => new SwShModMergerConflictResolution(
                resolution.ConflictId,
                resolution.Source)).ToArray(),
            request.Payload.MergeMode);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadSvModMergerWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadSvModMergerWorkflowRequest>(requestJson);
        var workflow = svWorkflowService.LoadModMerger(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.ModSources.Select(ToCore).ToArray());
        var response = SvBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageSvModMerge(string requestJson)
    {
        var request = DeserializeRequest<StageSvModMergeRequest>(requestJson);
        var result = svWorkflowService.StageModMerge(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.ModSources.Select(ToCore).ToArray());
        var response = SvBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchApplySvModMerge(string requestJson)
    {
        var request = DeserializeRequest<ApplySvModMergeRequest>(requestJson);
        var result = svWorkflowService.ApplyModMerge(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.ModSources.Select(ToCore).ToArray());
        var response = SvBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchGetSvCacheStatus(string requestJson)
    {
        var request = DeserializeRequest<GetSvCacheStatusRequest>(requestJson);
        var paths = request.Payload.Paths is null
            ? null
            : ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = SvBridgeMapper.ToDto(svWorkflowService.GetCacheStatus(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateSvCacheSettings(string requestJson)
    {
        var request = DeserializeRequest<UpdateSvCacheSettingsRequest>(requestJson);
        var paths = request.Payload.Paths is null
            ? null
            : ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = SvBridgeMapper.ToDto(svWorkflowService.UpdateCacheSettings(
            SvBridgeMapper.ToCore(request.Payload.Mode),
            request.Payload.MaxCacheSizeBytes,
            paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchClearSvCache(string requestJson)
    {
        var request = DeserializeRequest<ClearSvCacheRequest>(requestJson);
        var paths = request.Payload.ActivePaths is null
            ? null
            : ProjectBridgeMapper.ToCore(request.Payload.ActivePaths);
        var response = SvBridgeMapper.ToDto(svWorkflowService.ClearCache(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchWarmupSvCacheStep(string requestJson)
    {
        var request = DeserializeRequest<WarmupSvCacheStepRequest>(requestJson);
        var response = SvBridgeMapper.ToDto(svWorkflowService.WarmupCacheStep(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.StepIndex));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchGetZaCacheStatus(string requestJson)
    {
        var request = DeserializeRequest<GetZaCacheStatusRequest>(requestJson);
        var paths = request.Payload.Paths is null
            ? null
            : ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = ZaBridgeMapper.ToDto(zaWorkflowService.GetCacheStatus(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateZaCacheSettings(string requestJson)
    {
        var request = DeserializeRequest<UpdateZaCacheSettingsRequest>(requestJson);
        var paths = request.Payload.Paths is null
            ? null
            : ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = ZaBridgeMapper.ToDto(zaWorkflowService.UpdateCacheSettings(
            ZaBridgeMapper.ToCore(request.Payload.Mode),
            request.Payload.MaxCacheSizeBytes,
            paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchClearZaCache(string requestJson)
    {
        var request = DeserializeRequest<ClearZaCacheRequest>(requestJson);
        var paths = request.Payload.ActivePaths is null
            ? null
            : ProjectBridgeMapper.ToCore(request.Payload.ActivePaths);
        var response = ZaBridgeMapper.ToDto(zaWorkflowService.ClearCache(paths));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchWarmupZaCacheStep(string requestJson)
    {
        var request = DeserializeRequest<WarmupZaCacheStepRequest>(requestJson);
        var response = ZaBridgeMapper.ToDto(zaWorkflowService.WarmupCacheStep(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.StepIndex));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadFpsPatch(string requestJson)
    {
        var request = DeserializeRequest<LoadFpsPatchRequest>(requestJson);
        var status = fpsPatchService.Load(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = new LoadFpsPatchResponse(ToDto(status));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchApplyFpsPatch(string requestJson)
    {
        var request = DeserializeRequest<ApplyFpsPatchRequest>(requestJson);
        var result = fpsPatchService.Apply(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = new ApplyFpsPatchResponse(
            ToDto(result.Status),
            EditSessionBridgeMapper.ToDto(result.ApplyResult));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchRestoreFpsPatch(string requestJson)
    {
        var request = DeserializeRequest<RestoreFpsPatchRequest>(requestJson);
        var result = fpsPatchService.Restore(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = new RestoreFpsPatchResponse(
            ToDto(result.Status),
            EditSessionBridgeMapper.ToDto(result.ApplyResult));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchImportRandomizerSeed(string requestJson)
    {
        var request = DeserializeRequest<ImportRandomizerSeedRequest>(requestJson);
        var result = randomizerService.ImportSeed(request.Payload.Seed);
        var response = new ImportRandomizerSeedResponse(
            result.Config is null ? null : ToDto(result.Config),
            result.Seed,
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchApplyRandomizer(string requestJson)
    {
        var request = DeserializeRequest<ApplyRandomizerRequest>(requestJson);
        var result = randomizerService.Apply(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            ToCore(request.Payload.Config));
        var response = new ApplyRandomizerResponse(
            result.Seed,
            EditSessionBridgeMapper.ToDto(result.ApplyResult));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchRestoreRandomizer(string requestJson)
    {
        var request = DeserializeRequest<RestoreRandomizerRequest>(requestJson);
        var result = randomizerService.Restore(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = new RestoreRandomizerResponse(EditSessionBridgeMapper.ToDto(result));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateItemField(string requestJson)
    {
        var request = DeserializeRequest<UpdateItemFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToDto(zaWorkflowService.UpdateItemField(
                paths,
                session,
                request.Payload.ItemId,
                request.Payload.Field,
                request.Payload.Value))
            : IsScarletViolet(paths)
            ? SvBridgeMapper.ToDto(svWorkflowService.UpdateItemField(
                paths,
                session,
                request.Payload.ItemId,
                request.Payload.Field,
                request.Payload.Value))
            : SwShBridgeMapper.ToDto(itemsEditSessionService.UpdateField(
                paths,
                session,
                request.Payload.ItemId,
                request.Payload.Field,
                request.Payload.Value));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateItemFields(string requestJson)
    {
        var request = DeserializeRequest<UpdateItemFieldsRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = IsPokemonLegendsZA(paths)
            ? ZaBridgeMapper.ToItemFieldsDto(
                zaWorkflowService.UpdateItemFields(
                    paths,
                    session,
                    request.Payload.Updates
                        .Select(update => new ZaItemFieldUpdate(update.ItemId, update.Field, update.Value))
                        .ToArray()))
            : SvBridgeMapper.ToItemFieldsDto(
                svWorkflowService.UpdateItemFields(
                    paths,
                    session,
                    request.Payload.Updates
                        .Select(update => new SvItemFieldUpdate(update.ItemId, update.Field, update.Value))
                        .ToArray()));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStartEditSession(string requestJson)
    {
        var request = DeserializeRequest<StartEditSessionRequest>(requestJson);
        _ = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var response = new StartEditSessionResponse(
            EditSessionBridgeMapper.ToDto(EditSession.Start()));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchValidateEditSession(string requestJson)
    {
        var request = DeserializeRequest<ValidateEditSessionRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var session = EditSessionBridgeMapper.ToCore(request.Payload.Session);
        if (IsPokemonLegendsZA(paths))
        {
            var zaValidation = zaWorkflowService.ValidateEditSession(paths, session);
            var zaResponse = ZaBridgeMapper.ToDto(zaValidation);

            return SerializeSuccess(zaResponse, request.RequestId);
        }

        if (IsScarletViolet(paths))
        {
            var svValidation = svWorkflowService.ValidateEditSession(paths, session);
            var svResponse = SvBridgeMapper.ToDto(svValidation);

            return SerializeSuccess(svResponse, request.RequestId);
        }

        var validation = ValidateSwShEditSession(paths, session);
        var response = SwShBridgeMapper.ToDto(validation);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchCreateChangePlan(string requestJson)
    {
        var request = DeserializeRequest<CreateChangePlanRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var session = EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var changePlan = IsPokemonLegendsZA(paths)
            ? zaWorkflowService.CreateChangePlan(paths, session, ZaBridgeMapper.ToCore(request.Payload.OutputMode))
            : IsScarletViolet(paths)
            ? svWorkflowService.CreateChangePlan(paths, session, SvBridgeMapper.ToCore(request.Payload.OutputMode))
            : CreateSwShChangePlan(paths, session);
        var response = new CreateChangePlanResponse(EditSessionBridgeMapper.ToDto(changePlan));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchApplyChangePlan(string requestJson)
    {
        var request = DeserializeRequest<ApplyChangePlanRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var session = EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var changePlan = EditSessionBridgeMapper.ToCore(request.Payload.ChangePlan);
        var applyResult = IsPokemonLegendsZA(paths)
            ? zaWorkflowService.ApplyChangePlan(paths, session, changePlan, ZaBridgeMapper.ToCore(request.Payload.OutputMode))
            : IsScarletViolet(paths)
            ? svWorkflowService.ApplyChangePlan(paths, session, changePlan, SvBridgeMapper.ToCore(request.Payload.OutputMode))
            : ApplySwShChangePlan(paths, session, changePlan);
        var response = new ApplyChangePlanResponse(EditSessionBridgeMapper.ToDto(applyResult));

        return SerializeSuccess(response, request.RequestId);
    }

    private SwShEditSessionValidation ValidateSwShEditSession(ProjectPaths paths, EditSession session)
    {
        var domain = GetEditSessionDomain(session);
        return domain == EditSessionDomain.Mixed && TryGetNormalSwShDomains(session, out var domains)
            ? ValidateNormalSwShDomains(paths, session, domains)
            : ValidateSingleSwShDomain(paths, session, domain);
    }

    private ChangePlan CreateSwShChangePlan(ProjectPaths paths, EditSession session)
    {
        var domain = GetEditSessionDomain(session);
        return domain == EditSessionDomain.Mixed && TryGetNormalSwShDomains(session, out var domains)
            ? CreateNormalSwShChangePlan(paths, session, domains)
            : CreateSingleSwShChangePlan(paths, session, domain);
    }

    private ApplyResult ApplySwShChangePlan(ProjectPaths paths, EditSession session, ChangePlan reviewedPlan)
    {
        var domain = GetEditSessionDomain(session);
        return domain == EditSessionDomain.Mixed && TryGetNormalSwShDomains(session, out var domains)
            ? ApplyNormalSwShChangePlan(paths, session, reviewedPlan, domains)
            : ApplySingleSwShChangePlan(paths, session, reviewedPlan, domain);
    }

    private SwShEditSessionValidation ValidateSingleSwShDomain(
        ProjectPaths paths,
        EditSession session,
        EditSessionDomain domain)
    {
        return domain switch
        {
            EditSessionDomain.DynamaxAdventures => dynamaxAdventuresEditSessionService.Validate(paths, session),
            EditSessionDomain.Encounters => encountersEditSessionService.Validate(paths, session),
            EditSessionDomain.ExeFsPatches => exeFsPatchEditSessionService.Validate(paths, session),
            EditSessionDomain.BagHook => bagHookEditSessionService.Validate(paths, session),
            EditSessionDomain.CatchCap => catchCapEditSessionService.Validate(paths, session),
            EditSessionDomain.HyperTraining => hyperTrainingEditSessionService.Validate(paths, session),
            EditSessionDomain.ShinyRate => shinyRateEditSessionService.Validate(paths, session),
            EditSessionDomain.TypeChart => typeChartEditSessionService.Validate(paths, session),
            EditSessionDomain.FairyGymBoosts => fairyGymBoostsEditSessionService.Validate(paths, session),
            EditSessionDomain.FashionUnlock => fashionUnlockEditSessionService.Validate(paths, session),
            EditSessionDomain.GymUniformRemoval => gymUniformRemovalEditSessionService.Validate(paths, session),
            EditSessionDomain.IvScreen => ivScreenEditSessionService.Validate(paths, session),
            EditSessionDomain.GiftPokemon => giftPokemonEditSessionService.Validate(paths, session),
            EditSessionDomain.TradePokemon => tradePokemonEditSessionService.Validate(paths, session),
            EditSessionDomain.RentalPokemon => rentalPokemonEditSessionService.Validate(paths, session),
            EditSessionDomain.Placement => placementEditSessionService.Validate(paths, session),
            EditSessionDomain.Behavior => behaviorEditSessionService.Validate(paths, session),
            EditSessionDomain.RaidBattles => raidBattlesEditSessionService.Validate(paths, session),
            EditSessionDomain.RaidRewards => raidRewardsEditSessionService.Validate(paths, session),
            EditSessionDomain.RaidBonusRewards => raidRewardsEditSessionService.Validate(paths, session),
            EditSessionDomain.StaticEncounters => staticEncountersEditSessionService.Validate(paths, session),
            EditSessionDomain.Trainers => trainersEditSessionService.Validate(paths, session),
            EditSessionDomain.Shops => shopsEditSessionService.Validate(paths, session),
            EditSessionDomain.Text => textEditSessionService.Validate(paths, session),
            EditSessionDomain.Items => itemsEditSessionService.Validate(paths, session),
            EditSessionDomain.Pokemon => pokemonEditSessionService.Validate(paths, session),
            EditSessionDomain.Moves => movesEditSessionService.Validate(paths, session),
            EditSessionDomain.RoyalCandy => royalCandyEditSessionService.Validate(paths, session),
            EditSessionDomain.StartingItems => startingItemsEditSessionService.Validate(paths, session),
            EditSessionDomain.NpcItemGift => npcItemGiftEditSessionService.Validate(paths, session),
            EditSessionDomain.Mixed => CreateUnsupportedMixedValidation(session),
            _ => itemsEditSessionService.Validate(paths, session),
        };
    }

    private ChangePlan CreateSingleSwShChangePlan(
        ProjectPaths paths,
        EditSession session,
        EditSessionDomain domain)
    {
        return domain switch
        {
            EditSessionDomain.DynamaxAdventures => dynamaxAdventuresEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Encounters => encountersEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.ExeFsPatches => exeFsPatchEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.BagHook => bagHookEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.CatchCap => catchCapEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.HyperTraining => hyperTrainingEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.ShinyRate => shinyRateEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.TypeChart => typeChartEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.FairyGymBoosts => fairyGymBoostsEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.FashionUnlock => fashionUnlockEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.GymUniformRemoval => gymUniformRemovalEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.IvScreen => ivScreenEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.GiftPokemon => giftPokemonEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.TradePokemon => tradePokemonEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.RentalPokemon => rentalPokemonEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Placement => placementEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Behavior => behaviorEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.RaidBattles => raidBattlesEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.RaidRewards => raidRewardsEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.RaidBonusRewards => raidRewardsEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.StaticEncounters => staticEncountersEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Trainers => trainersEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Shops => shopsEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Text => textEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Items => itemsEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Pokemon => pokemonEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Moves => movesEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.RoyalCandy => royalCandyEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.StartingItems => startingItemsEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.NpcItemGift => npcItemGiftEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Mixed => CreateUnsupportedMixedChangePlan(session),
            _ => itemsEditSessionService.CreateChangePlan(paths, session),
        };
    }

    private ApplyResult ApplySingleSwShChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        EditSessionDomain domain)
    {
        return domain switch
        {
            EditSessionDomain.DynamaxAdventures => dynamaxAdventuresEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.Encounters => encountersEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.ExeFsPatches => exeFsPatchEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.BagHook => bagHookEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.CatchCap => catchCapEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.HyperTraining => hyperTrainingEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.ShinyRate => shinyRateEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.TypeChart => typeChartEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.FairyGymBoosts => fairyGymBoostsEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.FashionUnlock => fashionUnlockEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.GymUniformRemoval => gymUniformRemovalEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.IvScreen => ivScreenEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.GiftPokemon => giftPokemonEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.TradePokemon => tradePokemonEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.RentalPokemon => rentalPokemonEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.Placement => placementEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.Behavior => behaviorEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.RaidBattles => raidBattlesEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.RaidRewards => raidRewardsEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.RaidBonusRewards => raidRewardsEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.StaticEncounters => staticEncountersEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.Trainers => trainersEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.Shops => shopsEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.Text => textEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.Items => itemsEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.Pokemon => pokemonEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.Moves => movesEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.RoyalCandy => royalCandyEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.StartingItems => startingItemsEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.NpcItemGift => npcItemGiftEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
            EditSessionDomain.Mixed => CreateUnsupportedMixedApplyResult(session),
            _ => itemsEditSessionService.ApplyChangePlan(paths, session, reviewedPlan),
        };
    }

    private SwShEditSessionValidation ValidateNormalSwShDomains(
        ProjectPaths paths,
        EditSession session,
        IReadOnlyList<EditSessionDomain> domains)
    {
        var diagnostics = new List<ValidationDiagnostic>();
        foreach (var domain in domains)
        {
            var validation = ValidateSingleSwShDomain(paths, SliceSession(session, domain), domain);
            diagnostics.AddRange(validation.Diagnostics);
        }

        return new SwShEditSessionValidation(
            session,
            diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            diagnostics);
    }

    private ChangePlan CreateNormalSwShChangePlan(
        ProjectPaths paths,
        EditSession session,
        IReadOnlyList<EditSessionDomain> domains)
    {
        var validation = ValidateNormalSwShDomains(paths, session, domains);
        var diagnostics = validation.Diagnostics.ToList();
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        var writes = new List<PlannedFileWrite>();
        foreach (var domain in domains)
        {
            var domainPlan = CreateSingleSwShChangePlan(paths, SliceSession(session, domain), domain);
            diagnostics.AddRange(domainPlan.Diagnostics);
            writes.AddRange(domainPlan.Writes);
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), diagnostics);
        }

        return new ChangePlan(session.Id, CombinePlannedWrites(writes), diagnostics);
    }

    private ApplyResult ApplyNormalSwShChangePlan(
        ProjectPaths paths,
        EditSession session,
        ChangePlan reviewedPlan,
        IReadOnlyList<EditSessionDomain> domains)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var currentPlan = CreateNormalSwShChangePlan(paths, session, domains);
        var diagnostics = currentPlan.Diagnostics.ToList();
        var writtenFiles = new List<ProjectFileReference>();

        if (!ReviewedPlanMatchesCurrentPlan(reviewedPlan, currentPlan))
        {
            diagnostics.Add(new ValidationDiagnostic(
                DiagnosticSeverity.Error,
                "Reviewed change plan is stale. Review the change plan again before applying.",
                Domain: "workflow.editSession",
                Expected: "Current reviewed Sword/Shield change plan"));
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return CreateCombinedApplyResult(applyId, appliedAt, currentPlan, writtenFiles, diagnostics);
        }

        foreach (var domain in domains)
        {
            var domainSession = SliceSession(session, domain);
            var domainPlan = CreateSingleSwShChangePlan(paths, domainSession, domain);
            var result = ApplySingleSwShChangePlan(paths, domainSession, domainPlan, domain);
            diagnostics.AddRange(result.Diagnostics);
            writtenFiles.AddRange(result.WrittenFiles);

            if (result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                break;
            }
        }

        return CreateCombinedApplyResult(
            applyId,
            appliedAt,
            currentPlan,
            writtenFiles.Distinct().ToArray(),
            diagnostics);
    }

    private static ApplyResult CreateCombinedApplyResult(
        string applyId,
        DateTimeOffset appliedAt,
        ChangePlan currentPlan,
        IReadOnlyList<ProjectFileReference> writtenFiles,
        IReadOnlyList<ValidationDiagnostic> diagnostics)
    {
        return new ApplyResult(
            applyId,
            appliedAt,
            writtenFiles,
            new WriteManifest(applyId, appliedAt, currentPlan.Writes),
            diagnostics);
    }

    private static bool TryGetNormalSwShDomains(
        EditSession session,
        out IReadOnlyList<EditSessionDomain> domains)
    {
        var orderedDomains = session.PendingEdits
            .Select(edit => GetEditSessionDomain(edit.Domain))
            .Where(domain => domain != EditSessionDomain.None)
            .Distinct()
            .ToArray();

        domains = orderedDomains;
        return orderedDomains.Length > 1 && orderedDomains.All(IsNormalSwShDomain);
    }

    private static EditSessionDomain GetEditSessionDomain(string? domain)
    {
        return domain switch
        {
            "workflow.items" => EditSessionDomain.Items,
            "workflow.moves" => EditSessionDomain.Moves,
            "workflow.text" => EditSessionDomain.Text,
            "workflow.pokemon" => EditSessionDomain.Pokemon,
            "workflow.trainers" => EditSessionDomain.Trainers,
            "workflow.shops" => EditSessionDomain.Shops,
            "workflow.encounters" => EditSessionDomain.Encounters,
            "workflow.exefsPatches" => EditSessionDomain.ExeFsPatches,
            "workflow.bagHook" => EditSessionDomain.BagHook,
            "workflow.catchCap" => EditSessionDomain.CatchCap,
            "workflow.hyperTraining" => EditSessionDomain.HyperTraining,
            "workflow.shinyRate" => EditSessionDomain.ShinyRate,
            "workflow.typeChart" => EditSessionDomain.TypeChart,
            "workflow.fairyGymBoosts" => EditSessionDomain.FairyGymBoosts,
            "workflow.fashionUnlock" => EditSessionDomain.FashionUnlock,
            "workflow.gymUniformRemoval" => EditSessionDomain.GymUniformRemoval,
            "workflow.ivScreen" => EditSessionDomain.IvScreen,
            "workflow.giftPokemon" => EditSessionDomain.GiftPokemon,
            "workflow.tradePokemon" => EditSessionDomain.TradePokemon,
            "workflow.rentalPokemon" => EditSessionDomain.RentalPokemon,
            "workflow.dynamaxAdventures" => EditSessionDomain.DynamaxAdventures,
            "workflow.staticEncounters" => EditSessionDomain.StaticEncounters,
            "workflow.placement" => EditSessionDomain.Placement,
            "workflow.behavior" => EditSessionDomain.Behavior,
            "workflow.raidBattles" => EditSessionDomain.RaidBattles,
            "workflow.raidRewards" => EditSessionDomain.RaidRewards,
            "workflow.raidBonusRewards" => EditSessionDomain.RaidBonusRewards,
            "workflow.royalCandy" => EditSessionDomain.RoyalCandy,
            "workflow.startingItems" => EditSessionDomain.StartingItems,
            "workflow.npcItemGift" => EditSessionDomain.NpcItemGift,
            null or "" => EditSessionDomain.None,
            _ => EditSessionDomain.Mixed,
        };
    }

    private static bool IsNormalSwShDomain(EditSessionDomain domain)
    {
        return domain is
            EditSessionDomain.Items or
            EditSessionDomain.Moves or
            EditSessionDomain.Text or
            EditSessionDomain.Pokemon or
            EditSessionDomain.Trainers or
            EditSessionDomain.Shops or
            EditSessionDomain.Encounters or
            EditSessionDomain.GiftPokemon or
            EditSessionDomain.TradePokemon or
            EditSessionDomain.RentalPokemon or
            EditSessionDomain.StaticEncounters or
            EditSessionDomain.Placement or
            EditSessionDomain.Behavior or
            EditSessionDomain.RaidBattles or
            EditSessionDomain.RaidRewards or
            EditSessionDomain.RaidBonusRewards;
    }

    private static EditSession SliceSession(EditSession session, EditSessionDomain domain)
    {
        var domainName = GetEditSessionDomainName(domain);
        return session with
        {
            PendingEdits = session.PendingEdits
                .Where(edit => string.Equals(edit.Domain, domainName, StringComparison.Ordinal))
                .ToArray(),
        };
    }

    private static string GetEditSessionDomainName(EditSessionDomain domain)
    {
        return domain switch
        {
            EditSessionDomain.Items => "workflow.items",
            EditSessionDomain.Moves => "workflow.moves",
            EditSessionDomain.Text => "workflow.text",
            EditSessionDomain.Pokemon => "workflow.pokemon",
            EditSessionDomain.Trainers => "workflow.trainers",
            EditSessionDomain.Shops => "workflow.shops",
            EditSessionDomain.Encounters => "workflow.encounters",
            EditSessionDomain.ExeFsPatches => "workflow.exefsPatches",
            EditSessionDomain.BagHook => "workflow.bagHook",
            EditSessionDomain.CatchCap => "workflow.catchCap",
            EditSessionDomain.HyperTraining => "workflow.hyperTraining",
            EditSessionDomain.ShinyRate => "workflow.shinyRate",
            EditSessionDomain.TypeChart => "workflow.typeChart",
            EditSessionDomain.FairyGymBoosts => "workflow.fairyGymBoosts",
            EditSessionDomain.FashionUnlock => "workflow.fashionUnlock",
            EditSessionDomain.GymUniformRemoval => "workflow.gymUniformRemoval",
            EditSessionDomain.IvScreen => "workflow.ivScreen",
            EditSessionDomain.GiftPokemon => "workflow.giftPokemon",
            EditSessionDomain.TradePokemon => "workflow.tradePokemon",
            EditSessionDomain.RentalPokemon => "workflow.rentalPokemon",
            EditSessionDomain.DynamaxAdventures => "workflow.dynamaxAdventures",
            EditSessionDomain.StaticEncounters => "workflow.staticEncounters",
            EditSessionDomain.Placement => "workflow.placement",
            EditSessionDomain.Behavior => "workflow.behavior",
            EditSessionDomain.RaidBattles => "workflow.raidBattles",
            EditSessionDomain.RaidRewards => "workflow.raidRewards",
            EditSessionDomain.RaidBonusRewards => "workflow.raidBonusRewards",
            EditSessionDomain.RoyalCandy => "workflow.royalCandy",
            EditSessionDomain.StartingItems => "workflow.startingItems",
            EditSessionDomain.NpcItemGift => "workflow.npcItemGift",
            _ => string.Empty,
        };
    }

    private static IReadOnlyList<PlannedFileWrite> CombinePlannedWrites(IEnumerable<PlannedFileWrite> writes)
    {
        return writes
            .GroupBy(write => write.TargetRelativePath, StringComparer.Ordinal)
            .Select(group =>
            {
                var groupedWrites = group.ToArray();
                if (groupedWrites.Length == 1)
                {
                    return groupedWrites[0];
                }

                return new PlannedFileWrite(
                    group.Key,
                    groupedWrites
                        .SelectMany(write => write.Sources)
                        .Distinct()
                        .ToArray(),
                    groupedWrites.Any(write => write.ReplacesExistingOutput),
                    string.Join(
                        " ",
                        groupedWrites
                            .Select(write => write.Reason)
                            .Where(reason => !string.IsNullOrWhiteSpace(reason))
                            .Distinct(StringComparer.Ordinal)));
            })
            .OrderBy(write => write.TargetRelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ReviewedPlanMatchesCurrentPlan(ChangePlan reviewedPlan, ChangePlan currentPlan)
    {
        if (!reviewedPlan.CanApply
            || reviewedPlan.SessionId != currentPlan.SessionId
            || reviewedPlan.Writes.Count != currentPlan.Writes.Count)
        {
            return false;
        }

        var reviewedTargets = reviewedPlan.Writes
            .Select(write => write.TargetRelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var currentTargets = currentPlan.Writes
            .Select(write => write.TargetRelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return reviewedTargets.SequenceEqual(currentTargets, StringComparer.Ordinal);
    }

    private static EditSessionDomain GetEditSessionDomain(EditSession session)
    {
        var domains = session.PendingEdits
            .Select(edit => edit.Domain)
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return domains switch
        {
            [] => EditSessionDomain.None,
            ["workflow.items"] => EditSessionDomain.Items,
            ["workflow.moves"] => EditSessionDomain.Moves,
            ["workflow.text"] => EditSessionDomain.Text,
            ["workflow.pokemon"] => EditSessionDomain.Pokemon,
            ["workflow.trainers"] => EditSessionDomain.Trainers,
            ["workflow.shops"] => EditSessionDomain.Shops,
            ["workflow.encounters"] => EditSessionDomain.Encounters,
            ["workflow.exefsPatches"] => EditSessionDomain.ExeFsPatches,
            ["workflow.bagHook"] => EditSessionDomain.BagHook,
            ["workflow.catchCap"] => EditSessionDomain.CatchCap,
            ["workflow.hyperTraining"] => EditSessionDomain.HyperTraining,
            ["workflow.shinyRate"] => EditSessionDomain.ShinyRate,
            ["workflow.typeChart"] => EditSessionDomain.TypeChart,
            ["workflow.fairyGymBoosts"] => EditSessionDomain.FairyGymBoosts,
            ["workflow.fashionUnlock"] => EditSessionDomain.FashionUnlock,
            ["workflow.gymUniformRemoval"] => EditSessionDomain.GymUniformRemoval,
            ["workflow.ivScreen"] => EditSessionDomain.IvScreen,
            ["workflow.giftPokemon"] => EditSessionDomain.GiftPokemon,
            ["workflow.tradePokemon"] => EditSessionDomain.TradePokemon,
            ["workflow.rentalPokemon"] => EditSessionDomain.RentalPokemon,
            ["workflow.dynamaxAdventures"] => EditSessionDomain.DynamaxAdventures,
            ["workflow.staticEncounters"] => EditSessionDomain.StaticEncounters,
            ["workflow.placement"] => EditSessionDomain.Placement,
            ["workflow.behavior"] => EditSessionDomain.Behavior,
            ["workflow.raidBattles"] => EditSessionDomain.RaidBattles,
            ["workflow.raidRewards"] => EditSessionDomain.RaidRewards,
            ["workflow.raidBonusRewards"] => EditSessionDomain.RaidBonusRewards,
            ["workflow.royalCandy"] => EditSessionDomain.RoyalCandy,
            ["workflow.startingItems"] => EditSessionDomain.StartingItems,
            ["workflow.npcItemGift"] => EditSessionDomain.NpcItemGift,
            _ => EditSessionDomain.Mixed,
        };
    }

    private static bool IsScarletViolet(ProjectPaths paths)
    {
        return paths.SelectedGame is ProjectGame.Scarlet or ProjectGame.Violet;
    }

    private static bool IsPokemonLegendsZA(ProjectPaths paths)
    {
        return paths.SelectedGame is ProjectGame.ZA;
    }

    private static string? ValidateCommandGameScope(BridgeCommandEnvelope? envelope, string requestJson)
    {
        if (envelope?.Command is not { } command || !TryReadSelectedGame(requestJson, out var selectedGame))
        {
            return null;
        }

        if (IsSwordShieldOnlyCommand(command) && !IsSwordShield(selectedGame))
        {
            return SerializeFailure(
                "bridge.gameMismatch",
                $"Bridge command '{command}' is only available for Sword/Shield projects.",
                envelope.RequestId);
        }

        if (IsScarletVioletOnlyCommand(command)
            && !IsScarletViolet(selectedGame)
            && !((command is KmCommandNames.UpdateItemFields or KmCommandNames.UpdateTrainerFields)
                && IsPokemonLegendsZA(selectedGame)))
        {
            return SerializeFailure(
                "bridge.gameMismatch",
                $"Bridge command '{command}' is only available for Scarlet/Violet projects.",
                envelope.RequestId);
        }

        if (IsPokemonLegendsZAOnlyCommand(command) && !IsPokemonLegendsZA(selectedGame))
        {
            return SerializeFailure(
                "bridge.gameMismatch",
                $"Bridge command '{command}' is only available for Pokemon Legends Z-A projects.",
                envelope.RequestId);
        }

        if (IsPokemonLegendsZA(selectedGame) && !IsPokemonLegendsZAAllowedCommand(command))
        {
            return SerializeFailure(
                "bridge.gameMismatch",
                $"Bridge command '{command}' is not available for Pokemon Legends Z-A projects yet.",
                envelope.RequestId);
        }

        return null;
    }

    private static bool TryReadSelectedGame(string requestJson, out ProjectGameDto selectedGame)
    {
        selectedGame = default;

        var request = JsonSerializer.Deserialize<BridgeRequest<JsonElement>>(requestJson, BridgeJson.SerializerOptions);
        if (request?.Payload.ValueKind is not JsonValueKind.Object
            || !request.Payload.TryGetProperty("paths", out var paths)
            || paths.ValueKind is not JsonValueKind.Object
            || !paths.TryGetProperty("selectedGame", out var selectedGameJson)
            || selectedGameJson.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        var parsedGame = selectedGameJson.Deserialize<ProjectGameDto?>(BridgeJson.SerializerOptions);
        if (parsedGame is null)
        {
            return false;
        }

        selectedGame = parsedGame.Value;
        return true;
    }

    private static bool IsSwordShield(ProjectGameDto game)
    {
        return game is ProjectGameDto.Sword or ProjectGameDto.Shield;
    }

    private static bool IsScarletViolet(ProjectGameDto game)
    {
        return game is ProjectGameDto.Scarlet or ProjectGameDto.Violet;
    }

    private static bool IsPokemonLegendsZA(ProjectGameDto game)
    {
        return game is ProjectGameDto.ZA;
    }

    private static bool IsSwordShieldOnlyCommand(string command)
    {
        return command is
            KmCommandNames.LoadRentalPokemonWorkflow or
            KmCommandNames.UpdateRentalPokemonField or
            KmCommandNames.LoadDynamaxAdventuresWorkflow or
            KmCommandNames.UpdateDynamaxAdventureField or
            KmCommandNames.PreviewDynamaxAdventureDefaults or
            KmCommandNames.PlanDynamaxAdventureSeed or
            KmCommandNames.SearchDynamaxAdventureSeed or
            KmCommandNames.SetDynamaxAdventureSaveSeed or
            KmCommandNames.LoadRaidBattlesWorkflow or
            KmCommandNames.UpdateRaidBattleSlotField or
            KmCommandNames.LoadRaidRewardsWorkflow or
            KmCommandNames.UpdateRaidRewardField or
            KmCommandNames.LoadRaidBonusRewardsWorkflow or
            KmCommandNames.UpdateRaidBonusRewardField or
            KmCommandNames.LoadBehaviorWorkflow or
            KmCommandNames.UpdateBehaviorEntryField or
            KmCommandNames.LoadFlagworkSaveWorkflow or
            KmCommandNames.LoadBagHookWorkflow or
            KmCommandNames.StageBagHookInstall or
            KmCommandNames.StageBagHookUninstall or
            KmCommandNames.LoadCatchCapWorkflow or
            KmCommandNames.StageCatchCap or
            KmCommandNames.StageCatchCapUninstall or
            KmCommandNames.LoadHyperTrainingWorkflow or
            KmCommandNames.StageHyperTraining or
            KmCommandNames.LoadShinyRateWorkflow or
            KmCommandNames.StageShinyRate or
            KmCommandNames.LoadFairyGymBoostsWorkflow or
            KmCommandNames.StageFairyGymBoosts or
            KmCommandNames.LoadGymUniformRemovalWorkflow or
            KmCommandNames.StageGymUniformRemovalInstall or
            KmCommandNames.StageGymUniformRemovalUninstall or
            KmCommandNames.LoadIvScreenWorkflow or
            KmCommandNames.StageIvScreenInstall or
            KmCommandNames.StageIvScreenUninstall or
            KmCommandNames.LoadExeFsPatchWorkflow or
            KmCommandNames.StageExeFsPatch or
            KmCommandNames.LoadRoyalCandyWorkflow or
            KmCommandNames.StageRoyalCandyWorkflow or
            KmCommandNames.LoadStartingItemsWorkflow or
            KmCommandNames.StageStartingItems or
            KmCommandNames.LoadNpcItemGiftWorkflow or
            KmCommandNames.StageNpcItemGift or
            KmCommandNames.LoadModMergerWorkflow or
            KmCommandNames.StageModMerge or
            KmCommandNames.ApplyModMerge or
            KmCommandNames.LoadFpsPatch or
            KmCommandNames.ApplyFpsPatch or
            KmCommandNames.RestoreFpsPatch or
            KmCommandNames.ImportRandomizerSeed or
            KmCommandNames.ApplyRandomizer or
            KmCommandNames.RestoreRandomizer;
    }

    private static bool IsScarletVioletOnlyCommand(string command)
    {
        return command is
            KmCommandNames.UpdateItemFields or
            KmCommandNames.UpdateTrainerFields or
            KmCommandNames.UpdateGiftPokemonFields or
            KmCommandNames.UpdateTradePokemonFields or
            KmCommandNames.UpdateEncounterSlotFields or
            KmCommandNames.LoadTeraRaidsWorkflow or
            KmCommandNames.UpdateTeraRaidField or
            KmCommandNames.UpdateTeraRaidFields or
            KmCommandNames.UpdatePlacementObjectFields or
            KmCommandNames.StageTypeChartUninstall or
            KmCommandNames.LoadHyperspaceBypassWorkflow or
            KmCommandNames.StageHyperspaceBypassInstall or
            KmCommandNames.StageHyperspaceBypassUninstall or
            KmCommandNames.LoadSvModMergerWorkflow or
            KmCommandNames.StageSvModMerge or
            KmCommandNames.ApplySvModMerge or
            KmCommandNames.GetSvCacheStatus or
            KmCommandNames.UpdateSvCacheSettings or
            KmCommandNames.ClearSvCache or
            KmCommandNames.WarmupSvCacheStep;
    }

    private static bool IsPokemonLegendsZAOnlyCommand(string command)
    {
        return command is
            KmCommandNames.GetZaCacheStatus or
            KmCommandNames.UpdateZaCacheSettings or
            KmCommandNames.ClearZaCache or
            KmCommandNames.WarmupZaCacheStep;
    }

    private static bool IsPokemonLegendsZAAllowedCommand(string command)
    {
        return command is
            KmCommandNames.OpenProject or
            KmCommandNames.ValidateProject or
            KmCommandNames.RefreshFileGraph or
            KmCommandNames.ListWorkflows or
            KmCommandNames.LoadItemsWorkflow or
            KmCommandNames.UpdateItemField or
            KmCommandNames.UpdateItemFields or
            KmCommandNames.LoadPokemonWorkflow or
            KmCommandNames.UpdatePokemonField or
            KmCommandNames.UpdatePokemonFields or
            KmCommandNames.UpdatePokemonLearnset or
            KmCommandNames.UpdatePokemonEvolution or
            KmCommandNames.LoadTrainersWorkflow or
            KmCommandNames.UpdateTrainerField or
            KmCommandNames.UpdateTrainerFields or
            KmCommandNames.LoadMovesWorkflow or
            KmCommandNames.UpdateMoveField or
            KmCommandNames.UpdateMoveFields or
            KmCommandNames.LoadShopsWorkflow or
            KmCommandNames.UpdateShopInventoryItem or
            KmCommandNames.StartEditSession or
            KmCommandNames.ValidateEditSession or
            KmCommandNames.CreateChangePlan or
            KmCommandNames.ApplyChangePlan or
            KmCommandNames.GetZaCacheStatus or
            KmCommandNames.UpdateZaCacheSettings or
            KmCommandNames.ClearZaCache or
            KmCommandNames.WarmupZaCacheStep or
            KmCommandNames.LoadGameDumpWorkflow or
            KmCommandNames.RunGameDump;
    }

    private static SwShEditSessionValidation CreateUnsupportedMixedValidation(EditSession session)
    {
        var diagnostics = new[]
        {
            CreateMixedSessionDiagnostic(),
        };

        return new SwShEditSessionValidation(session, IsValid: false, diagnostics);
    }

    private static ChangePlan CreateUnsupportedMixedChangePlan(EditSession session)
    {
        return new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), [CreateMixedSessionDiagnostic()]);
    }

    private static ApplyResult CreateUnsupportedMixedApplyResult(EditSession session)
    {
        var applyId = Guid.NewGuid().ToString("N");
        var appliedAt = DateTimeOffset.UtcNow;
        var emptyPlan = new ChangePlan(session.Id, Array.Empty<PlannedFileWrite>(), [CreateMixedSessionDiagnostic()]);

        return new ApplyResult(
            applyId,
            appliedAt,
            Array.Empty<ProjectFileReference>(),
            new WriteManifest(applyId, appliedAt, emptyPlan.Writes),
            emptyPlan.Diagnostics);
    }

    private static FpsPatchStatusDto ToDto(SwShFpsPatchStatus status)
    {
        return new FpsPatchStatusDto(
            status.Status,
            status.Message,
            status.BuildId,
            status.DetectedGame is null ? null : ProjectBridgeMapper.ToDto(status.DetectedGame.Value),
            status.PatchedMainSiteCount,
            status.MainSiteCount,
            status.PatchedRomFsFileCount,
            status.ManagedRomFsFileCount,
            status.ConflictingRomFsFileCount,
            status.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ValidationDiagnostic CreateMixedSessionDiagnostic()
    {
        return new ValidationDiagnostic(
            DiagnosticSeverity.Error,
            "Edit sessions cannot mix workflow domains in one change plan yet.",
            Domain: "workflow.editSession",
            Expected: "Pending edits from one workflow domain");
    }

    private static SwShRandomizerConfig ToCore(RandomizerConfigDto config)
    {
        return new SwShRandomizerConfig(config.UserSeed, ToCore(config.Options), config.RollSeed, config.OutputHash);
    }

    private static SvModMergerSourceRequest ToCore(SvModMergerSourceDto source)
    {
        return new SvModMergerSourceRequest(source.Path, source.IsEnabled);
    }

    private static SwShRandomizerOptions ToCore(RandomizerOptionsDto options)
    {
        return new SwShRandomizerOptions(
            options.RandomizePokemonStats,
            options.ShufflePokemonStats,
            options.StatHp,
            options.StatAttack,
            options.StatDefense,
            options.StatSpecialAttack,
            options.StatSpecialDefense,
            options.StatSpeed,
            options.RandomizePokemonTypes,
            options.TypePrimary,
            options.TypeSecondary,
            options.AllowSameType,
            options.RandomizePokemonAbilities,
            options.Ability1,
            options.Ability2,
            options.HiddenAbility,
            options.RandomizePokemonHeldItems,
            options.RandomizePokemonCatchRates,
            options.RandomizePokemonLearnsets,
            options.LearnsetStabFirst,
            options.LearnsetExpandTo25,
            options.LearnsetBanFixedDamageMoves,
            options.LearnsetRequireDamagingMove,
            options.RandomizePokemonCompatibility,
            options.CompatibilityMachines,
            options.CompatibilityRecords,
            options.CompatibilityTutors,
            options.RandomizePokemonEvolutions,
            options.RandomizeWildEncounters,
            options.RandomizeStaticEncounters,
            options.RandomizeGiftEncounters,
            options.RandomizeRaidRewards,
            options.RandomizeRaidBonusRewards,
            options.RandomizeTypeChart,
            options.TypeChartNoImmunities,
            options.TypeChartOneImmunityPerType);
    }

    private static RandomizerConfigDto ToDto(SwShRandomizerConfig config)
    {
        return new RandomizerConfigDto(config.UserSeed, ToDto(config.Options), config.RollSeed, config.OutputHash);
    }

    private static RandomizerOptionsDto ToDto(SwShRandomizerOptions options)
    {
        return new RandomizerOptionsDto(
            options.RandomizePokemonStats,
            options.ShufflePokemonStats,
            options.StatHp,
            options.StatAttack,
            options.StatDefense,
            options.StatSpecialAttack,
            options.StatSpecialDefense,
            options.StatSpeed,
            options.RandomizePokemonTypes,
            options.TypePrimary,
            options.TypeSecondary,
            options.AllowSameType,
            options.RandomizePokemonAbilities,
            options.Ability1,
            options.Ability2,
            options.HiddenAbility,
            options.RandomizePokemonHeldItems,
            options.RandomizePokemonCatchRates,
            options.RandomizePokemonLearnsets,
            options.LearnsetStabFirst,
            options.LearnsetExpandTo25,
            options.LearnsetBanFixedDamageMoves,
            options.LearnsetRequireDamagingMove,
            options.RandomizePokemonCompatibility,
            options.CompatibilityMachines,
            options.CompatibilityRecords,
            options.CompatibilityTutors,
            options.RandomizePokemonEvolutions,
            options.RandomizeWildEncounters,
            options.RandomizeStaticEncounters,
            options.RandomizeGiftEncounters,
            options.RandomizeRaidRewards,
            options.RandomizeRaidBonusRewards,
            options.RandomizeTypeChart,
            options.TypeChartNoImmunities,
            options.TypeChartOneImmunityPerType);
    }

    private static bool TryParseSeed(string? value, out ulong seed)
    {
        seed = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(
                trimmed[2..],
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out seed);
        }

        return ulong.TryParse(
            trimmed,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out seed);
    }

    private static BridgeRequest<TPayload> DeserializeRequest<TPayload>(string requestJson)
    {
        var request = JsonSerializer.Deserialize<BridgeRequest<TPayload>>(requestJson, BridgeJson.SerializerOptions);

        if (request is null)
        {
            throw new JsonException("Bridge request could not be deserialized.");
        }

        if (request.Payload is null)
        {
            throw new JsonException("Bridge request payload is missing.");
        }

        return request;
    }

    private static string SerializeSuccess<TPayload>(TPayload payload, string? requestId)
    {
        var response = BridgeResponse<TPayload>.Success(payload, requestId);

        return JsonSerializer.Serialize(response, BridgeJson.SerializerOptions);
    }

    private static string SerializeFailure(string code, string message, string? requestId)
    {
        var response = BridgeResponse<object>.Failure(ApiError.Create(code, message), requestId);

        return JsonSerializer.Serialize(response, BridgeJson.SerializerOptions);
    }

    private sealed record BridgeCommandEnvelope(string? Command, string? RequestId);

    private enum EditSessionDomain
    {
        None,
        Items,
        Pokemon,
        Moves,
        Text,
        Trainers,
        Shops,
        Encounters,
        ExeFsPatches,
        BagHook,
        CatchCap,
        HyperTraining,
        ShinyRate,
        TypeChart,
        FairyGymBoosts,
        FashionUnlock,
        GymUniformRemoval,
        IvScreen,
        GiftPokemon,
        TradePokemon,
        RentalPokemon,
        DynamaxAdventures,
        StaticEncounters,
        Placement,
        Behavior,
        RaidBattles,
        RaidRewards,
        RaidBonusRewards,
        RoyalCandy,
        StartingItems,
        NpcItemGift,
        Mixed,
    }
}

