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
    public const int ShieldRoChartOffset = 0x00743600;
    public const int DependencyLength = 0x40;

    private const string SwordBuildId = "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471";
    private const string ShieldBuildId = "A16802625E7826BF83B6F9708E475B912A9AB7DF";

    private static readonly byte[] DependenciesBeforeChart =
    [
        0xE8, 0x4C, 0x74, 0xFE, 0x0C, 0x4D, 0x74, 0xFE,
        0x08, 0x4D, 0x74, 0xFE, 0x0C, 0x4D, 0x74, 0xFE,
        0x0C, 0x4D, 0x74, 0xFE, 0x0C, 0x4D, 0x74, 0xFE,
        0xF8, 0x4C, 0x74, 0xFE, 0xE0, 0x4D, 0x74, 0xFE,
        0xEC, 0x4D, 0x74, 0xFE, 0xF4, 0x4D, 0x74, 0xFE,
        0xEC, 0x4D, 0x74, 0xFE, 0x08, 0x4E, 0x74, 0xFE,
        0xEC, 0x4D, 0x74, 0xFE, 0xEC, 0x4D, 0x74, 0xFE,
        0xEC, 0x4D, 0x74, 0xFE, 0x00, 0x4E, 0x74, 0xFE,
    ];

    private static readonly byte[] DependenciesAfterChart =
    [
        0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
        0x02, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00,
        0x08, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00,
        0x20, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00,
        0x80, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00,
        0x00, 0x02, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00,
        0x00, 0x08, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00,
        0xF8, 0x5D, 0x74, 0xFE, 0x10, 0x5E, 0x74, 0xFE,
    ];

    private static readonly PatchLayout[] Layouts =
    [
        new(ProjectGame.Sword, "Pokemon Sword 1.3.2", SwordBuildId, SwordRoChartOffset),
        new(ProjectGame.Shield, "Pokemon Shield 1.3.2", ShieldBuildId, ShieldRoChartOffset),
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
                return new SwShTypeChartMainAnalysis(
                    SwShTypeChartMainKind.UnsupportedBuild,
                    "Type Chart supports Sword and Shield 1.3.2 exefs/main files. This build ID is not recognized.",
                    VanillaValues,
                    buildId,
                    "unknown",
                    ChartOffset: null,
                    DetectedGame: null);
            }

            detectedLayout = layout;
            detectedGame = layout.Game;
            var mismatch = CreateGameMismatchAnalysis(layout, expectedGame, buildId);
            if (mismatch is not null)
            {
                return mismatch;
            }

            var ro = nso.Ro.DecompressedData;
            EnsureChartRange(ro, layout);
            ValidateDependencies(ro, layout);
            if (!IsLegalChartAt(ro, layout.ChartOffset))
            {
                return new SwShTypeChartMainAnalysis(
                    SwShTypeChartMainKind.MissingChart,
                    $"Type Chart expected 324 legal effectiveness bytes at {FormatRoOffset(layout.ChartOffset)}.",
                    VanillaValues,
                    buildId,
                    FormatRoOffset(layout.ChartOffset),
                    layout.ChartOffset,
                    layout.Game);
            }

            var values = ReadChart(ro, layout.ChartOffset);
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
                FormatRoOffset(layout.ChartOffset),
                layout.ChartOffset,
                layout.Game);
        }
        catch (InvalidDataException exception)
        {
            return new SwShTypeChartMainAnalysis(
                SwShTypeChartMainKind.Conflict,
                exception.Message,
                VanillaValues,
                buildId,
                detectedLayout is null ? "unknown" : FormatRoOffset(detectedLayout.ChartOffset),
                detectedLayout?.ChartOffset,
                detectedGame);
        }
    }

    public static byte[] ApplyChart(byte[] mainBytes, IReadOnlyList<int> values, ProjectGame? expectedGame = null)
    {
        ArgumentNullException.ThrowIfNull(mainBytes);
        EnsureSupportedExpectedGame(expectedGame);
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
        ValidateRequiredSegmentHashes(nso);
        var layout = FindLayout(nso.BuildId)
            ?? throw new InvalidDataException("Type Chart supports Sword and Shield 1.3.2 exefs/main files.");
        var roBefore = nso.Ro.DecompressedData;
        EnsureChartRange(roBefore, layout);
        ValidateDependencies(roBefore, layout);
        var ro = roBefore.ToArray();
        WriteChart(ro, layout.ChartOffset, values);

        var output = nso.Write(roDecompressedData: ro);
        ValidateOutput(mainBytes, output, layout, values, expectedGame);
        return output;
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
        if (baseAnalysis.Kind != SwShTypeChartMainKind.Vanilla)
        {
            throw new InvalidDataException(
                "Type Chart restore requires a verified selected-game vanilla base exefs/main.");
        }

        var currentNso = NsoFile.Parse(currentMainBytes);
        var baseNso = NsoFile.Parse(baseMainBytes);
        ValidateRequiredSegmentHashes(currentNso);
        ValidateRequiredSegmentHashes(baseNso);
        EnsureSameBuildAndLayout(baseNso, currentNso, "Type Chart restore");
        var layout = FindLayout(baseNso.BuildId)
            ?? throw new InvalidDataException("Type Chart restore requires a supported Sword/Shield 1.3.2 base main.");
        var ro = currentNso.Ro.DecompressedData.ToArray();
        baseNso.Ro.DecompressedData.AsSpan(layout.ChartOffset, ChartLength)
            .CopyTo(ro.AsSpan(layout.ChartOffset, ChartLength));

        var output = currentNso.Write(roDecompressedData: ro);
        ValidateOutput(
            currentMainBytes,
            output,
            layout,
            baseAnalysis.EffectivenessValues,
            expectedGame,
            operation: "Type Chart restore");
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
        EnsureSameBuildAndLayout(baseNso, effectiveNso, "Type Chart apply");
    }

    public static IReadOnlyList<SwShExeFsReservedRegion> ReservedMainRoRegions()
    {
        return SwShExeFsReservedRegionLedger
            .MainRoRegionsForOwner(SwShExeFsReservedRegionLedger.OwnerTypeChart)
            .Where(region => !string.Equals(region.Rule, "requires-vanilla", StringComparison.Ordinal))
            .ToArray();
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

    private static bool IsLegalChartAt(ReadOnlySpan<byte> ro, int offset)
    {
        if (offset < 0 || ChartLength > ro.Length - offset)
        {
            return false;
        }

        foreach (var value in ro.Slice(offset, ChartLength))
        {
            if (!IsLegalEffectiveness(value))
            {
                return false;
            }
        }

        return true;
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
        PatchLayout layout,
        IReadOnlyList<int> expectedValues,
        ProjectGame? expectedGame,
        string operation = "Type Chart apply")
    {
        var before = NsoFile.Parse(input);
        var after = NsoFile.Parse(output);
        ValidateRequiredSegmentHashes(before);
        ValidateRequiredSegmentHashes(after);
        EnsureSameBuildAndLayout(before, after, operation);
        VerifyPreservedSegment(before.Text, after.Text, ".text", operation);
        VerifyPreservedSegment(before.Data, after.Data, ".data", operation);

        var roBefore = before.Ro.DecompressedData;
        var roAfter = after.Ro.DecompressedData;
        if (roBefore.Length != roAfter.Length)
        {
            throw new InvalidDataException("Type Chart patch changed the decompressed .ro segment size.");
        }

        for (var offset = 0; offset < roBefore.Length; offset++)
        {
            var changed = roBefore[offset] != roAfter[offset];
            var insideChart = offset >= layout.ChartOffset && offset < layout.ChartOffset + ChartLength;
            if (changed && !insideChart)
            {
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{operation} unexpectedly changed .ro byte 0x{offset:X}."));
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
                $"Type Chart patching rejected {segment.Name} because its required NSO header hash does not match the decompressed segment.");
        }
    }

    private static void EnsureChartRange(ReadOnlySpan<byte> ro, PatchLayout layout)
    {
        var dependencyBeforeOffset = layout.ChartOffset - DependenciesBeforeChart.Length;
        EnsureRoRange(
            ro,
            dependencyBeforeOffset,
            DependenciesBeforeChart.Length,
            "Type Chart dependencies before the effectiveness table");
        EnsureRoRange(ro, layout.ChartOffset, ChartLength, "Type Chart effectiveness table");
        EnsureRoRange(
            ro,
            layout.ChartOffset + ChartLength,
            DependenciesAfterChart.Length,
            "Type Chart dependencies after the effectiveness table");
    }

    private static void ValidateDependencies(ReadOnlySpan<byte> ro, PatchLayout layout)
    {
        var dependencyBeforeOffset = layout.ChartOffset - DependenciesBeforeChart.Length;
        if (!ro.Slice(dependencyBeforeOffset, DependenciesBeforeChart.Length)
            .SequenceEqual(DependenciesBeforeChart))
        {
            throw new InvalidDataException(
                $"Type Chart dependencies before {FormatRoOffset(layout.ChartOffset)} do not match the supported {layout.GameName} table.");
        }

        var dependencyAfterOffset = layout.ChartOffset + ChartLength;
        if (!ro.Slice(dependencyAfterOffset, DependenciesAfterChart.Length)
            .SequenceEqual(DependenciesAfterChart))
        {
            throw new InvalidDataException(
                $"Type Chart dependencies after {FormatRoOffset(layout.ChartOffset)} do not match the supported {layout.GameName} table.");
        }
    }

    private static void EnsureRoRange(
        ReadOnlySpan<byte> ro,
        int offset,
        int length,
        string label)
    {
        if (offset < 0 || length < 0 || length > ro.Length - offset)
        {
            throw new InvalidDataException($"{label} is outside the decompressed .ro segment.");
        }
    }

    private static bool IsLegalEffectiveness(int value)
    {
        return value is 0 or 2 or 4 or 8;
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

    private static bool IsBlocked(SwShTypeChartMainKind kind)
    {
        return kind is SwShTypeChartMainKind.UnsupportedBuild
            or SwShTypeChartMainKind.GameMismatch
            or SwShTypeChartMainKind.MissingChart
            or SwShTypeChartMainKind.AmbiguousChart
            or SwShTypeChartMainKind.Conflict;
    }

    private static void EnsureSupportedExpectedGame(ProjectGame? expectedGame)
    {
        if (expectedGame is not (ProjectGame.Sword or ProjectGame.Shield))
        {
            throw new InvalidDataException(
                "Type Chart patching requires Pokemon Sword or Pokemon Shield to be selected explicitly.");
        }
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
        int ChartOffset);
}
