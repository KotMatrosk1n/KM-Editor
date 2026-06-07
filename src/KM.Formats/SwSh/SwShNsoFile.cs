// SPDX-License-Identifier: GPL-3.0-only

using K4os.Compression.LZ4;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KM.Formats.SwSh;

[Flags]
public enum SwShNsoFlags : uint
{
    None = 0,
    CompressedText = 1 << 0,
    CompressedRo = 1 << 1,
    CompressedData = 1 << 2,
    CheckHashText = 1 << 3,
    CheckHashRo = 1 << 4,
    CheckHashData = 1 << 5,
}

public sealed record SwShNsoSegmentHeader(
    int FileOffset,
    int MemoryOffset,
    int DecompressedSize);

public sealed record SwShNsoSegment(
    string Name,
    SwShNsoSegmentHeader Header,
    int CompressedSize,
    byte[] Hash,
    byte[] CompressedData,
    byte[] DecompressedData);

public sealed record SwShNsoFile(
    uint Version,
    SwShNsoFlags Flags,
    byte[] BuildId,
    SwShNsoSegment Text,
    SwShNsoSegment Ro,
    SwShNsoSegment Data)
{
    public const uint Magic = 0x304F534E;
    public const int HeaderSize = 0x100;

    public IReadOnlyList<SwShNsoSegment> Segments => [Text, Ro, Data];

    public static SwShNsoFile Parse(byte[] data)
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
        var flags = (SwShNsoFlags)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x0C, sizeof(uint)));
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

        return new SwShNsoFile(
            version,
            flags,
            buildId,
            ReadSegment(data, ".text", textHeader, textCompressedSize, textHash, flags.HasFlag(SwShNsoFlags.CompressedText)),
            ReadSegment(data, ".ro", roHeader, roCompressedSize, roHash, flags.HasFlag(SwShNsoFlags.CompressedRo)),
            ReadSegment(data, ".data", dataHeader, dataCompressedSize, dataHash, flags.HasFlag(SwShNsoFlags.CompressedData)));
    }

    public static byte[] ComputeHash(ReadOnlySpan<byte> data)
    {
        return SHA256.HashData(data);
    }

    private static SwShNsoSegmentHeader ReadSegmentHeader(byte[] data, int offset)
    {
        var fileOffset = ReadNonNegativeInt32(data, offset, "segment file offset");
        var memoryOffset = ReadNonNegativeInt32(data, offset + 0x04, "segment memory offset");
        var decompressedSize = ReadNonNegativeInt32(data, offset + 0x08, "segment decompressed size");
        return new SwShNsoSegmentHeader(fileOffset, memoryOffset, decompressedSize);
    }

    private static SwShNsoSegment ReadSegment(
        byte[] data,
        string name,
        SwShNsoSegmentHeader header,
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

        return new SwShNsoSegment(name, header, compressedSize, hash, compressedData, decompressedData);
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
