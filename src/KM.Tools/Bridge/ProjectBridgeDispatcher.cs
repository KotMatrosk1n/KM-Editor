// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Bridge;
using KM.Api.BagHook;
using KM.Api.Behavior;
using KM.Api.CatchCap;
using KM.Api.DynamaxAdventures;
using KM.Api.Editing;
using KM.Api.Encounters;
using KM.Api.ExeFs;
using KM.Api.Flagwork;
using KM.Api.Gifts;
using KM.Api.GymUniformRemoval;
using KM.Api.HyperTraining;
using KM.Api.Items;
using KM.Api.IvScreen;
using KM.Api.ModMerger;
using KM.Api.Moves;
using KM.Api.Placement;
using KM.Api.Pokemon;
using KM.Api.Projects;
using KM.Api.Raids;
using KM.Api.Randomizer;
using KM.Api.Rentals;
using KM.Api.RoyalCandy;
using KM.Api.Shops;
using KM.Api.SpreadsheetImport;
using KM.Api.StartingItems;
using KM.Api.StaticEncounters;
using KM.Api.Text;
using KM.Api.Trainers;
using KM.Api.Trades;
using KM.Api.TypeChart;
using KM.Api.Workflows;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Behavior;
using KM.SwSh.BagHook;
using KM.SwSh.CatchCap;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Encounters;
using KM.SwSh.ExeFs;
using KM.SwSh.Gifts;
using KM.SwSh.GymUniformRemoval;
using KM.SwSh.HyperTraining;
using KM.SwSh.Items;
using KM.SwSh.IvScreen;
using KM.SwSh.ModMerger;
using KM.SwSh.Moves;
using KM.SwSh.Placement;
using KM.SwSh.Pokemon;
using KM.SwSh.Raids;
using KM.SwSh.Randomizer;
using KM.SwSh.Rentals;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Shops;
using KM.SwSh.SpreadsheetImport;
using KM.SwSh.StartingItems;
using KM.SwSh.StaticEncounters;
using KM.SwSh.Text;
using KM.SwSh.Trainers;
using KM.SwSh.Trades;
using KM.SwSh.TypeChart;
using KM.SwSh.Workflows;
using KM.SV.ModMerger;
using KM.SV.Workflows;
using System.Text.Json;

namespace KM.Tools.Bridge;

public sealed class ProjectBridgeDispatcher
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShDynamaxAdventuresEditSessionService dynamaxAdventuresEditSessionService;
    private readonly SwShEncountersEditSessionService encountersEditSessionService;
    private readonly SwShExeFsPatchEditSessionService exeFsPatchEditSessionService;
    private readonly SwShBagHookEditSessionService bagHookEditSessionService;
    private readonly SwShCatchCapEditSessionService catchCapEditSessionService;
    private readonly SwShHyperTrainingEditSessionService hyperTrainingEditSessionService;
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
    private readonly SwShShopsEditSessionService shopsEditSessionService;
    private readonly SwShSpreadsheetImportExecutionService spreadsheetImportExecutionService;
    private readonly SwShModMergerWorkflowService modMergerWorkflowService;
    private readonly SwShRandomizerService randomizerService;
    private readonly SwShStaticEncountersEditSessionService staticEncountersEditSessionService;
    private readonly SwShTextEditSessionService textEditSessionService;
    private readonly SwShTrainersEditSessionService trainersEditSessionService;
    private readonly SwShTradePokemonEditSessionService tradePokemonEditSessionService;
    private readonly SwShWorkflowService swShWorkflowService;
    private readonly SvWorkflowService svWorkflowService;

    public ProjectBridgeDispatcher(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShDynamaxAdventuresEditSessionService? dynamaxAdventuresEditSessionService = null,
        SwShEncountersEditSessionService? encountersEditSessionService = null,
        SwShExeFsPatchEditSessionService? exeFsPatchEditSessionService = null,
        SwShBagHookEditSessionService? bagHookEditSessionService = null,
        SwShCatchCapEditSessionService? catchCapEditSessionService = null,
        SwShHyperTrainingEditSessionService? hyperTrainingEditSessionService = null,
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
        SwShShopsEditSessionService? shopsEditSessionService = null,
        SwShSpreadsheetImportExecutionService? spreadsheetImportExecutionService = null,
        SwShModMergerWorkflowService? modMergerWorkflowService = null,
        SwShRandomizerService? randomizerService = null,
        SwShStaticEncountersEditSessionService? staticEncountersEditSessionService = null,
        SwShTextEditSessionService? textEditSessionService = null,
        SwShTrainersEditSessionService? trainersEditSessionService = null,
        SwShTradePokemonEditSessionService? tradePokemonEditSessionService = null,
        SwShWorkflowService? swShWorkflowService = null,
        SvWorkflowService? svWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.dynamaxAdventuresEditSessionService = dynamaxAdventuresEditSessionService ?? new SwShDynamaxAdventuresEditSessionService(this.projectWorkspaceService);
        this.encountersEditSessionService = encountersEditSessionService ?? new SwShEncountersEditSessionService(this.projectWorkspaceService);
        this.exeFsPatchEditSessionService = exeFsPatchEditSessionService ?? new SwShExeFsPatchEditSessionService(this.projectWorkspaceService);
        this.bagHookEditSessionService = bagHookEditSessionService ?? new SwShBagHookEditSessionService(this.projectWorkspaceService);
        this.catchCapEditSessionService = catchCapEditSessionService ?? new SwShCatchCapEditSessionService(this.projectWorkspaceService);
        this.hyperTrainingEditSessionService = hyperTrainingEditSessionService ?? new SwShHyperTrainingEditSessionService(this.projectWorkspaceService);
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
        this.shopsEditSessionService = shopsEditSessionService ?? new SwShShopsEditSessionService(this.projectWorkspaceService);
        this.spreadsheetImportExecutionService = spreadsheetImportExecutionService ?? new SwShSpreadsheetImportExecutionService(this.projectWorkspaceService);
        this.modMergerWorkflowService = modMergerWorkflowService ?? new SwShModMergerWorkflowService(this.projectWorkspaceService);
        this.randomizerService = randomizerService ?? new SwShRandomizerService(this.projectWorkspaceService);
        this.staticEncountersEditSessionService = staticEncountersEditSessionService ?? new SwShStaticEncountersEditSessionService(this.projectWorkspaceService);
        this.textEditSessionService = textEditSessionService ?? new SwShTextEditSessionService(this.projectWorkspaceService);
        this.trainersEditSessionService = trainersEditSessionService ?? new SwShTrainersEditSessionService(this.projectWorkspaceService);
        this.tradePokemonEditSessionService = tradePokemonEditSessionService ?? new SwShTradePokemonEditSessionService(this.projectWorkspaceService);
        this.swShWorkflowService = swShWorkflowService ?? new SwShWorkflowService(
            this.projectWorkspaceService,
            modMergerWorkflowService: this.modMergerWorkflowService);
        this.svWorkflowService = svWorkflowService ?? new SvWorkflowService(this.projectWorkspaceService);
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

            return envelope?.Command switch
            {
                KmCommandNames.OpenProject => DispatchOpenProject(requestJson),
                KmCommandNames.ValidateProject => DispatchValidateProject(requestJson),
                KmCommandNames.RefreshFileGraph => DispatchRefreshFileGraph(requestJson),
                KmCommandNames.ListWorkflows => DispatchListWorkflows(requestJson),
                KmCommandNames.LoadItemsWorkflow => DispatchLoadItemsWorkflow(requestJson),
                KmCommandNames.UpdateItemField => DispatchUpdateItemField(requestJson),
                KmCommandNames.LoadPokemonWorkflow => DispatchLoadPokemonWorkflow(requestJson),
                KmCommandNames.UpdatePokemonField => DispatchUpdatePokemonField(requestJson),
                KmCommandNames.UpdatePokemonLearnset => DispatchUpdatePokemonLearnset(requestJson),
                KmCommandNames.UpdatePokemonEvolution => DispatchUpdatePokemonEvolution(requestJson),
                KmCommandNames.LoadMovesWorkflow => DispatchLoadMovesWorkflow(requestJson),
                KmCommandNames.UpdateMoveField => DispatchUpdateMoveField(requestJson),
                KmCommandNames.LoadTextWorkflow => DispatchLoadTextWorkflow(requestJson),
                KmCommandNames.UpdateTextEntry => DispatchUpdateTextEntry(requestJson),
                KmCommandNames.LoadTrainersWorkflow => DispatchLoadTrainersWorkflow(requestJson),
                KmCommandNames.UpdateTrainerField => DispatchUpdateTrainerField(requestJson),
                KmCommandNames.LoadGiftPokemonWorkflow => DispatchLoadGiftPokemonWorkflow(requestJson),
                KmCommandNames.UpdateGiftPokemonField => DispatchUpdateGiftPokemonField(requestJson),
                KmCommandNames.LoadTradePokemonWorkflow => DispatchLoadTradePokemonWorkflow(requestJson),
                KmCommandNames.UpdateTradePokemonField => DispatchUpdateTradePokemonField(requestJson),
                KmCommandNames.LoadStaticEncountersWorkflow => DispatchLoadStaticEncountersWorkflow(requestJson),
                KmCommandNames.UpdateStaticEncounterField => DispatchUpdateStaticEncounterField(requestJson),
                KmCommandNames.LoadRentalPokemonWorkflow => DispatchLoadRentalPokemonWorkflow(requestJson),
                KmCommandNames.UpdateRentalPokemonField => DispatchUpdateRentalPokemonField(requestJson),
                KmCommandNames.LoadDynamaxAdventuresWorkflow => DispatchLoadDynamaxAdventuresWorkflow(requestJson),
                KmCommandNames.UpdateDynamaxAdventureField => DispatchUpdateDynamaxAdventureField(requestJson),
                KmCommandNames.LoadShopsWorkflow => DispatchLoadShopsWorkflow(requestJson),
                KmCommandNames.UpdateShopInventoryItem => DispatchUpdateShopInventoryItem(requestJson),
                KmCommandNames.LoadEncountersWorkflow => DispatchLoadEncountersWorkflow(requestJson),
                KmCommandNames.UpdateEncounterSlotField => DispatchUpdateEncounterSlotField(requestJson),
                KmCommandNames.LoadRaidBattlesWorkflow => DispatchLoadRaidBattlesWorkflow(requestJson),
                KmCommandNames.UpdateRaidBattleSlotField => DispatchUpdateRaidBattleSlotField(requestJson),
                KmCommandNames.LoadRaidRewardsWorkflow => DispatchLoadRaidRewardsWorkflow(requestJson),
                KmCommandNames.UpdateRaidRewardField => DispatchUpdateRaidRewardField(requestJson),
                KmCommandNames.LoadRaidBonusRewardsWorkflow => DispatchLoadRaidBonusRewardsWorkflow(requestJson),
                KmCommandNames.UpdateRaidBonusRewardField => DispatchUpdateRaidBonusRewardField(requestJson),
                KmCommandNames.LoadPlacementWorkflow => DispatchLoadPlacementWorkflow(requestJson),
                KmCommandNames.UpdatePlacementObjectField => DispatchUpdatePlacementObjectField(requestJson),
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
                KmCommandNames.LoadGymUniformRemovalWorkflow => DispatchLoadGymUniformRemovalWorkflow(requestJson),
                KmCommandNames.StageGymUniformRemovalInstall => DispatchStageGymUniformRemovalInstall(requestJson),
                KmCommandNames.StageGymUniformRemovalUninstall => DispatchStageGymUniformRemovalUninstall(requestJson),
                KmCommandNames.LoadIvScreenWorkflow => DispatchLoadIvScreenWorkflow(requestJson),
                KmCommandNames.StageIvScreenInstall => DispatchStageIvScreenInstall(requestJson),
                KmCommandNames.StageIvScreenUninstall => DispatchStageIvScreenUninstall(requestJson),
                KmCommandNames.LoadTypeChartWorkflow => DispatchLoadTypeChartWorkflow(requestJson),
                KmCommandNames.StageTypeChart => DispatchStageTypeChart(requestJson),
                KmCommandNames.LoadExeFsPatchWorkflow => DispatchLoadExeFsPatchWorkflow(requestJson),
                KmCommandNames.StageExeFsPatch => DispatchStageExeFsPatch(requestJson),
                KmCommandNames.LoadRoyalCandyWorkflow => DispatchLoadRoyalCandyWorkflow(requestJson),
                KmCommandNames.StageRoyalCandyWorkflow => DispatchStageRoyalCandyWorkflow(requestJson),
                KmCommandNames.LoadStartingItemsWorkflow => DispatchLoadStartingItemsWorkflow(requestJson),
                KmCommandNames.StageStartingItems => DispatchStageStartingItems(requestJson),
                KmCommandNames.LoadSpreadsheetImportWorkflow => DispatchLoadSpreadsheetImportWorkflow(requestJson),
                KmCommandNames.PreviewSpreadsheetImport => DispatchPreviewSpreadsheetImport(requestJson),
                KmCommandNames.LoadModMergerWorkflow => DispatchLoadModMergerWorkflow(requestJson),
                KmCommandNames.StageModMerge => DispatchStageModMerge(requestJson),
                KmCommandNames.ApplyModMerge => DispatchApplyModMerge(requestJson),
                KmCommandNames.LoadSvModMergerWorkflow => DispatchLoadSvModMergerWorkflow(requestJson),
                KmCommandNames.StageSvModMerge => DispatchStageSvModMerge(requestJson),
                KmCommandNames.ApplySvModMerge => DispatchApplySvModMerge(requestJson),
                KmCommandNames.ImportRandomizerSeed => DispatchImportRandomizerSeed(requestJson),
                KmCommandNames.ApplyRandomizer => DispatchApplyRandomizer(requestJson),
                KmCommandNames.RestoreRandomizer => DispatchRestoreRandomizer(requestJson),
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
        var workflowList = IsScarletViolet(paths)
            ? svWorkflowService.List(paths)
            : swShWorkflowService.List(paths);
        var response = SwShBridgeMapper.ToDto(workflowList);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadItemsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadItemsWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var workflow = IsScarletViolet(paths)
            ? svWorkflowService.LoadItems(paths)
            : swShWorkflowService.LoadItems(paths);
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadPokemonWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadPokemonWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var workflow = IsScarletViolet(paths)
            ? svWorkflowService.LoadPokemon(paths)
            : swShWorkflowService.LoadPokemon(paths);
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdatePokemonField(string requestJson)
    {
        var request = DeserializeRequest<UpdatePokemonFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var result = IsScarletViolet(paths)
            ? svWorkflowService.UpdatePokemonField(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Field,
                request.Payload.Value)
            : pokemonEditSessionService.UpdateField(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Field,
                request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdatePokemonLearnset(string requestJson)
    {
        var request = DeserializeRequest<UpdatePokemonLearnsetRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var result = IsScarletViolet(paths)
            ? svWorkflowService.UpdatePokemonLearnset(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Action,
                request.Payload.Slot,
                request.Payload.MoveId,
                request.Payload.Level)
            : pokemonEditSessionService.UpdateLearnset(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Action,
                request.Payload.Slot,
                request.Payload.MoveId,
                request.Payload.Level);
        var response = SwShBridgeMapper.ToDtoLearnsetUpdate(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdatePokemonEvolution(string requestJson)
    {
        var request = DeserializeRequest<UpdatePokemonEvolutionRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var result = IsScarletViolet(paths)
            ? svWorkflowService.UpdatePokemonEvolution(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Action,
                request.Payload.Slot,
                request.Payload.Method,
                request.Payload.Argument,
                request.Payload.Species,
                request.Payload.Form,
                request.Payload.Level)
            : pokemonEditSessionService.UpdateEvolution(
                paths,
                session,
                request.Payload.PersonalId,
                request.Payload.Action,
                request.Payload.Slot,
                request.Payload.Method,
                request.Payload.Argument,
                request.Payload.Species,
                request.Payload.Form,
                request.Payload.Level);
        var response = SwShBridgeMapper.ToDtoEvolutionUpdate(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadMovesWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadMovesWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadMoves(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateMoveField(string requestJson)
    {
        var request = DeserializeRequest<UpdateMoveFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = movesEditSessionService.UpdateField(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.MoveId,
            request.Payload.Field,
            request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadTextWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadTextWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadText(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateTextEntry(string requestJson)
    {
        var request = DeserializeRequest<UpdateTextEntryRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = textEditSessionService.UpdateEntry(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.TextKey,
            request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadTrainersWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadTrainersWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var workflow = IsScarletViolet(paths)
            ? svWorkflowService.LoadTrainers(paths)
            : swShWorkflowService.LoadTrainers(paths);
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateTrainerField(string requestJson)
    {
        var request = DeserializeRequest<UpdateTrainerFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var result = IsScarletViolet(paths)
            ? svWorkflowService.UpdateTrainerField(
                paths,
                session,
                request.Payload.TrainerId,
                request.Payload.Slot,
                request.Payload.Field,
                request.Payload.Value)
            : trainersEditSessionService.UpdateField(
                paths,
                session,
                request.Payload.TrainerId,
                request.Payload.Slot,
                request.Payload.Field,
                request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadShopsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadShopsWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadShops(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadGiftPokemonWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadGiftPokemonWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadGiftPokemon(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateGiftPokemonField(string requestJson)
    {
        var request = DeserializeRequest<UpdateGiftPokemonFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = giftPokemonEditSessionService.UpdateField(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.GiftIndex,
            request.Payload.Field,
            request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadTradePokemonWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadTradePokemonWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadTradePokemon(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateTradePokemonField(string requestJson)
    {
        var request = DeserializeRequest<UpdateTradePokemonFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = tradePokemonEditSessionService.UpdateField(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.TradeIndex,
            request.Payload.Field,
            request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadStaticEncountersWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadStaticEncountersWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadStaticEncounters(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateStaticEncounterField(string requestJson)
    {
        var request = DeserializeRequest<UpdateStaticEncounterFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = staticEncountersEditSessionService.UpdateField(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.EncounterIndex,
            request.Payload.Field,
            request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

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

    private string DispatchUpdateShopInventoryItem(string requestJson)
    {
        var request = DeserializeRequest<UpdateShopInventoryItemRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = shopsEditSessionService.UpdateInventoryItem(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.ShopId,
            request.Payload.Slot,
            request.Payload.Field,
            request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadEncountersWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadEncountersWorkflowRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var workflow = IsScarletViolet(paths)
            ? svWorkflowService.LoadEncounters(paths)
            : swShWorkflowService.LoadEncounters(paths);
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateEncounterSlotField(string requestJson)
    {
        var request = DeserializeRequest<UpdateEncounterSlotFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var result = IsScarletViolet(paths)
            ? svWorkflowService.UpdateEncounterSlotField(
                paths,
                session,
                request.Payload.TableId,
                request.Payload.Slot,
                request.Payload.Field,
                request.Payload.Value)
            : encountersEditSessionService.UpdateSlotField(
                paths,
                session,
                request.Payload.TableId,
                request.Payload.Slot,
                request.Payload.Field,
                request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

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
        var workflow = swShWorkflowService.LoadPlacement(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdatePlacementObjectField(string requestJson)
    {
        var request = DeserializeRequest<UpdatePlacementObjectFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = placementEditSessionService.UpdateObjectField(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            session,
            request.Payload.ObjectId,
            request.Payload.Field,
            request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

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

    private string DispatchLoadTypeChartWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadTypeChartWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadTypeChart(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchStageTypeChart(string requestJson)
    {
        var request = DeserializeRequest<StageTypeChartRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = typeChartEditSessionService.StageChart(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.Values,
            session);
        var response = SwShBridgeMapper.ToDto(result);

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

    private string DispatchLoadSpreadsheetImportWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadSpreadsheetImportWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadSpreadsheetImport(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchPreviewSpreadsheetImport(string requestJson)
    {
        var request = DeserializeRequest<PreviewSpreadsheetImportRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = spreadsheetImportExecutionService.Preview(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            request.Payload.ProfileId,
            request.Payload.SourcePath,
            session);
        var response = SwShBridgeMapper.ToDto(result);

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
        var result = IsScarletViolet(paths)
            ? svWorkflowService.UpdateItemField(
                paths,
                session,
                request.Payload.ItemId,
                request.Payload.Field,
                request.Payload.Value)
            : itemsEditSessionService.UpdateField(
                paths,
                session,
                request.Payload.ItemId,
                request.Payload.Field,
                request.Payload.Value);
        var response = SwShBridgeMapper.ToDto(result);

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
        var validation = IsScarletViolet(paths)
            ? svWorkflowService.ValidateEditSession(paths, session)
            : GetEditSessionDomain(session) switch
            {
                EditSessionDomain.DynamaxAdventures => dynamaxAdventuresEditSessionService.Validate(paths, session),
                EditSessionDomain.Encounters => encountersEditSessionService.Validate(paths, session),
                EditSessionDomain.ExeFsPatches => exeFsPatchEditSessionService.Validate(paths, session),
                EditSessionDomain.BagHook => bagHookEditSessionService.Validate(paths, session),
                EditSessionDomain.CatchCap => catchCapEditSessionService.Validate(paths, session),
                EditSessionDomain.HyperTraining => hyperTrainingEditSessionService.Validate(paths, session),
                EditSessionDomain.TypeChart => typeChartEditSessionService.Validate(paths, session),
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
                EditSessionDomain.Mixed => CreateUnsupportedMixedValidation(session),
                _ => itemsEditSessionService.Validate(paths, session),
            };
        var response = SwShBridgeMapper.ToDto(validation);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchCreateChangePlan(string requestJson)
    {
        var request = DeserializeRequest<CreateChangePlanRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var session = EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var changePlan = IsScarletViolet(paths)
            ? svWorkflowService.CreateChangePlan(paths, session)
            : GetEditSessionDomain(session) switch
            {
                EditSessionDomain.DynamaxAdventures => dynamaxAdventuresEditSessionService.CreateChangePlan(paths, session),
                EditSessionDomain.Encounters => encountersEditSessionService.CreateChangePlan(paths, session),
                EditSessionDomain.ExeFsPatches => exeFsPatchEditSessionService.CreateChangePlan(paths, session),
                EditSessionDomain.BagHook => bagHookEditSessionService.CreateChangePlan(paths, session),
                EditSessionDomain.CatchCap => catchCapEditSessionService.CreateChangePlan(paths, session),
                EditSessionDomain.HyperTraining => hyperTrainingEditSessionService.CreateChangePlan(paths, session),
                EditSessionDomain.TypeChart => typeChartEditSessionService.CreateChangePlan(paths, session),
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
                EditSessionDomain.Mixed => CreateUnsupportedMixedChangePlan(session),
                _ => itemsEditSessionService.CreateChangePlan(paths, session),
            };
        var response = new CreateChangePlanResponse(EditSessionBridgeMapper.ToDto(changePlan));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchApplyChangePlan(string requestJson)
    {
        var request = DeserializeRequest<ApplyChangePlanRequest>(requestJson);
        var paths = ProjectBridgeMapper.ToCore(request.Payload.Paths);
        var session = EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var changePlan = EditSessionBridgeMapper.ToCore(request.Payload.ChangePlan);
        var applyResult = IsScarletViolet(paths)
            ? svWorkflowService.ApplyChangePlan(paths, session, changePlan)
            : GetEditSessionDomain(session) switch
            {
                EditSessionDomain.DynamaxAdventures => dynamaxAdventuresEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.Encounters => encountersEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.ExeFsPatches => exeFsPatchEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.BagHook => bagHookEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.CatchCap => catchCapEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.HyperTraining => hyperTrainingEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.TypeChart => typeChartEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.GymUniformRemoval => gymUniformRemovalEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.IvScreen => ivScreenEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.GiftPokemon => giftPokemonEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.TradePokemon => tradePokemonEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.RentalPokemon => rentalPokemonEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.Placement => placementEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.Behavior => behaviorEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.RaidBattles => raidBattlesEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.RaidRewards => raidRewardsEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.RaidBonusRewards => raidRewardsEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.StaticEncounters => staticEncountersEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.Trainers => trainersEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.Shops => shopsEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.Text => textEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.Items => itemsEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.Pokemon => pokemonEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.Moves => movesEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.RoyalCandy => royalCandyEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.StartingItems => startingItemsEditSessionService.ApplyChangePlan(paths, session, changePlan),
                EditSessionDomain.Mixed => CreateUnsupportedMixedApplyResult(session),
                _ => itemsEditSessionService.ApplyChangePlan(paths, session, changePlan),
            };
        var response = new ApplyChangePlanResponse(EditSessionBridgeMapper.ToDto(applyResult));

        return SerializeSuccess(response, request.RequestId);
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
            ["workflow.typeChart"] => EditSessionDomain.TypeChart,
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
            _ => EditSessionDomain.Mixed,
        };
    }

    private static bool IsScarletViolet(ProjectPaths paths)
    {
        return paths.SelectedGame is ProjectGame.Scarlet or ProjectGame.Violet;
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
            options.RandomizeRaidBonusRewards);
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
            options.RandomizeRaidBonusRewards);
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
        TypeChart,
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
        Mixed,
    }
}

