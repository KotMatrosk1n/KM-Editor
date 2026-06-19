// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text;

namespace KM.SwSh.FpsPatch;

internal sealed record SwShFpsDemoAudienceClipInfo(
    ulong RelativeHash,
    uint KeyFrames,
    uint FrameRate,
    IReadOnlyList<int> TranslateV0ValueCounts,
    IReadOnlyList<float[]> TranslateV0Values);

internal static class SwShFpsDemoAudiencePatcher
{
    internal const string AudienceArchiveRelativePath = "romfs/bin/archive/demo/share/anime/a_pl0110.gfpak";

    private const string TranslateV0ParamName = "TranslateV0";
    private const ulong AudienceClipHash = 0xE46DE99F0E990642;
    private const ulong AudienceSubClipHash = 0xCD523A1B23139151;

    private static readonly HashSet<ulong> TargetClipHashes =
    [
        AudienceClipHash,
        AudienceSubClipHash,
    ];

    public static byte[] ConvertArchive(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);

        try
        {
            return ConvertArchiveCore(source);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Invalid opening audience GFPAK archive.", exception);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("Invalid opening audience GF animation FlatBuffer.", exception);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Invalid opening audience GF animation FlatBuffer.", exception);
        }
    }

    private static byte[] ConvertArchiveCore(byte[] source)
    {
        var archive = GfPakArchive.Read(source);
        var changedTargetClips = 0;
        for (var index = 0; index < archive.Files.Count; index++)
        {
            var file = archive.Files[index];
            if (!TargetClipHashes.Contains(file.RelativeHash))
            {
                continue;
            }

            var keyFrames = FlatBufferAnimation.GetKeyFrames(file.Data);
            var frameRate = FlatBufferAnimation.GetFrameRate(file.Data);
            var (patchedData, changedVectors) = FlatBufferMaterial.ExpandFloatVectorParamTo60Fps(file.Data, TranslateV0ParamName);
            if (changedVectors == 0)
            {
                throw new InvalidDataException("60FPS Patch could not find opening audience TranslateV0 flipbook tracks.");
            }

            FlatBufferAnimation.SetKeyFrames(patchedData, ConvertKeyFramesTo60Fps(keyFrames));
            FlatBufferAnimation.SetFrameRate(patchedData, ConvertFrameRateTo60Fps(frameRate));
            archive.Files[index] = file with { Data = patchedData };
            changedTargetClips++;
        }

        if (changedTargetClips != TargetClipHashes.Count)
        {
            throw new InvalidDataException("60FPS Patch could not find every opening audience animation clip in a_pl0110.gfpak.");
        }

        return archive.Write();
    }

    internal static IReadOnlyList<SwShFpsDemoAudienceClipInfo> InspectArchive(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var archive = GfPakArchive.Read(source);
        return archive.Files
            .Where(file => TargetClipHashes.Contains(file.RelativeHash))
            .OrderBy(file => file.RelativeHash)
            .Select(file =>
            {
                var vectors = FlatBufferMaterial.GetFloatVectorParams(file.Data, TranslateV0ParamName)
                    .GroupBy(vector => vector.VectorOffset)
                    .Select(group => group.First().Values)
                    .ToArray();
                return new SwShFpsDemoAudienceClipInfo(
                    file.RelativeHash,
                    FlatBufferAnimation.GetKeyFrames(file.Data),
                    FlatBufferAnimation.GetFrameRate(file.Data),
                    vectors.Select(vector => vector.Length).ToArray(),
                    vectors);
            })
            .ToArray();
    }

    private static uint ConvertKeyFramesTo60Fps(uint keyFrames)
    {
        return keyFrames > 1 ? checked((keyFrames - 1) * 2 + 1) : keyFrames;
    }

    private static uint ConvertFrameRateTo60Fps(uint frameRate)
    {
        return frameRate > 0 ? checked(frameRate * 2) : frameRate;
    }

    private sealed record GfPakFile(
        int Index,
        ulong FileHash,
        ulong FolderHash,
        ulong RelativeHash,
        byte[] Data);

    private sealed class GfPakArchive
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("GFLXPACK");

        public required List<GfPakFile> Files { get; init; }

        public static GfPakArchive Read(byte[] source)
        {
            using var stream = new MemoryStream(source, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            var magic = reader.ReadBytes(Magic.Length);
            if (!magic.SequenceEqual(Magic))
            {
                throw new InvalidDataException("Invalid GFLXPACK archive.");
            }

            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            var fileCount = reader.ReadUInt32();
            var folderCount = reader.ReadUInt32();
            var fileHeaderOffset = reader.ReadUInt64();
            var hashOffset = reader.ReadUInt64();

            var folderOffsets = new ulong[checked((int)folderCount)];
            for (var index = 0; index < folderOffsets.Length; index++)
            {
                folderOffsets[index] = reader.ReadUInt64();
            }

            stream.Position = checked((long)hashOffset);
            var fileHashes = new ulong[checked((int)fileCount)];
            for (var index = 0; index < fileHashes.Length; index++)
            {
                fileHashes[index] = reader.ReadUInt64();
            }

            var indexEntries = new Dictionary<uint, (ulong FolderHash, ulong RelativeHash)>();
            foreach (var folderOffset in folderOffsets)
            {
                stream.Position = checked((long)folderOffset);
                var folderHash = reader.ReadUInt64();
                var contentCount = reader.ReadUInt32();
                _ = reader.ReadUInt32();
                for (var index = 0; index < contentCount; index++)
                {
                    var relativeHash = reader.ReadUInt64();
                    var fileIndex = reader.ReadUInt32();
                    _ = reader.ReadUInt32();
                    indexEntries[fileIndex] = (folderHash, relativeHash);
                }
            }

            stream.Position = checked((long)fileHeaderOffset);
            var headers = new PakFileHeader[fileHashes.Length];
            for (var index = 0; index < headers.Length; index++)
            {
                headers[index] = new PakFileHeader(
                    reader.ReadUInt16(),
                    reader.ReadUInt16(),
                    reader.ReadUInt32(),
                    reader.ReadUInt32(),
                    reader.ReadUInt32(),
                    reader.ReadUInt64());
            }

            var files = new List<GfPakFile>(headers.Length);
            for (var index = 0; index < headers.Length; index++)
            {
                if (!indexEntries.TryGetValue((uint)index, out var entry))
                {
                    throw new InvalidDataException("Invalid GFLXPACK folder index.");
                }

                var header = headers[index];
                stream.Position = checked((long)header.Offset);
                var storedBytes = reader.ReadBytes(checked((int)header.FileSize));
                if (storedBytes.Length != header.FileSize)
                {
                    throw new InvalidDataException("Invalid GFLXPACK file bounds.");
                }

                var data = header.CompressionType == 2
                    ? Lz4Block.Decompress(storedBytes, checked((int)header.BufferSize))
                    : storedBytes;
                files.Add(new GfPakFile(index, fileHashes[index], entry.FolderHash, entry.RelativeHash, data));
            }

            return new GfPakArchive { Files = files };
        }

        public byte[] Write()
        {
            var folderGroups = Files
                .GroupBy(file => file.FolderHash)
                .Select(group => group.OrderBy(file => file.Index).ToArray())
                .ToArray();

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(0x1000u);
            writer.Write(0u);
            writer.Write((uint)Files.Count);
            writer.Write((uint)folderGroups.Length);

            var fileHeaderOffsetPointer = stream.Position;
            writer.Write(0ul);
            var hashOffsetPointer = stream.Position;
            writer.Write(0ul);
            var folderOffsetPointers = new long[folderGroups.Length];
            for (var index = 0; index < folderOffsetPointers.Length; index++)
            {
                folderOffsetPointers[index] = stream.Position;
                writer.Write(0ul);
            }

            var hashOffset = stream.Position;
            foreach (var file in Files.OrderBy(file => file.Index))
            {
                writer.Write(file.FileHash);
            }

            PatchU64(stream, hashOffsetPointer, (ulong)hashOffset);

            for (var index = 0; index < folderGroups.Length; index++)
            {
                var group = folderGroups[index];
                var folderOffset = stream.Position;
                PatchU64(stream, folderOffsetPointers[index], (ulong)folderOffset);
                writer.Write(group[0].FolderHash);
                writer.Write((uint)group.Length);
                writer.Write(0xCCu);
                foreach (var file in group)
                {
                    writer.Write(file.RelativeHash);
                    writer.Write((uint)file.Index);
                    writer.Write(0xCCu);
                }
            }

            var compressedFiles = Files
                .OrderBy(file => file.Index)
                .Select(file => Lz4Block.CompressLiterals(file.Data))
                .ToArray();
            var orderedFiles = Files.OrderBy(file => file.Index).ToArray();

            var fileHeaderOffset = stream.Position;
            PatchU64(stream, fileHeaderOffsetPointer, (ulong)fileHeaderOffset);
            var dataOffset = fileHeaderOffset + compressedFiles.Length * 0x18L;
            var nextDataOffset = dataOffset;
            for (var index = 0; index < compressedFiles.Length; index++)
            {
                writer.Write((ushort)9);
                writer.Write((ushort)2);
                writer.Write((uint)orderedFiles[index].Data.Length);
                writer.Write((uint)compressedFiles[index].Length);
                writer.Write(0xCCu);
                writer.Write((ulong)nextDataOffset);
                nextDataOffset += compressedFiles[index].Length;
            }

            foreach (var compressed in compressedFiles)
            {
                writer.Write(compressed);
            }

            writer.Flush();
            return stream.ToArray();
        }

        private static void PatchU64(Stream stream, long offset, ulong value)
        {
            var oldPosition = stream.Position;
            stream.Position = offset;
            Span<byte> bytes = stackalloc byte[sizeof(ulong)];
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
            stream.Write(bytes);
            stream.Position = oldPosition;
        }

        private sealed record PakFileHeader(
            ushort Level,
            ushort CompressionType,
            uint BufferSize,
            uint FileSize,
            uint Reserved,
            ulong Offset);
    }

    private static class Lz4Block
    {
        public static byte[] Decompress(byte[] compressed, int decompressedLength)
        {
            var output = new byte[decompressedLength];
            var inputPosition = 0;
            var outputPosition = 0;

            while (inputPosition < compressed.Length && outputPosition < output.Length)
            {
                var token = compressed[inputPosition++];
                var literalLength = ReadLength(compressed, ref inputPosition, token >> 4);
                if (inputPosition + literalLength > compressed.Length || outputPosition + literalLength > output.Length)
                {
                    throw new InvalidDataException("Invalid LZ4 literal bounds.");
                }

                Buffer.BlockCopy(compressed, inputPosition, output, outputPosition, literalLength);
                inputPosition += literalLength;
                outputPosition += literalLength;

                if (inputPosition >= compressed.Length)
                {
                    break;
                }

                if (inputPosition + 2 > compressed.Length)
                {
                    throw new InvalidDataException("Invalid LZ4 match offset.");
                }

                var back = compressed[inputPosition] | (compressed[inputPosition + 1] << 8);
                inputPosition += 2;
                if (back <= 0 || back > outputPosition)
                {
                    throw new InvalidDataException("Invalid LZ4 match distance.");
                }

                var matchLength = ReadLength(compressed, ref inputPosition, token & 0xF) + 4;
                var matchPosition = outputPosition - back;
                if (outputPosition + matchLength > output.Length)
                {
                    throw new InvalidDataException("Invalid LZ4 match bounds.");
                }

                if (matchLength <= back)
                {
                    Buffer.BlockCopy(output, matchPosition, output, outputPosition, matchLength);
                    outputPosition += matchLength;
                }
                else
                {
                    for (var index = 0; index < matchLength; index++)
                    {
                        output[outputPosition++] = output[matchPosition++];
                    }
                }
            }

            if (outputPosition != output.Length)
            {
                throw new InvalidDataException("Invalid LZ4 decompressed length.");
            }

            return output;
        }

        public static byte[] CompressLiterals(byte[] data)
        {
            using var stream = new MemoryStream();
            var literalLength = data.Length;
            var tokenLength = Math.Min(literalLength, 0xF);
            stream.WriteByte((byte)(tokenLength << 4));
            if (literalLength >= 0xF)
            {
                var remaining = literalLength - 0xF;
                while (remaining >= 0xFF)
                {
                    stream.WriteByte(0xFF);
                    remaining -= 0xFF;
                }

                stream.WriteByte((byte)remaining);
            }

            stream.Write(data);
            return stream.ToArray();
        }

        private static int ReadLength(byte[] data, ref int position, int length)
        {
            if (length != 0xF)
            {
                return length;
            }

            int next;
            do
            {
                if (position >= data.Length)
                {
                    throw new InvalidDataException("Invalid LZ4 extended length.");
                }

                next = data[position++];
                length += next;
            }
            while (next == 0xFF);

            return length;
        }
    }

    private static class FlatBufferAnimation
    {
        public static uint GetFrameRate(byte[] data)
        {
            var frameRateOffset = GetInfoFieldOffset(data, 2);
            return frameRateOffset < 0 ? 0 : ReadU32(data, frameRateOffset);
        }

        public static uint GetKeyFrames(byte[] data)
        {
            var keyFrameOffset = GetInfoFieldOffset(data, 1);
            return keyFrameOffset < 0 ? 0 : ReadU32(data, keyFrameOffset);
        }

        public static void SetKeyFrames(byte[] data, uint keyFrames)
        {
            var keyFrameOffset = GetInfoFieldOffset(data, 1);
            if (keyFrameOffset < 0)
            {
                throw new InvalidDataException("GF animation KeyFrames field not found.");
            }

            WriteU32(data, keyFrameOffset, keyFrames);
        }

        public static void SetFrameRate(byte[] data, uint frameRate)
        {
            var frameRateOffset = GetInfoFieldOffset(data, 2);
            if (frameRateOffset < 0)
            {
                throw new InvalidDataException("GF animation FrameRate field not found.");
            }

            WriteU32(data, frameRateOffset, frameRate);
        }

        private static int GetInfoFieldOffset(byte[] data, int infoFieldIndex)
        {
            var root = ReadUOffset(data, 0);
            var infoOffset = GetTableFieldOffset(data, root, 0);
            if (infoOffset == 0)
            {
                return -1;
            }

            var infoTable = checked(root + infoOffset + ReadUOffset(data, root + infoOffset));
            var fieldOffset = GetTableFieldOffset(data, infoTable, infoFieldIndex);
            return fieldOffset == 0 ? -1 : infoTable + fieldOffset;
        }

        private static int GetTableFieldOffset(byte[] data, int tableOffset, int fieldIndex)
        {
            if (tableOffset < sizeof(int) || tableOffset + sizeof(int) > data.Length)
            {
                return 0;
            }

            var vtableOffset = tableOffset - ReadI32(data, tableOffset);
            if (vtableOffset < 0 || vtableOffset + sizeof(ushort) * 2 > data.Length)
            {
                return 0;
            }

            var vtableLength = ReadU16(data, vtableOffset);
            var entryOffset = 4 + fieldIndex * 2;
            if (entryOffset + 2 > vtableLength || vtableOffset + entryOffset + 2 > data.Length)
            {
                return 0;
            }

            return ReadU16(data, vtableOffset + entryOffset);
        }
    }

    private sealed record FloatVectorParam(
        int ValueFieldLocation,
        int VectorOffset,
        float[] Values);

    private static class FlatBufferMaterial
    {
        public static IReadOnlyList<FloatVectorParam> GetFloatVectorParams(byte[] data, string paramName)
        {
            var root = ReadUOffset(data, 0);
            var materialTable = GetTableFieldTarget(data, root, 2);
            if (materialTable < 0)
            {
                return [];
            }

            var materialVector = GetTableFieldTarget(data, materialTable, 0);
            if (materialVector < 0 || !TryGetVectorCount(data, materialVector, out var materialCount))
            {
                return [];
            }

            var result = new List<FloatVectorParam>();
            for (var index = 0; index < materialCount; index++)
            {
                var materialItem = GetVectorTableElement(data, materialVector, index);
                if (materialItem < 0)
                {
                    continue;
                }

                CollectFloatVectorParams(data, materialItem, 2, paramName, result);
                CollectFloatVectorParams(data, materialItem, 3, paramName, result);
            }

            return result;
        }

        public static (byte[] Data, int ChangedVectors) ExpandFloatVectorParamTo60Fps(byte[] data, string paramName)
        {
            var vectors = GetFloatVectorParams(data, paramName);
            if (vectors.Count == 0)
            {
                return (data, 0);
            }

            var appended = new List<byte>(data);
            var patches = new List<(int FieldLocation, int NewVectorOffset)>();
            var changed = 0;

            foreach (var group in vectors.GroupBy(vector => vector.VectorOffset))
            {
                var first = group.First();
                if (first.Values.Length < 2)
                {
                    continue;
                }

                var expanded = ExpandFlipbookValues(first.Values);
                Align(appended, sizeof(uint));
                var newVectorOffset = appended.Count;
                AppendU32(appended, (uint)expanded.Length);
                foreach (var value in expanded)
                {
                    AppendF32(appended, value);
                }

                foreach (var vectorParam in group)
                {
                    patches.Add((vectorParam.ValueFieldLocation, newVectorOffset));
                }

                changed++;
            }

            if (changed == 0)
            {
                return (data, 0);
            }

            var output = appended.ToArray();
            foreach (var (fieldLocation, newVectorOffset) in patches)
            {
                var relative = checked((uint)(newVectorOffset - fieldLocation));
                WriteU32(output, fieldLocation, relative);
            }

            return (output, changed);
        }

        private static void CollectFloatVectorParams(
            byte[] data,
            int materialItem,
            int fieldIndex,
            string paramName,
            List<FloatVectorParam> result)
        {
            var paramVector = GetTableFieldTarget(data, materialItem, fieldIndex);
            if (paramVector < 0 || !TryGetVectorCount(data, paramVector, out var count))
            {
                return;
            }

            for (var index = 0; index < count; index++)
            {
                var paramTable = GetVectorTableElement(data, paramVector, index);
                if (paramTable < 0)
                {
                    continue;
                }

                var name = GetTableFieldString(data, paramTable, 0);
                if (!string.Equals(name, paramName, StringComparison.Ordinal))
                {
                    continue;
                }

                var vector = GetFloatValueVector(data, paramTable, out var fieldLocation);
                if (vector < 0 || !TryGetVectorCount(data, vector, out var valueCount) || valueCount < 2)
                {
                    continue;
                }

                var values = new float[valueCount];
                for (var valueIndex = 0; valueIndex < valueCount; valueIndex++)
                {
                    values[valueIndex] = ReadF32(data, vector + sizeof(uint) + valueIndex * sizeof(float));
                }

                result.Add(new FloatVectorParam(fieldLocation, vector, values));
            }
        }

        private static int GetFloatValueVector(byte[] data, int paramTable, out int fieldLocation)
        {
            fieldLocation = -1;
            var valueTable = GetTableFieldTarget(data, paramTable, 2);
            if (valueTable < 0)
            {
                return -1;
            }

            fieldLocation = GetTableFieldLocation(data, valueTable, 0);
            return fieldLocation < 0 ? -1 : GetOffsetTarget(data, fieldLocation);
        }

        private static float[] ExpandFlipbookValues(IReadOnlyList<float> values)
        {
            var expanded = new float[(values.Count - 1) * 2 + 1];
            var outputIndex = 0;
            for (var index = 0; index < values.Count - 1; index++)
            {
                expanded[outputIndex++] = values[index];
                expanded[outputIndex++] = values[index];
            }

            expanded[outputIndex] = values[^1];
            return expanded;
        }

        private static void Align(List<byte> bytes, int alignment)
        {
            while (bytes.Count % alignment != 0)
            {
                bytes.Add(0);
            }
        }

        private static void AppendU32(List<byte> bytes, uint value)
        {
            Span<byte> scratch = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(scratch, value);
            bytes.AddRange(scratch.ToArray());
        }

        private static void AppendF32(List<byte> bytes, float value)
        {
            Span<byte> scratch = stackalloc byte[sizeof(float)];
            BinaryPrimitives.WriteSingleLittleEndian(scratch, value);
            bytes.AddRange(scratch.ToArray());
        }
    }

    private static int GetVectorTableElement(byte[] data, int vector, int index)
    {
        var location = vector + sizeof(uint) + index * sizeof(uint);
        return GetOffsetTarget(data, location);
    }

    private static string? GetTableFieldString(byte[] data, int table, int fieldIndex)
    {
        var location = GetTableFieldLocation(data, table, fieldIndex);
        if (location < 0)
        {
            return null;
        }

        var target = GetOffsetTarget(data, location);
        if (target < 0 || target + sizeof(uint) > data.Length)
        {
            return null;
        }

        var length = checked((int)ReadU32(data, target));
        if (target + sizeof(uint) + length > data.Length)
        {
            return null;
        }

        return Encoding.ASCII.GetString(data, target + sizeof(uint), length);
    }

    private static int GetTableFieldTarget(byte[] data, int table, int fieldIndex)
    {
        var location = GetTableFieldLocation(data, table, fieldIndex);
        return location < 0 ? -1 : GetOffsetTarget(data, location);
    }

    private static int GetTableFieldLocation(byte[] data, int table, int fieldIndex)
    {
        if (table < sizeof(int) || table + sizeof(int) > data.Length)
        {
            return -1;
        }

        var vtable = table - ReadI32(data, table);
        if (vtable < 0 || vtable + sizeof(ushort) * 2 > data.Length)
        {
            return -1;
        }

        var vtableLength = ReadU16(data, vtable);
        var entryOffset = 4 + fieldIndex * 2;
        if (entryOffset + sizeof(ushort) > vtableLength || vtable + entryOffset + sizeof(ushort) > data.Length)
        {
            return -1;
        }

        var relative = ReadU16(data, vtable + entryOffset);
        return relative == 0 ? -1 : table + relative;
    }

    private static int GetOffsetTarget(byte[] data, int offset)
    {
        if (offset < 0 || offset + sizeof(uint) > data.Length)
        {
            return -1;
        }

        var raw = ReadU32(data, offset);
        if (raw > int.MaxValue)
        {
            return -1;
        }

        var target = offset + (int)raw;
        return target >= 0 && target < data.Length ? target : -1;
    }

    private static bool TryGetVectorCount(byte[] data, int vector, out int count)
    {
        count = 0;
        if (vector < 0 || vector + sizeof(uint) > data.Length)
        {
            return false;
        }

        var raw = ReadU32(data, vector);
        if (raw > int.MaxValue)
        {
            return false;
        }

        count = (int)raw;
        return count >= 0 && vector + sizeof(uint) + count * (long)sizeof(uint) <= data.Length;
    }

    private static ushort ReadU16(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)));
    }

    private static uint ReadU32(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)));
    }

    private static int ReadI32(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, sizeof(int)));
    }

    private static int ReadUOffset(byte[] data, int offset)
    {
        return checked((int)ReadU32(data, offset));
    }

    private static float ReadF32(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset, sizeof(float)));
    }

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);
    }
}
