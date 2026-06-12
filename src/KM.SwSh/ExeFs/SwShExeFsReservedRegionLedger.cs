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
    public const string OwnerIvScreen = "IV Screen";
    public const string OwnerPokemonSummaryRuntime = "Pokemon Summary Runtime";
    public const string OwnerRoyalCandy = "Royal Candy";
    public const string OwnerRoyalCandyStoryLimits = "Royal Candy with Story Limits";
    public const string OwnerStartingItems = "Starting Items";

    public const string ExeFsMainPath = SwShExeFsPatchWorkflowService.ExeFsMainPath;
    public const string BagEventScriptPath = "romfs/bin/script/amx/main_event_0020.amx";

    private static readonly SwShExeFsReservedRegion[] regions =
    [
        new(OwnerBagHook, "bag-hook-v2", BagEventScriptPath, "whole-file", null, null, "Bag-event AMX grant route", "owns-file"),
        new(OwnerStartingItems, "starting-items-bag-hook-slots", BagEventScriptPath, "bag-hook-slots", null, null, "Bag Hook slots 2-20 startup grants", "payload-only"),

        // Catch Cap shares one cap table between the trainer card display path and the runtime
        // capture gate, so both executable ranges are reserved under the same owner.
        new(OwnerCatchCap, "catch-cap-hook-site", ExeFsMainPath, "main.text", 0x013AE3AC, 0x04, "Catch Cap hook branch site", "do-not-overwrite"),
        new(OwnerCatchCap, "catch-cap-table", ExeFsMainPath, "main.text", 0x013AE3B0, 0x09, "Catch Cap nine-byte cap table", "payload-only"),
        new(OwnerCatchCap, "catch-cap-marker", ExeFsMainPath, "main.text", 0x013AE3B9, 0x07, "Catch Cap marker/version", "do-not-overwrite"),
        new(OwnerCatchCap, "catch-cap-reserved-metadata", ExeFsMainPath, "main.text", 0x013AE3C0, 0x08, "Catch Cap reserved metadata", "do-not-overwrite"),
        new(OwnerCatchCap, "catch-cap-return", ExeFsMainPath, "main.text", 0x013AE3C8, 0x05, "Catch Cap vanilla return target", "do-not-overwrite"),
        new(OwnerCatchCap, "catch-cap-runtime-gate", ExeFsMainPath, "main.text", 0x013AE3DC, 0x18, "Catch Cap runtime capture gate", "do-not-overwrite"),
        new(OwnerCatchCap, "catch-cap-cave-1", ExeFsMainPath, "main.text", 0x013AE0B4, 0x0C, "Catch Cap standard cave slot 1", "do-not-allocate"),
        new(OwnerCatchCap, "catch-cap-cave-2", ExeFsMainPath, "main.text", 0x013AEBE4, 0x0C, "Catch Cap standard cave slot 2", "do-not-allocate"),
        new(OwnerCatchCap, "catch-cap-cave-3", ExeFsMainPath, "main.text", 0x013AD734, 0x0C, "Catch Cap standard cave slot 3", "do-not-allocate"),
        new(OwnerCatchCap, "catch-cap-cave-4", ExeFsMainPath, "main.text", 0x013AF464, 0x0C, "Catch Cap fallback cave slot 1", "do-not-allocate"),
        new(OwnerCatchCap, "catch-cap-cave-5", ExeFsMainPath, "main.text", 0x013AF6B4, 0x0C, "Catch Cap fallback cave slot 2", "do-not-allocate"),

        new(OwnerIvScreen, "iv-screen-legacy-secondary-hook-site", ExeFsMainPath, "main.text", 0x0137F634, 0x04, "IV Screen legacy secondary-stats setup hook branch site", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-legacy-normal-hook-site", ExeFsMainPath, "main.text", 0x0138F268, 0x04, "IV Screen legacy normal stats graph refresh hook branch site", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-normal-value-source-01", ExeFsMainPath, "main.text", 0x0138FBE8, 0x04, "IV Screen legacy normal graph value source 01", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-normal-value-source-02", ExeFsMainPath, "main.text", 0x0138FC38, 0x04, "IV Screen legacy normal graph value source 02", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-normal-value-source-03", ExeFsMainPath, "main.text", 0x0138FC74, 0x04, "IV Screen legacy normal graph value source 03", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-normal-value-source-04", ExeFsMainPath, "main.text", 0x0138FC9C, 0x04, "IV Screen legacy normal graph value source 04", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-normal-value-source-05", ExeFsMainPath, "main.text", 0x0138FD2C, 0x04, "IV Screen legacy normal graph value source 05", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-normal-value-source-06", ExeFsMainPath, "main.text", 0x0138FD5C, 0x04, "IV Screen legacy normal graph value source 06", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-normal-value-source-07", ExeFsMainPath, "main.text", 0x0138FD84, 0x04, "IV Screen legacy normal graph value source 07", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-normal-value-source-08", ExeFsMainPath, "main.text", 0x0138FEA0, 0x04, "IV Screen legacy normal graph value source 08", "restore-on-migrate"),

        // X-mode value-source ranges are patched in place. The old sparkle-layer and
        // renderer-wrapper ranges below stay IV-owned only so reinstall/uninstall can scrub them.
        new(OwnerIvScreen, "iv-screen-multichart-text-hp-value-01", ExeFsMainPath, "main.text", 0x0138A2B4, 0x04, "IV Screen multi-chart HP text value source 01", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-multichart-text-hp-value-02", ExeFsMainPath, "main.text", 0x0138A3CC, 0x04, "IV Screen multi-chart HP text value source 02", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-multichart-text-stat-value-01", ExeFsMainPath, "main.text", 0x0138A47C, 0x04, "IV Screen multi-chart stat text value source 01", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-multichart-text-stat-value-02", ExeFsMainPath, "main.text", 0x0138A518, 0x04, "IV Screen multi-chart stat text value source 02", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-multichart-text-stat-value-03", ExeFsMainPath, "main.text", 0x0138A5B4, 0x04, "IV Screen multi-chart stat text value source 03", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-multichart-text-stat-value-04", ExeFsMainPath, "main.text", 0x0138A650, 0x04, "IV Screen multi-chart stat text value source 04", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-multichart-text-stat-value-05", ExeFsMainPath, "main.text", 0x0138A6F0, 0x04, "IV Screen multi-chart stat text value source 05", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-multichart-source-01", ExeFsMainPath, "main.text", 0x0138AA50, 0x04, "IV Screen multi-chart stat source 01", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-multichart-source-02", ExeFsMainPath, "main.text", 0x0138AA60, 0x04, "IV Screen multi-chart stat source 02", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-multichart-source-03", ExeFsMainPath, "main.text", 0x0138AA90, 0x04, "IV Screen multi-chart stat source 03", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-multichart-source-04", ExeFsMainPath, "main.text", 0x0138AAA0, 0x04, "IV Screen multi-chart stat source 04", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-multichart-source-05", ExeFsMainPath, "main.text", 0x0138AAD0, 0x04, "IV Screen multi-chart stat source 05", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-multichart-source-06", ExeFsMainPath, "main.text", 0x0138AAE0, 0x04, "IV Screen multi-chart stat source 06", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-multichart-source-07", ExeFsMainPath, "main.text", 0x0138AB10, 0x04, "IV Screen multi-chart stat source 07", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-multichart-source-08", ExeFsMainPath, "main.text", 0x0138AB20, 0x04, "IV Screen multi-chart stat source 08", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-multichart-source-09", ExeFsMainPath, "main.text", 0x0138AB50, 0x04, "IV Screen multi-chart stat source 09", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-multichart-source-10", ExeFsMainPath, "main.text", 0x0138AB60, 0x04, "IV Screen multi-chart stat source 10", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-multichart-source-11", ExeFsMainPath, "main.text", 0x0138AB90, 0x04, "IV Screen multi-chart stat source 11", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-multichart-source-12", ExeFsMainPath, "main.text", 0x0138ABA0, 0x04, "IV Screen multi-chart stat source 12", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-yellow-raw-value-01", ExeFsMainPath, "main.text", 0x0138AC88, 0x04, "IV Screen yellow graph raw value site 01", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-yellow-raw-value-02", ExeFsMainPath, "main.text", 0x0138ACAC, 0x04, "IV Screen yellow graph raw value site 02", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-yellow-raw-value-03", ExeFsMainPath, "main.text", 0x0138ACD0, 0x04, "IV Screen yellow graph raw value site 03", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-yellow-raw-value-04", ExeFsMainPath, "main.text", 0x0138ACF8, 0x04, "IV Screen yellow graph raw value site 04", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-yellow-raw-value-05", ExeFsMainPath, "main.text", 0x0138AD1C, 0x04, "IV Screen yellow graph raw value site 05", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-yellow-raw-value-06", ExeFsMainPath, "main.text", 0x0138AD40, 0x04, "IV Screen yellow graph raw value site 06", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-yellow-value-source-01", ExeFsMainPath, "main.text", 0x0138AE28, 0x04, "IV Screen legacy X-mode sparkle value source 01", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-yellow-value-source-02", ExeFsMainPath, "main.text", 0x0138AE3C, 0x04, "IV Screen legacy X-mode sparkle value source 02", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-yellow-value-source-03", ExeFsMainPath, "main.text", 0x0138AE50, 0x04, "IV Screen legacy X-mode sparkle value source 03", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-yellow-value-source-04", ExeFsMainPath, "main.text", 0x0138AE64, 0x04, "IV Screen legacy X-mode sparkle value source 04", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-yellow-value-source-05", ExeFsMainPath, "main.text", 0x0138AE78, 0x04, "IV Screen legacy X-mode sparkle value source 05", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-yellow-value-source-06", ExeFsMainPath, "main.text", 0x0138AE8C, 0x04, "IV Screen legacy X-mode sparkle value source 06", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-yellow-pane-visibility-01", ExeFsMainPath, "main.text", 0x0138AEAC, 0x14, "IV Screen X-mode value-pane visibility 01", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-yellow-pane-visibility-02", ExeFsMainPath, "main.text", 0x0138AEE0, 0x18, "IV Screen X-mode value-pane visibility 02", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-yellow-pane-visibility-03", ExeFsMainPath, "main.text", 0x0138AF18, 0x18, "IV Screen X-mode value-pane visibility 03", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-yellow-pane-visibility-04", ExeFsMainPath, "main.text", 0x0138AF54, 0x18, "IV Screen X-mode value-pane visibility 04", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-yellow-pane-visibility-05", ExeFsMainPath, "main.text", 0x0138AF8C, 0x18, "IV Screen X-mode value-pane visibility 05", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-yellow-pane-visibility-06", ExeFsMainPath, "main.text", 0x0138AFC4, 0x18, "IV Screen X-mode value-pane visibility 06", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-x-toggle-number-pane-01", ExeFsMainPath, "main.text", 0x0138B230, 0x04, "IV Screen X-toggle numeric text pane visibility 01", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-x-toggle-number-pane-02", ExeFsMainPath, "main.text", 0x0138B264, 0x04, "IV Screen X-toggle numeric text pane visibility 02", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-x-toggle-number-pane-03", ExeFsMainPath, "main.text", 0x0138B298, 0x04, "IV Screen X-toggle numeric text pane visibility 03", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-x-toggle-number-pane-04", ExeFsMainPath, "main.text", 0x0138B2CC, 0x04, "IV Screen X-toggle numeric text pane visibility 04", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-x-toggle-number-pane-05", ExeFsMainPath, "main.text", 0x0138B300, 0x04, "IV Screen X-toggle numeric text pane visibility 05", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-x-toggle-number-pane-06", ExeFsMainPath, "main.text", 0x0138B334, 0x04, "IV Screen X-toggle numeric text pane visibility 06", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-x-toggle-number-pane-07", ExeFsMainPath, "main.text", 0x0138B368, 0x04, "IV Screen X-toggle numeric text pane visibility 07", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-x-toggle-number-pane-08", ExeFsMainPath, "main.text", 0x0138B39C, 0x04, "IV Screen X-toggle numeric text pane visibility 08", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-x-toggle-refresh", ExeFsMainPath, "main.text", 0x0138B3AC, 0x0C, "IV Screen X-toggle stats refresh call", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-value-pane-visible-flag", ExeFsMainPath, "main.text", 0x0138B1FC, 0x04, "IV Screen legacy value-pane visibility flag", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-value-pane-visible-mask", ExeFsMainPath, "main.text", 0x0138B200, 0x04, "IV Screen legacy value-pane visibility mask", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-hook-site", ExeFsMainPath, "main.text", 0x01392EA8, 0x04, "IV Screen legacy summary renderer wrapper primary call", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-renderer-wrapper-call-02", ExeFsMainPath, "main.text", 0x01393310, 0x04, "IV Screen legacy summary renderer wrapper call 02", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-renderer-wrapper-call-03", ExeFsMainPath, "main.text", 0x0139EF4C, 0x04, "IV Screen legacy summary renderer wrapper call 03", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-x-yellow-bypass", ExeFsMainPath, "main.text", 0x0139FB60, 0x04, "IV Screen legacy X-toggle yellow graph bypass branch", "restore-on-migrate"),
        new(OwnerIvScreen, "iv-screen-cave-01", ExeFsMainPath, "main.text", 0x0138F324, 0x0C, "IV Screen wrapper cave slot 01", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-02", ExeFsMainPath, "main.text", 0x0138F704, 0x0C, "IV Screen wrapper cave slot 02", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-03", ExeFsMainPath, "main.text", 0x0138F764, 0x0C, "IV Screen wrapper cave slot 03", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-04", ExeFsMainPath, "main.text", 0x0138F984, 0x0C, "IV Screen wrapper cave slot 04", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-05", ExeFsMainPath, "main.text", 0x0138FB54, 0x0C, "IV Screen wrapper cave slot 05", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-06", ExeFsMainPath, "main.text", 0x0138FFF4, 0x0C, "IV Screen wrapper cave slot 06", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-07", ExeFsMainPath, "main.text", 0x01390054, 0x0C, "IV Screen wrapper cave slot 07", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-08", ExeFsMainPath, "main.text", 0x01390064, 0x0C, "IV Screen wrapper cave slot 08", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-09", ExeFsMainPath, "main.text", 0x01390134, 0x0C, "IV Screen wrapper cave slot 09", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-10", ExeFsMainPath, "main.text", 0x01390144, 0x0C, "IV Screen wrapper cave slot 10", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-11", ExeFsMainPath, "main.text", 0x013901A4, 0x0C, "IV Screen wrapper cave slot 11", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-12", ExeFsMainPath, "main.text", 0x01390204, 0x0C, "IV Screen wrapper cave slot 12", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-13", ExeFsMainPath, "main.text", 0x01390BE4, 0x0C, "IV Screen wrapper cave slot 13", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-14", ExeFsMainPath, "main.text", 0x01391114, 0x0C, "IV Screen wrapper cave slot 14", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-15", ExeFsMainPath, "main.text", 0x013912F4, 0x0C, "IV Screen wrapper cave slot 15", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-16", ExeFsMainPath, "main.text", 0x01391304, 0x0C, "IV Screen wrapper cave slot 16", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-17", ExeFsMainPath, "main.text", 0x013916F4, 0x0C, "IV Screen wrapper cave slot 17", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-18", ExeFsMainPath, "main.text", 0x01391704, 0x0C, "IV Screen wrapper cave slot 18", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-19", ExeFsMainPath, "main.text", 0x01391734, 0x0C, "IV Screen wrapper cave slot 19", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-20", ExeFsMainPath, "main.text", 0x01391744, 0x0C, "IV Screen wrapper cave slot 20", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-21", ExeFsMainPath, "main.text", 0x01392334, 0x0C, "IV Screen wrapper cave slot 21", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-22", ExeFsMainPath, "main.text", 0x01392464, 0x0C, "IV Screen wrapper cave slot 22", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-23", ExeFsMainPath, "main.text", 0x01392854, 0x0C, "IV Screen wrapper cave slot 23", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-24", ExeFsMainPath, "main.text", 0x01392864, 0x0C, "IV Screen wrapper cave slot 24", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-25", ExeFsMainPath, "main.text", 0x01392894, 0x0C, "IV Screen wrapper cave slot 25", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-26", ExeFsMainPath, "main.text", 0x013928A4, 0x0C, "IV Screen wrapper cave slot 26", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-27", ExeFsMainPath, "main.text", 0x01393464, 0x0C, "IV Screen wrapper cave slot 27", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-28", ExeFsMainPath, "main.text", 0x01393584, 0x0C, "IV Screen wrapper cave slot 28", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-29", ExeFsMainPath, "main.text", 0x01393984, 0x0C, "IV Screen wrapper cave slot 29", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-30", ExeFsMainPath, "main.text", 0x01393994, 0x0C, "IV Screen wrapper cave slot 30", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-31", ExeFsMainPath, "main.text", 0x013939C4, 0x0C, "IV Screen wrapper cave slot 31", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-32", ExeFsMainPath, "main.text", 0x013939D4, 0x0C, "IV Screen wrapper cave slot 32", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-33", ExeFsMainPath, "main.text", 0x01393CA4, 0x0C, "IV Screen wrapper cave slot 33", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-34", ExeFsMainPath, "main.text", 0x01393CE4, 0x0C, "IV Screen wrapper cave slot 34", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-35", ExeFsMainPath, "main.text", 0x01394A44, 0x0C, "IV Screen wrapper cave slot 35", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-36", ExeFsMainPath, "main.text", 0x01395DD4, 0x0C, "IV Screen wrapper cave slot 36", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-37", ExeFsMainPath, "main.text", 0x01395EB4, 0x0C, "IV Screen wrapper cave slot 37", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-38", ExeFsMainPath, "main.text", 0x01395FF4, 0x0C, "IV Screen wrapper cave slot 38", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-39", ExeFsMainPath, "main.text", 0x013963E4, 0x0C, "IV Screen wrapper cave slot 39", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-40", ExeFsMainPath, "main.text", 0x013963F4, 0x0C, "IV Screen wrapper cave slot 40", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-41", ExeFsMainPath, "main.text", 0x01396424, 0x0C, "IV Screen wrapper cave slot 41", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-42", ExeFsMainPath, "main.text", 0x01396434, 0x0C, "IV Screen wrapper cave slot 42", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-43", ExeFsMainPath, "main.text", 0x01396FB4, 0x0C, "IV Screen wrapper cave slot 43", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-44", ExeFsMainPath, "main.text", 0x013970C4, 0x0C, "IV Screen wrapper cave slot 44", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-45", ExeFsMainPath, "main.text", 0x013970E4, 0x0C, "IV Screen wrapper cave slot 45", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-46", ExeFsMainPath, "main.text", 0x01397104, 0x0C, "IV Screen wrapper cave slot 46", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-47", ExeFsMainPath, "main.text", 0x01397944, 0x0C, "IV Screen wrapper cave slot 47", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-48", ExeFsMainPath, "main.text", 0x01397974, 0x0C, "IV Screen wrapper cave slot 48", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-49", ExeFsMainPath, "main.text", 0x01397984, 0x0C, "IV Screen wrapper cave slot 49", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-50", ExeFsMainPath, "main.text", 0x01397B04, 0x0C, "IV Screen wrapper cave slot 50", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-51", ExeFsMainPath, "main.text", 0x01397C24, 0x0C, "IV Screen wrapper cave slot 51", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-52", ExeFsMainPath, "main.text", 0x01398144, 0x0C, "IV Screen wrapper cave slot 52", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-53", ExeFsMainPath, "main.text", 0x01398154, 0x0C, "IV Screen wrapper cave slot 53", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-54", ExeFsMainPath, "main.text", 0x01398284, 0x0C, "IV Screen wrapper cave slot 54", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-55", ExeFsMainPath, "main.text", 0x01398294, 0x0C, "IV Screen wrapper cave slot 55", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-56", ExeFsMainPath, "main.text", 0x013985F4, 0x0C, "IV Screen wrapper cave slot 56", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-57", ExeFsMainPath, "main.text", 0x01399764, 0x0C, "IV Screen wrapper cave slot 57", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-58", ExeFsMainPath, "main.text", 0x0139A954, 0x0C, "IV Screen wrapper cave slot 58", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-59", ExeFsMainPath, "main.text", 0x0139B0E4, 0x0C, "IV Screen wrapper cave slot 59", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-60", ExeFsMainPath, "main.text", 0x0139B264, 0x0C, "IV Screen wrapper cave slot 60", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-61", ExeFsMainPath, "main.text", 0x0139B654, 0x0C, "IV Screen wrapper cave slot 61", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-62", ExeFsMainPath, "main.text", 0x0139B664, 0x0C, "IV Screen wrapper cave slot 62", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-63", ExeFsMainPath, "main.text", 0x0139B694, 0x0C, "IV Screen wrapper cave slot 63", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-64", ExeFsMainPath, "main.text", 0x0139B6A4, 0x0C, "IV Screen wrapper cave slot 64", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-65", ExeFsMainPath, "main.text", 0x0139C504, 0x0C, "IV Screen wrapper cave slot 65", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-66", ExeFsMainPath, "main.text", 0x0139CA04, 0x0C, "IV Screen wrapper cave slot 66", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-67", ExeFsMainPath, "main.text", 0x0139CA74, 0x0C, "IV Screen wrapper cave slot 67", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-68", ExeFsMainPath, "main.text", 0x0139D464, 0x0C, "IV Screen wrapper cave slot 68", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-69", ExeFsMainPath, "main.text", 0x0139D704, 0x0C, "IV Screen wrapper cave slot 69", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-70", ExeFsMainPath, "main.text", 0x0139DCC4, 0x0C, "IV Screen wrapper cave slot 70", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-71", ExeFsMainPath, "main.text", 0x0139DCD4, 0x0C, "IV Screen wrapper cave slot 71", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-72", ExeFsMainPath, "main.text", 0x0139DCE4, 0x0C, "IV Screen wrapper cave slot 72", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-73", ExeFsMainPath, "main.text", 0x0139DD14, 0x0C, "IV Screen wrapper cave slot 73", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-74", ExeFsMainPath, "main.text", 0x0139DD24, 0x0C, "IV Screen wrapper cave slot 74", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-75", ExeFsMainPath, "main.text", 0x0139DF14, 0x0C, "IV Screen wrapper cave slot 75", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-cave-76", ExeFsMainPath, "main.text", 0x0139DF24, 0x0C, "IV Screen wrapper cave slot 76", "do-not-allocate"),
        new(OwnerIvScreen, "iv-screen-marker-1", ExeFsMainPath, "main.text", 0x013975B4, 0x0C, "IV Screen marker/version fragment 1", "do-not-overwrite"),
        new(OwnerIvScreen, "iv-screen-marker-2", ExeFsMainPath, "main.text", 0x01397934, 0x0C, "IV Screen marker/version fragment 2", "do-not-overwrite"),
        new(OwnerPokemonSummaryRuntime, "pokemon-summary-raw-iv-getter", ExeFsMainPath, "main.text", 0x00779070, 0x04, "Raw IV getter used by IV Screen", "do-not-overwrite"),
        new(OwnerPokemonSummaryRuntime, "pokemon-summary-hyper-training-iv-wrapper", ExeFsMainPath, "main.text", 0x007790D0, 0x04, "Hyper Training-adjusted IV wrapper not used by IV Screen", "do-not-overwrite"),

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
