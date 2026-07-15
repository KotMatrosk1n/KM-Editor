// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Formats.SwSh;
using KM.SwSh.Gifts;
using KM.SwSh.Pokemon;
using KM.SwSh.Tests.Items;
using KM.SwSh.Tests.Pokemon;
using Xunit;

namespace KM.SwSh.Tests.Gifts;

public sealed class SwShGiftPokemonEditSessionServiceTests
{
    [Fact]
    public void UpdateFieldRejectsOutOfRangeIvsInsteadOfClamping()
    {
        using var temp = TemporarySwShProject.Create();
        SwShGiftPokemonWorkflowServiceTests.WriteGiftFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShGiftPokemonEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            giftIndex: 0,
            field: SwShGiftPokemonWorkflowService.IvAttackField,
            value: "80");
        var second = service.UpdateField(
            temp.Paths,
            result.Session,
            giftIndex: 0,
            field: SwShGiftPokemonWorkflowService.IvDefenseField,
            value: "-50");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Empty(second.Session.PendingEdits);
        Assert.Equal(30, result.Workflow.Gifts[0].Ivs.Attack);
        Assert.Equal(29, second.Workflow.Gifts[0].Ivs.Defense);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(second.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyChangePlanWritesLayeredGiftPokemonFixedIvs()
    {
        using var temp = TemporarySwShProject.Create();
        SwShGiftPokemonWorkflowServiceTests.WriteGiftFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShGiftPokemonEditSessionService();

        var update = service.UpdateField(temp.Paths, null, 0, SwShGiftPokemonWorkflowService.IvHpField, "0");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShGiftPokemonWorkflowService.IvAttackField, "1");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShGiftPokemonWorkflowService.IvDefenseField, "2");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShGiftPokemonWorkflowService.IvSpeedField, "3");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShGiftPokemonWorkflowService.IvSpecialAttackField, "4");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShGiftPokemonWorkflowService.IvSpecialDefenseField, "5");

        var validation = service.Validate(temp.Paths, update.Session);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.True(validation.IsValid);
        Assert.True(plan.CanApply);
        Assert.Equal(SwShGiftPokemonWorkflowService.GiftPokemonDataPath, Assert.Single(plan.Writes).TargetRelativePath);
        Assert.Equal(SwShGiftPokemonWorkflowService.GiftPokemonDataPath, Assert.Single(apply.WrittenFiles).RelativePath);
        var output = SwShGiftPokemonArchive.Parse(File.ReadAllBytes(GetOutputGiftPath(temp)));
        Assert.Equal(new SwShGiftPokemonIvs(0, 1, 2, 3, 4, 5), output.Gifts[0].Ivs);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyChangePlanWritesThreePerfectIvSentinel()
    {
        using var temp = TemporarySwShProject.Create();
        SwShGiftPokemonWorkflowServiceTests.WriteGiftFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShGiftPokemonEditSessionService();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            giftIndex: 1,
            field: SwShGiftPokemonWorkflowService.FlawlessIvCountField,
            value: "3");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        _ = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        var output = SwShGiftPokemonArchive.Parse(File.ReadAllBytes(GetOutputGiftPath(temp)));
        Assert.Equal(new SwShGiftPokemonIvs(-4, -1, -1, -1, -1, -1), output.Gifts[1].Ivs);
    }

    [Fact]
    public void UpdateFieldsIsTransactionalWhenALaterValueIsInvalid()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShGiftPokemonEditSessionService();

        var result = service.UpdateFields(
            temp.Paths,
            session: null,
            [
                new(0, SwShGiftPokemonWorkflowService.LevelField, "51"),
                new(0, SwShGiftPokemonWorkflowService.BallItemIdField, "100"),
            ]);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Equal(50, result.Workflow.Gifts[0].Level);
        Assert.Equal(4, result.Workflow.Gifts[0].BallItemId);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UpdateFieldSupportsRandomAndThreePerfectIvSentinelsWithoutClamping()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShGiftPokemonEditSessionService();

        var random = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShGiftPokemonWorkflowService.IvAttackField,
            "-1");
        var sentinel = service.UpdateField(
            temp.Paths,
            random.Session,
            1,
            SwShGiftPokemonWorkflowService.IvHpField,
            "-4");

        Assert.Equal(-1, sentinel.Workflow.Gifts[0].Ivs.Attack);
        Assert.Equal(-4, sentinel.Workflow.Gifts[1].Ivs.HP);
        Assert.Equal(2, sentinel.Session.PendingEdits.Count);
        Assert.All(sentinel.Session.PendingEdits, edit =>
        {
            Assert.StartsWith("gift:", edit.RecordId, StringComparison.Ordinal);
            Assert.Equal(2, edit.RecordId!.Count(character => character == ':'));
        });
        Assert.DoesNotContain(sentinel.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UpdateFieldRejectsThreePerfectSentinelOnMixedIvLayoutTransactionally()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShGiftPokemonEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShGiftPokemonWorkflowService.IvHpField,
            "-4");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Equal(new SwShGiftPokemonIvsRecord(31, 30, 29, 27, 26, 28), result.Workflow.Gifts[0].Ivs);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("3-perfect", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdatingAFieldBackToItsSourceValueRemovesThePendingEdit()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShGiftPokemonEditSessionService();

        var changed = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShGiftPokemonWorkflowService.LevelField,
            "51");
        var reverted = service.UpdateField(
            temp.Paths,
            changed.Session,
            0,
            SwShGiftPokemonWorkflowService.LevelField,
            "50");

        Assert.Single(changed.Session.PendingEdits);
        Assert.Empty(reverted.Session.PendingEdits);
        Assert.Equal(50, reverted.Workflow.Gifts[0].Level);
    }

    [Fact]
    public void IvPresetAndIndividualStatUpdatesNormalizeEachOther()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShGiftPokemonEditSessionService();

        var preset = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShGiftPokemonWorkflowService.FlawlessIvCountField,
            "6");
        var stat = service.UpdateField(
            temp.Paths,
            preset.Session,
            1,
            SwShGiftPokemonWorkflowService.IvAttackField,
            "30");
        var presetAgain = service.UpdateField(
            temp.Paths,
            stat.Session,
            1,
            SwShGiftPokemonWorkflowService.FlawlessIvCountField,
            "3");

        var statEdit = Assert.Single(stat.Session.PendingEdits);
        Assert.Equal(SwShGiftPokemonWorkflowService.IvAttackField, statEdit.Field);
        var finalEdit = Assert.Single(presetAgain.Session.PendingEdits);
        Assert.Equal(SwShGiftPokemonWorkflowService.FlawlessIvCountField, finalEdit.Field);
        Assert.Equal(new SwShGiftPokemonIvsRecord(-4, -1, -1, -1, -1, -1), presetAgain.Workflow.Gifts[1].Ivs);
    }

    [Fact]
    public void PendingIvPresetRecalculatesWorkflowStatsAndRevertRestoresThem()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShGiftPokemonEditSessionService();

        var preset = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShGiftPokemonWorkflowService.FlawlessIvCountField,
            "3");
        var reverted = service.UpdateField(
            temp.Paths,
            preset.Session,
            1,
            SwShGiftPokemonWorkflowService.FlawlessIvCountField,
            "0");

        Assert.Equal(2, preset.Workflow.Stats.FixedIvGiftCount);
        Assert.Equal(3, preset.Workflow.Gifts[1].FlawlessIvCount);
        Assert.Single(preset.Session.PendingEdits);
        Assert.Equal(1, reverted.Workflow.Stats.FixedIvGiftCount);
        Assert.Equal(0, reverted.Workflow.Gifts[1].FlawlessIvCount);
        Assert.Empty(reverted.Session.PendingEdits);
    }

    [Fact]
    public void GiftWorkflowPreservesAndIgnoresForeignDomainEdits()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShGiftPokemonEditSessionService();
        var foreign = new PendingEdit(
            "workflow.other",
            "Unrelated edit",
            [],
            RecordId: "other:0",
            Field: "value",
            NewValue: "1");
        var session = EditSession.Start().WithPendingEdit(foreign);

        var result = service.UpdateField(
            temp.Paths,
            session,
            0,
            SwShGiftPokemonWorkflowService.LevelField,
            "51");
        var validation = service.Validate(temp.Paths, result.Session);
        var plan = service.CreateChangePlan(temp.Paths, result.Session);

        Assert.Equal(2, result.Session.PendingEdits.Count);
        Assert.Contains(foreign, result.Session.PendingEdits);
        Assert.True(validation.IsValid);
        Assert.True(plan.CanApply);
        Assert.Contains("Apply pending Gift Pokemon edit", Assert.Single(plan.Writes).Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("Unrelated edit", Assert.Single(plan.Writes).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyRejectsAPlanAfterThePendingGiftValueChanges()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShGiftPokemonEditSessionService();
        var first = service.UpdateField(temp.Paths, null, 0, SwShGiftPokemonWorkflowService.LevelField, "51");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, first.Session);
        var changed = service.UpdateField(temp.Paths, first.Session, 0, SwShGiftPokemonWorkflowService.LevelField, "52");

        var apply = service.ApplyChangePlan(temp.Paths, changed.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(GetOutputGiftPath(temp)));
    }

    [Fact]
    public void ApplyRejectsAPlanAfterTheGiftSourceChanges()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShGiftPokemonEditSessionService();
        var update = service.UpdateField(temp.Paths, null, 0, SwShGiftPokemonWorkflowService.LevelField, "51");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, update.Session);
        var sourcePath = GetBaseGiftPath(temp);
        var source = SwShGiftPokemonArchive.Parse(File.ReadAllBytes(sourcePath));
        File.WriteAllBytes(
            sourcePath,
            new SwShGiftPokemonArchive(source.Gifts.Select(gift =>
                gift.Index == 1 ? gift with { Level = 2 } : gift).ToArray()).Write());

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(GetOutputGiftPath(temp)));
    }

    [Fact]
    public void ApplyRejectsAPlanAfterTheGiftSourceLayerChanges()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShGiftPokemonEditSessionService();
        var update = service.UpdateField(temp.Paths, null, 0, SwShGiftPokemonWorkflowService.LevelField, "51");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, update.Session);
        var unexpectedOutput = SwShGiftPokemonWorkflowServiceTests.CreateGiftTable(
            new SwShGiftPokemonIvs(31, 31, 31, 31, 31, 31));
        temp.WriteOutputFile(SwShGiftPokemonWorkflowService.GiftPokemonDataPath, unexpectedOutput);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Equal(unexpectedOutput, File.ReadAllBytes(GetOutputGiftPath(temp)));
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void FailedAtomicWritePreservesExistingOutputAndReportsNoWrittenFile()
    {
        using var temp = CreateEditableFixture();
        var originalOutput = SwShGiftPokemonWorkflowServiceTests.CreateGiftTable(
            new SwShGiftPokemonIvs(31, 31, 31, 31, 31, 31));
        temp.WriteOutputFile(SwShGiftPokemonWorkflowService.GiftPokemonDataPath, originalOutput);
        var service = new SwShGiftPokemonEditSessionService(
            (_, _) => throw new IOException("Injected write failure."));
        var update = service.UpdateField(temp.Paths, null, 0, SwShGiftPokemonWorkflowService.LevelField, "51");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Equal(originalOutput, File.ReadAllBytes(GetOutputGiftPath(temp)));
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyRefreshesWorkspaceCacheSoLaterLoadsUseLayeredOutput()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShGiftPokemonEditSessionService();
        var update = service.UpdateField(temp.Paths, null, 0, SwShGiftPokemonWorkflowService.IvAttackField, "-1");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        var next = service.UpdateField(temp.Paths, null, 0, SwShGiftPokemonWorkflowService.LevelField, "51");

        Assert.Single(apply.WrittenFiles);
        Assert.Equal(-1, next.Workflow.Gifts[0].Ivs.Attack);
        Assert.Equal(ProjectFileLayer.Layered, next.Workflow.Gifts[0].Provenance.SourceLayer);
    }

    [Fact]
    public void BallAndGenderMappingsRejectUnsupportedNewValues()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShGiftPokemonEditSessionService();

        var invalidBall = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShGiftPokemonWorkflowService.BallItemIdField,
            "100");
        var validRareBall = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShGiftPokemonWorkflowService.BallItemIdField,
            "851");
        var invalidGender = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShGiftPokemonWorkflowService.GenderField,
            "3");

        Assert.Empty(invalidBall.Session.PendingEdits);
        Assert.Single(validRareBall.Session.PendingEdits);
        Assert.Equal(851, validRareBall.Workflow.Gifts[1].BallItemId);
        Assert.Empty(invalidGender.Session.PendingEdits);
        Assert.Contains(invalidGender.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ItemAndMoveEditsFailClosedWithoutLookupsButCanStillBeCleared()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            SwShGiftPokemonWorkflowService.GiftPokemonDataPath["romfs/".Length..],
            SwShGiftPokemonWorkflowServiceTests.CreateGiftTable(
                new SwShGiftPokemonIvs(31, 30, 29, 28, 27, 26)));
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShGiftPokemonEditSessionService();

        var invalidItem = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShGiftPokemonWorkflowService.HeldItemIdField,
            "2");
        var invalidMove = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShGiftPokemonWorkflowService.SpecialMoveIdField,
            "1");
        var cleared = service.UpdateFields(
            temp.Paths,
            null,
            [
                new(0, SwShGiftPokemonWorkflowService.HeldItemIdField, "0"),
                new(0, SwShGiftPokemonWorkflowService.SpecialMoveIdField, "0"),
            ]);

        Assert.Empty(invalidItem.Session.PendingEdits);
        Assert.Empty(invalidMove.Session.PendingEdits);
        Assert.All(new[] { invalidItem, invalidMove }, result =>
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("lookup data is unavailable", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2, cleared.Session.PendingEdits.Count);
        Assert.Equal(0, cleared.Workflow.Gifts[0].HeldItemId);
        Assert.Equal(0, cleared.Workflow.Gifts[0].SpecialMoveId);
        Assert.DoesNotContain(cleared.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ItemAndMoveEditsUseLoadedWorkflowLookupOptions()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShGiftPokemonEditSessionService();

        var result = service.UpdateFields(
            temp.Paths,
            null,
            [
                new(1, SwShGiftPokemonWorkflowService.HeldItemIdField, "1"),
                new(1, SwShGiftPokemonWorkflowService.SpecialMoveIdField, "2"),
            ]);

        Assert.Equal(2, result.Session.PendingEdits.Count);
        Assert.Equal(1, result.Workflow.Gifts[1].HeldItemId);
        Assert.Equal(2, result.Workflow.Gifts[1].SpecialMoveId);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData(" 31")]
    [InlineData("+31")]
    [InlineData("031")]
    public void UpdateFieldRejectsNonCanonicalIntegerText(string value)
    {
        using var temp = CreateEditableFixture();
        var service = new SwShGiftPokemonEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShGiftPokemonWorkflowService.IvAttackField,
            value);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("canonical", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdateFieldReportsMalformedExistingIvPresetWithoutThrowing()
    {
        using var temp = CreateEditableFixture();
        var invalidEdit = new PendingEdit(
            "workflow.giftPokemon",
            "Invalid IV preset.",
            [new ProjectFileReference(ProjectFileLayer.Base, SwShGiftPokemonWorkflowService.GiftPokemonDataPath)],
            RecordId: "gift:1",
            Field: SwShGiftPokemonWorkflowService.FlawlessIvCountField,
            NewValue: "5");
        var session = EditSession.Start().WithPendingEdit(invalidEdit);
        var service = new SwShGiftPokemonEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session,
            1,
            SwShGiftPokemonWorkflowService.LevelField,
            "21");

        Assert.Equal(session, result.Session);
        Assert.Single(result.Session.PendingEdits);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("IV preset", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SemanticValidationUsesFinalSpeciesFormAbilityGenderAndGigantamaxState()
    {
        using var temp = CreateEditableFixture();
        WritePersonalFixture(temp, eeveeGenderRatio: 0);
        var service = new SwShGiftPokemonEditSessionService();

        var invalidAbility = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShGiftPokemonWorkflowService.AbilityField,
            "2");
        var invalidGender = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShGiftPokemonWorkflowService.GenderField,
            "2");
        var invalidForm = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShGiftPokemonWorkflowService.FormField,
            "1");
        var invalidGigantamax = service.UpdateFields(
            temp.Paths,
            null,
            [
                new(1, SwShGiftPokemonWorkflowService.SpeciesField, "810"),
                new(1, SwShGiftPokemonWorkflowService.CanGigantamaxField, "1"),
            ]);

        Assert.All(
            new[] { invalidAbility, invalidGender, invalidForm, invalidGigantamax },
            result =>
            {
                Assert.Empty(result.Session.PendingEdits);
                Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            });
    }

    [Fact]
    public void GenderlessValueTwoGetsContextualLabelAndPersonalSourceGuard()
    {
        using var temp = CreateEditableFixture();
        WritePersonalFixture(temp, eeveeGenderRatio: byte.MaxValue);
        var service = new SwShGiftPokemonEditSessionService();

        var update = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShGiftPokemonWorkflowService.GenderField,
            "2");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        Assert.Single(update.Session.PendingEdits);
        Assert.Equal("Genderless", update.Workflow.Gifts[1].GenderLabel);
        Assert.Contains(
            Assert.Single(plan.Writes).Sources,
            source => source.RelativePath == SwShPokemonWorkflowService.PersonalDataPath);
    }

    [Fact]
    public void LegacyRandomizerStyleRecordIdsRemainValidAndGainCurrentSemanticSources()
    {
        using var temp = CreateEditableFixture();
        WritePersonalFixture(temp, eeveeGenderRatio: 127);
        var edit = new PendingEdit(
            "workflow.giftPokemon",
            "Randomize gift species.",
            [new ProjectFileReference(ProjectFileLayer.Base, SwShGiftPokemonWorkflowService.GiftPokemonDataPath)],
            RecordId: "gift:1",
            Field: SwShGiftPokemonWorkflowService.SpeciesField,
            NewValue: "810");
        var session = EditSession.Start().WithPendingEdit(edit);
        var service = new SwShGiftPokemonEditSessionService();

        var validation = service.Validate(temp.Paths, session);
        var plan = service.CreateChangePlan(temp.Paths, session);

        Assert.True(validation.IsValid);
        Assert.True(plan.CanApply);
        Assert.Contains(
            Assert.Single(plan.Writes).Sources,
            source => source.RelativePath == SwShPokemonWorkflowService.PersonalDataPath);
    }

    [Fact]
    public void SignedRecordIdentityRejectsAnEditAfterInvariantSourceFieldsChange()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShGiftPokemonEditSessionService();
        var update = service.UpdateField(temp.Paths, null, 0, SwShGiftPokemonWorkflowService.LevelField, "51");
        var sourcePath = GetBaseGiftPath(temp);
        var archive = SwShGiftPokemonArchive.Parse(File.ReadAllBytes(sourcePath));
        File.WriteAllBytes(
            sourcePath,
            new SwShGiftPokemonArchive(archive.Gifts.Select(gift =>
                gift.Index == 0 ? gift with { Hash1 = gift.Hash1 + 1 } : gift).ToArray()).Write());

        var validation = service.Validate(temp.Paths, update.Session);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SignedRecordIdentityRejectsReorderedEditableDataWithMatchingInvariantMetadata()
    {
        using var temp = CreateEditableFixture();
        var sourcePath = GetBaseGiftPath(temp);
        var archive = SwShGiftPokemonArchive.Parse(File.ReadAllBytes(sourcePath));
        var firstGift = archive.Gifts[0];
        var replacementGift = archive.Gifts[1] with
        {
            IsEgg = firstGift.IsEgg,
            Field04 = firstGift.Field04,
            Hash1 = firstGift.Hash1,
            Field0A = firstGift.Field0A,
            MemoryCode = firstGift.MemoryCode,
            MemoryData = firstGift.MemoryData,
            MemoryFeel = firstGift.MemoryFeel,
            MemoryLevel = firstGift.MemoryLevel,
            OtNameId = firstGift.OtNameId,
            OtGender = firstGift.OtGender,
        };
        File.WriteAllBytes(
            sourcePath,
            new SwShGiftPokemonArchive([firstGift, replacementGift]).Write());
        var service = new SwShGiftPokemonEditSessionService();
        var update = service.UpdateField(temp.Paths, null, 0, SwShGiftPokemonWorkflowService.LevelField, "51");
        File.WriteAllBytes(
            sourcePath,
            new SwShGiftPokemonArchive([replacementGift, firstGift]).Write());

        var validation = service.Validate(temp.Paths, update.Session);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("identity", StringComparison.OrdinalIgnoreCase));
    }

    private static TemporarySwShProject CreateEditableFixture()
    {
        var temp = TemporarySwShProject.Create();
        SwShGiftPokemonWorkflowServiceTests.WriteGiftFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        return temp;
    }

    private static void WritePersonalFixture(TemporarySwShProject temp, byte eeveeGenderRatio)
    {
        var records = Enumerable.Range(0, 812)
            .Select(_ => SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord())
            .ToArray();
        var eevee = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hatchedSpecies: 133);
        eevee[0x12] = eeveeGenderRatio;
        records[133] = eevee;
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

    private static string GetBaseGiftPath(TemporarySwShProject temp)
    {
        return Path.Combine(
            temp.BaseRomFsPath,
            "bin",
            "script_event_data",
            "add_poke.bin");
    }

    private static string GetOutputGiftPath(TemporarySwShProject temp)
    {
        return Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "script_event_data",
            "add_poke.bin");
    }
}
