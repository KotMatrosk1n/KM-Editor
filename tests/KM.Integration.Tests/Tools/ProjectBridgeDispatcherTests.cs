// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Bridge;
using KM.Api.Projects;
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

