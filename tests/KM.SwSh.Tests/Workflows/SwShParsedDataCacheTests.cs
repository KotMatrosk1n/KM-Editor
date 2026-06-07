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
        Assert.Equal(0, afterExeFs.HitCount);
        Assert.Equal(1, afterExeFs.MissCount);

        var royalCandy = workflowService.LoadRoyalCandy(temp.Paths);
        var afterRoyalCandy = cache.Snapshot();

        Assert.True(royalCandy.Checks.Count > 0);
        Assert.Equal(1, afterRoyalCandy.EntryCount);
        Assert.Equal(1, afterRoyalCandy.HitCount);
        Assert.Equal(1, afterRoyalCandy.MissCount);
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
}
