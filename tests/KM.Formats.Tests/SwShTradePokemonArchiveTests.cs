// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShTradePokemonArchiveTests
{
    [Fact]
    public void WriteRoundTripsTradePokemonRecords()
    {
        var archive = CreateArchive();

        var parsed = SwShTradePokemonArchive.Parse(archive.Write());

        Assert.Collection(
            parsed.Trades,
            trade =>
            {
                Assert.Equal(0, trade.Index);
                Assert.Equal(1, trade.Form);
                Assert.Equal(10, trade.DynamaxLevel);
                Assert.Equal(851, trade.BallItemId);
                Assert.Equal(4, trade.Field03);
                Assert.Equal(0x1122334455667788UL, trade.Hash0);
                Assert.True(trade.CanGigantamax);
                Assert.Equal(234, trade.HeldItem);
                Assert.Equal(50, trade.Level);
                Assert.Equal(810, trade.Species);
                Assert.Equal(0x8877665544332211UL, trade.Hash1);
                Assert.Equal(123456, trade.TrainerId);
                Assert.Equal(11, trade.MemoryCode);
                Assert.Equal(0x1234, trade.MemoryTextVariable);
                Assert.Equal(12, trade.MemoryFeel);
                Assert.Equal(13, trade.MemoryIntensity);
                Assert.Equal(0x0102030405060708UL, trade.Hash2);
                Assert.Equal(1, trade.OtGender);
                Assert.Equal(2, trade.RequiredForm);
                Assert.Equal(25, trade.RequiredSpecies);
                Assert.Equal(24, trade.RequiredNature);
                Assert.Equal(3, trade.UnknownRequirement);
                Assert.Equal(2, trade.ShinyLock);
                Assert.Equal(25, trade.Nature);
                Assert.Equal(1, trade.Gender);
                Assert.Equal(new SwShTradePokemonIvs(31, 30, 29, 28, 27, 26), trade.Ivs);
                Assert.Equal(2, trade.Ability);
                Assert.Equal([344, 345, 346, 347], trade.RelearnMoves);
            },
            trade =>
            {
                Assert.Equal(1, trade.Index);
                Assert.Equal(0, trade.Form);
                Assert.Equal(0, trade.DynamaxLevel);
                Assert.Equal(4, trade.BallItemId);
                Assert.False(trade.CanGigantamax);
                Assert.Equal(1, trade.Level);
                Assert.Equal(133, trade.Species);
                Assert.Equal(new SwShTradePokemonIvs(-1, -1, -1, -1, -1, -1), trade.Ivs);
                Assert.Equal([0, 0, 0, 0], trade.RelearnMoves);
            });
    }

    [Fact]
    public void WriteEditsUpdatesTradeFieldsAndFixedIvs()
    {
        var archive = CreateArchive();

        var output = archive.WriteEdits(
        [
            new SwShTradePokemonEdit(0, SwShTradePokemonField.Species, 25),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.Form, 2),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.Level, 15),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.BallItemId, 4),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.HeldItem, 99),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.RequiredSpecies, 52),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.RequiredForm, 1),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.RequiredNature, 25),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.MemoryTextVariable, 77),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.RelearnMove2, 400),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.CanGigantamax, 0),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.IvHp, 0),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.IvAttack, 1),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.IvDefense, 2),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.IvSpeed, 3),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.IvSpecialAttack, 4),
            new SwShTradePokemonEdit(0, SwShTradePokemonField.IvSpecialDefense, 5),
        ]);

        var trade = SwShTradePokemonArchive.Parse(output).Trades[0];
        Assert.Equal(25, trade.Species);
        Assert.Equal(2, trade.Form);
        Assert.Equal(15, trade.Level);
        Assert.Equal(4, trade.BallItemId);
        Assert.Equal(99, trade.HeldItem);
        Assert.Equal(52, trade.RequiredSpecies);
        Assert.Equal(1, trade.RequiredForm);
        Assert.Equal(25, trade.RequiredNature);
        Assert.Equal(77, trade.MemoryTextVariable);
        Assert.Equal(400, trade.RelearnMoves[2]);
        Assert.False(trade.CanGigantamax);
        Assert.Equal(new SwShTradePokemonIvs(0, 1, 2, 3, 4, 5), trade.Ivs);
    }

    [Fact]
    public void FlawlessIvCountPresetsRoundTripSentinels()
    {
        var archive = CreateArchive();

        var threePerfectOutput = archive.WriteEdits(
        [
            new SwShTradePokemonEdit(1, SwShTradePokemonField.FlawlessIvCount, 3),
        ]);
        var threePerfectTrade = SwShTradePokemonArchive.Parse(threePerfectOutput).Trades[1];
        Assert.Equal(3, SwShTradePokemonArchive.GetFlawlessIvCount(threePerfectTrade.Ivs));
        Assert.Equal(new SwShTradePokemonIvs(-4, -1, -1, -1, -1, -1), threePerfectTrade.Ivs);

        var sixPerfectOutput = SwShTradePokemonArchive.Parse(threePerfectOutput).WriteEdits(
        [
            new SwShTradePokemonEdit(1, SwShTradePokemonField.FlawlessIvCount, 6),
        ]);
        var sixPerfectTrade = SwShTradePokemonArchive.Parse(sixPerfectOutput).Trades[1];
        Assert.Equal(6, SwShTradePokemonArchive.GetFlawlessIvCount(sixPerfectTrade.Ivs));
        Assert.Equal(new SwShTradePokemonIvs(31, 31, 31, 31, 31, 31), sixPerfectTrade.Ivs);

        var randomOutput = SwShTradePokemonArchive.Parse(sixPerfectOutput).WriteEdits(
        [
            new SwShTradePokemonEdit(1, SwShTradePokemonField.FlawlessIvCount, 0),
        ]);
        var randomTrade = SwShTradePokemonArchive.Parse(randomOutput).Trades[1];
        Assert.Equal(0, SwShTradePokemonArchive.GetFlawlessIvCount(randomTrade.Ivs));
        Assert.Equal(new SwShTradePokemonIvs(-1, -1, -1, -1, -1, -1), randomTrade.Ivs);
    }

    [Fact]
    public void WriteEditsRejectsInvalidIvValues()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits([new SwShTradePokemonEdit(0, SwShTradePokemonField.IvAttack, -4)]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits([new SwShTradePokemonEdit(0, SwShTradePokemonField.IvHp, 32)]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits([new SwShTradePokemonEdit(0, SwShTradePokemonField.FlawlessIvCount, 5)]));
    }

    private static SwShTradePokemonArchive CreateArchive()
    {
        return new SwShTradePokemonArchive(
        [
            new SwShTradePokemonRecord(
                0,
                1,
                10,
                851,
                4,
                0x1122334455667788,
                true,
                234,
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
                3,
                2,
                25,
                1,
                new SwShTradePokemonIvs(31, 30, 29, 28, 27, 26),
                2,
                [344, 345, 346, 347]),
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
        ]);
    }
}
