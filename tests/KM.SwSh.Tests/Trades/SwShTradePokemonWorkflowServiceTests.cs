// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Trades;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Trades;

public sealed class SwShTradePokemonWorkflowServiceTests
{
    [Fact]
    public void LoadReadsTradePokemonFromRealSwordShieldTradeTable()
    {
        using var temp = TemporarySwShProject.Create();
        WriteTradeFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShTradePokemonWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Equal(2, workflow.Stats.TotalTradeCount);
        Assert.Equal(1, workflow.Stats.FixedIvTradeCount);
        Assert.Equal(4, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);

        var firstTrade = workflow.Trades[0];
        Assert.Equal(0, firstTrade.TradeIndex);
        Assert.Equal("Trade 001: Pikachu (Form 2) -> Grookey (Form 1) Lv. 50", firstTrade.Label);
        Assert.Equal(810, firstTrade.SpeciesId);
        Assert.Equal("Grookey", firstTrade.Species);
        Assert.Equal(25, firstTrade.RequiredSpeciesId);
        Assert.Equal("Pikachu", firstTrade.RequiredSpecies);
        Assert.Equal(2, firstTrade.RequiredForm);
        Assert.Equal(24, firstTrade.RequiredNature);
        Assert.Equal("Quirky", firstTrade.RequiredNatureLabel);
        Assert.Equal(1, firstTrade.HeldItemId);
        Assert.Equal("Potion", firstTrade.HeldItem);
        Assert.Equal(4, firstTrade.BallItemId);
        Assert.Equal("Poke Ball", firstTrade.BallItem);
        Assert.Equal(3, firstTrade.Ability);
        Assert.Equal("Hidden Ability", firstTrade.AbilityLabel);
        Assert.Equal(25, firstTrade.Nature);
        Assert.Equal("Random", firstTrade.NatureLabel);
        Assert.Equal(0, firstTrade.Gender);
        Assert.Equal("Random", firstTrade.GenderLabel);
        Assert.Equal(0, firstTrade.ShinyLock);
        Assert.Equal("Random", firstTrade.ShinyLockLabel);
        Assert.Equal(10, firstTrade.DynamaxLevel);
        Assert.True(firstTrade.CanGigantamax);
        Assert.Equal(123456, firstTrade.TrainerId);
        Assert.Equal("Female", firstTrade.OtGenderLabel);
        Assert.Equal(11, firstTrade.MemoryCode);
        Assert.Equal(0x1234, firstTrade.MemoryTextVariable);
        Assert.Equal("Growl", firstTrade.RelearnMoves[1].Move);
        Assert.Equal(new SwShTradePokemonIvsRecord(31, 30, 29, 27, 26, 28), firstTrade.Ivs);
        Assert.Null(firstTrade.FlawlessIvCount);
        Assert.Contains("HP 31", firstTrade.IvSummary, StringComparison.Ordinal);
        Assert.Equal(ProjectFileLayer.Base, firstTrade.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, firstTrade.Provenance.FileState);

        var secondTrade = workflow.Trades[1];
        Assert.Equal("Random IVs", secondTrade.IvSummary);
        Assert.Equal(0, secondTrade.FlawlessIvCount);
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShTradePokemonWorkflowService.SpeciesField).Options,
            option => option.Value == 810 && option.Label == "810 Grookey");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShTradePokemonWorkflowService.RequiredSpeciesField).Options,
            option => option.Value == 25 && option.Label == "025 Pikachu");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShTradePokemonWorkflowService.HeldItemIdField).Options,
            option => option.Value == 1 && option.Label == "001 Potion");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShTradePokemonWorkflowService.RelearnMove1Field).Options,
            option => option.Value == 2 && option.Label == "002 Growl");
    }

    [Fact]
    public void LoadFormatsRegionalTradeFormsWithReadableLabels()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            SwShTradePokemonWorkflowService.TradePokemonDataPath["romfs/".Length..],
            new SwShTradePokemonArchive(
            [
                new SwShTradePokemonRecord(
                    0,
                    1,
                    0,
                    4,
                    0,
                    0,
                    false,
                    1,
                    50,
                    83,
                    0,
                    123456,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    2,
                    52,
                    0,
                    0,
                    0,
                    0,
                    0,
                    new SwShTradePokemonIvs(-1, -1, -1, -1, -1, -1),
                    1,
                    [1, 0, 0, 0]),
            ]).Write());
        temp.WriteBaseRomFsFile("bin/message/English/common/monsname.dat", CreateTextTable(83, (52, "Meowth"), (83, "Farfetch'd")));
        temp.WriteBaseRomFsFile("bin/message/English/common/itemname.dat", CreateTextTable(4, (4, "Poke Ball")));
        temp.WriteBaseRomFsFile("bin/message/English/common/wazaname.dat", CreateTextTable(1, (1, "Scratch")));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShTradePokemonWorkflowService().Load(project);

        var trade = Assert.Single(workflow.Trades);
        Assert.Equal("Trade 001: Meowth (Galarian) -> Farfetch'd (Galarian) Lv. 50", trade.Label);
    }

    [Fact]
    public void LoadPrefersLayeredTradePokemonDataWhenOutputOverridesBase()
    {
        using var temp = TemporarySwShProject.Create();
        WriteTradeFixture(temp);
        temp.WriteOutputFile(
            SwShTradePokemonWorkflowService.TradePokemonDataPath,
            CreateTradeTable(new SwShTradePokemonIvs(31, 31, 31, 31, 31, 31)));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShTradePokemonWorkflowService().Load(project);

        var trade = workflow.Trades[0];
        Assert.Equal(ProjectFileLayer.Layered, trade.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.LayeredOverride, trade.Provenance.FileState);
        Assert.Equal("6 guaranteed perfect IVs", trade.IvSummary);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenTradeTableIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/trades.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShTradePokemonWorkflowService().Load(project);

        Assert.Empty(workflow.Trades);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.tradePokemon");
    }

    internal static void WriteTradeFixture(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            SwShTradePokemonWorkflowService.TradePokemonDataPath["romfs/".Length..],
            CreateTradeTable(new SwShTradePokemonIvs(31, 30, 29, 28, 27, 26)));
        temp.WriteBaseRomFsFile("bin/message/English/common/monsname.dat", CreateTextTable(810, (25, "Pikachu"), (133, "Eevee"), (810, "Grookey")));
        temp.WriteBaseRomFsFile("bin/message/English/common/itemname.dat", CreateTextTable(4, (1, "Potion"), (4, "Poke Ball")));
        temp.WriteBaseRomFsFile("bin/message/English/common/wazaname.dat", CreateTextTable(4, (1, "Scratch"), (2, "Growl"), (3, "Vine Whip"), (4, "Razor Leaf")));
    }

    internal static byte[] CreateTradeTable(SwShTradePokemonIvs firstTradeIvs)
    {
        return new SwShTradePokemonArchive(
        [
            new SwShTradePokemonRecord(
                0,
                1,
                10,
                4,
                0,
                0x1122334455667788,
                true,
                1,
                50,
                810,
                0x8877665544332211,
                123456,
                11,
                0x1234,
                12,
                13,
                0x0102030405060708,
                1,
                2,
                25,
                24,
                0,
                0,
                25,
                0,
                firstTradeIvs,
                3,
                [1, 2, 3, 4]),
            new SwShTradePokemonRecord(
                1,
                0,
                0,
                4,
                0,
                0,
                false,
                0,
                1,
                133,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                new SwShTradePokemonIvs(-1, -1, -1, -1, -1, -1),
                0,
                [0, 0, 0, 0]),
        ]).Write();
    }

    private static byte[] CreateTextTable(int highestIndex, params (int index, string value)[] entries)
    {
        var lines = Enumerable.Range(0, highestIndex + 1)
            .Select(_ => new SwShGameTextLine(string.Empty, Flags: 0))
            .ToArray();

        foreach (var (index, value) in entries)
        {
            lines[index] = new SwShGameTextLine(value, Flags: 0);
        }

        return SwShGameTextFile.Write(lines);
    }
}
