// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.Pokemon;
using KM.SwSh.Raids;
using KM.SwSh.Tests.Items;
using KM.SwSh.Tests.Pokemon;
using System.Buffers.Binary;

namespace KM.SwSh.Tests.Raids;

internal static class SwShRaidBattleTestFixtures
{
    public const ulong BattleTableId = 0xAABBCCDD00112233;

    public static void WriteBaseRaidBattles(TemporarySwShProject temp)
    {
        WriteBaseRaidBattles(temp, CreateArchive());
    }

    public static void WriteBaseRaidBattles(
        TemporarySwShProject temp,
        SwShEncounterNestArchive encounterArchive)
    {
        ArgumentNullException.ThrowIfNull(encounterArchive);

        temp.WriteBaseRomFsFile(
            SwShRaidRewardsWorkflowService.NestDataPath["romfs/".Length..],
            CreateRaidBattlePack(encounterArchive));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            CreateTextTable(133, (25, "Pikachu"), (133, "Eevee")));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames(
                "",
                "Potion",
                "Rare Candy",
                "Exp. Candy L",
                "Armorite Ore"));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/tokusei.dat",
            CreateTextTable(
                107,
                (9, "Static"),
                (31, "Lightning Rod"),
                (34, "Chlorophyll"),
                (50, "Run Away"),
                (65, "Overgrow"),
                (91, "Adaptability"),
                (107, "Anticipation")));

        var personalRecords = Enumerable.Range(0, 136)
            .Select(_ => SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord())
            .ToArray();
        personalRecords[1] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hatchedSpecies: 1);
        personalRecords[1][0x12] = 0;
        personalRecords[25] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hatchedSpecies: 25);
        WriteAbilities(personalRecords[25], ability1: 9, ability2: 0, hiddenAbility: 31);
        personalRecords[133] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hatchedSpecies: 133,
            formStatsIndex: 134,
            formCount: 3);
        WriteAbilities(personalRecords[133], ability1: 50, ability2: 91, hiddenAbility: 107);
        personalRecords[134] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hatchedSpecies: 133,
            formStatsIndex: 134,
            formCount: 3,
            form: 1);
        WriteAbilities(personalRecords[134], ability1: 50, ability2: 91, hiddenAbility: 107);
        personalRecords[135] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(
            hatchedSpecies: 133,
            formStatsIndex: 134,
            formCount: 3,
            form: 2);
        WriteAbilities(personalRecords[135], ability1: 50, ability2: 91, hiddenAbility: 107);
        temp.WriteBaseRomFsFile(
            SwShPokemonWorkflowService.PersonalDataPath["romfs/".Length..],
            SwShPokemonWorkflowServiceTests.CreatePersonalTable(personalRecords));
    }

    public static void WriteBaseRaidBattlesWithFirstProbabilityCount(
        TemporarySwShProject temp,
        uint probabilityCount)
    {
        WriteBaseRaidBattles(temp, CreatePairedArchive());

        var encounterData = CreatePairedArchive().Write();
        Span<byte> pattern = stackalloc byte[sizeof(uint) * 6];
        BinaryPrimitives.WriteUInt32LittleEndian(pattern, 5);
        BinaryPrimitives.WriteUInt32LittleEndian(pattern[4..], 100);
        BinaryPrimitives.WriteUInt32LittleEndian(pattern[8..], 20);
        BinaryPrimitives.WriteUInt32LittleEndian(pattern[12..], 30);
        BinaryPrimitives.WriteUInt32LittleEndian(pattern[16..], 40);
        BinaryPrimitives.WriteUInt32LittleEndian(pattern[20..], 50);
        var vectorOffset = encounterData.AsSpan().IndexOf(pattern);
        if (vectorOffset < 0)
        {
            throw new InvalidOperationException("Raid battle probability vector fixture was not found.");
        }

        BinaryPrimitives.WriteUInt32LittleEndian(encounterData.AsSpan(vectorOffset), probabilityCount);
        var pack = SwShGfPackFile.Create(
        [
            new SwShGfPackNamedFile(SwShRaidBattlesWorkflowService.EncounterMemberName, encounterData),
            new SwShGfPackNamedFile("nest_hole_drop_rewards.bin", SwShRaidRewardTestFixtures.CreateDropArchive().Write()),
            new SwShGfPackNamedFile("nest_hole_bonus_rewards.bin", SwShRaidRewardTestFixtures.CreateBonusArchive().Write()),
        ]);
        temp.WriteBaseRomFsFile(
            SwShRaidRewardsWorkflowService.NestDataPath["romfs/".Length..],
            pack.Write());
    }

    public static byte[] CreateRaidBattlePack()
    {
        return CreateRaidBattlePack(CreateArchive());
    }

    public static byte[] CreateRaidBattlePack(SwShEncounterNestArchive encounterArchive)
    {
        ArgumentNullException.ThrowIfNull(encounterArchive);

        return SwShGfPackFile.Create(
        [
            new SwShGfPackNamedFile(SwShRaidBattlesWorkflowService.EncounterMemberName, encounterArchive.Write()),
            new SwShGfPackNamedFile("nest_hole_drop_rewards.bin", SwShRaidRewardTestFixtures.CreateDropArchive().Write()),
            new SwShGfPackNamedFile("nest_hole_bonus_rewards.bin", SwShRaidRewardTestFixtures.CreateBonusArchive().Write()),
        ]).Write();
    }

    public static SwShEncounterNestArchive CreatePairedArchive()
    {
        var swordTable = CreateArchive().Tables[0];
        return new SwShEncounterNestArchive(
        [
            swordTable,
            new SwShEncounterNestTable(BattleTableId + 1, 2, Array.Empty<SwShEncounterNest>()),
        ]);
    }

    public static SwShEncounterNestArchive CreateArchive()
    {
        return new SwShEncounterNestArchive(
        [
            new SwShEncounterNestTable(
                BattleTableId,
                1,
                [
                    new SwShEncounterNest(
                        0,
                        133,
                        1,
                        0x1122334455667788,
                        4,
                        true,
                        SwShRaidRewardTestFixtures.DropTableId,
                        SwShRaidRewardTestFixtures.BonusTableId,
                        [100, 20, 30, 40, 50],
                        1,
                        4),
                    new SwShEncounterNest(
                        1,
                        25,
                        0,
                        0x2233445566778899,
                        0,
                        false,
                        SwShRaidRewardTestFixtures.DropTableId,
                        0x0807060504030201,
                        [0, 80, 70, 60, 50],
                        0,
                        0),
                ]),
        ]);
    }

    private static byte[] CreateTextTable(int highestIndex, params (int index, string value)[] entries)
    {
        var lines = Enumerable.Range(0, highestIndex + 1)
            .Select(_ => new SwShGameTextLine(string.Empty, Flags: 0))
            .ToArray();

        foreach (var (index, value) in entries)
        {
            lines[index] = new SwShGameTextLine(value, Flags: 0);
        }

        return SwShGameTextFile.Write(lines);
    }

    private static void WriteAbilities(byte[] record, int ability1, int ability2, int hiddenAbility)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x18), checked((ushort)ability1));
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x1A), checked((ushort)ability2));
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x1C), checked((ushort)hiddenAbility));
    }
}
