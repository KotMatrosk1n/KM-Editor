// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;

namespace KM.ZA.ExeFs;

internal sealed record ZaExeFsReservedRegion(
    string Owner,
    string FeatureId,
    string RelativePath,
    string Area,
    int? StartOffset,
    int? Length,
    string Label,
    string Rule)
{
    public int? EndExclusive => StartOffset is null || Length is null
        ? null
        : StartOffset.Value + Length.Value;

    public string OffsetLabel => StartOffset is null || Length is null
        ? "whole-file"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{FormatArea(Area)}+0x{StartOffset.Value:X}..0x{StartOffset.Value + Length.Value - 1:X}");

    private static string FormatArea(string area)
    {
        const string mainPrefix = "main.";
        return area.StartsWith(mainPrefix, StringComparison.Ordinal)
            ? area[mainPrefix.Length..]
            : area;
    }
}

internal static class ZaExeFsReservedRegionLedger
{
    public const string OwnerTypeChart = "Type Chart";
    public const string ExeFsMainPath = "exefs/main";

    private static readonly ZaExeFsReservedRegion[] regions =
    [
        new(
            OwnerTypeChart,
            "type-chart-za",
            ExeFsMainPath,
            "main.ro",
            0x0019F2A4,
            0x144,
            "Pokemon Legends Z-A type-effectiveness table",
            "do-not-overwrite"),
    ];

    public static IReadOnlyList<ZaExeFsReservedRegion> Regions => regions;

    public static IReadOnlyList<ZaExeFsReservedRegion> MainTextRegionsForOwner(string owner)
    {
        return regions
            .Where(region => string.Equals(region.RelativePath, ExeFsMainPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(region.Area, "main.text", StringComparison.Ordinal)
                && string.Equals(region.Owner, owner, StringComparison.Ordinal)
                && region.StartOffset is not null
                && region.Length is not null)
            .ToArray();
    }

    public static IReadOnlyList<ZaExeFsReservedRegion> MainRoRegionsForOwner(string owner)
    {
        return regions
            .Where(region => string.Equals(region.RelativePath, ExeFsMainPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(region.Area, "main.ro", StringComparison.Ordinal)
                && string.Equals(region.Owner, owner, StringComparison.Ordinal)
                && region.StartOffset is not null
                && region.Length is not null)
            .ToArray();
    }

    public static bool Overlaps(ZaExeFsReservedRegion region, int startOffset, int length)
    {
        if (region.StartOffset is null || region.EndExclusive is null)
        {
            return false;
        }

        var endExclusive = startOffset + length;
        return startOffset < region.EndExclusive.Value && endExclusive > region.StartOffset.Value;
    }
}
