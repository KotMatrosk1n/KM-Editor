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
    public void WriteSerializesAndRehashesInPlaceUncompressedMutation()
    {
        var nso = NsoFile.Parse(CreateNso([1, 2, 3, 4], [5, 6], [7, 8, 9]));
        nso.Text.DecompressedData[0] = 0x44;
        var expected = nso.Text.DecompressedData.ToArray();

        var reparsed = NsoFile.Parse(nso.Write());

        Assert.Equal(expected, reparsed.Text.DecompressedData);
        Assert.Equal(NsoFile.ComputeHash(expected), reparsed.Text.Hash);
    }

    [Fact]
    public void WriteSerializesAndRehashesInPlaceCompressedMutation()
    {
        var text = Enumerable.Repeat((byte)0x11, 128).ToArray();
        var nso = NsoFile.Parse(CreateNso(text, [5, 6], [7, 8, 9], NsoFlags.CompressedText));
        nso.Text.DecompressedData.AsSpan().Fill(0x22);
        var expected = nso.Text.DecompressedData.ToArray();

        var reparsed = NsoFile.Parse(nso.Write());

        Assert.Equal(expected, reparsed.Text.DecompressedData);
        Assert.Equal(NsoFile.ComputeHash(expected), reparsed.Text.Hash);
        Assert.True(reparsed.Flags.HasFlag(NsoFlags.CompressedText));
    }

    [Fact]
    public void WriteIgnoresPublicCompressedDataAndHashMutation()
    {
        var text = Enumerable.Repeat((byte)0x11, 128).ToArray();
        var nso = NsoFile.Parse(CreateNso(
            text,
            [5, 6],
            [7, 8, 9],
            NsoFlags.CompressedText | NsoFlags.CheckHashText));
        var expectedCompressedData = nso.Text.CompressedData.ToArray();
        var expectedHash = nso.Text.Hash.ToArray();
        nso.Text.CompressedData.AsSpan().Fill(0xFF);
        nso.Text.Hash.AsSpan().Clear();

        var reparsed = NsoFile.Parse(nso.Write());

        Assert.Equal(text, reparsed.Text.DecompressedData);
        Assert.Equal(expectedCompressedData, reparsed.Text.CompressedData);
        Assert.Equal(expectedHash, reparsed.Text.Hash);
        Assert.True(reparsed.Flags.HasFlag(NsoFlags.CheckHashText));
    }

    [Fact]
    public void WriteReencodesWhenCompressionFlagIsEnabled()
    {
        var text = Enumerable.Repeat((byte)0x11, 128).ToArray();
        var nso = NsoFile.Parse(CreateNso(text, [5, 6], [7, 8, 9]));
        var changed = nso with { Flags = nso.Flags | NsoFlags.CompressedText };

        var reparsed = NsoFile.Parse(changed.Write());

        Assert.True(reparsed.Flags.HasFlag(NsoFlags.CompressedText));
        Assert.Equal(text, reparsed.Text.DecompressedData);
        Assert.True(reparsed.Text.CompressedSize < text.Length);
    }

    [Fact]
    public void WriteReencodesWhenCompressionFlagIsDisabled()
    {
        var text = Enumerable.Repeat((byte)0x11, 128).ToArray();
        var nso = NsoFile.Parse(CreateNso(text, [5, 6], [7, 8, 9], NsoFlags.CompressedText));
        var changed = nso with { Flags = nso.Flags & ~NsoFlags.CompressedText };

        var reparsed = NsoFile.Parse(changed.Write());

        Assert.False(reparsed.Flags.HasFlag(NsoFlags.CompressedText));
        Assert.Equal(text, reparsed.Text.DecompressedData);
        Assert.Equal(text.Length, reparsed.Text.CompressedSize);
    }

    [Fact]
    public void WriteRehashesWhenHashVerificationIsEnabled()
    {
        var text = new byte[] { 1, 2, 3, 4 };
        var bytes = CreateNso(text, [5, 6], [7, 8, 9]);
        bytes.AsSpan(0xA0, 0x20).Fill(0xCC);
        var nso = NsoFile.Parse(bytes);
        var changed = nso with { Flags = nso.Flags | NsoFlags.CheckHashText };

        var reparsed = NsoFile.Parse(changed.Write());

        Assert.True(reparsed.Flags.HasFlag(NsoFlags.CheckHashText));
        Assert.Equal(NsoFile.ComputeHash(text), reparsed.Text.Hash);
    }

    [Fact]
    public void ParseRejectsInvalidMagic()
    {
        var bytes = new byte[NsoFile.HeaderSize];

        var exception = Assert.Throws<InvalidDataException>(() => NsoFile.Parse(bytes));

        Assert.Contains("NSO magic is invalid", exception.Message);
    }

    [Fact]
    public void ParseRejectsSegmentThatStartsInsideHeader()
    {
        var bytes = CreateNso([1, 2, 3, 4], [5, 6], [7, 8, 9]);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x10), NsoFile.HeaderSize - 1);

        var exception = Assert.Throws<InvalidDataException>(() => NsoFile.Parse(bytes));

        Assert.Contains(".text segment file offset", exception.Message);
        Assert.Contains("overlaps the 0x100-byte NSO header", exception.Message);
    }

    [Fact]
    public void ParseAllowsZeroFileOffsetsForEmptySegments()
    {
        var bytes = CreateNso([], [], []);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x10), 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x20), 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x30), 0);

        var nso = NsoFile.Parse(bytes);

        Assert.Empty(nso.Text.DecompressedData);
        Assert.Empty(nso.Ro.DecompressedData);
        Assert.Empty(nso.Data.DecompressedData);
    }

    [Fact]
    public void ParseAllowsEmptyCompressedSegment()
    {
        var bytes = CreateNso([], [1], [2]);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x0C), (uint)NsoFlags.CompressedText);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x10), 0);

        var nso = NsoFile.Parse(bytes);

        Assert.Empty(nso.Text.DecompressedData);
    }

    [Fact]
    public void ParseRejectsSegmentThatExtendsPastFile()
    {
        var bytes = CreateNso([1, 2, 3, 4], [5, 6], [7, 8, 9]);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x64), bytes.Length);

        var exception = Assert.Throws<InvalidDataException>(() => NsoFile.Parse(bytes));

        Assert.Contains(".ro segment range", exception.Message);
        Assert.Contains("exceeds NSO length", exception.Message);
    }

    [Fact]
    public void ParseRejectsOverlappingFileSegmentRanges()
    {
        var bytes = CreateNso([1, 2, 3, 4], [5, 6], [7, 8, 9]);
        var textOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x10));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x20), textOffset + 2);

        var exception = Assert.Throws<InvalidDataException>(() => NsoFile.Parse(bytes));

        Assert.Contains("NSO file ranges overlap", exception.Message);
        Assert.Contains(".text", exception.Message);
        Assert.Contains(".ro", exception.Message);
    }

    [Fact]
    public void ParseRejectsOverlappingMemorySegmentRanges()
    {
        var bytes = CreateNso([1, 2, 3, 4], [5, 6], [7, 8, 9]);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x24), 2);

        var exception = Assert.Throws<InvalidDataException>(() => NsoFile.Parse(bytes));

        Assert.Contains("NSO memory ranges overlap", exception.Message);
        Assert.Contains(".text", exception.Message);
        Assert.Contains(".ro", exception.Message);
    }

    [Fact]
    public void ParseAcceptsNonstandardFileOrderAndWriteNormalizesIt()
    {
        var text = new byte[] { 1, 2, 3, 4 };
        var ro = new byte[] { 5, 6 };
        var data = new byte[] { 7, 8, 9 };
        var bytes = CreateNso(text, ro, data);
        bytes.AsSpan(0x100).Clear();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x10), 0x120);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x20), 0x100);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x30), 0x110);
        text.CopyTo(bytes.AsSpan(0x120));
        ro.CopyTo(bytes.AsSpan(0x100));
        data.CopyTo(bytes.AsSpan(0x110));

        var nso = NsoFile.Parse(bytes);
        var normalized = NsoFile.Parse(nso.Write());

        Assert.Equal(text, nso.Text.DecompressedData);
        Assert.Equal(ro, nso.Ro.DecompressedData);
        Assert.Equal(data, nso.Data.DecompressedData);
        Assert.Equal(text, normalized.Text.DecompressedData);
        Assert.Equal(ro, normalized.Ro.DecompressedData);
        Assert.Equal(data, normalized.Data.DecompressedData);
        Assert.True(normalized.Text.Header.FileOffset < normalized.Ro.Header.FileOffset);
        Assert.True(normalized.Ro.Header.FileOffset < normalized.Data.Header.FileOffset);
    }

    [Fact]
    public void ParseRejectsCompressedSegmentLargerThanSupportedByteArray()
    {
        var bytes = CreateNso([1], [], [], NsoFlags.CompressedText);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x18), int.MaxValue);

        var exception = Assert.Throws<InvalidDataException>(() => NsoFile.Parse(bytes));

        Assert.Contains(".text segment decompressed size", exception.Message);
        Assert.Contains("exceeds the maximum supported byte-array length", exception.Message);
    }

    [Fact]
    public void WritePreservesOpaqueHeaderBytes()
    {
        var bytes = CreateNso([1, 2, 3, 4], [5, 6], [7, 8, 9]);
        bytes[0x08] = 0x5A;
        bytes[0x6C] = 0xA5;
        bytes[0x9F] = 0x3C;

        var output = NsoFile.Parse(bytes).Write();

        Assert.Equal(0x5A, output[0x08]);
        Assert.Equal(0xA5, output[0x6C]);
        Assert.Equal(0x3C, output[0x9F]);
    }

    [Fact]
    public void WriteRejectsReplacementThatOverlapsNextMemorySegment()
    {
        var nso = NsoFile.Parse(CreateNso([1, 2, 3, 4], [5, 6], [7, 8, 9]));

        var exception = Assert.Throws<InvalidDataException>(() =>
            nso.Write(textDecompressedData: [1, 2, 3, 4, 5]));

        Assert.Contains("NSO memory ranges overlap", exception.Message);
        Assert.Contains(".text", exception.Message);
        Assert.Contains(".ro", exception.Message);
    }

    [Fact]
    public void WritePreservesUnchangedCompressedNsoByteForByte()
    {
        var fixture = CreateNsoWithOpaqueLayout(
            Enumerable.Repeat((byte)0x11, 256).ToArray(),
            [5, 6],
            [7, 8, 9],
            NsoFlags.CompressedText);
        fixture.Bytes[0xA0] ^= 0xFF;
        var nso = NsoFile.Parse(fixture.Bytes);

        var output = nso.Write(textDecompressedData: nso.Text.DecompressedData.ToArray());

        Assert.Equal(fixture.Bytes, output);
    }

    [Fact]
    public void WritePreservesLeadingOpaqueDataWhenTextSegmentIsEmpty()
    {
        var fixture = CreateNsoWithOpaqueLayout(
            [],
            [5, 6],
            [7, 8, 9],
            NsoFlags.None);

        var output = NsoFile.Parse(fixture.Bytes).Write();

        Assert.Equal(fixture.Bytes, output);
    }

    [Fact]
    public void WritePreservesOpaqueLayoutWhenCompressedTextSizeChanges()
    {
        var text = Enumerable.Repeat((byte)0x11, 256).ToArray();
        var replacementText = Enumerable.Range(0, text.Length).Select(index => (byte)index).ToArray();
        var fixture = CreateNsoWithOpaqueLayout(
            text,
            [5, 6],
            [7, 8, 9],
            NsoFlags.CompressedText);
        var nso = NsoFile.Parse(fixture.Bytes);

        var output = nso.Write(textDecompressedData: replacementText);
        var reparsed = NsoFile.Parse(output);

        Assert.NotEqual(nso.Text.CompressedSize, reparsed.Text.CompressedSize);
        Assert.Equal(replacementText, reparsed.Text.DecompressedData);
        Assert.Equal(fixture.Prefix, output.AsSpan(NsoFile.HeaderSize, fixture.Prefix.Length).ToArray());
        AssertOpaqueRegionAfterSegment(output, reparsed.Text, fixture.TextToRoGap);
        AssertOpaqueRegionAfterSegment(output, reparsed.Ro, fixture.RoToDataGap);
        AssertOpaqueRegionAfterSegment(output, reparsed.Data, fixture.Trailing);
        Assert.Equal(NsoFile.HeaderSize, BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(0x1C)));
        Assert.Equal(fixture.Prefix.Length, BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(0x2C)));
        Assert.Equal(nso.Ro.CompressedData, reparsed.Ro.CompressedData);
        Assert.Equal(nso.Ro.Hash, reparsed.Ro.Hash);
        Assert.Equal(nso.Data.CompressedData, reparsed.Data.CompressedData);
        Assert.Equal(nso.Data.Hash, reparsed.Data.Hash);
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

    private static OpaqueNsoFixture CreateNsoWithOpaqueLayout(
        byte[] text,
        byte[] ro,
        byte[] data,
        NsoFlags flags)
    {
        var textSegment = flags.HasFlag(NsoFlags.CompressedText) ? Compress(text) : text;
        var roSegment = flags.HasFlag(NsoFlags.CompressedRo) ? Compress(ro) : ro;
        var dataSegment = flags.HasFlag(NsoFlags.CompressedData) ? Compress(data) : data;
        var prefix = Enumerable.Repeat((byte)0xA1, 0x10).ToArray();
        var textOffset = NsoFile.HeaderSize + prefix.Length;
        var textToRoGap = CreateAlignedOpaqueRegion(textOffset + textSegment.Length, 0xB2);
        var roOffset = textOffset + textSegment.Length + textToRoGap.Length;
        var roToDataGap = CreateAlignedOpaqueRegion(roOffset + roSegment.Length, 0xC3);
        var dataOffset = roOffset + roSegment.Length + roToDataGap.Length;
        var trailing = CreateAlignedOpaqueRegion(dataOffset + dataSegment.Length, 0xD4);
        var output = new byte[dataOffset + dataSegment.Length + trailing.Length];

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), NsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x04), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x0C), (uint)flags);
        WriteSegmentHeader(output, 0x10, textOffset, 0, text.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x1C), NsoFile.HeaderSize);
        WriteSegmentHeader(output, 0x20, roOffset, text.Length, ro.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x2C), prefix.Length);
        WriteSegmentHeader(output, 0x30, dataOffset, text.Length + ro.Length, data.Length);
        output.AsSpan(0x40, 0x20).Fill(0xAB);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x60), textSegment.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x64), roSegment.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x68), dataSegment.Length);
        NsoFile.ComputeHash(text).CopyTo(output.AsSpan(0xA0));
        NsoFile.ComputeHash(ro).CopyTo(output.AsSpan(0xC0));
        NsoFile.ComputeHash(data).CopyTo(output.AsSpan(0xE0));
        prefix.CopyTo(output.AsSpan(NsoFile.HeaderSize));
        textSegment.CopyTo(output.AsSpan(textOffset));
        textToRoGap.CopyTo(output.AsSpan(textOffset + textSegment.Length));
        roSegment.CopyTo(output.AsSpan(roOffset));
        roToDataGap.CopyTo(output.AsSpan(roOffset + roSegment.Length));
        dataSegment.CopyTo(output.AsSpan(dataOffset));
        trailing.CopyTo(output.AsSpan(dataOffset + dataSegment.Length));

        return new OpaqueNsoFixture(
            output,
            prefix,
            textToRoGap,
            roToDataGap,
            trailing);
    }

    private static byte[] CreateAlignedOpaqueRegion(int precedingEnd, byte value)
    {
        var length = Align(precedingEnd, 0x10) - precedingEnd + 0x10;
        return Enumerable.Repeat(value, length).ToArray();
    }

    private static void AssertOpaqueRegionAfterSegment(
        byte[] output,
        NsoSegment segment,
        byte[] expected)
    {
        var offset = segment.Header.FileOffset + segment.CompressedSize;
        Assert.Equal(expected, output.AsSpan(offset, expected.Length).ToArray());
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

    private sealed record OpaqueNsoFixture(
        byte[] Bytes,
        byte[] Prefix,
        byte[] TextToRoGap,
        byte[] RoToDataGap,
        byte[] Trailing);
}
