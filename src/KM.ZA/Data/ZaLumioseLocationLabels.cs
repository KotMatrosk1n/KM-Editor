// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;

namespace KM.ZA.Data;

internal static class ZaLumioseLocationLabels
{
    private const string WildZonePlaceNamePrefix = "wild_";
    private const string ZdmRandomLocationKey = "zdm_random_dimension_wilds";
    private const string ZdmRandomLocationLabel = "Dimension Wild Pools";
    private const string DimensionMegaEventsLocationKey = "dimension_mega_events";
    private static readonly IReadOnlyDictionary<string, string> KnownLocationNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["d01"] = "Lysandre Labs",
            ["d01_01"] = "Lysandre Labs",
            ["d02_01"] = "Lumiose Sewers Main Area",
            ["d02_02"] = "Lumiose Sewers Side Area",
            ["d03"] = "Old Building",
            ["d03_01"] = "Old Building",
            ["t2"] = "Lysandre Labs",
            ["t3"] = "Lumiose Sewers Main Area",
            ["t3_2"] = "Lumiose Sewers Side Area",
        };

    private static readonly IReadOnlyDictionary<string, int> WildZoneNumbers =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["a0102_w01"] = 1,
            ["a0103_w01"] = 2,
            ["a0403_w01"] = 3,
            ["a0402_w01"] = 4,
            ["a0202_w02"] = 5,
            ["a0502_w01"] = 6,
            ["a0303_w01"] = 7,
            ["a0503_w01"] = 8,
            ["a0301_w02"] = 9,
            ["a0202_w01"] = 10,
            ["a0502_w02"] = 11,
            ["a0201_w01"] = 12,
            ["a0401_w01"] = 13,
            ["a0301_w01"] = 14,
            ["a0501_w01"] = 15,
            ["a0203_w01"] = 16,
            ["a0101_w01"] = 17,
            ["a0302_w01"] = 18,
            ["a0501_w02"] = 19,
            ["a0601_w01"] = 20,
        };

    private static readonly IReadOnlyDictionary<int, string> DistrictNames =
        new Dictionary<int, string>
        {
            [1] = "Vert District",
            [2] = "Bleu District",
            [3] = "Magenta District",
            [4] = "Rouge District",
            [5] = "Jaune District",
        };

    private static readonly IReadOnlyDictionary<int, string> DimensionWildTypePoolNames =
        new Dictionary<int, string>
        {
            [0] = "Normal Type Pool",
            [1] = "Fighting Type Pool",
            [2] = "Flying Type Pool",
            [3] = "Poison Type Pool",
            [4] = "Ground Type Pool",
            [5] = "Rock Type Pool",
            [6] = "Bug Type Pool",
            [7] = "Ghost Type Pool",
            [8] = "Steel Type Pool",
            [9] = "Fire Type Pool",
            [10] = "Water Type Pool",
            [11] = "Grass Type Pool",
            [12] = "Electric Type Pool",
            [13] = "Psychic Type Pool",
            [14] = "Ice Type Pool",
            [15] = "Dragon Type Pool",
            [16] = "Dark Type Pool",
            [17] = "Fairy Type Pool",
        };

    public static string CreateLocationKey(string? zoneId, string? variationId, string? spawnerId)
    {
        var zoneKey = CreateZoneKey(zoneId, variationId);
        if (!string.IsNullOrWhiteSpace(zoneKey))
        {
            return zoneKey;
        }

        return string.IsNullOrWhiteSpace(spawnerId)
            ? "Unknown Z-A Area"
            : FormatRawSpawnerLocationKey(spawnerId);
    }

    public static string? CreateZoneKey(string? zoneId, string? variationId)
    {
        var zone = zoneId?.Trim();
        if (string.IsNullOrWhiteSpace(zone))
        {
            return null;
        }

        var variation = variationId?.Trim();
        if (string.IsNullOrWhiteSpace(variation) || zone.Contains('_', StringComparison.Ordinal))
        {
            return zone;
        }

        return $"{zone}_{variation}";
    }

    public static string FormatLocation(
        string locationKey,
        Func<string, string?>? placeNameResolver = null,
        Func<int, string>? pokemonNameResolver = null,
        Func<ZaMissionDescriptor, string>? missionTitleResolver = null)
    {
        var normalizedKey = NormalizeLocationKey(locationKey);
        if (TryGetWildZoneNumber(normalizedKey, out var zoneNumber))
        {
            return ResolvePlaceName(placeNameResolver, $"{WildZonePlaceNamePrefix}{normalizedKey}")
                ?? $"Wild Zone {zoneNumber.ToString(CultureInfo.InvariantCulture)}";
        }

        return ResolvePlaceName(placeNameResolver, normalizedKey)
            ?? FormatKnownLocation(normalizedKey)
            ?? TryFormatZdmRandomLocationKey(normalizedKey)
            ?? TryFormatOutzoneLocationKey(normalizedKey)
            ?? TryFormatBossLocationKey(normalizedKey, pokemonNameResolver)
            ?? TryFormatDungeonLocationKey(normalizedKey)
            ?? TryFormatStoryLocationKey(normalizedKey, missionTitleResolver)
            ?? FormatLumioseArea(normalizedKey)
            ?? FormatRawSpawnerId(normalizedKey, pokemonNameResolver, missionTitleResolver);
    }

    public static string FormatPlacementMap(
        string fallback,
        string? zoneId,
        string? variationId,
        string? dungeonName,
        string? battleAreaId,
        string? spawnerId,
        Func<string, string?>? placeNameResolver = null,
        Func<int, string>? pokemonNameResolver = null,
        Func<ZaMissionDescriptor, string>? missionTitleResolver = null)
    {
        var zoneKey = CreateZoneKey(zoneId, variationId);
        if (!string.IsNullOrWhiteSpace(zoneKey))
        {
            var location = FormatLocation(zoneKey, placeNameResolver, pokemonNameResolver, missionTitleResolver);
            var districtSector = FormatDistrictSector(zoneKey);
            return string.IsNullOrWhiteSpace(districtSector)
                ? location
                : $"{location} - {districtSector}";
        }

        if (!string.IsNullOrWhiteSpace(dungeonName))
        {
            return FormatLocation(dungeonName, placeNameResolver, pokemonNameResolver, missionTitleResolver);
        }

        if (!string.IsNullOrWhiteSpace(battleAreaId))
        {
            return FormatLocation(battleAreaId, placeNameResolver, pokemonNameResolver, missionTitleResolver);
        }

        return string.IsNullOrWhiteSpace(spawnerId)
            ? fallback
            : FormatLocation(
                spawnerId,
                placeNameResolver,
                pokemonNameResolver,
                missionTitleResolver);
    }

    public static int? GetLocationSort(string locationKey)
    {
        if (TryGetWildZoneNumber(locationKey, out var zoneNumber))
        {
            return zoneNumber;
        }

        return TryGetSideMission(locationKey, out var mission)
            ? mission.Kind == ZaMissionKind.ExtraSide ? 1000 + mission.Number : mission.Number
            : null;
    }

    public static string? GetMissionDetails(string? value)
    {
        return TryGetSideMission(value, out var mission)
            ? mission.DisplayReference
            : null;
    }

    public static bool TryGetSideMission(string? value, out ZaMissionDescriptor mission)
    {
        mission = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var tokens = value.Trim().Split('_', StringSplitOptions.RemoveEmptyEntries);
        var index = 0;
        while (index < tokens.Length
            && (tokens[index].Equals("id", StringComparison.OrdinalIgnoreCase)
                || tokens[index].Equals("spn", StringComparison.OrdinalIgnoreCase)
                || tokens[index].Equals("ect", StringComparison.OrdinalIgnoreCase)
                || tokens[index].Equals("btl", StringComparison.OrdinalIgnoreCase)
                || tokens[index].Equals("sys", StringComparison.OrdinalIgnoreCase)
                || tokens[index].Equals("ev", StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        if (index >= tokens.Length)
        {
            return false;
        }

        if (TryParseMissionToken(tokens, index, "rest", out var restaurantNumber))
        {
            return restaurantNumber is >= 1 and <= 4
                && ZaMissionCatalog.TryGetSideMissionByInternalId(80 + restaurantNumber, out mission);
        }

        return (TryParseMissionToken(tokens, index, "subq", out var internalId)
                || TryParseMissionToken(tokens, index, "sub", out internalId))
            && ZaMissionCatalog.TryGetSideMissionByInternalId(internalId, out mission);
    }

    public static bool IsNumberedWildZone(string locationKey)
    {
        return TryGetWildZoneNumber(locationKey, out _);
    }

    public static string? FormatKnownLocation(string? locationKey)
    {
        if (string.IsNullOrWhiteSpace(locationKey))
        {
            return null;
        }

        return KnownLocationNames.TryGetValue(NormalizeLocationKey(locationKey), out var label)
            ? label
            : null;
    }

    public static string? FormatDistrict(string? locationKey)
    {
        return TryParseLumioseArea(locationKey, out var area)
            ? FormatDistrictName(area.District)
            : null;
    }

    public static string? FormatSector(string? locationKey)
    {
        return TryParseLumioseArea(locationKey, out var area)
            ? $"Sector {area.Sector.ToString(CultureInfo.InvariantCulture)}"
            : null;
    }

    public static string? FormatDistrictSector(string? locationKey)
    {
        return TryParseLumioseArea(locationKey, out var area)
            ? FormatDistrictSector(area)
            : null;
    }

    public static string FormatRawObjectName(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("itemball_spawn_", StringComparison.OrdinalIgnoreCase))
        {
            return $"Item Ball Spawn {trimmed["itemball_spawn_".Length..].ToUpperInvariant()}";
        }

        if (trimmed.StartsWith("wild_spawn_", StringComparison.OrdinalIgnoreCase))
        {
            return $"Wild Spawn {trimmed["wild_spawn_".Length..].ToUpperInvariant()}";
        }

        return FormatRawSpawnerId(trimmed);
    }

    public static string FormatRawSpawnerId(
        string value,
        Func<int, string>? pokemonNameResolver = null,
        Func<ZaMissionDescriptor, string>? missionTitleResolver = null)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("id_spn_", StringComparison.Ordinal))
        {
            trimmed = trimmed["id_spn_".Length..];
        }
        var spawnerTrimmed = StripSpawnerPrefix(trimmed);

        var randomSpawner = TryFormatZdmRandomSpawnerId(trimmed, pokemonNameResolver);
        if (randomSpawner is not null)
        {
            return randomSpawner;
        }

        var boss = TryFormatBossSpawnerId(trimmed, pokemonNameResolver);
        if (boss is not null)
        {
            return boss;
        }

        var outzone = TryFormatOutzoneSpawnerId(spawnerTrimmed);
        if (outzone is not null)
        {
            return outzone;
        }

        var wildZone = TryFormatWildZoneSpawnerId(spawnerTrimmed);
        if (wildZone is not null)
        {
            return wildZone;
        }

        var knownLocation = TryFormatKnownLocationSpawnerId(spawnerTrimmed);
        if (knownLocation is not null)
        {
            return knownLocation;
        }

        var dungeon = TryFormatDungeonSpawnerId(spawnerTrimmed);
        if (dungeon is not null)
        {
            return dungeon;
        }

        var story = TryFormatStorySpawnerId(spawnerTrimmed, missionTitleResolver);
        if (story is not null)
        {
            return story;
        }

        return ToReadableId(trimmed);
    }

    public static string FormatRawSpawnerLocationKey(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("id_spn_", StringComparison.Ordinal))
        {
            trimmed = trimmed["id_spn_".Length..];
        }

        var randomSpawnerGroupKey = TryFormatZdmRandomGroupKey(trimmed);
        if (randomSpawnerGroupKey is not null)
        {
            return randomSpawnerGroupKey;
        }

        var bossGroupKey = TryFormatBossGroupKey(trimmed);
        if (bossGroupKey is not null)
        {
            return bossGroupKey;
        }

        var knownLocationGroupKey = TryFormatKnownLocationGroupKey(trimmed);
        if (knownLocationGroupKey is not null)
        {
            return knownLocationGroupKey;
        }

        var groupKey = TryFormatRawSpawnerGroupKey(trimmed);
        if (groupKey is not null)
        {
            return groupKey;
        }

        var storyGroupKey = TryFormatStoryGroupKey(trimmed);
        return storyGroupKey ?? value;
    }

    private static string NormalizeLocationKey(string locationKey)
    {
        var trimmed = locationKey.Trim();
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && IsLumioseAreaCode(parts[0]) && IsWildVariationCode(parts[1]))
        {
            return $"{parts[0]}_{parts[1]}";
        }

        return trimmed;
    }

    private static bool TryGetWildZoneNumber(string locationKey, out int zoneNumber)
    {
        return WildZoneNumbers.TryGetValue(NormalizeLocationKey(locationKey), out zoneNumber);
    }

    private static string? ResolvePlaceName(Func<string, string?>? placeNameResolver, string key)
    {
        var value = placeNameResolver?.Invoke(key);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? FormatLumioseArea(string locationKey)
    {
        if (!TryParseLumioseArea(locationKey, out var area))
        {
            return null;
        }

        var districtSector = FormatDistrictSector(area);
        return area.WildArea is null
            ? districtSector
            : $"{districtSector}, Wild Area {area.WildArea.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string FormatDistrictName(int district)
    {
        return DistrictNames.TryGetValue(district, out var name)
            ? name
            : $"District {district.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string FormatDistrictSector(LumioseArea area)
    {
        return $"{FormatDistrictName(area.District)}, Sector {area.Sector.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string? TryFormatKnownLocationSpawnerId(string value)
    {
        var tokens = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (!TryGetKnownLocationPrefix(tokens, out _, out var label, out var prefixLength))
        {
            return null;
        }

        var tail = FormatSpawnerIdTail(tokens.Skip(prefixLength));
        return string.IsNullOrWhiteSpace(tail)
            ? label
            : $"{label} {tail}";
    }

    private static string? TryFormatKnownLocationGroupKey(string value)
    {
        var tokens = StripSpawnerPrefix(value).Split('_', StringSplitOptions.RemoveEmptyEntries);
        return TryGetKnownLocationPrefix(tokens, out var locationKey, out _, out _)
            ? locationKey
            : null;
    }

    private static bool TryGetKnownLocationPrefix(
        IReadOnlyList<string> tokens,
        out string locationKey,
        out string label,
        out int prefixLength)
    {
        locationKey = string.Empty;
        label = string.Empty;
        prefixLength = 0;
        for (var count = tokens.Count; count > 0; count--)
        {
            var candidate = string.Join('_', tokens.Take(count));
            if (!KnownLocationNames.TryGetValue(candidate, out var knownLabel))
            {
                continue;
            }

            locationKey = candidate.ToLowerInvariant();
            label = knownLabel;
            prefixLength = count;
            return true;
        }

        return false;
    }

    private static bool TryParseLumioseArea(string? locationKey, out LumioseArea area)
    {
        area = default;
        if (string.IsNullOrWhiteSpace(locationKey))
        {
            return false;
        }

        var parts = NormalizeLocationKey(locationKey).Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !IsLumioseAreaCode(parts[0]))
        {
            return false;
        }

        var district = int.Parse(parts[0].Substring(1, 2), CultureInfo.InvariantCulture);
        var sector = int.Parse(parts[0].Substring(3, 2), CultureInfo.InvariantCulture);
        int? wildArea = null;
        foreach (var part in parts.Skip(1))
        {
            if (IsWildVariationCode(part))
            {
                wildArea = int.Parse(part[1..], CultureInfo.InvariantCulture);
                break;
            }
        }

        area = new LumioseArea(district, sector, wildArea);
        return true;
    }

    private static bool IsLumioseAreaCode(string value)
    {
        return value.Length == 5
            && value[0] is 'a' or 'A'
            && value.Skip(1).All(char.IsDigit);
    }

    private static bool IsWildVariationCode(string value)
    {
        return value.Length > 1
            && value[0] is 'w' or 'W'
            && value[1..].All(char.IsDigit);
    }

    private static string? TryFormatRawSpawnerGroupKey(string value)
    {
        var parts = StripSpawnerPrefix(value).Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3
            && string.Equals(parts[0], "random", StringComparison.OrdinalIgnoreCase)
            && parts[1].StartsWith('z')
            && parts[1].Length > 1
            && IsDungeonSpawnerSection(parts[2]))
        {
            return string.Join('_', parts.Skip(1).Take(2));
        }

        if (parts.Length >= 2 && string.Equals(parts[0], "outzone", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join('_', parts.Take(2));
        }

        if (parts.Length >= 2 && IsLumioseAreaCode(parts[0]) && IsWildVariationCode(parts[1]))
        {
            return string.Join('_', parts.Take(2));
        }

        if (parts.Length >= 2
            && parts[0].StartsWith('z')
            && parts[0].Length > 1
            && IsDungeonSpawnerSection(parts[1]))
        {
            return string.Join('_', parts.Take(2));
        }

        if (parts.Length >= 2 && parts[0].StartsWith('d') && parts[0].Length > 1 && parts[1].All(char.IsDigit))
        {
            return string.Join('_', parts.Take(2));
        }

        return null;
    }

    private static string StripSpawnerPrefix(string value)
    {
        return value.StartsWith("spn_", StringComparison.OrdinalIgnoreCase)
            ? value["spn_".Length..]
            : value;
    }

    private static string? TryFormatZdmRandomGroupKey(string value)
    {
        return TryParseZdmRandomSpawnerId(value, out _)
            ? ZdmRandomLocationKey
            : null;
    }

    private static string? TryFormatZdmRandomSpawnerId(string value, Func<int, string>? pokemonNameResolver = null)
    {
        if (!TryParseZdmRandomSpawnerId(value, out var spawner))
        {
            return null;
        }

        var location = FormatZdmRandomLocation(spawner);
        var tail = FormatZdmRandomTail(spawner.Tail, pokemonNameResolver);
        return string.IsNullOrWhiteSpace(tail)
            ? location
            : $"{location}, {tail}";
    }

    private static bool TryParseZdmRandomSpawnerId(string value, out ZdmRandomSpawnerId spawner)
    {
        spawner = default;
        var parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            return false;
        }

        var index = string.Equals(parts[0], "id", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (parts.Length - index < 3
            || !string.Equals(parts[index], "zdm", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[index + 1], "random", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int? pool = null;
        int? rank = null;
        var tailStart = -1;
        for (var partIndex = index + 2; partIndex < parts.Length; partIndex++)
        {
            var part = parts[partIndex];
            if (pool is null && TryParsePrefixedNumber(part, 't', out var parsedPool))
            {
                pool = parsedPool;
                continue;
            }

            if (rank is null && TryParsePrefixedNumber(part, 'r', out var parsedRank))
            {
                rank = parsedRank;
                continue;
            }

            tailStart = partIndex;
            break;
        }

        if (pool is null)
        {
            return false;
        }

        spawner = new ZdmRandomSpawnerId(
            pool.Value,
            rank,
            tailStart < 0 ? [] : parts[tailStart..]);
        return true;
    }

    private static bool TryParsePrefixedNumber(string value, char prefix, out int number)
    {
        number = 0;
        return value.Length > 1
            && char.ToLowerInvariant(value[0]) == prefix
            && int.TryParse(value[1..], NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    private static string FormatZdmRandomLocation(ZdmRandomSpawnerId spawner)
    {
        var poolName = DimensionWildTypePoolNames.TryGetValue(spawner.Pool, out var name)
            ? name
            : "Dimension Wild Type Pool";
        var location = $"{poolName} {spawner.Pool.ToString(CultureInfo.InvariantCulture)}";
        return spawner.Rank is null
            ? location
            : $"{location}, Rank {spawner.Rank.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string? TryFormatZdmRandomLocationKey(string locationKey)
    {
        return string.Equals(locationKey, ZdmRandomLocationKey, StringComparison.Ordinal)
            ? ZdmRandomLocationLabel
            : null;
    }

    private static string FormatZdmRandomTail(
        IReadOnlyList<string> tail,
        Func<int, string>? pokemonNameResolver = null)
    {
        if (tail.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        var index = 0;
        if (tail[0].All(char.IsDigit))
        {
            parts.Add(FormatPokemonReference(tail[0], pokemonNameResolver));
            index = 1;
        }

        for (; index < tail.Count; index++)
        {
            var token = tail[index];
            if (token.StartsWith("set", StringComparison.OrdinalIgnoreCase))
            {
                var setNumber = token["set".Length..];
                parts.Add(string.IsNullOrWhiteSpace(setNumber) ? "Set" : $"Set {setNumber}");
                continue;
            }

            if (token.All(char.IsDigit))
            {
                parts.Add($"Form {token}");
                continue;
            }

            parts.Add(FormatSpawnerIdToken(token));
        }

        return parts.Count == 0 ? string.Empty : string.Join(" ", parts);
    }

    private static string? TryFormatOutzoneSpawnerId(string value)
    {
        var parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !string.Equals(parts[0], "outzone", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var label = FormatOutzoneArea(parts[1]);
        var tail = FormatOutzoneTail(parts.Skip(2).ToArray());
        return parts.Length == 2
            ? label
            : $"{label}, {tail}";
    }

    private static string? TryFormatWildZoneSpawnerId(string value)
    {
        var parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !IsLumioseAreaCode(parts[0]) || !IsWildVariationCode(parts[1]))
        {
            return null;
        }

        var label = FormatLocation($"{parts[0]}_{parts[1]}");
        var tail = FormatWildZoneTail(parts.Skip(2));
        return string.IsNullOrWhiteSpace(tail)
            ? label
            : $"{label} {tail}";
    }

    private static string FormatWildZoneTail(IEnumerable<string> tokens)
    {
        var parts = tokens
            .Select(token =>
            {
                return TryParsePrefixedNumber(token, 'v', out var variant)
                    ? $"Variant {variant.ToString(CultureInfo.InvariantCulture)}"
                    : FormatSpawnerIdToken(token);
            })
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
        return parts.Length == 0 ? string.Empty : string.Join(" ", parts);
    }

    private static string? TryFormatDungeonSpawnerId(string value)
    {
        var parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        if (parts.Length >= 3
            && string.Equals(parts[0], "random", StringComparison.OrdinalIgnoreCase)
            && parts[1].StartsWith('z')
            && parts[1].Length > 1
            && IsDungeonSpawnerSection(parts[2]))
        {
            var dungeon = FormatDimensionDungeonLocation(parts[1], parts[2]);
            var tail = FormatSpawnerIdTail(parts.Skip(3));
            return string.IsNullOrWhiteSpace(tail)
                ? dungeon
                : $"{dungeon} {tail}";
        }

        if (parts[0].StartsWith('z') && parts[0].Length > 1 && IsDungeonSpawnerSection(parts[1]))
        {
            var dungeon = FormatDimensionDungeonLocation(parts[0], parts[1]);
            var tail = FormatSpawnerIdTail(parts.Skip(2));
            return string.IsNullOrWhiteSpace(tail)
                ? dungeon
                : $"{dungeon} {tail}";
        }

        if (parts[0].StartsWith('d') && parts[0].Length > 1 && parts[1].All(char.IsDigit))
        {
            var dungeon = FormatMainDungeonLocation(parts[0], parts[1]);
            var tail = FormatSpawnerIdTail(parts.Skip(2));
            return string.IsNullOrWhiteSpace(tail)
                ? dungeon
                : $"{dungeon} {tail}";
        }

        return null;
    }

    private static string? TryFormatBossGroupKey(string value)
    {
        return TryParseBossSpawnerId(value, out var boss)
            ? boss.LocationKey
            : null;
    }

    private static string? TryFormatBossSpawnerId(string value, Func<int, string>? pokemonNameResolver = null)
    {
        if (!TryParseBossSpawnerId(value, out var boss))
        {
            return null;
        }

        var location = FormatBossLocation(boss, pokemonNameResolver);
        var tail = FormatSpawnerIdTail(boss.Tail);
        return string.IsNullOrWhiteSpace(tail)
            ? location
            : $"{location} {tail}";
    }

    private static string? TryFormatBossLocationKey(string locationKey, Func<int, string>? pokemonNameResolver = null)
    {
        return TryParseBossSpawnerId(locationKey, out var boss)
            ? FormatBossLocation(boss, pokemonNameResolver)
            : null;
    }

    private static string? TryFormatOutzoneLocationKey(string locationKey)
    {
        var parts = locationKey.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !string.Equals(parts[0], "outzone", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return FormatOutzoneArea(parts[1]);
    }

    private static string? TryFormatDungeonLocationKey(string locationKey)
    {
        var parts = locationKey.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return null;
        }

        if (parts[0].StartsWith('z') && parts[0].Length > 1 && IsDungeonSpawnerSection(parts[1]))
        {
            return FormatDimensionDungeonLocation(parts[0], parts[1]);
        }

        return parts[0].StartsWith('d') && parts[0].Length > 1 && parts[1].All(char.IsDigit)
            ? FormatMainDungeonLocation(parts[0], parts[1])
            : null;
    }

    private static string? TryFormatStoryGroupKey(string value)
    {
        var tokens = SplitSpawnerIdTokens(value);
        if (tokens.Length == 0)
        {
            return null;
        }

        if (IsDimensionMegaEvent(tokens))
        {
            return DimensionMegaEventsLocationKey;
        }

        if (TryParseNumberedToken(tokens[0], "chapter", out var chapter))
        {
            return $"id_chapter{chapter}";
        }

        if (TryParseNumberedToken(tokens[0], "rest", out var rest))
        {
            return $"id_rest{rest}";
        }

        if (TryParseNumberedToken(tokens[0], "sub", out var sideMission))
        {
            return $"id_sub{sideMission}";
        }

        if (TryParseNumberedToken(tokens[0], "subq", out var sideQuest))
        {
            return $"id_spn_subq{sideQuest}";
        }

        if (tokens.Length >= 3 && string.Equals(tokens[0], "dlc", StringComparison.OrdinalIgnoreCase))
        {
            return $"id_dlc_{tokens[1]}_{tokens[2]}";
        }

        return IsRoseDedeEvent(tokens)
            ? "id_10rom_poke_spawner_rose_dede"
            : null;
    }

    private static string? TryFormatStorySpawnerId(
        string value,
        Func<ZaMissionDescriptor, string>? missionTitleResolver)
    {
        var tokens = SplitSpawnerIdTokens(value);
        if (tokens.Length == 0)
        {
            return null;
        }

        if (IsDimensionMegaEvent(tokens))
        {
            var eventNumber = tokens[1]["mega".Length..];
            return string.IsNullOrWhiteSpace(eventNumber)
                ? "Dimension Mega Event"
                : $"Dimension Mega Event {eventNumber}";
        }

        if (TryParseNumberedToken(tokens[0], "chapter", out var chapter))
        {
            return AppendFormattedTail($"Story Chapter Event {FormatNumberForLabel(chapter)}", tokens.Skip(1));
        }

        if (TryParseNumberedToken(tokens[0], "rest", out var rest))
        {
            return TryResolveRestaurantMission(rest, missionTitleResolver, out var restaurantTitle)
                ? FormatRestaurantMissionLabel(restaurantTitle, tokens.Skip(1))
                : AppendFormattedTail($"Rest Event {FormatNumberForLabel(rest)}", tokens.Skip(1));
        }

        if (TryParseNumberedToken(tokens[0], "sub", out var sideMission))
        {
            return TryResolveInternalSideMission(sideMission, missionTitleResolver, out var missionTitle)
                ? AppendFormattedTail(missionTitle, tokens.Skip(1))
                : AppendFormattedTail($"Side Mission Event {FormatNumberForLabel(sideMission)}", tokens.Skip(1));
        }

        if (TryParseNumberedToken(tokens[0], "subq", out var sideQuest))
        {
            return TryResolveInternalSideMission(sideQuest, missionTitleResolver, out var missionTitle)
                ? AppendFormattedTail(missionTitle, tokens.Skip(1))
                : AppendFormattedTail($"Side Mission Event {FormatNumberForLabel(sideQuest)}", tokens.Skip(1));
        }

        if (tokens.Length >= 3 && string.Equals(tokens[0], "dlc", StringComparison.OrdinalIgnoreCase))
        {
            return AppendFormattedTail($"DLC Event {FormatNumberForLabel(tokens[1])}.{FormatNumberForLabel(tokens[2])}", tokens.Skip(3));
        }

        return IsRoseDedeEvent(tokens)
            ? "Story Event Rose Dede"
            : null;
    }

    private static string? TryFormatStoryLocationKey(
        string locationKey,
        Func<ZaMissionDescriptor, string>? missionTitleResolver)
    {
        if (string.Equals(locationKey, DimensionMegaEventsLocationKey, StringComparison.Ordinal))
        {
            return "Dimension Mega Events";
        }

        var tokens = SplitSpawnerIdTokens(locationKey);
        if (tokens.Length == 0)
        {
            return null;
        }

        if (TryParseNumberedToken(tokens[0], "chapter", out var chapter))
        {
            return $"Story Chapter Event {FormatNumberForLabel(chapter)}";
        }

        if (TryParseNumberedToken(tokens[0], "rest", out var rest))
        {
            return TryResolveRestaurantMission(rest, missionTitleResolver, out var restaurantTitle)
                ? FormatRestaurantMissionLabel(restaurantTitle, tokens.Skip(1))
                : $"Rest Event {FormatNumberForLabel(rest)}";
        }

        if (TryParseNumberedToken(tokens[0], "sub", out var sideMission))
        {
            return TryResolveInternalSideMission(sideMission, missionTitleResolver, out var missionTitle)
                ? missionTitle
                : $"Side Mission Event {FormatNumberForLabel(sideMission)}";
        }

        if (TryParseNumberedToken(tokens[0], "subq", out var sideQuest))
        {
            return TryResolveInternalSideMission(sideQuest, missionTitleResolver, out var missionTitle)
                ? missionTitle
                : $"Side Mission Event {FormatNumberForLabel(sideQuest)}";
        }

        if (tokens.Length >= 3 && string.Equals(tokens[0], "dlc", StringComparison.OrdinalIgnoreCase))
        {
            return $"DLC Event {FormatNumberForLabel(tokens[1])}.{FormatNumberForLabel(tokens[2])}";
        }

        return IsRoseDedeEvent(tokens)
            ? "Story Event Rose Dede"
            : null;
    }

    private static string FormatRestaurantMissionLabel(
        string title,
        IEnumerable<string> tailTokens)
    {
        var tokens = tailTokens.ToArray();
        if (tokens.Length == 0)
        {
            return title;
        }

        if (int.TryParse(tokens[0], NumberStyles.None, CultureInfo.InvariantCulture, out var battle)
            && battle is >= 1 and <= 5)
        {
            return AppendFormattedTail(
                $"{title} Battle {battle.ToString(CultureInfo.InvariantCulture)}",
                tokens.Skip(1));
        }

        return AppendFormattedTail(title, tokens);
    }

    private static bool TryResolveRestaurantMission(
        string number,
        Func<ZaMissionDescriptor, string>? missionTitleResolver,
        out string title)
    {
        title = string.Empty;
        return int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            && parsed is >= 1 and <= 4
            && TryResolveSideMission(80 + parsed, missionTitleResolver, out title);
    }

    private static bool TryResolveInternalSideMission(
        string internalId,
        Func<ZaMissionDescriptor, string>? missionTitleResolver,
        out string title)
    {
        title = string.Empty;
        return int.TryParse(internalId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            && TryResolveSideMission(parsed, missionTitleResolver, out title);
    }

    private static bool TryResolveSideMission(
        int internalId,
        Func<ZaMissionDescriptor, string>? missionTitleResolver,
        out string title)
    {
        title = string.Empty;
        if (!ZaMissionCatalog.TryGetSideMissionByInternalId(internalId, out var mission))
        {
            return false;
        }

        title = missionTitleResolver?.Invoke(mission) ?? mission.ResolveTitle(localizedTitle: null);
        return true;
    }

    private static string FormatOutzoneArea(string areaCode)
    {
        var districtSector = FormatDistrictSector(areaCode);
        return string.IsNullOrWhiteSpace(districtSector)
            ? $"Outside Wild Zone {areaCode.ToUpperInvariant()}"
            : $"{districtSector} Outside Wild Zone";
    }

    private static string FormatDimensionDungeonLocation(string dungeonId, string section)
    {
        return $"Dimension Dungeon {FormatDimensionDungeonId(dungeonId)} {FormatDimensionDungeonSection(section)}";
    }

    private static string FormatDimensionDungeonId(string dungeonId)
    {
        var digits = dungeonId.StartsWith("zdm", StringComparison.OrdinalIgnoreCase)
            ? dungeonId["zdm".Length..]
            : dungeonId;
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedDungeon)
            ? parsedDungeon.ToString(CultureInfo.InvariantCulture)
            : dungeonId.ToUpperInvariant();
    }

    private static string FormatDimensionDungeonSection(string section)
    {
        if (TryParsePrefixedNumber(section, 'v', out var variant))
        {
            return $"Variant {variant.ToString(CultureInfo.InvariantCulture)}";
        }

        if (section.StartsWith("poke", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(section["poke".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var pokemonSet))
        {
            return $"Pokemon Set {pokemonSet.ToString(CultureInfo.InvariantCulture)}";
        }

        if (section.StartsWith("sp", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(section["sp".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var specialArea))
        {
            return $"Special Area {specialArea.ToString(CultureInfo.InvariantCulture)}";
        }

        return section.ToUpperInvariant();
    }

    private static string FormatMainDungeonLocation(string dungeonId, string floorId)
    {
        var dungeonNumber = int.TryParse(dungeonId[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedDungeon)
            ? parsedDungeon.ToString(CultureInfo.InvariantCulture)
            : dungeonId.ToUpperInvariant();
        var floorNumber = int.TryParse(floorId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedFloor)
            ? parsedFloor.ToString(CultureInfo.InvariantCulture)
            : floorId;
        return $"Dungeon {dungeonNumber} Floor {floorNumber}";
    }

    private static bool IsDungeonSpawnerSection(string value)
    {
        return value.StartsWith("sp", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("poke", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("v", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatSpawnerIdTail(IEnumerable<string> tokens)
    {
        var parts = tokens
            .Select(FormatSpawnerIdToken)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
        return parts.Length == 0 ? string.Empty : string.Join(" ", parts);
    }

    private static string AppendFormattedTail(string label, IEnumerable<string> tokens)
    {
        var tail = FormatSpawnerIdTail(tokens);
        return string.IsNullOrWhiteSpace(tail)
            ? label
            : $"{label} {tail}";
    }

    private static string FormatSpawnerIdToken(string token)
    {
        if (token.All(char.IsDigit))
        {
            return $"Spawn Point {token}";
        }

        if (token.Length > 1
            && token.SkipLast(1).All(char.IsDigit)
            && char.IsAsciiLetter(token[^1]))
        {
            return $"Spawn Point {token.ToUpperInvariant()}";
        }

        if (token.Length > 1
            && (token[0] is 'A' or 'a')
            && token[1..].All(char.IsDigit))
        {
            return $"Alpha Spawn Point {token[1..]}";
        }

        if (token.StartsWith("follower", StringComparison.OrdinalIgnoreCase))
        {
            var followerNumber = token["follower".Length..];
            return string.IsNullOrWhiteSpace(followerNumber)
                ? "Follower"
                : $"Follower {followerNumber}";
        }

        if (token.Length > 1
            && (token[0] is 'D' or 'd')
            && token[1..].All(char.IsDigit)
            && int.TryParse(token[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var dungeonNumber))
        {
            return $"Dungeon {dungeonNumber.ToString(CultureInfo.InvariantCulture)}";
        }

        return token switch
        {
            _ when string.Equals(token, "ev", StringComparison.OrdinalIgnoreCase) => "Event",
            _ when string.Equals(token, "A", StringComparison.OrdinalIgnoreCase) => "Alpha",
            _ when string.Equals(token, "BZ", StringComparison.OrdinalIgnoreCase) => "Battle Zone",
            _ when string.Equals(token, "DT", StringComparison.OrdinalIgnoreCase) => "Daytime",
            _ when string.Equals(token, "EV", StringComparison.OrdinalIgnoreCase) => "Event",
            _ when string.Equals(token, "FLY", StringComparison.OrdinalIgnoreCase) => "Flying Spawn",
            _ when string.Equals(token, "NT", StringComparison.OrdinalIgnoreCase) => "Nighttime",
            _ when string.Equals(token, "PH", StringComparison.OrdinalIgnoreCase) => "Phase Condition",
            _ when string.Equals(token, "RE", StringComparison.OrdinalIgnoreCase) => "Rematch",
            _ when string.Equals(token, "SIM", StringComparison.OrdinalIgnoreCase) => "Simulation",
            _ when string.Equals(token, "SIM2", StringComparison.OrdinalIgnoreCase) => "Simulation 2",
            _ when string.Equals(token, "SPN", StringComparison.OrdinalIgnoreCase) => string.Empty,
            _ when string.Equals(token, "TUTORIAL", StringComparison.OrdinalIgnoreCase) => "Tutorial",
            _ when string.Equals(token, "WT", StringComparison.OrdinalIgnoreCase) => "Weather Condition",
            _ when token.Length == 1 && char.IsAsciiLetter(token[0]) => $"Variant {token.ToUpperInvariant()}",
            _ => token.ToUpperInvariant(),
        };
    }

    private static string FormatOutzoneTail(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        var first = tokens[0];
        if (first.All(char.IsDigit))
        {
            parts.Add($"Spawn Point {first}");
        }
        else if (first.Length > 1
            && (first[0] is 'A' or 'a')
            && first[1..].All(char.IsDigit))
        {
            parts.Add($"Alpha Spawn Point {first[1..]}");
        }
        else if (first.StartsWith("sp", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(first["sp".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var specialSpawn))
        {
            parts.Add($"Special Encounter {specialSpawn.ToString(CultureInfo.InvariantCulture)}");
        }
        else if (TryParseLetterSpawnPoint(first, out var group, out var point))
        {
            parts.Add($"Spawn Group {group}, Point {point}");
        }
        else
        {
            parts.Add(FormatSpawnerIdToken(first));
        }

        foreach (var token in tokens.Skip(1))
        {
            if (token.All(char.IsDigit))
            {
                parts.Add($"Variant {token}");
                continue;
            }

            parts.Add(FormatSpawnerIdToken(token));
        }

        return string.Join(", ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static bool TryParseLetterSpawnPoint(string token, out string group, out string point)
    {
        group = string.Empty;
        point = string.Empty;
        if (token.Length < 2
            || !char.IsAsciiLetter(token[0])
            || !token[1..].All(char.IsDigit))
        {
            return false;
        }

        group = char.ToUpperInvariant(token[0]).ToString();
        point = token[1..];
        return true;
    }

    private static bool TryParseBossSpawnerId(string value, out BossSpawnerId boss)
    {
        boss = default;
        var parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        int speciesIndex;
        if (parts.Length >= 4
            && string.Equals(parts[0], "btl", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[1], "spn", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[2], "boss", StringComparison.OrdinalIgnoreCase))
        {
            speciesIndex = 3;
        }
        else if (parts.Length >= 3
            && string.Equals(parts[0], "spn", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[1], "boss", StringComparison.OrdinalIgnoreCase))
        {
            speciesIndex = 2;
        }
        else if (parts.Length >= 2 && string.Equals(parts[0], "boss", StringComparison.OrdinalIgnoreCase))
        {
            speciesIndex = 1;
        }
        else
        {
            return false;
        }

        var species = parts[speciesIndex];
        if (string.IsNullOrWhiteSpace(species))
        {
            return false;
        }

        var variant = new List<string>();
        var tail = new List<string>();
        foreach (var token in parts.Skip(speciesIndex + 1))
        {
            if (tail.Count > 0 || token.StartsWith("follower", StringComparison.OrdinalIgnoreCase))
            {
                tail.Add(token);
                continue;
            }

            variant.Add(token);
        }

        boss = new BossSpawnerId(species, variant, tail);
        return true;
    }

    private static string FormatBossLocation(BossSpawnerId boss, Func<int, string>? pokemonNameResolver = null)
    {
        var label = $"Boss Battle {FormatBossSpecies(boss.SpeciesId, pokemonNameResolver)}";
        var variant = FormatBossVariant(boss.Variant);
        return string.IsNullOrWhiteSpace(variant)
            ? label
            : $"{label} {variant}";
    }

    private static string FormatBossSpecies(string speciesId, Func<int, string>? pokemonNameResolver)
    {
        if (!int.TryParse(speciesId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSpecies))
        {
            return speciesId;
        }

        return FormatPokemonReference(parsedSpecies.ToString(CultureInfo.InvariantCulture), pokemonNameResolver);
    }

    private static string FormatBossVariant(IReadOnlyList<string> tokens)
    {
        var parts = tokens
            .Select(FormatBossVariantToken)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
        return parts.Length == 0 ? string.Empty : string.Join(" ", parts);
    }

    private static string FormatBossVariantToken(string token)
    {
        if (token.All(char.IsDigit)
            && int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var phase))
        {
            return $"Phase {phase.ToString(CultureInfo.InvariantCulture)}";
        }

        if (token.StartsWith("rus", StringComparison.OrdinalIgnoreCase))
        {
            var rushNumber = token["rus".Length..];
            return string.IsNullOrWhiteSpace(rushNumber) ? "Rush" : $"Rush {rushNumber}";
        }

        if (token.StartsWith("sim", StringComparison.OrdinalIgnoreCase))
        {
            var simulationNumber = token["sim".Length..];
            return string.IsNullOrWhiteSpace(simulationNumber)
                ? "Simulation"
                : $"Simulation {simulationNumber}";
        }

        return token switch
        {
            _ when string.Equals(token, "dim", StringComparison.OrdinalIgnoreCase) => "Dimension",
            _ when string.Equals(token, "re", StringComparison.OrdinalIgnoreCase) => "Rematch",
            _ when string.Equals(token, "sim", StringComparison.OrdinalIgnoreCase) => "Simulation",
            _ when string.Equals(token, "sim2", StringComparison.OrdinalIgnoreCase) => "Simulation 2",
            _ when string.Equals(token, "y", StringComparison.OrdinalIgnoreCase) => "Y",
            _ when string.Equals(token, "z", StringComparison.OrdinalIgnoreCase) => "Z",
            _ => token.ToUpperInvariant(),
        };
    }

    private static string[] SplitSpawnerIdTokens(string value)
    {
        var parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2
            && string.Equals(parts[0], "id", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[1], "spn", StringComparison.OrdinalIgnoreCase))
        {
            return parts[2..];
        }

        return parts.Length >= 1 && string.Equals(parts[0], "id", StringComparison.OrdinalIgnoreCase)
            ? parts[1..]
            : parts;
    }

    private static bool IsDimensionMegaEvent(string[] tokens)
    {
        return tokens.Length >= 2
            && string.Equals(tokens[0], "izigen", StringComparison.OrdinalIgnoreCase)
            && tokens[1].StartsWith("mega", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRoseDedeEvent(string[] tokens)
    {
        return tokens.Length >= 5
            && string.Equals(tokens[0], "10rom", StringComparison.OrdinalIgnoreCase)
            && string.Equals(tokens[1], "poke", StringComparison.OrdinalIgnoreCase)
            && string.Equals(tokens[2], "spawner", StringComparison.OrdinalIgnoreCase)
            && string.Equals(tokens[3], "rose", StringComparison.OrdinalIgnoreCase)
            && string.Equals(tokens[4], "dede", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseNumberedToken(string token, string prefix, out string number)
    {
        number = string.Empty;
        if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = token[prefix.Length..];
        if (suffix.Length == 0 || !suffix.All(char.IsDigit))
        {
            return false;
        }

        number = suffix;
        return true;
    }

    private static bool TryParseMissionToken(
        IReadOnlyList<string> tokens,
        int index,
        string prefix,
        out int number)
    {
        number = 0;
        if (index >= tokens.Count)
        {
            return false;
        }

        if (TryParseNumberedToken(tokens[index], prefix, out var inlineNumber))
        {
            return int.TryParse(inlineNumber, NumberStyles.None, CultureInfo.InvariantCulture, out number);
        }

        return tokens[index].Equals(prefix, StringComparison.OrdinalIgnoreCase)
            && index + 1 < tokens.Count
            && int.TryParse(tokens[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    private static string FormatNumberForLabel(string number)
    {
        return int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : number;
    }

    private static string FormatPokemonReference(string speciesId, Func<int, string>? pokemonNameResolver)
    {
        if (!int.TryParse(speciesId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSpecies))
        {
            return $"Pokemon {speciesId}";
        }

        var fallback = ZaLabels.Pokemon(parsedSpecies);
        var pokemonName = pokemonNameResolver?.Invoke(parsedSpecies);
        return string.IsNullOrWhiteSpace(pokemonName) || string.Equals(pokemonName, fallback, StringComparison.Ordinal)
            ? fallback
            : $"{pokemonName} ({parsedSpecies.ToString(CultureInfo.InvariantCulture)})";
    }

    private static string ToReadableId(string value)
    {
        return value
            .Replace('_', ' ')
            .Trim();
    }

    private readonly record struct LumioseArea(
        int District,
        int Sector,
        int? WildArea);

    private readonly record struct ZdmRandomSpawnerId(
        int Pool,
        int? Rank,
        IReadOnlyList<string> Tail);

    private readonly record struct BossSpawnerId(
        string SpeciesId,
        IReadOnlyList<string> Variant,
        IReadOnlyList<string> Tail)
    {
        public string LocationKey
        {
            get
            {
                var tokens = new[] { "boss", SpeciesId }.Concat(Variant);
                return string.Join('_', tokens).ToLowerInvariant();
            }
        }
    }
}
