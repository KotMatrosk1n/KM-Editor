// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;

namespace KM.Core.Workflows;

public sealed class ProjectWorkflowMemoryCache<TWorkflow>
    where TWorkflow : class
{
    private readonly object syncRoot = new();
    private ProjectPaths? paths;
    private TWorkflow? workflow;
    private string? fileSystemStamp;
    private readonly AsyncLocal<(ProjectPaths Paths, string? Stamp)?> observation = new();

    public bool TryGet(ProjectPaths activePaths, out TWorkflow? cachedWorkflow)
    {
        ArgumentNullException.ThrowIfNull(activePaths);

        lock (syncRoot)
        {
            var observedFileSystemStamp = ProjectFileSystemStamp.Capture(activePaths);
            observation.Value = (activePaths, observedFileSystemStamp);
            if (workflow is not null && Equals(paths, activePaths)
                && fileSystemStamp is not null && fileSystemStamp == observedFileSystemStamp)
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
            // Bind to the observation before loading, so a concurrent change expires this value.
            fileSystemStamp = observation.Value is { } observed && Equals(observed.Paths, activePaths)
                ? observed.Stamp : null;
        }
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            paths = null;
            workflow = null;
            fileSystemStamp = null;
            observation.Value = null;
        }
    }
}
