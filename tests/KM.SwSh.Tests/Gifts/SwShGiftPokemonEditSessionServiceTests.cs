// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.Gifts;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.Gifts;

public sealed class SwShGiftPokemonEditSessionServiceTests
{
    [Fact]
    public void UpdateFieldCreatesPendingGiftPokemonIvEdit()
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
        result = service.UpdateField(
            temp.Paths,
            result.Session,
            giftIndex: 0,
            field: SwShGiftPokemonWorkflowService.IvDefenseField,
            value: "-50");

        Assert.Equal(2, result.Session.PendingEdits.Count);
        Assert.Contains(result.Session.PendingEdits, edit =>
            edit.Domain == "workflow.giftPokemon"
            && edit.Field == SwShGiftPokemonWorkflowService.IvAttackField
            && edit.RecordId == "gift:0"
            && edit.NewValue == "31");
        Assert.Contains(result.Session.PendingEdits, edit =>
            edit.Field == SwShGiftPokemonWorkflowService.IvDefenseField
            && edit.NewValue == "0");
        Assert.Equal(31, result.Workflow.Gifts[0].Ivs.Attack);
        Assert.Equal(0, result.Workflow.Gifts[0].Ivs.Defense);
        Assert.Empty(result.Diagnostics);
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
