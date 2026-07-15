// SPDX-License-Identifier: GPL-3.0-only

using K4os.Compression.LZ4;
using System.Buffers.Binary;
using System.IO.Compression;

namespace KM.Formats.SwSh;

public enum SwShGfPackCompressionType : byte
{
    None = 0,
    Zlib = 1,
    Lz4 = 2,
    OodleKraken = 3,
    OodleLeviathan = 4,
    OodleMermaid = 5,
    OodleSelkie = 6,
    OodleHydra = 7,
}

public sealed record SwShGfPackNamedFile(
    string FileName,
    byte[] Data,
    SwShGfPackCompressionType CompressionType = SwShGfPackCompressionType.None);

public sealed class SwShGfPackFile
{
    public const ulong Magic = 0x4B434150_584C4647;

    private const int HeaderSize = 0x18;
    private const int FileHashAbsoluteSize = 0x08;
    private const int FileHashFolderInfoSize = 0x10;
    private const int FileHashIndexSize = 0x10;
    private const int FileDataSize = 0x18;
    private const ulong FnvPrime64 = 0x00000100000001B3;
    private const ulong FnvOffsetBasis64 = 0xCBF29CE484222645;

    private readonly List<FileEntry> entries;
    private readonly List<FolderEntry> folders;
    private readonly byte[]? sourceData;
    private readonly IReadOnlyList<int>? sourceFileEntryOffsets;

    private SwShGfPackFile(
        uint version,
        uint isRelocated,
        List<FileHashAbsoluteEntry> absoluteHashes,
        List<FolderEntry> folders,
        List<FileEntry> entries,
        byte[]? sourceData = null,
        IReadOnlyList<int>? sourceFileEntryOffsets = null)
    {
        Version = version;
        IsRelocated = isRelocated;
        AbsoluteHashes = absoluteHashes;
        this.folders = folders;
        this.entries = entries;
        this.sourceData = sourceData;
        this.sourceFileEntryOffsets = sourceFileEntryOffsets;
    }

    public uint Version { get; }

    public uint IsRelocated { get; }

    public IReadOnlyList<FileHashAbsoluteEntry> AbsoluteHashes { get; }

    public int FileCount => entries.Count;

    public static SwShGfPackFile Parse(ReadOnlySpan<byte> data)
    {
        try
        {
            return ParseCore(data);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("GFPAK contains an invalid count, size, or offset.", exception);
        }
    }

    private static SwShGfPackFile ParseCore(ReadOnlySpan<byte> data)
    {
        EnsureRange(data, 0, HeaderSize, "GFPAK header");
        var ranges = new StructuralRangeRegistry();
        ranges.Register(0, HeaderSize, "GFPAK header", "metadata", allowExactAlias: false);
        var magic = BinaryPrimitives.ReadUInt64LittleEndian(data[..sizeof(ulong)]);
        if (magic != Magic)
        {
            throw new InvalidDataException("GFPAK magic is not GFLXPACK.");
        }

        var version = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0x08, sizeof(uint)));
        var isRelocated = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0x0C, sizeof(uint)));
        var fileCount = ReadNonNegativeInt32(data, 0x10, "GFPAK file count");
        var folderCount = ReadNonNegativeInt32(data, 0x14, "GFPAK folder count");
        var pointerTableOffset = HeaderSize;
        var pointerTableLength = checked(sizeof(long) * (2 + folderCount));
        EnsureRange(data, pointerTableOffset, pointerTableLength, "GFPAK pointer table");
        ranges.Register(
            pointerTableOffset,
            pointerTableLength,
            "GFPAK pointer table",
            "metadata",
            allowExactAlias: false);

        var fileTablePointer = ReadOffset(data, pointerTableOffset, "GFPAK file table pointer");
        var absoluteHashPointer = ReadOffset(data, pointerTableOffset + sizeof(long), "GFPAK absolute hash pointer");
        var folderPointers = new long[folderCount];
        for (var index = 0; index < folderPointers.Length; index++)
        {
            folderPointers[index] = ReadOffset(
                data,
                pointerTableOffset + (sizeof(long) * (2 + index)),
                "GFPAK folder hash pointer");
        }

        var absoluteHashLength = checked(fileCount * FileHashAbsoluteSize);
        EnsureRange(data, absoluteHashPointer, absoluteHashLength, "GFPAK absolute hash table");
        ranges.Register(
            absoluteHashPointer,
            absoluteHashLength,
            "GFPAK absolute hash table",
            "metadata",
            allowExactAlias: false);
        var fileTableLength = checked(fileCount * FileDataSize);
        EnsureRange(data, fileTablePointer, fileTableLength, "GFPAK file table");
        ranges.Register(
            fileTablePointer,
            fileTableLength,
            "GFPAK file table",
            "metadata",
            allowExactAlias: false);

        var absoluteHashes = ReadAbsoluteHashes(data, absoluteHashPointer, fileCount);
        var folders = ReadFolders(data, folderPointers, fileCount, ranges);
        var entries = ReadFileEntries(data, fileTablePointer, fileCount, ranges);
        var sourceFileEntryOffsets = Enumerable.Range(0, fileCount)
            .Select(index => checked((int)fileTablePointer + (index * FileDataSize)))
            .ToArray();

        return new SwShGfPackFile(
            version,
            isRelocated,
            absoluteHashes,
            folders,
            entries,
            data.ToArray(),
            sourceFileEntryOffsets);
    }

    public static SwShGfPackFile Create(IReadOnlyList<SwShGfPackNamedFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var absoluteHashes = new List<FileHashAbsoluteEntry>(files.Count);
        var folder = new FolderEntry(HashFnv1a64(""), []);
        var entries = new List<FileEntry>(files.Count);

        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            ArgumentException.ThrowIfNullOrWhiteSpace(file.FileName);
            absoluteHashes.Add(new FileHashAbsoluteEntry(HashFnv1a64(file.FileName)));
            folder.Files.Add(new FileHashIndexEntry(HashFnv1a64(Path.GetFileName(file.FileName)), index, 0xCC));
            entries.Add(FileEntry.Create(file.Data, file.CompressionType));
        }

        return new SwShGfPackFile(
            version: 0x1000,
            isRelocated: 0,
            absoluteHashes,
            [folder],
            entries);
    }

    public bool ContainsFileName(string fileName)
    {
        return FindFileNameIndex(fileName) >= 0;
    }

    public byte[] GetFileByName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var index = FindFileNameIndex(fileName);
        if (index < 0)
        {
            throw new InvalidDataException($"GFPAK file '{fileName}' is not present.");
        }

        return GetFile(index);
    }

    public bool TryGetFileByName(string fileName, out byte[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var index = FindFileNameIndex(fileName);
        if (index < 0)
        {
            data = [];
            return false;
        }

        data = GetFile(index);
        return true;
    }

    public void SetFileByName(string fileName, byte[] data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(data);

        var index = FindFileNameIndex(fileName);
        if (index < 0)
        {
            throw new InvalidDataException($"GFPAK file '{fileName}' is not present.");
        }

        var entry = entries[index];
        EnsureCanCompress(entry.CompressionType);
        var isOriginalData = data.AsSpan().SequenceEqual(GetOriginalFile(entry));
        entries[index] = entry with
        {
            DecompressedData = data.ToArray(),
            Modified = !isOriginalData,
        };
    }

    public byte[] Write()
    {
        if (sourceData is not null && sourceFileEntryOffsets is not null)
        {
            return WriteSourcePreserving();
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        var pointerTableSize = sizeof(long) * (2 + folders.Count);
        var absoluteHashOffset = HeaderSize + pointerTableSize;
        var folderOffsets = new long[folders.Count];
        var cursor = checked(absoluteHashOffset + (AbsoluteHashes.Count * FileHashAbsoluteSize));

        for (var folderIndex = 0; folderIndex < folders.Count; folderIndex++)
        {
            var folder = folders[folderIndex];
            folderOffsets[folderIndex] = cursor;
            cursor = checked(cursor + FileHashFolderInfoSize + (folder.Files.Count * FileHashIndexSize));
        }

        var fileTableOffset = cursor;

        WriteHeaderAndTables(writer, fileTableOffset, absoluteHashOffset, folderOffsets);

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var compressed = entry.Modified
                ? Compress(entry.DecompressedData ?? [], entry.CompressionType)
                : entry.CompressedData.ToArray();
            entries[index] = entry with
            {
                CompressedData = compressed,
                SizeCompressed = compressed.Length,
                SizeDecompressed = entry.Modified
                    ? entry.DecompressedData?.Length ?? 0
                    : entry.SizeDecompressed,
                OffsetPacked = checked((int)writer.BaseStream.Position),
            };

            writer.Write(compressed);
            while ((writer.BaseStream.Position % 0x10) != 0)
            {
                writer.Write((byte)0);
            }
        }

        writer.BaseStream.Position = 0;
        WriteHeaderAndTables(writer, fileTableOffset, absoluteHashOffset, folderOffsets);

        return stream.ToArray();
    }

    private byte[] WriteSourcePreserving()
    {
        var original = sourceData
            ?? throw new InvalidDataException("GFPAK source bytes are unavailable.");
        var fileEntryOffsets = sourceFileEntryOffsets
            ?? throw new InvalidDataException("GFPAK source file-table layout is unavailable.");
        if (fileEntryOffsets.Count != entries.Count)
        {
            throw new InvalidDataException("GFPAK source file-table layout is inconsistent.");
        }

        var modifiedEntries = entries
            .Select((entry, index) => (Entry: entry, Index: index))
            .Where(item => item.Entry.Modified)
            .ToArray();
        if (modifiedEntries.Length == 0)
        {
            return original.ToArray();
        }

        var output = new List<byte>(original.Length);
        output.AddRange(original);
        foreach (var (entry, index) in modifiedEntries)
        {
            while ((output.Count % 0x10) != 0)
            {
                output.Add(0);
            }

            var decompressed = entry.DecompressedData
                ?? throw new InvalidDataException("Modified GFPAK member data is unavailable.");
            var compressed = Compress(decompressed, entry.CompressionType);
            var packedOffset = output.Count;
            output.AddRange(compressed);
            while ((output.Count % 0x10) != 0)
            {
                output.Add(0);
            }

            var fileEntryOffset = fileEntryOffsets[index];
            WriteInt32At(output, checked(fileEntryOffset + 0x04), decompressed.Length);
            WriteInt32At(output, checked(fileEntryOffset + 0x08), compressed.Length);
            WriteInt32At(output, checked(fileEntryOffset + 0x10), packedOffset);
        }

        return output.ToArray();
    }

    public static ulong HashFnv1a64(ReadOnlySpan<char> input)
    {
        var hash = FnvOffsetBasis64;
        foreach (var character in input)
        {
            hash ^= character;
            hash *= FnvPrime64;
        }

        return hash;
    }

    private byte[] GetFile(int index)
    {
        var entry = entries[index];
        if (entry.DecompressedData is not null)
        {
            return entry.DecompressedData.ToArray();
        }

        EnsureCanDecompress(entry.CompressionType);
        var decompressed = Decompress(entry.CompressedData, entry.SizeDecompressed, entry.CompressionType);
        entries[index] = entry with { DecompressedData = decompressed };

        return decompressed.ToArray();
    }

    private static byte[] GetOriginalFile(FileEntry entry)
    {
        EnsureCanDecompress(entry.CompressionType);
        return Decompress(entry.CompressedData, entry.SizeDecompressed, entry.CompressionType);
    }

    private static void WriteInt32At(List<byte> output, int offset, int value)
    {
        if (offset < 0 || offset > output.Count - sizeof(int))
        {
            throw new InvalidDataException("GFPAK file-table patch points outside the output.");
        }

        BinaryPrimitives.WriteInt32LittleEndian(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(output).Slice(offset, sizeof(int)),
            value);
    }

    private int FindFileNameIndex(string fileName)
    {
        var hash = HashFnv1a64(Path.GetFileName(fileName));
        foreach (var folder in folders)
        {
            var match = folder.Files.FirstOrDefault(file => file.HashFnv1aPathFileName == hash);
            if (match is not null)
            {
                return match.Index;
            }
        }

        return -1;
    }

    private void WriteHeaderAndTables(
        BinaryWriter writer,
        long fileTableOffset,
        long absoluteHashOffset,
        IReadOnlyList<long> folderOffsets)
    {
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(IsRelocated);
        writer.Write(entries.Count);
        writer.Write(folders.Count);
        writer.Write(fileTableOffset);
        writer.Write(absoluteHashOffset);
        foreach (var folderOffset in folderOffsets)
        {
            writer.Write(folderOffset);
        }

        foreach (var absoluteHash in AbsoluteHashes)
        {
            writer.Write(absoluteHash.HashFnv1aPathFull);
        }

        foreach (var folder in folders)
        {
            writer.Write(folder.HashFnv1aPathFolderName);
            writer.Write(folder.Files.Count);
            writer.Write(folder.Padding);
            foreach (var file in folder.Files)
            {
                writer.Write(file.HashFnv1aPathFileName);
                writer.Write(file.Index);
                writer.Write(file.Padding);
            }
        }

        foreach (var entry in entries)
        {
            writer.Write(entry.Level);
            writer.Write((byte)entry.CompressionType);
            writer.Write(entry.TypePadding);
            writer.Write(entry.SizeDecompressed);
            writer.Write(entry.SizeCompressed);
            writer.Write(entry.Padding);
            writer.Write(entry.OffsetPacked);
            writer.Write(entry.Unused);
        }
    }

    private static List<FileHashAbsoluteEntry> ReadAbsoluteHashes(
        ReadOnlySpan<byte> data,
        long offset,
        int fileCount)
    {
        EnsureRange(data, offset, checked(fileCount * FileHashAbsoluteSize), "GFPAK absolute hash table");
        var hashes = new List<FileHashAbsoluteEntry>(fileCount);
        for (var index = 0; index < fileCount; index++)
        {
            hashes.Add(new FileHashAbsoluteEntry(
                BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(
                    checked((int)offset + (index * FileHashAbsoluteSize)),
                    sizeof(ulong)))));
        }

        return hashes;
    }

    private static List<FolderEntry> ReadFolders(
        ReadOnlySpan<byte> data,
        IReadOnlyList<long> offsets,
        int fileCount,
        StructuralRangeRegistry ranges)
    {
        var folders = new List<FolderEntry>(offsets.Count);
        foreach (var offset in offsets)
        {
            EnsureRange(data, offset, FileHashFolderInfoSize, "GFPAK folder hash table");
            var folderOffset = checked((int)offset);
            var fileCountInFolder = ReadNonNegativeInt32(data, folderOffset + 0x08, "GFPAK folder file count");
            EnsureRange(
                data,
                folderOffset + FileHashFolderInfoSize,
                checked(fileCountInFolder * FileHashIndexSize),
                "GFPAK folder file hash table");
            ranges.Register(
                folderOffset,
                checked(FileHashFolderInfoSize + (fileCountInFolder * FileHashIndexSize)),
                "GFPAK folder hash table",
                "metadata",
                allowExactAlias: false);
            var folder = new FolderEntry(
                BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(folderOffset, sizeof(ulong))),
                [])
            {
                Padding = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(folderOffset + 0x0C, sizeof(uint))),
            };

            for (var index = 0; index < fileCountInFolder; index++)
            {
                var entryOffset = folderOffset + FileHashFolderInfoSize + (index * FileHashIndexSize);
                var fileIndex = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(entryOffset + 0x08, sizeof(int)));
                if (fileIndex < 0 || fileIndex >= fileCount)
                {
                    throw new InvalidDataException("GFPAK file hash table points outside the file table.");
                }

                folder.Files.Add(new FileHashIndexEntry(
                    BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(entryOffset, sizeof(ulong))),
                    fileIndex,
                    BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(entryOffset + 0x0C, sizeof(uint)))));
            }

            folders.Add(folder);
        }

        return folders;
    }

    private static List<FileEntry> ReadFileEntries(
        ReadOnlySpan<byte> data,
        long offset,
        int fileCount,
        StructuralRangeRegistry ranges)
    {
        EnsureRange(data, offset, checked(fileCount * FileDataSize), "GFPAK file table");
        var entries = new List<FileEntry>(fileCount);
        for (var index = 0; index < fileCount; index++)
        {
            var entryOffset = checked((int)offset + (index * FileDataSize));
            var compressionType = (SwShGfPackCompressionType)data[entryOffset + 0x02];
            var sizeDecompressed = ReadNonNegativeInt32(data, entryOffset + 0x04, "GFPAK decompressed file size");
            var sizeCompressed = ReadNonNegativeInt32(data, entryOffset + 0x08, "GFPAK compressed file size");
            var offsetPacked = ReadNonNegativeInt32(data, entryOffset + 0x10, "GFPAK packed file offset");
            EnsureRange(data, offsetPacked, sizeCompressed, "GFPAK packed file data");
            ranges.Register(
                offsetPacked,
                sizeCompressed,
                $"GFPAK packed file {index}",
                "packed file data",
                allowExactAlias: true);
            var compressedData = data.Slice(offsetPacked, sizeCompressed).ToArray();
            entries.Add(new FileEntry(
                Level: BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(entryOffset, sizeof(ushort))),
                CompressionType: compressionType,
                TypePadding: data[entryOffset + 0x03],
                SizeDecompressed: sizeDecompressed,
                SizeCompressed: sizeCompressed,
                Padding: BinaryPrimitives.ReadInt32LittleEndian(data.Slice(entryOffset + 0x0C, sizeof(int))),
                OffsetPacked: offsetPacked,
                Unused: BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(entryOffset + 0x14, sizeof(uint))),
                CompressedData: compressedData,
                DecompressedData: null,
                Modified: false));
        }

        return entries;
    }

    private static byte[] Decompress(
        byte[] compressedData,
        int decompressedSize,
        SwShGfPackCompressionType compressionType)
    {
        return compressionType switch
        {
            SwShGfPackCompressionType.None => DecompressNone(compressedData, decompressedSize),
            SwShGfPackCompressionType.Zlib => DecompressZlib(compressedData, decompressedSize),
            SwShGfPackCompressionType.Lz4 => DecompressLz4(compressedData, decompressedSize),
            _ => throw CreateUnsupportedCompressionException(compressionType),
        };
    }

    private static byte[] Compress(byte[] decompressedData, SwShGfPackCompressionType compressionType)
    {
        return compressionType switch
        {
            SwShGfPackCompressionType.None => decompressedData.ToArray(),
            SwShGfPackCompressionType.Zlib => CompressZlib(decompressedData),
            SwShGfPackCompressionType.Lz4 => CompressLz4(decompressedData),
            _ => throw CreateUnsupportedCompressionException(compressionType),
        };
    }

    private static byte[] DecompressNone(byte[] compressedData, int decompressedSize)
    {
        if (compressedData.Length != decompressedSize)
        {
            throw new InvalidDataException("Uncompressed GFPAK file size does not match the file table.");
        }

        return compressedData.ToArray();
    }

    private static byte[] DecompressZlib(byte[] compressedData, int decompressedSize)
    {
        using var input = new MemoryStream(compressedData);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        var result = output.ToArray();
        if (result.Length != decompressedSize)
        {
            throw new InvalidDataException("Zlib-compressed GFPAK file size does not match the file table.");
        }

        return result;
    }

    private static byte[] DecompressLz4(byte[] compressedData, int decompressedSize)
    {
        var output = new byte[decompressedSize];
        var decoded = LZ4Codec.Decode(compressedData, 0, compressedData.Length, output, 0, output.Length);
        if (decoded != decompressedSize)
        {
            throw new InvalidDataException("LZ4-compressed GFPAK file size does not match the file table.");
        }

        return output;
    }

    private static byte[] CompressZlib(byte[] decompressedData)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(decompressedData);
        }

        return output.ToArray();
    }

    private static byte[] CompressLz4(byte[] decompressedData)
    {
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
            throw new InvalidDataException("GFPAK LZ4 compression failed.");
        }

        return output[..length];
    }

    private static bool CanDecompress(SwShGfPackCompressionType compressionType)
    {
        return compressionType
            is SwShGfPackCompressionType.None
            or SwShGfPackCompressionType.Zlib
            or SwShGfPackCompressionType.Lz4;
    }

    private static void EnsureCanDecompress(SwShGfPackCompressionType compressionType)
    {
        if (!CanDecompress(compressionType))
        {
            throw CreateUnsupportedCompressionException(compressionType);
        }
    }

    private static void EnsureCanCompress(SwShGfPackCompressionType compressionType)
    {
        if (!CanDecompress(compressionType))
        {
            throw CreateUnsupportedCompressionException(compressionType);
        }
    }

    private static InvalidDataException CreateUnsupportedCompressionException(
        SwShGfPackCompressionType compressionType)
    {
        return new InvalidDataException(
            $"GFPAK compression type '{compressionType}' is not supported by this workflow.");
    }

    private static long ReadOffset(ReadOnlySpan<byte> data, int offset, string name)
    {
        var value = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset, sizeof(long)));
        if (value < 0 || value > int.MaxValue)
        {
            throw new InvalidDataException($"{name} points outside the supported range.");
        }

        return value;
    }

    private static int ReadNonNegativeInt32(ReadOnlySpan<byte> data, int offset, string name)
    {
        EnsureRange(data, offset, sizeof(int), name);
        var value = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, sizeof(int)));
        if (value < 0)
        {
            throw new InvalidDataException($"{name} must not be negative.");
        }

        return value;
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, long offset, int length, string name)
    {
        if (offset < 0 || length < 0 || offset > data.Length || length > data.Length - offset)
        {
            throw new InvalidDataException($"{name} points outside the GFPAK file.");
        }
    }

    public sealed record FileHashAbsoluteEntry(ulong HashFnv1aPathFull);

    private sealed record FolderEntry(
        ulong HashFnv1aPathFolderName,
        List<FileHashIndexEntry> Files)
    {
        public uint Padding { get; init; } = 0xCC;
    }

    private sealed record FileHashIndexEntry(
        ulong HashFnv1aPathFileName,
        int Index,
        uint Padding);

    private sealed record FileEntry(
        ushort Level,
        SwShGfPackCompressionType CompressionType,
        byte TypePadding,
        int SizeDecompressed,
        int SizeCompressed,
        int Padding,
        int OffsetPacked,
        uint Unused,
        byte[] CompressedData,
        byte[]? DecompressedData,
        bool Modified)
    {
        public static FileEntry Create(byte[] data, SwShGfPackCompressionType compressionType)
        {
            ArgumentNullException.ThrowIfNull(data);
            EnsureCanCompress(compressionType);
            var compressedData = Compress(data, compressionType);

            return new FileEntry(
                Level: 9,
                CompressionType: compressionType,
                TypePadding: 0,
                SizeDecompressed: data.Length,
                SizeCompressed: compressedData.Length,
                Padding: 0xCC,
                OffsetPacked: 0,
                Unused: 0,
                CompressedData: compressedData,
                DecompressedData: data.ToArray(),
                Modified: true);
        }
    }

    private sealed class StructuralRangeRegistry
    {
        private readonly List<StructuralRange> ranges = [];

        public void Register(
            long offset,
            int length,
            string label,
            string kind,
            bool allowExactAlias)
        {
            if (length == 0)
            {
                return;
            }

            if (offset < 0 || offset > int.MaxValue)
            {
                throw new InvalidDataException($"{label} points outside the supported GFPAK range.");
            }

            var intOffset = (int)offset;
            foreach (var existing in ranges)
            {
                if (!RangesOverlap(intOffset, length, existing.Offset, existing.Length))
                {
                    continue;
                }

                var exactAlias = intOffset == existing.Offset && length == existing.Length;
                if (exactAlias
                    && allowExactAlias
                    && existing.AllowExactAlias
                    && string.Equals(kind, existing.Kind, StringComparison.Ordinal))
                {
                    continue;
                }

                throw new InvalidDataException(
                    $"{label} and {existing.Label} overlap unsafely.");
            }

            ranges.Add(new StructuralRange(intOffset, length, label, kind, allowExactAlias));
        }

        private static bool RangesOverlap(int firstOffset, int firstLength, int secondOffset, int secondLength)
        {
            return firstOffset < (long)secondOffset + secondLength
                && secondOffset < (long)firstOffset + firstLength;
        }

        private sealed record StructuralRange(
            int Offset,
            int Length,
            string Label,
            string Kind,
            bool AllowExactAlias);
    }
}
