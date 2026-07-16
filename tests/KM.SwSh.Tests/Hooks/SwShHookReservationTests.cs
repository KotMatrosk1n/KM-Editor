// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.Formats.Executable;
using KM.SwSh.BagHook;
using KM.SwSh.CatchCap;
using KM.SwSh.ExeFs;
using KM.SwSh.FashionUnlock;
using KM.SwSh.FpsPatch;
using KM.SwSh.GymUniformRemoval;
using KM.SwSh.HyperTraining;
using KM.SwSh.IvScreen;
using KM.SwSh.Items;
using KM.SwSh.NameFilter;
using KM.SwSh.RoyalCandy;
using KM.SwSh.ShinyRate;
using KM.SwSh.StartingItems;
using KM.SwSh.Tests.FpsPatch;
using KM.SwSh.Tests.Items;
using KM.SwSh.Tests.Performance;
using KM.SwSh.TypeChart;
using KM.SwSh.Workflows;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.Hooks;

public sealed class SwShHookReservationTests
{
    private const ulong SwordTitleId = 0x0100ABF008968000;
    private const ulong ShieldTitleId = 0x01008DB008C2C000;
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";
    private const string RoyalCandyUnlimitedWorkflowId = "royal-candy-unlimited";
    private const string RoyalCandyStoryLimitsWorkflowId = "royal-candy-story-limits";
    private const string RoyalCandyUninstallWorkflowId = "royal-candy-uninstall";
    private static readonly Lazy<byte[]> SwordSharedHookNso = new(
        () => CreateSharedHookNsoCore(ProjectGame.Sword),
        LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<byte[]> ShieldSharedHookNso = new(
        () => CreateSharedHookNsoCore(ProjectGame.Shield),
        LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<byte[]> SwordSharedHookNsoWithFpsAnchors = new(
        () => CreateSharedHookNsoWithFpsAnchorsCore(ProjectGame.Sword),
        LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<byte[]> ShieldSharedHookNsoWithFpsAnchors = new(
        () => CreateSharedHookNsoWithFpsAnchorsCore(ProjectGame.Shield),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static IEnumerable<object[]> ExeFsInstallOrders()
    {
        foreach (var game in new[] { ProjectGame.Sword, ProjectGame.Shield })
        {
            yield return [game, RoyalCandyUnlimitedWorkflowId, true];
            yield return [game, RoyalCandyUnlimitedWorkflowId, false];
        }
    }

    public static IEnumerable<object[]> RoyalCandyVariantsByGame()
    {
        foreach (var workflowId in new[] { RoyalCandyUnlimitedWorkflowId, RoyalCandyStoryLimitsWorkflowId })
        {
            yield return [ProjectGame.Sword, workflowId];
        }
    }

    public static IEnumerable<object[]> RoyalCandyBuildVariants()
    {
        foreach (var game in new[] { ProjectGame.Sword, ProjectGame.Shield })
        {
            yield return [game, RoyalCandyUnlimitedWorkflowId];
            yield return [game, RoyalCandyStoryLimitsWorkflowId];
        }
    }

    [Fact]
    public void NewHookWorkflowsLoadAgainstShieldProject()
    {
        using var temp = SwShPerformanceFixtureProject.Create();
        temp.WriteBaseRomFsFile("bin/script/amx/main_event_0020.amx", CreateVanillaBagEventScript());
        temp.WriteBaseExeFsFile("main", CreateSharedHookNso(ProjectGame.Shield));
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(ShieldTitleId));
        var paths = temp.Paths with { SelectedGame = ProjectGame.Shield };
        InstallEmptyBagHook(paths);
        var project = new ProjectWorkspaceService().Open(paths);

        var bagHook = new SwShBagHookWorkflowService().Load(project);
        var catchCap = new SwShCatchCapWorkflowService().Load(project);
        var gymUniformRemoval = new SwShGymUniformRemovalWorkflowService().Load(project);
        var fashionUnlock = new SwShFashionUnlockWorkflowService().Load(project);
        var ivScreen = new SwShIvScreenWorkflowService().Load(project);
        var royalCandy = new SwShRoyalCandyWorkflowService().Load(project);
        var startingItems = new SwShStartingItemsWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.Available, bagHook.Summary.Availability);
        Assert.Equal("installed", bagHook.InstallStatus);
        Assert.Equal(20, bagHook.Slots.Count);
        Assert.Equal("Reserved for Royal Candy", bagHook.Slots.Single(slot => slot.Slot == 1).Owner);
        Assert.All(
            bagHook.Slots.Where(slot => slot.Slot is >= 2 and <= 20),
            slot => Assert.Equal("Starting Items", slot.ReservedFor));

        Assert.Equal(SwShWorkflowAvailability.Available, catchCap.Summary.Availability);
        Assert.Equal("available", catchCap.InstallStatus);
        Assert.Equal(9, catchCap.Caps.Count);

        Assert.Equal(SwShWorkflowAvailability.Available, gymUniformRemoval.Summary.Availability);
        Assert.Equal("available", gymUniformRemoval.InstallStatus);
        Assert.Equal("main.text+0x01472630", gymUniformRemoval.PatchOffsetHex);
        Assert.Contains(gymUniformRemoval.ReservedRegions, region => region.RegionId == "gym-uniform-removal-shield-handler");

        Assert.Equal(SwShWorkflowAvailability.Available, fashionUnlock.Summary.Availability);
        Assert.Equal("available", fashionUnlock.InstallStatus);
        Assert.Equal("main.text+0x0143A2E0", fashionUnlock.DirectGetterOffsetHex);
        Assert.Equal("main.text+0x0143A330", fashionUnlock.MappedGetterOffsetHex);
        Assert.Contains(fashionUnlock.ReservedRegions, region => region.RegionId == "fashion-unlock-shield-direct-owned-getter");

        Assert.Equal(SwShWorkflowAvailability.Available, ivScreen.Summary.Availability);
        Assert.Equal("available", ivScreen.InstallStatus);
        Assert.Equal("SWSH_IV_DISPLAY_V1", ivScreen.Marker);
        Assert.Equal(ShieldBuildId, ivScreen.BuildId);
        Assert.Equal(ProjectGame.Shield, ivScreen.DetectedGame);
        Assert.Equal("main.text+0x0138A2E4", ivScreen.PrimaryValueSourceOffsetHex);
        Assert.Equal("main.text+0x0138B3DC", ivScreen.XToggleRefreshOffsetHex);
        Assert.Equal("main.text+0x00779070", ivScreen.RawIvGetterOffsetHex);
        Assert.Equal("main.text+0x007790D0", ivScreen.HyperTrainingWrapperOffsetHex);
        Assert.False(ivScreen.CanUninstall);
        Assert.Contains(ivScreen.ReservedRegions, region => region.RegionId == "iv-screen-hook-site");

        Assert.Equal(SwShWorkflowAvailability.Available, royalCandy.Summary.Availability);
        Assert.Contains(
            royalCandy.Checks,
            check => check.CheckId.EndsWith(":game-flavor", StringComparison.Ordinal)
                && check.Status == "Pass"
                && check.Message.Contains("Pokemon Shield", StringComparison.Ordinal));
        Assert.Contains(royalCandy.Workflows, workflow => workflow.WorkflowId == "royal-candy-unlimited" && workflow.Status == "available");
        Assert.Contains(royalCandy.Workflows, workflow => workflow.WorkflowId == "royal-candy-story-limits" && workflow.Status == "available");

        Assert.Equal(SwShWorkflowAvailability.Available, startingItems.Summary.Availability);
        Assert.Equal("available", startingItems.InstallStatus);
        Assert.Equal(19, startingItems.Grants.Count);
        Assert.DoesNotContain(startingItems.Grants, grant => grant.Slot == 1);
    }

    [Fact]
    public void TypeChartReservesTheVerifiedMainRoRange()
    {
        var regions = SwShExeFsReservedRegionLedger.MainRoRegionsForOwner(
            SwShExeFsReservedRegionLedger.OwnerTypeChart);
        var region = Assert.Single(regions, region => region.Rule == "payload-only");
        var dependencies = regions
            .Where(region => region.Rule == "requires-vanilla")
            .OrderBy(region => region.StartOffset)
            .ToArray();

        Assert.Equal("type-chart-swsh", region.FeatureId);
        Assert.Equal(SwShExeFsReservedRegionLedger.ExeFsMainPath, region.RelativePath);
        Assert.Equal("main.ro", region.Area);
        Assert.Equal(0x00743600, region.StartOffset);
        Assert.Equal(0x144, region.Length);
        Assert.Equal("ro+0x743600..0x743743", region.OffsetLabel);
        Assert.Equal(2, dependencies.Length);
        Assert.Equal(0x007435C0, dependencies[0].StartOffset);
        Assert.Equal(0x40, dependencies[0].Length);
        Assert.Equal(0x00743744, dependencies[1].StartOffset);
        Assert.Equal(0x40, dependencies[1].Length);
        Assert.Equal(new[] { region }, SwShTypeChartMainPatcher.ReservedMainRoRegions());
    }

    [Fact]
    public void IvScreenRuntimeDependenciesAreProtectedButNotIvOwnedRestoreRegions()
    {
        var runtimeRegions = SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(
            SwShExeFsReservedRegionLedger.OwnerPokemonSummaryRuntime);
        var requiredOffsets = runtimeRegions
            .Where(region => region.Rule == "requires-vanilla")
            .Select(region => region.StartOffset!.Value)
            .Order()
            .ToArray();
        int[] expectedOffsets =
        [
            0x00778E20,
            0x00779F50,
            0x0077AC30,
            0x0077AC70,
            0x0077AFD0,
            0x0138A1A0,
            0x0138A1D0,
            0x0138B1E0,
            0x0138B550,
            0x0138B580,
            0x0138FB60,
            0x0138FB90,
        ];

        Assert.Equal(expectedOffsets, requiredOffsets);
        var allocatorReservations = SwShExeFsReservedRegionLedger.MainTextReservationsForOtherOwners(
            SwShExeFsReservedRegionLedger.OwnerRoyalCandy,
            SwShExeFsReservedRegionLedger.OwnerRoyalCandyStoryLimits);
        Assert.All(expectedOffsets, offset => Assert.Contains(
            allocatorReservations,
            region => region.Owner == SwShExeFsReservedRegionLedger.OwnerPokemonSummaryRuntime
                && region.StartOffset == offset));

        foreach (var game in new[] { ProjectGame.Sword, ProjectGame.Shield })
        {
            var ivOwned = SwShIvScreenMainPatcher.ReservedMainTextRegions(game);
            Assert.DoesNotContain(ivOwned, region =>
                region.StartOffset is not null && expectedOffsets.Contains(region.StartOffset.Value));
        }
    }

    [Fact]
    public void FashionUnlockReservesSwordAndShieldOwnershipGetterRanges()
    {
        var regions = SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerFashionUnlock);

        Assert.Collection(
            regions.OrderBy(region => region.StartOffset).ToArray(),
            region =>
            {
                Assert.Equal("fashion-unlock-sword-direct-owned-getter", region.FeatureId);
                Assert.Equal(0x0143A2B0, region.StartOffset);
                Assert.Equal(0x08, region.Length);
            },
            region =>
            {
                Assert.Equal("fashion-unlock-shield-direct-owned-getter", region.FeatureId);
                Assert.Equal(0x0143A2E0, region.StartOffset);
                Assert.Equal(0x08, region.Length);
            },
            region =>
            {
                Assert.Equal("fashion-unlock-sword-mapped-owned-getter", region.FeatureId);
                Assert.Equal(0x0143A300, region.StartOffset);
                Assert.Equal(0x08, region.Length);
            },
            region =>
            {
                Assert.Equal("fashion-unlock-shield-mapped-owned-getter", region.FeatureId);
                Assert.Equal(0x0143A330, region.StartOffset);
                Assert.Equal(0x08, region.Length);
            });
    }

    [Fact]
    public void ShinyRateReservesExactSwordAndShieldWrittenAndDependencyRanges()
    {
        var regions = SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerShinyRate);

        Assert.Equal(8, regions.Count);
        AssertRegion("shiny-rate-sword-function-prelude", 0x00D311C0, 0x24, "requires-vanilla");
        AssertRegion("shiny-rate-sword-reroll-loop-dependencies-before", 0x00D31448, 0x40, "requires-vanilla");
        AssertRegion("shiny-rate-sword-reroll-loop-control", 0x00D31488, 0x08, "do-not-overwrite");
        AssertRegion("shiny-rate-sword-reroll-loop-dependencies-after", 0x00D31490, 0x20, "requires-vanilla");
        AssertRegion("shiny-rate-shield-function-prelude", 0x00D311F0, 0x24, "requires-vanilla");
        AssertRegion("shiny-rate-shield-reroll-loop-dependencies-before", 0x00D31478, 0x40, "requires-vanilla");
        AssertRegion("shiny-rate-shield-reroll-loop-control", 0x00D314B8, 0x08, "do-not-overwrite");
        AssertRegion("shiny-rate-shield-reroll-loop-dependencies-after", 0x00D314C0, 0x20, "requires-vanilla");

        void AssertRegion(string featureId, int offset, int length, string rule)
        {
            var region = Assert.Single(regions, candidate => candidate.FeatureId == featureId);
            Assert.Equal(offset, region.StartOffset);
            Assert.Equal(length, region.Length);
            Assert.Equal(rule, region.Rule);
        }
    }

    [Theory]
    [InlineData(ProjectGame.Sword, 0x00D31488)]
    [InlineData(ProjectGame.Shield, 0x00D314B8)]
    public void ShinyRateExposesOnlySelectedGameWritableRegion(ProjectGame game, int expectedOffset)
    {
        var allSelectedGameRegions = SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(
            SwShExeFsReservedRegionLedger.OwnerShinyRate,
            game);
        var writableRegions = SwShShinyRateMainPatcher.ReservedMainTextRegions(game);

        Assert.Equal(4, allSelectedGameRegions.Count);
        var writable = Assert.Single(writableRegions);
        Assert.Equal(expectedOffset, writable.StartOffset);
        Assert.Equal(0x08, writable.Length);
        Assert.Equal("do-not-overwrite", writable.Rule);
    }

    [Fact]
    public void ProfanityFilterReservesSwordAndShieldProfanityCheckCalls()
    {
        var regions = SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerNameFilterBypass);

        Assert.Collection(
            regions.OrderBy(region => region.StartOffset).ToArray(),
            region =>
            {
                Assert.Equal("name-filter-bypass-sword-profanity-call", region.FeatureId);
                Assert.Equal(SwShNameFilterMainPatcher.SwordProfanityCheckCallOffset, region.StartOffset);
                Assert.Equal(0x04, region.Length);
                Assert.Equal("text+0xEF1228..0xEF122B", region.OffsetLabel);
            },
            region =>
            {
                Assert.Equal("name-filter-bypass-shield-profanity-call", region.FeatureId);
                Assert.Equal(SwShNameFilterMainPatcher.ShieldProfanityCheckCallOffset, region.StartOffset);
                Assert.Equal(0x04, region.Length);
                Assert.Equal("text+0xEF1258..0xEF125B", region.OffsetLabel);
            });
    }

    [Fact]
    public void ReservedMainRegionsDoNotOverlapBetweenFeatureFamiliesInSameSegment()
    {
        var regions = SwShExeFsReservedRegionLedger.Regions
            .Where(region => string.Equals(region.RelativePath, SwShExeFsReservedRegionLedger.ExeFsMainPath, StringComparison.OrdinalIgnoreCase)
                && region.StartOffset is not null
                && region.Length is not null)
            .ToArray();

        for (var leftIndex = 0; leftIndex < regions.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < regions.Length; rightIndex++)
            {
                var left = regions[leftIndex];
                var right = regions[rightIndex];
                if (IsSameFeatureFamily(left.Owner, right.Owner)
                    || IsMutuallyExclusiveCatchCapLayout(left, right)
                    || IsMutuallyExclusiveShinyRateLayout(left, right))
                {
                    continue;
                }

                if (!string.Equals(left.Area, right.Area, StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.False(
                    SwShExeFsReservedRegionLedger.Overlaps(left, right.StartOffset!.Value, right.Length!.Value),
                    $"{left.Owner} {left.FeatureId} {left.OffsetLabel} overlaps {right.Owner} {right.FeatureId} {right.OffsetLabel}.");
            }
        }
    }

    [Fact]
    public void CatchCapLedgerReservesExactSwordAndShieldWrittenAndDependencyRanges()
    {
        var regions = SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(
            SwShExeFsReservedRegionLedger.OwnerCatchCap);
        var pairs = new (string SwordId, string ShieldId)[]
        {
            ("catch-cap-hook-site", "catch-cap-shield-hook-site"),
            ("catch-cap-table", "catch-cap-shield-table"),
            ("catch-cap-marker", "catch-cap-shield-marker"),
            ("catch-cap-reserved-metadata", "catch-cap-shield-reserved-metadata"),
            ("catch-cap-return", "catch-cap-shield-return"),
            ("catch-cap-runtime-gate", "catch-cap-shield-runtime-gate"),
            ("catch-cap-runtime-return", "catch-cap-shield-runtime-return"),
            ("catch-cap-cave-1", "catch-cap-shield-cave-1"),
            ("catch-cap-cave-2", "catch-cap-shield-cave-2"),
            ("catch-cap-cave-3", "catch-cap-shield-cave-3"),
            ("catch-cap-cave-4", "catch-cap-shield-cave-4"),
        };

        Assert.Equal(pairs.Length * 2, regions.Count);
        Assert.DoesNotContain(regions, region => region.FeatureId.Contains("cave-5", StringComparison.Ordinal));
        foreach (var (swordId, shieldId) in pairs)
        {
            var sword = Assert.Single(regions, region => region.FeatureId == swordId);
            var shield = Assert.Single(regions, region => region.FeatureId == shieldId);
            Assert.Equal(sword.StartOffset + 0x30, shield.StartOffset);
            Assert.Equal(sword.Length, shield.Length);
            Assert.Equal(sword.Rule, shield.Rule);
        }

        foreach (var dependencyId in new[]
        {
            "catch-cap-return",
            "catch-cap-runtime-return",
            "catch-cap-shield-return",
            "catch-cap-shield-runtime-return",
        })
        {
            var dependency = Assert.Single(regions, region => region.FeatureId == dependencyId);
            Assert.Equal(0x08, dependency.Length);
            Assert.Equal("requires-vanilla", dependency.Rule);
        }
    }

    [Fact]
    public void FpsPatchReservesTheWorkingSwordAndShieldMainTextSites()
    {
        var regions = SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerFpsPatch);
        var expected = new Dictionary<string, (int Offset, int Length)>
        {
            ["60fps-sword-nvn-present-interval"] = (0x018A2C88, 0x04),
            ["60fps-shield-nvn-present-interval"] = (0x018A2D18, 0x04),
            ["60fps-duration-table-index-0"] = (0x000061F0, 0x04),
            ["60fps-duration-table-paired-index-0"] = (0x0000620C, 0x04),
            ["60fps-inline-frame-duration-low"] = (0x005DE834, 0x04),
            ["60fps-inline-frame-duration-high"] = (0x005DE838, 0x04),
            ["60fps-sword-battle-event-scheduler-adrp"] = (0x0131677C, 0x04),
            ["60fps-sword-battle-event-scheduler-ldr"] = (0x01316780, 0x04),
            ["60fps-shield-battle-event-scheduler-adrp"] = (0x013167AC, 0x04),
            ["60fps-shield-battle-event-scheduler-ldr"] = (0x013167B0, 0x04),
            ["60fps-actor-model-speed-setter"] = (0x009D17B0, 0x10),
            ["60fps-actor-direct-speed-seed-a"] = (0x009D05C8, 0x04),
            ["60fps-actor-direct-speed-seed-b"] = (0x009D0834, 0x04),
            ["60fps-actor-direct-speed-seed-c"] = (0x009D0838, 0x04),
            ["60fps-actor-direct-speed-seed-d"] = (0x009D0848, 0x04),
        };

        Assert.Equal(expected.Count, regions.Count);
        foreach (var (featureId, site) in expected)
        {
            var region = Assert.Single(regions, candidate => candidate.FeatureId == featureId);
            Assert.Equal(SwShExeFsReservedRegionLedger.ExeFsMainPath, region.RelativePath);
            Assert.Equal("main.text", region.Area);
            Assert.Equal(site.Offset, region.StartOffset);
            Assert.Equal(site.Length, region.Length);
            Assert.Equal(SwShExeFsReservedRegionLedger.OwnerFpsPatch, region.Owner);
        }
    }

    [Theory]
    [MemberData(nameof(ExeFsInstallOrders))]
    public void RoyalCandyAndCatchCapInstallInEitherOrderWithoutOverlapping(ProjectGame game, string workflowId, bool royalCandyFirst)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);

        if (royalCandyFirst)
        {
            ApplyRoyalCandy(paths, workflowId);
            ApplyCatchCap(paths);
        }
        else
        {
            ApplyCatchCap(paths);
            ApplyRoyalCandy(paths, workflowId);
        }

        var main = File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath));
        Assert.Equal(SwShCatchCapInstallKind.InstalledV1, SwShCatchCapMainPatcher.Analyze(main).Kind);
        Assert.Equal(ExpectedRoyalCandySignature(workflowId), SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(main).Kind);
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void IvScreenInstallsAlongsideCatchCapAndRoyalCandy(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyCatchCap(paths);
        ApplyRoyalCandy(paths, workflowId);
        ApplyIvScreen(paths);

        var main = File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath));
        Assert.Equal(SwShIvScreenInstallKind.InstalledV1, SwShIvScreenMainPatcher.Analyze(main).Kind);
        Assert.Equal(SwShCatchCapInstallKind.InstalledV1, SwShCatchCapMainPatcher.Analyze(main).Kind);
        Assert.Equal(ExpectedRoyalCandySignature(workflowId), SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(main).Kind);
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void GymUniformRemovalInstallsAlongsideCatchCapIvScreenAndRoyalCandy(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyCatchCap(paths);
        ApplyIvScreen(paths);
        ApplyRoyalCandy(paths, workflowId);
        ApplyGymUniformRemoval(paths);

        AssertGymUniformIpsInstalled(paths, game);
        var main = File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath));
        Assert.Equal(SwShCatchCapInstallKind.InstalledV1, SwShCatchCapMainPatcher.Analyze(main).Kind);
        Assert.Equal(SwShIvScreenInstallKind.InstalledV1, SwShIvScreenMainPatcher.Analyze(main).Kind);
        Assert.Equal(ExpectedRoyalCandySignature(workflowId), SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(main).Kind);
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void HyperTrainingMainPatchPreservesCatchCapIvScreenAndRoyalCandy(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyCatchCap(paths);
        ApplyIvScreen(paths);
        ApplyRoyalCandy(paths, workflowId);

        ApplyHyperTrainingMain(paths, minimumLevel: 50);

        var main = File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath));
        var hyperTraining = SwShHyperTrainingMainPatcher.Analyze(main, game);
        Assert.Equal(SwShHyperTrainingMainKind.CustomMinimumLevel, hyperTraining.Kind);
        Assert.Equal(50, hyperTraining.MinimumLevel);
        Assert.Equal(SwShCatchCapInstallKind.InstalledV1, SwShCatchCapMainPatcher.Analyze(main).Kind);
        Assert.Equal(SwShIvScreenInstallKind.InstalledV1, SwShIvScreenMainPatcher.Analyze(main).Kind);
        Assert.Equal(ExpectedRoyalCandySignature(workflowId), SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(main).Kind);
    }

    [Theory]
    [MemberData(nameof(ExeFsInstallOrders))]
    public void RoyalCandyAndStartingItemsApplyInEitherOrderWithoutOverwritingBagHookSlots(ProjectGame game, string workflowId, bool royalCandyFirst)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);

        if (royalCandyFirst)
        {
            ApplyRoyalCandy(paths, workflowId);
            ApplyStartingItems(paths);
        }
        else
        {
            ApplyStartingItems(paths);
            ApplyRoyalCandy(paths, workflowId);
        }

        var bagHook = SwShBagHookAmxPatcher.Analyze(File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.BagEventScriptPath)));
        var slot1 = bagHook.Slots.Single(slot => slot.Slot == 1);
        var slot2 = bagHook.Slots.Single(slot => slot.Slot == 2);
        Assert.Equal(SwShBagHookInstallKind.InstalledV2, bagHook.Kind);
        Assert.Equal("occupied", slot1.Status);
        Assert.Equal(SwShBagHookAmxPatcher.RoyalCandyItemId, slot1.ItemId);
        Assert.Equal(1, slot1.Quantity);
        Assert.Equal("occupied", slot2.Status);
        Assert.Equal(50, slot2.ItemId);
        Assert.Equal(3, slot2.Quantity);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void FpsPatchMainBytesSurviveExeFsHookStack(ProjectGame game)
    {
        using var temp = CreateHookProjectWithFpsAnchors(game);
        var paths = temp.Paths with { SelectedGame = game };
        ApplyFpsPatchMain(paths);
        InstallEmptyBagHook(paths);
        ApplyCatchCap(paths);
        ApplyIvScreen(paths);
        ApplyRoyalCandy(paths, RoyalCandyUnlimitedWorkflowId);
        ApplyFashionUnlock(paths);
        ApplyHyperTrainingMain(paths, minimumLevel: 50);
        ApplyGymUniformRemoval(paths);

        var main = File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath));

        Assert.Equal(SwShFpsPatchMainKind.Installed, SwShFpsMainPatcher.Analyze(main, game).Kind);
        Assert.Equal(SwShCatchCapInstallKind.InstalledV1, SwShCatchCapMainPatcher.Analyze(main, game).Kind);
        Assert.Equal(SwShIvScreenInstallKind.InstalledV1, SwShIvScreenMainPatcher.Analyze(main, game).Kind);
        Assert.Equal(SwShFashionUnlockInstallKind.Installed, SwShFashionUnlockMainPatcher.Analyze(main, game).Kind);
        Assert.Equal(SwShHyperTrainingMainKind.CustomMinimumLevel, SwShHyperTrainingMainPatcher.Analyze(main, game).Kind);
        Assert.Equal(SwShRoyalCandyExeFsSignatureKind.Unlimited, SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(main, game).Kind);
        AssertGymUniformIpsInstalled(paths, game);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void ExeFsHookCleanupPreservesFpsPatchMainBytes(ProjectGame game)
    {
        using var temp = CreateHookProjectWithFpsAnchors(game);
        var paths = temp.Paths with { SelectedGame = game };
        ApplyFpsPatchMain(paths);
        InstallEmptyBagHook(paths);
        ApplyCatchCap(paths);
        ApplyIvScreen(paths);
        ApplyRoyalCandy(paths, RoyalCandyUnlimitedWorkflowId);
        ApplyFashionUnlock(paths);

        ApplyCatchCapCleanup(paths);
        ApplyIvScreenCleanup(paths);
        ApplyFashionUnlockCleanup(paths);
        ApplyRoyalCandyCleanup(paths);

        var main = File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath));

        Assert.Equal(SwShFpsPatchMainKind.Installed, SwShFpsMainPatcher.Analyze(main, game).Kind);
        Assert.Equal(SwShCatchCapInstallKind.NotInstalled, SwShCatchCapMainPatcher.Analyze(main, game).Kind);
        Assert.Equal(SwShIvScreenInstallKind.NotInstalled, SwShIvScreenMainPatcher.Analyze(main, game).Kind);
        Assert.Equal(SwShFashionUnlockInstallKind.NotInstalled, SwShFashionUnlockMainPatcher.Analyze(main, game).Kind);
        Assert.Equal(SwShRoyalCandyExeFsSignatureKind.NotInstalled, SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(main, game).Kind);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void CatchCapCleanupPreservesHyperTrainingMainPatch(ProjectGame game)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        ApplyHyperTrainingMain(paths, minimumLevel: 50);
        ApplyCatchCap(paths);

        ApplyCatchCapCleanup(paths);

        var main = File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath));
        var hyperTraining = SwShHyperTrainingMainPatcher.Analyze(main, game);
        Assert.Equal(SwShHyperTrainingMainKind.CustomMinimumLevel, hyperTraining.Kind);
        Assert.Equal(50, hyperTraining.MinimumLevel);
        Assert.Equal(SwShCatchCapInstallKind.NotInstalled, SwShCatchCapMainPatcher.Analyze(main).Kind);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void GymUniformRemovalPatchesOnlyOwnedHandlerBytesAndRestoresFromBase(ProjectGame game)
    {
        var baseMain = CreateSharedHookNso(game);
        var patchedMain = SwShGymUniformRemovalMainPatcher.Apply(baseMain);
        var baseText = NsoFile.Parse(baseMain).Text.DecompressedData;
        var patchedNso = NsoFile.Parse(patchedMain);
        var patchedText = patchedNso.Text.DecompressedData;
        var patchOffset = GymUniformRemovalPatchOffset(game);

        Assert.Equal(NsoFile.ComputeHash(patchedText), patchedNso.Text.Hash);
        Assert.Equal(0x320003E0u, ReadInstruction(patchedText, patchOffset));
        Assert.Equal(0xD65F03C0u, ReadInstruction(patchedText, patchOffset + 4));
        Assert.All(
            ChangedTextOffsets(baseText, patchedText),
            changedOffset => Assert.InRange(
                changedOffset,
                patchOffset,
                patchOffset + SwShGymUniformRemovalMainPatcher.PatchLength - 1));

        var restoredMain = SwShGymUniformRemovalMainPatcher.RestoreFromBase(patchedMain, baseMain);
        var restoredNso = NsoFile.Parse(restoredMain);
        Assert.Equal(NsoFile.ComputeHash(restoredNso.Text.DecompressedData), restoredNso.Text.Hash);
        Assert.Equal(baseText, restoredNso.Text.DecompressedData);
        Assert.Equal(SwShGymUniformRemovalInstallKind.NotInstalled, SwShGymUniformRemovalMainPatcher.Analyze(restoredMain).Kind);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void FashionUnlockPatchesOnlyOwnedGetterBytesAndRestoresFromBase(ProjectGame game)
    {
        var baseMain = CreateSharedHookNso(game);
        var patchedMain = SwShFashionUnlockMainPatcher.Apply(baseMain, game);
        var baseNso = NsoFile.Parse(baseMain);
        var patchedNso = NsoFile.Parse(patchedMain);
        var baseText = baseNso.Text.DecompressedData;
        var patchedText = patchedNso.Text.DecompressedData;
        var directOffset = FashionUnlockDirectGetterOffset(game);
        var mappedOffset = FashionUnlockMappedGetterOffset(game);

        Assert.Equal(NsoFile.ComputeHash(patchedText), patchedNso.Text.Hash);
        Assert.Equal(0x52800020u, ReadInstruction(patchedText, directOffset));
        Assert.Equal(0xD65F03C0u, ReadInstruction(patchedText, directOffset + 4));
        Assert.Equal(0x52800020u, ReadInstruction(patchedText, mappedOffset));
        Assert.Equal(0xD65F03C0u, ReadInstruction(patchedText, mappedOffset + 4));
        Assert.All(
            ChangedTextOffsets(baseText, patchedText),
            changedOffset => Assert.True(
                IsFashionUnlockOwnedOffset(game, changedOffset),
                $"Fashion Unlock changed unexpected .text offset 0x{changedOffset:X8}."));

        var currentNso = NsoFile.Parse(patchedMain);
        var currentText = currentNso.Text.DecompressedData.ToArray();
        var otherEditOffset = mappedOffset + 0x80;
        WriteInstruction(currentText, otherEditOffset, 0xD503201F);
        var currentWithOtherEdit = currentNso.Write(textDecompressedData: currentText);
        var restoredMain = SwShFashionUnlockMainPatcher.RestoreFromBase(currentWithOtherEdit, baseMain, game);
        var restoredNso = NsoFile.Parse(restoredMain);
        var restoredText = restoredNso.Text.DecompressedData;

        Assert.Equal(NsoFile.ComputeHash(restoredText), restoredNso.Text.Hash);
        Assert.Equal(baseText.AsSpan(directOffset, SwShFashionUnlockMainPatcher.PatchLength).ToArray(), restoredText.AsSpan(directOffset, SwShFashionUnlockMainPatcher.PatchLength).ToArray());
        Assert.Equal(baseText.AsSpan(mappedOffset, SwShFashionUnlockMainPatcher.PatchLength).ToArray(), restoredText.AsSpan(mappedOffset, SwShFashionUnlockMainPatcher.PatchLength).ToArray());
        Assert.Equal(0xD503201Fu, ReadInstruction(restoredText, otherEditOffset));
        Assert.Equal(SwShFashionUnlockInstallKind.NotInstalled, SwShFashionUnlockMainPatcher.Analyze(restoredMain, game).Kind);
    }

    [Theory]
    [InlineData(ProjectGame.Sword, ProjectGame.Shield)]
    [InlineData(ProjectGame.Shield, ProjectGame.Sword)]
    public void FashionUnlockBlocksMainBuildIdThatDoesNotMatchSelectedGame(
        ProjectGame selectedGame,
        ProjectGame actualMainGame)
    {
        var mismatchedMain = CreateSharedHookNso(actualMainGame);
        var directAnalysis = SwShFashionUnlockMainPatcher.Analyze(mismatchedMain, selectedGame);
        Assert.Equal(SwShFashionUnlockInstallKind.GameMismatch, directAnalysis.Kind);
        Assert.Throws<InvalidDataException>(() => SwShFashionUnlockMainPatcher.Apply(mismatchedMain, selectedGame));

        using var temp = CreateHookProject(selectedGame);
        temp.WriteBaseExeFsFile("main", mismatchedMain);
        var paths = temp.Paths with { SelectedGame = selectedGame };
        var project = new ProjectWorkspaceService().Open(paths);
        var workflow = new SwShFashionUnlockWorkflowService().Load(project);

        Assert.Equal("blocked", workflow.InstallStatus);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Sword and Shield use separate verified patch layouts", StringComparison.Ordinal));

        var stage = new SwShFashionUnlockEditSessionService().StageInstall(paths, session: null);
        Assert.Empty(stage.Session.PendingEdits);
        Assert.Contains(
            stage.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Sword and Shield use separate verified patch layouts", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void GymUniformRemovalCreatesReferenceIps32Patch(ProjectGame game)
    {
        var baseMain = CreateSharedHookNso(game);

        var ipsBytes = SwShGymUniformRemovalMainPatcher.CreateIpsPatch(baseMain, game);
        var analysis = SwShGymUniformRemovalMainPatcher.AnalyzeIpsPatch(ipsBytes, baseMain, game);

        Assert.Equal(SwShGymUniformRemovalInstallKind.InstalledV1, analysis.Kind);
        Assert.Equal(ExpectedGymUniformIpsBytes(game), ipsBytes);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void GymUniformRemovalRecognizesLegacyEofIpsPatchAsRefreshable(ProjectGame game)
    {
        var baseMain = CreateSharedHookNso(game);
        var legacyIpsBytes = Convert.FromHexString(game == ProjectGame.Shield
            ? "4950533332014726300008E0030032C0035FD6454F46"
            : "4950533332014726000008E0030032C0035FD6454F46");

        var analysis = SwShGymUniformRemovalMainPatcher.AnalyzeIpsPatch(legacyIpsBytes, baseMain, game);

        Assert.Equal(SwShGymUniformRemovalInstallKind.InstalledCompatible, analysis.Kind);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void GymUniformRemovalRecognizesCompatibleReturnTrueStub(ProjectGame game)
    {
        var baseNso = NsoFile.Parse(CreateSharedHookNso(game));
        var text = baseNso.Text.DecompressedData.ToArray();
        var patchOffset = GymUniformRemovalPatchOffset(game);
        WriteInstruction(text, patchOffset, 0x52800020);
        WriteInstruction(text, patchOffset + 4, 0xD65F03C0);
        var compatibleMain = baseNso.Write(textDecompressedData: text);

        var analysis = SwShGymUniformRemovalMainPatcher.Analyze(compatibleMain);
        Assert.Equal(SwShGymUniformRemovalInstallKind.InstalledCompatible, analysis.Kind);

        var refreshedText = NsoFile.Parse(SwShGymUniformRemovalMainPatcher.Apply(compatibleMain)).Text.DecompressedData;
        Assert.Equal(0x320003E0u, ReadInstruction(refreshedText, patchOffset));
        Assert.Equal(0xD65F03C0u, ReadInstruction(refreshedText, patchOffset + 4));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void GymUniformRemovalBlocksForeignHandlerBytes(ProjectGame game)
    {
        var baseNso = NsoFile.Parse(CreateSharedHookNso(game));
        var text = baseNso.Text.DecompressedData.ToArray();
        WriteInstruction(text, GymUniformRemovalPatchOffset(game), 0xD503201F);
        var foreignMain = baseNso.Write(textDecompressedData: text);

        var analysis = SwShGymUniformRemovalMainPatcher.Analyze(foreignMain);
        Assert.Equal(SwShGymUniformRemovalInstallKind.Conflict, analysis.Kind);
        Assert.Throws<InvalidDataException>(() => SwShGymUniformRemovalMainPatcher.Apply(foreignMain));
    }

    [Theory]
    [InlineData(ProjectGame.Sword, ProjectGame.Shield)]
    [InlineData(ProjectGame.Shield, ProjectGame.Sword)]
    public void GymUniformRemovalBlocksMainBuildIdThatDoesNotMatchSelectedGame(
        ProjectGame selectedGame,
        ProjectGame actualMainGame)
    {
        var mismatchedMain = CreateSharedHookNso(actualMainGame);
        var directAnalysis = SwShGymUniformRemovalMainPatcher.Analyze(mismatchedMain, selectedGame);
        Assert.Equal(SwShGymUniformRemovalInstallKind.GameMismatch, directAnalysis.Kind);
        Assert.Throws<InvalidDataException>(() => SwShGymUniformRemovalMainPatcher.Apply(mismatchedMain, selectedGame));

        using var temp = CreateHookProject(selectedGame);
        temp.WriteBaseExeFsFile("main", mismatchedMain);
        var paths = temp.Paths with { SelectedGame = selectedGame };
        var project = new ProjectWorkspaceService().Open(paths);
        var workflow = new SwShGymUniformRemovalWorkflowService().Load(project);

        Assert.Equal("blocked", workflow.InstallStatus);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Sword and Shield use different patch sites", StringComparison.Ordinal));

        var stage = new SwShGymUniformRemovalEditSessionService().StageInstall(paths, session: null);
        Assert.Empty(stage.Session.PendingEdits);
        Assert.Contains(
            stage.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Sword and Shield use different patch sites", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ProjectGame.Sword, ProjectGame.Shield)]
    [InlineData(ProjectGame.Shield, ProjectGame.Sword)]
    public void CatchCapBlocksMainBuildIdThatDoesNotMatchSelectedGame(
        ProjectGame selectedGame,
        ProjectGame actualMainGame)
    {
        var mismatchedMain = CreateSharedHookNso(actualMainGame);
        var directAnalysis = SwShCatchCapMainPatcher.Analyze(mismatchedMain, selectedGame);
        Assert.Equal(SwShCatchCapInstallKind.GameMismatch, directAnalysis.Kind);
        Assert.Throws<InvalidDataException>(() => SwShCatchCapMainPatcher.Apply(
            mismatchedMain,
            Enumerable.Range(0, SwShCatchCapMainPatcher.CapCount)
                .Select(index => index == SwShCatchCapMainPatcher.FinalBadgeCount ? 100 : 20 + index * 5)
                .ToArray(),
            selectedGame));

        using var temp = CreateHookProject(selectedGame);
        temp.WriteBaseExeFsFile("main", mismatchedMain);
        var paths = temp.Paths with { SelectedGame = selectedGame };
        var project = new ProjectWorkspaceService().Open(paths);
        var workflow = new SwShCatchCapWorkflowService().Load(project);

        Assert.Equal("blocked", workflow.InstallStatus);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Sword and Shield use different hook sites", StringComparison.Ordinal));

        var stage = new SwShCatchCapEditSessionService().StageCaps(
            paths,
            Enumerable.Range(0, SwShCatchCapMainPatcher.CapCount)
                .Select(index => new SwShCatchCapSelection(
                    index,
                    index == SwShCatchCapMainPatcher.FinalBadgeCount ? 100 : 20 + index * 5))
                .ToArray(),
            session: null);
        Assert.Empty(stage.Session.PendingEdits);
        Assert.Contains(
            stage.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData(ProjectGame.Sword, ProjectGame.Shield)]
    [InlineData(ProjectGame.Shield, ProjectGame.Sword)]
    public void IvScreenBlocksMainBuildIdThatDoesNotMatchSelectedGame(
        ProjectGame selectedGame,
        ProjectGame actualMainGame)
    {
        var mismatchedMain = CreateSharedHookNso(actualMainGame);
        var directAnalysis = SwShIvScreenMainPatcher.Analyze(mismatchedMain, selectedGame);
        Assert.Equal(SwShIvScreenInstallKind.GameMismatch, directAnalysis.Kind);
        Assert.Throws<InvalidDataException>(() => SwShIvScreenMainPatcher.Apply(mismatchedMain, selectedGame));

        using var temp = CreateHookProject(selectedGame);
        temp.WriteBaseExeFsFile("main", mismatchedMain);
        var paths = temp.Paths with { SelectedGame = selectedGame };
        var project = new ProjectWorkspaceService().Open(paths);
        var workflow = new SwShIvScreenWorkflowService().Load(project);

        Assert.Equal("blocked", workflow.InstallStatus);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Sword and Shield use different Pokemon Summary hook sites", StringComparison.Ordinal));

        var stage = new SwShIvScreenEditSessionService().StageInstall(paths, session: null);
        Assert.Empty(stage.Session.PendingEdits);
        Assert.Contains(
            stage.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("foreign or conflicting Pokemon Summary hook", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ProjectGame.Sword, ProjectGame.Shield)]
    [InlineData(ProjectGame.Shield, ProjectGame.Sword)]
    public void RoyalCandyBlocksMainBuildIdThatDoesNotMatchSelectedGame(
        ProjectGame selectedGame,
        ProjectGame actualMainGame)
    {
        var mismatchedMain = CreateSharedHookNso(actualMainGame);
        var directAnalysis = SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(mismatchedMain, selectedGame);
        Assert.Equal(SwShRoyalCandyExeFsSignatureKind.GameMismatch, directAnalysis.Kind);
        Assert.Throws<InvalidDataException>(() => SwShExeFsRoyalCandyMainPatcher.ApplyBasePatch(mismatchedMain, selectedGame));

        using var temp = CreateHookProject(selectedGame);
        temp.WriteBaseExeFsFile("main", mismatchedMain);
        var paths = temp.Paths with { SelectedGame = selectedGame };
        InstallEmptyBagHook(paths);
        var project = new ProjectWorkspaceService().Open(paths);
        var workflow = new SwShRoyalCandyWorkflowService().Load(project);

        Assert.Contains(
            workflow.Checks,
            check => check.Status == "Fail"
                && check.Message.Contains("will not patch a different game's executable", StringComparison.Ordinal));

        var stage = new SwShRoyalCandyEditSessionService().StageWorkflow(
            paths,
            RoyalCandyUnlimitedWorkflowId,
            levelCaps: null,
            session: null);
        Assert.Empty(stage.Session.PendingEdits);
        Assert.Contains(
            stage.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("will not patch a different game's executable", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ProjectGame.Sword, 0x014114C0, 0x01410F00)]
    [InlineData(ProjectGame.Shield, 0x014114F0, 0x01410F30)]
    public void RoyalCandyStoryLimitsUsesGameSpecificProgressAccessors(
        ProjectGame game,
        int expectedWorkGetOffset,
        int expectedFlagGetOffset)
    {
        var patched = SwShExeFsRoyalCandyMainPatcher.ApplyStoryLimitsPatch(
            CreateSharedHookNso(game),
            [
                new SwShRoyalCandyStoryLevelCap(
                    35,
                    0x123456789ABCDEF0UL,
                    "Work milestone",
                    SwShRoyalCandyStoryLevelCapProgressKind.WorkAtLeast,
                    WorkMinimum: 530),
                new SwShRoyalCandyStoryLevelCap(
                    20,
                    0x0FEDCBA987654321UL,
                    "Flag milestone"),
            ],
            game);
        var text = NsoFile.Parse(patched).Text.DecompressedData;

        var accessorTargets = ReadRoyalCandyStoryAccessorTargets(text, expectedCount: 2);

        Assert.Equal([expectedWorkGetOffset, expectedFlagGetOffset], accessorTargets);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void RoyalCandyStoryLimitsRestoreClearsOwnedCaveGraphAndReturnsToBase(ProjectGame game)
    {
        var baseMain = CreateSharedHookNso(game);
        var patchedMain = SwShExeFsRoyalCandyMainPatcher.ApplyStoryLimitsPatch(
            baseMain,
            [
                new SwShRoyalCandyStoryLevelCap(
                    35,
                    0x123456789ABCDEF0UL,
                    "Work milestone",
                    SwShRoyalCandyStoryLevelCapProgressKind.WorkAtLeast,
                    WorkMinimum: 530),
                new SwShRoyalCandyStoryLevelCap(
                    20,
                    0x0FEDCBA987654321UL,
                    "Flag milestone"),
            ],
            game);
        var baseNso = NsoFile.Parse(baseMain);
        var patchedText = NsoFile.Parse(patchedMain).Text.DecompressedData;
        var storyCaveGraph = ReadRoyalCandyStoryCaveGraph(
            patchedText,
            baseNso.Text.DecompressedData);

        var restoredMain = SwShExeFsRoyalCandyMainPatcher.RestoreFromBase(
            patchedMain,
            baseMain,
            game);
        var restoredNso = NsoFile.Parse(restoredMain);

        Assert.True(storyCaveGraph.Length > 4);
        Assert.Contains(0x007BB20C, storyCaveGraph);
        Assert.Contains(0x007BB3C8, storyCaveGraph);
        Assert.All(
            storyCaveGraph,
            offset => Assert.Equal(
                baseNso.Text.DecompressedData.AsSpan(offset, 0x0C).ToArray(),
                restoredNso.Text.DecompressedData.AsSpan(offset, 0x0C).ToArray()));
        Assert.Equal(baseNso.BuildId, restoredNso.BuildId);
        Assert.Equal(baseNso.Text.DecompressedData, restoredNso.Text.DecompressedData);
        Assert.Equal(baseNso.Ro.DecompressedData, restoredNso.Ro.DecompressedData);
        Assert.Equal(baseNso.Data.DecompressedData, restoredNso.Data.DecompressedData);
        Assert.Equal(
            SwShRoyalCandyExeFsSignatureKind.NotInstalled,
            SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(restoredMain, game).Kind);
    }

    [Theory]
    [MemberData(nameof(RoyalCandyBuildVariants))]
    public void RoyalCandyRestorePreservesUnusedOtherGameVirtualInventoryOffsets(
        ProjectGame game,
        string workflowId)
    {
        var baseMain = CreateSharedHookNso(game);
        var patchedMain = ApplyRoyalCandyMainPatch(baseMain, workflowId, game);
        var patchedNso = NsoFile.Parse(patchedMain);
        var currentText = patchedNso.Text.DecompressedData.ToArray();
        var unusedOwnershipOffset = game == ProjectGame.Shield ? 0x01420EF0 : 0x01420F20;
        var unusedCountOffset = game == ProjectGame.Shield ? 0x01421090 : 0x014210C0;
        const uint unrelatedOwnershipEdit = 0xD503201F;
        const uint unrelatedCountEdit = 0xD65F03C0;
        WriteInstruction(currentText, unusedOwnershipOffset, unrelatedOwnershipEdit);
        WriteInstruction(currentText, unusedCountOffset, unrelatedCountEdit);
        var currentMain = patchedNso.Write(textDecompressedData: currentText);

        Assert.Equal(
            ExpectedRoyalCandySignature(workflowId),
            SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(currentMain, game).Kind);

        var restoredMain = SwShExeFsRoyalCandyMainPatcher.RestoreFromBase(currentMain, baseMain, game);
        var restoredText = NsoFile.Parse(restoredMain).Text.DecompressedData;

        Assert.Equal(unrelatedOwnershipEdit, ReadInstruction(restoredText, unusedOwnershipOffset));
        Assert.Equal(unrelatedCountEdit, ReadInstruction(restoredText, unusedCountOffset));
        Assert.Equal(
            SwShRoyalCandyExeFsSignatureKind.NotInstalled,
            SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(restoredMain, game).Kind);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void RoyalCandyStoryRestoreUsesExactEightByteCaveLength(ProjectGame game)
    {
        const int caveSearchStart = 0x007BC338;
        const int isolatedEightByteCave = caveSearchStart + 0x10;
        const uint unrelatedAdjacentEdit = 0x52800020;

        var originalBaseNso = NsoFile.Parse(CreateSharedHookNso(game));
        var baseText = originalBaseNso.Text.DecompressedData.ToArray();
        for (var offset = caveSearchStart; offset < caveSearchStart + 0x40; offset += sizeof(uint))
        {
            WriteInstruction(baseText, offset, EncodeNop());
        }

        WriteInstruction(baseText, isolatedEightByteCave, 0);
        WriteInstruction(baseText, isolatedEightByteCave + sizeof(uint), 0);
        var baseMain = originalBaseNso.Write(textDecompressedData: baseText);
        var patchedMain = SwShExeFsRoyalCandyMainPatcher.ApplyStoryLimitsPatch(
            baseMain,
            CreateRoyalCandyTestStoryCaps(),
            game);
        var patchedNso = NsoFile.Parse(patchedMain);
        var currentText = patchedNso.Text.DecompressedData.ToArray();

        Assert.NotEqual(
            baseText.AsSpan(isolatedEightByteCave, 0x08).ToArray(),
            currentText.AsSpan(isolatedEightByteCave, 0x08).ToArray());
        WriteInstruction(currentText, isolatedEightByteCave + 0x08, unrelatedAdjacentEdit);
        var currentMain = patchedNso.Write(textDecompressedData: currentText);
        Assert.Equal(
            SwShRoyalCandyExeFsSignatureKind.StoryLimits,
            SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(currentMain, game).Kind);

        var restoredMain = SwShExeFsRoyalCandyMainPatcher.RestoreFromBase(currentMain, baseMain, game);
        var restoredText = NsoFile.Parse(restoredMain).Text.DecompressedData;

        Assert.Equal(
            baseText.AsSpan(isolatedEightByteCave, 0x08).ToArray(),
            restoredText.AsSpan(isolatedEightByteCave, 0x08).ToArray());
        Assert.Equal(unrelatedAdjacentEdit, ReadInstruction(restoredText, isolatedEightByteCave + 0x08));
        Assert.Equal(
            SwShRoyalCandyExeFsSignatureKind.NotInstalled,
            SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(restoredMain, game).Kind);
    }

    [Theory]
    [MemberData(nameof(RoyalCandyBuildVariants))]
    public void RoyalCandyPatchesSharedAllowedConsumableRoute(ProjectGame game, string workflowId)
    {
        var baseMain = CreateSharedHookNso(game);
        var patched = workflowId == RoyalCandyStoryLimitsWorkflowId
            ? SwShExeFsRoyalCandyMainPatcher.ApplyStoryLimitsPatch(
                baseMain,
                [
                    new SwShRoyalCandyStoryLevelCap(
                        35,
                        0x123456789ABCDEF0UL,
                        "Flag milestone"),
                ],
                game)
            : SwShExeFsRoyalCandyMainPatcher.ApplyBasePatch(baseMain, game);
        var text = NsoFile.Parse(patched).Text.DecompressedData;

        const int branchOffset = 0x007DDA90;
        var branchInstruction = ReadInstruction(text, branchOffset);
        Assert.Equal(0x54000000u, branchInstruction & 0xFF000010u);
        Assert.Equal((uint)Arm64Condition.HI, branchInstruction & 0xFu);

        var caveOffset = DecodeConditionalBranchTarget(branchInstruction, branchOffset);
        Assert.Equal(EncodeCmpImmediate(8, 1128 - 0x12), ReadInstruction(text, caveOffset));
        Assert.Equal(EncodeConditionalBranch(caveOffset + 4, 0x007DDA48, Arm64Condition.EQ), ReadInstruction(text, caveOffset + 4));
        Assert.Equal(EncodeBranch(caveOffset + 8, 0x007DDAF8), ReadInstruction(text, caveOffset + 8));
    }

    [Theory]
    [MemberData(nameof(RoyalCandyBuildVariants))]
    public void RoyalCandyPatchesVirtualInventoryHelpers(ProjectGame game, string workflowId)
    {
        var baseMain = CreateSharedHookNso(game);
        var patched = workflowId == RoyalCandyStoryLimitsWorkflowId
            ? SwShExeFsRoyalCandyMainPatcher.ApplyStoryLimitsPatch(
                baseMain,
                [
                    new SwShRoyalCandyStoryLevelCap(
                        35,
                        0x123456789ABCDEF0UL,
                        "Flag milestone"),
                ],
                game)
            : SwShExeFsRoyalCandyMainPatcher.ApplyBasePatch(baseMain, game);
        var text = NsoFile.Parse(patched).Text.DecompressedData;
        var ownershipOffset = game == ProjectGame.Shield ? 0x01420F20 : 0x01420EF0;
        var countOffset = game == ProjectGame.Shield ? 0x014210C0 : 0x01421090;

        AssertRoyalCandyVirtualInventoryHelper(text, ownershipOffset, 0xF81D0FF5, 1);
        AssertRoyalCandyVirtualInventoryHelper(text, countOffset, 0xA9BE4FF4, 999);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void RoyalCandyStoryLimitsRejectsRequiredTextHashMismatch(ProjectGame game)
    {
        var main = CreateSharedHookNso(game);
        BinaryPrimitives.WriteUInt32LittleEndian(main.AsSpan(0x0C, sizeof(uint)), (uint)NsoFlags.CheckHashText);
        main[0xA0] ^= 0xFF;

        Assert.Throws<InvalidDataException>(() => SwShExeFsRoyalCandyMainPatcher.ApplyStoryLimitsPatch(
            main,
            CreateRoyalCandyTestStoryCaps(),
            game));
    }

    [Fact]
    public void RoyalCandyFailsClosedOnUnsupportedBuildWithoutSelectedGame()
    {
        var main = CreateSharedHookNso();
        main[0x40] ^= 0xFF;

        Assert.Equal(
            SwShRoyalCandyExeFsSignatureKind.ForeignPatch,
            SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(main).Kind);
        Assert.Throws<InvalidDataException>(() => SwShExeFsRoyalCandyMainPatcher.ApplyBasePatch(main));
        Assert.Throws<InvalidDataException>(() => SwShExeFsRoyalCandyMainPatcher.ApplyStoryLimitsPatch(
            main,
            CreateRoyalCandyTestStoryCaps()));
        Assert.Throws<InvalidDataException>(() => SwShExeFsRoyalCandyMainPatcher.ReadInstalledStoryLevelCaps(main));
    }

    [Theory]
    [InlineData(ProjectGame.Sword, RoyalCandyUnlimitedWorkflowId, false)]
    [InlineData(ProjectGame.Shield, RoyalCandyUnlimitedWorkflowId, true)]
    [InlineData(ProjectGame.Sword, RoyalCandyStoryLimitsWorkflowId, true)]
    [InlineData(ProjectGame.Shield, RoyalCandyStoryLimitsWorkflowId, false)]
    public void RoyalCandyAnalysisAndRestoreRejectMalformedOwnedPayloads(
        ProjectGame game,
        string workflowId,
        bool corruptVirtualInventory)
    {
        var baseMain = CreateSharedHookNso(game);
        var patchedMain = ApplyRoyalCandyMainPatch(baseMain, workflowId, game);
        var patchedNso = NsoFile.Parse(patchedMain);
        var text = patchedNso.Text.DecompressedData.ToArray();
        if (corruptVirtualInventory)
        {
            var ownershipOffset = game == ProjectGame.Shield ? 0x01420F20 : 0x01420EF0;
            var dispatchOffset = DecodeBranchTarget(ReadInstruction(text, ownershipOffset), ownershipOffset);
            WriteInstruction(text, dispatchOffset, EncodeNop());
        }
        else
        {
            const int allowedConsumableBranchOffset = 0x007DDA90;
            var caveOffset = DecodeConditionalBranchTarget(
                ReadInstruction(text, allowedConsumableBranchOffset),
                allowedConsumableBranchOffset);
            WriteInstruction(text, caveOffset + 8, EncodeNop());
        }

        var malformedMain = patchedNso.Write(textDecompressedData: text);

        Assert.Equal(
            SwShRoyalCandyExeFsSignatureKind.ForeignPatch,
            SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(malformedMain, game).Kind);
        Assert.Throws<InvalidDataException>(() => SwShExeFsRoyalCandyMainPatcher.RestoreFromBase(
            malformedMain,
            baseMain,
            game));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void RoyalCandyRestoreRejectsNotInstalledAndGameMismatchStates(ProjectGame game)
    {
        var baseMain = CreateSharedHookNso(game);
        var patchedMain = SwShExeFsRoyalCandyMainPatcher.ApplyBasePatch(baseMain, game);
        var otherGame = game == ProjectGame.Sword ? ProjectGame.Shield : ProjectGame.Sword;

        Assert.Throws<InvalidDataException>(() => SwShExeFsRoyalCandyMainPatcher.RestoreFromBase(
            baseMain,
            baseMain,
            game));
        Assert.Throws<InvalidDataException>(() => SwShExeFsRoyalCandyMainPatcher.RestoreFromBase(
            patchedMain,
            baseMain,
            otherGame));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void RoyalCandyStoryVerifierRequiresExactRequestedLadder(ProjectGame game)
    {
        var baseMain = CreateSharedHookNso(game);
        var levelCaps = CreateRoyalCandyTestStoryCaps();
        var patchedMain = SwShExeFsRoyalCandyMainPatcher.ApplyStoryLimitsPatch(baseMain, levelCaps, game);

        SwShExeFsRoyalCandyMainPatcher.VerifyStoryLimitsPatchOutput(
            baseMain,
            patchedMain,
            levelCaps,
            game);
        var installed = SwShExeFsRoyalCandyMainPatcher.ReadInstalledStoryLevelCaps(patchedMain, game);
        Assert.Equal([35, 20], installed.Select(levelCap => levelCap.LevelCap));
        Assert.Equal(
            [0x123456789ABCDEF0UL, 0x0FEDCBA987654321UL],
            installed.Select(levelCap => levelCap.ProgressHash));

        var differentLevelCaps = levelCaps
            .Select((levelCap, index) => index == 0 ? levelCap with { LevelCap = levelCap.LevelCap + 1 } : levelCap)
            .ToArray();
        Assert.Throws<InvalidDataException>(() => SwShExeFsRoyalCandyMainPatcher.VerifyStoryLimitsPatchOutput(
            baseMain,
            patchedMain,
            differentLevelCaps,
            game));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void RoyalCandyStoryLimitsRejectsDuplicateMilestones(ProjectGame game)
    {
        var duplicate = new SwShRoyalCandyStoryLevelCap(
            40,
            0x123456789ABCDEF0UL,
            "Duplicate work milestone",
            SwShRoyalCandyStoryLevelCapProgressKind.WorkAtLeast,
            WorkMinimum: 530);

        Assert.Throws<InvalidDataException>(() => SwShExeFsRoyalCandyMainPatcher.ApplyStoryLimitsPatch(
            CreateSharedHookNso(game),
            [.. CreateRoyalCandyTestStoryCaps(), duplicate],
            game));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void RoyalCandyStoryReadbackRejectsPartialPayloadGraph(ProjectGame game)
    {
        var baseMain = CreateSharedHookNso(game);
        var patchedMain = SwShExeFsRoyalCandyMainPatcher.ApplyStoryLimitsPatch(
            baseMain,
            CreateRoyalCandyTestStoryCaps(),
            game);
        var patchedNso = NsoFile.Parse(patchedMain);
        var text = patchedNso.Text.DecompressedData.ToArray();
        var helperOffset = ResolveRoyalCandyStoryHelperOffset(text);
        WriteInstruction(text, helperOffset + 8, EncodeNop());
        var malformedMain = patchedNso.Write(textDecompressedData: text);

        Assert.Equal(
            SwShRoyalCandyExeFsSignatureKind.ForeignPatch,
            SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(malformedMain, game).Kind);
        Assert.Throws<InvalidDataException>(() =>
            SwShExeFsRoyalCandyMainPatcher.ReadInstalledStoryLevelCaps(malformedMain, game));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void RoyalCandyStoryVerifierRejectsOtherOwnerAndPreservedSegmentChanges(ProjectGame game)
    {
        var baseMain = CreateSharedHookNso(game);
        var levelCaps = CreateRoyalCandyTestStoryCaps();
        var patchedMain = SwShExeFsRoyalCandyMainPatcher.ApplyStoryLimitsPatch(baseMain, levelCaps, game);
        var patchedNso = NsoFile.Parse(patchedMain);

        var otherOwnerText = patchedNso.Text.DecompressedData.ToArray();
        var catchCapHookOffset = game == ProjectGame.Shield
            ? SwShCatchCapMainPatcher.ShieldExeFsHookSiteOffset
            : SwShCatchCapMainPatcher.ExeFsHookSiteOffset;
        WriteInstruction(otherOwnerText, catchCapHookOffset, EncodeNop());
        var changedOtherOwner = patchedNso.Write(textDecompressedData: otherOwnerText);
        Assert.Throws<InvalidDataException>(() => SwShExeFsRoyalCandyMainPatcher.VerifyStoryLimitsPatchOutput(
            baseMain,
            changedOtherOwner,
            levelCaps,
            game));

        var changedRo = patchedNso.Ro.DecompressedData.ToArray();
        changedRo[0] ^= 0xFF;
        var changedPreservedSegment = patchedNso.Write(roDecompressedData: changedRo);
        Assert.Throws<InvalidDataException>(() => SwShExeFsRoyalCandyMainPatcher.VerifyStoryLimitsPatchOutput(
            baseMain,
            changedPreservedSegment,
            levelCaps,
            game));
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void RoyalCandyCanRefreshInstalledWorkflowWithoutDroppingOtherMainHooks(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyCatchCap(paths);
        ApplyIvScreen(paths);
        ApplyRoyalCandy(paths, workflowId);

        ApplyRoyalCandy(paths, workflowId);

        var main = File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath));
        Assert.Equal(SwShCatchCapInstallKind.InstalledV1, SwShCatchCapMainPatcher.Analyze(main).Kind);
        Assert.Equal(SwShIvScreenInstallKind.InstalledV1, SwShIvScreenMainPatcher.Analyze(main).Kind);
        Assert.Equal(ExpectedRoyalCandySignature(workflowId), SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(main).Kind);
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void RoyalCandyCleanupPreservesBagHookStartingItemsCatchCapAndIvScreen(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyStartingItems(paths);
        ApplyCatchCap(paths);
        ApplyIvScreen(paths);
        ApplyGymUniformRemoval(paths);
        ApplyRoyalCandy(paths, workflowId);

        ApplyRoyalCandyCleanup(paths);

        var bagHook = SwShBagHookAmxPatcher.Analyze(File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.BagEventScriptPath)));
        var slot1 = bagHook.Slots.Single(slot => slot.Slot == 1);
        var slot2 = bagHook.Slots.Single(slot => slot.Slot == 2);
        Assert.Equal(SwShBagHookInstallKind.InstalledV2, bagHook.Kind);
        Assert.Equal("empty", slot1.Status);
        Assert.Null(slot1.ItemId);
        Assert.Equal("occupied", slot2.Status);
        Assert.Equal(50, slot2.ItemId);
        Assert.Equal(3, slot2.Quantity);

        var main = File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath));
        Assert.Equal(SwShCatchCapInstallKind.InstalledV1, SwShCatchCapMainPatcher.Analyze(main).Kind);
        Assert.Equal(SwShIvScreenInstallKind.InstalledV1, SwShIvScreenMainPatcher.Analyze(main).Kind);
        AssertGymUniformIpsInstalled(paths, game);
        Assert.Equal(SwShRoyalCandyExeFsSignatureKind.NotInstalled, SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(main).Kind);
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void RoyalCandyCleanupRemovesExeFsOutputWhenNoIndependentExeFsModRemains(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyRoyalCandy(paths, workflowId);

        ApplyRoyalCandyCleanup(paths);

        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath)));
        var bagHook = SwShBagHookAmxPatcher.Analyze(File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.BagEventScriptPath)));
        Assert.Equal(SwShBagHookInstallKind.InstalledV2, bagHook.Kind);
        Assert.Null(bagHook.Slots.Single(slot => slot.Slot == 1).ItemId);
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void BagHookCleanupRemovesBagScriptAndRoyalCandyExeFsAndPreservesCatchCap(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyStartingItems(paths);
        ApplyCatchCap(paths);
        ApplyRoyalCandy(paths, workflowId);

        ApplyBagHookCleanup(paths);

        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.BagEventScriptPath)));
        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ItemPath)));
        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ShopDataPath)));
        Assert.False(File.Exists(OutputPath(paths, "romfs/bin/message/English/common/itemname.dat")));
        Assert.False(File.Exists(OutputPath(paths, "romfs/bin/message/English/common/itemname_plural.dat")));
        Assert.False(File.Exists(OutputPath(paths, "romfs/bin/message/English/common/iteminfo.dat")));

        var main = File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath));
        Assert.Equal(SwShCatchCapInstallKind.InstalledV1, SwShCatchCapMainPatcher.Analyze(main).Kind);
        Assert.Equal(SwShRoyalCandyExeFsSignatureKind.NotInstalled, SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(main).Kind);
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void BagHookCleanupRemovesBagScriptAndRoyalCandyExeFsAndPreservesIvScreen(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyRoyalCandy(paths, workflowId);
        ApplyIvScreen(paths);

        ApplyBagHookCleanup(paths);

        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.BagEventScriptPath)));
        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ItemPath)));
        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ShopDataPath)));
        Assert.False(File.Exists(OutputPath(paths, "romfs/bin/message/English/common/itemname.dat")));
        Assert.False(File.Exists(OutputPath(paths, "romfs/bin/message/English/common/itemname_plural.dat")));
        Assert.False(File.Exists(OutputPath(paths, "romfs/bin/message/English/common/iteminfo.dat")));

        var main = File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath));
        Assert.Equal(SwShIvScreenInstallKind.InstalledV1, SwShIvScreenMainPatcher.Analyze(main).Kind);
        Assert.Equal(SwShRoyalCandyExeFsSignatureKind.NotInstalled, SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(main).Kind);
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void BagHookCleanupRemovesRoyalCandyExeFsAndPreservesGymUniformRemoval(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyRoyalCandy(paths, workflowId);
        ApplyGymUniformRemoval(paths);

        ApplyBagHookCleanup(paths);

        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.BagEventScriptPath)));
        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath)));
        AssertGymUniformIpsInstalled(paths, game);
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void CatchCapCleanupPreservesBagHookStartingItemsRoyalCandyAndIvScreen(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyStartingItems(paths);
        ApplyRoyalCandy(paths, workflowId);
        ApplyIvScreen(paths);
        ApplyGymUniformRemoval(paths);
        ApplyCatchCap(paths);

        ApplyCatchCapCleanup(paths);

        var bagHook = SwShBagHookAmxPatcher.Analyze(File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.BagEventScriptPath)));
        var slot1 = bagHook.Slots.Single(slot => slot.Slot == 1);
        var slot2 = bagHook.Slots.Single(slot => slot.Slot == 2);
        Assert.Equal(SwShBagHookInstallKind.InstalledV2, bagHook.Kind);
        Assert.Equal("occupied", slot1.Status);
        Assert.Equal(1128, slot1.ItemId);
        Assert.Equal(1, slot1.Quantity);
        Assert.Equal("occupied", slot2.Status);
        Assert.Equal(50, slot2.ItemId);
        Assert.Equal(3, slot2.Quantity);

        var main = File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath));
        Assert.Equal(SwShCatchCapInstallKind.NotInstalled, SwShCatchCapMainPatcher.Analyze(main).Kind);
        Assert.Equal(SwShIvScreenInstallKind.InstalledV1, SwShIvScreenMainPatcher.Analyze(main).Kind);
        AssertGymUniformIpsInstalled(paths, game);
        Assert.Equal(ExpectedRoyalCandySignature(workflowId), SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(main).Kind);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void CatchCapCleanupRemovesExeFsOutputWhenNoOtherExeFsHookRemains(ProjectGame game)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        ApplyCatchCap(paths);

        ApplyCatchCapCleanup(paths);

        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath)));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void CatchCapCleanupPreservesUnrecognizedMainEdits(ProjectGame game)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        ApplyCatchCap(paths);
        WriteUnownedMainEdit(paths);

        ApplyCatchCapCleanup(paths);

        AssertUnownedMainEditPreserved(paths);
        Assert.Equal(
            SwShCatchCapInstallKind.NotInstalled,
            SwShCatchCapMainPatcher.Analyze(File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath)), game).Kind);
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void IvScreenCleanupPreservesCatchCapAndRoyalCandy(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyCatchCap(paths);
        ApplyRoyalCandy(paths, workflowId);
        ApplyGymUniformRemoval(paths);
        ApplyIvScreen(paths);

        ApplyIvScreenCleanup(paths);

        var main = File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath));
        Assert.Equal(SwShIvScreenInstallKind.NotInstalled, SwShIvScreenMainPatcher.Analyze(main).Kind);
        Assert.Equal(SwShCatchCapInstallKind.InstalledV1, SwShCatchCapMainPatcher.Analyze(main).Kind);
        AssertGymUniformIpsInstalled(paths, game);
        Assert.Equal(ExpectedRoyalCandySignature(workflowId), SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(main).Kind);
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void GymUniformRemovalCleanupPreservesCatchCapIvScreenAndRoyalCandy(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyCatchCap(paths);
        ApplyIvScreen(paths);
        ApplyRoyalCandy(paths, workflowId);
        ApplyGymUniformRemoval(paths);

        ApplyGymUniformRemovalCleanup(paths);

        var main = File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath));
        Assert.False(File.Exists(OutputPath(paths, SwShGymUniformRemovalMainPatcher.IpsRelativePath(game))));
        Assert.Equal(SwShCatchCapInstallKind.InstalledV1, SwShCatchCapMainPatcher.Analyze(main).Kind);
        Assert.Equal(SwShIvScreenInstallKind.InstalledV1, SwShIvScreenMainPatcher.Analyze(main).Kind);
        Assert.Equal(ExpectedRoyalCandySignature(workflowId), SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(main).Kind);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void GymUniformRemovalCleanupRemovesExeFsOutputWhenNoOtherExeFsHookRemains(ProjectGame game)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        ApplyGymUniformRemoval(paths);

        ApplyGymUniformRemovalCleanup(paths);

        Assert.False(File.Exists(OutputPath(paths, SwShGymUniformRemovalMainPatcher.IpsRelativePath(game))));
        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath)));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void FashionUnlockCleanupPreservesOtherGeneratedMainEdits(ProjectGame game)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        ApplyFashionUnlock(paths);

        var outputMainPath = OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath);
        var mainWithOtherEdit = NsoFile.Parse(File.ReadAllBytes(outputMainPath));
        var textWithOtherEdit = mainWithOtherEdit.Text.DecompressedData.ToArray();
        var otherEditOffset = FashionUnlockMappedGetterOffset(game) + 0x80;
        WriteInstruction(textWithOtherEdit, otherEditOffset, 0xD503201F);
        File.WriteAllBytes(outputMainPath, mainWithOtherEdit.Write(textDecompressedData: textWithOtherEdit));

        ApplyFashionUnlockCleanup(paths);

        Assert.True(File.Exists(outputMainPath));
        var restoredText = NsoFile.Parse(File.ReadAllBytes(outputMainPath)).Text.DecompressedData;
        Assert.Equal(SwShFashionUnlockInstallKind.NotInstalled, SwShFashionUnlockMainPatcher.Analyze(File.ReadAllBytes(outputMainPath), game).Kind);
        Assert.Equal(0xD503201Fu, ReadInstruction(restoredText, otherEditOffset));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void FashionUnlockCleanupRemovesExeFsOutputWhenNoOtherMainEditRemains(ProjectGame game)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        ApplyFashionUnlock(paths);

        ApplyFashionUnlockCleanup(paths);

        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath)));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void IvScreenCleanupRemovesExeFsOutputWhenNoOtherExeFsHookRemains(ProjectGame game)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        ApplyIvScreen(paths);

        ApplyIvScreenCleanup(paths);

        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath)));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void IvScreenCleanupPreservesUnrecognizedMainEdits(ProjectGame game)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        ApplyIvScreen(paths);
        WriteUnownedMainEdit(paths);

        ApplyIvScreenCleanup(paths);

        AssertUnownedMainEditPreserved(paths);
        Assert.Equal(
            SwShIvScreenInstallKind.NotInstalled,
            SwShIvScreenMainPatcher.Analyze(File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath)), game).Kind);
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void RoyalCandyCleanupPreservesUnrecognizedMainEdits(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyRoyalCandy(paths, workflowId);
        WriteUnownedMainEdit(paths);

        ApplyRoyalCandyCleanup(paths);

        AssertUnownedMainEditPreserved(paths);
        Assert.Equal(
            SwShRoyalCandyExeFsSignatureKind.NotInstalled,
            SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(
                File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath)),
                game).Kind);
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void BagHookCleanupRemovesOwnedRoyalCandyOutputsWhenRoyalCandyWasOnlyExeFsMod(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyRoyalCandy(paths, workflowId);

        ApplyBagHookCleanup(paths);

        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.BagEventScriptPath)));
        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath)));
        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ItemPath)));
        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ShopDataPath)));
        Assert.False(File.Exists(OutputPath(paths, "romfs/bin/message/English/common/itemname.dat")));
        Assert.False(File.Exists(OutputPath(paths, "romfs/bin/message/English/common/itemname_plural.dat")));
        Assert.False(File.Exists(OutputPath(paths, "romfs/bin/message/English/common/iteminfo.dat")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BagHookCleanupRejectsRoyalCandySourceDriftAfterReview(bool mutateBaseSource)
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        InstallEmptyBagHook(paths);
        ApplyRoyalCandy(paths, RoyalCandyUnlimitedWorkflowId);

        var service = new SwShBagHookEditSessionService();
        var stage = service.StageUninstall(paths, session: null);
        var plan = service.CreateChangePlan(paths, stage.Session);
        Assert.True(plan.CanApply);
        Assert.All(plan.Writes, write => Assert.False(string.IsNullOrWhiteSpace(write.SourceFingerprint)));
        Assert.Contains(
            plan.Writes.Single(write => write.TargetRelativePath == SwShRoyalCandyWorkflowService.ItemPath).Sources,
            source => source.Layer == KM.Core.Files.ProjectFileLayer.Base
                && source.RelativePath == SwShRoyalCandyWorkflowService.ItemPath);

        if (mutateBaseSource)
        {
            var baseItemPath = BasePath(paths, SwShRoyalCandyWorkflowService.ItemPath);
            var editedBase = SwShItemTable.Parse(File.ReadAllBytes(baseItemPath)).WriteEdits(
                [new SwShItemTableEdit(1, SwShItemTableField.BuyPrice, 777)]);
            File.WriteAllBytes(baseItemPath, editedBase);
        }
        else
        {
            WriteLayeredTextEdit(
                paths,
                "romfs/bin/message/English/common/itemname.dat",
                lineIndex: 10,
                text: "Changed after review");
        }

        var outputsBeforeApply = plan.Writes.ToDictionary(
            write => write.TargetRelativePath,
            write => File.ReadAllBytes(OutputPath(paths, write.TargetRelativePath)),
            StringComparer.OrdinalIgnoreCase);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        foreach (var (relativePath, expectedBytes) in outputsBeforeApply)
        {
            Assert.Equal(expectedBytes, File.ReadAllBytes(OutputPath(paths, relativePath)));
        }
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void BagHookCleanupPreservesUnownedRoyalCandyCandidateRomFsFiles(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyRoyalCandy(paths, workflowId);
        var candidateFiles = WriteUnownedRoyalCandyCandidateRomFsFiles(temp);

        ApplyBagHookCleanup(paths);

        AssertUnownedRoyalCandyCandidateRomFsFiles(paths, candidateFiles);
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void RoyalCandyCleanupPreservesUnownedRoyalCandyCandidateRomFsFiles(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyRoyalCandy(paths, workflowId);
        var candidateFiles = WriteUnownedRoyalCandyCandidateRomFsFiles(temp);

        ApplyRoyalCandyCleanup(paths);

        AssertUnownedRoyalCandyCandidateRomFsFiles(paths, candidateFiles);
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void RoyalCandyCleanupRestoresOwnedTextRowsAndPreservesOtherLayeredTextEdits(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyRoyalCandy(paths, workflowId);
        WriteLayeredTextEdit(paths, "romfs/bin/message/English/common/itemname.dat", 10, "User-edited item name");
        WriteLayeredTextEdit(paths, "romfs/bin/message/English/common/itemname_plural.dat", 10, "User-edited item plural name");
        WriteLayeredTextEdit(paths, "romfs/bin/message/English/common/iteminfo.dat", 10, "User-edited item info");

        ApplyRoyalCandyCleanup(paths);

        AssertRestoredRoyalCandyTextRow(paths, "romfs/bin/message/English/common/itemname.dat", 10, "User-edited item name");
        AssertRestoredRoyalCandyTextRow(paths, "romfs/bin/message/English/common/itemname_plural.dat", 10, "User-edited item plural name");
        AssertRestoredRoyalCandyTextRow(paths, "romfs/bin/message/English/common/iteminfo.dat", 10, "User-edited item info");
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void BagHookCleanupRestoresOwnedRoyalCandyTextRowsAndPreservesOtherLayeredTextEdits(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyRoyalCandy(paths, workflowId);
        WriteLayeredTextEdit(paths, "romfs/bin/message/English/common/itemname.dat", 10, "User-edited item name");
        WriteLayeredTextEdit(paths, "romfs/bin/message/English/common/itemname_plural.dat", 10, "User-edited item plural name");
        WriteLayeredTextEdit(paths, "romfs/bin/message/English/common/iteminfo.dat", 10, "User-edited item info");

        ApplyBagHookCleanup(paths);

        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.BagEventScriptPath)));
        AssertRestoredRoyalCandyTextRow(paths, "romfs/bin/message/English/common/itemname.dat", 10, "User-edited item name");
        AssertRestoredRoyalCandyTextRow(paths, "romfs/bin/message/English/common/itemname_plural.dat", 10, "User-edited item plural name");
        AssertRestoredRoyalCandyTextRow(paths, "romfs/bin/message/English/common/iteminfo.dat", 10, "User-edited item info");
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void BagHookCleanupWithStartingItemsOnlyRemovesBagEventOutput(ProjectGame game)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyStartingItems(paths);

        ApplyBagHookCleanup(paths);

        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.BagEventScriptPath)));
        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath)));
    }

    [Fact]
    public void BagHookCleanupPreservesUnrelatedBagEventEditsAndRemovesDependentFeatures()
    {
        const ulong editBeforeInstall = 0x12345678UL;
        const ulong editAfterInstall = 1UL;
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var bagPath = OutputPath(paths, SwShBagHookWorkflowService.BagEventScriptPath);
        Directory.CreateDirectory(Path.GetDirectoryName(bagPath)!);
        File.WriteAllBytes(
            bagPath,
            PatchTestCodeCell(
                File.ReadAllBytes(BasePath(paths, SwShBagHookWorkflowService.BagEventScriptPath)),
                cellIndex: 100,
                editBeforeInstall));

        InstallEmptyBagHook(paths);
        ApplyStartingItems(paths);
        ApplyRoyalCandy(paths, RoyalCandyUnlimitedWorkflowId);
        File.WriteAllBytes(
            bagPath,
            UseRedundantTestCellEncoding(
                PatchTestCodeCell(File.ReadAllBytes(bagPath), cellIndex: 101, editAfterInstall),
                cellIndex: 101));
        var unrelatedEncoding = ReadTestCompactCellEncoding(File.ReadAllBytes(bagPath), cellIndex: 101);

        ApplyBagHookCleanup(paths);

        Assert.True(File.Exists(bagPath));
        var restored = File.ReadAllBytes(bagPath);
        Assert.Equal(SwShBagHookInstallKind.NotInstalled, SwShBagHookAmxPatcher.Analyze(restored).Kind);
        var decoded = DecodeTestAmx(restored);
        var codeCells = ReadTestCells(decoded.Expanded, decoded.Header.Cod, decoded.Header.Dat - decoded.Header.Cod, cellSize: 8);
        Assert.Equal(editBeforeInstall, codeCells[100]);
        Assert.Equal(editAfterInstall, codeCells[101]);
        Assert.Equal(unrelatedEncoding, ReadTestCompactCellEncoding(restored, cellIndex: 101));
        Assert.Equal(5022, codeCells.Length);
        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath)));
        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ShopDataPath)));
    }

    [Fact]
    public void BagHookCleanupRefusesNonterminalHookWithoutChangingReviewedOutputs()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        InstallEmptyBagHook(paths);
        ApplyStartingItems(paths);
        ApplyRoyalCandy(paths, RoyalCandyUnlimitedWorkflowId);

        var bagPath = OutputPath(paths, SwShBagHookWorkflowService.BagEventScriptPath);
        var mainPath = OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath);
        var shopPath = OutputPath(paths, SwShRoyalCandyWorkflowService.ShopDataPath);
        var nonterminal = AppendTestCodeCell(File.ReadAllBytes(bagPath), 89);
        Assert.Equal(SwShBagHookInstallKind.InstalledV2, SwShBagHookAmxPatcher.Analyze(nonterminal).Kind);
        File.WriteAllBytes(bagPath, nonterminal);
        var mainBefore = File.ReadAllBytes(mainPath);
        var shopBefore = File.ReadAllBytes(shopPath);

        var service = new SwShBagHookEditSessionService();
        var stage = service.StageUninstall(paths, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var plan = service.CreateChangePlan(paths, stage.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("not terminal", StringComparison.Ordinal));
        Assert.Equal(nonterminal, File.ReadAllBytes(bagPath));
        Assert.Equal(mainBefore, File.ReadAllBytes(mainPath));
        Assert.Equal(shopBefore, File.ReadAllBytes(shopPath));
    }

    [Fact]
    public void BagHookUninstallBlocksAtomicallyWhenRoyalCandyShopMappingBecomesAmbiguous()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        InstallEmptyBagHook(paths);
        ApplyRoyalCandy(paths, RoyalCandyUnlimitedWorkflowId);

        var shopPath = OutputPath(paths, SwShRoyalCandyWorkflowService.ShopDataPath);
        var installedShopData = SwShShopDataFile.Parse(File.ReadAllBytes(shopPath));
        var ambiguousShopBytes = new SwShShopDataFile(
            [.. installedShopData.SingleShops, installedShopData.SingleShops[0]],
            installedShopData.MultiShops).Write();
        Assert.Equal(2, SwShShopDataFile.Parse(ambiguousShopBytes).SingleShops.Count);
        File.WriteAllBytes(shopPath, ambiguousShopBytes);
        var outputsBefore = SnapshotOutputTree(paths.OutputRootPath!);

        var service = new SwShBagHookEditSessionService();
        var stage = service.StageUninstall(paths, session: null);
        Assert.Contains(
            stage.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("dependent Royal Candy cleanup", StringComparison.Ordinal));
        Assert.Empty(stage.Session.PendingEdits);

        var plan = service.CreateChangePlan(paths, stage.Session);
        Assert.False(plan.CanApply);
        Assert.Empty(plan.Writes);
        var apply = service.ApplyChangePlan(paths, stage.Session, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        AssertOutputTreeMatches(paths.OutputRootPath!, outputsBefore);
    }

    [Fact]
    public void ForeignRoyalCandyMainBlocksRoyalAndBagHookUninstallAtomically()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        InstallEmptyBagHook(paths);
        Assert.Empty(SwShRoyalCandyCleanup.FindBlockingCleanupTargets(
            new ProjectWorkspaceService().Open(paths)));
        ApplyRoyalCandy(paths, RoyalCandyUnlimitedWorkflowId);

        var itemInfoPath = OutputPath(
            paths,
            "romfs/bin/message/English/common/iteminfo.dat");
        Assert.Contains(
            "candy packed with strange energy",
            SwShGameTextFile.Parse(File.ReadAllBytes(itemInfoPath)).Lines[1128].Text,
            StringComparison.OrdinalIgnoreCase);

        var mainPath = OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath);
        var installedNso = NsoFile.Parse(File.ReadAllBytes(mainPath));
        var text = installedNso.Text.DecompressedData.ToArray();
        const int allowedConsumableBranchOffset = 0x007DDA90;
        var caveOffset = DecodeConditionalBranchTarget(
            ReadInstruction(text, allowedConsumableBranchOffset),
            allowedConsumableBranchOffset);
        WriteInstruction(text, caveOffset + 8, EncodeNop());
        var foreignMain = installedNso.Write(textDecompressedData: text);
        Assert.Equal(
            SwShRoyalCandyExeFsSignatureKind.ForeignPatch,
            SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(foreignMain, ProjectGame.Sword).Kind);
        File.WriteAllBytes(mainPath, foreignMain);
        var outputsBefore = SnapshotOutputTree(paths.OutputRootPath!);

        var royalService = new SwShRoyalCandyEditSessionService();
        var royalStage = royalService.StageWorkflow(
            paths,
            RoyalCandyUninstallWorkflowId,
            levelCaps: null,
            session: null);
        Assert.Equal(
            "blocked",
            royalStage.Workflow.Workflows.Single(workflow =>
                workflow.WorkflowId == RoyalCandyUninstallWorkflowId).Status);
        Assert.Contains(royalStage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(royalStage.Session.PendingEdits);
        var royalPlan = royalService.CreateChangePlan(paths, royalStage.Session);
        Assert.False(royalPlan.CanApply);
        Assert.Empty(royalPlan.Writes);
        var royalApply = royalService.ApplyChangePlan(paths, royalStage.Session, royalPlan);
        Assert.Empty(royalApply.WrittenFiles);
        AssertOutputTreeMatches(paths.OutputRootPath!, outputsBefore);

        var bagService = new SwShBagHookEditSessionService();
        var bagStage = bagService.StageUninstall(paths, session: null);
        Assert.Contains(
            bagStage.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("dependent Royal Candy cleanup", StringComparison.Ordinal));
        Assert.Empty(bagStage.Session.PendingEdits);
        var bagPlan = bagService.CreateChangePlan(paths, bagStage.Session);
        Assert.False(bagPlan.CanApply);
        Assert.Empty(bagPlan.Writes);
        var bagApply = bagService.ApplyChangePlan(paths, bagStage.Session, bagPlan);
        Assert.Empty(bagApply.WrittenFiles);
        AssertOutputTreeMatches(paths.OutputRootPath!, outputsBefore);
    }

    [Fact]
    public async Task BagHookCleanupRollsBackAllReviewedOutputsWhenLateTargetDisappears()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        InstallEmptyBagHook(paths);
        ApplyStartingItems(paths);
        ApplyRoyalCandy(paths, RoyalCandyUnlimitedWorkflowId);

        var service = new SwShBagHookEditSessionService();
        var stage = service.StageUninstall(paths, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var plan = service.CreateChangePlan(paths, stage.Session);
        Assert.True(plan.CanApply);
        var outputsBefore = plan.Writes.ToDictionary(
            write => write.TargetRelativePath,
            write => File.ReadAllBytes(OutputPath(paths, write.TargetRelativePath)),
            StringComparer.OrdinalIgnoreCase);
        var bagPath = OutputPath(paths, SwShBagHookWorkflowService.BagEventScriptPath);
        var lateTargetRelativePath = plan.Writes
            .Where(write => !string.Equals(
                write.TargetRelativePath,
                SwShBagHookWorkflowService.BagEventScriptPath,
                StringComparison.OrdinalIgnoreCase))
            .Last()
            .TargetRelativePath;
        var lateTargetPath = OutputPath(paths, lateTargetRelativePath);
        var sabotageStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sabotage = Task.Run(
            async () =>
            {
                sabotageStarted.SetResult();
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (File.Exists(bagPath))
                {
                    if (DateTime.UtcNow >= deadline)
                    {
                        throw new TimeoutException("Bag Hook cleanup did not reach the Bag-event output.");
                    }

                    await Task.Delay(5, TestContext.Current.CancellationToken);
                }

                File.Delete(lateTargetPath);
            },
            TestContext.Current.CancellationToken);
        await sabotageStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        await sabotage;

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.File == lateTargetRelativePath
                && diagnostic.Message.Contains("no longer exists", StringComparison.Ordinal));
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Info
                && diagnostic.Message.Contains("rolled back", StringComparison.Ordinal));
        Assert.All(
            outputsBefore,
            output =>
            {
                var restoredPath = OutputPath(paths, output.Key);
                Assert.True(File.Exists(restoredPath));
                Assert.Equal(output.Value, File.ReadAllBytes(restoredPath));
            });
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void BagHookCleanupPreservesRoyalCandyShopPatchWhenRoyalCandyIsNotInstalled(ProjectGame game)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        var shopOutputPath = OutputPath(paths, SwShRoyalCandyWorkflowService.ShopDataPath);
        var shopOutput = new SwShShopDataFile(
            [new SwShSingleShopRecord(0x1F3FF031A3A24490UL, new SwShShopInventory([50, 51]))],
            [new SwShMultiShopRecord(
                0x66CA73B2966BB871UL,
                [
                    new SwShShopInventory([1, 2]),
                    new SwShShopInventory([3, 4]),
                ])])
            .Write();
        Directory.CreateDirectory(Path.GetDirectoryName(shopOutputPath)!);
        File.WriteAllBytes(shopOutputPath, shopOutput);

        ApplyBagHookCleanup(paths);

        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.BagEventScriptPath)));
        Assert.True(File.Exists(shopOutputPath));
        Assert.Equal(shopOutput, File.ReadAllBytes(shopOutputPath));
    }

    [Fact]
    public void RoyalCandyStageWithoutBagHookReturnsDependencyDiagnostic()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };

        var result = new SwShRoyalCandyEditSessionService().StageWorkflow(
            paths,
            RoyalCandyUnlimitedWorkflowId,
            levelCaps: null,
            session: null);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Bag Hook", StringComparison.Ordinal));
    }

    [Fact]
    public void StartingItemsStageWithoutBagHookReturnsDependencyDiagnostic()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };

        var result = new SwShStartingItemsEditSessionService().StageGrants(
            paths,
            [new SwShStartingItemGrantSelection(2, 50, 3)],
            session: null);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Bag Hook", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void StartingItemsHideRoyalCandyReservedItemWhenRoyalCandyIsInstalled(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyRoyalCandy(paths, workflowId);

        var workflow = new SwShStartingItemsWorkflowService().Load(new ProjectWorkspaceService().Open(paths));

        Assert.DoesNotContain(
            workflow.ItemOptions,
            option => option.ItemId == SwShBagHookAmxPatcher.RoyalCandyItemId);
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void StartingItemsRejectRoyalCandyReservedItemWhenRoyalCandyIsInstalled(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyRoyalCandy(paths, workflowId);

        var result = new SwShStartingItemsEditSessionService().StageGrants(
            paths,
            [new SwShStartingItemGrantSelection(2, SwShBagHookAmxPatcher.RoyalCandyItemId, 1)],
            session: null);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Royal Candy and EXP Candy XL", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("2:8:0")]
    [InlineData("2:50:3;2:51:4")]
    [InlineData(" 2:50:3")]
    public void StartingItemsRejectNoncanonicalPendingPayloadsWithoutWriting(string payload)
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        InstallEmptyBagHook(paths);
        var service = new SwShStartingItemsEditSessionService();
        var stage = service.StageGrants(
            paths,
            [new SwShStartingItemGrantSelection(2, 8, 25)],
            session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("2:8:1", Assert.Single(stage.Session.PendingEdits).NewValue);
        var reviewedPlan = service.CreateChangePlan(paths, stage.Session);
        Assert.True(reviewedPlan.CanApply);

        var craftedSession = stage.Session with
        {
            PendingEdits = [stage.Session.PendingEdits[0] with { NewValue = payload }],
        };
        var bagPath = OutputPath(paths, SwShBagHookWorkflowService.BagEventScriptPath);
        var before = File.ReadAllBytes(bagPath);

        var validation = service.Validate(paths, craftedSession);
        var plan = service.CreateChangePlan(paths, craftedSession);
        var apply = service.ApplyChangePlan(paths, craftedSession, reviewedPlan);

        Assert.False(validation.IsValid);
        Assert.Empty(plan.Writes);
        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(before, File.ReadAllBytes(bagPath));
    }

    [Fact]
    public void StartingItemsRejectNoncanonicalPendingEditShapeWithoutThrowing()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        InstallEmptyBagHook(paths);
        var service = new SwShStartingItemsEditSessionService();
        var stage = service.StageGrants(
            paths,
            [new SwShStartingItemGrantSelection(2, 50, 3)],
            session: null);
        var edit = Assert.Single(stage.Session.PendingEdits);
        var multiple = stage.Session with { PendingEdits = [edit, edit] };
        var wrongTarget = stage.Session with
        {
            PendingEdits = [edit with { RecordId = "other", Field = "other" }],
        };

        Assert.False(service.Validate(paths, multiple).IsValid);
        Assert.Empty(service.CreateChangePlan(paths, multiple).Writes);
        Assert.False(service.Validate(paths, wrongTarget).IsValid);
        Assert.Empty(service.CreateChangePlan(paths, wrongTarget).Writes);
    }

    [Fact]
    public void StartingItemsReviewedPlanIsBoundToCanonicalGrants()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        InstallEmptyBagHook(paths);
        var service = new SwShStartingItemsEditSessionService();
        var firstStage = service.StageGrants(
            paths,
            [new SwShStartingItemGrantSelection(2, 50, 3)],
            session: null);
        var firstPlan = service.CreateChangePlan(paths, firstStage.Session);
        var secondStage = service.StageGrants(
            paths,
            [new SwShStartingItemGrantSelection(2, 51, 4)],
            firstStage.Session);
        var bagPath = OutputPath(paths, SwShBagHookWorkflowService.BagEventScriptPath);
        var before = File.ReadAllBytes(bagPath);

        var apply = service.ApplyChangePlan(paths, secondStage.Session, firstPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(
            apply.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before, File.ReadAllBytes(bagPath));
        Assert.Contains(
            firstPlan.Writes.Single().Sources,
            source => source.Layer == ProjectFileLayer.Pending
                && source.RelativePath.StartsWith("pending/starting-items/", StringComparison.Ordinal));
    }

    [Fact]
    public void StartingItemsBlocksAndSanitizesMalformedActiveSlot()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        InstallEmptyBagHook(paths);
        var bagPath = OutputPath(paths, SwShBagHookWorkflowService.BagEventScriptPath);
        var itemZero = SwShBagHookAmxPatcher.ApplySlotPatches(
            File.ReadAllBytes(bagPath),
            [new SwShBagHookSlotPatch(2, 0, 1)]);
        Assert.Equal(
            "empty",
            SwShBagHookAmxPatcher.Analyze(itemZero).Slots.Single(candidate => candidate.Slot == 2).Status);
        var active = SwShBagHookAmxPatcher.ApplySlotPatches(
            itemZero,
            [new SwShBagHookSlotPatch(2, 50, 1)]);
        var malformed = PatchTestCodeCell(
            active,
            cellIndex: 5029,
            value: (0xFFFFFFFFUL << 32) | 188UL);
        File.WriteAllBytes(bagPath, malformed);

        var analysis = SwShBagHookAmxPatcher.Analyze(malformed);
        var workflow = new SwShStartingItemsWorkflowService().Load(new ProjectWorkspaceService().Open(paths));
        var slot = workflow.Grants.Single(grant => grant.Slot == 2);
        var stage = new SwShStartingItemsEditSessionService().StageGrants(
            paths,
            [new SwShStartingItemGrantSelection(2, 50, 3)],
            session: null);

        Assert.Equal("conflict", analysis.Slots.Single(candidate => candidate.Slot == 2).Status);
        Assert.Equal(SwShStartingItemsWorkflowService.BagHookDamagedBlockerKind, workflow.BlockerKind);
        Assert.Equal("conflict", slot.Status);
        Assert.Null(slot.ItemId);
        Assert.InRange(slot.Quantity, 1, 999);
        Assert.Contains("Invalid grant", slot.ItemName, StringComparison.Ordinal);
        Assert.Empty(stage.Session.PendingEdits);
        Assert.Throws<InvalidDataException>(() => SwShBagHookAmxPatcher.ApplySlotPatches(
            malformed,
            [new SwShBagHookSlotPatch(2, null, null)]));
    }

    [Fact]
    public void StartingItemsReservesRoyalCandyWhenAnyAuthoritativeMarkerRemains()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        InstallEmptyBagHook(paths);
        ApplyRoyalCandy(paths, RoyalCandyUnlimitedWorkflowId);
        var bagPath = OutputPath(paths, SwShBagHookWorkflowService.BagEventScriptPath);
        File.WriteAllBytes(
            bagPath,
            SwShBagHookAmxPatcher.ApplySlotPatches(
                File.ReadAllBytes(bagPath),
                [new SwShBagHookSlotPatch(SwShBagHookAmxPatcher.RoyalCandySlot, null, null)]));
        Assert.Equal(
            "empty",
            SwShBagHookAmxPatcher.Analyze(File.ReadAllBytes(bagPath)).Slots.Single(slot => slot.Slot == 1).Status);

        var workflow = new SwShStartingItemsWorkflowService().Load(new ProjectWorkspaceService().Open(paths));
        var stage = new SwShStartingItemsEditSessionService().StageGrants(
            paths,
            [new SwShStartingItemGrantSelection(2, SwShBagHookAmxPatcher.RoyalCandyItemId, 1)],
            session: null);

        Assert.DoesNotContain(workflow.ItemOptions, option => option.ItemId == SwShBagHookAmxPatcher.RoyalCandyItemId);
        Assert.Empty(stage.Session.PendingEdits);
        Assert.Contains(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void StartingItemsBlocksUnreadableAndEmptyItemMetadata()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        InstallEmptyBagHook(paths);
        var layeredItemPath = OutputPath(paths, SwShItemsWorkflowService.ItemDataPath);
        Directory.CreateDirectory(Path.GetDirectoryName(layeredItemPath)!);
        File.WriteAllBytes(layeredItemPath, [1, 2, 3, 4]);

        var unreadable = new SwShStartingItemsWorkflowService().Load(new ProjectWorkspaceService().Open(paths));
        var unreadableStage = new SwShStartingItemsEditSessionService().StageGrants(
            paths,
            [new SwShStartingItemGrantSelection(2, 50, 3)],
            session: null);

        Assert.Equal(SwShStartingItemsWorkflowService.ItemMetadataUnavailableBlockerKind, unreadable.BlockerKind);
        Assert.Contains(unreadable.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(unreadableStage.Session.PendingEdits);

        File.Delete(layeredItemPath);
        temp.WriteBaseRomFsFile(
            SwShItemsWorkflowService.ItemDataPath["romfs/".Length..],
            SwShItemTestFixtures.CreateItemTable(new ItemFixtureRecord(
                ItemId: 0,
                RawRowIndex: 0,
                BuyPrice: 0,
                WattsPrice: 0,
                AlternatePrice: 0,
                SwShItemPouch.Items)));
        var emptyCatalog = new SwShStartingItemsWorkflowService().Load(new ProjectWorkspaceService().Open(paths));

        Assert.Empty(emptyCatalog.ItemOptions);
        Assert.Equal(SwShStartingItemsWorkflowService.ItemMetadataUnavailableBlockerKind, emptyCatalog.BlockerKind);
        Assert.Contains(
            emptyCatalog.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("eligible item", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StartingItemsSourceCountUsesDistinctResolvedPhysicalInputs()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        InstallEmptyBagHook(paths);
        var baseItemPath = Path.Combine(paths.BaseRomFsPath!, "bin", "pml", "item", "item.dat");
        var layeredItemPath = OutputPath(paths, SwShItemsWorkflowService.ItemDataPath);

        var baseOnly = new SwShStartingItemsWorkflowService().Load(new ProjectWorkspaceService().Open(paths));
        Directory.CreateDirectory(Path.GetDirectoryName(layeredItemPath)!);
        File.Copy(baseItemPath, layeredItemPath, overwrite: true);
        var layered = new SwShStartingItemsWorkflowService().Load(new ProjectWorkspaceService().Open(paths));
        File.Delete(Path.Combine(paths.BaseRomFsPath!, "bin", "message", "English", "common", "wazaname.dat"));
        var missingMoveNames = new SwShStartingItemsWorkflowService().Load(new ProjectWorkspaceService().Open(paths));

        Assert.Equal(4, baseOnly.Stats.SourceFileCount);
        Assert.Equal(5, layered.Stats.SourceFileCount);
        Assert.Equal(4, missingMoveNames.Stats.SourceFileCount);
    }

    [Fact]
    public void StartingItemsPlanBindsLayeredAndBaseItemMetadataSources()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        InstallEmptyBagHook(paths);
        var baseItemPath = Path.Combine(paths.BaseRomFsPath!, "bin", "pml", "item", "item.dat");
        var layeredItemPath = OutputPath(paths, SwShItemsWorkflowService.ItemDataPath);
        Directory.CreateDirectory(Path.GetDirectoryName(layeredItemPath)!);
        File.Copy(baseItemPath, layeredItemPath, overwrite: true);
        var service = new SwShStartingItemsEditSessionService();
        var stage = service.StageGrants(
            paths,
            [new SwShStartingItemGrantSelection(2, 50, 3)],
            session: null);
        var plan = service.CreateChangePlan(paths, stage.Session);
        var sources = plan.Writes.Single().Sources;
        var bagPath = OutputPath(paths, SwShBagHookWorkflowService.BagEventScriptPath);
        var before = File.ReadAllBytes(bagPath);

        Assert.Contains(sources, source => source.Layer == ProjectFileLayer.Layered
            && source.RelativePath == SwShItemsWorkflowService.ItemDataPath);
        Assert.Contains(sources, source => source.Layer == ProjectFileLayer.Base
            && source.RelativePath == SwShItemsWorkflowService.ItemDataPath);
        var baseTable = SwShItemTable.Parse(File.ReadAllBytes(baseItemPath));
        File.WriteAllBytes(
            baseItemPath,
            baseTable.WriteEdits([new SwShItemTableEdit(50, SwShItemTableField.BuyPrice, 12345)]));

        var applyAfterBaseDrift = service.ApplyChangePlan(paths, stage.Session, plan);

        Assert.Empty(applyAfterBaseDrift.WrittenFiles);
        Assert.Contains(applyAfterBaseDrift.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(before, File.ReadAllBytes(bagPath));
    }

    [Theory]
    [InlineData(ProjectGame.Scarlet)]
    [InlineData(ProjectGame.Violet)]
    public void StartingItemsDirectServiceFailsClosedForWrongGame(ProjectGame game)
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var swordPaths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        InstallEmptyBagHook(swordPaths);
        var paths = swordPaths with { SelectedGame = game };
        var bagPath = OutputPath(paths, SwShBagHookWorkflowService.BagEventScriptPath);
        var before = File.ReadAllBytes(bagPath);
        var project = new ProjectWorkspaceService().Open(paths);
        var workflow = new SwShStartingItemsWorkflowService().Load(project);
        var service = new SwShStartingItemsEditSessionService();
        var stage = service.StageGrants(
            paths,
            [new SwShStartingItemGrantSelection(2, 50, 3)],
            session: null);

        Assert.Equal(SwShWorkflowAvailability.Disabled, workflow.Summary.Availability);
        Assert.Empty(workflow.Grants);
        Assert.Empty(workflow.ItemOptions);
        Assert.Empty(stage.Session.PendingEdits);
        Assert.Contains(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(before, File.ReadAllBytes(bagPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoyalCandyBlocksExistingStartingItem1128WithoutDeletingIt(bool damageQuantity)
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        InstallEmptyBagHook(paths);
        var startingItems = new SwShStartingItemsEditSessionService();
        var startingStage = startingItems.StageGrants(
            paths,
            [new SwShStartingItemGrantSelection(2, SwShBagHookAmxPatcher.RoyalCandyItemId, 7)],
            session: null);
        Assert.DoesNotContain(startingStage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var startingPlan = startingItems.CreateChangePlan(paths, startingStage.Session);
        var startingApply = startingItems.ApplyChangePlan(paths, startingStage.Session, startingPlan);
        Assert.DoesNotContain(startingApply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var bagPath = OutputPath(paths, SwShBagHookWorkflowService.BagEventScriptPath);
        if (damageQuantity)
        {
            File.WriteAllBytes(
                bagPath,
                PatchTestCodeCell(
                    File.ReadAllBytes(bagPath),
                    cellIndex: 5028,
                    value: 188UL));
        }
        var before = File.ReadAllBytes(bagPath);

        var workflow = new SwShRoyalCandyWorkflowService().Load(new ProjectWorkspaceService().Open(paths));
        var royalStage = new SwShRoyalCandyEditSessionService().StageWorkflow(
            paths,
            RoyalCandyUnlimitedWorkflowId,
            levelCaps: null,
            session: null);

        Assert.Contains(
            workflow.Checks,
            check => check.CheckId.EndsWith(":bag-hook-starting-items-item-1128", StringComparison.Ordinal)
                && check.Status == "Fail");
        Assert.Empty(royalStage.Session.PendingEdits);
        Assert.Contains(
            royalStage.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("Clear item 1128", StringComparison.Ordinal));
        Assert.Equal(before, File.ReadAllBytes(bagPath));
        var slot2 = SwShBagHookAmxPatcher.Analyze(before).Slots.Single(slot => slot.Slot == 2);
        Assert.Equal(SwShBagHookAmxPatcher.RoyalCandyItemId, slot2.ItemId);
        Assert.Equal(damageQuantity ? "conflict" : "occupied", slot2.Status);
        Assert.Equal(damageQuantity ? null : 7, slot2.Quantity);
    }

    [Fact]
    public void RoyalCandyCleanupRemainsAvailableAndPreservesStartingItem1128()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        InstallEmptyBagHook(paths);
        ApplyRoyalCandy(paths, RoyalCandyUnlimitedWorkflowId);
        var bagPath = OutputPath(paths, SwShBagHookWorkflowService.BagEventScriptPath);
        File.WriteAllBytes(
            bagPath,
            SwShBagHookAmxPatcher.ApplySlotPatches(
                File.ReadAllBytes(bagPath),
                [new SwShBagHookSlotPatch(2, SwShBagHookAmxPatcher.RoyalCandyItemId, 7)]));

        var service = new SwShRoyalCandyEditSessionService();
        var stage = service.StageWorkflow(
            paths,
            RoyalCandyUninstallWorkflowId,
            levelCaps: null,
            session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var plan = service.CreateChangePlan(paths, stage.Session);
        var apply = service.ApplyChangePlan(paths, stage.Session, plan);

        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var analysis = SwShBagHookAmxPatcher.Analyze(File.ReadAllBytes(bagPath));
        Assert.Equal("empty", analysis.Slots.Single(slot => slot.Slot == 1).Status);
        var slot2 = analysis.Slots.Single(slot => slot.Slot == 2);
        Assert.Equal("occupied", slot2.Status);
        Assert.Equal(SwShBagHookAmxPatcher.RoyalCandyItemId, slot2.ItemId);
        Assert.Equal(7, slot2.Quantity);
    }

    [Fact]
    public void BagHookInstallStoresSignatureOutsideExecutableAmxCode()
    {
        var patched = SwShBagHookAmxPatcher.InstallEmptyHook(CreateVanillaBagEventScript());
        var decoded = DecodeTestAmx(patched);
        var codeCells = ReadTestCells(decoded.Expanded, decoded.Header.Cod, decoded.Header.Dat - decoded.Header.Cod, cellSize: 8);
        var markerCells = ReadTestCells(decoded.Expanded, decoded.Header.Hea - 5 * 8, 5 * 8, cellSize: 8);

        Assert.Equal(5022 + 103, codeCells.Length);
        Assert.DoesNotContain(0x4741425F48535753UL, codeCells);
        Assert.DoesNotContain(0x32565F4B4F4F485FUL, codeCells);
        Assert.Equal(
            [0x4741425F48535753UL, 0x32565F4B4F4F485FUL, 20UL, 5UL, 70UL],
            markerCells);

        var analysis = SwShBagHookAmxPatcher.Analyze(patched);
        Assert.Equal(SwShBagHookInstallKind.InstalledV2, analysis.Kind);
    }

    [Fact]
    public void SlotPatchesMigrateLegacyCodeSectionBagHookSignature()
    {
        var legacy = MoveTestMarkerFromDataToCode(SwShBagHookAmxPatcher.InstallEmptyHook(CreateVanillaBagEventScript()));

        var legacyAnalysis = SwShBagHookAmxPatcher.Analyze(legacy);
        Assert.Equal(SwShBagHookInstallKind.InstalledV2, legacyAnalysis.Kind);
        Assert.Contains("legacy code-section marker", legacyAnalysis.Message, StringComparison.Ordinal);

        var patched = SwShBagHookAmxPatcher.ApplySlotPatches(
            legacy,
            [new SwShBagHookSlotPatch(1, SwShBagHookAmxPatcher.RoyalCandyItemId, 1)]);

        var decoded = DecodeTestAmx(patched);
        var codeCells = ReadTestCells(decoded.Expanded, decoded.Header.Cod, decoded.Header.Dat - decoded.Header.Cod, cellSize: 8);
        var markerCells = ReadTestCells(decoded.Expanded, decoded.Header.Hea - 5 * 8, 5 * 8, cellSize: 8);
        Assert.Equal(5022 + 103, codeCells.Length);
        Assert.DoesNotContain(0x4741425F48535753UL, codeCells);
        Assert.Equal(
            [0x4741425F48535753UL, 0x32565F4B4F4F485FUL, 20UL, 5UL, 70UL],
            markerCells);

        var analysis = SwShBagHookAmxPatcher.Analyze(patched);
        var slot1 = analysis.Slots.Single(slot => slot.Slot == 1);
        Assert.Equal(SwShBagHookInstallKind.InstalledV2, analysis.Kind);
        Assert.Equal("occupied", slot1.Status);
        Assert.Equal(SwShBagHookAmxPatcher.RoyalCandyItemId, slot1.ItemId);
        Assert.Equal(1, slot1.Quantity);
        Assert.DoesNotContain("legacy code-section marker", analysis.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BagHookRestoreFromBaseRemovesOwnedCurrentAndLegacyLayouts()
    {
        var baseData = CreateVanillaBagEventScript();
        var installed = SwShBagHookAmxPatcher.ApplySlotPatches(
            SwShBagHookAmxPatcher.InstallEmptyHook(baseData),
            [new SwShBagHookSlotPatch(2, 50, 3)]);

        var currentRestore = SwShBagHookAmxPatcher.RestoreFromBase(installed, baseData);
        var legacyRestore = SwShBagHookAmxPatcher.RestoreFromBase(
            MoveTestMarkerFromDataToCode(installed),
            baseData);

        Assert.True(currentRestore.IsBaseEquivalent);
        Assert.True(legacyRestore.IsBaseEquivalent);
        Assert.Equal(baseData, currentRestore.Data);
        Assert.Equal(baseData, legacyRestore.Data);
        Assert.Equal(SwShBagHookInstallKind.NotInstalled, SwShBagHookAmxPatcher.Analyze(currentRestore.Data).Kind);
        Assert.Equal(SwShBagHookInstallKind.NotInstalled, SwShBagHookAmxPatcher.Analyze(legacyRestore.Data).Kind);
    }

    [Fact]
    public void BagHookStageInstallRepairsLegacyCodeSectionSignature()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var targetPath = OutputPath(paths, SwShBagHookWorkflowService.BagEventScriptPath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllBytes(
            targetPath,
            MoveTestMarkerFromDataToCode(SwShBagHookAmxPatcher.InstallEmptyHook(CreateVanillaBagEventScript())));

        var workflow = new SwShBagHookWorkflowService().Load(new ProjectWorkspaceService().Open(paths));
        Assert.Equal(SwShBagHookWorkflowService.RepairableStatus, workflow.InstallStatus);

        var service = new SwShBagHookEditSessionService();
        var stage = service.StageInstall(paths, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var plan = service.CreateChangePlan(paths, stage.Session);
        Assert.True(plan.CanApply);
        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        workflow = new SwShBagHookWorkflowService().Load(new ProjectWorkspaceService().Open(paths));
        Assert.Equal(SwShBagHookWorkflowService.InstalledStatus, workflow.InstallStatus);

        var decoded = DecodeTestAmx(File.ReadAllBytes(targetPath));
        var codeCells = ReadTestCells(decoded.Expanded, decoded.Header.Cod, decoded.Header.Dat - decoded.Header.Cod, cellSize: 8);
        Assert.DoesNotContain(0x4741425F48535753UL, codeCells);
    }

    [Fact]
    public void CatchCapStageRejectsDescendingBadgeCaps()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };

        var result = new SwShCatchCapEditSessionService().StageCaps(
            paths,
            Enumerable.Range(0, SwShCatchCapMainPatcher.CapCount)
                .Select(index => new SwShCatchCapSelection(index, index == 1 ? 19 : 20 + index * 5))
                .ToArray(),
            session: null);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("same as or higher", StringComparison.Ordinal));
    }

    [Fact]
    public void CatchCapStageRejectsEighthBadgeOverride()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };

        var result = new SwShCatchCapEditSessionService().StageCaps(
            paths,
            Enumerable.Range(0, SwShCatchCapMainPatcher.CapCount)
                .Select(index => new SwShCatchCapSelection(
                    index,
                    index == SwShCatchCapMainPatcher.FinalBadgeCount ? 33 : 20 + index * 5))
                .ToArray(),
            session: null);

        Assert.Empty(result.Session.PendingEdits);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("fixed at level 100", StringComparison.Ordinal));
    }

    [Fact]
    public void CatchCapApplyRejectsEighthBadgeOverride()
    {
        var caps = Enumerable.Range(0, SwShCatchCapMainPatcher.CapCount)
            .Select(index => index == SwShCatchCapMainPatcher.FinalBadgeCount ? 33 : 20 + index * 5)
            .ToArray();

        var exception = Assert.Throws<InvalidDataException>(() =>
            SwShCatchCapMainPatcher.Apply(CreateSharedHookNso(), caps));

        Assert.Contains("fixed at level 100", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void CatchCapAnalysisRejectsRedirectedDisplayBranch(ProjectGame game)
    {
        int[] caps = [18, 22, 27, 33, 38, 44, 49, 60, 100];
        var patched = SwShCatchCapMainPatcher.Apply(CreateSharedHookNso(game), caps, game);
        var nso = NsoFile.Parse(patched);
        var text = nso.Text.DecompressedData.ToArray();
        var hookOffset = game == ProjectGame.Shield
            ? SwShCatchCapMainPatcher.ShieldExeFsHookSiteOffset
            : SwShCatchCapMainPatcher.ExeFsHookSiteOffset;
        var caveOffset = SwShCatchCapMainPatcher.ExeFsCaveClampOffset
            + (game == ProjectGame.Shield ? 0x30 : 0);
        WriteInstruction(text, hookOffset, EncodeBranch(hookOffset, caveOffset + 4));

        var analysis = SwShCatchCapMainPatcher.Analyze(
            nso.Write(textDecompressedData: text),
            game);

        Assert.Equal(SwShCatchCapInstallKind.Conflict, analysis.Kind);
        Assert.Contains("damaged or redirected", analysis.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void CatchCapAnalysisRejectsDamagedOwnedCave(ProjectGame game)
    {
        int[] caps = [18, 22, 27, 33, 38, 44, 49, 60, 100];
        var patched = SwShCatchCapMainPatcher.Apply(CreateSharedHookNso(game), caps, game);
        var nso = NsoFile.Parse(patched);
        var text = nso.Text.DecompressedData.ToArray();
        var caveOffset = SwShCatchCapMainPatcher.ExeFsCaveLoadValueOffset
            + (game == ProjectGame.Shield ? 0x30 : 0);
        WriteInstruction(text, caveOffset + 8, EncodeNop());

        var analysis = SwShCatchCapMainPatcher.Analyze(
            nso.Write(textDecompressedData: text),
            game);

        Assert.Equal(SwShCatchCapInstallKind.Conflict, analysis.Kind);
        Assert.Contains("damaged or redirected", analysis.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void IvScreenRejectsNonCanonicalFullBuildIdentity(ProjectGame game)
    {
        var baseMain = CreateSharedHookNso(game);
        var patched = SwShIvScreenMainPatcher.Apply(baseMain, game);
        var nonCanonicalMain = baseMain.ToArray();
        nonCanonicalMain[0x40 + 0x1F] = 0x01;

        var analysis = SwShIvScreenMainPatcher.Analyze(nonCanonicalMain, game);

        Assert.Equal(SwShIvScreenInstallKind.UnsupportedBuild, analysis.Kind);
        Assert.Equal(game == ProjectGame.Shield ? ShieldBuildId : SwordBuildId, analysis.BuildId);
        Assert.Throws<InvalidDataException>(() =>
            SwShIvScreenMainPatcher.Apply(nonCanonicalMain, game));
        Assert.Throws<InvalidDataException>(() =>
            SwShIvScreenMainPatcher.RestoreFromBase(patched, nonCanonicalMain, game));
    }

    [Fact]
    public void IvScreenRejectsRequiredNsoSegmentHashMismatch()
    {
        var corrupt = CreateSharedHookNso(ProjectGame.Sword);
        BinaryPrimitives.WriteUInt32LittleEndian(
            corrupt.AsSpan(0x0C, sizeof(uint)),
            (uint)NsoFlags.CheckHashText);
        corrupt[0xA0] ^= 0xFF;

        var analysis = SwShIvScreenMainPatcher.Analyze(corrupt, ProjectGame.Sword);

        Assert.Equal(SwShIvScreenInstallKind.Conflict, analysis.Kind);
        Assert.Contains("required NSO header hash", analysis.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidDataException>(() =>
            SwShIvScreenMainPatcher.Apply(corrupt, ProjectGame.Sword));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void IvScreenRejectsDamagedCurrentGraphAndClaimedCave(ProjectGame game)
    {
        var baseMain = CreateSharedHookNso(game);
        var patched = SwShIvScreenMainPatcher.Apply(baseMain, game);
        var nso = NsoFile.Parse(patched);
        var shift = game == ProjectGame.Shield ? 0x30 : 0;

        var damagedText = nso.Text.DecompressedData.ToArray();
        WriteInstruction(damagedText, 0x0138B3AC + shift + 4, EncodeNop());
        var damagedGraph = nso.Write(textDecompressedData: damagedText);
        Assert.Equal(
            SwShIvScreenInstallKind.Conflict,
            SwShIvScreenMainPatcher.Analyze(damagedGraph, game).Kind);
        Assert.Throws<InvalidDataException>(() =>
            SwShIvScreenMainPatcher.RestoreFromBase(damagedGraph, baseMain, game));

        var occupiedText = nso.Text.DecompressedData.ToArray();
        occupiedText[0x01392334 + shift] = 0x01;
        var occupiedCave = nso.Write(textDecompressedData: occupiedText);
        Assert.Equal(
            SwShIvScreenInstallKind.Conflict,
            SwShIvScreenMainPatcher.Analyze(occupiedCave, game).Kind);
        Assert.Throws<InvalidDataException>(() =>
            SwShIvScreenMainPatcher.RestoreFromBase(occupiedCave, baseMain, game));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void IvScreenBlocksFreshInstallWhenAnyClaimedCaveIsOccupied(ProjectGame game)
    {
        var nso = NsoFile.Parse(CreateSharedHookNso(game));
        var text = nso.Text.DecompressedData.ToArray();
        var shift = game == ProjectGame.Shield ? 0x30 : 0;
        text[0x01392334 + shift] = 0x01;
        var occupied = nso.Write(textDecompressedData: text);

        Assert.Equal(
            SwShIvScreenInstallKind.Conflict,
            SwShIvScreenMainPatcher.Analyze(occupied, game).Kind);
        Assert.Throws<InvalidDataException>(() =>
            SwShIvScreenMainPatcher.Apply(occupied, game));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void IvScreenValidatesEveryActiveDependency(ProjectGame game)
    {
        var shift = game == ProjectGame.Shield ? 0x30 : 0;
        var dependencyOffsets = new[]
        {
            0x00779070,
            0x00778E20,
            0x0077AFD0,
            0x0077AC70,
            0x0077AC30,
            0x00779F50,
            0x0138A1A0 + shift,
            0x0138B550 + shift,
            0x0138FB60 + shift,
        };

        foreach (var dependencyOffset in dependencyOffsets)
        {
            var nso = NsoFile.Parse(CreateSharedHookNso(game));
            var text = nso.Text.DecompressedData.ToArray();
            WriteInstruction(text, dependencyOffset, EncodeNop());
            var damaged = nso.Write(textDecompressedData: text);

            Assert.Equal(
                SwShIvScreenInstallKind.NotInstalledDependencyConflict,
                SwShIvScreenMainPatcher.Analyze(damaged, game).Kind);
            Assert.Throws<InvalidDataException>(() =>
                SwShIvScreenMainPatcher.Apply(damaged, game));
        }
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void IvScreenApplyAndRestorePreserveOutsideOwnedTextAndOtherSegments(ProjectGame game)
    {
        const int outsideOffset = 0x1000;
        var initialNso = NsoFile.Parse(CreateSharedHookNso(game));
        var baseText = initialNso.Text.DecompressedData.ToArray();
        var baseRo = initialNso.Ro.DecompressedData.ToArray();
        var baseData = initialNso.Data.DecompressedData.ToArray();
        baseText[outsideOffset] = 0x5A;
        baseRo[0] = 0x66;
        baseData[0] = 0x77;
        var baseMain = initialNso.Write(
            textDecompressedData: baseText,
            roDecompressedData: baseRo,
            dataDecompressedData: baseData);

        var patched = SwShIvScreenMainPatcher.Apply(baseMain, game);
        var patchedNso = NsoFile.Parse(patched);
        Assert.Equal(0x5A, patchedNso.Text.DecompressedData[outsideOffset]);
        Assert.Equal(0x66, patchedNso.Ro.DecompressedData[0]);
        Assert.Equal(0x77, patchedNso.Data.DecompressedData[0]);
        Assert.Equal(
            baseText.AsSpan(SwShIvScreenMainPatcher.RawIvGetterOffset, 4).ToArray(),
            patchedNso.Text.DecompressedData.AsSpan(SwShIvScreenMainPatcher.RawIvGetterOffset, 4).ToArray());
        Assert.Equal(
            baseText.AsSpan(SwShIvScreenMainPatcher.HyperTrainingIvWrapperOffset, 4).ToArray(),
            patchedNso.Text.DecompressedData.AsSpan(SwShIvScreenMainPatcher.HyperTrainingIvWrapperOffset, 4).ToArray());

        var currentText = patchedNso.Text.DecompressedData.ToArray();
        var currentRo = patchedNso.Ro.DecompressedData.ToArray();
        var currentData = patchedNso.Data.DecompressedData.ToArray();
        currentText[outsideOffset + 1] = 0xA5;
        currentRo[0] = 0x88;
        currentData[0] = 0x99;
        var currentMain = patchedNso.Write(
            textDecompressedData: currentText,
            roDecompressedData: currentRo,
            dataDecompressedData: currentData);

        var restored = SwShIvScreenMainPatcher.RestoreFromBase(currentMain, baseMain, game);
        var restoredNso = NsoFile.Parse(restored);
        Assert.Equal(0x5A, restoredNso.Text.DecompressedData[outsideOffset]);
        Assert.Equal(0xA5, restoredNso.Text.DecompressedData[outsideOffset + 1]);
        Assert.Equal(0x88, restoredNso.Ro.DecompressedData[0]);
        Assert.Equal(0x99, restoredNso.Data.DecompressedData[0]);
        Assert.Equal(
            SwShIvScreenInstallKind.NotInstalled,
            SwShIvScreenMainPatcher.Analyze(restored, game).Kind);
    }

    [Fact]
    public void IvScreenRecognizesMigratesAndRestoresExactInitialSwordLayout()
    {
        var baseMain = CreateSharedHookNso(ProjectGame.Sword);
        var legacy = CreateInitialIvScreenLegacyNso(baseMain);

        Assert.Equal(
            SwShIvScreenInstallKind.InstalledLegacyV1,
            SwShIvScreenMainPatcher.Analyze(legacy, ProjectGame.Sword).Kind);

        var migrated = SwShIvScreenMainPatcher.Apply(legacy, ProjectGame.Sword);
        Assert.Equal(
            SwShIvScreenInstallKind.InstalledV1,
            SwShIvScreenMainPatcher.Analyze(migrated, ProjectGame.Sword).Kind);

        var legacyNso = NsoFile.Parse(legacy);
        var legacyText = legacyNso.Text.DecompressedData.ToArray();
        var legacyRo = legacyNso.Ro.DecompressedData.ToArray();
        var legacyData = legacyNso.Data.DecompressedData.ToArray();
        legacyText[0x1000] = 0x5A;
        WriteInstruction(legacyText, 0x0077AFD0, EncodeNop());
        legacyRo[0] = 0x66;
        legacyData[0] = 0x77;
        var legacyWithOtherEdits = legacyNso.Write(
            textDecompressedData: legacyText,
            roDecompressedData: legacyRo,
            dataDecompressedData: legacyData);
        Assert.Equal(
            SwShIvScreenInstallKind.InstalledLegacyV1,
            SwShIvScreenMainPatcher.Analyze(legacyWithOtherEdits, ProjectGame.Sword).Kind);
        var restored = SwShIvScreenMainPatcher.RestoreFromBase(
            legacyWithOtherEdits,
            baseMain,
            ProjectGame.Sword);
        var restoredNso = NsoFile.Parse(restored);
        Assert.Equal(0x5A, restoredNso.Text.DecompressedData[0x1000]);
        Assert.Equal(EncodeNop(), ReadInstruction(restoredNso.Text.DecompressedData, 0x0077AFD0));
        Assert.Equal(0x66, restoredNso.Ro.DecompressedData[0]);
        Assert.Equal(0x77, restoredNso.Data.DecompressedData[0]);
        Assert.Equal(
            SwShIvScreenInstallKind.NotInstalledDependencyConflict,
            SwShIvScreenMainPatcher.Analyze(restored, ProjectGame.Sword).Kind);
    }

    [Fact]
    public void IvScreenRejectsDamagedOrForeignAugmentedInitialSwordLayout()
    {
        var baseMain = CreateSharedHookNso(ProjectGame.Sword);
        var legacy = CreateInitialIvScreenLegacyNso(baseMain);
        var legacyNso = NsoFile.Parse(legacy);

        var damagedText = legacyNso.Text.DecompressedData.ToArray();
        damagedText[0x0138F324] ^= 0x01;
        var damaged = legacyNso.Write(textDecompressedData: damagedText);
        Assert.Equal(
            SwShIvScreenInstallKind.Conflict,
            SwShIvScreenMainPatcher.Analyze(damaged, ProjectGame.Sword).Kind);
        Assert.Throws<InvalidDataException>(() =>
            SwShIvScreenMainPatcher.RestoreFromBase(damaged, baseMain, ProjectGame.Sword));

        var foreignCaveText = legacyNso.Text.DecompressedData.ToArray();
        foreignCaveText[0x01392334] = 0x01;
        var foreignCave = legacyNso.Write(textDecompressedData: foreignCaveText);
        Assert.Equal(
            SwShIvScreenInstallKind.Conflict,
            SwShIvScreenMainPatcher.Analyze(foreignCave, ProjectGame.Sword).Kind);
        Assert.Throws<InvalidDataException>(() =>
            SwShIvScreenMainPatcher.Apply(foreignCave, ProjectGame.Sword));
    }

    [Theory]
    [InlineData(0x00778E20)]
    [InlineData(0x00779070)]
    [InlineData(0x0138A1A0)]
    [InlineData(0x0138B1E0)]
    [InlineData(0x0138FB60)]
    public void IvScreenRejectsDamagedInitialSwordDependency(int dependencyOffset)
    {
        var baseMain = CreateSharedHookNso(ProjectGame.Sword);
        var legacyNso = NsoFile.Parse(CreateInitialIvScreenLegacyNso(baseMain));
        var damagedText = legacyNso.Text.DecompressedData.ToArray();
        WriteInstruction(damagedText, dependencyOffset, EncodeNop());
        var damaged = legacyNso.Write(textDecompressedData: damagedText);

        Assert.Equal(
            SwShIvScreenInstallKind.Conflict,
            SwShIvScreenMainPatcher.Analyze(damaged, ProjectGame.Sword).Kind);
        Assert.Throws<InvalidDataException>(() =>
            SwShIvScreenMainPatcher.RestoreFromBase(damaged, baseMain, ProjectGame.Sword));
    }

    [Fact]
    public void IvScreenLegacyDependencySplitBlocksMigrationButAllowsUninstall()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var baseMain = CreateSharedHookNso(ProjectGame.Sword);
        var legacyNso = NsoFile.Parse(CreateInitialIvScreenLegacyNso(baseMain));
        var legacyText = legacyNso.Text.DecompressedData.ToArray();
        WriteInstruction(legacyText, 0x0077AFD0, EncodeNop());
        var legacy = legacyNso.Write(textDecompressedData: legacyText);
        temp.WriteOutputFile(SwShIvScreenWorkflowService.ExeFsMainPath, legacy);

        var service = new SwShIvScreenEditSessionService();
        var workflow = new SwShIvScreenWorkflowService().Load(
            new ProjectWorkspaceService().Open(paths));
        var installStage = service.StageInstall(paths, session: null);
        var uninstallStage = service.StageUninstall(paths, session: null);
        var uninstallPlan = service.CreateChangePlan(paths, uninstallStage.Session);
        var uninstallApply = service.ApplyChangePlan(paths, uninstallStage.Session, uninstallPlan);

        Assert.Equal("blocked", workflow.InstallStatus);
        Assert.True(workflow.CanUninstall);
        Assert.Contains(workflow.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Warning
            && diagnostic.Message.Contains("migration is unavailable", StringComparison.OrdinalIgnoreCase));
        Assert.Throws<InvalidDataException>(() =>
            SwShIvScreenMainPatcher.Apply(legacy, ProjectGame.Sword));
        Assert.Empty(installStage.Session.PendingEdits);
        Assert.Contains(installStage.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("migration is unavailable", StringComparison.OrdinalIgnoreCase));
        Assert.Single(uninstallStage.Session.PendingEdits);
        Assert.True(uninstallPlan.CanApply);
        Assert.DoesNotContain(uninstallApply.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);

        var outputPath = OutputPath(paths, SwShIvScreenWorkflowService.ExeFsMainPath);
        Assert.True(File.Exists(outputPath));
        var restored = File.ReadAllBytes(outputPath);
        Assert.Equal(
            SwShIvScreenInstallKind.NotInstalledDependencyConflict,
            SwShIvScreenMainPatcher.Analyze(restored, ProjectGame.Sword).Kind);
        Assert.Equal(
            EncodeNop(),
            ReadInstruction(NsoFile.Parse(restored).Text.DecompressedData, 0x0077AFD0));
    }

    [Fact]
    public void RoyalCandyAllocatorRejectsSwordAndShieldIvScreenCaves()
    {
        var sword = SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(
                SwShExeFsReservedRegionLedger.OwnerIvScreen,
                ProjectGame.Sword)
            .Single(region => region.FeatureId == "iv-screen-cave-21");
        var shield = SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(
                SwShExeFsReservedRegionLedger.OwnerIvScreen,
                ProjectGame.Shield)
            .Single(region => region.FeatureId == "iv-screen-cave-21");
        var allocatorReservations = SwShExeFsReservedRegionLedger.MainTextReservationsForOtherOwners(
            SwShExeFsReservedRegionLedger.OwnerRoyalCandy,
            SwShExeFsReservedRegionLedger.OwnerRoyalCandyStoryLimits);

        Assert.Equal(sword.StartOffset + 0x30, shield.StartOffset);
        Assert.Contains(allocatorReservations, region =>
            region.Owner == SwShExeFsReservedRegionLedger.OwnerIvScreen
            && region.StartOffset == sword.StartOffset);
        Assert.Contains(allocatorReservations, region =>
            region.Owner == SwShExeFsReservedRegionLedger.OwnerIvScreen
            && region.StartOffset == shield.StartOffset);

        var availability = typeof(SwShExeFsRoyalCandyMainPatcher).GetMethod(
            "IsAvailableCodeCave",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(availability);
        Assert.False((bool)availability.Invoke(null, [sword.StartOffset!.Value, 0x0C])!);
        Assert.False((bool)availability.Invoke(null, [shield.StartOffset!.Value, 0x0C])!);
    }

    [Fact]
    public void IvScreenWorkflowDoesNotExposeGameSpecificReservationsWithoutDetectedGame()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = null };

        var workflow = new SwShIvScreenWorkflowService().Load(
            new ProjectWorkspaceService().Open(paths));

        Assert.Equal(SwShWorkflowAvailability.Disabled, workflow.Summary.Availability);
        Assert.Null(workflow.DetectedGame);
        Assert.Equal("unknown", workflow.BuildId);
        Assert.Empty(workflow.ReservedRegions);
    }

    [Fact]
    public void IvScreenCanUninstallRequiresVerifiedBaseAndGeneratedExactInstall()
    {
        var vanilla = CreateSharedHookNso(ProjectGame.Sword);
        var current = SwShIvScreenMainPatcher.Apply(vanilla, ProjectGame.Sword);
        var legacy = CreateInitialIvScreenLegacyNso(vanilla);

        using (var baseOnly = CreateHookProject(ProjectGame.Sword))
        {
            baseOnly.WriteBaseExeFsFile("main", current);
            var paths = baseOnly.Paths with { SelectedGame = ProjectGame.Sword };
            var workflow = new SwShIvScreenWorkflowService().Load(
                new ProjectWorkspaceService().Open(paths));
            Assert.Equal("blocked", workflow.InstallStatus);
            Assert.False(workflow.CanUninstall);
        }

        using (var invalidBase = CreateHookProject(ProjectGame.Sword))
        {
            var invalidBaseNso = NsoFile.Parse(vanilla);
            var invalidBaseText = invalidBaseNso.Text.DecompressedData.ToArray();
            WriteInstruction(invalidBaseText, SwShIvScreenMainPatcher.PrimaryValueSourceOffset, EncodeNop());
            invalidBase.WriteBaseExeFsFile(
                "main",
                invalidBaseNso.Write(textDecompressedData: invalidBaseText));
            invalidBase.WriteOutputFile(SwShIvScreenWorkflowService.ExeFsMainPath, current);
            var paths = invalidBase.Paths with { SelectedGame = ProjectGame.Sword };
            var workflow = new SwShIvScreenWorkflowService().Load(
                new ProjectWorkspaceService().Open(paths));
            Assert.Equal("blocked", workflow.InstallStatus);
            Assert.False(workflow.CanUninstall);
        }

        foreach (var exactInstall in new[] { current, legacy })
        {
            using var generated = CreateHookProject(ProjectGame.Sword);
            generated.WriteOutputFile(SwShIvScreenWorkflowService.ExeFsMainPath, exactInstall);
            var paths = generated.Paths with { SelectedGame = ProjectGame.Sword };
            var workflow = new SwShIvScreenWorkflowService().Load(
                new ProjectWorkspaceService().Open(paths));
            Assert.Equal("installed", workflow.InstallStatus);
            Assert.True(workflow.CanUninstall);
        }
    }

    [Fact]
    public void IvScreenWorkflowBlocksIncompatibleBaseAndEffectiveNsoIdentity()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var baseMain = CreateSharedHookNso(ProjectGame.Sword);
        var incompatible = baseMain.ToArray();
        incompatible[0x70] ^= 0xFF;
        temp.WriteBaseExeFsFile("main", baseMain);
        temp.WriteOutputFile("exefs/main", incompatible);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };

        var workflow = new SwShIvScreenWorkflowService().Load(
            new ProjectWorkspaceService().Open(paths));
        var stage = new SwShIvScreenEditSessionService().StageInstall(paths, session: null);

        Assert.Equal("blocked", workflow.InstallStatus);
        Assert.Contains(workflow.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("stable NSO header metadata", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(stage.Session.PendingEdits);
    }

    [Fact]
    public void IvScreenRequiresOneCanonicalPendingEditAndSourceFingerprint()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShIvScreenEditSessionService();
        var stage = service.StageInstall(paths, session: null);
        var stagedEdit = Assert.Single(stage.Session.PendingEdits);

        Assert.Contains(stagedEdit.Sources, source => source.Layer == ProjectFileLayer.Base);
        Assert.Contains(stagedEdit.Sources, source => source.Layer == ProjectFileLayer.Pending);
        Assert.DoesNotContain(stagedEdit.Sources, source => source.Layer == ProjectFileLayer.Generated);

        var duplicateSession = stage.Session with
        {
            PendingEdits = [stagedEdit, stagedEdit],
        };
        Assert.False(service.Validate(paths, duplicateSession).IsValid);

        var tamperedSession = stage.Session with
        {
            PendingEdits =
            [
                stagedEdit with
                {
                    Sources = [new ProjectFileReference(ProjectFileLayer.Base, SwShIvScreenWorkflowService.ExeFsMainPath)],
                },
            ],
        };
        Assert.False(service.Validate(paths, tamperedSession).IsValid);

        foreach (var tamperedEdit in new[]
        {
            stagedEdit with { Summary = "tampered" },
            stagedEdit with { NewValue = "TRUE" },
            stagedEdit with { RecordId = "iv-screen-other" },
            stagedEdit with { Field = "other" },
        })
        {
            var session = stage.Session with { PendingEdits = [tamperedEdit] };
            Assert.False(service.Validate(paths, session).IsValid);
        }

        var outputPath = OutputPath(paths, SwShIvScreenWorkflowService.ExeFsMainPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllBytes(
            outputPath,
            SwShIvScreenMainPatcher.Apply(CreateSharedHookNso(ProjectGame.Sword), ProjectGame.Sword));
        var layeredStage = new SwShIvScreenEditSessionService().StageInstall(paths, session: null);
        var layeredSources = Assert.Single(layeredStage.Session.PendingEdits).Sources;
        Assert.Equal(
            [ProjectFileLayer.Base, ProjectFileLayer.Layered, ProjectFileLayer.Pending],
            layeredSources.Select(source => source.Layer).ToArray());
    }

    [Fact]
    public void IvScreenApplyRejectsSourcesChangedAfterPlanReview()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var service = new SwShIvScreenEditSessionService();
        var stage = service.StageInstall(paths, session: null);
        var plan = service.CreateChangePlan(paths, stage.Session);
        Assert.True(plan.CanApply);

        var nso = NsoFile.Parse(CreateSharedHookNso(ProjectGame.Sword));
        var text = nso.Text.DecompressedData.ToArray();
        text[0x1000] = 0x7A;
        temp.WriteBaseExeFsFile("main", nso.Write(textDecompressedData: text));

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(OutputPath(paths, SwShIvScreenWorkflowService.ExeFsMainPath)));
    }

    [Fact]
    public void IvScreenLatePromotionCollisionPreservesConcurrentOutput()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        byte[] concurrentOutput = [0x43, 0x4F, 0x4E, 0x43, 0x55, 0x52, 0x52, 0x45, 0x4E, 0x54];
        var service = new SwShIvScreenEditSessionService(
            projectWorkspaceService: null,
            ivScreenWorkflowService: null,
            beforeVerifiedPromotion: (_, _) =>
            {
                var outputPath = OutputPath(paths, SwShIvScreenWorkflowService.ExeFsMainPath);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllBytes(outputPath, concurrentOutput);
            });
        var stage = service.StageInstall(paths, session: null);
        var plan = service.CreateChangePlan(paths, stage.Session);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);

        Assert.Contains(apply.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Message.Contains("changed before verified promotion", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(apply.WrittenFiles);
        Assert.Equal(
            concurrentOutput,
            File.ReadAllBytes(OutputPath(paths, SwShIvScreenWorkflowService.ExeFsMainPath)));
    }

    [Fact]
    public void IvScreenRejectsMissingSelectedGameAcrossEditLifecycle()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var validPaths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        var missingGamePaths = validPaths with { SelectedGame = null };
        var service = new SwShIvScreenEditSessionService();
        var staged = service.StageInstall(validPaths, session: null);
        var reviewedPlan = service.CreateChangePlan(validPaths, staged.Session);

        var missingGameStage = service.StageInstall(missingGamePaths, session: null);
        var validation = service.Validate(missingGamePaths, staged.Session);
        var apply = service.ApplyChangePlan(missingGamePaths, staged.Session, reviewedPlan);

        Assert.Empty(missingGameStage.Session.PendingEdits);
        Assert.Contains(missingGameStage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(OutputPath(validPaths, SwShIvScreenWorkflowService.ExeFsMainPath)));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void CatchCapRestoreRejectsDamagedProtectedEpilogue(ProjectGame game)
    {
        int[] caps = [18, 22, 27, 33, 38, 44, 49, 60, 100];
        var baseMain = CreateSharedHookNso(game);
        var patched = SwShCatchCapMainPatcher.Apply(baseMain, caps, game);
        var nso = NsoFile.Parse(patched);
        var text = nso.Text.DecompressedData.ToArray();
        var returnOffset = game == ProjectGame.Shield
            ? SwShCatchCapMainPatcher.ShieldExeFsReturnOffset
            : SwShCatchCapMainPatcher.ExeFsReturnOffset;
        WriteInstruction(text, returnOffset + 4, EncodeNop());
        var damaged = nso.Write(textDecompressedData: text);

        Assert.Equal(
            SwShCatchCapInstallKind.Conflict,
            SwShCatchCapMainPatcher.Analyze(damaged, game).Kind);
        Assert.Throws<InvalidDataException>(() =>
            SwShCatchCapMainPatcher.RestoreFromBase(damaged, baseMain, game));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void CatchCapRejectsNonCanonicalFullBuildIdentity(ProjectGame game)
    {
        int[] caps = [18, 22, 27, 33, 38, 44, 49, 60, 100];
        var baseMain = CreateSharedHookNso(game);
        var patched = SwShCatchCapMainPatcher.Apply(baseMain, caps, game);
        var nonCanonicalMain = baseMain.ToArray();
        nonCanonicalMain[0x40 + 0x1F] = 0x01;

        var analysis = SwShCatchCapMainPatcher.Analyze(nonCanonicalMain, game);
        Assert.Equal(SwShCatchCapInstallKind.UnsupportedBuild, analysis.Kind);
        Assert.Equal(game == ProjectGame.Shield ? ShieldBuildId : SwordBuildId, analysis.BuildId);
        Assert.Throws<InvalidDataException>(() =>
            SwShCatchCapMainPatcher.Apply(nonCanonicalMain, caps, game));

        using var temp = CreateHookProject(game);
        temp.WriteBaseExeFsFile("main", nonCanonicalMain);
        var paths = temp.Paths with { SelectedGame = game };
        var workflow = new SwShCatchCapWorkflowService().Load(new ProjectWorkspaceService().Open(paths));
        Assert.Equal("blocked", workflow.InstallStatus);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("build ID", StringComparison.OrdinalIgnoreCase));

        Assert.Throws<InvalidDataException>(() =>
            SwShCatchCapMainPatcher.RestoreFromBase(patched, nonCanonicalMain, game));
    }

    [Theory]
    [InlineData(ProjectGame.Sword)]
    [InlineData(ProjectGame.Shield)]
    public void CatchCapApplyAndRestorePreserveOutsideOwnedTextAndOtherSegments(ProjectGame game)
    {
        const int outsideOffset = 0x1000;
        int[] caps = [18, 22, 27, 33, 38, 44, 49, 60, 100];
        var initialNso = NsoFile.Parse(CreateSharedHookNso(game));
        var baseText = initialNso.Text.DecompressedData.ToArray();
        var baseRo = initialNso.Ro.DecompressedData.ToArray();
        var baseData = initialNso.Data.DecompressedData.ToArray();
        baseText[outsideOffset] = 0x5A;
        baseRo[0] = 0x66;
        baseData[0] = 0x77;
        var baseMain = initialNso.Write(
            textDecompressedData: baseText,
            roDecompressedData: baseRo,
            dataDecompressedData: baseData);

        var patched = SwShCatchCapMainPatcher.Apply(baseMain, caps, game);
        var patchedNso = NsoFile.Parse(patched);
        Assert.Equal(0x5A, patchedNso.Text.DecompressedData[outsideOffset]);
        Assert.Equal(0x66, patchedNso.Ro.DecompressedData[0]);
        Assert.Equal(0x77, patchedNso.Data.DecompressedData[0]);

        var currentText = patchedNso.Text.DecompressedData.ToArray();
        var currentRo = patchedNso.Ro.DecompressedData.ToArray();
        var currentData = patchedNso.Data.DecompressedData.ToArray();
        currentText[outsideOffset + 1] = 0xA5;
        currentRo[0] = 0x88;
        currentData[0] = 0x99;
        var currentMain = patchedNso.Write(
            textDecompressedData: currentText,
            roDecompressedData: currentRo,
            dataDecompressedData: currentData);

        var restored = SwShCatchCapMainPatcher.RestoreFromBase(currentMain, baseMain, game);
        var restoredNso = NsoFile.Parse(restored);
        Assert.Equal(0x5A, restoredNso.Text.DecompressedData[outsideOffset]);
        Assert.Equal(0xA5, restoredNso.Text.DecompressedData[outsideOffset + 1]);
        Assert.Equal(0x88, restoredNso.Ro.DecompressedData[0]);
        Assert.Equal(0x99, restoredNso.Data.DecompressedData[0]);
        Assert.Equal(SwShCatchCapInstallKind.NotInstalled, SwShCatchCapMainPatcher.Analyze(restored, game).Kind);
    }

    [Fact]
    public void CatchCapAnalysisNormalizesStaleEighthBadgeMetadata()
    {
        var patched = SwShCatchCapMainPatcher.Apply(
            CreateSharedHookNso(),
            Enumerable.Range(0, SwShCatchCapMainPatcher.CapCount)
                .Select(index => index == SwShCatchCapMainPatcher.FinalBadgeCount ? 100 : 20 + index * 5)
                .ToArray());
        var nso = NsoFile.Parse(patched);
        var text = nso.Text.DecompressedData.ToArray();
        text[SwShCatchCapMainPatcher.ExeFsTableOffset + SwShCatchCapMainPatcher.FinalBadgeCount] = 33;

        var analysis = SwShCatchCapMainPatcher.Analyze(nso.Write(textDecompressedData: text));

        Assert.Equal(SwShCatchCapInstallKind.InstalledV1, analysis.Kind);
        Assert.Equal(100, analysis.Caps[SwShCatchCapMainPatcher.FinalBadgeCount]);
        Assert.Contains("stale Lv.33 metadata", analysis.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CatchCapApplyPatchesRuntimeCaptureGate()
    {
        int[] caps = [18, 22, 27, 33, 38, 44, 49, 60, 100];

        var patched = SwShCatchCapMainPatcher.Apply(CreateSharedHookNso(), caps);

        var text = NsoFile.Parse(patched).Text.DecompressedData;
        AssertRuntimeCatchCapHook(text);
        Assert.Equal((byte)33, text[SwShCatchCapMainPatcher.ExeFsTableOffset + 3]);
    }

    [Fact]
    public void CatchCapApplyUpgradesLegacyDisplayOnlyHook()
    {
        int[] caps = [18, 22, 27, 33, 38, 44, 49, 60, 100];
        var patched = SwShCatchCapMainPatcher.Apply(CreateSharedHookNso(), caps);
        var nso = NsoFile.Parse(patched);
        var text = nso.Text.DecompressedData.ToArray();
        WriteCatchCapRuntimeVanillaFormula(text);
        var legacy = nso.Write(textDecompressedData: text);

        var legacyAnalysis = SwShCatchCapMainPatcher.Analyze(legacy);
        Assert.Equal(SwShCatchCapInstallKind.InstalledV1, legacyAnalysis.Kind);
        Assert.Contains("legacy display-only hook", legacyAnalysis.Message, StringComparison.Ordinal);

        var upgraded = SwShCatchCapMainPatcher.Apply(legacy, caps);

        AssertRuntimeCatchCapHook(NsoFile.Parse(upgraded).Text.DecompressedData);
    }

    [Fact]
    public void CatchCapStagesCustomCapsBeforeFinalBadge()
    {
        using var temp = CreateHookProject(ProjectGame.Sword);
        var paths = temp.Paths with { SelectedGame = ProjectGame.Sword };
        int[] caps = [18, 22, 27, 33, 38, 44, 49, 60, 100];
        var service = new SwShCatchCapEditSessionService();

        var stage = service.StageCaps(
            paths,
            caps.Select((cap, index) => new SwShCatchCapSelection(index, cap)).ToArray(),
            session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var plan = service.CreateChangePlan(paths, stage.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var analysis = SwShCatchCapMainPatcher.Analyze(
            File.ReadAllBytes(OutputPath(paths, SwShCatchCapWorkflowService.ExeFsMainPath)));
        Assert.Equal(SwShCatchCapInstallKind.InstalledV1, analysis.Kind);
        Assert.Equal(caps.Select(cap => (byte)cap), analysis.Caps);
        Assert.Equal("badge_count < 8 ? cap_table[badge_count] : 100", analysis.LogicExpression);
    }

    private static bool IsSameFeatureFamily(string leftOwner, string rightOwner)
    {
        return IsRoyalCandyFamily(leftOwner) && IsRoyalCandyFamily(rightOwner);
    }

    private static bool IsMutuallyExclusiveShinyRateLayout(
        SwShExeFsReservedRegion left,
        SwShExeFsReservedRegion right)
    {
        return string.Equals(left.Owner, SwShExeFsReservedRegionLedger.OwnerShinyRate, StringComparison.Ordinal)
            && string.Equals(right.Owner, SwShExeFsReservedRegionLedger.OwnerShinyRate, StringComparison.Ordinal)
            && ((left.FeatureId.Contains("-sword-", StringComparison.Ordinal)
                    && right.FeatureId.Contains("-shield-", StringComparison.Ordinal))
                || (left.FeatureId.Contains("-shield-", StringComparison.Ordinal)
                    && right.FeatureId.Contains("-sword-", StringComparison.Ordinal)));
    }

    private static bool IsMutuallyExclusiveCatchCapLayout(
        SwShExeFsReservedRegion left,
        SwShExeFsReservedRegion right)
    {
        return left.Owner == SwShExeFsReservedRegionLedger.OwnerCatchCap
            && right.Owner == SwShExeFsReservedRegionLedger.OwnerCatchCap
            && left.FeatureId.Contains("-shield-", StringComparison.Ordinal)
                != right.FeatureId.Contains("-shield-", StringComparison.Ordinal);
    }

    private static bool IsRoyalCandyFamily(string owner)
    {
        return owner is SwShExeFsReservedRegionLedger.OwnerRoyalCandy
            or SwShExeFsReservedRegionLedger.OwnerRoyalCandyStoryLimits;
    }

    private static TemporarySwShProject CreateHookProject(ProjectGame game)
    {
        var temp = SwShPerformanceFixtureProject.Create();
        temp.WriteBaseRomFsFile("bin/script/amx/main_event_0020.amx", CreateVanillaBagEventScript());
        temp.WriteBaseRomFsFile(
            SwShRoyalCandyWorkflowService.ShopDataPath["romfs/".Length..],
            new SwShShopDataFile(
                [new SwShSingleShopRecord(0x1F3FF031A3A24490UL, new SwShShopInventory([50, 1128, 51]))],
                [new SwShMultiShopRecord(
                    0x66CA73B2966BB871UL,
                    [
                        new SwShShopInventory([1, 1128, 2]),
                        new SwShShopInventory([3, 4]),
                    ])])
                .Write());
        temp.WriteBaseExeFsFile("main", CreateSharedHookNso(game));
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(game == ProjectGame.Sword ? SwordTitleId : ShieldTitleId));
        return temp;
    }

    private static TemporarySwShProject CreateHookProjectWithFpsAnchors(ProjectGame game)
    {
        var temp = CreateHookProject(game);
        temp.WriteBaseExeFsFile("main", CreateSharedHookNsoWithFpsAnchors(game));
        return temp;
    }

    private static void InstallEmptyBagHook(ProjectPaths paths)
    {
        var service = new SwShBagHookEditSessionService();
        var stage = service.StageInstall(paths, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var plan = service.CreateChangePlan(paths, stage.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static void ApplyFpsPatchMain(ProjectPaths paths)
    {
        var targetPath = OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath);
        var sourcePath = File.Exists(targetPath)
            ? targetPath
            : Path.Combine(paths.BaseExeFsPath!, "main");
        var output = SwShFpsMainPatcher.Apply(File.ReadAllBytes(sourcePath), paths.SelectedGame);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllBytes(targetPath, output);
    }

    private static void ApplyStartingItems(ProjectPaths paths)
    {
        var service = new SwShStartingItemsEditSessionService();
        var stage = service.StageGrants(
            paths,
            [new SwShStartingItemGrantSelection(2, 50, 3)],
            session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var plan = service.CreateChangePlan(paths, stage.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static void ApplyCatchCap(ProjectPaths paths)
    {
        var service = new SwShCatchCapEditSessionService();
        var stage = service.StageCaps(
            paths,
            Enumerable.Range(0, SwShCatchCapMainPatcher.CapCount)
                .Select(index => new SwShCatchCapSelection(index, index == 8 ? 100 : 20 + index * 5))
                .ToArray(),
            session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var plan = service.CreateChangePlan(paths, stage.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static void ApplyCatchCapCleanup(ProjectPaths paths)
    {
        var service = new SwShCatchCapEditSessionService();
        var stage = service.StageUninstall(paths, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var plan = service.CreateChangePlan(paths, stage.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static void ApplyIvScreen(ProjectPaths paths)
    {
        var service = new SwShIvScreenEditSessionService();
        var stage = service.StageInstall(paths, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var plan = service.CreateChangePlan(paths, stage.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static void ApplyIvScreenCleanup(ProjectPaths paths)
    {
        var service = new SwShIvScreenEditSessionService();
        var stage = service.StageUninstall(paths, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var plan = service.CreateChangePlan(paths, stage.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static void ApplyGymUniformRemoval(ProjectPaths paths)
    {
        var service = new SwShGymUniformRemovalEditSessionService();
        var stage = service.StageInstall(paths, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var plan = service.CreateChangePlan(paths, stage.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static void ApplyGymUniformRemovalCleanup(ProjectPaths paths)
    {
        var service = new SwShGymUniformRemovalEditSessionService();
        var stage = service.StageUninstall(paths, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var plan = service.CreateChangePlan(paths, stage.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static void ApplyFashionUnlock(ProjectPaths paths)
    {
        var service = new SwShFashionUnlockEditSessionService();
        var stage = service.StageInstall(paths, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var plan = service.CreateChangePlan(paths, stage.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static void ApplyFashionUnlockCleanup(ProjectPaths paths)
    {
        var service = new SwShFashionUnlockEditSessionService();
        var stage = service.StageUninstall(paths, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var plan = service.CreateChangePlan(paths, stage.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static void ApplyHyperTrainingMain(ProjectPaths paths, int minimumLevel)
    {
        var targetPath = OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath);
        var sourcePath = File.Exists(targetPath)
            ? targetPath
            : Path.Combine(paths.BaseExeFsPath!, "main");
        var output = SwShHyperTrainingMainPatcher.ApplyMinimumLevel(
            File.ReadAllBytes(sourcePath),
            minimumLevel,
            paths.SelectedGame);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllBytes(targetPath, output);
    }

    private static void AssertGymUniformIpsInstalled(ProjectPaths paths, ProjectGame game)
    {
        var ipsRelativePath = SwShGymUniformRemovalMainPatcher.IpsRelativePath(game);
        var ipsPath = OutputPath(paths, ipsRelativePath);
        Assert.True(File.Exists(ipsPath));
        var ipsBytes = File.ReadAllBytes(ipsPath);
        var sourceMain = File.ReadAllBytes(Path.Combine(paths.BaseExeFsPath!, "main"));
        var analysis = SwShGymUniformRemovalMainPatcher.AnalyzeIpsPatch(ipsBytes, sourceMain, game);

        Assert.Equal(SwShGymUniformRemovalInstallKind.InstalledV1, analysis.Kind);
        Assert.Equal("IPS32", System.Text.Encoding.ASCII.GetString(ipsBytes.AsSpan(0, 5)));
        Assert.Equal("EEOF", System.Text.Encoding.ASCII.GetString(ipsBytes.AsSpan(ipsBytes.Length - 4, 4)));
        Assert.Equal(23, ipsBytes.Length);
        Assert.Equal(ExpectedGymUniformIpsBytes(game), ipsBytes);
    }

    private static byte[] ExpectedGymUniformIpsBytes(ProjectGame game)
    {
        return Convert.FromHexString(game == ProjectGame.Shield
            ? "4950533332014726300008E0030032C0035FD645454F46"
            : "4950533332014726000008E0030032C0035FD645454F46");
    }

    private static void ApplyRoyalCandy(ProjectPaths paths, string workflowId)
    {
        var service = new SwShRoyalCandyEditSessionService();
        var stage = service.StageWorkflow(paths, workflowId, levelCaps: null, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var plan = service.CreateChangePlan(paths, stage.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static void ApplyRoyalCandyCleanup(ProjectPaths paths)
    {
        var service = new SwShRoyalCandyEditSessionService();
        var stage = service.StageWorkflow(paths, RoyalCandyUninstallWorkflowId, levelCaps: null, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var plan = service.CreateChangePlan(paths, stage.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static void ApplyBagHookCleanup(ProjectPaths paths)
    {
        var service = new SwShBagHookEditSessionService();
        var stage = service.StageUninstall(paths, session: null);
        Assert.DoesNotContain(stage.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var plan = service.CreateChangePlan(paths, stage.Session);
        Assert.True(plan.CanApply);

        var apply = service.ApplyChangePlan(paths, stage.Session, plan);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static SwShRoyalCandyExeFsSignatureKind ExpectedRoyalCandySignature(string workflowId)
    {
        return workflowId == RoyalCandyStoryLimitsWorkflowId
            ? SwShRoyalCandyExeFsSignatureKind.StoryLimits
            : SwShRoyalCandyExeFsSignatureKind.Unlimited;
    }

    private static IReadOnlyDictionary<string, byte[]> WriteUnownedRoyalCandyCandidateRomFsFiles(TemporarySwShProject temp)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [SwShRoyalCandyWorkflowService.ItemPath] = [0xA0, 0x01, 0x02, 0x03],
            [SwShRoyalCandyWorkflowService.ItemHashPath] = [0xB0, 0x01, 0x02, 0x03],
            [SwShRoyalCandyWorkflowService.ShopDataPath] = [0xC0, 0x01, 0x02, 0x03],
            [SwShRoyalCandyWorkflowService.NestDataPath] = [0xD0, 0x01, 0x02, 0x03],
            [SwShRoyalCandyWorkflowService.PlacementPath] = [0xE0, 0x01, 0x02, 0x03],
        };

        foreach (var (relativePath, contents) in files)
        {
            temp.WriteOutputFile(relativePath, contents);
        }

        return files;
    }

    private static void AssertUnownedRoyalCandyCandidateRomFsFiles(
        ProjectPaths paths,
        IReadOnlyDictionary<string, byte[]> expectedFiles)
    {
        foreach (var (relativePath, contents) in expectedFiles)
        {
            Assert.True(File.Exists(OutputPath(paths, relativePath)));
            Assert.Equal(contents, File.ReadAllBytes(OutputPath(paths, relativePath)));
        }
    }

    private static void WriteLayeredTextEdit(
        ProjectPaths paths,
        string relativePath,
        int lineIndex,
        string text)
    {
        var targetPath = OutputPath(paths, relativePath);
        var textFile = SwShGameTextFile.Parse(File.ReadAllBytes(targetPath));
        var lines = textFile.Lines.ToArray();
        lines[lineIndex] = lines[lineIndex] with { Text = text };
        File.WriteAllBytes(targetPath, SwShGameTextFile.Write(lines));
    }

    private static void WriteUnownedMainEdit(ProjectPaths paths)
    {
        const int unownedTextOffset = 0x100;
        var targetPath = OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath);
        var main = NsoFile.Parse(File.ReadAllBytes(targetPath));
        var text = main.Text.DecompressedData.ToArray();
        text[unownedTextOffset] = 0x5A;
        File.WriteAllBytes(targetPath, main.Write(textDecompressedData: text));
    }

    private static void AssertUnownedMainEditPreserved(ProjectPaths paths)
    {
        const int unownedTextOffset = 0x100;
        var targetPath = OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath);
        Assert.True(File.Exists(targetPath));
        var main = NsoFile.Parse(File.ReadAllBytes(targetPath));
        Assert.Equal(0x5A, main.Text.DecompressedData[unownedTextOffset]);
    }

    private static void AssertRestoredRoyalCandyTextRow(
        ProjectPaths paths,
        string relativePath,
        int editedLineIndex,
        string editedText)
    {
        var targetText = SwShGameTextFile.Parse(File.ReadAllBytes(OutputPath(paths, relativePath)));
        var baseText = SwShGameTextFile.Parse(File.ReadAllBytes(BasePath(paths, relativePath)));
        Assert.Equal(editedText, targetText.Lines[editedLineIndex].Text);
        Assert.Equal(baseText.Lines[SwShBagHookAmxPatcher.RoyalCandyItemId].Text, targetText.Lines[SwShBagHookAmxPatcher.RoyalCandyItemId].Text);
    }

    private static string OutputPath(ProjectPaths paths, string relativePath)
    {
        return Path.Combine(paths.OutputRootPath!, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static IReadOnlyDictionary<string, byte[]> SnapshotOutputTree(string outputRootPath)
    {
        return Directory.EnumerateFiles(outputRootPath, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(outputRootPath, path).Replace('\\', '/'),
                File.ReadAllBytes,
                StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertOutputTreeMatches(
        string outputRootPath,
        IReadOnlyDictionary<string, byte[]> expected)
    {
        var actualPaths = Directory.EnumerateFiles(outputRootPath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(outputRootPath, path).Replace('\\', '/'))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(expected.Keys.Order(StringComparer.OrdinalIgnoreCase), actualPaths);
        Assert.All(
            expected,
            output => Assert.Equal(
                output.Value,
                File.ReadAllBytes(Path.Combine(
                    outputRootPath,
                    output.Key.Replace('/', Path.DirectorySeparatorChar)))));
    }

    private static string BasePath(ProjectPaths paths, string relativePath)
    {
        return Path.Combine(paths.BaseRomFsPath!, relativePath["romfs/".Length..].Replace('/', Path.DirectorySeparatorChar));
    }

    private static byte[] CreateSharedHookNso(ProjectGame game = ProjectGame.Sword)
    {
        var cached = game == ProjectGame.Shield
            ? ShieldSharedHookNso.Value
            : SwordSharedHookNso.Value;
        return cached.ToArray();
    }

    private static byte[] CreateSharedHookNsoCore(ProjectGame game)
    {
        var text = new byte[0x0157D000];
        WriteRoyalCandyVanillaAnchors(text);
        WriteCatchCapVanillaAnchors(text, game);
        WriteIvScreenVanillaAnchors(text, game);
        WriteGymUniformRemovalVanillaAnchors(text);
        WriteFashionUnlockVanillaAnchors(text);
        WriteHyperTrainingVanillaAnchors(text, game);
        return CreateNso(text, [0x10], [0x20], BuildIdForGame(game));
    }

    private static byte[] CreateSharedHookNsoWithFpsAnchors(ProjectGame game = ProjectGame.Sword)
    {
        var cached = game == ProjectGame.Shield
            ? ShieldSharedHookNsoWithFpsAnchors.Value
            : SwordSharedHookNsoWithFpsAnchors.Value;
        return cached.ToArray();
    }

    private static byte[] CreateSharedHookNsoWithFpsAnchorsCore(ProjectGame game)
    {
        var text = new byte[SwShFpsMainTestAnchors.RequiredTextLength];
        WriteRoyalCandyVanillaAnchors(text);
        WriteCatchCapVanillaAnchors(text, game);
        WriteIvScreenVanillaAnchors(text, game);
        WriteGymUniformRemovalVanillaAnchors(text);
        WriteFashionUnlockVanillaAnchors(text);
        WriteHyperTrainingVanillaAnchors(text, game);
        SwShFpsMainTestAnchors.WriteVanilla(text, game);
        return CreateNso(text, [0x10], [0x20], BuildIdForGame(game));
    }

    private static void WriteHyperTrainingVanillaAnchors(byte[] text, ProjectGame game)
    {
        var offsets = game == ProjectGame.Sword
            ? (Preflight: 0x00F98F18, Eligibility: 0x00F9A314, GrayOut: 0x00F9A334, Detail: 0x00F9E4C0,
                PreflightCall: 0x97DF85B7u, EligibilityCall: 0x97DF80B8u, GrayOutCall: 0x97DF80B0u, DetailCall: 0x97DF704Du)
            : (Preflight: 0x00F98F48, Eligibility: 0x00F9A344, GrayOut: 0x00F9A364, Detail: 0x00F9E4F0,
                PreflightCall: 0x97DF85ABu, EligibilityCall: 0x97DF80ACu, GrayOutCall: 0x97DF80A4u, DetailCall: 0x97DF7041u);
        uint[] getterWords =
        [
            0xF81D0FF5, 0xA9014FF4, 0xA9027BFD, 0x910083FD, 0xAA0003F3,
            0xF9404C00, 0x97FFB4EA, 0x2A0003E8, 0xF9404E60, 0x360000A8,
            0xA9427BFD, 0xA9414FF4, 0xF84307F5, 0x17FFB8BB, 0x97FFB9A6,
            0x2A0003F4, 0xF9404E60, 0x97FFC34B, 0x2A0003F5, 0xF9404E60,
            0x97FFBA90, 0x2A0003E2, 0x2A1403E0, 0x2A1503E1, 0x97FFF188,
            0xA9427BFD, 0x12001C00, 0xA9414FF4, 0xF84307F5, 0xD65F03C0,
        ];
        for (var index = 0; index < getterWords.Length; index++)
        {
            WriteInstruction(text, 0x0077A5F0 + (index * sizeof(uint)), getterWords[index]);
        }

        WriteInstruction(text, offsets.Preflight - 4, offsets.PreflightCall);
        WriteInstruction(text, offsets.Preflight, EncodeCmpImmediate(0, 100));
        WriteInstruction(text, offsets.Preflight + 4, 0x1A9F27E8);
        WriteInstruction(text, offsets.Preflight + 8, 0x54000123);
        WriteInstruction(text, offsets.Eligibility - 4, offsets.EligibilityCall);
        WriteInstruction(text, offsets.Eligibility, EncodeCmpImmediate(0, 100));
        WriteInstruction(text, offsets.Eligibility + 4, 0x54000061);
        WriteInstruction(text, offsets.GrayOut - 4, offsets.GrayOutCall);
        WriteInstruction(text, offsets.GrayOut, EncodeCmpImmediate(0, 100));
        WriteInstruction(text, offsets.GrayOut + 4, 0x540000A1);
        WriteInstruction(text, offsets.Detail - 4, offsets.DetailCall);
        WriteInstruction(text, offsets.Detail, EncodeCmpImmediate(0, 100));
        WriteInstruction(text, offsets.Detail + 4, 0x540002C1);
    }

    private static void WriteRoyalCandyVanillaAnchors(byte[] text)
    {
        WriteInstruction(text, 0x00747988, EncodeCmpImmediate(28, 50));
        WriteInstruction(text, 0x0074798C, EncodeConditionalBranch(0x0074798C, 0x00747A80, Arm64Condition.NE));
        WriteInstruction(text, 0x00747D44, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x00747D48, EncodeConditionalBranch(0x00747D48, 0x007477E8, Arm64Condition.NE));
        WriteInstruction(text, 0x0074BA24, EncodeCmpImmediate(26, 50));
        WriteInstruction(text, 0x0074BA28, EncodeConditionalBranch(0x0074BA28, 0x0074BAD4, Arm64Condition.NE));
        WriteInstruction(text, 0x0074BDA8, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0074BDAC, EncodeConditionalBranch(0x0074BDAC, 0x0074B788, Arm64Condition.NE));
        WriteInstruction(text, 0x0074DFE4, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0074DFE8, EncodeConditionalBranch(0x0074DFE8, 0x0074DE78, Arm64Condition.NE));
        WriteInstruction(text, 0x0074DFF8, EncodeCmpImmediate(28, 50));
        WriteInstruction(text, 0x0074DFFC, EncodeConditionalBranch(0x0074DFFC, 0x0074E16C, Arm64Condition.NE));
        WriteInstruction(text, 0x0075CEFC, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0075CF00, EncodeConditionalBranch(0x0075CF00, 0x0075CC18, Arm64Condition.NE));
        WriteInstruction(text, 0x007BB204, EncodeCmpImmediate(20, 50));
        WriteInstruction(text, 0x007BB208, EncodeConditionalBranch(0x007BB208, 0x007BB26C, Arm64Condition.NE));
        WriteInstruction(text, 0x007BB3C0, EncodeCmpImmediate(19, 50));
        WriteInstruction(text, 0x007BB3C4, EncodeConditionalBranch(0x007BB3C4, 0x007BB3EC, Arm64Condition.NE));
        WriteInstruction(text, 0x007BC1F8, EncodeCmpImmediate(8, 50));
        WriteInstruction(text, 0x007BC1FC, EncodeConditionalBranch(0x007BC1FC, 0x007BC2B4, Arm64Condition.NE));
        WriteInstruction(text, 0x00747DE0, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x00747DE4, EncodeConditionalBranch(0x00747DE4, 0x00747D4C, Arm64Condition.EQ));
        WriteInstruction(text, 0x0074BE44, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0074BE48, EncodeConditionalBranch(0x0074BE48, 0x0074BDB0, Arm64Condition.EQ));
        WriteInstruction(text, 0x0075CCE8, EncodeCmpImmediate(27, 50));
        WriteInstruction(text, 0x0075CCEC, EncodeConditionalBranch(0x0075CCEC, 0x0075D064, Arm64Condition.EQ));
        WriteInstruction(text, 0x0075D08C, EncodeCmpImmediate(10, 50));
        WriteInstruction(text, 0x0075D090, EncodeConditionalBranch(0x0075D090, 0x0075D05C, Arm64Condition.EQ));
        WriteInstruction(text, 0x007BBFD4, EncodeCmpImmediate(23, 50));
        WriteInstruction(text, 0x007BBFD8, EncodeConditionalBranch(0x007BBFD8, 0x007BC054, Arm64Condition.EQ));
        WriteInstruction(text, 0x007BC1BC, EncodeCmpImmediate(9, 4));
        WriteInstruction(text, 0x007BC1C4, EncodeCmpImmediate(9, 4));
        WriteInstruction(text, 0x007B1F20, 0x2A0003E2);
        WriteInstruction(text, 0x007BAF38, 0x6B36231F);
        WriteInstruction(text, 0x007BAF3C, 0x1A963316);
        WriteInstruction(text, 0x007DDA8C, EncodeCmpImmediate(8, 0x32));
        WriteInstruction(text, 0x007DDA90, EncodeConditionalBranch(0x007DDA90, 0x007DDAF8, Arm64Condition.HI));
        WriteInstruction(text, 0x01420EF0, 0xF81D0FF5);
        WriteInstruction(text, 0x01421090, 0xA9BE4FF4);
        WriteInstruction(text, 0x01420F20, 0xF81D0FF5);
        WriteInstruction(text, 0x014210C0, 0xA9BE4FF4);
    }

    private static void WriteCatchCapVanillaAnchors(byte[] text, ProjectGame game = ProjectGame.Sword)
    {
        var hookOffset = game == ProjectGame.Shield
            ? SwShCatchCapMainPatcher.ShieldExeFsHookSiteOffset
            : SwShCatchCapMainPatcher.ExeFsHookSiteOffset;
        var tableOffset = game == ProjectGame.Shield
            ? SwShCatchCapMainPatcher.ShieldExeFsTableOffset
            : SwShCatchCapMainPatcher.ExeFsTableOffset;
        var returnOffset = game == ProjectGame.Shield
            ? SwShCatchCapMainPatcher.ShieldExeFsReturnOffset
            : SwShCatchCapMainPatcher.ExeFsReturnOffset;

        // Sword/Shield has two adjacent catch cap formulas: the first feeds display text, and the
        // second is the runtime capture gate that blocks or allows the throw.
        WriteInstruction(text, hookOffset, 0x0B000809);
        WriteInstruction(text, tableOffset, 0xA9417BFD);
        WriteInstruction(text, tableOffset + 4, 0x12001C08);
        WriteInstruction(text, tableOffset + 8, 0x71001D1F);
        WriteInstruction(text, tableOffset + 0x0C, 0x52800C88);
        WriteInstruction(text, tableOffset + 0x10, 0x11005129);
        WriteInstruction(text, tableOffset + 0x14, 0x1A898100);
        WriteInstruction(text, returnOffset, 0xA8C24FF4);
        WriteInstruction(text, returnOffset + 4, 0xD65F03C0);
        WriteCatchCapRuntimeVanillaFormula(text, game);
    }

    private static void WriteCatchCapRuntimeVanillaFormula(byte[] text, ProjectGame game = ProjectGame.Sword)
    {
        var runtimeOffset = game == ProjectGame.Shield
            ? SwShCatchCapMainPatcher.ShieldExeFsRuntimeHookSiteOffset
            : SwShCatchCapMainPatcher.ExeFsRuntimeHookSiteOffset;
        var runtimeReturnOffset = game == ProjectGame.Shield
            ? SwShCatchCapMainPatcher.ShieldExeFsRuntimeReturnOffset
            : SwShCatchCapMainPatcher.ExeFsRuntimeReturnOffset;

        WriteInstruction(text, runtimeOffset, 0x0B000809);
        WriteInstruction(text, runtimeOffset + 4, 0x12001C08);
        WriteInstruction(text, runtimeOffset + 8, 0x71001D1F);
        WriteInstruction(text, runtimeOffset + 0x0C, 0x52800C88);
        WriteInstruction(text, runtimeOffset + 0x10, 0x11005129);
        WriteInstruction(text, runtimeOffset + 0x14, 0x1A898100);
        WriteInstruction(text, runtimeReturnOffset, 0xA8C17BFD);
        WriteInstruction(text, runtimeReturnOffset + 4, 0xD65F03C0);
    }

    private static void AssertRuntimeCatchCapHook(byte[] text)
    {
        Assert.Equal(EncodeCmpImmediate(0, 8), ReadInstruction(text, SwShCatchCapMainPatcher.ExeFsRuntimeHookSiteOffset));
        Assert.Equal(
            EncodeConditionalBranch(
                SwShCatchCapMainPatcher.ExeFsRuntimeHookSiteOffset + 4,
                SwShCatchCapMainPatcher.ExeFsRuntimeHookSiteOffset + 0x0C,
                Arm64Condition.LS),
            ReadInstruction(text, SwShCatchCapMainPatcher.ExeFsRuntimeHookSiteOffset + 4));
        Assert.Equal(EncodeMovzImmediate32(0, 8), ReadInstruction(text, SwShCatchCapMainPatcher.ExeFsRuntimeHookSiteOffset + 8));
        Assert.Equal(
            EncodeAdr(8, SwShCatchCapMainPatcher.ExeFsRuntimeHookSiteOffset + 0x0C, SwShCatchCapMainPatcher.ExeFsTableOffset),
            ReadInstruction(text, SwShCatchCapMainPatcher.ExeFsRuntimeHookSiteOffset + 0x0C));
        Assert.Equal(
            EncodeLdrbRegisterOffsetUxtw(0, 8, 0),
            ReadInstruction(text, SwShCatchCapMainPatcher.ExeFsRuntimeHookSiteOffset + 0x10));
        Assert.Equal(EncodeNop(), ReadInstruction(text, SwShCatchCapMainPatcher.ExeFsRuntimeHookSiteOffset + 0x14));
        Assert.Equal(0xA8C17BFD, ReadInstruction(text, SwShCatchCapMainPatcher.ExeFsRuntimeReturnOffset));
        Assert.Equal(0xD65F03C0, ReadInstruction(text, SwShCatchCapMainPatcher.ExeFsRuntimeReturnOffset + 4));
    }

    private static void WriteIvScreenVanillaAnchors(byte[] text, ProjectGame game = ProjectGame.Sword)
    {
        var shift = game == ProjectGame.Shield ? 0x30 : 0;
        WriteInstruction(text, ShiftIvOffset(0x0137F634, shift), 0x94001F27);
        WriteInstruction(text, ShiftIvOffset(0x0138F268, shift), 0x9400023E);
        WriteInstruction(text, ShiftIvOffset(0x013872D0, shift), 0xD103C3FF);
        WriteInstruction(text, ShiftIvOffset(0x01385A70, shift), 0xD10143FF);
        WriteInstruction(text, ShiftIvOffset(0x0138F990, shift), 0xA9BC5FF8);
        WriteInstruction(text, ShiftIvOffset(0x0138FB60, shift), 0xD10243FF);
        WriteInstruction(text, ShiftIvOffset(0x0138A1A0, shift), 0xD10503FF);
        WriteInstruction(text, ShiftIvOffset(0x0138B550, shift), 0xA9457BFD);
        WriteInstruction(text, ShiftIvOffset(0x0138B1E0, shift), 0xD10183FF);
        WriteInstruction(text, ShiftIvOffset(0x0138B1FC, shift), 0x39592408);
        WriteInstruction(text, ShiftIvOffset(0x0138B200, shift), 0x52000108);
        WriteInstruction(text, ShiftIvOffset(0x0139FB60, shift), 0x340000A8);
        WriteInstruction(text, ShiftIvOffset(0x013B2F90, shift), 0xD10143FF);
        WriteInstruction(text, ShiftIvOffset(0x013CA220, shift), 0xF81D0FF5);
        WriteInstruction(text, 0x00779070, 0x7100143F);
        WriteInstruction(text, 0x00778E20, 0xA9BF7BFD);
        WriteInstruction(text, 0x007790D0, 0xA9BE4FF4);
        WriteInstruction(text, 0x0077AFD0, 0xF81E0FF3);
        WriteInstruction(text, 0x0077AC70, 0xF81E0FF3);
        WriteInstruction(text, 0x0077AC30, 0x7100143F);
        WriteInstruction(text, 0x00779F50, 0xA9BF7BFD);
        WriteIvScreenCallSiteAnchors(text, game);
    }

    private static byte[] CreateInitialIvScreenLegacyNso(byte[] baseMain)
    {
        var nso = NsoFile.Parse(baseMain);
        var text = nso.Text.DecompressedData.ToArray();
        const int valueWrapperOffset = 0x0138F324;
        const int rawIvGetterOffset = 0x00779070;
        const int renderWrapperOffset = 0x01390204;
        const int renderContinueOffset = 0x01390BE4;
        const int renderReturnOffset = 0x01391114;

        foreach (var offset in new[]
        {
            0x0138FBE8, 0x0138FC38, 0x0138FC74, 0x0138FC9C,
            0x0138FD2C, 0x0138FD5C, 0x0138FD84, 0x0138FEA0,
        })
        {
            WriteInstruction(text, offset, EncodeBranchLink(offset, valueWrapperOffset));
        }

        foreach (var offset in new[]
        {
            0x0138AA50, 0x0138AA60, 0x0138AA90, 0x0138AAA0,
            0x0138AAD0, 0x0138AAE0, 0x0138AB10, 0x0138AB20,
            0x0138AB50, 0x0138AB60, 0x0138AB90, 0x0138ABA0,
        })
        {
            WriteInstruction(text, offset, EncodeBranchLink(offset, rawIvGetterOffset));
        }

        foreach (var offset in new[]
        {
            0x0138AC88, 0x0138ACAC, 0x0138ACD0,
            0x0138ACF8, 0x0138AD1C, 0x0138AD40,
        })
        {
            WriteInstruction(text, offset, 0x2A0003E8);
        }

        WriteInstruction(text, 0x0138B1FC, 0x52800028);
        WriteInstruction(text, 0x0138B200, EncodeNop());
        foreach (var offset in new[] { 0x01392EA8, 0x01393310, 0x0139EF4C })
        {
            WriteInstruction(text, offset, EncodeBranchLink(offset, renderWrapperOffset));
        }

        WriteInstruction(text, 0x0139FB60, 0x14000005);

        (int Offset, uint[] Instructions)[] valueWrappers =
        [
            (0x0138F324, [0x7100003F, 0x54007400, 0x140000F6]),
            (0x0138F704, [0x7100043F, 0x54005500, 0x14000016]),
            (0x0138F764, [0x7100083F, 0x54005200, 0x14000086]),
            (0x0138F984, [0x71000C3F, 0x54003D80, 0x14000072]),
            (0x0138FB54, [0x7100103F, 0x54003280, 0x14000126]),
            (0x0138FFF4, [0x7100143F, 0x54000D80, 0x14000016]),
            (0x01390054, [0x7100183F, 0x54000780, 0x14000002]),
            (0x01390064, [0x71001C3F, 0x54000680, 0x14000032]),
            (0x01390134, [0x17CFA33B, 0x52800001, 0x14000002]),
            (0x01390144, [0x17CFA3CB, 0x52800061, 0x14000016]),
            (0x013901A4, [0x17CFA3B3, 0x17CFA3B2, 0xD503201F]),
        ];
        foreach (var slot in valueWrappers)
        {
            for (var index = 0; index < slot.Instructions.Length; index++)
            {
                WriteInstruction(text, slot.Offset + (index * sizeof(uint)), slot.Instructions[index]);
            }
        }

        WriteInstruction(text, renderWrapperOffset + 0x00, 0xA9BF7BF3);
        WriteInstruction(text, renderWrapperOffset + 0x04, 0xAA0003F3);
        WriteInstruction(text, renderWrapperOffset + 0x08, EncodeBranchLink(renderWrapperOffset + 0x08, 0x0138A1A0));
        WriteInstruction(text, renderContinueOffset + 0x00, 0xAA1303E0);
        WriteInstruction(text, renderContinueOffset + 0x04, EncodeBranchLink(renderContinueOffset + 0x04, 0x0138B1E0));
        WriteInstruction(text, renderContinueOffset + 0x08, EncodeBranch(renderContinueOffset + 0x08, renderReturnOffset));
        WriteInstruction(text, renderReturnOffset + 0x00, 0xA8C17BF3);
        WriteInstruction(text, renderReturnOffset + 0x04, 0xD65F03C0);
        WriteInstruction(text, renderReturnOffset + 0x08, EncodeNop());

        var marker = System.Text.Encoding.ASCII.GetBytes("SWSH_IV_DISPLAY_V1");
        var markerIndex = 0;
        foreach (var markerOffset in new[] { 0x013975B4, 0x01397934 })
        {
            text.AsSpan(markerOffset, 0x0C).Clear();
            var length = Math.Min(0x0C, marker.Length - markerIndex);
            marker.AsSpan(markerIndex, length).CopyTo(text.AsSpan(markerOffset, length));
            markerIndex += length;
        }

        return nso.Write(textDecompressedData: text);
    }

    private static void WriteGymUniformRemovalVanillaAnchors(byte[] text)
    {
        foreach (var offset in new[] { SwShGymUniformRemovalMainPatcher.SwordPatchOffset, SwShGymUniformRemovalMainPatcher.ShieldPatchOffset })
        {
            WriteInstruction(text, offset, 0xD0008CE8);
            WriteInstruction(text, offset + 4, 0xB9400833);
        }
    }

    private static void WriteFashionUnlockVanillaAnchors(byte[] text)
    {
        foreach (var (directOffset, mappedOffset) in new[]
        {
            (SwShFashionUnlockMainPatcher.SwordDirectGetterOffset, SwShFashionUnlockMainPatcher.SwordMappedGetterOffset),
            (SwShFashionUnlockMainPatcher.ShieldDirectGetterOffset, SwShFashionUnlockMainPatcher.ShieldMappedGetterOffset),
        })
        {
            WriteInstruction(text, directOffset, 0xAA0003E8);
            WriteInstruction(text, directOffset + 4, 0x2A1F03E0);
            WriteInstruction(text, mappedOffset, 0xD10603FF);
            WriteInstruction(text, mappedOffset + 4, 0xA9145FFC);
        }
    }

    private static void WriteIvScreenCallSiteAnchors(byte[] text, ProjectGame game = ProjectGame.Sword)
    {
        var shift = game == ProjectGame.Shield ? 0x30 : 0;

        foreach (var (offset, instruction) in new (int Offset, uint Instruction)[]
        {
            (0x0138FBE8, 0x97CFA48E),
            (0x0138FC38, 0x97CFA47A),
            (0x0138FC74, 0x97CFA46B),
            (0x0138FC9C, 0x97CFA461),
            (0x0138FD2C, 0x97CFA43D),
            (0x0138FD5C, 0x97CFA431),
            (0x0138FD84, 0x97CFA427),
            (0x0138FEA0, 0x97CFA3E0),
            (0x0138A2B4, 0x97CFC347),
            (0x0138A3CC, 0x97CFC229),
            (0x0138A47C, 0x97CFC1ED),
            (0x0138A518, 0x97CFC1C6),
            (0x0138A5B4, 0x97CFC19F),
            (0x0138A650, 0x97CFC178),
            (0x0138A6F0, 0x97CFC150),
            (0x0138AA50, 0x97CFBD40),
            (0x0138AA60, 0x97CFC074),
            (0x0138AA90, 0x97CFBD30),
            (0x0138AAA0, 0x97CFC064),
            (0x0138AAD0, 0x97CFBD20),
            (0x0138AAE0, 0x97CFC054),
            (0x0138AB10, 0x97CFBD10),
            (0x0138AB20, 0x97CFC044),
            (0x0138AB50, 0x97CFBD00),
            (0x0138AB60, 0x97CFC034),
            (0x0138AB90, 0x97CFBCF0),
            (0x0138ABA0, 0x97CFC024),
            (0x0138AE28, 0x97CFBE2E),
            (0x0138AE3C, 0x97CFBE29),
            (0x0138AE50, 0x97CFBE24),
            (0x0138AE64, 0x97CFBE1F),
            (0x0138AE78, 0x97CFBE1A),
            (0x0138AE8C, 0x97CFBE15),
        })
        {
            WriteInstruction(
                text,
                ShiftIvOffset(offset, shift),
                EncodeBranchLink(ShiftIvOffset(offset, shift), DecodeBranchTarget(instruction, offset)));
        }

        foreach (var (offset, instruction) in new (int Offset, uint Instruction)[]
        {
            (0x0138AC88, 0x0B130008),
            (0x0138ACAC, 0x0B130008),
            (0x0138ACD0, 0x0B130008),
            (0x0138ACF8, 0x0B170008),
            (0x0138AD1C, 0x0B130008),
            (0x0138AD40, 0x0B130008),
            (0x0138AEAC, 0x2A1F03E8),
            (0x0138AEB0, 0x7103F27F),
            (0x0138AEB4, 0x54000063),
            (0x0138AEB8, 0x39592688),
            (0x0138AEBC, 0x52000108),
            (0x0138AEE0, 0x7103EF1F),
            (0x0138AEE4, 0x54000089),
            (0x0138AEE8, 0x39592688),
            (0x0138AEEC, 0x52000108),
            (0x0138AEF0, 0x14000002),
            (0x0138AEF4, 0x2A1F03E8),
            (0x0138AF18, 0x7103F2FF),
            (0x0138AF1C, 0x54000083),
            (0x0138AF20, 0x39592688),
            (0x0138AF24, 0x52000108),
            (0x0138AF28, 0x14000002),
            (0x0138AF2C, 0x2A1F03E8),
            (0x0138AF54, 0x7103F39F),
            (0x0138AF58, 0x54000083),
            (0x0138AF5C, 0x39592688),
            (0x0138AF60, 0x52000108),
            (0x0138AF64, 0x14000002),
            (0x0138AF68, 0x2A1F03E8),
            (0x0138AF8C, 0x7103F37F),
            (0x0138AF90, 0x54000083),
            (0x0138AF94, 0x39592688),
            (0x0138AF98, 0x52000108),
            (0x0138AF9C, 0x14000002),
            (0x0138AFA0, 0x2A1F03E8),
            (0x0138AFC4, 0x7103F33F),
            (0x0138AFC8, 0x54000083),
            (0x0138AFCC, 0x39592688),
            (0x0138AFD0, 0x52000108),
            (0x0138AFD4, 0x14000002),
            (0x0138AFD8, 0x2A1F03E8),
            (0x0138B230, 0x39592668),
            (0x0138B264, 0x39592668),
            (0x0138B298, 0x39592668),
            (0x0138B2CC, 0x39592668),
            (0x0138B300, 0x39592668),
            (0x0138B334, 0x39592668),
            (0x0138B368, 0x39592668),
            (0x0138B39C, 0x39592668),
            (0x0138B3AC, 0xF942EE60),
            (0x0138B3B0, 0x97FFE9B0),
            (0x0138B3B4, 0x2A1F03E1),
            (0x01392EA8, 0x97FFDCBE),
            (0x01393310, 0x97FFDBA4),
            (0x0139EF4C, 0x97FFAC95),
        })
        {
            WriteInstruction(text, ShiftIvOffset(offset, shift), instruction);
        }
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, sizeof(uint)), instruction);
    }

    private static uint ReadInstruction(byte[] text, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(offset, sizeof(uint)));
    }

    private static int GymUniformRemovalPatchOffset(ProjectGame game)
    {
        return game == ProjectGame.Shield
            ? SwShGymUniformRemovalMainPatcher.ShieldPatchOffset
            : SwShGymUniformRemovalMainPatcher.SwordPatchOffset;
    }

    private static int FashionUnlockDirectGetterOffset(ProjectGame game)
    {
        return game == ProjectGame.Shield
            ? SwShFashionUnlockMainPatcher.ShieldDirectGetterOffset
            : SwShFashionUnlockMainPatcher.SwordDirectGetterOffset;
    }

    private static int FashionUnlockMappedGetterOffset(ProjectGame game)
    {
        return game == ProjectGame.Shield
            ? SwShFashionUnlockMainPatcher.ShieldMappedGetterOffset
            : SwShFashionUnlockMainPatcher.SwordMappedGetterOffset;
    }

    private static bool IsFashionUnlockOwnedOffset(ProjectGame game, int offset)
    {
        var directOffset = FashionUnlockDirectGetterOffset(game);
        var mappedOffset = FashionUnlockMappedGetterOffset(game);
        return offset >= directOffset && offset < directOffset + SwShFashionUnlockMainPatcher.PatchLength
            || offset >= mappedOffset && offset < mappedOffset + SwShFashionUnlockMainPatcher.PatchLength;
    }

    private static IReadOnlyList<SwShRoyalCandyStoryLevelCap> CreateRoyalCandyTestStoryCaps()
    {
        return
        [
            new SwShRoyalCandyStoryLevelCap(
                20,
                0x0FEDCBA987654321UL,
                "Flag milestone"),
            new SwShRoyalCandyStoryLevelCap(
                35,
                0x123456789ABCDEF0UL,
                "Work milestone",
                SwShRoyalCandyStoryLevelCapProgressKind.WorkAtLeast,
                WorkMinimum: 530),
        ];
    }

    private static byte[] ApplyRoyalCandyMainPatch(byte[] baseMain, string workflowId, ProjectGame game)
    {
        return workflowId switch
        {
            RoyalCandyUnlimitedWorkflowId => SwShExeFsRoyalCandyMainPatcher.ApplyBasePatch(baseMain, game),
            RoyalCandyStoryLimitsWorkflowId => SwShExeFsRoyalCandyMainPatcher.ApplyStoryLimitsPatch(
                baseMain,
                CreateRoyalCandyTestStoryCaps(),
                game),
            _ => throw new ArgumentOutOfRangeException(nameof(workflowId), workflowId, "Unknown Royal Candy workflow."),
        };
    }

    private static void AssertRoyalCandyVirtualInventoryHelper(
        byte[] text,
        int hookOffset,
        uint expectedFirstInstruction,
        int expectedReturnValue)
    {
        var dispatchOffset = DecodeBranchTarget(ReadInstruction(text, hookOffset), hookOffset);
        var returnOffset = DecodeConditionalBranchTarget(ReadInstruction(text, dispatchOffset + 4), dispatchOffset + 4);
        var vanillaOffset = DecodeBranchTarget(ReadInstruction(text, dispatchOffset + 8), dispatchOffset + 8);

        Assert.Equal(EncodeCmpImmediate(1, 1128), ReadInstruction(text, dispatchOffset));
        Assert.Equal(EncodeConditionalBranch(dispatchOffset + 4, returnOffset, Arm64Condition.EQ), ReadInstruction(text, dispatchOffset + 4));
        Assert.Equal(EncodeBranch(dispatchOffset + 8, vanillaOffset), ReadInstruction(text, dispatchOffset + 8));
        Assert.Equal(EncodeMovzImmediate32(0, expectedReturnValue), ReadInstruction(text, returnOffset));
        Assert.Equal(0xD65F03C0u, ReadInstruction(text, returnOffset + 4));
        Assert.Equal(expectedFirstInstruction, ReadInstruction(text, vanillaOffset));
        Assert.Equal(EncodeBranch(vanillaOffset + 4, hookOffset + 4), ReadInstruction(text, vanillaOffset + 4));
    }

    private static int[] ChangedTextOffsets(byte[] before, byte[] after)
    {
        Assert.Equal(before.Length, after.Length);
        return Enumerable.Range(0, before.Length)
            .Where(index => before[index] != after[index])
            .ToArray();
    }

    private static int[] ReadRoyalCandyStoryAccessorTargets(byte[] text, int expectedCount)
    {
        var targets = new List<int>(expectedCount);
        var offset = ResolveRoyalCandyStoryHelperOffset(text);
        var visited = new HashSet<int>();
        while (targets.Count < expectedCount && visited.Add(offset))
        {
            var loadTableOffset = DecodeBranchTarget(ReadInstruction(text, offset + 8), offset + 8);
            var hashLowOffset = DecodeBranchTarget(ReadInstruction(text, loadTableOffset + 8), loadTableOffset + 8);
            var hashHighOffset = DecodeBranchTarget(ReadInstruction(text, hashLowOffset + 8), hashLowOffset + 8);
            var callOffset = DecodeBranchTarget(ReadInstruction(text, hashHighOffset + 8), hashHighOffset + 8);
            targets.Add(DecodeBranchTarget(ReadInstruction(text, callOffset + 4), callOffset + 4));

            var restoreOffset = DecodeBranchTarget(ReadInstruction(text, callOffset + 8), callOffset + 8);
            var decisionOffset = DecodeBranchTarget(ReadInstruction(text, restoreOffset + 4), restoreOffset + 4);
            var decisionInstruction = ReadInstruction(text, decisionOffset);
            var nextBranchOffset = IsCompareAndBranchNonZero32(decisionInstruction)
                ? decisionOffset + 4
                : decisionOffset + 8;
            offset = DecodeBranchTarget(ReadInstruction(text, nextBranchOffset), nextBranchOffset);
        }

        return targets.ToArray();
    }

    private static int[] ReadRoyalCandyStoryCaveGraph(byte[] patchedText, byte[] baseText)
    {
        int[] storyBranches = [0x007BB208, 0x007BB3C4];
        var pending = new Queue<int>(storyBranches.Select(offset => DecodeConditionalBranchTarget(
            ReadInstruction(patchedText, offset),
            offset)));
        var visited = new HashSet<int>();
        while (pending.Count > 0)
        {
            var offset = pending.Dequeue();
            if (offset < 0
                || offset + 0x0C > patchedText.Length
                || !baseText.AsSpan(offset, 0x0C).SequenceEqual(new byte[0x0C])
                || patchedText.AsSpan(offset, 0x0C).SequenceEqual(new byte[0x0C])
                || !visited.Add(offset))
            {
                continue;
            }

            for (var instructionOffset = offset; instructionOffset < offset + 0x0C; instructionOffset += 4)
            {
                var instruction = ReadInstruction(patchedText, instructionOffset);
                int? target = null;
                if ((instruction & 0x7C000000u) == 0x14000000u)
                {
                    target = DecodeBranchTarget(instruction, instructionOffset);
                }
                else if ((instruction & 0xFF000010u) == 0x54000000u
                    || (instruction & 0x7E000000u) == 0x34000000u)
                {
                    target = DecodeConditionalBranchTarget(instruction, instructionOffset);
                }

                if (target is not null)
                {
                    pending.Enqueue(target.Value);
                }
            }
        }

        return visited.Order().ToArray();
    }

    private static int ResolveRoyalCandyStoryHelperOffset(byte[] text)
    {
        const int storyUseGateBranchOffset = 0x007BB208;
        var itemCheckOffset = DecodeConditionalBranchTarget(
            ReadInstruction(text, storyUseGateBranchOffset),
            storyUseGateBranchOffset);
        var firstLogicOffset = DecodeBranchTarget(
            ReadInstruction(text, itemCheckOffset + 8),
            itemCheckOffset + 8);
        var secondLogicOffset = DecodeBranchTarget(
            ReadInstruction(text, firstLogicOffset + 8),
            firstLogicOffset + 8);
        return DecodeBranchTarget(
            ReadInstruction(text, secondLogicOffset + 4),
            secondLogicOffset + 4);
    }

    private static bool IsCompareAndBranchNonZero32(uint instruction)
    {
        return (instruction & 0xFF000000u) == 0x35000000u;
    }

    private static uint EncodeCmpImmediate(int register, int immediate)
    {
        return (uint)(0x7100001F | ((immediate & 0xFFF) << 10) | ((register & 0x1F) << 5));
    }

    private static uint EncodeConditionalBranch(int sourceOffset, int targetOffset, Arm64Condition condition)
    {
        var delta = targetOffset - sourceOffset;
        var imm19 = delta >> 2;
        return (uint)(0x54000000 | ((imm19 & 0x7FFFF) << 5) | ((int)condition & 0xF));
    }

    private static uint EncodeBranch(int sourceOffset, int targetOffset)
    {
        var delta = targetOffset - sourceOffset;
        var imm26 = delta >> 2;
        return 0x14000000u | (uint)(imm26 & 0x03FFFFFF);
    }

    private static uint EncodeBranchLink(int sourceOffset, int targetOffset)
    {
        var delta = targetOffset - sourceOffset;
        var imm26 = delta >> 2;
        return 0x94000000u | (uint)(imm26 & 0x03FFFFFF);
    }

    private static int DecodeBranchTarget(uint instruction, int sourceOffset)
    {
        var imm26 = (int)(instruction & 0x03FFFFFF);
        if ((imm26 & 0x02000000) != 0)
        {
            imm26 |= unchecked((int)0xFC000000);
        }

        return sourceOffset + (imm26 << 2);
    }

    private static int DecodeConditionalBranchTarget(uint instruction, int sourceOffset)
    {
        var imm19 = (int)((instruction >> 5) & 0x7FFFF);
        if ((imm19 & 0x40000) != 0)
        {
            imm19 |= unchecked((int)0xFFF80000);
        }

        return sourceOffset + (imm19 << 2);
    }

    private static int ShiftIvOffset(int offset, int shift)
    {
        return offset + shift;
    }

    private static uint EncodeAdr(int register, int sourceOffset, int targetOffset)
    {
        var delta = targetOffset - sourceOffset;
        var immediate = delta & 0x1FFFFF;
        var immediateLow = immediate & 0x3;
        var immediateHigh = (immediate >> 2) & 0x7FFFF;
        return 0x10000000u
            | (uint)(immediateLow << 29)
            | (uint)(immediateHigh << 5)
            | (uint)(register & 0x1F);
    }

    private static uint EncodeMovzImmediate32(int register, int immediate)
    {
        return (uint)(0x52800000 | ((immediate & 0xFFFF) << 5) | (register & 0x1F));
    }

    private static uint EncodeLdrbRegisterOffsetUxtw(int targetRegister, int baseRegister, int offsetRegister)
    {
        return 0x38604800u
            | (uint)((offsetRegister & 0x1F) << 16)
            | (uint)((baseRegister & 0x1F) << 5)
            | (uint)(targetRegister & 0x1F);
    }

    private static uint EncodeNop()
    {
        return 0xD503201F;
    }

    private static byte[] CreateNso(byte[] text, byte[] ro, byte[] data, byte[]? buildId = null)
    {
        var textOffset = NsoFile.HeaderSize;
        var roOffset = Align(textOffset + text.Length, 0x10);
        var dataOffset = Align(roOffset + ro.Length, 0x10);
        var output = new byte[Align(dataOffset + data.Length, 0x10)];

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), NsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x04), 1);
        WriteSegmentHeader(output, 0x10, textOffset, 0, text.Length);
        WriteSegmentHeader(output, 0x20, roOffset, text.Length, ro.Length);
        WriteSegmentHeader(output, 0x30, dataOffset, text.Length + ro.Length, data.Length);
        (buildId ?? Convert.FromHexString(SwordBuildId)).CopyTo(output.AsSpan(0x40, 0x20));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x60), text.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x64), ro.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x68), data.Length);
        NsoFile.ComputeHash(text).CopyTo(output.AsSpan(0xA0));
        NsoFile.ComputeHash(ro).CopyTo(output.AsSpan(0xC0));
        NsoFile.ComputeHash(data).CopyTo(output.AsSpan(0xE0));
        text.CopyTo(output.AsSpan(textOffset));
        ro.CopyTo(output.AsSpan(roOffset));
        data.CopyTo(output.AsSpan(dataOffset));
        return output;
    }

    private static byte[] BuildIdForGame(ProjectGame game)
    {
        return Convert.FromHexString(game == ProjectGame.Shield ? ShieldBuildId : SwordBuildId);
    }

    private static void WriteSegmentHeader(byte[] output, int offset, int fileOffset, int memoryOffset, int decompressedSize)
    {
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset), fileOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x04), memoryOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x08), decompressedSize);
    }

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }

    private static byte[] CreateNpdm(ulong titleId)
    {
        var data = new byte[0x298];
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0x290, 8), titleId);
        return data;
    }

    private static byte[] CreateVanillaBagEventScript()
    {
        const ushort pawnMagic64 = 0xF1E1;
        const short pawnFlagCompact = 0x0004;
        const short defSize = 12;
        const int cellSize = 8;
        const int nativeCount = 77;
        const int natives = 0x38;
        const int libraries = natives + nativeCount * defSize;
        const int cod = libraries;
        const int codeCellCount = 5022;
        const uint duplicatedNativeHash = 0x0473BE4E;

        var prefix = new byte[cod];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(natives + 70 * defSize + 8), duplicatedNativeHash);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(natives + 76 * defSize + 8), duplicatedNativeHash);

        var cells = new ulong[codeCellCount];
        cells[3686] = 135;
        cells[3687] = 70;
        cells[3688] = 8;
        cells[4991] = 46;
        cells[4992] = 89;
        cells[4993] = 48;
        cells[5020] = 49;
        cells[5021] = unchecked((ulong)((4991 - 5020) * cellSize));

        var compactCode = CompactAmxCells(cells);
        var data = new byte[cod + compactCode.Length];
        Array.Copy(prefix, data, prefix.Length);
        Array.Copy(compactCode, 0, data, cod, compactCode.Length);

        var dat = cod + codeCellCount * cellSize;
        WriteAmxHeaderFields(
            data,
            size: data.Length,
            magic: pawnMagic64,
            flags: pawnFlagCompact,
            defSize: defSize,
            cod: cod,
            dat: dat,
            hea: dat,
            stp: dat,
            publics: natives,
            natives: natives,
            libraries: libraries,
            nameTable: libraries);
        return data;
    }

    private static byte[] PatchTestCodeCell(byte[] data, int cellIndex, ulong value)
    {
        var decoded = DecodeTestAmx(data);
        var codeCellCount = (decoded.Header.Dat - decoded.Header.Cod) / 8;
        Assert.InRange(cellIndex, 0, codeCellCount - 1);
        WriteTestCell(decoded.Expanded, decoded.Header.Cod + cellIndex * 8, value);
        return BuildTestCompactAmx(data[..decoded.Header.Cod], decoded.Header, decoded.Expanded);
    }

    private static byte[] UseRedundantTestCellEncoding(byte[] data, int cellIndex)
    {
        var span = ReadTestCompactCellSpans(data)[cellIndex];
        var result = new byte[data.Length + 1];
        data.AsSpan(0, span.Offset).CopyTo(result);
        result[span.Offset] = 0x80;
        data.AsSpan(span.Offset).CopyTo(result.AsSpan(span.Offset + 1));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x00), result.Length);
        var originalExpanded = DecodeTestAmx(data).Expanded;
        var redundantExpanded = DecodeTestAmx(result).Expanded;
        BinaryPrimitives.WriteInt32LittleEndian(originalExpanded.AsSpan(0x00), 0);
        BinaryPrimitives.WriteInt32LittleEndian(redundantExpanded.AsSpan(0x00), 0);
        Assert.Equal(originalExpanded, redundantExpanded);
        return result;
    }

    private static byte[] ReadTestCompactCellEncoding(byte[] data, int cellIndex)
    {
        var span = ReadTestCompactCellSpans(data)[cellIndex];
        return data.AsSpan(span.Offset, span.Length).ToArray();
    }

    private static TestCompactCellSpan[] ReadTestCompactCellSpans(byte[] data)
    {
        var header = ReadTestHeader(data);
        var cellCount = (header.Hea - header.Cod) / 8;
        var spans = new TestCompactCellSpan[cellCount];
        var source = header.Size - header.Cod;
        var cell = cellCount;
        while (source > 0)
        {
            var encodedEnd = source;
            do
            {
                source--;
            } while (source > 0 && (data[header.Cod + source - 1] & 0x80) != 0);

            spans[--cell] = new TestCompactCellSpan(header.Cod + source, encodedEnd - source);
        }

        Assert.Equal(0, cell);
        return spans;
    }

    private static byte[] AppendTestCodeCell(byte[] data, ulong value)
    {
        var decoded = DecodeTestAmx(data);
        const int cellSize = 8;
        var appendedHeader = decoded.Header with
        {
            Dat = decoded.Header.Dat + cellSize,
            Hea = decoded.Header.Hea + cellSize,
            Stp = decoded.Header.Stp + cellSize,
        };
        var appendedExpanded = new byte[appendedHeader.Hea];
        Array.Copy(decoded.Expanded, 0, appendedExpanded, 0, decoded.Header.Dat);
        WriteTestCell(appendedExpanded, decoded.Header.Dat, value);
        Array.Copy(
            decoded.Expanded,
            decoded.Header.Dat,
            appendedExpanded,
            appendedHeader.Dat,
            decoded.Header.Hea - decoded.Header.Dat);
        WriteAmxHeaderFields(appendedExpanded, appendedHeader);
        return BuildTestCompactAmx(
            appendedExpanded[..appendedHeader.Cod],
            appendedHeader,
            appendedExpanded);
    }

    private static byte[] MoveTestMarkerFromDataToCode(byte[] safeBagHook)
    {
        var decoded = DecodeTestAmx(safeBagHook);
        const int markerLength = 5 * 8;
        var legacyHeader = decoded.Header with
        {
            Dat = decoded.Header.Dat + markerLength,
        };
        var legacyExpanded = new byte[decoded.Header.Hea];
        Array.Copy(decoded.Expanded, 0, legacyExpanded, 0, decoded.Header.Dat);
        Array.Copy(decoded.Expanded, decoded.Header.Hea - markerLength, legacyExpanded, decoded.Header.Dat, markerLength);
        var shiftedDataLength = decoded.Header.Hea - decoded.Header.Dat - markerLength;
        if (shiftedDataLength > 0)
        {
            Array.Copy(decoded.Expanded, decoded.Header.Dat, legacyExpanded, legacyHeader.Dat, shiftedDataLength);
        }

        WriteAmxHeaderFields(legacyExpanded, legacyHeader);
        return BuildTestCompactAmx(legacyExpanded[..legacyHeader.Cod], legacyHeader, legacyExpanded);
    }

    private static TestAmx DecodeTestAmx(byte[] data)
    {
        var header = ReadTestHeader(data);
        var expanded = ExpandTestCompactAmx(data, header, cellSize: 8);
        return new TestAmx(header, expanded);
    }

    private static TestAmxHeader ReadTestHeader(byte[] data)
    {
        return new TestAmxHeader(
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x00)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x04)),
            data[0x06],
            data[0x07],
            BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(0x08)),
            BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(0x0A)),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x0C)),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x10)),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x14)),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x18)),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x1C)),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x20)),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x24)),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x28)),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x2C)),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x30)),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x34)));
    }

    private static byte[] ExpandTestCompactAmx(byte[] data, TestAmxHeader header, int cellSize)
    {
        var expanded = new byte[header.Hea];
        Array.Copy(data, expanded, Math.Min(header.Cod, data.Length));

        var src = header.Size - header.Cod;
        var dst = header.Hea - header.Cod;
        while (src > 0)
        {
            ulong cell = 0;
            var shift = 0;
            var signSource = 0;
            do
            {
                src--;
                signSource = header.Cod + src;
                var current = data[signSource];
                cell |= (ulong)(current & 0x7F) << shift;
                shift += 7;
            } while (src > 0 && (data[header.Cod + src - 1] & 0x80) != 0);

            if ((data[signSource] & 0x40) != 0)
            {
                while (shift < cellSize * 8)
                {
                    cell |= 0xFFUL << shift;
                    shift += 8;
                }
            }

            dst -= cellSize;
            WriteTestCell(expanded, header.Cod + dst, cell);
        }

        return expanded;
    }

    private static ulong[] ReadTestCells(byte[] data, int offset, int length, int cellSize)
    {
        var cells = new ulong[length / cellSize];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset + i * cellSize));
        }

        return cells;
    }

    private static byte[] BuildTestCompactAmx(byte[] prefix, TestAmxHeader header, byte[] expanded)
    {
        var cells = ReadTestCells(expanded, header.Cod, header.Hea - header.Cod, cellSize: 8);
        var compactBody = CompactAmxCells(cells);
        var result = new byte[header.Cod + compactBody.Length];
        Array.Copy(prefix, result, prefix.Length);
        Array.Copy(compactBody, 0, result, header.Cod, compactBody.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0x00), result.Length);
        return result;
    }

    private static void WriteAmxHeaderFields(byte[] data, TestAmxHeader header)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x00), header.Size);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x04), header.Magic);
        data[0x06] = header.FileVersion;
        data[0x07] = header.AmxVersion;
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0x08), header.Flags);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0x0A), header.DefSize);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x0C), header.Cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x10), header.Dat);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x14), header.Hea);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x18), header.Stp);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x1C), header.Cip);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x20), header.Publics);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x24), header.Natives);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x28), header.Libraries);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x2C), header.PubVars);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x30), header.Tags);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x34), header.NameTable);
    }

    private static void WriteTestCell(byte[] data, int offset, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset), value);
    }

    private static byte[] CompactAmxCells(IEnumerable<ulong> cells)
    {
        var compact = new List<byte>();
        foreach (var cell in cells)
        {
            var value = unchecked((long)cell);
            var chunks = new List<byte>();
            while (true)
            {
                var payload = (byte)(value & 0x7F);
                chunks.Add(payload);
                value >>= 7;
                var signBitSet = (payload & 0x40) != 0;
                if ((value == 0 && !signBitSet) || (value == -1 && signBitSet))
                {
                    break;
                }
            }

            for (var i = chunks.Count - 1; i >= 0; i--)
            {
                var current = chunks[i];
                if (i != 0)
                {
                    current |= 0x80;
                }

                compact.Add(current);
            }
        }

        return compact.ToArray();
    }

    private static void WriteAmxHeaderFields(
        byte[] data,
        int size,
        ushort magic,
        short flags,
        short defSize,
        int cod,
        int dat,
        int hea,
        int stp,
        int publics,
        int natives,
        int libraries,
        int nameTable)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x00), size);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x04), magic);
        data[0x06] = 11;
        data[0x07] = 11;
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0x08), flags);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0x0A), defSize);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x0C), cod);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x10), dat);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x14), hea);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x18), stp);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x1C), 0);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x20), publics);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x24), natives);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x28), libraries);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x2C), libraries);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x30), libraries);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x34), nameTable);
    }

    private enum Arm64Condition
    {
        EQ = 0,
        NE = 1,
        HI = 8,
        LS = 9,
    }

    private sealed record TestAmx(TestAmxHeader Header, byte[] Expanded);

    private readonly record struct TestCompactCellSpan(int Offset, int Length);

    private sealed record TestAmxHeader(
        int Size,
        ushort Magic,
        byte FileVersion,
        byte AmxVersion,
        short Flags,
        short DefSize,
        int Cod,
        int Dat,
        int Hea,
        int Stp,
        int Cip,
        int Publics,
        int Natives,
        int Libraries,
        int PubVars,
        int Tags,
        int NameTable);
}
