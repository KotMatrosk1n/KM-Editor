// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Formats.SV.Trinity;

namespace KM.Formats.SV;

public static class SvTrinityDescriptorPatcher
{
    public const string DescriptorVirtualPath = "arc/data.trpfd";

    private static readonly EnumerationOptions RecursiveEnumeration = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
        RecurseSubdirectories = true,
        ReturnSpecialDirectories = false,
    };

    public static byte[] CreateLayeredDescriptor(string baseRomFsRoot, string outputRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRomFsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        var descriptorPath = Path.Combine(ResolveRomFsRoot(baseRomFsRoot), "arc", "data.trpfd");
        if (!File.Exists(descriptorPath))
        {
            throw new FileNotFoundException("Scarlet/Violet Trinity descriptor was not found.", descriptorPath);
        }

        var layeredFileHashes = EnumerateLayeredVirtualPaths(outputRoot)
            .Select(SvTrinityPathHasher.HashPath)
            .ToHashSet();

        return RemoveFileHashes(File.ReadAllBytes(descriptorPath), layeredFileHashes);
    }

    public static byte[] RemoveFileHashes(byte[] descriptorBytes, IReadOnlySet<ulong> removedHashes)
    {
        ArgumentNullException.ThrowIfNull(descriptorBytes);
        ArgumentNullException.ThrowIfNull(removedHashes);

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

        for (var index = 0; index < descriptor.PackNamesLength; index++)
        {
            packNames.Add(
                descriptor.PackNames(index)
                    ?? throw new InvalidDataException($"Trinity descriptor pack name {index} is missing."));
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
        return builder.SizedByteArray();
    }

    private static IEnumerable<string> EnumerateLayeredVirtualPaths(string outputRoot)
    {
        var romFsRoot = Path.Combine(outputRoot, "romfs");
        if (!Directory.Exists(romFsRoot))
        {
            return [];
        }

        var root = Path.GetFullPath(romFsRoot);
        return Directory
            .EnumerateFiles(root, "*", RecursiveEnumeration)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !string.Equals(path, DescriptorVirtualPath, StringComparison.OrdinalIgnoreCase));
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

    private sealed record DescriptorModel(
        IReadOnlyList<ulong> FileHashes,
        IReadOnlyList<string> PackNames,
        IReadOnlyList<FileEntry> Files,
        IReadOnlyList<PackEntry> Packs);

    private sealed record FileEntry(ulong PackIndex, bool HasUnk1);

    private sealed record PackEntry(ulong FileSize, ulong FileCount);
}
