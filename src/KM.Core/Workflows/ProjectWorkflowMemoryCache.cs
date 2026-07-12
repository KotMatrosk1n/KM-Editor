// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;

namespace KM.Core.Workflows;

public sealed class ProjectWorkflowMemoryCache<TWorkflow>
    where TWorkflow : class
{
    private readonly object syncRoot = new();
    private ProjectPaths? paths;
    private TWorkflow? workflow;

    public bool TryGet(ProjectPaths activePaths, out TWorkflow? cachedWorkflow)
    {
        ArgumentNullException.ThrowIfNull(activePaths);

        lock (syncRoot)
        {
            if (workflow is not null && Equals(paths, activePaths))
            {
                cachedWorkflow = workflow;
                return true;
            }

            cachedWorkflow = null;
            return false;
        }
    }

    public void Set(ProjectPaths activePaths, TWorkflow loadedWorkflow)
    {
        ArgumentNullException.ThrowIfNull(activePaths);
        ArgumentNullException.ThrowIfNull(loadedWorkflow);

        lock (syncRoot)
        {
            paths = activePaths;
            workflow = loadedWorkflow;
        }
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            paths = null;
            workflow = null;
        }
    }
}
