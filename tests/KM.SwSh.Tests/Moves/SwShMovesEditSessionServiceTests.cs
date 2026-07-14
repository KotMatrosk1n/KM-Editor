// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Formats.SwSh;
using KM.SwSh.Editing;
using KM.SwSh.Moves;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.Moves;

public sealed class SwShMovesEditSessionServiceTests
{
    [Fact]
    public void UpdateFieldCreatesPendingMoveEditAndOverlaysWorkflow()
    {
        using var temp = CreateEditableProject();
        var service = new SwShMovesEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.PowerField,
            value: "80");

        Assert.Empty(result.Diagnostics);
        Assert.True(result.Session.HasPendingChanges);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.moves", edit.Domain);
        Assert.Equal(SwShMovesWorkflowService.PowerField, edit.Field);
        Assert.Equal("33", edit.RecordId);
        Assert.Equal("80", edit.NewValue);
        Assert.Equal(80, Assert.Single(result.Workflow.Moves).Power);
    }

    [Fact]
    public void UpdateFieldOverlaysBooleanAndSignedFields()
    {
        using var temp = CreateEditableProject();
        var service = new SwShMovesEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.MakesContactField,
            value: "false");
        result = service.UpdateField(
            temp.Paths,
            result.Session,
            moveId: 33,
            field: SwShMovesWorkflowService.RecoilField,
            value: "-10");

        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, result.Session.PendingEdits.Count);
        var move = Assert.Single(result.Workflow.Moves);
        Assert.DoesNotContain(move.Flags, flag => flag.Field == SwShMovesWorkflowService.MakesContactField && flag.Enabled);
        Assert.Equal(-10, move.Recoil);
    }

    [Fact]
    public void UpdateFieldReplacesPendingEditForSameMoveField()
    {
        using var temp = CreateEditableProject();
        var service = new SwShMovesEditSessionService();
        var first = service.UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.PowerField,
            value: "80");

        var second = service.UpdateField(
            temp.Paths,
            first.Session,
            moveId: 33,
            field: SwShMovesWorkflowService.PowerField,
            value: "90");

        var edit = Assert.Single(second.Session.PendingEdits);
        Assert.Equal("90", edit.NewValue);
        Assert.Equal(90, Assert.Single(second.Workflow.Moves).Power);
    }

    [Fact]
    public void ValidateAndCreateChangePlanUseMoveDataTargets()
    {
        using var temp = CreateEditableProject();
        var service = new SwShMovesEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.PowerField,
            value: "80");

        var validation = service.Validate(temp.Paths, update.Session);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        Assert.True(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Info);
        Assert.True(plan.CanApply);
        var write = Assert.Single(plan.Writes);
        Assert.Equal("romfs/bin/pml/waza/waza_033.bin", write.TargetRelativePath);
        Assert.Equal("romfs/bin/pml/waza/waza_033.bin", Assert.Single(write.Sources).RelativePath);
        Assert.False(write.ReplacesExistingOutput);
    }

    [Fact]
    public void ApplyChangePlanWritesEditedMoveDataToOutputRoot()
    {
        using var temp = CreateEditableProject();
        var service = new SwShMovesEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.PowerField,
            value: "80");
        update = service.UpdateField(
            temp.Paths,
            update.Session,
            moveId: 33,
            field: SwShMovesWorkflowService.Stat1StageField,
            value: "-2");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("romfs/bin/pml/waza/waza_033.bin", Assert.Single(apply.WrittenFiles).RelativePath);
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "waza", "waza_033.bin");
        var output = SwShMoveDataFile.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal(80, output.Record.Core.Power);
        Assert.Equal(-2, output.Record.StatChanges[0].Stage);

        var basePath = Path.Combine(temp.BaseRomFsPath, "bin", "pml", "waza", "waza_033.bin");
        var unchangedBase = SwShMoveDataFile.Parse(File.ReadAllBytes(basePath));
        Assert.Equal(40, unchangedBase.Record.Core.Power);
        Assert.Equal(-1, unchangedBase.Record.StatChanges[0].Stage);
    }

    [Fact]
    public void ApplyReportsSafeEditFailureInsteadOfDiscardingOpaqueSparseSourceData()
    {
        using var temp = CreateEditableProject();
        var source = SwShMoveDataFile.Write(SwShMovesWorkflowServiceTests.CreateMoveRecord(moveId: 33));
        BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(52), 0);
        Array.Resize(ref source, source.Length + 4);
        source[^4] = 0xDE;
        source[^3] = 0xAD;
        source[^2] = 0xBE;
        source[^1] = 0xEF;
        temp.WriteBaseRomFsFile("bin/pml/waza/waza_033.bin", source);
        var service = new SwShMovesEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.TargetField,
            value: "1");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("edited safely", StringComparison.Ordinal)
            && diagnostic.Message.Contains("opaque", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateFieldRequiresEditableProjectPaths()
    {
        using var temp = CreateEditableProject();
        var service = new SwShMovesEditSessionService();

        var result = service.UpdateField(
            temp.Paths with { OutputRootPath = null },
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.PowerField,
            value: "80");

        Assert.False(result.Session.HasPendingChanges);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UpdateFieldRejectsOutOfRangeMoveValue()
    {
        using var temp = CreateEditableProject();
        var service = new SwShMovesEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.TypeField,
            value: "18");

        Assert.False(result.Session.HasPendingChanges);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Field == SwShMovesWorkflowService.TypeField);
    }

    [Fact]
    public void UpdateFieldRejectsInvalidHitPair()
    {
        using var temp = CreateEditableProject();
        var service = new SwShMovesEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.HitMinField,
            value: "2");

        Assert.False(result.Session.HasPendingChanges);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Field == SwShMovesWorkflowService.HitMinField);
    }

    [Fact]
    public void UpdateFieldPreservesUnchangedLegacyInvalidPairButValidatesPairEdits()
    {
        using var temp = CreateEditableProject(
            SwShMovesWorkflowServiceTests.CreateMoveRecord(hitMin: 0, hitMax: 1));
        var service = new SwShMovesEditSessionService();

        var unrelatedUpdate = service.UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.PowerField,
            value: "80");

        Assert.DoesNotContain(
            unrelatedUpdate.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.True(unrelatedUpdate.Session.HasPendingChanges);
        Assert.True(service.CreateChangePlan(temp.Paths, unrelatedUpdate.Session).CanApply);

        var invalidPairEdit = service.UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.HitMinField,
            value: "2");

        Assert.False(invalidPairEdit.Session.HasPendingChanges);
        Assert.Contains(
            invalidPairEdit.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Field == SwShMovesWorkflowService.HitMinField);
    }

    [Fact]
    public void RawStatEightLoadsStagesAndAppliesAsAllStats()
    {
        using var temp = CreateEditableProject(
            SwShMovesWorkflowServiceTests.CreateMoveRecord(stat1: 8),
            moveName: "Ancient Power");
        var service = new SwShMovesEditSessionService();

        var update = service.UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.Stat1StageField,
            value: "1");

        Assert.Empty(update.Diagnostics);
        var stat = Assert.Single(update.Workflow.Moves).StatChanges[0];
        Assert.Equal(8, stat.Stat);
        Assert.Equal("All Stats", stat.StatName);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "waza", "waza_033.bin");
        var output = SwShMoveDataFile.Parse(File.ReadAllBytes(outputPath)).Record;
        Assert.Equal(8, output.StatChanges[0].Stat);
        Assert.Equal(1, output.StatChanges[0].Stage);
    }

    [Fact]
    public void UpdateFieldRejectsSwShStatNine()
    {
        using var temp = CreateEditableProject();
        var service = new SwShMovesEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.Stat1Field,
            value: "9");

        Assert.False(result.Session.HasPendingChanges);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Field == SwShMovesWorkflowService.Stat1Field);
    }

    [Fact]
    public void UpdateFieldLabelsSwShStatEightAsAllStats()
    {
        using var temp = CreateEditableProject();

        var result = new SwShMovesEditSessionService().UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.Stat1Field,
            value: "8");

        Assert.Empty(result.Diagnostics);
        var stat = Assert.Single(result.Workflow.Moves).StatChanges[0];
        Assert.Equal(8, stat.Stat);
        Assert.Equal("All Stats", stat.StatName);
    }

    [Fact]
    public void UpdateFieldLabelsScriptedInflictSentinelNeutrally()
    {
        using var temp = CreateEditableProject();

        var result = new SwShMovesEditSessionService().UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.InflictField,
            value: ushort.MaxValue.ToString());

        Assert.Empty(result.Diagnostics);
        var move = Assert.Single(result.Workflow.Moves);
        Assert.Equal(ushort.MaxValue, move.Inflict);
        Assert.Equal("Move-defined / scripted effect", move.InflictName);
    }

    [Theory]
    [InlineData(SwShMovesWorkflowService.QualityField, "14")]
    [InlineData(SwShMovesWorkflowService.AccuracyField, "102")]
    [InlineData(SwShMovesWorkflowService.PpField, "0")]
    [InlineData(SwShMovesWorkflowService.PriorityField, "6")]
    [InlineData(SwShMovesWorkflowService.CritStageField, "3")]
    [InlineData(SwShMovesWorkflowService.MaxMovePowerField, "105")]
    [InlineData(SwShMovesWorkflowService.TargetField, "14")]
    [InlineData(SwShMovesWorkflowService.InflictField, "10")]
    [InlineData(SwShMovesWorkflowService.InflictPercentField, "101")]
    [InlineData(SwShMovesWorkflowService.RawInflictCountField, "5")]
    [InlineData(SwShMovesWorkflowService.FlinchField, "101")]
    [InlineData(SwShMovesWorkflowService.Stat1StageField, "7")]
    [InlineData(SwShMovesWorkflowService.Stat1PercentField, "101")]
    public void UpdateFieldRejectsUnsupportedSemanticValues(string field, string value)
    {
        using var temp = CreateEditableProject();
        var result = new SwShMovesEditSessionService().UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field,
            value);

        Assert.False(result.Session.HasPendingChanges);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Field == field);
    }

    [Fact]
    public void UpdateFieldsStagesPairedHitRangeAtomically()
    {
        using var temp = CreateEditableProject();
        var service = new SwShMovesEditSessionService();

        var result = service.UpdateFields(
            temp.Paths,
            session: null,
            [
                new SwShMoveFieldUpdate(33, SwShMovesWorkflowService.HitMinField, "2"),
                new SwShMoveFieldUpdate(33, SwShMovesWorkflowService.HitMaxField, "3"),
            ]);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, result.Session.PendingEdits.Count);
        var move = Assert.Single(result.Workflow.Moves);
        Assert.Equal(2, move.HitMin);
        Assert.Equal(3, move.HitMax);
    }

    [Fact]
    public void UpdateFieldsRejectsEntireInvalidBatch()
    {
        using var temp = CreateEditableProject();
        var service = new SwShMovesEditSessionService();

        var result = service.UpdateFields(
            temp.Paths,
            session: null,
            [
                new SwShMoveFieldUpdate(33, SwShMovesWorkflowService.PowerField, "80"),
                new SwShMoveFieldUpdate(33, SwShMovesWorkflowService.HitMinField, "2"),
                new SwShMoveFieldUpdate(33, SwShMovesWorkflowService.HitMaxField, "0"),
            ]);

        Assert.False(result.Session.HasPendingChanges);
        var move = Assert.Single(result.Workflow.Moves);
        Assert.Equal(40, move.Power);
        Assert.Equal(1, move.HitMin);
        Assert.Equal(1, move.HitMax);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void UpdateFieldRemovesPendingEditWhenRevertedToLoadedValue()
    {
        using var temp = CreateEditableProject();
        var service = new SwShMovesEditSessionService();
        var changed = service.UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.PowerField,
            value: "80");

        var reverted = service.UpdateField(
            temp.Paths,
            changed.Session,
            moveId: 33,
            field: SwShMovesWorkflowService.PowerField,
            value: "40");

        Assert.False(reverted.Session.HasPendingChanges);
        Assert.Equal(40, Assert.Single(reverted.Workflow.Moves).Power);
    }

    [Fact]
    public void UpdateFieldsRefreshesWorkflowStats()
    {
        using var temp = CreateEditableProject();
        var result = new SwShMovesEditSessionService().UpdateFields(
            temp.Paths,
            session: null,
            [
                new SwShMoveFieldUpdate(33, SwShMovesWorkflowService.CanUseMoveField, "false"),
                new SwShMoveFieldUpdate(33, SwShMovesWorkflowService.MakesContactField, "false"),
            ]);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(0, result.Workflow.Stats.EnabledMoveCount);
        Assert.Equal(2, result.Workflow.Stats.ActiveFlagCount);
    }

    [Fact]
    public void UpdateFieldWarnsWhenEnablingBaseDisabledMove()
    {
        using var temp = CreateEditableProject(
            SwShMovesWorkflowServiceTests.CreateMoveRecord(canUseMove: false),
            moveName: "Disabled Move");

        var result = new SwShMovesEditSessionService().UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.CanUseMoveField,
            value: "true");

        Assert.True(result.Session.HasPendingChanges);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Warning
            && diagnostic.Field == SwShMovesWorkflowService.CanUseMoveField
            && diagnostic.Message.Contains("Asset-verify", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateFieldDoesNotWarnForAlreadyEnabledOrDisablingMove()
    {
        using var temp = CreateEditableProject();
        var service = new SwShMovesEditSessionService();

        var unchanged = service.UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.CanUseMoveField,
            value: "true");
        var disabled = service.UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.CanUseMoveField,
            value: "false");

        Assert.DoesNotContain(unchanged.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning);
        Assert.False(unchanged.Session.HasPendingChanges);
        Assert.DoesNotContain(disabled.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning);
        Assert.True(disabled.Session.HasPendingChanges);
    }

    [Fact]
    public void UpdateFieldPreservesLocalizedTypeLabel()
    {
        using var temp = CreateEditableProject();
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/typename.dat",
            SwShMovesWorkflowServiceTests.CreateTextTable(
                "Normal Localized",
                "Fighting Localized",
                "Flying Localized",
                "Poison Localized",
                "Ground Localized",
                "Rock Localized",
                "Bug Localized",
                "Ghost Localized",
                "Steel Localized",
                "Fire Localized",
                "Water Localized",
                "Grass Localized",
                "Electric Localized",
                "Psychic Localized",
                "Ice Localized",
                "Dragon Localized",
                "Dark Localized",
                "Fairy Localized"));

        var result = new SwShMovesEditSessionService().UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.TypeField,
            value: "1");

        Assert.Empty(result.Diagnostics);
        Assert.Equal("Fighting Localized", Assert.Single(result.Workflow.Moves).TypeName);
    }

    [Fact]
    public void ApplyRejectsReviewedPlanWhenPendingValueChanged()
    {
        using var temp = CreateEditableProject();
        var service = new SwShMovesEditSessionService();
        var first = service.UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.PowerField,
            value: "80");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, first.Session);
        var second = service.UpdateField(
            temp.Paths,
            first.Session,
            moveId: 33,
            field: SwShMovesWorkflowService.PowerField,
            value: "90");

        var apply = service.ApplyChangePlan(temp.Paths, second.Session, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplyRejectsReviewedPlanWhenPendingValueChangesWithoutSummaryChange()
    {
        using var temp = CreateEditableProject();
        var service = new SwShMovesEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.PowerField,
            value: "80");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, update.Session);
        var pendingEdit = Assert.Single(update.Session.PendingEdits);
        var changedSession = update.Session with
        {
            PendingEdits = [pendingEdit with { NewValue = "90" }],
        };

        var apply = service.ApplyChangePlan(temp.Paths, changedSession, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateRejectsDuplicateLogicalMoveFieldEdits()
    {
        using var temp = CreateEditableProject();
        var service = new SwShMovesEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.PowerField,
            value: "80");
        var pendingEdit = Assert.Single(update.Session.PendingEdits);
        var duplicateSession = update.Session with
        {
            PendingEdits =
            [
                pendingEdit,
                pendingEdit with { RecordId = "033", NewValue = "90" },
            ],
        };

        var validation = service.Validate(temp.Paths, duplicateSession);
        var plan = service.CreateChangePlan(temp.Paths, duplicateSession);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("more than one pending edit", StringComparison.Ordinal));
        Assert.Empty(plan.Writes);
    }

    [Fact]
    public void ApplyAcceptsPlanWithSourceGuardFingerprint()
    {
        using var temp = CreateEditableProject();
        var service = new SwShMovesEditSessionService();
        var update = service.UpdateField(
            temp.Paths,
            session: null,
            moveId: 33,
            field: SwShMovesWorkflowService.PowerField,
            value: "80");
        var plan = SwShChangePlanSourceGuard.Capture(
            temp.Paths,
            service.CreateChangePlan(temp.Paths, update.Session));

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Single(apply.WrittenFiles);
    }

    [Fact]
    public void ApplyRollsBackEarlierMoveWhenLaterTargetCannotBeInstalled()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/pml/waza/waza_033.bin",
            SwShMoveDataFile.Write(SwShMovesWorkflowServiceTests.CreateMoveRecord(moveId: 33)));
        temp.WriteBaseRomFsFile(
            "bin/pml/waza/waza_034.bin",
            SwShMoveDataFile.Write(SwShMovesWorkflowServiceTests.CreateMoveRecord(moveId: 34)));
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShMovesEditSessionService();
        var update = service.UpdateFields(
            temp.Paths,
            session: null,
            [
                new SwShMoveFieldUpdate(33, SwShMovesWorkflowService.PowerField, "80"),
                new SwShMoveFieldUpdate(34, SwShMovesWorkflowService.PowerField, "90"),
            ]);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var outputDirectory = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "waza");
        Directory.CreateDirectory(Path.Combine(outputDirectory, "waza_034.bin"));

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("rolled back", StringComparison.Ordinal));
        Assert.DoesNotContain(apply.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("no partial changes", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(Path.Combine(outputDirectory, "waza_033.bin")));
        Assert.Empty(Directory.EnumerateFiles(outputDirectory, "*.tmp"));
        Assert.Empty(Directory.EnumerateFiles(outputDirectory, "*.bak"));
    }

    private static TemporarySwShProject CreateEditableProject(
        SwShMoveDataRecord? record = null,
        string moveName = "Tackle")
    {
        var temp = TemporarySwShProject.Create();
        SwShMovesWorkflowServiceTests.WriteBaseMoves(temp, record, moveName);
        temp.WriteBaseExeFsFile("main", "base-main");

        return temp;
    }
}
