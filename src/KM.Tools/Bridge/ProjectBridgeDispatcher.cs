// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Bridge;
using KM.Api.Editing;
using KM.Api.Encounters;
using KM.Api.Items;
using KM.Api.Projects;
using KM.Api.Raids;
using KM.Api.Shops;
using KM.Api.Text;
using KM.Api.Trainers;
using KM.Api.Workflows;
using KM.Core.Projects;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using System.Text.Json;

namespace KM.Tools.Bridge;

public sealed class ProjectBridgeDispatcher
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly SwShItemsEditSessionService itemsEditSessionService;
    private readonly SwShWorkflowService swShWorkflowService;

    public ProjectBridgeDispatcher(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShItemsEditSessionService? itemsEditSessionService = null,
        SwShWorkflowService? swShWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.itemsEditSessionService = itemsEditSessionService ?? new SwShItemsEditSessionService(this.projectWorkspaceService);
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
                KmCommandNames.LoadTextWorkflow => DispatchLoadTextWorkflow(requestJson),
                KmCommandNames.LoadTrainersWorkflow => DispatchLoadTrainersWorkflow(requestJson),
                KmCommandNames.LoadShopsWorkflow => DispatchLoadShopsWorkflow(requestJson),
                KmCommandNames.LoadEncountersWorkflow => DispatchLoadEncountersWorkflow(requestJson),
                KmCommandNames.LoadRaidRewardsWorkflow => DispatchLoadRaidRewardsWorkflow(requestJson),
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

    private string DispatchLoadTextWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadTextWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadText(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadTrainersWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadTrainersWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadTrainers(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadShopsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadShopsWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadShops(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadEncountersWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadEncountersWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadEncounters(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchLoadRaidRewardsWorkflow(string requestJson)
    {
        var request = DeserializeRequest<LoadRaidRewardsWorkflowRequest>(requestJson);
        var workflow = swShWorkflowService.LoadRaidRewards(ProjectBridgeMapper.ToCore(request.Payload.Paths));
        var response = SwShBridgeMapper.ToDto(workflow);

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
            EditSessionBridgeMapper.ToDto(itemsEditSessionService.StartSession()));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchValidateEditSession(string requestJson)
    {
        var request = DeserializeRequest<ValidateEditSessionRequest>(requestJson);
        var validation = itemsEditSessionService.Validate(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            EditSessionBridgeMapper.ToCore(request.Payload.Session));
        var response = SwShBridgeMapper.ToDto(validation);

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchCreateChangePlan(string requestJson)
    {
        var request = DeserializeRequest<CreateChangePlanRequest>(requestJson);
        var changePlan = itemsEditSessionService.CreateChangePlan(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            EditSessionBridgeMapper.ToCore(request.Payload.Session));
        var response = new CreateChangePlanResponse(EditSessionBridgeMapper.ToDto(changePlan));

        return SerializeSuccess(response, request.RequestId);
    }

    private string DispatchApplyChangePlan(string requestJson)
    {
        var request = DeserializeRequest<ApplyChangePlanRequest>(requestJson);
        var applyResult = itemsEditSessionService.ApplyChangePlan(
            ProjectBridgeMapper.ToCore(request.Payload.Paths),
            EditSessionBridgeMapper.ToCore(request.Payload.Session),
            EditSessionBridgeMapper.ToCore(request.Payload.ChangePlan));
        var response = new ApplyChangePlanResponse(EditSessionBridgeMapper.ToDto(applyResult));

        return SerializeSuccess(response, request.RequestId);
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
}

