// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.Formats.Executable;
using KM.SwSh.ExeFs;
using System.Globalization;

namespace KM.SwSh.TypeChart;

internal enum SwShTypeChartMainKind
{
    Vanilla,
    Modified,
    UnsupportedBuild,
    GameMismatch,
    MissingChart,
    AmbiguousChart,
    Conflict,
}

internal sealed record SwShTypeChartMainAnalysis(
    SwShTypeChartMainKind Kind,
    string Message,
    IReadOnlyList<int> EffectivenessValues,
    string BuildId,
    string ChartOffsetHex,
    int? ChartOffset,
    ProjectGame? DetectedGame);

internal static class SwShTypeChartMainPatcher
{
    public const int TypeCount = 18;
    public const int ChartLength = TypeCount * TypeCount;
    public const int SwordRoChartOffset = 0x00743600;

    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";

    private static readonly byte[] VanillaSignature =
    [
        0x04, 0x04, 0x04, 0x04, 0x04, 0x02, 0x04, 0x00, 0x02,
    ];

    private static readonly PatchLayout[] Layouts =
    [
        new(ProjectGame.Sword, "Pokemon Sword 1.3.2", SwordBuildId, [SwordRoChartOffset]),
        new(ProjectGame.Shield, "Pokemon Shield 1.3.2", ShieldBuildId, [SwordRoChartOffset]),
    ];

    private static readonly int[] VanillaValues =
    [
        4, 4, 4, 4, 4, 2, 4, 0, 2, 4, 4, 4, 4, 4, 4, 4, 4, 4,
        8, 4, 2, 2, 4, 8, 2, 0, 8, 4, 4, 4, 4, 2, 8, 4, 8, 2,
        4, 8, 4, 4, 4, 2, 8, 4, 2, 4, 4, 8, 2, 4, 4, 4, 4, 4,
        4, 4, 4, 2, 2, 2, 4, 2, 0, 4, 4, 8, 4, 4, 4, 4, 4, 8,
        4, 4, 0, 8, 4, 8, 2, 4, 8, 8, 4, 2, 8, 4, 4, 4, 4, 4,
        4, 2, 8, 4, 2, 4, 8, 4, 2, 8, 4, 4, 4, 4, 8, 4, 4, 4,
        4, 2, 2, 2, 4, 4, 4, 2, 2, 2, 4, 8, 4, 8, 4, 4, 8, 2,
        0, 4, 4, 4, 4, 4, 4, 8, 4, 4, 4, 4, 4, 8, 4, 4, 2, 4,
        4, 4, 4, 4, 4, 8, 4, 4, 2, 2, 2, 4, 2, 4, 8, 4, 4, 8,
        4, 4, 4, 4, 4, 2, 8, 4, 8, 2, 2, 8, 4, 4, 8, 2, 4, 4,
        4, 4, 4, 4, 8, 8, 4, 4, 4, 8, 2, 2, 4, 4, 4, 2, 4, 4,
        4, 4, 2, 2, 8, 8, 2, 4, 2, 2, 8, 2, 4, 4, 4, 2, 4, 4,
        4, 4, 8, 4, 0, 4, 4, 4, 4, 4, 8, 2, 2, 4, 4, 2, 4, 4,
        4, 8, 4, 8, 4, 4, 4, 4, 2, 4, 4, 4, 4, 2, 4, 4, 0, 4,
        4, 4, 8, 4, 8, 4, 4, 4, 2, 2, 2, 8, 4, 4, 2, 8, 4, 4,
        4, 4, 4, 4, 4, 4, 4, 4, 2, 4, 4, 4, 4, 4, 4, 8, 4, 0,
        4, 2, 4, 4, 4, 4, 4, 8, 4, 4, 4, 4, 4, 8, 4, 4, 2, 2,
        4, 8, 4, 2, 4, 4, 4, 4, 2, 2, 4, 4, 4, 4, 4, 8, 8, 4,
    ];

    public static IReadOnlyList<int> VanillaChartValues => VanillaValues;

    public static SwShTypeChartMainAnalysis Analyze(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        try
        {
            var nso = NsoFile.Parse(mainBytes);
            var buildId = FormatBuildId(nso.BuildId);
            var layout = FindLayout(buildId);
            if (layout is null)
            {
                return new SwShTypeChartMainAnalysis(
                    SwShTypeChartMainKind.UnsupportedBuild,
                    "Type Chart supports Sword and Shield 1.3.2 exefs/main files. This build ID is not recognized.",
                    VanillaValues,
                    buildId,
                    "unknown",
                    ChartOffset: null,
                    DetectedGame: null);
            }

            var mismatch = CreateGameMismatchAnalysis(layout, expectedGame, buildId);
            if (mismatch is not null)
            {
                return mismatch;
            }

            var chartOffset = FindChartOffset(nso.Ro.DecompressedData, layout);
            if (chartOffset is null)
            {
                return new SwShTypeChartMainAnalysis(
                    SwShTypeChartMainKind.MissingChart,
                    "Type Chart could not find a unique legal 18x18 effectiveness table in exefs/main.",
                    VanillaValues,
                    buildId,
                    "unknown",
                    ChartOffset: null,
                    layout.Game);
            }

            var values = ReadChart(nso.Ro.DecompressedData, chartOffset.Value);
            var kind = values.SequenceEqual(VanillaValues)
                ? SwShTypeChartMainKind.Vanilla
                : SwShTypeChartMainKind.Modified;
            var message = kind == SwShTypeChartMainKind.Vanilla
                ? "Type Chart matches the base Sword/Shield effectiveness table."
                : "Type Chart contains custom effectiveness values.";

            return new SwShTypeChartMainAnalysis(
                kind,
                message,
                values,
                buildId,
                FormatRoOffset(chartOffset.Value),
                chartOffset.Value,
                layout.Game);
        }
        catch (InvalidDataException exception)
        {
            return new SwShTypeChartMainAnalysis(
                SwShTypeChartMainKind.Conflict,
                exception.Message,
                VanillaValues,
                "unknown",
                "unknown",
                ChartOffset: null,
                DetectedGame: null);
        }
    }

    public static byte[] ApplyChart(byte[] mainBytes, IReadOnlyList<int> values, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);
        ValidateValues(values);

        var analysis = Analyze(mainBytes, expectedGame);
        if (analysis.Kind is SwShTypeChartMainKind.UnsupportedBuild
            or SwShTypeChartMainKind.GameMismatch
            or SwShTypeChartMainKind.MissingChart
            or SwShTypeChartMainKind.AmbiguousChart
            or SwShTypeChartMainKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }

        if (analysis.ChartOffset is null)
        {
            throw new InvalidDataException("Type Chart could not resolve the exefs/main .ro chart offset.");
        }

        var nso = NsoFile.Parse(mainBytes);
        var roBefore = nso.Ro.DecompressedData;
        var ro = roBefore.ToArray();
        WriteChart(ro, analysis.ChartOffset.Value, values);

        var output = nso.Write(roDecompressedData: ro);
        ValidateOutput(mainBytes, output, analysis.ChartOffset.Value, values, expectedGame);
        return output;
    }

    public static IReadOnlyList<SwShExeFsReservedRegion> ReservedMainRoRegions()
    {
        return SwShExeFsReservedRegionLedger.MainRoRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerTypeChart);
    }

    public static void ValidateValues(IReadOnlyList<int> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count != ChartLength)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Type Chart requires exactly {ChartLength} effectiveness values."));
        }

        for (var i = 0; i < values.Count; i++)
        {
            if (!IsLegalEffectiveness(values[i]))
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Type Chart value at index {i} must be 0, 2, 4, or 8."));
            }
        }
    }

    private static int? FindChartOffset(byte[] ro, PatchLayout layout)
    {
        foreach (var offset in layout.CandidateOffsets)
        {
            if (IsLegalChartAt(ro, offset))
            {
                return offset;
            }
        }

        var matches = FindSignatureMatches(ro)
            .Where(offset => IsLegalChartAt(ro, offset))
            .Distinct()
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => null,
            _ => throw new InvalidDataException("Type Chart found multiple legal 18x18 effectiveness tables in exefs/main .ro and will not guess which one to patch."),
        };
    }

    private static IEnumerable<int> FindSignatureMatches(byte[] ro)
    {
        for (var offset = 0; offset <= ro.Length - VanillaSignature.Length; offset++)
        {
            if (ro.AsSpan(offset, VanillaSignature.Length).SequenceEqual(VanillaSignature))
            {
                yield return offset;
            }
        }
    }

    private static bool IsLegalChartAt(byte[] ro, int offset)
    {
        if (offset < 0 || offset + ChartLength > ro.Length)
        {
            return false;
        }

        return ro.AsSpan(offset, ChartLength).ToArray().All(value => IsLegalEffectiveness(value));
    }

    private static int[] ReadChart(byte[] ro, int offset)
    {
        if (!IsLegalChartAt(ro, offset))
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Type Chart expected 324 legal effectiveness bytes at {FormatRoOffset(offset)}."));
        }

        return ro.AsSpan(offset, ChartLength).ToArray().Select(value => (int)value).ToArray();
    }

    private static void WriteChart(byte[] ro, int offset, IReadOnlyList<int> values)
    {
        if (offset < 0 || offset + ChartLength > ro.Length)
        {
            throw new InvalidDataException("Type Chart target range is outside the decompressed .ro segment.");
        }

        for (var i = 0; i < values.Count; i++)
        {
            ro[offset + i] = checked((byte)values[i]);
        }
    }

    private static void ValidateOutput(
        byte[] input,
        byte[] output,
        int chartOffset,
        IReadOnlyList<int> expectedValues,
        ProjectGame? expectedGame)
    {
        var before = NsoFile.Parse(input);
        var after = NsoFile.Parse(output);
        if (!before.BuildId.SequenceEqual(after.BuildId))
        {
            throw new InvalidDataException("Type Chart patch changed the NSO build ID.");
        }

        if (!before.Text.DecompressedData.SequenceEqual(after.Text.DecompressedData))
        {
            throw new InvalidDataException("Type Chart patch unexpectedly changed the .text segment.");
        }

        if (!before.Data.DecompressedData.SequenceEqual(after.Data.DecompressedData))
        {
            throw new InvalidDataException("Type Chart patch unexpectedly changed the .data segment.");
        }

        var roBefore = before.Ro.DecompressedData;
        var roAfter = after.Ro.DecompressedData;
        if (roBefore.Length != roAfter.Length)
        {
            throw new InvalidDataException("Type Chart patch changed the decompressed .ro segment size.");
        }

        for (var offset = 0; offset < roBefore.Length; offset++)
        {
            var changed = roBefore[offset] != roAfter[offset];
            var insideChart = offset >= chartOffset && offset < chartOffset + ChartLength;
            if (changed && !insideChart)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Type Chart patch unexpectedly changed .ro byte 0x{offset:X}."));
            }
        }

        var analysis = Analyze(output, expectedGame);
        if (analysis.Kind is SwShTypeChartMainKind.UnsupportedBuild
            or SwShTypeChartMainKind.GameMismatch
            or SwShTypeChartMainKind.MissingChart
            or SwShTypeChartMainKind.AmbiguousChart
            or SwShTypeChartMainKind.Conflict
            || !analysis.EffectivenessValues.SequenceEqual(expectedValues))
        {
            throw new InvalidDataException("Type Chart patch verification failed after writing exefs/main.");
        }
    }

    private static bool IsLegalEffectiveness(int value)
    {
        return value is 0 or 2 or 4 or 8;
    }

    private static PatchLayout? FindLayout(string buildId)
    {
        return Layouts.FirstOrDefault(layout =>
            string.Equals(layout.BuildId, buildId, StringComparison.OrdinalIgnoreCase));
    }

    private static SwShTypeChartMainAnalysis? CreateGameMismatchAnalysis(
        PatchLayout layout,
        ProjectGame? expectedGame,
        string buildId)
    {
        if (expectedGame is null || layout.Game == expectedGame.Value)
        {
            return null;
        }

        return new SwShTypeChartMainAnalysis(
            SwShTypeChartMainKind.GameMismatch,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Selected {FormatGame(expectedGame.Value)}, but exefs/main build ID is {layout.GameName}. Type Chart will not patch a different game's executable."),
            VanillaValues,
            buildId,
            "unknown",
            ChartOffset: null,
            layout.Game);
    }

    private static string FormatBuildId(byte[] buildId)
    {
        var buildIdLength = Math.Min(20, buildId.Length);
        return Convert.ToHexString(buildId.AsSpan(0, buildIdLength));
    }

    private static string FormatRoOffset(int offset)
    {
        return string.Create(CultureInfo.InvariantCulture, $"main.ro+0x{offset:X8}");
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
        IReadOnlyList<int> CandidateOffsets);
}
