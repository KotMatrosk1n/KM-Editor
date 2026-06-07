// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.Tests.Items;
using KM.SwSh.Tests.Trainers;
using System.Buffers.Binary;

namespace KM.SwSh.Tests.Performance;

internal static class SwShPerformanceFixtureProject
{
    public const int ItemCount = 1_500;
    public const int TrainerCount = 120;
    public const int TextTableCount = 120;
    public const int TextLinesPerTable = 40;
    public const int ExtraRomFsFileCount = 600;
    public const int EncounterTableCount = 48;
    public const int EncounterSlotsPerTable = 24;
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
        WriteTextTables(temp);
        WriteTrainers(temp);
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
