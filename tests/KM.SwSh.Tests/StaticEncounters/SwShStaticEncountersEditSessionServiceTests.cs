// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using KM.SwSh.StaticEncounters;
using KM.SwSh.Tests.Items;
using KM.SwSh.Tests.Pokemon;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.StaticEncounters;

public sealed class SwShStaticEncountersEditSessionServiceTests
{
    [Fact]
    public void UpdateFieldsPreservesRandomIvSentinelWithoutClamping()
    {
        using var temp = TemporarySwShProject.Create();
        SwShStaticEncountersWorkflowServiceTests.WriteStaticEncounterFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShStaticEncountersEditSessionService();

        var result = service.UpdateFields(
            temp.Paths,
            session: null,
            [
                new(0, SwShStaticEncountersWorkflowService.IvAttackField, "-1"),
                new(0, SwShStaticEncountersWorkflowService.EvHpField, "200"),
                new(0, SwShStaticEncountersWorkflowService.EvAttackField, "200"),
            ]);

        Assert.Equal(3, result.Session.PendingEdits.Count);
        Assert.Contains(result.Session.PendingEdits, edit =>
            edit.Domain == "workflow.staticEncounters"
            && edit.Field == SwShStaticEncountersWorkflowService.IvAttackField
            && edit.RecordId == "static:0:0102030405060708"
            && edit.Summary.Contains("Static 000", StringComparison.Ordinal)
            && edit.NewValue == "-1");
        Assert.Equal(-1, result.Workflow.Encounters[0].Ivs.Attack);
        Assert.Equal(29, result.Workflow.Encounters[0].Ivs.Defense);
        Assert.Equal(200, result.Workflow.Encounters[0].Evs.HP);
        Assert.Equal(200, result.Workflow.Encounters[0].Evs.Attack);
        Assert.Empty(result.Diagnostics);
        Assert.True(service.Validate(temp.Paths, result.Session).IsValid);
    }

    [Fact]
    public void OverlayRecalculatesFixedIvWorkflowStats()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShStaticEncountersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            encounterIndex: 1,
            field: SwShStaticEncountersWorkflowService.IvAttackField,
            value: "0");

        Assert.Equal(2, result.Workflow.Stats.FixedIvEncounterCount);
        Assert.Contains("HP -1", result.Workflow.Encounters[1].IvSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateFieldsRollsBackTheWholeBatchWhenOneValueIsInvalid()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShStaticEncountersEditSessionService();

        var result = service.UpdateFields(
            temp.Paths,
            session: null,
            [
                new(0, SwShStaticEncountersWorkflowService.LevelField, "55"),
                new(0, SwShStaticEncountersWorkflowService.IvAttackField, "80"),
            ]);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Equal(50, result.Workflow.Encounters[0].Level);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UpdateFieldsRejectsAnExpectedEncounterIdFromAnotherIndex()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShStaticEncountersEditSessionService();

        var result = service.UpdateFields(
            temp.Paths,
            session: null,
            [
                new(
                    1,
                    SwShStaticEncountersWorkflowService.LevelField,
                    "55",
                    ExpectedEncounterId: "0x0102030405060708"),
            ]);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("index 1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdateFieldRejectsADuplicateEncounterIdEvenWhenTheIndexMatches()
    {
        using var temp = CreateEditableFixture();
        var sourcePath = GetBaseStaticEncounterPath(temp);
        var parsed = SwShStaticEncounterArchive.Parse(File.ReadAllBytes(sourcePath));
        var records = parsed.Encounters
            .Select(encounter => encounter.Index == 1
                ? encounter with { EncounterId = parsed.Encounters[0].EncounterId }
                : encounter)
            .ToArray();
        File.WriteAllBytes(sourcePath, new SwShStaticEncounterArchive(records).Write());
        var service = new SwShStaticEncountersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            encounterIndex: 0,
            field: SwShStaticEncountersWorkflowService.LevelField,
            value: "51");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("duplicated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateRejectsForgedNonCanonicalIntegerText()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShStaticEncountersEditSessionService();
        var session = EditSession.Start().WithPendingEdit(new PendingEdit(
            "workflow.staticEncounters",
            "Forged level edit",
            [new ProjectFileReference(ProjectFileLayer.Base, SwShStaticEncountersWorkflowService.StaticEncounterDataPath)],
            RecordId: "static:0:0102030405060708",
            Field: SwShStaticEncountersWorkflowService.LevelField,
            NewValue: "+51"));

        var validation = service.Validate(temp.Paths, session);

        Assert.False(validation.IsValid);
        Assert.Contains(
            validation.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("canonical integer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdateFieldsRejectsAnEvAggregateAbove510WithoutClamping()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShStaticEncountersEditSessionService();

        var result = service.UpdateFields(
            temp.Paths,
            session: null,
            [
                new(0, SwShStaticEncountersWorkflowService.EvHpField, "252"),
                new(0, SwShStaticEncountersWorkflowService.EvAttackField, "252"),
                new(0, SwShStaticEncountersWorkflowService.EvDefenseField, "7"),
            ]);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Equal(new SwShStaticEncounterStatsRecord(1, 2, 3, 4, 5, 6), result.Workflow.Encounters[0].Evs);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("total EVs", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateFieldRejectsAThreePerfectSentinelMixedWithIndividualIvs()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShStaticEncountersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            encounterIndex: 0,
            field: SwShStaticEncountersWorkflowService.IvHpField,
            value: "-4");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Equal(31, result.Workflow.Encounters[0].Ivs.HP);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("mixes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdateFieldRejectsLaterMovesWhenMoveOneIsEmpty()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShStaticEncountersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            encounterIndex: 1,
            field: SwShStaticEncountersWorkflowService.Move1Field,
            value: "2");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Move 1 is empty", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateFieldPreflightRejectsAnOmittedFlatBufferDefault()
    {
        using var temp = CreateEditableFixture();
        var sourcePath = GetBaseStaticEncounterPath(temp);
        var source = File.ReadAllBytes(sourcePath);
        OmitEncounterField(source, encounterIndex: 0, fieldIndex: 15);
        File.WriteAllBytes(sourcePath, source);
        var service = new SwShStaticEncountersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            encounterIndex: 0,
            field: SwShStaticEncountersWorkflowService.LevelField,
            value: "51");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("omitted from the source FlatBuffer", StringComparison.Ordinal));
    }

    [Fact]
    public void UnrelatedLevelEditPreservesUntouchedLegacyIvOutlier()
    {
        using var temp = CreateEditableFixture();
        var sourcePath = GetBaseStaticEncounterPath(temp);
        var parsed = SwShStaticEncounterArchive.Parse(File.ReadAllBytes(sourcePath));
        var records = parsed.Encounters
            .Select(encounter => encounter.Index == 0
                ? encounter with { Ivs = encounter.Ivs with { Attack = -2 } }
                : encounter)
            .ToArray();
        File.WriteAllBytes(sourcePath, new SwShStaticEncounterArchive(records).Write());
        var service = new SwShStaticEncountersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            encounterIndex: 0,
            field: SwShStaticEncountersWorkflowService.LevelField,
            value: "51");

        Assert.Single(result.Session.PendingEdits);
        Assert.Equal(-2, result.Workflow.Encounters[0].Ivs.Attack);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SpeciesAndFormBatchUsesPersonalDataAndRefreshesDependentLabels()
    {
        using var temp = CreateEditableFixture();
        WritePersonalFixture(temp, genderlessPikachu: false);
        var service = new SwShStaticEncountersEditSessionService();

        var result = service.UpdateFields(
            temp.Paths,
            session: null,
            [
                new(0, SwShStaticEncountersWorkflowService.SpeciesField, "25"),
                new(0, SwShStaticEncountersWorkflowService.FormField, "0"),
            ]);

        Assert.Equal(2, result.Session.PendingEdits.Count);
        Assert.Equal("Pikachu", result.Workflow.Encounters[0].Species);
        Assert.Contains("099", result.Workflow.Encounters[0].AbilityLabel, StringComparison.Ordinal);
        Assert.Equal(2, result.Workflow.Stats.TotalEncounterCount);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var invalidForm = service.UpdateField(
            temp.Paths,
            session: null,
            encounterIndex: 1,
            field: SwShStaticEncountersWorkflowService.FormField,
            value: "2");
        Assert.Empty(invalidForm.Session.PendingEdits);
        Assert.Contains(invalidForm.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var unavailableAbility = service.UpdateField(
            temp.Paths,
            session: null,
            encounterIndex: 1,
            field: SwShStaticEncountersWorkflowService.AbilityField,
            value: "2");
        Assert.Empty(unavailableAbility.Session.PendingEdits);
        Assert.Contains(
            unavailableAbility.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("ability slot unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdateFieldRejectsUnknownItemAndMoveLookupIds()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShStaticEncountersEditSessionService();

        var invalidItem = service.UpdateField(
            temp.Paths,
            session: null,
            encounterIndex: 0,
            field: SwShStaticEncountersWorkflowService.HeldItemIdField,
            value: "999");
        var invalidMove = service.UpdateField(
            temp.Paths,
            session: null,
            encounterIndex: 0,
            field: SwShStaticEncountersWorkflowService.Move0Field,
            value: "999");

        Assert.Empty(invalidItem.Session.PendingEdits);
        Assert.Contains(invalidItem.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(invalidMove.Session.PendingEdits);
        Assert.Contains(invalidMove.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void GigantamaxFlagRejectsAnIncapableSpecies()
    {
        using var temp = CreateEditableFixture();
        var sourcePath = GetBaseStaticEncounterPath(temp);
        var parsed = SwShStaticEncounterArchive.Parse(File.ReadAllBytes(sourcePath));
        var records = parsed.Encounters
            .Select(encounter => encounter.Index == 1
                ? encounter with { Species = 1, CanGigantamax = false }
                : encounter)
            .ToArray();
        File.WriteAllBytes(sourcePath, new SwShStaticEncounterArchive(records).Write());
        var service = new SwShStaticEncountersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            encounterIndex: 1,
            field: SwShStaticEncountersWorkflowService.CanGigantamaxField,
            value: "1");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("not a Gigantamax-capable", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(25, 1)]
    [InlineData(52, 1)]
    public void GigantamaxFlagRejectsInvalidPikachuAndMeowthForms(int species, int form)
    {
        using var temp = CreateEditableFixture();
        WriteSecondEncounterSpeciesForm(temp, species, form);
        var service = new SwShStaticEncountersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            encounterIndex: 1,
            field: SwShStaticEncountersWorkflowService.CanGigantamaxField,
            value: "1");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains($"form {form}", StringComparison.Ordinal)
                && diagnostic.Message.Contains("not a Gigantamax-capable", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(849, 1)]
    [InlineData(892, 1)]
    [InlineData(809, 0)]
    public void GigantamaxFlagAllowsValidAlternateFormsAndMelmetal(int species, int form)
    {
        using var temp = CreateEditableFixture();
        WriteSecondEncounterSpeciesForm(temp, species, form);
        var service = new SwShStaticEncountersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            encounterIndex: 1,
            field: SwShStaticEncountersWorkflowService.CanGigantamaxField,
            value: "1");

        Assert.Single(result.Session.PendingEdits);
        Assert.True(result.Workflow.Encounters[1].CanGigantamax);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void GigantamaxFlagRejectsAFormMarkedUnableToDynamax()
    {
        using var temp = CreateEditableFixture();
        WritePersonalFixture(temp, genderlessPikachu: false, canNotDynamaxPikachu: true);
        var service = new SwShStaticEncountersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            encounterIndex: 1,
            field: SwShStaticEncountersWorkflowService.CanGigantamaxField,
            value: "1");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("unable to Dynamax", StringComparison.Ordinal));
    }

    [Fact]
    public void GenderValueTwoUsesGenderlessLabelFromPersonalData()
    {
        using var temp = CreateEditableFixture();
        WritePersonalFixture(temp, genderlessPikachu: true);
        var service = new SwShStaticEncountersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            encounterIndex: 1,
            field: SwShStaticEncountersWorkflowService.GenderField,
            value: "2");

        Assert.Equal("Genderless", result.Workflow.Encounters[1].GenderLabel);
        Assert.Contains(result.Workflow.Encounters[1].GenderOptions, option => option.Value == 2 && option.Label == "Genderless");
    }

    [Fact]
    public void ApplyChangePlanWritesLayeredStaticEncounterFixedIvs()
    {
        using var temp = TemporarySwShProject.Create();
        SwShStaticEncountersWorkflowServiceTests.WriteStaticEncounterFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShStaticEncountersEditSessionService();

        var update = service.UpdateField(temp.Paths, null, 0, SwShStaticEncountersWorkflowService.IvHpField, "0");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShStaticEncountersWorkflowService.IvAttackField, "1");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShStaticEncountersWorkflowService.IvDefenseField, "2");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShStaticEncountersWorkflowService.IvSpeedField, "3");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShStaticEncountersWorkflowService.IvSpecialAttackField, "4");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShStaticEncountersWorkflowService.IvSpecialDefenseField, "5");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShStaticEncountersWorkflowService.Move0Field, "2");

        var validation = service.Validate(temp.Paths, update.Session);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.True(validation.IsValid);
        Assert.True(plan.CanApply);
        var plannedWrite = Assert.Single(plan.Writes);
        Assert.Equal(SwShStaticEncountersWorkflowService.StaticEncounterDataPath, plannedWrite.TargetRelativePath);
        Assert.All(update.Session.PendingEdits, edit => Assert.Contains(edit.Summary, plannedWrite.Reason, StringComparison.Ordinal));
        Assert.Equal(SwShStaticEncountersWorkflowService.StaticEncounterDataPath, Assert.Single(apply.WrittenFiles).RelativePath);
        var output = SwShStaticEncounterArchive.Parse(File.ReadAllBytes(GetOutputStaticEncounterPath(temp)));
        Assert.Equal(new SwShStaticEncounterStats(0, 1, 2, 4, 5, 3), output.Encounters[0].Ivs);
        Assert.Equal(2, output.Encounters[0].Moves[0]);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyChangePlanWritesThreePerfectIvSentinel()
    {
        using var temp = TemporarySwShProject.Create();
        SwShStaticEncountersWorkflowServiceTests.WriteStaticEncounterFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShStaticEncountersEditSessionService();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            encounterIndex: 1,
            field: SwShStaticEncountersWorkflowService.FlawlessIvCountField,
            value: "3");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        _ = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        var output = SwShStaticEncounterArchive.Parse(File.ReadAllBytes(GetOutputStaticEncounterPath(temp)));
        Assert.Equal(new SwShStaticEncounterStats(-4, -1, -1, -1, -1, -1), output.Encounters[1].Ivs);
    }

    [Fact]
    public void ApplyWriteFailurePreservesTheExistingLayeredStaticEncounterTable()
    {
        using var temp = CreateEditableFixture();
        var existingLayered = File.ReadAllBytes(GetBaseStaticEncounterPath(temp));
        temp.WriteOutputFile(SwShStaticEncountersWorkflowService.StaticEncounterDataPath, existingLayered);
        var service = new SwShStaticEncountersEditSessionService((tempPath, contents) =>
        {
            File.WriteAllBytes(tempPath, contents[..Math.Min(16, contents.Length)]);
            throw new IOException("Simulated temporary output write failure.");
        });
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            encounterIndex: 0,
            field: SwShStaticEncountersWorkflowService.LevelField,
            value: "51");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.True(plan.CanApply);
        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Simulated temporary output write failure", StringComparison.Ordinal));
        Assert.Equal(existingLayered, File.ReadAllBytes(GetOutputStaticEncounterPath(temp)));
        Assert.Empty(Directory.EnumerateFiles(
            Path.GetDirectoryName(GetOutputStaticEncounterPath(temp))!,
            "*.tmp",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void ApplyRejectsAReviewedPlanWhenSourceBytesChangeInPlace()
    {
        using var temp = CreateEditableFixture();
        var workspace = new ProjectWorkspaceService();
        var service = new SwShStaticEncountersEditSessionService(workspace);
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            encounterIndex: 0,
            field: SwShStaticEncountersWorkflowService.LevelField,
            value: "51");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, update.Session);
        var sourcePath = GetBaseStaticEncounterPath(temp);
        var changedSource = SwShStaticEncounterArchive.Parse(File.ReadAllBytes(sourcePath)).WriteEdits(
        [
            new SwShStaticEncounterEdit(0, SwShStaticEncounterField.Level, 49),
        ]);
        File.WriteAllBytes(sourcePath, changedSource);
        workspace.ClearMemoryCache();

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("source file changed", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(GetOutputStaticEncounterPath(temp)));
    }

    [Fact]
    public void ApplyRejectsAReviewedPlanWhenOnePendingValueChangesAtTheSameCount()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShStaticEncountersEditSessionService();
        var first = service.UpdateField(
            temp.Paths,
            session: null,
            encounterIndex: 0,
            field: SwShStaticEncountersWorkflowService.LevelField,
            value: "51");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, first.Session);
        var changed = service.UpdateField(
            temp.Paths,
            first.Session,
            encounterIndex: 0,
            field: SwShStaticEncountersWorkflowService.LevelField,
            value: "52");

        var apply = service.ApplyChangePlan(temp.Paths, changed.Session, reviewedPlan);

        Assert.Single(first.Session.PendingEdits);
        Assert.Single(changed.Session.PendingEdits);
        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(GetOutputStaticEncounterPath(temp)));
    }

    private static TemporarySwShProject CreateEditableFixture()
    {
        var temp = TemporarySwShProject.Create();
        SwShStaticEncountersWorkflowServiceTests.WriteStaticEncounterFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        return temp;
    }

    private static void WritePersonalFixture(
        TemporarySwShProject temp,
        bool genderlessPikachu,
        bool canNotDynamaxPikachu = false)
    {
        var records = Enumerable.Range(0, 812)
            .Select(_ => SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord())
            .ToArray();
        var pikachu = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hatchedSpecies: 25);
        pikachu[0x12] = genderlessPikachu ? byte.MaxValue : (byte)127;
        if (canNotDynamaxPikachu)
        {
            pikachu[0x5A] |= 0x04;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(pikachu.AsSpan(0x1C), 99);
        records[25] = pikachu;
        records[810] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hatchedSpecies: 810,
            formStatsIndex: 811,
            formCount: 2);
        records[811] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hatchedSpecies: 810,
            formStatsIndex: 811,
            formCount: 2,
            form: 1);
        temp.WriteBaseRomFsFile(
            SwShPokemonWorkflowService.PersonalDataPath["romfs/".Length..],
            SwShPokemonWorkflowServiceTests.CreatePersonalTable(records));
    }

    private static string GetBaseStaticEncounterPath(TemporarySwShProject temp)
    {
        return Path.Combine(
            temp.BaseRomFsPath,
            "bin",
            "script_event_data",
            "event_encount_data.bin");
    }

    private static void WriteSecondEncounterSpeciesForm(
        TemporarySwShProject temp,
        int species,
        int form)
    {
        var sourcePath = GetBaseStaticEncounterPath(temp);
        var parsed = SwShStaticEncounterArchive.Parse(File.ReadAllBytes(sourcePath));
        var records = parsed.Encounters
            .Select(encounter => encounter.Index == 1
                ? encounter with
                {
                    Species = species,
                    Form = form,
                    CanGigantamax = false,
                }
                : encounter)
            .ToArray();
        File.WriteAllBytes(sourcePath, new SwShStaticEncounterArchive(records).Write());
    }

    private static void OmitEncounterField(byte[] data, int encounterIndex, int fieldIndex)
    {
        var rootTableOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data));
        var rootVtableOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(rootTableOffset));
        var rootVtableStart = rootTableOffset - rootVtableOffset;
        var vectorFieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(rootVtableStart + 4));
        var vectorReferenceOffset = rootTableOffset + vectorFieldOffset;
        var vectorOffset = vectorReferenceOffset
            + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(vectorReferenceOffset)));
        var elementOffset = vectorOffset + sizeof(uint) + (encounterIndex * sizeof(uint));
        var encounterTableOffset = elementOffset
            + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(elementOffset)));
        var encounterVtableOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(encounterTableOffset));
        var encounterVtableStart = encounterTableOffset - encounterVtableOffset;
        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(encounterVtableStart + 4 + (fieldIndex * sizeof(ushort))),
            0);
    }

    private static string GetOutputStaticEncounterPath(TemporarySwShProject temp)
    {
        return Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "script_event_data",
            "event_encount_data.bin");
    }
}
