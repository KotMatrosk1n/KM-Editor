// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShRentalPokemonArchiveTests
{
    [Fact]
    public void WriteRoundTripsRentalPokemonRecords()
    {
        var archive = CreateArchive();

        var reparsed = SwShRentalPokemonArchive.Parse(archive.Write());

        var rental = Assert.Single(reparsed.Rentals);
        Assert.Equal(0, rental.Index);
        Assert.Equal(3, rental.Form);
        Assert.Equal(4, rental.BallItemId);
        Assert.Equal(0x1122334455667788UL, rental.Hash1);
        Assert.Equal(25, rental.HeldItem);
        Assert.Equal(50, rental.Level);
        Assert.Equal(133, rental.Species);
        Assert.Equal(0x8877665544332211UL, rental.Hash2);
        Assert.Equal(12345u, rental.TrainerId);
        Assert.Equal(13, rental.Nature);
        Assert.Equal(2, rental.Gender);
        Assert.Equal(1, rental.Ability);
        Assert.Equal([33, 45, 98, 129], rental.Moves);
        Assert.Equal(new SwShRentalPokemonStats(10, 20, 30, 40, 50, 60), rental.Evs);
        Assert.Equal(new SwShRentalPokemonStats(1, 2, 3, 4, 5, 6), rental.Ivs);
    }

    [Fact]
    public void WriteEditsUpdatesStableFieldsAndPreservesHashes()
    {
        var archive = CreateArchive();

        var updated = SwShRentalPokemonArchive.Parse(archive.WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Species, 25),
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.Move2, 85),
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.EvHp, 252),
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.IvSpecialAttack, 31),
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.FixedIvPreset, 31),
        ]));

        var rental = Assert.Single(updated.Rentals);
        Assert.Equal(25, rental.Species);
        Assert.Equal([33, 45, 85, 129], rental.Moves);
        Assert.Equal(252, rental.Evs.HP);
        Assert.Equal(new SwShRentalPokemonStats(31, 31, 31, 31, 31, 31), rental.Ivs);
        Assert.Equal(0x1122334455667788UL, rental.Hash1);
        Assert.Equal(0x8877665544332211UL, rental.Hash2);
    }

    [Fact]
    public void WriteEditsRejectsUnsupportedIvValue()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(() => archive.WriteEdits(
        [
            new SwShRentalPokemonEdit(0, SwShRentalPokemonField.IvHp, -1),
        ]));
    }

    private static SwShRentalPokemonArchive CreateArchive()
    {
        return new SwShRentalPokemonArchive(
        [
            new SwShRentalPokemonRecord(
                0,
                new SwShRentalPokemonStats(10, 20, 30, 40, 50, 60),
                3,
                4,
                0x1122334455667788UL,
                25,
                50,
                133,
                0x8877665544332211UL,
                12345,
                13,
                2,
                new SwShRentalPokemonStats(1, 2, 3, 4, 5, 6),
                1,
                [33, 45, 98, 129]),
        ]);
    }
}
