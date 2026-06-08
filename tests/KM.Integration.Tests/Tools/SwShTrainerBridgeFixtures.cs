// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;

namespace KM.Integration.Tests.Tools;

internal static class SwShTrainerBridgeFixtures
{
    public static void WriteBaseTrainers(TemporaryBridgeProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/trainer/trainer_data/trainer_010.bin",
            CreateTrainerData(
                classId: 5,
                battleMode: 1,
                pokemonCount: 1,
                items: [1, 2, 0, 0],
                aiFlags: 0x4D,
                heal: true,
                money: 24,
                gift: 7));
        temp.WriteBaseRomFsFile("bin/trainer/trainer_type/trainer_type_005.bin", CreateTrainerClass(ballId: 4));
        temp.WriteBaseRomFsFile(
            "bin/trainer/trainer_poke/trainer_010.bin",
            CreateTrainerTeam((speciesId: 810, level: 12, heldItemId: 1, moves: new[] { 1, 2, 0, 0 })));
        temp.WriteBaseRomFsFile("bin/message/English/common/trname.dat", CreateTextTable(10, (10, "Avery")));
        temp.WriteBaseRomFsFile("bin/message/English/common/trtype.dat", CreateTextTable(5, (5, "Pokemon Trainer")));
        temp.WriteBaseRomFsFile("bin/message/English/common/monsname.dat", CreateTextTable(810, (810, "Grookey")));
        temp.WriteBaseRomFsFile("bin/message/English/common/itemname.dat", CreateTextTable(2, (1, "Potion"), (2, "Antidote")));
        temp.WriteBaseRomFsFile("bin/message/English/common/wazaname.dat", CreateTextTable(2, (1, "Scratch"), (2, "Growl")));
    }

    public static byte[] CreateTrainerData(
        int classId,
        int battleMode,
        int pokemonCount,
        int[]? items = null,
        int aiFlags = 0,
        bool heal = false,
        int money = 0,
        int gift = 0)
    {
        var data = new byte[SwShTrainerDataFile.Size];
        var itemIds = items ?? [0, 0, 0, 0];
        WriteUInt16(data, 0x00, classId);
        data[0x02] = checked((byte)battleMode);
        data[0x03] = checked((byte)pokemonCount);
        WriteUInt16(data, 0x04, itemIds[0]);
        WriteUInt16(data, 0x06, itemIds[1]);
        WriteUInt16(data, 0x08, itemIds[2]);
        WriteUInt16(data, 0x0A, itemIds[3]);
        WriteUInt32(data, 0x0C, checked((uint)aiFlags));
        data[0x10] = heal ? (byte)1 : (byte)0;
        data[0x11] = checked((byte)money);
        WriteUInt16(data, 0x12, gift);

        return data;
    }

    public static byte[] CreateTrainerTeam(params (int speciesId, int level, int heldItemId, int[] moves)[] pokemon)
    {
        var data = new byte[pokemon.Length * SwShTrainerTeamFile.RowSize];

        for (var index = 0; index < pokemon.Length; index++)
        {
            var rowOffset = index * SwShTrainerTeamFile.RowSize;
            var record = pokemon[index];
            if (index == 0)
            {
                data[rowOffset] = 0x21;
                data[rowOffset + 0x01] = 13;
                data[rowOffset + 0x02] = 10;
                data[rowOffset + 0x03] = 20;
                data[rowOffset + 0x04] = 30;
                data[rowOffset + 0x05] = 40;
                data[rowOffset + 0x06] = 50;
                data[rowOffset + 0x07] = 60;
                data[rowOffset + 0x08] = 7;
                data[rowOffset + 0x09] = 1;
                WriteUInt32(data, rowOffset + 0x1C, PackIvs(1, 2, 3, 4, 5, 6, shiny: true, canDynamax: false));
            }

            WriteUInt16(data, rowOffset + 0x0A, record.level);
            WriteUInt16(data, rowOffset + 0x0C, record.speciesId);
            WriteUInt16(data, rowOffset + 0x10, record.heldItemId);
            WriteUInt16(data, rowOffset + 0x12, record.moves[0]);
            WriteUInt16(data, rowOffset + 0x14, record.moves[1]);
            WriteUInt16(data, rowOffset + 0x16, record.moves[2]);
            WriteUInt16(data, rowOffset + 0x18, record.moves[3]);
        }

        return data;
    }

    public static byte[] CreateTrainerClass(int ballId)
    {
        var data = new byte[SwShTrainerClassFile.Size];
        data[0x01] = 8;
        data[0x02] = checked((byte)ballId);

        return data;
    }

    private static byte[] CreateTextTable(int highestIndex, params (int index, string value)[] entries)
    {
        var lines = Enumerable.Range(0, highestIndex + 1)
            .Select(index => new SwShGameTextLine($"Value {index}", Flags: 0))
            .ToArray();

        foreach (var entry in entries)
        {
            lines[entry.index] = new SwShGameTextLine(entry.value, Flags: 0);
        }

        return SwShGameTextFile.Write(lines);
    }

    private static void WriteUInt16(byte[] data, int offset, int value)
    {
        data[offset] = checked((byte)(value & 0xFF));
        data[offset + 1] = checked((byte)(value >> 8));
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = checked((byte)(value & 0xFF));
        data[offset + 1] = checked((byte)((value >> 8) & 0xFF));
        data[offset + 2] = checked((byte)((value >> 16) & 0xFF));
        data[offset + 3] = checked((byte)(value >> 24));
    }

    private static uint PackIvs(
        int hp,
        int attack,
        int defense,
        int speed,
        int specialAttack,
        int specialDefense,
        bool shiny,
        bool canDynamax)
    {
        return (uint)hp
            | ((uint)attack << 5)
            | ((uint)defense << 10)
            | ((uint)speed << 15)
            | ((uint)specialAttack << 20)
            | ((uint)specialDefense << 25)
            | (shiny ? 1u << 30 : 0)
            | (canDynamax ? 1u << 31 : 0);
    }
}
