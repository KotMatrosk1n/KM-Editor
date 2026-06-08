// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.Raids;
using KM.SwSh.Tests.Items;

namespace KM.SwSh.Tests.Raids;

internal static class SwShRaidBattleTestFixtures
{
    public const ulong BattleTableId = 0xAABBCCDD00112233;

    public static void WriteBaseRaidBattles(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            SwShRaidRewardsWorkflowService.NestDataPath["romfs/".Length..],
            CreateRaidBattlePack());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            CreateTextTable(133, (25, "Pikachu"), (133, "Eevee")));
    }

    public static byte[] CreateRaidBattlePack()
    {
        return SwShGfPackFile.Create(
        [
            new SwShGfPackNamedFile(SwShRaidBattlesWorkflowService.EncounterMemberName, CreateArchive().Write()),
            new SwShGfPackNamedFile("nest_hole_drop_rewards.bin", SwShRaidRewardTestFixtures.CreateDropArchive().Write()),
        ]).Write();
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
                        [5, 10, 15, 20, 25],
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
}
