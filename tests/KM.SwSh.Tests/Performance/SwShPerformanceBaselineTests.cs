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
using KM.SwSh.Workflows;
using System.Diagnostics;
using Xunit;

namespace KM.SwSh.Tests.Performance;

public sealed class SwShPerformanceBaselineTests(ITestOutputHelper output)
{
    [Fact]
    public void FullWorkflowLoadingHasSyntheticPerformanceBaseline()
    {
        using var temp = SwShPerformanceFixtureProject.Create();
        var workflowService = new SwShWorkflowService();
        var measurements = new List<Measurement>();

        var openedProject = Record(measurements, "project.open", () => new ProjectWorkspaceService().Open(temp.Paths));
        Assert.True(
            openedProject.FileGraph.Entries.Count >= SwShPerformanceFixtureProject.ExtraRomFsFileCount,
            "The synthetic baseline must include enough files to exercise project graph enumeration.");

        var workflowList = Record(measurements, "workflows.list", () => workflowService.List(temp.Paths));
        Assert.Equal(11, workflowList.Workflows.Count);

        var items = Record(measurements, "items.load", () => workflowService.LoadItems(temp.Paths));
        var text = Record(measurements, "text.load", () => workflowService.LoadText(temp.Paths));
        var trainers = Record(measurements, "trainers.load", () => workflowService.LoadTrainers(temp.Paths));
        var shops = Record(measurements, "shops.load", () => workflowService.LoadShops(temp.Paths));
        var encounters = Record(measurements, "encounters.load", () => workflowService.LoadEncounters(temp.Paths));
        var raidRewards = Record(measurements, "raidRewards.load", () => workflowService.LoadRaidRewards(temp.Paths));
        var placement = Record(measurements, "placement.load", () => workflowService.LoadPlacement(temp.Paths));
        var flagwork = Record(measurements, "flagworkSave.load", () => workflowService.LoadFlagworkSave(temp.Paths));
        var exeFs = Record(measurements, "exefsPatches.load", () => workflowService.LoadExeFsPatches(temp.Paths));
        var royalCandy = Record(measurements, "royalCandy.load", () => workflowService.LoadRoyalCandy(temp.Paths));
        var spreadsheetImport = Record(measurements, "spreadsheetImport.load", () => workflowService.LoadSpreadsheetImport(temp.Paths));

        Assert.Equal(SwShPerformanceFixtureProject.ItemCount, items.Stats.TotalItemCount);
        Assert.True(text.Stats.TotalTextEntryCount >= SwShPerformanceFixtureProject.TextTableCount * SwShPerformanceFixtureProject.TextLinesPerTable);
        Assert.Equal(SwShPerformanceFixtureProject.TrainerCount, trainers.Stats.TotalTrainerCount);
        Assert.True(shops.Stats.TotalInventoryItemCount > 0);
        Assert.Equal(SwShPerformanceFixtureProject.EncounterTableCount * 3 * 2, encounters.Stats.TotalTableCount);
        Assert.Equal(SwShPerformanceFixtureProject.RaidRewardTableCount * 2, raidRewards.Stats.TotalTableCount);
        Assert.True(placement.Stats.TotalObjectCount > 0);
        Assert.Equal(
            SwShPerformanceFixtureProject.FlagworkTableCount * SwShPerformanceFixtureProject.FlagworkRowsPerTable,
            flagwork.Stats.TotalFlagCount);
        Assert.Single(exeFs.Patches);
        Assert.True(royalCandy.Checks.Count > 0);
        Assert.Single(spreadsheetImport.Profiles);

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
        var exeFsService = new SwShExeFsPatchWorkflowService();

        var items = Record(measurements, "items.load.openedProject", () => itemsService.Load(project));
        var shops = Record(measurements, "shops.load.openedProject.includesItems", () => new SwShShopsWorkflowService(itemsService).Load(project));
        var spreadsheetImport = Record(measurements, "spreadsheetImport.load.openedProject.includesItems", () => new SwShSpreadsheetImportWorkflowService(itemsService).Load(project));
        var exeFs = Record(measurements, "exefsPatches.load.openedProject", () => exeFsService.Load(project));
        var royalCandy = Record(measurements, "royalCandy.load.openedProject.sharedExeFs", () => new SwShRoyalCandyWorkflowService(exeFsService).Load(project));
        var text = Record(measurements, "text.load.openedProject", () => new SwShTextWorkflowService().Load(project));
        var trainers = Record(measurements, "trainers.load.openedProject", () => new SwShTrainersWorkflowService().Load(project));

        Assert.Equal(SwShPerformanceFixtureProject.ItemCount, items.Items.Count);
        Assert.True(shops.Shops.Count > 0);
        Assert.Single(spreadsheetImport.Profiles);
        Assert.Single(exeFs.Patches);
        Assert.True(royalCandy.Outputs.Count > 0);
        Assert.True(text.Entries.Count > 0);
        Assert.Equal(SwShPerformanceFixtureProject.TrainerCount, trainers.Trainers.Count);

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
