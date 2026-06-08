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
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(machineTableOffset + (10 * sizeof(uint)) + 2), 345);
        }

        return data;
    }
}
