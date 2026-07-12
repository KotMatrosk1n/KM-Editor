// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.SwSh.ExeFs;
using KM.SwSh.Tests.Items;
using KM.SwSh.Tests.Performance;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Workflows;

public sealed class SwShParsedDataCacheTests
{
    [Fact]
    public void WorkflowServiceReusesParsedExeFsMainAcrossExeFsAndRoyalCandy()
    {
        using var temp = SwShPerformanceFixtureProject.Create();
        var cache = new SwShParsedDataCache();
        var workflowService = new SwShWorkflowService(parsedDataCache: cache);

        var exeFs = workflowService.LoadExeFsPatches(temp.Paths);
        var afterExeFs = cache.Snapshot();

        Assert.Single(exeFs.Patches);
        Assert.Equal(1, afterExeFs.EntryCount);
        Assert.Equal(SwShParsedDataCache.DefaultMaximumEntryCount, afterExeFs.MaximumEntryCount);
        Assert.Equal(0, afterExeFs.HitCount);
        Assert.Equal(1, afterExeFs.MissCount);
        Assert.Equal(0, afterExeFs.EvictionCount);

        var royalCandy = workflowService.LoadRoyalCandy(temp.Paths);
        var afterRoyalCandy = cache.Snapshot();

        Assert.True(royalCandy.Checks.Count > 0);
        Assert.Equal(1, afterRoyalCandy.EntryCount);
        Assert.Equal(1, afterRoyalCandy.HitCount);
        Assert.Equal(1, afterRoyalCandy.MissCount);
        Assert.Equal(0, afterRoyalCandy.EvictionCount);
    }

    [Fact]
    public void CacheInvalidatesParsedExeFsMainWhenSourceFileChanges()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "base-items");
        temp.WriteBaseExeFsFile("main", SwShPerformanceFixtureProject.CreateCompatibleNso());
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });
        var cache = new SwShParsedDataCache();
        var service = new SwShExeFsPatchWorkflowService(cache);

        var first = service.Load(project);
        var afterFirst = cache.Snapshot();

        Assert.Single(first.Patches);
        Assert.Equal(1, afterFirst.EntryCount);
        Assert.Equal(0, afterFirst.HitCount);
        Assert.Equal(1, afterFirst.MissCount);

        temp.WriteBaseExeFsFile("main", "not-an-nso");
        var second = service.Load(project);
        var afterSecond = cache.Snapshot();

        Assert.Empty(second.Patches);
        Assert.Contains(
            second.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Domain == "workflow.exefsPatches"
                && diagnostic.File == SwShExeFsPatchWorkflowService.ExeFsMainPath);
        Assert.Equal(1, afterSecond.EntryCount);
        Assert.Equal(0, afterSecond.HitCount);
        Assert.Equal(2, afterSecond.MissCount);
    }

    [Fact]
    public void CacheEvictsLeastRecentlyUsedEntriesAtConfiguredLimit()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("cache/first.txt", "first");
        temp.WriteBaseRomFsFile("cache/second.txt", "second");
        temp.WriteBaseRomFsFile("cache/third.txt", "third");
        var firstPath = Path.Combine(temp.BaseRomFsPath, "cache", "first.txt");
        var secondPath = Path.Combine(temp.BaseRomFsPath, "cache", "second.txt");
        var thirdPath = Path.Combine(temp.BaseRomFsPath, "cache", "third.txt");
        var cache = new SwShParsedDataCache(maximumEntryCount: 2);

        Assert.False(cache.GetOrAdd<string>(firstPath, File.ReadAllText).WasCacheHit);
        Assert.False(cache.GetOrAdd<string>(secondPath, File.ReadAllText).WasCacheHit);
        Assert.True(cache.GetOrAdd<string>(firstPath, File.ReadAllText).WasCacheHit);
        Assert.False(cache.GetOrAdd<string>(thirdPath, File.ReadAllText).WasCacheHit);
        Assert.True(cache.GetOrAdd<string>(firstPath, File.ReadAllText).WasCacheHit);
        Assert.False(cache.GetOrAdd<string>(secondPath, File.ReadAllText).WasCacheHit);

        var snapshot = cache.Snapshot();
        Assert.Equal(2, snapshot.EntryCount);
        Assert.Equal(2, snapshot.MaximumEntryCount);
        Assert.Equal(2, snapshot.HitCount);
        Assert.Equal(4, snapshot.MissCount);
        Assert.Equal(2, snapshot.EvictionCount);
    }

    [Fact]
    public void WorkflowCacheBoundaryClearsParsedExeFsEntries()
    {
        using var temp = SwShPerformanceFixtureProject.Create();
        var cache = new SwShParsedDataCache();
        var workflowService = new SwShWorkflowService(parsedDataCache: cache);

        Assert.Single(workflowService.LoadExeFsPatches(temp.Paths).Patches);
        Assert.Equal(1, cache.Snapshot().EntryCount);

        workflowService.ClearMemoryCaches();

        var afterClear = cache.Snapshot();
        Assert.Equal(0, afterClear.EntryCount);
        Assert.Equal(0, afterClear.HitCount);
        Assert.Equal(0, afterClear.MissCount);
        Assert.Equal(0, afterClear.EvictionCount);

        Assert.Single(workflowService.LoadExeFsPatches(temp.Paths).Patches);
        var afterReload = cache.Snapshot();
        Assert.Equal(1, afterReload.EntryCount);
        Assert.Equal(0, afterReload.HitCount);
        Assert.Equal(1, afterReload.MissCount);
    }

    [Fact]
    public void OutputMutationBoundaryKeepsReusableParsedEntriesWarm()
    {
        using var temp = SwShPerformanceFixtureProject.Create();
        var cache = new SwShParsedDataCache();
        var workflowService = new SwShWorkflowService(parsedDataCache: cache);

        Assert.Single(workflowService.LoadExeFsPatches(temp.Paths).Patches);

        workflowService.ClearMemoryCaches(clearReusableDataCaches: false);
        Assert.Single(workflowService.LoadExeFsPatches(temp.Paths).Patches);

        var afterReload = cache.Snapshot();
        Assert.Equal(1, afterReload.EntryCount);
        Assert.Equal(1, afterReload.HitCount);
        Assert.Equal(1, afterReload.MissCount);
        Assert.Equal(0, afterReload.EvictionCount);
    }

    [Fact]
    public void CacheRejectsNonPositiveEntryLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SwShParsedDataCache(maximumEntryCount: 0));
    }
}
