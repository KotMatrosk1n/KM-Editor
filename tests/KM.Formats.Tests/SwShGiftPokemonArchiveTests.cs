// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShGiftPokemonArchiveTests
{
    [Fact]
    public void WriteRoundTripsGiftPokemonRecords()
    {
        var archive = CreateArchive();

        var parsed = SwShGiftPokemonArchive.Parse(archive.Write());

        Assert.Collection(
            parsed.Gifts,
            gift =>
            {
                Assert.Equal(0, gift.Index);
                Assert.Equal(0, gift.IsEgg);
                Assert.Equal(1, gift.Form);
                Assert.Equal(10, gift.DynamaxLevel);
                Assert.Equal(851, gift.BallItemId);
                Assert.Equal(4, gift.Field04);
                Assert.Equal(0x1122334455667788UL, gift.Hash1);
                Assert.True(gift.CanGigantamax);
                Assert.Equal(234, gift.HeldItem);
                Assert.Equal(50, gift.Level);
                Assert.Equal(810, gift.Species);
                Assert.Equal(10, gift.Field0A);
                Assert.Equal(11, gift.MemoryCode);
                Assert.Equal(0x1234, gift.MemoryData);
                Assert.Equal(12, gift.MemoryFeel);
                Assert.Equal(13, gift.MemoryLevel);
                Assert.Equal(0x8877665544332211UL, gift.OtNameId);
                Assert.Equal(1, gift.OtGender);
                Assert.Equal(2, gift.ShinyLock);
                Assert.Equal(25, gift.Nature);
                Assert.Equal(3, gift.Gender);
                Assert.Equal(new SwShGiftPokemonIvs(31, 30, 29, 28, 27, 26), gift.Ivs);
                Assert.Equal(2, gift.Ability);
                Assert.Equal(344, gift.SpecialMove);
            },
            gift =>
            {
                Assert.Equal(1, gift.Index);
                Assert.Equal(1, gift.IsEgg);
                Assert.Equal(0, gift.Form);
                Assert.Equal(0, gift.DynamaxLevel);
                Assert.Equal(4, gift.BallItemId);
                Assert.False(gift.CanGigantamax);
                Assert.Equal(1, gift.Level);
                Assert.Equal(133, gift.Species);
                Assert.Equal(new SwShGiftPokemonIvs(-1, -1, -1, -1, -1, -1), gift.Ivs);
            });
    }

    [Fact]
    public void WriteEditsUpdatesGiftFieldsAndFixedIvs()
    {
        var archive = CreateArchive();

        var output = archive.WriteEdits(
        [
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.Species, 25),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.Form, 2),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.Level, 15),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.BallItemId, 4),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.HeldItem, 99),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.CanGigantamax, 0),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvHp, 0),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvAttack, 1),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvDefense, 2),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvSpeed, 3),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvSpecialAttack, 4),
            new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvSpecialDefense, 5),
        ]);

        var gift = SwShGiftPokemonArchive.Parse(output).Gifts[0];
        Assert.Equal(25, gift.Species);
        Assert.Equal(2, gift.Form);
        Assert.Equal(15, gift.Level);
        Assert.Equal(4, gift.BallItemId);
        Assert.Equal(99, gift.HeldItem);
        Assert.False(gift.CanGigantamax);
        Assert.Equal(new SwShGiftPokemonIvs(0, 1, 2, 3, 4, 5), gift.Ivs);
    }

    [Fact]
    public void FlawlessIvCountPresetsRoundTripSentinels()
    {
        var archive = CreateArchive();

        var threePerfectOutput = archive.WriteEdits(
        [
            new SwShGiftPokemonEdit(1, SwShGiftPokemonField.FlawlessIvCount, 3),
        ]);
        var threePerfectGift = SwShGiftPokemonArchive.Parse(threePerfectOutput).Gifts[1];
        Assert.Equal(3, SwShGiftPokemonArchive.GetFlawlessIvCount(threePerfectGift.Ivs));
        Assert.Equal(new SwShGiftPokemonIvs(-4, -1, -1, -1, -1, -1), threePerfectGift.Ivs);

        var sixPerfectOutput = SwShGiftPokemonArchive.Parse(threePerfectOutput).WriteEdits(
        [
            new SwShGiftPokemonEdit(1, SwShGiftPokemonField.FlawlessIvCount, 6),
        ]);
        var sixPerfectGift = SwShGiftPokemonArchive.Parse(sixPerfectOutput).Gifts[1];
        Assert.Equal(6, SwShGiftPokemonArchive.GetFlawlessIvCount(sixPerfectGift.Ivs));
        Assert.Equal(new SwShGiftPokemonIvs(31, 31, 31, 31, 31, 31), sixPerfectGift.Ivs);

        var randomOutput = SwShGiftPokemonArchive.Parse(sixPerfectOutput).WriteEdits(
        [
            new SwShGiftPokemonEdit(1, SwShGiftPokemonField.FlawlessIvCount, 0),
        ]);
        var randomGift = SwShGiftPokemonArchive.Parse(randomOutput).Gifts[1];
        Assert.Equal(0, SwShGiftPokemonArchive.GetFlawlessIvCount(randomGift.Ivs));
        Assert.Equal(new SwShGiftPokemonIvs(-1, -1, -1, -1, -1, -1), randomGift.Ivs);
    }

    [Fact]
    public void WriteEditsRejectsInvalidIvValues()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits([new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvAttack, -4)]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits([new SwShGiftPokemonEdit(0, SwShGiftPokemonField.IvHp, 32)]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits([new SwShGiftPokemonEdit(0, SwShGiftPokemonField.FlawlessIvCount, 5)]));
    }

    [Fact]
    public void WriteEditsRejectsInvalidDynamaxLevel()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits([new SwShGiftPokemonEdit(0, SwShGiftPokemonField.DynamaxLevel, 11)]));
    }

    private static SwShGiftPokemonArchive CreateArchive()
    {
        return new SwShGiftPokemonArchive(
        [
            new SwShGiftPokemonRecord(
                0,
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
                10,
                11,
                0x1234,
                12,
                13,
                0x8877665544332211,
                1,
                2,
                25,
                3,
                new SwShGiftPokemonIvs(31, 30, 29, 28, 27, 26),
                2,
                344),
            new SwShGiftPokemonRecord(
                1,
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
                new SwShGiftPokemonIvs(-1, -1, -1, -1, -1, -1),
                0,
                0),
        ]);
    }
}
