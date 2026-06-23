// SPDX-License-Identifier: GPL-3.0-only

using K4os.Compression.LZ4;
using KM.Formats.Executable;
using System.Buffers.Binary;
using Xunit;

namespace KM.Formats.Tests;

public sealed class NsoFileTests
{
    [Fact]
    public void ParseReadsUncompressedNsoSegments()
    {
        var text = new byte[] { 1, 2, 3, 4 };
        var ro = new byte[] { 5, 6 };
        var data = new byte[] { 7, 8, 9 };
        var nsoBytes = CreateNso(text, ro, data, NsoFlags.None);

        var nso = NsoFile.Parse(nsoBytes);

        Assert.Equal((uint)1, nso.Version);
        Assert.Equal(NsoFlags.None, nso.Flags);
        Assert.Equal(text, nso.Text.DecompressedData);
        Assert.Equal(ro, nso.Ro.DecompressedData);
        Assert.Equal(data, nso.Data.DecompressedData);
        Assert.Equal(NsoFile.ComputeHash(text), nso.Text.Hash);
    }

    [Fact]
    public void ParseDecodesCompressedTextSegment()
    {
        var text = Enumerable.Range(0, 64).Select(index => (byte)(index % 4)).ToArray();
        var ro = new byte[] { 5, 6 };
        var data = new byte[] { 7, 8, 9 };
        var nsoBytes = CreateNso(text, ro, data, NsoFlags.CompressedText);

        var nso = NsoFile.Parse(nsoBytes);

        Assert.Equal(NsoFlags.CompressedText, nso.Flags);
        Assert.Equal(text, nso.Text.DecompressedData);
        Assert.True(nso.Text.CompressedSize < text.Length);
    }

    [Fact]
    public void WriteUpdatesUncompressedTextAndSegmentHash()
    {
        var text = new byte[] { 1, 2, 3, 4 };
        var replacementText = new byte[] { 4, 3, 2, 1 };
        var ro = new byte[] { 5, 6 };
        var data = new byte[] { 7, 8, 9 };
        var nso = NsoFile.Parse(CreateNso(text, ro, data));

        var output = nso.Write(textDecompressedData: replacementText);
        var reparsed = NsoFile.Parse(output);

        Assert.Equal(replacementText, reparsed.Text.DecompressedData);
        Assert.Equal(ro, reparsed.Ro.DecompressedData);
        Assert.Equal(data, reparsed.Data.DecompressedData);
        Assert.Equal(NsoFile.ComputeHash(replacementText), reparsed.Text.Hash);
    }

    [Fact]
    public void WriteRecompressesCompressedTextAndKeepsItReadable()
    {
        var text = Enumerable.Range(0, 128).Select(index => (byte)(index % 4)).ToArray();
        var replacementText = Enumerable.Range(0, 128).Select(index => (byte)(3 - index % 4)).ToArray();
        var ro = new byte[] { 5, 6 };
        var data = new byte[] { 7, 8, 9 };
        var nso = NsoFile.Parse(CreateNso(text, ro, data, NsoFlags.CompressedText));

        var output = nso.Write(textDecompressedData: replacementText);
        var reparsed = NsoFile.Parse(output);

        Assert.Equal(NsoFlags.CompressedText, reparsed.Flags);
        Assert.Equal(replacementText, reparsed.Text.DecompressedData);
        Assert.Equal(NsoFile.ComputeHash(replacementText), reparsed.Text.Hash);
        Assert.True(reparsed.Text.CompressedSize < replacementText.Length);
    }

    [Fact]
    public void ParseRejectsInvalidMagic()
    {
        var bytes = new byte[NsoFile.HeaderSize];

        var exception = Assert.Throws<InvalidDataException>(() => NsoFile.Parse(bytes));

        Assert.Contains("NSO magic is invalid", exception.Message);
    }

    public static byte[] CreateNso(
        byte[] text,
        byte[] ro,
        byte[] data,
        NsoFlags flags = NsoFlags.None)
    {
        var textSegment = flags.HasFlag(NsoFlags.CompressedText) ? Compress(text) : text;
        var roSegment = flags.HasFlag(NsoFlags.CompressedRo) ? Compress(ro) : ro;
        var dataSegment = flags.HasFlag(NsoFlags.CompressedData) ? Compress(data) : data;
        var textOffset = NsoFile.HeaderSize;
        var roOffset = Align(textOffset + textSegment.Length, 0x10);
        var dataOffset = Align(roOffset + roSegment.Length, 0x10);
        var output = new byte[Align(dataOffset + dataSegment.Length, 0x10)];

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), NsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x04), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x0C), (uint)flags);
        WriteSegmentHeader(output, 0x10, textOffset, 0, text.Length);
        WriteSegmentHeader(output, 0x20, roOffset, text.Length, ro.Length);
        WriteSegmentHeader(output, 0x30, dataOffset, text.Length + ro.Length, data.Length);
        output.AsSpan(0x40, 0x20).Fill(0xAB);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x60), textSegment.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x64), roSegment.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x68), dataSegment.Length);
        NsoFile.ComputeHash(text).CopyTo(output.AsSpan(0xA0));
        NsoFile.ComputeHash(ro).CopyTo(output.AsSpan(0xC0));
        NsoFile.ComputeHash(data).CopyTo(output.AsSpan(0xE0));
        textSegment.CopyTo(output.AsSpan(textOffset));
        roSegment.CopyTo(output.AsSpan(roOffset));
        dataSegment.CopyTo(output.AsSpan(dataOffset));

        return output;
    }

    private static void WriteSegmentHeader(
        byte[] output,
        int offset,
        int fileOffset,
        int memoryOffset,
        int decompressedSize)
    {
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset), fileOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x04), memoryOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x08), decompressedSize);
    }

    private static byte[] Compress(byte[] data)
    {
        var output = new byte[LZ4Codec.MaximumOutputSize(data.Length)];
        var length = LZ4Codec.Encode(data, 0, data.Length, output, 0, output.Length);
        Array.Resize(ref output, length);
        return output;
    }

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }
}
