// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.SwSh.Encounters;
using KM.SwSh.ExeFs;
using KM.SwSh.Flagwork;
using KM.SwSh.Gifts;
using KM.SwSh.Items;
using KM.SwSh.Moves;
using KM.SwSh.Placement;
using KM.SwSh.Pokemon;
using KM.SwSh.Raids;
using KM.SwSh.Rentals;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Shops;
using KM.SwSh.SpreadsheetImport;
using KM.SwSh.StaticEncounters;
using KM.SwSh.Text;
using KM.SwSh.Trainers;
using KM.SwSh.Trades;

namespace KM.SwSh.Workflows;

public sealed class SwShWorkflowService
{
    private readonly SwShItemsWorkflowService itemsWorkflowService;
    private readonly SwShPokemonWorkflowService pokemonWorkflowService;
    private readonly SwShMovesWorkflowService movesWorkflowService;
    private readonly SwShEncountersWorkflowService encountersWorkflowService;
    private readonly SwShExeFsPatchWorkflowService exeFsPatchWorkflowService;
    private readonly SwShFlagworkSaveWorkflowService flagworkSaveWorkflowService;
    private readonly SwShGiftPokemonWorkflowService giftPokemonWorkflowService;
    private readonly SwShTradePokemonWorkflowService tradePokemonWorkflowService;
    private readonly SwShStaticEncountersWorkflowService staticEncountersWorkflowService;
    private readonly SwShRentalPokemonWorkflowService rentalPokemonWorkflowService;
    private readonly SwShPlacementWorkflowService placementWorkflowService;
    private readonly SwShRaidBattlesWorkflowService raidBattlesWorkflowService;
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
        SwShPokemonWorkflowService? pokemonWorkflowService = null,
        SwShMovesWorkflowService? movesWorkflowService = null,
        SwShTextWorkflowService? textWorkflowService = null,
        SwShTrainersWorkflowService? trainersWorkflowService = null,
        SwShShopsWorkflowService? shopsWorkflowService = null,
        SwShEncountersWorkflowService? encountersWorkflowService = null,
        SwShRaidBattlesWorkflowService? raidBattlesWorkflowService = null,
        SwShRaidRewardsWorkflowService? raidRewardsWorkflowService = null,
        SwShPlacementWorkflowService? placementWorkflowService = null,
        SwShFlagworkSaveWorkflowService? flagworkSaveWorkflowService = null,
        SwShGiftPokemonWorkflowService? giftPokemonWorkflowService = null,
        SwShTradePokemonWorkflowService? tradePokemonWorkflowService = null,
        SwShStaticEncountersWorkflowService? staticEncountersWorkflowService = null,
        SwShRentalPokemonWorkflowService? rentalPokemonWorkflowService = null,
        SwShExeFsPatchWorkflowService? exeFsPatchWorkflowService = null,
        SwShRoyalCandyWorkflowService? royalCandyWorkflowService = null,
        SwShSpreadsheetImportWorkflowService? spreadsheetImportWorkflowService = null,
        SwShParsedDataCache? parsedDataCache = null)
    {
        var sharedParsedDataCache = parsedDataCache ?? new SwShParsedDataCache();
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.itemsWorkflowService = itemsWorkflowService ?? new SwShItemsWorkflowService();
        this.pokemonWorkflowService = pokemonWorkflowService ?? new SwShPokemonWorkflowService();
        this.movesWorkflowService = movesWorkflowService ?? new SwShMovesWorkflowService();
        this.encountersWorkflowService = encountersWorkflowService ?? new SwShEncountersWorkflowService();
        this.exeFsPatchWorkflowService = exeFsPatchWorkflowService ?? new SwShExeFsPatchWorkflowService(sharedParsedDataCache);
        this.flagworkSaveWorkflowService = flagworkSaveWorkflowService ?? new SwShFlagworkSaveWorkflowService();
        this.giftPokemonWorkflowService = giftPokemonWorkflowService ?? new SwShGiftPokemonWorkflowService();
        this.tradePokemonWorkflowService = tradePokemonWorkflowService ?? new SwShTradePokemonWorkflowService();
        this.staticEncountersWorkflowService = staticEncountersWorkflowService ?? new SwShStaticEncountersWorkflowService();
        this.rentalPokemonWorkflowService = rentalPokemonWorkflowService ?? new SwShRentalPokemonWorkflowService();
        this.placementWorkflowService = placementWorkflowService ?? new SwShPlacementWorkflowService();
        this.raidBattlesWorkflowService = raidBattlesWorkflowService ?? new SwShRaidBattlesWorkflowService();
        this.raidRewardsWorkflowService = raidRewardsWorkflowService ?? new SwShRaidRewardsWorkflowService();
        this.royalCandyWorkflowService = royalCandyWorkflowService ?? new SwShRoyalCandyWorkflowService(this.exeFsPatchWorkflowService);
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
                pokemonWorkflowService.CreateSummary(project),
                movesWorkflowService.CreateSummary(project),
                textWorkflowService.CreateSummary(project),
                trainersWorkflowService.CreateSummary(project),
                giftPokemonWorkflowService.CreateSummary(project),
                tradePokemonWorkflowService.CreateSummary(project),
                staticEncountersWorkflowService.CreateSummary(project),
                rentalPokemonWorkflowService.CreateSummary(project),
                shopsWorkflowService.CreateSummary(project),
                encountersWorkflowService.CreateSummary(project),
                raidBattlesWorkflowService.CreateSummary(project),
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

    public SwShPokemonWorkflow LoadPokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return pokemonWorkflowService.Load(project);
    }

    public SwShMovesWorkflow LoadMoves(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return movesWorkflowService.Load(project);
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

    public SwShGiftPokemonWorkflow LoadGiftPokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return giftPokemonWorkflowService.Load(project);
    }

    public SwShStaticEncountersWorkflow LoadStaticEncounters(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return staticEncountersWorkflowService.Load(project);
    }

    public SwShTradePokemonWorkflow LoadTradePokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return tradePokemonWorkflowService.Load(project);
    }

    public SwShRentalPokemonWorkflow LoadRentalPokemon(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return rentalPokemonWorkflowService.Load(project);
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

    public SwShRaidBattlesWorkflow LoadRaidBattles(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return raidBattlesWorkflowService.Load(project);
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
