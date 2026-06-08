// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Tests.Items;
using KM.SwSh.Tests.Rentals;
using KM.SwSh.Tests.StaticEncounters;
using KM.SwSh.Tests.Trades;
using KM.SwSh.Tests.Trainers;
using System.Buffers.Binary;

namespace KM.SwSh.Tests.Performance;

internal static class SwShPerformanceFixtureProject
{
    public const int ItemCount = 1_500;
    public const int TrainerCount = 120;
    public const int PokemonCount = 920;
    public const int MoveCount = 200;
    public const int TextTableCount = 120;
    public const int TextLinesPerTable = 40;
    public const int ExtraRomFsFileCount = 600;
    public const int EncounterTableCount = 48;
    public const int EncounterSlotsPerTable = 24;
    public const int RaidBattleTableCount = 24;
    public const int RaidBattleSlotsPerTable = 12;
    public const int DynamaxAdventureCount = 80;
    public const int RaidRewardTableCount = 24;
    public const int RaidRewardRowsPerTable = 12;
    public const int PlacementAreaCount = 12;
    public const int FlagworkTableCount = 4;
    public const int FlagworkRowsPerTable = 64;

    private const ulong SwordTitleId = 0x0100ABF008968000;

    public static TemporarySwShProject Create()
    {
        var temp = TemporarySwShProject.Create();

        WriteItems(temp);
        WritePokemonData(temp);
        WriteTextTables(temp);
        WriteTrainers(temp);
        WriteMovesData(temp);
        WriteTradePokemon(temp);
        WriteStaticEncounters(temp);
        WriteRentalPokemon(temp);
        WriteDynamaxAdventures(temp);
        WriteShopData(temp);
        WriteDataTablePack(temp);
        WritePlacement(temp);
        WriteFlagwork(temp);
        WriteRoyalCandyPreflightInputs(temp);
        WriteExeFs(temp);
        WriteExtraProjectFiles(temp);

        return temp;
    }

    private static void WriteItems(TemporarySwShProject temp)
    {
        var records = Enumerable.Range(0, ItemCount)
            .Select(index => new ItemFixtureRecord(
                index,
                index,
                BuyPrice: (index * 13) % 25_000,
                WattsPrice: (index * 7) % 10_000,
                AlternatePrice: (index * 5) % 12_000,
                (SwShItemPouch)(index % 9)))
            .ToArray();

        temp.WriteBaseRomFsFile(
            "bin/pml/item/item.dat",
            SwShItemTestFixtures.CreateItemTable(records));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames(CreateIndexedNames("Item", ItemCount)));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/iteminfo.dat",
            CreateTextTable(Enumerable.Range(0, ItemCount)
                .Select(index => $"Synthetic item info {index}")
                .ToArray()));
        temp.WriteBaseRomFsFile(
            "bin/pml/item/item_hash_to_index.dat",
            new SwShItemHashTable(
                Enumerable.Range(0, ItemCount)
                    .Select(index => new SwShItemHashEntry(index, CreateItemHash(index)))
                    .ToArray()).Write());
    }

    private static void WriteTextTables(TemporarySwShProject temp)
    {
        for (var tableIndex = 0; tableIndex < TextTableCount; tableIndex++)
        {
            var lines = Enumerable.Range(0, TextLinesPerTable)
                .Select(lineIndex => $"Synthetic text table {tableIndex:D3}, line {lineIndex:D2}")
                .ToArray();
            temp.WriteBaseRomFsFile(
                $"bin/message/English/scenario/map_{tableIndex / 10:D2}/event_{tableIndex:D3}.dat",
                CreateTextTable(lines));
        }

        for (var tableIndex = 0; tableIndex < 12; tableIndex++)
        {
            temp.WriteBaseRomFsFile(
                $"bin/message/French/scenario/event_{tableIndex:D3}.dat",
                CreateTextTable($"Texte synthetique {tableIndex}"));
        }
    }

    private static void WritePokemonData(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/pml/personal/personal_total.bin",
            CreatePersonalTable(Enumerable.Range(0, PokemonCount).Select(CreatePokemonPersonalRecord)));
        temp.WriteBaseRomFsFile(
            "bin/pml/waza_oboe/wazaoboe_total.bin",
            CreateLearnsetTable(PokemonCount));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/pokelist.dat",
            CreateTextTable(CreateIndexedNames("Pokemon", PokemonCount)));

        var emptyEvolutionFile = new byte[SwShEvolutionSet.FileSize];
        for (var pokemonId = 0; pokemonId < PokemonCount; pokemonId++)
        {
            temp.WriteBaseRomFsFile(
                $"bin/pml/evolution/evo_{pokemonId:D3}.bin",
                emptyEvolutionFile);
        }
    }

    private static void WriteTrainers(TemporarySwShProject temp)
    {
        for (var trainerId = 0; trainerId < TrainerCount; trainerId++)
        {
            temp.WriteBaseRomFsFile(
                $"bin/trainer/trainer_data/trainer_{trainerId:D3}.bin",
                SwShTrainersWorkflowServiceTests.CreateTrainerData(
                    classId: trainerId % 20,
                    battleMode: trainerId % 2,
                    pokemonCount: 3));
            temp.WriteBaseRomFsFile(
                $"bin/trainer/trainer_poke/trainer_{trainerId:D3}.bin",
                SwShTrainersWorkflowServiceTests.CreateTrainerTeam(
                    (speciesId: 1 + (trainerId % 900), level: 10 + (trainerId % 50), heldItemId: trainerId % ItemCount, moves: new[] { 1, 2, 3, 4 }),
                    (speciesId: 2 + (trainerId % 900), level: 11 + (trainerId % 50), heldItemId: (trainerId + 1) % ItemCount, moves: new[] { 5, 6, 7, 8 }),
                    (speciesId: 3 + (trainerId % 900), level: 12 + (trainerId % 50), heldItemId: (trainerId + 2) % ItemCount, moves: new[] { 9, 10, 11, 12 })));
        }

        temp.WriteBaseRomFsFile(
            "bin/message/English/common/trname.dat",
            CreateTextTable(CreateIndexedNames("Trainer", TrainerCount)));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/trtype.dat",
            CreateTextTable(CreateIndexedNames("Trainer Class", 24)));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            CreateTextTable(CreateIndexedNames("Species", 920)));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            CreateTextTable(CreateIndexedNames("Move", 32)));
    }

    private static void WriteMovesData(TemporarySwShProject temp)
    {
        for (var moveId = 0; moveId < MoveCount; moveId++)
        {
            temp.WriteBaseRomFsFile(
                $"bin/pml/waza/waza_{moveId:D3}.bin",
                SwShMoveDataFile.Write(CreateMoveRecord(moveId)));
        }

        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            CreateTextTable(CreateIndexedNames("Move", MoveCount)));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazainfo.dat",
            CreateTextTable(Enumerable.Range(0, MoveCount)
                .Select(index => $"Synthetic move info {index}")
                .ToArray()));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/typename.dat",
            CreateTextTable(
                "Normal",
                "Fighting",
                "Flying",
                "Poison",
                "Ground",
                "Rock",
                "Bug",
                "Ghost",
                "Steel",
                "Fire",
                "Water",
                "Grass",
                "Electric",
                "Psychic",
                "Ice",
                "Dragon",
                "Dark",
                "Fairy"));
    }

    private static void WriteStaticEncounters(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/script_event_data/event_encount_data.bin",
            SwShStaticEncountersWorkflowServiceTests.CreateStaticEncounterTable(
                new SwShStaticEncounterStats(31, 31, 31, 31, 31, 31)));
    }

    private static void WriteTradePokemon(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/script_event_data/field_trade.bin",
            SwShTradePokemonWorkflowServiceTests.CreateTradeTable(
                new SwShTradePokemonIvs(31, 30, 29, 28, 27, 26)));
    }

    private static void WriteRentalPokemon(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/script_event_data/rental.bin",
            SwShRentalPokemonWorkflowServiceTests.CreateRentalTable(
                new SwShRentalPokemonStats(31, 31, 31, 31, 31, 31)));
    }

    private static void WriteDynamaxAdventures(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath["romfs/".Length..],
            new SwShDynamaxAdventureArchive(
                Enumerable.Range(0, DynamaxAdventureCount)
                    .Select(index => new SwShDynamaxAdventureRecord(
                        index,
                        IsSingleCapture: index % 5 == 0,
                        SingleCaptureFlagBlock: 0x5500000000000000UL + (ulong)index,
                        Field02: 0,
                        Form: index % 3,
                        GigantamaxState: index % 3,
                        BallItemId: 4,
                        AdventureIndex: 1000 + index,
                        Level: 60 + (index % 10),
                        Species: 1 + (index % 900),
                        UiMessageId: 0x6600000000000000UL + (ulong)index,
                        OtGender: 1,
                        Version: index % 3,
                        ShinyRoll: 1,
                        new SwShDynamaxAdventureIvs(
                            Hp: index % 2 == 0 ? -4 : -1,
                            Attack: -1,
                            Defense: index % 32,
                            Speed: -1,
                            SpecialAttack: -1,
                            SpecialDefense: -1),
                        Ability: index % 3,
                        IsStoryProgressGated: index % 11 == 0,
                        Moves: [1 + (index % MoveCount), 2 + (index % MoveCount), 3 + (index % MoveCount), 4 + (index % MoveCount)]))
                    .ToArray()).Write());
    }

    private static SwShMoveDataRecord CreateMoveRecord(int moveId)
    {
        var type = checked((byte)(moveId % 18));
        var category = checked((byte)(moveId % 3));
        var power = checked((byte)(category == 0 ? 0 : 20 + (moveId % 130)));

        return new SwShMoveDataRecord(
            Version: 1,
            MoveId: checked((uint)moveId),
            CanUseMove: moveId % 11 != 0,
            new SwShMoveCoreStats(
                Type: type,
                Quality: checked((byte)(moveId % 8)),
                Category: category,
                Power: power,
                Accuracy: 100,
                PP: checked((byte)(5 + (moveId % 35))),
                Priority: checked((sbyte)(moveId % 5 == 0 ? 1 : 0)),
                CritStage: checked((sbyte)(moveId % 9 == 0 ? 1 : 0)),
                GigantamaxPower: checked((byte)(90 + (moveId % 60)))),
            new SwShMoveTargeting(
                RawTarget: checked((byte)(moveId % 12)),
                HitMin: 1,
                HitMax: checked((byte)(moveId % 6 == 0 ? 5 : 1)),
                TurnMin: 0,
                TurnMax: 0),
            new SwShMoveSecondaryEffects(
                Inflict: checked((ushort)(moveId % 7)),
                InflictPercent: checked((byte)(moveId % 3 == 0 ? 30 : 0)),
                RawInflictCount: 1,
                Flinch: checked((byte)(moveId % 10 == 0 ? 10 : 0)),
                EffectSequence: checked((ushort)moveId),
                Recoil: checked((sbyte)(moveId % 17 == 0 ? -25 : 0)),
                RawHealing: checked((sbyte)(moveId % 19 == 0 ? -50 : 0))),
            [
                new SwShMoveStatChange(
                    1,
                    Stat: checked((byte)(moveId % 8)),
                    Stage: checked((sbyte)(moveId % 4 == 0 ? -1 : 0)),
                    Percent: checked((byte)(moveId % 4 == 0 ? 30 : 0))),
                new SwShMoveStatChange(2, Stat: 0, Stage: 0, Percent: 0),
                new SwShMoveStatChange(3, Stat: 0, Stage: 0, Percent: 0),
            ],
            new SwShMoveFlags(
                MakesContact: moveId % 2 == 0,
                Charge: false,
                Recharge: moveId % 23 == 0,
                Protect: true,
                Reflectable: false,
                Snatch: false,
                Mirror: false,
                Punch: moveId % 13 == 0,
                Sound: moveId % 29 == 0,
                Gravity: false,
                Defrost: false,
                DistanceTriple: false,
                Heal: moveId % 19 == 0,
                IgnoreSubstitute: false,
                FailSkyBattle: false,
                AnimateAlly: false,
                Dance: false,
                Metronome: moveId % 31 != 0));
    }

    private static void WriteShopData(TemporarySwShProject temp)
    {
        var singleShops = Enumerable.Range(0, 40)
            .Select(index => new SwShSingleShopRecord(
                0x1F3FF031A3A24490UL + (ulong)index,
                new SwShShopInventory(CreateItemIds(index, count: 12))))
            .ToArray();
        var multiShops = Enumerable.Range(0, 10)
            .Select(index => new SwShMultiShopRecord(
                0x66CA73B2966BB871UL + (ulong)index,
                Enumerable.Range(0, 6)
                    .Select(inventoryIndex => new SwShShopInventory(CreateItemIds(index + inventoryIndex, count: 10)))
                    .ToArray()))
            .ToArray();

        temp.WriteBaseRomFsFile(
            "bin/app/shop/shop_data.bin",
            new SwShShopDataFile(singleShops, multiShops).Write());
    }

    private static void WriteDataTablePack(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/data_table.gfpak",
            SwShGfPackFile.Create(
            [
                new SwShGfPackNamedFile("encount_symbol_k.bin", CreateEncounterArchive(speciesOffset: 0).Write()),
                new SwShGfPackNamedFile("encount_k.bin", CreateEncounterArchive(speciesOffset: 2).Write()),
                new SwShGfPackNamedFile("nest_hole_encount.bin", CreateRaidBattleArchive().Write()),
                new SwShGfPackNamedFile("nest_hole_drop_rewards.bin", CreateRaidArchive(itemOffset: 0).Write()),
                new SwShGfPackNamedFile("nest_hole_bonus_rewards.bin", CreateRaidArchive(itemOffset: 50).Write()),
            ]).Write());
    }

    private static SwShWildEncounterArchive CreateEncounterArchive(int speciesOffset)
    {
        var tables = Enumerable.Range(0, EncounterTableCount)
            .Select(tableIndex => new SwShWildEncounterTable(
                0x1122334400000000UL + (ulong)tableIndex,
                Enumerable.Range(0, 3)
                    .Select(subTableIndex => new SwShWildEncounterSubTable(
                        (byte)(5 + subTableIndex),
                        (byte)(20 + subTableIndex),
                        Enumerable.Range(0, EncounterSlotsPerTable / 3)
                            .Select(slotIndex => new SwShWildEncounterSlot(
                                (byte)(5 + slotIndex),
                                1 + speciesOffset + ((tableIndex + slotIndex) % 900),
                                (byte)(slotIndex % 4)))
                            .ToArray()))
                    .ToArray()))
            .ToArray();

        return new SwShWildEncounterArchive(1, tables);
    }

    private static SwShNestHoleRewardArchive CreateRaidArchive(int itemOffset)
    {
        return new SwShNestHoleRewardArchive(
            Enumerable.Range(0, RaidRewardTableCount)
                .Select(tableIndex => new SwShNestHoleRewardTable(
                    0xAABBCCDD00000000UL + (ulong)tableIndex + (ulong)itemOffset,
                    Enumerable.Range(0, RaidRewardRowsPerTable)
                        .Select(rowIndex => new SwShNestHoleReward(
                            (uint)rowIndex,
                            (uint)(1 + itemOffset + ((tableIndex * RaidRewardRowsPerTable + rowIndex) % 500)),
                            [10, 20, 30, 40, 50]))
                        .ToArray()))
                .ToArray());
    }

    private static SwShEncounterNestArchive CreateRaidBattleArchive()
    {
        return new SwShEncounterNestArchive(
            Enumerable.Range(0, RaidBattleTableCount)
                .Select(tableIndex => new SwShEncounterNestTable(
                    0xBEEFCAFE00000000UL + (ulong)tableIndex,
                    tableIndex % 2,
                    Enumerable.Range(0, RaidBattleSlotsPerTable)
                        .Select(slotIndex => new SwShEncounterNest(
                            slotIndex,
                            1 + ((tableIndex * RaidBattleSlotsPerTable + slotIndex) % 900),
                            slotIndex % 3,
                            0x1111000000000000UL + (ulong)slotIndex,
                            slotIndex % 5,
                            slotIndex % 7 == 0,
                            0xAABBCCDD00000000UL + (ulong)(tableIndex % RaidRewardTableCount),
                            0xAABBCCDD00000000UL + (ulong)(50 + (tableIndex % RaidRewardTableCount)),
                            [10, 20, 30, 40, 50],
                            slotIndex % 3,
                            slotIndex % 7))
                        .ToArray()))
                .ToArray());
    }

    private static void WritePlacement(TemporarySwShProject temp)
    {
        var packFiles = new List<SwShGfPackNamedFile>
        {
            new(
                "AreaNameHashTable.tbl",
                new SwShAhtbFile(
                    Enumerable.Range(0, PlacementAreaCount)
                        .Select(index => new SwShAhtbEntry(
                            SwShGfPackFile.HashFnv1a64($"a_perf_{index:D2}"),
                            $"a_perf_{index:D2}"))
                        .ToArray()).Write()),
            new(
                "ZoneNameHashTable.tbl",
                new SwShAhtbFile(
                    Enumerable.Range(0, PlacementAreaCount)
                        .Select(index => new SwShAhtbEntry(
                            0x8877665500000000UL + (ulong)index,
                            $"Synthetic Zone {index:D2}"))
                        .ToArray()).Write()),
            new(
                "ObjectNameHashTable.tbl",
                new SwShAhtbFile(
                    Enumerable.Range(0, PlacementAreaCount)
                        .Select(index => new SwShAhtbEntry(
                            0x7766554400000000UL + (ulong)index,
                            $"objects/perf_{index:D2}.gfbmdl"))
                        .ToArray()).Write()),
        };

        for (var areaIndex = 0; areaIndex < PlacementAreaCount; areaIndex++)
        {
            packFiles.Add(new SwShGfPackNamedFile(
                $"a_perf_{areaIndex:D2}.bin",
                CreatePlacementArchive(areaIndex).Write()));
        }

        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/placement.gfpak",
            SwShGfPackFile.Create(packFiles).Write());
    }

    private static SwShPlacementZoneArchive CreatePlacementArchive(int areaIndex)
    {
        return new SwShPlacementZoneArchive(
            Enumerable.Range(0, 4)
                .Select(zoneIndex => new SwShPlacementZone(
                    zoneIndex,
                    0x8877665500000000UL + (ulong)areaIndex,
                    0x7766554400000000UL + (ulong)areaIndex,
                    new SwShPlacementTransform(areaIndex, 0, zoneIndex, 0),
                    Enumerable.Range(0, 3)
                        .Select(objectIndex => new SwShPlacementFieldItem(
                            objectIndex,
                            $"objects/field_{areaIndex:D2}_{objectIndex:D2}.gfbmdl",
                            new SwShPlacementTransform(objectIndex, 0, zoneIndex, 45),
                            [CreateItemHash((areaIndex * 10 + objectIndex) % ItemCount)],
                            [],
                            [],
                            [],
                            Quantity: 1,
                            QuantityOffset: 0,
                            new PlacementTransformOffsets(0, 0, 0, 0)))
                        .ToArray(),
                    Enumerable.Range(0, 2)
                        .Select(objectIndex => new SwShPlacementHiddenItem(
                            objectIndex,
                            new SwShPlacementTransform(objectIndex, 0, zoneIndex, 90),
                            [
                                new SwShPlacementHiddenItemChance(
                                    ChanceIndex: 0,
                                    CreateItemHash((areaIndex * 10 + objectIndex + 3) % ItemCount),
                                    ItemId: null,
                                    Chance: 50,
                                    Quantity: 2,
                                    ItemHashOffset: 0,
                                    ChanceOffset: 0,
                                    QuantityOffset: 0),
                            ],
                            new PlacementTransformOffsets(0, 0, 0, 0)))
                        .ToArray()))
                .ToArray(),
            0x0123456700000000UL + (ulong)areaIndex,
            $"Synthetic placement archive {areaIndex:D2}",
            []);
    }

    private static void WriteFlagwork(TemporarySwShProject temp)
    {
        for (var tableIndex = 0; tableIndex < FlagworkTableCount; tableIndex++)
        {
            temp.WriteBaseRomFsFile(
                $"bin/flagwork/perf_flags_{tableIndex:D2}.tbl",
                new SwShAhtbFile(
                    Enumerable.Range(0, FlagworkRowsPerTable)
                        .Select(rowIndex => new SwShAhtbEntry(
                            0x9900000000000000UL + ((ulong)tableIndex << 16) + (ulong)rowIndex,
                            tableIndex % 2 == 0
                                ? $"FE_PERF_FLAG_{tableIndex:D2}_{rowIndex:D3}"
                                : $"WK_PERF_WORK_{tableIndex:D2}_{rowIndex:D3}"))
                        .ToArray()).Write());
        }
    }

    private static void WriteRoyalCandyPreflightInputs(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile("bin/script/amx/main_event_0020.amx", new byte[] { 0x41, 0x4D, 0x58, 0x00 });
    }

    private static void WriteExeFs(TemporarySwShProject temp)
    {
        temp.WriteBaseExeFsFile("main", CreateCompatibleNso());
        temp.WriteBaseExeFsFile("main.npdm", CreateNpdm(SwordTitleId));
    }

    private static void WriteExtraProjectFiles(TemporarySwShProject temp)
    {
        for (var index = 0; index < ExtraRomFsFileCount; index++)
        {
            temp.WriteBaseRomFsFile(
                $"bin/unused/perf_{index / 100:D2}/asset_{index:D4}.bin",
                new byte[] { (byte)(index & 0xFF), (byte)((index >> 8) & 0xFF), 0xCC, 0xDD });
        }

        for (var index = 0; index < 24; index++)
        {
            temp.WriteOutputFile(
                $"romfs/bin/unused/layered_{index:D2}.bin",
                new byte[] { 0x4C, 0x46, (byte)index });
        }
    }

    private static int[] CreateItemIds(int seed, int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => 1 + ((seed * 17 + index) % (ItemCount - 1)))
            .ToArray();
    }

    private static byte[] CreatePersonalTable(IEnumerable<byte[]> records)
    {
        var rows = records.ToArray();
        var data = new byte[rows.Length * SwShPersonalTable.RecordSize];
        for (var index = 0; index < rows.Length; index++)
        {
            rows[index].CopyTo(data.AsSpan(index * SwShPersonalTable.RecordSize));
        }

        return data;
    }

    private static byte[] CreatePokemonPersonalRecord(int pokemonId)
    {
        var record = new byte[SwShPersonalTable.RecordSize];
        record[0x00] = (byte)(35 + (pokemonId % 70));
        record[0x01] = (byte)(40 + ((pokemonId * 3) % 80));
        record[0x02] = (byte)(40 + ((pokemonId * 5) % 80));
        record[0x03] = (byte)(35 + ((pokemonId * 7) % 85));
        record[0x04] = (byte)(40 + ((pokemonId * 11) % 80));
        record[0x05] = (byte)(40 + ((pokemonId * 13) % 80));
        record[0x06] = (byte)(pokemonId % 18);
        record[0x07] = (byte)((pokemonId + 1) % 18);
        record[0x08] = (byte)(45 + (pokemonId % 120));
        record[0x09] = (byte)(pokemonId % 3);
        record[0x12] = 31;
        record[0x13] = (byte)(10 + (pokemonId % 30));
        record[0x14] = 70;
        record[0x15] = (byte)(pokemonId % 6);
        record[0x16] = (byte)(pokemonId % 16);
        record[0x17] = (byte)((pokemonId + 3) % 16);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x18), (ushort)(1 + (pokemonId % 256)));
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x1A), (ushort)(1 + ((pokemonId + 17) % 256)));
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x1C), (ushort)(1 + ((pokemonId + 31) % 256)));
        record[0x20] = 1;
        record[0x21] = (byte)((pokemonId % 14) | (1 << 6));
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x22), (ushort)(50 + (pokemonId % 200)));
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x24), (ushort)(5 + (pokemonId % 30)));
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x26), (ushort)(50 + (pokemonId % 900)));
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x56), (ushort)pokemonId);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x5C), (ushort)pokemonId);

        return record;
    }

    private static byte[] CreateLearnsetTable(int recordCount)
    {
        var data = new byte[recordCount * SwShPokemonLearnsetTable.RecordSize];
        for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
        {
            var recordOffset = recordIndex * SwShPokemonLearnsetTable.RecordSize;
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(recordOffset), (ushort)(1 + (recordIndex % 16)));
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(recordOffset + 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(recordOffset + 4), (ushort)(1 + ((recordIndex + 1) % 16)));
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(recordOffset + 6), 5);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(recordOffset + 8), ushort.MaxValue);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(recordOffset + 10), ushort.MaxValue);
        }

        return data;
    }

    private static string[] CreateIndexedNames(string prefix, int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => $"{prefix} {index}")
            .ToArray();
    }

    private static byte[] CreateTextTable(params string[] lines)
    {
        return SwShGameTextFile.Write(lines.Select(line => new SwShGameTextLine(line, Flags: 0)).ToArray());
    }

    private static ulong CreateItemHash(int itemId)
    {
        return 0xAABBCCDD00000000UL + (uint)itemId;
    }

    private static byte[] CreateNpdm(ulong titleId)
    {
        var data = new byte[0x298];
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0x290, sizeof(ulong)), titleId);
        return data;
    }

    internal static byte[] CreateCompatibleNso()
    {
        return CreateNso(CreateCompatibleText(), [0x10], [0x20]);
    }

    private static byte[] CreateCompatibleText()
    {
        var text = new byte[0x007DDA90];
        WriteInstruction(text, 0x00747988, EncodeCmpImmediate(28, 50));
        WriteInstruction(text, 0x00747D44, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0074BA24, EncodeCmpImmediate(26, 50));
        WriteInstruction(text, 0x0074BDA8, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0074DFE4, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0074DFF8, EncodeCmpImmediate(28, 50));
        WriteInstruction(text, 0x0075CEFC, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x007BB204, EncodeCmpImmediate(20, 50));
        WriteInstruction(text, 0x007BB3C0, EncodeCmpImmediate(19, 50));
        WriteInstruction(text, 0x007BC1F8, EncodeCmpImmediate(8, 50));
        WriteInstruction(text, 0x00747DE0, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0074BE44, EncodeCmpImmediate(9, 50));
        WriteInstruction(text, 0x0075CCE8, EncodeCmpImmediate(27, 50));
        WriteInstruction(text, 0x0075D08C, EncodeCmpImmediate(10, 50));
        WriteInstruction(text, 0x007BBFD4, EncodeCmpImmediate(23, 50));
        WriteInstruction(text, 0x007BC1BC, EncodeCmpImmediate(9, 4));
        WriteInstruction(text, 0x007BC1C4, EncodeCmpImmediate(9, 4));
        WriteInstruction(text, 0x007B1F20, 0x2A0003E2);
        WriteInstruction(text, 0x007DDA8C, EncodeCmpImmediate(8, 0x32));
        return text;
    }

    private static void WriteInstruction(byte[] text, int offset, uint instruction)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(text.AsSpan(offset, sizeof(uint)), instruction);
    }

    private static uint EncodeCmpImmediate(int register, int immediate)
    {
        return (uint)(0x7100001F | ((immediate & 0xFFF) << 10) | ((register & 0x1F) << 5));
    }

    private static byte[] CreateNso(byte[] text, byte[] ro, byte[] data)
    {
        var textOffset = SwShNsoFile.HeaderSize;
        var roOffset = Align(textOffset + text.Length, 0x10);
        var dataOffset = Align(roOffset + ro.Length, 0x10);
        var output = new byte[Align(dataOffset + data.Length, 0x10)];

        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x00), SwShNsoFile.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0x04), 1);
        WriteSegmentHeader(output, 0x10, textOffset, 0, text.Length);
        WriteSegmentHeader(output, 0x20, roOffset, text.Length, ro.Length);
        WriteSegmentHeader(output, 0x30, dataOffset, text.Length + ro.Length, data.Length);
        output.AsSpan(0x40, 0x20).Fill(0xAB);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x60), text.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x64), ro.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(0x68), data.Length);
        SwShNsoFile.ComputeHash(text).CopyTo(output.AsSpan(0xA0));
        SwShNsoFile.ComputeHash(ro).CopyTo(output.AsSpan(0xC0));
        SwShNsoFile.ComputeHash(data).CopyTo(output.AsSpan(0xE0));
        text.CopyTo(output.AsSpan(textOffset));
        ro.CopyTo(output.AsSpan(roOffset));
        data.CopyTo(output.AsSpan(dataOffset));
        return output;
    }

    private static void WriteSegmentHeader(
        byte[] output,
        int offset,
        int fileOffset,
        int memoryOffset,
        int decompressedSize)
    {
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset), fileOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x04), memoryOffset);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(offset + 0x08), decompressedSize);
    }

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }
}
