// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Formats.ZA.Trinity;
using System.Text;

namespace KM.Formats.ZA;

public static class ZaTrinityDescriptorPatcher
{
    public const string DescriptorVirtualPath = "arc/data.trpfd";
    private const int MaximumDescriptorBytes = 64 * 1024 * 1024;
    private const int MaximumDescriptorEntries = 1_000_000;
    private const int MaximumLayeredEntries = 100_000;
    private const int MaximumCombinedLayeredEntries = MaximumLayeredEntries * 2;
    private const int MaximumTraversalDepth = 128;
    private const int MaximumVirtualPathLength = 4_096;
    private const int MaximumPackNameLength = 4_096;
    private const int MaximumPackNameCharacters = 16 * 1024 * 1024;

    public static byte[] CreateLayeredDescriptor(string baseRomFsRoot, string outputRoot)
    {
        return CreateLayeredDescriptor(
            baseRomFsRoot,
            outputRoot,
            Array.Empty<string>());
    }

    public static byte[] CreateLayeredDescriptor(
        string baseRomFsRoot,
        string outputRoot,
        IEnumerable<string> excludedLayeredVirtualPaths)
    {
        return CreateLayeredDescriptorIncludingVirtualPaths(
            baseRomFsRoot,
            outputRoot,
            additionalLayeredVirtualPaths: [],
            excludedLayeredVirtualPaths: excludedLayeredVirtualPaths);
    }

    public static byte[] CreateLayeredDescriptorIncludingVirtualPaths(
        string baseRomFsRoot,
        string outputRoot,
        IEnumerable<string> additionalLayeredVirtualPaths,
        IEnumerable<string> excludedLayeredVirtualPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRomFsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentNullException.ThrowIfNull(additionalLayeredVirtualPaths);
        ArgumentNullException.ThrowIfNull(excludedLayeredVirtualPaths);

        return CreateLayeredDescriptorFromVirtualPaths(
            baseRomFsRoot,
            EnumerateLayeredVirtualPaths(outputRoot),
            additionalLayeredVirtualPaths,
            excludedLayeredVirtualPaths);
    }

    public static byte[] CreateLayeredDescriptorFromVirtualPaths(
        string baseRomFsRoot,
        IEnumerable<string> layeredVirtualPaths,
        IEnumerable<string> excludedLayeredVirtualPaths)
    {
        return CreateLayeredDescriptorFromVirtualPaths(
            baseRomFsRoot,
            layeredVirtualPaths,
            additionalLayeredVirtualPaths: [],
            excludedLayeredVirtualPaths);
    }

    public static byte[] CreateLayeredDescriptorFromVirtualPaths(
        string baseRomFsRoot,
        IEnumerable<string> layeredVirtualPaths,
        IEnumerable<string> additionalLayeredVirtualPaths,
        IEnumerable<string> excludedLayeredVirtualPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRomFsRoot);
        ArgumentNullException.ThrowIfNull(layeredVirtualPaths);
        ArgumentNullException.ThrowIfNull(additionalLayeredVirtualPaths);
        ArgumentNullException.ThrowIfNull(excludedLayeredVirtualPaths);

        var excludedPaths = MaterializeVirtualPaths(excludedLayeredVirtualPaths)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var layeredFileHashes = MaterializeVirtualPaths(layeredVirtualPaths)
            .Where(path => !string.Equals(path, DescriptorVirtualPath, StringComparison.OrdinalIgnoreCase))
            .Where(path => !excludedPaths.Contains(path))
            .Select(ZaTrinityPathHasher.HashPath)
            .ToHashSet();
        var additionalFileHashes = MaterializeDistinctVirtualPathHashes(
            additionalLayeredVirtualPaths);
        layeredFileHashes.UnionWith(additionalFileHashes);

        var descriptorBytes = ReadBaseDescriptor(baseRomFsRoot);
        return layeredFileHashes.Count == 0
            ? descriptorBytes
            : RemoveFileHashesCore(
                descriptorBytes,
                layeredFileHashes,
                MaximumCombinedLayeredEntries);
    }

    public static byte[] ReadBaseDescriptor(string baseRomFsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRomFsRoot);

        var descriptorPath = Path.Combine(ResolveRomFsRoot(baseRomFsRoot), "arc", "data.trpfd");
        if (!File.Exists(descriptorPath))
        {
            throw new FileNotFoundException("Pokemon Legends Z-A Trinity descriptor was not found.", descriptorPath);
        }

        return ReadBoundedFile(descriptorPath);
    }

    public static bool HasLayeredVirtualPaths(
        string outputRoot,
        IEnumerable<string> excludedLayeredVirtualPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentNullException.ThrowIfNull(excludedLayeredVirtualPaths);

        var excludedPaths = MaterializeVirtualPaths(excludedLayeredVirtualPaths)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return EnumerateLayeredVirtualPaths(outputRoot)
            .Any(path => !excludedPaths.Contains(path));
    }

    public static bool HasLayeredVirtualPaths(
        IEnumerable<string> layeredVirtualPaths,
        IEnumerable<string> excludedLayeredVirtualPaths)
    {
        ArgumentNullException.ThrowIfNull(layeredVirtualPaths);
        ArgumentNullException.ThrowIfNull(excludedLayeredVirtualPaths);

        var excludedPaths = excludedLayeredVirtualPaths
            .Select(NormalizeVirtualPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return MaterializeVirtualPaths(layeredVirtualPaths)
            .Any(path => !string.Equals(path, DescriptorVirtualPath, StringComparison.OrdinalIgnoreCase)
                && !excludedPaths.Contains(path));
    }

    public static byte[] RemoveFileHashes(byte[] descriptorBytes, IReadOnlySet<ulong> removedHashes)
    {
        return RemoveFileHashesCore(
            descriptorBytes,
            removedHashes,
            MaximumLayeredEntries);
    }

    private static byte[] RemoveFileHashesCore(
        byte[] descriptorBytes,
        IReadOnlySet<ulong> removedHashes,
        int maximumRemovedHashes)
    {
        ArgumentNullException.ThrowIfNull(descriptorBytes);
        ArgumentNullException.ThrowIfNull(removedHashes);
        if (descriptorBytes.Length > MaximumDescriptorBytes
            || removedHashes.Count > maximumRemovedHashes)
        {
            throw new InvalidDataException("The Trinity descriptor request exceeds its bounded limit.");
        }

        var descriptor = FileDescriptor.GetRootAsFileDescriptor(new ByteBuffer(descriptorBytes));
        var model = ReadDescriptor(descriptor);
        var files = new List<FileEntry>(model.Files.Count);
        var hashes = new List<ulong>(model.FileHashes.Count);

        for (var index = 0; index < model.FileHashes.Count; index++)
        {
            var hash = model.FileHashes[index];
            if (removedHashes.Contains(hash))
            {
                continue;
            }

            hashes.Add(hash);
            files.Add(model.Files[index]);
        }

        return WriteDescriptor(model with { FileHashes = hashes, Files = files });
    }

    private static DescriptorModel ReadDescriptor(FileDescriptor descriptor)
    {
        if (descriptor.FileHashesLength < 0
            || descriptor.FileHashesLength > MaximumDescriptorEntries
            || descriptor.FilesLength < 0
            || descriptor.FilesLength > MaximumDescriptorEntries
            || descriptor.PackNamesLength < 0
            || descriptor.PackNamesLength > MaximumDescriptorEntries
            || descriptor.PacksLength < 0
            || descriptor.PacksLength > MaximumDescriptorEntries)
        {
            throw new InvalidDataException("The Trinity descriptor tables exceed their bounded row limit.");
        }

        if (descriptor.FileHashesLength != descriptor.FilesLength)
        {
            throw new InvalidDataException(
                $"Trinity descriptor has {descriptor.FileHashesLength} hashes but {descriptor.FilesLength} file entries.");
        }

        var fileHashes = new List<ulong>(descriptor.FileHashesLength);
        var files = new List<FileEntry>(descriptor.FilesLength);
        var packNames = new List<string>(descriptor.PackNamesLength);
        var packs = new List<PackEntry>(descriptor.PacksLength);

        for (var index = 0; index < descriptor.FileHashesLength; index++)
        {
            fileHashes.Add(descriptor.FileHashes(index));
            var file = descriptor.Files(index)
                ?? throw new InvalidDataException($"Trinity descriptor has no file entry at index {index}.");
            files.Add(new FileEntry(file.PackIndex, file.Unk1 is not null));
        }

        var packNameCharacters = 0;
        for (var index = 0; index < descriptor.PackNamesLength; index++)
        {
            var packName = descriptor.PackNames(index)
                ?? throw new InvalidDataException($"Trinity descriptor pack name {index} is missing.");
            if (packName.Length > MaximumPackNameLength
                || packNameCharacters > MaximumPackNameCharacters - packName.Length)
            {
                throw new InvalidDataException("The Trinity descriptor pack names exceed their bounded limit.");
            }

            packNameCharacters += packName.Length;
            packNames.Add(packName);
        }

        for (var index = 0; index < descriptor.PacksLength; index++)
        {
            var pack = descriptor.Packs(index)
                ?? throw new InvalidDataException($"Trinity descriptor pack entry {index} is missing.");
            packs.Add(new PackEntry(pack.FileSize, pack.FileCount));
        }

        return new DescriptorModel(fileHashes, packNames, files, packs);
    }

    private static byte[] WriteDescriptor(DescriptorModel model)
    {
        var builder = new FlatBufferBuilder(1024);
        var packNameOffsets = model.PackNames
            .Select(builder.CreateString)
            .ToArray();
        var fileOffsets = model.Files
            .Select(file =>
            {
                Offset<EmptyTable> emptyTable = default;
                if (file.HasUnk1)
                {
                    EmptyTable.StartEmptyTable(builder);
                    emptyTable = EmptyTable.EndEmptyTable(builder);
                }

                return FileDescriptorEntry.CreateFileDescriptorEntry(
                    builder,
                    file.PackIndex,
                    emptyTable);
            })
            .ToArray();
        var packOffsets = model.Packs
            .Select(pack => PackDescriptorEntry.CreatePackDescriptorEntry(builder, pack.FileSize, pack.FileCount))
            .ToArray();

        var fileHashes = FileDescriptor.CreateFileHashesVector(builder, model.FileHashes.ToArray());
        var packNames = FileDescriptor.CreatePackNamesVector(builder, packNameOffsets);
        var files = FileDescriptor.CreateFilesVector(builder, fileOffsets);
        var packs = FileDescriptor.CreatePacksVector(builder, packOffsets);
        var root = FileDescriptor.CreateFileDescriptor(builder, fileHashes, packNames, files, packs);
        FileDescriptor.FinishFileDescriptorBuffer(builder, root);
        var bytes = builder.SizedByteArray();
        if (bytes.Length > MaximumDescriptorBytes)
        {
            throw new InvalidDataException("The patched Trinity descriptor exceeds its bounded size.");
        }

        return bytes;
    }

    private static IEnumerable<string> EnumerateLayeredVirtualPaths(string outputRoot)
    {
        var romFsRoot = Path.Combine(outputRoot, "romfs");
        if (!Directory.Exists(romFsRoot))
        {
            return [];
        }

        var root = Path.GetFullPath(romFsRoot);
        ValidateLayeredRomFsRoot(root);
        var paths = new List<string>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            var (current, depth) = pending.Pop();
            if (depth > MaximumTraversalDepth)
            {
                throw new InvalidDataException("The layered RomFS exceeds its bounded traversal depth.");
            }

            foreach (var child in Directory.EnumerateFileSystemEntries(current))
            {
                var info = Directory.Exists(child)
                    ? (FileSystemInfo)new DirectoryInfo(child)
                    : new FileInfo(child);
                info.Refresh();
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)
                    || !string.IsNullOrEmpty(info.LinkTarget))
                {
                    throw new InvalidDataException("The layered RomFS contains an unsafe linked entry.");
                }

                if (info is DirectoryInfo)
                {
                    pending.Push((child, depth + 1));
                    continue;
                }

                if (paths.Count == MaximumLayeredEntries)
                {
                    throw new InvalidDataException("The layered RomFS exceeds its bounded entry limit.");
                }

                var relative = NormalizeVirtualPath(
                    Path.GetRelativePath(root, child).Replace('\\', '/'));
                if (!string.Equals(relative, DescriptorVirtualPath, StringComparison.OrdinalIgnoreCase))
                {
                    paths.Add(relative);
                }
            }
        }

        return paths;
    }

    private static void ValidateLayeredRomFsRoot(string root)
    {
        var info = new DirectoryInfo(root);
        info.Refresh();
        if (!info.Exists
            || (info.Attributes.HasFlag(FileAttributes.ReparsePoint)
                && !string.IsNullOrEmpty(info.LinkTarget)))
        {
            throw new InvalidDataException("The layered RomFS root is not a safe physical directory.");
        }
    }

    private static string ResolveRomFsRoot(string path)
    {
        if (File.Exists(Path.Combine(path, "arc", "data.trpfd")))
        {
            return path;
        }

        var nestedRomFsPath = Path.Combine(path, "romfs");
        return File.Exists(Path.Combine(nestedRomFsPath, "arc", "data.trpfd"))
            ? nestedRomFsPath
            : path;
    }

    private static string NormalizeVirtualPath(string virtualPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        var normalized = virtualPath.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["romfs/".Length..];
        }

        var segments = normalized.Split('/');
        if (normalized.Length > MaximumVirtualPathLength
            || Encoding.UTF8.GetByteCount(normalized) > MaximumVirtualPathLength
            || segments.Length == 0
            || segments.Length > MaximumTraversalDepth
            || segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."))
        {
            throw new ArgumentException(
                $"Pokemon Legends Z-A virtual path '{virtualPath}' is not canonical.",
                nameof(virtualPath));
        }

        return normalized;
    }

    private static string[] MaterializeVirtualPaths(IEnumerable<string> paths)
    {
        var result = new List<string>();
        foreach (var path in paths)
        {
            if (result.Count == MaximumLayeredEntries)
            {
                throw new InvalidDataException("The layered virtual path list exceeds its bounded limit.");
            }

            result.Add(NormalizeVirtualPath(path));
        }

        return result.ToArray();
    }

    private static HashSet<ulong> MaterializeDistinctVirtualPathHashes(
        IEnumerable<string> paths)
    {
        var result = new HashSet<ulong>();
        foreach (var path in paths)
        {
            var normalized = NormalizeVirtualPath(path);
            if (string.Equals(normalized, DescriptorVirtualPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(ZaTrinityPathHasher.HashPath(normalized));
            if (result.Count > MaximumLayeredEntries)
            {
                throw new InvalidDataException(
                    "The additional layered virtual path list exceeds its bounded distinct-entry limit.");
            }
        }

        return result;
    }

    private static byte[] ReadBoundedFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        if (stream.Length > MaximumDescriptorBytes)
        {
            throw new InvalidDataException("The Trinity descriptor exceeds its bounded size.");
        }

        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private sealed record DescriptorModel(
        IReadOnlyList<ulong> FileHashes,
        IReadOnlyList<string> PackNames,
        IReadOnlyList<FileEntry> Files,
        IReadOnlyList<PackEntry> Packs);

    private sealed record FileEntry(ulong PackIndex, bool HasUnk1);

    private sealed record PackEntry(ulong FileSize, ulong FileCount);
}
