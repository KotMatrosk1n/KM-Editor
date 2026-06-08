// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;

namespace KM.Integration.Tests.Tools;

internal static class SwShEncounterBridgeFixtures
{
    public const ulong ZoneId = 0x1122334455667788;

    public static void WriteBaseEncounters(TemporaryBridgeProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/data_table.gfpak",
            CreateWildDataPack());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            SwShGameTextFile.Write(
            [
                new SwShGameTextLine("", Flags: 0),
                new SwShGameTextLine("Bulbasaur", Flags: 0),
                new SwShGameTextLine("Ivysaur", Flags: 0),
                new SwShGameTextLine("Venusaur", Flags: 0),
                new SwShGameTextLine("Charmander", Flags: 0),
                new SwShGameTextLine("Charmeleon", Flags: 0),
                new SwShGameTextLine("Charizard", Flags: 0),
            ]));
    }

    public static byte[] CreateWildDataPack()
    {
        return SwShGfPackFile.Create(
        [
            new SwShGfPackNamedFile("encount_symbol_k.bin", CreateArchive().Write()),
            new SwShGfPackNamedFile("encount_k.bin", CreateArchive(speciesOffset: 2).Write()),
        ]).Write();
    }

    public static SwShWildEncounterArchive CreateArchive(int speciesOffset = 0)
    {
        return new SwShWildEncounterArchive(
            1,
            [
                new SwShWildEncounterTable(
                    ZoneId,
                    [
                        new SwShWildEncounterSubTable(
                            3,
                            8,
                            [
                                new SwShWildEncounterSlot(35, 1 + speciesOffset, 0),
                                new SwShWildEncounterSlot(65, 4 + speciesOffset, 1),
                            ]),
                    ]),
            ]);
    }
}
