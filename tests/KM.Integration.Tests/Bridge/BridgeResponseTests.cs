// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Bridge;
using KM.Api.Projects;
using Xunit;

namespace KM.Integration.Tests.Bridge;

public sealed class BridgeResponseTests
{
    [Fact]
    public void SuccessCarriesPayloadAndRequestId()
    {
        var health = new ProjectHealthDto(
            State: ProjectHealthStateDto.EditableReady,
            CanOpenReadOnlyWorkflows: true,
            CanOpenEditableWorkflows: true,
            Paths: [],
            FileGraph: new ProjectFileGraphSummaryDto(BaseFileCount: 0, LayeredFileCount: 0, OverrideCount: 0, LayeredOnlyCount: 0),
            Diagnostics: []);
        var payload = new OpenProjectResponse("project-1", health);

        var response = BridgeResponse<OpenProjectResponse>.Success(payload, requestId: "request-1");

        Assert.True(response.Succeeded);
        Assert.Same(payload, response.Payload);
        Assert.Null(response.Error);
        Assert.Equal("request-1", response.RequestId);
    }

    [Fact]
    public void FailureCarriesErrorAndRequestId()
    {
        var error = ApiError.Create("project.invalidPaths", "Project paths are not valid.");

        var response = BridgeResponse<OpenProjectResponse>.Failure(error, requestId: "request-2");

        Assert.False(response.Succeeded);
        Assert.Null(response.Payload);
        Assert.Same(error, response.Error);
        Assert.Equal("request-2", response.RequestId);
    }
}
