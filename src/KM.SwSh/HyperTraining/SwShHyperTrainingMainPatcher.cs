// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace KM.SwSh.HyperTraining;

internal enum SwShHyperTrainingMainKind
{
    NotInstalled,
    CustomMinimumLevel,
    UnsupportedBuild,
    GameMismatch,
    Conflict,
}

internal sealed record SwShHyperTrainingMainAnalysis(
    SwShHyperTrainingMainKind Kind,
    string Message,
    int MinimumLevel,
    string BuildId,
    string PatchOffsetHex,
    ProjectGame? DetectedGame);

internal static class SwShHyperTrainingMainPatcher
{
    public const int SwordPreflightCompareOffset = 0x00F98F18;
    public const int SwordEligibilityCompareOffset = 0x00F9A314;
    public const int SwordEligibilityBranchOffset = 0x00F9A318;
    public const int SwordGrayOutCompareOffset = 0x00F9A334;
    public const int SwordGrayOutBranchOffset = 0x00F9A338;
    public const int SwordDetailCompareOffset = 0x00F9E4C0;
    public const int SwordDetailBranchOffset = 0x00F9E4C4;

    public const int ShieldPreflightCompareOffset = 0x00F98F48;
    public const int ShieldEligibilityCompareOffset = 0x00F9A344;
    public const int ShieldEligibilityBranchOffset = 0x00F9A348;
    public const int ShieldGrayOutCompareOffset = 0x00F9A364;
    public const int ShieldGrayOutBranchOffset = 0x00F9A368;
    public const int ShieldDetailCompareOffset = 0x00F9E4F0;
    public const int ShieldDetailBranchOffset = 0x00F9E4F4;

    public const int LevelGetterOffset = 0x0077A5F0;
    public const int LevelGetterLength = 0x78;

    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";
    private const string LevelGetterSha256 = "144A4F91C5EE67F4A8CA088205646863C6FDBE6DF07410168A17FBF8087A0410";

    private const uint VanillaEligibilityBranchNotEqual = 0x54000061;
    private const uint PatchedEligibilityBranchLowerThan = 0x54000063;
    private const uint VanillaGrayOutBranchNotEqual = 0x540000A1;
    private const uint PatchedGrayOutBranchLowerThan = 0x540000A3;
    private const uint VanillaDetailBranchNotEqual = 0x540002C1;
    private const uint PatchedDetailBranchLowerThan = 0x540002C3;

    private static readonly PatchLayout[] Layouts =
    [
        new(
            ProjectGame.Sword,
            "Pokemon Sword 1.3.2",
            SwordBuildId,
            SwordPreflightCompareOffset,
            SwordEligibilityCompareOffset,
            SwordEligibilityBranchOffset,
            SwordGrayOutCompareOffset,
            SwordGrayOutBranchOffset,
            SwordDetailCompareOffset,
            SwordDetailBranchOffset,
            0x97DF85B7,
            0x97DF80B8,
            0x97DF80B0,
            0x97DF704D),
        new(
            ProjectGame.Shield,
            "Pokemon Shield 1.3.2",
            ShieldBuildId,
            ShieldPreflightCompareOffset,
            ShieldEligibilityCompareOffset,
            ShieldEligibilityBranchOffset,
            ShieldGrayOutCompareOffset,
            ShieldGrayOutBranchOffset,
            ShieldDetailCompareOffset,
            ShieldDetailBranchOffset,
            0x97DF85AB,
            0x97DF80AC,
            0x97DF80A4,
            0x97DF7041),
    ];

    public static SwShHyperTrainingMainAnalysis Analyze(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        var buildId = "unknown";
        var patchOffset = "unknown";
        ProjectGame? detectedGame = null;
        try
        {
            var nso = NsoFile.Parse(mainBytes);
            ValidateRequiredSegmentHashes(nso);
            buildId = FormatBuildId(nso.BuildId);
            var layout = FindLayout(nso.BuildId);
            if (layout is null)
            {
                return new SwShHyperTrainingMainAnalysis(
                    SwShHyperTrainingMainKind.UnsupportedBuild,
                    "Hyper Training picker supports Sword and Shield 1.3.2 exefs/main files. This build ID is not recognized.",
                    SwShHyperTrainingAmxPatcher.VanillaMinimumLevel,
                    buildId,
                    "unknown",
                    DetectedGame: null);
            }

            detectedGame = layout.Game;
            patchOffset = FormatTextOffset(layout.EligibilityCompareOffset);

            var mismatch = CreateGameMismatchAnalysis(layout, expectedGame, buildId);
            if (mismatch is not null)
            {
                return mismatch;
            }

            var text = nso.Text.DecompressedData;
            EnsurePatchRange(text, layout);
            ValidateDependencies(text, layout);
            var minimumLevel = ReadSharedMinimumLevel(text, layout);
            ValidateBranches(text, layout);

            var kind = minimumLevel == SwShHyperTrainingAmxPatcher.VanillaMinimumLevel
                ? SwShHyperTrainingMainKind.NotInstalled
                : SwShHyperTrainingMainKind.CustomMinimumLevel;
            var message = kind == SwShHyperTrainingMainKind.NotInstalled
                ? "Hyper Training picker is using the vanilla Lv.100 minimum."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hyper Training picker currently enables Pokemon at Lv.{minimumLevel} or higher.");

            return new SwShHyperTrainingMainAnalysis(
                kind,
                message,
                minimumLevel,
                buildId,
                patchOffset,
                layout.Game);
        }
        catch (InvalidDataException exception)
        {
            return new SwShHyperTrainingMainAnalysis(
                SwShHyperTrainingMainKind.Conflict,
                exception.Message,
                SwShHyperTrainingAmxPatcher.VanillaMinimumLevel,
                buildId,
                patchOffset,
                detectedGame);
        }
    }

    public static byte[] ApplyMinimumLevel(byte[] mainBytes, int minimumLevel, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);
        ValidateLevel(minimumLevel);
        EnsureSupportedExpectedGame(expectedGame);

        var analysis = Analyze(mainBytes, expectedGame);
        if (analysis.Kind is SwShHyperTrainingMainKind.UnsupportedBuild
            or SwShHyperTrainingMainKind.GameMismatch
            or SwShHyperTrainingMainKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }

        var nso = NsoFile.Parse(mainBytes);
        ValidateRequiredSegmentHashes(nso);
        var layout = FindLayout(nso.BuildId)
            ?? throw new InvalidDataException("Hyper Training picker supports Sword and Shield 1.3.2 exefs/main files.");
        var text = nso.Text.DecompressedData.ToArray();
        EnsurePatchRange(text, layout);
        ValidateDependencies(text, layout);

        WriteInstruction(text, layout.PreflightCompareOffset, EncodeCompareW0Immediate(minimumLevel));
        WriteInstruction(text, layout.EligibilityCompareOffset, EncodeCompareW0Immediate(minimumLevel));
        WriteInstruction(text, layout.GrayOutCompareOffset, EncodeCompareW0Immediate(minimumLevel));
        WriteInstruction(text, layout.DetailCompareOffset, EncodeCompareW0Immediate(minimumLevel));

        if (minimumLevel == SwShHyperTrainingAmxPatcher.VanillaMinimumLevel)
        {
            WriteInstruction(text, layout.EligibilityBranchOffset, VanillaEligibilityBranchNotEqual);
            WriteInstruction(text, layout.GrayOutBranchOffset, VanillaGrayOutBranchNotEqual);
            WriteInstruction(text, layout.DetailBranchOffset, VanillaDetailBranchNotEqual);
        }
        else
        {
            WriteInstruction(text, layout.EligibilityBranchOffset, PatchedEligibilityBranchLowerThan);
            WriteInstruction(text, layout.GrayOutBranchOffset, PatchedGrayOutBranchLowerThan);
            WriteInstruction(text, layout.DetailBranchOffset, PatchedDetailBranchLowerThan);
        }

        var output = nso.Write(textDecompressedData: text);
        VerifyOutputPreservation(mainBytes, output, layout, "Hyper Training apply");
        var roundTrip = Analyze(output, expectedGame);
        if (roundTrip.Kind != (minimumLevel == SwShHyperTrainingAmxPatcher.VanillaMinimumLevel
                ? SwShHyperTrainingMainKind.NotInstalled
                : SwShHyperTrainingMainKind.CustomMinimumLevel)
            || roundTrip.MinimumLevel != minimumLevel)
        {
            throw new InvalidDataException("Hyper Training picker output did not round-trip with the requested minimum level.");
        }

        return output;
    }

    public static bool HasInstalledHook(byte[] mainBytes)
    {
        return Analyze(mainBytes).Kind == SwShHyperTrainingMainKind.CustomMinimumLevel;
    }

    public static IReadOnlyList<SwShExeFsReservedRegion> ReservedMainTextRegions(ProjectGame? game = null)
    {
        return SwShExeFsReservedRegionLedger
            .MainTextRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerHyperTraining, game)
            .Where(region => !string.Equals(region.Rule, "requires-vanilla", StringComparison.Ordinal))
            .ToArray();
    }

    public static byte[] RestoreFromBase(
        byte[] currentMainBytes,
        byte[] baseMainBytes,
        ProjectGame? expectedGame)
    {
        ArgumentNullException.ThrowIfNull(currentMainBytes);
        ArgumentNullException.ThrowIfNull(baseMainBytes);
        EnsureSupportedExpectedGame(expectedGame);

        var currentAnalysis = Analyze(currentMainBytes, expectedGame);
        if (currentAnalysis.Kind is SwShHyperTrainingMainKind.UnsupportedBuild
            or SwShHyperTrainingMainKind.GameMismatch
            or SwShHyperTrainingMainKind.Conflict)
        {
            throw new InvalidDataException(currentAnalysis.Message);
        }

        var baseAnalysis = Analyze(baseMainBytes, expectedGame);
        if (baseAnalysis.Kind != SwShHyperTrainingMainKind.NotInstalled)
        {
            throw new InvalidDataException(
                "Hyper Training restore requires a verified selected-game vanilla base exefs/main.");
        }

        var currentNso = NsoFile.Parse(currentMainBytes);
        var baseNso = NsoFile.Parse(baseMainBytes);
        ValidateRequiredSegmentHashes(currentNso);
        ValidateRequiredSegmentHashes(baseNso);
        EnsureSameBuildAndLayout(baseNso, currentNso, "Hyper Training restore");
        var layout = FindLayout(baseNso.BuildId)
            ?? throw new InvalidDataException("Hyper Training restore requires a supported Sword/Shield 1.3.2 base main.");
        var text = currentNso.Text.DecompressedData.ToArray();
        foreach (var region in WrittenRegions(layout))
        {
            baseNso.Text.DecompressedData.AsSpan(region.Offset, region.Length)
                .CopyTo(text.AsSpan(region.Offset, region.Length));
        }

        var output = currentNso.Write(textDecompressedData: text);
        VerifyOutputPreservation(currentMainBytes, output, layout, "Hyper Training restore");
        var restored = Analyze(output, expectedGame);
        if (restored.Kind != SwShHyperTrainingMainKind.NotInstalled)
        {
            throw new InvalidDataException("Hyper Training restore output did not round-trip as vanilla.");
        }

        return output;
    }

    internal static void EnsureCompatibleExecutableIdentity(
        byte[] baseMainBytes,
        byte[] effectiveMainBytes)
    {
        ArgumentNullException.ThrowIfNull(baseMainBytes);
        ArgumentNullException.ThrowIfNull(effectiveMainBytes);

        var baseNso = NsoFile.Parse(baseMainBytes);
        var effectiveNso = NsoFile.Parse(effectiveMainBytes);
        ValidateRequiredSegmentHashes(baseNso);
        ValidateRequiredSegmentHashes(effectiveNso);
        EnsureSameBuildAndLayout(baseNso, effectiveNso, "Hyper Training apply");
    }

    private static int ReadSharedMinimumLevel(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        var levels = new[]
        {
            ReadCompareLevel(text, layout.PreflightCompareOffset, "preflight picker level check"),
            ReadCompareLevel(text, layout.EligibilityCompareOffset, "eligible-slot picker level check"),
            ReadCompareLevel(text, layout.GrayOutCompareOffset, "gray-out picker level check"),
            ReadCompareLevel(text, layout.DetailCompareOffset, "selected Pokemon detail level check"),
        };

        var minimumLevel = levels[0];
        if (levels.Any(level => level != minimumLevel))
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hyper Training picker level checks disagree: found Lv.{string.Join(", Lv.", levels)}."));
        }

        ValidateLevel(minimumLevel);
        return minimumLevel;
    }

    private static int ReadCompareLevel(ReadOnlySpan<byte> text, int offset, string label)
    {
        var instruction = ReadInstruction(text, offset);
        if (!TryDecodeCompareW0Immediate(instruction, out var level))
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hyper Training expected {label} at {FormatTextOffset(offset)}, but found 0x{instruction:X8}."));
        }

        return level;
    }

    private static void ValidateBranches(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        ValidateBranchShape(
            ReadInstruction(text, layout.EligibilityBranchOffset),
            ReadCompareLevel(text, layout.EligibilityCompareOffset, "eligible-slot picker level check"),
            VanillaEligibilityBranchNotEqual,
            PatchedEligibilityBranchLowerThan,
            "eligibility");
        ValidateBranchShape(
            ReadInstruction(text, layout.GrayOutBranchOffset),
            ReadCompareLevel(text, layout.GrayOutCompareOffset, "gray-out picker level check"),
            VanillaGrayOutBranchNotEqual,
            PatchedGrayOutBranchLowerThan,
            "gray-out");
        ValidateBranchShape(
            ReadInstruction(text, layout.DetailBranchOffset),
            ReadCompareLevel(text, layout.DetailCompareOffset, "selected Pokemon detail level check"),
            VanillaDetailBranchNotEqual,
            PatchedDetailBranchLowerThan,
            "detail");
    }

    private static void ValidateDependencies(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        EnsureTextRange(text, LevelGetterOffset, LevelGetterLength, "Hyper Training level getter");
        var actualGetterHash = Convert.ToHexString(SHA256.HashData(
            text.Slice(LevelGetterOffset, LevelGetterLength)));
        if (!string.Equals(actualGetterHash, LevelGetterSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Hyper Training level getter at {FormatTextOffset(LevelGetterOffset)} does not match the supported Sword/Shield 1.3.2 function.");
        }

        ExpectInstruction(
            text,
            layout.PreflightCompareOffset - sizeof(uint),
            layout.PreflightGetterCall,
            "preflight level getter call");
        ExpectInstruction(
            text,
            layout.PreflightCompareOffset + sizeof(uint),
            0x1A9F27E8,
            "preflight comparison result");
        ExpectInstruction(
            text,
            layout.PreflightCompareOffset + (2 * sizeof(uint)),
            0x54000123,
            "preflight lower-than branch");
        ExpectInstruction(
            text,
            layout.EligibilityCompareOffset - sizeof(uint),
            layout.EligibilityGetterCall,
            "eligible-slot level getter call");
        ExpectInstruction(
            text,
            layout.GrayOutCompareOffset - sizeof(uint),
            layout.GrayOutGetterCall,
            "gray-out level getter call");
        ExpectInstruction(
            text,
            layout.DetailCompareOffset - sizeof(uint),
            layout.DetailGetterCall,
            "selected-detail level getter call");
    }

    private static void ExpectInstruction(
        ReadOnlySpan<byte> text,
        int offset,
        uint expected,
        string label)
    {
        var actual = ReadInstruction(text, offset);
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"Hyper Training expected {label} 0x{expected:X8} at {FormatTextOffset(offset)}, but found 0x{actual:X8}.");
        }
    }

    private static void ValidateBranchShape(
        uint actualBranch,
        int compareLevel,
        uint vanillaBranch,
        uint patchedBranch,
        string label)
    {
        var expectedBranch = compareLevel == SwShHyperTrainingAmxPatcher.VanillaMinimumLevel
            ? vanillaBranch
            : patchedBranch;
        if (actualBranch == expectedBranch)
        {
            return;
        }

        throw new InvalidDataException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Hyper Training picker {label} branch does not match its Lv.{compareLevel} compare: expected 0x{expectedBranch:X8}, found 0x{actualBranch:X8}."));
    }

    private static void ValidateLevel(int minimumLevel)
    {
        if (minimumLevel is < SwShHyperTrainingAmxPatcher.MinimumAllowedLevel
            or > SwShHyperTrainingAmxPatcher.MaximumAllowedLevel)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hyper Training picker minimum level {minimumLevel} is outside the supported {SwShHyperTrainingAmxPatcher.MinimumAllowedLevel}-{SwShHyperTrainingAmxPatcher.MaximumAllowedLevel} range."));
        }
    }

    private static void EnsurePatchRange(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        EnsureTextRange(text, layout.PreflightCompareOffset, sizeof(uint), "Hyper Training preflight compare");
        EnsureTextRange(text, layout.EligibilityCompareOffset, sizeof(uint), "Hyper Training eligibility compare");
        EnsureTextRange(text, layout.EligibilityBranchOffset, sizeof(uint), "Hyper Training eligibility branch");
        EnsureTextRange(text, layout.GrayOutCompareOffset, sizeof(uint), "Hyper Training gray-out compare");
        EnsureTextRange(text, layout.GrayOutBranchOffset, sizeof(uint), "Hyper Training gray-out branch");
        EnsureTextRange(text, layout.DetailCompareOffset, sizeof(uint), "Hyper Training detail compare");
        EnsureTextRange(text, layout.DetailBranchOffset, sizeof(uint), "Hyper Training detail branch");
    }

    private static void EnsureTextRange(ReadOnlySpan<byte> text, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset + length > text.Length)
        {
            throw new InvalidDataException($"{label} is outside the decompressed .text segment.");
        }
    }

    private static uint ReadInstruction(ReadOnlySpan<byte> text, int offset)
    {
        EnsureTextRange(text, offset, sizeof(uint), $"Instruction {FormatTextOffset(offset)}");
        return BinaryPrimitives.ReadUInt32LittleEndian(text[offset..(offset + sizeof(uint))]);
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction)
    {
        EnsureTextRange(text, offset, sizeof(uint), $"Patch instruction {FormatTextOffset(offset)}");
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, sizeof(uint)), instruction);
    }

    private static bool TryDecodeCompareW0Immediate(uint instruction, out int immediate)
    {
        const uint immediateMask = 0x003FFC00;
        immediate = 0;
        if ((instruction & ~immediateMask) != 0x7100001F)
        {
            return false;
        }

        immediate = (int)((instruction & immediateMask) >> 10);
        return true;
    }

    private static uint EncodeCompareW0Immediate(int immediate)
    {
        ValidateLevel(immediate);
        return 0x7100001F | (uint)(immediate << 10);
    }

    private static PatchLayout? FindLayout(ReadOnlySpan<byte> buildId)
    {
        foreach (var layout in Layouts)
        {
            if (IsCanonicalBuildId(buildId, layout.BuildId))
            {
                return layout;
            }
        }

        return null;
    }

    private static void EnsureSupportedExpectedGame(ProjectGame? expectedGame)
    {
        if (expectedGame is not (ProjectGame.Sword or ProjectGame.Shield))
        {
            throw new InvalidDataException(
                "Hyper Training patching requires Pokemon Sword or Pokemon Shield to be selected explicitly.");
        }
    }

    private static bool IsCanonicalBuildId(ReadOnlySpan<byte> buildId, string expectedPrefixHex)
    {
        const int nsoBuildIdLength = 0x20;
        const int knownBuildIdLength = 0x14;
        if (buildId.Length != nsoBuildIdLength)
        {
            return false;
        }

        var expectedPrefix = Convert.FromHexString(expectedPrefixHex);
        return expectedPrefix.Length == knownBuildIdLength
            && buildId[..knownBuildIdLength].SequenceEqual(expectedPrefix)
            && IsZero(buildId[knownBuildIdLength..]);
    }

    private static SwShHyperTrainingMainAnalysis? CreateGameMismatchAnalysis(
        PatchLayout layout,
        ProjectGame? expectedGame,
        string buildId)
    {
        if (expectedGame is null || layout.Game == expectedGame.Value)
        {
            return null;
        }

        return new SwShHyperTrainingMainAnalysis(
            SwShHyperTrainingMainKind.GameMismatch,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Selected {FormatGame(expectedGame.Value)}, but exefs/main build ID is {layout.GameName}. Hyper Training will not patch this file because Sword and Shield use different picker check sites."),
            SwShHyperTrainingAmxPatcher.VanillaMinimumLevel,
            buildId,
            FormatTextOffset(layout.EligibilityCompareOffset),
            layout.Game);
    }

    private static string FormatBuildId(byte[] buildId)
    {
        var buildIdLength = Math.Min(20, buildId.Length);
        return Convert.ToHexString(buildId.AsSpan(0, buildIdLength));
    }

    private static bool IsZero(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            if (value != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatTextOffset(int offset)
    {
        return string.Create(CultureInfo.InvariantCulture, $"main.text+0x{offset:X8}");
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

    private static void VerifyOutputPreservation(
        byte[] sourceMainBytes,
        byte[] outputMainBytes,
        PatchLayout layout,
        string operation)
    {
        var source = NsoFile.Parse(sourceMainBytes);
        var output = NsoFile.Parse(outputMainBytes);
        ValidateRequiredSegmentHashes(source);
        ValidateRequiredSegmentHashes(output);
        EnsureSameBuildAndLayout(source, output, operation);
        VerifyPreservedSegment(source.Ro, output.Ro, ".ro", operation);
        VerifyPreservedSegment(source.Data, output.Data, ".data", operation);
        VerifyTextOutsideRegions(
            source.Text.DecompressedData,
            output.Text.DecompressedData,
            WrittenRegions(layout),
            operation);
    }

    private static void EnsureSameBuildAndLayout(NsoFile left, NsoFile right, string operation)
    {
        if (left.Version != right.Version
            || left.Flags != right.Flags
            || !left.BuildId.SequenceEqual(right.BuildId)
            || !SwShExeFsMainComparison.StableHeaderBytesMatch(left.RawHeader, right.RawHeader))
        {
            throw new InvalidDataException(
                $"{operation} requires matching NSO version, flags, stable header metadata, and full 32-byte build identity.");
        }

        for (var index = 0; index < left.Segments.Count; index++)
        {
            var leftSegment = left.Segments[index];
            var rightSegment = right.Segments[index];
            if (leftSegment.Header.MemoryOffset != rightSegment.Header.MemoryOffset
                || leftSegment.Header.DecompressedSize != rightSegment.Header.DecompressedSize
                || leftSegment.DecompressedData.Length != rightSegment.DecompressedData.Length)
            {
                throw new InvalidDataException(
                    $"{operation} requires matching {leftSegment.Name} memory offsets and decompressed sizes.");
            }
        }
    }

    private static void VerifyPreservedSegment(
        NsoSegment source,
        NsoSegment output,
        string segmentName,
        string operation)
    {
        if (source.Header.MemoryOffset != output.Header.MemoryOffset
            || source.Header.DecompressedSize != output.Header.DecompressedSize
            || source.CompressedSize != output.CompressedSize
            || !source.Hash.SequenceEqual(output.Hash)
            || !source.CompressedData.SequenceEqual(output.CompressedData)
            || !source.DecompressedData.SequenceEqual(output.DecompressedData))
        {
            throw new InvalidDataException($"{operation} verification found a changed {segmentName} segment.");
        }
    }

    private static void VerifyTextOutsideRegions(
        ReadOnlySpan<byte> sourceText,
        ReadOnlySpan<byte> outputText,
        IReadOnlyList<PatchRegion> writtenRegions,
        string operation)
    {
        if (sourceText.Length != outputText.Length)
        {
            throw new InvalidDataException($"{operation} verification found a changed .text size.");
        }

        var cursor = 0;
        foreach (var region in writtenRegions.OrderBy(region => region.Offset))
        {
            EnsureTextRange(sourceText, region.Offset, region.Length, region.Label);
            if (!sourceText.Slice(cursor, region.Offset - cursor)
                .SequenceEqual(outputText.Slice(cursor, region.Offset - cursor)))
            {
                throw new InvalidDataException(
                    $"{operation} verification found a change outside Hyper Training written ranges before {region.Label}.");
            }

            cursor = region.Offset + region.Length;
        }

        if (!sourceText[cursor..].SequenceEqual(outputText[cursor..]))
        {
            throw new InvalidDataException(
                $"{operation} verification found a change outside Hyper Training written ranges after the final region.");
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
                $"Hyper Training patching rejected {segment.Name} because its required NSO header hash does not match the decompressed segment.");
        }
    }

    private static IReadOnlyList<PatchRegion> WrittenRegions(PatchLayout layout)
    {
        return
        [
            new PatchRegion(layout.PreflightCompareOffset, sizeof(uint), "preflight compare"),
            new PatchRegion(layout.EligibilityCompareOffset, 2 * sizeof(uint), "eligibility compare and branch"),
            new PatchRegion(layout.GrayOutCompareOffset, 2 * sizeof(uint), "gray-out compare and branch"),
            new PatchRegion(layout.DetailCompareOffset, 2 * sizeof(uint), "detail compare and branch"),
        ];
    }

    private sealed record PatchLayout(
        ProjectGame Game,
        string GameName,
        string BuildId,
        int PreflightCompareOffset,
        int EligibilityCompareOffset,
        int EligibilityBranchOffset,
        int GrayOutCompareOffset,
        int GrayOutBranchOffset,
        int DetailCompareOffset,
        int DetailBranchOffset,
        uint PreflightGetterCall,
        uint EligibilityGetterCall,
        uint GrayOutGetterCall,
        uint DetailGetterCall);

    private sealed record PatchRegion(int Offset, int Length, string Label);
}
