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
    public void UpdateFieldRejectsUnverifiedMultiBattleType()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTrainersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            trainerId: 10,
            slot: null,
            field: SwShTrainersWorkflowService.BattleTypeField,
            value: "2");

        Assert.False(result.Session.HasPendingChanges);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("between 0 and 1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdateFieldCreatesPendingTrainerMetadataEditAndOverlaysWorkflow()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTrainersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            trainerId: 10,
            slot: null,
            field: SwShTrainersWorkflowService.MoneyField,
            value: "99");

        Assert.Empty(result.Diagnostics);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.trainers", edit.Domain);
        Assert.Equal("money", edit.Field);
        Assert.Equal("10", edit.RecordId);
        Assert.Equal("99", edit.NewValue);
        Assert.Equal(99, Assert.Single(result.Workflow.Trainers).Money);
    }

    [Fact]
    public void UpdateFieldCreatesPendingClassBallEditAndOverlaysWorkflow()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTrainersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            trainerId: 10,
            slot: null,
            field: SwShTrainersWorkflowService.ClassBallIdField,
            value: "3");

        Assert.Empty(result.Diagnostics);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.trainers", edit.Domain);
        Assert.Equal("classBallId", edit.Field);
        Assert.Equal("5", edit.RecordId);
        Assert.Equal("3", edit.NewValue);
        var trainer = Assert.Single(result.Workflow.Trainers);
        Assert.Equal(3, trainer.ClassBallId);
        Assert.Equal("3 Great Ball", trainer.ClassBall);
    }

    [Fact]
    public void UpdateFieldRejectsSharedClassBallEdit()
    {
        using var temp = CreateEditableProject();
        temp.WriteBaseRomFsFile(
            "bin/trainer/trainer_data/trainer_011.bin",
            SwShTrainersWorkflowServiceTests.CreateTrainerData(classId: 5, battleMode: 0, pokemonCount: 1));
        temp.WriteBaseRomFsFile(
            "bin/trainer/trainer_poke/trainer_011.bin",
            SwShTrainersWorkflowServiceTests.CreateTrainerTeam(
                (speciesId: 821, level: 11, heldItemId: 0, moves: new[] { 3, 0, 0, 0 })));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/trname.dat",
            SwShTrainersWorkflowServiceTests.CreateTextTable(11, (10, "Avery"), (11, "Other Trainer")));
        var service = new SwShTrainersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            trainerId: 10,
            slot: null,
            field: SwShTrainersWorkflowService.ClassBallIdField,
            value: "3");

        Assert.False(result.Session.HasPendingChanges);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("uniquely owned", StringComparison.OrdinalIgnoreCase));
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
    public void UpdateFieldCreatesPendingPartyStatEditAndOverlaysWorkflow()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTrainersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            trainerId: 10,
            slot: 1,
            field: SwShTrainersWorkflowService.IvAttackField,
            value: "80");

        Assert.Empty(result.Diagnostics);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.trainers", edit.Domain);
        Assert.Equal("ivAttack", edit.Field);
        Assert.Equal("10:1", edit.RecordId);
        Assert.Equal("31", edit.NewValue);
        Assert.Equal(31, Assert.Single(result.Workflow.Trainers).Team[0].Ivs.Attack);
    }

    [Fact]
    public void UpdateFieldClampsTrainerPartyIvAndEvSpreads()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTrainersEditSessionService();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            trainerId: 10,
            slot: 1,
            field: SwShTrainersWorkflowService.IvAttackField,
            value: "-50");
        update = service.UpdateField(
            temp.Paths,
            update.Session,
            trainerId: 10,
            slot: 1,
            field: SwShTrainersWorkflowService.EvHpField,
            value: "999");
        update = service.UpdateField(
            temp.Paths,
            update.Session,
            trainerId: 10,
            slot: 1,
            field: SwShTrainersWorkflowService.EvAttackField,
            value: "999");

        Assert.Empty(update.Diagnostics);
        var trainer = Assert.Single(update.Workflow.Trainers);
        Assert.Equal(0, trainer.Team[0].Ivs.Attack);
        Assert.Equal(252, trainer.Team[0].Evs.HP);
        Assert.Equal(78, trainer.Team[0].Evs.Attack);
        Assert.Contains(update.Session.PendingEdits, edit =>
            edit.Field == SwShTrainersWorkflowService.IvAttackField && edit.NewValue == "0");
        Assert.Contains(update.Session.PendingEdits, edit =>
            edit.Field == SwShTrainersWorkflowService.EvHpField && edit.NewValue == "252");
        Assert.Contains(update.Session.PendingEdits, edit =>
            edit.Field == SwShTrainersWorkflowService.EvAttackField && edit.NewValue == "78");
        Assert.True(service.Validate(temp.Paths, update.Session).IsValid);
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
    public void ApplyChangePlanWritesEditedTrainerMetadataToOutputRoot()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTrainersEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            trainerId: 10,
            slot: null,
            field: SwShTrainersWorkflowService.TrainerItem1IdField,
            value: "2");
        update = service.UpdateField(
            temp.Paths,
            update.Session,
            trainerId: 10,
            slot: null,
            field: SwShTrainersWorkflowService.AiFlagsField,
            value: "63");
        update = service.UpdateField(
            temp.Paths,
            update.Session,
            trainerId: 10,
            slot: null,
            field: SwShTrainersWorkflowService.HealField,
            value: "0");
        update = service.UpdateField(
            temp.Paths,
            update.Session,
            trainerId: 10,
            slot: null,
            field: SwShTrainersWorkflowService.MoneyField,
            value: "99");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("romfs/bin/trainer/trainer_data/trainer_010.bin", Assert.Single(apply.WrittenFiles).RelativePath);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "trainer",
            "trainer_data",
            "trainer_010.bin");
        var output = SwShTrainerDataFile.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal([2, 2, 0, 0], output.Record.Items);
        Assert.Equal(63u, output.Record.AiFlags);
        Assert.False(output.Record.Heal);
        Assert.Equal(99, output.Record.Money);
        Assert.Equal(7, output.Record.Gift);
    }

    [Fact]
    public void ApplyChangePlanWritesEditedClassBallToOutputRoot()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTrainersEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            trainerId: 10,
            slot: null,
            field: SwShTrainersWorkflowService.ClassBallIdField,
            value: "3");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("romfs/bin/trainer/trainer_type/trainer_type_005.bin", Assert.Single(apply.WrittenFiles).RelativePath);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "trainer",
            "trainer_type",
            "trainer_type_005.bin");
        var output = SwShTrainerClassFile.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal(3, output.Record.BallId);
        Assert.Equal(8, output.Record.Group);
    }

    [Fact]
    public void ApplyChangePlanWritesEditedPartyStatsToOutputRoot()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTrainersEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            trainerId: 10,
            slot: 1,
            field: SwShTrainersWorkflowService.IvSpecialAttackField,
            value: "30");
        update = service.UpdateField(
            temp.Paths,
            update.Session,
            trainerId: 10,
            slot: 1,
            field: SwShTrainersWorkflowService.CanDynamaxField,
            value: "1");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "trainer",
            "trainer_poke",
            "trainer_010.bin");
        var output = SwShTrainerTeamFile.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal(30, output.Records[0].Ivs.SpecialAttack);
        Assert.Equal(2, output.Records[0].Ivs.Attack);
        Assert.True(output.Records[0].CanDynamax);
    }

    [Fact]
    public void ApplyChangePlanAllowsClearingLastPartySlotAndShrinksTrainerCount()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTrainersEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            trainerId: 10,
            slot: 2,
            field: SwShTrainersWorkflowService.SpeciesIdField,
            value: "0");
        var trainer = Assert.Single(update.Workflow.Trainers);

        Assert.Empty(update.Diagnostics);
        Assert.Equal(6, trainer.Team.Count);
        Assert.Equal(0, trainer.Team[1].SpeciesId);
        Assert.Equal("None", trainer.Team[1].Species);
        Assert.Equal(SwShTrainerTeamFile.MinimumLevel, trainer.Team[1].Level);
        Assert.Equal(0, trainer.Team[1].HeldItemId);
        Assert.Equal([0, 0, 0, 0], trainer.Team[1].MoveIds);
        Assert.Equal(new SwShTrainerPokemonStatsRecord(0, 0, 0, 0, 0, 0), trainer.Team[1].Evs);
        Assert.Equal(new SwShTrainerPokemonStatsRecord(0, 0, 0, 0, 0, 0), trainer.Team[1].Ivs);
        Assert.Equal(0, trainer.Team[1].Ability);
        Assert.True(service.Validate(temp.Paths, update.Session).IsValid);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        Assert.True(plan.CanApply);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == "romfs/bin/trainer/trainer_data/trainer_010.bin");
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == "romfs/bin/trainer/trainer_poke/trainer_010.bin");

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var dataOutputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "trainer",
            "trainer_data",
            "trainer_010.bin");
        var dataOutput = SwShTrainerDataFile.Parse(File.ReadAllBytes(dataOutputPath));
        Assert.Equal(1, dataOutput.Record.PokemonCount);
        var teamOutputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "trainer",
            "trainer_poke",
            "trainer_010.bin");
        var teamOutput = SwShTrainerTeamFile.Parse(File.ReadAllBytes(teamOutputPath));
        var row = Assert.Single(teamOutput.Records);
        Assert.Equal(810, row.SpeciesId);
    }

    [Fact]
    public void ApplyChangePlanAllowsFillingNextEmptyPartySlotAndExpandsTrainerCount()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTrainersEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            trainerId: 10,
            slot: 3,
            field: SwShTrainersWorkflowService.SpeciesIdField,
            value: "810");
        var trainer = Assert.Single(update.Workflow.Trainers);

        Assert.Empty(update.Diagnostics);
        Assert.Equal(6, trainer.Team.Count);
        Assert.Equal(810, trainer.Team[2].SpeciesId);
        Assert.True(service.Validate(temp.Paths, update.Session).IsValid);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var dataOutputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "trainer",
            "trainer_data",
            "trainer_010.bin");
        var dataOutput = SwShTrainerDataFile.Parse(File.ReadAllBytes(dataOutputPath));
        Assert.Equal(3, dataOutput.Record.PokemonCount);
        var teamOutputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "trainer",
            "trainer_poke",
            "trainer_010.bin");
        var teamOutput = SwShTrainerTeamFile.Parse(File.ReadAllBytes(teamOutputPath));
        Assert.Equal(3, teamOutput.Records.Count);
        Assert.Equal(810, teamOutput.Records[2].SpeciesId);
        Assert.Equal(1, teamOutput.Records[2].Level);
    }

    [Fact]
    public void UpdateFieldRejectsEditingEmptyPartySlotDetails()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTrainersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            trainerId: 10,
            slot: 3,
            field: SwShTrainersWorkflowService.Move1IdField,
            value: "1");

        Assert.False(result.Session.HasPendingChanges);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("slot is empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdateFieldRejectsPartySlotGaps()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTrainersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            trainerId: 10,
            slot: 4,
            field: SwShTrainersWorkflowService.SpeciesIdField,
            value: "810");

        Assert.False(result.Session.HasPendingChanges);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("filled in order", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public void UpdateFieldClampsOutOfRangePartyIvValue()
    {
        using var temp = CreateEditableProject();
        var service = new SwShTrainersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            trainerId: 10,
            slot: 1,
            field: SwShTrainersWorkflowService.IvHpField,
            value: "32");

        Assert.Empty(result.Diagnostics);
        Assert.True(result.Session.HasPendingChanges);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("31", edit.NewValue);
        Assert.Equal(31, Assert.Single(result.Workflow.Trainers).Team[0].Ivs.HP);
    }

    private static TemporarySwShProject CreateEditableProject()
    {
        var temp = TemporarySwShProject.Create();
        SwShTrainersWorkflowServiceTests.WriteTrainerFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");

        return temp;
    }
}
