// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Formats.SV;
using KM.Formats.SV.Trinity;
using Xunit;

namespace KM.Formats.Tests.SV;

public sealed class SvTrinityDescriptorPatcherTests
{
    [Fact]
    public void CreateLayeredDescriptorRemovesHashesForLooseRomFsFiles()
    {
        using var temp = TemporaryFolder.Create();
        var baseRomFs = Path.Combine(temp.Path, "base", "romfs");
        var outputRoot = Path.Combine(temp.Path, "output");
        var layeredItem = Path.Combine(outputRoot, "romfs", "world", "data", "item", "itemdata", "itemdata_array.bin");
        Directory.CreateDirectory(Path.Combine(baseRomFs, "arc"));
        Directory.CreateDirectory(Path.GetDirectoryName(layeredItem)!);
        File.WriteAllBytes(layeredItem, [0x01]);
        File.WriteAllBytes(
            Path.Combine(baseRomFs, "arc", "data.trpfd"),
            CreateDescriptor(
                [
                    "world/data/item/itemdata/itemdata_array.bin",
                    "avalon/data/personal_array.bin",
                ]));

        var patched = SvTrinityDescriptorPatcher.CreateLayeredDescriptor(Path.Combine(temp.Path, "base"), outputRoot);
        var descriptor = FileDescriptor.GetRootAsFileDescriptor(new ByteBuffer(patched));

        Assert.Equal(1, descriptor.FileHashesLength);
        Assert.Equal(1, descriptor.FilesLength);
        Assert.Equal(SvTrinityPathHasher.HashPath("avalon/data/personal_array.bin"), descriptor.FileHashes(0));
        Assert.Equal("pack/test.trpak", descriptor.PackNames(0));
        Assert.Equal((ulong)2, descriptor.Packs(0)!.Value.FileCount);
    }

    [Fact]
    public void RemoveFileHashesRejectsDescriptorWithMismatchedHashAndFileVectors()
    {
        var descriptor = CreateDescriptorWithMismatchedVectors();
        var exception = Assert.Throws<InvalidDataException>(
            () => SvTrinityDescriptorPatcher.RemoveFileHashes(descriptor, new HashSet<ulong>()));

        Assert.Contains("hashes", exception.Message, StringComparison.Ordinal);
        Assert.Contains("file entries", exception.Message, StringComparison.Ordinal);
    }

    private static byte[] CreateDescriptor(IReadOnlyList<string> virtualPaths)
    {
        var builder = new FlatBufferBuilder(1024);
        var packName = builder.CreateString("pack/test.trpak");
        var packNames = FileDescriptor.CreatePackNamesVector(builder, [packName]);
        var fileHashes = FileDescriptor.CreateFileHashesVector(
            builder,
            virtualPaths.Select(SvTrinityPathHasher.HashPath).ToArray());
        var fileEntries = virtualPaths
            .Select(_ => FileDescriptorEntry.CreateFileDescriptorEntry(builder, pack_index: 0))
            .ToArray();
        var files = FileDescriptor.CreateFilesVector(builder, fileEntries);
        var pack = PackDescriptorEntry.CreatePackDescriptorEntry(
            builder,
            file_size: 123,
            file_count: checked((ulong)virtualPaths.Count));
        var packs = FileDescriptor.CreatePacksVector(builder, [pack]);
        var root = FileDescriptor.CreateFileDescriptor(builder, fileHashes, packNames, files, packs);
        FileDescriptor.FinishFileDescriptorBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateDescriptorWithMismatchedVectors()
    {
        var builder = new FlatBufferBuilder(1024);
        var packName = builder.CreateString("pack/test.trpak");
        var packNames = FileDescriptor.CreatePackNamesVector(builder, [packName]);
        var fileHashes = FileDescriptor.CreateFileHashesVector(
            builder,
            [
                SvTrinityPathHasher.HashPath("world/data/item/itemdata/itemdata_array.bin"),
                SvTrinityPathHasher.HashPath("avalon/data/personal_array.bin"),
            ]);
        var fileEntry = FileDescriptorEntry.CreateFileDescriptorEntry(builder, pack_index: 0);
        var files = FileDescriptor.CreateFilesVector(builder, [fileEntry]);
        var pack = PackDescriptorEntry.CreatePackDescriptorEntry(builder, file_size: 123, file_count: 2);
        var packs = FileDescriptor.CreatePacksVector(builder, [pack]);
        var root = FileDescriptor.CreateFileDescriptor(builder, fileHashes, packNames, files, packs);
        FileDescriptor.FinishFileDescriptorBuffer(builder, root);
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
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "km-sv-descriptor-tests",
                Guid.NewGuid().ToString("N"));
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
