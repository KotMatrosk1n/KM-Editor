// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using Xunit;

namespace KM.SwSh.Tests.Pokemon;

public sealed class SwShPokemonEditSessionServiceTests
{
    [Fact]
    public void UpdateFieldOverlaysPendingPersonalEdit()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.HPField,
            "99");

        var pokemon = result.Workflow.Pokemon.Single(record => record.PersonalId == 1);
        Assert.Equal(99, pokemon.BaseStats.HP);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.pokemon", edit.Domain);
        Assert.Equal("1", edit.RecordId);
        Assert.Equal(SwShPokemonWorkflowService.HPField, edit.Field);
        Assert.Equal("99", edit.NewValue);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void UpdateFieldOverlaysBooleanPersonalFlag()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.CanNotDynamaxField,
            "true");

        var pokemon = result.Workflow.Pokemon.Single(record => record.PersonalId == 1);
        Assert.True(pokemon.Personal.CanNotDynamax);
        Assert.Equal("1", Assert.Single(result.Session.PendingEdits).NewValue);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void UpdateFieldOverlaysCompatibilityFlag()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();
        var field = SwShPokemonWorkflowService.CreateCompatibilityFieldId(
            SwShPokemonWorkflowService.TechnicalMachineCompatibilityGroupId,
            0);

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            field,
            "true");

        var pokemon = result.Workflow.Pokemon.Single(record => record.PersonalId == 1);
        var tmGroup = pokemon.Compatibility.Single(group => group.GroupId == SwShPokemonWorkflowService.TechnicalMachineCompatibilityGroupId);
        Assert.True(tmGroup.Entries.Single(entry => entry.Slot == 0).CanLearn);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal(field, edit.Field);
        Assert.Equal("1", edit.NewValue);
        Assert.Equal("Enable Bulbasaur TM00 (Mega Punch) compatibility.", edit.Summary);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void UpdateLearnsetOverlaysPendingRowEdit()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();

        var result = service.UpdateLearnset(
            temp.Paths,
            session: null,
            personalId: 1,
            action: "upsert",
            slot: 1,
            moveId: 345,
            level: 7);

        var pokemon = result.Workflow.Pokemon.Single(record => record.PersonalId == 1);
        Assert.Collection(
            pokemon.Learnset,
            move =>
            {
                Assert.Equal(0, move.Slot);
                Assert.Equal(33, move.MoveId);
                Assert.Equal(1, move.Level);
            },
            move =>
            {
                Assert.Equal(1, move.Slot);
                Assert.Equal(345, move.MoveId);
                Assert.Equal("Magical Leaf", move.MoveName);
                Assert.Equal(7, move.Level);
            });
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.pokemon", edit.Domain);
        Assert.Equal("1", edit.RecordId);
        Assert.Equal("learnset:upsert:1", edit.Field);
        Assert.Equal("345:7", edit.NewValue);
        Assert.Equal("Set Bulbasaur learnset slot 1 to Lv. 7 Magical Leaf.", edit.Summary);
        Assert.Contains(edit.Sources, source => source.RelativePath == SwShPokemonWorkflowService.LearnsetDataPath);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void UpdateLearnsetOverlaysPendingMoveToEdit()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();

        var result = service.UpdateLearnset(
            temp.Paths,
            session: null,
            personalId: 1,
            action: "moveTo",
            slot: 1,
            moveId: 0,
            level: null);

        var pokemon = result.Workflow.Pokemon.Single(record => record.PersonalId == 1);
        Assert.Collection(
            pokemon.Learnset,
            move =>
            {
                Assert.Equal(0, move.Slot);
                Assert.Equal(45, move.MoveId);
                Assert.Equal(3, move.Level);
            },
            move =>
            {
                Assert.Equal(1, move.Slot);
                Assert.Equal(33, move.MoveId);
                Assert.Equal(1, move.Level);
            });
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.pokemon", edit.Domain);
        Assert.Equal("1", edit.RecordId);
        Assert.Equal("learnset:moveTo:1", edit.Field);
        Assert.Equal("0", edit.NewValue);
        Assert.Equal("Move Bulbasaur learnset slot 1 to slot 0.", edit.Summary);
        Assert.Contains(edit.Sources, source => source.RelativePath == SwShPokemonWorkflowService.LearnsetDataPath);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void UpdateEvolutionOverlaysPendingRowEdit()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();

        var result = service.UpdateEvolution(
            temp.Paths,
            session: null,
            personalId: 1,
            action: "upsert",
            slot: 0,
            method: 8,
            argument: 25,
            species: 2,
            form: 1,
            level: 32);

        var pokemon = result.Workflow.Pokemon.Single(record => record.PersonalId == 1);
        var evolution = Assert.Single(pokemon.Evolutions);
        Assert.Equal(0, evolution.Slot);
        Assert.Equal(8, evolution.Method);
        Assert.Equal(25, evolution.Argument);
        Assert.Equal(2, evolution.Species);
        Assert.Equal(1, evolution.Form);
        Assert.Equal(32, evolution.Level);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.pokemon", edit.Domain);
        Assert.Equal("1", edit.RecordId);
        Assert.Equal("evolution:upsert:0", edit.Field);
        Assert.Equal("8:25:2:1:32", edit.NewValue);
        Assert.Equal("Set Bulbasaur evolution slot 0 to species 2 at level 32.", edit.Summary);
        Assert.Contains(edit.Sources, source => source.RelativePath == SwShPokemonWorkflowService.CreateEvolutionDataPath(1));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void UpdateFieldReplacesPendingEditForSamePokemonAndField()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();
        var first = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.HPField,
            "90");

        var second = service.UpdateField(
            temp.Paths,
            first.Session,
            personalId: 1,
            SwShPokemonWorkflowService.HPField,
            "91");

        var edit = Assert.Single(second.Session.PendingEdits);
        Assert.Equal("91", edit.NewValue);
        Assert.Equal(91, second.Workflow.Pokemon.Single(record => record.PersonalId == 1).BaseStats.HP);
    }

    [Fact]
    public void CreateChangePlanTargetsPersonalTable()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.Type1Field,
            "9");

        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        Assert.True(plan.CanApply);
        var write = Assert.Single(plan.Writes);
        Assert.Equal(SwShPokemonWorkflowService.PersonalDataPath, write.TargetRelativePath);
        Assert.Contains(write.Sources, source => source.Layer == ProjectFileLayer.Base);
    }

    [Fact]
    public void CreateChangePlanCanTargetPersonalAndLearnsetTables()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();
        var personalUpdate = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.Type1Field,
            "9");
        var learnsetUpdate = service.UpdateLearnset(
            temp.Paths,
            personalUpdate.Session,
            personalId: 1,
            action: "add",
            slot: null,
            moveId: 520,
            level: 12);

        var plan = service.CreateChangePlan(temp.Paths, learnsetUpdate.Session);

        Assert.True(plan.CanApply);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == SwShPokemonWorkflowService.PersonalDataPath);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == SwShPokemonWorkflowService.LearnsetDataPath);
    }

    [Fact]
    public void CreateChangePlanCanTargetEvolutionFile()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();
        var evolutionUpdate = service.UpdateEvolution(
            temp.Paths,
            session: null,
            personalId: 1,
            action: "add",
            slot: null,
            method: 4,
            argument: 0,
            species: 3,
            form: 0,
            level: 36);

        var plan = service.CreateChangePlan(temp.Paths, evolutionUpdate.Session);

        Assert.True(plan.CanApply);
        var write = Assert.Single(plan.Writes);
        Assert.Equal(SwShPokemonWorkflowService.CreateEvolutionDataPath(1), write.TargetRelativePath);
        Assert.Contains(write.Sources, source => source.Layer == ProjectFileLayer.Base);
    }

    [Fact]
    public void ApplyChangePlanWritesOutputPersonalTableAndLeavesBaseUntouched()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();
        var hpUpdate = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.HPField,
            "100");
        var flagUpdate = service.UpdateField(
            temp.Paths,
            hpUpdate.Session,
            personalId: 1,
            SwShPokemonWorkflowService.CanNotDynamaxField,
            "true");
        var compatibilityUpdate = service.UpdateField(
            temp.Paths,
            flagUpdate.Session,
            personalId: 1,
            SwShPokemonWorkflowService.CreateCompatibilityFieldId(
                SwShPokemonWorkflowService.TechnicalMachineCompatibilityGroupId,
                0),
            "true");
        var plan = service.CreateChangePlan(temp.Paths, compatibilityUpdate.Session);

        var apply = service.ApplyChangePlan(temp.Paths, compatibilityUpdate.Session, plan);

        Assert.Contains(apply.WrittenFiles, file => file.RelativePath == SwShPokemonWorkflowService.PersonalDataPath);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var outputBytes = File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            SwShPokemonWorkflowService.PersonalDataPath.Replace('/', Path.DirectorySeparatorChar)));
        var outputRecord = SwShPersonalTable.Parse(outputBytes).Records[1];
        Assert.Equal(100, outputRecord.HP);
        Assert.True(outputRecord.CanNotDynamax);
        Assert.True(outputRecord.TechnicalMachines[0]);
        var baseBytes = File.ReadAllBytes(Path.Combine(
            temp.BaseRomFsPath,
            "bin/pml/personal/personal_total.bin"));
        Assert.Equal(45, SwShPersonalTable.Parse(baseBytes).Records[1].HP);
        Assert.False(SwShPersonalTable.Parse(baseBytes).Records[1].TechnicalMachines[0]);
    }

    [Fact]
    public void ApplyChangePlanWritesOutputLearnsetTableAndLeavesBaseUntouched()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();
        var rowUpdate = service.UpdateLearnset(
            temp.Paths,
            session: null,
            personalId: 1,
            action: "upsert",
            slot: 1,
            moveId: 345,
            level: 9);
        var addUpdate = service.UpdateLearnset(
            temp.Paths,
            rowUpdate.Session,
            personalId: 1,
            action: "add",
            slot: null,
            moveId: 520,
            level: 12);
        var plan = service.CreateChangePlan(temp.Paths, addUpdate.Session);

        var apply = service.ApplyChangePlan(temp.Paths, addUpdate.Session, plan);

        Assert.Contains(apply.WrittenFiles, file => file.RelativePath == SwShPokemonWorkflowService.LearnsetDataPath);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var outputBytes = File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            SwShPokemonWorkflowService.LearnsetDataPath.Replace('/', Path.DirectorySeparatorChar)));
        var outputLearnset = SwShPokemonLearnsetTable.Parse(outputBytes).Records[1];
        Assert.Collection(
            outputLearnset.Moves,
            move =>
            {
                Assert.Equal(33, move.MoveId);
                Assert.Equal(1, move.Level);
            },
            move =>
            {
                Assert.Equal(345, move.MoveId);
                Assert.Equal(9, move.Level);
            },
            move =>
            {
                Assert.Equal(520, move.MoveId);
                Assert.Equal(12, move.Level);
            });
        var baseBytes = File.ReadAllBytes(Path.Combine(
            temp.BaseRomFsPath,
            "bin/pml/waza_oboe/wazaoboe_total.bin"));
        var baseLearnset = SwShPokemonLearnsetTable.Parse(baseBytes).Records[1];
        Assert.Collection(
            baseLearnset.Moves,
            move => Assert.Equal(33, move.MoveId),
            move => Assert.Equal(45, move.MoveId));
    }

    [Fact]
    public void ApplyChangePlanWritesOutputEvolutionFileAndLeavesBaseUntouched()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();
        var rowUpdate = service.UpdateEvolution(
            temp.Paths,
            session: null,
            personalId: 1,
            action: "upsert",
            slot: 0,
            method: 8,
            argument: 25,
            species: 2,
            form: 1,
            level: 32);
        var addUpdate = service.UpdateEvolution(
            temp.Paths,
            rowUpdate.Session,
            personalId: 1,
            action: "add",
            slot: null,
            method: 4,
            argument: 0,
            species: 3,
            form: 0,
            level: 36);
        var plan = service.CreateChangePlan(temp.Paths, addUpdate.Session);

        var apply = service.ApplyChangePlan(temp.Paths, addUpdate.Session, plan);

        var targetRelativePath = SwShPokemonWorkflowService.CreateEvolutionDataPath(1);
        Assert.Contains(apply.WrittenFiles, file => file.RelativePath == targetRelativePath);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var outputBytes = File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            targetRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var outputEvolutions = SwShEvolutionSet.Parse(outputBytes).Evolutions;
        Assert.Collection(
            outputEvolutions,
            evolution =>
            {
                Assert.Equal(0, evolution.Slot);
                Assert.Equal(8, evolution.Method);
                Assert.Equal(25, evolution.Argument);
                Assert.Equal(2, evolution.Species);
                Assert.Equal(1, evolution.Form);
                Assert.Equal(32, evolution.Level);
            },
            evolution =>
            {
                Assert.Equal(1, evolution.Slot);
                Assert.Equal(4, evolution.Method);
                Assert.Equal(3, evolution.Species);
                Assert.Equal(36, evolution.Level);
            });
        var baseBytes = File.ReadAllBytes(Path.Combine(
            temp.BaseRomFsPath,
            "bin/pml/evolution/evo_001.bin"));
        var baseEvolution = Assert.Single(SwShEvolutionSet.Parse(baseBytes).Evolutions);
        Assert.Equal(4, baseEvolution.Method);
        Assert.Equal(2, baseEvolution.Species);
        Assert.Equal(16, baseEvolution.Level);
    }

    [Fact]
    public void UpdateFieldRejectsOutOfRangeValue()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.Type1Field,
            "18");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UpdateFieldRequiresEditableProjectPaths()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();

        var result = service.UpdateField(
            temp.Paths with { OutputRootPath = null },
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.HPField,
            "99");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static TemporaryPokemonProject CreateEditableProject()
    {
        var temp = TemporaryPokemonProject.Create();
        SwShPokemonWorkflowServiceTests.WriteBasePokemonData(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        return temp;
    }
}
