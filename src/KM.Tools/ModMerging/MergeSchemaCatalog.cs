// SPDX-License-Identifier: GPL-3.0-only
using KM.Core.ModMerging;
using KM.SV.Data;
using KM.ZA.Data;
using KM.Formats.ZA.Generated.GameData;
using KM.Formats.ZA.Generated.BattleMoves;
using KM.Formats.ZA.Generated.Field.PokemonSpawner;
using KM.Formats.SV.Placement;
using KM.Formats.SwSh;

namespace KM.Tools.ModMerging;

internal static class MergeSchemaCatalog
{
    internal sealed record Entry(string GameFamily, string Path, Func<byte[], MergeDocument> Read);
    internal static readonly Entry[] Entries = Create();

    internal static MergeDocument? Read(string game, string path, byte[] bytes) => Entries.FirstOrDefault(entry =>
        entry.GameFamily == MergeInputs.Family(game) && path.Equals("romfs/" + entry.Path, StringComparison.OrdinalIgnoreCase))?.Read(bytes);

    private static Entry[] Create()
    {
        var entries = new List<Entry>();
        void Add<T>(string family, string path, string kind) where T : struct, Google.FlatBuffers.IFlatbufferObject =>
            entries.Add(new(family, path, bytes => MergeFlatBuffer.Read<T>(bytes, kind)));
        void Table(string family, string path, string schema, string kind) =>
            entries.Add(new(family, path, bytes => MergeTableSchemas.Tables[schema].Read(bytes, kind)));
        Table("swsh", "bin/script_event_data/add_poke.bin", "SwShGifts", "gifts");
        Table("swsh", "bin/script_event_data/event_encount_data.bin", "SwShStaticEncounters", "static-encounters");
        Table("swsh", "bin/script_event_data/field_trade.bin", "SwShTrades", "trades");
        Table("swsh", "bin/script_event_data/field_trade_data.bin", "SwShTrades", "trades");
        Table("swsh", "bin/script_event_data/rental.bin", "SwShRentals", "rentals");
        Table("swsh", "bin/appli/chika/data_table/underground_exploration_poke.bin", "SwShAdventures", "dynamax-adventures");
        var behavior = new MergeTableSchema("SwShBehavior", SwShSymbolBehaviorArchive.FieldSpecs.OrderBy(field => field.FieldIndex).Select(field =>
            new MergeTableSchema.Field(field.Field, field.FieldType switch
            {
                SwShSymbolBehaviorFieldType.Single => "float", SwShSymbolBehaviorFieldType.Byte => "ubyte",
                SwShSymbolBehaviorFieldType.Int32 => "int", SwShSymbolBehaviorFieldType.UInt64 => "ulong",
                SwShSymbolBehaviorFieldType.String => "string", _ => throw new InvalidDataException("Unknown behavior field storage."),
            })).ToArray());
        var behaviors = new MergeTableSchema("SwShBehaviors", new MergeTableSchema.Field("Entries", "table", behavior, true));
        entries.Add(new("swsh", "bin/field/param/symbol_encount_mons_param/symbol_encount_mons_param.bin", bytes => behaviors.Read(bytes, "behavior")));
        Table("sv", SvDataPaths.EvolutionItemConversionArray, "EvolutionItemConversions", "evolution-items");
        Table("za", ZaDataPaths.EvolutionItemConversionArray, "EvolutionItemConversions", "evolution-items");
        Table("za", ZaDataPaths.BattleTrainerSpawnerDataArray, "ZaTrainerPoolSpawnerDatabaseArray", "spawners");
        Table("za", ZaDataPaths.ShopDressUpLineupArray, "ZaFashionLineupArray", "fashion-lineups");
        Table("za", ZaDataPaths.ShopHairMakeLineupArray, "ZaFashionLineupArray", "fashion-lineups");
        Add<EventAddPokemonArray>("sv", SvDataPaths.EventAddPokemonArray, "gifts");
        Add<EventTradeListArray>("sv", SvDataPaths.EventTradeListArray, "trades");
        Add<EventTradePokemonArray>("sv", SvDataPaths.EventTradePokemonArray, "trades");
        Add<FixedSymbolTableArray>("sv", SvDataPaths.FixedSymbolTableArray, "fixed-encounters");
        Add<EventBattlePokemonArray>("sv", SvDataPaths.EventBattlePokemonArray, "event-battles");
        foreach (var path in new[] { SvDataPaths.HiddenItemDataTableArray, SvDataPaths.HiddenItemDataTableSu1Array, SvDataPaths.HiddenItemDataTableSu2Array, SvDataPaths.HiddenItemDataTableLcArray })
            Add<HiddenItemDataTableArray>("sv", path, "item-drops");
        Add<RummagingItemDataTableArray>("sv", SvDataPaths.RummagingItemDataTableArray, "item-drops");
        Add<RaidFixedRewardItemArray>("sv", SvDataPaths.TeraRaidFixedRewardItemArray, "rewards");
        Add<RaidLotteryRewardItemArray>("sv", SvDataPaths.TeraRaidLotteryRewardItemArray, "rewards");
        Add<RaidEnemyTable01Array>("sv", SvDataPaths.TeraRaidEnemyPaldea1, "raids");
        Add<RaidEnemyTable02Array>("sv", SvDataPaths.TeraRaidEnemyPaldea2, "raids");
        Add<RaidEnemyTable03Array>("sv", SvDataPaths.TeraRaidEnemyPaldea3, "raids");
        Add<RaidEnemyTable04Array>("sv", SvDataPaths.TeraRaidEnemyPaldea4, "raids");
        Add<RaidEnemyTable05Array>("sv", SvDataPaths.TeraRaidEnemyPaldea5, "raids");
        Add<RaidEnemyTable06Array>("sv", SvDataPaths.TeraRaidEnemyPaldea6, "raids");
        Add<Su1RaidEnemyTable01Array>("sv", SvDataPaths.TeraRaidEnemyKitakami1, "raids");
        Add<Su1RaidEnemyTable02Array>("sv", SvDataPaths.TeraRaidEnemyKitakami2, "raids");
        Add<Su1RaidEnemyTable03Array>("sv", SvDataPaths.TeraRaidEnemyKitakami3, "raids");
        Add<Su1RaidEnemyTable04Array>("sv", SvDataPaths.TeraRaidEnemyKitakami4, "raids");
        Add<Su1RaidEnemyTable05Array>("sv", SvDataPaths.TeraRaidEnemyKitakami5, "raids");
        Add<Su1RaidEnemyTable06Array>("sv", SvDataPaths.TeraRaidEnemyKitakami6, "raids");
        Add<Su2RaidEnemyTable01Array>("sv", SvDataPaths.TeraRaidEnemyBlueberry1, "raids");
        Add<Su2RaidEnemyTable02Array>("sv", SvDataPaths.TeraRaidEnemyBlueberry2, "raids");
        Add<Su2RaidEnemyTable03Array>("sv", SvDataPaths.TeraRaidEnemyBlueberry3, "raids");
        Add<Su2RaidEnemyTable04Array>("sv", SvDataPaths.TeraRaidEnemyBlueberry4, "raids");
        Add<Su2RaidEnemyTable05Array>("sv", SvDataPaths.TeraRaidEnemyBlueberry5, "raids");
        Add<Su2RaidEnemyTable06Array>("sv", SvDataPaths.TeraRaidEnemyBlueberry6, "raids");
        Add<ZaPokemonDataDbArray>("za", ZaDataPaths.PokemonDataArray, "encounters");
        Add<ZaEncounterDataDbArray>("za", ZaDataPaths.EncountDataArray, "encounters");
        Add<PokemonSpawnerDataDBArray>("za", ZaDataPaths.PokemonSpawnerDataArray, "spawners");
        Table("za", ZaDataPaths.TrainerPoolTableDataArray, "ZaTrainerPoolDatabaseArray", "trainer-pools");
        Table("za", ZaDataPaths.TrainerPoolIdentityDataArray, "ZaTrainerPoolIdentityDatabaseArray", "trainer-pools");
        Add<ZaBattleMoveParameterArray>("za", ZaDataPaths.BattleMoveParameterArray, "battle-parameters");
        Add<ZaMoveTimingParameterArray>("za", ZaDataPaths.MoveTimingParameterArray, "battle-parameters");
        Add<ZaShopDataArray>("za", ZaDataPaths.ShopItemArray, "shops");
        Add<ZaShopDataArray>("za", ZaDataPaths.ShopDressUpArray, "shops");
        Table("za", ZaDataPaths.DressUpDataArray, "ZaDressUpCatalogArray", "fashion");
        Table("za", ZaDataPaths.HairMakeDataArray, "ZaHairAndMakeupCatalogArray", "fashion");
        Table("za", "world/exl/dress_up_data/dress_up_group_data/dress_up_group_data.bin", "ZaDressUpGroupCatalogArray", "fashion");
        Table("za", ZaDataPaths.PokemonSpawnerTransformArray, "ZaSpawnerTransforms", "transforms");
        Table("za", ZaDataPaths.ItemBallSpawnerTransformArray, "ZaSpawnerTransforms", "transforms");
        Add<ZaAlphaMoveTable>("za", ZaDataPaths.AlphaMoveTable, "alpha-moves");
        Add<ZaPokedexContentsDataArray>("za", ZaDataPaths.PokedexContentsData, "pokedex");
        return entries.ToArray();
    }
}
