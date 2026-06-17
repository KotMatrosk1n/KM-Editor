// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.SwSh.Behavior;
using KM.SwSh.BagHook;
using KM.SwSh.CatchCap;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Encounters;
using KM.SwSh.ExeFs;
using KM.SwSh.FairyGymBoosts;
using KM.SwSh.FashionUnlock;
using KM.SwSh.Flagwork;
using KM.SwSh.Gifts;
using KM.SwSh.GymUniformRemoval;
using KM.SwSh.HyperTraining;
using KM.SwSh.Items;
using KM.SwSh.IvScreen;
using KM.SwSh.ModMerger;
using KM.SwSh.Moves;
using KM.SwSh.Placement;
using KM.SwSh.Pokemon;
using KM.SwSh.Raids;
using KM.SwSh.Rentals;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Shops;
using KM.SwSh.ShinyRate;
using KM.SwSh.SpreadsheetImport;
using KM.SwSh.StartingItems;
using KM.SwSh.StaticEncounters;
using KM.SwSh.Text;
using KM.SwSh.TypeChart;
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
    private readonly SwShBagHookWorkflowService bagHookWorkflowService;
    private readonly SwShCatchCapWorkflowService catchCapWorkflowService;
    private readonly SwShHyperTrainingWorkflowService hyperTrainingWorkflowService;
    private readonly SwShFairyGymBoostsWorkflowService fairyGymBoostsWorkflowService;
    private readonly SwShGymUniformRemovalWorkflowService gymUniformRemovalWorkflowService;
    private readonly SwShFashionUnlockWorkflowService fashionUnlockWorkflowService;
    private readonly SwShIvScreenWorkflowService ivScreenWorkflowService;
    private readonly SwShShinyRateWorkflowService shinyRateWorkflowService;
    private readonly SwShTypeChartWorkflowService typeChartWorkflowService;
    private readonly SwShFlagworkSaveWorkflowService flagworkSaveWorkflowService;
    private readonly SwShGiftPokemonWorkflowService giftPokemonWorkflowService;
    private readonly SwShTradePokemonWorkflowService tradePokemonWorkflowService;
    private readonly SwShStaticEncountersWorkflowService staticEncountersWorkflowService;
    private readonly SwShRentalPokemonWorkflowService rentalPokemonWorkflowService;
    private readonly SwShDynamaxAdventuresWorkflowService dynamaxAdventuresWorkflowService;
    private readonly SwShPlacementWorkflowService placementWorkflowService;
    private readonly SwShBehaviorWorkflowService behaviorWorkflowService;
    private readonly SwShRaidBattlesWorkflowService raidBattlesWorkflowService;
    private readonly SwShRaidRewardsWorkflowService raidRewardsWorkflowService;
    private readonly SwShRoyalCandyWorkflowService royalCandyWorkflowService;
    private readonly SwShStartingItemsWorkflowService startingItemsWorkflowService;
    private readonly SwShShopsWorkflowService shopsWorkflowService;
    private readonly SwShSpreadsheetImportWorkflowService spreadsheetImportWorkflowService;
    private readonly SwShModMergerWorkflowService modMergerWorkflowService;
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
        SwShBehaviorWorkflowService? behaviorWorkflowService = null,
        SwShFlagworkSaveWorkflowService? flagworkSaveWorkflowService = null,
        SwShGiftPokemonWorkflowService? giftPokemonWorkflowService = null,
        SwShTradePokemonWorkflowService? tradePokemonWorkflowService = null,
        SwShStaticEncountersWorkflowService? staticEncountersWorkflowService = null,
        SwShRentalPokemonWorkflowService? rentalPokemonWorkflowService = null,
        SwShDynamaxAdventuresWorkflowService? dynamaxAdventuresWorkflowService = null,
        SwShExeFsPatchWorkflowService? exeFsPatchWorkflowService = null,
        SwShBagHookWorkflowService? bagHookWorkflowService = null,
        SwShCatchCapWorkflowService? catchCapWorkflowService = null,
        SwShHyperTrainingWorkflowService? hyperTrainingWorkflowService = null,
        SwShFairyGymBoostsWorkflowService? fairyGymBoostsWorkflowService = null,
        SwShGymUniformRemovalWorkflowService? gymUniformRemovalWorkflowService = null,
        SwShFashionUnlockWorkflowService? fashionUnlockWorkflowService = null,
        SwShIvScreenWorkflowService? ivScreenWorkflowService = null,
        SwShShinyRateWorkflowService? shinyRateWorkflowService = null,
        SwShTypeChartWorkflowService? typeChartWorkflowService = null,
        SwShRoyalCandyWorkflowService? royalCandyWorkflowService = null,
        SwShStartingItemsWorkflowService? startingItemsWorkflowService = null,
        SwShSpreadsheetImportWorkflowService? spreadsheetImportWorkflowService = null,
        SwShModMergerWorkflowService? modMergerWorkflowService = null,
        SwShParsedDataCache? parsedDataCache = null)
    {
        var sharedParsedDataCache = parsedDataCache ?? new SwShParsedDataCache();
        this.projectWorkspaceService = projectWorkspaceService ?? new ProjectWorkspaceService();
        this.itemsWorkflowService = itemsWorkflowService ?? new SwShItemsWorkflowService();
        this.pokemonWorkflowService = pokemonWorkflowService ?? new SwShPokemonWorkflowService();
        this.movesWorkflowService = movesWorkflowService ?? new SwShMovesWorkflowService();
        this.encountersWorkflowService = encountersWorkflowService ?? new SwShEncountersWorkflowService();
        this.exeFsPatchWorkflowService = exeFsPatchWorkflowService ?? new SwShExeFsPatchWorkflowService(sharedParsedDataCache);
        this.bagHookWorkflowService = bagHookWorkflowService ?? new SwShBagHookWorkflowService(this.itemsWorkflowService);
        this.catchCapWorkflowService = catchCapWorkflowService ?? new SwShCatchCapWorkflowService();
        this.hyperTrainingWorkflowService = hyperTrainingWorkflowService ?? new SwShHyperTrainingWorkflowService();
        this.fairyGymBoostsWorkflowService = fairyGymBoostsWorkflowService ?? new SwShFairyGymBoostsWorkflowService();
        this.gymUniformRemovalWorkflowService = gymUniformRemovalWorkflowService ?? new SwShGymUniformRemovalWorkflowService();
        this.fashionUnlockWorkflowService = fashionUnlockWorkflowService ?? new SwShFashionUnlockWorkflowService();
        this.ivScreenWorkflowService = ivScreenWorkflowService ?? new SwShIvScreenWorkflowService();
        this.shinyRateWorkflowService = shinyRateWorkflowService ?? new SwShShinyRateWorkflowService();
        this.typeChartWorkflowService = typeChartWorkflowService ?? new SwShTypeChartWorkflowService();
        this.flagworkSaveWorkflowService = flagworkSaveWorkflowService ?? new SwShFlagworkSaveWorkflowService();
        this.giftPokemonWorkflowService = giftPokemonWorkflowService ?? new SwShGiftPokemonWorkflowService();
        this.tradePokemonWorkflowService = tradePokemonWorkflowService ?? new SwShTradePokemonWorkflowService();
        this.staticEncountersWorkflowService = staticEncountersWorkflowService ?? new SwShStaticEncountersWorkflowService();
        this.rentalPokemonWorkflowService = rentalPokemonWorkflowService ?? new SwShRentalPokemonWorkflowService();
        this.dynamaxAdventuresWorkflowService = dynamaxAdventuresWorkflowService ?? new SwShDynamaxAdventuresWorkflowService();
        this.placementWorkflowService = placementWorkflowService ?? new SwShPlacementWorkflowService();
        this.behaviorWorkflowService = behaviorWorkflowService ?? new SwShBehaviorWorkflowService();
        this.raidBattlesWorkflowService = raidBattlesWorkflowService ?? new SwShRaidBattlesWorkflowService();
        this.raidRewardsWorkflowService = raidRewardsWorkflowService ?? new SwShRaidRewardsWorkflowService();
        this.royalCandyWorkflowService = royalCandyWorkflowService ?? new SwShRoyalCandyWorkflowService(this.exeFsPatchWorkflowService, this.bagHookWorkflowService);
        this.startingItemsWorkflowService = startingItemsWorkflowService ?? new SwShStartingItemsWorkflowService(this.bagHookWorkflowService, this.itemsWorkflowService);
        this.shopsWorkflowService = shopsWorkflowService ?? new SwShShopsWorkflowService();
        this.spreadsheetImportWorkflowService = spreadsheetImportWorkflowService ?? new SwShSpreadsheetImportWorkflowService();
        this.modMergerWorkflowService = modMergerWorkflowService ?? new SwShModMergerWorkflowService(this.projectWorkspaceService);
        this.textWorkflowService = textWorkflowService ?? new SwShTextWorkflowService();
        this.trainersWorkflowService = trainersWorkflowService ?? new SwShTrainersWorkflowService();
    }

    public SwShWorkflowList List(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (!ProjectGameMetadata.IsSwordShield(paths.SelectedGame))
        {
            return new SwShWorkflowList([]);
        }

        var project = projectWorkspaceService.Open(paths);

        var summaries = new[]
        {
            itemsWorkflowService.CreateSummary(project),
            pokemonWorkflowService.CreateSummary(project),
            movesWorkflowService.CreateSummary(project),
            textWorkflowService.CreateSummary(project),
            trainersWorkflowService.CreateSummary(project),
            giftPokemonWorkflowService.CreateSummary(project),
            tradePokemonWorkflowService.CreateSummary(project),
            staticEncountersWorkflowService.CreateSummary(project),
            rentalPokemonWorkflowService.CreateSummary(project),
            dynamaxAdventuresWorkflowService.CreateSummary(project),
            shopsWorkflowService.CreateSummary(project),
            encountersWorkflowService.CreateSummary(project),
            raidBattlesWorkflowService.CreateSummary(project),
            raidRewardsWorkflowService.CreateSummary(project),
            raidRewardsWorkflowService.CreateBonusSummary(project),
            placementWorkflowService.CreateSummary(project),
            behaviorWorkflowService.CreateSummary(project),
            flagworkSaveWorkflowService.CreateSummary(project),
            bagHookWorkflowService.CreateSummary(project),
            catchCapWorkflowService.CreateSummary(project),
            hyperTrainingWorkflowService.CreateSummary(project),
            shinyRateWorkflowService.CreateSummary(project),
            typeChartWorkflowService.CreateSummary(project),
            fairyGymBoostsWorkflowService.CreateSummary(project),
            fashionUnlockWorkflowService.CreateSummary(project),
            gymUniformRemovalWorkflowService.CreateSummary(project),
            ivScreenWorkflowService.CreateSummary(project),
            royalCandyWorkflowService.CreateSummary(project),
            startingItemsWorkflowService.CreateSummary(project),
            spreadsheetImportWorkflowService.CreateSummary(project),
            modMergerWorkflowService.CreateSummary(project),
        };

        return new SwShWorkflowList(
            summaries
                .Select(summary => SwShWorkflowDependencyValidator.Apply(project, summary))
                .ToArray());
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

    public SwShDynamaxAdventuresWorkflow LoadDynamaxAdventures(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return dynamaxAdventuresWorkflowService.Load(project);
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

    public SwShRaidRewardsWorkflow LoadRaidBonusRewards(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return raidRewardsWorkflowService.LoadBonus(project);
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

    public SwShBehaviorWorkflow LoadBehavior(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return behaviorWorkflowService.Load(project);
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

    public SwShBagHookWorkflow LoadBagHook(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return bagHookWorkflowService.Load(project);
    }

    public SwShCatchCapWorkflow LoadCatchCap(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return catchCapWorkflowService.Load(project);
    }

    public SwShHyperTrainingWorkflow LoadHyperTraining(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return hyperTrainingWorkflowService.Load(project);
    }

    public SwShIvScreenWorkflow LoadIvScreen(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return ivScreenWorkflowService.Load(project);
    }

    public SwShTypeChartWorkflow LoadTypeChart(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return typeChartWorkflowService.Load(project);
    }

    public SwShShinyRateWorkflow LoadShinyRate(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return shinyRateWorkflowService.Load(project);
    }

    public SwShFairyGymBoostsWorkflow LoadFairyGymBoosts(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return fairyGymBoostsWorkflowService.Load(project);
    }

    public SwShGymUniformRemovalWorkflow LoadGymUniformRemoval(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return gymUniformRemovalWorkflowService.Load(project);
    }

    public SwShFashionUnlockWorkflow LoadFashionUnlock(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return fashionUnlockWorkflowService.Load(project);
    }

    public SwShRoyalCandyWorkflow LoadRoyalCandy(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return royalCandyWorkflowService.Load(project);
    }

    public SwShStartingItemsWorkflow LoadStartingItems(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return startingItemsWorkflowService.Load(project);
    }

    public SwShSpreadsheetImportWorkflow LoadSpreadsheetImport(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var project = projectWorkspaceService.Open(paths);

        return spreadsheetImportWorkflowService.Load(project);
    }

    public SwShModMergerWorkflow LoadModMerger(
        ProjectPaths paths,
        string? modDirectory1,
        string? modDirectory2)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return modMergerWorkflowService.Load(paths, modDirectory1, modDirectory2);
    }
}
