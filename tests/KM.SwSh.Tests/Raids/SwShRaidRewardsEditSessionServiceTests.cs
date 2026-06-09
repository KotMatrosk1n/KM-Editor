// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Editing;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Raids;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.Raids;

public sealed class SwShRaidRewardsEditSessionServiceTests
{
    [Fact]
    public void UpdateRewardFieldAddsPendingEditAndOverlaysWorkflow()
    {
        using var temp = CreateEditableProject();
        var service = new SwShRaidRewardsEditSessionService();
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShRaidRewardsWorkflowService().Load(project);
        var dropTable = workflow.Tables.Single(table => table.RewardKind == "drop");
        var session = EditSession.Start();

        var result = service.UpdateRewardField(
            temp.Paths,
            session,
            dropTable.TableId,
            slot: 1,
            SwShRaidRewardsWorkflowService.Star3ValueField,
            "55");

        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.raidRewards", edit.Domain);
        Assert.Equal(SwShRaidRewardsWorkflowService.Star3ValueField, edit.Field);
        Assert.Contains("3-star drop chance", edit.Summary, StringComparison.Ordinal);
        var updatedTable = result.Workflow.Tables.Single(table => table.TableId == dropTable.TableId);
        Assert.Equal(55, updatedTable.Rewards[0].Values[2]);
    }

    [Fact]
    public void UpdateRewardFieldRejectsDropValuesAboveOneHundred()
    {
        using var temp = CreateEditableProject();
        var service = new SwShRaidRewardsEditSessionService();
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShRaidRewardsWorkflowService().Load(project);
        var dropTable = workflow.Tables.Single(table => table.RewardKind == "drop");

        var result = service.UpdateRewardField(
            temp.Paths,
            EditSession.Start(),
            dropTable.TableId,
            slot: 1,
            SwShRaidRewardsWorkflowService.Star1ValueField,
            "101");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyChangePlanWritesUpdatedRaidRewardPackToOutputRoot()
    {
        using var temp = CreateEditableProject();
        var service = new SwShRaidRewardsEditSessionService();
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShRaidRewardsWorkflowService().Load(project);
        var dropTable = workflow.Tables.Single(table => table.RewardKind == "drop");
        var update = service.UpdateRewardField(
            temp.Paths,
            EditSession.Start(),
            dropTable.TableId,
            slot: 2,
            SwShRaidRewardsWorkflowService.ItemIdField,
            "4");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal("romfs/bin/archive/field/resident/data_table.gfpak", Assert.Single(apply.WrittenFiles).RelativePath);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "archive",
            "field",
            "resident",
            "data_table.gfpak");
        var outputPack = SwShGfPackFile.Parse(File.ReadAllBytes(outputPath));
        var dropArchive = SwShNestHoleRewardArchive.Parse(outputPack.GetFileByName("nest_hole_drop_rewards.bin"));
        Assert.Equal(4u, dropArchive.Tables[0].Rewards[1].ItemId);
    }

    private static TemporarySwShProject CreateEditableProject()
    {
        var temp = TemporarySwShProject.Create();
        SwShRaidRewardTestFixtures.WriteBaseRaidRewards(temp);
        temp.WriteBaseExeFsFile("main", "base-main");

        return temp;
    }
}
