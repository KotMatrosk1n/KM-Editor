// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Formats.SwSh;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShItemTableTests
{
    [Fact]
    public void WriteEditsPatchesItemMetadataAndPreservesPackedPouchFlags()
    {
        var table = SwShItemTable.Parse(CreateItemTable());

        var output = table.WriteEdits(
        [
            new SwShItemTableEdit(1, SwShItemTableField.Pouch, 4),
            new SwShItemTableEdit(1, SwShItemTableField.HealAmount, 254),
            new SwShItemTableEdit(1, SwShItemTableField.EvAttack, -10),
            new SwShItemTableEdit(1, SwShItemTableField.CanUseOnPokemon, 0),
        ]);
        var item = SwShItemTable.Parse(output).Records[1];

        Assert.Equal(SwShItemPouch.Items, item.Pouch);
        Assert.Equal(9, item.PouchFlags);
        Assert.Equal(254, item.HealAmount);
        Assert.Equal(-10, item.EvAttack);
        Assert.False(item.CanUseOnPokemon);
    }

    [Fact]
    public void WriteEditsRejectsOutOfRangeItemMetadataValues()
    {
        var table = SwShItemTable.Parse(CreateItemTable());

        Assert.Throws<ArgumentOutOfRangeException>(
            () => table.WriteEdits([new SwShItemTableEdit(1, SwShItemTableField.Pouch, 16)]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => table.WriteEdits([new SwShItemTableEdit(1, SwShItemTableField.EvHp, -129)]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => table.WriteEdits([new SwShItemTableEdit(1, SwShItemTableField.CanUseOnPokemon, 2)]));
    }

    [Fact]
    public void ParseAndWriteEditsHandleMachineMoveTable()
    {
        var table = SwShItemTable.Parse(CreateItemTable(includeMachineTable: true));
        var original = table.Records[1];
        Assert.Equal(10, original.MachineSlot);
        Assert.Equal((ushort)345, original.MachineMoveId);

        var output = table.WriteEdits([new SwShItemTableEdit(1, SwShItemTableField.MachineMove, 85)]);
        var item = SwShItemTable.Parse(output).Records[1];

        Assert.Equal(10, item.MachineSlot);
        Assert.Equal((ushort)85, item.MachineMoveId);
        Assert.Equal(SwShItemPouch.TMs, item.Pouch);
        Assert.Equal(10, item.GroupIndex);
    }

    [Fact]
    public void WriteEditsPatchesBattlePouchAndPreservesEveryOtherByte()
    {
        var source = CreateItemTable();
        var rowOffset = GetRowOffset(source, itemId: 1);
        source[rowOffset + 0x14] = 1;
        Array.Resize(ref source, source.Length + 3);
        source[^3] = 0xA5;
        source[^2] = 0x5A;
        source[^1] = 0xC3;
        var original = source.ToArray();

        var output = SwShItemTable.Parse(source).WriteEdits(
        [
            new SwShItemTableEdit(1, SwShItemTableField.BattlePouch, 2),
        ]);

        Assert.Equal(2, SwShItemTable.Parse(output).Records[1].BattlePouch);
        Assert.Equal([rowOffset + 0x14], GetChangedOffsets(original, output));
        Assert.Equal(original[^3..], output[^3..]);
    }

    [Fact]
    public void ParseRejectsRowsThatOverlapItemIndex()
    {
        var data = CreateItemTable();
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x40), 0x44);

        var exception = Assert.Throws<InvalidDataException>(() => SwShItemTable.Parse(data));

        Assert.Contains("overlaps the header or item index", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsTruncatedNonzeroMachineTable()
    {
        var data = CreateItemTable(includeMachineTable: true);
        Array.Resize(ref data, data.Length - 1);

        var exception = Assert.Throws<InvalidDataException>(() => SwShItemTable.Parse(data));

        Assert.Contains("machine table extends past", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsMachineTableThatOverlapsItemIndex()
    {
        var data = CreateItemTable(includeMachineTable: true);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x02), 1);

        var exception = Assert.Throws<InvalidDataException>(() => SwShItemTable.Parse(data));

        Assert.Contains("overlaps the header or item index", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsMachineTableThatOverlapsRows()
    {
        var data = CreateItemTable(includeMachineTable: true);
        var rowsStart = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x40));
        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(0x02),
            checked((ushort)((rowsStart - 0x44) / sizeof(ushort))));

        var exception = Assert.Throws<InvalidDataException>(() => SwShItemTable.Parse(data));

        Assert.Contains("overlaps the item row data", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsDuplicateNonzeroMachineOwners()
    {
        var data = CreateItemTable(includeMachineTable: true);
        WriteMachineEntry(data, slot: 11, itemId: 1, moveId: 86);

        var exception = Assert.Throws<InvalidDataException>(() => SwShItemTable.Parse(data));

        Assert.Contains("assigns item 1 to both slots 10 and 11", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseOnlyExposesMachineMoveWhenSlotOwnsItem()
    {
        var data = CreateItemTable(includeMachineTable: true);
        WriteMachineEntry(data, slot: 10, itemId: 2, moveId: 345);

        var table = SwShItemTable.Parse(data);

        Assert.Null(table.Records[1].MachineSlot);
        Assert.Null(table.Records[1].MachineMoveId);
        Assert.Throws<InvalidDataException>(
            () => table.WriteEdits([new SwShItemTableEdit(1, SwShItemTableField.MachineMove, 85)]));
    }

    [Fact]
    public void WriteEditsUsesFinalMachineLinkageRegardlessOfEditOrder()
    {
        var data = CreateItemTable(includeMachineTable: true);
        WriteMachineEntry(data, slot: 10, itemId: 0, moveId: 345);
        WriteMachineEntry(data, slot: 11, itemId: 1, moveId: 86);
        var table = SwShItemTable.Parse(data);
        Assert.Null(table.Records[1].MachineSlot);

        var output = table.WriteEdits(
        [
            new SwShItemTableEdit(1, SwShItemTableField.MachineMove, 85),
            new SwShItemTableEdit(1, SwShItemTableField.GroupIndex, 11),
        ]);
        var item = SwShItemTable.Parse(output).Records[1];

        Assert.Equal(11, item.MachineSlot);
        Assert.Equal((ushort)85, item.MachineMoveId);
        Assert.Equal((ushort)345, ReadMachineMove(output, slot: 10));
    }

    [Fact]
    public void WriteEditsRejectsMachineRelinkToSlotOwnedByAnotherItem()
    {
        var data = CreateItemTable(includeMachineTable: true);
        WriteMachineEntry(data, slot: 11, itemId: 2, moveId: 86);
        var table = SwShItemTable.Parse(data);

        var exception = Assert.Throws<InvalidDataException>(() => table.WriteEdits(
        [
            new SwShItemTableEdit(1, SwShItemTableField.MachineMove, 85),
            new SwShItemTableEdit(1, SwShItemTableField.GroupIndex, 11),
        ]));

        Assert.Contains("owned by item 2, not item 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteEditsRejectsMachineMoveWhenRowIsUnlinkedBySameBatch()
    {
        var table = SwShItemTable.Parse(CreateItemTable(includeMachineTable: true));

        var exception = Assert.Throws<InvalidDataException>(() => table.WriteEdits(
        [
            new SwShItemTableEdit(1, SwShItemTableField.MachineMove, 85),
            new SwShItemTableEdit(1, SwShItemTableField.GroupType, 0),
        ]));

        Assert.Contains("not linked to a TM/TR slot after applying", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteMachineMoveOnlyChangesMoveHalfAndPreservesTrailingBytes()
    {
        var source = CreateItemTable(includeMachineTable: true);
        Array.Resize(ref source, source.Length + 4);
        source[^4] = 0xDE;
        source[^3] = 0xAD;
        source[^2] = 0xBE;
        source[^1] = 0xEF;
        var original = source.ToArray();
        var moveOffset = GetMachineTableOffset(source) + (10 * sizeof(uint)) + sizeof(ushort);

        var output = SwShItemTable.Parse(source).WriteEdits(
        [
            new SwShItemTableEdit(1, SwShItemTableField.MachineMove, 85),
        ]);

        Assert.Equal([moveOffset, moveOffset + 1], GetChangedOffsets(original, output));
        Assert.Equal(original.AsSpan(moveOffset - sizeof(ushort), sizeof(ushort)), output.AsSpan(moveOffset - sizeof(ushort), sizeof(ushort)));
        Assert.Equal(original[^4..], output[^4..]);
    }

    [Fact]
    public void WriteEditsPatchesNamedBehaviorFlagsAndBoosts()
    {
        var data = CreateItemTable();
        var rowOffset = 0x44 + (2 * sizeof(ushort)) + 0x30;
        data[rowOffset + 0x1E] = 0x02;
        var table = SwShItemTable.Parse(data);

        var output = table.WriteEdits(
        [
            new SwShItemTableEdit(1, SwShItemTableField.CurePoison, 0),
            new SwShItemTableEdit(1, SwShItemTableField.CureBurn, 1),
            new SwShItemTableEdit(1, SwShItemTableField.LevelUpItem, 1),
            new SwShItemTableEdit(1, SwShItemTableField.AttackBoost, 6),
            new SwShItemTableEdit(1, SwShItemTableField.DefenseBoost, 3),
            new SwShItemTableEdit(1, SwShItemTableField.CriticalHitBoost, 2),
            new SwShItemTableEdit(1, SwShItemTableField.PpMaxFlag, 1),
            new SwShItemTableEdit(1, SwShItemTableField.RestoreHpFlag, 1),
            new SwShItemTableEdit(1, SwShItemTableField.SpecialAttackEvFlag, 1),
            new SwShItemTableEdit(1, SwShItemTableField.Friendship2Flag, 1),
        ]);
        var item = SwShItemTable.Parse(output).Records[1];

        Assert.Equal(0x04, item.CureStatusFlags);
        Assert.Equal(0x64, item.Boost0);
        Assert.Equal(0x03, item.Boost1);
        Assert.Equal(0xA0, item.Boost3);
        Assert.Equal(0x84, item.UseFlags1);
        Assert.Equal(0x08, item.UseFlags2);
    }

    [Fact]
    public void WriteRoyalCandyRowAppendsUniqueKeyItemRowAndPreservesBaseDestination()
    {
        var data = CreateRoyalCandyItemTable();
        var table = SwShItemTable.Parse(data);

        var output = table.WriteRoyalCandyRow(table, templateItemId: 50, targetItemId: 1128);
        var outputTable = SwShItemTable.Parse(output);
        var royalCandy = outputTable.Records[1128];

        Assert.Equal(data.Length + 0x30, output.Length);
        Assert.Equal(51, table.Records[1128].RawRowIndex);
        Assert.Equal(52, royalCandy.RawRowIndex);
        Assert.Equal(1u, royalCandy.BuyPrice);
        Assert.Equal(0u, royalCandy.WattsPrice);
        Assert.Equal(20u, royalCandy.AlternatePrice);
        Assert.Equal(SwShItemPouch.KeyItems, royalCandy.Pouch);
        Assert.Equal(7, royalCandy.PouchFlags);
        Assert.Equal(1, royalCandy.FieldUseType);
        Assert.True(royalCandy.CanUseOnPokemon);
        Assert.Equal(9, royalCandy.ItemType);
        Assert.Equal(4, royalCandy.SortIndex);
        Assert.Equal(50, royalCandy.ItemSprite);
        Assert.Equal(0, royalCandy.GroupType);
        Assert.Equal(0, royalCandy.GroupIndex);
        Assert.Equal(0x04, royalCandy.Boost0 & 0x04);

        var rowsStart = BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(0x40));
        var originalTargetOffset = rowsStart + (51 * 0x30);
        Assert.Equal(
            data.AsSpan(originalTargetOffset, 0x30).ToArray(),
            output.AsSpan(originalTargetOffset, 0x30).ToArray());
    }

    [Fact]
    public void WriteRoyalCandyRowRefreshesExistingRoyalCandyRow()
    {
        var data = CreateRoyalCandyItemTable();
        var baseTable = SwShItemTable.Parse(data);
        var firstOutput = baseTable.WriteRoyalCandyRow(baseTable, templateItemId: 50, targetItemId: 1128);
        var firstTable = SwShItemTable.Parse(firstOutput);

        var secondOutput = firstTable.WriteRoyalCandyRow(baseTable, templateItemId: 50, targetItemId: 1128);
        var secondTable = SwShItemTable.Parse(secondOutput);

        Assert.Equal(firstOutput.Length, secondOutput.Length);
        Assert.Equal(firstTable.Records[1128].RawRowIndex, secondTable.Records[1128].RawRowIndex);
        Assert.Equal(SwShItemPouch.KeyItems, secondTable.Records[1128].Pouch);
        Assert.Equal(9, secondTable.Records[1128].ItemType);
        Assert.Equal(
            data,
            secondTable.RestoreRoyalCandyRowFromBase(
                baseTable,
                templateItemId: 50,
                targetItemId: 1128));
    }

    [Fact]
    public void WriteRoyalCandyRowInsertsBeforeTrailingDataAndShiftsMachinePointer()
    {
        const int rowSize = 0x30;
        const int machineTableLength = 200 * sizeof(uint);
        var itemData = CreateRoyalCandyItemTable();
        var machineOffset = itemData.Length;
        var data = new byte[itemData.Length + machineTableLength + 5];
        itemData.CopyTo(data, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(0x02),
            checked((ushort)((machineOffset - 0x44) / sizeof(ushort))));
        var trailingMarker = new byte[] { 0x91, 0x82, 0x73, 0x64, 0x55 };
        trailingMarker.CopyTo(data.AsSpan(machineOffset + machineTableLength));

        var baseTable = SwShItemTable.Parse(data);
        var output = baseTable.WriteRoyalCandyRow(baseTable, templateItemId: 50, targetItemId: 1128);

        Assert.Equal(data.Length + rowSize, output.Length);
        Assert.Equal(machineOffset + rowSize, GetMachineTableOffset(output));
        Assert.Equal(
            data.AsSpan(machineOffset, machineTableLength).ToArray(),
            output.AsSpan(machineOffset + rowSize, machineTableLength).ToArray());
        Assert.Equal(trailingMarker, output[^trailingMarker.Length..]);
        Assert.Equal(52, SwShItemTable.Parse(output).Records[1128].RawRowIndex);
    }

    [Fact]
    public void RestoreRoyalCandyRowReturnsInstalledTableToBaseBytes()
    {
        var data = CreateRoyalCandyItemTable();
        var baseTable = SwShItemTable.Parse(data);
        var installed = baseTable.WriteRoyalCandyRow(baseTable, templateItemId: 50, targetItemId: 1128);

        var restored = SwShItemTable.Parse(installed).RestoreRoyalCandyRowFromBase(
            baseTable,
            templateItemId: 50,
            targetItemId: 1128);

        Assert.Equal(data, restored);
    }

    [Fact]
    public void RestoreRoyalCandyRowCompactsOwnedRowAndPreservesLaterRows()
    {
        const int rowSize = 0x30;
        var data = CreateRoyalCandyItemTable();
        var baseTable = SwShItemTable.Parse(data);
        var installed = baseTable.WriteRoyalCandyRow(baseTable, templateItemId: 50, targetItemId: 1128);
        var withLaterRow = new byte[installed.Length + rowSize];
        installed.CopyTo(withLaterRow, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(withLaterRow.AsSpan(0x04), 54);
        BinaryPrimitives.WriteUInt16LittleEndian(
            withLaterRow.AsSpan(0x44 + sizeof(ushort)),
            53);
        withLaterRow[^1] = 0x7A;

        var restored = SwShItemTable.Parse(withLaterRow).RestoreRoyalCandyRowFromBase(
            baseTable,
            templateItemId: 50,
            targetItemId: 1128);
        var restoredTable = SwShItemTable.Parse(restored);

        Assert.Equal(withLaterRow.Length - rowSize, restored.Length);
        Assert.Equal(51, restoredTable.Records[1128].RawRowIndex);
        Assert.Equal(52, restoredTable.Records[1].RawRowIndex);
        Assert.Equal(0x7A, restored[^1]);

        var changed = installed.ToArray();
        var ownedRowOffset = GetRowOffset(changed, 1128);
        changed[ownedRowOffset + 0x08] ^= 0x01;
        Assert.Throws<InvalidDataException>(() =>
            SwShItemTable.Parse(changed).RestoreRoyalCandyRowFromBase(
                baseTable,
                templateItemId: 50,
                targetItemId: 1128));
    }

    [Fact]
    public void RestoreRoyalCandyRowPreservesChangedBaseDestinationAndRejectsReownedDestination()
    {
        const int rowSize = 0x30;
        var data = CreateRoyalCandyItemTable();
        var baseTable = SwShItemTable.Parse(data);
        var installed = baseTable.WriteRoyalCandyRow(baseTable, templateItemId: 50, targetItemId: 1128);
        var rowsStart = BinaryPrimitives.ReadInt32LittleEndian(installed.AsSpan(0x40));
        var baseTargetOffset = rowsStart + (51 * rowSize);

        var changedDestination = installed.ToArray();
        changedDestination[baseTargetOffset + 0x08] ^= 0x01;
        var restoredChangedDestination = SwShItemTable.Parse(changedDestination).RestoreRoyalCandyRowFromBase(
            baseTable,
            templateItemId: 50,
            targetItemId: 1128);
        Assert.Equal(data.Length, restoredChangedDestination.Length);
        Assert.Equal(changedDestination[baseTargetOffset + 0x08], restoredChangedDestination[baseTargetOffset + 0x08]);
        Assert.Equal(51, SwShItemTable.Parse(restoredChangedDestination).Records[1128].RawRowIndex);

        var reownedDestination = installed.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(
            reownedDestination.AsSpan(0x44 + sizeof(ushort)),
            51);
        Assert.Throws<InvalidDataException>(() =>
            SwShItemTable.Parse(reownedDestination).RestoreRoyalCandyRowFromBase(
                baseTable,
                templateItemId: 50,
                targetItemId: 1128));
    }

    [Fact]
    public void RestoreRoyalCandyRowCleansLegacySecondExactRowAfterCorruptedFirstInstall()
    {
        const int rowSize = 0x30;
        var data = CreateRoyalCandyItemTable();
        var baseTable = SwShItemTable.Parse(data);
        var firstInstall = baseTable.WriteRoyalCandyRow(baseTable, templateItemId: 50, targetItemId: 1128);
        var exactRoyalRow = firstInstall.AsSpan(GetRowOffset(firstInstall, 1128), rowSize).ToArray();
        var legacyCorruptedInstall = firstInstall.ToArray();
        var firstAppendedRowOffset = GetRowOffset(legacyCorruptedInstall, 1128);
        legacyCorruptedInstall[firstAppendedRowOffset + 0x20] ^= 0x80;

        var legacySecondInstall = new byte[legacyCorruptedInstall.Length + rowSize];
        legacyCorruptedInstall.CopyTo(legacySecondInstall, 0);
        exactRoyalRow.CopyTo(legacySecondInstall.AsSpan(firstAppendedRowOffset, rowSize));
        exactRoyalRow.CopyTo(legacySecondInstall.AsSpan(legacyCorruptedInstall.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(legacySecondInstall.AsSpan(0x04), 54);
        BinaryPrimitives.WriteUInt16LittleEndian(
            legacySecondInstall.AsSpan(0x44 + (1128 * sizeof(ushort))),
            53);

        var restored = SwShItemTable.Parse(legacySecondInstall).RestoreRoyalCandyRowFromBase(
            baseTable,
            templateItemId: 50,
            targetItemId: 1128);
        var restoredTable = SwShItemTable.Parse(restored);

        Assert.Equal(51, restoredTable.Records[1128].RawRowIndex);
        Assert.Equal(data, restored);
    }

    [Fact]
    public void WriteRoyalCandyRowRejectsChangedInstalledRowWithoutAppending()
    {
        var data = CreateRoyalCandyItemTable();
        var baseTable = SwShItemTable.Parse(data);
        var installed = baseTable.WriteRoyalCandyRow(baseTable, templateItemId: 50, targetItemId: 1128);
        var corrupted = installed.ToArray();
        corrupted[GetRowOffset(corrupted, 1128) + 0x20] ^= 0x80;
        var before = corrupted.ToArray();

        var exception = Assert.Throws<InvalidDataException>(() =>
            SwShItemTable.Parse(corrupted).WriteRoyalCandyRow(
                baseTable,
                templateItemId: 50,
                targetItemId: 1128));

        Assert.Contains("will not append another row", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, corrupted);
    }

    private static byte[] CreateItemTable(bool includeMachineTable = false)
    {
        const int headerSize = 0x44;
        const int rowSize = 0x30;
        const int machineTableCount = 200;
        const int itemCount = 2;
        const int rowsStart = headerSize + (itemCount * sizeof(ushort));
        var machineTableOffset = rowsStart + (itemCount * rowSize);
        var data = new byte[machineTableOffset + (includeMachineTable ? machineTableCount * sizeof(uint) : 0)];

        BinaryPrimitives.WriteUInt16LittleEndian(data, checked((ushort)itemCount));
        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(0x02),
            includeMachineTable ? checked((ushort)((machineTableOffset - headerSize) / 2)) : (ushort)0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x04), checked((ushort)itemCount));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x40), rowsStart);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x44), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x46), 1);

        var rowOffset = rowsStart + rowSize;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rowOffset), 300);
        data[rowOffset + 0x11] = (byte)((9 << 4) | (int)(includeMachineTable ? SwShItemPouch.TMs : SwShItemPouch.Medicine));
        data[rowOffset + 0x13] = includeMachineTable ? (byte)2 : (byte)0;
        data[rowOffset + 0x15] = 1;
        data[rowOffset + 0x1C] = includeMachineTable ? (byte)4 : (byte)0;
        data[rowOffset + 0x1D] = includeMachineTable ? (byte)10 : (byte)0;
        data[rowOffset + 0x26] = 5;
        data[rowOffset + 0x2B] = 20;
        if (includeMachineTable)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(machineTableOffset + (10 * sizeof(uint))), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(machineTableOffset + (10 * sizeof(uint)) + 2), 345);
        }

        return data;
    }

    private static int GetRowOffset(byte[] data, int itemId)
    {
        var rowsStart = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x40));
        var rowIndex = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x44 + (itemId * sizeof(ushort))));
        return rowsStart + (rowIndex * 0x30);
    }

    private static int GetMachineTableOffset(byte[] data)
    {
        return 0x44 + (BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x02)) * sizeof(ushort));
    }

    private static void WriteMachineEntry(byte[] data, int slot, ushort itemId, ushort moveId)
    {
        var entryOffset = GetMachineTableOffset(data) + (slot * sizeof(uint));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entryOffset), itemId);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entryOffset + sizeof(ushort)), moveId);
    }

    private static ushort ReadMachineMove(byte[] data, int slot)
    {
        var moveOffset = GetMachineTableOffset(data) + (slot * sizeof(uint)) + sizeof(ushort);
        return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(moveOffset));
    }

    private static int[] GetChangedOffsets(byte[] before, byte[] after)
    {
        Assert.Equal(before.Length, after.Length);
        return Enumerable.Range(0, before.Length)
            .Where(offset => before[offset] != after[offset])
            .ToArray();
    }

    private static byte[] CreateRoyalCandyItemTable()
    {
        const int headerSize = 0x44;
        const int rowSize = 0x30;
        const int itemCount = 1129;
        const int rawRowCount = 52;
        const int rowsStart = headerSize + (itemCount * sizeof(ushort));
        var data = new byte[rowsStart + (rawRowCount * rowSize)];

        BinaryPrimitives.WriteUInt16LittleEndian(data, checked((ushort)itemCount));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x04), checked((ushort)rawRowCount));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x40), rowsStart);

        for (var itemId = 0; itemId < itemCount; itemId++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x44 + (itemId * sizeof(ushort))), 0);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x44 + (50 * sizeof(ushort))), 50);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x44 + (1128 * sizeof(ushort))), 51);

        var rareCandyOffset = rowsStart + (50 * rowSize);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rareCandyOffset), 10000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rareCandyOffset + 0x08), 20);
        data[rareCandyOffset + 0x11] = (byte)((7 << 4) | (int)SwShItemPouch.Items);
        data[rareCandyOffset + 0x13] = 1;
        data[rareCandyOffset + 0x15] = 1;
        data[rareCandyOffset + 0x16] = 1;
        data[rareCandyOffset + 0x18] = 4;
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(rareCandyOffset + 0x1A), 50);
        data[rareCandyOffset + 0x1F] = 0x04;

        var targetOffset = rowsStart + (51 * rowSize);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(targetOffset), 10000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(targetOffset + 0x08), 3);
        data[targetOffset + 0x11] = (byte)SwShItemPouch.Items;
        data[targetOffset + 0x13] = 1;
        data[targetOffset + 0x15] = 1;
        data[targetOffset + 0x16] = 1;
        data[targetOffset + 0x18] = 9;
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(targetOffset + 0x1A), 1128);

        return data;
    }
}
