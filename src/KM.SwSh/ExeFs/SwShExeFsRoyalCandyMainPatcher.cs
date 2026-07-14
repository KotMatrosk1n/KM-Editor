// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.Formats.Executable;
using System.Buffers.Binary;
using System.Globalization;

namespace KM.SwSh.ExeFs;

internal enum SwShRoyalCandyStoryLevelCapProgressKind
{
    Flag,
    WorkAtLeast,
}

internal sealed record SwShRoyalCandyStoryLevelCap(
    int LevelCap,
    ulong ProgressHash,
    string Label,
    SwShRoyalCandyStoryLevelCapProgressKind ProgressKind = SwShRoyalCandyStoryLevelCapProgressKind.Flag,
    int WorkMinimum = 0);

internal sealed record SwShRoyalCandyInstalledStoryLevelCap(
    int LevelCap,
    ulong ProgressHash,
    SwShRoyalCandyStoryLevelCapProgressKind ProgressKind,
    int WorkMinimum);

internal enum SwShRoyalCandyExeFsSignatureKind
{
    NotInstalled,
    Unlimited,
    StoryLimits,
    GameMismatch,
    ForeignPatch,
}

internal sealed record SwShRoyalCandyExeFsSignature(
    SwShRoyalCandyExeFsSignatureKind Kind,
    string Message,
    int ReservedAnchorCount,
    int RecognizedAnchorCount);

internal static class SwShExeFsRoyalCandyMainPatcher
{
    private const int RareCandyItemId = 50;
    private const int RoyalCandyItemId = 1128;
    private const int RareCandyUiHookCodeCaveSearchStart = 0x007BC338;
    private const int StoryDefaultLevelCap = 1;
    private const int StoryUseGateCompareOffset = 0x007BB204;
    private const int StoryQuantityMaxCompareOffset = 0x007BB3C0;
    private const int QuantityMoveOffset = 0x007B1F20;
    private const int StoryInventoryClampSelectOffset = 0x007BAF3C;
    private const int AllowedConsumableCompareOffset = 0x007DDA8C;
    private const int AllowedConsumableBranchOffset = AllowedConsumableCompareOffset + 4;
    private const int AllowedConsumablePassOffset = 0x007DDA48;
    private const int AllowedConsumableFailOffset = 0x007DDAF8;
    private const int FlagGetOffset = 0x01410F00;
    private const int WorkGetOffset = 0x014114C0;
    private const int ShieldFlagGetOffset = 0x01410F30;
    private const int ShieldWorkGetOffset = 0x014114F0;
    private const int ItemOwnershipFunctionOffset = 0x01420EF0;
    private const int ItemCountFunctionOffset = 0x01421090;
    private const int ShieldItemOwnershipFunctionOffset = 0x01420F20;
    private const int ShieldItemCountFunctionOffset = 0x014210C0;
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";
    private const uint ExpectedQuantityMove = 0x2A0003E2; // MOV w2, w0
    private const uint ExpectedQuantityClampSelect = 0x1A963316;
    private const uint ExpectedItemOwnershipFirstInstruction = 0xF81D0FF5;
    private const uint ExpectedItemCountFirstInstruction = 0xA9BE4FF4;
    private const uint NopInstruction = 0xD503201F;
    private const int RoyalCandyAllowedConsumableAdjustedItemId = RoyalCandyItemId - 0x12;
    private const int RoyalCandyVirtualInventoryCount = 999;

    private static readonly HashSet<int> ExternalBranchLinkTargets =
    [
        0x0077A5F0,
        0x007C8330,
        FlagGetOffset,
        WorkGetOffset,
        ShieldFlagGetOffset,
        ShieldWorkGetOffset,
        ItemOwnershipFunctionOffset,
        ItemCountFunctionOffset,
        ShieldItemOwnershipFunctionOffset,
        ShieldItemCountFunctionOffset,
    ];

    private static readonly RoyalCandyPatchLayout SwordPatchLayout = new(
        FlagworkGlobalAddress: 0x02610798,
        FlagworkObjectOffset: 0x1B8,
        FlagGetOffset,
        WorkGetOffset,
        ItemOwnershipFunctionOffset,
        ItemCountFunctionOffset);

    private static readonly RoyalCandyPatchLayout ShieldPatchLayout = new(
        FlagworkGlobalAddress: 0x02610798,
        FlagworkObjectOffset: 0x1B8,
        ShieldFlagGetOffset,
        ShieldWorkGetOffset,
        ShieldItemOwnershipFunctionOffset,
        ShieldItemCountFunctionOffset);

    private static readonly IReadOnlyList<SwShExeFsReservedRegion> MainTextReservations =
        SwShExeFsReservedRegionLedger.MainTextRegionsForOwners(
            SwShExeFsReservedRegionLedger.OwnerCatchCap,
            SwShExeFsReservedRegionLedger.OwnerRoyalCandy,
            SwShExeFsReservedRegionLedger.OwnerRoyalCandyStoryLimits);

    private static readonly IReadOnlyList<SwShExeFsReservedRegion> NonRoyalCandyReservations =
        SwShExeFsReservedRegionLedger.MainTextReservationsForOtherOwners(
            SwShExeFsReservedRegionLedger.OwnerRoyalCandy,
            SwShExeFsReservedRegionLedger.OwnerRoyalCandyStoryLimits);

    private static int ReservedAnchorCount => UiRouteChecks.Length
        + EqualBranchChecks.Length
        + 2
        + 1
        + 1;

    public static SwShRoyalCandyExeFsSignature AnalyzeInstallation(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        var nso = NsoFile.Parse(mainBytes);
        var gameMismatch = CreateGameMismatchSignature(nso.BuildId, expectedGame);
        if (gameMismatch is not null)
        {
            return gameMismatch;
        }

        var text = nso.Text.DecompressedData;
        var routeInstalledCount = UiRouteChecks.Count(check => IsUiRouteCheckInstalled(text, check));
        var equalInstalledCount = EqualBranchChecks.Count(check => IsEqualBranchCheckInstalled(text, check));
        var expCandyBypassCount = CountInstalledExpCandyBypass(text);
        var infiniteUseInstalled = TryReadInstruction(text, QuantityMoveOffset, out var quantityMove)
            && IsUnconditionalBranch(quantityMove);
        var storyLimitsInstalled = TryReadInstruction(text, StoryInventoryClampSelectOffset, out var storyClampSelect)
            && IsUnconditionalBranch(storyClampSelect);

        var recognizedAnchorCount = routeInstalledCount
            + equalInstalledCount
            + expCandyBypassCount
            + (infiniteUseInstalled ? 1 : 0)
            + (storyLimitsInstalled ? 1 : 0);
        var reservedAnchorCount = UiRouteChecks.Length
            + EqualBranchChecks.Length
            + 2
            + 1
            + 1;
        var baseInstalled = routeInstalledCount == UiRouteChecks.Length
            && equalInstalledCount == EqualBranchChecks.Length
            && expCandyBypassCount == 2
            && infiniteUseInstalled;

        if (baseInstalled)
        {
            var kind = storyLimitsInstalled
                ? SwShRoyalCandyExeFsSignatureKind.StoryLimits
                : SwShRoyalCandyExeFsSignatureKind.Unlimited;
            return new SwShRoyalCandyExeFsSignature(
                kind,
                storyLimitsInstalled
                    ? "Royal Candy with Story Limits ExeFS signature detected from reserved item-route, decrement, and story-cap anchors."
                    : "Unlimited Royal Candy ExeFS signature detected from reserved item-route and decrement anchors.",
                reservedAnchorCount,
                recognizedAnchorCount);
        }

        if (recognizedAnchorCount > 0 || HasForeignReservedAnchorChange(text))
        {
            return new SwShRoyalCandyExeFsSignature(
                SwShRoyalCandyExeFsSignatureKind.ForeignPatch,
                "Royal Candy reserved ExeFS anchors are partially patched or do not match a KM Royal Candy signature.",
                reservedAnchorCount,
                recognizedAnchorCount);
        }

        return new SwShRoyalCandyExeFsSignature(
            SwShRoyalCandyExeFsSignatureKind.NotInstalled,
            "Royal Candy reserved ExeFS anchors are vanilla and available.",
            reservedAnchorCount,
            recognizedAnchorCount);
    }

    internal static IReadOnlyList<SwShRoyalCandyInstalledStoryLevelCap> ReadInstalledStoryLevelCaps(
        byte[] mainBytes,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        var nso = NsoFile.Parse(mainBytes);
        ValidateSelectedGame(nso.BuildId, expectedGame);
        var layout = ResolvePatchLayout(nso.BuildId, expectedGame);
        var text = nso.Text.DecompressedData;
        if (!TryResolveStoryCapHelperOffset(text, out var helperOffset))
        {
            return Array.Empty<SwShRoyalCandyInstalledStoryLevelCap>();
        }

        return ReadStoryCapLadder(text, helperOffset, layout);
    }

    public static byte[] ApplyBasePatch(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        var nso = NsoFile.Parse(mainBytes);
        ValidateSelectedGame(nso.BuildId, expectedGame);
        var layout = ResolvePatchLayout(nso.BuildId, expectedGame);
        var text = nso.Text.DecompressedData.ToArray();

        PatchExpCandyFixedAmountBypass(text);
        PatchRoyalCandyAllowedConsumableRoute(text);
        PatchRoyalCandyVirtualInventory(text, layout);
        PatchInfiniteRoyalCandyUse(text);
        PatchRoyalCandyUiRoute(text);

        return nso.Write(textDecompressedData: text);
    }

    public static byte[] ApplyStoryLimitsPatch(
        byte[] mainBytes,
        IReadOnlyList<SwShRoyalCandyStoryLevelCap> levelCaps,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);
        ArgumentNullException.ThrowIfNull(levelCaps);

        if (levelCaps.Count == 0)
        {
            throw new InvalidDataException("Royal Candy story-limit patch requires at least one level-cap milestone.");
        }

        var nso = NsoFile.Parse(mainBytes);
        ValidateSelectedGame(nso.BuildId, expectedGame);
        var layout = ResolvePatchLayout(nso.BuildId, expectedGame);
        var text = nso.Text.DecompressedData.ToArray();

        PatchExpCandyFixedAmountBypass(text);
        PatchRoyalCandyAllowedConsumableRoute(text);
        PatchRoyalCandyVirtualInventory(text, layout);
        PatchInfiniteRoyalCandyUse(text);
        PatchStoryCapLadder(text, levelCaps, layout);
        PatchRoyalCandyUiRoute(text, skipStoryLimitHooks: true);

        return nso.Write(textDecompressedData: text);
    }

    public static byte[] RestoreFromBase(
        byte[] currentMainBytes,
        byte[] baseMainBytes,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(currentMainBytes);
        ArgumentNullException.ThrowIfNull(baseMainBytes);

        var currentNso = NsoFile.Parse(currentMainBytes);
        var baseNso = NsoFile.Parse(baseMainBytes);
        ValidateSelectedGame(currentNso.BuildId, expectedGame);
        ValidateSelectedGame(baseNso.BuildId, expectedGame);
        var currentText = currentNso.Text.DecompressedData.ToArray();
        var baseText = baseNso.Text.DecompressedData;
        if (currentText.Length != baseText.Length)
        {
            throw new InvalidDataException("Royal Candy ExeFS restore requires current and base main NSO files with matching .text sizes.");
        }

        var layout = ResolvePatchLayout(currentNso.BuildId, expectedGame);
        ClearReachableRoyalCandyCaves(currentText, baseText, layout);
        foreach (var region in SwShExeFsReservedRegionLedger.MainTextRegionsForOwners(
            SwShExeFsReservedRegionLedger.OwnerRoyalCandy,
            SwShExeFsReservedRegionLedger.OwnerRoyalCandyStoryLimits))
        {
            baseText.AsSpan(region.StartOffset!.Value, region.Length!.Value)
                .CopyTo(currentText.AsSpan(region.StartOffset.Value, region.Length.Value));
        }

        return currentNso.Write(textDecompressedData: currentText);
    }

    private static void ValidateSelectedGame(byte[] buildId, ProjectGame? expectedGame)
    {
        var mismatch = CreateGameMismatchSignature(buildId, expectedGame);
        if (mismatch is not null)
        {
            throw new InvalidDataException(mismatch.Message);
        }
    }

    private static SwShRoyalCandyExeFsSignature? CreateGameMismatchSignature(byte[] buildId, ProjectGame? expectedGame)
    {
        if (expectedGame is null)
        {
            return null;
        }

        var detectedGame = DetectGame(buildId);
        if (detectedGame == expectedGame.Value)
        {
            return null;
        }

        var buildDescription = detectedGame is null
            ? "an unsupported Sword/Shield build"
            : FormatGame(detectedGame.Value);
        return new SwShRoyalCandyExeFsSignature(
            SwShRoyalCandyExeFsSignatureKind.GameMismatch,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Selected {FormatGame(expectedGame.Value)}, but exefs/main build ID is {buildDescription}. Royal Candy will not patch a different game's executable."),
            ReservedAnchorCount,
            RecognizedAnchorCount: 0);
    }

    private static ProjectGame? DetectGame(byte[] buildId)
    {
        var formattedBuildId = FormatBuildId(buildId);
        if (string.Equals(formattedBuildId, SwordBuildId, StringComparison.OrdinalIgnoreCase))
        {
            return ProjectGame.Sword;
        }

        if (string.Equals(formattedBuildId, ShieldBuildId, StringComparison.OrdinalIgnoreCase))
        {
            return ProjectGame.Shield;
        }

        return null;
    }

    private static RoyalCandyPatchLayout ResolvePatchLayout(byte[] buildId, ProjectGame? expectedGame)
    {
        return (DetectGame(buildId) ?? expectedGame) == ProjectGame.Shield
            ? ShieldPatchLayout
            : SwordPatchLayout;
    }

    private static string FormatBuildId(byte[] buildId)
    {
        var buildIdLength = Math.Min(20, buildId.Length);
        return Convert.ToHexString(buildId.AsSpan(0, buildIdLength));
    }

    private static string FormatGame(ProjectGame game)
    {
        return game switch
        {
            ProjectGame.Sword => "Pokemon Sword",
            ProjectGame.Shield => "Pokemon Shield",
            _ => game.ToString(),
        };
    }

    private static void PatchRoyalCandyUiRoute(byte[] text, bool skipStoryLimitHooks = false)
    {
        foreach (var check in UiRouteChecks)
        {
            if (skipStoryLimitHooks
                && (check.CompareOffset == StoryUseGateCompareOffset
                    || check.CompareOffset == StoryQuantityMaxCompareOffset))
            {
                continue;
            }

            var caveOffset = FindCodeCave(text, 0x0C, $"Royal Candy UI route check at text+0x{check.CompareOffset:X}");
            ExpectInstruction(
                text,
                check.CompareOffset,
                EncodeCmpImmediate(check.ItemRegister, RareCandyItemId),
                $"Rare Candy UI compare at text+0x{check.CompareOffset:X}");

            var branchOffset = check.CompareOffset + 4;
            ExpectInstruction(
                text,
                branchOffset,
                EncodeConditionalBranch(branchOffset, check.FailOffset, Arm64Condition.NE),
                $"Rare Candy UI branch at text+0x{branchOffset:X}");

            WriteInstruction(text, branchOffset, EncodeConditionalBranch(branchOffset, caveOffset, Arm64Condition.NE));
            WriteInstruction(text, caveOffset, EncodeCmpImmediate(check.ItemRegister, RoyalCandyItemId));
            WriteInstruction(text, caveOffset + 4, EncodeConditionalBranch(caveOffset + 4, check.PassOffset, Arm64Condition.EQ));
            WriteInstruction(text, caveOffset + 8, EncodeBranch(caveOffset + 8, check.FailOffset));
        }

        foreach (var check in EqualBranchChecks)
        {
            ExpectInstruction(
                text,
                check.CompareOffset,
                EncodeCmpImmediate(check.ItemRegister, RareCandyItemId),
                $"Rare Candy equal-branch compare at text+0x{check.CompareOffset:X}");

            var branchOffset = check.CompareOffset + 4;
            ExpectInstruction(
                text,
                branchOffset,
                EncodeConditionalBranch(branchOffset, check.TargetOffset, Arm64Condition.EQ),
                $"Rare Candy equal branch at text+0x{branchOffset:X}");

            var firstCaveOffset = FindCodeCave(text, 0x0C, $"Royal Candy equal-branch first stub at text+0x{check.CompareOffset:X}");
            var secondCaveOffset = FindZeroRun(text, 0x0C, firstCaveOffset + 0x0C);
            if (secondCaveOffset < 0)
            {
                secondCaveOffset = FindZeroRun(text, 0x0C, 0);
            }

            if (secondCaveOffset < 0 || secondCaveOffset == firstCaveOffset)
            {
                throw NoCodeCave(text, 0x0C, $"Royal Candy equal-branch second stub at text+0x{check.CompareOffset:X}");
            }

            WriteInstruction(text, check.CompareOffset, EncodeBranch(check.CompareOffset, firstCaveOffset));
            WriteInstruction(text, branchOffset, EncodeNop());

            WriteInstruction(text, firstCaveOffset, EncodeCmpImmediate(check.ItemRegister, RareCandyItemId));
            WriteInstruction(text, firstCaveOffset + 4, EncodeConditionalBranch(firstCaveOffset + 4, check.TargetOffset, Arm64Condition.EQ));
            WriteInstruction(text, firstCaveOffset + 8, EncodeBranch(firstCaveOffset + 8, secondCaveOffset));

            WriteInstruction(text, secondCaveOffset, EncodeCmpImmediate(check.ItemRegister, RoyalCandyItemId));
            WriteInstruction(text, secondCaveOffset + 4, EncodeConditionalBranch(secondCaveOffset + 4, check.TargetOffset, Arm64Condition.EQ));
            WriteInstruction(text, secondCaveOffset + 8, EncodeBranch(secondCaveOffset + 8, check.FallthroughOffset));
        }
    }

    private static void PatchExpCandyFixedAmountBypass(byte[] text)
    {
        foreach (var offset in new[] { 0x007BC1BC, 0x007BC1C4 })
        {
            ExpectInstruction(
                text,
                offset,
                EncodeCmpImmediate(register: 9, immediate: 4),
                $"Exp Candy fixed amount compare at text+0x{offset:X}");
            WriteInstruction(text, offset, EncodeCmpImmediate(register: 9, immediate: 3));
        }
    }

    private static void PatchInfiniteRoyalCandyUse(byte[] text)
    {
        const int resumeOffset = QuantityMoveOffset + 4;
        const int itemRegister = 22;

        ExpectInstruction(text, QuantityMoveOffset, ExpectedQuantityMove, "consume quantity move");

        var caveOffset = FindCodeCave(text, 0x0C, "Royal Candy infinite-use stub");
        WriteInstruction(text, QuantityMoveOffset, EncodeBranch(QuantityMoveOffset, caveOffset));
        WriteInstruction(text, caveOffset, EncodeCmpImmediate(itemRegister, RoyalCandyItemId));
        WriteInstruction(text, caveOffset + 4, EncodeConditionalSelect32(2, 31, 0, Arm64Condition.EQ));
        WriteInstruction(text, caveOffset + 8, EncodeBranch(caveOffset + 8, resumeOffset));
    }

    private static void PatchRoyalCandyAllowedConsumableRoute(byte[] text)
    {
        ExpectInstruction(
            text,
            AllowedConsumableCompareOffset,
            EncodeCmpImmediate(register: 8, immediate: RareCandyItemId),
            "allowed consumable upper-bound compare");
        ExpectInstruction(
            text,
            AllowedConsumableBranchOffset,
            EncodeConditionalBranch(AllowedConsumableBranchOffset, AllowedConsumableFailOffset, Arm64Condition.HI),
            "allowed consumable upper-bound failure branch");

        var caveOffset = FindCodeCave(text, 0x0C, "Royal Candy allowed-consumable route");
        WriteInstruction(text, AllowedConsumableBranchOffset, EncodeConditionalBranch(AllowedConsumableBranchOffset, caveOffset, Arm64Condition.HI));
        WriteInstruction(text, caveOffset, EncodeCmpImmediate(register: 8, immediate: RoyalCandyAllowedConsumableAdjustedItemId));
        WriteInstruction(text, caveOffset + 4, EncodeConditionalBranch(caveOffset + 4, AllowedConsumablePassOffset, Arm64Condition.EQ));
        WriteInstruction(text, caveOffset + 8, EncodeBranch(caveOffset + 8, AllowedConsumableFailOffset));
    }

    private static void PatchRoyalCandyVirtualInventory(byte[] text, RoyalCandyPatchLayout layout)
    {
        PatchRoyalCandyVirtualInventoryOwnership(text, layout.ItemOwnershipFunctionOffset);
        PatchRoyalCandyVirtualInventoryCount(text, layout.ItemCountFunctionOffset);
    }

    private static void PatchRoyalCandyVirtualInventoryOwnership(byte[] text, int functionOffset)
    {
        const int itemRegister = 1;
        var resumeOffset = functionOffset + 4;

        ExpectInstruction(text, functionOffset, ExpectedItemOwnershipFirstInstruction, "item-ownership helper first instruction");

        var dispatchCaveOffset = AllocateCodeCave(text, 0x0C, "Royal Candy virtual ownership dispatch");
        var returnCaveOffset = AllocateCodeCave(text, 0x0C, "Royal Candy virtual ownership return");
        var vanillaCaveOffset = AllocateCodeCave(text, 0x0C, "Royal Candy virtual ownership vanilla path");

        WriteInstruction(text, functionOffset, EncodeBranch(functionOffset, dispatchCaveOffset));
        WriteInstruction(text, dispatchCaveOffset, EncodeCmpImmediate(itemRegister, RoyalCandyItemId));
        WriteInstruction(text, dispatchCaveOffset + 4, EncodeConditionalBranch(dispatchCaveOffset + 4, returnCaveOffset, Arm64Condition.EQ));
        WriteInstruction(text, dispatchCaveOffset + 8, EncodeBranch(dispatchCaveOffset + 8, vanillaCaveOffset));
        WriteInstruction(text, returnCaveOffset, EncodeMovzImmediate32(0, 1));
        WriteInstruction(text, returnCaveOffset + 4, EncodeRet());
        WriteInstruction(text, returnCaveOffset + 8, EncodeNop());
        WriteInstruction(text, vanillaCaveOffset, ExpectedItemOwnershipFirstInstruction);
        WriteInstruction(text, vanillaCaveOffset + 4, EncodeBranch(vanillaCaveOffset + 4, resumeOffset));
        WriteInstruction(text, vanillaCaveOffset + 8, EncodeNop());
    }

    private static void PatchRoyalCandyVirtualInventoryCount(byte[] text, int functionOffset)
    {
        const int itemRegister = 1;
        var resumeOffset = functionOffset + 4;

        ExpectInstruction(text, functionOffset, ExpectedItemCountFirstInstruction, "item-count helper first instruction");

        var dispatchCaveOffset = AllocateCodeCave(text, 0x0C, "Royal Candy virtual count dispatch");
        var returnCaveOffset = AllocateCodeCave(text, 0x0C, "Royal Candy virtual count return");
        var vanillaCaveOffset = AllocateCodeCave(text, 0x0C, "Royal Candy virtual count vanilla path");

        WriteInstruction(text, functionOffset, EncodeBranch(functionOffset, dispatchCaveOffset));
        WriteInstruction(text, dispatchCaveOffset, EncodeCmpImmediate(itemRegister, RoyalCandyItemId));
        WriteInstruction(text, dispatchCaveOffset + 4, EncodeConditionalBranch(dispatchCaveOffset + 4, returnCaveOffset, Arm64Condition.EQ));
        WriteInstruction(text, dispatchCaveOffset + 8, EncodeBranch(dispatchCaveOffset + 8, vanillaCaveOffset));
        WriteInstruction(text, returnCaveOffset, EncodeMovzImmediate32(0, RoyalCandyVirtualInventoryCount));
        WriteInstruction(text, returnCaveOffset + 4, EncodeRet());
        WriteInstruction(text, returnCaveOffset + 8, EncodeNop());
        WriteInstruction(text, vanillaCaveOffset, ExpectedItemCountFirstInstruction);
        WriteInstruction(text, vanillaCaveOffset + 4, EncodeBranch(vanillaCaveOffset + 4, resumeOffset));
        WriteInstruction(text, vanillaCaveOffset + 8, EncodeNop());
    }

    private static void PatchStoryCapLadder(
        byte[] text,
        IReadOnlyList<SwShRoyalCandyStoryLevelCap> levelCaps,
        RoyalCandyPatchLayout layout)
    {
        var milestones = levelCaps
            .OrderByDescending(levelCap => levelCap.LevelCap)
            .ToArray();
        var capHelperOffset = WriteStoryCapHelper(text, milestones, StoryDefaultLevelCap, layout);
        PatchUseGateDynamicCap(text, capHelperOffset);
        PatchQuantityMaxDynamicCap(text, capHelperOffset);
        PatchQuantityInventoryClampBypass(text);
    }

    private static int WriteStoryCapHelper(
        byte[] text,
        IReadOnlyList<SwShRoyalCandyStoryLevelCap> milestones,
        int defaultCap,
        RoyalCandyPatchLayout layout)
    {
        var checks = milestones.Select((milestone, index) => new
        {
            Milestone = milestone,
            Chunks = AllocateCapCheckChunks(text, index),
        }).ToArray();
        var defaultReturn = AllocateCodeCave(text, 0x08, "Royal Candy cap ladder default return");

        for (var i = 0; i < checks.Length; i++)
        {
            var current = checks[i];
            var nextOffset = i == checks.Length - 1 ? defaultReturn : checks[i + 1].Chunks.LoadGlobal;
            WriteLevelCapCheck(
                text,
                current.Chunks,
                current.Milestone,
                nextOffset,
                layout.FlagworkGlobalAddress,
                layout.FlagworkObjectOffset,
                layout.FlagGetOffset,
                layout.WorkGetOffset);
        }

        WriteInstruction(text, defaultReturn, EncodeMovzImmediate32(0, defaultCap));
        WriteInstruction(text, defaultReturn + 4, EncodeRet());
        return checks[0].Chunks.LoadGlobal;
    }

    private static RoyalCandyLevelCapCheckChunks AllocateCapCheckChunks(byte[] text, int index)
    {
        return new RoyalCandyLevelCapCheckChunks(
            AllocateCodeCave(text, 0x0C, $"Royal Candy cap ladder check {index} load global"),
            AllocateCodeCave(text, 0x0C, $"Royal Candy cap ladder check {index} load table"),
            AllocateCodeCave(text, 0x0C, $"Royal Candy cap ladder check {index} hash low"),
            AllocateCodeCave(text, 0x0C, $"Royal Candy cap ladder check {index} hash high"),
            AllocateCodeCave(text, 0x0C, $"Royal Candy cap ladder check {index} call flag getter"),
            AllocateCodeCave(text, 0x0C, $"Royal Candy cap ladder check {index} restore link register"),
            AllocateCodeCave(text, 0x0C, $"Royal Candy cap ladder check {index} decision"),
            AllocateCodeCave(text, 0x08, $"Royal Candy cap ladder check {index} cap return"));
    }

    private static void WriteLevelCapCheck(
        byte[] text,
        RoyalCandyLevelCapCheckChunks chunks,
        SwShRoyalCandyStoryLevelCap milestone,
        int nextOffset,
        int flagworkGlobalAddress,
        int flagworkObjectOffset,
        int flagGetOffset,
        int workGetOffset)
    {
        WriteInstruction(text, chunks.LoadGlobal, EncodeAdrp(8, chunks.LoadGlobal, flagworkGlobalAddress));
        WriteInstruction(text, chunks.LoadGlobal + 4, EncodeLdrUnsigned64(8, 8, flagworkGlobalAddress & 0xFFF));
        WriteInstruction(text, chunks.LoadGlobal + 8, EncodeBranch(chunks.LoadGlobal + 8, chunks.LoadTable));

        WriteInstruction(text, chunks.LoadTable, EncodeLdrUnsigned64(8, 8, 0));
        WriteInstruction(text, chunks.LoadTable + 4, EncodeLdrUnsigned64(0, 8, flagworkObjectOffset));
        WriteInstruction(text, chunks.LoadTable + 8, EncodeBranch(chunks.LoadTable + 8, chunks.HashLow));

        WriteInstruction(text, chunks.HashLow, EncodeMovzImmediate64(1, (int)(milestone.ProgressHash & 0xFFFF), 0));
        WriteInstruction(text, chunks.HashLow + 4, EncodeMovkImmediate64(1, (int)((milestone.ProgressHash >> 16) & 0xFFFF), 16));
        WriteInstruction(text, chunks.HashLow + 8, EncodeBranch(chunks.HashLow + 8, chunks.HashHigh));

        WriteInstruction(text, chunks.HashHigh, EncodeMovkImmediate64(1, (int)((milestone.ProgressHash >> 32) & 0xFFFF), 32));
        WriteInstruction(text, chunks.HashHigh + 4, EncodeMovkImmediate64(1, (int)((milestone.ProgressHash >> 48) & 0xFFFF), 48));
        WriteInstruction(text, chunks.HashHigh + 8, EncodeBranch(chunks.HashHigh + 8, chunks.Call));

        var accessorOffset = milestone.ProgressKind == SwShRoyalCandyStoryLevelCapProgressKind.WorkAtLeast
            ? workGetOffset
            : flagGetOffset;
        WriteInstruction(text, chunks.Call, 0xA9BF7BFD);
        WriteInstruction(text, chunks.Call + 4, EncodeBranchLink(chunks.Call + 4, accessorOffset));
        WriteInstruction(text, chunks.Call + 8, EncodeBranch(chunks.Call + 8, chunks.Restore));

        WriteInstruction(text, chunks.Restore, 0xA8C17BFD);
        WriteInstruction(text, chunks.Restore + 4, EncodeBranch(chunks.Restore + 4, chunks.Decision));
        WriteInstruction(text, chunks.Restore + 8, EncodeNop());

        if (milestone.ProgressKind == SwShRoyalCandyStoryLevelCapProgressKind.WorkAtLeast)
        {
            WriteInstruction(text, chunks.Decision, EncodeCmpImmediate(0, milestone.WorkMinimum));
            WriteInstruction(text, chunks.Decision + 4, EncodeConditionalBranch(chunks.Decision + 4, chunks.ReturnCap, Arm64Condition.HS));
            WriteInstruction(text, chunks.Decision + 8, EncodeBranch(chunks.Decision + 8, nextOffset));
        }
        else
        {
            WriteInstruction(text, chunks.Decision, EncodeCompareAndBranchNonZero32(chunks.Decision, chunks.ReturnCap, 0));
            WriteInstruction(text, chunks.Decision + 4, EncodeBranch(chunks.Decision + 4, nextOffset));
            WriteInstruction(text, chunks.Decision + 8, EncodeNop());
        }

        WriteInstruction(text, chunks.ReturnCap, EncodeMovzImmediate32(0, milestone.LevelCap));
        WriteInstruction(text, chunks.ReturnCap + 4, EncodeRet());
    }

    private static void PatchUseGateDynamicCap(byte[] text, int capHelperOffset)
    {
        const int branchOffset = StoryUseGateCompareOffset + 4;
        const int nonRareCandyOffset = 0x007BB26C;
        const int epilogueOffset = 0x007BB2E0;
        const int getLevelOffset = 0x0077A5F0;
        const int itemRegister = 20;
        const uint expectedBranch = 0x54000321;
        const uint moveSelectedPokemonToX0 = 0xAA1303E0;

        ExpectInstruction(text, StoryUseGateCompareOffset, EncodeCmpImmediate(itemRegister, RareCandyItemId), "Rare Candy use-gate compare");
        ExpectInstruction(text, branchOffset, expectedBranch, "Rare Candy use-gate branch");

        var itemCheckCaveOffset = FindNearbyConditionalBranchZeroRun(text, 0x0C, branchOffset);
        if (itemCheckCaveOffset < 0)
        {
            throw NoCodeCave(text, 0x0C, "Royal Candy dynamic use-gate item check");
        }

        WriteInstruction(text, branchOffset, EncodeConditionalBranch(branchOffset, itemCheckCaveOffset, Arm64Condition.NE));
        WriteInstruction(text, itemCheckCaveOffset, EncodeCmpImmediate(itemRegister, RoyalCandyItemId));
        WriteInstruction(text, itemCheckCaveOffset + 4, EncodeConditionalBranch(itemCheckCaveOffset + 4, nonRareCandyOffset, Arm64Condition.NE));

        var logicChunks = AllocateCodeCaves(text, 4, "Royal Candy dynamic use-gate logic");
        WriteInstruction(text, itemCheckCaveOffset + 8, EncodeBranch(itemCheckCaveOffset + 8, logicChunks[0]));
        WriteInstruction(text, logicChunks[0], moveSelectedPokemonToX0);
        WriteInstruction(text, logicChunks[0] + 4, EncodeBranchLink(logicChunks[0] + 4, getLevelOffset));
        WriteInstruction(text, logicChunks[0] + 8, EncodeBranch(logicChunks[0] + 8, logicChunks[1]));
        WriteInstruction(text, logicChunks[1], EncodeMovRegister32(21, 0));
        WriteInstruction(text, logicChunks[1] + 4, EncodeBranchLink(logicChunks[1] + 4, capHelperOffset));
        WriteInstruction(text, logicChunks[1] + 8, EncodeBranch(logicChunks[1] + 8, logicChunks[2]));
        WriteInstruction(text, logicChunks[2], EncodeCmpRegister32(21, 0));
        WriteInstruction(text, logicChunks[2] + 4, EncodeMovzImmediate32(8, 1));
        WriteInstruction(text, logicChunks[2] + 8, EncodeBranch(logicChunks[2] + 8, logicChunks[3]));
        WriteInstruction(text, logicChunks[3], EncodeConditionalSelect32(0, 8, 31, Arm64Condition.LT));
        WriteInstruction(text, logicChunks[3] + 4, EncodeBranch(logicChunks[3] + 4, epilogueOffset));
    }

    private static void PatchQuantityMaxDynamicCap(byte[] text, int capHelperOffset)
    {
        const int branchOffset = StoryQuantityMaxCompareOffset + 4;
        const int nonRareCandyOffset = 0x007BB3EC;
        const int epilogueOffset = 0x007BB458;
        const int getLevelOffset = 0x0077A5F0;
        const int itemRegister = 19;
        const uint expectedBranch = 0x54000141;
        const uint moveSelectedPokemonToX0 = 0xAA1403E0;

        ExpectInstruction(text, StoryQuantityMaxCompareOffset, EncodeCmpImmediate(itemRegister, RareCandyItemId), "Rare Candy quantity-cap compare");
        ExpectInstruction(text, branchOffset, expectedBranch, "Rare Candy quantity-cap branch");

        var itemCheckCaveOffset = FindNearbyConditionalBranchZeroRun(text, 0x0C, branchOffset);
        if (itemCheckCaveOffset < 0)
        {
            throw NoCodeCave(text, 0x0C, "Royal Candy dynamic quantity item check");
        }

        WriteInstruction(text, branchOffset, EncodeConditionalBranch(branchOffset, itemCheckCaveOffset, Arm64Condition.NE));
        WriteInstruction(text, itemCheckCaveOffset, EncodeCmpImmediate(itemRegister, RoyalCandyItemId));
        WriteInstruction(text, itemCheckCaveOffset + 4, EncodeConditionalBranch(itemCheckCaveOffset + 4, nonRareCandyOffset, Arm64Condition.NE));

        var logicChunks = AllocateCodeCaves(text, 4, "Royal Candy dynamic quantity logic");
        WriteInstruction(text, itemCheckCaveOffset + 8, EncodeBranch(itemCheckCaveOffset + 8, logicChunks[0]));
        WriteInstruction(text, logicChunks[0], moveSelectedPokemonToX0);
        WriteInstruction(text, logicChunks[0] + 4, EncodeBranchLink(logicChunks[0] + 4, getLevelOffset));
        WriteInstruction(text, logicChunks[0] + 8, EncodeBranch(logicChunks[0] + 8, logicChunks[1]));
        WriteInstruction(text, logicChunks[1], EncodeMovRegister32(21, 0));
        WriteInstruction(text, logicChunks[1] + 4, EncodeBranchLink(logicChunks[1] + 4, capHelperOffset));
        WriteInstruction(text, logicChunks[1] + 8, EncodeBranch(logicChunks[1] + 8, logicChunks[2]));
        WriteInstruction(text, logicChunks[2], EncodeSubRegister32(0, 0, 21));
        WriteInstruction(text, logicChunks[2] + 4, EncodeCmpImmediate(0, 0));
        WriteInstruction(text, logicChunks[2] + 8, EncodeBranch(logicChunks[2] + 8, logicChunks[3]));
        WriteInstruction(text, logicChunks[3], EncodeConditionalSelect32(0, 0, 31, Arm64Condition.GT));
        WriteInstruction(text, logicChunks[3] + 4, EncodeBranch(logicChunks[3] + 4, epilogueOffset));
    }

    private static void PatchQuantityInventoryClampBypass(byte[] text)
    {
        const int originalCompareOffset = 0x007BAF38;
        const int resumeOffset = 0x007BAF40;
        const int getItemIdOffset = 0x007C8330;
        const uint expectedCompare = 0x6B36231F;
        const uint moveSelectedItemToX0 = 0xAA1703E0;

        ExpectInstruction(text, originalCompareOffset, expectedCompare, "quantity clamp compare");
        ExpectInstruction(text, StoryInventoryClampSelectOffset, ExpectedQuantityClampSelect, "quantity clamp CSEL");

        var firstCaveOffset = AllocateCodeCave(text, 0x0C, "Royal Candy inventory clamp first stub");
        WriteInstruction(text, firstCaveOffset, moveSelectedItemToX0);
        WriteInstruction(text, firstCaveOffset + 4, EncodeBranchLink(firstCaveOffset + 4, getItemIdOffset));

        var secondCaveOffset = AllocateCodeCave(text, 0x0C, "Royal Candy inventory clamp item check");
        WriteInstruction(text, firstCaveOffset + 8, EncodeBranch(firstCaveOffset + 8, secondCaveOffset));
        WriteInstruction(text, secondCaveOffset, EncodeCmpImmediate(0, RoyalCandyItemId));
        WriteInstruction(text, secondCaveOffset + 4, EncodeConditionalBranch(secondCaveOffset + 4, resumeOffset, Arm64Condition.EQ));

        var thirdCaveOffset = AllocateCodeCave(text, 0x0C, "Royal Candy inventory clamp vanilla replay");
        WriteInstruction(text, secondCaveOffset + 8, EncodeBranch(secondCaveOffset + 8, thirdCaveOffset));
        WriteInstruction(text, thirdCaveOffset, expectedCompare);
        WriteInstruction(text, thirdCaveOffset + 4, ExpectedQuantityClampSelect);
        WriteInstruction(text, thirdCaveOffset + 8, EncodeBranch(thirdCaveOffset + 8, resumeOffset));
        WriteInstruction(text, StoryInventoryClampSelectOffset, EncodeBranch(StoryInventoryClampSelectOffset, firstCaveOffset));
    }

    private static int FindCodeCave(byte[] text, int requiredBytes, string label)
    {
        var caveOffset = FindZeroRun(text, requiredBytes, RareCandyUiHookCodeCaveSearchStart);
        if (caveOffset >= 0)
        {
            return caveOffset;
        }

        caveOffset = FindZeroRun(text, requiredBytes, 0);
        if (caveOffset >= 0)
        {
            return caveOffset;
        }

        throw NoCodeCave(text, requiredBytes, label);
    }

    private static int AllocateCodeCave(byte[] text, int requiredBytes, string label)
    {
        var caveOffset = FindCodeCave(text, requiredBytes, label);
        ReserveCodeCave(text, caveOffset, requiredBytes);
        return caveOffset;
    }

    private static int[] AllocateCodeCaves(byte[] text, int count, string label)
    {
        var offsets = new int[count];
        for (var index = 0; index < offsets.Length; index++)
        {
            offsets[index] = AllocateCodeCave(text, 0x0C, $"{label} chunk {index}");
        }

        return offsets;
    }

    private static InvalidDataException NoCodeCave(byte[] text, int requiredBytes, string label)
    {
        var largest = FindLargestZeroRun(text);
        return new InvalidDataException(
            $"Could not find a {requiredBytes}-byte zero-filled code cave for {label}. Largest run: text+0x{largest.Offset:X} length 0x{largest.Length:X}.");
    }

    private static int FindZeroRun(byte[] data, int requiredBytes, int startOffset)
    {
        var runStart = -1;
        for (var offset = Math.Max(0, startOffset); offset < data.Length; offset++)
        {
            if (data[offset] == 0)
            {
                if (runStart < 0)
                {
                    runStart = offset;
                }

                var alignedStart = (runStart + 3) & ~3;
                if (offset - alignedStart + 1 >= requiredBytes && IsAvailableCodeCave(alignedStart, requiredBytes))
                {
                    return alignedStart;
                }

                continue;
            }

            runStart = -1;
        }

        return -1;
    }

    private static int FindNearbyConditionalBranchZeroRun(byte[] text, int requiredBytes, int anchorOffset)
    {
        const int ConditionalBranchReachBytes = (1 << 18) * 4;
        var minOffset = Math.Max(0, anchorOffset - ConditionalBranchReachBytes + requiredBytes);
        var maxOffset = Math.Min(text.Length - requiredBytes, anchorOffset + ConditionalBranchReachBytes - requiredBytes);
        if (minOffset > maxOffset)
        {
            return -1;
        }

        var afterAnchor = FindZeroRunWithin(text, requiredBytes, anchorOffset, maxOffset);
        if (afterAnchor >= 0)
        {
            return afterAnchor;
        }

        return FindZeroRunWithin(text, requiredBytes, minOffset, anchorOffset);
    }

    private static int FindZeroRunWithin(byte[] data, int requiredBytes, int startOffset, int endOffset)
    {
        var runStart = -1;
        var start = Math.Max(0, startOffset);
        var end = Math.Min(data.Length - 1, endOffset);
        for (var offset = start; offset <= end; offset++)
        {
            if (data[offset] == 0)
            {
                if (runStart < 0)
                {
                    runStart = offset;
                }

                var alignedStart = (runStart + 3) & ~3;
                if (offset - alignedStart + 1 >= requiredBytes && IsAvailableCodeCave(alignedStart, requiredBytes))
                {
                    return alignedStart;
                }

                continue;
            }

            runStart = -1;
        }

        return -1;
    }

    private static ZeroRun FindLargestZeroRun(byte[] data)
    {
        var best = new ZeroRun(-1, 0);
        var runStart = -1;
        for (var offset = 0; offset < data.Length; offset++)
        {
            if (data[offset] == 0)
            {
                if (runStart < 0)
                {
                    runStart = offset;
                }

                var length = offset - runStart + 1;
                if (length > best.Length)
                {
                    best = new ZeroRun(runStart, length);
                }

                continue;
            }

            runStart = -1;
        }

        return best;
    }

    private static void ExpectInstruction(byte[] text, int offset, uint expectedInstruction, string label)
    {
        if (offset < 0 || offset + 4 > text.Length)
        {
            throw new InvalidDataException($"{label} is outside the decompressed .text segment.");
        }

        var actualInstruction = BinaryPrimitives.ReadUInt32LittleEndian(text.AsSpan(offset, 4));
        if (actualInstruction != expectedInstruction)
        {
            throw new InvalidDataException(
                $"Unexpected {label}: 0x{actualInstruction:X8}; expected 0x{expectedInstruction:X8}.");
        }
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction)
    {
        if (offset < 0 || offset + 4 > text.Length)
        {
            throw new InvalidDataException($"Patch instruction target text+0x{offset:X} is outside the decompressed .text segment.");
        }

        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, 4), instruction);
    }

    private static void ReserveCodeCave(byte[] text, int offset, int length)
    {
        for (var current = offset; current < offset + length; current += 4)
        {
            WriteInstruction(text, current, EncodeNop());
        }
    }

    private static bool IsAvailableCodeCave(int offset, int length)
    {
        return !NonRoyalCandyReservations.Any(region => SwShExeFsReservedRegionLedger.Overlaps(region, offset, length));
    }

    private static bool IsUiRouteCheckInstalled(ReadOnlySpan<byte> text, RareCandyUiCheck check)
    {
        var branchOffset = check.CompareOffset + 4;
        if (!TryReadInstruction(text, check.CompareOffset, out var compareInstruction)
            || compareInstruction != EncodeCmpImmediate(check.ItemRegister, RareCandyItemId)
            || !TryReadInstruction(text, branchOffset, out var branchInstruction))
        {
            return false;
        }

        var vanillaBranch = EncodeConditionalBranch(branchOffset, check.FailOffset, Arm64Condition.NE);
        return branchInstruction != vanillaBranch
            && IsConditionalBranch(branchInstruction, Arm64Condition.NE);
    }

    private static bool IsEqualBranchCheckInstalled(ReadOnlySpan<byte> text, RareCandyEqualBranchCheck check)
    {
        return TryReadInstruction(text, check.CompareOffset, out var compareInstruction)
            && TryReadInstruction(text, check.CompareOffset + 4, out var branchInstruction)
            && IsUnconditionalBranch(compareInstruction)
            && branchInstruction == NopInstruction;
    }

    private static int CountInstalledExpCandyBypass(ReadOnlySpan<byte> text)
    {
        var count = 0;
        foreach (var offset in new[] { 0x007BC1BC, 0x007BC1C4 })
        {
            if (TryReadInstruction(text, offset, out var instruction)
                && instruction == EncodeCmpImmediate(register: 9, immediate: 3))
            {
                count++;
            }
        }

        return count;
    }

    private static bool HasForeignReservedAnchorChange(ReadOnlySpan<byte> text)
    {
        if (TryReadInstruction(text, QuantityMoveOffset, out var quantityMove)
            && quantityMove != ExpectedQuantityMove
            && !IsUnconditionalBranch(quantityMove))
        {
            return true;
        }

        if (TryReadInstruction(text, StoryInventoryClampSelectOffset, out var storyClampSelect)
            && storyClampSelect != ExpectedQuantityClampSelect
            && !IsUnconditionalBranch(storyClampSelect))
        {
            return true;
        }

        foreach (var offset in new[] { 0x007BC1BC, 0x007BC1C4 })
        {
            if (TryReadInstruction(text, offset, out var instruction)
                && instruction != EncodeCmpImmediate(register: 9, immediate: 4)
                && instruction != EncodeCmpImmediate(register: 9, immediate: 3))
            {
                return true;
            }
        }

        foreach (var check in UiRouteChecks)
        {
            var branchOffset = check.CompareOffset + 4;
            if (!TryReadInstruction(text, check.CompareOffset, out var compareInstruction)
                || !TryReadInstruction(text, branchOffset, out var branchInstruction))
            {
                return true;
            }

            var expectedCompare = EncodeCmpImmediate(check.ItemRegister, RareCandyItemId);
            var expectedBranch = EncodeConditionalBranch(branchOffset, check.FailOffset, Arm64Condition.NE);
            if (compareInstruction != expectedCompare)
            {
                return true;
            }

            if (branchInstruction != expectedBranch
                && !IsConditionalBranch(branchInstruction, Arm64Condition.NE))
            {
                return true;
            }
        }

        foreach (var check in EqualBranchChecks)
        {
            if (!TryReadInstruction(text, check.CompareOffset, out var compareInstruction)
                || !TryReadInstruction(text, check.CompareOffset + 4, out var branchInstruction))
            {
                return true;
            }

            var expectedCompare = EncodeCmpImmediate(check.ItemRegister, RareCandyItemId);
            var expectedBranch = EncodeConditionalBranch(check.CompareOffset + 4, check.TargetOffset, Arm64Condition.EQ);
            var looksInstalled = IsUnconditionalBranch(compareInstruction) && branchInstruction == NopInstruction;
            if (!looksInstalled && (compareInstruction != expectedCompare || branchInstruction != expectedBranch))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveStoryCapHelperOffset(ReadOnlySpan<byte> text, out int helperOffset)
    {
        helperOffset = 0;
        const int branchOffset = StoryUseGateCompareOffset + 4;

        if (!TryReadInstruction(text, branchOffset, out var itemCheckBranch)
            || !IsConditionalBranch(itemCheckBranch, Arm64Condition.NE)
            || !TryDecodeConditionalBranchTarget(itemCheckBranch, branchOffset, out var itemCheckOffset)
            || !TryReadInstruction(text, itemCheckOffset + 8, out var firstLogicBranch)
            || !TryDecodeBranchTarget(firstLogicBranch, itemCheckOffset + 8, out var firstLogicOffset)
            || !TryReadInstruction(text, firstLogicOffset + 8, out var secondLogicBranch)
            || !TryDecodeBranchTarget(secondLogicBranch, firstLogicOffset + 8, out var secondLogicOffset)
            || !TryReadInstruction(text, secondLogicOffset + 4, out var helperCall)
            || !IsBranchLink(helperCall)
            || !TryDecodeBranchTarget(helperCall, secondLogicOffset + 4, out helperOffset))
        {
            return false;
        }

        return helperOffset >= 0 && helperOffset < text.Length;
    }

    private static IReadOnlyList<SwShRoyalCandyInstalledStoryLevelCap> ReadStoryCapLadder(
        ReadOnlySpan<byte> text,
        int helperOffset,
        RoyalCandyPatchLayout layout)
    {
        var records = new List<SwShRoyalCandyInstalledStoryLevelCap>();
        var visited = new HashSet<int>();
        var offset = helperOffset;
        while (records.Count < 64 && visited.Add(offset))
        {
            if (IsStoryCapDefaultReturn(text, offset))
            {
                break;
            }

            if (!TryReadStoryCapCheck(text, offset, layout, out var record, out var nextOffset))
            {
                break;
            }

            records.Add(record);
            offset = nextOffset;
        }

        return records;
    }

    private static bool TryReadStoryCapCheck(
        ReadOnlySpan<byte> text,
        int loadGlobalOffset,
        RoyalCandyPatchLayout layout,
        out SwShRoyalCandyInstalledStoryLevelCap record,
        out int nextOffset)
    {
        record = null!;
        nextOffset = 0;

        if (!TryReadInstruction(text, loadGlobalOffset + 8, out var loadTableBranch)
            || !TryDecodeBranchTarget(loadTableBranch, loadGlobalOffset + 8, out var loadTableOffset)
            || !TryReadInstruction(text, loadTableOffset + 8, out var hashLowBranch)
            || !TryDecodeBranchTarget(hashLowBranch, loadTableOffset + 8, out var hashLowOffset)
            || !TryReadInstruction(text, hashLowOffset, out var hashLowMovz)
            || !TryReadInstruction(text, hashLowOffset + 4, out var hashMidLowMovk)
            || !TryDecodeMovzImmediate64(hashLowMovz, register: 1, shift: 0, out var hashPart0)
            || !TryDecodeMovkImmediate64(hashMidLowMovk, register: 1, shift: 16, out var hashPart1)
            || !TryReadInstruction(text, hashLowOffset + 8, out var hashHighBranch)
            || !TryDecodeBranchTarget(hashHighBranch, hashLowOffset + 8, out var hashHighOffset)
            || !TryReadInstruction(text, hashHighOffset, out var hashMidHighMovk)
            || !TryReadInstruction(text, hashHighOffset + 4, out var hashHighMovk)
            || !TryDecodeMovkImmediate64(hashMidHighMovk, register: 1, shift: 32, out var hashPart2)
            || !TryDecodeMovkImmediate64(hashHighMovk, register: 1, shift: 48, out var hashPart3)
            || !TryReadInstruction(text, hashHighOffset + 8, out var callBranch)
            || !TryDecodeBranchTarget(callBranch, hashHighOffset + 8, out var callOffset)
            || !TryReadInstruction(text, callOffset + 4, out var accessorCall)
            || !TryDecodeBranchTarget(accessorCall, callOffset + 4, out var accessorOffset)
            || !TryReadInstruction(text, callOffset + 8, out var restoreBranch)
            || !TryDecodeBranchTarget(restoreBranch, callOffset + 8, out var restoreOffset)
            || !TryReadInstruction(text, restoreOffset + 4, out var decisionBranch)
            || !TryDecodeBranchTarget(decisionBranch, restoreOffset + 4, out var decisionOffset))
        {
            return false;
        }

        var progressHash = (ulong)(ushort)hashPart0
            | ((ulong)(ushort)hashPart1 << 16)
            | ((ulong)(ushort)hashPart2 << 32)
            | ((ulong)(ushort)hashPart3 << 48);

        var progressKind = accessorOffset switch
        {
            var offset when offset == layout.FlagGetOffset => SwShRoyalCandyStoryLevelCapProgressKind.Flag,
            var offset when offset == layout.WorkGetOffset => SwShRoyalCandyStoryLevelCapProgressKind.WorkAtLeast,
            _ => (SwShRoyalCandyStoryLevelCapProgressKind?)null,
        };
        if (progressKind is null)
        {
            return false;
        }

        int returnCapOffset;
        var workMinimum = 0;
        if (progressKind.Value == SwShRoyalCandyStoryLevelCapProgressKind.WorkAtLeast)
        {
            if (!TryReadInstruction(text, decisionOffset, out var compareInstruction)
                || !TryDecodeCmpImmediate(compareInstruction, register: 0, out workMinimum)
                || !TryReadInstruction(text, decisionOffset + 4, out var capBranch)
                || !IsConditionalBranch(capBranch, Arm64Condition.HS)
                || !TryDecodeConditionalBranchTarget(capBranch, decisionOffset + 4, out returnCapOffset)
                || !TryReadInstruction(text, decisionOffset + 8, out var nextBranch)
                || !TryDecodeBranchTarget(nextBranch, decisionOffset + 8, out nextOffset))
            {
                return false;
            }
        }
        else
        {
            if (!TryReadInstruction(text, decisionOffset, out var capBranch)
                || !TryDecodeCompareAndBranchNonZero32(capBranch, register: 0, decisionOffset, out returnCapOffset)
                || !TryReadInstruction(text, decisionOffset + 4, out var nextBranch)
                || !TryDecodeBranchTarget(nextBranch, decisionOffset + 4, out nextOffset))
            {
                return false;
            }
        }

        if (!TryReadInstruction(text, returnCapOffset, out var returnCapInstruction)
            || !TryDecodeMovzImmediate32(returnCapInstruction, register: 0, out var levelCap))
        {
            return false;
        }

        record = new SwShRoyalCandyInstalledStoryLevelCap(
            levelCap,
            progressHash,
            progressKind.Value,
            workMinimum);
        return true;
    }

    private static bool IsStoryCapDefaultReturn(ReadOnlySpan<byte> text, int offset)
    {
        return TryReadInstruction(text, offset, out var capInstruction)
            && TryDecodeMovzImmediate32(capInstruction, register: 0, out _)
            && TryReadInstruction(text, offset + 4, out var retInstruction)
            && retInstruction == EncodeRet();
    }

    private static bool TryReadInstruction(ReadOnlySpan<byte> text, int offset, out uint instruction)
    {
        instruction = 0;
        if (offset < 0 || offset + sizeof(uint) > text.Length)
        {
            return false;
        }

        instruction = BinaryPrimitives.ReadUInt32LittleEndian(text.Slice(offset, sizeof(uint)));
        return true;
    }

    private static bool IsConditionalBranch(uint instruction, Arm64Condition condition)
    {
        return (instruction & 0xFF000010u) == 0x54000000u
            && (instruction & 0xFu) == (uint)condition;
    }

    private static bool IsUnconditionalBranch(uint instruction)
    {
        return (instruction & 0xFC000000u) == 0x14000000u;
    }

    private static void ClearReachableRoyalCandyCaves(
        byte[] currentText,
        ReadOnlySpan<byte> baseText,
        RoyalCandyPatchLayout layout)
    {
        var seeds = new List<int>();
        var trustedStoryRoots = new HashSet<int>();
        foreach (var check in UiRouteChecks)
        {
            var branchOffset = check.CompareOffset + 4;
            if (TryReadInstruction(currentText, branchOffset, out var instruction)
                && IsConditionalBranch(instruction, Arm64Condition.NE)
                && instruction != EncodeConditionalBranch(branchOffset, check.FailOffset, Arm64Condition.NE)
                && TryDecodeConditionalBranchTarget(instruction, branchOffset, out var target))
            {
                seeds.Add(target);
                if (IsVerifiedStoryItemCheckRoot(currentText, baseText, check, target))
                {
                    trustedStoryRoots.Add(target);
                }
            }
        }

        foreach (var check in EqualBranchChecks)
        {
            if (TryReadInstruction(currentText, check.CompareOffset, out var instruction)
                && IsUnconditionalBranch(instruction)
                && TryDecodeBranchTarget(instruction, check.CompareOffset, out var target))
            {
                seeds.Add(target);
            }
        }

        if (TryReadInstruction(currentText, QuantityMoveOffset, out var quantityMove)
            && IsUnconditionalBranch(quantityMove)
            && TryDecodeBranchTarget(quantityMove, QuantityMoveOffset, out var quantityMoveTarget))
        {
            seeds.Add(quantityMoveTarget);
        }

        if (TryReadInstruction(currentText, AllowedConsumableBranchOffset, out var allowedConsumableBranch)
            && IsConditionalBranch(allowedConsumableBranch, Arm64Condition.HI)
            && allowedConsumableBranch != EncodeConditionalBranch(AllowedConsumableBranchOffset, AllowedConsumableFailOffset, Arm64Condition.HI)
            && TryDecodeConditionalBranchTarget(allowedConsumableBranch, AllowedConsumableBranchOffset, out var allowedConsumableTarget))
        {
            seeds.Add(allowedConsumableTarget);
        }

        AddVirtualInventoryHookSeed(
            currentText,
            layout.ItemOwnershipFunctionOffset,
            ExpectedItemOwnershipFirstInstruction,
            seeds);
        AddVirtualInventoryHookSeed(
            currentText,
            layout.ItemCountFunctionOffset,
            ExpectedItemCountFirstInstruction,
            seeds);

        if (TryReadInstruction(currentText, StoryInventoryClampSelectOffset, out var storyClampSelect)
            && IsUnconditionalBranch(storyClampSelect)
            && TryDecodeBranchTarget(storyClampSelect, StoryInventoryClampSelectOffset, out var storyClampTarget))
        {
            seeds.Add(storyClampTarget);
        }

        ClearReachableCodeCaves(currentText, baseText, seeds, trustedStoryRoots);
    }

    private static bool IsVerifiedStoryItemCheckRoot(
        ReadOnlySpan<byte> currentText,
        ReadOnlySpan<byte> baseText,
        RareCandyUiCheck check,
        int target)
    {
        if ((check.CompareOffset != StoryUseGateCompareOffset
                && check.CompareOffset != StoryQuantityMaxCompareOffset)
            || target != check.PassOffset
            || !IsBaseZeroUnreservedCave(baseText, target, 0x0C)
            || !TryReadInstruction(currentText, target, out var compare)
            || compare != EncodeCmpImmediate(check.ItemRegister, RoyalCandyItemId)
            || !TryReadInstruction(currentText, target + 4, out var nonRoyalBranch)
            || nonRoyalBranch != EncodeConditionalBranch(target + 4, check.FailOffset, Arm64Condition.NE)
            || !TryReadInstruction(currentText, target + 8, out var logicBranch)
            || !IsUnconditionalBranch(logicBranch)
            || !TryDecodeBranchTarget(logicBranch, target + 8, out var logicTarget))
        {
            return false;
        }

        return IsRestorableCodeCave(baseText, logicTarget, 0x0C);
    }

    private static void AddVirtualInventoryHookSeed(
        ReadOnlySpan<byte> currentText,
        int hookOffset,
        uint expectedFirstInstruction,
        ICollection<int> seeds)
    {
        if (TryReadInstruction(currentText, hookOffset, out var instruction)
            && instruction != expectedFirstInstruction
            && IsUnconditionalBranch(instruction)
            && TryDecodeBranchTarget(instruction, hookOffset, out var target))
        {
            seeds.Add(target);
        }
    }

    private static void ClearReachableCodeCaves(byte[] currentText, ReadOnlySpan<byte> baseText, IEnumerable<int> seeds)
    {
        ClearReachableCodeCaves(currentText, baseText, seeds, new HashSet<int>());
    }

    private static void ClearReachableCodeCaves(
        byte[] currentText,
        ReadOnlySpan<byte> baseText,
        IEnumerable<int> seeds,
        IReadOnlySet<int> trustedBelowBoundaryRoots)
    {
        var pending = new Queue<int>(seeds);
        var visited = new HashSet<int>();
        while (pending.Count > 0)
        {
            var offset = pending.Dequeue();
            var isRestorable = trustedBelowBoundaryRoots.Contains(offset)
                ? IsBaseZeroUnreservedCave(baseText, offset, 0x0C)
                : IsRestorableCodeCave(baseText, offset, 0x0C);
            if (!visited.Add(offset) || !isRestorable)
            {
                continue;
            }

            foreach (var target in ReadCaveBranchTargets(currentText, offset))
            {
                if (IsRestorableCodeCave(baseText, target, 0x0C))
                {
                    pending.Enqueue(target);
                }
            }

            baseText.Slice(offset, 0x0C).CopyTo(currentText.AsSpan(offset, 0x0C));
        }
    }

    private static IReadOnlyList<int> ReadCaveBranchTargets(ReadOnlySpan<byte> text, int caveOffset)
    {
        var targets = new List<int>();
        for (var offset = caveOffset; offset < caveOffset + 0x0C; offset += 4)
        {
            if (!TryReadInstruction(text, offset, out var instruction))
            {
                continue;
            }

            if (IsUnconditionalBranch(instruction)
                && TryDecodeBranchTarget(instruction, offset, out var branchTarget))
            {
                targets.Add(branchTarget);
            }
            else if (IsConditionalBranchInstruction(instruction)
                && TryDecodeConditionalBranchTarget(instruction, offset, out var conditionalTarget))
            {
                targets.Add(conditionalTarget);
            }
            else if (IsCompareAndBranchInstruction(instruction)
                && TryDecodeCompareAndBranchTarget(instruction, offset, out var compareBranchTarget))
            {
                targets.Add(compareBranchTarget);
            }
            else if (IsBranchLink(instruction)
                && TryDecodeBranchTarget(instruction, offset, out var branchLinkTarget)
                && !ExternalBranchLinkTargets.Contains(branchLinkTarget))
            {
                targets.Add(branchLinkTarget);
            }
        }

        return targets;
    }

    private static bool IsRestorableCodeCave(ReadOnlySpan<byte> baseText, int offset, int length)
    {
        return offset >= RareCandyUiHookCodeCaveSearchStart
            && IsBaseZeroUnreservedCave(baseText, offset, length);
    }

    private static bool IsBaseZeroUnreservedCave(ReadOnlySpan<byte> baseText, int offset, int length)
    {
        return offset >= 0
            && offset % 4 == 0
            && offset + length <= baseText.Length
            && baseText.Slice(offset, length).IndexOfAnyExcept((byte)0) < 0
            && !MainTextReservations.Any(region => SwShExeFsReservedRegionLedger.Overlaps(region, offset, length));
    }

    private static bool IsConditionalBranchInstruction(uint instruction)
    {
        return (instruction & 0xFF000010u) == 0x54000000u;
    }

    private static bool IsCompareAndBranchInstruction(uint instruction)
    {
        return (instruction & 0x7E000000u) == 0x34000000u;
    }

    private static bool IsBranchLink(uint instruction)
    {
        return (instruction & 0xFC000000u) == 0x94000000u;
    }

    private static bool TryDecodeBranchTarget(uint instruction, int sourceOffset, out int targetOffset)
    {
        targetOffset = 0;
        if (!IsUnconditionalBranch(instruction) && !IsBranchLink(instruction))
        {
            return false;
        }

        var immediate = SignExtend((int)(instruction & 0x03FFFFFF), 26) << 2;
        targetOffset = sourceOffset + immediate;
        return true;
    }

    private static bool TryDecodeConditionalBranchTarget(uint instruction, int sourceOffset, out int targetOffset)
    {
        targetOffset = 0;
        if (!IsConditionalBranchInstruction(instruction))
        {
            return false;
        }

        var immediate = SignExtend((int)((instruction >> 5) & 0x7FFFF), 19) << 2;
        targetOffset = sourceOffset + immediate;
        return true;
    }

    private static bool TryDecodeCompareAndBranchTarget(uint instruction, int sourceOffset, out int targetOffset)
    {
        targetOffset = 0;
        if (!IsCompareAndBranchInstruction(instruction))
        {
            return false;
        }

        var immediate = SignExtend((int)((instruction >> 5) & 0x7FFFF), 19) << 2;
        targetOffset = sourceOffset + immediate;
        return true;
    }

    private static bool TryDecodeCompareAndBranchNonZero32(
        uint instruction,
        int register,
        int sourceOffset,
        out int targetOffset)
    {
        targetOffset = 0;
        if ((instruction & 0xFF000000u) != 0x35000000u
            || (instruction & 0x1Fu) != (uint)(register & 0x1F))
        {
            return false;
        }

        return TryDecodeCompareAndBranchTarget(instruction, sourceOffset, out targetOffset);
    }

    private static bool TryDecodeMovzImmediate32(uint instruction, int register, out int immediate)
    {
        immediate = 0;
        if ((instruction & 0xFF800000u) != 0x52800000u
            || (instruction & 0x1Fu) != (uint)(register & 0x1F)
            || ((instruction >> 21) & 0x3u) != 0)
        {
            return false;
        }

        immediate = (int)((instruction >> 5) & 0xFFFF);
        return true;
    }

    private static bool TryDecodeMovzImmediate64(
        uint instruction,
        int register,
        int shift,
        out int immediate)
    {
        immediate = 0;
        if ((instruction & 0xFF800000u) != 0xD2800000u
            || (instruction & 0x1Fu) != (uint)(register & 0x1F)
            || ((instruction >> 21) & 0x3u) != (uint)(shift / 16))
        {
            return false;
        }

        immediate = (int)((instruction >> 5) & 0xFFFF);
        return true;
    }

    private static bool TryDecodeMovkImmediate64(
        uint instruction,
        int register,
        int shift,
        out int immediate)
    {
        immediate = 0;
        if ((instruction & 0xFF800000u) != 0xF2800000u
            || (instruction & 0x1Fu) != (uint)(register & 0x1F)
            || ((instruction >> 21) & 0x3u) != (uint)(shift / 16))
        {
            return false;
        }

        immediate = (int)((instruction >> 5) & 0xFFFF);
        return true;
    }

    private static bool TryDecodeCmpImmediate(uint instruction, int register, out int immediate)
    {
        immediate = 0;
        if ((instruction & 0xFFC0001Fu) != 0x7100001Fu
            || ((instruction >> 5) & 0x1Fu) != (uint)(register & 0x1F))
        {
            return false;
        }

        immediate = (int)((instruction >> 10) & 0xFFF);
        return true;
    }

    private static int SignExtend(int value, int bitCount)
    {
        var shift = 32 - bitCount;
        return (value << shift) >> shift;
    }

    private static uint EncodeCmpImmediate(int register, int immediate)
    {
        return (uint)(0x7100001F | ((immediate & 0xFFF) << 10) | ((register & 0x1F) << 5));
    }

    private static uint EncodeConditionalBranch(int sourceOffset, int targetOffset, Arm64Condition condition)
    {
        var delta = targetOffset - sourceOffset;
        if ((delta & 3) != 0)
        {
            throw new InvalidDataException("Conditional branch target must be 4-byte aligned.");
        }

        var imm19 = delta >> 2;
        if (imm19 < -(1 << 18) || imm19 >= (1 << 18))
        {
            throw new InvalidDataException("Conditional branch target is outside ARM64 range.");
        }

        return (uint)(0x54000000 | ((imm19 & 0x7FFFF) << 5) | ((int)condition & 0xF));
    }

    private static uint EncodeBranch(int sourceOffset, int targetOffset)
    {
        var delta = targetOffset - sourceOffset;
        if ((delta & 3) != 0)
        {
            throw new InvalidDataException("Branch target must be 4-byte aligned.");
        }

        var imm26 = delta >> 2;
        if (imm26 < -(1 << 25) || imm26 >= (1 << 25))
        {
            throw new InvalidDataException("Branch target is outside ARM64 range.");
        }

        return (uint)(0x14000000 | (imm26 & 0x03FFFFFF));
    }

    private static uint EncodeCompareAndBranchNonZero32(int sourceOffset, int targetOffset, int register)
    {
        var delta = targetOffset - sourceOffset;
        if ((delta & 3) != 0)
        {
            throw new InvalidDataException("Compare-and-branch target must be 4-byte aligned.");
        }

        var imm19 = delta >> 2;
        if (imm19 < -(1 << 18) || imm19 >= (1 << 18))
        {
            throw new InvalidDataException("Compare-and-branch target is outside ARM64 range.");
        }

        return (uint)(0x35000000 | ((imm19 & 0x7FFFF) << 5) | (register & 0x1F));
    }

    private static uint EncodeBranchLink(int sourceOffset, int targetOffset)
    {
        var delta = targetOffset - sourceOffset;
        if ((delta & 3) != 0)
        {
            throw new InvalidDataException("Branch-link target must be 4-byte aligned.");
        }

        var imm26 = delta >> 2;
        if (imm26 < -(1 << 25) || imm26 >= (1 << 25))
        {
            throw new InvalidDataException("Branch-link target is outside ARM64 range.");
        }

        return (uint)(0x94000000 | (imm26 & 0x03FFFFFF));
    }

    private static uint EncodeConditionalSelect32(
        int destinationRegister,
        int trueRegister,
        int falseRegister,
        Arm64Condition condition)
    {
        return (uint)(0x1A800000
            | (((int)condition & 0xF) << 12)
            | ((falseRegister & 0x1F) << 16)
            | ((trueRegister & 0x1F) << 5)
            | (destinationRegister & 0x1F));
    }

    private static uint EncodeMovzImmediate32(int register, int immediate)
    {
        if (immediate is < 0 or > 0xFFFF)
        {
            throw new InvalidDataException("MOVZ immediate must fit in 16 bits.");
        }

        return (uint)(0x52800000 | ((immediate & 0xFFFF) << 5) | (register & 0x1F));
    }

    private static uint EncodeMovzImmediate64(int register, int immediate, int shift)
    {
        if (immediate is < 0 or > 0xFFFF)
        {
            throw new InvalidDataException("MOVZ immediate must fit in 16 bits.");
        }

        if (shift is not (0 or 16 or 32 or 48))
        {
            throw new InvalidDataException("MOVZ 64-bit shift must be 0, 16, 32, or 48.");
        }

        return 0xD2800000u
            | (uint)((shift / 16) << 21)
            | (uint)((immediate & 0xFFFF) << 5)
            | (uint)(register & 0x1F);
    }

    private static uint EncodeMovkImmediate64(int register, int immediate, int shift)
    {
        if (immediate is < 0 or > 0xFFFF)
        {
            throw new InvalidDataException("MOVK immediate must fit in 16 bits.");
        }

        if (shift is not (0 or 16 or 32 or 48))
        {
            throw new InvalidDataException("MOVK 64-bit shift must be 0, 16, 32, or 48.");
        }

        return 0xF2800000u
            | (uint)((shift / 16) << 21)
            | (uint)((immediate & 0xFFFF) << 5)
            | (uint)(register & 0x1F);
    }

    private static uint EncodeMovRegister32(int destinationRegister, int sourceRegister)
    {
        return (uint)(0x2A0003E0 | ((sourceRegister & 0x1F) << 16) | (destinationRegister & 0x1F));
    }

    private static uint EncodeCmpRegister32(int leftRegister, int rightRegister)
    {
        return (uint)(0x6B00001F | ((rightRegister & 0x1F) << 16) | ((leftRegister & 0x1F) << 5));
    }

    private static uint EncodeSubRegister32(int destinationRegister, int leftRegister, int rightRegister)
    {
        return (uint)(0x4B000000
            | ((rightRegister & 0x1F) << 16)
            | ((leftRegister & 0x1F) << 5)
            | (destinationRegister & 0x1F));
    }

    private static uint EncodeLdrUnsigned64(int targetRegister, int baseRegister, int byteOffset)
    {
        if ((byteOffset & 7) != 0)
        {
            throw new InvalidDataException("64-bit LDR unsigned offset must be 8-byte aligned.");
        }

        var scaled = byteOffset >> 3;
        if (scaled is < 0 or > 0xFFF)
        {
            throw new InvalidDataException("64-bit LDR unsigned offset must fit the ARM64 imm12 field.");
        }

        return 0xF9400000u
            | (uint)(scaled << 10)
            | (uint)((baseRegister & 0x1F) << 5)
            | (uint)(targetRegister & 0x1F);
    }

    private static uint EncodeAdrp(int register, int sourceOffset, int targetAddress)
    {
        var sourcePage = sourceOffset & ~0xFFF;
        var targetPage = targetAddress & ~0xFFF;
        var pageDelta = (targetPage - sourcePage) >> 12;
        if (pageDelta < -(1 << 20) || pageDelta >= (1 << 20))
        {
            throw new InvalidDataException("ADRP target is outside ARM64 range.");
        }

        var immediate = pageDelta & 0x1FFFFF;
        var immediateLow = immediate & 0x3;
        var immediateHigh = (immediate >> 2) & 0x7FFFF;
        return 0x90000000u
            | (uint)(immediateLow << 29)
            | (uint)(immediateHigh << 5)
            | (uint)(register & 0x1F);
    }

    private static uint EncodeNop()
    {
        return 0xD503201F;
    }

    private static uint EncodeRet()
    {
        return 0xD65F03C0;
    }

    private static readonly RareCandyUiCheck[] UiRouteChecks =
    [
        new(0x00747988, 28, 0x00747990, 0x00747A80),
        new(0x00747D44, 9, 0x00747D4C, 0x007477E8),
        new(0x0074BA24, 26, 0x0074BA2C, 0x0074BAD4),
        new(0x0074BDA8, 9, 0x0074BDB0, 0x0074B788),
        new(0x0074DFE4, 9, 0x0074DFEC, 0x0074DE78),
        new(0x0074DFF8, 28, 0x0074E000, 0x0074E16C),
        new(0x0075CEFC, 9, 0x0075CF04, 0x0075CC18),
        new(0x007BB204, 20, 0x007BB20C, 0x007BB26C),
        new(0x007BB3C0, 19, 0x007BB3C8, 0x007BB3EC),
        new(0x007BC1F8, 8, 0x007BC200, 0x007BC2B4),
    ];

    private static readonly RareCandyEqualBranchCheck[] EqualBranchChecks =
    [
        new(0x00747DE0, 9, 0x00747D4C, 0x00747DE8),
        new(0x0074BE44, 9, 0x0074BDB0, 0x0074BE4C),
        new(0x0075CCE8, 27, 0x0075D064, 0x0075CCF0),
        new(0x0075D08C, 10, 0x0075D05C, 0x0075D094),
        new(0x007BBFD4, 23, 0x007BC054, 0x007BBFDC),
    ];

    private enum Arm64Condition
    {
        EQ = 0,
        NE = 1,
        HS = 2,
        HI = 8,
        LT = 11,
        GT = 12,
    }

    private sealed record RareCandyUiCheck(
        int CompareOffset,
        int ItemRegister,
        int PassOffset,
        int FailOffset);

    private sealed record RareCandyEqualBranchCheck(
        int CompareOffset,
        int ItemRegister,
        int TargetOffset,
        int FallthroughOffset);

    private sealed record RoyalCandyLevelCapCheckChunks(
        int LoadGlobal,
        int LoadTable,
        int HashLow,
        int HashHigh,
        int Call,
        int Restore,
        int Decision,
        int ReturnCap);

    private sealed record ZeroRun(int Offset, int Length);

    private sealed record RoyalCandyPatchLayout(
        int FlagworkGlobalAddress,
        int FlagworkObjectOffset,
        int FlagGetOffset,
        int WorkGetOffset,
        int ItemOwnershipFunctionOffset,
        int ItemCountFunctionOffset);
}
