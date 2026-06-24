// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;

namespace KM.ZA.Data;

internal static class ZaLabels
{
    public static string Pokemon(int speciesId) => $"Pokemon {speciesId.ToString(CultureInfo.InvariantCulture)}";

    public static string Move(int moveId) => $"Move {moveId.ToString(CultureInfo.InvariantCulture)}";

    public static string Item(int itemId) => itemId == 0
        ? "None"
        : $"Item {itemId.ToString(CultureInfo.InvariantCulture)}";

    public static string Ability(int abilityId) => abilityId == 0
        ? "None"
        : $"Ability {abilityId.ToString(CultureInfo.InvariantCulture)}";

    public static string Bool(bool value) => value ? "Yes" : "No";

    public static string FormatRawNameForLookup(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        var value = raw.Replace('_', ' ').Trim();
        return value.Length == 0
            ? raw
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
    }
}
