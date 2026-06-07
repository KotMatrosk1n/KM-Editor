// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Formats.SwSh;

namespace KM.Integration.Tests.Tools;

internal static class SwShItemBridgeFixtures
{
    private const int HeaderSize = 0x44;
    private const int RowSize = 0x30;
    private const int EntryTableOffset = 0x44;
    private const int RowsStartOffset = 0x40;

    public static byte[] CreateItemTable(params ItemBridgeFixtureRecord[] records)
    {
        var itemCount = records.Length;
        var maxRowIndex = records.Length == 0
            ? 0
            : records.Max(record => record.RawRowIndex) + 1;
        var rowsStart = HeaderSize + (itemCount * sizeof(ushort));
        var data = new byte[rowsStart + (maxRowIndex * RowSize)];

        BinaryPrimitives.WriteUInt16LittleEndian(data, checked((ushort)itemCount));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x04), checked((ushort)maxRowIndex));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(RowsStartOffset), rowsStart);

        foreach (var record in records)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                data.AsSpan(EntryTableOffset + (record.ItemId * sizeof(ushort))),
                checked((ushort)record.RawRowIndex));
            var rowOffset = rowsStart + (record.RawRowIndex * RowSize);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rowOffset), checked((uint)record.BuyPrice));
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rowOffset + 0x04), checked((uint)record.WattsPrice));
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rowOffset + 0x08), checked((uint)record.AlternatePrice));
            data[rowOffset + 0x11] = (byte)record.Pouch;
        }

        return data;
    }

    public static byte[] CreateItemNames(params string[] names)
    {
        return SwShGameTextFile.Write(names.Select(name => new SwShGameTextLine(name, Flags: 0)).ToArray());
    }

    public static void WriteBaseItems(TemporaryBridgeProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/pml/item/item.dat",
            CreateItemTable(
                new ItemBridgeFixtureRecord(0, 0, 0, 0, 0, SwShItemPouch.Items),
                new ItemBridgeFixtureRecord(1, 1, 300, 15, 3, SwShItemPouch.Medicine),
                new ItemBridgeFixtureRecord(2, 2, 200, 10, 5, SwShItemPouch.Medicine)));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            CreateItemNames("None", "Potion", "Antidote"));
    }
}

internal sealed record ItemBridgeFixtureRecord(
    int ItemId,
    int RawRowIndex,
    int BuyPrice,
    int WattsPrice,
    int AlternatePrice,
    SwShItemPouch Pouch);
