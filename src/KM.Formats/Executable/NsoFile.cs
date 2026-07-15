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

    private NsoOpaqueLayout? OpaqueLayout { get; init; }

    private NsoOriginalSegments? OriginalSegments { get; init; }

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
        var segmentLayouts = new[]
        {
            new NsoSegmentLayout(".text", textHeader, textCompressedSize),
            new NsoSegmentLayout(".ro", roHeader, roCompressedSize),
            new NsoSegmentLayout(".data", dataHeader, dataCompressedSize),
        };

        ValidateSegmentLayouts(data.Length, segmentLayouts);
        var opaqueLayout = HasStandardFileOrder(segmentLayouts)
            ? CaptureOpaqueLayout(data, segmentLayouts)
            : null;
        var textSegment = ReadSegment(
            data,
            ".text",
            textHeader,
            textCompressedSize,
            textHash,
            flags.HasFlag(NsoFlags.CompressedText));
        var roSegment = ReadSegment(
            data,
            ".ro",
            roHeader,
            roCompressedSize,
            roHash,
            flags.HasFlag(NsoFlags.CompressedRo));
        var dataSegment = ReadSegment(
            data,
            ".data",
            dataHeader,
            dataCompressedSize,
            dataHash,
            flags.HasFlag(NsoFlags.CompressedData));

        return new NsoFile(
            version,
            flags,
            buildId,
            textSegment,
            roSegment,
            dataSegment)
        {
            RawHeader = data.AsSpan(0, HeaderSize).ToArray(),
            OpaqueLayout = opaqueLayout,
            OriginalSegments = new NsoOriginalSegments(
                CaptureOriginalSegment(
                    textSegment,
                    flags.HasFlag(NsoFlags.CompressedText),
                    flags.HasFlag(NsoFlags.CheckHashText)),
                CaptureOriginalSegment(
                    roSegment,
                    flags.HasFlag(NsoFlags.CompressedRo),
                    flags.HasFlag(NsoFlags.CheckHashRo)),
                CaptureOriginalSegment(
                    dataSegment,
                    flags.HasFlag(NsoFlags.CompressedData),
                    flags.HasFlag(NsoFlags.CheckHashData))),
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
        ValidateWriteMemoryLayout(
            textDecompressedData?.Length ?? Text.DecompressedData.Length,
            roDecompressedData?.Length ?? Ro.DecompressedData.Length,
            dataDecompressedData?.Length ?? Data.DecompressedData.Length);
        var originalSegments = OriginalSegments;
        var textSegment = PrepareSegment(
            Text,
            originalSegments?.Text,
            textDecompressedData,
            Flags.HasFlag(NsoFlags.CompressedText),
            Flags.HasFlag(NsoFlags.CheckHashText));
        var roSegment = PrepareSegment(
            Ro,
            originalSegments?.Ro,
            roDecompressedData,
            Flags.HasFlag(NsoFlags.CompressedRo),
            Flags.HasFlag(NsoFlags.CheckHashRo));
        var dataSegment = PrepareSegment(
            Data,
            originalSegments?.Data,
            dataDecompressedData,
            Flags.HasFlag(NsoFlags.CompressedData),
            Flags.HasFlag(NsoFlags.CheckHashData));
        var opaqueLayout = OpaqueLayout;
        var textPlacement = default(NsoWritePlacement);
        var roPlacement = default(NsoWritePlacement);
        var dataPlacement = default(NsoWritePlacement);
        var trailingOffset = 0;
        int textOffset;
        int roOffset;
        int dataOffset;
        int outputLength;

        if (opaqueLayout is null)
        {
            textOffset = Math.Max(HeaderSize, Text.Header.FileOffset);
            roOffset = ToSupportedOffset(
                Align((long)textOffset + textSegment.CompressedData.Length, 0x10),
                ".ro segment");
            dataOffset = ToSupportedOffset(
                Align((long)roOffset + roSegment.CompressedData.Length, 0x10),
                ".data segment");
            outputLength = ToSupportedOutputLength(
                Align((long)dataOffset + dataSegment.CompressedData.Length, 0x10));
        }
        else
        {
            long cursor = HeaderSize;
            textPlacement = PlaceSegment(
                ref cursor,
                opaqueLayout.BeforeText,
                Text,
                textSegment.CompressedData);
            roPlacement = PlaceSegment(
                ref cursor,
                opaqueLayout.BeforeRo,
                Ro,
                roSegment.CompressedData);
            dataPlacement = PlaceSegment(
                ref cursor,
                opaqueLayout.BeforeData,
                Data,
                dataSegment.CompressedData);
            trailingOffset = ToSupportedOffset(cursor, "trailing data");
            cursor += opaqueLayout.Trailing.Length;

            textOffset = textPlacement.SegmentOffset;
            roOffset = roPlacement.SegmentOffset;
            dataOffset = dataPlacement.SegmentOffset;
            outputLength = ToSupportedOutputLength(
                cursor == opaqueLayout.OriginalFileLength
                    ? cursor
                    : Align(cursor, 0x10));
        }

        var output = new byte[outputLength];
        var header = RawHeader.Length == HeaderSize ? (byte[])RawHeader.Clone() : new byte[HeaderSize];

        WriteHeader(
            header,
            textOffset,
            textSegment.DecompressedData.Length,
            textSegment.CompressedData.Length,
            textSegment.Hash,
            roOffset,
            roSegment.DecompressedData.Length,
            roSegment.CompressedData.Length,
            roSegment.Hash,
            dataOffset,
            dataSegment.DecompressedData.Length,
            dataSegment.CompressedData.Length,
            dataSegment.Hash);
        header.CopyTo(output.AsSpan(0, HeaderSize));

        if (opaqueLayout is not null)
        {
            opaqueLayout.BeforeText.CopyTo(output.AsSpan(textPlacement.OpaqueOffset));
            opaqueLayout.BeforeRo.CopyTo(output.AsSpan(roPlacement.OpaqueOffset));
            opaqueLayout.BeforeData.CopyTo(output.AsSpan(dataPlacement.OpaqueOffset));
            opaqueLayout.Trailing.CopyTo(output.AsSpan(trailingOffset));
        }

        textSegment.CompressedData.CopyTo(output.AsSpan(textOffset));
        roSegment.CompressedData.CopyTo(output.AsSpan(roOffset));
        dataSegment.CompressedData.CopyTo(output.AsSpan(dataOffset));
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
        if (!isCompressed && compressedSize != header.DecompressedSize)
        {
            throw new InvalidDataException(
                $"{name} segment decompressed size mismatch: expected 0x{header.DecompressedSize:X}, got 0x{compressedSize:X}.");
        }

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
        if (decompressedSize > Array.MaxLength)
        {
            throw new InvalidDataException(
                $"{name} segment decompressed size 0x{decompressedSize:X} exceeds the maximum supported byte-array length 0x{Array.MaxLength:X}.");
        }

        if (compressedData.Length == 0)
        {
            if (decompressedSize != 0)
            {
                throw new InvalidDataException(
                    $"{name} compressed segment cannot decode to 0x{decompressedSize:X} bytes from an empty payload.");
            }

            return [];
        }

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
        if (!shouldCompress || decompressedData.Length == 0)
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

    private static NsoEncodedSegment PrepareSegment(
        NsoSegment original,
        NsoOriginalSegment? originalSnapshot,
        byte[]? replacement,
        bool shouldCompress,
        bool shouldCheckHash)
    {
        var decompressedData = replacement ?? original.DecompressedData;
        if (originalSnapshot is not null
            && originalSnapshot.WasCompressed == shouldCompress
            && originalSnapshot.CheckedHash == shouldCheckHash
            && decompressedData.AsSpan().SequenceEqual(originalSnapshot.DecompressedData))
        {
            return new NsoEncodedSegment(
                decompressedData,
                originalSnapshot.CompressedData,
                originalSnapshot.Hash);
        }

        return new NsoEncodedSegment(
            decompressedData,
            EncodeSegment(original.Name, decompressedData, shouldCompress),
            ComputeHash(decompressedData));
    }

    private static NsoOriginalSegment CaptureOriginalSegment(
        NsoSegment segment,
        bool wasCompressed,
        bool checkedHash)
    {
        return new NsoOriginalSegment(
            segment.DecompressedData.ToArray(),
            segment.CompressedData.ToArray(),
            segment.Hash.ToArray(),
            wasCompressed,
            checkedHash);
    }

    private void ValidateWriteMemoryLayout(
        int textDecompressedSize,
        int roDecompressedSize,
        int dataDecompressedSize)
    {
        var segments = new[]
        {
            new NsoSegmentLayout(
                ".text",
                Text.Header with { DecompressedSize = textDecompressedSize },
                Text.CompressedSize),
            new NsoSegmentLayout(
                ".ro",
                Ro.Header with { DecompressedSize = roDecompressedSize },
                Ro.CompressedSize),
            new NsoSegmentLayout(
                ".data",
                Data.Header with { DecompressedSize = dataDecompressedSize },
                Data.CompressedSize),
        };

        ValidateNonOverlappingRanges(
            segments,
            static segment => segment.Header.MemoryOffset,
            static segment => segment.Header.DecompressedSize,
            "memory");
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

    private static long Align(long value, int alignment)
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

    private static void ValidateSegmentLayouts(int fileLength, IReadOnlyList<NsoSegmentLayout> segments)
    {
        foreach (var segment in segments)
        {
            if (segment.CompressedSize > 0 && segment.Header.FileOffset < HeaderSize)
            {
                throw new InvalidDataException(
                    $"{segment.Name} segment file offset 0x{segment.Header.FileOffset:X} overlaps the 0x{HeaderSize:X}-byte NSO header.");
            }

            var fileEnd = (long)segment.Header.FileOffset + segment.CompressedSize;
            if (fileEnd > fileLength)
            {
                throw new InvalidDataException(
                    $"{segment.Name} segment range 0x{segment.Header.FileOffset:X}..0x{fileEnd:X} exceeds NSO length 0x{fileLength:X}.");
            }
        }

        ValidateNonOverlappingRanges(
            segments,
            static segment => segment.Header.FileOffset,
            static segment => segment.CompressedSize,
            "file");
        ValidateNonOverlappingRanges(
            segments,
            static segment => segment.Header.MemoryOffset,
            static segment => segment.Header.DecompressedSize,
            "memory");
    }

    private static bool HasStandardFileOrder(IReadOnlyList<NsoSegmentLayout> segments)
    {
        var occupiedSegments = segments
            .Where(segment => segment.CompressedSize > 0)
            .ToArray();

        for (var index = 1; index < occupiedSegments.Length; index++)
        {
            var previous = occupiedSegments[index - 1];
            var current = occupiedSegments[index];
            if (current.Header.FileOffset < previous.Header.FileOffset)
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateNonOverlappingRanges(
        IReadOnlyList<NsoSegmentLayout> segments,
        Func<NsoSegmentLayout, int> selectOffset,
        Func<NsoSegmentLayout, int> selectSize,
        string rangeKind)
    {
        var occupiedSegments = segments
            .Where(segment => selectSize(segment) > 0)
            .OrderBy(selectOffset)
            .ToArray();

        for (var index = 1; index < occupiedSegments.Length; index++)
        {
            var previous = occupiedSegments[index - 1];
            var current = occupiedSegments[index];
            var previousEnd = (long)selectOffset(previous) + selectSize(previous);
            if (selectOffset(current) < previousEnd)
            {
                var currentEnd = (long)selectOffset(current) + selectSize(current);
                throw new InvalidDataException(
                    $"NSO {rangeKind} ranges overlap: {previous.Name} " +
                    $"0x{selectOffset(previous):X}..0x{previousEnd:X} and {current.Name} " +
                    $"0x{selectOffset(current):X}..0x{currentEnd:X}.");
            }
        }
    }

    private static NsoOpaqueLayout CaptureOpaqueLayout(
        byte[] data,
        IReadOnlyList<NsoSegmentLayout> segments)
    {
        var beforeSegments = Enumerable
            .Range(0, segments.Count)
            .Select(static _ => Array.Empty<byte>())
            .ToArray();
        var cursor = HeaderSize;
        var foundOccupiedSegment = false;

        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (segment.CompressedSize == 0)
            {
                continue;
            }

            var opaqueData = data
                .AsSpan(cursor, segment.Header.FileOffset - cursor)
                .ToArray();
            beforeSegments[foundOccupiedSegment ? index : 0] = opaqueData;
            cursor = segment.Header.FileOffset + segment.CompressedSize;
            foundOccupiedSegment = true;
        }

        if (!foundOccupiedSegment)
        {
            beforeSegments[0] = data.AsSpan(HeaderSize).ToArray();
            cursor = data.Length;
        }

        return new NsoOpaqueLayout(
            beforeSegments[0],
            beforeSegments[1],
            beforeSegments[2],
            data.AsSpan(cursor).ToArray(),
            data.Length);
    }

    private static NsoWritePlacement PlaceSegment(
        ref long cursor,
        byte[] opaqueBefore,
        NsoSegment original,
        byte[] compressedData)
    {
        var opaqueOffset = ToSupportedOffset(cursor, $"opaque data before {original.Name}");
        cursor += opaqueBefore.Length;

        if (compressedData.Length == 0 && original.CompressedSize == 0)
        {
            return new NsoWritePlacement(opaqueOffset, original.Header.FileOffset);
        }

        var segmentOffset = cursor == original.Header.FileOffset
            ? cursor
            : Align(cursor, 0x10);
        var supportedSegmentOffset = ToSupportedOffset(segmentOffset, original.Name);
        cursor = segmentOffset + compressedData.Length;
        return new NsoWritePlacement(opaqueOffset, supportedSegmentOffset);
    }

    private static int ToSupportedOffset(long value, string fieldName)
    {
        if (value > int.MaxValue)
        {
            throw new InvalidDataException(
                $"NSO {fieldName} offset 0x{value:X} exceeds the supported 32-bit signed range.");
        }

        return (int)value;
    }

    private static int ToSupportedOutputLength(long value)
    {
        if (value > Array.MaxLength)
        {
            throw new InvalidDataException(
                $"NSO output length 0x{value:X} exceeds the maximum supported byte-array length 0x{Array.MaxLength:X}.");
        }

        return (int)value;
    }

    private sealed record NsoSegmentLayout(
        string Name,
        NsoSegmentHeader Header,
        int CompressedSize);

    private sealed record NsoOpaqueLayout(
        byte[] BeforeText,
        byte[] BeforeRo,
        byte[] BeforeData,
        byte[] Trailing,
        int OriginalFileLength);

    private sealed record NsoEncodedSegment(
        byte[] DecompressedData,
        byte[] CompressedData,
        byte[] Hash);

    private sealed record NsoOriginalSegments(
        NsoOriginalSegment Text,
        NsoOriginalSegment Ro,
        NsoOriginalSegment Data);

    private sealed record NsoOriginalSegment(
        byte[] DecompressedData,
        byte[] CompressedData,
        byte[] Hash,
        bool WasCompressed,
        bool CheckedHash);

    private readonly record struct NsoWritePlacement(
        int OpaqueOffset,
        int SegmentOffset);
}
