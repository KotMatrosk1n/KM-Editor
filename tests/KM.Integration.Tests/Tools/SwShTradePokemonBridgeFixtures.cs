// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;

namespace KM.Integration.Tests.Tools;

internal static class SwShTradePokemonBridgeFixtures
{
    public static void WriteBaseTradePokemon(TemporaryBridgeProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/script_event_data/field_trade.bin",
            CreateTradeTable(new SwShTradePokemonIvs(31, 30, 29, 28, 27, 26)));
        temp.WriteBaseRomFsFile("bin/message/English/common/monsname.dat", CreateTextTable(810, (25, "Pikachu"), (133, "Eevee"), (810, "Grookey")));
        temp.WriteBaseRomFsFile("bin/message/English/common/itemname.dat", CreateTextTable(4, (1, "Potion"), (4, "Poke Ball")));
        temp.WriteBaseRomFsFile("bin/message/English/common/wazaname.dat", CreateTextTable(4, (1, "Scratch"), (2, "Growl"), (3, "Vine Whip"), (4, "Razor Leaf")));
    }

    public static byte[] CreateTradeTable(SwShTradePokemonIvs firstTradeIvs)
    {
        return new SwShTradePokemonArchive(
        [
            new SwShTradePokemonRecord(
                0,
                1,
                10,
                4,
                0,
                0x1122334455667788,
                true,
                1,
                50,
                810,
                0x8877665544332211,
                123456,
                11,
                0x1234,
                12,
                13,
                0x0102030405060708,
                1,
                2,
                25,
                24,
                0,
                0,
                25,
                0,
                firstTradeIvs,
                3,
                [1, 2, 3, 4]),
            new SwShTradePokemonRecord(
                1,
                0,
                0,
                4,
                0,
                0,
                false,
                0,
                1,
                133,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                new SwShTradePokemonIvs(-1, -1, -1, -1, -1, -1),
                0,
                [0, 0, 0, 0]),
        ]).Write();
    }

    private static byte[] CreateTextTable(int highestIndex, params (int index, string value)[] entries)
    {
        var lines = Enumerable.Range(0, highestIndex + 1)
            .Select(index => new SwShGameTextLine($"Value {index}", Flags: 0))
            .ToArray();

        foreach (var (index, value) in entries)
        {
            lines[index] = new SwShGameTextLine(value, Flags: 0);
        }

        return SwShGameTextFile.Write(lines);
    }
}
