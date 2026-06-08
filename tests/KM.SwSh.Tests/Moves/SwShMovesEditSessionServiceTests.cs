// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Formats.SwSh;
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

    private static TemporarySwShProject CreateEditableProject()
    {
        var temp = TemporarySwShProject.Create();
        SwShMovesWorkflowServiceTests.WriteBaseMoves(temp);
        temp.WriteBaseExeFsFile("main", "base-main");

        return temp;
    }
}
