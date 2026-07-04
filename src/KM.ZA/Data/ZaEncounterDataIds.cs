// SPDX-License-Identifier: GPL-3.0-only

namespace KM.ZA.Data;

internal static class ZaEncounterDataIds
{
    private const string AlphaSpawnerVariantSuffix = "_Alpha";

    private static readonly string[] SpawnerVariantSuffixes =
    [
        AlphaSpawnerVariantSuffix,
    ];

    public static bool IsAlphaSpawnerEncounterDataId(string? encounterDataId)
    {
        return HasSpawnerVariantSuffix(encounterDataId, AlphaSpawnerVariantSuffix);
    }

    public static string NormalizeSpawnerEncounterDataId(string encounterDataId)
    {
        foreach (var suffix in SpawnerVariantSuffixes)
        {
            if (encounterDataId.EndsWith(suffix, StringComparison.Ordinal))
            {
                return encounterDataId[..^suffix.Length];
            }
        }

        return encounterDataId;
    }

    public static void AddSpawnerEncounterDataTargets(ISet<string> encounterDataIds, string? encounterDataId)
    {
        if (string.IsNullOrWhiteSpace(encounterDataId))
        {
            return;
        }

        encounterDataIds.Add(encounterDataId);
        encounterDataIds.Add(NormalizeSpawnerEncounterDataId(encounterDataId));
    }

    private static bool HasSpawnerVariantSuffix(string? encounterDataId, string suffix)
    {
        return !string.IsNullOrWhiteSpace(encounterDataId)
            && encounterDataId.EndsWith(suffix, StringComparison.Ordinal);
    }
}
