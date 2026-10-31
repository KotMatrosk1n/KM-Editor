// SPDX-License-Identifier: GPL-3.0-only

using KM.SV.Data;

namespace KM.SV.Encounters;

internal sealed record SvEncounterRegion(string Key, string Label, string VirtualPath, string AreaPrefix)
{
    public static IReadOnlyList<SvEncounterRegion> All { get; } =
    [
        new("paldea", "Paldea", SvDataPaths.WildEncounterArray, "a_w"),
        new("kitakami", "Kitakami", SvDataPaths.WildEncounterKitakamiArray, "a_su1"),
        new("blueberry", "Blueberry Academy", SvDataPaths.WildEncounterBlueberryArray, "a_su02"),
    ];

    // Preserve existing Paldea identities used by pending edits and editable dumps.
    public string TableId(string groupKey) => Key == "paldea" ? groupKey : $"{groupKey}|region:{Key}";

    public static SvEncounterRegion? FromTableId(string tableId) =>
        All.FirstOrDefault(region => region.Key == "paldea"
            ? tableId.Split('|').Length == 7
            : tableId.Split('|').Length == 8 && tableId.EndsWith($"|region:{region.Key}", StringComparison.Ordinal));

    public bool OwnsSource(string path) => string.Equals(
        path.StartsWith("romfs/", StringComparison.OrdinalIgnoreCase) ? path[6..] : path,
        VirtualPath,
        StringComparison.OrdinalIgnoreCase);
}
