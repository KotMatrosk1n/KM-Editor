// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Bridge;
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Items;
using KM.Api.Projects;
using KM.Api.Text;
using KM.Api.Workflows;
using KM.Tools.Bridge;
using System.Text.Json;
using Xunit;

namespace KM.Integration.Tests.Tools;

public sealed class ProjectBridgeDispatcherTests
{
    [Fact]
    public void DispatchOpenProjectReturnsProjectHealthAndFileGraph()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");
        temp.WriteOutputFile("romfs/data/items.bin", "layered-items");

        var requestJson = SerializeRequest(
            KmCommandNames.OpenProject,
            new OpenProjectRequest(temp.Paths),
            requestId: "request-open");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<OpenProjectResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-open", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.False(string.IsNullOrWhiteSpace(response.Payload.ProjectId));
        Assert.Equal(ProjectHealthStateDto.EditableReady, response.Payload.Health.State);
        Assert.Equal(2, response.Payload.FileGraph.Summary.BaseFileCount);
        Assert.Equal(1, response.Payload.FileGraph.Summary.OverrideCount);
        Assert.Contains(
            response.Payload.FileGraph.Entries,
            entry => entry.RelativePath == "romfs/data/items.bin"
                && entry.State == ProjectFileGraphEntryStateDto.LayeredOverride);
    }

    [Fact]
    public void DispatchValidateProjectReturnsValidationPayloadForMissingPaths()
    {
        using var temp = TemporaryBridgeProject.Create();
        var missingPaths = temp.Paths with { BaseRomFsPath = Path.Combine(temp.RootPath, "missing-romfs") };
        var requestJson = SerializeRequest(
            KmCommandNames.ValidateProject,
            new ValidateProjectRequest(missingPaths),
            requestId: "request-validate");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<ValidateProjectResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-validate", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal(ProjectHealthStateDto.NeedsPaths, response.Payload.Health.State);
        Assert.Contains(
            response.Payload.Health.Paths,
            path => path.Role == ProjectPathRoleDto.BaseRomFs && path.Status == ProjectPathStatusDto.Missing);
    }

    [Fact]
    public void DispatchRefreshFileGraphReturnsBaseGraphWhenOutputRootIsNotConfigured()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");
        var paths = temp.Paths with { OutputRootPath = null };
        var requestJson = SerializeRequest(
            KmCommandNames.RefreshFileGraph,
            new RefreshFileGraphRequest(paths),
            requestId: "request-refresh");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<RefreshFileGraphResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-refresh", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal(2, response.Payload.FileGraph.Summary.BaseFileCount);
        Assert.Equal(0, response.Payload.FileGraph.Summary.LayeredFileCount);
    }

    [Fact]
    public void DispatchListWorkflowsReturnsItemsWorkflowAvailability()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.ListWorkflows,
            new ListWorkflowsRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-workflows");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<ListWorkflowsResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-workflows", response.RequestId);
        var workflows = response.Payload?.Workflows ?? [];
        Assert.Collection(
            workflows,
            workflow =>
            {
                Assert.Equal("items", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            },
            workflow =>
            {
                Assert.Equal("text", workflow.Id);
                Assert.Equal(WorkflowAvailabilityDto.ReadOnly, workflow.Availability);
            });
    }

    [Fact]
    public void DispatchLoadItemsWorkflowReturnsSanitizedItemRecords()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/items.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "items": [
                {
                  "itemId": 1,
                  "name": "Potion",
                  "category": "Medicine",
                  "buyPrice": 300,
                  "sellPrice": 150
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadItemsWorkflow,
            new LoadItemsWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-items");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadItemsWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-items", response.RequestId);
        Assert.NotNull(response.Payload);
        var item = Assert.Single(response.Payload.Workflow.Items);
        Assert.Equal("Potion", item.Name);
        Assert.Equal("romfs/kmeditor/items.readmodel.json", item.Provenance.SourceFile);
        Assert.Equal(ProjectFileLayerDto.Base, item.Provenance.SourceLayer);
        Assert.Collection(
            response.Payload.Workflow.EditableFields,
            editableField =>
            {
                Assert.Equal("buyPrice", editableField.Field);
                Assert.Equal(999_999, editableField.MaximumValue);
            },
            editableField =>
            {
                Assert.Equal("sellPrice", editableField.Field);
                Assert.Equal(999_999, editableField.MaximumValue);
            });
    }

    [Fact]
    public void DispatchLoadTextWorkflowReturnsSanitizedDialogueRecords()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/text.dialogue.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "language": "en",
              "entries": [
                {
                  "textId": 10,
                  "label": "Greeting",
                  "value": "Welcome to the lab."
                }
              ],
              "dialogueReferences": [
                {
                  "dialogueId": "intro.lab.greeting",
                  "label": "Lab greeting",
                  "textId": 10,
                  "context": "Intro",
                  "preview": "Welcome to the lab."
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.LoadTextWorkflow,
            new LoadTextWorkflowRequest(temp.Paths with { OutputRootPath = null }),
            requestId: "request-text");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<LoadTextWorkflowResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-text", response.RequestId);
        Assert.NotNull(response.Payload);
        var entry = Assert.Single(response.Payload.Workflow.Entries);
        Assert.Equal("Greeting", entry.Label);
        Assert.Equal("en", entry.Language);
        Assert.Equal(ProjectFileLayerDto.Base, entry.Provenance.SourceLayer);
        var reference = Assert.Single(response.Payload.Workflow.DialogueReferences);
        Assert.Equal("intro.lab.greeting", reference.DialogueId);
        Assert.Equal(10, reference.TextId);
        Assert.Equal(1, response.Payload.Workflow.Stats.SourceFileCount);
    }

    [Fact]
    public void DispatchUpdateItemFieldReturnsPendingEditSession()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/items.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "items": [
                {
                  "itemId": 1,
                  "name": "Potion",
                  "category": "Medicine",
                  "buyPrice": 300,
                  "sellPrice": 150
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.UpdateItemField,
            new UpdateItemFieldRequest(temp.Paths, Session: null, ItemId: 1, Field: "buyPrice", Value: "450"),
            requestId: "request-items-edit");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<UpdateItemFieldResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        Assert.True(response.Payload.Session.HasPendingChanges);
        Assert.Equal(450, Assert.Single(response.Payload.Workflow.Items).BuyPrice);
        Assert.Equal("450", Assert.Single(response.Payload.Session.PendingEdits).NewValue);
    }

    [Fact]
    public void DispatchUpdateItemFieldReturnsPendingSellPriceSession()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/items.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "items": [
                {
                  "itemId": 1,
                  "name": "Potion",
                  "category": "Medicine",
                  "buyPrice": 300,
                  "sellPrice": 150
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var requestJson = SerializeRequest(
            KmCommandNames.UpdateItemField,
            new UpdateItemFieldRequest(
                temp.Paths,
                Session: null,
                ItemId: 1,
                Field: "sellPrice",
                Value: "175"),
            requestId: "request-items-sell-edit");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<UpdateItemFieldResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        Assert.True(response.Payload.Session.HasPendingChanges);
        var item = Assert.Single(response.Payload.Workflow.Items);
        Assert.Equal(300, item.BuyPrice);
        Assert.Equal(175, item.SellPrice);
        var edit = Assert.Single(response.Payload.Session.PendingEdits);
        Assert.Equal("sellPrice", edit.Field);
        Assert.Equal("175", edit.NewValue);
    }

    [Fact]
    public void DispatchValidateEditSessionReturnsValidationPayload()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/items.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "items": [
                {
                  "itemId": 1,
                  "name": "Potion",
                  "category": "Medicine",
                  "buyPrice": 300,
                  "sellPrice": 150
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var sessionResponseJson = new ProjectBridgeDispatcher().Dispatch(SerializeRequest(
            KmCommandNames.UpdateItemField,
            new UpdateItemFieldRequest(temp.Paths, Session: null, ItemId: 1, Field: "buyPrice", Value: "450"),
            requestId: "request-items-edit"));
        var sessionResponse = DeserializeResponse<UpdateItemFieldResponse>(sessionResponseJson);
        Assert.NotNull(sessionResponse.Payload);
        var requestJson = SerializeRequest(
            KmCommandNames.ValidateEditSession,
            new ValidateEditSessionRequest(temp.Paths, sessionResponse.Payload.Session),
            requestId: "request-session-validate");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<ValidateEditSessionResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.NotNull(response.Payload);
        Assert.True(response.Payload.IsValid);
        Assert.Contains(response.Payload.Diagnostics, diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Info);
    }

    [Fact]
    public void DispatchCreateChangePlanReturnsPlannedTargetFiles()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/items.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "items": [
                {
                  "itemId": 1,
                  "name": "Potion",
                  "category": "Medicine",
                  "buyPrice": 300,
                  "sellPrice": 150
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var sessionResponseJson = new ProjectBridgeDispatcher().Dispatch(SerializeRequest(
            KmCommandNames.UpdateItemField,
            new UpdateItemFieldRequest(temp.Paths, Session: null, ItemId: 1, Field: "buyPrice", Value: "450"),
            requestId: "request-items-edit"));
        var sessionResponse = DeserializeResponse<UpdateItemFieldResponse>(sessionResponseJson);
        Assert.NotNull(sessionResponse.Payload);
        var requestJson = SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, sessionResponse.Payload.Session),
            requestId: "request-change-plan");

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<CreateChangePlanResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-change-plan", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.True(response.Payload.ChangePlan.CanApply);
        var write = Assert.Single(response.Payload.ChangePlan.Writes);
        Assert.Equal("romfs/kmeditor/items.readmodel.json", write.TargetRelativePath);
        Assert.Equal(FileLayerDto.Base, Assert.Single(write.Sources).Layer);
    }

    [Fact]
    public void DispatchApplyChangePlanReturnsWrittenFiles()
    {
        using var temp = TemporaryBridgeProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/items.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "items": [
                {
                  "itemId": 1,
                  "name": "Potion",
                  "category": "Medicine",
                  "buyPrice": 300,
                  "sellPrice": 150
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var dispatcher = new ProjectBridgeDispatcher();
        var sessionResponseJson = dispatcher.Dispatch(SerializeRequest(
            KmCommandNames.UpdateItemField,
            new UpdateItemFieldRequest(temp.Paths, Session: null, ItemId: 1, Field: "buyPrice", Value: "450"),
            requestId: "request-items-edit"));
        var sessionResponse = DeserializeResponse<UpdateItemFieldResponse>(sessionResponseJson);
        Assert.NotNull(sessionResponse.Payload);
        var planResponseJson = dispatcher.Dispatch(SerializeRequest(
            KmCommandNames.CreateChangePlan,
            new CreateChangePlanRequest(temp.Paths, sessionResponse.Payload.Session),
            requestId: "request-change-plan"));
        var planResponse = DeserializeResponse<CreateChangePlanResponse>(planResponseJson);
        Assert.NotNull(planResponse.Payload);
        var requestJson = SerializeRequest(
            KmCommandNames.ApplyChangePlan,
            new ApplyChangePlanRequest(temp.Paths, sessionResponse.Payload.Session, planResponse.Payload.ChangePlan),
            requestId: "request-change-plan-apply");

        var responseJson = dispatcher.Dispatch(requestJson);
        var response = DeserializeResponse<ApplyChangePlanResponse>(responseJson);

        Assert.Null(response.Error);
        Assert.Equal("request-change-plan-apply", response.RequestId);
        Assert.NotNull(response.Payload);
        Assert.Equal("romfs/kmeditor/items.readmodel.json", Assert.Single(response.Payload.ApplyResult.WrittenFiles));
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "kmeditor", "items.readmodel.json");
        Assert.Contains("\"buyPrice\": 450", File.ReadAllText(outputPath));
        Assert.DoesNotContain(
            response.Payload.ApplyResult.Diagnostics,
            diagnostic => diagnostic.Severity == ApiDiagnosticSeverity.Error);
    }

    [Fact]
    public void DispatchUnsupportedCommandReturnsBridgeError()
    {
        var requestJson = JsonSerializer.Serialize(
            new BridgeRequest<object>("project.unsupported", new { }, RequestId: "request-unsupported"),
            BridgeJson.SerializerOptions);

        var responseJson = new ProjectBridgeDispatcher().Dispatch(requestJson);
        var response = DeserializeResponse<object>(responseJson);

        Assert.Null(response.Payload);
        Assert.NotNull(response.Error);
        Assert.Equal("bridge.unsupportedCommand", response.Error.Code);
        Assert.Equal("request-unsupported", response.RequestId);
    }

    private static string SerializeRequest<TPayload>(string command, TPayload payload, string requestId)
    {
        return JsonSerializer.Serialize(
            new BridgeRequest<TPayload>(command, payload, RequestId: requestId),
            BridgeJson.SerializerOptions);
    }

    private static BridgeResponse<TPayload> DeserializeResponse<TPayload>(string responseJson)
    {
        var response = JsonSerializer.Deserialize<BridgeResponse<TPayload>>(
            responseJson,
            BridgeJson.SerializerOptions);

        Assert.NotNull(response);

        return response;
    }
}

