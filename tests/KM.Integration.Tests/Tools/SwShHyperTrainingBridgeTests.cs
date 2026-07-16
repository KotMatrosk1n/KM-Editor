// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using KM.Api.Bridge;
using KM.Api.Projects;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.HyperTraining;
using KM.SwSh.Workflows;
using KM.Tools.Bridge;
using Xunit;

namespace KM.Integration.Tests.Tools;

public sealed class SwShHyperTrainingBridgeTests
{
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";

    [Fact]
    public void MapperPreservesExecutableIdentityAndPerSourceCutoffFields()
    {
        var workflow = new SwShHyperTrainingWorkflow(
            new SwShWorkflowSummary(
                SwShWorkflowIds.HyperTraining,
                "Hyper Training",
                "Hyper Training fixture.",
                SwShWorkflowAvailability.Available,
                []),
            "installed",
            "Hyper Training fixture is intentionally out of sync.",
            ShieldBuildId,
            ProjectGame.Shield,
            new SwShHyperTrainingLevelRule(
                MinimumLevel: 42,
                ScriptMinimumLevel: 41,
                RuntimeMinimumLevel: 42,
                DialogueMinimumLevel: 43,
                LevelsMatch: false,
                VanillaMinimumLevel: 100,
                MinimumAllowedLevel: 1,
                MaximumAllowedLevel: 100,
                ScriptCell: "AMX code cell 2294",
                DialogueSummary: "English dialogue lines 0 and 3 use Lv.43.",
                RuntimeSummary: "Picker cutoff uses Lv.42."),
            [
                new SwShHyperTrainingSourceRecord(
                    "script",
                    "Hyper Training script",
                    SwShHyperTrainingWorkflowService.ScriptPath,
                    "available",
                    new SwShHyperTrainingProvenance(
                        SwShHyperTrainingWorkflowService.ScriptPath,
                        ProjectFileLayer.Layered,
                        ProjectFileGraphEntryState.LayeredOverride)),
                new SwShHyperTrainingSourceRecord(
                    "dialogue",
                    "English Hyper Training dialogue",
                    SwShHyperTrainingWorkflowService.EnglishDialoguePath,
                    "optionalMissing",
                    new SwShHyperTrainingProvenance(
                        SwShHyperTrainingWorkflowService.EnglishDialoguePath,
                        ProjectFileLayer.Generated,
                        ProjectFileGraphEntryState.BaseOnly)),
                new SwShHyperTrainingSourceRecord(
                    "runtime",
                    "Hyper Training picker runtime",
                    SwShHyperTrainingWorkflowService.ExeFsMainPath,
                    "available",
                    new SwShHyperTrainingProvenance(
                        SwShHyperTrainingWorkflowService.ExeFsMainPath,
                        ProjectFileLayer.Base,
                        ProjectFileGraphEntryState.BaseOnly)),
            ],
            new SwShHyperTrainingWorkflowStats(SourceFileCount: 2, OutputFileCount: 2),
            []);

        var response = SwShBridgeMapper.ToDto(workflow);
        var dto = response.Workflow;
        var json = JsonSerializer.Serialize(dto, BridgeJson.SerializerOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var levelRule = root.GetProperty("levelRule");

        Assert.Equal(ShieldBuildId, dto.BuildId);
        Assert.Equal(ProjectGameDto.Shield, dto.DetectedGame);
        Assert.Equal(41, dto.LevelRule.ScriptMinimumLevel);
        Assert.Equal(42, dto.LevelRule.RuntimeMinimumLevel);
        Assert.Equal(43, dto.LevelRule.DialogueMinimumLevel);
        Assert.False(dto.LevelRule.LevelsMatch);
        Assert.Equal("optionalMissing", dto.Sources[1].Status);

        Assert.Equal(ShieldBuildId, root.GetProperty("buildId").GetString());
        Assert.Equal("shield", root.GetProperty("detectedGame").GetString());
        Assert.Equal(41, levelRule.GetProperty("scriptMinimumLevel").GetInt32());
        Assert.Equal(42, levelRule.GetProperty("runtimeMinimumLevel").GetInt32());
        Assert.Equal(43, levelRule.GetProperty("dialogueMinimumLevel").GetInt32());
        Assert.False(levelRule.GetProperty("levelsMatch").GetBoolean());
        Assert.False(levelRule.TryGetProperty("scriptLevel", out _));
        Assert.False(root.TryGetProperty("game", out _));
    }
}
