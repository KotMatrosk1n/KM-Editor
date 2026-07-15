// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Items;
using KM.SwSh.Pokemon;
using KM.SwSh.Rentals;
using KM.SwSh.Tests.Items;
using KM.SwSh.Tests.Pokemon;
using Xunit;

namespace KM.SwSh.Tests.Rentals;

public sealed class SwShRentalPokemonEditSessionServiceTests
{
    [Fact]
    public void UpdateFieldsStagesValidRentalPokemonEditsAtomically()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShRentalPokemonEditSessionService();

        var result = service.UpdateFields(
            temp.Paths,
            session: null,
            [
                new SwShRentalPokemonFieldUpdate(
                    0,
                    SwShRentalPokemonWorkflowService.IvAttackField,
                    "30"),
                new SwShRentalPokemonFieldUpdate(
                    0,
                    SwShRentalPokemonWorkflowService.EvHpField,
                    "252"),
            ]);

        Assert.Equal(2, result.Session.PendingEdits.Count);
        Assert.All(result.Session.PendingEdits, edit =>
        {
            Assert.Equal("workflow.rentalPokemon", edit.Domain);
            Assert.StartsWith("rental:0:", edit.RecordId, StringComparison.Ordinal);
        });
        Assert.Contains(result.Session.PendingEdits, edit =>
            edit.Field == SwShRentalPokemonWorkflowService.IvAttackField
            && edit.NewValue == "30");
        Assert.Contains(result.Session.PendingEdits, edit =>
            edit.Field == SwShRentalPokemonWorkflowService.EvHpField
            && edit.NewValue == "252");
        Assert.Equal(30, result.Workflow.Rentals[0].Ivs.Attack);
        Assert.Equal(252, result.Workflow.Rentals[0].Evs.HP);
        Assert.Empty(result.Diagnostics);
        Assert.True(service.Validate(temp.Paths, result.Session).IsValid);
    }

    [Fact]
    public void UpdateFieldsRollsBackTheWholeBatchWhenAnyValueIsInvalid()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShRentalPokemonEditSessionService();
        var original = service.UpdateField(
            temp.Paths,
            session: null,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.LevelField,
            value: "60");

        var result = service.UpdateFields(
            temp.Paths,
            original.Session,
            [
                new SwShRentalPokemonFieldUpdate(
                    0,
                    SwShRentalPokemonWorkflowService.IvAttackField,
                    "30"),
                new SwShRentalPokemonFieldUpdate(
                    0,
                    SwShRentalPokemonWorkflowService.IvDefenseField,
                    "80"),
            ]);

        Assert.Equal(original.Session, result.Session);
        Assert.Equal(60, result.Workflow.Rentals[0].Level);
        Assert.Equal(31, result.Workflow.Rentals[0].Ivs.Attack);
        Assert.Equal(31, result.Workflow.Rentals[0].Ivs.Defense);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Field == SwShRentalPokemonWorkflowService.IvDefenseField);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void UpdateFieldEnforcesRentalPokemonLevelBoundaries(int level, bool isValid)
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShRentalPokemonEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.LevelField,
            value: level.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (isValid)
        {
            var edit = Assert.Single(result.Session.PendingEdits);
            Assert.Equal(level.ToString(System.Globalization.CultureInfo.InvariantCulture), edit.NewValue);
            Assert.Equal(level, result.Workflow.Rentals[0].Level);
            Assert.Empty(result.Diagnostics);
            return;
        }

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Field == SwShRentalPokemonWorkflowService.LevelField
                && diagnostic.Message.Contains("between 1 and 100", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyChangePlanWritesLayeredRentalPokemonFixedIvsAndMoves()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShRentalPokemonEditSessionService();

        var update = service.UpdateField(temp.Paths, null, 0, SwShRentalPokemonWorkflowService.IvHpField, "0");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShRentalPokemonWorkflowService.IvAttackField, "1");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShRentalPokemonWorkflowService.IvDefenseField, "2");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShRentalPokemonWorkflowService.IvSpeedField, "3");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShRentalPokemonWorkflowService.IvSpecialAttackField, "4");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShRentalPokemonWorkflowService.IvSpecialDefenseField, "5");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShRentalPokemonWorkflowService.EvHpField, "252");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShRentalPokemonWorkflowService.Move2Field, "4");

        var validation = service.Validate(temp.Paths, update.Session);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.True(validation.IsValid);
        Assert.True(plan.CanApply);
        Assert.Equal(SwShRentalPokemonWorkflowService.RentalPokemonDataPath, Assert.Single(plan.Writes).TargetRelativePath);
        Assert.Equal(SwShRentalPokemonWorkflowService.RentalPokemonDataPath, Assert.Single(apply.WrittenFiles).RelativePath);
        var output = SwShRentalPokemonArchive.Parse(File.ReadAllBytes(GetOutputRentalPath(temp)));
        Assert.Equal(new SwShRentalPokemonStats(0, 1, 2, 4, 5, 3), output.Rentals[0].Ivs);
        Assert.Equal(252, output.Rentals[0].Evs.HP);
        Assert.Equal(4, output.Rentals[0].Moves[2]);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyChangePlanWritesPerfectIvPreset()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShRentalPokemonEditSessionService();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            rentalIndex: 1,
            field: SwShRentalPokemonWorkflowService.FixedIvPresetField,
            value: "31");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        _ = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        var output = SwShRentalPokemonArchive.Parse(File.ReadAllBytes(GetOutputRentalPath(temp)));
        Assert.Equal(new SwShRentalPokemonStats(31, 31, 31, 31, 31, 31), output.Rentals[1].Ivs);
    }

    [Fact]
    public void ApplyWriteFailurePreservesTheExistingLayeredRentalTable()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var existingLayered = SwShRentalPokemonWorkflowServiceTests.CreateRentalTable(
            new SwShRentalPokemonStats(1, 2, 3, 4, 5, 6));
        temp.WriteOutputFile(SwShRentalPokemonWorkflowService.RentalPokemonDataPath, existingLayered);
        var service = new SwShRentalPokemonEditSessionService((tempPath, contents) =>
        {
            File.WriteAllBytes(tempPath, contents[..Math.Min(16, contents.Length)]);
            throw new IOException("Simulated temporary output write failure.");
        });
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.IvHpField,
            value: "0");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.True(plan.CanApply);
        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Simulated temporary output write failure", StringComparison.Ordinal));
        Assert.Equal(existingLayered, File.ReadAllBytes(GetOutputRentalPath(temp)));
        Assert.Empty(Directory.EnumerateFiles(
            Path.GetDirectoryName(GetOutputRentalPath(temp))!,
            "*.tmp",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void ApplyRejectsAReviewedPlanWhenSourceBytesChangeInPlace()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var workspace = new ProjectWorkspaceService();
        var service = new SwShRentalPokemonEditSessionService(workspace);
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.IvHpField,
            value: "0");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, update.Session);
        var changedSource = SwShRentalPokemonWorkflowServiceTests.CreateRentalTable(
            new SwShRentalPokemonStats(1, 2, 3, 4, 5, 6));
        temp.WriteBaseRomFsFile(
            SwShRentalPokemonWorkflowService.RentalPokemonDataPath["romfs/".Length..],
            changedSource);
        workspace.ClearMemoryCache();

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("source file changed", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(GetOutputRentalPath(temp)));
    }

    [Fact]
    public void ApplyRejectsAReviewedPlanAfterThePendingValueChanges()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShRentalPokemonEditSessionService();
        var first = service.UpdateField(
            temp.Paths,
            session: null,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.IvHpField,
            value: "0");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, first.Session);
        var changed = service.UpdateField(
            temp.Paths,
            first.Session,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.IvHpField,
            value: "1");

        var apply = service.ApplyChangePlan(temp.Paths, changed.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(GetOutputRentalPath(temp)));
    }

    [Fact]
    public void RepeatedSaveUsesTheLayeredSourceAndPreservesTheEarlierEdit()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var workspace = new ProjectWorkspaceService();
        var service = new SwShRentalPokemonEditSessionService(workspace);
        var first = service.UpdateField(
            temp.Paths,
            session: null,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.IvHpField,
            value: "0");
        var firstPlan = service.CreateChangePlan(temp.Paths, first.Session);
        _ = service.ApplyChangePlan(temp.Paths, first.Session, firstPlan);

        var second = service.UpdateField(
            temp.Paths,
            session: null,
            rentalIndex: 0,
            field: SwShRentalPokemonWorkflowService.Move2Field,
            value: "4");
        var secondPlan = service.CreateChangePlan(temp.Paths, second.Session);
        var secondApply = service.ApplyChangePlan(temp.Paths, second.Session, secondPlan);

        Assert.Contains(
            Assert.Single(secondPlan.Writes).Sources,
            source => source.RelativePath == SwShRentalPokemonWorkflowService.RentalPokemonDataPath
                && source.Layer == ProjectFileLayer.Layered);
        Assert.DoesNotContain(secondApply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var output = SwShRentalPokemonArchive.Parse(File.ReadAllBytes(GetOutputRentalPath(temp)));
        Assert.Equal(0, output.Rentals[0].Ivs.HP);
        Assert.Equal(4, output.Rentals[0].Moves[2]);
        var baseOutput = SwShRentalPokemonArchive.Parse(File.ReadAllBytes(Path.Combine(
            temp.BaseRomFsPath,
            "bin",
            "script_event_data",
            "rental.bin")));
        Assert.Equal(31, baseOutput.Rentals[0].Ivs.HP);
        Assert.Equal(3, baseOutput.Rentals[0].Moves[2]);
    }

    [Fact]
    public void SpeciesAndFormEditsMustResolveToAPresentPersonalRecord()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var personalRecords = Enumerable.Range(0, 135)
            .Select(_ => SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord())
            .ToArray();
        personalRecords[25] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hatchedSpecies: 25,
            formCount: 1);
        personalRecords[133] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hatchedSpecies: 133,
            formStatsIndex: 134,
            formCount: 2);
        personalRecords[134] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hatchedSpecies: 133,
            localFormIndex: 1,
            form: 1);
        temp.WriteBaseRomFsFile(
            "bin/pml/personal/personal_total.bin",
            SwShPokemonWorkflowServiceTests.CreatePersonalTable(personalRecords));
        var service = new SwShRentalPokemonEditSessionService();

        var update = service.UpdateFields(
            temp.Paths,
            session: null,
            [
                new SwShRentalPokemonFieldUpdate(
                    0,
                    SwShRentalPokemonWorkflowService.SpeciesField,
                    "25"),
                new SwShRentalPokemonFieldUpdate(
                    0,
                    SwShRentalPokemonWorkflowService.FormField,
                    "0"),
            ]);
        var validation = service.Validate(temp.Paths, update.Session);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.True(validation.IsValid);
        Assert.Equal(25, update.Workflow.Rentals[0].SpeciesId);
        Assert.Equal("Pikachu", update.Workflow.Rentals[0].Species);
        Assert.Contains("Pikachu", update.Workflow.Rentals[0].Label, StringComparison.Ordinal);
        Assert.Contains(update.Workflow.Rentals[0].AbilityOptions, option => option.Value == 0);
        Assert.DoesNotContain(update.Workflow.Rentals[0].AbilityOptions, option => option.Value == 1);
        Assert.Contains(update.Workflow.Rentals[0].AbilityOptions, option => option.Value == 2);
        Assert.Equal(3, update.Workflow.Rentals[0].GenderOptions.Count);
        var linkedWarnings = update.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Message.Contains("hash identifiers", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, linkedWarnings.Length);
        Assert.Contains(linkedWarnings, diagnostic => diagnostic.Field == SwShRentalPokemonWorkflowService.SpeciesField);
        Assert.Contains(linkedWarnings, diagnostic => diagnostic.Field == SwShRentalPokemonWorkflowService.FormField);
        Assert.Contains(
            validation.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Message.Contains("hash identifiers", StringComparison.Ordinal));
        Assert.Contains(
            Assert.Single(plan.Writes).Sources,
            source => source.RelativePath == SwShPokemonWorkflowService.PersonalDataPath
                && source.Layer == ProjectFileLayer.Base);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var output = SwShRentalPokemonArchive.Parse(File.ReadAllBytes(GetOutputRentalPath(temp)));
        Assert.Equal(25, output.Rentals[0].Species);
        Assert.Equal(0, output.Rentals[0].Form);
        Assert.Equal(0x1122334455667788UL, output.Rentals[0].Hash1);
        Assert.Equal(0x8877665544332211UL, output.Rentals[0].Hash2);
    }

    [Fact]
    public void UpdateFieldsRejectsAnInvalidFinalEvTotalWithoutPartialStaging()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShRentalPokemonEditSessionService();

        var result = service.UpdateFields(
            temp.Paths,
            session: null,
            [
                new SwShRentalPokemonFieldUpdate(0, SwShRentalPokemonWorkflowService.EvHpField, "252"),
                new SwShRentalPokemonFieldUpdate(0, SwShRentalPokemonWorkflowService.EvAttackField, "252"),
            ]);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Equal(10, result.Workflow.Rentals[0].Evs.HP);
        Assert.Equal(20, result.Workflow.Rentals[0].Evs.Attack);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("more than 510 total EVs", StringComparison.Ordinal));
    }

    [Fact]
    public void RentalSessionsPreserveForeignDomainsAndRemoveRevertedRentalEdits()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShRentalPokemonEditSessionService();
        var foreignEdit = new PendingEdit(
            "workflow.moves",
            "Keep this edit.",
            [],
            RecordId: "move:1",
            Field: "power",
            NewValue: "50");
        var session = service.StartSession().WithPendingEdit(foreignEdit);

        var changed = service.UpdateField(
            temp.Paths,
            session,
            0,
            SwShRentalPokemonWorkflowService.LevelField,
            "60");
        var reverted = service.UpdateField(
            temp.Paths,
            changed.Session,
            0,
            SwShRentalPokemonWorkflowService.LevelField,
            "50");
        var plan = service.CreateChangePlan(temp.Paths, changed.Session);
        var apply = service.ApplyChangePlan(temp.Paths, changed.Session, plan);

        Assert.Equal(2, changed.Session.PendingEdits.Count);
        Assert.Contains(foreignEdit, changed.Session.PendingEdits);
        Assert.Single(plan.Writes);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(60, SwShRentalPokemonArchive.Parse(
            File.ReadAllBytes(GetOutputRentalPath(temp))).Rentals[0].Level);

        Assert.Equal(foreignEdit, Assert.Single(reverted.Session.PendingEdits));
        Assert.Equal(50, reverted.Workflow.Rentals[0].Level);
        Assert.True(service.Validate(temp.Paths, reverted.Session).IsValid);
    }

    [Fact]
    public void SwitchingPresetToCustomMaterializesUntouchedIvsWhenChangedStatMatchesSource()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShRentalPokemonEditSessionService();
        var foreignEdit = new PendingEdit(
            "workflow.moves",
            "Keep this edit.",
            [],
            RecordId: "move:1",
            Field: "power",
            NewValue: "50");
        var session = service.StartSession().WithPendingEdit(foreignEdit);
        var preset = service.UpdateField(
            temp.Paths,
            session,
            1,
            SwShRentalPokemonWorkflowService.FixedIvPresetField,
            "31");
        var custom = service.UpdateField(
            temp.Paths,
            preset.Session,
            1,
            SwShRentalPokemonWorkflowService.IvAttackField,
            "0");
        var plan = service.CreateChangePlan(temp.Paths, custom.Session);
        var apply = service.ApplyChangePlan(temp.Paths, custom.Session, plan);

        Assert.Equal(
            SwShRentalPokemonWorkflowService.FixedIvPresetField,
            Assert.Single(
                preset.Session.PendingEdits,
                edit => edit.Domain == "workflow.rentalPokemon").Field);
        Assert.Contains(foreignEdit, custom.Session.PendingEdits);
        var materialized = custom.Session.PendingEdits
            .Where(edit => edit.Domain == "workflow.rentalPokemon")
            .ToArray();
        Assert.Equal(5, materialized.Length);
        Assert.All(materialized, edit =>
        {
            Assert.StartsWith("rental:1:", edit.RecordId, StringComparison.Ordinal);
            Assert.Equal("31", edit.NewValue);
            Assert.NotEqual(SwShRentalPokemonWorkflowService.FixedIvPresetField, edit.Field);
            Assert.NotEqual(SwShRentalPokemonWorkflowService.IvAttackField, edit.Field);
        });
        Assert.Equal(
            new SwShRentalPokemonStatsRecord(31, 0, 31, 31, 31, 31),
            custom.Workflow.Rentals[1].Ivs);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(
            new SwShRentalPokemonStats(31, 0, 31, 31, 31, 31),
            SwShRentalPokemonArchive.Parse(File.ReadAllBytes(GetOutputRentalPath(temp))).Rentals[1].Ivs);
    }

    [Fact]
    public void ValidationRejectsAStagedEditWhenTheIndexedSourceRecordChanges()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShRentalPokemonEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShRentalPokemonWorkflowService.LevelField,
            "60");
        temp.WriteBaseRomFsFile(
            SwShRentalPokemonWorkflowService.RentalPokemonDataPath["romfs/".Length..],
            SwShRentalPokemonWorkflowServiceTests.CreateRentalTable(
                new SwShRentalPokemonStats(1, 2, 3, 4, 5, 6)));

        var validation = service.Validate(temp.Paths, update.Session);

        Assert.False(validation.IsValid);
        Assert.Contains(
            validation.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("source record changed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidationContinuesToAcceptLegacyUnsignedRentalRecordIds()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShRentalPokemonEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShRentalPokemonWorkflowService.LevelField,
            "60");
        var edit = Assert.Single(update.Session.PendingEdits) with { RecordId = "rental:0" };
        var legacySession = update.Session with { PendingEdits = [edit] };

        var validation = service.Validate(temp.Paths, legacySession);

        Assert.True(validation.IsValid);
    }

    [Fact]
    public void ApplyRejectsForgedPendingValueWhenTheHumanSummaryIsUnchanged()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShRentalPokemonEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShRentalPokemonWorkflowService.LevelField,
            "60");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, update.Session);
        var originalEdit = Assert.Single(update.Session.PendingEdits);
        var forgedSession = update.Session with
        {
            PendingEdits = [originalEdit with { NewValue = "61" }],
        };

        var apply = service.ApplyChangePlan(temp.Paths, forgedSession, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(GetOutputRentalPath(temp)));
    }

    [Fact]
    public void TrainerIdSupportsTheFullUnsigned32BitRange()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShRentalPokemonEditSessionService();

        var update = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShRentalPokemonWorkflowService.TrainerIdField,
            uint.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.Equal(uint.MaxValue, update.Workflow.Rentals[0].TrainerId);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(
            uint.MaxValue,
            SwShRentalPokemonArchive.Parse(File.ReadAllBytes(GetOutputRentalPath(temp))).Rentals[0].TrainerId);
    }

    [Fact]
    public void AbilityGenderAndBallUpdatesRejectUnavailableSemanticValues()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var personalRecords = Enumerable.Range(0, 135)
            .Select(_ => SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord())
            .ToArray();
        personalRecords[133] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hatchedSpecies: 133,
            formStatsIndex: 134,
            formCount: 2);
        personalRecords[134] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hatchedSpecies: 133,
            localFormIndex: 1,
            form: 1);
        personalRecords[134][0x12] = 0;
        temp.WriteBaseRomFsFile(
            "bin/pml/personal/personal_total.bin",
            SwShPokemonWorkflowServiceTests.CreatePersonalTable(personalRecords));
        var service = new SwShRentalPokemonEditSessionService();

        var ability = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShRentalPokemonWorkflowService.AbilityField,
            "1");
        var gender = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShRentalPokemonWorkflowService.GenderField,
            "2");
        var ball = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShRentalPokemonWorkflowService.BallItemIdField,
            "17");

        Assert.Empty(ability.Session.PendingEdits);
        Assert.DoesNotContain(ability.Workflow.Rentals[0].AbilityOptions, option => option.Value == 1);
        Assert.Contains(
            ability.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Field == SwShRentalPokemonWorkflowService.AbilityField);
        Assert.Empty(gender.Session.PendingEdits);
        Assert.DoesNotContain(gender.Workflow.Rentals[0].GenderOptions, option => option.Value == 2);
        Assert.Contains(
            gender.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Field == SwShRentalPokemonWorkflowService.GenderField);
        Assert.Empty(ball.Session.PendingEdits);
        Assert.Contains(
            ball.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Field == SwShRentalPokemonWorkflowService.BallItemIdField);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonzeroHeldItemsAndMovesRequireValidSemanticData(bool malformedData)
    {
        using var temp = TemporarySwShProject.Create();
        SwShRentalPokemonWorkflowServiceTests.WriteRentalFixture(temp, includeSemanticData: false);
        temp.WriteBaseExeFsFile("main", "base-main");
        if (malformedData)
        {
            temp.WriteBaseRomFsFile(
                SwShItemsWorkflowService.ItemDataPath["romfs/".Length..],
                "malformed-item-data");
            temp.WriteBaseRomFsFile(
                "bin/pml/waza/waza0004.wazabin",
                "malformed-move-data");
        }

        var service = new SwShRentalPokemonEditSessionService();
        var heldItem = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShRentalPokemonWorkflowService.HeldItemIdField,
            "2");
        var move = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShRentalPokemonWorkflowService.Move2Field,
            "4");
        var clears = service.UpdateFields(
            temp.Paths,
            null,
            [
                new SwShRentalPokemonFieldUpdate(0, SwShRentalPokemonWorkflowService.HeldItemIdField, "0"),
                new SwShRentalPokemonFieldUpdate(0, SwShRentalPokemonWorkflowService.Move0Field, "0"),
            ]);
        var ball = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShRentalPokemonWorkflowService.BallItemIdField,
            "851");
        var unrelated = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShRentalPokemonWorkflowService.LevelField,
            "60");

        Assert.Empty(heldItem.Session.PendingEdits);
        Assert.Contains(
            heldItem.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Field == SwShRentalPokemonWorkflowService.HeldItemIdField
                && diagnostic.Message.Contains("item data table is unavailable", StringComparison.Ordinal));
        Assert.Empty(move.Session.PendingEdits);
        Assert.Contains(
            move.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Field == SwShRentalPokemonWorkflowService.Move2Field
                && diagnostic.Message.Contains("move data is unavailable", StringComparison.Ordinal));
        Assert.Equal(2, clears.Session.PendingEdits.Count);
        Assert.DoesNotContain(clears.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Single(ball.Session.PendingEdits);
        Assert.Equal(851, ball.Workflow.Rentals[0].BallItemId);
        Assert.DoesNotContain(ball.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Single(unrelated.Session.PendingEdits);
        Assert.Equal(1, unrelated.Workflow.Rentals[0].HeldItemId);
        Assert.Equal(3, unrelated.Workflow.Rentals[0].Moves[2].MoveId);
    }

    private static string GetOutputRentalPath(TemporarySwShProject temp)
    {
        return Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "script_event_data",
            "rental.bin");
    }

}
