// SPDX-License-Identifier: GPL-3.0-only

using K4os.Compression.LZ4;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KM.Formats.Executable;

[Flags]
public enum NsoFlags : uint
{
    None = 0,
    CompressedText = 1 << 0,
    CompressedRo = 1 << 1,
    CompressedData = 1 << 2,
    CheckHashText = 1 << 3,
    CheckHashRo = 1 << 4,
    CheckHashData = 1 << 5,
}

public sealed record NsoSegmentHeader(
    int FileOffset,
    int MemoryOffset,
    int DecompressedSize);

public sealed record NsoSegment(
    string Name,
    NsoSegmentHeader Header,
    int CompressedSize,
    byte[] Hash,
    byte[] CompressedData,
    byte[] DecompressedData);

public sealed record NsoFile(
    uint Version,
    NsoFlags Flags,
    byte[] BuildId,
    NsoSegment Text,
    NsoSegment Ro,
    NsoSegment Data)
{
    public const uint Magic = 0x304F534E;
    public const int HeaderSize = 0x100;

    public byte[] RawHeader { get; init; } = [];

    public IReadOnlyList<NsoSegment> Segments => [Text, Ro, Data];

    public static NsoFile Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException(
                $"NSO file is too small: expected at least 0x{HeaderSize:X} bytes, got 0x{data.Length:X}.");
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x00, sizeof(uint)));
        if (magic != Magic)
        {
            throw new InvalidDataException($"NSO magic is invalid: 0x{magic:X8}.");
        }

        var version = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x04, sizeof(uint)));
        var flags = (NsoFlags)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x0C, sizeof(uint)));
        var textHeader = ReadSegmentHeader(data, 0x10);
        var roHeader = ReadSegmentHeader(data, 0x20);
        var dataHeader = ReadSegmentHeader(data, 0x30);
        var buildId = data.AsSpan(0x40, 0x20).ToArray();
        var textCompressedSize = ReadNonNegativeInt32(data, 0x60, "text compressed size");
        var roCompressedSize = ReadNonNegativeInt32(data, 0x64, "ro compressed size");
        var dataCompressedSize = ReadNonNegativeInt32(data, 0x68, "data compressed size");
        var textHash = data.AsSpan(0xA0, 0x20).ToArray();
        var roHash = data.AsSpan(0xC0, 0x20).ToArray();
        var dataHash = data.AsSpan(0xE0, 0x20).ToArray();

        return new NsoFile(
            version,
            flags,
            buildId,
            ReadSegment(data, ".text", textHeader, textCompressedSize, textHash, flags.HasFlag(NsoFlags.CompressedText)),
            ReadSegment(data, ".ro", roHeader, roCompressedSize, roHash, flags.HasFlag(NsoFlags.CompressedRo)),
            ReadSegment(data, ".data", dataHeader, dataCompressedSize, dataHash, flags.HasFlag(NsoFlags.CompressedData)))
        {
            RawHeader = data.AsSpan(0, HeaderSize).ToArray(),
        };
    }

    public static byte[] ComputeHash(ReadOnlySpan<byte> data)
    {
        return SHA256.HashData(data);
    }

    public byte[] Write(
        byte[]? textDecompressedData = null,
        byte[]? roDecompressedData = null,
        byte[]? dataDecompressedData = null)
    {
        var textData = textDecompressedData ?? Text.DecompressedData;
        var roData = roDecompressedData ?? Ro.DecompressedData;
        var dataData = dataDecompressedData ?? Data.DecompressedData;
        var textSegment = EncodeSegment(".text", textData, Flags.HasFlag(NsoFlags.CompressedText));
        var roSegment = EncodeSegment(".ro", roData, Flags.HasFlag(NsoFlags.CompressedRo));
        var dataSegment = EncodeSegment(".data", dataData, Flags.HasFlag(NsoFlags.CompressedData));

        var textOffset = Math.Max(HeaderSize, Text.Header.FileOffset);
        var roOffset = Align(textOffset + textSegment.Length, 0x10);
        var dataOffset = Align(roOffset + roSegment.Length, 0x10);
        var output = new byte[Align(dataOffset + dataSegment.Length, 0x10)];
        var header = RawHeader.Length == HeaderSize ? (byte[])RawHeader.Clone() : new byte[HeaderSize];

        WriteHeader(
            header,
            textOffset,
            textData.Length,
            textSegment.Length,
            ComputeHash(textData),
            roOffset,
            roData.Length,
            roSegment.Length,
            ComputeHash(roData),
            dataOffset,
            dataData.Length,
            dataSegment.Length,
            ComputeHash(dataData));
        header.CopyTo(output.AsSpan(0, HeaderSize));
        textSegment.CopyTo(output.AsSpan(textOffset));
        roSegment.CopyTo(output.AsSpan(roOffset));
        dataSegment.CopyTo(output.AsSpan(dataOffset));
        return output;
    }

    private static NsoSegmentHeader ReadSegmentHeader(byte[] data, int offset)
    {
        var fileOffset = ReadNonNegativeInt32(data, offset, "segment file offset");
        var memoryOffset = ReadNonNegativeInt32(data, offset + 0x04, "segment memory offset");
        var decompressedSize = ReadNonNegativeInt32(data, offset + 0x08, "segment decompressed size");
        return new NsoSegmentHeader(fileOffset, memoryOffset, decompressedSize);
    }

    private static NsoSegment ReadSegment(
        byte[] data,
        string name,
        NsoSegmentHeader header,
        int compressedSize,
        byte[] hash,
        bool isCompressed)
    {
        var segmentEnd = (long)header.FileOffset + compressedSize;
        if (header.FileOffset > data.Length || compressedSize > data.Length - header.FileOffset)
        {
            throw new InvalidDataException(
                $"{name} segment range 0x{header.FileOffset:X}..0x{segmentEnd:X} exceeds NSO length 0x{data.Length:X}.");
        }

        var compressedData = data.AsSpan(header.FileOffset, compressedSize).ToArray();
        var decompressedData = isCompressed
            ? DecodeLz4(name, compressedData, header.DecompressedSize)
            : compressedData;

        if (decompressedData.Length != header.DecompressedSize)
        {
            throw new InvalidDataException(
                $"{name} segment decompressed size mismatch: expected 0x{header.DecompressedSize:X}, got 0x{decompressedData.Length:X}.");
        }

        return new NsoSegment(name, header, compressedSize, hash, compressedData, decompressedData);
    }

    private static byte[] DecodeLz4(string name, byte[] compressedData, int decompressedSize)
    {
        var output = new byte[decompressedSize];
        var decoded = LZ4Codec.Decode(compressedData, 0, compressedData.Length, output, 0, output.Length);
        if (decoded != decompressedSize)
        {
            throw new InvalidDataException(
                $"{name} LZ4 segment decoded to 0x{decoded:X} bytes instead of 0x{decompressedSize:X}.");
        }

        return output;
    }

    private static byte[] EncodeSegment(string name, byte[] decompressedData, bool shouldCompress)
    {
        if (!shouldCompress)
        {
            return decompressedData.ToArray();
        }

        var output = new byte[LZ4Codec.MaximumOutputSize(decompressedData.Length)];
        var length = LZ4Codec.Encode(
            decompressedData,
            0,
            decompressedData.Length,
            output,
            0,
            output.Length,
            LZ4Level.L00_FAST);
        if (length <= 0)
        {
            throw new InvalidDataException($"{name} LZ4 compression failed.");
        }

        return output[..length];
    }

    private void WriteHeader(
        byte[] header,
        int textOffset,
        int textDecompressedSize,
        int textCompressedSize,
        byte[] textHash,
        int roOffset,
        int roDecompressedSize,
        int roCompressedSize,
        byte[] roHash,
        int dataOffset,
        int dataDecompressedSize,
        int dataCompressedSize,
        byte[] dataHash)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x00), Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x04), Version);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x0C), (uint)Flags);
        WriteSegmentHeader(header, 0x10, textOffset, Text.Header.MemoryOffset, textDecompressedSize);
        WriteSegmentHeader(header, 0x20, roOffset, Ro.Header.MemoryOffset, roDecompressedSize);
        WriteSegmentHeader(header, 0x30, dataOffset, Data.Header.MemoryOffset, dataDecompressedSize);
        BuildId.CopyTo(header.AsSpan(0x40, 0x20));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0x60), textCompressedSize);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0x64), roCompressedSize);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0x68), dataCompressedSize);
        textHash.CopyTo(header.AsSpan(0xA0, 0x20));
        roHash.CopyTo(header.AsSpan(0xC0, 0x20));
        dataHash.CopyTo(header.AsSpan(0xE0, 0x20));
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

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }

    private static int ReadNonNegativeInt32(byte[] data, int offset, string fieldName)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, sizeof(int)));
        if (value < 0)
        {
            throw new InvalidDataException($"NSO {fieldName} is negative: {value}.");
        }

        return value;
    }
}
