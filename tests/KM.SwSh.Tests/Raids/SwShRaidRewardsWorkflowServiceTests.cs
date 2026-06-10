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
    public void LoadReadsRaidRewardTablesFromRealNestDataPack()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRaidRewardTestFixtures.WriteBaseRaidRewards(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidRewardsWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Single(workflow.Tables);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShRaidRewardsWorkflowService.ItemIdField);
        var dropTable = workflow.Tables.Single(table => table.RewardKind == "drop");
        Assert.Equal("Drop 000", dropTable.DisplayName);
        Assert.Equal("nest_hole_drop_rewards.bin", dropTable.ArchiveMember);
        Assert.Equal("0xAABBCCDD00112233", dropTable.SourceTableHash);
        Assert.Equal(ProjectFileLayer.Base, dropTable.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, dropTable.Provenance.FileState);
        Assert.Equal("Exp. Candy L", dropTable.Rewards[0].ItemName);
        Assert.Equal([40, 30, 20, 10, 5], dropTable.Rewards[0].Values);
        Assert.Equal(1, workflow.Stats.TotalTableCount);
        Assert.Equal(2, workflow.Stats.TotalRewardItemCount);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadBonusReadsRaidBonusRewardTablesFromRealNestDataPack()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRaidRewardTestFixtures.WriteBaseRaidRewards(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidRewardsWorkflowService().LoadBonus(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Single(workflow.Tables);
        Assert.Equal(SwShWorkflowIds.RaidBonusRewards, workflow.Summary.Id);
        var bonusTable = workflow.Tables.Single(table => table.RewardKind == "bonus");
        Assert.Equal("Bonus 000", bonusTable.DisplayName);
        Assert.Equal("nest_hole_bonus_rewards.bin", bonusTable.ArchiveMember);
        Assert.Equal("Armorite Ore", bonusTable.Rewards[0].ItemName);
        Assert.Equal([1, 2, 3, 4, 5], bonusTable.Rewards[0].Values);
        Assert.Equal(1, workflow.Stats.TotalTableCount);
        Assert.Equal(1, workflow.Stats.TotalRewardItemCount);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadUsesRaidBattleUsageLabelsWhenEncounterDataIsAvailable()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRaidBattleTestFixtures.WriteBaseRaidBattles(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidRewardsWorkflowService().Load(project);
        var bonusWorkflow = new SwShRaidRewardsWorkflowService().LoadBonus(project);

        Assert.Equal(
            "Drop 000 | SW Den 0 Slot 00, 1-5-Star Eevee-1; SW Den 0 Slot 01, 1-5-Star Pikachu",
            Assert.Single(workflow.Tables).DisplayName);
        Assert.Equal(
            "Bonus 000 | SW Den 0 Slot 00, 1-5-Star Eevee-1",
            Assert.Single(bonusWorkflow.Tables).DisplayName);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenNestDataPackIsMissing()
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
    public void LoadReturnsDiagnosticWhenNestDataPackIsUnsupported()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("bin/archive/field/resident/data_table.gfpak", "not-a-pack");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidRewardsWorkflowService().Load(project);

        Assert.Empty(workflow.Tables);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Domain == "workflow.raidRewards");
    }
}
