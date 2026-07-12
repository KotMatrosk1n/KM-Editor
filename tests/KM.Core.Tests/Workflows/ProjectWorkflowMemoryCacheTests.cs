// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Core.Workflows;
using Xunit;

namespace KM.Core.Tests.Workflows;

public sealed class ProjectWorkflowMemoryCacheTests
{
    [Fact]
    public void CacheReusesOnlyTheMatchingProjectPathsAndCanBeCleared()
    {
        var cache = new ProjectWorkflowMemoryCache<object>();
        var swordPaths = CreatePaths(ProjectGame.Sword, "sword-output");
        var shieldPaths = CreatePaths(ProjectGame.Shield, "shield-output");
        var workflow = new object();

        cache.Set(swordPaths, workflow);

        Assert.True(cache.TryGet(swordPaths, out var cachedWorkflow));
        Assert.Same(workflow, cachedWorkflow);
        Assert.False(cache.TryGet(shieldPaths, out _));

        cache.Clear();

        Assert.False(cache.TryGet(swordPaths, out _));
    }

    private static ProjectPaths CreatePaths(ProjectGame game, string outputRoot)
    {
        return new ProjectPaths(
            BaseRomFsPath: "base-romfs",
            BaseExeFsPath: "base-exefs",
            OutputRootPath: outputRoot,
            SaveFilePath: null,
            ScarletVioletSupportFolderPath: null,
            SelectedGame: game);
    }
}
