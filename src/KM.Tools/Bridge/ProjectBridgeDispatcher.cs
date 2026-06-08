// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Bridge;
using KM.Api.Editing;
using KM.Api.Encounters;
using KM.Api.ExeFs;
using KM.Api.Flagwork;
using KM.Api.Items;
using KM.Api.Moves;
using KM.Api.Placement;
using KM.Api.Pokemon;
using KM.Api.Projects;
using KM.Api.Raids;
using KM.Api.RoyalCandy;
using KM.Api.Shops;
using KM.Api.SpreadsheetImport;
using KM.Api.Text;
using KM.Api.Trainers;
using KM.Api.Workflows;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Encounters;
using KM.SwSh.ExeFs;
using KM.SwSh.Items;
using KM.SwSh.Moves;
using KM.SwSh.Placement;
using KM.SwSh.Raids;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Shops;
using KM.SwSh.SpreadsheetImport;
using KM.SwSh.Text;
using KM.SwSh.Trainers;
using KM.SwSh.Workflows;
using System.Text.Json;

namespace KM.Tools.Bridge;

public sealed class ProjectBridgeDispatcher
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShEncountersEditSessionService encountersEditSessionService;
    private readonly SwShExeFsPatchEditSessionService exeFsPatchEditSessionService;
    private readonly SwShItemsEditSessionService itemsEditSessionService;
    private readonly SwShMovesEditSessionService movesEditSessionService;
    private readonly SwShPlacementEditSessionService placementEditSessionService;
    private readonly SwShRaidRewardsEditSessionService raidRewardsEditSessionService;
    private readonly SwShRoyalCandyEditSessionService royalCandyEditSessionService;
    private readonly SwShShopsEditSessionService shopsEditSessionService;
    private readonly SwShSpreadsheetImportExecutionService spreadsheetImportExecutionService;
    private readonly SwShTextEditSessionService textEditSessionService;
    private readonly SwShTrainersEditSessionService trainersEditSessionService;
    private readonly SwShWorkflowService swShWorkflowService;

    public ProjectBridgeDispatcher(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShEncountersEditSessionService? encountersEditSessionService = null,
        SwShExeFsPatchEditSessionService? exeFsPatchEditSessionService = null,
        SwShItemsEditSessionService? itemsEditSessionService = null,
        SwShMovesEditSessionService? movesEditSessionService = null,
        SwShPlacementEditSessionService? placementEditSessionService = null,
        SwShRaidRewardsEditSessionService? raidRewardsEditSessionService = null,
        SwShRoyalCandyEditSessionService? royalCandyEditSessionService = null,
        SwShShopsEditSessionService? shopsEditSessionService = null,
        SwShSpreadsheetImportExecutionService? spreadsheetImportExecutionService = null,
        SwShTextEditSessionService? textEditSessionService = null,
        SwShTrainersEditSessionService? trainersEditSessionService = null,
        SwShWorkflowService? swShWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.encountersEditSessionService = encountersEditSessionService ?? new SwShEncountersEditSessionService(this.projectWorkspaceService);
        this.exeFsPatchEditSessionService = exeFsPatchEditSessionService ?? new SwShExeFsPatchEditSessionService(this.projectWorkspaceService);
        this.itemsEditSessionService = itemsEditSessionService ?? new SwShItemsEditSessionService(this.projectWorkspaceService);
        this.movesEditSessionService = movesEditSessionService ?? new SwShMovesEditSessionService(this.projectWorkspaceService);
        this.placementEditSessionService = placementEditSessionService ?? new SwShPlacementEditSessionService(this.projectWorkspaceService);
        this.raidRewardsEditSessionService = raidRewardsEditSessionService ?? new SwShRaidRewardsEditSessionService(this.projectWorkspaceService);
        this.royalCandyEditSessionService = royalCandyEditSessionService ?? new SwShRoyalCandyEditSessionService(this.projectWorkspaceService);
        this.shopsEditSessionService = shopsEditSessionService ?? new SwShShopsEditSessionService(this.projectWorkspaceService);
        this.spreadsheetImportExecutionService = spreadsheetImportExecutionService ?? new SwShSpreadsheetImportExecutionService(this.projectWorkspaceService);
        this.textEditSessionService = textEditSessionService ?? new SwShTextEditSessionService(this.projectWorkspaceService);
        this.trainersEditSessionService = trainersEditSessionService ?? new SwShTrainersEditSessionService(this.projectWorkspaceService);
        this.swShWorkflowService = swShWorkflowService ?? new SwShWorkflowService(this.projectWorkspaceService);
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
                KmCommandNames.LoadMovesWorkflow => DispatchLoadMovesWorkflow(requestJson),
                KmCommandNames.UpdateMoveField => DispatchUpdateMoveField(requestJson),
                KmCommandNames.LoadTextWorkflow => DispatchLoadTextWorkflow(requestJson),
                KmCommandNames.UpdateTextEntry => DispatchUpdateTextEntry(requestJson),
                KmCommandNames.LoadTrainersWorkflow => DispatchLoadTrainersWorkflow(requestJson),
                KmCommandNames.UpdateTrainerField => DispatchUpdateTrainerField(requestJson),
                KmCommandNames.LoadShopsWorkflow => DispatchLoadShopsWorkflow(requestJson),
                KmCommandNames.UpdateShopInventoryItem => DispatchUpdateShopInventoryItem(requestJson),
                KmCommandNames.LoadEncountersWorkflow => DispatchLoadEncountersWorkflow(requestJson),
                KmCommandNames.UpdateEncounterSlotField => DispatchUpdateEncounterSlotField(requestJson),
                KmCommandNames.LoadRaidRewardsWorkflow => DispatchLoadRaidRewardsWorkflow(requestJson),
                KmCommandNames.UpdateRaidRewardField => DispatchUpdateRaidRewardField(requestJson),
                KmCommandNames.LoadPlacementWorkflow => DispatchLoadPlacementWorkflow(requestJson),
                KmCommandNames.UpdatePlacementObjectField => DispatchUpdatePlacementObjectField(requestJson),
                KmCommandNames.LoadFlagworkSaveWorkflow => DispatchLoadFlagworkSaveWorkflow(requestJson),
                KmCommandNames.LoadExeFsPatchWorkflow => DispatchLoadExeFsPatchWorkflow(requestJson),
                KmCommandNames.StageExeFsPatch => DispatchStageExeFsPatch(requestJson),
                KmCommandNames.LoadRoyalCandyWorkflow => DispatchLoadRoyalCandyWorkflow(requestJson),
                KmCommandNames.StageRoyalCandyWorkflow => DispatchStageRoyalCandyWorkflow(requestJson),
                KmCommandNames.LoadSpreadsheetImportWorkflow => DispatchLoadSpreadsheetImportWorkflow(requestJson),
                KmCommandNames.PreviewSpreadsheetImport => DispatchPreviewSpreadsheetImport(requestJson),
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
        var workflowList = swShWorkflowService.List(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflowList);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadItemsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadItemsWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadItems(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadPokemonWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadPokemonWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadPokemon(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

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
        var workflow = swShWorkflowService.LoadTrainers(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateTrainerField(string requestJson)
    {
        var request = DeserializeRequest<UpdateTrainerFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = trainersEditSessionService.UpdateField(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
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
        var workflow = swShWorkflowService.LoadEncounters(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchUpdateEncounterSlotField(string requestJson)
    {
        var request = DeserializeRequest<UpdateEncounterSlotFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = encountersEditSessionService.UpdateSlotField(
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

    private string DispatchLoadFlagworkSaveWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadFlagworkSaveWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadFlagworkSave(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

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

    private string DispatchUpdateItemField(string requestJson)
    {
        var request = DeserializeRequest<UpdateItemFieldRequest>(requestJson);
        var session = request.Payload.Session is null
            ? null
            : EditSessionBridgeMapper.ToCore(request.Payload.Session);
        var result = itemsEditSessionService.UpdateField(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
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
        var validation = GetEditSessionDomain(session) switch
        {
            EditSessionDomain.Encounters => encountersEditSessionService.Validate(paths, session),
            EditSessionDomain.ExeFsPatches => exeFsPatchEditSessionService.Validate(paths, session),
            EditSessionDomain.Placement => placementEditSessionService.Validate(paths, session),
            EditSessionDomain.RaidRewards => raidRewardsEditSessionService.Validate(paths, session),
            EditSessionDomain.Trainers => trainersEditSessionService.Validate(paths, session),
            EditSessionDomain.Shops => shopsEditSessionService.Validate(paths, session),
            EditSessionDomain.Text => textEditSessionService.Validate(paths, session),
            EditSessionDomain.Items => itemsEditSessionService.Validate(paths, session),
            EditSessionDomain.Moves => movesEditSessionService.Validate(paths, session),
            EditSessionDomain.RoyalCandy => royalCandyEditSessionService.Validate(paths, session),
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
        var changePlan = GetEditSessionDomain(session) switch
        {
            EditSessionDomain.Encounters => encountersEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.ExeFsPatches => exeFsPatchEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Placement => placementEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.RaidRewards => raidRewardsEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Trainers => trainersEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Shops => shopsEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Text => textEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Items => itemsEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.Moves => movesEditSessionService.CreateChangePlan(paths, session),
            EditSessionDomain.RoyalCandy => royalCandyEditSessionService.CreateChangePlan(paths, session),
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
        var applyResult = GetEditSessionDomain(session) switch
        {
            EditSessionDomain.Encounters => encountersEditSessionService.ApplyChangePlan(paths, session, changePlan),
            EditSessionDomain.ExeFsPatches => exeFsPatchEditSessionService.ApplyChangePlan(paths, session, changePlan),
            EditSessionDomain.Placement => placementEditSessionService.ApplyChangePlan(paths, session, changePlan),
            EditSessionDomain.RaidRewards => raidRewardsEditSessionService.ApplyChangePlan(paths, session, changePlan),
            EditSessionDomain.Trainers => trainersEditSessionService.ApplyChangePlan(paths, session, changePlan),
            EditSessionDomain.Shops => shopsEditSessionService.ApplyChangePlan(paths, session, changePlan),
            EditSessionDomain.Text => textEditSessionService.ApplyChangePlan(paths, session, changePlan),
            EditSessionDomain.Items => itemsEditSessionService.ApplyChangePlan(paths, session, changePlan),
            EditSessionDomain.Moves => movesEditSessionService.ApplyChangePlan(paths, session, changePlan),
            EditSessionDomain.RoyalCandy => royalCandyEditSessionService.ApplyChangePlan(paths, session, changePlan),
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
            ["workflow.trainers"] => EditSessionDomain.Trainers,
            ["workflow.shops"] => EditSessionDomain.Shops,
            ["workflow.encounters"] => EditSessionDomain.Encounters,
            ["workflow.exefsPatches"] => EditSessionDomain.ExeFsPatches,
            ["workflow.placement"] => EditSessionDomain.Placement,
            ["workflow.raidRewards"] => EditSessionDomain.RaidRewards,
            ["workflow.royalCandy"] => EditSessionDomain.RoyalCandy,
            _ => EditSessionDomain.Mixed,
        };
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
        Moves,
        Text,
        Trainers,
        Shops,
        Encounters,
        ExeFsPatches,
        Placement,
        RaidRewards,
        RoyalCandy,
        Mixed,
    }
}

