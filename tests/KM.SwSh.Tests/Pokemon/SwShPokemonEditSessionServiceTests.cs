// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using KM.SwSh.Tests.Items;
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
            argument: 1,
            species: 1,
            form: 1,
            level: 32);

        var pokemon = result.Workflow.Pokemon.Single(record => record.PersonalId == 1);
        Assert.Equal(2, pokemon.Evolutions.Count);
        var evolution = pokemon.Evolutions.Single(candidate => candidate.Slot == 1);
        Assert.Equal(1, evolution.Slot);
        Assert.Equal(8, evolution.Method);
        Assert.Equal(1, evolution.Argument);
        Assert.Equal("001 Leaf Stone", evolution.ArgumentValue);
        Assert.Equal(1, evolution.Species);
        Assert.Equal(1, evolution.Form);
        Assert.Equal(32, evolution.Level);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.pokemon", edit.Domain);
        Assert.Equal("1", edit.RecordId);
        Assert.Equal("evolution:upsert:1", edit.Field);
        Assert.Equal("8:1:1:1:32", edit.NewValue);
        Assert.Equal("Add Bulbasaur evolution to species 1 at level 32.", edit.Summary);
        Assert.Contains(edit.Sources, source => source.RelativePath == SwShPokemonWorkflowService.CreateEvolutionDataPath(1));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void UpdateFieldsRejectsTheWholeBatchWhenAnyFieldIsInvalid()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();

        var result = service.UpdateFields(
            temp.Paths,
            session: null,
            [
                new SwShPokemonFieldUpdate(1, SwShPokemonWorkflowService.HPField, "99"),
                new SwShPokemonFieldUpdate(1, SwShPokemonWorkflowService.Type1Field, "18"),
            ]);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Equal(45, result.Workflow.Pokemon.Single(record => record.PersonalId == 1).BaseStats.HP);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void OrderedLearnsetEditsPreserveUpsertMoveAndUpsertHistory()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();
        var first = service.UpdateLearnset(
            temp.Paths,
            session: null,
            personalId: 1,
            action: "upsert",
            slot: 1,
            moveId: 345,
            level: 7);
        var moved = service.UpdateLearnset(
            temp.Paths,
            first.Session,
            personalId: 1,
            action: "moveUp",
            slot: 1,
            moveId: null,
            level: null);

        var final = service.UpdateLearnset(
            temp.Paths,
            moved.Session,
            personalId: 1,
            action: "upsert",
            slot: 1,
            moveId: 45,
            level: 9);

        Assert.Equal(3, final.Session.PendingEdits.Count);
        Assert.Collection(
            final.Workflow.Pokemon.Single(record => record.PersonalId == 1).Learnset,
            move =>
            {
                Assert.Equal(345, move.MoveId);
                Assert.Equal(1, move.Level);
            },
            move =>
            {
                Assert.Equal(45, move.MoveId);
                Assert.Equal(9, move.Level);
            });

        var plan = service.CreateChangePlan(temp.Paths, final.Session);
        var apply = service.ApplyChangePlan(temp.Paths, final.Session, plan);

        Assert.True(plan.CanApply);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var outputBytes = File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            SwShPokemonWorkflowService.LearnsetDataPath.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Collection(
            SwShPokemonLearnsetTable.Parse(outputBytes).Records[1].Moves,
            move =>
            {
                Assert.Equal(345, move.MoveId);
                Assert.Equal(1, move.Level);
            },
            move =>
            {
                Assert.Equal(45, move.MoveId);
                Assert.Equal(9, move.Level);
            });
    }

    [Fact]
    public void ApplyChangePlanRejectsReviewedPlanAfterPendingEditChanges()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();
        var reviewed = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.HPField,
            "90");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, reviewed.Session);
        var changed = service.UpdateField(
            temp.Paths,
            reviewed.Session,
            personalId: 1,
            SwShPokemonWorkflowService.HPField,
            "91");

        var apply = service.ApplyChangePlan(temp.Paths, changed.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShPokemonWorkflowService.PersonalDataPath.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void ApplyChangePlanRejectsReviewedPlanAfterSourceContentChanges()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.HPField,
            "90");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, update.Session);
        var sourcePath = Path.Combine(
            temp.BaseRomFsPath,
            "bin",
            "pml",
            "personal",
            "personal_total.bin");
        var sourceBytes = File.ReadAllBytes(sourcePath);
        sourceBytes[SwShPersonalTable.RecordSize + 1] ^= 1;
        File.WriteAllBytes(sourcePath, sourceBytes);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(Path.Combine(
            temp.OutputRootPath,
            SwShPokemonWorkflowService.PersonalDataPath.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void ConsecutiveDirectAppliesPreserveEarlierLayeredPersonalEdits()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();
        var hpUpdate = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.HPField,
            "99");
        var hpPlan = service.CreateChangePlan(temp.Paths, hpUpdate.Session);

        var hpApply = service.ApplyChangePlan(temp.Paths, hpUpdate.Session, hpPlan);
        var attackUpdate = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.AttackField,
            "88");
        var attackPlan = service.CreateChangePlan(temp.Paths, attackUpdate.Session);
        var attackApply = service.ApplyChangePlan(temp.Paths, attackUpdate.Session, attackPlan);

        Assert.DoesNotContain(hpApply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(99, attackUpdate.Workflow.Pokemon.Single(record => record.PersonalId == 1).BaseStats.HP);
        Assert.DoesNotContain(attackApply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var output = SwShPersonalTable.Parse(File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            SwShPokemonWorkflowService.PersonalDataPath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal(99, output.Records[1].HP);
        Assert.Equal(88, output.Records[1].Attack);
    }

    [Fact]
    public void PlanRefreshesLayeredPersonalSourceThatAppearsAfterStaging()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.HPField,
            "99");
        var layeredRecord = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hp: 45);
        layeredRecord[1] = 88;
        temp.WriteOutputFile(
            SwShPokemonWorkflowService.PersonalDataPath,
            SwShPokemonWorkflowServiceTests.CreatePersonalTable(
                SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord(),
                layeredRecord));
        var layeredPath = Path.Combine(
            temp.OutputRootPath,
            SwShPokemonWorkflowService.PersonalDataPath.Replace('/', Path.DirectorySeparatorChar));
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var output = SwShPersonalTable.Parse(File.ReadAllBytes(layeredPath));
        Assert.Equal(99, output.Records[1].HP);
        Assert.Equal(88, output.Records[1].Attack);
    }

    [Fact]
    public void ApplyRejectsMachineMetadataThatAppearsAfterPlanReview()
    {
        using var temp = CreateEditableProject();
        File.Delete(Path.Combine(temp.BaseRomFsPath, "bin", "pml", "item", "item.dat"));
        var service = new SwShPokemonEditSessionService();
        var field = SwShPokemonWorkflowService.CreateCompatibilityFieldId(
            SwShPokemonWorkflowService.TechnicalMachineCompatibilityGroupId,
            10);
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            field,
            "false");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, update.Session);
        temp.WriteBaseRomFsFile(
            "bin/pml/item/item.dat",
            SwShItemTestFixtures.CreateItemTableWithMachineMoves(
                new Dictionary<int, int> { [10] = 45 },
                new ItemFixtureRecord(0, 0, 0, 0, 0, SwShItemPouch.Items),
                new ItemFixtureRecord(
                    1,
                    1,
                    3000,
                    0,
                    0,
                    SwShItemPouch.Items,
                    CanUseOnPokemon: true,
                    Boost0: 0x08),
                new ItemFixtureRecord(
                    2,
                    2,
                    1000,
                    0,
                    0,
                    SwShItemPouch.TMs,
                    FieldUseType: 2,
                    GroupType: 4,
                    GroupIndex: 10)));

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, reviewedPlan);

        Assert.True(reviewedPlan.CanApply);
        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RestorePlanRetainsBaseDependencyWhenLayeredPersonalAppearsBeforeReview()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 0,
            field: "evYieldAll",
            value: "restore");
        temp.WriteOutputFile(
            SwShPokemonWorkflowService.PersonalDataPath,
            SwShPokemonWorkflowServiceTests.CreatePersonalTable(
                SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord(),
                CreateBulbasaurPersonalRecordWithEvYield(hp: 3)));

        var reviewedPlan = service.CreateChangePlan(temp.Paths, update.Session);

        var write = Assert.Single(reviewedPlan.Writes);
        Assert.Contains(
            write.Sources,
            source => source.RelativePath == SwShPokemonWorkflowService.PersonalDataPath
                && source.Layer == ProjectFileLayer.Base);
        Assert.Contains(
            write.Sources,
            source => source.RelativePath == SwShPokemonWorkflowService.PersonalDataPath
                && source.Layer == ProjectFileLayer.Layered);
        var basePath = Path.Combine(
            temp.BaseRomFsPath,
            "bin",
            "pml",
            "personal",
            "personal_total.bin");
        var baseBytes = File.ReadAllBytes(basePath);
        baseBytes[SwShPersonalTable.RecordSize + 0x0A] ^= 1;
        File.WriteAllBytes(basePath, baseBytes);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, reviewedPlan);

        Assert.True(reviewedPlan.CanApply);
        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void LearnsetIdentityMismatchDisablesOnlyLearnsetEditing()
    {
        using var temp = CreateEditableProject();
        temp.WriteBaseRomFsFile(
            "bin/pml/waza_oboe/wazaoboe_total.bin",
            SwShPokemonWorkflowServiceTests.CreateLearnsetTable([]));
        var service = new SwShPokemonEditSessionService();

        var learnset = service.UpdateLearnset(
            temp.Paths,
            session: null,
            personalId: 1,
            action: "upsert",
            slot: 0,
            moveId: 45,
            level: 5);
        var personal = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.HPField,
            "99");

        Assert.Empty(learnset.Session.PendingEdits);
        Assert.Contains(learnset.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("99", Assert.Single(personal.Session.PendingEdits).NewValue);
        Assert.DoesNotContain(personal.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void RowUpdatesRejectInvalidSemanticSentinelsAndLevels()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();

        var zeroMove = service.UpdateLearnset(
            temp.Paths,
            session: null,
            personalId: 1,
            action: "upsert",
            slot: 0,
            moveId: 0,
            level: 5);
        var highLevel = service.UpdateLearnset(
            temp.Paths,
            session: null,
            personalId: 1,
            action: "upsert",
            slot: 0,
            moveId: 33,
            level: 101);
        var zeroMethod = service.UpdateEvolution(
            temp.Paths,
            session: null,
            personalId: 1,
            action: "upsert",
            slot: 0,
            method: 0,
            argument: 0,
            species: 2,
            form: 0,
            level: 16);
        var zeroSpecies = service.UpdateEvolution(
            temp.Paths,
            session: null,
            personalId: 1,
            action: "upsert",
            slot: 0,
            method: 4,
            argument: 0,
            species: 0,
            form: 0,
            level: 16);

        foreach (var result in new[] { zeroMove, highLevel, zeroMethod, zeroSpecies })
        {
            Assert.Empty(result.Session.PendingEdits);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
    }

    [Fact]
    public void EvolutionArgumentEditRejectsUnsupportedLegacyMethodWhileUntouchedRowRemainsAllowed()
    {
        using var temp = CreateEditableProject();
        var targetRelativePath = SwShPokemonWorkflowService.CreateEvolutionDataPath(1);
        temp.WriteBaseRomFsFile(
            targetRelativePath["romfs/".Length..],
            SwShEvolutionSet.Write(
            [
                new SwShEvolutionRecord(0, 50, 7, 2, 0, 16),
            ]));
        var service = new SwShPokemonEditSessionService();

        var unchanged = service.UpdateEvolution(
            temp.Paths,
            session: null,
            personalId: 1,
            action: "upsert",
            slot: 0,
            method: 50,
            argument: 7,
            species: 2,
            form: 0,
            level: 16);
        var changedArgument = service.UpdateEvolution(
            temp.Paths,
            session: null,
            personalId: 1,
            action: "upsert",
            slot: 0,
            method: 50,
            argument: 8,
            species: 2,
            form: 0,
            level: 16);

        Assert.Single(unchanged.Session.PendingEdits);
        Assert.DoesNotContain(unchanged.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(changedArgument.Session.PendingEdits);
        Assert.Contains(
            changedArgument.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Field == "argument");
    }

    [Fact]
    public void PendingOverlaysRefreshLabelsAndDerivedCounts()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();
        var fields = service.UpdateFields(
            temp.Paths,
            session: null,
            [
                new SwShPokemonFieldUpdate(1, SwShPokemonWorkflowService.Ability1Field, "34"),
                new SwShPokemonFieldUpdate(1, SwShPokemonWorkflowService.GenderRatioField, "255"),
            ]);
        var learnset = service.UpdateLearnset(
            temp.Paths,
            fields.Session,
            personalId: 1,
            action: "add",
            slot: null,
            moveId: 345,
            level: 7);
        var evolution = service.UpdateEvolution(
            temp.Paths,
            learnset.Session,
            personalId: 1,
            action: "add",
            slot: null,
            method: 4,
            argument: 0,
            species: 1,
            form: 0,
            level: 32);

        var pokemon = evolution.Workflow.Pokemon.Single(record => record.PersonalId == 1);
        Assert.Equal("034 Chlorophyll", pokemon.Abilities.Ability1Label);
        Assert.Equal("255 Genderless", pokemon.GenderRatioLabel);
        Assert.Equal(3, evolution.Workflow.Stats.TotalLearnsetMoveCount);
        Assert.Equal(2, evolution.Workflow.Stats.TotalEvolutionCount);
        Assert.DoesNotContain(evolution.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void PendingFormOwnershipEditsRefreshDisplayAndSpriteIdentity()
    {
        using var temp = CreateEditableProject();
        var records = Enumerable.Range(0, 54)
            .Select(_ => SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord())
            .ToArray();
        records[52] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hp: 40,
            hatchedSpecies: 52);
        records[53] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hp: 50,
            hatchedSpecies: 52,
            form: 1,
            isRegionalForm: true);
        temp.WriteBaseRomFsFile(
            "bin/pml/personal/personal_total.bin",
            SwShPokemonWorkflowServiceTests.CreatePersonalTable(records));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            SwShPokemonWorkflowServiceTests.CreateNamedPokemonNames(54, (52, "Meowth")));
        var service = new SwShPokemonEditSessionService();

        var result = service.UpdateFields(
            temp.Paths,
            session: null,
            [
                new SwShPokemonFieldUpdate(52, SwShPokemonWorkflowService.FormStatsIndexField, "53"),
                new SwShPokemonFieldUpdate(52, SwShPokemonWorkflowService.FormCountField, "2"),
            ]);

        var form = result.Workflow.Pokemon.Single(record => record.PersonalId == 53);
        Assert.Equal(52, form.SpeciesId);
        Assert.Equal("Meowth (Alolan)", form.Name);
        Assert.Equal("Alolan", form.FormLabel);
        Assert.Equal("Meowth (Alolan)", form.SpriteName);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void PokemonPlansPreserveAndIgnoreUnrelatedPendingDomains()
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();
        var unrelated = EditSession.Start().WithPendingEdit(new PendingEdit(
            "workflow.items",
            "Unrelated item edit.",
            [],
            RecordId: "1",
            Field: "buyPrice",
            NewValue: "650"));

        var result = service.UpdateField(
            temp.Paths,
            unrelated,
            personalId: 1,
            SwShPokemonWorkflowService.HPField,
            "99");
        var plan = service.CreateChangePlan(temp.Paths, result.Session);

        Assert.Equal(2, result.Session.PendingEdits.Count);
        Assert.Contains(result.Session.PendingEdits, edit => edit.Domain == "workflow.items");
        Assert.True(plan.CanApply);
        Assert.Single(plan.Writes);
        Assert.StartsWith("Apply pending Pokemon Data edit:", Assert.Single(plan.Writes).Reason);
    }

    [Fact]
    public void RestoreRejectsMismatchedCurrentAndBasePersonalIdentityBeforeStaging()
    {
        using var temp = CreateEditableProject();
        temp.WriteOutputFile(
            SwShPokemonWorkflowService.PersonalDataPath,
            SwShPokemonWorkflowServiceTests.CreatePersonalTable(
                SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord()));
        var service = new SwShPokemonEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 0,
            field: "evYieldAll",
            value: "restore");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
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
            species: 1,
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
    public void RestoreYieldPreviewUsesBasePersonalValues()
    {
        using var temp = CreateEditableProject();
        var layeredRecord = CreateBulbasaurPersonalRecordWithEvYield(
            hp: 1,
            attack: 2,
            defense: 3,
            speed: 1,
            specialAttack: 2,
            specialDefense: 3);
        BinaryPrimitives.WriteUInt16LittleEndian(layeredRecord.AsSpan(0x22), 123);
        temp.WriteOutputFile(
            SwShPokemonWorkflowService.PersonalDataPath,
            SwShPokemonWorkflowServiceTests.CreatePersonalTable(
                SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord(),
                layeredRecord));
        var service = new SwShPokemonEditSessionService();

        var evRestore = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 0,
            field: "evYieldAll",
            value: "restore");
        var expRestore = service.UpdateField(
            temp.Paths,
            evRestore.Session,
            personalId: 0,
            field: "expYieldAll",
            value: "restore");

        var restoredPokemon = expRestore.Workflow.Pokemon.Single(record => record.PersonalId == 1);
        Assert.Equal(0, restoredPokemon.Personal.EVYieldHP);
        Assert.Equal(0, restoredPokemon.Personal.EVYieldAttack);
        Assert.Equal(0, restoredPokemon.Personal.EVYieldDefense);
        Assert.Equal(0, restoredPokemon.Personal.EVYieldSpeed);
        Assert.Equal(0, restoredPokemon.Personal.EVYieldSpecialAttack);
        Assert.Equal(0, restoredPokemon.Personal.EVYieldSpecialDefense);
        Assert.Equal(64, restoredPokemon.BaseExperience);
        Assert.Equal(64, restoredPokemon.Personal.BaseExperience);
        Assert.Equal(2, expRestore.Session.PendingEdits.Count);
        Assert.DoesNotContain(expRestore.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void RestoreThenIndividualYieldEditsUseBasePreviewAndApplyInPendingOrder()
    {
        using var temp = CreateEditableProject();
        var layeredRecord = CreateBulbasaurPersonalRecordWithEvYield(hp: 3, attack: 3);
        BinaryPrimitives.WriteUInt16LittleEndian(layeredRecord.AsSpan(0x22), 123);
        temp.WriteOutputFile(
            SwShPokemonWorkflowService.PersonalDataPath,
            SwShPokemonWorkflowServiceTests.CreatePersonalTable(
                SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord(),
                layeredRecord));
        var service = new SwShPokemonEditSessionService();

        var evRestore = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 0,
            field: "evYieldAll",
            value: "restore");
        var evEdit = service.UpdateField(
            temp.Paths,
            evRestore.Session,
            personalId: 1,
            field: SwShPokemonWorkflowService.EVYieldHPField,
            value: "2");
        var expRestore = service.UpdateField(
            temp.Paths,
            evEdit.Session,
            personalId: 0,
            field: "expYieldAll",
            value: "restore");
        var expEdit = service.UpdateField(
            temp.Paths,
            expRestore.Session,
            personalId: 1,
            field: SwShPokemonWorkflowService.BaseExperienceField,
            value: "77");

        Assert.Collection(
            expEdit.Session.PendingEdits,
            edit => Assert.Equal("evYieldAll", edit.Field),
            edit => Assert.Equal(SwShPokemonWorkflowService.EVYieldHPField, edit.Field),
            edit => Assert.Equal("expYieldAll", edit.Field),
            edit => Assert.Equal(SwShPokemonWorkflowService.BaseExperienceField, edit.Field));
        var previewPokemon = expEdit.Workflow.Pokemon.Single(record => record.PersonalId == 1);
        Assert.Equal(2, previewPokemon.Personal.EVYieldHP);
        Assert.Equal(0, previewPokemon.Personal.EVYieldAttack);
        Assert.Equal(77, previewPokemon.BaseExperience);
        Assert.Equal(77, previewPokemon.Personal.BaseExperience);

        var plan = service.CreateChangePlan(temp.Paths, expEdit.Session);
        var apply = service.ApplyChangePlan(temp.Paths, expEdit.Session, plan);

        Assert.True(plan.CanApply);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var output = SwShPersonalTable.Parse(File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            SwShPokemonWorkflowService.PersonalDataPath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal(2, output.Records[1].EVYieldHP);
        Assert.Equal(0, output.Records[1].EVYieldAttack);
        Assert.Equal(77, output.Records[1].BaseExperience);
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
            argument: 1,
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
            species: 1,
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
                Assert.Equal(1, evolution.Argument);
                Assert.Equal(2, evolution.Species);
                Assert.Equal(1, evolution.Form);
                Assert.Equal(32, evolution.Level);
            },
            evolution =>
            {
                Assert.Equal(1, evolution.Slot);
                Assert.Equal(4, evolution.Method);
                Assert.Equal(1, evolution.Species);
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
            argument: 1,
            species: 3,
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
        Assert.Equal(3, pendingEvolution.Species);

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
        Assert.Equal(1, outputEvolution.Argument);
        Assert.Equal(3, outputEvolution.Species);
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

    [Theory]
    [InlineData(SwShPokemonWorkflowService.FormField, 255)]
    [InlineData(SwShPokemonWorkflowService.LocalFormIndexField, 255)]
    public void UpdateFieldAcceptsMaximumSemanticFormValue(string field, int value)
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            field,
            value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(value.ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.Single(result.Session.PendingEdits).NewValue);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData(SwShPokemonWorkflowService.FormField)]
    [InlineData(SwShPokemonWorkflowService.LocalFormIndexField)]
    public void UpdateFieldRejectsChangedFormValueAboveByteRange(string field)
    {
        using var temp = CreateEditableProject();
        var service = new SwShPokemonEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            field,
            "256");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UnrelatedPersonalEditPreservesLegacyHighRawFormValues()
    {
        using var temp = CreateEditableProject();
        temp.WriteBaseRomFsFile(
            SwShPokemonWorkflowService.PersonalDataPath["romfs/".Length..],
            SwShPokemonWorkflowServiceTests.CreatePersonalTable(
                SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord(),
                SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
                    localFormIndex: 300,
                    form: 400)));
        var service = new SwShPokemonEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            personalId: 1,
            SwShPokemonWorkflowService.HPField,
            "99");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var output = SwShPersonalTable.Parse(File.ReadAllBytes(Path.Combine(
            temp.OutputRootPath,
            SwShPokemonWorkflowService.PersonalDataPath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal(300, output.Records[1].LocalFormIndex);
        Assert.Equal(400, output.Records[1].Form);
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
