// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;

namespace KM.ZA.Workflows;

public sealed class ZaWorkflowService
{
    private readonly ProjectWorkspaceService projectWorkspaceService;
    private readonly ZaCacheManager cacheManager;

    public ZaWorkflowService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        ZaCacheManager? cacheManager = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.cacheManager = cacheManager ?? new ZaCacheManager();
    }

    public ZaCacheStatus GetCacheStatus(ProjectPaths? paths = null)
    {
        return cacheManager.GetStatus(paths);
    }

    public ZaCacheStatus UpdateCacheSettings(
        ZaCacheMode mode,
        long maxCacheSizeBytes,
        ProjectPaths? activePaths = null)
    {
        cacheManager.UpdateSettings(mode, maxCacheSizeBytes, activePaths);
        return cacheManager.GetStatus(activePaths);
    }

    public ZaCacheStatus ClearCache(ProjectPaths? activePaths = null)
    {
        return cacheManager.Clear(activePaths);
    }

    public ZaCacheStatus WarmupCacheStep(ProjectPaths paths, int stepIndex)
    {
        return cacheManager.WarmupStep(paths, stepIndex);
    }

    public ZaWorkflowList List(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.SelectedGame is not ProjectGame.ZA)
        {
            return new ZaWorkflowList([]);
        }

        _ = projectWorkspaceService.Open(paths);
        return new ZaWorkflowList([]);
    }
}
