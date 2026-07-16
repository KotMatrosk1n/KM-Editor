// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;
using System.Buffers.Binary;
using System.Globalization;

namespace KM.SwSh.ShinyRate;

internal enum SwShShinyRateMainKind
{
    Default,
    FixedRolls,
    AlwaysShiny,
    UnsupportedBuild,
    GameMismatch,
    MissingFunction,
    AmbiguousFunction,
    Conflict,
}

internal enum SwShShinyRateMode
{
    Default,
    FixedRolls,
    AlwaysShiny,
}

internal sealed record SwShShinyRateMainAnalysis(
    SwShShinyRateMainKind Kind,
    string Message,
    string BuildId,
    string FunctionOffsetHex,
    string CompareOffsetHex,
    string BreakOffsetHex,
    int? RollCount,
    double? Chance,
    int? OddsDenominator,
    ProjectGame? DetectedGame);

internal static class SwShShinyRateMainPatcher
{
    public const int BaseShinyOdds = 4096;
    public const int MinimumFixedRollCount = 1;
    public const int MaximumFixedRollCount = 4091;
    public const int MinimumCustomDenominator = 2;
    public const int MaximumCustomDenominator = BaseShinyOdds;

    public const int SwordFunctionOffset = 0x00D311C0;
    public const int SwordCompareOffset = 0x00D31488;
    public const int SwordBreakOffset = 0x00D3148C;
    public const int ShieldOffsetDelta = 0x30;
    public const int PatchLength = 0x08;

    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";

    private const uint VanillaCompareInstruction = 0x6B17033F;
    private const uint VanillaBreakInstruction = 0x54000062;
    private const uint NopInstruction = 0xD503201F;
    private const uint FixedCompareBaseInstruction = 0x7100033F;

    private static readonly byte[] FunctionPrelude =
    [
        0xFF, 0x03, 0x06, 0xD1, 0xFC, 0x6F, 0x12, 0xA9,
        0xFA, 0x67, 0x13, 0xA9, 0xF8, 0x5F, 0x14, 0xA9,
        0xF6, 0x57, 0x15, 0xA9, 0xF4, 0x4F, 0x16, 0xA9,
        0xFD, 0x7B, 0x17, 0xA9, 0xFD, 0xC3, 0x05, 0x91,
        0xFA, 0xC6, 0x00, 0xF0,
    ];

    private static readonly byte[] SwordLoopDependenciesBeforePatch =
    [
        0x17, 0x1D, 0x00, 0x72, 0x40, 0x05, 0x00, 0x54,
        0xF8, 0x03, 0x1F, 0x2A, 0xF9, 0x03, 0x00, 0x32,
        0x48, 0x03, 0x40, 0xF9, 0x08, 0x4D, 0x40, 0xF9,
        0x09, 0x29, 0x40, 0xA9, 0x41, 0x01, 0x09, 0x0B,
        0x4A, 0x01, 0x09, 0xCA, 0x49, 0xA1, 0xC9, 0xCA,
        0x29, 0x41, 0x0A, 0xCA, 0x4A, 0x6D, 0xCA, 0x93,
        0x09, 0x29, 0x00, 0xA9, 0xA0, 0xA2, 0x42, 0xB9,
        0xB4, 0x17, 0xE9, 0x97, 0x18, 0x03, 0x00, 0x2A,
    ];

    private static readonly byte[] ShieldLoopDependenciesBeforePatch =
    [
        0x17, 0x1D, 0x00, 0x72, 0x40, 0x05, 0x00, 0x54,
        0xF8, 0x03, 0x1F, 0x2A, 0xF9, 0x03, 0x00, 0x32,
        0x48, 0x03, 0x40, 0xF9, 0x08, 0x4D, 0x40, 0xF9,
        0x09, 0x29, 0x40, 0xA9, 0x41, 0x01, 0x09, 0x0B,
        0x4A, 0x01, 0x09, 0xCA, 0x49, 0xA1, 0xC9, 0xCA,
        0x29, 0x41, 0x0A, 0xCA, 0x4A, 0x6D, 0xCA, 0x93,
        0x09, 0x29, 0x00, 0xA9, 0xA0, 0xA2, 0x42, 0xB9,
        0xA8, 0x17, 0xE9, 0x97, 0x18, 0x03, 0x00, 0x2A,
    ];

    private static readonly byte[] LoopDependenciesAfterPatch =
    [
        0x39, 0x07, 0x00, 0x11,
        0x20, 0xFE, 0x07, 0x36,
        0x1F, 0x03, 0x00, 0x72,
        0xE8, 0x03, 0x00, 0x32,
        0x08, 0x15, 0x88, 0x1A,
        0x88, 0x0A, 0x00, 0xB9,
        0x88, 0x12, 0x40, 0xB9,
        0x88, 0xFA, 0xFF, 0x35,
    ];

    private static readonly PatchLayout[] Layouts =
    [
        new(
            ProjectGame.Sword,
            "Pokemon Sword 1.3.2",
            SwordBuildId,
            0,
            SwordLoopDependenciesBeforePatch),
        new(
            ProjectGame.Shield,
            "Pokemon Shield 1.3.2",
            ShieldBuildId,
            ShieldOffsetDelta,
            ShieldLoopDependenciesBeforePatch),
    ];

    public static SwShShinyRateMainAnalysis Analyze(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        var buildId = "unknown";
        ProjectGame? detectedGame = null;
        PatchLayout? detectedLayout = null;
        try
        {
            var nso = NsoFile.Parse(mainBytes);
            ValidateRequiredSegmentHashes(nso);
            buildId = FormatBuildId(nso.BuildId);
            var layout = FindLayout(nso.BuildId);
            if (layout is null)
            {
                return CreateAnalysis(
                    SwShShinyRateMainKind.UnsupportedBuild,
                    "Shiny Rate supports Sword and Shield 1.3.2 exefs/main files. This build ID is not recognized.",
                    buildId,
                    layout: null,
                    rollCount: null,
                    chance: null,
                    oddsDenominator: null,
                    detectedGame: null);
            }

            detectedLayout = layout;
            detectedGame = layout.Game;

            var mismatch = CreateGameMismatchAnalysis(layout, expectedGame, buildId);
            if (mismatch is not null)
            {
                return mismatch;
            }

            var text = nso.Text.DecompressedData;
            EnsurePatchRange(text, layout);
            ValidateDependencies(text, layout);

            var preludeMatch = FindVerifiedFunctionOffset(text, layout, buildId);
            if (preludeMatch is not null)
            {
                return preludeMatch;
            }

            var compareInstruction = ReadInstruction(text, layout.CompareOffset);
            var breakInstruction = ReadInstruction(text, layout.BreakOffset);
            var isVanillaCompare = compareInstruction == VanillaCompareInstruction;
            var isVanillaBreak = breakInstruction == VanillaBreakInstruction;
            var isAlwaysBreak = breakInstruction == NopInstruction;
            var hasFixedCompare = TryDecodeFixedCompareInstruction(compareInstruction, out var fixedRollCount);

            if (isVanillaCompare && isVanillaBreak)
            {
                return CreateAnalysis(
                    SwShShinyRateMainKind.Default,
                    "Shiny Rate is using the game's original shiny reroll logic.",
                    buildId,
                    layout,
                    rollCount: null,
                    chance: null,
                    oddsDenominator: null,
                    layout.Game);
            }

            if (hasFixedCompare && isVanillaBreak)
            {
                return CreateRateAnalysis(
                    SwShShinyRateMainKind.FixedRolls,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Shiny Rate is fixed at {fixedRollCount} PID roll{(fixedRollCount == 1 ? string.Empty : "s")}."),
                    buildId,
                    layout,
                    fixedRollCount,
                    layout.Game);
            }

            if (isVanillaCompare && isAlwaysBreak)
            {
                return CreateAnalysis(
                    SwShShinyRateMainKind.AlwaysShiny,
                    "Shiny Rate is patched to always resolve random shiny checks as shiny.",
                    buildId,
                    layout,
                    rollCount: null,
                    chance: 1,
                    oddsDenominator: 1,
                    layout.Game);
            }

            var expectedShapes = "vanilla compare plus branch, fixed roll compare plus branch, or vanilla compare plus always-shiny NOP";
            return CreateAnalysis(
                SwShShinyRateMainKind.Conflict,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Shiny Rate expected {expectedShapes} at {FormatTextOffset(layout.CompareOffset)}, but found compare 0x{compareInstruction:X8} and branch 0x{breakInstruction:X8}."),
                buildId,
                layout,
                rollCount: null,
                chance: null,
                oddsDenominator: null,
                layout.Game);
        }
        catch (InvalidDataException exception)
        {
            return CreateAnalysis(
                SwShShinyRateMainKind.Conflict,
                exception.Message,
                buildId,
                detectedLayout,
                rollCount: null,
                chance: null,
                oddsDenominator: null,
                detectedGame);
        }
    }

    public static byte[] ApplyRate(
        byte[] mainBytes,
        SwShShinyRateMode mode,
        int? rollCount,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);
        EnsureSupportedExpectedGame(expectedGame);
        if (mode == SwShShinyRateMode.FixedRolls)
        {
            ValidateRollCount(rollCount);
        }

        var analysis = Analyze(mainBytes, expectedGame);
        if (analysis.Kind is SwShShinyRateMainKind.UnsupportedBuild
            or SwShShinyRateMainKind.GameMismatch
            or SwShShinyRateMainKind.MissingFunction
            or SwShShinyRateMainKind.AmbiguousFunction
            or SwShShinyRateMainKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }

        var nso = NsoFile.Parse(mainBytes);
        ValidateRequiredSegmentHashes(nso);
        var layout = FindLayout(nso.BuildId)
            ?? throw new InvalidDataException("Shiny Rate supports Sword and Shield 1.3.2 exefs/main files.");
        var text = nso.Text.DecompressedData.ToArray();
        EnsurePatchRange(text, layout);
        ValidateDependencies(text, layout);

        WriteInstruction(text, layout.CompareOffset, VanillaCompareInstruction);
        WriteInstruction(text, layout.BreakOffset, VanillaBreakInstruction);

        if (mode == SwShShinyRateMode.FixedRolls)
        {
            WriteInstruction(text, layout.CompareOffset, EncodeFixedCompareInstruction(rollCount!.Value));
        }
        else if (mode == SwShShinyRateMode.AlwaysShiny)
        {
            WriteInstruction(text, layout.BreakOffset, NopInstruction);
        }

        var output = nso.Write(textDecompressedData: text);
        ValidateOutput(mainBytes, output, mode, rollCount, expectedGame, layout);
        return output;
    }

    public static double CalculateChance(int rollCount)
    {
        ValidateRollCount(rollCount);
        return 1 - Math.Pow((BaseShinyOdds - 1d) / BaseShinyOdds, rollCount);
    }

    public static int CalculateOddsDenominator(double chance)
    {
        if (chance >= 1)
        {
            return 1;
        }

        if (chance <= 0)
        {
            return int.MaxValue;
        }

        return Math.Max(1, (int)Math.Round(1 / chance));
    }

    public static int CalculateRollsForTargetDenominator(int denominator)
    {
        var targetDenominator = Math.Clamp(
            denominator,
            MinimumCustomDenominator,
            MaximumCustomDenominator);
        var targetChance = 1d / targetDenominator;
        var rolls = (int)Math.Ceiling(
            Math.Log(1 - targetChance) / Math.Log((BaseShinyOdds - 1d) / BaseShinyOdds));

        return Math.Clamp(rolls, MinimumFixedRollCount, MaximumFixedRollCount);
    }

    public static IReadOnlyList<SwShExeFsReservedRegion> ReservedMainTextRegions(ProjectGame? game = null)
    {
        return SwShExeFsReservedRegionLedger
            .MainTextRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerShinyRate, game)
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
        if (IsBlocked(currentAnalysis.Kind))
        {
            throw new InvalidDataException(currentAnalysis.Message);
        }

        var baseAnalysis = Analyze(baseMainBytes, expectedGame);
        if (baseAnalysis.Kind != SwShShinyRateMainKind.Default)
        {
            throw new InvalidDataException(
                "Shiny Rate restore requires a verified selected-game vanilla base exefs/main.");
        }

        var currentNso = NsoFile.Parse(currentMainBytes);
        var baseNso = NsoFile.Parse(baseMainBytes);
        ValidateRequiredSegmentHashes(currentNso);
        ValidateRequiredSegmentHashes(baseNso);
        EnsureSameBuildAndLayout(baseNso, currentNso, "Shiny Rate restore");
        var layout = FindLayout(baseNso.BuildId)
            ?? throw new InvalidDataException("Shiny Rate restore requires a supported Sword/Shield 1.3.2 base main.");
        var text = currentNso.Text.DecompressedData.ToArray();
        baseNso.Text.DecompressedData.AsSpan(layout.CompareOffset, PatchLength)
            .CopyTo(text.AsSpan(layout.CompareOffset, PatchLength));

        var output = currentNso.Write(textDecompressedData: text);
        ValidateOutput(
            currentMainBytes,
            output,
            SwShShinyRateMode.Default,
            rollCount: null,
            expectedGame,
            layout,
            operation: "Shiny Rate restore");
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
        EnsureSameBuildAndLayout(baseNso, effectiveNso, "Shiny Rate apply");
    }

    public static string FormatTextOffset(int offset)
    {
        return string.Create(CultureInfo.InvariantCulture, $"main.text+0x{offset:X8}");
    }

    internal static uint EncodeFixedCompareInstruction(int rollCount)
    {
        ValidateRollCount(rollCount);
        return FixedCompareBaseInstruction | (uint)((rollCount & 0xFFF) << 10);
    }

    internal static bool TryDecodeFixedCompareInstruction(uint instruction, out int rollCount)
    {
        const uint immediateMask = 0x003FFC00;
        rollCount = (int)((instruction & immediateMask) >> 10);
        return rollCount is >= MinimumFixedRollCount and <= MaximumFixedRollCount
            && instruction == EncodeFixedCompareInstruction(rollCount);
    }

    internal static void ValidateRollCount(int? rollCount)
    {
        if (rollCount is null)
        {
            throw new InvalidDataException("Shiny Rate fixed mode requires a roll count.");
        }

        if (rollCount.Value is < MinimumFixedRollCount or > MaximumFixedRollCount)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Shiny Rate roll count must be between {MinimumFixedRollCount} and {MaximumFixedRollCount}."));
        }
    }

    private static SwShShinyRateMainAnalysis CreateRateAnalysis(
        SwShShinyRateMainKind kind,
        string message,
        string buildId,
        PatchLayout layout,
        int rollCount,
        ProjectGame detectedGame)
    {
        var chance = CalculateChance(rollCount);
        return CreateAnalysis(
            kind,
            message,
            buildId,
            layout,
            rollCount,
            chance,
            CalculateOddsDenominator(chance),
            detectedGame);
    }

    private static SwShShinyRateMainAnalysis CreateAnalysis(
        SwShShinyRateMainKind kind,
        string message,
        string buildId,
        PatchLayout? layout,
        int? rollCount,
        double? chance,
        int? oddsDenominator,
        ProjectGame? detectedGame)
    {
        return new SwShShinyRateMainAnalysis(
            kind,
            message,
            buildId,
            layout is null ? "unknown" : FormatTextOffset(layout.FunctionOffset),
            layout is null ? "unknown" : FormatTextOffset(layout.CompareOffset),
            layout is null ? "unknown" : FormatTextOffset(layout.BreakOffset),
            rollCount,
            chance,
            oddsDenominator,
            detectedGame);
    }

    private static SwShShinyRateMainAnalysis? FindVerifiedFunctionOffset(
        byte[] text,
        PatchLayout layout,
        string buildId)
    {
        if (text.AsSpan(layout.FunctionOffset, FunctionPrelude.Length).SequenceEqual(FunctionPrelude))
        {
            return null;
        }

        return CreateAnalysis(
            SwShShinyRateMainKind.MissingFunction,
            $"Shiny Rate expected the verified {layout.GameName} reroll loop at {FormatTextOffset(layout.FunctionOffset)}, but that exact function prelude is missing.",
            buildId,
            layout,
            rollCount: null,
            chance: null,
            oddsDenominator: null,
            layout.Game);
    }

    private static SwShShinyRateMainAnalysis? CreateGameMismatchAnalysis(
        PatchLayout layout,
        ProjectGame? expectedGame,
        string buildId)
    {
        if (expectedGame is null || layout.Game == expectedGame.Value)
        {
            return null;
        }

        return CreateAnalysis(
            SwShShinyRateMainKind.GameMismatch,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Selected {FormatGame(expectedGame.Value)}, but exefs/main build ID is {layout.GameName}. Shiny Rate will not patch this file because Sword and Shield use different verified reroll loop sites."),
            buildId,
            layout,
            rollCount: null,
            chance: null,
            oddsDenominator: null,
            layout.Game);
    }

    private static bool IsBlocked(SwShShinyRateMainKind kind)
    {
        return kind is SwShShinyRateMainKind.UnsupportedBuild
            or SwShShinyRateMainKind.GameMismatch
            or SwShShinyRateMainKind.MissingFunction
            or SwShShinyRateMainKind.AmbiguousFunction
            or SwShShinyRateMainKind.Conflict;
    }

    private static void ValidateOutput(
        byte[] input,
        byte[] output,
        SwShShinyRateMode mode,
        int? rollCount,
        ProjectGame? expectedGame,
        PatchLayout layout,
        string operation = "Shiny Rate apply")
    {
        var before = NsoFile.Parse(input);
        var after = NsoFile.Parse(output);
        ValidateRequiredSegmentHashes(before);
        ValidateRequiredSegmentHashes(after);
        EnsureSameBuildAndLayout(before, after, operation);
        VerifyPreservedSegment(before.Ro, after.Ro, ".ro", operation);
        VerifyPreservedSegment(before.Data, after.Data, ".data", operation);
        VerifyTextOutsideWrittenRegion(
            before.Text.DecompressedData,
            after.Text.DecompressedData,
            layout,
            operation);

        var analysis = Analyze(output, expectedGame);
        var verified = mode switch
        {
            SwShShinyRateMode.Default => analysis.Kind == SwShShinyRateMainKind.Default,
            SwShShinyRateMode.FixedRolls => analysis.Kind == SwShShinyRateMainKind.FixedRolls
                && analysis.RollCount == rollCount,
            SwShShinyRateMode.AlwaysShiny => analysis.Kind == SwShShinyRateMainKind.AlwaysShiny,
            _ => false,
        };

        if (!verified)
        {
            throw new InvalidDataException($"{operation} verification failed after writing exefs/main.");
        }
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

    private static void VerifyTextOutsideWrittenRegion(
        ReadOnlySpan<byte> sourceText,
        ReadOnlySpan<byte> outputText,
        PatchLayout layout,
        string operation)
    {
        if (sourceText.Length != outputText.Length)
        {
            throw new InvalidDataException($"{operation} verification found a changed .text size.");
        }

        if (!sourceText[..layout.CompareOffset].SequenceEqual(outputText[..layout.CompareOffset])
            || !sourceText[(layout.CompareOffset + PatchLength)..]
                .SequenceEqual(outputText[(layout.CompareOffset + PatchLength)..]))
        {
            throw new InvalidDataException(
                $"{operation} verification found a change outside the Shiny Rate written range.");
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
                $"Shiny Rate patching rejected {segment.Name} because its required NSO header hash does not match the decompressed segment.");
        }
    }

    private static void ValidateDependencies(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        var dependencyBeforeOffset = layout.CompareOffset - layout.DependenciesBeforePatch.Length;
        EnsureTextRange(
            text,
            dependencyBeforeOffset,
            layout.DependenciesBeforePatch.Length,
            "Shiny Rate reroll loop dependencies before the patch site");
        if (!text.Slice(dependencyBeforeOffset, layout.DependenciesBeforePatch.Length)
            .SequenceEqual(layout.DependenciesBeforePatch))
        {
            throw new InvalidDataException(
                $"Shiny Rate reroll loop dependencies before {FormatTextOffset(layout.CompareOffset)} do not match the supported {layout.GameName} function.");
        }

        var dependencyAfterOffset = layout.CompareOffset + PatchLength;
        EnsureTextRange(
            text,
            dependencyAfterOffset,
            LoopDependenciesAfterPatch.Length,
            "Shiny Rate reroll loop dependencies after the patch site");
        if (!text.Slice(dependencyAfterOffset, LoopDependenciesAfterPatch.Length)
            .SequenceEqual(LoopDependenciesAfterPatch))
        {
            throw new InvalidDataException(
                $"Shiny Rate reroll loop dependencies after {FormatTextOffset(layout.BreakOffset)} do not match the supported {layout.GameName} function.");
        }
    }

    private static void EnsurePatchRange(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        EnsureTextRange(text, layout.FunctionOffset, FunctionPrelude.Length, "Shiny Rate reroll loop function");
        EnsureTextRange(
            text,
            layout.CompareOffset - layout.DependenciesBeforePatch.Length,
            layout.DependenciesBeforePatch.Length,
            "Shiny Rate reroll loop dependencies before the patch site");
        EnsureTextRange(text, layout.CompareOffset, sizeof(uint), "Shiny Rate reroll compare");
        EnsureTextRange(text, layout.BreakOffset, sizeof(uint), "Shiny Rate reroll break branch");
        EnsureTextRange(
            text,
            layout.CompareOffset + PatchLength,
            LoopDependenciesAfterPatch.Length,
            "Shiny Rate reroll loop dependencies after the patch site");
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
                "Shiny Rate patching requires Pokemon Sword or Pokemon Shield to be selected explicitly.");
        }
    }

    private static string FormatBuildId(byte[] buildId)
    {
        var buildIdLength = Math.Min(20, buildId.Length);
        return Convert.ToHexString(buildId.AsSpan(0, buildIdLength));
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

    private static string FormatGame(ProjectGame game)
    {
        return game switch
        {
            ProjectGame.Sword => "Pokemon Sword",
            ProjectGame.Shield => "Pokemon Shield",
            _ => game.ToString(),
        };
    }

    private sealed record PatchLayout(
        ProjectGame Game,
        string GameName,
        string BuildId,
        int Shift,
        byte[] DependenciesBeforePatch)
    {
        public int FunctionOffset => SwordFunctionOffset + Shift;
        public int CompareOffset => SwordCompareOffset + Shift;
        public int BreakOffset => SwordBreakOffset + Shift;
    }
}
