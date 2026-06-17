// SPDX-License-Identifier: GPL-3.0-only

using KM.SV.Data;

namespace KM.SV.Encounters;

internal static class SvEncounterGrouping
{
    private static readonly IReadOnlyDictionary<string, string> LocationAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["a_d1108"] = "Alfornada Cavern",
            ["a_d1202"] = "Glaseado Cave (a_d1202)",
            ["loc_desert_east"] = "Asado Desert (East)",
            ["loc_desert_west"] = "Asado Desert (West)",
            ["loc_lake_east"] = "Casseroya Lake (East)",
            ["loc_lake_south"] = "Casseroya Lake (South)",
            ["loc_snowymountain_01"] = "Glaseado Mountain",
            ["a_w23_d01"] = "Area Zero Cave",
            ["a_w23_d02"] = "Area Zero Cave",
            ["a_w23_d03"] = "Area Zero Cave",
            ["a_w23_d04"] = "Area Zero Cave",
        };

    public static string CreateGroupKey(global::EncountPokeData row)
    {
        return string.Join(
            "|",
            Normalize(row.LocationName, "location"),
            Normalize(row.Area, "area"),
            FormatVersions(row.Versiontable),
            FormatEncounterType(row),
            FormatTimes(row.Timetable),
            FormatBiomes(row),
            Normalize(row.FlagName, "no-flag"),
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"height:{row.Minheight}-{row.Maxheight}"),
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"band:{row.Bandrate}:{row.Bandtype}:{(int)row.Bandpoke}:{row.BandFormno}"),
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"outbreak:{row.OutbreakLotvalue}"),
            Normalize(row.PokeVoiceClassification, "voice:any"));
    }

    public static string FormatLocation(global::EncountPokeData row, SvTextLabelLookup labels)
    {
        var explicitNames = FormatLocationTokens(row.LocationName, labels);
        if (explicitNames.Count > 0)
        {
            return SummarizeLocationNames(explicitNames);
        }

        var areaNames = FormatAreaTokens(row.Area, labels);
        return areaNames.Count == 0 ? "Biome-Based Habitat" : SummarizeLocationNames(areaNames);
    }

    public static string FormatDisplayArea(global::EncountPokeData row, SvTextLabelLookup labels)
    {
        var areaNames = FormatAreaTokens(row.Area, labels);
        if (areaNames.Count > 0)
        {
            return string.Join(", ", areaNames);
        }

        var explicitNames = FormatLocationTokens(row.LocationName, labels);
        if (explicitNames.Count > 0)
        {
            return string.Join(", ", explicitNames);
        }

        if (!string.IsNullOrWhiteSpace(row.Area))
        {
            return row.Area.Trim();
        }

        var parts = new[]
            {
                FormatEncounterType(row),
                FormatTimes(row.Timetable),
                FormatBiomes(row),
            }
            .Where(part => !string.IsNullOrWhiteSpace(part) && part != "Any")
            .ToArray();

        return parts.Length == 0 ? "Unknown Area" : string.Join(" / ", parts);
    }

    public static string FormatEncounterType(global::EncountPokeData row)
    {
        var enable = row.Enabletable;
        if (enable is null)
        {
            return "Wild";
        }

        var parts = new List<string>();
        if (enable.Value.Land)
        {
            parts.Add("Land");
        }

        if (enable.Value.UpWater)
        {
            parts.Add("Water");
        }

        if (enable.Value.Underwater)
        {
            parts.Add("Underwater");
        }

        if (enable.Value.Air1 || enable.Value.Air2)
        {
            parts.Add("Air");
        }

        return parts.Count == 0 ? "Wild" : string.Join(", ", parts);
    }

    public static string FormatTimes(global::TimeTable? table)
    {
        if (table is null)
        {
            return "Any";
        }

        var parts = new List<string>();
        if (table.Value.Morning)
        {
            parts.Add("Morning");
        }

        if (table.Value.Noon)
        {
            parts.Add("Noon");
        }

        if (table.Value.Evening)
        {
            parts.Add("Evening");
        }

        if (table.Value.Night)
        {
            parts.Add("Night");
        }

        return parts.Count == 0 ? "Any" : string.Join(", ", parts);
    }

    public static string FormatVersions(global::VersionTable? table)
    {
        if (table is null || (table.Value.A && table.Value.B) || (!table.Value.A && !table.Value.B))
        {
            return "Scarlet/Violet";
        }

        return table.Value.A ? "Scarlet" : "Violet";
    }

    public static string FormatBiomes(global::EncountPokeData row)
    {
        var biomes = new[]
        {
            (row.Biome1, row.Lotvalue1),
            (row.Biome2, row.Lotvalue2),
            (row.Biome3, row.Lotvalue3),
            (row.Biome4, row.Lotvalue4),
        };

        var parts = biomes
            .Where(biome => biome.Item1 != global::Biome.NONE || biome.Item2 != 0)
            .Select(biome => $"{FormatBiome(biome.Item1)} {biome.Item2}")
            .ToArray();

        return parts.Length == 0 ? "Any" : string.Join(", ", parts);
    }

    private static IReadOnlyList<string> FormatAreaTokens(string? area, SvTextLabelLookup labels)
    {
        return SplitTokens(area)
            .Select(token => FormatAreaToken(token, labels))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? FormatAreaToken(string token, SvTextLabelLookup labels)
    {
        if (int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var areaId)
            && areaId > 0)
        {
            return labels.PlaceName($"PLACENAME_a_w{areaId.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)}_01")
                ?? $"Area {areaId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }

        return FormatLocationToken(token, labels);
    }

    private static IReadOnlyList<string> FormatLocationTokens(string? location, SvTextLabelLookup labels)
    {
        return SplitTokens(location)
            .Select(token => FormatLocationToken(token, labels))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? FormatLocationToken(string token, SvTextLabelLookup labels)
    {
        if (LocationAliases.TryGetValue(token, out var alias))
        {
            return alias;
        }

        return labels.PlaceName($"{token}_02")
            ?? labels.PlaceName(token)
            ?? labels.PlaceName($"{token}_01")
            ?? FormatRawLocationToken(token);
    }

    private static IReadOnlyList<string> SplitTokens(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .ToArray();
    }

    private static string SummarizeLocationNames(IReadOnlyList<string> names)
    {
        return names.Count switch
        {
            0 => "Biome-Based Habitat",
            1 => names[0],
            2 => string.Join(", ", names),
            _ => $"{names[0]} + {FormatAdditionalAreaCount(names.Count - 1)}",
        };
    }

    private static string FormatAdditionalAreaCount(int count)
    {
        return count == 1
            ? "1 area"
            : $"{count.ToString(System.Globalization.CultureInfo.InvariantCulture)} areas";
    }

    private static string FormatRawLocationToken(string token)
    {
        return token
            .Replace('_', ' ')
            .Trim();
    }

    private static string FormatBiome(global::Biome biome)
    {
        return biome switch
        {
            global::Biome.OSEAN => "Ocean",
            global::Biome.CAVE_WATER => "Cave Water",
            global::Biome.DENKI_ISHI => "Electric Stone",
            _ => SvLabels.EnumName(biome),
        };
    }

    private static string Normalize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
