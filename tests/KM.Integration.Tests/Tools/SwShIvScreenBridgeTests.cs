// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using KM.Api.Bridge;
using KM.Api.Projects;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.IvScreen;
using KM.SwSh.Workflows;
using KM.Tools.Bridge;
using Xunit;

namespace KM.Integration.Tests.Tools;

public sealed class SwShIvScreenBridgeTests
{
    [Fact]
    public void MapperPreservesExecutableIdentityAndActiveOffsetFields()
    {
        var workflow = new SwShIvScreenWorkflow(
            new SwShWorkflowSummary(
                SwShWorkflowIds.IvScreen,
                "IV Screen",
                "IV Screen fixture.",
                SwShWorkflowAvailability.Available,
                []),
            "installed",
            "Installed fixture.",
            "SWSH_IV_DISPLAY_V1",
            "BUILDID",
            ProjectGame.Shield,
            "main.text+0x0138A2E4",
            "main.text+0x0138B3DC",
            "main.text+0x00779070",
            "main.text+0x007790D0",
            CanUninstall: true,
            [],
            new SwShIvScreenProvenance(
                SwShIvScreenWorkflowService.ExeFsMainPath,
                ProjectFileLayer.Layered,
                ProjectFileGraphEntryState.LayeredOverride),
            new SwShIvScreenWorkflowStats(0, 2),
            []);

        var response = SwShBridgeMapper.ToDto(workflow);
        var dto = response.Workflow;
        var json = JsonSerializer.Serialize(dto, BridgeJson.SerializerOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("BUILDID", dto.BuildId);
        Assert.Equal(ProjectGameDto.Shield, dto.DetectedGame);
        Assert.Equal("main.text+0x0138A2E4", dto.PrimaryValueSourceOffsetHex);
        Assert.Equal("main.text+0x0138B3DC", dto.XToggleRefreshOffsetHex);
        Assert.True(dto.CanUninstall);
        Assert.Equal("BUILDID", root.GetProperty("buildId").GetString());
        Assert.Equal("shield", root.GetProperty("detectedGame").GetString());
        Assert.Equal(
            "main.text+0x0138A2E4",
            root.GetProperty("primaryValueSourceOffsetHex").GetString());
        Assert.Equal(
            "main.text+0x0138B3DC",
            root.GetProperty("xToggleRefreshOffsetHex").GetString());
        Assert.True(root.GetProperty("canUninstall").GetBoolean());
        Assert.False(root.TryGetProperty("hookSiteOffsetHex", out _));
    }
}
