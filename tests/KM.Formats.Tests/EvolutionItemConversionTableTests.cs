// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Formats.Pokemon;
using Xunit;

namespace KM.Formats.Tests;

public sealed class EvolutionItemConversionTableTests
{
    [Fact]
    public void WriteAndReadPreserveItemParameterMappingsAndFreeSlots()
    {
        EvolutionItemConversion[] rows =
        [
            new(83, 4),
            new(0, 11),
            new(248, 12),
            new(80, 1),
        ];

        var bytes = EvolutionItemConversionTable.Write(rows);
        var parsed = EvolutionItemConversionTable.Read(bytes);

        Assert.Equal(rows, parsed);
    }

    [Fact]
    public void WriteLeavesUnusedItemFieldsAbsentLikeTheStockTable()
    {
        EvolutionItemConversion[] rows =
        [
            new(83, 4),
            new(0, 11),
            new(248, 12),
            new(0, 17),
        ];

        var bytes = EvolutionItemConversionTable.Write(rows);

        Assert.True(HasTableField(bytes, rowIndex: 0, fieldIndex: 0));
        Assert.False(HasTableField(bytes, rowIndex: 1, fieldIndex: 0));
        Assert.True(HasTableField(bytes, rowIndex: 2, fieldIndex: 0));
        Assert.False(HasTableField(bytes, rowIndex: 3, fieldIndex: 0));
        Assert.All(
            Enumerable.Range(0, rows.Length),
            rowIndex => Assert.True(HasTableField(bytes, rowIndex, fieldIndex: 1)));
    }

    [Fact]
    public void ReadRejectsTruncatedOrInvalidTables()
    {
        byte[][] invalidTables =
        [
            [],
            [4, 0, 0, 0],
            [255, 255, 255, 127, 0, 0, 0, 0],
        ];

        foreach (var bytes in invalidTables)
        {
            Assert.Throws<InvalidDataException>(() => EvolutionItemConversionTable.Read(bytes));
        }
    }

    private static bool HasTableField(byte[] bytes, int rowIndex, int fieldIndex)
    {
        var rootTable = ResolveForwardOffset(bytes, origin: 0);
        var rootVtable = rootTable - ReadInt32(bytes, rootTable);
        var rowsFieldOffset = ReadUInt16(bytes, rootVtable + (sizeof(ushort) * 2));
        var rowsReference = rootTable + rowsFieldOffset;
        var rowsVector = ResolveForwardOffset(bytes, rowsReference);
        var rowReference = rowsVector + sizeof(int) + (rowIndex * sizeof(int));
        var rowTable = ResolveForwardOffset(bytes, rowReference);
        var rowVtable = rowTable - ReadInt32(bytes, rowTable);
        var rowVtableLength = ReadUInt16(bytes, rowVtable);
        var fieldEntry = rowVtable + (sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort));
        return fieldEntry + sizeof(ushort) <= rowVtable + rowVtableLength
            && ReadUInt16(bytes, fieldEntry) != 0;
    }

    private static int ResolveForwardOffset(byte[] bytes, int origin) =>
        checked(origin + ReadInt32(bytes, origin));

    private static int ReadInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)));

    private static ushort ReadUInt16(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, sizeof(ushort)));
}
