// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;

namespace KM.Core.Projects;

public sealed class ProjectWorkspaceService
{
    private static readonly ProjectFileGraph EmptyFileGraph = new(Array.Empty<ProjectFileGraphEntry>());

    private readonly ProjectFileGraphBuilder fileGraphBuilder;
    private readonly ProjectValidator validator;

    public ProjectWorkspaceService(ProjectValidator? validator = null, ProjectFileGraphBuilder? fileGraphBuilder = null)
    {
        this.fileGraphBuilder = fileGraphBuilder ?? new ProjectFileGraphBuilder();
        this.validator = validator ?? new ProjectValidator(this.fileGraphBuilder);
    }

    public OpenedProject Open(ProjectPaths paths, DateTimeOffset? openedAt = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var (health, fileGraph) = ValidateAndBuildFileGraph(paths);

        return new OpenedProject(
            ProjectId.New(),
            paths,
            health,
            fileGraph,
            openedAt ?? DateTimeOffset.UtcNow);
    }

    public ProjectHealth Validate(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return validator.Validate(paths);
    }

    public ProjectFileGraph RefreshFileGraph(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var (_, fileGraph) = ValidateAndBuildFileGraph(paths);

        return fileGraph;
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
