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
    IReadOnlyList<int>? Items = null,
    int ShopIndex = -1);

public sealed record SwShShopDataFile(
    IReadOnlyList<SwShSingleShopRecord> SingleShops,
    IReadOnlyList<SwShMultiShopRecord> MultiShops)
{
    private byte[]? SourceData { get; init; }

    private IReadOnlyList<SourceShopLayout>? SourceSingleShopLayouts { get; init; }

    private IReadOnlyList<SourceShopLayout>? SourceMultiShopLayouts { get; init; }

    public static SwShShopDataFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("Shop data is too small to contain a FlatBuffer root.");
        }

        var ranges = new StructuralRangeRegistry();
        ranges.Register(offset: 0, sizeof(uint), "root offset", allowExactAlias: false);
        var rootTableOffset = ReadUOffset(data, offset: 0);
        var rootTable = ReadTableLayout(
            data,
            rootTableOffset,
            "root table",
            ranges,
            [new FieldLayout(0, sizeof(uint), sizeof(uint)), new FieldLayout(1, sizeof(uint), sizeof(uint))]);
        var singleVectorOffset = ReadTableUOffset(data, rootTable, fieldIndex: 0, required: true);
        var multiVectorOffset = ReadTableUOffset(data, rootTable, fieldIndex: 1, required: true);

        var (singleShops, singleLayouts) = ReadSingleShopVector(data, singleVectorOffset, ranges);
        var (multiShops, multiLayouts) = ReadMultiShopVector(data, multiVectorOffset, ranges);

        return new SwShShopDataFile(
            singleShops,
            multiShops)
        {
            SourceData = data.ToArray(),
            SourceSingleShopLayouts = singleLayouts,
            SourceMultiShopLayouts = multiLayouts,
        };
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

        var editArray = edits.ToArray();
        if (editArray.Length == 0 && SourceData is not null)
        {
            return SourceData.ToArray();
        }

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
        var touchedSingleShops = new HashSet<int>();
        var touchedMultiShops = new HashSet<int>();

        foreach (var edit in editArray)
        {
            ApplyEdit(singleShops, multiShops, edit, touchedSingleShops, touchedMultiShops);
        }

        if (SourceData is not null
            && SourceSingleShopLayouts is not null
            && SourceMultiShopLayouts is not null)
        {
            var changedSingleShops = touchedSingleShops
                .Where(index => !SingleShops[index].Inventory.Items.SequenceEqual(singleShops[index].Inventory.Items))
                .Order()
                .ToArray();
            var changedMultiShops = touchedMultiShops
                .Where(index => !InventoriesEqual(MultiShops[index].Inventories, multiShops[index].Inventories))
                .Order()
                .ToArray();
            if (changedSingleShops.Length == 0 && changedMultiShops.Length == 0)
            {
                return SourceData.ToArray();
            }

            var writer = new CopyOnWriteShopWriter(SourceData);
            foreach (var shopIndex in changedSingleShops)
            {
                writer.AppendSingleShop(
                    SourceSingleShopLayouts[shopIndex],
                    singleShops[shopIndex]);
            }

            foreach (var shopIndex in changedMultiShops)
            {
                writer.AppendMultiShop(
                    SourceMultiShopLayouts[shopIndex],
                    multiShops[shopIndex]);
            }

            return writer.ToArray();
        }

        return new SwShShopDataFile(singleShops, multiShops).Write();
    }

    private static void ApplyEdit(
        SwShSingleShopRecord[] singleShops,
        SwShMultiShopRecord[] multiShops,
        SwShShopInventoryEdit edit,
        ISet<int> touchedSingleShops,
        ISet<int> touchedMultiShops)
    {
        if (edit.Slot < 0)
        {
            throw new InvalidDataException("Shop inventory slot must not be negative.");
        }

        if (edit.Kind == SwShShopKind.Single)
        {
            if (edit.InventoryIndex != 0)
            {
                throw new InvalidDataException("Single shop inventory index must be zero.");
            }

            var shopIndex = ResolveShopIndex(singleShops, edit.Hash, edit.ShopIndex, "Single");

            singleShops[shopIndex] = singleShops[shopIndex] with
            {
                Inventory = ApplyInventoryEdit(singleShops[shopIndex].Inventory, edit),
            };
            touchedSingleShops.Add(shopIndex);
            return;
        }

        if (edit.Kind != SwShShopKind.Multi)
        {
            throw new InvalidDataException($"Shop kind '{edit.Kind}' is not supported.");
        }

        var multiShopIndex = ResolveShopIndex(multiShops, edit.Hash, edit.ShopIndex, "Multi");

        var multiShop = multiShops[multiShopIndex];
        if ((uint)edit.InventoryIndex >= (uint)multiShop.Inventories.Count)
        {
            throw new InvalidDataException($"Multi shop 0x{edit.Hash:X16} inventory {edit.InventoryIndex} is not present.");
        }

        var inventories = multiShop.Inventories.ToArray();
        inventories[edit.InventoryIndex] = ApplyInventoryEdit(inventories[edit.InventoryIndex], edit);
        multiShops[multiShopIndex] = multiShop with { Inventories = inventories };
        touchedMultiShops.Add(multiShopIndex);
    }

    private static int ResolveShopIndex<TShop>(
        IReadOnlyList<TShop> shops,
        ulong hash,
        int requestedIndex,
        string kind)
        where TShop : notnull
    {
        static ulong GetHash(TShop shop) => shop switch
        {
            SwShSingleShopRecord single => single.Hash,
            SwShMultiShopRecord multi => multi.Hash,
            _ => throw new InvalidOperationException("Unsupported shop record type."),
        };

        if (requestedIndex < -1)
        {
            throw new InvalidDataException(
                $"{kind} shop index {requestedIndex} is not a supported physical or legacy shop index.");
        }

        if (requestedIndex >= 0)
        {
            if ((uint)requestedIndex >= (uint)shops.Count || GetHash(shops[requestedIndex]) != hash)
            {
                throw new InvalidDataException(
                    $"{kind} shop index {requestedIndex} does not match shop 0x{hash:X16}.");
            }

            return requestedIndex;
        }

        var matches = Enumerable.Range(0, shops.Count)
            .Where(index => GetHash(shops[index]) == hash)
            .Take(2)
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidDataException($"{kind} shop 0x{hash:X16} is not present."),
            _ => throw new InvalidDataException(
                $"{kind} shop 0x{hash:X16} is ambiguous; specify its physical shop index."),
        };
    }

    private static bool InventoriesEqual(
        IReadOnlyList<SwShShopInventory> left,
        IReadOnlyList<SwShShopInventory> right)
    {
        return left.Count == right.Count
            && left.Select((inventory, index) => inventory.Items.SequenceEqual(right[index].Items)).All(equal => equal);
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

    private static (SwShSingleShopRecord[] Shops, SourceShopLayout[] Layouts) ReadSingleShopVector(
        ReadOnlySpan<byte> data,
        int vectorOffset,
        StructuralRangeRegistry ranges)
    {
        var count = ReadVectorCount(data, vectorOffset, sizeof(uint), "single shop vector", ranges);
        var shops = new SwShSingleShopRecord[count];
        var layouts = new SourceShopLayout[count];
        for (var index = 0; index < count; index++)
        {
            var elementOffset = checked(vectorOffset + sizeof(uint) + (index * sizeof(uint)));
            var tableOffset = ReadUOffset(data, elementOffset);
            var table = ReadTableLayout(
                data,
                tableOffset,
                "single shop table",
                ranges,
                [new FieldLayout(0, sizeof(ulong), sizeof(ulong)), new FieldLayout(1, sizeof(uint), sizeof(uint))]);
            var inventoryOffset = ReadTableUOffset(data, table, fieldIndex: 1, required: true);
            var (inventory, inventoryLayout) = ReadInventory(data, inventoryOffset, ranges);
            shops[index] = new SwShSingleShopRecord(
                ReadTableUInt64(data, table, fieldIndex: 0, required: false),
                inventory);
            layouts[index] = new SourceShopLayout(elementOffset, table, [inventoryLayout]);
        }

        return (shops, layouts);
    }

    private static (SwShMultiShopRecord[] Shops, SourceShopLayout[] Layouts) ReadMultiShopVector(
        ReadOnlySpan<byte> data,
        int vectorOffset,
        StructuralRangeRegistry ranges)
    {
        var count = ReadVectorCount(data, vectorOffset, sizeof(uint), "multi shop vector", ranges);
        var shops = new SwShMultiShopRecord[count];
        var layouts = new SourceShopLayout[count];
        for (var index = 0; index < count; index++)
        {
            var elementOffset = checked(vectorOffset + sizeof(uint) + (index * sizeof(uint)));
            var tableOffset = ReadUOffset(data, elementOffset);
            var table = ReadTableLayout(
                data,
                tableOffset,
                "multi shop table",
                ranges,
                [new FieldLayout(0, sizeof(ulong), sizeof(ulong)), new FieldLayout(1, sizeof(uint), sizeof(uint))]);
            var inventoryVectorOffset = ReadTableUOffset(data, table, fieldIndex: 1, required: true);
            var (inventories, inventoryLayouts) = ReadInventoryVector(data, inventoryVectorOffset, ranges);
            shops[index] = new SwShMultiShopRecord(
                ReadTableUInt64(data, table, fieldIndex: 0, required: false),
                inventories);
            layouts[index] = new SourceShopLayout(elementOffset, table, inventoryLayouts);
        }

        return (shops, layouts);
    }

    private static (SwShShopInventory[] Inventories, SourceInventoryLayout[] Layouts) ReadInventoryVector(
        ReadOnlySpan<byte> data,
        int vectorOffset,
        StructuralRangeRegistry ranges)
    {
        var count = ReadVectorCount(data, vectorOffset, sizeof(uint), "shop inventory table vector", ranges);
        var inventories = new SwShShopInventory[count];
        var layouts = new SourceInventoryLayout[count];
        for (var index = 0; index < count; index++)
        {
            var elementOffset = checked(vectorOffset + sizeof(uint) + (index * sizeof(uint)));
            var tableOffset = ReadUOffset(data, elementOffset);
            (inventories[index], layouts[index]) = ReadInventory(data, tableOffset, ranges);
        }

        return (inventories, layouts);
    }

    private static (SwShShopInventory Inventory, SourceInventoryLayout Layout) ReadInventory(
        ReadOnlySpan<byte> data,
        int tableOffset,
        StructuralRangeRegistry ranges)
    {
        var table = ReadTableLayout(
            data,
            tableOffset,
            "shop inventory table",
            ranges,
            [new FieldLayout(0, sizeof(uint), sizeof(uint))]);
        var itemsVectorOffset = ReadTableUOffset(data, table, fieldIndex: 0, required: true);
        return (
            new SwShShopInventory(ReadIntVector(data, itemsVectorOffset, ranges)),
            new SourceInventoryLayout(table, itemsVectorOffset));
    }

    private static int ReadUOffset(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, sizeof(uint));
        var relativeOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));
        if (relativeOffset == 0 || relativeOffset > int.MaxValue)
        {
            throw new InvalidDataException("FlatBuffer uoffset must point forward inside the shop data file.");
        }

        int targetOffset;
        try
        {
            targetOffset = checked(offset + (int)relativeOffset);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("FlatBuffer uoffset overflows the shop data file.", exception);
        }

        EnsureRange(data, targetOffset, sizeof(int));

        return targetOffset;
    }

    private static int ReadTableUOffset(
        ReadOnlySpan<byte> data,
        TableLayout table,
        int fieldIndex,
        bool required)
    {
        var fieldOffset = ReadTableFieldOffset(table, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        return ReadUOffset(data, checked(table.TableOffset + fieldOffset));
    }

    private static ulong ReadTableUInt64(
        ReadOnlySpan<byte> data,
        TableLayout table,
        int fieldIndex,
        bool required)
    {
        var fieldOffset = ReadTableFieldOffset(table, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        var absoluteOffset = checked(table.TableOffset + fieldOffset);
        EnsureRange(data, absoluteOffset, sizeof(ulong));

        return BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(absoluteOffset, sizeof(ulong)));
    }

    private static int ReadTableFieldOffset(TableLayout table, int fieldIndex)
    {
        return (uint)fieldIndex < (uint)table.FieldOffsets.Count
            ? table.FieldOffsets[fieldIndex]
            : 0;
    }

    private static TableLayout ReadTableLayout(
        ReadOnlySpan<byte> data,
        int tableOffset,
        string kind,
        StructuralRangeRegistry ranges,
        IReadOnlyList<FieldLayout> knownFields)
    {
        if ((tableOffset & (sizeof(uint) - 1)) != 0)
        {
            throw new InvalidDataException($"FlatBuffer {kind} is not 4-byte aligned.");
        }

        EnsureRange(data, tableOffset, sizeof(int));
        var vtableDistance = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(tableOffset, sizeof(int)));
        if (vtableDistance == 0)
        {
            throw new InvalidDataException($"FlatBuffer {kind} has an invalid vtable offset.");
        }

        var vtableOffset64 = (long)tableOffset - vtableDistance;
        if (vtableOffset64 < 0 || vtableOffset64 > int.MaxValue)
        {
            throw new InvalidDataException($"FlatBuffer {kind} has an invalid vtable offset.");
        }

        var vtableOffset = (int)vtableOffset64;
        if ((vtableOffset & (sizeof(ushort) - 1)) != 0)
        {
            throw new InvalidDataException($"FlatBuffer {kind} vtable is not 2-byte aligned.");
        }

        EnsureRange(data, vtableOffset, sizeof(ushort) * 2);
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vtableOffset, sizeof(ushort)));
        var objectLength = BinaryPrimitives.ReadUInt16LittleEndian(
            data.Slice(vtableOffset + sizeof(ushort), sizeof(ushort)));
        if (vtableLength < sizeof(ushort) * 2 || (vtableLength & 1) != 0)
        {
            throw new InvalidDataException($"FlatBuffer {kind} has an invalid vtable length.");
        }

        if (objectLength < sizeof(int))
        {
            throw new InvalidDataException($"FlatBuffer {kind} has an invalid object length.");
        }

        EnsureRange(data, vtableOffset, vtableLength);
        EnsureRange(data, tableOffset, objectLength);
        ranges.Register(vtableOffset, vtableLength, "vtable", allowExactAlias: true);
        ranges.Register(tableOffset, objectLength, kind, allowExactAlias: true);

        var fieldCount = (vtableLength - (sizeof(ushort) * 2)) / sizeof(ushort);
        var fieldOffsets = new ushort[fieldCount];
        var materializedOffsets = new HashSet<ushort>();
        for (var fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            var entryOffset = checked(vtableOffset + (sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort)));
            var fieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(entryOffset, sizeof(ushort)));
            fieldOffsets[fieldIndex] = fieldOffset;
            if (fieldOffset == 0)
            {
                continue;
            }

            if (fieldOffset < sizeof(int) || fieldOffset >= objectLength || !materializedOffsets.Add(fieldOffset))
            {
                throw new InvalidDataException($"FlatBuffer {kind} has an invalid or overlapping field offset.");
            }
        }

        var knownRanges = new List<(int Start, int End)>();
        foreach (var field in knownFields)
        {
            var fieldOffset = (uint)field.Index < (uint)fieldOffsets.Length
                ? fieldOffsets[field.Index]
                : 0;
            if (fieldOffset == 0)
            {
                continue;
            }

            var fieldEnd = checked(fieldOffset + field.Size);
            if (fieldEnd > objectLength
                || ((tableOffset + fieldOffset) & (field.Alignment - 1)) != 0
                || knownRanges.Any(range => fieldOffset < range.End && fieldEnd > range.Start))
            {
                throw new InvalidDataException($"FlatBuffer {kind} field {field.Index} is outside or overlaps its table object.");
            }

            knownRanges.Add((fieldOffset, fieldEnd));
        }

        for (var fieldIndex = 0; fieldIndex < fieldOffsets.Length; fieldIndex++)
        {
            if (knownFields.Any(field => field.Index == fieldIndex) || fieldOffsets[fieldIndex] == 0)
            {
                continue;
            }

            if (knownRanges.Any(range => fieldOffsets[fieldIndex] >= range.Start && fieldOffsets[fieldIndex] < range.End))
            {
                throw new InvalidDataException($"FlatBuffer {kind} has an unknown field overlapping a known field.");
            }
        }

        return new TableLayout(tableOffset, vtableOffset, vtableLength, objectLength, fieldOffsets);
    }

    private static int[] ReadIntVector(
        ReadOnlySpan<byte> data,
        int vectorOffset,
        StructuralRangeRegistry ranges)
    {
        var count = ReadVectorCount(data, vectorOffset, sizeof(int), "shop item vector", ranges);
        var values = new int[count];

        for (var index = 0; index < count; index++)
        {
            values[index] = BinaryPrimitives.ReadInt32LittleEndian(
                data.Slice(checked(vectorOffset + sizeof(uint) + (index * sizeof(int))), sizeof(int)));
        }

        return values;
    }

    private static int ReadVectorCount(
        ReadOnlySpan<byte> data,
        int vectorOffset,
        int elementSize,
        string kind,
        StructuralRangeRegistry ranges)
    {
        const int MaximumVectorElementCount = 1_000_000;
        if ((vectorOffset & (sizeof(uint) - 1)) != 0)
        {
            throw new InvalidDataException($"FlatBuffer {kind} is not 4-byte aligned.");
        }

        EnsureRange(data, vectorOffset, sizeof(uint));
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(vectorOffset, sizeof(uint)));
        if (count > MaximumVectorElementCount)
        {
            throw new InvalidDataException($"FlatBuffer {kind} is too large.");
        }

        int byteLength;
        try
        {
            byteLength = checked(sizeof(uint) + ((int)count * elementSize));
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException($"FlatBuffer {kind} length overflows the shop data file.", exception);
        }

        EnsureRange(data, vectorOffset, byteLength);
        ranges.Register(vectorOffset, byteLength, kind, allowExactAlias: true);
        return (int)count;
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length || length > data.Length - offset)
        {
            throw new InvalidDataException("FlatBuffer offset points outside the shop data file.");
        }
    }

    private sealed record FieldLayout(int Index, int Size, int Alignment);

    private sealed record TableLayout(
        int TableOffset,
        int VTableOffset,
        int VTableLength,
        int ObjectLength,
        IReadOnlyList<ushort> FieldOffsets)
    {
        public bool HasMaterializedFieldAtOrAfter(int fieldIndex)
        {
            return FieldOffsets.Skip(fieldIndex).Any(offset => offset != 0);
        }
    }

    private sealed record SourceInventoryLayout(
        TableLayout Table,
        int ItemsVectorOffset);

    private sealed record SourceShopLayout(
        int RootVectorElementOffset,
        TableLayout Table,
        IReadOnlyList<SourceInventoryLayout> Inventories);

    private sealed class StructuralRangeRegistry
    {
        private readonly List<StructuralRange> ranges = [];

        public void Register(int offset, int length, string kind, bool allowExactAlias)
        {
            var end = checked(offset + length);
            foreach (var existing in ranges)
            {
                if (offset >= existing.End || end <= existing.Offset)
                {
                    continue;
                }

                if (allowExactAlias
                    && offset == existing.Offset
                    && length == existing.Length
                    && string.Equals(kind, existing.Kind, StringComparison.Ordinal))
                {
                    return;
                }

                throw new InvalidDataException(
                    $"FlatBuffer {kind} overlaps {existing.Kind} in the shop data file.");
            }

            ranges.Add(new StructuralRange(offset, length, kind));
        }

        private sealed record StructuralRange(int Offset, int Length, string Kind)
        {
            public int End => checked(Offset + Length);
        }
    }

    private sealed class CopyOnWriteShopWriter
    {
        private readonly byte[] source;
        private readonly List<byte> bytes;

        public CopyOnWriteShopWriter(byte[] source)
        {
            this.source = source.ToArray();
            bytes = source.ToList();
        }

        public void AppendSingleShop(SourceShopLayout layout, SwShSingleShopRecord shop)
        {
            if (layout.Inventories.Count != 1)
            {
                throw new InvalidDataException("Single shop layout does not contain exactly one inventory.");
            }

            EnsureShopCanBeCloned(layout);
            var shopTableOffset = AppendTableClone(layout.Table, alignment: sizeof(ulong));
            var inventoryTableOffset = AppendTableClone(layout.Inventories[0].Table, alignment: sizeof(uint));
            var itemsVectorOffset = AppendIntVector(shop.Inventory.Items);
            PatchTableUOffset(inventoryTableOffset, layout.Inventories[0].Table, fieldIndex: 0, itemsVectorOffset);
            PatchTableUOffset(shopTableOffset, layout.Table, fieldIndex: 1, inventoryTableOffset);
            PatchUOffset(layout.RootVectorElementOffset, shopTableOffset);
        }

        public void AppendMultiShop(SourceShopLayout layout, SwShMultiShopRecord shop)
        {
            if (layout.Inventories.Count != shop.Inventories.Count)
            {
                throw new InvalidDataException("Multi shop inventory layout count changed unexpectedly.");
            }

            EnsureShopCanBeCloned(layout);
            var shopTableOffset = AppendTableClone(layout.Table, alignment: sizeof(ulong));
            var inventoryVector = AppendTableVector(shop.Inventories.Count);
            PatchTableUOffset(shopTableOffset, layout.Table, fieldIndex: 1, inventoryVector.VectorOffset);
            for (var inventoryIndex = 0; inventoryIndex < shop.Inventories.Count; inventoryIndex++)
            {
                var inventoryLayout = layout.Inventories[inventoryIndex];
                var inventoryTableOffset = AppendTableClone(inventoryLayout.Table, alignment: sizeof(uint));
                PatchUOffset(inventoryVector.ElementOffsets[inventoryIndex], inventoryTableOffset);
                var itemsVectorOffset = AppendIntVector(shop.Inventories[inventoryIndex].Items);
                PatchTableUOffset(inventoryTableOffset, inventoryLayout.Table, fieldIndex: 0, itemsVectorOffset);
            }

            PatchUOffset(layout.RootVectorElementOffset, shopTableOffset);
        }

        public byte[] ToArray()
        {
            return bytes.ToArray();
        }

        private static void EnsureShopCanBeCloned(SourceShopLayout layout)
        {
            if (layout.Table.HasMaterializedFieldAtOrAfter(fieldIndex: 2))
            {
                throw new InvalidDataException(
                    "Edited shop table contains an unknown materialized field and cannot be relocated safely.");
            }

            if (layout.Inventories.Any(inventory =>
                    inventory.Table.HasMaterializedFieldAtOrAfter(fieldIndex: 1)))
            {
                throw new InvalidDataException(
                    "Edited shop inventory table contains an unknown materialized field and cannot be relocated safely.");
            }
        }

        private int AppendTableClone(TableLayout layout, int alignment)
        {
            var sourceCongruence = layout.TableOffset & (alignment - 1);
            while (((Position + layout.VTableLength) & (alignment - 1)) != sourceCongruence)
            {
                bytes.Add(0);
            }

            var vtableOffset = Position;
            AppendBytes(source.AsSpan(layout.VTableOffset, layout.VTableLength));
            var tableOffset = Position;
            AppendBytes(source.AsSpan(layout.TableOffset, layout.ObjectLength));
            WriteInt32At(tableOffset, checked(tableOffset - vtableOffset));
            return tableOffset;
        }

        private void PatchTableUOffset(
            int clonedTableOffset,
            TableLayout sourceLayout,
            int fieldIndex,
            int targetOffset)
        {
            var fieldOffset = ReadTableFieldOffset(sourceLayout, fieldIndex);
            if (fieldOffset == 0)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            PatchUOffset(checked(clonedTableOffset + fieldOffset), targetOffset);
        }

        private VectorFields AppendTableVector(int count)
        {
            Align(sizeof(uint));
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

        private int AppendIntVector(IReadOnlyList<int> values)
        {
            Align(sizeof(uint));
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
                throw new InvalidDataException("FlatBuffer copy-on-write target must point forward.");
            }

            WriteUInt32At(sourceOffset, checked((uint)(targetOffset - sourceOffset)));
        }

        private void Align(int alignment)
        {
            while ((Position & (alignment - 1)) != 0)
            {
                bytes.Add(0);
            }
        }

        private void AppendBytes(ReadOnlySpan<byte> values)
        {
            foreach (var value in values)
            {
                bytes.Add(value);
            }
        }

        private int Position => bytes.Count;

        private void WriteInt32(int value)
        {
            var offset = Grow(sizeof(int));
            BinaryPrimitives.WriteInt32LittleEndian(
                CollectionsMarshal.AsSpan(bytes).Slice(offset, sizeof(int)),
                value);
        }

        private void WriteUInt32(uint value)
        {
            var offset = Grow(sizeof(uint));
            BinaryPrimitives.WriteUInt32LittleEndian(
                CollectionsMarshal.AsSpan(bytes).Slice(offset, sizeof(uint)),
                value);
        }

        private void WriteInt32At(int offset, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                CollectionsMarshal.AsSpan(bytes).Slice(offset, sizeof(int)),
                value);
        }

        private void WriteUInt32At(int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                CollectionsMarshal.AsSpan(bytes).Slice(offset, sizeof(uint)),
                value);
        }

        private int Grow(int count)
        {
            var offset = bytes.Count;
            for (var index = 0; index < count; index++)
            {
                bytes.Add(0);
            }

            return offset;
        }

        private sealed record VectorFields(
            int VectorOffset,
            IReadOnlyList<int> ElementOffsets);
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
