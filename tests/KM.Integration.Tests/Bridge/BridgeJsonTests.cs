// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using KM.Api.Bridge;
using KM.Api.Diagnostics;
using KM.Api.Projects;
using Xunit;

namespace KM.Integration.Tests.Bridge;

public sealed class BridgeJsonTests
{
    [Fact]
    public void SerializesRequestEnvelopeWithCamelCaseNames()
    {
        var paths = new ProjectPathsDto(
            "base-romfs",
            "base-exefs",
            OutputRootPath: null,
            SaveFilePath: null,
            SelectedGame: ProjectGameDto.Shield);
        var request = new BridgeRequest<OpenProjectRequest>(
            KmCommandNames.OpenProject,
            new OpenProjectRequest(paths),
            RequestId: "request-1");

        var json = JsonSerializer.Serialize(request, BridgeJson.SerializerOptions);

        Assert.Contains("\"command\":\"project.open\"", json);
        Assert.Contains("\"requestId\":\"request-1\"", json);
        Assert.Contains("\"baseRomFsPath\":\"base-romfs\"", json);
        Assert.Contains("\"selectedGame\":\"shield\"", json);
        Assert.DoesNotContain("BaseRomFsPath", json);
    }

    [Fact]
    public void SerializesScarletProjectGameAsCamelCaseName()
    {
        var paths = new ProjectPathsDto(
            "base-romfs",
            "base-exefs",
            OutputRootPath: null,
            SaveFilePath: null,
            SelectedGame: ProjectGameDto.Scarlet);
        var request = new BridgeRequest<ValidateProjectRequest>(
            KmCommandNames.ValidateProject,
            new ValidateProjectRequest(paths),
            RequestId: "request-scarlet");

        var json = JsonSerializer.Serialize(request, BridgeJson.SerializerOptions);

        Assert.Contains("\"selectedGame\":\"scarlet\"", json);
    }

    [Fact]
    public void SerializesResponseEnvelopeWithStringDiagnostics()
    {
        var diagnostic = new ApiDiagnostic(ApiDiagnosticSeverity.Warning, "Project has missing optional output.");
        var error = new ApiError("project.invalidPaths", "Project paths are not valid.", [diagnostic]);
        var response = BridgeResponse<OpenProjectResponse>.Failure(error, requestId: "request-2");

        var json = JsonSerializer.Serialize(response, BridgeJson.SerializerOptions);

        Assert.Contains("\"error\":", json);
        Assert.Contains("\"severity\":\"warning\"", json);
        Assert.Contains("\"requestId\":\"request-2\"", json);
        Assert.DoesNotContain("\"succeeded\"", json);
    }

    [Fact]
    public void SerializesProjectHealthStateAsString()
    {
        var health = new ProjectHealthDto(
            State: ProjectHealthStateDto.EditableReady,
            CanOpenReadOnlyWorkflows: true,
            CanOpenEditableWorkflows: true,
            Paths:
            [
                new ProjectPathValidationDto(
                    Role: ProjectPathRoleDto.BaseRomFs,
                    Path: "base-romfs",
                    Status: ProjectPathStatusDto.Valid,
                    IsRequired: true,
                    Diagnostics: []),
            ],
            FileGraph: new ProjectFileGraphSummaryDto(BaseFileCount: 1, LayeredFileCount: 0, OverrideCount: 0, LayeredOnlyCount: 0),
            Diagnostics: []);
        var response = BridgeResponse<OpenProjectResponse>.Success(
            new OpenProjectResponse(
                "project-1",
                health,
                new ProjectFileGraphDto(
                    Entries:
                    [
                        new ProjectFileGraphEntryDto(
                            RelativePath: "romfs/data/items.bin",
                            BaseFile: new ProjectFileReferenceDto(ProjectFileLayerDto.Base, "romfs/data/items.bin"),
                            LayeredFile: new ProjectFileReferenceDto(ProjectFileLayerDto.Layered, "romfs/data/items.bin"),
                            State: ProjectFileGraphEntryStateDto.LayeredOverride),
                    ],
                    Summary: new ProjectFileGraphSummaryDto(BaseFileCount: 1, LayeredFileCount: 1, OverrideCount: 1, LayeredOnlyCount: 0))),
            requestId: "request-3");

        var json = JsonSerializer.Serialize(response, BridgeJson.SerializerOptions);

        Assert.Contains("\"state\":\"editableReady\"", json);
        Assert.Contains("\"role\":\"baseRomFs\"", json);
        Assert.Contains("\"status\":\"valid\"", json);
        Assert.Contains("\"layer\":\"base\"", json);
        Assert.Contains("\"state\":\"layeredOverride\"", json);
    }
}
