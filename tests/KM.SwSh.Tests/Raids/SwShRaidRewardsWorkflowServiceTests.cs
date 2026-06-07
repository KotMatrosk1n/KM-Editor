// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Raids;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Raids;

public sealed class SwShRaidRewardsWorkflowServiceTests
{
    [Fact]
    public void LoadReadsRaidRewardTablesFromSanitizedBaseReadModel()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/raid.rewards.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "tables": [
                {
                  "tableId": "den_001_rank_5_sword",
                  "denId": "den_001",
                  "rank": 5,
                  "gameVersion": "Sword",
                  "rewards": [
                    {
                      "slot": 2,
                      "itemId": 2,
                      "itemName": "Rare Candy",
                      "quantity": 1,
                      "weight": 5
                    },
                    {
                      "slot": 1,
                      "itemId": 1,
                      "itemName": "Exp. Candy L",
                      "quantity": 2,
                      "weight": 40
                    }
                  ]
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidRewardsWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        var table = Assert.Single(workflow.Tables);
        Assert.Equal("den_001_rank_5_sword", table.TableId);
        Assert.Equal("den_001", table.DenId);
        Assert.Equal(5, table.Rank);
        Assert.Equal("Sword", table.GameVersion);
        Assert.Equal(2, table.Rewards.Count);
        Assert.Equal("Exp. Candy L", table.Rewards[0].ItemName);
        Assert.Equal(2, table.Rewards[0].Quantity);
        Assert.Equal("Rare Candy", table.Rewards[1].ItemName);
        Assert.Equal(ProjectFileLayer.Base, table.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, table.Provenance.FileState);
        Assert.Equal(1, workflow.Stats.TotalTableCount);
        Assert.Equal(2, workflow.Stats.TotalRewardItemCount);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenReadModelIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/raid-rewards.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidRewardsWorkflowService().Load(project);

        Assert.Empty(workflow.Tables);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.raidRewards");
    }

    [Fact]
    public void LoadWarnsWhenTableIdsAreDuplicated()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/raid.rewards.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "tables": [
                {
                  "tableId": "den_001_rank_5_sword",
                  "denId": "den_001",
                  "rank": 5,
                  "gameVersion": "Sword",
                  "rewards": []
                },
                {
                  "tableId": "den_001_rank_5_sword",
                  "denId": "den_001",
                  "rank": 5,
                  "gameVersion": "Shield",
                  "rewards": []
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidRewardsWorkflowService().Load(project);

        Assert.Equal(2, workflow.Tables.Count);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.raidRewards");
    }
}
