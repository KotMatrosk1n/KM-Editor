// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.Executable;
using KM.SV.ExeFs;
using System.Globalization;

namespace KM.SV.TypeChart;

internal enum SvTypeChartMainKind
{
    Vanilla,
    Modified,
    UnsupportedBuild,
    GameMismatch,
    MissingChart,
    AmbiguousChart,
    Conflict,
}

internal sealed record SvTypeChartMainAnalysis(
    SvTypeChartMainKind Kind,
    string Message,
    IReadOnlyList<int> EffectivenessValues,
    string BuildId,
    string ChartOffsetHex,
    int? ChartOffset,
    ProjectGame? DetectedGame);

internal static class SvTypeChartMainPatcher
{
    public const int TypeCount = 18;
    public const int ChartLength = TypeCount * TypeCount;
    public const int RoChartOffset = 0x0082286C;

    private const string ScarletBuildId = "421C5411B487EB4D049DD065FEC9547773E8E598";
    private const string VioletBuildId = "709BFD66115298640155FCC4979DBA151C7CC79A";

    private static readonly byte[] VanillaSignature =
    [
        0x04, 0x04, 0x04, 0x04, 0x04, 0x02, 0x04, 0x00, 0x02,
    ];

    private static readonly PatchLayout[] Layouts =
    [
        new(ProjectGame.Scarlet, "Pokemon Scarlet 4.0.0", ScarletBuildId, [RoChartOffset]),
        new(ProjectGame.Violet, "Pokemon Violet 4.0.0", VioletBuildId, [RoChartOffset]),
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

    public static SvTypeChartMainAnalysis Analyze(byte[] mainBytes, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);

        try
        {
            var nso = NsoFile.Parse(mainBytes);
            var buildId = FormatBuildId(nso.BuildId);
            var layout = FindLayout(buildId);
            if (layout is null)
            {
                return new SvTypeChartMainAnalysis(
                    SvTypeChartMainKind.UnsupportedBuild,
                    "Type Chart supports verified Scarlet and Violet exefs/main builds only. This build ID is not recognized.",
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
                return new SvTypeChartMainAnalysis(
                    SvTypeChartMainKind.MissingChart,
                    "Type Chart could not find a unique legal 18x18 effectiveness table in exefs/main.",
                    VanillaValues,
                    buildId,
                    "unknown",
                    ChartOffset: null,
                    layout.Game);
            }

            var values = ReadChart(nso.Ro.DecompressedData, chartOffset.Value);
            var kind = values.SequenceEqual(VanillaValues)
                ? SvTypeChartMainKind.Vanilla
                : SvTypeChartMainKind.Modified;
            var message = kind == SvTypeChartMainKind.Vanilla
                ? "Type Chart matches the base Scarlet/Violet effectiveness table."
                : "Type Chart contains custom effectiveness values.";

            return new SvTypeChartMainAnalysis(
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
            return new SvTypeChartMainAnalysis(
                SvTypeChartMainKind.Conflict,
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
        if (analysis.Kind is SvTypeChartMainKind.UnsupportedBuild
            or SvTypeChartMainKind.GameMismatch
            or SvTypeChartMainKind.MissingChart
            or SvTypeChartMainKind.AmbiguousChart
            or SvTypeChartMainKind.Conflict)
        {
            throw new InvalidDataException(analysis.Message);
        }

        if (analysis.ChartOffset is null)
        {
            throw new InvalidDataException("Type Chart could not resolve the exefs/main .ro chart offset.");
        }

        var nso = NsoFile.Parse(mainBytes);
        var ro = nso.Ro.DecompressedData.ToArray();
        WriteChart(ro, analysis.ChartOffset.Value, values);

        var output = nso.Write(roDecompressedData: ro);
        ValidateOutput(mainBytes, output, analysis.ChartOffset.Value, values, expectedGame);
        return output;
    }

    public static byte[] RestoreFromBase(
        byte[] currentMainBytes,
        byte[] baseMainBytes,
        ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(currentMainBytes);
        ArgumentNullException.ThrowIfNull(baseMainBytes);

        var currentAnalysis = Analyze(currentMainBytes, expectedGame);
        if (currentAnalysis.Kind == SvTypeChartMainKind.GameMismatch)
        {
            throw new InvalidDataException(currentAnalysis.Message);
        }

        if (currentAnalysis.Kind != SvTypeChartMainKind.Modified)
        {
            throw new InvalidDataException("Type Chart restore requires modified effectiveness values in the generated exefs/main.");
        }

        if (currentAnalysis.ChartOffset is null)
        {
            throw new InvalidDataException("Type Chart restore could not resolve the generated exefs/main .ro chart offset.");
        }

        var currentNso = NsoFile.Parse(currentMainBytes);
        var baseNso = NsoFile.Parse(baseMainBytes);
        var currentBuildId = FormatBuildId(currentNso.BuildId);
        var baseBuildId = FormatBuildId(baseNso.BuildId);
        if (!string.Equals(currentBuildId, baseBuildId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Type Chart restore requires current and base main NSO files with the same build ID.");
        }

        var layout = FindLayout(baseBuildId)
            ?? throw new InvalidDataException("Type Chart restore requires a supported Scarlet or Violet base main NSO.");
        var baseMismatch = CreateGameMismatchAnalysis(layout, expectedGame, baseBuildId);
        if (baseMismatch is not null)
        {
            throw new InvalidDataException(baseMismatch.Message);
        }

        var baseAnalysis = Analyze(baseMainBytes, expectedGame);
        if (baseAnalysis.Kind is SvTypeChartMainKind.UnsupportedBuild
            or SvTypeChartMainKind.GameMismatch
            or SvTypeChartMainKind.MissingChart
            or SvTypeChartMainKind.AmbiguousChart
            or SvTypeChartMainKind.Conflict
            || baseAnalysis.ChartOffset is null)
        {
            throw new InvalidDataException("Type Chart restore requires a base exefs/main with one legal 18x18 effectiveness table.");
        }

        var currentRo = currentNso.Ro.DecompressedData.ToArray();
        var baseRo = baseNso.Ro.DecompressedData;
        if (currentRo.Length != baseRo.Length)
        {
            throw new InvalidDataException("Type Chart restore requires current and base main NSO files with matching .ro sizes.");
        }

        var baseValues = ReadChart(baseRo, baseAnalysis.ChartOffset.Value);
        baseRo
            .AsSpan(baseAnalysis.ChartOffset.Value, ChartLength)
            .CopyTo(currentRo.AsSpan(currentAnalysis.ChartOffset.Value, ChartLength));

        var output = currentNso.Write(roDecompressedData: currentRo);
        ValidateRestoreOutput(
            currentMainBytes,
            output,
            currentAnalysis.ChartOffset.Value,
            baseValues,
            expectedGame);
        return output;
    }

    public static IReadOnlyList<SvExeFsReservedRegion> ReservedMainRoRegions()
    {
        return SvExeFsReservedRegionLedger.MainRoRegionsForOwner(SvExeFsReservedRegionLedger.OwnerTypeChart);
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

        for (var index = 0; index < values.Count; index++)
        {
            if (!IsLegalEffectiveness(values[index]))
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Type Chart value at index {index} must be 0, 2, 4, or 8."));
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

        for (var index = 0; index < values.Count; index++)
        {
            ro[offset + index] = checked((byte)values[index]);
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
            var owned = offset >= chartOffset && offset < chartOffset + ChartLength;
            if (changed && !owned)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Type Chart patch unexpectedly changed .ro byte 0x{offset:X}."));
            }
        }

        var analysis = Analyze(output, expectedGame);
        if (analysis.Kind is SvTypeChartMainKind.UnsupportedBuild
            or SvTypeChartMainKind.GameMismatch
            or SvTypeChartMainKind.MissingChart
            or SvTypeChartMainKind.AmbiguousChart
            or SvTypeChartMainKind.Conflict
            || !analysis.EffectivenessValues.SequenceEqual(expectedValues))
        {
            throw new InvalidDataException("Type Chart patch verification failed after writing exefs/main.");
        }
    }

    private static void ValidateRestoreOutput(
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
            throw new InvalidDataException("Type Chart restore changed the NSO build ID.");
        }

        if (!before.Text.DecompressedData.SequenceEqual(after.Text.DecompressedData))
        {
            throw new InvalidDataException("Type Chart restore unexpectedly changed the .text segment.");
        }

        if (!before.Data.DecompressedData.SequenceEqual(after.Data.DecompressedData))
        {
            throw new InvalidDataException("Type Chart restore unexpectedly changed the .data segment.");
        }

        var roBefore = before.Ro.DecompressedData;
        var roAfter = after.Ro.DecompressedData;
        if (roBefore.Length != roAfter.Length)
        {
            throw new InvalidDataException("Type Chart restore changed the decompressed .ro segment size.");
        }

        for (var offset = 0; offset < roBefore.Length; offset++)
        {
            var changed = roBefore[offset] != roAfter[offset];
            var owned = offset >= chartOffset && offset < chartOffset + ChartLength;
            if (changed && !owned)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Type Chart restore unexpectedly changed .ro byte 0x{offset:X}."));
            }
        }

        var analysis = Analyze(output, expectedGame);
        if (analysis.Kind is SvTypeChartMainKind.UnsupportedBuild
            or SvTypeChartMainKind.GameMismatch
            or SvTypeChartMainKind.MissingChart
            or SvTypeChartMainKind.AmbiguousChart
            or SvTypeChartMainKind.Conflict
            || !analysis.EffectivenessValues.SequenceEqual(expectedValues))
        {
            throw new InvalidDataException("Type Chart restore verification failed after writing exefs/main.");
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

    private static SvTypeChartMainAnalysis? CreateGameMismatchAnalysis(
        PatchLayout layout,
        ProjectGame? expectedGame,
        string buildId)
    {
        if (expectedGame is null || layout.Game == expectedGame.Value)
        {
            return null;
        }

        return new SvTypeChartMainAnalysis(
            SvTypeChartMainKind.GameMismatch,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Selected {FormatGame(expectedGame.Value)}, but exefs/main build ID is {layout.GameName}. Type Chart will not patch this file because Scarlet and Violet use separate verified build IDs."),
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
        return ProjectGameMetadata.Get(game).DisplayName;
    }

    private sealed record PatchLayout(
        ProjectGame Game,
        string GameName,
        string BuildId,
        IReadOnlyList<int> CandidateOffsets);
}
