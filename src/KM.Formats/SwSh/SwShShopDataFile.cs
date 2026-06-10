// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace KM.Formats.SwSh;

public enum SwShShopKind
{
    Single,
    Multi,
}

public enum SwShShopInventoryEditAction
{
    Replace,
    Add,
    Remove,
    Set,
}

public sealed record SwShShopInventory(IReadOnlyList<int> Items);

public sealed record SwShSingleShopRecord(
    ulong Hash,
    SwShShopInventory Inventory);

public sealed record SwShMultiShopRecord(
    ulong Hash,
    IReadOnlyList<SwShShopInventory> Inventories);

public sealed record SwShShopInventoryEdit(
    SwShShopKind Kind,
    ulong Hash,
    int InventoryIndex,
    int Slot,
    int ItemId,
    SwShShopInventoryEditAction Action = SwShShopInventoryEditAction.Replace,
    IReadOnlyList<int>? Items = null);

public sealed record SwShShopDataFile(
    IReadOnlyList<SwShSingleShopRecord> SingleShops,
    IReadOnlyList<SwShMultiShopRecord> MultiShops)
{
    public static SwShShopDataFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("Shop data is too small to contain a FlatBuffer root.");
        }

        var rootTableOffset = ReadUOffset(data, offset: 0);
        var singleVectorOffset = ReadTableUOffset(data, rootTableOffset, fieldIndex: 0, required: true);
        var multiVectorOffset = ReadTableUOffset(data, rootTableOffset, fieldIndex: 1, required: true);

        return new SwShShopDataFile(
            ReadTableVector(data, singleVectorOffset, ReadSingleShop),
            ReadTableVector(data, multiVectorOffset, ReadMultiShop));
    }

    public byte[] Write()
    {
        var writer = new ShopFlatBufferWriter();
        writer.Write(this);

        return writer.ToArray();
    }

    public byte[] WriteEdits(IEnumerable<SwShShopInventoryEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var singleShops = SingleShops
            .Select(shop => shop with
            {
                Inventory = new SwShShopInventory(shop.Inventory.Items.ToArray()),
            })
            .ToArray();
        var multiShops = MultiShops
            .Select(shop => shop with
            {
                Inventories = shop.Inventories
                    .Select(inventory => new SwShShopInventory(inventory.Items.ToArray()))
                    .ToArray(),
            })
            .ToArray();

        foreach (var edit in edits)
        {
            ApplyEdit(singleShops, multiShops, edit);
        }

        return new SwShShopDataFile(singleShops, multiShops).Write();
    }

    private static void ApplyEdit(
        SwShSingleShopRecord[] singleShops,
        SwShMultiShopRecord[] multiShops,
        SwShShopInventoryEdit edit)
    {
        if (edit.Slot < 0)
        {
            throw new InvalidDataException("Shop inventory slot must not be negative.");
        }

        if (edit.Kind == SwShShopKind.Single)
        {
            var shopIndex = Array.FindIndex(singleShops, candidate => candidate.Hash == edit.Hash);
            if (shopIndex < 0)
            {
                throw new InvalidDataException($"Single shop 0x{edit.Hash:X16} is not present.");
            }

            singleShops[shopIndex] = singleShops[shopIndex] with
            {
                Inventory = ApplyInventoryEdit(singleShops[shopIndex].Inventory, edit),
            };
            return;
        }

        var multiShopIndex = Array.FindIndex(multiShops, candidate => candidate.Hash == edit.Hash);
        if (multiShopIndex < 0)
        {
            throw new InvalidDataException($"Multi shop 0x{edit.Hash:X16} is not present.");
        }

        var multiShop = multiShops[multiShopIndex];
        if ((uint)edit.InventoryIndex >= (uint)multiShop.Inventories.Count)
        {
            throw new InvalidDataException($"Multi shop 0x{edit.Hash:X16} inventory {edit.InventoryIndex} is not present.");
        }

        var inventories = multiShop.Inventories.ToArray();
        inventories[edit.InventoryIndex] = ApplyInventoryEdit(inventories[edit.InventoryIndex], edit);
        multiShops[multiShopIndex] = multiShop with { Inventories = inventories };
    }

    private static SwShShopInventory ApplyInventoryEdit(
        SwShShopInventory inventory,
        SwShShopInventoryEdit edit)
    {
        return edit.Action switch
        {
            SwShShopInventoryEditAction.Replace => ReplaceInventoryItem(inventory, edit.Slot, edit.ItemId),
            SwShShopInventoryEditAction.Add => InsertInventoryItem(inventory, edit.Slot, edit.ItemId),
            SwShShopInventoryEditAction.Remove => RemoveInventoryItem(inventory, edit.Slot),
            SwShShopInventoryEditAction.Set => SetInventoryItems(inventory, edit.Items),
            _ => throw new InvalidDataException($"Shop inventory edit action '{edit.Action}' is not supported."),
        };
    }

    private static SwShShopInventory SetInventoryItems(
        SwShShopInventory inventory,
        IReadOnlyList<int>? items)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        return new SwShShopInventory((items ?? Array.Empty<int>()).ToArray());
    }

    private static SwShShopInventory ReplaceInventoryItem(SwShShopInventory inventory, int slot, int itemId)
    {
        if ((uint)slot >= (uint)inventory.Items.Count)
        {
            throw new InvalidDataException($"Shop inventory slot {slot} is not present.");
        }

        var items = inventory.Items.ToArray();
        items[slot] = itemId;
        return new SwShShopInventory(items);
    }

    private static SwShShopInventory InsertInventoryItem(SwShShopInventory inventory, int slot, int itemId)
    {
        if ((uint)slot > (uint)inventory.Items.Count)
        {
            throw new InvalidDataException($"Shop inventory insert slot {slot} is outside the inventory.");
        }

        var items = inventory.Items.ToList();
        items.Insert(slot, itemId);
        return new SwShShopInventory(items.ToArray());
    }

    private static SwShShopInventory RemoveInventoryItem(SwShShopInventory inventory, int slot)
    {
        if ((uint)slot >= (uint)inventory.Items.Count)
        {
            throw new InvalidDataException($"Shop inventory slot {slot} is not present.");
        }

        var items = inventory.Items.ToList();
        items.RemoveAt(slot);
        return new SwShShopInventory(items.ToArray());
    }

    private static SwShSingleShopRecord ReadSingleShop(ReadOnlySpan<byte> data, int tableOffset)
    {
        return new SwShSingleShopRecord(
            ReadTableUInt64(data, tableOffset, fieldIndex: 0, required: true),
            ReadInventory(data, ReadTableUOffset(data, tableOffset, fieldIndex: 1, required: true)));
    }

    private static SwShMultiShopRecord ReadMultiShop(ReadOnlySpan<byte> data, int tableOffset)
    {
        return new SwShMultiShopRecord(
            ReadTableUInt64(data, tableOffset, fieldIndex: 0, required: true),
            ReadTableVector(data, ReadTableUOffset(data, tableOffset, fieldIndex: 1, required: true), ReadInventory));
    }

    private static SwShShopInventory ReadInventory(ReadOnlySpan<byte> data, int tableOffset)
    {
        return new SwShShopInventory(ReadIntVector(data, ReadTableUOffset(data, tableOffset, fieldIndex: 0, required: true)));
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

    private static ulong ReadTableUInt64(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
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

        EnsureRange(data, tableOffset + fieldOffset, sizeof(ulong));

        return BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(tableOffset + fieldOffset, sizeof(ulong)));
    }

    private static int ReadTableFieldOffset(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex)
    {
        EnsureRange(data, tableOffset, sizeof(int));
        var vtableOffset = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(tableOffset, sizeof(int)));
        var vtableStart = tableOffset - vtableOffset;
        EnsureRange(data, vtableStart, sizeof(ushort) * 2);
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vtableStart, sizeof(ushort)));
        var fieldEntryOffset = sizeof(ushort) * 2 + (fieldIndex * sizeof(ushort));
        if (fieldEntryOffset + sizeof(ushort) > vtableLength)
        {
            return 0;
        }

        EnsureRange(data, vtableStart + fieldEntryOffset, sizeof(ushort));

        return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vtableStart + fieldEntryOffset, sizeof(ushort)));
    }

    private static T[] ReadTableVector<T>(
        ReadOnlySpan<byte> data,
        int vectorOffset,
        Func<ReadOnlySpan<byte>, int, T> readTable)
    {
        var count = ReadVectorLength(data, vectorOffset);
        var values = new T[count];

        for (var index = 0; index < count; index++)
        {
            var elementOffset = vectorOffset + sizeof(uint) + (index * sizeof(uint));
            values[index] = readTable(data, ReadUOffset(data, elementOffset));
        }

        return values;
    }

    private static int[] ReadIntVector(ReadOnlySpan<byte> data, int vectorOffset)
    {
        var count = ReadVectorLength(data, vectorOffset);
        EnsureRange(data, vectorOffset + sizeof(uint), checked(count * sizeof(int)));
        var values = new int[count];

        for (var index = 0; index < count; index++)
        {
            values[index] = BinaryPrimitives.ReadInt32LittleEndian(
                data.Slice(vectorOffset + sizeof(uint) + (index * sizeof(int)), sizeof(int)));
        }

        return values;
    }

    private static int ReadVectorLength(ReadOnlySpan<byte> data, int vectorOffset)
    {
        EnsureRange(data, vectorOffset, sizeof(uint));
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(vectorOffset, sizeof(uint)));
        if (count > int.MaxValue)
        {
            throw new InvalidDataException("FlatBuffer vector is too large.");
        }

        return (int)count;
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length || length > data.Length - offset)
        {
            throw new InvalidDataException("FlatBuffer offset points outside the shop data file.");
        }
    }

    private sealed class ShopFlatBufferWriter
    {
        private readonly List<byte> bytes = [];

        public void Write(SwShShopDataFile shopData)
        {
            WriteUInt32(0);
            var root = WriteRootTable();
            WriteUInt32At(0, checked((uint)root.TableOffset));

            var singleVector = WriteTableVector(shopData.SingleShops.Count);
            PatchUOffset(root.Field0Offset, singleVector.VectorOffset);
            for (var index = 0; index < shopData.SingleShops.Count; index++)
            {
                var shop = shopData.SingleShops[index];
                var tableOffset = WriteSingleShop(shop);
                PatchUOffset(singleVector.ElementOffsets[index], tableOffset);
            }

            var multiVector = WriteTableVector(shopData.MultiShops.Count);
            PatchUOffset(root.Field1Offset, multiVector.VectorOffset);
            for (var index = 0; index < shopData.MultiShops.Count; index++)
            {
                var shop = shopData.MultiShops[index];
                var tableOffset = WriteMultiShop(shop);
                PatchUOffset(multiVector.ElementOffsets[index], tableOffset);
            }
        }

        public byte[] ToArray()
        {
            return bytes.ToArray();
        }

        private TableFields WriteRootTable()
        {
            return WriteTwoUOffsetTable(
                field0VTableOffset: 4,
                field1VTableOffset: 8,
                objectLength: 12,
                alignment: 4);
        }

        private int WriteSingleShop(SwShSingleShopRecord shop)
        {
            var table = WriteShopTable(shop.Hash);
            var inventoryOffset = WriteInventory(shop.Inventory);
            PatchUOffset(table.Field0Offset, inventoryOffset);

            return table.TableOffset;
        }

        private int WriteMultiShop(SwShMultiShopRecord shop)
        {
            var table = WriteShopTable(shop.Hash);
            var inventoryVector = WriteTableVector(shop.Inventories.Count);
            PatchUOffset(table.Field0Offset, inventoryVector.VectorOffset);

            for (var index = 0; index < shop.Inventories.Count; index++)
            {
                var inventoryOffset = WriteInventory(shop.Inventories[index]);
                PatchUOffset(inventoryVector.ElementOffsets[index], inventoryOffset);
            }

            return table.TableOffset;
        }

        private TableFields WriteShopTable(ulong hash)
        {
            AlignForTable(vtableLength: 8, alignment: 8);
            var vtableOffset = Position;
            WriteUInt16(8);
            WriteUInt16(16);
            WriteUInt16(8);
            WriteUInt16(4);
            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            var inventoryFieldOffset = Position;
            WriteUInt32(0);
            WriteUInt64(hash);

            return new TableFields(tableOffset, inventoryFieldOffset, Field1Offset: -1);
        }

        private int WriteInventory(SwShShopInventory inventory)
        {
            AlignForTable(vtableLength: 6, alignment: 4);
            var vtableOffset = Position;
            WriteUInt16(6);
            WriteUInt16(8);
            WriteUInt16(4);
            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            var itemsFieldOffset = Position;
            WriteUInt32(0);
            var itemsOffset = WriteIntVector(inventory.Items);
            PatchUOffset(itemsFieldOffset, itemsOffset);

            return tableOffset;
        }

        private TableFields WriteTwoUOffsetTable(
            ushort field0VTableOffset,
            ushort field1VTableOffset,
            ushort objectLength,
            int alignment)
        {
            AlignForTable(vtableLength: 8, alignment);
            var vtableOffset = Position;
            WriteUInt16(8);
            WriteUInt16(objectLength);
            WriteUInt16(field0VTableOffset);
            WriteUInt16(field1VTableOffset);
            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            var field0Offset = Position;
            WriteUInt32(0);
            var field1Offset = Position;
            WriteUInt32(0);

            return new TableFields(tableOffset, field0Offset, field1Offset);
        }

        private VectorFields WriteTableVector(int count)
        {
            Align(4);
            var vectorOffset = Position;
            WriteUInt32(checked((uint)count));
            var elementOffsets = new int[count];
            for (var index = 0; index < count; index++)
            {
                elementOffsets[index] = Position;
                WriteUInt32(0);
            }

            return new VectorFields(vectorOffset, elementOffsets);
        }

        private int WriteIntVector(IReadOnlyList<int> values)
        {
            Align(4);
            var vectorOffset = Position;
            WriteUInt32(checked((uint)values.Count));
            foreach (var value in values)
            {
                WriteInt32(value);
            }

            return vectorOffset;
        }

        private void PatchUOffset(int sourceOffset, int targetOffset)
        {
            if (targetOffset <= sourceOffset)
            {
                throw new InvalidOperationException("FlatBuffer target offsets must point forward.");
            }

            WriteUInt32At(sourceOffset, checked((uint)(targetOffset - sourceOffset)));
        }

        private void AlignForTable(int vtableLength, int alignment)
        {
            while (((Position + vtableLength) % alignment) != 0)
            {
                bytes.Add(0);
            }
        }

        private void Align(int alignment)
        {
            while ((Position % alignment) != 0)
            {
                bytes.Add(0);
            }
        }

        private int Position => bytes.Count;

        private void WriteUInt16(ushort value)
        {
            var start = Grow(sizeof(ushort));
            BinaryPrimitives.WriteUInt16LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(start, sizeof(ushort)), value);
        }

        private void WriteInt32(int value)
        {
            var start = Grow(sizeof(int));
            BinaryPrimitives.WriteInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(start, sizeof(int)), value);
        }

        private void WriteUInt32(uint value)
        {
            var start = Grow(sizeof(uint));
            BinaryPrimitives.WriteUInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(start, sizeof(uint)), value);
        }

        private void WriteUInt64(ulong value)
        {
            var start = Grow(sizeof(ulong));
            BinaryPrimitives.WriteUInt64LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(start, sizeof(ulong)), value);
        }

        private void WriteUInt32At(int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(offset, sizeof(uint)), value);
        }

        private int Grow(int count)
        {
            var start = bytes.Count;
            for (var index = 0; index < count; index++)
            {
                bytes.Add(0);
            }

            return start;
        }

        private sealed record TableFields(
            int TableOffset,
            int Field0Offset,
            int Field1Offset);

        private sealed record VectorFields(
            int VectorOffset,
            IReadOnlyList<int> ElementOffsets);
    }
}
