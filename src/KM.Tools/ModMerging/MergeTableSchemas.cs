// SPDX-License-Identifier: GPL-3.0-only
namespace KM.Tools.ModMerging;

internal static class MergeTableSchemas
{
    internal static readonly IReadOnlyDictionary<string, MergeTableSchema> Tables = Create();

    private static Dictionary<string, MergeTableSchema> Create()
    {
        var tables = new Dictionary<string, MergeTableSchema>(StringComparer.Ordinal);
        tables["ZaTrainerPoolIdentity"] = new("ZaTrainerPoolIdentity",
            new MergeTableSchema.Field("TrainerId", "string"),
            new MergeTableSchema.Field("AssetId", "string"),
            new MergeTableSchema.Field("RosterId", "string"));
        tables["ZaTrainerPoolIdentityGroup"] = new("ZaTrainerPoolIdentityGroup",
            new MergeTableSchema.Field("Identities", "table", tables["ZaTrainerPoolIdentity"], true));
        tables["ZaTrainerPoolIdentityDatabaseArray"] = new("ZaTrainerPoolIdentityDatabaseArray",
            new MergeTableSchema.Field("Groups", "table", tables["ZaTrainerPoolIdentityGroup"], true));
        tables["ZaTrainerPoolSpawnerActivationParameter"] = new("ZaTrainerPoolSpawnerActivationParameter",
            new MergeTableSchema.Field("Condition", "string"),
            new MergeTableSchema.Field("Op", "int"),
            new MergeTableSchema.Field("Parameters", "string", null, true));
        tables["ZaTrainerPoolSpawnerActivationElement"] = new("ZaTrainerPoolSpawnerActivationElement",
            new MergeTableSchema.Field("Parameters", "table", tables["ZaTrainerPoolSpawnerActivationParameter"], true));
        tables["ZaTrainerPoolSpawnerActivationCondition"] = new("ZaTrainerPoolSpawnerActivationCondition",
            new MergeTableSchema.Field("Elements", "table", tables["ZaTrainerPoolSpawnerActivationElement"], true));
        tables["ZaTrainerPoolSpawnerTableReference"] = new("ZaTrainerPoolSpawnerTableReference",
            new MergeTableSchema.Field("TableId", "string"),
            new MergeTableSchema.Field("ActivationConditions", "table", tables["ZaTrainerPoolSpawnerActivationCondition"], true));
        tables["ZaTrainerPoolSpawnerZoneInfo"] = new("ZaTrainerPoolSpawnerZoneInfo",
            new MergeTableSchema.Field("ZoneId", "string"),
            new MergeTableSchema.Field("VariationId", "string"));
        tables["ZaTrainerPoolSpawnerAppearanceInfo"] = new("ZaTrainerPoolSpawnerAppearanceInfo",
            new MergeTableSchema.Field("MinimumCount", "int"),
            new MergeTableSchema.Field("MaximumCount", "int"));
        tables["ZaTrainerPoolSpawnerObjectInfo"] = new("ZaTrainerPoolSpawnerObjectInfo",
            new MergeTableSchema.Field("ObjectName", "string"),
            new MergeTableSchema.Field("CreateScenePath", "string"),
            new MergeTableSchema.Field("AdditionalFlags", "int", null, true),
            new MergeTableSchema.Field("ZoneInfo", "table", tables["ZaTrainerPoolSpawnerZoneInfo"]),
            new MergeTableSchema.Field("AppearanceInfo", "table", tables["ZaTrainerPoolSpawnerAppearanceInfo"]));
        tables["ZaTrainerPoolSpawnerCoolTimeInfo"] = new("ZaTrainerPoolSpawnerCoolTimeInfo",
            new MergeTableSchema.Field("Time", "float"));
        tables["ZaTrainerPoolSpawner"] = new("ZaTrainerPoolSpawner",
            new MergeTableSchema.Field("Id", "string"),
            new MergeTableSchema.Field("AppearanceObjects", "table", tables["ZaTrainerPoolSpawnerObjectInfo"], true),
            new MergeTableSchema.Field("CoolTimeInfo", "table", tables["ZaTrainerPoolSpawnerCoolTimeInfo"]),
            new MergeTableSchema.Field("TableReferences", "table", tables["ZaTrainerPoolSpawnerTableReference"], true));
        tables["ZaTrainerPoolSpawnerGroup"] = new("ZaTrainerPoolSpawnerGroup",
            new MergeTableSchema.Field("Spawners", "table", tables["ZaTrainerPoolSpawner"], true));
        tables["ZaTrainerPoolSpawnerDatabaseArray"] = new("ZaTrainerPoolSpawnerDatabaseArray",
            new MergeTableSchema.Field("Groups", "table", tables["ZaTrainerPoolSpawnerGroup"], true));
        tables["ZaTrainerPoolActivationParameter"] = new("ZaTrainerPoolActivationParameter",
            new MergeTableSchema.Field("Condition", "string"),
            new MergeTableSchema.Field("Op", "int"),
            new MergeTableSchema.Field("Parameters", "string", null, true));
        tables["ZaTrainerPoolActivationElement"] = new("ZaTrainerPoolActivationElement",
            new MergeTableSchema.Field("Parameters", "table", tables["ZaTrainerPoolActivationParameter"], true));
        tables["ZaTrainerPoolActivationCondition"] = new("ZaTrainerPoolActivationCondition",
            new MergeTableSchema.Field("Elements", "table", tables["ZaTrainerPoolActivationElement"], true));
        tables["ZaTrainerPoolAiInfo"] = new("ZaTrainerPoolAiInfo",
            new MergeTableSchema.Field("ActionId", "int"),
            new MergeTableSchema.Field("PointName", "string"),
            new MergeTableSchema.Field("ActorName", "string"),
            new MergeTableSchema.Field("CreateIgnoreFlags", "int", null, true),
            new MergeTableSchema.Field("HomeRange", "float"),
            new MergeTableSchema.Field("PopActionId", "int"));
        tables["ZaTrainerPoolAppearance"] = new("ZaTrainerPoolAppearance",
            new MergeTableSchema.Field("Id", "string"),
            new MergeTableSchema.Field("TrainerId", "string"),
            new MergeTableSchema.Field("Tags", "string", null, true),
            new MergeTableSchema.Field("Weight", "int"),
            new MergeTableSchema.Field("ActivationConditions", "table", tables["ZaTrainerPoolActivationCondition"], true),
            new MergeTableSchema.Field("AiInfo", "table", tables["ZaTrainerPoolAiInfo"]));
        tables["ZaTrainerPoolTable"] = new("ZaTrainerPoolTable",
            new MergeTableSchema.Field("Id", "string"),
            new MergeTableSchema.Field("Appearances", "table", tables["ZaTrainerPoolAppearance"], true));
        tables["ZaTrainerPoolDatabaseGroup"] = new("ZaTrainerPoolDatabaseGroup",
            new MergeTableSchema.Field("Tables", "table", tables["ZaTrainerPoolTable"], true));
        tables["ZaTrainerPoolDatabaseArray"] = new("ZaTrainerPoolDatabaseArray",
            new MergeTableSchema.Field("Groups", "table", tables["ZaTrainerPoolDatabaseGroup"], true));
        tables["ZaFashionLineupAppearCondition"] = new("ZaFashionLineupAppearCondition",
            new MergeTableSchema.Field("Condition", "string"),
            new MergeTableSchema.Field("Comparison", "uint"),
            new MergeTableSchema.Field("Arguments", "string", null, true));
        tables["ZaFashionLineupConditionHolder"] = new("ZaFashionLineupConditionHolder",
            new MergeTableSchema.Field("Values", "table", tables["ZaFashionLineupAppearCondition"], true));
        tables["ZaFashionLineupConditionGroup"] = new("ZaFashionLineupConditionGroup",
            new MergeTableSchema.Field("Values", "table", tables["ZaFashionLineupConditionHolder"], true));
        tables["ZaFashionLineupEntry"] = new("ZaFashionLineupEntry",
            new MergeTableSchema.Field("ItemId", "uint"),
            new MergeTableSchema.Field("Conditions", "table", tables["ZaFashionLineupConditionGroup"], true));
        tables["ZaFashionLineup"] = new("ZaFashionLineup",
            new MergeTableSchema.Field("LineupId", "string"),
            new MergeTableSchema.Field("Entries", "table", tables["ZaFashionLineupEntry"], true));
        tables["ZaFashionLineupArray"] = new("ZaFashionLineupArray",
            new MergeTableSchema.Field("Values", "table", tables["ZaFashionLineup"], true));
        tables["ZaDressUpCatalogEntry"] = new("ZaDressUpCatalogEntry",
            new MergeTableSchema.Field("ItemId", "uint"),
            new MergeTableSchema.Field("ModelPart", "string"),
            new MergeTableSchema.Field("CatalogGroupCode", "uint"),
            new MergeTableSchema.Field("ModelVariant", "string"),
            new MergeTableSchema.Field("CategoryCode", "uint"),
            new MergeTableSchema.Field("ColorVariantCode", "uint"),
            new MergeTableSchema.Field("PrimaryColorLabel", "string"),
            new MergeTableSchema.Field("SecondaryColorLabel", "string"),
            new MergeTableSchema.Field("ReservedFlagA", "bool"),
            new MergeTableSchema.Field("Price", "uint"),
            new MergeTableSchema.Field("UiIndex", "uint"),
            new MergeTableSchema.Field("FootwearSubtype", "string"),
            new MergeTableSchema.Field("ReservedFlagB", "bool"));
        tables["ZaDressUpCatalogArray"] = new("ZaDressUpCatalogArray",
            new MergeTableSchema.Field("Entries", "table", tables["ZaDressUpCatalogEntry"], true));
        tables["ZaDressUpGroupCatalogEntry"] = new("ZaDressUpGroupCatalogEntry",
            new MergeTableSchema.Field("ModelPart", "string"),
            new MergeTableSchema.Field("DisplayOrder", "uint"),
            new MergeTableSchema.Field("DisplayLabel", "string"));
        tables["ZaDressUpGroupCatalogArray"] = new("ZaDressUpGroupCatalogArray",
            new MergeTableSchema.Field("Entries", "table", tables["ZaDressUpGroupCatalogEntry"], true));
        tables["ZaHairAndMakeupCatalogEntry"] = new("ZaHairAndMakeupCatalogEntry",
            new MergeTableSchema.Field("ItemId", "uint"),
            new MergeTableSchema.Field("ModelKey", "string"),
            new MergeTableSchema.Field("CatalogTypeCode", "uint"),
            new MergeTableSchema.Field("ReservedFlag", "bool"),
            new MergeTableSchema.Field("ColorValue", "string"),
            new MergeTableSchema.Field("LabelKey", "string"),
            new MergeTableSchema.Field("DisplayOrder", "uint"),
            new MergeTableSchema.Field("GroupCode", "int"),
            new MergeTableSchema.Field("VariantCode", "int"));
        tables["ZaHairAndMakeupCatalogArray"] = new("ZaHairAndMakeupCatalogArray",
            new MergeTableSchema.Field("Entries", "table", tables["ZaHairAndMakeupCatalogEntry"], true));
        tables["SwShGift"] = new("SwShGift",
            new MergeTableSchema.Field("IsEgg", "int"),
            new MergeTableSchema.Field("Form", "ubyte"),
            new MergeTableSchema.Field("DynamaxLevel", "ubyte"),
            new MergeTableSchema.Field("BallItemId", "int"),
            new MergeTableSchema.Field("Field04", "ubyte"),
            new MergeTableSchema.Field("Hash1", "ulong"),
            new MergeTableSchema.Field("CanGigantamax", "bool"),
            new MergeTableSchema.Field("HeldItem", "int"),
            new MergeTableSchema.Field("Level", "ubyte"),
            new MergeTableSchema.Field("Species", "int"),
            new MergeTableSchema.Field("Field0A", "ubyte"),
            new MergeTableSchema.Field("MemoryCode", "ubyte"),
            new MergeTableSchema.Field("MemoryData", "ushort"),
            new MergeTableSchema.Field("MemoryFeel", "ubyte"),
            new MergeTableSchema.Field("MemoryLevel", "ubyte"),
            new MergeTableSchema.Field("OtNameId", "ulong"),
            new MergeTableSchema.Field("OtGender", "int"),
            new MergeTableSchema.Field("ShinyLock", "int"),
            new MergeTableSchema.Field("Nature", "int"),
            new MergeTableSchema.Field("Gender", "ubyte"),
            new MergeTableSchema.Field("IvSpeed", "byte"),
            new MergeTableSchema.Field("IvAttack", "byte"),
            new MergeTableSchema.Field("IvDefense", "byte"),
            new MergeTableSchema.Field("IvHp", "byte"),
            new MergeTableSchema.Field("IvSpecialAttack", "byte"),
            new MergeTableSchema.Field("IvSpecialDefense", "byte"),
            new MergeTableSchema.Field("Ability", "int"),
            new MergeTableSchema.Field("SpecialMove", "int"));
        tables["SwShGifts"] = new("SwShGifts",
            new MergeTableSchema.Field("Gifts", "table", tables["SwShGift"], true));
        tables["SwShWildSlot"] = new("SwShWildSlot",
            new MergeTableSchema.Field("Probability", "ubyte"),
            new MergeTableSchema.Field("Species", "int"),
            new MergeTableSchema.Field("Form", "ubyte"));
        tables["SwShWildSubTable"] = new("SwShWildSubTable",
            new MergeTableSchema.Field("LevelMin", "ubyte"),
            new MergeTableSchema.Field("LevelMax", "ubyte"),
            new MergeTableSchema.Field("Slots", "table", tables["SwShWildSlot"], true));
        tables["SwShWildTable"] = new("SwShWildTable",
            new MergeTableSchema.Field("ZoneId", "ulong"),
            new MergeTableSchema.Field("SubTables", "table", tables["SwShWildSubTable"], true));
        tables["SwShWildEncounters"] = new("SwShWildEncounters",
            new MergeTableSchema.Field("Field00", "uint"),
            new MergeTableSchema.Field("Tables", "table", tables["SwShWildTable"], true));
        tables["SwShReward"] = new("SwShReward",
            new MergeTableSchema.Field("EntryId", "uint"),
            new MergeTableSchema.Field("ItemId", "uint"),
            new MergeTableSchema.Field("Values", "uint", null, true));
        tables["SwShRewardTable"] = new("SwShRewardTable",
            new MergeTableSchema.Field("TableId", "ulong"),
            new MergeTableSchema.Field("Rewards", "table", tables["SwShReward"], true));
        tables["SwShRewards"] = new("SwShRewards",
            new MergeTableSchema.Field("Tables", "table", tables["SwShRewardTable"], true));
        tables["SwShNest"] = new("SwShNest",
            new MergeTableSchema.Field("EntryIndex", "int"),
            new MergeTableSchema.Field("Species", "int"),
            new MergeTableSchema.Field("Form", "int"),
            new MergeTableSchema.Field("LevelTableId", "ulong"),
            new MergeTableSchema.Field("Ability", "ubyte"),
            new MergeTableSchema.Field("IsGigantamax", "bool"),
            new MergeTableSchema.Field("DropTableId", "ulong"),
            new MergeTableSchema.Field("BonusTableId", "ulong"),
            new MergeTableSchema.Field("Probabilities", "uint", null, true),
            new MergeTableSchema.Field("Gender", "byte"),
            new MergeTableSchema.Field("FlawlessIvs", "byte"));
        tables["SwShNestTable"] = new("SwShNestTable",
            new MergeTableSchema.Field("TableId", "ulong"),
            new MergeTableSchema.Field("GameVersion", "int"),
            new MergeTableSchema.Field("Entries", "table", tables["SwShNest"], true));
        tables["SwShNests"] = new("SwShNests",
            new MergeTableSchema.Field("Tables", "table", tables["SwShNestTable"], true));
        tables["EvolutionItemConversion"] = new("EvolutionItemConversion",
            new MergeTableSchema.Field("ItemId", "int"),
            new MergeTableSchema.Field("ParameterId", "int"));
        tables["EvolutionItemConversions"] = new("EvolutionItemConversions",
            new MergeTableSchema.Field("Values", "table", tables["EvolutionItemConversion"], true));
        tables["SwShStaticEncounterRecord"] = new("SwShStaticEncounterRecord",
            new MergeTableSchema.Field("BackgroundFarTypeId", "ulong"),
            new MergeTableSchema.Field("BackgroundNearTypeId", "ulong"),
            new MergeTableSchema.Field("EvsSpeed", "ubyte"),
            new MergeTableSchema.Field("EvsAttack", "ubyte"),
            new MergeTableSchema.Field("EvsDefense", "ubyte"),
            new MergeTableSchema.Field("EvsHP", "ubyte"),
            new MergeTableSchema.Field("EvsSpecialAttack", "ubyte"),
            new MergeTableSchema.Field("EvsSpecialDefense", "ubyte"),
            new MergeTableSchema.Field("Form", "ubyte"),
            new MergeTableSchema.Field("DynamaxLevel", "ubyte"),
            new MergeTableSchema.Field("Field0A", "int"),
            new MergeTableSchema.Field("EncounterId", "ulong"),
            new MergeTableSchema.Field("Field0C", "ubyte"),
            new MergeTableSchema.Field("CanGigantamax", "bool"),
            new MergeTableSchema.Field("HeldItem", "int"),
            new MergeTableSchema.Field("Level", "ubyte"),
            new MergeTableSchema.Field("EncounterScenario", "int"),
            new MergeTableSchema.Field("Species", "int"),
            new MergeTableSchema.Field("ShinyLock", "uint"),
            new MergeTableSchema.Field("Nature", "uint"),
            new MergeTableSchema.Field("Gender", "byte"),
            new MergeTableSchema.Field("IvsSpeed", "byte"),
            new MergeTableSchema.Field("IvsAttack", "byte"),
            new MergeTableSchema.Field("IvsDefense", "byte"),
            new MergeTableSchema.Field("IvsHP", "byte"),
            new MergeTableSchema.Field("IvsSpecialAttack", "byte"),
            new MergeTableSchema.Field("IvsSpecialDefense", "byte"),
            new MergeTableSchema.Field("Ability", "int"),
            new MergeTableSchema.Field("Moves1", "int"),
            new MergeTableSchema.Field("Moves2", "int"),
            new MergeTableSchema.Field("Moves3", "int"),
            new MergeTableSchema.Field("Moves4", "int"));
        tables["SwShStaticEncounters"] = new("SwShStaticEncounters",
            new MergeTableSchema.Field("Values", "table", tables["SwShStaticEncounterRecord"], true));
        tables["SwShTradePokemonRecord"] = new("SwShTradePokemonRecord",
            new MergeTableSchema.Field("Form", "ubyte"),
            new MergeTableSchema.Field("DynamaxLevel", "ubyte"),
            new MergeTableSchema.Field("BallItemId", "int"),
            new MergeTableSchema.Field("Field03", "int"),
            new MergeTableSchema.Field("Hash0", "ulong"),
            new MergeTableSchema.Field("CanGigantamax", "bool"),
            new MergeTableSchema.Field("HeldItem", "int"),
            new MergeTableSchema.Field("Level", "ubyte"),
            new MergeTableSchema.Field("Species", "int"),
            new MergeTableSchema.Field("Hash1", "ulong"),
            new MergeTableSchema.Field("TrainerId", "int"),
            new MergeTableSchema.Field("MemoryCode", "ubyte"),
            new MergeTableSchema.Field("MemoryTextVariable", "ushort"),
            new MergeTableSchema.Field("MemoryFeel", "ubyte"),
            new MergeTableSchema.Field("MemoryIntensity", "ubyte"),
            new MergeTableSchema.Field("Hash2", "ulong"),
            new MergeTableSchema.Field("OtGender", "ubyte"),
            new MergeTableSchema.Field("RequiredForm", "ubyte"),
            new MergeTableSchema.Field("RequiredSpecies", "int"),
            new MergeTableSchema.Field("RequiredNature", "int"),
            new MergeTableSchema.Field("UnknownRequirement", "ubyte"),
            new MergeTableSchema.Field("ShinyLock", "int"),
            new MergeTableSchema.Field("Nature", "int"),
            new MergeTableSchema.Field("Gender", "byte"),
            new MergeTableSchema.Field("IvsSpeed", "byte"),
            new MergeTableSchema.Field("IvsAttack", "byte"),
            new MergeTableSchema.Field("IvsDefense", "byte"),
            new MergeTableSchema.Field("IvsHp", "byte"),
            new MergeTableSchema.Field("IvsSpecialAttack", "byte"),
            new MergeTableSchema.Field("IvsSpecialDefense", "byte"),
            new MergeTableSchema.Field("Ability", "ubyte"),
            new MergeTableSchema.Field("RelearnMoves1", "ushort"),
            new MergeTableSchema.Field("RelearnMoves2", "ushort"),
            new MergeTableSchema.Field("RelearnMoves3", "ushort"),
            new MergeTableSchema.Field("RelearnMoves4", "ushort"));
        tables["SwShTrades"] = new("SwShTrades",
            new MergeTableSchema.Field("Values", "table", tables["SwShTradePokemonRecord"], true));
        tables["SwShRentalPokemonRecord"] = new("SwShRentalPokemonRecord",
            new MergeTableSchema.Field("EvsSpeed", "ubyte"),
            new MergeTableSchema.Field("EvsAttack", "ubyte"),
            new MergeTableSchema.Field("EvsDefense", "ubyte"),
            new MergeTableSchema.Field("EvsHP", "ubyte"),
            new MergeTableSchema.Field("EvsSpecialAttack", "ubyte"),
            new MergeTableSchema.Field("EvsSpecialDefense", "ubyte"),
            new MergeTableSchema.Field("Form", "ubyte"),
            new MergeTableSchema.Field("BallItemId", "int"),
            new MergeTableSchema.Field("Hash1", "ulong"),
            new MergeTableSchema.Field("HeldItem", "int"),
            new MergeTableSchema.Field("Level", "ubyte"),
            new MergeTableSchema.Field("Species", "int"),
            new MergeTableSchema.Field("Hash2", "ulong"),
            new MergeTableSchema.Field("TrainerId", "uint"),
            new MergeTableSchema.Field("Nature", "int"),
            new MergeTableSchema.Field("Gender", "int"),
            new MergeTableSchema.Field("IvsSpeed", "byte"),
            new MergeTableSchema.Field("IvsAttack", "byte"),
            new MergeTableSchema.Field("IvsDefense", "byte"),
            new MergeTableSchema.Field("IvsHP", "byte"),
            new MergeTableSchema.Field("IvsSpecialAttack", "byte"),
            new MergeTableSchema.Field("IvsSpecialDefense", "byte"),
            new MergeTableSchema.Field("Ability", "int"),
            new MergeTableSchema.Field("Moves1", "int"),
            new MergeTableSchema.Field("Moves2", "int"),
            new MergeTableSchema.Field("Moves3", "int"),
            new MergeTableSchema.Field("Moves4", "int"));
        tables["SwShRentals"] = new("SwShRentals",
            new MergeTableSchema.Field("Values", "table", tables["SwShRentalPokemonRecord"], true));
        tables["SwShDynamaxAdventureRecord"] = new("SwShDynamaxAdventureRecord",
            new MergeTableSchema.Field("IsSingleCapture", "bool"),
            new MergeTableSchema.Field("SingleCaptureFlagBlock", "ulong"),
            new MergeTableSchema.Field("Field02", "ubyte"),
            new MergeTableSchema.Field("Form", "ubyte"),
            new MergeTableSchema.Field("GigantamaxState", "uint"),
            new MergeTableSchema.Field("BallItemId", "uint"),
            new MergeTableSchema.Field("AdventureIndex", "uint"),
            new MergeTableSchema.Field("Level", "uint"),
            new MergeTableSchema.Field("Species", "int"),
            new MergeTableSchema.Field("UiMessageId", "ulong"),
            new MergeTableSchema.Field("OtGender", "uint"),
            new MergeTableSchema.Field("Version", "ubyte"),
            new MergeTableSchema.Field("ShinyRoll", "uint"),
            new MergeTableSchema.Field("IvsSpeed", "byte"),
            new MergeTableSchema.Field("IvsAttack", "byte"),
            new MergeTableSchema.Field("IvsDefense", "byte"),
            new MergeTableSchema.Field("IvsHp", "byte"),
            new MergeTableSchema.Field("IvsSpecialAttack", "byte"),
            new MergeTableSchema.Field("IvsSpecialDefense", "byte"),
            new MergeTableSchema.Field("Ability", "uint"),
            new MergeTableSchema.Field("IsStoryProgressGated", "bool"),
            new MergeTableSchema.Field("Moves1", "uint"),
            new MergeTableSchema.Field("Moves2", "uint"),
            new MergeTableSchema.Field("Moves3", "uint"),
            new MergeTableSchema.Field("Moves4", "uint"));
        tables["SwShAdventures"] = new("SwShAdventures",
            new MergeTableSchema.Field("Values", "table", tables["SwShDynamaxAdventureRecord"], true));
        tables["ZaSpawnerVector"] = new("ZaSpawnerVector",
            new MergeTableSchema.Field("X", "float"),
            new MergeTableSchema.Field("Y", "float"),
            new MergeTableSchema.Field("Z", "float"));
        tables["ZaSpawnerTransform"] = new("ZaSpawnerTransform",
            new MergeTableSchema.Field("Name", "string"),
            new MergeTableSchema.Field("Position", "table", tables["ZaSpawnerVector"]),
            new MergeTableSchema.Field("Rotation", "table", tables["ZaSpawnerVector"]),
            new MergeTableSchema.Field("AttachTransformEnable", "bool"));
        tables["ZaSpawnerTransformGroup"] = new("ZaSpawnerTransformGroup",
            new MergeTableSchema.Field("Rows", "table", tables["ZaSpawnerTransform"], true));
        tables["ZaSpawnerTransforms"] = new("ZaSpawnerTransforms",
            new MergeTableSchema.Field("Groups", "table", tables["ZaSpawnerTransformGroup"], true));
        return tables;
    }
}
