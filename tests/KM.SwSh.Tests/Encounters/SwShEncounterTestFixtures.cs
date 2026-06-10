// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Tests.Items;

namespace KM.SwSh.Tests.Encounters;

internal static class SwShEncounterTestFixtures
{
    private const int NpdmTitleIdOffset = 0x290;
    private const ulong SwordTitleId = 0x0100ABF008968000;
    private const ulong ShieldTitleId = 0x01008DB008C2C000;

    public const ulong ZoneId = 0x1122334455667788;
    public const ulong BridgeFieldFlyingZoneId = 0x5F4E0AB29FD3F13A;
    public const ulong BallimereLakeSurfingZoneId = 0x9BDD6D11FFBEDA3F;

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

    public static void WriteSelectedGameNpdm(TemporarySwShProject temp, ProjectGame game)
    {
        temp.WriteBaseExeFsFile(
            "main.npdm",
            CreateNpdm(game == ProjectGame.Sword ? SwordTitleId : ShieldTitleId));
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

    public static SwShWildEncounterArchive CreateArchiveForZones(params ulong[] zoneIds)
    {
        return new SwShWildEncounterArchive(
            1,
            zoneIds.Select((zoneId, index) => new SwShWildEncounterTable(
                zoneId,
                [
                    new SwShWildEncounterSubTable(
                        3,
                        8,
                        [
                            new SwShWildEncounterSlot(35, 1 + index, 0),
                            new SwShWildEncounterSlot(65, 4 + index, 1),
                        ]),
                ])).ToArray());
    }

    private static byte[] CreateNpdm(ulong titleId)
    {
        var npdm = new byte[NpdmTitleIdOffset + sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(npdm.AsSpan(NpdmTitleIdOffset), titleId);
        return npdm;
    }
}
