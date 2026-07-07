// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;

namespace KM.SV.Data;

internal static class SvLabels
{
    public static string FieldPocket(global::FieldPocket value) => value switch
    {
        global::FieldPocket.FPOCKET_DRUG => "Medicine",
        global::FieldPocket.FPOCKET_BALL => "Poke Balls",
        global::FieldPocket.FPOCKET_BATTLE => "Battle Items",
        global::FieldPocket.FPOCKET_NUTS => "Berries",
        global::FieldPocket.FPOCKET_OTHER => "Other Items",
        global::FieldPocket.FPOCKET_WAZA => "TMs",
        global::FieldPocket.FPOCKET_TREASURE => "Treasures",
        global::FieldPocket.FPOCKET_PICNIC => "Picnic Items",
        global::FieldPocket.FPOCKET_EVENT => "Key Items",
        global::FieldPocket.FPOCKET_MATERIAL => "Materials",
        global::FieldPocket.FPOCKET_RECIPE => "Recipes",
        global::FieldPocket.FPOCKET_NONE => "None",
        _ => EnumName(value, "FPOCKET_"),
    };

    public static string FieldFunction(global::FieldFunctionType value) => value switch
    {
        global::FieldFunctionType.FIELDFUNC_NONE => "None",
        global::FieldFunctionType.FIELDFUNC_RECOVER => "Recovery",
        global::FieldFunctionType.FIELDFUNC_WAZA => "Teach Move",
        global::FieldFunctionType.FIELDFUNC_CYCLE => "Ride",
        global::FieldFunctionType.FIELDFUNC_MITSU => "Honey",
        global::FieldFunctionType.FIELDFUNC_BAG_MSG => "Bag Message",
        global::FieldFunctionType.FIELDFUNC_EVOLUTION => "Evolution",
        global::FieldFunctionType.FIELDFUNC_ANANUKE => "Escape",
        global::FieldFunctionType.FIELDFUNC_APPLICATION => "Application",
        global::FieldFunctionType.FIELDFUNC_FLY => "Fly",
        global::FieldFunctionType.FIELDFUNC_VIDRO => "Flute",
        global::FieldFunctionType.FIELDFUNC_MAIL => "Mail",
        global::FieldFunctionType.FIELDFUNC_KINOMI => "Berry",
        global::FieldFunctionType.FIELDFUNC_FISHING_ROD_GREAT => "Good Rod",
        global::FieldFunctionType.FIELDFUNC_BATTLE_REC => "Battle Recorder",
        global::FieldFunctionType.FIELDFUNC_FORM_CHANGE => "Form Change",
        global::FieldFunctionType.FIELDFUNC_DOWSING_MACHINE => "Dowsing Machine",
        global::FieldFunctionType.FIELDFUNC_UNION => "Union",
        global::FieldFunctionType.FIELDFUNC_ROTOPON => "Rotom Power",
        _ => EnumName(value, "FIELDFUNC_"),
    };

    public static string ItemType(global::ItemType value) => value switch
    {
        global::ItemType.ITEMTYPE_POCKET => "Bag Item",
        global::ItemType.ITEMTYPE_DRUG => "Medicine",
        global::ItemType.ITEMTYPE_EQUIP => "Held Item",
        global::ItemType.ITEMTYPE_NORMAL => "General Item",
        global::ItemType.ITEMTYPE_BATTLE => "Battle Item",
        global::ItemType.ITEMTYPE_BALL => "Poke Ball",
        global::ItemType.ITEMTYPE_MAIL => "Mail",
        global::ItemType.ITEMTYPE_WAZA => "TM",
        global::ItemType.ITEMTYPE_NUTS => "Berry",
        global::ItemType.ITEMTYPE_EVENT => "Key Item",
        global::ItemType.ITEMTYPE_RECIPE => "Recipe",
        global::ItemType.ITEMTYPE_CAPTURE => "Capture Item",
        global::ItemType.ITEMTYPE_MATERIAL => "Material",
        global::ItemType.ITEMTYPE_NONE => "None",
        _ => EnumName(value, "ITEMTYPE_"),
    };

    public static string ItemGroup(global::ItemGroup value) => value switch
    {
        global::ItemGroup.ITEMGROUP_NONE => "None",
        global::ItemGroup.ITEMGROUP_BALL => "Poke Ball",
        global::ItemGroup.ITEMGROUP_POCKET => "Bag Item",
        global::ItemGroup.ITEMGROUP_NUTS => "Berry",
        global::ItemGroup.ITEMGROUP_WAZA_MACHINE => "TM",
        global::ItemGroup.ITEMGROUP_HIDEN_MACHINE => "HM",
        global::ItemGroup.ITEMGROUP_JEWEL => "Jewel",
        global::ItemGroup.ITEMGROUP_MEGA_STONE => "Mega Stone",
        global::ItemGroup.ITEMGROUP_PIECE => "Shard",
        global::ItemGroup.ITEMGROUP_BEADS => "Beads",
        global::ItemGroup.ITEMGROUP_ROTOPON => "Rotom Power",
        global::ItemGroup.ITEMGROUP_HEART => "Heart",
        global::ItemGroup.ITEMGROUP_RESEARCH => "Research",
        global::ItemGroup.ITEMGROUP_AMULET_ITEM => "Charm",
        _ => EnumName(value, "ITEMGROUP_"),
    };

    public static string BattleFunction(global::BattleFunctionType value) => value switch
    {
        global::BattleFunctionType.BTLFUNC_NONE => "None",
        global::BattleFunctionType.BTLFUNC_BALL => "Poke Ball",
        global::BattleFunctionType.BTLFUNC_RECOVER => "Recovery",
        global::BattleFunctionType.BTLFUNC_ESCAPE => "Escape",
        _ => EnumName(value, "BTLFUNC_"),
    };

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

    public static string FormatRawNameForLookup(string raw)
    {
        return FormatRawName(raw, string.Empty);
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
