// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShPlacementZoneArchiveTests
{
    [Fact]
    public void WriteRoundTripsPlacementObjects()
    {
        var archive = CreateArchive();

        var parsed = SwShPlacementZoneArchive.Parse(
            archive.Write(),
            new Dictionary<ulong, int> { [0xAABBCCDD00112233] = 1 });

        var zone = Assert.Single(parsed.Zones);
        Assert.Equal(0x1122334455667788UL, zone.ZoneId);
        Assert.Equal(0x8877665544332211UL, zone.ObjectHash);
        var fieldItem = Assert.Single(zone.FieldItems);
        Assert.Equal("objects/visible_potion.gfbmdl", fieldItem.Model);
        Assert.Equal(10.5f, fieldItem.Transform.X);
        Assert.Equal(-4.25f, fieldItem.Transform.Z);
        Assert.Equal(90, fieldItem.Transform.RotationY);
        Assert.Equal([0xAABBCCDD00112233UL], fieldItem.ItemHashes);
        Assert.Equal(1, fieldItem.Quantity);
        var hiddenItem = Assert.Single(zone.HiddenItems);
        var chance = Assert.Single(hiddenItem.Chances);
        Assert.Equal(1, chance.ItemId);
        Assert.Equal(50, chance.Chance);
        Assert.Equal(2, chance.Quantity);
    }

    [Fact]
    public void WriteEditsPatchesCoordinatesAndHiddenItemChance()
    {
        var archive = SwShPlacementZoneArchive.Parse(
            CreateArchive().Write(),
            new Dictionary<ulong, int>
            {
                [0xAABBCCDD00112233] = 1,
                [0xAABBCCDD00112244] = 2,
            });

        var output = archive.WriteEdits(
        [
            new SwShPlacementObjectEdit(0, SwShPlacementObjectKind.FieldItem, 0, null, SwShPlacementEditableField.LocationX, 20),
            new SwShPlacementObjectEdit(0, SwShPlacementObjectKind.FieldItem, 0, null, SwShPlacementEditableField.Quantity, 4),
            new SwShPlacementObjectEdit(0, SwShPlacementObjectKind.HiddenItem, 0, 0, SwShPlacementEditableField.ItemId, 2, 0xAABBCCDD00112244),
            new SwShPlacementObjectEdit(0, SwShPlacementObjectKind.HiddenItem, 0, 0, SwShPlacementEditableField.Chance, 75),
            new SwShPlacementObjectEdit(0, SwShPlacementObjectKind.HiddenItem, 0, 0, SwShPlacementEditableField.Quantity, 6),
        ]);

        var parsed = SwShPlacementZoneArchive.Parse(
            output,
            new Dictionary<ulong, int>
            {
                [0xAABBCCDD00112233] = 1,
                [0xAABBCCDD00112244] = 2,
            });
        Assert.Equal(20, parsed.Zones[0].FieldItems[0].Transform.X);
        Assert.Equal(4, parsed.Zones[0].FieldItems[0].Quantity);
        Assert.Equal(2, parsed.Zones[0].HiddenItems[0].Chances[0].ItemId);
        Assert.Equal(75, parsed.Zones[0].HiddenItems[0].Chances[0].Chance);
        Assert.Equal(6, parsed.Zones[0].HiddenItems[0].Chances[0].Quantity);
    }

    private static SwShPlacementZoneArchive CreateArchive()
    {
        return new SwShPlacementZoneArchive(
        [
            new SwShPlacementZone(
                ZoneIndex: 0,
                ZoneId: 0x1122334455667788,
                ObjectHash: 0x8877665544332211,
                Transform: new SwShPlacementTransform(0, 0, 0, 0),
                FieldItems:
                [
                    new SwShPlacementFieldItem(
                        ObjectIndex: 0,
                        Model: "objects/visible_potion.gfbmdl",
                        Transform: new SwShPlacementTransform(10.5f, 0, -4.25f, 90),
                        ItemHashes: [0xAABBCCDD00112233],
                        ItemHashOffsets: [],
                        ItemIds: [],
                        ItemIdOffsets: [],
                        Quantity: 1,
                        QuantityOffset: 0,
                        TransformOffsets: new PlacementTransformOffsets(0, 0, 0, 0)),
                ],
                HiddenItems:
                [
                    new SwShPlacementHiddenItem(
                        ObjectIndex: 0,
                        Transform: new SwShPlacementTransform(12, 0, -5, 180),
                        Chances:
                        [
                            new SwShPlacementHiddenItemChance(
                                ChanceIndex: 0,
                                ItemHash: 0xAABBCCDD00112233,
                                ItemId: 1,
                                Chance: 50,
                                Quantity: 2,
                                ItemHashOffset: 0,
                                ChanceOffset: 0,
                                QuantityOffset: 0),
                        ],
                        TransformOffsets: new PlacementTransformOffsets(0, 0, 0, 0)),
                ]),
        ],
        Hash: 0x0123456789ABCDEF,
        Description: "sanitized placement test archive",
        SourceData: []);
    }
}
