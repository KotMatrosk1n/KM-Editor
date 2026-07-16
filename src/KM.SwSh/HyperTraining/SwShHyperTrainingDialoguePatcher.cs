// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using System.Globalization;

namespace KM.SwSh.HyperTraining;

internal enum SwShHyperTrainingDialogueKind
{
    NotInstalled,
    CustomMinimumLevel,
    Conflict,
}

internal sealed record SwShHyperTrainingDialogueAnalysis(
    SwShHyperTrainingDialogueKind Kind,
    string Message,
    int MinimumLevel);

internal static class SwShHyperTrainingDialoguePatcher
{
    public const int IntroLineIndex = 0;
    public const int LevelFailureLineIndex = 3;

    private const string HyperTrainingAnchor = "Hyper Training";
    private const string LevelPrefix = "Lv.";

    public static SwShHyperTrainingDialogueAnalysis Analyze(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            var textFile = SwShGameTextFile.Parse(data);
            var minimumLevel = ReadSharedMinimumLevel(textFile.Lines);
            var kind = minimumLevel == SwShHyperTrainingAmxPatcher.VanillaMinimumLevel
                ? SwShHyperTrainingDialogueKind.NotInstalled
                : SwShHyperTrainingDialogueKind.CustomMinimumLevel;
            var message = kind == SwShHyperTrainingDialogueKind.NotInstalled
                ? "English Hyper Training dialogue uses the vanilla Lv.100 cutoff."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"English Hyper Training dialogue currently mentions Lv.{minimumLevel}.");
            return new SwShHyperTrainingDialogueAnalysis(kind, message, minimumLevel);
        }
        catch (Exception exception) when (exception is InvalidDataException
            or ArgumentException
            or OverflowException)
        {
            return new SwShHyperTrainingDialogueAnalysis(
                SwShHyperTrainingDialogueKind.Conflict,
                exception.Message,
                SwShHyperTrainingAmxPatcher.VanillaMinimumLevel);
        }
    }

    public static byte[] ApplyMinimumLevel(byte[] data, int minimumLevel)
    {
        ArgumentNullException.ThrowIfNull(data);
        ValidateLevel(minimumLevel);

        var textFile = SwShGameTextFile.Parse(data);
        _ = ReadSharedMinimumLevel(textFile.Lines);
        var lines = textFile.Lines.ToArray();
        lines[IntroLineIndex] = lines[IntroLineIndex] with
        {
            Text = ReplaceMinimumLevel(lines[IntroLineIndex].Text, IntroLineIndex, minimumLevel),
        };
        lines[LevelFailureLineIndex] = lines[LevelFailureLineIndex] with
        {
            Text = ReplaceMinimumLevel(lines[LevelFailureLineIndex].Text, LevelFailureLineIndex, minimumLevel),
        };

        var output = textFile.WritePreserving(lines);
        VerifyOutput(textFile.Lines, output, minimumLevel);
        return output;
    }

    private static int ReadSharedMinimumLevel(IReadOnlyList<SwShGameTextLine> lines)
    {
        if (lines.Count <= LevelFailureLineIndex)
        {
            throw new InvalidDataException(
                "Hyper Training dialogue table does not contain the expected intro and level failure lines.");
        }

        var introLevel = FindMinimumLevelToken(lines[IntroLineIndex].Text, IntroLineIndex).Level;
        var failureLevel = FindMinimumLevelToken(
            lines[LevelFailureLineIndex].Text,
            LevelFailureLineIndex).Level;
        if (introLevel != failureLevel)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hyper Training dialogue level lines disagree: intro Lv.{introLevel}, failure Lv.{failureLevel}."));
        }

        ValidateLevelShape(introLevel);
        return introLevel;
    }

    private static string ReplaceMinimumLevel(string text, int lineIndex, int minimumLevel)
    {
        var token = FindMinimumLevelToken(text, lineIndex);
        return string.Concat(
            text.AsSpan(0, token.DigitStart),
            minimumLevel.ToString(CultureInfo.InvariantCulture),
            text.AsSpan(token.DigitStart + token.DigitLength));
    }

    private static LevelToken FindMinimumLevelToken(string text, int lineIndex)
    {
        if (!text.Contains(HyperTrainingAnchor, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Hyper Training dialogue line {lineIndex} does not contain the expected Hyper Training anchor.");
        }

        var prefixIndex = text.IndexOf(LevelPrefix, StringComparison.Ordinal);
        if (prefixIndex < 0
            || text.IndexOf(LevelPrefix, prefixIndex + LevelPrefix.Length, StringComparison.Ordinal) >= 0)
        {
            throw new InvalidDataException(
                $"Hyper Training dialogue line {lineIndex} must contain exactly one Lv. cutoff token.");
        }

        var digitStart = prefixIndex + LevelPrefix.Length;
        while (digitStart < text.Length && char.IsWhiteSpace(text[digitStart]))
        {
            digitStart++;
        }

        var digitEnd = digitStart;
        while (digitEnd < text.Length && char.IsAsciiDigit(text[digitEnd]))
        {
            digitEnd++;
        }

        if (digitStart == digitEnd
            || !int.TryParse(
                text.AsSpan(digitStart, digitEnd - digitStart),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var level))
        {
            throw new InvalidDataException(
                $"Hyper Training dialogue line {lineIndex} has no valid numeric Lv. cutoff.");
        }

        ValidateLevelShape(level);
        return new LevelToken(digitStart, digitEnd - digitStart, level);
    }

    private static void VerifyOutput(
        IReadOnlyList<SwShGameTextLine> sourceLines,
        byte[] output,
        int expectedMinimumLevel)
    {
        var outputFile = SwShGameTextFile.Parse(output);
        if (outputFile.Lines.Count != sourceLines.Count)
        {
            throw new InvalidDataException("Hyper Training dialogue output changed the line count.");
        }

        for (var index = 0; index < sourceLines.Count; index++)
        {
            var source = sourceLines[index];
            var actual = outputFile.Lines[index];
            if (source.Flags != actual.Flags)
            {
                throw new InvalidDataException($"Hyper Training dialogue output changed flags for line {index}.");
            }

            if (index is not (IntroLineIndex or LevelFailureLineIndex)
                && !string.Equals(source.Text, actual.Text, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Hyper Training dialogue output changed unowned line {index}.");
            }
        }

        var analysis = Analyze(output);
        if (analysis.Kind == SwShHyperTrainingDialogueKind.Conflict
            || analysis.MinimumLevel != expectedMinimumLevel)
        {
            throw new InvalidDataException(
                "Hyper Training dialogue output did not round-trip with the requested minimum level.");
        }
    }

    private static void ValidateLevel(int minimumLevel)
    {
        if (minimumLevel is < SwShHyperTrainingAmxPatcher.MinimumAllowedLevel
            or > SwShHyperTrainingAmxPatcher.MaximumAllowedLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumLevel),
                minimumLevel,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hyper Training minimum level must be between {SwShHyperTrainingAmxPatcher.MinimumAllowedLevel} and {SwShHyperTrainingAmxPatcher.MaximumAllowedLevel}."));
        }
    }

    private static void ValidateLevelShape(int minimumLevel)
    {
        if (minimumLevel is < SwShHyperTrainingAmxPatcher.MinimumAllowedLevel
            or > SwShHyperTrainingAmxPatcher.MaximumAllowedLevel)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hyper Training dialogue level {minimumLevel} is outside the supported {SwShHyperTrainingAmxPatcher.MinimumAllowedLevel}-{SwShHyperTrainingAmxPatcher.MaximumAllowedLevel} range."));
        }
    }

    private readonly record struct LevelToken(int DigitStart, int DigitLength, int Level);
}
