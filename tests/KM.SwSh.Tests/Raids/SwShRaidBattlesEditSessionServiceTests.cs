// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Editing;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Raids;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.Raids;

public sealed class SwShRaidBattlesEditSessionServiceTests
{
    [Fact]
    public void UpdateSlotFieldAddsPendingIvEditAndOverlaysWorkflow()
    {
        using var temp = CreateEditableProject();
        var service = new SwShRaidBattlesEditSessionService();
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShRaidBattlesWorkflowService().Load(project);
        var table = Assert.Single(workflow.Tables);

        var result = service.UpdateSlotField(
            temp.Paths,
            EditSession.Start(),
            table.TableId,
            slot: 2,
            SwShRaidBattlesWorkflowService.FlawlessIvsField,
            "6");

        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.raidBattles", edit.Domain);
        Assert.Equal(SwShRaidBattlesWorkflowService.FlawlessIvsField, edit.Field);
        var updatedTable = Assert.Single(result.Workflow.Tables);
        Assert.Equal(6, updatedTable.Slots[1].FlawlessIvs);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void UpdateSlotFieldRejectsProbabilityAboveOneHundred()
    {
        using var temp = CreateEditableProject();
        var service = new SwShRaidBattlesEditSessionService();
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShRaidBattlesWorkflowService().Load(project);
        var table = Assert.Single(workflow.Tables);

        var result = service.UpdateSlotField(
            temp.Paths,
            EditSession.Start(),
            table.TableId,
            slot: 1,
            SwShRaidBattlesWorkflowService.Star1ProbabilityField,
            "101");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyChangePlanWritesUpdatedRaidBattlePackToOutputRoot()
    {
        using var temp = CreateEditableProject();
        var service = new SwShRaidBattlesEditSessionService();
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShRaidBattlesWorkflowService().Load(project);
        var table = Assert.Single(workflow.Tables);

        var update = service.UpdateSlotField(
            temp.Paths,
            null,
            table.TableId,
            slot: 2,
            SwShRaidBattlesWorkflowService.SpeciesField,
            "133");
        update = service.UpdateSlotField(
            temp.Paths,
            update.Session,
            table.TableId,
            slot: 2,
            SwShRaidBattlesWorkflowService.IsGigantamaxField,
            "1");
        update = service.UpdateSlotField(
            temp.Paths,
            update.Session,
            table.TableId,
            slot: 2,
            SwShRaidBattlesWorkflowService.FlawlessIvsField,
            "6");
        update = service.UpdateSlotField(
            temp.Paths,
            update.Session,
            table.TableId,
            slot: 2,
            SwShRaidBattlesWorkflowService.Star5ProbabilityField,
            "80");

        var validation = service.Validate(temp.Paths, update.Session);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.True(validation.IsValid);
        Assert.True(plan.CanApply);
        Assert.Equal(SwShRaidRewardsWorkflowService.NestDataPath, Assert.Single(plan.Writes).TargetRelativePath);
        Assert.Equal(SwShRaidRewardsWorkflowService.NestDataPath, Assert.Single(apply.WrittenFiles).RelativePath);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error);

        var outputPack = SwShGfPackFile.Parse(File.ReadAllBytes(GetOutputNestDataPath(temp)));
        var battleArchive = SwShEncounterNestArchive.Parse(outputPack.GetFileByName(SwShRaidBattlesWorkflowService.EncounterMemberName));
        var updatedSlot = battleArchive.Tables[0].Entries[1];
        Assert.Equal(133, updatedSlot.Species);
        Assert.True(updatedSlot.IsGigantamax);
        Assert.Equal(6, updatedSlot.FlawlessIvs);
        Assert.Equal(80u, updatedSlot.Probabilities[4]);

        var rewardArchive = SwShNestHoleRewardArchive.Parse(outputPack.GetFileByName("nest_hole_drop_rewards.bin"));
        Assert.Equal(3u, rewardArchive.Tables[0].Rewards[0].ItemId);
    }

    private static TemporarySwShProject CreateEditableProject()
    {
        var temp = TemporarySwShProject.Create();
        SwShRaidBattleTestFixtures.WriteBaseRaidBattles(temp);
        temp.WriteBaseExeFsFile("main", "base-main");

        return temp;
    }

    private static string GetOutputNestDataPath(TemporarySwShProject temp)
    {
        return Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "archive",
            "field",
            "resident",
            "data_table.gfpak");
    }
}
