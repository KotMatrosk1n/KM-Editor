// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Formats.ZA.Trinity;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace KM.Formats.ZA;

public sealed class ZaTrinityArchive : IDisposable
{
    public const int IndexSchemaVersion = 1;

    private const long PackCacheBudgetBytes = 64L * 1024 * 1024;
    private const int MaximumBoundedFileEntries = 500_000;
    private const int MaximumBoundedPackEntries = 100_000;
    private const int MaximumBoundedFilesPerPack = 250_000;
    private const string DescriptorRelativePath = "arc/data.trpfd";
    private const string FileSystemRelativePath = "arc/data.trpfs";
    private const int OneFileHeaderSize = 16;

    private static readonly ConditionalWeakTable<ZaTrinityArchiveIndex, CompiledIndexLookup> CompiledIndexes = new();

    private readonly string trpfsPath;
    private readonly CompiledIndexLookup compiledIndex;
    private readonly ByteBudgetLruCache<ulong, PackedArchiveCacheEntry> packCache = new(PackCacheBudgetBytes);
    private readonly string? compressionSupportFolderPath;
    private readonly long? maximumPackBytes;
    private ZaCompressionRuntimeLibrary? compressionLibrary;
    private bool ownsCompressionLibrary;
    private bool disposed;

    private ZaTrinityArchive(
        string trpfsPath,
        CompiledIndexLookup compiledIndex,
        string? compressionSupportFolderPath,
        ZaCompressionRuntimeLibrary? compressionLibrary,
        long? maximumPackBytes)
    {
        this.trpfsPath = trpfsPath;
        this.compiledIndex = compiledIndex;
        this.compressionSupportFolderPath = compressionSupportFolderPath;
        this.compressionLibrary = compressionLibrary;
        this.maximumPackBytes = maximumPackBytes;
        ownsCompressionLibrary = compressionLibrary is null;
    }

    public static ZaTrinityArchive Open(
        string romFsRoot,
        string? compressionSupportFolderPath = null,
        ZaCompressionRuntimeLibrary? compressionLibrary = null,
        ZaTrinityArchiveIndex? index = null,
        int? maximumIndexBytes = null,
        long? maximumPackBytes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(romFsRoot);
        if (maximumIndexBytes is <= 0 || maximumPackBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumIndexBytes));
        }

        var normalizedRoot = ResolveRomFsRoot(romFsRoot);
        var descriptorPath = Path.Combine(normalizedRoot, DescriptorRelativePath);
        var trpfsPath = Path.Combine(normalizedRoot, FileSystemRelativePath);

        if (!File.Exists(descriptorPath))
        {
            throw new FileNotFoundException("Pokemon Legends Z-A Trinity descriptor was not found.", descriptorPath);
        }

        if (!File.Exists(trpfsPath))
        {
            throw new FileNotFoundException("Pokemon Legends Z-A Trinity file system was not found.", trpfsPath);
        }

        var archiveIndex = index ?? BuildIndexFromFiles(descriptorPath, trpfsPath, maximumIndexBytes);
        if (maximumIndexBytes is not null
            && (archiveIndex.Files.Count > MaximumBoundedFileEntries
                || archiveIndex.Packs.Count > MaximumBoundedPackEntries))
        {
            throw new InvalidDataException("The bounded Trinity archive index exceeds its entry limit.");
        }

        return new ZaTrinityArchive(
            trpfsPath,
            CompiledIndexes.GetValue(archiveIndex, CreateCompiledIndex),
            compressionSupportFolderPath,
            compressionLibrary,
            maximumPackBytes);
    }

    public static ZaTrinityArchiveIndex BuildIndex(string romFsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(romFsRoot);

        var normalizedRoot = ResolveRomFsRoot(romFsRoot);
        var descriptorPath = Path.Combine(normalizedRoot, DescriptorRelativePath);
        var trpfsPath = Path.Combine(normalizedRoot, FileSystemRelativePath);

        if (!File.Exists(descriptorPath))
        {
            throw new FileNotFoundException("Pokemon Legends Z-A Trinity descriptor was not found.", descriptorPath);
        }

        if (!File.Exists(trpfsPath))
        {
            throw new FileNotFoundException("Pokemon Legends Z-A Trinity file system was not found.", trpfsPath);
        }

        return BuildIndexFromFiles(descriptorPath, trpfsPath, maximumIndexBytes: null);
    }

    public static ZaTrinityArchiveIndex BuildIndex(string romFsRoot, int maximumIndexBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(romFsRoot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumIndexBytes);

        var normalizedRoot = ResolveRomFsRoot(romFsRoot);
        var descriptorPath = Path.Combine(normalizedRoot, DescriptorRelativePath);
        var trpfsPath = Path.Combine(normalizedRoot, FileSystemRelativePath);

        if (!File.Exists(descriptorPath))
        {
            throw new FileNotFoundException("Pokemon Legends Z-A Trinity descriptor was not found.", descriptorPath);
        }

        if (!File.Exists(trpfsPath))
        {
            throw new FileNotFoundException("Pokemon Legends Z-A Trinity file system was not found.", trpfsPath);
        }

        return BuildIndexFromFiles(descriptorPath, trpfsPath, maximumIndexBytes);
    }

    public bool ContainsFile(string virtualPath)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return compiledIndex.FileIndicesByHash.ContainsKey(
            ZaTrinityPathHasher.HashPath(NormalizeVirtualPath(virtualPath)));
    }

    internal object CompiledIndexIdentity => compiledIndex;

    public bool TryReadFile(string virtualPath, out byte[] bytes)
    {
        return TryReadFileCore(virtualPath, maximumBytes: null, out bytes);
    }

    public bool TryReadFile(string virtualPath, int maximumBytes, out byte[] bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        return TryReadFileCore(virtualPath, maximumBytes, out bytes);
    }

    private bool TryReadFileCore(string virtualPath, int? maximumBytes, out byte[] bytes)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var fileHash = ZaTrinityPathHasher.HashPath(NormalizeVirtualPath(virtualPath));
        if (!compiledIndex.FileIndicesByHash.TryGetValue(fileHash, out var locationIndex))
        {
            bytes = [];
            return false;
        }

        var location = compiledIndex.Files[locationIndex];
        if (maximumPackBytes is { } packLimit && location.PackSize > packLimit)
        {
            throw new InvalidDataException("The bounded Trinity source pack exceeds its safe read limit.");
        }

        var pack = GetPack(location);
        if (!pack.FileIndicesByHash.TryGetValue(fileHash, out var fileIndex))
        {
            bytes = [];
            return false;
        }

        var packedFile = pack.Archive.Files(fileIndex)
            ?? throw new InvalidDataException($"Packed archive '{location.PackName}' has no file entry at index {fileIndex}.");
        if (maximumBytes is { } limit
            && (packedFile.FileBufferLength > limit || packedFile.FileSize > (ulong)limit))
        {
            throw new InvalidDataException("The bounded Trinity source file exceeds its safe read limit.");
        }

        bytes = ReadPackedFile(location.PackName, packedFile);
        return true;
    }

    public byte[] ReadFile(string virtualPath)
    {
        return TryReadFile(virtualPath, out var bytes)
            ? bytes
            : throw new FileNotFoundException($"Pokemon Legends Z-A Trinity file '{virtualPath}' was not found.");
    }

    public byte[] ReadFile(string virtualPath, int maximumBytes)
    {
        return TryReadFile(virtualPath, maximumBytes, out var bytes)
            ? bytes
            : throw new FileNotFoundException($"Pokemon Legends Z-A Trinity file '{virtualPath}' was not found.");
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        try
        {
            if (ownsCompressionLibrary)
            {
                compressionLibrary?.Dispose();
            }
        }
        finally
        {
            packCache.Clear();
            disposed = true;
        }
    }

    private static string ResolveRomFsRoot(string path)
    {
        var descriptorPath = Path.Combine(path, DescriptorRelativePath);
        if (File.Exists(descriptorPath))
        {
            return path;
        }

        var nestedRomFsPath = Path.Combine(path, "romfs");
        descriptorPath = Path.Combine(nestedRomFsPath, DescriptorRelativePath);
        if (File.Exists(descriptorPath))
        {
            return nestedRomFsPath;
        }

        return path;
    }

    private static FileSystem ReadFileSystem(string trpfsPath, int? maximumIndexBytes)
    {
        using var stream = new FileStream(trpfsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> header = stackalloc byte[OneFileHeaderSize];
        if (stream.Read(header) != OneFileHeaderSize)
        {
            throw new InvalidDataException("Pokemon Legends Z-A Trinity file system header is truncated.");
        }

        var fileSystemOffset = BinaryPrimitives.ReadInt64LittleEndian(header[8..]);
        if (fileSystemOffset < OneFileHeaderSize || fileSystemOffset >= stream.Length)
        {
            throw new InvalidDataException(
                $"Pokemon Legends Z-A Trinity file system offset {fileSystemOffset} is outside data.trpfs.");
        }

        var fileSystemSize = stream.Length - fileSystemOffset;
        if (fileSystemSize > int.MaxValue
            || (maximumIndexBytes is { } limit && fileSystemSize > limit))
        {
            throw new InvalidDataException(
                $"Pokemon Legends Z-A Trinity file system index is too large to load: {fileSystemSize} bytes.");
        }

        var buffer = new byte[fileSystemSize];
        stream.Position = fileSystemOffset;
        stream.ReadExactly(buffer);
        return FileSystem.GetRootAsFileSystem(new ByteBuffer(buffer));
    }

    private static ZaTrinityArchiveIndex BuildIndexFromFiles(
        string descriptorPath,
        string trpfsPath,
        int? maximumIndexBytes)
    {
        var descriptor = FileDescriptor.GetRootAsFileDescriptor(new ByteBuffer(
            ReadIndexBytes(descriptorPath, maximumIndexBytes)));
        var fileSystem = ReadFileSystem(trpfsPath, maximumIndexBytes);
        ValidateBoundedIndexShape(descriptor, fileSystem, maximumIndexBytes is not null);
        return new ZaTrinityArchiveIndex(
            IndexSchemaVersion,
            BuildFileIndexEntries(descriptor),
            BuildPackIndexEntries(fileSystem));
    }

    private static void ValidateBoundedIndexShape(
        FileDescriptor descriptor,
        FileSystem fileSystem,
        bool bounded)
    {
        if (descriptor.FileHashesLength != descriptor.FilesLength
            || descriptor.PackNamesLength != descriptor.PacksLength
            || fileSystem.FileHashesLength != fileSystem.FileOffsetsLength)
        {
            throw new InvalidDataException("The Trinity archive index vectors have inconsistent lengths.");
        }

        if (bounded
            && (descriptor.FileHashesLength > MaximumBoundedFileEntries
                || descriptor.PackNamesLength > MaximumBoundedPackEntries
                || fileSystem.FileHashesLength > MaximumBoundedPackEntries))
        {
            throw new InvalidDataException("The bounded Trinity archive index exceeds its entry limit.");
        }
    }

    private static byte[] ReadIndexBytes(string path, int? maximumIndexBytes)
    {
        if (maximumIndexBytes is null)
        {
            return File.ReadAllBytes(path);
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1_024,
            FileOptions.SequentialScan);
        if (stream.Length < 0 || stream.Length > maximumIndexBytes.Value)
        {
            throw new InvalidDataException("The bounded Trinity archive index exceeds its safe read limit.");
        }

        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
        {
            throw new InvalidDataException("The bounded Trinity archive index changed while it was read.");
        }

        return bytes;
    }

    private static Dictionary<ulong, int> BuildFileIndex(ZaTrinityArchiveIndex index)
    {
        if (index.SchemaVersion != IndexSchemaVersion)
        {
            throw new InvalidDataException(
                $"Pokemon Legends Z-A Trinity cache index schema {index.SchemaVersion} is not supported.");
        }

        var result = new Dictionary<ulong, int>(index.Files.Count);
        for (var fileIndex = 0; fileIndex < index.Files.Count; fileIndex++)
        {
            result[index.Files[fileIndex].FileHash] = fileIndex;
        }

        return result;
    }

    private static CompiledIndexLookup CreateCompiledIndex(ZaTrinityArchiveIndex index)
    {
        return new CompiledIndexLookup(
            index.Files,
            BuildFileIndex(index),
            BuildPackOffsetIndex(index));
    }

    private static IReadOnlyList<ZaTrinityArchiveFileIndexEntry> BuildFileIndexEntries(FileDescriptor descriptor)
    {
        var result = new List<ZaTrinityArchiveFileIndexEntry>(descriptor.FileHashesLength);
        var packNames = new string?[descriptor.PackNamesLength];
        var packSizes = new long[descriptor.PacksLength];
        var loadedPackSizes = new bool[descriptor.PacksLength];

        for (var index = 0; index < descriptor.FileHashesLength; index++)
        {
            var hash = descriptor.FileHashes(index);
            var file = descriptor.Files(index)
                ?? throw new InvalidDataException($"Trinity descriptor has no file entry at index {index}.");
            var packIndex = checked((int)file.PackIndex);

            if (packIndex < 0 || packIndex >= descriptor.PackNamesLength || packIndex >= descriptor.PacksLength)
            {
                throw new InvalidDataException($"Trinity descriptor pack index {packIndex} is invalid.");
            }

            var packName = packNames[packIndex] ??= descriptor.PackNames(packIndex)
                ?? throw new InvalidDataException($"Trinity descriptor pack name {packIndex} is missing.");
            if (!loadedPackSizes[packIndex])
            {
                var pack = descriptor.Packs(packIndex)
                    ?? throw new InvalidDataException($"Trinity descriptor pack entry {packIndex} is missing.");
                packSizes[packIndex] = checked((long)pack.FileSize);
                loadedPackSizes[packIndex] = true;
            }

            result.Add(new ZaTrinityArchiveFileIndexEntry(
                hash,
                packName,
                ZaTrinityPathHasher.HashPath(packName),
                packSizes[packIndex]));
        }

        return result;
    }

    private static Dictionary<ulong, ulong> BuildPackOffsetIndex(ZaTrinityArchiveIndex index)
    {
        if (index.SchemaVersion != IndexSchemaVersion)
        {
            throw new InvalidDataException(
                $"Pokemon Legends Z-A Trinity cache index schema {index.SchemaVersion} is not supported.");
        }

        var result = new Dictionary<ulong, ulong>(index.Packs.Count);
        foreach (var pack in index.Packs)
        {
            result[pack.PackHash] = pack.Offset;
        }

        return result;
    }

    private static IReadOnlyList<ZaTrinityArchivePackIndexEntry> BuildPackIndexEntries(FileSystem fileSystem)
    {
        if (fileSystem.FileHashesLength != fileSystem.FileOffsetsLength)
        {
            throw new InvalidDataException(
                $"Trinity file system has {fileSystem.FileHashesLength} hashes but {fileSystem.FileOffsetsLength} offsets.");
        }

        var result = new List<ZaTrinityArchivePackIndexEntry>(fileSystem.FileHashesLength);
        for (var index = 0; index < fileSystem.FileHashesLength; index++)
        {
            result.Add(new ZaTrinityArchivePackIndexEntry(
                fileSystem.FileHashes(index),
                fileSystem.FileOffsets(index)));
        }

        return result;
    }

    private PackedArchiveCacheEntry GetPack(ZaTrinityArchiveFileIndexEntry location)
    {
        if (packCache.TryGetValue(location.PackHash, out var cached))
        {
            return cached;
        }

        if (!compiledIndex.PackOffsetsByHash.TryGetValue(location.PackHash, out var packOffset))
        {
            throw new FileNotFoundException($"Pokemon Legends Z-A Trinity pack '{location.PackName}' was not indexed.");
        }

        if (location.PackSize < 0 || location.PackSize > int.MaxValue)
        {
            throw new InvalidDataException(
                $"Pokemon Legends Z-A Trinity pack '{location.PackName}' is too large to load: {location.PackSize} bytes.");
        }

        if (packOffset > long.MaxValue)
        {
            throw new InvalidDataException(
                $"Pokemon Legends Z-A Trinity pack '{location.PackName}' offset 0x{packOffset:X} is outside data.trpfs.");
        }

        var packOffsetValue = (long)packOffset;
        var packSize = checked((int)location.PackSize);
        byte[] packBytes;
        using (var stream = new FileStream(trpfsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (packOffsetValue < OneFileHeaderSize
                || packOffsetValue > stream.Length
                || packSize > stream.Length - packOffsetValue)
            {
                throw new InvalidDataException(
                    $"Pokemon Legends Z-A Trinity pack '{location.PackName}' at offset "
                    + $"0x{packOffsetValue:X} with length {packSize} is outside data.trpfs.");
            }

            packBytes = new byte[packSize];
            stream.Position = packOffsetValue;
            stream.ReadExactly(packBytes);
        }

        var archive = PackedArchive.GetRootAsPackedArchive(new ByteBuffer(packBytes));
        if (archive.FileHashesLength != archive.FilesLength
            || (maximumPackBytes is not null
                && archive.FileHashesLength > MaximumBoundedFilesPerPack))
        {
            throw new InvalidDataException("The bounded Trinity pack index is inconsistent or too large.");
        }

        var fileIndices = new Dictionary<ulong, int>(archive.FileHashesLength);
        for (var index = 0; index < archive.FileHashesLength; index++)
        {
            fileIndices[archive.FileHashes(index)] = index;
        }

        var entry = new PackedArchiveCacheEntry(packBytes, archive, fileIndices);
        packCache.Set(location.PackHash, entry, packBytes.LongLength);
        return entry;
    }

    private byte[] ReadPackedFile(string packName, PackedFile packedFile)
    {
        var payload = packedFile.GetFileBufferArray();
        if (packedFile.EncryptionType == -1)
        {
            return payload;
        }

        if (packedFile.FileSize > int.MaxValue)
        {
            throw new InvalidDataException(
                $"Packed file in '{packName}' is too large to decompress: {packedFile.FileSize} bytes.");
        }

        return GetCompressionLibrary().Decompress(payload, checked((int)packedFile.FileSize));
    }

    private ZaCompressionRuntimeLibrary GetCompressionLibrary()
    {
        if (compressionLibrary is not null)
        {
            return compressionLibrary;
        }

        compressionLibrary = ZaCompressionRuntimeLibrary.LoadFromFolder(compressionSupportFolderPath);
        return compressionLibrary;
    }

    private static string NormalizeVirtualPath(string virtualPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        var normalized = virtualPath.Replace('\\', '/');
        if (normalized.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["romfs/".Length..];
        }

        return normalized.TrimStart('/');
    }

    private sealed record CompiledIndexLookup(
        IReadOnlyList<ZaTrinityArchiveFileIndexEntry> Files,
        Dictionary<ulong, int> FileIndicesByHash,
        Dictionary<ulong, ulong> PackOffsetsByHash);

    private sealed record PackedArchiveCacheEntry(
        byte[] Buffer,
        PackedArchive Archive,
        Dictionary<ulong, int> FileIndicesByHash);
}
