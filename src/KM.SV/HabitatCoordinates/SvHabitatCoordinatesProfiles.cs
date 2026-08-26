// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.SV.Habitat;
using System.Buffers.Binary;

namespace KM.SV.HabitatCoordinates;

internal sealed record SvHabitatRegionProfile(
    string Region,
    string Label,
    string SourceFile,
    string ExactBaseSha256);

internal static class SvHabitatCoordinatesProfiles
{
    public const string SupportedBuildLabel = "Scarlet/Violet 4.0.0";
    public const int MaximumQueryLimit = 200;
    public const int DefaultQueryLimit = 50;
    public const int MaximumSearchLength = 80;

    private const string ScarletBuildId =
        "421C5411B487EB4D049DD065FEC9547773E8E598000000000000000000000000";
    private const string VioletBuildId =
        "709BFD66115298640155FCC4979DBA151C7CC79A000000000000000000000000";
    private const long MaximumMainBytes = 512L * 1024L * 1024L;

    public static IReadOnlyList<SvHabitatRegionProfile> Regions { get; } =
    [
        new(
            "paldea",
            "Paldea",
            "world/data/ui/pokedex/distribution_data/distribution_data_array.bin",
            "ABAD4F870D02DFC7C2550AEA01565925E3D12B8F2189B55D5BEFD7D87C03BD4C"),
        new(
            "kitakami",
            "Kitakami",
            "world/data/ui/pokedex/distribution_data_dlc1/distribution_data_dlc1_array.bin",
            "1BB9ACBA17EC8AE1778F22D031AEB57CCFD8BCC9BF23D5FC28C836F22A00BA00"),
        new(
            "blueberry",
            "Blueberry",
            "world/data/ui/pokedex/distribution_data_dlc2/distribution_data_dlc2_array.bin",
            "C4CF412A2DF05F5020B4E00DD90C8A33A5DDEA15196D97E2A13E32B29E431BBD"),
    ];

    public static SvHabitatRegionProfile ResolveRegion(string region)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        return Regions.FirstOrDefault(profile => string.Equals(
                profile.Region,
                region,
                StringComparison.Ordinal))
            ?? throw new ArgumentException("The habitat region is not supported.", nameof(region));
    }

    public static SvHabitatBuildGateResult InspectBuild(ProjectPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.SelectedGame is not (ProjectGame.Scarlet or ProjectGame.Violet))
        {
            return new SvHabitatBuildGateResult(
                false,
                "unknown",
                "Habitat Coordinates requires a Pokemon Scarlet or Pokemon Violet project.");
        }

        if (string.IsNullOrWhiteSpace(paths.BaseExeFsPath))
        {
            return new SvHabitatBuildGateResult(
                false,
                "unknown",
                "Habitat Coordinates requires the exact base exefs/main build identity.");
        }

        try
        {
            var mainPath = Path.Combine(paths.BaseExeFsPath, "main");
            using var stream = new FileStream(
                mainPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4_096,
                FileOptions.SequentialScan);
            if (stream.Length is < 0x100 or > MaximumMainBytes)
            {
                return new SvHabitatBuildGateResult(
                    false,
                    "unknown",
                    "The base exefs/main file is outside the supported bounded NSO size.");
            }

            var initialLength = stream.Length;
            Span<byte> header = stackalloc byte[0x60];
            stream.ReadExactly(header);
            if (stream.Length != initialLength)
            {
                return new SvHabitatBuildGateResult(
                    false,
                    "unknown",
                    "The base exefs/main file changed while its build identity was inspected.");
            }

            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x304F534E)
            {
                return new SvHabitatBuildGateResult(false, "unknown", "The base exefs/main file is not an NSO0 image.");
            }

            var buildId = Convert.ToHexString(header[0x40..0x60]);
            var expected = paths.SelectedGame == ProjectGame.Scarlet
                ? ScarletBuildId
                : VioletBuildId;
            return string.Equals(buildId, expected, StringComparison.Ordinal)
                ? new SvHabitatBuildGateResult(true, buildId, "The exact Scarlet/Violet 4.0.0 build is verified.")
                : new SvHabitatBuildGateResult(
                    false,
                    buildId,
                    "The base exefs/main build ID is not the exact supported Scarlet/Violet 4.0.0 profile.");
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException)
        {
            return new SvHabitatBuildGateResult(
                false,
                "unknown",
                $"The base exefs/main build identity could not be inspected: {exception.Message}");
        }
    }

    public static SvHabitatCoordinatesQuery NormalizeQuery(SvHabitatCoordinatesQuery? query)
    {
        query ??= new SvHabitatCoordinatesQuery(
            Regions[0].Region,
            string.Empty,
            Offset: 0,
            Limit: DefaultQueryLimit);
        _ = ResolveRegion(query.Region);
        var search = query.Search?.Trim() ?? string.Empty;
        if (search.Length > MaximumSearchLength || search.Any(char.IsControl))
        {
            throw new ArgumentException("The habitat search is outside its bounded text limit.", nameof(query));
        }

        if (query.Offset is < 0 or > SvHabitatDistributionDocument.MaximumRowCount
            || query.Limit is < 1 or > MaximumQueryLimit)
        {
            throw new ArgumentException("The habitat page range is outside its bounded limit.", nameof(query));
        }

        return query with { Search = search };
    }
}
