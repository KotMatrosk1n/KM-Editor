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
    double Chance,
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

    private const int FunctionSearchStartOffset = 0x00700000;
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

    private static readonly PatchLayout[] Layouts =
    [
        new(ProjectGame.Sword, "Pokemon Sword 1.3.2", SwordBuildId, 0),
        new(ProjectGame.Shield, "Pokemon Shield 1.3.2", ShieldBuildId, ShieldOffsetDelta),
    ];

    public static SwShShinyRateMainAnalysis Analyze(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        try
        {
            var nso = NsoFile.Parse(mainBytes);
            var buildId = FormatBuildId(nso.BuildId);
            var layout = FindLayout(buildId);
            if (layout is null)
            {
                return CreateAnalysis(
                    SwShShinyRateMainKind.UnsupportedBuild,
                    "Shiny Rate supports Sword and Shield 1.3.2 exefs/main files. This build ID is not recognized.",
                    buildId,
                    layout: null,
                    rollCount: null,
                    chance: 0,
                    oddsDenominator: null,
                    detectedGame: null);
            }

            var mismatch = CreateGameMismatchAnalysis(layout, expectedGame, buildId);
            if (mismatch is not null)
            {
                return mismatch;
            }

            var text = nso.Text.DecompressedData;
            EnsurePatchRange(text, layout);

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
                return CreateRateAnalysis(
                    SwShShinyRateMainKind.Default,
                    "Shiny Rate is using the game's original shiny reroll logic.",
                    buildId,
                    layout,
                    rollCount: MinimumFixedRollCount,
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
                chance: 0,
                oddsDenominator: null,
                layout.Game);
        }
        catch (InvalidDataException exception)
        {
            return CreateAnalysis(
                SwShShinyRateMainKind.Conflict,
                exception.Message,
                buildId: "unknown",
                layout: null,
                rollCount: null,
                chance: 0,
                oddsDenominator: null,
                detectedGame: null);
        }
    }

    public static byte[] ApplyRate(
        byte[] mainBytes,
        SwShShinyRateMode mode,
        int? rollCount,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);
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
        var layout = FindLayout(FormatBuildId(nso.BuildId))
            ?? throw new InvalidDataException("Shiny Rate supports Sword and Shield 1.3.2 exefs/main files.");
        var text = nso.Text.DecompressedData.ToArray();
        EnsurePatchRange(text, layout);

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

    public static IReadOnlyList<SwShExeFsReservedRegion> ReservedMainTextRegions()
    {
        return SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerShinyRate);
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
        double chance,
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
        var matches = FindPreludeMatches(text).ToArray();
        if (matches.Length == 0)
        {
            return CreateAnalysis(
                SwShShinyRateMainKind.MissingFunction,
                "Shiny Rate could not find the verified shiny reroll loop in exefs/main.",
                buildId,
                layout,
                rollCount: null,
                chance: 0,
                oddsDenominator: null,
                layout.Game);
        }

        if (matches.Length > 1)
        {
            return CreateAnalysis(
                SwShShinyRateMainKind.AmbiguousFunction,
                "Shiny Rate found multiple possible shiny reroll loops in exefs/main and will not guess which one to patch.",
                buildId,
                layout,
                rollCount: null,
                chance: 0,
                oddsDenominator: null,
                layout.Game);
        }

        if (matches[0] != layout.FunctionOffset)
        {
            return CreateAnalysis(
                SwShShinyRateMainKind.Conflict,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Shiny Rate found the reroll loop at {FormatTextOffset(matches[0])}, but {layout.GameName} expects it at {FormatTextOffset(layout.FunctionOffset)}."),
                buildId,
                layout,
                rollCount: null,
                chance: 0,
                oddsDenominator: null,
                layout.Game);
        }

        return null;
    }

    private static IEnumerable<int> FindPreludeMatches(byte[] text)
    {
        var searchStart = Math.Min(FunctionSearchStartOffset, text.Length);
        for (var offset = searchStart; offset <= text.Length - FunctionPrelude.Length; offset++)
        {
            if (text.AsSpan(offset, FunctionPrelude.Length).SequenceEqual(FunctionPrelude))
            {
                yield return offset;
            }
        }
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
            chance: 0,
            oddsDenominator: null,
            layout.Game);
    }

    private static void ValidateOutput(
        byte[] input,
        byte[] output,
        SwShShinyRateMode mode,
        int? rollCount,
        ProjectGame? expectedGame,
        PatchLayout layout)
    {
        var before = NsoFile.Parse(input);
        var after = NsoFile.Parse(output);
        if (!before.BuildId.SequenceEqual(after.BuildId))
        {
            throw new InvalidDataException("Shiny Rate patch changed the NSO build ID.");
        }

        if (!before.Ro.DecompressedData.SequenceEqual(after.Ro.DecompressedData))
        {
            throw new InvalidDataException("Shiny Rate patch unexpectedly changed the .ro segment.");
        }

        if (!before.Data.DecompressedData.SequenceEqual(after.Data.DecompressedData))
        {
            throw new InvalidDataException("Shiny Rate patch unexpectedly changed the .data segment.");
        }

        var textBefore = before.Text.DecompressedData;
        var textAfter = after.Text.DecompressedData;
        if (textBefore.Length != textAfter.Length)
        {
            throw new InvalidDataException("Shiny Rate patch changed the decompressed .text segment size.");
        }

        for (var offset = 0; offset < textBefore.Length; offset++)
        {
            var changed = textBefore[offset] != textAfter[offset];
            var insidePatch = offset >= layout.CompareOffset && offset < layout.CompareOffset + PatchLength;
            if (changed && !insidePatch)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Shiny Rate patch unexpectedly changed .text byte 0x{offset:X}."));
            }
        }

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
            throw new InvalidDataException("Shiny Rate patch verification failed after writing exefs/main.");
        }
    }

    private static void EnsurePatchRange(ReadOnlySpan<byte> text, PatchLayout layout)
    {
        EnsureTextRange(text, layout.FunctionOffset, FunctionPrelude.Length, "Shiny Rate reroll loop function");
        EnsureTextRange(text, layout.CompareOffset, sizeof(uint), "Shiny Rate reroll compare");
        EnsureTextRange(text, layout.BreakOffset, sizeof(uint), "Shiny Rate reroll break branch");
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

    private static PatchLayout? FindLayout(string buildId)
    {
        return Layouts.FirstOrDefault(layout =>
            string.Equals(layout.BuildId, buildId, StringComparison.OrdinalIgnoreCase));
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

    private sealed record PatchLayout(
        ProjectGame Game,
        string GameName,
        string BuildId,
        int Shift)
    {
        public int FunctionOffset => SwordFunctionOffset + Shift;
        public int CompareOffset => SwordCompareOffset + Shift;
        public int BreakOffset => SwordBreakOffset + Shift;
    }
}
