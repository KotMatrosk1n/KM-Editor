// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;

namespace KM.SV.ExeFs;

internal sealed record SvExeFsReservedRegion(
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

internal static class SvExeFsReservedRegionLedger
{
    public const string OwnerFashionUnlock = "Fashion Unlock";
    public const string OwnerHyperspaceBypass = "Hyperspace Bypass";
    public const string OwnerNativeGameplayMenu = "Native Gameplay Menu";
    public const string OwnerTypeChart = "Type Chart";
    public const string ExeFsMainPath = "exefs/main";

    private static readonly SvExeFsReservedRegion[] regions =
    [
        new(
            OwnerNativeGameplayMenu,
            "native-gameplay-menu-bss-reservation",
            ExeFsMainPath,
            "main.header",
            0x3C,
            0x04,
            "Native gameplay menu runtime snapshot BSS reservation",
            "do-not-overwrite"),
        new(
            OwnerNativeGameplayMenu,
            "native-gameplay-menu-share-hook",
            ExeFsMainPath,
            "main.text",
            0x01141BA0,
            0x04,
            "Native gameplay menu EXP Share runtime hook",
            "do-not-overwrite"),
        new(
            OwnerNativeGameplayMenu,
            "native-gameplay-menu-level-cap-hook",
            ExeFsMainPath,
            "main.text",
            0x0178143C,
            0x04,
            "Native gameplay menu level-cap runtime hook",
            "do-not-overwrite"),
        new(
            OwnerNativeGameplayMenu,
            "native-gameplay-menu-rate-hook",
            ExeFsMainPath,
            "main.text",
            0x01781E30,
            0x04,
            "Native gameplay menu EXP rate runtime hook",
            "do-not-overwrite"),
        new(
            OwnerNativeGameplayMenu,
            "native-gameplay-menu-runtime-scaffold",
            ExeFsMainPath,
            "main.text",
            0x0343FC90,
            0x01B0,
            "Native gameplay menu runtime scaffold",
            "do-not-overwrite"),
        new(
            OwnerTypeChart,
            "type-chart-sv",
            ExeFsMainPath,
            "main.ro",
            0x0082286C,
            0x144,
            "Scarlet/Violet type-effectiveness table",
            "do-not-overwrite"),
        new(
            OwnerFashionUnlock,
            "fashion-unlock-sv-dressup-ownership-check",
            ExeFsMainPath,
            "main.text",
            0x00EAE95C,
            0x08,
            "Scarlet/Violet dress-up ownership check",
            "do-not-overwrite"),
        new(
            OwnerHyperspaceBypass,
            "hyperspace-hoopa-runtime-gate",
            ExeFsMainPath,
            "main.text",
            0x02873A50,
            0x04,
            "Hyperspace Hole/Fury Hoopa runtime gate",
            "do-not-overwrite"),
    ];

    public static IReadOnlyList<SvExeFsReservedRegion> Regions => regions;

    public static IReadOnlyList<SvExeFsReservedRegion> MainTextRegionsForOwner(string owner)
    {
        return regions
            .Where(region => string.Equals(region.RelativePath, ExeFsMainPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(region.Area, "main.text", StringComparison.Ordinal)
                && string.Equals(region.Owner, owner, StringComparison.Ordinal)
                && region.StartOffset is not null
                && region.Length is not null)
            .ToArray();
    }

    public static IReadOnlyList<SvExeFsReservedRegion> MainRoRegionsForOwner(string owner)
    {
        return regions
            .Where(region => string.Equals(region.RelativePath, ExeFsMainPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(region.Area, "main.ro", StringComparison.Ordinal)
                && string.Equals(region.Owner, owner, StringComparison.Ordinal)
                && region.StartOffset is not null
                && region.Length is not null)
            .ToArray();
    }

    public static bool Overlaps(SvExeFsReservedRegion region, int startOffset, int length)
    {
        if (region.StartOffset is null || region.EndExclusive is null)
        {
            return false;
        }

        var endExclusive = startOffset + length;
        return startOffset < region.EndExclusive.Value && endExclusive > region.StartOffset.Value;
    }
}
