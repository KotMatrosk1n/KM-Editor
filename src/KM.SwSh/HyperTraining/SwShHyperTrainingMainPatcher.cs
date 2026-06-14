// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.ExeFs;
using System.Buffers.Binary;
using System.Globalization;

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

    private const int ShieldOffsetDelta = 0x30;
    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";

    private const uint VanillaEligibilityBranchNotEqual = 0x54000061;
    private const uint PatchedEligibilityBranchLowerThan = 0x54000063;
    private const uint VanillaGrayOutBranchNotEqual = 0x540000A1;
    private const uint PatchedGrayOutBranchLowerThan = 0x540000A3;
    private const uint VanillaDetailBranchNotEqual = 0x540002C1;
    private const uint PatchedDetailBranchLowerThan = 0x540002C3;

    private static readonly PatchLayout[] Layouts =
    [
        new(ProjectGame.Sword, "Pokemon Sword 1.3.2", SwordBuildId, 0),
        new(ProjectGame.Shield, "Pokemon Shield 1.3.2", ShieldBuildId, ShieldOffsetDelta),
    ];

    public static SwShHyperTrainingMainAnalysis Analyze(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        try
        {
            var nso = SwShNsoFile.Parse(mainBytes);
            var buildId = FormatBuildId(nso.BuildId);
            var layout = FindLayout(buildId);
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

            var mismatch = CreateGameMismatchAnalysis(layout, expectedGame, buildId);
            if (mismatch is not null)
            {
                return mismatch;
            }

            var text = nso.Text.DecompressedData;
            EnsurePatchRange(text, layout);
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
                FormatTextOffset(layout.EligibilityCompareOffset),
                layout.Game);
        }
        catch (InvalidDataException exception)
        {
            return new SwShHyperTrainingMainAnalysis(
                SwShHyperTrainingMainKind.Conflict,
                exception.Message,
                SwShHyperTrainingAmxPatcher.VanillaMinimumLevel,
                "unknown",
                "unknown",
                DetectedGame: null);
        }
    }

    public static byte[] ApplyMinimumLevel(byte[] mainBytes, int minimumLevel, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);
        ValidateLevel(minimumLevel);

        var analysis = Analyze(mainBytes, expectedGame);
        if (analysis.Kind is SwShHyperTrainingMainKind.UnsupportedBuild
            or SwShHyperTrainingMainKind.GameMismatch
            or SwShHyperTrainingMainKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }

        var nso = SwShNsoFile.Parse(mainBytes);
        var layout = FindLayout(FormatBuildId(nso.BuildId))
            ?? throw new InvalidDataException("Hyper Training picker supports Sword and Shield 1.3.2 exefs/main files.");
        var text = nso.Text.DecompressedData.ToArray();
        EnsurePatchRange(text, layout);

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

        return nso.Write(textDecompressedData: text);
    }

    public static bool HasInstalledHook(byte[] mainBytes)
    {
        return Analyze(mainBytes).Kind == SwShHyperTrainingMainKind.CustomMinimumLevel;
    }

    public static IReadOnlyList<SwShExeFsReservedRegion> ReservedMainTextRegions()
    {
        return SwShExeFsReservedRegionLedger.MainTextRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerHyperTraining);
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

    private static PatchLayout? FindLayout(string buildId)
    {
        return Layouts.FirstOrDefault(layout =>
            string.Equals(layout.BuildId, buildId, StringComparison.OrdinalIgnoreCase));
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

    private sealed record PatchLayout(
        ProjectGame Game,
        string GameName,
        string BuildId,
        int Shift)
    {
        public int PreflightCompareOffset => ShiftOffset(SwordPreflightCompareOffset);
        public int EligibilityCompareOffset => ShiftOffset(SwordEligibilityCompareOffset);
        public int EligibilityBranchOffset => ShiftOffset(SwordEligibilityBranchOffset);
        public int GrayOutCompareOffset => ShiftOffset(SwordGrayOutCompareOffset);
        public int GrayOutBranchOffset => ShiftOffset(SwordGrayOutBranchOffset);
        public int DetailCompareOffset => ShiftOffset(SwordDetailCompareOffset);
        public int DetailBranchOffset => ShiftOffset(SwordDetailBranchOffset);

        private int ShiftOffset(int offset)
        {
            return offset + Shift;
        }
    }
}
