// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using Google.FlatBuffers;
using KM.Core.Projects;
using KM.Formats.SV;
using KM.Formats.SV.Trinity;
using KM.SV.Data;
using KM.SV.Workflows;
using TrinityFileSystem = KM.Formats.SV.Trinity.FileSystem;
using Xunit;

namespace KM.Integration.Tests.SV;

public sealed class SvCacheManagerTests
{
    [Fact]
    public void WarmupIncludesScarletVioletEditorPayloads()
    {
        Assert.Contains(SvDataPaths.FriendlyShopLineupDataArray, SvCacheManager.WarmupVirtualPaths);
        Assert.Contains(SvDataPaths.ShopWazaMachineDataArray, SvCacheManager.WarmupVirtualPaths);
        Assert.Contains(SvDataPaths.FixedSymbolTableArray, SvCacheManager.WarmupVirtualPaths);
        Assert.Contains(SvDataPaths.EventBattlePokemonArray, SvCacheManager.WarmupVirtualPaths);
        Assert.Contains(SvDataPaths.TeraRaidEnemyPaldea1, SvCacheManager.WarmupVirtualPaths);
        Assert.Contains(SvDataPaths.VisibleItemScenePaldeaScarlet, SvCacheManager.WarmupVirtualPaths);
    }

    [Fact]
    public void BalancedWarmupWritesMetadataAndClearPreservesActiveProject()
    {
        using var temp = TemporaryFolder.Create();
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [(SvCacheManager.WarmupVirtualPaths[0], [0x10, 0x20])]);
        var paths = temp.CreatePaths(ProjectGame.Scarlet);
        var cache = new SvCacheManager(temp.CacheRootPath);

        cache.UpdateSettings(SvCacheMode.Balanced, 2L * 1024 * 1024 * 1024, paths);
        var warmup = cache.WarmupStep(paths, stepIndex: 0);

        Assert.Equal(SvCacheMode.Balanced, warmup.Settings.Mode);
        Assert.Equal(SvCacheManager.WarmupVirtualPaths.Count, warmup.WarmupTotal);
        Assert.Equal(1, warmup.WarmupCompleted);
        Assert.True(warmup.CacheSizeBytes > 0);

        var cleared = cache.Clear(paths);

        Assert.True(cleared.IsActiveProjectPreserved);
        Assert.Equal(1, cleared.WarmupCompleted);
        Assert.True(cleared.CacheSizeBytes > 0);
    }

    [Fact]
    public void PerformanceWarmupCachesTheDecompressedPayload()
    {
        using var temp = TemporaryFolder.Create();
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [(SvCacheManager.WarmupVirtualPaths[0], [0x7A, 0x7B, 0x7C])]);
        var paths = temp.CreatePaths(ProjectGame.Violet);
        var cache = new SvCacheManager(temp.CacheRootPath);

        cache.UpdateSettings(SvCacheMode.Performance, 2L * 1024 * 1024 * 1024, paths);
        var warmup = cache.WarmupStep(paths, stepIndex: 0);
        var bytes = cache.ReadBaseTrinityFile(paths, SvCacheManager.WarmupVirtualPaths[0]);

        Assert.Equal(SvCacheMode.Performance, warmup.Settings.Mode);
        Assert.Equal(1, warmup.WarmupCompleted);
        Assert.Equal(
            (int)Math.Round(100.0 / SvCacheManager.WarmupVirtualPaths.Count, MidpointRounding.AwayFromZero),
            warmup.ProgressPercent);
        Assert.Equal([0x7A, 0x7B, 0x7C], bytes);
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
        var hashes = TrinityFileSystem.CreateFileHashesVector(builder, [SvTrinityPathHasher.HashPath(packName)]);
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
            files.Select(file => SvTrinityPathHasher.HashPath(file.VirtualPath)).ToArray());
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
            var path = Path.Combine(Path.GetTempPath(), "km-sv-cache-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryFolder(path);
        }

        public ProjectPaths CreatePaths(ProjectGame game)
        {
            return new ProjectPaths(
                BaseRomFsPath,
                BaseExeFsPath,
                OutputRootPath,
                SaveFilePath: null,
                ScarletVioletSupportFolderPath: null,
                SelectedGame: game);
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
