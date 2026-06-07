// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Formats.SwSh;
using KM.SwSh.Tests.Items;
using KM.SwSh.Trainers;
using Xunit;

namespace KM.SwSh.Tests.Trainers;

public sealed class SwShTrainersEditSessionServiceTests
{
    [Fact]
    public void UpdateFieldCreatesPendingTrainerDataEditAndOverlaysWorkflow()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTrainersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            trainerId: 10,
            slot: null,
            field: SwShTrainersWorkflowService.BattleTypeField,
            value: "0");

        Assert.Empty(result.Diagnostics);
        Assert.True(result.Session.HasPendingChanges);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.trainers", edit.Domain);
        Assert.Equal("battleType", edit.Field);
        Assert.Equal("10", edit.RecordId);
        Assert.Equal("0", edit.NewValue);
        Assert.Equal("Singles", Assert.Single(result.Workflow.Trainers).BattleType);
    }

    [Fact]
    public void UpdateFieldCreatesPendingPartyEditAndOverlaysWorkflow()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTrainersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            trainerId: 10,
            slot: 1,
            field: SwShTrainersWorkflowService.LevelField,
            value: "25");

        Assert.Empty(result.Diagnostics);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.trainers", edit.Domain);
        Assert.Equal("level", edit.Field);
        Assert.Equal("10:1", edit.RecordId);
        Assert.Equal("25", edit.NewValue);
        Assert.Equal(25, Assert.Single(result.Workflow.Trainers).Team[0].Level);
    }

    [Fact]
    public void UpdateFieldReplacesPendingEditForSameTrainerField()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTrainersEditSessionService();
        var first = service.UpdateField(
            temp.Paths,
            session: null,
            trainerId: 10,
            slot: 1,
            field: SwShTrainersWorkflowService.LevelField,
            value: "25");

        var second = service.UpdateField(
            temp.Paths,
            first.Session,
            trainerId: 10,
            slot: 1,
            field: SwShTrainersWorkflowService.LevelField,
            value: "30");

        var edit = Assert.Single(second.Session.PendingEdits);
        Assert.Equal("30", edit.NewValue);
        Assert.Equal(30, Assert.Single(second.Workflow.Trainers).Team[0].Level);
    }

    [Fact]
    public void ValidateAndCreateChangePlanUseTrainerTargets()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTrainersEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            trainerId: 10,
            slot: 1,
            field: SwShTrainersWorkflowService.LevelField,
            value: "25");

        var validation = service.Validate(temp.Paths, update.Session);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        Assert.True(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Info);
        Assert.True(plan.CanApply);
        var write = Assert.Single(plan.Writes);
        Assert.Equal("romfs/bin/trainer/trainer_poke/trainer_010.bin", write.TargetRelativePath);
        Assert.Equal("romfs/bin/trainer/trainer_poke/trainer_010.bin", Assert.Single(write.Sources).RelativePath);
        Assert.False(write.ReplacesExistingOutput);
    }

    [Fact]
    public void ApplyChangePlanWritesEditedTrainerFilesToOutputRoot()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTrainersEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            trainerId: 10,
            slot: 1,
            field: SwShTrainersWorkflowService.LevelField,
            value: "25");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("romfs/bin/trainer/trainer_poke/trainer_010.bin", Assert.Single(apply.WrittenFiles).RelativePath);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "trainer",
            "trainer_poke",
            "trainer_010.bin");
        var output = SwShTrainerTeamFile.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal(25, output.Records[0].Level);
        Assert.Equal(11, output.Records[1].Level);
    }

    [Fact]
    public void ApplyChangePlanRejectsStaleReviewedPlan()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTrainersEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            trainerId: 10,
            slot: 1,
            field: SwShTrainersWorkflowService.LevelField,
            value: "25");
        var stalePlan = new ChangePlan(update.Session.Id, Array.Empty<PlannedFileWrite>(), Array.Empty<ValidationDiagnostic>());

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, stalePlan);

        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdateFieldRequiresEditableProjectPaths()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTrainersEditSessionService();

        var result = service.UpdateField(
            temp.Paths with { OutputRootPath = null },
            session: null,
            trainerId: 10,
            slot: 1,
            field: SwShTrainersWorkflowService.LevelField,
            value: "25");

        Assert.False(result.Session.HasPendingChanges);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static TemporarySwShProject CreateEditableProject()
    {
        var temp = TemporarySwShProject.Create();
        SwShTrainersWorkflowServiceTests.WriteTrainerFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");

        return temp;
    }
}
