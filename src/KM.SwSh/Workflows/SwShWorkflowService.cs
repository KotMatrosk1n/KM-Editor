// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.SwSh.Encounters;
using KM.SwSh.ExeFs;
using KM.SwSh.Flagwork;
using KM.SwSh.Items;
using KM.SwSh.Placement;
using KM.SwSh.Raids;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Shops;
using KM.SwSh.SpreadsheetImport;
using KM.SwSh.Text;
using KM.SwSh.Trainers;

namespace KM.SwSh.Workflows;

public sealed class SwShWorkflowService
{
    private readonly SwShItemsWorkflowService itemsWorkflowService;
    private readonly SwShEncountersWorkflowService encountersWorkflowService;
    private readonly SwShExeFsPatchWorkflowService exeFsPatchWorkflowService;
    private readonly SwShFlagworkSaveWorkflowService flagworkSaveWorkflowService;
    private readonly SwShPlacementWorkflowService placementWorkflowService;
    private readonly SwShRaidRewardsWorkflowService raidRewardsWorkflowService;
    private readonly SwShRoyalCandyWorkflowService royalCandyWorkflowService;
    private readonly SwShShopsWorkflowService shopsWorkflowService;
    private readonly SwShSpreadsheetImportWorkflowService spreadsheetImportWorkflowService;
    private readonly SwShTextWorkflowService textWorkflowService;
    private readonly SwShTrainersWorkflowService trainersWorkflowService;
    private readonly ProjectWorkspaceService projectWorkspaceService;

    public SwShWorkflowService(
        ProjectWorkspaceService? projectWorkspaceService = null,
        SwShItemsWorkflowService? itemsWorkflowService = null,
        SwShTextWorkflowService? textWorkflowService = null,
        SwShTrainersWorkflowService? trainersWorkflowService = null,
        SwShShopsWorkflowService? shopsWorkflowService = null,
        SwShEncountersWorkflowService? encountersWorkflowService = null,
        SwShRaidRewardsWorkflowService? raidRewardsWorkflowService = null,
        SwShPlacementWorkflowService? placementWorkflowService = null,
        SwShFlagworkSaveWorkflowService? flagworkSaveWorkflowService = null,
        SwShExeFsPatchWorkflowService? exeFsPatchWorkflowService = null,
        SwShRoyalCandyWorkflowService? royalCandyWorkflowService = null,
        SwShSpreadsheetImportWorkflowService? spreadsheetImportWorkflowService = null)
    {
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.itemsWorkflowService = itemsWorkflowService ?? new SwShItemsWorkflowService();
        this.encountersWorkflowService = encountersWorkflowService ?? new SwShEncountersWorkflowService();
        this.exeFsPatchWorkflowService = exeFsPatchWorkflowService ?? new SwShExeFsPatchWorkflowService();
        this.flagworkSaveWorkflowService = flagworkSaveWorkflowService ?? new SwShFlagworkSaveWorkflowService();
        this.placementWorkflowService = placementWorkflowService ?? new SwShPlacementWorkflowService();
        this.raidRewardsWorkflowService = raidRewardsWorkflowService ?? new SwShRaidRewardsWorkflowService();
        this.royalCandyWorkflowService = royalCandyWorkflowService ?? new SwShRoyalCandyWorkflowService();
        this.shopsWorkflowService = shopsWorkflowService ?? new SwShShopsWorkflowService();
        this.spreadsheetImportWorkflowService = spreadsheetImportWorkflowService ?? new SwShSpreadsheetImportWorkflowService();
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
                shopsWorkflowService.CreateSummary(project),
                encountersWorkflowService.CreateSummary(project),
                raidRewardsWorkflowService.CreateSummary(project),
                placementWorkflowService.CreateSummary(project),
                flagworkSaveWorkflowService.CreateSummary(project),
                exeFsPatchWorkflowService.CreateSummary(project),
                royalCandyWorkflowService.CreateSummary(project),
                spreadsheetImportWorkflowService.CreateSummary(project),
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

    public SwShShopsWorkflow LoadShops(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return shopsWorkflowService.Load(project);
    }

    public SwShEncountersWorkflow LoadEncounters(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return encountersWorkflowService.Load(project);
    }

    public SwShRaidRewardsWorkflow LoadRaidRewards(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return raidRewardsWorkflowService.Load(project);
    }

    public SwShPlacementWorkflow LoadPlacement(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return placementWorkflowService.Load(project);
    }

    public SwShFlagworkSaveWorkflow LoadFlagworkSave(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return flagworkSaveWorkflowService.Load(project);
    }

    public SwShExeFsPatchWorkflow LoadExeFsPatches(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return exeFsPatchWorkflowService.Load(project);
    }

    public SwShRoyalCandyWorkflow LoadRoyalCandy(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return royalCandyWorkflowService.Load(project);
    }

    public SwShSpreadsheetImportWorkflow LoadSpreadsheetImport(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return spreadsheetImportWorkflowService.Load(project);
    }
}
