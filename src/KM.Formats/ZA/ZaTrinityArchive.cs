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
    private const string DescriptorRelativePath = "arc/data.trpfd";
    private const string FileSystemRelativePath = "arc/data.trpfs";
    private const int OneFileHeaderSize = 16;

    private static readonly ConditionalWeakTable<ZaTrinityArchiveIndex, CompiledIndexLookup> CompiledIndexes = new();

    private readonly string trpfsPath;
    private readonly CompiledIndexLookup compiledIndex;
    private readonly ByteBudgetLruCache<ulong, PackedArchiveCacheEntry> packCache = new(PackCacheBudgetBytes);
    private readonly string? compressionSupportFolderPath;
    private ZaCompressionRuntimeLibrary? compressionLibrary;
    private bool ownsCompressionLibrary;
    private bool disposed;

    private ZaTrinityArchive(
        string trpfsPath,
        CompiledIndexLookup compiledIndex,
        string? compressionSupportFolderPath,
        ZaCompressionRuntimeLibrary? compressionLibrary)
    {
        this.trpfsPath = trpfsPath;
        this.compiledIndex = compiledIndex;
        this.compressionSupportFolderPath = compressionSupportFolderPath;
        this.compressionLibrary = compressionLibrary;
        ownsCompressionLibrary = compressionLibrary is null;
    }

    public static ZaTrinityArchive Open(
        string romFsRoot,
        string? compressionSupportFolderPath = null,
        ZaCompressionRuntimeLibrary? compressionLibrary = null,
        ZaTrinityArchiveIndex? index = null)
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

        var archiveIndex = index ?? BuildIndexFromFiles(descriptorPath, trpfsPath);

        return new ZaTrinityArchive(
            trpfsPath,
            CompiledIndexes.GetValue(archiveIndex, CreateCompiledIndex),
            compressionSupportFolderPath,
            compressionLibrary);
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

        return BuildIndexFromFiles(descriptorPath, trpfsPath);
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
        ObjectDisposedException.ThrowIf(disposed, this);

        var fileHash = ZaTrinityPathHasher.HashPath(NormalizeVirtualPath(virtualPath));
        if (!compiledIndex.FileIndicesByHash.TryGetValue(fileHash, out var locationIndex))
        {
            bytes = [];
            return false;
        }

        var location = compiledIndex.Files[locationIndex];

        var pack = GetPack(location);
        if (!pack.FileIndicesByHash.TryGetValue(fileHash, out var fileIndex))
        {
            bytes = [];
            return false;
        }

        var packedFile = pack.Archive.Files(fileIndex)
            ?? throw new InvalidDataException($"Packed archive '{location.PackName}' has no file entry at index {fileIndex}.");
        bytes = ReadPackedFile(location.PackName, packedFile);
        return true;
    }

    public byte[] ReadFile(string virtualPath)
    {
        return TryReadFile(virtualPath, out var bytes)
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

    private static FileSystem ReadFileSystem(string trpfsPath)
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
        if (fileSystemSize > int.MaxValue)
        {
            throw new InvalidDataException(
                $"Pokemon Legends Z-A Trinity file system index is too large to load: {fileSystemSize} bytes.");
        }

        var buffer = new byte[fileSystemSize];
        stream.Position = fileSystemOffset;
        stream.ReadExactly(buffer);
        return FileSystem.GetRootAsFileSystem(new ByteBuffer(buffer));
    }

    private static ZaTrinityArchiveIndex BuildIndexFromFiles(string descriptorPath, string trpfsPath)
    {
        var descriptor = FileDescriptor.GetRootAsFileDescriptor(new ByteBuffer(File.ReadAllBytes(descriptorPath)));
        var fileSystem = ReadFileSystem(trpfsPath);
        return new ZaTrinityArchiveIndex(
            IndexSchemaVersion,
            BuildFileIndexEntries(descriptor),
            BuildPackIndexEntries(fileSystem));
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

        if (location.PackSize > int.MaxValue)
        {
            throw new InvalidDataException(
                $"Pokemon Legends Z-A Trinity pack '{location.PackName}' is too large to load: {location.PackSize} bytes.");
        }

        var packBytes = new byte[location.PackSize];
        using (var stream = new FileStream(trpfsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            stream.Position = checked((long)packOffset);
            stream.ReadExactly(packBytes);
        }

        var archive = PackedArchive.GetRootAsPackedArchive(new ByteBuffer(packBytes));
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
