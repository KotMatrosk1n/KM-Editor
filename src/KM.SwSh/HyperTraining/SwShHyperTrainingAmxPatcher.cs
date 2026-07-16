// SPDX-License-Identifier: GPL-3.0-only

using KM.SwSh.Scripts;
using System.Globalization;

namespace KM.SwSh.HyperTraining;

internal enum SwShHyperTrainingScriptKind
{
    NotInstalled,
    CustomMinimumLevel,
    Conflict,
}

internal sealed record SwShHyperTrainingScriptAnalysis(
    SwShHyperTrainingScriptKind Kind,
    string Message,
    int MinimumLevel,
    string ScriptCell);

internal static class SwShHyperTrainingAmxPatcher
{
    public const int VanillaMinimumLevel = 100;
    public const int MinimumAllowedLevel = 1;
    public const int MaximumAllowedLevel = 100;
    public const int LevelThresholdCell = 2294;
    public const string LevelThresholdCellLabel = "AMX code cell 2294 (RND_TO_FLOOR operand)";

    private const int OpRndToFloor = 172;
    private const int OpJsgeq = 64;
    private const int OpFloatGt = 176;

    public static SwShHyperTrainingScriptAnalysis Analyze(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            var level = ReadMinimumLevel(data);
            var kind = level == VanillaMinimumLevel
                ? SwShHyperTrainingScriptKind.NotInstalled
                : SwShHyperTrainingScriptKind.CustomMinimumLevel;
            var message = kind == SwShHyperTrainingScriptKind.NotInstalled
                ? "Hyper Training is using the vanilla Lv.100 minimum."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hyper Training currently accepts Pokemon at Lv.{level} or higher.");

            return new SwShHyperTrainingScriptAnalysis(
                kind,
                message,
                level,
                LevelThresholdCellLabel);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentOutOfRangeException)
        {
            return new SwShHyperTrainingScriptAnalysis(
                SwShHyperTrainingScriptKind.Conflict,
                exception.Message,
                VanillaMinimumLevel,
                LevelThresholdCellLabel);
        }
    }

    public static byte[] ApplyMinimumLevel(byte[] data, int minimumLevel)
    {
        ArgumentNullException.ThrowIfNull(data);
        ValidateLevel(minimumLevel);
        _ = ReadMinimumLevel(data);

        var patched = SwShAmxCellPatcher.ApplyPackedInstructionOperand(
            data,
            LevelThresholdCell,
            OpRndToFloor,
            minimumLevel);
        var roundTripLevel = ReadMinimumLevel(patched);
        if (roundTripLevel != minimumLevel)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hyper Training AMX level round-tripped as {roundTripLevel} instead of {minimumLevel}."));
        }

        return patched;
    }

    public static int ReadMinimumLevel(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var reader = SwShAmxCellPatcher.OpenCodeCellReader(data);
        if (!reader.TryReadPackedInstructionOperand(
                LevelThresholdCell,
                OpRndToFloor,
                out var minimumLevel))
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hyper Training level threshold cell {LevelThresholdCell} is missing or is not packed RND_TO_FLOOR <level>."));
        }

        ValidateLevelShape(minimumLevel);
        ExpectCell(reader, LevelThresholdCell + 1, OpJsgeq, "Hyper Training level comparison jump");
        ExpectCell(reader, LevelThresholdCell + 2, OpFloatGt, "Hyper Training level failure branch");
        return minimumLevel;
    }

    private static void ExpectCell(
        SwShAmxCellPatcher.SwShAmxCodeCellReader reader,
        int index,
        int expected,
        string label)
    {
        if (!reader.TryReadInt(index, out var actual, out _) || actual != expected)
        {
            throw new InvalidDataException(
                $"{label} cell {index} is missing or has an unexpected value; expected {expected}.");
        }
    }

    private static void ValidateLevel(int minimumLevel)
    {
        if (minimumLevel is < MinimumAllowedLevel or > MaximumAllowedLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumLevel),
                minimumLevel,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hyper Training minimum level must be between {MinimumAllowedLevel} and {MaximumAllowedLevel}."));
        }
    }

    private static void ValidateLevelShape(int minimumLevel)
    {
        if (minimumLevel is < MinimumAllowedLevel or > MaximumAllowedLevel)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hyper Training minimum level {minimumLevel} is outside the supported {MinimumAllowedLevel}-{MaximumAllowedLevel} range."));
        }
    }
}
