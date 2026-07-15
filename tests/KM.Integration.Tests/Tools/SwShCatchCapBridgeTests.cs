// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using KM.Api.Bridge;
using KM.Api.Projects;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.CatchCap;
using KM.SwSh.Workflows;
using KM.Tools.Bridge;
using Xunit;

namespace KM.Integration.Tests.Tools;

public sealed class SwShCatchCapBridgeTests
{
    [Fact]
    public void MapperPreservesExecutableIdentityFieldsWithExactContractNames()
    {
        var workflow = new SwShCatchCapWorkflow(
            new SwShWorkflowSummary(
                SwShWorkflowIds.CatchCap,
                "Catch Cap Editor",
                "Catch Cap fixture.",
                SwShWorkflowAvailability.Available,
                []),
            "installed",
            "Installed fixture.",
            "cap_table[badge_count]",
            "CAPHASH",
            "BUILDID",
            ProjectGame.Shield,
            "main.text+0x013AE3DC",
            "main.text+0x013AE40C",
            [new SwShCatchCapRecord(0, "No badges", 20, 1, 100)],
            new SwShCatchCapProvenance(
                SwShCatchCapWorkflowService.ExeFsMainPath,
                ProjectFileLayer.Layered,
                ProjectFileGraphEntryState.LayeredOverride),
            new SwShCatchCapWorkflowStats(1, 2),
            []);

        var response = SwShBridgeMapper.ToDto(workflow);
        var dto = response.Workflow;
        var json = JsonSerializer.Serialize(dto, BridgeJson.SerializerOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("BUILDID", dto.BuildId);
        Assert.Equal(ProjectGameDto.Shield, dto.DetectedGame);
        Assert.Equal("main.text+0x013AE3DC", dto.DisplayHookOffsetHex);
        Assert.Equal("main.text+0x013AE40C", dto.RuntimeHookOffsetHex);
        Assert.Equal("BUILDID", root.GetProperty("buildId").GetString());
        Assert.Equal("shield", root.GetProperty("detectedGame").GetString());
        Assert.Equal("main.text+0x013AE3DC", root.GetProperty("displayHookOffsetHex").GetString());
        Assert.Equal("main.text+0x013AE40C", root.GetProperty("runtimeHookOffsetHex").GetString());
    }
}
