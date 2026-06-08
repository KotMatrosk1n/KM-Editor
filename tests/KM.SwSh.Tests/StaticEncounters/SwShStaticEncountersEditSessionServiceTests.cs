// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Editing;
using KM.Formats.SwSh;
using KM.SwSh.StaticEncounters;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.StaticEncounters;

public sealed class SwShStaticEncountersEditSessionServiceTests
{
    [Fact]
    public void UpdateFieldCreatesPendingStaticEncounterIvEdit()
    {
        using var temp = TemporarySwShProject.Create();
        SwShStaticEncountersWorkflowServiceTests.WriteStaticEncounterFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShStaticEncountersEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            encounterIndex: 0,
            field: SwShStaticEncountersWorkflowService.IvAttackField,
            value: "12");

        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.staticEncounters", edit.Domain);
        Assert.Equal(SwShStaticEncountersWorkflowService.IvAttackField, edit.Field);
        Assert.Equal("static:0", edit.RecordId);
        Assert.Equal("12", edit.NewValue);
        Assert.Equal(12, result.Workflow.Encounters[0].Ivs.Attack);
        Assert.Empty(result.Diagnostics);
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
        Assert.Equal(SwShStaticEncountersWorkflowService.StaticEncounterDataPath, Assert.Single(plan.Writes).TargetRelativePath);
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
    public void ValidateRejectsUnsupportedStaticEncounterIvSentinelField()
    {
        using var temp = TemporarySwShProject.Create();
        SwShStaticEncountersWorkflowServiceTests.WriteStaticEncounterFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShStaticEncountersEditSessionService();
        var session = EditSession.Start() with
        {
            PendingEdits =
            [
                new PendingEdit(
                    "workflow.staticEncounters",
                    "Set unsupported IV sentinel.",
                    [],
                    RecordId: "static:0",
                    Field: SwShStaticEncountersWorkflowService.IvAttackField,
                    NewValue: "-4")
            ],
        };

        var validation = service.Validate(temp.Paths, session);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Field == SwShStaticEncountersWorkflowService.IvAttackField);
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
