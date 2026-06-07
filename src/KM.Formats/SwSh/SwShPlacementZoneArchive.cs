// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace KM.Formats.SwSh;

public sealed record SwShPlacementTransform(
    float X,
    float Y,
    float Z,
    float RotationY);

public enum SwShPlacementObjectKind
{
    FieldItem,
    HiddenItem,
}

public enum SwShPlacementEditableField
{
    LocationX,
    LocationY,
    LocationZ,
    RotationY,
    ItemId,
    Quantity,
    Chance,
}

public sealed record SwShPlacementObjectEdit(
    int ZoneIndex,
    SwShPlacementObjectKind ObjectKind,
    int ObjectIndex,
    int? ChanceIndex,
    SwShPlacementEditableField Field,
    double Value,
    ulong? HashValue = null);

public sealed record SwShPlacementHiddenItemChance(
    int ChanceIndex,
    ulong ItemHash,
    int? ItemId,
    int Chance,
    int Quantity,
    int ItemHashOffset,
    int ChanceOffset,
    int QuantityOffset);

public sealed record SwShPlacementFieldItem(
    int ObjectIndex,
    string Model,
    SwShPlacementTransform Transform,
    IReadOnlyList<ulong> ItemHashes,
    IReadOnlyList<int> ItemHashOffsets,
    IReadOnlyList<uint> ItemIds,
    IReadOnlyList<int> ItemIdOffsets,
    byte Quantity,
    int QuantityOffset,
    PlacementTransformOffsets TransformOffsets);

public sealed record SwShPlacementHiddenItem(
    int ObjectIndex,
    SwShPlacementTransform Transform,
    IReadOnlyList<SwShPlacementHiddenItemChance> Chances,
    PlacementTransformOffsets TransformOffsets);

public sealed record SwShPlacementZone(
    int ZoneIndex,
    ulong ZoneId,
    ulong ObjectHash,
    SwShPlacementTransform Transform,
    IReadOnlyList<SwShPlacementFieldItem> FieldItems,
    IReadOnlyList<SwShPlacementHiddenItem> HiddenItems);

public sealed record SwShPlacementZoneArchive(
    IReadOnlyList<SwShPlacementZone> Zones,
    ulong Hash,
    string Description,
    byte[] SourceData)
{
    public static SwShPlacementZoneArchive Parse(ReadOnlySpan<byte> data, IReadOnlyDictionary<ulong, int>? itemIdsByHash = null)
    {
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("Placement archive is too small to contain a FlatBuffer root.");
        }

        var rootTableOffset = ReadUOffset(data, offset: 0);
        var zoneVectorOffset = ReadTableUOffset(data, rootTableOffset, fieldIndex: 0, required: true);
        var zones = ReadZoneVector(data, zoneVectorOffset, itemIdsByHash);
        var hash = ReadTableUInt64(data, rootTableOffset, fieldIndex: 1, required: false);
        var descriptionOffset = ReadTableUOffset(data, rootTableOffset, fieldIndex: 2, required: false);
        var description = descriptionOffset == 0 ? string.Empty : ReadString(data, descriptionOffset);

        return new SwShPlacementZoneArchive(zones, hash, description, data.ToArray());
    }

    public byte[] Write()
    {
        var writer = new PlacementFlatBufferWriter();
        return writer.Write(this);
    }

    public byte[] WriteEdits(IEnumerable<SwShPlacementObjectEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var output = SourceData.ToArray();
        foreach (var edit in edits)
        {
            ApplyEdit(output, edit);
        }

        return output;
    }

    private void ApplyEdit(byte[] output, SwShPlacementObjectEdit edit)
    {
        if ((uint)edit.ZoneIndex >= (uint)Zones.Count)
        {
            throw new InvalidDataException($"Placement zone index {edit.ZoneIndex} is not present.");
        }

        var zone = Zones[edit.ZoneIndex];
        switch (edit.ObjectKind)
        {
            case SwShPlacementObjectKind.FieldItem:
                ApplyFieldItemEdit(output, zone, edit);
                break;
            case SwShPlacementObjectKind.HiddenItem:
                ApplyHiddenItemEdit(output, zone, edit);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(edit), $"Placement object kind '{edit.ObjectKind}' is not supported.");
        }
    }

    private static void ApplyFieldItemEdit(byte[] output, SwShPlacementZone zone, SwShPlacementObjectEdit edit)
    {
        if ((uint)edit.ObjectIndex >= (uint)zone.FieldItems.Count)
        {
            throw new InvalidDataException($"Placement field item index {edit.ObjectIndex} is not present.");
        }

        var item = zone.FieldItems[edit.ObjectIndex];
        switch (edit.Field)
        {
            case SwShPlacementEditableField.LocationX:
                WriteSingle(output, item.TransformOffsets.X, edit.Value);
                break;
            case SwShPlacementEditableField.LocationY:
                WriteSingle(output, item.TransformOffsets.Y, edit.Value);
                break;
            case SwShPlacementEditableField.LocationZ:
                WriteSingle(output, item.TransformOffsets.Z, edit.Value);
                break;
            case SwShPlacementEditableField.RotationY:
                WriteSingle(output, item.TransformOffsets.RotationY, edit.Value);
                break;
            case SwShPlacementEditableField.Quantity:
                WriteByte(output, item.QuantityOffset, edit.Value, minimum: 0, maximum: byte.MaxValue);
                break;
            case SwShPlacementEditableField.ItemId:
                if (item.ItemHashOffsets.Count > 0)
                {
                    if (edit.HashValue is null)
                    {
                        throw new InvalidDataException("Placement field item hash edits require an item hash lookup.");
                    }

                    WriteUInt64(output, item.ItemHashOffsets[0], edit.HashValue.Value);
                }
                else if (item.ItemIdOffsets.Count > 0)
                {
                    WriteUInt32(output, item.ItemIdOffsets[0], edit.Value);
                }
                else
                {
                    throw new InvalidDataException("Placement field item does not have an editable item vector entry.");
                }

                break;
            default:
                throw new InvalidDataException($"Placement field '{edit.Field}' is not supported for field items.");
        }
    }

    private static void ApplyHiddenItemEdit(byte[] output, SwShPlacementZone zone, SwShPlacementObjectEdit edit)
    {
        if ((uint)edit.ObjectIndex >= (uint)zone.HiddenItems.Count)
        {
            throw new InvalidDataException($"Placement hidden item index {edit.ObjectIndex} is not present.");
        }

        var item = zone.HiddenItems[edit.ObjectIndex];
        switch (edit.Field)
        {
            case SwShPlacementEditableField.LocationX:
                WriteSingle(output, item.TransformOffsets.X, edit.Value);
                break;
            case SwShPlacementEditableField.LocationY:
                WriteSingle(output, item.TransformOffsets.Y, edit.Value);
                break;
            case SwShPlacementEditableField.LocationZ:
                WriteSingle(output, item.TransformOffsets.Z, edit.Value);
                break;
            case SwShPlacementEditableField.RotationY:
                WriteSingle(output, item.TransformOffsets.RotationY, edit.Value);
                break;
            case SwShPlacementEditableField.ItemId:
            case SwShPlacementEditableField.Quantity:
            case SwShPlacementEditableField.Chance:
                var chanceIndex = edit.ChanceIndex ?? 0;
                if ((uint)chanceIndex >= (uint)item.Chances.Count)
                {
                    throw new InvalidDataException($"Placement hidden item chance index {chanceIndex} is not present.");
                }

                var chance = item.Chances[chanceIndex];
                if (edit.Field == SwShPlacementEditableField.ItemId)
                {
                    if (edit.HashValue is null)
                    {
                        throw new InvalidDataException("Placement hidden item hash edits require an item hash lookup.");
                    }

                    WriteUInt64(output, chance.ItemHashOffset, edit.HashValue.Value);
                }
                else if (edit.Field == SwShPlacementEditableField.Quantity)
                {
                    WriteInt32(output, chance.QuantityOffset, edit.Value, minimum: 0, maximum: 999);
                }
                else
                {
                    WriteInt32(output, chance.ChanceOffset, edit.Value, minimum: 0, maximum: 100);
                }

                break;
            default:
                throw new InvalidDataException($"Placement field '{edit.Field}' is not supported for hidden items.");
        }
    }

    private static SwShPlacementZone ReadZone(
        ReadOnlySpan<byte> data,
        int zoneOffset,
        int zoneIndex,
        IReadOnlyDictionary<ulong, int>? itemIdsByHash)
    {
        var metaOffset = ReadTableUOffset(data, zoneOffset, fieldIndex: 0, required: true);
        var zoneMetaTransformOffset = ReadTableUOffset(data, metaOffset, fieldIndex: 0, required: true);
        var zoneId = ReadTableUInt64(data, metaOffset, fieldIndex: 1, required: false);
        var transform = ReadTransform(data, zoneMetaTransformOffset);
        var objectHash = ReadTableUInt64(data, zoneMetaTransformOffset, fieldIndex: 9, required: false);

        var fieldItemsOffset = ReadTableUOffset(data, zoneOffset, fieldIndex: 6, required: false);
        var hiddenItemsOffset = ReadTableUOffset(data, zoneOffset, fieldIndex: 19, required: false);

        return new SwShPlacementZone(
            zoneIndex,
            zoneId,
            objectHash,
            transform.Transform,
            fieldItemsOffset == 0
                ? Array.Empty<SwShPlacementFieldItem>()
                : ReadFieldItemVector(data, fieldItemsOffset),
            hiddenItemsOffset == 0
                ? Array.Empty<SwShPlacementHiddenItem>()
                : ReadHiddenItemVector(data, hiddenItemsOffset, itemIdsByHash));
    }

    private static SwShPlacementFieldItem ReadFieldItem(ReadOnlySpan<byte> data, int holderOffset, int objectIndex)
    {
        var itemOffset = ReadTableUOffset(data, holderOffset, fieldIndex: 0, required: true);
        var transformOffset = ReadTableUOffset(data, itemOffset, fieldIndex: 0, required: true);
        var transform = ReadTransform(data, transformOffset);
        var modelOffset = ReadTableUOffset(data, itemOffset, fieldIndex: 2, required: false);
        var flagsOffset = ReadTableUOffset(data, itemOffset, fieldIndex: 6, required: false);
        var itemIdsOffset = ReadTableUOffset(data, itemOffset, fieldIndex: 7, required: false);
        var quantityOffset = ReadTableFieldAbsoluteOffset(data, itemOffset, fieldIndex: 8);

        IReadOnlyList<ulong> flags = Array.Empty<ulong>();
        IReadOnlyList<int> flagOffsets = Array.Empty<int>();
        if (flagsOffset != 0)
        {
            flags = ReadUInt64Vector(data, flagsOffset, out flagOffsets);
        }

        IReadOnlyList<uint> itemIds = Array.Empty<uint>();
        IReadOnlyList<int> itemIdOffsets = Array.Empty<int>();
        if (itemIdsOffset != 0)
        {
            itemIds = ReadUInt32Vector(data, itemIdsOffset, out itemIdOffsets);
        }

        return new SwShPlacementFieldItem(
            objectIndex,
            modelOffset == 0 ? string.Empty : ReadString(data, modelOffset),
            transform.Transform,
            flags,
            flagOffsets,
            itemIds,
            itemIdOffsets,
            quantityOffset == 0 ? (byte)0 : data[quantityOffset],
            quantityOffset,
            transform.Offsets);
    }

    private static SwShPlacementHiddenItem ReadHiddenItem(
        ReadOnlySpan<byte> data,
        int holderOffset,
        int objectIndex,
        IReadOnlyDictionary<ulong, int>? itemIdsByHash)
    {
        var itemOffset = ReadTableUOffset(data, holderOffset, fieldIndex: 0, required: true);
        var transformOffset = ReadTableUOffset(data, itemOffset, fieldIndex: 0, required: true);
        var transform = ReadTransform(data, transformOffset);
        var chancesOffset = ReadTableUOffset(data, itemOffset, fieldIndex: 2, required: false);

        return new SwShPlacementHiddenItem(
            objectIndex,
            transform.Transform,
            chancesOffset == 0
                ? Array.Empty<SwShPlacementHiddenItemChance>()
                : ReadHiddenItemChanceVector(data, chancesOffset, itemIdsByHash),
            transform.Offsets);
    }

    private static SwShPlacementHiddenItemChance ReadHiddenItemChance(
        ReadOnlySpan<byte> data,
        int chanceOffset,
        int chanceIndex,
        IReadOnlyDictionary<ulong, int>? itemIdsByHash)
    {
        var itemHashOffset = ReadTableFieldAbsoluteOffset(data, chanceOffset, fieldIndex: 0);
        var chanceValueOffset = ReadTableFieldAbsoluteOffset(data, chanceOffset, fieldIndex: 1);
        var quantityOffset = ReadTableFieldAbsoluteOffset(data, chanceOffset, fieldIndex: 2);
        var hash = itemHashOffset == 0
            ? 0
            : BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(itemHashOffset, sizeof(ulong)));

        return new SwShPlacementHiddenItemChance(
            chanceIndex,
            hash,
            itemIdsByHash is not null && itemIdsByHash.TryGetValue(hash, out var itemId) ? itemId : null,
            chanceValueOffset == 0 ? 0 : BinaryPrimitives.ReadInt32LittleEndian(data.Slice(chanceValueOffset, sizeof(int))),
            quantityOffset == 0 ? 0 : BinaryPrimitives.ReadInt32LittleEndian(data.Slice(quantityOffset, sizeof(int))),
            itemHashOffset,
            chanceValueOffset,
            quantityOffset);
    }

    private static TransformWithOffsets ReadTransform(ReadOnlySpan<byte> data, int tableOffset)
    {
        return new TransformWithOffsets(
            new SwShPlacementTransform(
                ReadTableSingle(data, tableOffset, fieldIndex: 0, required: false),
                ReadTableSingle(data, tableOffset, fieldIndex: 1, required: false),
                ReadTableSingle(data, tableOffset, fieldIndex: 2, required: false),
                ReadTableSingle(data, tableOffset, fieldIndex: 4, required: false)),
            new PlacementTransformOffsets(
                ReadTableFieldAbsoluteOffset(data, tableOffset, fieldIndex: 0),
                ReadTableFieldAbsoluteOffset(data, tableOffset, fieldIndex: 1),
                ReadTableFieldAbsoluteOffset(data, tableOffset, fieldIndex: 2),
                ReadTableFieldAbsoluteOffset(data, tableOffset, fieldIndex: 4)));
    }

    private static int ReadUOffset(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, sizeof(uint));
        var relativeOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));
        var targetOffset = checked(offset + (int)relativeOffset);
        EnsureRange(data, targetOffset, sizeof(int));

        return targetOffset;
    }

    private static int ReadTableUOffset(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        return ReadUOffset(data, tableOffset + fieldOffset);
    }

    private static int ReadTableFieldAbsoluteOffset(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        return fieldOffset == 0 ? 0 : tableOffset + fieldOffset;
    }

    private static float ReadTableSingle(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var offset = ReadTableFieldAbsoluteOffset(data, tableOffset, fieldIndex);
        if (offset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        EnsureRange(data, offset, sizeof(float));
        return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, sizeof(int))));
    }

    private static ulong ReadTableUInt64(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var offset = ReadTableFieldAbsoluteOffset(data, tableOffset, fieldIndex);
        if (offset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        EnsureRange(data, offset, sizeof(ulong));
        return BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, sizeof(ulong)));
    }

    private static IReadOnlyList<SwShPlacementZone> ReadZoneVector(
        ReadOnlySpan<byte> data,
        int vectorOffset,
        IReadOnlyDictionary<ulong, int>? itemIdsByHash)
    {
        var length = ReadVectorLength(data, vectorOffset);
        var values = new SwShPlacementZone[length];
        for (var index = 0; index < values.Length; index++)
        {
            var elementOffset = checked(vectorOffset + sizeof(int) + (index * sizeof(uint)));
            values[index] = ReadZone(data, ReadUOffset(data, elementOffset), index, itemIdsByHash);
        }

        return values;
    }

    private static IReadOnlyList<SwShPlacementFieldItem> ReadFieldItemVector(ReadOnlySpan<byte> data, int vectorOffset)
    {
        var length = ReadVectorLength(data, vectorOffset);
        var values = new SwShPlacementFieldItem[length];
        for (var index = 0; index < values.Length; index++)
        {
            var elementOffset = checked(vectorOffset + sizeof(int) + (index * sizeof(uint)));
            values[index] = ReadFieldItem(data, ReadUOffset(data, elementOffset), index);
        }

        return values;
    }

    private static IReadOnlyList<SwShPlacementHiddenItem> ReadHiddenItemVector(
        ReadOnlySpan<byte> data,
        int vectorOffset,
        IReadOnlyDictionary<ulong, int>? itemIdsByHash)
    {
        var length = ReadVectorLength(data, vectorOffset);
        var values = new SwShPlacementHiddenItem[length];
        for (var index = 0; index < values.Length; index++)
        {
            var elementOffset = checked(vectorOffset + sizeof(int) + (index * sizeof(uint)));
            values[index] = ReadHiddenItem(data, ReadUOffset(data, elementOffset), index, itemIdsByHash);
        }

        return values;
    }

    private static IReadOnlyList<SwShPlacementHiddenItemChance> ReadHiddenItemChanceVector(
        ReadOnlySpan<byte> data,
        int vectorOffset,
        IReadOnlyDictionary<ulong, int>? itemIdsByHash)
    {
        var length = ReadVectorLength(data, vectorOffset);
        var values = new SwShPlacementHiddenItemChance[length];
        for (var index = 0; index < values.Length; index++)
        {
            var elementOffset = checked(vectorOffset + sizeof(int) + (index * sizeof(uint)));
            values[index] = ReadHiddenItemChance(data, ReadUOffset(data, elementOffset), index, itemIdsByHash);
        }

        return values;
    }

    private static IReadOnlyList<uint> ReadUInt32Vector(ReadOnlySpan<byte> data, int vectorOffset, out IReadOnlyList<int> offsets)
    {
        var length = ReadVectorLength(data, vectorOffset);
        var values = new uint[length];
        var valueOffsets = new int[length];
        for (var index = 0; index < values.Length; index++)
        {
            var elementOffset = checked(vectorOffset + sizeof(int) + (index * sizeof(uint)));
            EnsureRange(data, elementOffset, sizeof(uint));
            values[index] = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(elementOffset, sizeof(uint)));
            valueOffsets[index] = elementOffset;
        }

        offsets = valueOffsets;
        return values;
    }

    private static IReadOnlyList<ulong> ReadUInt64Vector(ReadOnlySpan<byte> data, int vectorOffset, out IReadOnlyList<int> offsets)
    {
        var length = ReadVectorLength(data, vectorOffset);
        var values = new ulong[length];
        var valueOffsets = new int[length];
        for (var index = 0; index < values.Length; index++)
        {
            var elementOffset = checked(vectorOffset + sizeof(int) + (index * sizeof(ulong)));
            EnsureRange(data, elementOffset, sizeof(ulong));
            values[index] = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(elementOffset, sizeof(ulong)));
            valueOffsets[index] = elementOffset;
        }

        offsets = valueOffsets;
        return values;
    }

    private static int ReadVectorLength(ReadOnlySpan<byte> data, int vectorOffset)
    {
        EnsureRange(data, vectorOffset, sizeof(int));
        var length = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(vectorOffset, sizeof(int)));
        if (length < 0)
        {
            throw new InvalidDataException("FlatBuffer vector length must not be negative.");
        }

        return length;
    }

    private static string ReadString(ReadOnlySpan<byte> data, int stringOffset)
    {
        var length = ReadVectorLength(data, stringOffset);
        EnsureRange(data, stringOffset + sizeof(int), length);
        return Encoding.UTF8.GetString(data.Slice(stringOffset + sizeof(int), length));
    }

    private static int ReadTableFieldOffset(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex)
    {
        EnsureRange(data, tableOffset, sizeof(int));
        var vTableOffset = tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data.Slice(tableOffset, sizeof(int)));
        EnsureRange(data, vTableOffset, sizeof(ushort) * 2);
        var vTableSize = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vTableOffset, sizeof(ushort)));
        var fieldOffset = sizeof(ushort) * (2 + fieldIndex);
        if (fieldOffset + sizeof(ushort) > vTableSize)
        {
            return 0;
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vTableOffset + fieldOffset, sizeof(ushort)));
    }

    private static void WriteSingle(byte[] data, int offset, double value)
    {
        EnsurePatchOffset(data, offset, sizeof(float));
        if (double.IsNaN(value) || double.IsInfinity(value) || value < -1_000_000 || value > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Placement coordinate value is outside the supported range.");
        }

        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), BitConverter.SingleToInt32Bits((float)value));
    }

    private static void WriteByte(byte[] data, int offset, double value, byte minimum, byte maximum)
    {
        EnsureInteger(value, minimum, maximum);
        EnsurePatchOffset(data, offset, sizeof(byte));
        data[offset] = (byte)value;
    }

    private static void WriteInt32(byte[] data, int offset, double value, int minimum, int maximum)
    {
        EnsureInteger(value, minimum, maximum);
        EnsurePatchOffset(data, offset, sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), (int)value);
    }

    private static void WriteUInt32(byte[] data, int offset, double value)
    {
        EnsureInteger(value, 0, ushort.MaxValue);
        EnsurePatchOffset(data, offset, sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), (uint)value);
    }

    private static void WriteUInt64(byte[] data, int offset, ulong value)
    {
        EnsurePatchOffset(data, offset, sizeof(ulong));
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, sizeof(ulong)), value);
    }

    private static void EnsureInteger(double value, int minimum, int maximum)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || Math.Truncate(value) != value || value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Placement integer value must be in the supported range {minimum}-{maximum}."));
        }
    }

    private static void EnsurePatchOffset(byte[] data, int offset, int length)
    {
        if (offset <= 0 || length < 0 || offset > data.Length || length > data.Length - offset)
        {
            throw new InvalidDataException("Placement field is not present in the FlatBuffer layout and cannot be patched in place.");
        }
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length || length > data.Length - offset)
        {
            throw new InvalidDataException("FlatBuffer offset points outside the placement archive.");
        }
    }

    private sealed record TransformWithOffsets(
        SwShPlacementTransform Transform,
        PlacementTransformOffsets Offsets);

    private sealed class PlacementFlatBufferWriter
    {
        private readonly MemoryStream stream = new();
        private readonly BinaryWriter writer;

        public PlacementFlatBufferWriter()
        {
            writer = new BinaryWriter(stream);
        }

        public byte[] Write(SwShPlacementZoneArchive archive)
        {
            writer.Write(0);
            var archiveTable = WriteTable(fieldCount: 3, objectSize: 20, [4, 8, 16]);
            PatchUOffset(0, archiveTable);
            WriteUInt64(archiveTable + 8, archive.Hash);

            var description = WriteString(archive.Description);
            PatchUOffset(archiveTable + 16, description);

            var zoneVector = WriteTableVector(archive.Zones.Count);
            PatchUOffset(archiveTable + 4, zoneVector);
            for (var index = 0; index < archive.Zones.Count; index++)
            {
                var zone = WriteZone(archive.Zones[index]);
                PatchUOffset(zoneVector + sizeof(int) + (index * sizeof(uint)), zone);
            }

            return stream.ToArray();
        }

        private int WriteZone(SwShPlacementZone zone)
        {
            var offsets = new ushort[20];
            offsets[0] = 4;
            offsets[6] = 8;
            offsets[19] = 12;
            var zoneTable = WriteTable(fieldCount: 20, objectSize: 16, offsets);

            var metaTable = WriteMeta(zone);
            PatchUOffset(zoneTable + 4, metaTable);

            var fieldItems = WriteTableVector(zone.FieldItems.Count);
            PatchUOffset(zoneTable + 8, fieldItems);
            for (var index = 0; index < zone.FieldItems.Count; index++)
            {
                var item = WriteFieldItemHolder(zone.FieldItems[index]);
                PatchUOffset(fieldItems + sizeof(int) + (index * sizeof(uint)), item);
            }

            var hiddenItems = WriteTableVector(zone.HiddenItems.Count);
            PatchUOffset(zoneTable + 12, hiddenItems);
            for (var index = 0; index < zone.HiddenItems.Count; index++)
            {
                var item = WriteHiddenItemHolder(zone.HiddenItems[index]);
                PatchUOffset(hiddenItems + sizeof(int) + (index * sizeof(uint)), item);
            }

            return zoneTable;
        }

        private int WriteMeta(SwShPlacementZone zone)
        {
            var meta = WriteTable(fieldCount: 2, objectSize: 16, [4, 8]);
            WriteUInt64(meta + 8, zone.ZoneId);
            var transform = WriteTransform(zone.Transform, zone.ObjectHash);
            PatchUOffset(meta + 4, transform);
            return meta;
        }

        private int WriteFieldItemHolder(SwShPlacementFieldItem item)
        {
            var holder = WriteTable(fieldCount: 1, objectSize: 8, [4]);
            var fieldItem = WriteTable(fieldCount: 10, objectSize: 40, [4, 24, 8, 0, 0, 32, 12, 16, 20, 0]);
            PatchUOffset(holder + 4, fieldItem);

            var transform = WriteTransform(item.Transform, hashObjectName: 0);
            PatchUOffset(fieldItem + 4, transform);
            var model = WriteString(item.Model);
            PatchUOffset(fieldItem + 8, model);
            var hashes = WriteUInt64Vector(item.ItemHashes);
            PatchUOffset(fieldItem + 12, hashes);
            var itemIds = WriteUInt32Vector(item.ItemIds);
            PatchUOffset(fieldItem + 16, itemIds);
            writer.BaseStream.Position = fieldItem + 20;
            writer.Write(item.Quantity);
            writer.BaseStream.Position = writer.BaseStream.Length;
            return holder;
        }

        private int WriteHiddenItemHolder(SwShPlacementHiddenItem item)
        {
            var holder = WriteTable(fieldCount: 1, objectSize: 8, [4]);
            var hiddenItem = WriteTable(fieldCount: 6, objectSize: 24, [4, 0, 8, 12, 16, 20]);
            PatchUOffset(holder + 4, hiddenItem);

            var transform = WriteTransform(item.Transform, hashObjectName: 0);
            PatchUOffset(hiddenItem + 4, transform);
            var chances = WriteTableVector(item.Chances.Count);
            PatchUOffset(hiddenItem + 8, chances);
            for (var index = 0; index < item.Chances.Count; index++)
            {
                var chance = WriteHiddenChance(item.Chances[index]);
                PatchUOffset(chances + sizeof(int) + (index * sizeof(uint)), chance);
            }

            return holder;
        }

        private int WriteHiddenChance(SwShPlacementHiddenItemChance chance)
        {
            var table = WriteTable(fieldCount: 3, objectSize: 24, [8, 16, 20]);
            WriteUInt64(table + 8, chance.ItemHash);
            WriteInt32(table + 16, chance.Chance);
            WriteInt32(table + 20, chance.Quantity);
            return table;
        }

        private int WriteTransform(SwShPlacementTransform transform, ulong hashObjectName)
        {
            var table = WriteTable(fieldCount: 12, objectSize: 64, [4, 8, 12, 0, 16, 0, 20, 24, 28, 32, 40, 48]);
            WriteSingleRaw(table + 4, transform.X);
            WriteSingleRaw(table + 8, transform.Y);
            WriteSingleRaw(table + 12, transform.Z);
            WriteSingleRaw(table + 16, transform.RotationY);
            WriteSingleRaw(table + 20, 1);
            WriteSingleRaw(table + 24, 1);
            WriteSingleRaw(table + 28, 1);
            WriteUInt64(table + 32, hashObjectName);
            WriteUInt64(table + 40, 0);
            WriteUInt64(table + 48, 0);
            return table;
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

        private int WriteUInt32Vector(IReadOnlyList<uint> values)
        {
            var offset = checked((int)stream.Position);
            writer.Write(values.Count);
            foreach (var value in values)
            {
                writer.Write(value);
            }

            return offset;
        }

        private int WriteUInt64Vector(IReadOnlyList<ulong> values)
        {
            var offset = checked((int)stream.Position);
            writer.Write(values.Count);
            foreach (var value in values)
            {
                writer.Write(value);
            }

            return offset;
        }

        private int WriteString(string value)
        {
            var data = Encoding.UTF8.GetBytes(value);
            var offset = checked((int)stream.Position);
            writer.Write(data.Length);
            writer.Write(data);
            writer.Write((byte)0);
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

        private void PatchUOffset(int sourceOffset, int targetOffset)
        {
            if (targetOffset < sourceOffset)
            {
                throw new InvalidOperationException("FlatBuffer target offsets must point forward.");
            }

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

        private void WriteInt32(int offset, int value)
        {
            var position = writer.BaseStream.Position;
            writer.BaseStream.Position = offset;
            writer.Write(value);
            writer.BaseStream.Position = position;
        }

        private void WriteSingleRaw(int offset, float value)
        {
            var position = writer.BaseStream.Position;
            writer.BaseStream.Position = offset;
            writer.Write(value);
            writer.BaseStream.Position = position;
        }
    }
}

public sealed record PlacementTransformOffsets(
    int X,
    int Y,
    int Z,
    int RotationY);
