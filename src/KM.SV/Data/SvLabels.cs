// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;

namespace KM.SV.Data;

internal static class SvLabels
{
    public static string Pokemon(int speciesId)
    {
        return FormatEnumValue(typeof(global::pml.common.DevID), speciesId, "DEV_", $"Pokemon {speciesId}");
    }

    public static string Move(int moveId)
    {
        return FormatEnumValue(typeof(global::pml.common.WazaID), moveId, "WAZA_", $"Move {moveId}");
    }

    public static string Item(int itemId)
    {
        return FormatEnumValue(typeof(global::ItemID), itemId, "ITEMID_", $"Item {itemId}");
    }

    public static string Ability(int abilityId)
    {
        return abilityId == 0 ? "None" : $"Ability {abilityId}";
    }

    public static string EnumName<TEnum>(TEnum value, string prefix = "")
        where TEnum : struct, Enum
    {
        return FormatRawName(value.ToString(), prefix);
    }

    public static string Bool(bool value)
    {
        return value ? "Yes" : "No";
    }

    private static string FormatEnumValue(Type enumType, int value, string prefix, string fallback)
    {
        var raw = Enum.ToObject(enumType, value).ToString();
        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            return fallback;
        }

        return FormatRawName(raw, prefix);
    }

    private static string FormatRawName(string raw, string prefix)
    {
        var value = raw;
        if (!string.IsNullOrEmpty(prefix) && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[prefix.Length..];
        }

        value = value.Replace('_', ' ').Trim();
        if (value.Length == 0)
        {
            return raw;
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
    }
}
