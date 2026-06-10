// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.Behavior;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.Behavior;

public sealed class SwShBehaviorEditSessionServiceTests
{
    [Fact]
    public void UpdateEntryFieldCreatesPendingBehaviorEdit()
    {
        using var temp = TemporarySwShProject.Create();
        SwShBehaviorTestFixtures.WriteBaseBehavior(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShBehaviorEditSessionService();

        var result = service.UpdateEntryField(
            temp.Paths,
            session: null,
            entryId: "0",
            field: SwShSymbolBehaviorArchive.BehaviorField,
            value: "Escape");

        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.behavior", edit.Domain);
        Assert.Equal(SwShSymbolBehaviorArchive.BehaviorField, edit.Field);
        Assert.Equal("0", edit.RecordId);
        Assert.Equal("Escape", edit.NewValue);
        Assert.Equal("Escape", result.Workflow.Entries.Single(entry => entry.EntryId == "0").Behavior);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ApplyChangePlanWritesLayeredBehaviorData()
    {
        using var temp = TemporarySwShProject.Create();
        SwShBehaviorTestFixtures.WriteBaseBehavior(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShBehaviorEditSessionService();
        var behaviorUpdate = service.UpdateEntryField(
            temp.Paths,
            session: null,
            entryId: "0",
            field: SwShSymbolBehaviorArchive.BehaviorField,
            value: "Escape");
        var radiusUpdate = service.UpdateEntryField(
            temp.Paths,
            behaviorUpdate.Session,
            entryId: "0",
            field: SwShSymbolBehaviorArchive.HitboxRadiusField,
            value: "9.5");

        var validation = service.Validate(temp.Paths, radiusUpdate.Session);
        var plan = service.CreateChangePlan(temp.Paths, radiusUpdate.Session);
        var apply = service.ApplyChangePlan(temp.Paths, radiusUpdate.Session, plan);

        Assert.True(validation.IsValid);
        Assert.True(plan.CanApply);
        Assert.Equal(SwShBehaviorWorkflowService.BehaviorDataPath, Assert.Single(plan.Writes).TargetRelativePath);
        Assert.Equal(SwShBehaviorWorkflowService.BehaviorDataPath, Assert.Single(apply.WrittenFiles).RelativePath);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "field",
            "param",
            "symbol_encount_mons_param",
            "symbol_encount_mons_param.bin");
        var archive = SwShSymbolBehaviorArchive.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal("Escape", archive.Entries[0].Behavior);
        Assert.Equal(9.5f, archive.Entries[0].HitboxRadius);
        Assert.Equal("WaterDash", archive.Entries[1].Behavior);
    }

    [Fact]
    public void UpdateEntryFieldRejectsReadOnlyUnknownFields()
    {
        using var temp = TemporarySwShProject.Create();
        SwShBehaviorTestFixtures.WriteBaseBehavior(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShBehaviorEditSessionService();

        var result = service.UpdateEntryField(
            temp.Paths,
            session: null,
            entryId: "0",
            field: SwShSymbolBehaviorArchive.Hash1Field,
            value: "123");

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error
                && diagnostic.Domain == "workflow.behavior"
                && diagnostic.Expected == "Editable Behavior field");
    }
}
