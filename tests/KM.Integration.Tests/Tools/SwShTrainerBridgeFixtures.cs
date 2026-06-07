// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;

namespace KM.Integration.Tests.Tools;

internal static class SwShTrainerBridgeFixtures
{
    public static void WriteBaseTrainers(TemporaryBridgeProject temp)
    {
        temp.WriteBaseRomFsFile("bin/trainer/trainer_data/trainer_010.bin", CreateTrainerData(classId: 5, battleMode: 1, pokemonCount: 1));
        temp.WriteBaseRomFsFile(
            "bin/trainer/trainer_poke/trainer_010.bin",
            CreateTrainerTeam((speciesId: 810, level: 12, heldItemId: 1, moves: new[] { 1, 2, 0, 0 })));
        temp.WriteBaseRomFsFile("bin/message/English/common/trname.dat", CreateTextTable(10, (10, "Avery")));
        temp.WriteBaseRomFsFile("bin/message/English/common/trtype.dat", CreateTextTable(5, (5, "Pokemon Trainer")));
        temp.WriteBaseRomFsFile("bin/message/English/common/monsname.dat", CreateTextTable(810, (810, "Grookey")));
        temp.WriteBaseRomFsFile("bin/message/English/common/itemname.dat", CreateTextTable(1, (1, "Potion")));
        temp.WriteBaseRomFsFile("bin/message/English/common/wazaname.dat", CreateTextTable(2, (1, "Scratch"), (2, "Growl")));
    }

    public static byte[] CreateTrainerData(int classId, int battleMode, int pokemonCount)
    {
        var data = new byte[SwShTrainerDataFile.Size];
        WriteUInt16(data, 0x00, classId);
        data[0x02] = checked((byte)battleMode);
        data[0x03] = checked((byte)pokemonCount);

        return data;
    }

    public static byte[] CreateTrainerTeam(params (int speciesId, int level, int heldItemId, int[] moves)[] pokemon)
    {
        var data = new byte[pokemon.Length * SwShTrainerTeamFile.RowSize];

        for (var index = 0; index < pokemon.Length; index++)
        {
            var rowOffset = index * SwShTrainerTeamFile.RowSize;
            var record = pokemon[index];
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
}
