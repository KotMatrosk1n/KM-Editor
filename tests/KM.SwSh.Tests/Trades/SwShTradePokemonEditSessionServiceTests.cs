// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using KM.SwSh.Trades;
using KM.SwSh.Tests.Items;
using KM.SwSh.Tests.Pokemon;
using Xunit;

namespace KM.SwSh.Tests.Trades;

public sealed class SwShTradePokemonEditSessionServiceTests
{
    [Fact]
    public void UpdateFieldRejectsOutOfRangeIvsInsteadOfClamping()
    {
        using var temp = TemporarySwShProject.Create();
        SwShTradePokemonWorkflowServiceTests.WriteTradeFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShTradePokemonEditSessionService();

        var high = service.UpdateField(
            temp.Paths,
            session: null,
            tradeIndex: 0,
            field: SwShTradePokemonWorkflowService.IvAttackField,
            value: "80");
        var low = service.UpdateField(
            temp.Paths,
            session: null,
            tradeIndex: 0,
            field: SwShTradePokemonWorkflowService.IvDefenseField,
            value: "-50");

        Assert.Empty(high.Session.PendingEdits);
        Assert.Empty(low.Session.PendingEdits);
        Assert.Equal(30, high.Workflow.Trades[0].Ivs.Attack);
        Assert.Equal(29, low.Workflow.Trades[0].Ivs.Defense);
        Assert.All(new[] { high, low }, result =>
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void UpdateFieldsStagesSignedIvsTransactionallyAndSupportsReverts()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShTradePokemonEditSessionService();

        var signed = service.UpdateFields(
            temp.Paths,
            null,
            [
                new(0, SwShTradePokemonWorkflowService.IvAttackField, "-1"),
                new(1, SwShTradePokemonWorkflowService.IvHpField, "-4"),
            ]);
        var invalid = service.UpdateFields(
            temp.Paths,
            signed.Session,
            [
                new(0, SwShTradePokemonWorkflowService.LevelField, "51"),
                new(0, SwShTradePokemonWorkflowService.BallItemIdField, "100"),
            ]);
        var reverted = service.UpdateField(
            temp.Paths,
            signed.Session,
            0,
            SwShTradePokemonWorkflowService.IvAttackField,
            "30");

        Assert.Equal(2, signed.Session.PendingEdits.Count);
        Assert.Equal(-1, signed.Workflow.Trades[0].Ivs.Attack);
        Assert.Equal(-4, signed.Workflow.Trades[1].Ivs.HP);
        Assert.All(signed.Session.PendingEdits, edit =>
        {
            Assert.StartsWith("trade:", edit.RecordId, StringComparison.Ordinal);
            Assert.Equal(2, edit.RecordId!.Count(character => character == ':'));
        });
        Assert.Equal(signed.Session, invalid.Session);
        Assert.Equal(50, invalid.Workflow.Trades[0].Level);
        Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Single(reverted.Session.PendingEdits);
        Assert.Equal(30, reverted.Workflow.Trades[0].Ivs.Attack);
    }

    [Fact]
    public void IvPresetAndIndividualUpdatesNormalizeAndRefreshStats()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShTradePokemonEditSessionService();

        var preset = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShTradePokemonWorkflowService.FlawlessIvCountField,
            "6");
        var individual = service.UpdateField(
            temp.Paths,
            preset.Session,
            1,
            SwShTradePokemonWorkflowService.IvAttackField,
            "30");

        Assert.Equal(2, preset.Workflow.Stats.FixedIvTradeCount);
        Assert.Equal(6, preset.Workflow.Trades[1].FlawlessIvCount);
        var edit = Assert.Single(individual.Session.PendingEdits);
        Assert.Equal(SwShTradePokemonWorkflowService.IvAttackField, edit.Field);
        Assert.Null(individual.Workflow.Trades[1].FlawlessIvCount);
        Assert.Equal(2, individual.Workflow.Stats.FixedIvTradeCount);
    }

    [Fact]
    public void TradeWorkflowPreservesAndIgnoresForeignDomainEdits()
    {
        using var temp = CreateEditableFixture();
        var foreign = new PendingEdit(
            "workflow.other",
            "Unrelated edit",
            [],
            RecordId: "other:0",
            Field: "value",
            NewValue: "1");
        var session = EditSession.Start().WithPendingEdit(foreign);
        var service = new SwShTradePokemonEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session,
            0,
            SwShTradePokemonWorkflowService.LevelField,
            "51");
        var validation = service.Validate(temp.Paths, result.Session);
        var plan = service.CreateChangePlan(temp.Paths, result.Session);

        Assert.Equal(2, result.Session.PendingEdits.Count);
        Assert.Contains(foreign, result.Session.PendingEdits);
        Assert.True(validation.IsValid);
        Assert.True(plan.CanApply);
        Assert.DoesNotContain("Unrelated edit", Assert.Single(plan.Writes).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyChangePlanWritesLayeredTradePokemonFixedIvs()
    {
        using var temp = TemporarySwShProject.Create();
        SwShTradePokemonWorkflowServiceTests.WriteTradeFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShTradePokemonEditSessionService();

        var update = service.UpdateField(temp.Paths, null, 0, SwShTradePokemonWorkflowService.IvHpField, "0");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShTradePokemonWorkflowService.IvAttackField, "1");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShTradePokemonWorkflowService.IvDefenseField, "2");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShTradePokemonWorkflowService.IvSpeedField, "3");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShTradePokemonWorkflowService.IvSpecialAttackField, "4");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShTradePokemonWorkflowService.IvSpecialDefenseField, "5");
        update = service.UpdateField(temp.Paths, update.Session, 0, SwShTradePokemonWorkflowService.RelearnMove2Field, "4");

        var validation = service.Validate(temp.Paths, update.Session);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.True(validation.IsValid);
        Assert.True(plan.CanApply);
        Assert.Equal(SwShTradePokemonWorkflowService.TradePokemonDataPath, Assert.Single(plan.Writes).TargetRelativePath);
        Assert.Equal(SwShTradePokemonWorkflowService.TradePokemonDataPath, Assert.Single(apply.WrittenFiles).RelativePath);
        var output = SwShTradePokemonArchive.Parse(File.ReadAllBytes(GetOutputTradePath(temp)));
        Assert.Equal(new SwShTradePokemonIvs(0, 1, 2, 3, 4, 5), output.Trades[0].Ivs);
        Assert.Equal(4, output.Trades[0].RelearnMoves[2]);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void EveryEditableWorkflowFieldOverlaysAndAppliesToTheExpectedArchiveMember()
    {
        using var temp = CreateEditableFixture();
        WritePersonalFixture(temp, eeveeGenderRatio: 127);
        var service = new SwShTradePokemonEditSessionService();
        SwShTradePokemonFieldUpdate[] updates =
        [
            new(1, SwShTradePokemonWorkflowService.SpeciesField, "6"),
            new(1, SwShTradePokemonWorkflowService.FormField, "1"),
            new(1, SwShTradePokemonWorkflowService.LevelField, "2"),
            new(1, SwShTradePokemonWorkflowService.HeldItemIdField, "1"),
            new(1, SwShTradePokemonWorkflowService.BallItemIdField, "1"),
            new(1, SwShTradePokemonWorkflowService.Field03Field, "5"),
            new(1, SwShTradePokemonWorkflowService.AbilityField, "1"),
            new(1, SwShTradePokemonWorkflowService.NatureField, "3"),
            new(1, SwShTradePokemonWorkflowService.GenderField, "1"),
            new(1, SwShTradePokemonWorkflowService.ShinyLockField, "1"),
            new(1, SwShTradePokemonWorkflowService.DynamaxLevelField, "1"),
            new(1, SwShTradePokemonWorkflowService.CanGigantamaxField, "1"),
            new(1, SwShTradePokemonWorkflowService.RequiredSpeciesField, "6"),
            new(1, SwShTradePokemonWorkflowService.RequiredFormField, "1"),
            new(1, SwShTradePokemonWorkflowService.RequiredNatureField, "3"),
            new(1, SwShTradePokemonWorkflowService.UnknownRequirementField, "0"),
            new(1, SwShTradePokemonWorkflowService.TrainerIdField, "1"),
            new(1, SwShTradePokemonWorkflowService.OtGenderField, "1"),
            new(1, SwShTradePokemonWorkflowService.MemoryCodeField, "1"),
            new(1, SwShTradePokemonWorkflowService.MemoryTextVariableField, "1"),
            new(1, SwShTradePokemonWorkflowService.MemoryFeelField, "1"),
            new(1, SwShTradePokemonWorkflowService.MemoryIntensityField, "1"),
            new(1, SwShTradePokemonWorkflowService.RelearnMove0Field, "1"),
            new(1, SwShTradePokemonWorkflowService.RelearnMove1Field, "2"),
            new(1, SwShTradePokemonWorkflowService.RelearnMove2Field, "3"),
            new(1, SwShTradePokemonWorkflowService.RelearnMove3Field, "4"),
            new(1, SwShTradePokemonWorkflowService.IvHpField, "1"),
            new(1, SwShTradePokemonWorkflowService.IvAttackField, "2"),
            new(1, SwShTradePokemonWorkflowService.IvDefenseField, "3"),
            new(1, SwShTradePokemonWorkflowService.IvSpeedField, "4"),
            new(1, SwShTradePokemonWorkflowService.IvSpecialAttackField, "5"),
            new(1, SwShTradePokemonWorkflowService.IvSpecialDefenseField, "6"),
        ];

        var update = service.UpdateFields(temp.Paths, null, updates);
        var workflowFields = update.Workflow.EditableFields.Select(field => field.Field).Order().ToArray();
        var coveredFields = updates.Select(update => update.Field)
            .Append(SwShTradePokemonWorkflowService.FlawlessIvCountField)
            .Order()
            .ToArray();
        var trade = update.Workflow.Trades[1];
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.Equal(workflowFields, coveredFields);
        Assert.Equal(updates.Length - 1, update.Session.PendingEdits.Count);
        Assert.DoesNotContain(update.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(6, trade.SpeciesId);
        Assert.Equal(1, trade.Form);
        Assert.Equal(2, trade.Level);
        Assert.Equal(1, trade.HeldItemId);
        Assert.Equal(1, trade.BallItemId);
        Assert.Equal(5, trade.Field03);
        Assert.Equal(1, trade.Ability);
        Assert.Equal(3, trade.Nature);
        Assert.Equal(1, trade.Gender);
        Assert.Equal(1, trade.ShinyLock);
        Assert.Equal(1, trade.DynamaxLevel);
        Assert.True(trade.CanGigantamax);
        Assert.Equal(6, trade.RequiredSpeciesId);
        Assert.Equal(1, trade.RequiredForm);
        Assert.Equal(3, trade.RequiredNature);
        Assert.Equal(0, trade.UnknownRequirement);
        Assert.Equal(1, trade.TrainerId);
        Assert.Equal(1, trade.OtGender);
        Assert.Equal(1, trade.MemoryCode);
        Assert.Equal(1, trade.MemoryTextVariable);
        Assert.Equal(1, trade.MemoryFeel);
        Assert.Equal(1, trade.MemoryIntensity);
        Assert.Equal([1, 2, 3, 4], trade.RelearnMoves.Select(move => move.MoveId).ToArray());
        Assert.Equal(new SwShTradePokemonIvsRecord(1, 2, 3, 5, 6, 4), trade.Ivs);
        Assert.True(plan.CanApply);
        Assert.Single(apply.WrittenFiles);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var output = SwShTradePokemonArchive.Parse(File.ReadAllBytes(GetOutputTradePath(temp))).Trades[1];
        Assert.Equal(1, output.Form);
        Assert.Equal(1, output.DynamaxLevel);
        Assert.Equal(1, output.BallItemId);
        Assert.Equal(5, output.Field03);
        Assert.True(output.CanGigantamax);
        Assert.Equal(1, output.HeldItem);
        Assert.Equal(2, output.Level);
        Assert.Equal(6, output.Species);
        Assert.Equal(1, output.TrainerId);
        Assert.Equal(1, output.MemoryCode);
        Assert.Equal(1, output.MemoryTextVariable);
        Assert.Equal(1, output.MemoryFeel);
        Assert.Equal(1, output.MemoryIntensity);
        Assert.Equal(1, output.OtGender);
        Assert.Equal(1, output.RequiredForm);
        Assert.Equal(6, output.RequiredSpecies);
        Assert.Equal(3, output.RequiredNature);
        Assert.Equal(0, output.UnknownRequirement);
        Assert.Equal(1, output.ShinyLock);
        Assert.Equal(3, output.Nature);
        Assert.Equal(1, output.Gender);
        Assert.Equal(new SwShTradePokemonIvs(1, 2, 3, 4, 5, 6), output.Ivs);
        Assert.Equal(1, output.Ability);
        Assert.Equal([1, 2, 3, 4], output.RelearnMoves);
    }

    [Fact]
    public void ApplyChangePlanWritesThreePerfectIvSentinel()
    {
        using var temp = TemporarySwShProject.Create();
        SwShTradePokemonWorkflowServiceTests.WriteTradeFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShTradePokemonEditSessionService();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            tradeIndex: 1,
            field: SwShTradePokemonWorkflowService.FlawlessIvCountField,
            value: "3");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        _ = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        var output = SwShTradePokemonArchive.Parse(File.ReadAllBytes(GetOutputTradePath(temp)));
        Assert.Equal(new SwShTradePokemonIvs(-4, -1, -1, -1, -1, -1), output.Trades[1].Ivs);
    }

    [Fact]
    public void ApplyRejectsReviewedPlanAfterSameCountPendingValuesChange()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShTradePokemonEditSessionService();
        var first = service.UpdateFields(
            temp.Paths,
            null,
            [
                new(0, SwShTradePokemonWorkflowService.LevelField, "51"),
                new(0, SwShTradePokemonWorkflowService.DynamaxLevelField, "5"),
            ]);
        var reviewedPlan = service.CreateChangePlan(temp.Paths, first.Session);
        var changed = service.UpdateFields(
            temp.Paths,
            first.Session,
            [
                new(0, SwShTradePokemonWorkflowService.LevelField, "52"),
                new(0, SwShTradePokemonWorkflowService.DynamaxLevelField, "6"),
            ]);

        var apply = service.ApplyChangePlan(temp.Paths, changed.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(GetOutputTradePath(temp)));
    }

    [Fact]
    public void ApplyRejectsReviewedPlanAfterTradeSourceContentChanges()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShTradePokemonEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShTradePokemonWorkflowService.LevelField,
            "51");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, update.Session);
        var sourcePath = GetBaseTradePath(temp);
        var archive = SwShTradePokemonArchive.Parse(File.ReadAllBytes(sourcePath));
        File.WriteAllBytes(
            sourcePath,
            new SwShTradePokemonArchive(archive.Trades.Select(trade =>
                trade.Index == 1 ? trade with { Level = 2 } : trade).ToArray()).Write());

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(GetOutputTradePath(temp)));
    }

    [Fact]
    public void ApplyRejectsReviewedPlanAfterTradeSourceLayerChanges()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShTradePokemonEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShTradePokemonWorkflowService.LevelField,
            "51");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, update.Session);
        var unexpectedOutput = SwShTradePokemonWorkflowServiceTests.CreateTradeTable(
            new SwShTradePokemonIvs(31, 31, 31, 31, 31, 31));
        temp.WriteOutputFile(SwShTradePokemonWorkflowService.TradePokemonDataPath, unexpectedOutput);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Equal(unexpectedOutput, File.ReadAllBytes(GetOutputTradePath(temp)));
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyRejectsReviewedSemanticPlanAfterPersonalLookupChanges()
    {
        using var temp = CreateEditableFixture();
        WritePersonalFixture(temp, eeveeGenderRatio: 127);
        var service = new SwShTradePokemonEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShTradePokemonWorkflowService.GenderField,
            "1");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, update.Session);
        var personalPath = Path.Combine(
            temp.BaseRomFsPath,
            SwShPokemonWorkflowService.PersonalDataPath["romfs/".Length..]
                .Replace('/', Path.DirectorySeparatorChar));
        var personalData = File.ReadAllBytes(personalPath);
        personalData[0] ^= 1;
        File.WriteAllBytes(personalPath, personalData);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(GetOutputTradePath(temp)));
    }

    [Fact]
    public void SignedRecordIdentityRejectsReplacementAtTheSameIndex()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShTradePokemonEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShTradePokemonWorkflowService.LevelField,
            "51");
        var sourcePath = GetBaseTradePath(temp);
        var archive = SwShTradePokemonArchive.Parse(File.ReadAllBytes(sourcePath));
        File.WriteAllBytes(
            sourcePath,
            new SwShTradePokemonArchive(archive.Trades.Select(trade =>
                trade.Index == 0 ? trade with { Hash1 = trade.Hash1 + 1 } : trade).ToArray()).Write());

        var validation = service.Validate(temp.Paths, update.Session);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SignedRecordIdentityRejectsReorderedSourceRecords()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShTradePokemonEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShTradePokemonWorkflowService.LevelField,
            "51");
        var sourcePath = GetBaseTradePath(temp);
        var archive = SwShTradePokemonArchive.Parse(File.ReadAllBytes(sourcePath));
        File.WriteAllBytes(
            sourcePath,
            new SwShTradePokemonArchive([archive.Trades[1], archive.Trades[0]]).Write());

        var validation = service.Validate(temp.Paths, update.Session);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SemanticValidationUsesFinalOfferedAndRequestedPokemonState()
    {
        using var temp = CreateEditableFixture();
        WritePersonalFixture(temp, eeveeGenderRatio: 0);
        var service = new SwShTradePokemonEditSessionService();

        var invalidAbility = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShTradePokemonWorkflowService.AbilityField,
            "2");
        var invalidGender = service.UpdateField(
            temp.Paths,
            null,
            1,
            SwShTradePokemonWorkflowService.GenderField,
            "2");
        var invalidRequestedForm = service.UpdateFields(
            temp.Paths,
            null,
            [
                new(1, SwShTradePokemonWorkflowService.RequiredSpeciesField, "810"),
                new(1, SwShTradePokemonWorkflowService.RequiredFormField, "2"),
            ]);
        var clearedRequest = service.UpdateFields(
            temp.Paths,
            null,
            [
                new(0, SwShTradePokemonWorkflowService.RequiredSpeciesField, "0"),
                new(0, SwShTradePokemonWorkflowService.RequiredFormField, "0"),
            ]);

        Assert.All(
            new[] { invalidAbility, invalidGender, invalidRequestedForm },
            result =>
            {
                Assert.Empty(result.Session.PendingEdits);
                Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            });
        Assert.Equal(2, clearedRequest.Session.PendingEdits.Count);
        Assert.Equal(0, clearedRequest.Workflow.Trades[0].RequiredSpeciesId);
        Assert.Equal("None", clearedRequest.Workflow.Trades[0].RequiredSpecies);
    }

    [Fact]
    public void ItemBallAndMoveEditsFailClosedWithoutLookupsButCanStillBeCleared()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            SwShTradePokemonWorkflowService.TradePokemonDataPath["romfs/".Length..],
            SwShTradePokemonWorkflowServiceTests.CreateTradeTable(
                new SwShTradePokemonIvs(31, 30, 29, 28, 27, 26)));
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShTradePokemonEditSessionService();
        var fields = new[]
        {
            (Field: SwShTradePokemonWorkflowService.HeldItemIdField, InvalidValue: "2"),
            (Field: SwShTradePokemonWorkflowService.BallItemIdField, InvalidValue: "1"),
            (Field: SwShTradePokemonWorkflowService.RelearnMove0Field, InvalidValue: "2"),
            (Field: SwShTradePokemonWorkflowService.RelearnMove1Field, InvalidValue: "1"),
            (Field: SwShTradePokemonWorkflowService.RelearnMove2Field, InvalidValue: "1"),
            (Field: SwShTradePokemonWorkflowService.RelearnMove3Field, InvalidValue: "1"),
        };

        foreach (var (field, invalidValue) in fields)
        {
            var invalid = service.UpdateField(temp.Paths, null, 0, field, invalidValue);
            Assert.Empty(invalid.Session.PendingEdits);
            Assert.Contains(invalid.Diagnostics, diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("lookup data is unavailable", StringComparison.OrdinalIgnoreCase));
        }

        var cleared = service.UpdateFields(
            temp.Paths,
            null,
            fields.Select(field => new SwShTradePokemonFieldUpdate(0, field.Field, "0")).ToArray());

        Assert.Equal(fields.Length, cleared.Session.PendingEdits.Count);
        Assert.Equal(0, cleared.Workflow.Trades[0].HeldItemId);
        Assert.Equal(0, cleared.Workflow.Trades[0].BallItemId);
        Assert.All(cleared.Workflow.Trades[0].RelearnMoves, move => Assert.Equal(0, move.MoveId));
        Assert.DoesNotContain(cleared.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ItemBallAndMoveEditsUseLoadedWorkflowLookupOptions()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShTradePokemonEditSessionService();

        var result = service.UpdateFields(
            temp.Paths,
            null,
            [
                new(1, SwShTradePokemonWorkflowService.HeldItemIdField, "1"),
                new(1, SwShTradePokemonWorkflowService.BallItemIdField, "1"),
                new(1, SwShTradePokemonWorkflowService.RelearnMove0Field, "1"),
                new(1, SwShTradePokemonWorkflowService.RelearnMove1Field, "2"),
                new(1, SwShTradePokemonWorkflowService.RelearnMove2Field, "3"),
                new(1, SwShTradePokemonWorkflowService.RelearnMove3Field, "4"),
            ]);

        Assert.Equal(6, result.Session.PendingEdits.Count);
        Assert.Equal(1, result.Workflow.Trades[1].HeldItemId);
        Assert.Equal(1, result.Workflow.Trades[1].BallItemId);
        Assert.Equal([1, 2, 3, 4], result.Workflow.Trades[1].RelearnMoves.Select(move => move.MoveId).ToArray());
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UnknownRequirementOnlyClearsAndLegacySourceValueCanBeReverted()
    {
        using var temp = CreateEditableFixture();
        var source = SwShTradePokemonArchive.Parse(File.ReadAllBytes(GetBaseTradePath(temp)));
        var legacySource = new SwShTradePokemonArchive(
            source.Trades
                .Select(trade => trade.Index == 0 ? trade with { UnknownRequirement = 3 } : trade)
                .ToArray())
            .Write();
        temp.WriteBaseRomFsFile(
            SwShTradePokemonWorkflowService.TradePokemonDataPath["romfs/".Length..],
            legacySource);
        var service = new SwShTradePokemonEditSessionService();

        var unsupported = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShTradePokemonWorkflowService.UnknownRequirementField,
            "1");
        var noOp = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShTradePokemonWorkflowService.UnknownRequirementField,
            "3");
        var cleared = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShTradePokemonWorkflowService.UnknownRequirementField,
            "0");
        var reverted = service.UpdateField(
            temp.Paths,
            cleared.Session,
            0,
            SwShTradePokemonWorkflowService.UnknownRequirementField,
            "3");
        var recleared = service.UpdateField(
            temp.Paths,
            reverted.Session,
            0,
            SwShTradePokemonWorkflowService.UnknownRequirementField,
            "0");
        var plan = service.CreateChangePlan(temp.Paths, recleared.Session);
        var apply = service.ApplyChangePlan(temp.Paths, recleared.Session, plan);

        Assert.Empty(unsupported.Session.PendingEdits);
        Assert.Equal(3, unsupported.Workflow.Trades[0].UnknownRequirement);
        Assert.Contains(unsupported.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(noOp.Session.PendingEdits);
        Assert.Equal(3, noOp.Workflow.Trades[0].UnknownRequirement);
        Assert.DoesNotContain(noOp.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var unknownField = noOp.Workflow.EditableFields.Single(field =>
            field.Field == SwShTradePokemonWorkflowService.UnknownRequirementField);
        Assert.Equal(0, unknownField.MinimumValue);
        Assert.Equal(0, unknownField.MaximumValue);
        Assert.Single(cleared.Session.PendingEdits);
        Assert.Equal(0, cleared.Workflow.Trades[0].UnknownRequirement);
        Assert.Empty(reverted.Session.PendingEdits);
        Assert.Equal(3, reverted.Workflow.Trades[0].UnknownRequirement);
        Assert.DoesNotContain(reverted.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.True(plan.CanApply);
        Assert.Single(apply.WrittenFiles);
        Assert.Equal(
            0,
            SwShTradePokemonArchive.Parse(File.ReadAllBytes(GetOutputTradePath(temp))).Trades[0].UnknownRequirement);
    }

    [Fact]
    public void FailedAtomicWritePreservesExistingOutputAndReportsNoWrittenFile()
    {
        using var temp = CreateEditableFixture();
        var originalOutput = SwShTradePokemonWorkflowServiceTests.CreateTradeTable(
            new SwShTradePokemonIvs(31, 31, 31, 31, 31, 31));
        temp.WriteOutputFile(SwShTradePokemonWorkflowService.TradePokemonDataPath, originalOutput);
        var service = new SwShTradePokemonEditSessionService(
            (_, _) => throw new IOException("Injected write failure."));
        var update = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShTradePokemonWorkflowService.LevelField,
            "51");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Equal(originalOutput, File.ReadAllBytes(GetOutputTradePath(temp)));
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyRefreshesWorkspaceCacheSoLaterLoadsUseLayeredOutput()
    {
        using var temp = CreateEditableFixture();
        var service = new SwShTradePokemonEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShTradePokemonWorkflowService.IvAttackField,
            "-1");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        var next = service.UpdateField(
            temp.Paths,
            null,
            0,
            SwShTradePokemonWorkflowService.LevelField,
            "51");

        Assert.Single(apply.WrittenFiles);
        Assert.Equal(-1, next.Workflow.Trades[0].Ivs.Attack);
        Assert.Equal(ProjectFileLayer.Layered, next.Workflow.Trades[0].Provenance.SourceLayer);
    }

    [Fact]
    public void LegacyUnsignedRecordIdsRemainValidAndGainCurrentSemanticSources()
    {
        using var temp = CreateEditableFixture();
        WritePersonalFixture(temp, eeveeGenderRatio: 127);
        var edit = new PendingEdit(
            "workflow.tradePokemon",
            "Update offered species.",
            [new ProjectFileReference(ProjectFileLayer.Base, SwShTradePokemonWorkflowService.TradePokemonDataPath)],
            RecordId: "trade:1",
            Field: SwShTradePokemonWorkflowService.SpeciesField,
            NewValue: "810");
        var session = EditSession.Start().WithPendingEdit(edit);
        var service = new SwShTradePokemonEditSessionService();

        var validation = service.Validate(temp.Paths, session);
        var plan = service.CreateChangePlan(temp.Paths, session);

        Assert.True(validation.IsValid);
        Assert.True(plan.CanApply);
        Assert.Contains(
            Assert.Single(plan.Writes).Sources,
            source => source.RelativePath == SwShPokemonWorkflowService.PersonalDataPath);
    }

    private static TemporarySwShProject CreateEditableFixture()
    {
        var temp = TemporarySwShProject.Create();
        SwShTradePokemonWorkflowServiceTests.WriteTradeFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        return temp;
    }

    private static void WritePersonalFixture(TemporarySwShProject temp, byte eeveeGenderRatio)
    {
        var records = Enumerable.Range(0, 813)
            .Select(_ => SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord())
            .ToArray();
        records[6] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hatchedSpecies: 6,
            formStatsIndex: 812,
            formCount: 2);
        var pikachu = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hatchedSpecies: 25);
        records[25] = pikachu;
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
        records[812] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hatchedSpecies: 6,
            formStatsIndex: 812,
            formCount: 2,
            form: 1);
        temp.WriteBaseRomFsFile(
            SwShPokemonWorkflowService.PersonalDataPath["romfs/".Length..],
            SwShPokemonWorkflowServiceTests.CreatePersonalTable(records));
    }

    private static string GetBaseTradePath(TemporarySwShProject temp)
    {
        return Path.Combine(
            temp.BaseRomFsPath,
            "bin",
            "script_event_data",
            "field_trade.bin");
    }

    private static string GetOutputTradePath(TemporarySwShProject temp)
    {
        return Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "script_event_data",
            "field_trade.bin");
    }
}
