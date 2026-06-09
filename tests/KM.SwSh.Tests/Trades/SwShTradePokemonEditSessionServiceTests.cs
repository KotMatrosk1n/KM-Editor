// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.Trades;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.Trades;

public sealed class SwShTradePokemonEditSessionServiceTests
{
    [Fact]
    public void UpdateFieldCreatesPendingTradePokemonIvEdit()
    {
        using var temp = TemporarySwShProject.Create();
        SwShTradePokemonWorkflowServiceTests.WriteTradeFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShTradePokemonEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            tradeIndex: 0,
            field: SwShTradePokemonWorkflowService.IvAttackField,
            value: "80");
        result = service.UpdateField(
            temp.Paths,
            result.Session,
            tradeIndex: 0,
            field: SwShTradePokemonWorkflowService.IvDefenseField,
            value: "-50");

        Assert.Equal(2, result.Session.PendingEdits.Count);
        Assert.Contains(result.Session.PendingEdits, edit =>
            edit.Domain == "workflow.tradePokemon"
            && edit.Field == SwShTradePokemonWorkflowService.IvAttackField
            && edit.RecordId == "trade:0"
            && edit.NewValue == "31");
        Assert.Contains(result.Session.PendingEdits, edit =>
            edit.Field == SwShTradePokemonWorkflowService.IvDefenseField
            && edit.NewValue == "0");
        Assert.Equal(31, result.Workflow.Trades[0].Ivs.Attack);
        Assert.Equal(0, result.Workflow.Trades[0].Ivs.Defense);
        Assert.Empty(result.Diagnostics);
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
        var output = SwShTradePokemonArchive.Parse(File.ReadAllBytes(GetOutputtradePath(temp)));
        Assert.Equal(new SwShTradePokemonIvs(0, 1, 2, 3, 4, 5), output.Trades[0].Ivs);
        Assert.Equal(4, output.Trades[0].RelearnMoves[2]);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error);
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

        var output = SwShTradePokemonArchive.Parse(File.ReadAllBytes(GetOutputtradePath(temp)));
        Assert.Equal(new SwShTradePokemonIvs(-4, -1, -1, -1, -1, -1), output.Trades[1].Ivs);
    }

    private static string GetOutputtradePath(TemporarySwShProject temp)
    {
        return Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "script_event_data",
            "field_trade.bin");
    }
}
