// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;

namespace KM.Integration.Tests.Tools;

internal static class SwShRentalPokemonBridgeFixtures
{
    public static void WriteBaseRentalPokemon(TemporaryBridgeProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/script_event_data/rental.bin",
            CreateRentalTable(new SwShRentalPokemonStats(31, 31, 31, 31, 31, 31)));
        temp.WriteBaseRomFsFile("bin/message/English/common/monsname.dat", CreateTextTable(133, (25, "Pikachu"), (133, "Eevee")));
        temp.WriteBaseRomFsFile("bin/message/English/common/itemname.dat", CreateTextTable(4, (1, "Potion"), (4, "Poke Ball")));
        temp.WriteBaseRomFsFile("bin/message/English/common/wazaname.dat", CreateTextTable(4, (1, "Tackle"), (2, "Growl"), (3, "Vine Whip"), (4, "Razor Leaf")));
    }

    public static byte[] CreateRentalTable(SwShRentalPokemonStats firstRentalIvs)
    {
        return new SwShRentalPokemonArchive(
        [
            new SwShRentalPokemonRecord(
                0,
                new SwShRentalPokemonStats(10, 20, 30, 40, 50, 60),
                1,
                4,
                0x1122334455667788,
                1,
                50,
                133,
                0x8877665544332211,
                12345,
                13,
                1,
                firstRentalIvs,
                2,
                [1, 2, 3, 4]),
            new SwShRentalPokemonRecord(
                1,
                new SwShRentalPokemonStats(0, 0, 0, 0, 0, 0),
                0,
                4,
                0,
                0,
                1,
                25,
                0,
                0,
                0,
                0,
                new SwShRentalPokemonStats(0, 0, 0, 0, 0, 0),
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
