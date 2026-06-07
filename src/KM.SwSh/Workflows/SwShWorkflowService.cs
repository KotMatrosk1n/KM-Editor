// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.SwSh.Items;

namespace KM.SwSh.Workflows;

public sealed class SwShWorkflowService
{
    private readonly SwShItemsWorkflowService itemsWorkflowService;
    private readonly ProjectWorkspaceService projectWorkspaceService;

    public SwShWorkflowService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShItemsWorkflowService? itemsWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.itemsWorkflowService = itemsWorkflowService ?? new SwShItemsWorkflowService();
    }

    public SwShWorkflowList List(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return new SwShWorkflowList([itemsWorkflowService.CreateSummary(project)]);
    }

    public SwShItemsWorkflow LoadItems(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return itemsWorkflowService.Load(project);
    }
}
