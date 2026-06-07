// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.SwSh.Items;
using KM.SwSh.Text;
using KM.SwSh.Trainers;

namespace KM.SwSh.Workflows;

public sealed class SwShWorkflowService
{
    private readonly SwShItemsWorkflowService itemsWorkflowService;
    private readonly SwShTextWorkflowService textWorkflowService;
    private readonly SwShTrainersWorkflowService trainersWorkflowService;
    private readonly ProjectWorkspaceService projectWorkspaceService;

    public SwShWorkflowService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShItemsWorkflowService? itemsWorkflowService = null,
        SwShTextWorkflowService? textWorkflowService = null,
        SwShTrainersWorkflowService? trainersWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.itemsWorkflowService = itemsWorkflowService ?? new SwShItemsWorkflowService();
        this.textWorkflowService = textWorkflowService ?? new SwShTextWorkflowService();
        this.trainersWorkflowService = trainersWorkflowService ?? new SwShTrainersWorkflowService();
    }

    public SwShWorkflowList List(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return new SwShWorkflowList(
            [
                itemsWorkflowService.CreateSummary(project),
                textWorkflowService.CreateSummary(project),
                trainersWorkflowService.CreateSummary(project),
            ]);
    }

    public SwShItemsWorkflow LoadItems(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return itemsWorkflowService.Load(project);
    }

    public SwShTextWorkflow LoadText(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return textWorkflowService.Load(project);
    }

    public SwShTrainersWorkflow LoadTrainers(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return trainersWorkflowService.Load(project);
    }
}
