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
    public void WarmupOrdersCoreEditorPayloadsBeforeTextPayloads()
    {
        var firstTextIndex = IndexOf(
            SvCacheManager.WarmupVirtualPaths,
            SvDataPaths.ItemNames(SvGameTextLanguage.English));

        Assert.True(firstTextIndex > 0);
        Assert.True(IndexOf(SvCacheManager.WarmupVirtualPaths, SvDataPaths.PersonalArray) < firstTextIndex);
        Assert.True(IndexOf(SvCacheManager.WarmupVirtualPaths, SvDataPaths.MoveDataArray) < firstTextIndex);
        Assert.True(IndexOf(SvCacheManager.WarmupVirtualPaths, SvDataPaths.ItemDataArray) < firstTextIndex);
        Assert.True(IndexOf(SvCacheManager.WarmupVirtualPaths, SvDataPaths.TrainerDataArray) < firstTextIndex);
        Assert.True(IndexOf(SvCacheManager.WarmupVirtualPaths, SvDataPaths.WildEncounterArray) < firstTextIndex);
        Assert.True(IndexOf(SvCacheManager.WarmupVirtualPaths, SvDataPaths.RummagingItemDataTableArray) < firstTextIndex);
    }

    [Fact]
    public void SettingsClampToSupportedCacheSizeRange()
    {
        using var temp = TemporaryFolder.Create();
        var cache = new SvCacheManager(temp.CacheRootPath);

        Assert.Equal(512L * 1024 * 1024, cache.GetSettings().MaxCacheSizeBytes);

        var low = cache.UpdateSettings(SvCacheMode.Balanced, 64L * 1024 * 1024);

        Assert.Equal(128L * 1024 * 1024, low.MaxCacheSizeBytes);

        var high = cache.UpdateSettings(SvCacheMode.Balanced, 5L * 1024 * 1024 * 1024);

        Assert.Equal(2L * 1024 * 1024 * 1024, high.MaxCacheSizeBytes);
    }

    [Fact]
    public void BalancedWarmupWritesMetadataAndClearRemovesActiveProjectCache()
    {
        using var temp = TemporaryFolder.Create();
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [(SvCacheManager.WarmupVirtualPaths[0], [0x10, 0x20])]);
        var paths = temp.CreatePaths(ProjectGame.Scarlet);
        var cache = new SvCacheManager(temp.CacheRootPath);

        cache.UpdateSettings(SvCacheMode.Balanced, 2L * 1024 * 1024 * 1024, paths);
        var warmupPaths = cache.GetWarmupVirtualPaths(paths);
        var warmup = cache.WarmupStep(paths, stepIndex: 0);

        Assert.Equal(SvCacheMode.Balanced, warmup.Settings.Mode);
        Assert.Equal(warmupPaths.Count, warmup.WarmupTotal);
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
            [(SvCacheManager.WarmupVirtualPaths[0], [0x30, 0x31])]);
        var paths = temp.CreatePaths(ProjectGame.Scarlet);
        var cache = new SvCacheManager(temp.CacheRootPath);

        cache.UpdateSettings(SvCacheMode.Balanced, 2L * 1024 * 1024 * 1024, paths);
        var warmup = cache.WarmupStep(paths, stepIndex: 0);

        Assert.Equal(1, warmup.WarmupCompleted);
        Assert.True(warmup.CacheSizeBytes > 0);

        cache.UpdateSettings(SvCacheMode.Performance, 2L * 1024 * 1024 * 1024, paths);
        var status = cache.GetStatus(paths);

        Assert.Equal(SvCacheMode.Performance, status.Settings.Mode);
        Assert.Equal(0, status.WarmupCompleted);
        Assert.Equal(0, status.CacheSizeBytes);
    }

    [Fact]
    public void OutputRootChangesDoNotInvalidateBaseProjectCache()
    {
        using var temp = TemporaryFolder.Create();
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [(SvCacheManager.WarmupVirtualPaths[0], [0x40])]);
        var paths = temp.CreatePaths(ProjectGame.Violet);
        var cache = new SvCacheManager(temp.CacheRootPath);

        cache.UpdateSettings(SvCacheMode.Balanced, 2L * 1024 * 1024 * 1024, paths);
        var warmup = cache.WarmupStep(paths, stepIndex: 0);

        Assert.Equal(1, warmup.WarmupCompleted);
        Assert.Equal(1, CountProjectCacheDirectories(temp));

        File.WriteAllText(Path.Combine(temp.OutputRootPath, "romfs_override.bin"), "changed");
        var changedStatus = cache.GetStatus(paths);

        Assert.Equal(1, changedStatus.WarmupCompleted);
        Assert.True(changedStatus.CacheSizeBytes > 0);
        Assert.Equal(1, CountProjectCacheDirectories(temp));
        Assert.Single(Directory.EnumerateFiles(temp.CacheRootPath, "index.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void BaseCacheReadsDoNotScanTheOutputRoot()
    {
        using var temp = TemporaryFolder.Create();
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [(SvCacheManager.WarmupVirtualPaths[0], [0x41])]);
        var paths = temp.CreatePaths(ProjectGame.Scarlet) with
        {
            OutputRootPath = "invalid\0output-root",
        };
        var cache = new SvCacheManager(temp.CacheRootPath);
        cache.UpdateSettings(SvCacheMode.Balanced, 512L * 1024 * 1024, paths);

        var warmup = cache.WarmupStep(paths, stepIndex: 0);
        var status = cache.GetStatus(paths);
        var bytes = cache.ReadBaseTrinityFile(paths, SvCacheManager.WarmupVirtualPaths[0]);

        Assert.Equal(1, warmup.WarmupCompleted);
        Assert.Equal(1, status.WarmupCompleted);
        Assert.Equal([0x41], bytes);
        Assert.True(cache.ContainsBaseTrinityFile(paths, SvCacheManager.WarmupVirtualPaths[0]));
    }

    [Fact]
    public void SourceManifestCleansObsoleteCacheWithoutReadingItsIndex()
    {
        using var temp = TemporaryFolder.Create();
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [(SvCacheManager.WarmupVirtualPaths[0], [0x42])]);
        var paths = temp.CreatePaths(ProjectGame.Violet);
        var cache = new SvCacheManager(temp.CacheRootPath);
        cache.UpdateSettings(SvCacheMode.Balanced, 2L * 1024 * 1024 * 1024, paths);
        Assert.Equal(1, cache.WarmupStep(paths, stepIndex: 0).WarmupCompleted);

        var oldProjectDirectory = Directory.GetDirectories(Path.Combine(temp.CacheRootPath, "projects")).Single();
        Assert.True(File.Exists(Path.Combine(oldProjectDirectory, "source.json")));
        File.WriteAllText(Path.Combine(oldProjectDirectory, "index.json"), "{ invalid large index }");
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [(SvCacheManager.WarmupVirtualPaths[0], [0x42, 0x43])]);

        var refreshed = new SvCacheManager(temp.CacheRootPath).WarmupStep(paths, stepIndex: 0);

        Assert.Equal(1, refreshed.WarmupCompleted);
        Assert.False(Directory.Exists(oldProjectDirectory));
        Assert.Single(Directory.GetDirectories(Path.Combine(temp.CacheRootPath, "projects")));
    }

    [Fact]
    public void StatusUsesSmallWarmupManifestWithoutReadingTheFullIndex()
    {
        using var temp = TemporaryFolder.Create();
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [(SvCacheManager.WarmupVirtualPaths[0], [0x44])]);
        var paths = temp.CreatePaths(ProjectGame.Scarlet);
        var cache = new SvCacheManager(temp.CacheRootPath);
        cache.UpdateSettings(SvCacheMode.Balanced, 512L * 1024 * 1024, paths);
        Assert.Equal(1, cache.WarmupStep(paths, stepIndex: 0).WarmupCompleted);

        var indexPath = Directory.EnumerateFiles(temp.CacheRootPath, "index.json", SearchOption.AllDirectories).Single();
        Assert.Single(Directory.EnumerateFiles(temp.CacheRootPath, "warmup-paths.json", SearchOption.AllDirectories));
        File.WriteAllText(indexPath, "{ invalid index that status must not deserialize }");

        var status = new SvCacheManager(temp.CacheRootPath).GetStatus(paths);

        Assert.Equal(1, status.WarmupCompleted);
        Assert.Equal(1, status.WarmupTotal);
    }

    [Fact]
    public void ChangingOnlyCompressionRuntimeSupersedesTheSameBaseDumpCache()
    {
        using var temp = TemporaryFolder.Create();
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [(SvCacheManager.WarmupVirtualPaths[0], [0x45])]);
        var firstSupport = Directory.CreateDirectory(Path.Combine(temp.RootPath, "support-a")).FullName;
        var secondSupport = Directory.CreateDirectory(Path.Combine(temp.RootPath, "support-b")).FullName;
        File.WriteAllText(Path.Combine(firstSupport, SvCompressionRuntime.RequiredFileName), "first");
        File.WriteAllText(Path.Combine(secondSupport, SvCompressionRuntime.RequiredFileName), "second");
        var firstPaths = temp.CreatePaths(ProjectGame.Scarlet) with
        {
            ScarletVioletSupportFolderPath = firstSupport,
        };
        var secondPaths = firstPaths with
        {
            ScarletVioletSupportFolderPath = secondSupport,
        };
        var cache = new SvCacheManager(temp.CacheRootPath);
        cache.UpdateSettings(SvCacheMode.Balanced, 512L * 1024 * 1024, firstPaths);
        cache.WarmupStep(firstPaths, stepIndex: 0);
        var oldProjectDirectory = Directory.GetDirectories(Path.Combine(temp.CacheRootPath, "projects")).Single();

        new SvCacheManager(temp.CacheRootPath).WarmupStep(secondPaths, stepIndex: 0);

        Assert.False(Directory.Exists(oldProjectDirectory));
        Assert.Single(Directory.GetDirectories(Path.Combine(temp.CacheRootPath, "projects")));
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
        var warmupPaths = cache.GetWarmupVirtualPaths(paths);
        var warmup = cache.WarmupStep(paths, stepIndex: 0);
        var bytes = cache.ReadBaseTrinityFile(paths, SvCacheManager.WarmupVirtualPaths[0]);

        Assert.Equal(SvCacheMode.Performance, warmup.Settings.Mode);
        Assert.Equal(1, warmup.WarmupCompleted);
        Assert.Equal(
            (int)Math.Round(100.0 / warmupPaths.Count, MidpointRounding.AwayFromZero),
            warmup.ProgressPercent);
        Assert.Equal([0x7A, 0x7B, 0x7C], bytes);
    }

    [Fact]
    public void WarmupDiscoversTextMessagePayloadsForEverySupportedLanguage()
    {
        using var temp = TemporaryFolder.Create();
        var entries = SvGameTextLanguage.SupportedMessageLanguages
            .Select((language, index) => (
                PackName: $"arc/messagedat{language}scriptcommon_{index:0000}.trpak",
                VirtualPath: $"message/dat/{language}/script/common_{index:0000}.dat",
                Bytes: new[] { checked((byte)(index + 1)) }))
            .ToArray();
        WriteSyntheticArchiveEntries(temp.BaseRomFsPath, entries);
        var paths = temp.CreatePaths(ProjectGame.Scarlet);
        var cache = new SvCacheManager(temp.CacheRootPath);

        var warmupPaths = cache.GetWarmupVirtualPaths(paths);
        var status = cache.GetStatus(paths);

        foreach (var entry in entries)
        {
            Assert.Contains(entry.VirtualPath, warmupPaths);
        }

        Assert.Equal(entries.Length, warmupPaths.Count);
        Assert.Equal(entries.Length, status.WarmupTotal);
    }

    [Fact]
    public void PerformanceWarmupCachesDiscoveredTextPayloads()
    {
        using var temp = TemporaryFolder.Create();
        const string textPath = "message/dat/English/script/common_0025.dat";
        WriteSyntheticArchiveEntries(
            temp.BaseRomFsPath,
            [("arc/messagedatEnglishscriptcommon_0025.trpak", textPath, new byte[] { 0x54, 0x58, 0x54 })]);
        var paths = temp.CreatePaths(ProjectGame.Scarlet);
        var cache = new SvCacheManager(temp.CacheRootPath);

        cache.UpdateSettings(SvCacheMode.Performance, 2L * 1024 * 1024 * 1024, paths);
        var warmupPaths = cache.GetWarmupVirtualPaths(paths);
        var stepIndex = warmupPaths
            .Select((path, index) => (path, index))
            .First(entry => string.Equals(entry.path, textPath, StringComparison.OrdinalIgnoreCase))
            .index;
        var warmup = cache.WarmupStep(paths, stepIndex);
        var bytes = cache.ReadBaseTrinityFile(paths, textPath);

        Assert.Equal(SvCacheMode.Performance, warmup.Settings.Mode);
        Assert.Equal([0x54, 0x58, 0x54], bytes);
    }

    [Fact]
    public void WarmupStepResumesAtNextIncompletePayload()
    {
        using var temp = TemporaryFolder.Create();
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [
                (SvCacheManager.WarmupVirtualPaths[0], [0x01]),
                (SvCacheManager.WarmupVirtualPaths[1], [0x02]),
            ]);
        var paths = temp.CreatePaths(ProjectGame.Scarlet);
        var cache = new SvCacheManager(temp.CacheRootPath);

        cache.UpdateSettings(SvCacheMode.Performance, 2L * 1024 * 1024 * 1024, paths);
        var first = cache.WarmupStep(paths, stepIndex: 0);
        var resumed = cache.WarmupStep(paths, stepIndex: 0);

        Assert.Equal(1, first.WarmupCompleted);
        Assert.Equal(2, resumed.WarmupCompleted);
        Assert.Equal(2, resumed.WarmupTotal);
    }

    [Fact]
    public void PerformanceWarmupBatchesTextPayloads()
    {
        using var temp = TemporaryFolder.Create();
        var entries = Enumerable.Range(0, 12)
            .Select(index => (
                PackName: $"arc/messagedatEnglishscriptcommon_{index:0000}.trpak",
                VirtualPath: $"message/dat/English/script/common_{index:0000}.dat",
                Bytes: new[] { checked((byte)(0x20 + index)) }))
            .ToArray();
        WriteSyntheticArchiveEntries(temp.BaseRomFsPath, entries);
        var paths = temp.CreatePaths(ProjectGame.Violet);
        var cache = new SvCacheManager(temp.CacheRootPath);

        cache.UpdateSettings(SvCacheMode.Performance, 2L * 1024 * 1024 * 1024, paths);
        var warmup = cache.WarmupStep(paths, stepIndex: 0);

        Assert.InRange(warmup.WarmupCompleted, 1, 8);
        Assert.Equal(entries.Length, warmup.WarmupTotal);
        foreach (var entry in entries)
        {
            Assert.Equal(entry.Bytes, cache.ReadBaseTrinityFile(paths, entry.VirtualPath));
        }
    }

    [Fact]
    public void MinimalModeRetainsOneIndexUntilMemoryCacheIsCleared()
    {
        using var temp = TemporaryFolder.Create();
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [(SvCacheManager.WarmupVirtualPaths[0], [0x61])]);
        var paths = temp.CreatePaths(ProjectGame.Scarlet);
        var cache = new SvCacheManager(temp.CacheRootPath);
        cache.UpdateSettings(SvCacheMode.Minimal, 512L * 1024 * 1024, paths);

        Assert.True(cache.ContainsBaseTrinityFile(paths, SvCacheManager.WarmupVirtualPaths[0]));
        Assert.Contains(SvCacheManager.WarmupVirtualPaths[0], cache.GetWarmupVirtualPaths(paths));
        Assert.True(cache.HasRetainedIndex);
        Assert.Empty(Directory.EnumerateFiles(temp.CacheRootPath, "index.json", SearchOption.AllDirectories));

        cache.ClearMemoryCache();

        Assert.False(cache.HasRetainedIndex);
    }

    [Fact]
    public void RepeatedIndexLookupsReuseBoundedDerivedCaches()
    {
        using var temp = TemporaryFolder.Create();
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [(SvCacheManager.WarmupVirtualPaths[0], [0x64])]);
        var paths = temp.CreatePaths(ProjectGame.Scarlet);
        var cache = new SvCacheManager(temp.CacheRootPath);
        cache.UpdateSettings(SvCacheMode.Minimal, 512L * 1024 * 1024, paths);

        Assert.True(cache.ContainsBaseTrinityFile(paths, SvCacheManager.WarmupVirtualPaths[0]));
        Assert.False(cache.ContainsBaseTrinityFile(paths, "missing/file.bin"));
        var firstPackNames = cache.ListBaseTrinityPackNames(paths);

        Assert.Same(firstPackNames, cache.ListBaseTrinityPackNames(paths));

        cache.ClearMemoryCache();
        var reloadedPackNames = cache.ListBaseTrinityPackNames(paths);

        Assert.NotSame(firstPackNames, reloadedPackNames);
        Assert.Single(reloadedPackNames);
    }

    [Fact]
    public void OutputMutationBoundaryKeepsReusableIndexWarm()
    {
        using var temp = TemporaryFolder.Create();
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [(SvCacheManager.WarmupVirtualPaths[0], [0x63])]);
        var paths = temp.CreatePaths(ProjectGame.Scarlet);
        var cache = new SvCacheManager(temp.CacheRootPath);
        var workflows = new SvWorkflowService(cacheManager: cache);

        Assert.True(cache.ContainsBaseTrinityFile(paths, SvCacheManager.WarmupVirtualPaths[0]));

        workflows.ClearMemoryCaches(clearReusableDataCaches: false);
        Assert.True(cache.HasRetainedIndex);

        workflows.ClearMemoryCaches();
        Assert.False(cache.HasRetainedIndex);
    }

    [Fact]
    public void PruneEnforcesLimitInsideActiveProject()
    {
        using var temp = TemporaryFolder.Create();
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [(SvCacheManager.WarmupVirtualPaths[0], [0x62])]);
        var paths = temp.CreatePaths(ProjectGame.Violet);
        var cache = new SvCacheManager(temp.CacheRootPath);
        cache.UpdateSettings(SvCacheMode.Balanced, 2L * 1024 * 1024 * 1024, paths);
        cache.WarmupStep(paths, stepIndex: 0);

        var projectDirectory = Directory.GetDirectories(Path.Combine(temp.CacheRootPath, "projects")).Single();
        var payloadDirectory = Directory.CreateDirectory(Path.Combine(projectDirectory, "payloads")).FullName;
        var payloadPath = Path.Combine(payloadDirectory, "oldest.bin");
        using (var payload = File.Create(payloadPath))
        {
            payload.SetLength(140L * 1024 * 1024);
        }
        File.WriteAllText(Path.ChangeExtension(payloadPath, ".json"), "{}");

        var settings = cache.UpdateSettings(SvCacheMode.Balanced, 128L * 1024 * 1024, paths);

        Assert.False(File.Exists(payloadPath));
        Assert.True(cache.GetStatus(paths).CacheSizeBytes <= settings.MaxCacheSizeBytes);
    }

    [Fact]
    public void GetStatusPrunesOversizedPersistentCacheButDoesNotCountTransientWrites()
    {
        using var temp = TemporaryFolder.Create();
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [(SvCacheManager.WarmupVirtualPaths[0], [0x65])]);
        var paths = temp.CreatePaths(ProjectGame.Scarlet);
        var cache = new SvCacheManager(temp.CacheRootPath);
        var settings = cache.UpdateSettings(SvCacheMode.Balanced, 128L * 1024 * 1024, paths);
        cache.WarmupStep(paths, stepIndex: 0);

        var projectDirectory = Directory.GetDirectories(Path.Combine(temp.CacheRootPath, "projects")).Single();
        var oversizedPath = Path.Combine(projectDirectory, "startup-resident.bin");
        using (var oversized = File.Create(oversizedPath))
        {
            oversized.SetLength(140L * 1024 * 1024);
        }

        var tempDirectory = Directory.CreateDirectory(Path.Combine(temp.CacheRootPath, "tmp")).FullName;
        var transientPath = Path.Combine(tempDirectory, "active-write.tmp");
        using (var transient = File.Create(transientPath))
        {
            transient.SetLength(140L * 1024 * 1024);
        }

        var status = new SvCacheManager(temp.CacheRootPath).GetStatus(paths);

        Assert.False(File.Exists(oversizedPath));
        Assert.True(File.Exists(transientPath));
        Assert.True(status.CacheSizeBytes <= settings.MaxCacheSizeBytes);
    }

    [Fact]
    public void CapacityLimitedWarmupBecomesReadyAndMissingPayloadsStillLoadOnDemand()
    {
        using var temp = TemporaryFolder.Create();
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [(SvCacheManager.WarmupVirtualPaths[0], [0x66])]);
        var paths = temp.CreatePaths(ProjectGame.Violet);
        var cache = new SvCacheManager(temp.CacheRootPath);
        cache.UpdateSettings(SvCacheMode.Performance, 128L * 1024 * 1024, paths);
        cache.GetWarmupVirtualPaths(paths);

        var projectDirectory = Directory.GetDirectories(Path.Combine(temp.CacheRootPath, "projects")).Single();
        using (var resident = File.Create(Path.Combine(projectDirectory, "resident.bin")))
        {
            resident.SetLength(140L * 1024 * 1024);
        }

        cache = new SvCacheManager(temp.CacheRootPath);
        var limited = cache.WarmupStep(paths, stepIndex: 0);

        Assert.Equal(limited.WarmupTotal, limited.WarmupCompleted);
        Assert.Equal(100, limited.ProgressPercent);
        Assert.Equal("Cache ready", limited.Phase);
        Assert.Single(Directory.EnumerateFiles(temp.CacheRootPath, "warmup-state.json", SearchOption.AllDirectories));

        var restarted = new SvCacheManager(temp.CacheRootPath);
        var resumed = restarted.WarmupStep(paths, stepIndex: 0);

        Assert.Equal(resumed.WarmupTotal, resumed.WarmupCompleted);
        Assert.Empty(Directory.EnumerateFiles(temp.CacheRootPath, "index.json", SearchOption.AllDirectories));
        Assert.Equal([0x66], restarted.ReadBaseTrinityFile(paths, SvCacheManager.WarmupVirtualPaths[0]));

        restarted.UpdateSettings(SvCacheMode.Performance, 512L * 1024 * 1024, paths);
        var expanded = restarted.WarmupStep(paths, stepIndex: 0);

        Assert.Equal(expanded.WarmupTotal, expanded.WarmupCompleted);
        Assert.Empty(Directory.EnumerateFiles(temp.CacheRootPath, "warmup-state.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void StartupRemovesOrphanedAtomicWriteFiles()
    {
        using var temp = TemporaryFolder.Create();
        var tempDirectory = Directory.CreateDirectory(Path.Combine(temp.CacheRootPath, "tmp")).FullName;
        var orphanPath = Path.Combine(tempDirectory, "orphan.tmp");
        File.WriteAllText(orphanPath, "incomplete");
        File.SetLastWriteTimeUtc(orphanPath, DateTime.UtcNow - TimeSpan.FromHours(1));

        _ = new SvCacheManager(temp.CacheRootPath).GetSettings();

        Assert.False(File.Exists(orphanPath));
    }

    [Fact]
    public void PruneDoesNotBlockEditingWhenAStaleCacheFileIsTemporarilyLocked()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = TemporaryFolder.Create();
        WriteSyntheticArchive(
            temp.BaseRomFsPath,
            [(SvCacheManager.WarmupVirtualPaths[0], [0x63])]);
        var paths = temp.CreatePaths(ProjectGame.Violet);
        var cache = new SvCacheManager(temp.CacheRootPath);
        cache.UpdateSettings(SvCacheMode.Balanced, 2L * 1024 * 1024 * 1024, paths);
        cache.WarmupStep(paths, stepIndex: 0);

        var projectDirectory = Directory.GetDirectories(Path.Combine(temp.CacheRootPath, "projects")).Single();
        var payloadDirectory = Directory.CreateDirectory(Path.Combine(projectDirectory, "payloads")).FullName;
        var payloadPath = Path.Combine(payloadDirectory, "locked.bin");
        using (var payload = new FileStream(payloadPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            payload.SetLength(140L * 1024 * 1024);
            File.WriteAllText(Path.ChangeExtension(payloadPath, ".json"), "{}");

            var exception = Record.Exception(
                () => cache.UpdateSettings(SvCacheMode.Balanced, 128L * 1024 * 1024, paths));

            Assert.Null(exception);
            Assert.True(File.Exists(payloadPath));
        }

        var settings = cache.UpdateSettings(SvCacheMode.Balanced, 128L * 1024 * 1024, paths);

        Assert.False(File.Exists(payloadPath));
        Assert.True(cache.GetStatus(paths).CacheSizeBytes <= settings.MaxCacheSizeBytes);
    }

    private static int IndexOf(IReadOnlyList<string> paths, string virtualPath)
    {
        for (var index = 0; index < paths.Count; index++)
        {
            if (string.Equals(paths[index], virtualPath, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static int CountProjectCacheDirectories(TemporaryFolder temp)
    {
        var projectsPath = Path.Combine(temp.CacheRootPath, "projects");
        return Directory.Exists(projectsPath)
            ? Directory.GetDirectories(projectsPath).Length
            : 0;
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

    private static void WriteSyntheticArchiveEntries(
        string romFsRoot,
        IReadOnlyList<(string PackName, string VirtualPath, byte[] Bytes)> entries)
    {
        Directory.CreateDirectory(Path.Combine(romFsRoot, "arc"));
        var packedArchives = entries
            .Select(entry => CreatePackedArchive([(entry.VirtualPath, entry.Bytes)]))
            .ToArray();
        var descriptor = CreateDescriptorEntries(
            entries
                .Select((entry, index) => (entry.PackName, PackSize: packedArchives[index].Length, entry.VirtualPath))
                .ToArray());
        var packOffsets = new ulong[packedArchives.Length];
        var currentOffset = 16;
        for (var index = 0; index < packedArchives.Length; index++)
        {
            packOffsets[index] = checked((ulong)currentOffset);
            currentOffset += packedArchives[index].Length;
        }

        var fileSystem = CreateFileSystem(entries.Select(entry => entry.PackName).ToArray(), packOffsets);
        var trpfs = new byte[currentOffset + fileSystem.Length];
        BinaryPrimitives.WriteInt64LittleEndian(trpfs.AsSpan(8, sizeof(long)), currentOffset);
        for (var index = 0; index < packedArchives.Length; index++)
        {
            packedArchives[index].CopyTo(trpfs.AsSpan(checked((int)packOffsets[index])));
        }

        fileSystem.CopyTo(trpfs.AsSpan(currentOffset));

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

    private static byte[] CreateDescriptorEntries(
        IReadOnlyList<(string PackName, int PackSize, string VirtualPath)> entries)
    {
        var builder = new FlatBufferBuilder(1024);
        var packNameOffsets = entries
            .Select(entry => builder.CreateString(entry.PackName))
            .ToArray();
        var packNames = FileDescriptor.CreatePackNamesVector(builder, packNameOffsets);
        var fileHashes = FileDescriptor.CreateFileHashesVector(
            builder,
            entries.Select(entry => SvTrinityPathHasher.HashPath(entry.VirtualPath)).ToArray());
        var fileEntries = entries
            .Select((_, index) => FileDescriptorEntry.CreateFileDescriptorEntry(builder, pack_index: checked((ulong)index)))
            .ToArray();
        var files = FileDescriptor.CreateFilesVector(builder, fileEntries);
        var packEntries = entries
            .Select(entry => PackDescriptorEntry.CreatePackDescriptorEntry(
                builder,
                file_size: checked((ulong)entry.PackSize),
                file_count: 1))
            .ToArray();
        var packs = FileDescriptor.CreatePacksVector(builder, packEntries);
        var root = FileDescriptor.CreateFileDescriptor(builder, fileHashes, packNames, files, packs);
        FileDescriptor.FinishFileDescriptorBuffer(builder, root);
        return builder.SizedByteArray();
    }

    private static byte[] CreateFileSystem(string packName, ulong packOffset)
    {
        return CreateFileSystem([packName], [packOffset]);
    }

    private static byte[] CreateFileSystem(IReadOnlyList<string> packNames, IReadOnlyList<ulong> packOffsets)
    {
        var builder = new FlatBufferBuilder(128);
        var hashes = TrinityFileSystem.CreateFileHashesVector(
            builder,
            packNames.Select(SvTrinityPathHasher.HashPath).ToArray());
        var offsets = TrinityFileSystem.CreateFileOffsetsVector(builder, packOffsets.ToArray());
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
