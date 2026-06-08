// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Buffers.Binary;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShTrainerDataFileTests
{
    [Fact]
    public void ParseReadsTrainerMetadata()
    {
        var file = SwShTrainerDataFile.Parse(CreateTrainerData());

        Assert.Equal(5, file.Record.ClassId);
        Assert.Equal(1, file.Record.BattleMode);
        Assert.Equal(2, file.Record.PokemonCount);
        Assert.Equal([1, 2, 3, 4], file.Record.Items);
        Assert.Equal(0xAABBCC4Du, file.Record.AiFlags);
        Assert.True(file.Record.Heal);
        Assert.Equal(24, file.Record.Money);
        Assert.Equal(1234, file.Record.Gift);
    }

    [Fact]
    public void WriteEditsPatchesTrainerMetadataAndPreservesUnknownAiBits()
    {
        var file = SwShTrainerDataFile.Parse(CreateTrainerData());

        var output = file.WriteEdits(
        [
            new SwShTrainerDataEdit(SwShTrainerDataField.Item1Id, 10),
            new SwShTrainerDataEdit(SwShTrainerDataField.Item2Id, 11),
            new SwShTrainerDataEdit(SwShTrainerDataField.Item3Id, 12),
            new SwShTrainerDataEdit(SwShTrainerDataField.Item4Id, 13),
            new SwShTrainerDataEdit(SwShTrainerDataField.AiFlags, 0x3F),
            new SwShTrainerDataEdit(SwShTrainerDataField.Heal, 0),
            new SwShTrainerDataEdit(SwShTrainerDataField.Money, 99),
            new SwShTrainerDataEdit(SwShTrainerDataField.Gift, 4321),
        ]);

        var record = SwShTrainerDataFile.Parse(output).Record;
        Assert.Equal([10, 11, 12, 13], record.Items);
        Assert.Equal(0xAABBCC3Fu, record.AiFlags);
        Assert.False(record.Heal);
        Assert.Equal(99, record.Money);
        Assert.Equal(4321, record.Gift);
        Assert.Equal(2, record.PokemonCount);
    }

    private static byte[] CreateTrainerData()
    {
        var data = new byte[SwShTrainerDataFile.Size];
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x00), 5);
        data[0x02] = 1;
        data[0x03] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x04), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x06), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x08), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x0A), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x0C), 0xAABBCC4Du);
        data[0x10] = 1;
        data[0x11] = 24;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x12), 1234);

        return data;
    }
}
