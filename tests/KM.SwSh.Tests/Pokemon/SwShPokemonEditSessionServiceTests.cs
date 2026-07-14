// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
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
                Assert.Equal(1, move.Level);
            },
            move =>
            {
                Assert.Equal(1, move.Slot);
                Assert.Equal(33, move.MoveId);
                Assert.Equal(3, move.Level);
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

    [Theory]
    [InlineData("moveUp", 1)]
    [InlineData("moveDown", 0)]
    public void UpdateLearnsetMoveButtonsKeepLevelsWithSlots(string action, int slot)
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();

        var result = service.UpdateLearnset(
            temp.Paths,
            session: null,
            personalId: 1,
            action,
            slot,
            moveId: null,
            level: null);

        var pokemon = result.Workflow.Pokemon.Single(record => record.PersonalId == 1);
        Assert.Collection(
            pokemon.Learnset,
            move =>
            {
                Assert.Equal(0, move.Slot);
                Assert.Equal(45, move.MoveId);
                Assert.Equal("Growl", move.MoveName);
                Assert.Equal(1, move.Level);
            },
            move =>
            {
                Assert.Equal(1, move.Slot);
                Assert.Equal(33, move.MoveId);
                Assert.Equal("Tackle", move.MoveName);
                Assert.Equal(3, move.Level);
            });
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
            action: "add",
            slot: null,
            method: 8,
            argument: 2,
            species: 2,
            form: 1,
            level: 32);

        var pokemon = result.Workflow.Pokemon.Single(record => record.PersonalId == 1);
        Assert.Equal(2, pokemon.Evolutions.Count);
        var evolution = pokemon.Evolutions.Single(candidate => candidate.Slot == 1);
        Assert.Equal(1, evolution.Slot);
        Assert.Equal(8, evolution.Method);
        Assert.Equal(2, evolution.Argument);
        Assert.Equal("002 TM10 (Magical Leaf)", evolution.ArgumentValue);
        Assert.Equal(2, evolution.Species);
        Assert.Equal(1, evolution.Form);
        Assert.Equal(32, evolution.Level);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.pokemon", edit.Domain);
        Assert.Equal("1", edit.RecordId);
        Assert.Equal("evolution:upsert:1", edit.Field);
        Assert.Equal("8:2:2:1:32", edit.NewValue);
        Assert.Equal("Add Bulbasaur evolution to species 2 at level 32.", edit.Summary);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyChangePlanRollsBackEarlierOutputWhenLaterTargetFails(bool hasExistingPersonalOutput)
    {
        using var temp = CreateEditableProject();
        var personalOutputPath = Path.Combine(
            temp.OutputRootPath,
            SwShPokemonWorkflowService.PersonalDataPath.Replace('/', Path.DirectorySeparatorChar));
        byte[]? originalPersonalOutput = null;
        if (hasExistingPersonalOutput)
        {
            originalPersonalOutput = File.ReadAllBytes(Path.Combine(
                temp.BaseRomFsPath,
                SwShPokemonWorkflowService.PersonalDataPath["romfs/".Length..]
                    .Replace('/', Path.DirectorySeparatorChar)));
            originalPersonalOutput[0x60] ^= 0x5A;
            temp.WriteOutputFile(SwShPokemonWorkflowService.PersonalDataPath, originalPersonalOutput);
        }

        var service = new SwShPokemonEditSessionService();
        var personalUpdate = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.HPField,
            "99");
        var learnsetUpdate = service.UpdateLearnset(
            temp.Paths,
            personalUpdate.Session,
            personalId: 1,
            action: "upsert",
            slot: 1,
            moveId: 345,
            level: 7);
        var learnsetOutputPath = Path.Combine(
            temp.OutputRootPath,
            SwShPokemonWorkflowService.LearnsetDataPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(learnsetOutputPath);
        var plan = service.CreateChangePlan(temp.Paths, learnsetUpdate.Session);

        var apply = service.ApplyChangePlan(temp.Paths, learnsetUpdate.Session, plan);

        Assert.True(plan.CanApply);
        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.File == SwShPokemonWorkflowService.LearnsetDataPath);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Info
                && diagnostic.Message.Contains("rolled back", StringComparison.OrdinalIgnoreCase));
        if (originalPersonalOutput is null)
        {
            Assert.False(File.Exists(personalOutputPath));
        }
        else
        {
            Assert.Equal(originalPersonalOutput, File.ReadAllBytes(personalOutputPath));
        }

        Assert.True(Directory.Exists(learnsetOutputPath));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(temp.OutputRootPath, "*", SearchOption.AllDirectories),
            path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplyChangePlanCanRemoveAndRestoreAllEvYields()
    {
        using var temp = CreateEditableProject();
        temp.WriteOutputFile(
            SwShPokemonWorkflowService.PersonalDataPath,
            SwShPokemonWorkflowServiceTests.CreatePersonalTable(
                SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord(),
                CreateBulbasaurPersonalRecordWithEvYield(hp: 1, attack: 2, defense: 3)));
        var service = new SwShPokemonEditSessionService();

        var remove = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 0,
            field: "evYieldAll",
            value: "remove");

        var removedPokemon = remove.Workflow.Pokemon.Single(record => record.PersonalId == 1);
        Assert.Equal(0, removedPokemon.Personal.EVYieldHP);
        Assert.Equal(0, removedPokemon.Personal.EVYieldAttack);
        Assert.Equal(0, removedPokemon.Personal.EVYieldDefense);
        var removeEdit = Assert.Single(remove.Session.PendingEdits);
        Assert.Equal("all", removeEdit.RecordId);
        Assert.Equal("evYieldAll", removeEdit.Field);

        var restore = service.UpdateField(
            temp.Paths,
            remove.Session,
            personalId: 0,
            field: "evYieldAll",
            value: "restore");
        var plan = service.CreateChangePlan(temp.Paths, restore.Session);
        var apply = service.ApplyChangePlan(temp.Paths, restore.Session, plan);

        Assert.True(plan.CanApply);
        Assert.Contains(apply.WrittenFiles, file => file.RelativePath == SwShPokemonWorkflowService.PersonalDataPath);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var outputBytes = File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            SwShPokemonWorkflowService.PersonalDataPath.Replace('/', Path.DirectorySeparatorChar)));
        var outputRecord = SwShPersonalTable.Parse(outputBytes).Records[1];
        Assert.Equal(0, outputRecord.EVYieldHP);
        Assert.Equal(0, outputRecord.EVYieldAttack);
        Assert.Equal(0, outputRecord.EVYieldDefense);
    }

    [Fact]
    public void ApplyChangePlanCanRemoveAndRestoreAllExpYields()
    {
        using var temp = CreateEditableProject();
        temp.WriteOutputFile(
            SwShPokemonWorkflowService.PersonalDataPath,
            SwShPokemonWorkflowServiceTests.CreatePersonalTable(
                SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord(),
                CreateBulbasaurPersonalRecordWithBaseExperience(123)));
        var service = new SwShPokemonEditSessionService();

        var remove = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 0,
            field: "expYieldAll",
            value: "remove");

        var removedPokemon = remove.Workflow.Pokemon.Single(record => record.PersonalId == 1);
        Assert.Equal(0, removedPokemon.BaseExperience);
        Assert.Equal(0, removedPokemon.Personal.BaseExperience);
        var removeEdit = Assert.Single(remove.Session.PendingEdits);
        Assert.Equal("all", removeEdit.RecordId);
        Assert.Equal("expYieldAll", removeEdit.Field);

        var restore = service.UpdateField(
            temp.Paths,
            remove.Session,
            personalId: 0,
            field: "expYieldAll",
            value: "restore");
        var plan = service.CreateChangePlan(temp.Paths, restore.Session);
        var apply = service.ApplyChangePlan(temp.Paths, restore.Session, plan);

        Assert.True(plan.CanApply);
        Assert.Contains(apply.WrittenFiles, file => file.RelativePath == SwShPokemonWorkflowService.PersonalDataPath);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var outputBytes = File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            SwShPokemonWorkflowService.PersonalDataPath.Replace('/', Path.DirectorySeparatorChar)));
        var outputRecord = SwShPersonalTable.Parse(outputBytes).Records[1];
        Assert.Equal(64, outputRecord.BaseExperience);
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
    public void ConsecutiveLearnsetRemovalsFromTheSameVisibleSlotAccumulate()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();

        var first = service.UpdateLearnset(
            temp.Paths,
            session: null,
            personalId: 1,
            action: "remove",
            slot: 0,
            moveId: null,
            level: null);
        var second = service.UpdateLearnset(
            temp.Paths,
            first.Session,
            personalId: 1,
            action: "remove",
            slot: 0,
            moveId: null,
            level: null);

        Assert.Equal(2, second.Session.PendingEdits.Count);
        Assert.Empty(second.Workflow.Pokemon.Single(record => record.PersonalId == 1).Learnset);

        var plan = service.CreateChangePlan(temp.Paths, second.Session);
        var apply = service.ApplyChangePlan(temp.Paths, second.Session, plan);

        Assert.True(plan.CanApply);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var outputBytes = File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            SwShPokemonWorkflowService.LearnsetDataPath.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Empty(SwShPokemonLearnsetTable.Parse(outputBytes).Records[1].Moves);
    }

    [Fact]
    public void EvolutionEditsPreserveSparsePhysicalSlots()
    {
        using var temp = CreateEditableProject();
        var targetRelativePath = SwShPokemonWorkflowService.CreateEvolutionDataPath(1);
        temp.WriteBaseRomFsFile(
            targetRelativePath["romfs/".Length..],
            SwShEvolutionSet.Write(
            [
                new SwShEvolutionRecord(2, 4, 0, 2, 0, 16),
                new SwShEvolutionRecord(7, 7, 25, 3, 1, 32),
            ]));
        var service = new SwShPokemonEditSessionService();

        var update = service.UpdateEvolution(
            temp.Paths,
            session: null,
            personalId: 1,
            action: "upsert",
            slot: 7,
            method: 8,
            argument: 2,
            species: 4,
            form: 0,
            level: 40);
        var remove = service.UpdateEvolution(
            temp.Paths,
            update.Session,
            personalId: 1,
            action: "remove",
            slot: 2,
            method: null,
            argument: null,
            species: null,
            form: null,
            level: null);

        Assert.Empty(remove.Diagnostics);
        var pendingEvolution = Assert.Single(
            remove.Workflow.Pokemon.Single(record => record.PersonalId == 1).Evolutions);
        Assert.Equal(7, pendingEvolution.Slot);
        Assert.Equal(4, pendingEvolution.Species);

        var plan = service.CreateChangePlan(temp.Paths, remove.Session);
        var apply = service.ApplyChangePlan(temp.Paths, remove.Session, plan);

        Assert.True(plan.CanApply);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var outputBytes = File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            targetRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var outputEvolution = Assert.Single(SwShEvolutionSet.Parse(outputBytes).Evolutions);
        Assert.Equal(7, outputEvolution.Slot);
        Assert.Equal(8, outputEvolution.Method);
        Assert.Equal(2, outputEvolution.Argument);
        Assert.Equal(4, outputEvolution.Species);
        Assert.Equal(40, outputEvolution.Level);
    }

    [Fact]
    public void ConsecutiveEvolutionRemovalsOfTheNewlyVisibleRowAccumulate()
    {
        using var temp = CreateEditableProject();
        var targetRelativePath = SwShPokemonWorkflowService.CreateEvolutionDataPath(1);
        temp.WriteBaseRomFsFile(
            targetRelativePath["romfs/".Length..],
            SwShEvolutionSet.Write(
            [
                new SwShEvolutionRecord(0, 4, 0, 2, 0, 16),
                new SwShEvolutionRecord(1, 4, 0, 3, 0, 32),
            ]));
        var service = new SwShPokemonEditSessionService();

        var first = service.UpdateEvolution(
            temp.Paths,
            session: null,
            personalId: 1,
            action: "remove",
            slot: 0,
            method: null,
            argument: null,
            species: null,
            form: null,
            level: null);
        var newlyVisibleSlot = Assert.Single(
            first.Workflow.Pokemon.Single(record => record.PersonalId == 1).Evolutions).Slot;
        var second = service.UpdateEvolution(
            temp.Paths,
            first.Session,
            personalId: 1,
            action: "remove",
            slot: newlyVisibleSlot,
            method: null,
            argument: null,
            species: null,
            form: null,
            level: null);

        Assert.Equal(2, second.Session.PendingEdits.Count);
        Assert.Empty(second.Workflow.Pokemon.Single(record => record.PersonalId == 1).Evolutions);

        var plan = service.CreateChangePlan(temp.Paths, second.Session);
        var apply = service.ApplyChangePlan(temp.Paths, second.Session, plan);

        Assert.True(plan.CanApply);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var outputBytes = File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            targetRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Empty(SwShEvolutionSet.Parse(outputBytes).Evolutions);
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

    private static byte[] CreateBulbasaurPersonalRecordWithEvYield(
        int hp = 0,
        int attack = 0,
        int defense = 0,
        int speed = 0,
        int specialAttack = 0,
        int specialDefense = 0)
    {
        var record = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord();
        var evYield =
            (hp & 0x3)
            | ((attack & 0x3) << 2)
            | ((defense & 0x3) << 4)
            | ((speed & 0x3) << 6)
            | ((specialAttack & 0x3) << 8)
            | ((specialDefense & 0x3) << 10);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x0A), checked((ushort)evYield));
        return record;
    }

    private static byte[] CreateBulbasaurPersonalRecordWithBaseExperience(int baseExperience)
    {
        var record = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord();
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x22), checked((ushort)baseExperience));
        return record;
    }
}
