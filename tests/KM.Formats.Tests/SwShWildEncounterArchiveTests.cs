// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShWildEncounterArchiveTests
{
    [Fact]
    public void WriteRoundTripsEncounterTables()
    {
        var archive = CreateArchive();

        var parsed = SwShWildEncounterArchive.Parse(archive.Write());

        Assert.Equal(7u, parsed.Field00);
        var table = Assert.Single(parsed.Tables);
        Assert.Equal(0x1122334455667788UL, table.ZoneId);
        var subTable = Assert.Single(table.SubTables);
        Assert.Equal(3, subTable.LevelMin);
        Assert.Equal(8, subTable.LevelMax);
        Assert.Collection(
            subTable.Slots,
            slot =>
            {
                Assert.Equal(35, slot.Probability);
                Assert.Equal(810, slot.Species);
                Assert.Equal(0, slot.Form);
            },
            slot =>
            {
                Assert.Equal(15, slot.Probability);
                Assert.Equal(821, slot.Species);
                Assert.Equal(1, slot.Form);
            });
    }

    [Fact]
    public void WriteEditsUpdatesSlotAndSubTableValues()
    {
        var archive = CreateArchive();

        var output = archive.WriteEdits(
        [
            new SwShWildEncounterEdit(0, 0, 1, SwShWildEncounterField.SpeciesId, 25),
            new SwShWildEncounterEdit(0, 0, 1, SwShWildEncounterField.Form, 2),
            new SwShWildEncounterEdit(0, 0, 1, SwShWildEncounterField.Probability, 40),
            new SwShWildEncounterEdit(0, 0, null, SwShWildEncounterField.LevelMin, 5),
            new SwShWildEncounterEdit(0, 0, null, SwShWildEncounterField.LevelMax, 9),
        ]);

        var parsed = SwShWildEncounterArchive.Parse(output);
        var subTable = parsed.Tables[0].SubTables[0];
        Assert.Equal(5, subTable.LevelMin);
        Assert.Equal(9, subTable.LevelMax);
        Assert.Equal(25, subTable.Slots[1].Species);
        Assert.Equal(2, subTable.Slots[1].Form);
        Assert.Equal(40, subTable.Slots[1].Probability);
    }

    private static SwShWildEncounterArchive CreateArchive()
    {
        return new SwShWildEncounterArchive(
            7,
            [
                new SwShWildEncounterTable(
                    0x1122334455667788,
                    [
                        new SwShWildEncounterSubTable(
                            3,
                            8,
                            [
                                new SwShWildEncounterSlot(35, 810, 0),
                                new SwShWildEncounterSlot(15, 821, 1),
                            ]),
                    ]),
            ]);
    }
}
