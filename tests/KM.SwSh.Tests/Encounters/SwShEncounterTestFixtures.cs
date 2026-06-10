// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.Tests.Items;

namespace KM.SwSh.Tests.Encounters;

internal static class SwShEncounterTestFixtures
{
    public const ulong ZoneId = 0x1122334455667788;

    public static void WriteBaseEncounters(TemporarySwShProject temp)
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

    public static SwShWildEncounterArchive CreateArchive(
        int speciesOffset = 0,
        int? firstSlotSpecies = null,
        int? firstSlotProbability = null,
        int? secondSlotProbability = null,
        ulong? zoneId = null,
        IReadOnlyList<SwShWildEncounterSubTable>? subTables = null)
    {
        return new SwShWildEncounterArchive(
            1,
            [
                new SwShWildEncounterTable(
                    zoneId ?? ZoneId,
                    subTables ??
                    [
                        new SwShWildEncounterSubTable(
                            3,
                            8,
                            [
                                new SwShWildEncounterSlot((byte)(firstSlotProbability ?? 35), firstSlotSpecies ?? 1 + speciesOffset, 0),
                                new SwShWildEncounterSlot((byte)(secondSlotProbability ?? 65), 4 + speciesOffset, 1),
                            ]),
                    ]),
            ]);
    }
}
