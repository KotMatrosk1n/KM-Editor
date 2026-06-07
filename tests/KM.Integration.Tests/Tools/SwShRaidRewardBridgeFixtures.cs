// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;

namespace KM.Integration.Tests.Tools;

internal static class SwShRaidRewardBridgeFixtures
{
    public const ulong DropTableId = 0xAABBCCDD00112233;
    public const ulong BonusTableId = 0x1020304050607080;

    public static void WriteBaseRaidRewards(TemporaryBridgeProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/data_table.gfpak",
            CreateRaidRewardPack());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShGameTextFile.Write(
            [
                new SwShGameTextLine("", Flags: 0),
                new SwShGameTextLine("Potion", Flags: 0),
                new SwShGameTextLine("Rare Candy", Flags: 0),
                new SwShGameTextLine("Exp. Candy L", Flags: 0),
                new SwShGameTextLine("Armorite Ore", Flags: 0),
            ]));
    }

    public static byte[] CreateRaidRewardPack()
    {
        return SwShGfPackFile.Create(
        [
            new SwShGfPackNamedFile("nest_hole_drop_rewards.bin", CreateDropArchive().Write()),
            new SwShGfPackNamedFile("nest_hole_bonus_rewards.bin", CreateBonusArchive().Write()),
        ]).Write();
    }

    public static SwShNestHoleRewardArchive CreateDropArchive()
    {
        return new SwShNestHoleRewardArchive(
        [
            new SwShNestHoleRewardTable(
                DropTableId,
                [
                    new SwShNestHoleReward(10, 3, [40, 30, 20, 10, 5]),
                    new SwShNestHoleReward(11, 2, [5, 10, 15, 20, 25]),
                ]),
        ]);
    }

    public static SwShNestHoleRewardArchive CreateBonusArchive()
    {
        return new SwShNestHoleRewardArchive(
        [
            new SwShNestHoleRewardTable(
                BonusTableId,
                [
                    new SwShNestHoleReward(20, 4, [1, 2, 3, 4, 5]),
                ]),
        ]);
    }
}
