// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShBseqFileTests
{
    [Fact]
    public void ParseReadsSwShCommandTableAndPayloadOffsets()
    {
        const ulong commandHash = 0x1122334455667788;
        var data = CreateBseqWithGroupOptions(commandHash);

        var file = SwShBseqFile.Parse(data);

        Assert.Equal(SwShBseqFile.ExpectedVersion, file.Version);
        Assert.Equal(123u, file.FrameCount);
        Assert.Equal(2u, file.GroupOptionCount);
        var definition = Assert.Single(file.CommandDefinitions);
        Assert.Equal(commandHash, definition.Hash);
        Assert.Equal(8, definition.PayloadLength);
        var command = Assert.Single(file.Commands);
        Assert.Equal(0x24, command.Offset);
        Assert.Equal(10u, command.StartFrame);
        Assert.Equal(20u, command.EndFrame);
        Assert.Equal(3u, command.GroupNumber);
        Assert.Equal(0x48, command.HashOffset);
        Assert.Equal(0x50, command.PayloadOffset);
        Assert.Equal(8, command.PayloadLength);
        Assert.Collection(
            command.GroupOptions,
            option =>
            {
                Assert.Equal(0xAABBCCDDEEFF0011ul, option.Hash);
                Assert.Equal(7u, option.Value);
            },
            option =>
            {
                Assert.Equal(0x2233445566778899ul, option.Hash);
                Assert.Equal(11u, option.Value);
            });
        Assert.Equal(100, SwShBseqFile.ReadInt32Parameter(data, command, 0));
        Assert.Equal(-3, SwShBseqFile.ReadInt32Parameter(data, command, 1));
    }

    [Fact]
    public void WriteInt32ParameterUpdatesCommandPayload()
    {
        const ulong commandHash = 0x1122334455667788;
        var data = CreateBseqWithGroupOptions(commandHash);
        var command = Assert.Single(SwShBseqFile.Parse(data).Commands);

        SwShBseqFile.WriteInt32Parameter(data, command, 1, 44);

        Assert.Equal(44, SwShBseqFile.ReadInt32Parameter(data, command, 1));
        Assert.Equal(commandHash, BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(command.HashOffset, sizeof(ulong))));
    }

    [Fact]
    public void ParseRejectsUnknownCommandHash()
    {
        var data = CreateBseqWithGroupOptions(0x1122334455667788);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0x48, sizeof(ulong)), 0x8877665544332211);

        var exception = Assert.Throws<InvalidDataException>(() => SwShBseqFile.Parse(data));

        Assert.Contains("Unknown BSEQ command hash", exception.Message, StringComparison.Ordinal);
    }

    private static byte[] CreateBseqWithGroupOptions(ulong commandHash)
    {
        var data = new byte[0x5C];
        Encoding.ASCII.GetBytes("SESD").CopyTo(data, 0x00);
        WriteU32(data, 0x04, SwShBseqFile.ExpectedVersion);
        WriteU32(data, 0x0C, 123);
        WriteU32(data, 0x10, 2);
        WriteU32(data, 0x14, 1);
        WriteU64(data, 0x18, commandHash);
        WriteU32(data, 0x20, 8);

        WriteU32(data, 0x24, 10);
        WriteU32(data, 0x28, 20);
        WriteU32(data, 0x2C, 3);
        WriteU64(data, 0x30, 0xAABBCCDDEEFF0011);
        WriteU32(data, 0x38, 7);
        WriteU64(data, 0x3C, 0x2233445566778899);
        WriteU32(data, 0x44, 11);
        WriteU64(data, 0x48, commandHash);
        WriteI32(data, 0x50, 100);
        WriteI32(data, 0x54, -3);
        WriteU32(data, 0x58, 0xFFFFFFFF);
        return data;
    }

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);
    }

    private static void WriteI32(byte[] data, int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), value);
    }

    private static void WriteU64(byte[] data, int offset, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, sizeof(ulong)), value);
    }
}
