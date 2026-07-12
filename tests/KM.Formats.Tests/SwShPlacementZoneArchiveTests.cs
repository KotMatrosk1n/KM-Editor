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

    [Fact]
    public void ParsesAdvancedTipBoundsDefaultAsEditableField()
    {
        var archive = SwShPlacementZoneArchive.Parse(CreateAdvancedTipArchive());

        var advancedTip = Assert.Single(archive.Zones[0].RawObjects, rawObject => rawObject.ObjectType == "AdvancedTip");
        var field = advancedTip.Fields.Single(field =>
            field.Field == "raw.AdvancedTip.Field_00.Field_00.Field_11.Field_00");

        Assert.Equal("2", field.Value);
        Assert.Equal("Bounds A Type", field.Label);
        Assert.Equal("Bounds / Ranges", field.Group);
        Assert.False(field.IsReadOnly);
        Assert.True(field.CanRewriteTable);
        Assert.Contains("FlatBuffer default is 2", field.Description);
    }

    [Fact]
    public void WriteEditsMaterializesOmittedAdvancedTipBoundsDefault()
    {
        var data = CreateAdvancedTipArchive();
        var archive = SwShPlacementZoneArchive.Parse(data);
        var advancedTip = Assert.Single(archive.Zones[0].RawObjects, rawObject => rawObject.ObjectType == "AdvancedTip");
        var field = advancedTip.Fields.Single(field =>
            field.Field == "raw.AdvancedTip.Field_00.Field_00.Field_11.Field_00");

        var output = archive.WriteEdits(
            [],
            [new SwShPlacementRawFieldEdit(0, "AdvancedTip", 0, field.Field, "5")]);

        Assert.True(output.Length > data.Length);
        var parsed = SwShPlacementZoneArchive.Parse(output);
        var outputTip = Assert.Single(parsed.Zones[0].RawObjects, rawObject => rawObject.ObjectType == "AdvancedTip");
        var outputField = outputTip.Fields.Single(candidate => candidate.Field == field.Field);
        Assert.Equal("5", outputField.Value);
        Assert.False(outputField.IsReadOnly);
        Assert.True(outputField.ValueOffset > 0);
    }

    [Fact]
    public void ParseKeepsStoredZeroHashDistinctFromEmptyFnvHashAndReadOnly()
    {
        var archive = SwShPlacementZoneArchive.Parse(CreateArchive().Write());

        var fieldItem = Assert.Single(archive.Zones[0].RawObjects, rawObject =>
            rawObject.ObjectType == "FieldItem");
        var zeroHash = fieldItem.Fields.Single(field =>
            field.Field.EndsWith(".Hash_01", StringComparison.Ordinal));

        Assert.Equal("0x0000000000000000", zeroHash.Value);
        Assert.Equal("0x0000000000000000", zeroHash.DisplayValue);
        Assert.True(zeroHash.IsReadOnly);
        Assert.Contains("structural", zeroHash.Description, StringComparison.OrdinalIgnoreCase);

        var reparsed = SwShPlacementZoneArchive.Parse(archive.WriteEdits([], []));
        var reparsedFieldItem = Assert.Single(reparsed.Zones[0].RawObjects, rawObject =>
            rawObject.ObjectType == "FieldItem");
        var reparsedZeroHash = reparsedFieldItem.Fields.Single(field =>
            field.Field.EndsWith(".Hash_01", StringComparison.Ordinal));
        Assert.Equal("0x0000000000000000", reparsedZeroHash.Value);
    }

    [Fact]
    public void ParseFormatsRawFloatWithRoundTripPrecision()
    {
        const float storedX = 10.1234567f;
        var archive = SwShPlacementZoneArchive.Parse(CreateArchive(storedX).Write());

        var fieldItem = Assert.Single(archive.Zones[0].RawObjects, rawObject =>
            rawObject.ObjectType == "FieldItem");
        var x = fieldItem.Fields.Single(field =>
            field.Group == "Transform" && field.Label == "X");
        var rotationY = fieldItem.Fields.Single(field =>
            field.Group == "Transform" && field.Label == "Rotation Y");

        Assert.Equal(storedX.ToString("G9", System.Globalization.CultureInfo.InvariantCulture), x.Value);
        Assert.Equal(-3_600, rotationY.MinimumValue);
        Assert.Equal(3_600, rotationY.MaximumValue);

        var negativeZero = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        var zeroArchive = SwShPlacementZoneArchive.Parse(CreateArchive(negativeZero).Write());
        var zeroFieldItem = Assert.Single(zeroArchive.Zones[0].RawObjects, rawObject =>
            rawObject.ObjectType == "FieldItem");
        var zeroX = zeroFieldItem.Fields.Single(field =>
            field.Group == "Transform" && field.Label == "X");
        Assert.Equal("0", zeroX.Value);
    }

    [Fact]
    public void ParseKeepsObjectHashPairedWithFirstTransform()
    {
        const ulong firstObjectHash = 0x1111222233334444;
        const ulong secondObjectHash = 0xAAAABBBBCCCCDDDD;
        var archive = SwShPlacementZoneArchive.Parse(
            new MultiTransformCritterWriter(firstObjectHash, secondObjectHash).Write());

        var critter = Assert.Single(archive.Zones[0].RawObjects, rawObject =>
            rawObject.ObjectType == "Critter");

        Assert.Equal(1, critter.Transform.X);
        Assert.Equal(firstObjectHash, critter.ObjectHash);
        Assert.True(critter.Fields.Single(field => field.Label == "Species").IsReadOnly);
        Assert.Contains(
            critter.Fields,
            field => field.Field.Contains(".Field_01.", StringComparison.Ordinal)
                && field.Field.EndsWith(".HashObjectName", StringComparison.Ordinal)
                && field.Value == "0xAAAABBBBCCCCDDDD");
    }

    private static SwShPlacementZoneArchive CreateArchive(float fieldItemX = 10.5f)
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
                        Transform: new SwShPlacementTransform(fieldItemX, 0, -4.25f, 90),
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

    private static byte[] CreateAdvancedTipArchive()
    {
        var writer = new AdvancedTipPlacementWriter();
        return writer.Write();
    }

    private sealed class AdvancedTipPlacementWriter
    {
        private readonly MemoryStream stream = new();
        private readonly BinaryWriter writer;

        public AdvancedTipPlacementWriter()
        {
            writer = new BinaryWriter(stream);
        }

        public byte[] Write()
        {
            writer.Write(0);
            var root = WriteTable(1, objectSize: 8, Offsets(1, (0, 4)));
            PatchUOffset(0, root);

            var zones = WriteTableVector(1);
            PatchUOffset(root + 4, zones);
            var zone = WriteZone();
            PatchUOffset(zones + sizeof(int), zone);

            return stream.ToArray();
        }

        private int WriteZone()
        {
            var zone = WriteTable(20, objectSize: 12, Offsets(20, (0, 4), (14, 8)));
            var meta = WriteZoneMeta();
            PatchUOffset(zone + 4, meta);
            var advancedTips = WriteTableVector(1);
            PatchUOffset(zone + 8, advancedTips);
            var advancedTip = WriteAdvancedTipHolder();
            PatchUOffset(advancedTips + sizeof(int), advancedTip);
            return zone;
        }

        private int WriteZoneMeta()
        {
            var meta = WriteTable(2, objectSize: 8, Offsets(2, (0, 4)));
            var transform = WriteTable(12, objectSize: 4, Offsets(12));
            PatchUOffset(meta + 4, transform);
            return meta;
        }

        private int WriteAdvancedTipHolder()
        {
            var holder = WriteTable(4, objectSize: 16, Offsets(4, (0, 4), (3, 8)));
            var advancedTip = WriteAdvancedTip();
            PatchUOffset(holder + 4, advancedTip);
            WriteUInt64(holder + 8, 0x123456789ABCDEF0);
            return holder;
        }

        private int WriteAdvancedTip()
        {
            var tip = WriteTable(1, objectSize: 8, Offsets(1, (0, 4)));
            var f14 = WriteF14();
            PatchUOffset(tip + 4, f14);
            return tip;
        }

        private int WriteF14()
        {
            var f14 = WriteTable(15, objectSize: 16, Offsets(15, (0, 4), (11, 8), (13, 12)));
            var transform = WriteTable(12, objectSize: 4, Offsets(12));
            PatchUOffset(f14 + 4, transform);
            var boundsA = WriteF14Bounds(20, 200, 90);
            PatchUOffset(f14 + 8, boundsA);
            var boundsB = WriteF14Bounds(80, 100, 125);
            PatchUOffset(f14 + 12, boundsB);
            return f14;
        }

        private int WriteF14Bounds(float field10, float field9, float field8)
        {
            var bounds = WriteTable(11, objectSize: 16, Offsets(11, (8, 12), (9, 8), (10, 4)));
            WriteSingle(bounds + 4, field10);
            WriteSingle(bounds + 8, field9);
            WriteSingle(bounds + 12, field8);
            return bounds;
        }

        private int WriteTableVector(int count)
        {
            var offset = checked((int)stream.Position);
            writer.Write(count);
            for (var index = 0; index < count; index++)
            {
                writer.Write(0);
            }

            return offset;
        }

        private int WriteTable(int fieldCount, ushort objectSize, IReadOnlyList<ushort> fieldOffsets)
        {
            var vtableStart = checked((int)stream.Position);
            writer.Write((ushort)(sizeof(ushort) * (2 + fieldCount)));
            writer.Write(objectSize);
            for (var index = 0; index < fieldCount; index++)
            {
                writer.Write(index < fieldOffsets.Count ? fieldOffsets[index] : (ushort)0);
            }

            var tableStart = checked((int)stream.Position);
            writer.Write(tableStart - vtableStart);
            for (var index = sizeof(int); index < objectSize; index++)
            {
                writer.Write((byte)0);
            }

            return tableStart;
        }

        private static ushort[] Offsets(int fieldCount, params (int FieldIndex, ushort Offset)[] values)
        {
            var offsets = new ushort[fieldCount];
            foreach (var value in values)
            {
                offsets[value.FieldIndex] = value.Offset;
            }

            return offsets;
        }

        private void PatchUOffset(int sourceOffset, int targetOffset)
        {
            var position = writer.BaseStream.Position;
            writer.BaseStream.Position = sourceOffset;
            writer.Write((uint)(targetOffset - sourceOffset));
            writer.BaseStream.Position = position;
        }

        private void WriteUInt64(int offset, ulong value)
        {
            var position = writer.BaseStream.Position;
            writer.BaseStream.Position = offset;
            writer.Write(value);
            writer.BaseStream.Position = position;
        }

        private void WriteSingle(int offset, float value)
        {
            var position = writer.BaseStream.Position;
            writer.BaseStream.Position = offset;
            writer.Write(value);
            writer.BaseStream.Position = position;
        }
    }

    private sealed class MultiTransformCritterWriter
    {
        private readonly MemoryStream stream = new();
        private readonly BinaryWriter writer;
        private readonly ulong firstObjectHash;
        private readonly ulong secondObjectHash;

        public MultiTransformCritterWriter(ulong firstObjectHash, ulong secondObjectHash)
        {
            writer = new BinaryWriter(stream);
            this.firstObjectHash = firstObjectHash;
            this.secondObjectHash = secondObjectHash;
        }

        public byte[] Write()
        {
            writer.Write(0);
            var root = WriteTable(1, objectSize: 8, Offsets(1, (0, 4)));
            PatchUOffset(0, root);

            var zones = WriteTableVector(1);
            PatchUOffset(root + 4, zones);
            var zone = WriteZone();
            PatchUOffset(zones + sizeof(int), zone);
            return stream.ToArray();
        }

        private int WriteZone()
        {
            var zone = WriteTable(28, objectSize: 12, Offsets(28, (0, 4), (2, 8)));
            var meta = WriteZoneMeta();
            PatchUOffset(zone + 4, meta);
            var critters = WriteTableVector(1);
            PatchUOffset(zone + 8, critters);
            var critter = WriteCritter();
            PatchUOffset(critters + sizeof(int), critter);
            return zone;
        }

        private int WriteZoneMeta()
        {
            var meta = WriteTable(2, objectSize: 16, Offsets(2, (0, 4), (1, 8)));
            var transform = WriteTransform(0, objectHash: 0);
            PatchUOffset(meta + 4, transform);
            WriteUInt64(meta + 8, 0x0102030405060708);
            return meta;
        }

        private int WriteCritter()
        {
            var holder = WriteTable(16, objectSize: 16, Offsets(16, (0, 4), (1, 8), (2, 12)));
            var first = WriteFirstCritterTransform();
            PatchUOffset(holder + 4, first);
            var second = WriteSecondCritterWrapper();
            PatchUOffset(holder + 8, second);
            WriteUInt32(holder + 12, 25);
            return holder;
        }

        private int WriteFirstCritterTransform()
        {
            var first = WriteTable(13, objectSize: 8, Offsets(13, (0, 4)));
            var transform = WriteTransform(1, firstObjectHash);
            PatchUOffset(first + 4, transform);
            return first;
        }

        private int WriteSecondCritterWrapper()
        {
            var wrapper = WriteTable(1, objectSize: 8, Offsets(1, (0, 4)));
            var inner = WriteTable(8, objectSize: 8, Offsets(8, (0, 4)));
            PatchUOffset(wrapper + 4, inner);
            var transform = WriteTransform(2, secondObjectHash);
            PatchUOffset(inner + 4, transform);
            return wrapper;
        }

        private int WriteTransform(float x, ulong objectHash)
        {
            var transform = WriteTable(12, objectSize: 16, Offsets(12, (0, 4), (9, 8)));
            WriteSingle(transform + 4, x);
            WriteUInt64(transform + 8, objectHash);
            return transform;
        }

        private int WriteTableVector(int count)
        {
            var offset = checked((int)stream.Position);
            writer.Write(count);
            for (var index = 0; index < count; index++)
            {
                writer.Write(0);
            }

            return offset;
        }

        private int WriteTable(int fieldCount, ushort objectSize, IReadOnlyList<ushort> fieldOffsets)
        {
            var vtableStart = checked((int)stream.Position);
            writer.Write((ushort)(sizeof(ushort) * (2 + fieldCount)));
            writer.Write(objectSize);
            for (var index = 0; index < fieldCount; index++)
            {
                writer.Write(index < fieldOffsets.Count ? fieldOffsets[index] : (ushort)0);
            }

            var tableStart = checked((int)stream.Position);
            writer.Write(tableStart - vtableStart);
            for (var index = sizeof(int); index < objectSize; index++)
            {
                writer.Write((byte)0);
            }

            return tableStart;
        }

        private static ushort[] Offsets(int fieldCount, params (int FieldIndex, ushort Offset)[] values)
        {
            var offsets = new ushort[fieldCount];
            foreach (var value in values)
            {
                offsets[value.FieldIndex] = value.Offset;
            }

            return offsets;
        }

        private void PatchUOffset(int sourceOffset, int targetOffset)
        {
            var position = writer.BaseStream.Position;
            writer.BaseStream.Position = sourceOffset;
            writer.Write((uint)(targetOffset - sourceOffset));
            writer.BaseStream.Position = position;
        }

        private void WriteUInt64(int offset, ulong value)
        {
            var position = writer.BaseStream.Position;
            writer.BaseStream.Position = offset;
            writer.Write(value);
            writer.BaseStream.Position = position;
        }

        private void WriteUInt32(int offset, uint value)
        {
            var position = writer.BaseStream.Position;
            writer.BaseStream.Position = offset;
            writer.Write(value);
            writer.BaseStream.Position = position;
        }

        private void WriteSingle(int offset, float value)
        {
            var position = writer.BaseStream.Position;
            writer.BaseStream.Position = offset;
            writer.Write(value);
            writer.BaseStream.Position = position;
        }
    }
}
