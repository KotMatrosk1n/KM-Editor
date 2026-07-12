// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using Google.FlatBuffers;
using KM.Formats.SV;
using KM.Formats.SV.Trinity;
using Xunit;

namespace KM.Formats.Tests.SV;

public sealed class SvTrinityArchiveTests
{
    [Fact]
    public void OpenReadsUncompressedFilesFromSyntheticTrinityArchive()
    {
        using var temp = TemporaryFolder.Create();
        var romFsRoot = Path.Combine(temp.Path, "romfs");
        Directory.CreateDirectory(Path.Combine(romFsRoot, "arc"));
        WriteSyntheticArchive(
            romFsRoot,
            [
                ("avalon/data/personal_array.bin", [0x01, 0x02, 0x03]),
                ("world/data/item/itemdata/itemdata_array.bin", [0x04, 0x05]),
            ]);

        using var archive = SvTrinityArchive.Open(temp.Path);

        Assert.True(archive.ContainsFile("romfs/avalon/data/personal_array.bin"));
        Assert.True(archive.TryReadFile("avalon\\data\\personal_array.bin", out var personal));
        Assert.Equal([0x01, 0x02, 0x03], personal);
        Assert.Equal([0x04, 0x05], archive.ReadFile("world/data/item/itemdata/itemdata_array.bin"));
        Assert.False(archive.TryReadFile("missing/file.bin", out var missing));
        Assert.Empty(missing);
    }

    [Fact]
    public void OpenCanReuseSerializableIndex()
    {
        using var temp = TemporaryFolder.Create();
        var romFsRoot = Path.Combine(temp.Path, "romfs");
        Directory.CreateDirectory(Path.Combine(romFsRoot, "arc"));
        WriteSyntheticArchive(
            romFsRoot,
            [
                ("avalon/data/personal_array.bin", [0x10, 0x20]),
                ("world/data/item/itemdata/itemdata_array.bin", [0x30]),
            ]);

        var index = SvTrinityArchive.BuildIndex(temp.Path);

        Assert.Equal(SvTrinityArchive.IndexSchemaVersion, index.SchemaVersion);
        Assert.Equal(2, index.Files.Count);
        Assert.Single(index.Packs);
        Assert.Same(index.Files[0].PackName, index.Files[1].PackName);
        using var archive = SvTrinityArchive.Open(temp.Path, index: index);
        using var secondArchive = SvTrinityArchive.Open(temp.Path, index: index);

        Assert.Same(archive.CompiledIndexIdentity, secondArchive.CompiledIndexIdentity);

        Assert.Equal([0x10, 0x20], archive.ReadFile("avalon/data/personal_array.bin"));
        Assert.Equal([0x30], archive.ReadFile("world/data/item/itemdata/itemdata_array.bin"));
    }

    [Fact]
    public void OpenRejectsTruncatedFileSystemHeader()
    {
        using var temp = TemporaryFolder.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "arc"));
        File.WriteAllBytes(Path.Combine(temp.Path, "arc", "data.trpfd"), CreateDescriptor("pack/test.trpak", 0, []));
        File.WriteAllBytes(Path.Combine(temp.Path, "arc", "data.trpfs"), [0x00, 0x01]);

        var exception = Assert.Throws<InvalidDataException>(() => SvTrinityArchive.Open(temp.Path));
        Assert.Contains("header is truncated", exception.Message, StringComparison.Ordinal);
    }

    private static void WriteSyntheticArchive(
        string romFsRoot,
        IReadOnlyList<(string VirtualPath, byte[] Bytes)> files)
    {
        const string packName = "pack/test.trpak";
        var packedArchive = CreatePackedArchive(files);
        var descriptor = CreateDescriptor(packName, packedArchive.Length, files.Select(file => file.VirtualPath).ToArray());
        var fileSystem = CreateFileSystem(packName, packOffset: 16);
        var trpfs = new byte[16 + packedArchive.Length + fileSystem.Length];
        BinaryPrimitives.WriteInt64LittleEndian(trpfs.AsSpan(8, sizeof(long)), 16 + packedArchive.Length);
        packedArchive.CopyTo(trpfs.AsSpan(16));
        fileSystem.CopyTo(trpfs.AsSpan(16 + packedArchive.Length));

        File.WriteAllBytes(Path.Combine(romFsRoot, "arc", "data.trpfd"), descriptor);
        File.WriteAllBytes(Path.Combine(romFsRoot, "arc", "data.trpfs"), trpfs);
    }

    private static byte[] CreateDescriptor(
        string packName,
        int packSize,
        IReadOnlyList<string> virtualPaths)
    {
        var builder = new FlatBufferBuilder(1024);
        var packNameOffset = builder.CreateString(packName);
        var packNames = FileDescriptor.CreatePackNamesVector(builder, [packNameOffset]);
        var fileHashes = FileDescriptor.CreateFileHashesVector(
            builder,
            virtualPaths.Select(SvTrinityPathHasher.HashPath).ToArray());
        var fileEntries = virtualPaths
            .Select(_ => FileDescriptorEntry.CreateFileDescriptorEntry(builder, pack_index: 0))
            .ToArray();
        var files = FileDescriptor.CreateFilesVector(builder, fileEntries);
        var packEntry = PackDescriptorEntry.CreatePackDescriptorEntry(
            builder,
            file_size: checked((ulong)packSize),
            file_count: checked((ulong)virtualPaths.Count));
        var packs = FileDescriptor.CreatePacksVector(builder, [packEntry]);
        var root = FileDescriptor.CreateFileDescriptor(builder, fileHashes, packNames, files, packs);
        FileDescriptor.FinishFileDescriptorBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateFileSystem(string packName, ulong packOffset)
    {
        var builder = new FlatBufferBuilder(128);
        var hashes = FileSystem.CreateFileHashesVector(builder, [SvTrinityPathHasher.HashPath(packName)]);
        var offsets = FileSystem.CreateFileOffsetsVector(builder, [packOffset]);
        var root = FileSystem.CreateFileSystem(builder, hashes, offsets);
        builder.Finish(root.Value);
        return builder.SizedByteArray();
    }

    private static byte[] CreatePackedArchive(IReadOnlyList<(string VirtualPath, byte[] Bytes)> files)
    {
        var builder = new FlatBufferBuilder(1024);
        var packedFiles = files
            .Select(file =>
            {
                var buffer = PackedFile.CreateFileBufferVector(builder, file.Bytes);
                return PackedFile.CreatePackedFile(
                    builder,
                    encryption_type: -1,
                    file_size: checked((ulong)file.Bytes.Length),
                    file_bufferOffset: buffer);
            })
            .ToArray();
        var fileVector = PackedArchive.CreateFilesVector(builder, packedFiles);
        var fileHashes = PackedArchive.CreateFileHashesVector(
            builder,
            files.Select(file => SvTrinityPathHasher.HashPath(file.VirtualPath)).ToArray());
        var root = PackedArchive.CreatePackedArchive(builder, fileHashes, fileVector);
        builder.Finish(root.Value);
        return builder.SizedByteArray();
    }

    private sealed class TemporaryFolder : IDisposable
    {
        private TemporaryFolder(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryFolder Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "km-sv-trinity-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryFolder(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
