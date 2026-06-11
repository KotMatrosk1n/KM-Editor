// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.BagHook;
using KM.SwSh.CatchCap;
using KM.SwSh.ExeFs;
using KM.SwSh.RoyalCandy;
using KM.SwSh.StartingItems;
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
    private const string RoyalCandyUnlimitedWorkflowId = "royal-candy-unlimited";
    private const string RoyalCandyStoryLimitsWorkflowId = "royal-candy-story-limits";
    private const string RoyalCandyUninstallWorkflowId = "royal-candy-uninstall";

    public static IEnumerable<object[]> ExeFsInstallOrders()
    {
        foreach (var game in new[] { ProjectGame.Sword, ProjectGame.Shield })
        {
            foreach (var workflowId in new[] { RoyalCandyUnlimitedWorkflowId, RoyalCandyStoryLimitsWorkflowId })
            {
                yield return [game, workflowId, true];
                yield return [game, workflowId, false];
            }
        }
    }

    public static IEnumerable<object[]> RoyalCandyVariantsByGame()
    {
        foreach (var game in new[] { ProjectGame.Sword, ProjectGame.Shield })
        {
            foreach (var workflowId in new[] { RoyalCandyUnlimitedWorkflowId, RoyalCandyStoryLimitsWorkflowId })
            {
                yield return [game, workflowId];
            }
        }
    }

    [Fact]
    public void NewHookWorkflowsLoadAgainstShieldProject()
    {
        using var temp = SwShPerformanceFixtureProject.Create();
        temp.WriteBaseRomFsFile("bin/script/amx/main_event_0020.amx", CreateVanillaBagEventScript());
        temp.WriteBaseExeFsFile("main", CreateSharedHookNso());
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(ShieldTitleId));
        var paths = temp.Paths with { SelectedGame = ProjectGame.Shield };
        InstallEmptyBagHook(paths);
        var project = new ProjectWorkspaceService().Open(paths);

        var bagHook = new SwShBagHookWorkflowService().Load(project);
        var catchCap = new SwShCatchCapWorkflowService().Load(project);
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
    public void ReservedMainTextRegionsDoNotOverlapBetweenFeatureFamilies()
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

                Assert.False(
                    SwShExeFsReservedRegionLedger.Overlaps(left, right.StartOffset!.Value, right.Length!.Value),
                    $"{left.Owner} {left.FeatureId} {left.OffsetLabel} overlaps {right.Owner} {right.FeatureId} {right.OffsetLabel}.");
            }
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
    public void RoyalCandyCleanupPreservesBagHookStartingItemsAndCatchCap(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyStartingItems(paths);
        ApplyCatchCap(paths);
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
    public void BagHookCleanupRemovesDependentsAndPreservesCatchCap(ProjectGame game, string workflowId)
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

        var main = File.ReadAllBytes(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath));
        Assert.Equal(SwShCatchCapInstallKind.InstalledV1, SwShCatchCapMainPatcher.Analyze(main).Kind);
        Assert.Equal(SwShRoyalCandyExeFsSignatureKind.NotInstalled, SwShExeFsRoyalCandyMainPatcher.AnalyzeInstallation(main).Kind);
    }

    [Theory]
    [MemberData(nameof(RoyalCandyVariantsByGame))]
    public void CatchCapCleanupPreservesBagHookStartingItemsAndRoyalCandy(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyStartingItems(paths);
        ApplyRoyalCandy(paths, workflowId);
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
    public void BagHookCleanupRemovesExeFsWhenRoyalCandyWasOnlyExeFsMod(ProjectGame game, string workflowId)
    {
        using var temp = CreateHookProject(game);
        var paths = temp.Paths with { SelectedGame = game };
        InstallEmptyBagHook(paths);
        ApplyRoyalCandy(paths, workflowId);

        ApplyBagHookCleanup(paths);

        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.BagEventScriptPath)));
        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ExeFsMainPath)));
        Assert.False(File.Exists(OutputPath(paths, SwShRoyalCandyWorkflowService.ItemPath)));
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
        temp.WriteBaseExeFsFile("main", CreateSharedHookNso());
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(game == ProjectGame.Sword ? SwordTitleId : ShieldTitleId));
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

    private static string OutputPath(ProjectPaths paths, string relativePath)
    {
        return Path.Combine(paths.OutputRootPath!, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static byte[] CreateSharedHookNso()
    {
        var text = new byte[0x013AF6C0];
        WriteRoyalCandyVanillaAnchors(text);
        WriteCatchCapVanillaAnchors(text);
        return CreateNso(text, [0x10], [0x20]);
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
    }

    private static void WriteCatchCapVanillaAnchors(byte[] text)
    {
        WriteInstruction(text, 0x013AE3B0, 0xA9417BFD);
        WriteInstruction(text, 0x013AE3C8, 0xA8C24FF4);
        WriteInstruction(text, 0x013AE3CC, 0xD65F03C0);
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, sizeof(uint)), instruction);
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

    private static byte[] CreateNso(byte[] text, byte[] ro, byte[] data)
    {
        var textOffset = SwShNsoFile.HeaderSize;
        var roOffset = Align(textOffset + text.Length, 0x10);
        var dataOffset = Align(roOffset + ro.Length, 0x10);
        var output = new byte[Align(dataOffset + data.Length, 0x10)];

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), SwShNsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x04), 1);
        WriteSegmentHeader(output, 0x10, textOffset, 0, text.Length);
        WriteSegmentHeader(output, 0x20, roOffset, text.Length, ro.Length);
        WriteSegmentHeader(output, 0x30, dataOffset, text.Length + ro.Length, data.Length);
        output.AsSpan(0x40, 0x20).Fill(0xAB);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x60), text.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x64), ro.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x68), data.Length);
        SwShNsoFile.ComputeHash(text).CopyTo(output.AsSpan(0xA0));
        SwShNsoFile.ComputeHash(ro).CopyTo(output.AsSpan(0xC0));
        SwShNsoFile.ComputeHash(data).CopyTo(output.AsSpan(0xE0));
        text.CopyTo(output.AsSpan(textOffset));
        ro.CopyTo(output.AsSpan(roOffset));
        data.CopyTo(output.AsSpan(dataOffset));
        return output;
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
    }
}
