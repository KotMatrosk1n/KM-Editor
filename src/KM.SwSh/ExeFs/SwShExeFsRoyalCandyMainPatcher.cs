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

    private static readonly IReadOnlyList<SwShExeFsReservedRegion> NonRoyalCandyReservations =
        SwShExeFsReservedRegionLedger.MainTextReservationsForOtherOwners(
            SwShExeFsReservedRegionLedger.OwnerRoyalCandy,
            SwShExeFsReservedRegionLedger.OwnerRoyalCandyStoryLimits);

    private static int ReservedAnchorCount => UiRouteChecks.Length
        + EqualBranchChecks.Length
        + 2
        + 1
        + 1
        + 2
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

        var detectedGame = DetectSupportedGame(nso.BuildId);
        if (detectedGame is null)
        {
            return new SwShRoyalCandyExeFsSignature(
                SwShRoyalCandyExeFsSignatureKind.ForeignPatch,
                "Royal Candy cannot identify this unsupported Sword/Shield executable build.",
                ReservedAnchorCount,
                RecognizedAnchorCount: 0);
        }

        try
        {
            ValidateRequiredSegmentHashes(nso);
        }
        catch (InvalidDataException exception)
        {
            return new SwShRoyalCandyExeFsSignature(
                SwShRoyalCandyExeFsSignatureKind.ForeignPatch,
                exception.Message,
                ReservedAnchorCount,
                RecognizedAnchorCount: 0);
        }

        var text = nso.Text.DecompressedData;
        var layout = ResolvePatchLayout(nso.BuildId, expectedGame);
        if (TryValidateStoryLimitsSignature(text, layout, out _))
        {
            return new SwShRoyalCandyExeFsSignature(
                SwShRoyalCandyExeFsSignatureKind.StoryLimits,
                "Royal Candy with Story Limits ExeFS signature detected from the complete item-route, virtual-inventory, decrement, and story-cap payload graph.",
                ReservedAnchorCount,
                ReservedAnchorCount);
        }

        if (TryValidateUnlimitedSignature(text, layout))
        {
            return new SwShRoyalCandyExeFsSignature(
                SwShRoyalCandyExeFsSignatureKind.Unlimited,
                "Unlimited Royal Candy ExeFS signature detected from the complete item-route, virtual-inventory, and decrement payload graph.",
                ReservedAnchorCount,
                ReservedAnchorCount);
        }

        var recognizedAnchorCount = CountRecognizedExactAnchors(text, layout);
        if (AreRoyalCandyAnchorsVanilla(text, layout))
        {
            return new SwShRoyalCandyExeFsSignature(
                SwShRoyalCandyExeFsSignatureKind.NotInstalled,
                "Royal Candy reserved ExeFS anchors are vanilla and available.",
                ReservedAnchorCount,
                recognizedAnchorCount);
        }

        return new SwShRoyalCandyExeFsSignature(
            SwShRoyalCandyExeFsSignatureKind.ForeignPatch,
            "Royal Candy reserved ExeFS anchors or payload caves are partially patched or do not match a complete KM Royal Candy signature.",
            ReservedAnchorCount,
            recognizedAnchorCount);
    }

    internal static IReadOnlyList<SwShRoyalCandyInstalledStoryLevelCap> ReadInstalledStoryLevelCaps(
        byte[] mainBytes,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        var signature = AnalyzeInstallation(mainBytes, expectedGame);
        if (signature.Kind is SwShRoyalCandyExeFsSignatureKind.NotInstalled
            or SwShRoyalCandyExeFsSignatureKind.Unlimited)
        {
            return Array.Empty<SwShRoyalCandyInstalledStoryLevelCap>();
        }

        if (signature.Kind != SwShRoyalCandyExeFsSignatureKind.StoryLimits)
        {
            throw new InvalidDataException(signature.Message);
        }

        var nso = NsoFile.Parse(mainBytes);
        var layout = ResolvePatchLayout(nso.BuildId, expectedGame);
        if (!TryValidateStoryLimitsSignature(nso.Text.DecompressedData, layout, out var levelCaps))
        {
            throw new InvalidDataException("Royal Candy story-cap readback did not find one complete, unique milestone ladder.");
        }

        return levelCaps;
    }

    public static byte[] ApplyBasePatch(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        var nso = NsoFile.Parse(mainBytes);
        ValidateRequiredSegmentHashes(nso);
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

    internal static void VerifyBasePatchOutput(
        byte[] sourceMainBytes,
        byte[] outputMainBytes,
        ProjectGame expectedGame)
    {
        _ = VerifyPatchOutputPreservation(sourceMainBytes, outputMainBytes, expectedGame);

        var installation = AnalyzeInstallation(outputMainBytes, expectedGame);
        if (installation.Kind != SwShRoyalCandyExeFsSignatureKind.Unlimited)
        {
            throw new InvalidDataException("Royal Candy patch verification did not find the complete unlimited executable signature.");
        }
    }

    internal static void VerifyStoryLimitsPatchOutput(
        byte[] sourceMainBytes,
        byte[] outputMainBytes,
        IReadOnlyList<SwShRoyalCandyStoryLevelCap> levelCaps,
        ProjectGame expectedGame)
    {
        ArgumentNullException.ThrowIfNull(levelCaps);

        var (_, output) = VerifyPatchOutputPreservation(sourceMainBytes, outputMainBytes, expectedGame);
        var installation = AnalyzeInstallation(outputMainBytes, expectedGame);
        if (installation.Kind != SwShRoyalCandyExeFsSignatureKind.StoryLimits)
        {
            throw new InvalidDataException("Royal Candy patch verification did not find the complete Story Limits executable signature.");
        }

        var layout = ResolvePatchLayout(output.BuildId, expectedGame);
        if (!TryValidateStoryLimitsSignature(output.Text.DecompressedData, layout, out var installedLevelCaps))
        {
            throw new InvalidDataException("Royal Candy patch verification could not decode one complete Story Limits milestone ladder.");
        }

        var expectedLevelCaps = ValidateAndOrderStoryLevelCaps(levelCaps);
        if (installedLevelCaps.Count != expectedLevelCaps.Count)
        {
            throw new InvalidDataException(
                $"Royal Candy patch verification decoded {installedLevelCaps.Count:N0} story milestones; expected {expectedLevelCaps.Count:N0}.");
        }

        for (var index = 0; index < expectedLevelCaps.Count; index++)
        {
            var expected = expectedLevelCaps[index];
            var installed = installedLevelCaps[index];
            if (installed.LevelCap != expected.LevelCap
                || installed.ProgressHash != expected.ProgressHash
                || installed.ProgressKind != expected.ProgressKind
                || installed.WorkMinimum != expected.WorkMinimum)
            {
                throw new InvalidDataException(
                    $"Royal Candy patch verification found a different story milestone at ladder index {index:N0}.");
            }
        }
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
        ValidateRequiredSegmentHashes(nso);
        var layout = ResolvePatchLayout(nso.BuildId, expectedGame);
        var orderedLevelCaps = ValidateAndOrderStoryLevelCaps(levelCaps);
        var text = nso.Text.DecompressedData.ToArray();

        PatchExpCandyFixedAmountBypass(text);
        PatchRoyalCandyAllowedConsumableRoute(text);
        PatchRoyalCandyVirtualInventory(text, layout);
        PatchInfiniteRoyalCandyUse(text);
        PatchStoryCapLadder(text, orderedLevelCaps, layout);
        PatchRoyalCandyUiRoute(text, skipStoryLimitHooks: true);

        var output = nso.Write(textDecompressedData: text);
        var detectedGame = DetectSupportedGame(nso.BuildId)
            ?? throw new InvalidDataException("Royal Candy cannot patch an unsupported Sword/Shield executable build.");
        VerifyStoryLimitsPatchOutput(mainBytes, output, orderedLevelCaps, detectedGame);
        return output;
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
        ValidateRequiredSegmentHashes(currentNso);
        ValidateRequiredSegmentHashes(baseNso);
        var currentSignature = AnalyzeInstallation(currentMainBytes, expectedGame);
        if (currentSignature.Kind is not (SwShRoyalCandyExeFsSignatureKind.Unlimited
            or SwShRoyalCandyExeFsSignatureKind.StoryLimits))
        {
            throw new InvalidDataException(
                $"Royal Candy ExeFS restore requires one complete owned install; found {currentSignature.Kind}: {currentSignature.Message}");
        }

        var baseSignature = AnalyzeInstallation(baseMainBytes, expectedGame);
        if (baseSignature.Kind != SwShRoyalCandyExeFsSignatureKind.NotInstalled)
        {
            throw new InvalidDataException(
                $"Royal Candy ExeFS restore requires a vanilla Royal Candy base executable; found {baseSignature.Kind}: {baseSignature.Message}");
        }

        var layout = ResolvePatchLayout(currentNso.BuildId, expectedGame);
        _ = ResolvePatchLayout(baseNso.BuildId, expectedGame);
        if (!currentNso.BuildId.SequenceEqual(baseNso.BuildId)
            || currentNso.Text.Header.MemoryOffset != baseNso.Text.Header.MemoryOffset
            || currentNso.Text.Header.DecompressedSize != baseNso.Text.Header.DecompressedSize)
        {
            throw new InvalidDataException("Royal Candy ExeFS restore requires current and base main NSO files from the same supported build and .text layout.");
        }

        var currentText = currentNso.Text.DecompressedData.ToArray();
        var baseText = baseNso.Text.DecompressedData;
        if (currentText.Length != baseText.Length)
        {
            throw new InvalidDataException("Royal Candy ExeFS restore requires current and base main NSO files with matching .text sizes.");
        }

        IReadOnlyList<RoyalCandyOwnedCave> ownedCaves = Array.Empty<RoyalCandyOwnedCave>();
        var exactSignature = currentSignature.Kind switch
        {
            SwShRoyalCandyExeFsSignatureKind.Unlimited =>
                TryValidateUnlimitedSignature(currentText, layout, out ownedCaves),
            SwShRoyalCandyExeFsSignatureKind.StoryLimits =>
                TryValidateStoryLimitsSignature(currentText, layout, out _, out ownedCaves),
            _ => false,
        };
        if (!exactSignature)
        {
            throw new InvalidDataException("Royal Candy ExeFS restore could not recover the exact owned cave graph from the installed signature.");
        }

        RestoreOwnedCodeCaves(currentText, baseText, ownedCaves);
        foreach (var region in SwShExeFsReservedRegionLedger.MainTextRegionsForOwners(
            SwShExeFsReservedRegionLedger.OwnerRoyalCandy,
            SwShExeFsReservedRegionLedger.OwnerRoyalCandyStoryLimits)
            .Where(region => IsApplicableFixedRestoreRegion(region, layout)))
        {
            baseText.AsSpan(region.StartOffset!.Value, region.Length!.Value)
                .CopyTo(currentText.AsSpan(region.StartOffset.Value, region.Length.Value));
        }

        var restored = currentNso.Write(textDecompressedData: currentText);
        var restoredSignature = AnalyzeInstallation(restored, expectedGame);
        if (restoredSignature.Kind != SwShRoyalCandyExeFsSignatureKind.NotInstalled)
        {
            throw new InvalidDataException("Royal Candy ExeFS restore did not return every owned anchor to vanilla semantics.");
        }

        return restored;
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
        return DetectSupportedGame(buildId);
    }

    internal static ProjectGame? DetectSupportedGame(ReadOnlySpan<byte> buildId)
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
        var detectedGame = DetectSupportedGame(buildId)
            ?? throw new InvalidDataException("Royal Candy cannot patch an unsupported Sword/Shield executable build.");
        if (expectedGame is not null && detectedGame != expectedGame.Value)
        {
            throw new InvalidDataException(
                $"Selected {FormatGame(expectedGame.Value)}, but exefs/main belongs to {FormatGame(detectedGame)}. Royal Candy will not patch a different game's executable.");
        }

        return detectedGame == ProjectGame.Shield
            ? ShieldPatchLayout
            : SwordPatchLayout;
    }

    private static string FormatBuildId(ReadOnlySpan<byte> buildId)
    {
        var buildIdLength = Math.Min(20, buildId.Length);
        return Convert.ToHexString(buildId[..buildIdLength]);
    }

    private static (NsoFile Source, NsoFile Output) VerifyPatchOutputPreservation(
        byte[] sourceMainBytes,
        byte[] outputMainBytes,
        ProjectGame expectedGame)
    {
        ArgumentNullException.ThrowIfNull(sourceMainBytes);
        ArgumentNullException.ThrowIfNull(outputMainBytes);

        var source = NsoFile.Parse(sourceMainBytes);
        var output = NsoFile.Parse(outputMainBytes);
        ValidateRequiredSegmentHashes(source);
        ValidateRequiredSegmentHashes(output);
        _ = ResolvePatchLayout(source.BuildId, expectedGame);
        _ = ResolvePatchLayout(output.BuildId, expectedGame);

        if (source.Version != output.Version
            || source.Flags != output.Flags
            || !source.BuildId.SequenceEqual(output.BuildId))
        {
            throw new InvalidDataException("Royal Candy patch verification found changed executable identity metadata.");
        }

        if (!SwShExeFsMainComparison.StableHeaderBytesMatch(source.RawHeader, output.RawHeader))
        {
            throw new InvalidDataException("Royal Candy patch verification found changed executable header metadata.");
        }

        VerifyPreservedSegment(source.Ro, output.Ro, ".ro");
        VerifyPreservedSegment(source.Data, output.Data, ".data");
        if (source.Text.Header.MemoryOffset != output.Text.Header.MemoryOffset
            || source.Text.Header.DecompressedSize != output.Text.Header.DecompressedSize
            || source.Text.DecompressedData.Length != output.Text.DecompressedData.Length)
        {
            throw new InvalidDataException("Royal Candy patch verification found a changed .text layout.");
        }

        if (source.Text.DecompressedData.SequenceEqual(output.Text.DecompressedData))
        {
            throw new InvalidDataException("Royal Candy patch verification found no executable text changes.");
        }

        foreach (var reservation in NonRoyalCandyReservations)
        {
            var start = reservation.StartOffset!.Value;
            var length = reservation.Length!.Value;
            if (start + length > source.Text.DecompressedData.Length)
            {
                continue;
            }

            if (!source.Text.DecompressedData.AsSpan(start, length)
                .SequenceEqual(output.Text.DecompressedData.AsSpan(start, length)))
            {
                throw new InvalidDataException(
                    $"Royal Candy patch verification found a change in the {reservation.Owner} reserved range.");
            }
        }

        return (source, output);
    }

    private static IReadOnlyList<SwShRoyalCandyStoryLevelCap> ValidateAndOrderStoryLevelCaps(
        IReadOnlyList<SwShRoyalCandyStoryLevelCap> levelCaps)
    {
        if (levelCaps.Count is < 1 or > 64)
        {
            throw new InvalidDataException("Royal Candy Story Limits requires between 1 and 64 unique milestones.");
        }

        var milestoneKeys = new HashSet<RoyalCandyStoryMilestoneKey>();
        foreach (var levelCap in levelCaps)
        {
            if (levelCap.LevelCap is < 1 or > 100)
            {
                throw new InvalidDataException("Royal Candy story milestone caps must be between 1 and 100.");
            }

            if (levelCap.ProgressKind is not (SwShRoyalCandyStoryLevelCapProgressKind.Flag
                or SwShRoyalCandyStoryLevelCapProgressKind.WorkAtLeast))
            {
                throw new InvalidDataException("Royal Candy story milestone has an unsupported progress accessor kind.");
            }

            if (levelCap.ProgressKind == SwShRoyalCandyStoryLevelCapProgressKind.Flag
                && levelCap.WorkMinimum != 0)
            {
                throw new InvalidDataException("Royal Candy flag milestones cannot carry a work-value minimum.");
            }

            if (levelCap.ProgressKind == SwShRoyalCandyStoryLevelCapProgressKind.WorkAtLeast
                && levelCap.WorkMinimum is < 0 or > 0xFFF)
            {
                throw new InvalidDataException("Royal Candy work milestones require a minimum between 0 and 4095.");
            }

            var key = new RoyalCandyStoryMilestoneKey(
                levelCap.ProgressHash,
                levelCap.ProgressKind,
                levelCap.WorkMinimum);
            if (!milestoneKeys.Add(key))
            {
                throw new InvalidDataException("Royal Candy Story Limits requires one unique entry per progress milestone.");
            }
        }

        return levelCaps
            .OrderByDescending(levelCap => levelCap.LevelCap)
            .ToArray();
    }

    private static void VerifyPreservedSegment(NsoSegment source, NsoSegment output, string name)
    {
        if (source.Header.MemoryOffset != output.Header.MemoryOffset
            || source.Header.DecompressedSize != output.Header.DecompressedSize
            || source.CompressedSize != output.CompressedSize
            || !source.Hash.SequenceEqual(output.Hash)
            || !source.CompressedData.SequenceEqual(output.CompressedData)
            || !source.DecompressedData.SequenceEqual(output.DecompressedData))
        {
            throw new InvalidDataException($"Royal Candy patch verification found a changed {name} segment.");
        }
    }

    private static void ValidateRequiredSegmentHashes(NsoFile nso)
    {
        ValidateRequiredSegmentHash(nso.Text, nso.Flags.HasFlag(NsoFlags.CheckHashText));
        ValidateRequiredSegmentHash(nso.Ro, nso.Flags.HasFlag(NsoFlags.CheckHashRo));
        ValidateRequiredSegmentHash(nso.Data, nso.Flags.HasFlag(NsoFlags.CheckHashData));
    }

    private static void ValidateRequiredSegmentHash(NsoSegment segment, bool required)
    {
        if (required && !NsoFile.ComputeHash(segment.DecompressedData).SequenceEqual(segment.Hash))
        {
            throw new InvalidDataException(
                $"Royal Candy patching rejected {segment.Name} because its required NSO header hash does not match the decompressed segment.");
        }
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

    private static bool TryValidateUnlimitedSignature(ReadOnlySpan<byte> text, RoyalCandyPatchLayout layout)
    {
        return TryValidateUnlimitedSignature(text, layout, out _);
    }

    private static bool TryValidateUnlimitedSignature(
        ReadOnlySpan<byte> text,
        RoyalCandyPatchLayout layout,
        out IReadOnlyList<RoyalCandyOwnedCave> ownedCaves)
    {
        var caves = new List<RoyalCandyOwnedCave>();
        foreach (var check in UiRouteChecks)
        {
            if (!TryValidateUiRouteCheck(text, check, caves))
            {
                ownedCaves = Array.Empty<RoyalCandyOwnedCave>();
                return false;
            }
        }

        foreach (var check in EqualBranchChecks)
        {
            if (!TryValidateEqualBranchCheck(text, check, caves))
            {
                ownedCaves = Array.Empty<RoyalCandyOwnedCave>();
                return false;
            }
        }

        var isValid = CountInstalledExpCandyBypass(text) == 2
            && TryValidateInfiniteUsePayload(text, caves)
            && TryValidateAllowedConsumablePayload(text, caves)
            && TryValidateVirtualInventoryPayload(
                text,
                layout.ItemOwnershipFunctionOffset,
                ExpectedItemOwnershipFirstInstruction,
                expectedReturnValue: 1,
                caves)
            && TryValidateVirtualInventoryPayload(
                text,
                layout.ItemCountFunctionOffset,
                ExpectedItemCountFirstInstruction,
                RoyalCandyVirtualInventoryCount,
                caves)
            && MatchesInstruction(text, 0x007BAF38, 0x6B36231F)
            && MatchesInstruction(text, StoryInventoryClampSelectOffset, ExpectedQuantityClampSelect);
        ownedCaves = isValid
            ? caves.ToArray()
            : Array.Empty<RoyalCandyOwnedCave>();
        return isValid;
    }

    private static bool TryValidateStoryLimitsSignature(
        ReadOnlySpan<byte> text,
        RoyalCandyPatchLayout layout,
        out IReadOnlyList<SwShRoyalCandyInstalledStoryLevelCap> levelCaps)
    {
        return TryValidateStoryLimitsSignature(text, layout, out levelCaps, out _);
    }

    private static bool TryValidateStoryLimitsSignature(
        ReadOnlySpan<byte> text,
        RoyalCandyPatchLayout layout,
        out IReadOnlyList<SwShRoyalCandyInstalledStoryLevelCap> levelCaps,
        out IReadOnlyList<RoyalCandyOwnedCave> ownedCaves)
    {
        levelCaps = Array.Empty<SwShRoyalCandyInstalledStoryLevelCap>();
        ownedCaves = Array.Empty<RoyalCandyOwnedCave>();
        var caves = new List<RoyalCandyOwnedCave>();
        foreach (var check in UiRouteChecks)
        {
            if (check.CompareOffset is StoryUseGateCompareOffset or StoryQuantityMaxCompareOffset)
            {
                continue;
            }

            if (!TryValidateUiRouteCheck(text, check, caves))
            {
                return false;
            }
        }

        foreach (var check in EqualBranchChecks)
        {
            if (!TryValidateEqualBranchCheck(text, check, caves))
            {
                return false;
            }
        }

        if (CountInstalledExpCandyBypass(text) != 2
            || !TryValidateInfiniteUsePayload(text, caves)
            || !TryValidateAllowedConsumablePayload(text, caves)
            || !TryValidateVirtualInventoryPayload(
                text,
                layout.ItemOwnershipFunctionOffset,
                ExpectedItemOwnershipFirstInstruction,
                expectedReturnValue: 1,
                caves)
            || !TryValidateVirtualInventoryPayload(
                text,
                layout.ItemCountFunctionOffset,
                ExpectedItemCountFirstInstruction,
                RoyalCandyVirtualInventoryCount,
                caves)
            || !TryValidateStoryUseGatePayload(text, caves, out var useGateHelperOffset)
            || !TryValidateStoryQuantityPayload(text, caves, out var quantityHelperOffset)
            || useGateHelperOffset != quantityHelperOffset
            || !TryValidateStoryInventoryClampPayload(text, caves)
            || !TryReadCompleteStoryCapLadder(text, useGateHelperOffset, layout, caves, out levelCaps))
        {
            levelCaps = Array.Empty<SwShRoyalCandyInstalledStoryLevelCap>();
            return false;
        }

        ownedCaves = caves.ToArray();
        return true;
    }

    private static bool TryValidateUiRouteCheck(
        ReadOnlySpan<byte> text,
        RareCandyUiCheck check,
        ICollection<RoyalCandyOwnedCave> caves)
    {
        var branchOffset = check.CompareOffset + 4;
        if (!MatchesInstruction(text, check.CompareOffset, EncodeCmpImmediate(check.ItemRegister, RareCandyItemId))
            || !TryReadInstruction(text, branchOffset, out var branchInstruction)
            || !IsConditionalBranch(branchInstruction, Arm64Condition.NE)
            || !TryDecodeConditionalBranchTarget(branchInstruction, branchOffset, out var caveOffset)
            || !TryClaimOwnedCave(text, caveOffset, 0x0C, caves))
        {
            return false;
        }

        return MatchesInstruction(text, caveOffset, EncodeCmpImmediate(check.ItemRegister, RoyalCandyItemId))
            && MatchesInstruction(
                text,
                caveOffset + 4,
                EncodeConditionalBranch(caveOffset + 4, check.PassOffset, Arm64Condition.EQ))
            && MatchesInstruction(text, caveOffset + 8, EncodeBranch(caveOffset + 8, check.FailOffset));
    }

    private static bool TryValidateEqualBranchCheck(
        ReadOnlySpan<byte> text,
        RareCandyEqualBranchCheck check,
        ICollection<RoyalCandyOwnedCave> caves)
    {
        if (!TryReadInstruction(text, check.CompareOffset, out var firstCaveBranch)
            || !IsUnconditionalBranch(firstCaveBranch)
            || !TryDecodeBranchTarget(firstCaveBranch, check.CompareOffset, out var firstCaveOffset)
            || !MatchesInstruction(text, check.CompareOffset + 4, NopInstruction)
            || !TryClaimOwnedCave(text, firstCaveOffset, 0x0C, caves)
            || !MatchesInstruction(text, firstCaveOffset, EncodeCmpImmediate(check.ItemRegister, RareCandyItemId))
            || !MatchesInstruction(
                text,
                firstCaveOffset + 4,
                EncodeConditionalBranch(firstCaveOffset + 4, check.TargetOffset, Arm64Condition.EQ))
            || !TryReadInstruction(text, firstCaveOffset + 8, out var secondCaveBranch)
            || !IsUnconditionalBranch(secondCaveBranch)
            || !TryDecodeBranchTarget(secondCaveBranch, firstCaveOffset + 8, out var secondCaveOffset)
            || !TryClaimOwnedCave(text, secondCaveOffset, 0x0C, caves))
        {
            return false;
        }

        return MatchesInstruction(text, secondCaveOffset, EncodeCmpImmediate(check.ItemRegister, RoyalCandyItemId))
            && MatchesInstruction(
                text,
                secondCaveOffset + 4,
                EncodeConditionalBranch(secondCaveOffset + 4, check.TargetOffset, Arm64Condition.EQ))
            && MatchesInstruction(text, secondCaveOffset + 8, EncodeBranch(secondCaveOffset + 8, check.FallthroughOffset));
    }

    private static bool TryValidateInfiniteUsePayload(
        ReadOnlySpan<byte> text,
        ICollection<RoyalCandyOwnedCave> caves)
    {
        if (!TryReadInstruction(text, QuantityMoveOffset, out var branch)
            || !IsUnconditionalBranch(branch)
            || !TryDecodeBranchTarget(branch, QuantityMoveOffset, out var caveOffset)
            || !TryClaimOwnedCave(text, caveOffset, 0x0C, caves))
        {
            return false;
        }

        return MatchesInstruction(text, caveOffset, EncodeCmpImmediate(register: 22, RoyalCandyItemId))
            && MatchesInstruction(text, caveOffset + 4, EncodeConditionalSelect32(2, 31, 0, Arm64Condition.EQ))
            && MatchesInstruction(text, caveOffset + 8, EncodeBranch(caveOffset + 8, QuantityMoveOffset + 4));
    }

    private static bool TryValidateAllowedConsumablePayload(
        ReadOnlySpan<byte> text,
        ICollection<RoyalCandyOwnedCave> caves)
    {
        if (!MatchesInstruction(text, AllowedConsumableCompareOffset, EncodeCmpImmediate(register: 8, RareCandyItemId))
            || !TryReadInstruction(text, AllowedConsumableBranchOffset, out var branch)
            || !IsConditionalBranch(branch, Arm64Condition.HI)
            || !TryDecodeConditionalBranchTarget(branch, AllowedConsumableBranchOffset, out var caveOffset)
            || !TryClaimOwnedCave(text, caveOffset, 0x0C, caves))
        {
            return false;
        }

        return MatchesInstruction(text, caveOffset, EncodeCmpImmediate(register: 8, RoyalCandyAllowedConsumableAdjustedItemId))
            && MatchesInstruction(
                text,
                caveOffset + 4,
                EncodeConditionalBranch(caveOffset + 4, AllowedConsumablePassOffset, Arm64Condition.EQ))
            && MatchesInstruction(text, caveOffset + 8, EncodeBranch(caveOffset + 8, AllowedConsumableFailOffset));
    }

    private static bool TryValidateVirtualInventoryPayload(
        ReadOnlySpan<byte> text,
        int hookOffset,
        uint expectedFirstInstruction,
        int expectedReturnValue,
        ICollection<RoyalCandyOwnedCave> caves)
    {
        if (!TryReadInstruction(text, hookOffset, out var hookBranch)
            || !IsUnconditionalBranch(hookBranch)
            || !TryDecodeBranchTarget(hookBranch, hookOffset, out var dispatchOffset)
            || !TryClaimOwnedCave(text, dispatchOffset, 0x0C, caves)
            || !TryReadInstruction(text, dispatchOffset + 4, out var returnBranch)
            || !IsConditionalBranch(returnBranch, Arm64Condition.EQ)
            || !TryDecodeConditionalBranchTarget(returnBranch, dispatchOffset + 4, out var returnOffset)
            || !TryClaimOwnedCave(text, returnOffset, 0x0C, caves)
            || !TryReadInstruction(text, dispatchOffset + 8, out var vanillaBranch)
            || !IsUnconditionalBranch(vanillaBranch)
            || !TryDecodeBranchTarget(vanillaBranch, dispatchOffset + 8, out var vanillaOffset)
            || !TryClaimOwnedCave(text, vanillaOffset, 0x0C, caves))
        {
            return false;
        }

        return MatchesInstruction(text, dispatchOffset, EncodeCmpImmediate(register: 1, RoyalCandyItemId))
            && MatchesInstruction(
                text,
                dispatchOffset + 4,
                EncodeConditionalBranch(dispatchOffset + 4, returnOffset, Arm64Condition.EQ))
            && MatchesInstruction(text, dispatchOffset + 8, EncodeBranch(dispatchOffset + 8, vanillaOffset))
            && MatchesInstruction(text, returnOffset, EncodeMovzImmediate32(register: 0, expectedReturnValue))
            && MatchesInstruction(text, returnOffset + 4, EncodeRet())
            && MatchesInstruction(text, returnOffset + 8, NopInstruction)
            && MatchesInstruction(text, vanillaOffset, expectedFirstInstruction)
            && MatchesInstruction(text, vanillaOffset + 4, EncodeBranch(vanillaOffset + 4, hookOffset + 4))
            && MatchesInstruction(text, vanillaOffset + 8, NopInstruction);
    }

    private static bool TryValidateStoryUseGatePayload(
        ReadOnlySpan<byte> text,
        ICollection<RoyalCandyOwnedCave> caves,
        out int helperOffset)
    {
        const int branchOffset = StoryUseGateCompareOffset + 4;
        const int nonRoyalCandyOffset = 0x007BB26C;
        const int epilogueOffset = 0x007BB2E0;
        const int getLevelOffset = 0x0077A5F0;
        const int itemRegister = 20;
        const uint moveSelectedPokemonToX0 = 0xAA1303E0;

        helperOffset = 0;
        if (!MatchesInstruction(text, StoryUseGateCompareOffset, EncodeCmpImmediate(itemRegister, RareCandyItemId))
            || !TryReadConditionalBranchTarget(text, branchOffset, Arm64Condition.NE, out var itemCheckOffset)
            || !TryClaimOwnedCave(text, itemCheckOffset, 0x0C, caves)
            || !MatchesInstruction(text, itemCheckOffset, EncodeCmpImmediate(itemRegister, RoyalCandyItemId))
            || !MatchesInstruction(
                text,
                itemCheckOffset + 4,
                EncodeConditionalBranch(itemCheckOffset + 4, nonRoyalCandyOffset, Arm64Condition.NE))
            || !TryReadUnconditionalBranchTarget(text, itemCheckOffset + 8, out var firstLogicOffset)
            || !TryClaimOwnedCave(text, firstLogicOffset, 0x0C, caves)
            || !MatchesInstruction(text, firstLogicOffset, moveSelectedPokemonToX0)
            || !MatchesInstruction(text, firstLogicOffset + 4, EncodeBranchLink(firstLogicOffset + 4, getLevelOffset))
            || !TryReadUnconditionalBranchTarget(text, firstLogicOffset + 8, out var secondLogicOffset)
            || !TryClaimOwnedCave(text, secondLogicOffset, 0x0C, caves)
            || !MatchesInstruction(text, secondLogicOffset, EncodeMovRegister32(21, 0))
            || !TryReadInstruction(text, secondLogicOffset + 4, out var helperCall)
            || !IsBranchLink(helperCall)
            || !TryDecodeBranchTarget(helperCall, secondLogicOffset + 4, out helperOffset)
            || !TryReadUnconditionalBranchTarget(text, secondLogicOffset + 8, out var thirdLogicOffset)
            || !TryClaimOwnedCave(text, thirdLogicOffset, 0x0C, caves)
            || !MatchesInstruction(text, thirdLogicOffset, EncodeCmpRegister32(21, 0))
            || !MatchesInstruction(text, thirdLogicOffset + 4, EncodeMovzImmediate32(8, 1))
            || !TryReadUnconditionalBranchTarget(text, thirdLogicOffset + 8, out var fourthLogicOffset)
            || !TryClaimOwnedCave(text, fourthLogicOffset, 0x0C, caves))
        {
            helperOffset = 0;
            return false;
        }

        return MatchesInstruction(text, fourthLogicOffset, EncodeConditionalSelect32(0, 8, 31, Arm64Condition.LT))
            && MatchesInstruction(text, fourthLogicOffset + 4, EncodeBranch(fourthLogicOffset + 4, epilogueOffset))
            && MatchesInstruction(text, fourthLogicOffset + 8, NopInstruction);
    }

    private static bool TryValidateStoryQuantityPayload(
        ReadOnlySpan<byte> text,
        ICollection<RoyalCandyOwnedCave> caves,
        out int helperOffset)
    {
        const int branchOffset = StoryQuantityMaxCompareOffset + 4;
        const int nonRoyalCandyOffset = 0x007BB3EC;
        const int epilogueOffset = 0x007BB458;
        const int getLevelOffset = 0x0077A5F0;
        const int itemRegister = 19;
        const uint moveSelectedPokemonToX0 = 0xAA1403E0;

        helperOffset = 0;
        if (!MatchesInstruction(text, StoryQuantityMaxCompareOffset, EncodeCmpImmediate(itemRegister, RareCandyItemId))
            || !TryReadConditionalBranchTarget(text, branchOffset, Arm64Condition.NE, out var itemCheckOffset)
            || !TryClaimOwnedCave(text, itemCheckOffset, 0x0C, caves)
            || !MatchesInstruction(text, itemCheckOffset, EncodeCmpImmediate(itemRegister, RoyalCandyItemId))
            || !MatchesInstruction(
                text,
                itemCheckOffset + 4,
                EncodeConditionalBranch(itemCheckOffset + 4, nonRoyalCandyOffset, Arm64Condition.NE))
            || !TryReadUnconditionalBranchTarget(text, itemCheckOffset + 8, out var firstLogicOffset)
            || !TryClaimOwnedCave(text, firstLogicOffset, 0x0C, caves)
            || !MatchesInstruction(text, firstLogicOffset, moveSelectedPokemonToX0)
            || !MatchesInstruction(text, firstLogicOffset + 4, EncodeBranchLink(firstLogicOffset + 4, getLevelOffset))
            || !TryReadUnconditionalBranchTarget(text, firstLogicOffset + 8, out var secondLogicOffset)
            || !TryClaimOwnedCave(text, secondLogicOffset, 0x0C, caves)
            || !MatchesInstruction(text, secondLogicOffset, EncodeMovRegister32(21, 0))
            || !TryReadInstruction(text, secondLogicOffset + 4, out var helperCall)
            || !IsBranchLink(helperCall)
            || !TryDecodeBranchTarget(helperCall, secondLogicOffset + 4, out helperOffset)
            || !TryReadUnconditionalBranchTarget(text, secondLogicOffset + 8, out var thirdLogicOffset)
            || !TryClaimOwnedCave(text, thirdLogicOffset, 0x0C, caves)
            || !MatchesInstruction(text, thirdLogicOffset, EncodeSubRegister32(0, 0, 21))
            || !MatchesInstruction(text, thirdLogicOffset + 4, EncodeCmpImmediate(0, 0))
            || !TryReadUnconditionalBranchTarget(text, thirdLogicOffset + 8, out var fourthLogicOffset)
            || !TryClaimOwnedCave(text, fourthLogicOffset, 0x0C, caves))
        {
            helperOffset = 0;
            return false;
        }

        return MatchesInstruction(text, fourthLogicOffset, EncodeConditionalSelect32(0, 0, 31, Arm64Condition.GT))
            && MatchesInstruction(text, fourthLogicOffset + 4, EncodeBranch(fourthLogicOffset + 4, epilogueOffset))
            && MatchesInstruction(text, fourthLogicOffset + 8, NopInstruction);
    }

    private static bool TryValidateStoryInventoryClampPayload(
        ReadOnlySpan<byte> text,
        ICollection<RoyalCandyOwnedCave> caves)
    {
        const int originalCompareOffset = 0x007BAF38;
        const int resumeOffset = 0x007BAF40;
        const int getItemIdOffset = 0x007C8330;
        const uint expectedCompare = 0x6B36231F;
        const uint moveSelectedItemToX0 = 0xAA1703E0;

        if (!MatchesInstruction(text, originalCompareOffset, expectedCompare)
            || !TryReadUnconditionalBranchTarget(text, StoryInventoryClampSelectOffset, out var firstCaveOffset)
            || !TryClaimOwnedCave(text, firstCaveOffset, 0x0C, caves)
            || !MatchesInstruction(text, firstCaveOffset, moveSelectedItemToX0)
            || !MatchesInstruction(text, firstCaveOffset + 4, EncodeBranchLink(firstCaveOffset + 4, getItemIdOffset))
            || !TryReadUnconditionalBranchTarget(text, firstCaveOffset + 8, out var secondCaveOffset)
            || !TryClaimOwnedCave(text, secondCaveOffset, 0x0C, caves)
            || !MatchesInstruction(text, secondCaveOffset, EncodeCmpImmediate(0, RoyalCandyItemId))
            || !MatchesInstruction(
                text,
                secondCaveOffset + 4,
                EncodeConditionalBranch(secondCaveOffset + 4, resumeOffset, Arm64Condition.EQ))
            || !TryReadUnconditionalBranchTarget(text, secondCaveOffset + 8, out var thirdCaveOffset)
            || !TryClaimOwnedCave(text, thirdCaveOffset, 0x0C, caves))
        {
            return false;
        }

        return MatchesInstruction(text, thirdCaveOffset, expectedCompare)
            && MatchesInstruction(text, thirdCaveOffset + 4, ExpectedQuantityClampSelect)
            && MatchesInstruction(text, thirdCaveOffset + 8, EncodeBranch(thirdCaveOffset + 8, resumeOffset));
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

    private static bool AreRoyalCandyAnchorsVanilla(ReadOnlySpan<byte> text, RoyalCandyPatchLayout layout)
    {
        foreach (var check in UiRouteChecks)
        {
            if (!MatchesInstruction(text, check.CompareOffset, EncodeCmpImmediate(check.ItemRegister, RareCandyItemId))
                || !MatchesInstruction(
                    text,
                    check.CompareOffset + 4,
                    EncodeConditionalBranch(check.CompareOffset + 4, check.FailOffset, Arm64Condition.NE)))
            {
                return false;
            }
        }

        foreach (var check in EqualBranchChecks)
        {
            if (!MatchesInstruction(text, check.CompareOffset, EncodeCmpImmediate(check.ItemRegister, RareCandyItemId))
                || !MatchesInstruction(
                    text,
                    check.CompareOffset + 4,
                    EncodeConditionalBranch(check.CompareOffset + 4, check.TargetOffset, Arm64Condition.EQ)))
            {
                return false;
            }
        }

        return MatchesInstruction(text, 0x007BC1BC, EncodeCmpImmediate(register: 9, immediate: 4))
            && MatchesInstruction(text, 0x007BC1C4, EncodeCmpImmediate(register: 9, immediate: 4))
            && MatchesInstruction(text, QuantityMoveOffset, ExpectedQuantityMove)
            && MatchesInstruction(text, 0x007BAF38, 0x6B36231F)
            && MatchesInstruction(text, StoryInventoryClampSelectOffset, ExpectedQuantityClampSelect)
            && MatchesInstruction(text, AllowedConsumableCompareOffset, EncodeCmpImmediate(register: 8, RareCandyItemId))
            && MatchesInstruction(
                text,
                AllowedConsumableBranchOffset,
                EncodeConditionalBranch(AllowedConsumableBranchOffset, AllowedConsumableFailOffset, Arm64Condition.HI))
            && MatchesInstruction(text, layout.ItemOwnershipFunctionOffset, ExpectedItemOwnershipFirstInstruction)
            && MatchesInstruction(text, layout.ItemCountFunctionOffset, ExpectedItemCountFirstInstruction);
    }

    private static int CountRecognizedExactAnchors(ReadOnlySpan<byte> text, RoyalCandyPatchLayout layout)
    {
        var count = CountInstalledExpCandyBypass(text);
        foreach (var check in UiRouteChecks)
        {
            count += TryValidateUiRouteCheck(text, check, new List<RoyalCandyOwnedCave>()) ? 1 : 0;
        }

        foreach (var check in EqualBranchChecks)
        {
            count += TryValidateEqualBranchCheck(text, check, new List<RoyalCandyOwnedCave>()) ? 1 : 0;
        }

        count += TryValidateInfiniteUsePayload(text, new List<RoyalCandyOwnedCave>()) ? 1 : 0;
        count += TryValidateAllowedConsumablePayload(text, new List<RoyalCandyOwnedCave>()) ? 1 : 0;
        count += TryValidateVirtualInventoryPayload(
            text,
            layout.ItemOwnershipFunctionOffset,
            ExpectedItemOwnershipFirstInstruction,
            expectedReturnValue: 1,
            new List<RoyalCandyOwnedCave>()) ? 1 : 0;
        count += TryValidateVirtualInventoryPayload(
            text,
            layout.ItemCountFunctionOffset,
            ExpectedItemCountFirstInstruction,
            RoyalCandyVirtualInventoryCount,
            new List<RoyalCandyOwnedCave>()) ? 1 : 0;
        count += TryValidateStoryInventoryClampPayload(text, new List<RoyalCandyOwnedCave>()) ? 1 : 0;
        return Math.Min(count, ReservedAnchorCount);
    }

    private static bool MatchesInstruction(ReadOnlySpan<byte> text, int offset, uint expectedInstruction)
    {
        return TryReadInstruction(text, offset, out var instruction)
            && instruction == expectedInstruction;
    }

    private static bool TryReadUnconditionalBranchTarget(ReadOnlySpan<byte> text, int offset, out int targetOffset)
    {
        targetOffset = 0;
        return TryReadInstruction(text, offset, out var instruction)
            && IsUnconditionalBranch(instruction)
            && TryDecodeBranchTarget(instruction, offset, out targetOffset);
    }

    private static bool TryReadConditionalBranchTarget(
        ReadOnlySpan<byte> text,
        int offset,
        Arm64Condition condition,
        out int targetOffset)
    {
        targetOffset = 0;
        return TryReadInstruction(text, offset, out var instruction)
            && IsConditionalBranch(instruction, condition)
            && TryDecodeConditionalBranchTarget(instruction, offset, out targetOffset);
    }

    private static bool TryClaimOwnedCave(
        ReadOnlySpan<byte> text,
        int offset,
        int length,
        ICollection<RoyalCandyOwnedCave> caves)
    {
        if (offset < 0
            || offset % 4 != 0
            || length <= 0
            || offset + length > text.Length
            || NonRoyalCandyReservations.Any(region => SwShExeFsReservedRegionLedger.Overlaps(region, offset, length))
            || caves.Any(cave => offset < cave.Offset + cave.Length && offset + length > cave.Offset))
        {
            return false;
        }

        caves.Add(new RoyalCandyOwnedCave(offset, length));
        return true;
    }

    private static bool TryReadCompleteStoryCapLadder(
        ReadOnlySpan<byte> text,
        int helperOffset,
        RoyalCandyPatchLayout layout,
        ICollection<RoyalCandyOwnedCave> caves,
        out IReadOnlyList<SwShRoyalCandyInstalledStoryLevelCap> levelCaps)
    {
        var records = new List<SwShRoyalCandyInstalledStoryLevelCap>();
        var visited = new HashSet<int>();
        var milestones = new HashSet<RoyalCandyStoryMilestoneKey>();
        var offset = helperOffset;
        var previousLevelCap = int.MaxValue;
        while (records.Count <= 64)
        {
            if (IsExactStoryCapDefaultReturn(text, offset))
            {
                if (records.Count == 0 || !TryClaimOwnedCave(text, offset, 0x08, caves))
                {
                    levelCaps = Array.Empty<SwShRoyalCandyInstalledStoryLevelCap>();
                    return false;
                }

                levelCaps = records;
                return true;
            }

            if (records.Count == 64
                || !visited.Add(offset)
                || !TryReadStoryCapCheck(text, offset, layout, caves, out var record, out var nextOffset)
                || record.LevelCap > previousLevelCap
                || !milestones.Add(new RoyalCandyStoryMilestoneKey(
                    record.ProgressHash,
                    record.ProgressKind,
                    record.WorkMinimum)))
            {
                levelCaps = Array.Empty<SwShRoyalCandyInstalledStoryLevelCap>();
                return false;
            }

            records.Add(record);
            previousLevelCap = record.LevelCap;
            offset = nextOffset;
        }

        levelCaps = Array.Empty<SwShRoyalCandyInstalledStoryLevelCap>();
        return false;
    }

    private static bool TryReadStoryCapCheck(
        ReadOnlySpan<byte> text,
        int loadGlobalOffset,
        RoyalCandyPatchLayout layout,
        ICollection<RoyalCandyOwnedCave> caves,
        out SwShRoyalCandyInstalledStoryLevelCap record,
        out int nextOffset)
    {
        record = null!;
        nextOffset = 0;

        if (!TryClaimOwnedCave(text, loadGlobalOffset, 0x0C, caves)
            || !MatchesInstruction(
                text,
                loadGlobalOffset,
                EncodeAdrp(register: 8, loadGlobalOffset, layout.FlagworkGlobalAddress))
            || !MatchesInstruction(
                text,
                loadGlobalOffset + 4,
                EncodeLdrUnsigned64(targetRegister: 8, baseRegister: 8, layout.FlagworkGlobalAddress & 0xFFF))
            || !TryReadUnconditionalBranchTarget(text, loadGlobalOffset + 8, out var loadTableOffset)
            || !TryClaimOwnedCave(text, loadTableOffset, 0x0C, caves)
            || !MatchesInstruction(text, loadTableOffset, EncodeLdrUnsigned64(targetRegister: 8, baseRegister: 8, byteOffset: 0))
            || !MatchesInstruction(
                text,
                loadTableOffset + 4,
                EncodeLdrUnsigned64(targetRegister: 0, baseRegister: 8, layout.FlagworkObjectOffset))
            || !TryReadUnconditionalBranchTarget(text, loadTableOffset + 8, out var hashLowOffset)
            || !TryClaimOwnedCave(text, hashLowOffset, 0x0C, caves)
            || !TryReadInstruction(text, hashLowOffset, out var hashLowMovz)
            || !TryReadInstruction(text, hashLowOffset + 4, out var hashMidLowMovk)
            || !TryDecodeMovzImmediate64(hashLowMovz, register: 1, shift: 0, out var hashPart0)
            || !TryDecodeMovkImmediate64(hashMidLowMovk, register: 1, shift: 16, out var hashPart1)
            || !TryReadUnconditionalBranchTarget(text, hashLowOffset + 8, out var hashHighOffset)
            || !TryClaimOwnedCave(text, hashHighOffset, 0x0C, caves)
            || !TryReadInstruction(text, hashHighOffset, out var hashMidHighMovk)
            || !TryReadInstruction(text, hashHighOffset + 4, out var hashHighMovk)
            || !TryDecodeMovkImmediate64(hashMidHighMovk, register: 1, shift: 32, out var hashPart2)
            || !TryDecodeMovkImmediate64(hashHighMovk, register: 1, shift: 48, out var hashPart3)
            || !TryReadUnconditionalBranchTarget(text, hashHighOffset + 8, out var callOffset)
            || !TryClaimOwnedCave(text, callOffset, 0x0C, caves)
            || !MatchesInstruction(text, callOffset, 0xA9BF7BFD)
            || !TryReadInstruction(text, callOffset + 4, out var accessorCall)
            || !IsBranchLink(accessorCall)
            || !TryDecodeBranchTarget(accessorCall, callOffset + 4, out var accessorOffset)
            || !TryReadUnconditionalBranchTarget(text, callOffset + 8, out var restoreOffset)
            || !TryClaimOwnedCave(text, restoreOffset, 0x0C, caves)
            || !MatchesInstruction(text, restoreOffset, 0xA8C17BFD)
            || !TryReadUnconditionalBranchTarget(text, restoreOffset + 4, out var decisionOffset)
            || !MatchesInstruction(text, restoreOffset + 8, NopInstruction)
            || !TryClaimOwnedCave(text, decisionOffset, 0x0C, caves))
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
                || !IsUnconditionalBranch(nextBranch)
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
                || !IsUnconditionalBranch(nextBranch)
                || !TryDecodeBranchTarget(nextBranch, decisionOffset + 4, out nextOffset)
                || !MatchesInstruction(text, decisionOffset + 8, NopInstruction))
            {
                return false;
            }
        }

        if (!TryClaimOwnedCave(text, returnCapOffset, 0x08, caves)
            || !TryReadInstruction(text, returnCapOffset, out var returnCapInstruction)
            || !TryDecodeMovzImmediate32(returnCapInstruction, register: 0, out var levelCap)
            || levelCap is < 1 or > 100
            || !MatchesInstruction(text, returnCapOffset + 4, EncodeRet())
            || (progressKind.Value == SwShRoyalCandyStoryLevelCapProgressKind.WorkAtLeast
                && (workMinimum is < 0 or > 0xFFF
                    || !MatchesInstruction(text, decisionOffset, EncodeCmpImmediate(0, workMinimum)))))
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

    private static bool IsExactStoryCapDefaultReturn(ReadOnlySpan<byte> text, int offset)
    {
        return TryReadInstruction(text, offset, out var capInstruction)
            && TryDecodeMovzImmediate32(capInstruction, register: 0, out var levelCap)
            && levelCap == StoryDefaultLevelCap
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

    private static void RestoreOwnedCodeCaves(
        byte[] currentText,
        ReadOnlySpan<byte> baseText,
        IReadOnlyList<RoyalCandyOwnedCave> ownedCaves)
    {
        foreach (var cave in ownedCaves)
        {
            if (!IsBaseZeroUnreservedCave(baseText, cave.Offset, cave.Length))
            {
                throw new InvalidDataException(
                    $"Royal Candy ExeFS restore could not prove ownership of text+0x{cave.Offset:X}..0x{cave.Offset + cave.Length - 1:X} from the base executable.");
            }

            baseText.Slice(cave.Offset, cave.Length)
                .CopyTo(currentText.AsSpan(cave.Offset, cave.Length));
        }
    }

    private static bool IsApplicableFixedRestoreRegion(
        SwShExeFsReservedRegion region,
        RoyalCandyPatchLayout layout)
    {
        var offset = region.StartOffset!.Value;
        if (offset is ItemOwnershipFunctionOffset
            or ItemCountFunctionOffset
            or ShieldItemOwnershipFunctionOffset
            or ShieldItemCountFunctionOffset)
        {
            return offset == layout.ItemOwnershipFunctionOffset
                || offset == layout.ItemCountFunctionOffset;
        }

        return true;
    }

    private static bool IsBaseZeroUnreservedCave(ReadOnlySpan<byte> baseText, int offset, int length)
    {
        return offset >= 0
            && offset % 4 == 0
            && offset + length <= baseText.Length
            && baseText.Slice(offset, length).IndexOfAnyExcept((byte)0) < 0
            && !NonRoyalCandyReservations.Any(region => SwShExeFsReservedRegionLedger.Overlaps(region, offset, length));
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

    private readonly record struct RoyalCandyOwnedCave(int Offset, int Length);

    private readonly record struct RoyalCandyStoryMilestoneKey(
        ulong ProgressHash,
        SwShRoyalCandyStoryLevelCapProgressKind ProgressKind,
        int WorkMinimum);

    private sealed record ZeroRun(int Offset, int Length);

    private sealed record RoyalCandyPatchLayout(
        int FlagworkGlobalAddress,
        int FlagworkObjectOffset,
        int FlagGetOffset,
        int WorkGetOffset,
        int ItemOwnershipFunctionOffset,
        int ItemCountFunctionOffset);
}
