// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.ZA.Generated.GameData;

namespace KM.Formats.ZA;

public static class ZaPersonalLegacyRecovery
{
    public static IReadOnlyDictionary<(ushort Species, ushort Form), ZaPersonal?> CreateUniqueBaseRowsBySpecies(
        ZaPersonalTable table)
    {
        var rowsBySpecies = new Dictionary<(ushort Species, ushort Form), ZaPersonal?>();
        for (var index = 0; index < table.EntryLength; index++)
        {
            if (table.Entry(index) is not { } entry || entry.Species is not { } species)
            {
                continue;
            }

            var key = (species.Species, species.Form);
            if (!rowsBySpecies.TryAdd(key, entry))
            {
                rowsBySpecies[key] = null;
            }
        }

        return rowsBySpecies;
    }

    public static ZaPersonal? FindBaseRow(
        ZaPersonal? row,
        ZaPersonal? indexedBaseRow,
        IReadOnlyDictionary<(ushort Species, ushort Form), ZaPersonal?>? baseRowsBySpecies)
    {
        if (row is not { Species: { } species })
        {
            return indexedBaseRow;
        }

        if (indexedBaseRow is { Species: { } indexedSpecies }
            && SpeciesIdentityMatches(species, indexedSpecies))
        {
            return indexedBaseRow;
        }

        return baseRowsBySpecies is not null
            && baseRowsBySpecies.TryGetValue((species.Species, species.Form), out var uniqueBaseRow)
                ? uniqueBaseRow
                : null;
    }

    public static ushort ResolveZADexOrder(
        ZaPersonal entry,
        ZaPersonal? baseEntry,
        bool hasLegacyByteDexOrderLayout)
    {
        if (!hasLegacyByteDexOrderLayout)
        {
            return entry.ZADexOrder;
        }

        var lowByteValue = entry.ZADexOrderLowByte;
        if (baseEntry is not { } vanilla
            || !SpeciesIdentityMatches(entry.Species, vanilla.Species)
            || lowByteValue != unchecked((byte)vanilla.ZADexOrder))
        {
            return lowByteValue;
        }

        return vanilla.ZADexOrder;
    }

    public static uint ResolveSpeciesReserved3(
        ZaPersonal entry,
        ZaPersonal? baseEntry,
        bool hasLegacyByteDexOrderLayout)
    {
        if (entry.Species is not { } species)
        {
            return 0;
        }

        if (!hasLegacyByteDexOrderLayout)
        {
            return species.Reserved3;
        }

        return baseEntry is { Species: { } vanillaSpecies }
            && SpeciesIdentityMatches(species, vanillaSpecies)
                ? vanillaSpecies.Reserved3
                : 0;
    }

    public static bool SpeciesIdentityMatches(ZaSpeciesInfo? current, ZaSpeciesInfo? vanilla)
    {
        if (current is not { } currentValue || vanilla is not { } vanillaValue)
        {
            return current is null && vanilla is null;
        }

        return SpeciesIdentityMatches(currentValue, vanillaValue);
    }

    public static bool SpeciesIdentityMatches(ZaSpeciesInfo current, ZaSpeciesInfo vanilla)
    {
        return current.Species == vanilla.Species
            && current.Form == vanilla.Form;
    }
}
