// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Files;

public static class ScarletVioletKnownRomFsFiles
{
    private static readonly IReadOnlyList<string> SupportedMessageLanguages =
    [
        "English",
        "Spanish",
        "French",
        "German",
    ];

    private static readonly IReadOnlyList<string> KnownMessageFiles =
    [
        "itemname.dat",
        "itemname.tbl",
        "monsname.dat",
        "monsname.tbl",
        "tokusei.dat",
        "tokusei.tbl",
        "place_name.dat",
        "place_name.tbl",
        "trname.dat",
        "trname.tbl",
        "trtype.dat",
        "trtype.tbl",
        "wazaname.dat",
        "wazaname.tbl",
        "trmsg.dat",
        "trmsg.tbl",
    ];

    public static IReadOnlyList<string> Paths { get; } = CreatePaths();

    private static IReadOnlyList<string> CreatePaths()
    {
        return new[]
            {
                "avalon/data/personal_array.bin",
                "avalon/data/tokusei_array.bin",
                "avalon/data/waza_array.bin",
                "world/data/battle/plib_item_conversion/plib_item_conversion_array.bin",
                "world/data/item/itemdata/itemdata_array.bin",
                "world/scene/parts/field/streaming_event/world_item_/world_item_0.trscn",
                "world/scene/parts/field/streaming_event/world_item_/world_item_1.trscn",
                "world/scene/parts/field/streaming_event/su1_world_item_/su1_world_item_0.trscn",
                "world/scene/parts/field/streaming_event/su1_world_item_/su1_world_item_1.trscn",
                "world/scene/parts/field/streaming_event/su2_world_item_/su2_world_item_0.trscn",
                "world/scene/parts/field/streaming_event/su2_world_item_/su2_world_item_1.trscn",
                "world/data/trainer/trdata/trdata_array.bin",
                "world/data/trainer/trenv/trenv_array.bin",
                "world/data/trainer/trtype/trtype_array.bin",
                "world/data/encount/pokedata/pokedata/pokedata_array.bin",
                "world/data/encount/pokedata/pokedata_su1/pokedata_su1_array.bin",
                "world/data/encount/pokedata/pokedata_su2/pokedata_su2_array.bin",
                "world/data/encount/pokedata/pokedata_lc/pokedata_lc_array.bin",
                "world/data/raid/raid_enemy_01/raid_enemy_01_array.bin",
                "world/data/raid/raid_enemy_02/raid_enemy_02_array.bin",
                "world/data/raid/raid_enemy_03/raid_enemy_03_array.bin",
                "world/data/raid/raid_enemy_04/raid_enemy_04_array.bin",
                "world/data/raid/raid_enemy_05/raid_enemy_05_array.bin",
                "world/data/raid/raid_enemy_06/raid_enemy_06_array.bin",
                "world/data/raid/su1_raid_enemy_01/su1_raid_enemy_01_array.bin",
                "world/data/raid/su1_raid_enemy_02/su1_raid_enemy_02_array.bin",
                "world/data/raid/su1_raid_enemy_03/su1_raid_enemy_03_array.bin",
                "world/data/raid/su1_raid_enemy_04/su1_raid_enemy_04_array.bin",
                "world/data/raid/su1_raid_enemy_05/su1_raid_enemy_05_array.bin",
                "world/data/raid/su1_raid_enemy_06/su1_raid_enemy_06_array.bin",
                "world/data/raid/su2_raid_enemy_01/su2_raid_enemy_01_array.bin",
                "world/data/raid/su2_raid_enemy_02/su2_raid_enemy_02_array.bin",
                "world/data/raid/su2_raid_enemy_03/su2_raid_enemy_03_array.bin",
                "world/data/raid/su2_raid_enemy_04/su2_raid_enemy_04_array.bin",
                "world/data/raid/su2_raid_enemy_05/su2_raid_enemy_05_array.bin",
                "world/data/raid/su2_raid_enemy_06/su2_raid_enemy_06_array.bin",
                "world/data/raid/delivery_raid_enemy/delivery_raid_enemy_array.bin",
                "world/data/raid/raid_fixed_reward_item/raid_fixed_reward_item_array.bin",
                "world/data/raid/raid_lottery_reward_item/raid_lottery_reward_item_array.bin",
                "world/data/raid/raid_lottery_reward_slot/raid_lottery_reward_slot_array.bin",
                "world/data/event/eventTradeList/eventTradeList_array.bin",
                "world/data/event/eventTradePokemon/eventTradePokemon_array.bin",
                "world/data/event/event_add_pokemon/eventAddPokemon/eventAddPokemon_array.bin",
                "world/data/field/fixed_symbol/fixed_symbol_table/fixed_symbol_table_array.bin",
                "world/data/field/fixed_symbol/fixed_symbol_manager/fixed_symbol_manager_data.bin",
                "world/data/ui/shop/shop_data/shop_data_array.bin",
                "world/data/ui/shop/friendlyshop/friendlyshop_data/friendlyshop_data_array.bin",
                "world/data/ui/shop/friendlyshop/friendlyshop_lineup_data/friendlyshop_lineup_data_array.bin",
                "world/data/ui/shop/shop_wazamachine/shop_wazamachine_data/shop_wazamachine_data_array.bin",
            }
            .Concat(CreateMessagePaths())
            .ToArray();
    }

    private static IEnumerable<string> CreateMessagePaths()
    {
        foreach (var language in SupportedMessageLanguages)
        {
            foreach (var file in KnownMessageFiles)
            {
                yield return $"message/dat/{language}/common/{file}";
            }
        }
    }
}
