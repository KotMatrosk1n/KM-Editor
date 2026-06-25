// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using Google.FlatBuffers;
using KM.Core.Projects;
using KM.Formats.ZA;
using KM.Formats.ZA.Trinity;
using KM.ZA.Data;
using KM.ZA.Workflows;
using TrinityFileSystem = KM.Formats.ZA.Trinity.FileSystem;
using Xunit;

namespace KM.Integration.Tests.ZA;

public sealed class ZaCacheManagerTests
{
    [Fact]
    public void WarmupIncludesPokemonLegendsZAEditorPayloads()
    {
        Assert.Contains(ZaDataPaths.PersonalArray, ZaCacheManager.WarmupVirtualPaths);
        Assert.Contains(ZaDataPaths.TrainerDataArray, ZaCacheManager.WarmupVirtualPaths);
        Assert.Contains(ZaDataPaths.PokemonSpawnerDataArray, ZaCacheManager.WarmupVirtualPaths);
        Assert.Contains(ZaDataPaths.ShopItemArray, ZaCacheManager.WarmupVirtualPaths);
        Assert.Contains(ZaDataPaths.DressUpDataArray, ZaCacheManager.WarmupVirtualPaths);
    }

    [Fact]
    public void PerformanceWarmupCachesTheDecompressedPayload()
    {
        using var temp = TemporaryFolder.Create();
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [(ZaCacheManager.WarmupVirtualPaths[0], [0x7A, 0x0A])]);
        var paths = temp.CreatePaths();
        var cache = new ZaCacheManager(temp.CacheRootPath);

        cache.UpdateSettings(ZaCacheMode.Performance, 2L * 1024 * 1024 * 1024, paths);
        var warmup = cache.WarmupStep(paths, stepIndex: 0);
        var bytes = cache.ReadBaseTrinityFile(paths, ZaCacheManager.WarmupVirtualPaths[0]);

        Assert.Equal(ZaCacheMode.Performance, warmup.Settings.Mode);
        Assert.Equal(1, warmup.WarmupCompleted);
        Assert.Equal([0x7A, 0x0A], bytes);
    }

    [Fact]
    public void ClearRemovesActiveProjectCache()
    {
        using var temp = TemporaryFolder.Create();
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [(ZaCacheManager.WarmupVirtualPaths[0], [0x20])]);
        var paths = temp.CreatePaths();
        var cache = new ZaCacheManager(temp.CacheRootPath);

        cache.UpdateSettings(ZaCacheMode.Balanced, 2L * 1024 * 1024 * 1024, paths);
        var warmup = cache.WarmupStep(paths, stepIndex: 0);

        Assert.Equal(1, warmup.WarmupCompleted);
        Assert.True(warmup.CacheSizeBytes > 0);

        var cleared = cache.Clear(paths);

        Assert.False(cleared.IsActiveProjectPreserved);
        Assert.Equal(0, cleared.WarmupCompleted);
        Assert.Equal(0, cleared.CacheSizeBytes);
    }

    [Fact]
    public void ChangingCacheModeInvalidatesExistingProjectCache()
    {
        using var temp = TemporaryFolder.Create();
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [(ZaCacheManager.WarmupVirtualPaths[0], [0x30])]);
        var paths = temp.CreatePaths();
        var cache = new ZaCacheManager(temp.CacheRootPath);

        cache.UpdateSettings(ZaCacheMode.Balanced, 2L * 1024 * 1024 * 1024, paths);
        var warmup = cache.WarmupStep(paths, stepIndex: 0);

        Assert.Equal(1, warmup.WarmupCompleted);
        Assert.True(warmup.CacheSizeBytes > 0);

        cache.UpdateSettings(ZaCacheMode.Performance, 2L * 1024 * 1024 * 1024, paths);
        var status = cache.GetStatus(paths);

        Assert.Equal(ZaCacheMode.Performance, status.Settings.Mode);
        Assert.Equal(0, status.WarmupCompleted);
        Assert.Equal(0, status.CacheSizeBytes);
    }

    [Fact]
    public void OutputRootChangesInvalidateCompletedProjectCache()
    {
        using var temp = TemporaryFolder.Create();
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [(ZaCacheManager.WarmupVirtualPaths[0], [0x40])]);
        var paths = temp.CreatePaths();
        var cache = new ZaCacheManager(temp.CacheRootPath);

        cache.UpdateSettings(ZaCacheMode.Balanced, 2L * 1024 * 1024 * 1024, paths);
        var warmup = cache.WarmupStep(paths, stepIndex: 0);

        Assert.Equal(1, warmup.WarmupCompleted);

        File.WriteAllText(Path.Combine(temp.OutputRootPath, "romfs_override.bin"), "changed");
        var changedStatus = cache.GetStatus(paths);

        Assert.Equal(0, changedStatus.WarmupCompleted);
        Assert.True(changedStatus.WarmupTotal > 0);
    }

    private static void WriteSyntheticArchive(
        string romFsRoot,
        IReadOnlyList<(string VirtualPath, byte[] Bytes)> files)
    {
        Directory.CreateDirectory(Path.Combine(romFsRoot, "arc"));
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
            virtualPaths.Select(ZaTrinityPathHasher.HashPath).ToArray());
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
        var hashes = TrinityFileSystem.CreateFileHashesVector(builder, [ZaTrinityPathHasher.HashPath(packName)]);
        var offsets = TrinityFileSystem.CreateFileOffsetsVector(builder, [packOffset]);
        var root = TrinityFileSystem.CreateFileSystem(builder, hashes, offsets);
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
            files.Select(file => ZaTrinityPathHasher.HashPath(file.VirtualPath)).ToArray());
        var root = PackedArchive.CreatePackedArchive(builder, fileHashes, fileVector);
        builder.Finish(root.Value);
        return builder.SizedByteArray();
    }

    private sealed class TemporaryFolder : IDisposable
    {
        private TemporaryFolder(string path)
        {
            RootPath = path;
            BaseRomFsPath = Directory.CreateDirectory(Path.Combine(path, "romfs")).FullName;
            BaseExeFsPath = Directory.CreateDirectory(Path.Combine(path, "exefs")).FullName;
            OutputRootPath = Directory.CreateDirectory(Path.Combine(path, "output")).FullName;
            CacheRootPath = Path.Combine(path, "cache");
        }

        public string BaseExeFsPath { get; }

        public string BaseRomFsPath { get; }

        public string CacheRootPath { get; }

        public string OutputRootPath { get; }

        public string RootPath { get; }

        public static TemporaryFolder Create()
        {
            var path = Path.Combine(Path.GetTempPath(), "km-za-cache-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryFolder(path);
        }

        public ProjectPaths CreatePaths()
        {
            return new ProjectPaths(
                BaseRomFsPath,
                BaseExeFsPath,
                OutputRootPath,
                SaveFilePath: null,
                SelectedGame: ProjectGame.ZA);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
