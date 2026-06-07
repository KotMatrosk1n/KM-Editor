// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Formats.SwSh;

namespace KM.SwSh.Tests.Items;

internal static class SwShItemTestFixtures
{
    private const int HeaderSize = 0x44;
    private const int RowSize = 0x30;
    private const int EntryTableOffset = 0x44;
    private const int RowsStartOffset = 0x40;

    public static byte[] CreateItemTable(params ItemFixtureRecord[] records)
    {
        var itemCount = records.Length;
        var maxRowIndex = records.Length == 0
            ? 0
            : records.Max(record => record.RawRowIndex) + 1;
        var rowsStart = HeaderSize + (itemCount * sizeof(ushort));
        var data = new byte[rowsStart + (maxRowIndex * RowSize)];

        BinaryPrimitives.WriteUInt16LittleEndian(data, checked((ushort)itemCount));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x02), 0);
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
}

internal sealed record ItemFixtureRecord(
    int ItemId,
    int RawRowIndex,
    int BuyPrice,
    int WattsPrice,
    int AlternatePrice,
    SwShItemPouch Pouch);
