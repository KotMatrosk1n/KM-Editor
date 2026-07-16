// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Encounters;
using KM.SwSh.ExeFs;
using KM.SwSh.Flagwork;
using KM.SwSh.Gifts;
using KM.SwSh.Items;
using KM.SwSh.Moves;
using KM.SwSh.Placement;
using KM.SwSh.Raids;
using KM.SwSh.Rentals;
using KM.SwSh.RoyalCandy;
using KM.SwSh.ShinyRate;
using KM.SwSh.Shops;
using KM.SwSh.SpreadsheetImport;
using KM.SwSh.StaticEncounters;
using KM.SwSh.Text;
using KM.SwSh.Trades;
using KM.SwSh.Trainers;
using KM.SwSh.TypeChart;
using KM.SwSh.Workflows;
using System.Diagnostics;
using Xunit;

namespace KM.SwSh.Tests.Performance;

[Trait("Kind", "Slow")]
public sealed class SwShPerformanceBaselineTests(ITestOutputHelper output)
{
    [Fact]
    public void FullWorkflowLoadingHasSyntheticPerformanceBaseline()
    {
        using var temp = SwShPerformanceFixtureProject.Create();
        var workflowService = new SwShWorkflowService(
            dynamaxAdventuresWorkflowService: SwShDynamaxAdventuresWorkflowService.CreateForSyntheticTests());
        var measurements = new List<Measurement>();

        var openedProject = Record(measurements, "project.open", () => new ProjectWorkspaceService().Open(temp.Paths));
        Assert.True(
            openedProject.FileGraph.Entries.Count >= SwShPerformanceFixtureProject.ExtraRomFsFileCount,
            "The synthetic baseline must include enough files to exercise project graph enumeration.");

        var workflowList = Record(measurements, "workflows.list", () => workflowService.List(temp.Paths));
        Assert.Equal(32, workflowList.Workflows.Count);

        var items = Record(measurements, "items.load", () => workflowService.LoadItems(temp.Paths));
        var pokemon = Record(measurements, "pokemon.load", () => workflowService.LoadPokemon(temp.Paths));
        var moves = Record(measurements, "moves.load", () => workflowService.LoadMoves(temp.Paths));
        var text = Record(measurements, "text.load", () => workflowService.LoadText(temp.Paths));
        var trainers = Record(measurements, "trainers.load", () => workflowService.LoadTrainers(temp.Paths));
        var giftPokemon = Record(measurements, "giftPokemon.load", () => workflowService.LoadGiftPokemon(temp.Paths));
        var tradePokemon = Record(measurements, "tradePokemon.load", () => workflowService.LoadTradePokemon(temp.Paths));
        var staticEncounters = Record(measurements, "staticEncounters.load", () => workflowService.LoadStaticEncounters(temp.Paths));
        var rentalPokemon = Record(measurements, "rentalPokemon.load", () => workflowService.LoadRentalPokemon(temp.Paths));
        var dynamaxAdventures = Record(
            measurements,
            "dynamaxAdventures.load",
            () => workflowService.LoadDynamaxAdventures(
                temp.Paths with { SelectedGame = ProjectGame.Sword }));
        var shops = Record(measurements, "shops.load", () => workflowService.LoadShops(temp.Paths));
        var encounters = Record(measurements, "encounters.load", () => workflowService.LoadEncounters(temp.Paths));
        var raidBattles = Record(measurements, "raidBattles.load", () => workflowService.LoadRaidBattles(temp.Paths));
        var raidRewards = Record(measurements, "raidRewards.load", () => workflowService.LoadRaidRewards(temp.Paths));
        var raidBonusRewards = Record(measurements, "raidBonusRewards.load", () => workflowService.LoadRaidBonusRewards(temp.Paths));
        var placement = Record(measurements, "placement.load", () => workflowService.LoadPlacement(temp.Paths));
        var behavior = Record(measurements, "behavior.load", () => workflowService.LoadBehavior(temp.Paths));
        var flagwork = Record(measurements, "flagworkSave.load", () => workflowService.LoadFlagworkSave(temp.Paths));
        var exeFs = Record(measurements, "exefsPatches.load", () => workflowService.LoadExeFsPatches(temp.Paths));
        var bagHook = Record(measurements, "bagHook.load", () => workflowService.LoadBagHook(temp.Paths));
        var catchCap = Record(measurements, "catchCap.load", () => workflowService.LoadCatchCap(temp.Paths));
        var ivScreen = Record(
            measurements,
            "ivScreen.load",
            () => workflowService.LoadIvScreen(temp.Paths with { SelectedGame = ProjectGame.Sword }));
        var gymUniformRemoval = Record(
            measurements,
            "gymUniformRemoval.load",
            () => workflowService.LoadGymUniformRemoval(temp.Paths with { SelectedGame = ProjectGame.Sword }));
        var shinyRate = Record(measurements, "shinyRate.load", () => workflowService.LoadShinyRate(temp.Paths));
        var typeChart = Record(measurements, "typeChart.load", () => workflowService.LoadTypeChart(temp.Paths));
        var fairyGymBoosts = Record(measurements, "fairyGymBoosts.load", () => workflowService.LoadFairyGymBoosts(temp.Paths));
        var fashionUnlock = Record(
            measurements,
            "fashionUnlock.load",
            () => workflowService.LoadFashionUnlock(temp.Paths with { SelectedGame = ProjectGame.Sword }));
        var royalCandy = Record(measurements, "royalCandy.load", () => workflowService.LoadRoyalCandy(temp.Paths));
        var startingItems = Record(measurements, "startingItems.load", () => workflowService.LoadStartingItems(temp.Paths));
        var npcItemGift = Record(
            measurements,
            "npcItemGift.load",
            () => workflowService.LoadNpcItemGift(temp.Paths with { SelectedGame = ProjectGame.Sword }));
        var spreadsheetImport = Record(measurements, "spreadsheetImport.load", () => workflowService.LoadSpreadsheetImport(temp.Paths));
        var modMerger = Record(measurements, "modMerger.load", () => workflowService.LoadModMerger(temp.Paths, null, null));

        Assert.Equal(SwShPerformanceFixtureProject.ItemCount, items.Stats.TotalItemCount);
        Assert.Equal(SwShPerformanceFixtureProject.PokemonCount, pokemon.Stats.TotalPokemonCount);
        Assert.Equal(SwShPerformanceFixtureProject.MoveCount, moves.Stats.TotalMoveCount);
        Assert.True(text.Stats.TotalTextEntryCount >= SwShPerformanceFixtureProject.TextTableCount * SwShPerformanceFixtureProject.TextLinesPerTable);
        Assert.Equal(SwShPerformanceFixtureProject.VisibleTrainerCount, trainers.Stats.TotalTrainerCount);
        Assert.True(giftPokemon.Summary.Availability != SwShWorkflowAvailability.Disabled);
        Assert.Equal(2, tradePokemon.Stats.TotalTradeCount);
        Assert.Equal(2, staticEncounters.Stats.TotalEncounterCount);
        Assert.Equal(2, rentalPokemon.Stats.TotalRentalCount);
        Assert.Equal(SwShPerformanceFixtureProject.DynamaxAdventureCount, dynamaxAdventures.Stats.TotalEncounterCount);
        Assert.True(shops.Stats.TotalInventoryItemCount > 0);
        Assert.Equal(SwShPerformanceFixtureProject.EncounterTableCount * 3 * 2, encounters.Stats.TotalTableCount);
        Assert.Equal(SwShPerformanceFixtureProject.RaidBattleTableCount, raidBattles.Stats.TotalTableCount);
        Assert.Equal(
            SwShPerformanceFixtureProject.RaidBattleTableCount * SwShPerformanceFixtureProject.RaidBattleSlotsPerTable,
            raidBattles.Stats.TotalSlotCount);
        Assert.Equal(SwShPerformanceFixtureProject.RaidRewardTableCount, raidRewards.Stats.TotalTableCount);
        Assert.Equal(SwShPerformanceFixtureProject.RaidRewardTableCount, raidBonusRewards.Stats.TotalTableCount);
        Assert.True(placement.Stats.TotalObjectCount > 0);
        Assert.Equal(SwShPerformanceFixtureProject.SymbolBehaviorEntryCount, behavior.Stats.TotalEntryCount);
        Assert.Equal(
            SwShPerformanceFixtureProject.FlagworkTableCount * SwShPerformanceFixtureProject.FlagworkRowsPerTable,
            flagwork.Stats.TotalFlagCount);
        Assert.Single(exeFs.Patches);
        Assert.Equal(20, bagHook.Stats.TotalSlotCount);
        Assert.Equal(9, catchCap.Stats.TotalCapCount);
        Assert.True(ivScreen.Stats.ReservedMainTextRegionCount > 0);
        Assert.True(gymUniformRemoval.Stats.ReservedMainTextRegionCount > 0);
        Assert.Equal(SwShShinyRateWorkflowService.PresetDefinitions.Count, shinyRate.Stats.PresetCount);
        Assert.Equal(SwShTypeChartMainPatcher.ChartLength, typeChart.Stats.ChartCellCount);
        Assert.Equal(4, fairyGymBoosts.Stats.TrainerCount);
        Assert.True(fashionUnlock.Summary.Availability != SwShWorkflowAvailability.Disabled);
        Assert.True(royalCandy.Checks.Count > 0);
        Assert.Equal(19, startingItems.Stats.TotalGrantSlotCount);
        Assert.True(npcItemGift.Stats.GiftCount > 0);
        Assert.Single(spreadsheetImport.Profiles);
        Assert.Equal(0, modMerger.Stats.MatchingFileCount);

        ReportMeasurements(measurements);
        AssertBudget(measurements, TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void RepeatedOpenedProjectLoadsExposeSharedParseBaseline()
    {
        using var temp = SwShPerformanceFixtureProject.Create();
        var measurements = new List<Measurement>();
        var workspaceService = new ProjectWorkspaceService();
        var project = Record(measurements, "project.open.shared", () => workspaceService.Open(temp.Paths));
        var itemsService = new SwShItemsWorkflowService();
        var movesService = new SwShMovesWorkflowService();
        var exeFsService = new SwShExeFsPatchWorkflowService();

        var items = Record(measurements, "items.load.openedProject", () => itemsService.Load(project));
        var moves = Record(measurements, "moves.load.openedProject", () => movesService.Load(project));
        var shops = Record(measurements, "shops.load.openedProject.includesItems", () => new SwShShopsWorkflowService(itemsService).Load(project));
        var spreadsheetImport = Record(measurements, "spreadsheetImport.load.openedProject.includesItems", () => new SwShSpreadsheetImportWorkflowService(itemsService).Load(project));
        var exeFs = Record(measurements, "exefsPatches.load.openedProject", () => exeFsService.Load(project));
        var royalCandy = Record(measurements, "royalCandy.load.openedProject.sharedExeFs", () => new SwShRoyalCandyWorkflowService(exeFsService).Load(project));
        var text = Record(measurements, "text.load.openedProject", () => new SwShTextWorkflowService().Load(project));
        var trainers = Record(measurements, "trainers.load.openedProject", () => new SwShTrainersWorkflowService().Load(project));
        var giftPokemon = Record(measurements, "giftPokemon.load.openedProject", () => new SwShGiftPokemonWorkflowService().Load(project));
        var tradePokemon = Record(measurements, "tradePokemon.load.openedProject", () => new SwShTradePokemonWorkflowService().Load(project));
        var staticEncounters = Record(measurements, "staticEncounters.load.openedProject", () => new SwShStaticEncountersWorkflowService().Load(project));
        var rentalPokemon = Record(measurements, "rentalPokemon.load.openedProject", () => new SwShRentalPokemonWorkflowService().Load(project));

        Assert.Equal(SwShPerformanceFixtureProject.ItemCount, items.Items.Count);
        Assert.Equal(SwShPerformanceFixtureProject.MoveCount, moves.Moves.Count);
        Assert.True(shops.Shops.Count > 0);
        Assert.Single(spreadsheetImport.Profiles);
        Assert.Single(exeFs.Patches);
        Assert.True(royalCandy.Outputs.Count > 0);
        Assert.True(text.Entries.Count > 0);
        Assert.Equal(SwShPerformanceFixtureProject.VisibleTrainerCount, trainers.Trainers.Count);
        Assert.True(giftPokemon.Summary.Availability != SwShWorkflowAvailability.Disabled);
        Assert.Equal(2, tradePokemon.Trades.Count);
        Assert.Equal(2, staticEncounters.Encounters.Count);
        Assert.Equal(2, rentalPokemon.Rentals.Count);

        var repeatOpen1 = Record(measurements, "project.open.repeat1", () => workspaceService.Open(temp.Paths));
        var repeatOpen2 = Record(measurements, "project.open.repeat2", () => workspaceService.Open(temp.Paths));
        Assert.Equal(project.FileGraph.Entries.Count, repeatOpen1.FileGraph.Entries.Count);
        Assert.Equal(project.FileGraph.Entries.Count, repeatOpen2.FileGraph.Entries.Count);

        ReportMeasurements(measurements);
        AssertBudget(measurements, TimeSpan.FromSeconds(30));
    }

    private T Record<T>(ICollection<Measurement> measurements, string name, Func<T> action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = action();
        stopwatch.Stop();
        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        measurements.Add(new Measurement(
            name,
            stopwatch.Elapsed,
            Math.Max(0, allocatedAfter - allocatedBefore)));

        return result;
    }

    private void ReportMeasurements(IReadOnlyList<Measurement> measurements)
    {
        foreach (var measurement in measurements)
        {
            output.WriteLine(
                $"{measurement.Name}: {measurement.Elapsed.TotalMilliseconds:F1} ms, {measurement.AllocatedBytes / 1024d / 1024d:F2} MiB allocated");
        }

        var total = TimeSpan.FromTicks(measurements.Sum(measurement => measurement.Elapsed.Ticks));
        var allocated = measurements.Sum(measurement => measurement.AllocatedBytes);
        output.WriteLine($"total: {total.TotalMilliseconds:F1} ms, {allocated / 1024d / 1024d:F2} MiB allocated");
    }

    private static void AssertBudget(IReadOnlyList<Measurement> measurements, TimeSpan budget)
    {
        var total = TimeSpan.FromTicks(measurements.Sum(measurement => measurement.Elapsed.Ticks));
        Assert.True(
            total < budget,
            $"Synthetic performance baseline exceeded its generous sanity budget: {total.TotalSeconds:F1}s >= {budget.TotalSeconds:F1}s.");
    }

    private sealed record Measurement(
        string Name,
        TimeSpan Elapsed,
        long AllocatedBytes);
}
