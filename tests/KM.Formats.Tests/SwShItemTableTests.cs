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

    private static byte[] CreateItemTable()
    {
        const int headerSize = 0x44;
        const int rowSize = 0x30;
        const int itemCount = 2;
        const int rowsStart = headerSize + (itemCount * sizeof(ushort));
        var data = new byte[rowsStart + (itemCount * rowSize)];

        BinaryPrimitives.WriteUInt16LittleEndian(data, checked((ushort)itemCount));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x04), checked((ushort)itemCount));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x40), rowsStart);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x44), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x46), 1);

        var rowOffset = rowsStart + rowSize;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rowOffset), 300);
        data[rowOffset + 0x11] = (byte)((9 << 4) | (int)SwShItemPouch.Medicine);
        data[rowOffset + 0x15] = 1;
        data[rowOffset + 0x26] = 5;
        data[rowOffset + 0x2B] = 20;

        return data;
    }
}
