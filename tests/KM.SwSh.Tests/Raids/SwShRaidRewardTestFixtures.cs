// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.Tests.Items;

namespace KM.SwSh.Tests.Raids;

internal static class SwShRaidRewardTestFixtures
{
    public const ulong DropTableId = 0xAABBCCDD00112233;
    public const ulong BonusTableId = 0x1020304050607080;

    public static void WriteBaseRaidRewards(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/data_table.gfpak",
            CreateRaidRewardPack());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames(
                "",
                "Potion",
                "Rare Candy",
                "Exp. Candy L",
                "Armorite Ore"));
        temp.WriteBaseRomFsFile(
            "bin/pml/item/item.dat",
            SwShItemTestFixtures.CreateItemTable(
                new ItemFixtureRecord(0, 0, 0, 0, 0, SwShItemPouch.Items),
                new ItemFixtureRecord(1, 1, 300, 15, 0, SwShItemPouch.Medicine),
                new ItemFixtureRecord(2, 2, 10_000, 0, 0, SwShItemPouch.Items),
                new ItemFixtureRecord(3, 3, 0, 0, 0, SwShItemPouch.Items),
                new ItemFixtureRecord(4, 4, 0, 0, 0, SwShItemPouch.Items)));
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
