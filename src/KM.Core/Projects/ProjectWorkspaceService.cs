// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;

namespace KM.Core.Projects;

public sealed class ProjectWorkspaceService
{
    private static readonly ProjectFileGraph EmptyFileGraph = new(Array.Empty<ProjectFileGraphEntry>());

    private readonly ProjectFileGraphBuilder fileGraphBuilder;
    private readonly ProjectValidator validator;
    private readonly object memoryCacheSyncRoot = new();
    private ProjectPaths? cachedPaths;
    private OpenedProject? cachedProject;

    public ProjectWorkspaceService(ProjectValidator? validator = null, ProjectFileGraphBuilder? fileGraphBuilder = null)
    {
        this.fileGraphBuilder = fileGraphBuilder ?? new ProjectFileGraphBuilder();
        this.validator = validator ?? new ProjectValidator(this.fileGraphBuilder);
    }

    public OpenedProject Open(ProjectPaths paths, DateTimeOffset? openedAt = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (openedAt is null)
        {
            lock (memoryCacheSyncRoot)
            {
                if (cachedProject is not null && Equals(cachedPaths, paths))
                {
                    return cachedProject;
                }
            }
        }

        var (health, fileGraph) = ValidateAndBuildFileGraph(paths);
        var project = new OpenedProject(
            ProjectId.New(),
            paths,
            health,
            fileGraph,
            openedAt ?? DateTimeOffset.UtcNow);
        if (openedAt is null)
        {
            lock (memoryCacheSyncRoot)
            {
                cachedPaths = paths;
                cachedProject = project;
            }
        }

        return project;
    }

    public ProjectHealth Validate(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        ClearMemoryCache();
        return validator.Validate(paths);
    }

    public ProjectFileGraph RefreshFileGraph(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        ClearMemoryCache();
        var (_, fileGraph) = ValidateAndBuildFileGraph(paths);

        return fileGraph;
    }

    public void ClearMemoryCache()
    {
        lock (memoryCacheSyncRoot)
        {
            cachedPaths = null;
            cachedProject = null;
        }
    }

    private (ProjectHealth Health, ProjectFileGraph FileGraph) ValidateAndBuildFileGraph(ProjectPaths paths)
    {
        ProjectFileGraph? fileGraph = null;
        var health = validator.Validate(paths, graphPaths =>
        {
            fileGraph = fileGraphBuilder.Build(graphPaths);
            return fileGraph.ToSummary();
        });

        return (health, fileGraph ?? EmptyFileGraph);
    }
}
