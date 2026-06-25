// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Formats.ZA;
using KM.Formats.ZA.Trinity;
using Xunit;

namespace KM.Formats.Tests.ZA;

public sealed class ZaTrinityDescriptorPatcherTests
{
    [Fact]
    public void CreateLayeredDescriptorRemovesHashesForLooseRomFsFiles()
    {
        using var temp = TemporaryFolder.Create();
        var baseRomFs = Path.Combine(temp.Path, "base", "romfs");
        var outputRoot = Path.Combine(temp.Path, "output");
        var layeredItem = Path.Combine(outputRoot, "romfs", "world", "exl", "item_data", "item_data", "item_data.bin");
        Directory.CreateDirectory(Path.Combine(baseRomFs, "arc"));
        Directory.CreateDirectory(Path.GetDirectoryName(layeredItem)!);
        File.WriteAllBytes(layeredItem, [0x01]);
        File.WriteAllBytes(
            Path.Combine(baseRomFs, "arc", "data.trpfd"),
            CreateDescriptor(
                [
                    "world/exl/item_data/item_data/item_data.bin",
                    "avalon/data/personal_array.bin",
                ]));

        var patched = ZaTrinityDescriptorPatcher.CreateLayeredDescriptor(Path.Combine(temp.Path, "base"), outputRoot);
        var descriptor = FileDescriptor.GetRootAsFileDescriptor(new ByteBuffer(patched));

        Assert.Equal(1, descriptor.FileHashesLength);
        Assert.Equal(1, descriptor.FilesLength);
        Assert.Equal(ZaTrinityPathHasher.HashPath("avalon/data/personal_array.bin"), descriptor.FileHashes(0));
        Assert.Equal("pack/test.trpak", descriptor.PackNames(0));
        Assert.Equal((ulong)2, descriptor.Packs(0)!.Value.FileCount);
    }

    [Fact]
    public void CreateLayeredDescriptorRemovesAllLooseLayeredPathsButKeepsDescriptorEntry()
    {
        using var temp = TemporaryFolder.Create();
        var baseRomFs = Path.Combine(temp.Path, "base", "romfs");
        var outputRomFs = Path.Combine(temp.Path, "output", "romfs");
        Directory.CreateDirectory(Path.Combine(baseRomFs, "arc"));
        File.WriteAllBytes(
            Path.Combine(baseRomFs, "arc", "data.trpfd"),
            CreateDescriptor(
                [
                    "world/exl/item_data/item_data/item_data.bin",
                    "avalon/data/personal_array.bin",
                    "world/ik/data/field/pokemon/encount_data/encount_data/encount_data_array.bin",
                    ZaTrinityDescriptorPatcher.DescriptorVirtualPath,
                    "bin/packed-only.bin",
                ]));

        WriteLayeredFile(outputRomFs, "world/exl/item_data/item_data/item_data.bin");
        WriteLayeredFile(outputRomFs, "avalon/data/personal_array.bin");
        WriteLayeredFile(outputRomFs, "world/ik/data/field/pokemon/encount_data/encount_data/encount_data_array.bin");
        WriteLayeredFile(outputRomFs, ZaTrinityDescriptorPatcher.DescriptorVirtualPath);

        var patched = ZaTrinityDescriptorPatcher.CreateLayeredDescriptor(Path.Combine(temp.Path, "base"), Path.Combine(temp.Path, "output"));
        var hashes = ReadHashes(patched);

        Assert.Equal(2, hashes.Count);
        Assert.DoesNotContain(ZaTrinityPathHasher.HashPath("world/exl/item_data/item_data/item_data.bin"), hashes);
        Assert.DoesNotContain(ZaTrinityPathHasher.HashPath("avalon/data/personal_array.bin"), hashes);
        Assert.DoesNotContain(ZaTrinityPathHasher.HashPath("world/ik/data/field/pokemon/encount_data/encount_data/encount_data_array.bin"), hashes);
        Assert.Contains(ZaTrinityPathHasher.HashPath(ZaTrinityDescriptorPatcher.DescriptorVirtualPath), hashes);
        Assert.Contains(ZaTrinityPathHasher.HashPath("bin/packed-only.bin"), hashes);
    }

    [Fact]
    public void RemoveFileHashesRejectsDescriptorWithMismatchedHashAndFileVectors()
    {
        var descriptor = CreateDescriptorWithMismatchedVectors();
        var exception = Assert.Throws<InvalidDataException>(
            () => ZaTrinityDescriptorPatcher.RemoveFileHashes(descriptor, new HashSet<ulong>()));

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
            virtualPaths.Select(ZaTrinityPathHasher.HashPath).ToArray());
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

    private static void WriteLayeredFile(string romFsRoot, string virtualPath)
    {
        var path = Path.Combine(romFsRoot, virtualPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x01]);
    }

    private static HashSet<ulong> ReadHashes(byte[] descriptorBytes)
    {
        var descriptor = FileDescriptor.GetRootAsFileDescriptor(new ByteBuffer(descriptorBytes));
        return Enumerable
            .Range(0, descriptor.FileHashesLength)
            .Select(descriptor.FileHashes)
            .ToHashSet();
    }

    private static byte[] CreateDescriptorWithMismatchedVectors()
    {
        var builder = new FlatBufferBuilder(1024);
        var packName = builder.CreateString("pack/test.trpak");
        var packNames = FileDescriptor.CreatePackNamesVector(builder, [packName]);
        var fileHashes = FileDescriptor.CreateFileHashesVector(
            builder,
            [
                ZaTrinityPathHasher.HashPath("world/exl/item_data/item_data/item_data.bin"),
                ZaTrinityPathHasher.HashPath("avalon/data/personal_array.bin"),
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
                "km-za-descriptor-tests",
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
