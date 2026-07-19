// SPDX-License-Identifier: GPL-3.0-only

namespace KM.ZA.Data;

public static class ZaDataPaths
{
    public const string MessageRoot = "ik_message/dat";
    public const string LegacyMessageRoot = "message/dat";

    public const string PersonalArray = "avalon/data/personal_array.bin";
    public const string MoveDataArray = "avalon/data/waza_array.bin";
    public const string ItemDataArray = "world/exl/item_data/item_data/item_data.bin";
    public const string EvolutionItemConversionArray = "world/ik_data/battle/plib_item_conversion/plib_item_conversion_array.bin";
    public const string TrainerDataArray = "world/ik_data/trainer/trdata/trdata_array.bin";
    public const string PokemonDataArray = "world/ik_data/field/pokemon/pokemon_data/pokemon_data/pokemon_data_array.bin";
    public const string EncountDataArray = "world/ik_data/field/pokemon/encount_data/encount_data/encount_data_array.bin";
    public const string PokemonSpawnerDataArray = "world/ik_data/field/pokemon_spawner/pokemon_spawner_data/pokemon_spawner_data_array.bin";
    public const string PokemonSpawnerTransformArray = "world/ik_data/field/spawner_transform_data/pokemon_spawner_transform/pokemon_spawner_transform/pokemon_spawner_transform_array.bin";
    public const string ItemBallSpawnerDataArray = "world/ik_data/field/item_ball/item_ball_spawner_data/item_ball_spawner_data/item_ball_spawner_data_array.bin";
    public const string ItemBallSpawnerTransformArray = "world/ik_data/field/spawner_transform_data/itemball_spawner_transform/itemball_spawner_transform/itemball_spawner_transform_array.bin";
    public const string RandomPopItemSpawnerDataArray = "world/ik_data/field/random_pop_item_spawner/random_pop_item_spawner_data/random_pop_item_spawner_data_array.bin";
    public const string BattleTrainerSpawnerDataArray = "world/ik_data/field/npc_spawner/za_battle_trainer_spawner_data/za_battle_trainer_spawner_data/za_battle_trainer_spawner_data_array.bin";
    public const string FieldWazagimmickPublic = "world/ik_data/field/field_wazagimmick_public/field_wazagimmick_public/field_wazagimmick_public.bin";
    public const string AiAttackParamArray = "param_ai/data/ai/attack/attack_param/attack_param_array.bin";
    public const string AiBulletParamArray = "param_ai/data/ai/bullet/bullet_param/bullet_param_array.bin";
    public const string ShopItemArray = "world/exl/shop/shop/shop_item.bin";
    public const string ShopItemLineupArray = "world/exl/shop/shop_item_lineup/shop_item_lineup.bin";
    public const string ShopDressUpArray = "world/exl/shop/shop/shop_dressup.bin";
    public const string ShopDressUpLineupArray = "world/exl/shop/shop_dress_up_lineup/shop_dress_up_lineup.bin";
    public const string ShopHairMakeLineupArray = "world/exl/shop/shop_hair_make_lineup/shop_hair_make_lineup.bin";
    public const string DressUpDataArray = "world/exl/dress_up_data/dress_up_data/dress_up_data.bin";
    public const string HairMakeDataArray = "world/exl/hair_make_data/hair_make_data/hair_make_data.bin";

    public static string ItemNames(string language) => CommonMessage(language, "itemname.dat");

    public static string MoveNames(string language) => CommonMessage(language, "wazaname.dat");

    public static string MoveDescriptions(string language) => CommonMessage(language, "wazainfo.dat");

    public static string PokemonNames(string language) => CommonMessage(language, "monsname.dat");

    public static string AbilityNames(string language) => CommonMessage(language, "tokusei.dat");

    public static string MainMissionTitles(string language) => CommonMessage(language, "questlist_main.dat");

    public static string SideMissionTitles(string language) => CommonMessage(language, "questlist_sub.dat");

    public static string HyperspaceMissionTitles(string language) => ScriptMessage(language, "questlist_dlc.dat");

    public static string PlaceNames(string language) => CommonMessage(language, "place_name.dat");

    public static string PlaceNameKeys(string language) => CommonMessage(language, "place_name.tbl");

    public static string TrainerNames(string language) => CommonMessage(language, "trname.dat");

    public static string TrainerNameKeys(string language) => CommonMessage(language, "trname.tbl");

    public static string TrainerTypes(string language) => CommonMessage(language, "trtype.dat");

    public static string TrainerTypeKeys(string language) => CommonMessage(language, "trtype.tbl");

    private static string CommonMessage(string language, string fileName)
    {
        return $"{MessageRoot}/{language}/common/{fileName}";
    }

    private static string ScriptMessage(string language, string fileName)
    {
        return $"{MessageRoot}/{language}/sk/{fileName}";
    }

    internal static string? TryCreateLegacyMessagePath(string path)
    {
        const string prefix = MessageRoot + "/";
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? LegacyMessageRoot + "/" + path[prefix.Length..]
            : null;
    }
}
