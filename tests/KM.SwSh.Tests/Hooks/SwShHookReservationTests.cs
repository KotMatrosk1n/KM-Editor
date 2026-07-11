// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
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
using KM.SwSh.NameFilter;
using KM.SwSh.RoyalCandy;
using KM.SwSh.StartingItems;
using KM.SwSh.Tests.FpsPatch;
using KM.SwSh.Tests.Items;
using KM.SwSh.Tests.Performance;
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
        yield return [ProjectGame.Sword, RoyalCandyUnlimitedWorkflowId];
        yield return [ProjectGame.Shield, RoyalCandyUnlimitedWorkflowId];
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
        var region = Assert.Single(SwShExeFsReservedRegionLedger.MainRoRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerTypeChart));

        Assert.Equal("type-chart-swsh", region.FeatureId);
        Assert.Equal(SwShExeFsReservedRegionLedger.ExeFsMainPath, region.RelativePath);
        Assert.Equal("main.ro", region.Area);
        Assert.Equal(0x00743600, region.StartOffset);
        Assert.Equal(0x144, region.Length);
        Assert.Equal("ro+0x743600..0x743743", region.OffsetLabel);
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
    public void ShinyRateReservesSwordAndShieldRerollLoopControlRanges()
    {
        var regions = SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerShinyRate);

        Assert.Collection(
            regions.OrderBy(region => region.StartOffset).ToArray(),
            region =>
            {
                Assert.Equal("shiny-rate-sword-reroll-loop-control", region.FeatureId);
                Assert.Equal(0x00D31488, region.StartOffset);
                Assert.Equal(0x08, region.Length);
                Assert.Equal("text+0xD31488..0xD3148F", region.OffsetLabel);
            },
            region =>
            {
                Assert.Equal("shiny-rate-shield-reroll-loop-control", region.FeatureId);
                Assert.Equal(0x00D314B8, region.StartOffset);
                Assert.Equal(0x08, region.Length);
                Assert.Equal("text+0xD314B8..0xD314BF", region.OffsetLabel);
            });
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
                if (IsSameFeatureFamily(left.Owner, right.Owner))
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
        Assert.True(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ItemPath)));
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
        Assert.True(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ItemPath)));
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
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void BagHookCleanupRemovesExeFsAndPreservesRomFsWhenRoyalCandyWasOnlyExeFsMod(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyRoyalCandy(paths, workflowId);

        ApplyBagHookCleanup(paths);

        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.BagEventScriptPath)));
        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath)));
        Assert.True(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ItemPath)));
        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ShopDataPath)));
        Assert.False(File.Exists(OutputPath(paths, "romfs/bin/message/English/common/itemname.dat")));
        Assert.False(File.Exists(OutputPath(paths, "romfs/bin/message/English/common/itemname_plural.dat")));
        Assert.False(File.Exists(OutputPath(paths, "romfs/bin/message/English/common/iteminfo.dat")));
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
        var shift = game == ProjectGame.Shield ? 0x30 : 0;
        WriteInstruction(text, SwShHyperTrainingMainPatcher.SwordPreflightCompareOffset + shift, EncodeCmpImmediate(0, 100));
        WriteInstruction(text, SwShHyperTrainingMainPatcher.SwordEligibilityCompareOffset + shift, EncodeCmpImmediate(0, 100));
        WriteInstruction(text, SwShHyperTrainingMainPatcher.SwordEligibilityBranchOffset + shift, 0x54000061);
        WriteInstruction(text, SwShHyperTrainingMainPatcher.SwordGrayOutCompareOffset + shift, EncodeCmpImmediate(0, 100));
        WriteInstruction(text, SwShHyperTrainingMainPatcher.SwordGrayOutBranchOffset + shift, 0x540000A1);
        WriteInstruction(text, SwShHyperTrainingMainPatcher.SwordDetailCompareOffset + shift, EncodeCmpImmediate(0, 100));
        WriteInstruction(text, SwShHyperTrainingMainPatcher.SwordDetailBranchOffset + shift, 0x540002C1);
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
        WriteInstruction(text, ShiftIvOffset(0x0138B1E0, shift), 0xD10183FF);
        WriteInstruction(text, ShiftIvOffset(0x0138B1FC, shift), 0x39592408);
        WriteInstruction(text, ShiftIvOffset(0x0138B200, shift), 0x52000108);
        WriteInstruction(text, ShiftIvOffset(0x0139FB60, shift), 0x340000A8);
        WriteInstruction(text, ShiftIvOffset(0x013B2F90, shift), 0xD10143FF);
        WriteInstruction(text, ShiftIvOffset(0x013CA220, shift), 0xF81D0FF5);
        WriteInstruction(text, 0x00779070, 0x7100143F);
        WriteInstruction(text, 0x00778E20, 0xA9BF7BFD);
        WriteInstruction(text, 0x007790D0, 0xA9BE4FF4);
        WriteIvScreenCallSiteAnchors(text, game);
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
        output.AsSpan(0x40, 0x20).Fill(0xAB);
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
