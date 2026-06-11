// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;

namespace KM.SwSh.ExeFs;

internal sealed record SwShExeFsReservedRegion(
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
        : string.Create(CultureInfo.InvariantCulture, $"text+0x{StartOffset.Value:X}..0x{StartOffset.Value + Length.Value - 1:X}");
}

internal static class SwShExeFsReservedRegionLedger
{
    public const string OwnerBagHook = "Bag Hook";
    public const string OwnerCatchCap = "Catch Cap";
    public const string OwnerRoyalCandy = "Royal Candy";
    public const string OwnerRoyalCandyStoryLimits = "Royal Candy with Story Limits";
    public const string OwnerStartingItems = "Starting Items";

    public const string ExeFsMainPath = SwShExeFsPatchWorkflowService.ExeFsMainPath;
    public const string BagEventScriptPath = "romfs/bin/script/amx/main_event_0020.amx";

    private static readonly SwShExeFsReservedRegion[] regions =
    [
        new(OwnerBagHook, "bag-hook-v2", BagEventScriptPath, "whole-file", null, null, "Bag-event AMX grant route", "owns-file"),
        new(OwnerStartingItems, "starting-items-bag-hook-slots", BagEventScriptPath, "bag-hook-slots", null, null, "Bag Hook slots 2-20 startup grants", "payload-only"),

        new(OwnerCatchCap, "catch-cap-hook-site", ExeFsMainPath, "main.text", 0x013AE3AC, 0x04, "Catch Cap hook branch site", "do-not-overwrite"),
        new(OwnerCatchCap, "catch-cap-table", ExeFsMainPath, "main.text", 0x013AE3B0, 0x09, "Catch Cap nine-byte cap table", "payload-only"),
        new(OwnerCatchCap, "catch-cap-marker", ExeFsMainPath, "main.text", 0x013AE3B9, 0x07, "Catch Cap marker/version", "do-not-overwrite"),
        new(OwnerCatchCap, "catch-cap-reserved-metadata", ExeFsMainPath, "main.text", 0x013AE3C0, 0x08, "Catch Cap reserved metadata", "do-not-overwrite"),
        new(OwnerCatchCap, "catch-cap-return", ExeFsMainPath, "main.text", 0x013AE3C8, 0x05, "Catch Cap vanilla return target", "do-not-overwrite"),
        new(OwnerCatchCap, "catch-cap-cave-1", ExeFsMainPath, "main.text", 0x013AE0B4, 0x0C, "Catch Cap standard cave slot 1", "do-not-allocate"),
        new(OwnerCatchCap, "catch-cap-cave-2", ExeFsMainPath, "main.text", 0x013AEBE4, 0x0C, "Catch Cap standard cave slot 2", "do-not-allocate"),
        new(OwnerCatchCap, "catch-cap-cave-3", ExeFsMainPath, "main.text", 0x013AD734, 0x0C, "Catch Cap standard cave slot 3", "do-not-allocate"),
        new(OwnerCatchCap, "catch-cap-cave-4", ExeFsMainPath, "main.text", 0x013AF464, 0x0C, "Catch Cap fallback cave slot 1", "do-not-allocate"),
        new(OwnerCatchCap, "catch-cap-cave-5", ExeFsMainPath, "main.text", 0x013AF6B4, 0x0C, "Catch Cap fallback cave slot 2", "do-not-allocate"),

        new(OwnerRoyalCandy, "royal-candy-ui-check-a", ExeFsMainPath, "main.text", 0x00747988, 0x08, "Royal Candy medicine UI route", "do-not-overwrite"),
        new(OwnerRoyalCandy, "royal-candy-ui-check-b", ExeFsMainPath, "main.text", 0x00747D44, 0x08, "Royal Candy alternate medicine UI route", "do-not-overwrite"),
        new(OwnerRoyalCandy, "royal-candy-ui-check-c", ExeFsMainPath, "main.text", 0x0074BA24, 0x08, "Royal Candy party target route", "do-not-overwrite"),
        new(OwnerRoyalCandy, "royal-candy-ui-check-d", ExeFsMainPath, "main.text", 0x0074BDA8, 0x08, "Royal Candy alternate party target route", "do-not-overwrite"),
        new(OwnerRoyalCandy, "royal-candy-ui-check-e", ExeFsMainPath, "main.text", 0x0074DFE4, 0x08, "Royal Candy medicine capability route", "do-not-overwrite"),
        new(OwnerRoyalCandy, "royal-candy-ui-check-f", ExeFsMainPath, "main.text", 0x0074DFF8, 0x08, "Royal Candy alternate medicine capability route", "do-not-overwrite"),
        new(OwnerRoyalCandy, "royal-candy-ui-check-g", ExeFsMainPath, "main.text", 0x0075CEFC, 0x08, "Royal Candy quantity state route", "do-not-overwrite"),
        new(OwnerRoyalCandy, "royal-candy-ui-check-h", ExeFsMainPath, "main.text", 0x007BB204, 0x08, "Royal Candy bag use gate route", "do-not-overwrite"),
        new(OwnerRoyalCandy, "royal-candy-ui-check-i", ExeFsMainPath, "main.text", 0x007BB3C0, 0x08, "Royal Candy bag quantity route", "do-not-overwrite"),
        new(OwnerRoyalCandy, "royal-candy-ui-check-j", ExeFsMainPath, "main.text", 0x007BC1F8, 0x08, "Royal Candy bag UI route", "do-not-overwrite"),
        new(OwnerRoyalCandy, "royal-candy-equal-branch-a", ExeFsMainPath, "main.text", 0x00747DE0, 0x08, "Royal Candy medicine equal branch", "do-not-overwrite"),
        new(OwnerRoyalCandy, "royal-candy-equal-branch-b", ExeFsMainPath, "main.text", 0x0074BE44, 0x08, "Royal Candy party target equal branch", "do-not-overwrite"),
        new(OwnerRoyalCandy, "royal-candy-equal-branch-c", ExeFsMainPath, "main.text", 0x0075CCE8, 0x08, "Royal Candy quantity equal branch", "do-not-overwrite"),
        new(OwnerRoyalCandy, "royal-candy-equal-branch-d", ExeFsMainPath, "main.text", 0x0075D08C, 0x08, "Royal Candy alternate quantity equal branch", "do-not-overwrite"),
        new(OwnerRoyalCandy, "royal-candy-equal-branch-e", ExeFsMainPath, "main.text", 0x007BBFD4, 0x08, "Royal Candy bag state equal branch", "do-not-overwrite"),
        new(OwnerRoyalCandy, "royal-candy-exp-upper-bound-a", ExeFsMainPath, "main.text", 0x007BC1BC, 0x04, "Exp Candy fixed amount upper-bound A", "do-not-overwrite"),
        new(OwnerRoyalCandy, "royal-candy-exp-upper-bound-b", ExeFsMainPath, "main.text", 0x007BC1C4, 0x04, "Exp Candy fixed amount upper-bound B", "do-not-overwrite"),
        new(OwnerRoyalCandy, "royal-candy-infinite-use", ExeFsMainPath, "main.text", 0x007B1F20, 0x04, "Royal Candy infinite-use consume quantity move", "do-not-overwrite"),
        new(OwnerRoyalCandy, "royal-candy-consumable-upper-bound", ExeFsMainPath, "main.text", 0x007DDA8C, 0x04, "Allowed consumable upper bound", "do-not-overwrite"),

        new(OwnerRoyalCandyStoryLimits, "royal-candy-story-use-gate", ExeFsMainPath, "main.text", 0x007BB208, 0x04, "Royal Candy story-cap use gate branch", "do-not-overwrite"),
        new(OwnerRoyalCandyStoryLimits, "royal-candy-story-quantity", ExeFsMainPath, "main.text", 0x007BB3C4, 0x04, "Royal Candy story-cap quantity branch", "do-not-overwrite"),
        new(OwnerRoyalCandyStoryLimits, "royal-candy-story-clamp-compare", ExeFsMainPath, "main.text", 0x007BAF38, 0x04, "Royal Candy quantity clamp compare", "do-not-overwrite"),
        new(OwnerRoyalCandyStoryLimits, "royal-candy-story-clamp-select", ExeFsMainPath, "main.text", 0x007BAF3C, 0x04, "Royal Candy inventory clamp bypass", "do-not-overwrite"),
    ];

    public static IReadOnlyList<SwShExeFsReservedRegion> Regions => regions;

    public static IReadOnlyList<SwShExeFsReservedRegion> MainTextRegionsForOwner(string owner)
    {
        return regions
            .Where(region => string.Equals(region.RelativePath, ExeFsMainPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(region.Area, "main.text", StringComparison.Ordinal)
                && string.Equals(region.Owner, owner, StringComparison.Ordinal)
                && region.StartOffset is not null
                && region.Length is not null)
            .ToArray();
    }

    public static IReadOnlyList<SwShExeFsReservedRegion> MainTextRegionsForOwners(params string[] owners)
    {
        var ownerSet = owners.ToHashSet(StringComparer.Ordinal);
        return regions
            .Where(region => string.Equals(region.RelativePath, ExeFsMainPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(region.Area, "main.text", StringComparison.Ordinal)
                && ownerSet.Contains(region.Owner)
                && region.StartOffset is not null
                && region.Length is not null)
            .ToArray();
    }

    public static IReadOnlyList<SwShExeFsReservedRegion> MainTextReservationsForOtherOwners(params string[] allowedOwners)
    {
        var allowedOwnerSet = allowedOwners.ToHashSet(StringComparer.Ordinal);
        return regions
            .Where(region => string.Equals(region.RelativePath, ExeFsMainPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(region.Area, "main.text", StringComparison.Ordinal)
                && !allowedOwnerSet.Contains(region.Owner)
                && region.StartOffset is not null
                && region.Length is not null
                && (string.Equals(region.Rule, "do-not-overwrite", StringComparison.Ordinal)
                    || string.Equals(region.Rule, "do-not-allocate", StringComparison.Ordinal)
                    || string.Equals(region.Rule, "payload-only", StringComparison.Ordinal)))
            .ToArray();
    }

    public static bool Overlaps(SwShExeFsReservedRegion region, int startOffset, int length)
    {
        if (region.StartOffset is null || region.EndExclusive is null)
        {
            return false;
        }

        var endExclusive = startOffset + length;
        return startOffset < region.EndExclusive.Value && endExclusive > region.StartOffset.Value;
    }
}
