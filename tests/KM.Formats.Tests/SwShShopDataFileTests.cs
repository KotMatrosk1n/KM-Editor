// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Formats.SwSh;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShShopDataFileTests
{
    private const ulong SingleHash = 0x1122334455667788;
    private const ulong MultiHash = 0x8877665544332211;

    [Fact]
    public void CanonicalWriteRoundTripsEveryShopKind()
    {
        var source = CreateData(
            [new SwShSingleShopRecord(SingleHash, new SwShShopInventory([1, 2]))],
            [new SwShMultiShopRecord(
                MultiHash,
                [new SwShShopInventory([3]), new SwShShopInventory([4, 5])])]);

        var parsed = SwShShopDataFile.Parse(source);

        Assert.Equal([1, 2], Assert.Single(parsed.SingleShops).Inventory.Items);
        Assert.Collection(
            Assert.Single(parsed.MultiShops).Inventories,
            inventory => Assert.Equal([3], inventory.Items),
            inventory => Assert.Equal([4, 5], inventory.Items));
    }

    [Fact]
    public void ParserAcceptsForwardAndSharedVTablesAndKeepsThemEditable()
    {
        var source = CreateData(
            [
                new SwShSingleShopRecord(SingleHash, new SwShShopInventory([1])),
                new SwShSingleShopRecord(SingleHash + 1, new SwShShopInventory([2])),
            ],
            []);
        var root = ReadUOffset(source, 0);
        var rootVTable = GetVTableOffset(source, root);
        var firstTable = GetRootShopTable(source, fieldIndex: 0, shopIndex: 0);
        var secondTable = GetRootShopTable(source, fieldIndex: 0, shopIndex: 1);
        var shopVTable = GetVTableOffset(source, firstTable);
        var bytes = source.ToList();

        var sharedForwardShopVTable = AppendVTable(bytes, source, shopVTable);
        var forwardRootVTable = AppendVTable(bytes, source, rootVTable);
        source = bytes.ToArray();
        PatchVTableOffset(source, firstTable, sharedForwardShopVTable);
        PatchVTableOffset(source, secondTable, sharedForwardShopVTable);
        PatchVTableOffset(source, root, forwardRootVTable);

        var parsed = SwShShopDataFile.Parse(source);
        Assert.Equal([1], parsed.SingleShops[0].Inventory.Items);
        Assert.Equal([2], parsed.SingleShops[1].Inventory.Items);

        var output = SwShShopDataFile.Parse(parsed.WriteEdits(
        [
            new SwShShopInventoryEdit(
                SwShShopKind.Single,
                SingleHash + 1,
                InventoryIndex: 0,
                Slot: 0,
                ItemId: 9,
                ShopIndex: 1),
        ]));
        Assert.Equal([1], output.SingleShops[0].Inventory.Items);
        Assert.Equal([9], output.SingleShops[1].Inventory.Items);
    }

    [Fact]
    public void CopyOnWritePreservesRealShopTableAlignmentCongruence()
    {
        var source = CreateRealShapedSingleShopData();
        var parsed = SwShShopDataFile.Parse(source);
        Assert.Equal([1, 2], Assert.Single(parsed.SingleShops).Inventory.Items);

        var output = parsed.WriteEdits(
        [
            new SwShShopInventoryEdit(
                SwShShopKind.Single,
                SingleHash,
                InventoryIndex: 0,
                Slot: 0,
                ItemId: 9,
                ShopIndex: 0),
        ]);

        Assert.Equal([9, 2], Assert.Single(SwShShopDataFile.Parse(output).SingleShops).Inventory.Items);
    }

    [Fact]
    public void ParsedNoOpAndSameValueEditAreByteIdentical()
    {
        var canonical = CreateData(
            [new SwShSingleShopRecord(SingleHash, new SwShShopInventory([1, 2]))],
            []);
        var source = canonical.Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }).ToArray();
        var parsed = SwShShopDataFile.Parse(source);

        Assert.Equal(source, parsed.WriteEdits([]));
        Assert.Equal(
            source,
            parsed.WriteEdits(
            [
                new SwShShopInventoryEdit(
                    SwShShopKind.Single,
                    SingleHash,
                    InventoryIndex: 0,
                    Slot: 0,
                    ItemId: 1,
                    ShopIndex: 0),
            ]));
    }

    [Fact]
    public void ParsedSingleEditPatchesOnlyRootElementBeforeAppendingClone()
    {
        var canonical = CreateData(
            [new SwShSingleShopRecord(SingleHash, new SwShShopInventory([1, 2]))],
            []);
        var source = canonical.Concat(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }).ToArray();
        var rootElementOffset = GetRootShopVectorElement(source, fieldIndex: 0, shopIndex: 0);

        var output = SwShShopDataFile.Parse(source).WriteEdits(
        [
            new SwShShopInventoryEdit(
                SwShShopKind.Single,
                SingleHash,
                InventoryIndex: 0,
                Slot: 0,
                ItemId: 0,
                SwShShopInventoryEditAction.Set,
                Items: [7, 8, 9],
                ShopIndex: 0),
        ]);

        Assert.True(output.Length > source.Length);
        AssertOriginalPrefixIsUnchangedExcept(source, output, rootElementOffset, sizeof(uint));
        Assert.Equal([7, 8, 9], Assert.Single(SwShShopDataFile.Parse(output).SingleShops).Inventory.Items);
    }

    [Fact]
    public void ParsedMultiEditsAreCombinedIntoOneClonedShopChain()
    {
        var source = CreateData(
            [],
            [new SwShMultiShopRecord(
                MultiHash,
                [new SwShShopInventory([1]), new SwShShopInventory([2])])]);
        var rootElementOffset = GetRootShopVectorElement(source, fieldIndex: 1, shopIndex: 0);

        var output = SwShShopDataFile.Parse(source).WriteEdits(
        [
            new SwShShopInventoryEdit(
                SwShShopKind.Multi,
                MultiHash,
                InventoryIndex: 0,
                Slot: 0,
                ItemId: 11,
                ShopIndex: 0),
            new SwShShopInventoryEdit(
                SwShShopKind.Multi,
                MultiHash,
                InventoryIndex: 1,
                Slot: 0,
                ItemId: 22,
                ShopIndex: 0),
        ]);

        AssertOriginalPrefixIsUnchangedExcept(source, output, rootElementOffset, sizeof(uint));
        Assert.Collection(
            Assert.Single(SwShShopDataFile.Parse(output).MultiShops).Inventories,
            inventory => Assert.Equal([11], inventory.Items),
            inventory => Assert.Equal([22], inventory.Items));
    }

    [Fact]
    public void DuplicateHashesRequirePhysicalIndexAndTargetExactShop()
    {
        var source = CreateData(
            [
                new SwShSingleShopRecord(SingleHash, new SwShShopInventory([1])),
                new SwShSingleShopRecord(SingleHash, new SwShShopInventory([2])),
            ],
            []);
        var parsed = SwShShopDataFile.Parse(source);

        Assert.Throws<InvalidDataException>(() => parsed.WriteEdits(
        [
            new SwShShopInventoryEdit(
                SwShShopKind.Single,
                SingleHash,
                InventoryIndex: 0,
                Slot: 0,
                ItemId: 9),
        ]));
        Assert.Throws<InvalidDataException>(() => parsed.WriteEdits(
        [
            new SwShShopInventoryEdit(
                SwShShopKind.Single,
                SingleHash,
                InventoryIndex: 0,
                Slot: 0,
                ItemId: 9,
                ShopIndex: -2),
        ]));

        var output = SwShShopDataFile.Parse(parsed.WriteEdits(
        [
            new SwShShopInventoryEdit(
                SwShShopKind.Single,
                SingleHash,
                InventoryIndex: 0,
                Slot: 0,
                ItemId: 9,
                ShopIndex: 1),
        ]));
        Assert.Equal([1], output.SingleShops[0].Inventory.Items);
        Assert.Equal([9], output.SingleShops[1].Inventory.Items);
    }

    [Fact]
    public void RootTableAliasIsIsolatedBeforeEditing()
    {
        var source = CreateData(
            [
                new SwShSingleShopRecord(SingleHash, new SwShShopInventory([1])),
                new SwShSingleShopRecord(SingleHash + 1, new SwShShopInventory([2])),
            ],
            []);
        var firstElement = GetRootShopVectorElement(source, fieldIndex: 0, shopIndex: 0);
        var secondElement = GetRootShopVectorElement(source, fieldIndex: 0, shopIndex: 1);
        PatchUOffset(source, secondElement, ReadUOffset(source, firstElement));
        var parsed = SwShShopDataFile.Parse(source);
        Assert.Equal(SingleHash, parsed.SingleShops[1].Hash);

        var output = SwShShopDataFile.Parse(parsed.WriteEdits(
        [
            new SwShShopInventoryEdit(
                SwShShopKind.Single,
                SingleHash,
                InventoryIndex: 0,
                Slot: 0,
                ItemId: 9,
                ShopIndex: 0),
        ]));

        Assert.Equal([9], output.SingleShops[0].Inventory.Items);
        Assert.Equal([1], output.SingleShops[1].Inventory.Items);
    }

    [Fact]
    public void InventoryTableAliasIsIsolatedBeforeEditing()
    {
        var source = CreateData(
            [],
            [new SwShMultiShopRecord(
                MultiHash,
                [new SwShShopInventory([1]), new SwShShopInventory([2])])]);
        var multiTable = GetRootShopTable(source, fieldIndex: 1, shopIndex: 0);
        var inventoryVector = ReadTableUOffset(source, multiTable, fieldIndex: 1);
        var firstElement = inventoryVector + sizeof(uint);
        var secondElement = firstElement + sizeof(uint);
        PatchUOffset(source, secondElement, ReadUOffset(source, firstElement));

        var output = SwShShopDataFile.Parse(SwShShopDataFile.Parse(source).WriteEdits(
        [
            new SwShShopInventoryEdit(
                SwShShopKind.Multi,
                MultiHash,
                InventoryIndex: 0,
                Slot: 0,
                ItemId: 9,
                ShopIndex: 0),
        ]));

        Assert.Equal([9], output.MultiShops[0].Inventories[0].Items);
        Assert.Equal([1], output.MultiShops[0].Inventories[1].Items);
    }

    [Fact]
    public void OmittedOptionalHashParsesAsZeroAndStaysOmittedWhenEdited()
    {
        var source = CreateData(
            [new SwShSingleShopRecord(SingleHash, new SwShShopInventory([1]))],
            []);
        var table = GetRootShopTable(source, fieldIndex: 0, shopIndex: 0);
        var vtable = table - BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(table, sizeof(int)));
        BinaryPrimitives.WriteUInt16LittleEndian(
            source.AsSpan(vtable + (sizeof(ushort) * 2), sizeof(ushort)),
            0);

        var parsed = SwShShopDataFile.Parse(source);
        Assert.Equal(0UL, Assert.Single(parsed.SingleShops).Hash);
        var output = SwShShopDataFile.Parse(parsed.WriteEdits(
        [
            new SwShShopInventoryEdit(
                SwShShopKind.Single,
                Hash: 0,
                InventoryIndex: 0,
                Slot: 0,
                ItemId: 2,
                ShopIndex: 0),
        ]));
        Assert.Equal(0UL, Assert.Single(output.SingleShops).Hash);
        Assert.Equal([2], output.SingleShops[0].Inventory.Items);
    }

    [Fact]
    public void ParserRejectsHugeVectorBeforeAllocation()
    {
        var source = CreateData([], []);
        var root = ReadUOffset(source, 0);
        var vector = ReadTableUOffset(source, root, fieldIndex: 0);
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(vector, sizeof(uint)), uint.MaxValue);

        Assert.Throws<InvalidDataException>(() => SwShShopDataFile.Parse(source));
    }

    [Fact]
    public void ParserRejectsInvalidVTableAndOverlappingFields()
    {
        var source = CreateData(
            [new SwShSingleShopRecord(SingleHash, new SwShShopInventory([1]))],
            []);
        var table = GetRootShopTable(source, fieldIndex: 0, shopIndex: 0);
        var invalidVtable = source.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(invalidVtable.AsSpan(table, sizeof(int)), 0);
        Assert.Throws<InvalidDataException>(() => SwShShopDataFile.Parse(invalidVtable));

        var validVtable = GetVTableOffset(source, table);
        var validVtableLength = BinaryPrimitives.ReadUInt16LittleEndian(
            source.AsSpan(validVtable, sizeof(ushort)));
        var oddVtableOffset = source.Length + 1;
        var oddVtable = source.Concat(new byte[validVtableLength + 1]).ToArray();
        source.AsSpan(validVtable, validVtableLength).CopyTo(
            oddVtable.AsSpan(oddVtableOffset, validVtableLength));
        PatchVTableOffset(oddVtable, table, oddVtableOffset);
        Assert.Throws<InvalidDataException>(() => SwShShopDataFile.Parse(oddVtable));

        var overlappingFields = source.ToArray();
        var vtable = table - BinaryPrimitives.ReadInt32LittleEndian(overlappingFields.AsSpan(table, sizeof(int)));
        var hashFieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(
            overlappingFields.AsSpan(vtable + (sizeof(ushort) * 2), sizeof(ushort)));
        BinaryPrimitives.WriteUInt16LittleEndian(
            overlappingFields.AsSpan(vtable + (sizeof(ushort) * 3), sizeof(ushort)),
            hashFieldOffset);
        Assert.Throws<InvalidDataException>(() => SwShShopDataFile.Parse(overlappingFields));
    }

    [Fact]
    public void ParserRejectsRootVectorsThatAliasAcrossSchemaKinds()
    {
        var source = CreateData([], []);
        var root = ReadUOffset(source, 0);
        var singleField = GetTableFieldAddress(source, root, fieldIndex: 0);
        var multiVector = ReadTableUOffset(source, root, fieldIndex: 1);
        PatchUOffset(source, singleField, multiVector);

        Assert.Throws<InvalidDataException>(() => SwShShopDataFile.Parse(source));
    }

    private static byte[] CreateData(
        IReadOnlyList<SwShSingleShopRecord> singleShops,
        IReadOnlyList<SwShMultiShopRecord> multiShops)
    {
        return new SwShShopDataFile(singleShops, multiShops).Write();
    }

    private static byte[] CreateRealShapedSingleShopData()
    {
        var data = new byte[92];

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, sizeof(uint)), 12);
        WriteVTable(data, offset: 4, length: 8, objectLength: 12, 4, 8);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12, sizeof(int)), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16, sizeof(uint)), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20, sizeof(uint)), 68);

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(24, sizeof(uint)), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28, sizeof(uint)), 16);

        WriteVTable(data, offset: 36, length: 8, objectLength: 16, 4, 12);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(44, sizeof(int)), 8);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(48, sizeof(ulong)), SingleHash);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(56, sizeof(uint)), 12);

        WriteVTable(data, offset: 62, length: 6, objectLength: 8, 4);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(68, sizeof(int)), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(72, sizeof(uint)), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(76, sizeof(uint)), 2);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(80, sizeof(int)), 1);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(84, sizeof(int)), 2);

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(88, sizeof(uint)), 0);
        return data;
    }

    private static void WriteVTable(
        byte[] data,
        int offset,
        ushort length,
        ushort objectLength,
        params ushort[] fieldOffsets)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)), length);
        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(offset + sizeof(ushort), sizeof(ushort)),
            objectLength);
        for (var index = 0; index < fieldOffsets.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                data.AsSpan(offset + (sizeof(ushort) * (index + 2)), sizeof(ushort)),
                fieldOffsets[index]);
        }
    }

    private static int GetRootShopVectorElement(byte[] data, int fieldIndex, int shopIndex)
    {
        var root = ReadUOffset(data, 0);
        var vector = ReadTableUOffset(data, root, fieldIndex);
        return checked(vector + sizeof(uint) + (shopIndex * sizeof(uint)));
    }

    private static int GetRootShopTable(byte[] data, int fieldIndex, int shopIndex)
    {
        return ReadUOffset(data, GetRootShopVectorElement(data, fieldIndex, shopIndex));
    }

    private static int ReadTableUOffset(byte[] data, int tableOffset, int fieldIndex)
    {
        return ReadUOffset(data, GetTableFieldAddress(data, tableOffset, fieldIndex));
    }

    private static int GetTableFieldAddress(byte[] data, int tableOffset, int fieldIndex)
    {
        var vtable = GetVTableOffset(data, tableOffset);
        var fieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(
            data.AsSpan(vtable + (sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort)), sizeof(ushort)));
        Assert.NotEqual(0, fieldOffset);
        return tableOffset + fieldOffset;
    }

    private static int GetVTableOffset(byte[] data, int tableOffset)
    {
        return checked(tableOffset - BinaryPrimitives.ReadInt32LittleEndian(
            data.AsSpan(tableOffset, sizeof(int))));
    }

    private static int AppendVTable(List<byte> destination, byte[] source, int vtableOffset)
    {
        if ((destination.Count & 1) != 0)
        {
            destination.Add(0);
        }

        var appendedOffset = destination.Count;
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(
            source.AsSpan(vtableOffset, sizeof(ushort)));
        destination.AddRange(source.AsSpan(vtableOffset, vtableLength).ToArray());
        return appendedOffset;
    }

    private static void PatchVTableOffset(byte[] data, int tableOffset, int vtableOffset)
    {
        BinaryPrimitives.WriteInt32LittleEndian(
            data.AsSpan(tableOffset, sizeof(int)),
            checked(tableOffset - vtableOffset));
    }

    private static int ReadUOffset(byte[] data, int sourceOffset)
    {
        return checked(sourceOffset + (int)BinaryPrimitives.ReadUInt32LittleEndian(
            data.AsSpan(sourceOffset, sizeof(uint))));
    }

    private static void PatchUOffset(byte[] data, int sourceOffset, int targetOffset)
    {
        Assert.True(targetOffset > sourceOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(sourceOffset, sizeof(uint)),
            checked((uint)(targetOffset - sourceOffset)));
    }

    private static void AssertOriginalPrefixIsUnchangedExcept(
        byte[] source,
        byte[] output,
        int changedOffset,
        int changedLength)
    {
        Assert.True(output.Length >= source.Length);
        for (var offset = 0; offset < source.Length; offset++)
        {
            if (offset >= changedOffset && offset < changedOffset + changedLength)
            {
                continue;
            }

            Assert.Equal(source[offset], output[offset]);
        }
    }
}
