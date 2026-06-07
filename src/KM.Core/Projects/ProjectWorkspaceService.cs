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

        var health = Validate(paths);
        var fileGraph = RefreshFileGraph(paths, health);

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

        return RefreshFileGraph(paths, Validate(paths));
    }

    private ProjectFileGraph RefreshFileGraph(ProjectPaths paths, ProjectHealth health)
    {
        if (!CanBuildBaseFileGraph(health))
        {
            return EmptyFileGraph;
        }

        // Only a validated output root can add LayeredFS entries; invalid output paths must not leak into the graph.
        var graphPaths = IsOutputRootValid(health)
            ? paths
            : paths with { OutputRootPath = null };

        return fileGraphBuilder.Build(graphPaths);
    }

    private static bool IsOutputRootValid(ProjectHealth health)
    {
        return health.Paths.Any(path =>
            path.Role == ProjectPathRole.OutputRoot
            && path.Status == ProjectPathStatus.Valid
            && !path.HasBlockingError);
    }

    private static bool CanBuildBaseFileGraph(ProjectHealth health)
    {
        return health.Paths.Any(IsValidBasePath(ProjectPathRole.BaseRomFs))
            && health.Paths.Any(IsValidBasePath(ProjectPathRole.BaseExeFs));
    }

    private static Func<ProjectPathValidation, bool> IsValidBasePath(ProjectPathRole role)
    {
        return path =>
            path.Role == role
            && path.Status == ProjectPathStatus.Valid
            && !path.HasBlockingError;
    }
}
