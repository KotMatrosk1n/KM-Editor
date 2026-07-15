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
        Assert.Equal(55L, updatedTable.Rewards[0].Values[2]);
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
    public void UpdateRewardFieldsRejectsTheEntireBatchWhenOneValueIsInvalid()
    {
        using var temp = CreateEditableProject();
        var service = new SwShRaidRewardsEditSessionService();
        var workflow = new SwShRaidRewardsWorkflowService().Load(
            new ProjectWorkspaceService().Open(temp.Paths));
        var table = Assert.Single(workflow.Tables);

        var result = service.UpdateRewardFields(
            temp.Paths,
            EditSession.Start(),
            [
                new SwShRaidRewardFieldUpdate(
                    table.TableId,
                    1,
                    SwShRaidRewardsWorkflowService.Star2ValueField,
                    "50"),
                new SwShRaidRewardFieldUpdate(
                    table.TableId,
                    1,
                    SwShRaidRewardsWorkflowService.Star1ValueField,
                    "101"),
            ]);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Equal(30L, Assert.Single(result.Workflow.Tables).Rewards[0].Values[1]);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void UpdateRewardFieldsRejectsDuplicateTargetsAtomically()
    {
        using var temp = CreateEditableProject();
        var service = new SwShRaidRewardsEditSessionService();
        var workflow = new SwShRaidRewardsWorkflowService().Load(
            new ProjectWorkspaceService().Open(temp.Paths));
        var table = Assert.Single(workflow.Tables);

        var result = service.UpdateRewardFields(
            temp.Paths,
            EditSession.Start(),
            [
                new SwShRaidRewardFieldUpdate(
                    table.TableId,
                    1,
                    SwShRaidRewardsWorkflowService.Star2ValueField,
                    "50"),
                new SwShRaidRewardFieldUpdate(
                    table.TableId,
                    1,
                    SwShRaidRewardsWorkflowService.Star2ValueField,
                    "60"),
            ]);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
                && diagnostic.Message.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdatingAFieldBackToItsSourceValueRemovesThePendingEdit()
    {
        using var temp = CreateEditableProject();
        var service = new SwShRaidRewardsEditSessionService();
        var workflow = new SwShRaidRewardsWorkflowService().Load(
            new ProjectWorkspaceService().Open(temp.Paths));
        var table = Assert.Single(workflow.Tables);
        var staged = service.UpdateRewardField(
            temp.Paths,
            EditSession.Start(),
            table.TableId,
            1,
            SwShRaidRewardsWorkflowService.Star3ValueField,
            "55");

        var reverted = service.UpdateRewardField(
            temp.Paths,
            staged.Session,
            table.TableId,
            1,
            SwShRaidRewardsWorkflowService.Star3ValueField,
            "20");

        Assert.Empty(reverted.Session.PendingEdits);
        Assert.Equal(20L, Assert.Single(reverted.Workflow.Tables).Rewards[0].Values[2]);
    }

    [Fact]
    public void ItemUpdatesUseLoadedNamesAndRejectUnknownIds()
    {
        using var temp = CreateEditableProject();
        var service = new SwShRaidRewardsEditSessionService();
        var workflow = new SwShRaidRewardsWorkflowService().Load(
            new ProjectWorkspaceService().Open(temp.Paths));
        var table = Assert.Single(workflow.Tables);

        var accepted = service.UpdateRewardField(
            temp.Paths,
            EditSession.Start(),
            table.TableId,
            1,
            SwShRaidRewardsWorkflowService.ItemIdField,
            "1");
        var rejected = service.UpdateRewardField(
            temp.Paths,
            EditSession.Start(),
            table.TableId,
            1,
            SwShRaidRewardsWorkflowService.ItemIdField,
            "500");

        Assert.Equal("Potion", Assert.Single(accepted.Workflow.Tables).Rewards[0].ItemName);
        Assert.Empty(rejected.Session.PendingEdits);
        Assert.Contains(
            rejected.Diagnostics,
            diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
                && diagnostic.Message.Contains("current Sword/Shield item table", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceEquivalentFullWidthItemIdRemovesPendingEdit()
    {
        using var temp = CreateEditableProject();
        var sourceArchive = new SwShNestHoleRewardArchive(
        [
            new SwShNestHoleRewardTable(
                SwShRaidRewardTestFixtures.DropTableId,
                [new SwShNestHoleReward(1, uint.MaxValue, [40, 30, 20, 10, 5])]),
        ]);
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/data_table.gfpak",
            SwShGfPackFile.Create(
            [
                new SwShGfPackNamedFile("nest_hole_drop_rewards.bin", sourceArchive.Write()),
                new SwShGfPackNamedFile(
                    "nest_hole_bonus_rewards.bin",
                    SwShRaidRewardTestFixtures.CreateBonusArchive().Write()),
            ]).Write());
        var service = new SwShRaidRewardsEditSessionService();
        var workflow = new SwShRaidRewardsWorkflowService().Load(
            new ProjectWorkspaceService().Open(temp.Paths));
        var table = Assert.Single(workflow.Tables);
        var staged = service.UpdateRewardField(
            temp.Paths,
            EditSession.Start(),
            table.TableId,
            1,
            SwShRaidRewardsWorkflowService.ItemIdField,
            "1");

        var reverted = service.UpdateRewardField(
            temp.Paths,
            staged.Session,
            table.TableId,
            1,
            SwShRaidRewardsWorkflowService.ItemIdField,
            uint.MaxValue.ToString());

        Assert.Empty(reverted.Session.PendingEdits);
        Assert.Equal((long)uint.MaxValue, Assert.Single(reverted.Workflow.Tables).Rewards[0].ItemId);
        Assert.DoesNotContain(
            reverted.Diagnostics,
            diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void ReviewedPlanRejectsPendingValueDrift()
    {
        using var temp = CreateEditableProject();
        var service = new SwShRaidRewardsEditSessionService();
        var workflow = new SwShRaidRewardsWorkflowService().Load(
            new ProjectWorkspaceService().Open(temp.Paths));
        var table = Assert.Single(workflow.Tables);
        var first = service.UpdateRewardField(
            temp.Paths,
            EditSession.Start(),
            table.TableId,
            1,
            SwShRaidRewardsWorkflowService.Star3ValueField,
            "55");
        var reviewed = service.CreateChangePlan(temp.Paths, first.Session);
        var changed = service.UpdateRewardField(
            temp.Paths,
            first.Session,
            table.TableId,
            1,
            SwShRaidRewardsWorkflowService.Star3ValueField,
            "65");

        var apply = service.ApplyChangePlan(temp.Paths, changed.Session, reviewed);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SignedTableIdentityRejectsRowReorderingBeforeStaging()
    {
        using var temp = CreateEditableProject();
        var workspace = new ProjectWorkspaceService();
        var service = new SwShRaidRewardsEditSessionService(workspace);
        var workflow = new SwShRaidRewardsWorkflowService().Load(workspace.Open(temp.Paths));
        var staleTable = Assert.Single(workflow.Tables);
        var original = SwShRaidRewardTestFixtures.CreateDropArchive();
        var reordered = original with
        {
            Tables =
            [
                original.Tables[0] with
                {
                    Rewards = original.Tables[0].Rewards.Reverse().ToArray(),
                },
            ],
        };
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/data_table.gfpak",
            SwShGfPackFile.Create(
            [
                new SwShGfPackNamedFile("nest_hole_drop_rewards.bin", reordered.Write()),
                new SwShGfPackNamedFile(
                    "nest_hole_bonus_rewards.bin",
                    SwShRaidRewardTestFixtures.CreateBonusArchive().Write()),
            ]).Write());

        var result = service.UpdateRewardField(
            temp.Paths,
            EditSession.Start(),
            staleTable.TableId,
            1,
            SwShRaidRewardsWorkflowService.Star1ValueField,
            "50");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
                && diagnostic.Message.Contains("current source", StringComparison.Ordinal));
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

    [Fact]
    public void SequentialDropAndBonusAppliesPreserveBothMembers()
    {
        using var temp = CreateEditableProject();
        var workspace = new ProjectWorkspaceService();
        var service = new SwShRaidRewardsEditSessionService(workspace);
        var workflowService = new SwShRaidRewardsWorkflowService();
        var dropWorkflow = workflowService.Load(workspace.Open(temp.Paths));
        var dropTable = Assert.Single(dropWorkflow.Tables);
        var dropUpdate = service.UpdateRewardField(
            temp.Paths,
            EditSession.Start(),
            dropTable.TableId,
            1,
            SwShRaidRewardsWorkflowService.Star1ValueField,
            "77");
        var dropPlan = service.CreateChangePlan(temp.Paths, dropUpdate.Session);
        var dropApply = service.ApplyChangePlan(temp.Paths, dropUpdate.Session, dropPlan);
        Assert.DoesNotContain(
            dropApply.Diagnostics,
            diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);

        workspace.ClearMemoryCache();
        var bonusWorkflow = workflowService.LoadBonus(workspace.Open(temp.Paths));
        var bonusTable = Assert.Single(bonusWorkflow.Tables);
        var bonusUpdate = service.UpdateBonusRewardField(
            temp.Paths,
            EditSession.Start(),
            bonusTable.TableId,
            1,
            SwShRaidRewardsWorkflowService.Star5ValueField,
            "9");
        var bonusPlan = service.CreateChangePlan(temp.Paths, bonusUpdate.Session);
        var bonusApply = service.ApplyChangePlan(temp.Paths, bonusUpdate.Session, bonusPlan);
        Assert.DoesNotContain(
            bonusApply.Diagnostics,
            diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error);

        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "archive",
            "field",
            "resident",
            "data_table.gfpak");
        var outputPack = SwShGfPackFile.Parse(File.ReadAllBytes(outputPath));
        var dropArchive = SwShNestHoleRewardArchive.Parse(
            outputPack.GetFileByName("nest_hole_drop_rewards.bin"));
        var bonusArchive = SwShNestHoleRewardArchive.Parse(
            outputPack.GetFileByName("nest_hole_bonus_rewards.bin"));
        Assert.Equal(77u, dropArchive.Tables[0].Rewards[0].Values[0]);
        Assert.Equal(9u, bonusArchive.Tables[0].Rewards[0].Values[4]);
    }

    [Fact]
    public void DirectServiceRejectsMixedDropAndBonusSessions()
    {
        using var temp = CreateEditableProject();
        var workspace = new ProjectWorkspaceService();
        var service = new SwShRaidRewardsEditSessionService(workspace);
        var workflowService = new SwShRaidRewardsWorkflowService();
        var dropTable = Assert.Single(workflowService.Load(workspace.Open(temp.Paths)).Tables);
        var bonusTable = Assert.Single(workflowService.LoadBonus(workspace.Open(temp.Paths)).Tables);
        var dropUpdate = service.UpdateRewardField(
            temp.Paths,
            EditSession.Start(),
            dropTable.TableId,
            1,
            SwShRaidRewardsWorkflowService.Star1ValueField,
            "77");
        var mixedUpdate = service.UpdateBonusRewardField(
            temp.Paths,
            dropUpdate.Session,
            bonusTable.TableId,
            1,
            SwShRaidRewardsWorkflowService.Star5ValueField,
            "9");

        var validation = service.Validate(temp.Paths, mixedUpdate.Session);
        var plan = service.CreateChangePlan(temp.Paths, mixedUpdate.Session);
        var apply = service.ApplyChangePlan(temp.Paths, mixedUpdate.Session, plan);

        Assert.Equal(2, mixedUpdate.Session.PendingEdits.Count);
        Assert.False(validation.IsValid);
        Assert.Empty(plan.Writes);
        Assert.Empty(apply.WrittenFiles);
        Assert.All(
            [validation.Diagnostics, plan.Diagnostics, apply.Diagnostics],
            diagnostics => Assert.Contains(
                diagnostics,
                diagnostic => diagnostic.Severity == Core.Diagnostics.DiagnosticSeverity.Error
                    && diagnostic.Message.Contains("cannot be planned directly", StringComparison.Ordinal)));
    }

    private static TemporarySwShProject CreateEditableProject()
    {
        var temp = TemporarySwShProject.Create();
        SwShRaidRewardTestFixtures.WriteBaseRaidRewards(temp);
        temp.WriteBaseExeFsFile("main", "base-main");

        return temp;
    }
}
