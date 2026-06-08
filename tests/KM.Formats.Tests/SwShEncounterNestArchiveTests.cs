// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShEncounterNestArchiveTests
{
    [Fact]
    public void WriteRoundTripsEncounterNestTables()
    {
        var archive = CreateArchive();

        var parsed = SwShEncounterNestArchive.Parse(archive.Write());

        var table = Assert.Single(parsed.Tables);
        Assert.Equal(0xAABBCCDD00112233UL, table.TableId);
        Assert.Equal(1, table.GameVersion);
        Assert.Collection(
            table.Entries,
            entry =>
            {
                Assert.Equal(0, entry.EntryIndex);
                Assert.Equal(1, entry.Species);
                Assert.Equal(2, entry.Form);
                Assert.Equal(0x1122334455667788UL, entry.LevelTableId);
                Assert.Equal(3, entry.Ability);
                Assert.True(entry.IsGigantamax);
                Assert.Equal(0x8877665544332211UL, entry.DropTableId);
                Assert.Equal(0x0102030405060708UL, entry.BonusTableId);
                Assert.Equal([100u, 20u, 30u, 40u, 50u], entry.Probabilities);
                Assert.Equal(2, entry.Gender);
                Assert.Equal(4, entry.FlawlessIvs);
            },
            entry =>
            {
                Assert.Equal(1, entry.EntryIndex);
                Assert.Equal(25, entry.Species);
                Assert.Equal(0, entry.Form);
                Assert.Equal(0x2233445566778899UL, entry.LevelTableId);
                Assert.Equal(4, entry.Ability);
                Assert.False(entry.IsGigantamax);
                Assert.Equal(0x9988776655443322UL, entry.DropTableId);
                Assert.Equal(0x0807060504030201UL, entry.BonusTableId);
                Assert.Equal([5u, 10u, 15u, 20u, 25u], entry.Probabilities);
                Assert.Equal(1, entry.Gender);
                Assert.Equal(2, entry.FlawlessIvs);
            });
    }

    [Fact]
    public void WriteEditsUpdatesStableFieldsAndProbabilities()
    {
        var archive = CreateArchive();

        var output = archive.WriteEdits(
        [
            new SwShEncounterNestEdit(0, 1, SwShEncounterNestField.Species, 133),
            new SwShEncounterNestEdit(0, 1, SwShEncounterNestField.Form, 7),
            new SwShEncounterNestEdit(0, 1, SwShEncounterNestField.Ability, 2),
            new SwShEncounterNestEdit(0, 1, SwShEncounterNestField.IsGigantamax, 1),
            new SwShEncounterNestEdit(0, 1, SwShEncounterNestField.Gender, 3),
            new SwShEncounterNestEdit(0, 1, SwShEncounterNestField.FlawlessIvs, 6),
            new SwShEncounterNestEdit(0, 1, SwShEncounterNestField.Star5Probability, 80),
        ]);

        var entry = SwShEncounterNestArchive.Parse(output).Tables[0].Entries[1];
        Assert.Equal(133, entry.Species);
        Assert.Equal(7, entry.Form);
        Assert.Equal(2, entry.Ability);
        Assert.True(entry.IsGigantamax);
        Assert.Equal(3, entry.Gender);
        Assert.Equal(6, entry.FlawlessIvs);
        Assert.Equal(80u, entry.Probabilities[4]);
    }

    [Fact]
    public void WriteEditsRejectsInvalidProbability()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits(
            [
                new SwShEncounterNestEdit(0, 0, SwShEncounterNestField.Star1Probability, 101),
            ]));
    }

    [Fact]
    public void WriteEditsRejectsInvalidFlawlessIvCount()
    {
        var archive = CreateArchive();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => archive.WriteEdits(
            [
                new SwShEncounterNestEdit(0, 0, SwShEncounterNestField.FlawlessIvs, 7),
            ]));
    }

    private static SwShEncounterNestArchive CreateArchive()
    {
        return new SwShEncounterNestArchive(
        [
            new SwShEncounterNestTable(
                0xAABBCCDD00112233,
                1,
                [
                    new SwShEncounterNest(
                        0,
                        1,
                        2,
                        0x1122334455667788,
                        3,
                        true,
                        0x8877665544332211,
                        0x0102030405060708,
                        [100, 20, 30, 40, 50],
                        2,
                        4),
                    new SwShEncounterNest(
                        1,
                        25,
                        0,
                        0x2233445566778899,
                        4,
                        false,
                        0x9988776655443322,
                        0x0807060504030201,
                        [5, 10, 15, 20, 25],
                        1,
                        2),
                ]),
        ]);
    }
}
