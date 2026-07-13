// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.ZA.Generated.Field.PokemonSpawner;

namespace KM.ZA.Data;

internal static class ZaPokemonSpawnerDisplayOrder
{
    public static IReadOnlyDictionary<(int GroupIndex, int SpawnerIndex), ZaPokemonSpawnerDisplayPosition> Create(
        PokemonSpawnerDataDBArray table)
    {
        var positions = new Dictionary<(int GroupIndex, int SpawnerIndex), ZaPokemonSpawnerDisplayPosition>();
        var countsByLocation = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var groupIndex = 0; groupIndex < table.ValuesLength; groupIndex++)
        {
            var db = table.Values(groupIndex);
            if (db is null)
            {
                continue;
            }

            for (var spawnerIndex = 0; spawnerIndex < db.Value.RootLength; spawnerIndex++)
            {
                var spawner = db.Value.Root(spawnerIndex);
                if (spawner is null)
                {
                    continue;
                }

                var locationKey = CreateLocationKey(spawner.Value);
                countsByLocation.TryGetValue(locationKey, out var current);
                var ordinal = current + 1;
                countsByLocation[locationKey] = ordinal;
                positions.Add(
                    (groupIndex, spawnerIndex),
                    new ZaPokemonSpawnerDisplayPosition(locationKey, ordinal));
            }
        }

        return positions;
    }

    private static string CreateLocationKey(PokemonSpawnerData spawner)
    {
        var objectInfo = FirstAppearanceObject(spawner);
        var zoneInfo = objectInfo?.ZoneInfo;
        return ZaLumioseLocationLabels.CreateLocationKey(
            zoneInfo?.ZoneId,
            zoneInfo?.VariationId,
            spawner.Id);
    }

    private static AppearanceSpawnerObjectInfo? FirstAppearanceObject(PokemonSpawnerData spawner)
    {
        for (var index = 0; index < spawner.AppearanceSpawnerObjectInfoListLength; index++)
        {
            var objectInfo = spawner.AppearanceSpawnerObjectInfoList(index);
            if (objectInfo is not null)
            {
                return objectInfo;
            }
        }

        return null;
    }
}

internal readonly record struct ZaPokemonSpawnerDisplayPosition(
    string LocationKey,
    int Ordinal);
