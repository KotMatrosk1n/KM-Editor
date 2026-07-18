// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.RoyalCandy;
using KM.SwSh.Tests.Placement;
using Xunit;

namespace KM.SwSh.Tests.RoyalCandy;

public sealed class SwShRoyalCandyAcquisitionPatcherTests
{
    private const ulong RoyalCandyHash = 0xA6799EA1261B6824;
    private const ulong RareCandyHash = 0x0C05A768BC75B196;
    private const ulong ConflictHash = 0xDEADBEEF00112233;

    [Fact]
    public void RaidRewardsRoundTripOnlyVanillaExpCandyXlOccurrences()
    {
        var basePackBytes = CreateRaidPack();
        var basePack = SwShGfPackFile.Parse(basePackBytes);
        var unrelated = basePack.GetFileByName("unrelated.bin");

        var installed = SwShRoyalCandyAcquisitionPatcher.ApplyRaidRewards(
            basePackBytes,
            basePackBytes);

        Assert.Equal(2, installed.Before.BaseOccurrenceCount);
        Assert.Equal(2, installed.Before.OriginalOccurrenceCount);
        Assert.Equal(0, installed.Before.ReplacementOccurrenceCount);
        Assert.Equal(2, installed.ChangedOccurrenceCount);
        Assert.Equal(0, installed.After.OriginalOccurrenceCount);
        Assert.Equal(2, installed.After.ReplacementOccurrenceCount);
        Assert.Empty(installed.After.Conflicts);

        var installedPack = SwShGfPackFile.Parse(installed.Output);
        Assert.Equal(unrelated, installedPack.GetFileByName("unrelated.bin"));
        Assert.Equal(
            [50u, 50u],
            ReadRewardItems(installedPack, "nest_hole_drop_rewards.bin"));
        Assert.Equal(
            [50u, 50u],
            ReadRewardItems(installedPack, "nest_hole_bonus_rewards.bin"));

        var repeatedInstall = SwShRoyalCandyAcquisitionPatcher.ApplyRaidRewards(
            installed.Output,
            basePackBytes);
        Assert.Equal(0, repeatedInstall.ChangedOccurrenceCount);
        Assert.Equal(installed.Output, repeatedInstall.Output);

        var restored = SwShRoyalCandyAcquisitionPatcher.RestoreRaidRewards(
            installed.Output,
            basePackBytes);
        Assert.Equal(2, restored.ChangedOccurrenceCount);
        Assert.Equal(2, restored.After.OriginalOccurrenceCount);
        Assert.Equal(0, restored.After.ReplacementOccurrenceCount);
        Assert.Equal(basePackBytes, restored.Output);

        var restoredPack = SwShGfPackFile.Parse(restored.Output);
        Assert.Equal(
            [1128u, 50u],
            ReadRewardItems(restoredPack, "nest_hole_drop_rewards.bin"));
        Assert.Equal(
            [50u, 1128u],
            ReadRewardItems(restoredPack, "nest_hole_bonus_rewards.bin"));
        Assert.Equal(unrelated, restoredPack.GetFileByName("unrelated.bin"));
    }

    [Fact]
    public void RaidRewardsReportAndPreserveConflictingOwnedSlot()
    {
        var basePackBytes = CreateRaidPack();
        var targetPack = SwShGfPackFile.Parse(basePackBytes);
        var drop = SwShNestHoleRewardArchive.Parse(
            targetPack.GetFileByName("nest_hole_drop_rewards.bin"));
        targetPack.SetFileByName(
            "nest_hole_drop_rewards.bin",
            drop.WriteEdits(
            [
                new SwShNestHoleRewardEdit(
                    0,
                    0,
                    SwShNestHoleRewardField.ItemId,
                    999),
            ]));
        var targetPackBytes = targetPack.Write();

        var installed = SwShRoyalCandyAcquisitionPatcher.ApplyRaidRewards(
            targetPackBytes,
            basePackBytes);

        Assert.Equal(2, installed.Before.BaseOccurrenceCount);
        Assert.Equal(1, installed.Before.OriginalOccurrenceCount);
        Assert.Equal(0, installed.Before.ReplacementOccurrenceCount);
        Assert.Equal(1, installed.Before.ConflictOccurrenceCount);
        Assert.Equal(1, installed.ChangedOccurrenceCount);
        var conflict = Assert.Single(installed.After.Conflicts);
        Assert.Equal(999ul, conflict.CurrentValue);

        var output = SwShGfPackFile.Parse(installed.Output);
        Assert.Equal(
            [999u, 50u],
            ReadRewardItems(output, "nest_hole_drop_rewards.bin"));
        Assert.Equal(
            [50u, 50u],
            ReadRewardItems(output, "nest_hole_bonus_rewards.bin"));
    }

    [Fact]
    public void RaidRewardsRestorePreservesUnrelatedLayeredMemberChange()
    {
        var basePackBytes = CreateRaidPack();
        var layeredPack = SwShGfPackFile.Parse(basePackBytes);
        layeredPack.SetFileByName("unrelated.bin", [0x99, 0x88, 0x77]);
        var layeredPackBytes = layeredPack.Write();

        var installed = SwShRoyalCandyAcquisitionPatcher.ApplyRaidRewards(
            layeredPackBytes,
            basePackBytes);
        var restored = SwShRoyalCandyAcquisitionPatcher.RestoreRaidRewards(
            installed.Output,
            basePackBytes);

        Assert.NotEqual(basePackBytes, restored.Output);
        var output = SwShGfPackFile.Parse(restored.Output);
        Assert.Equal([0x99, 0x88, 0x77], output.GetFileByName("unrelated.bin"));
        Assert.Equal(
            [1128u, 50u],
            ReadRewardItems(output, "nest_hole_drop_rewards.bin"));
        Assert.Equal(
            [50u, 1128u],
            ReadRewardItems(output, "nest_hole_bonus_rewards.bin"));
    }

    [Fact]
    public void PlacementRoundTripOnlyVanillaExpCandyXlOccurrences()
    {
        var itemHashBytes = CreateItemHashTable();
        var basePackBytes = CreatePlacementPack(RoyalCandyHash);
        var basePack = SwShGfPackFile.Parse(basePackBytes);
        var unrelated = basePack.GetFileByName("ZoneNameHashTable.tbl");

        var installed = SwShRoyalCandyAcquisitionPatcher.ApplyPlacement(
            basePackBytes,
            basePackBytes,
            itemHashBytes);

        Assert.Equal(3, installed.Before.BaseOccurrenceCount);
        Assert.Equal(3, installed.Before.OriginalOccurrenceCount);
        Assert.Equal(3, installed.ChangedOccurrenceCount);
        Assert.Equal(0, installed.After.OriginalOccurrenceCount);
        Assert.Equal(3, installed.After.ReplacementOccurrenceCount);
        Assert.Empty(installed.After.Conflicts);

        var installedPack = SwShGfPackFile.Parse(installed.Output);
        Assert.Equal(unrelated, installedPack.GetFileByName("ZoneNameHashTable.tbl"));
        AssertPlacementValues(
            installedPack.GetFileByName(SwShPlacementTestFixtures.AreaMember),
            expectedOwnedHash: RareCandyHash,
            expectedOwnedId: 50);

        var repeatedInstall = SwShRoyalCandyAcquisitionPatcher.ApplyPlacement(
            installed.Output,
            basePackBytes,
            itemHashBytes);
        Assert.Equal(0, repeatedInstall.ChangedOccurrenceCount);
        Assert.Equal(installed.Output, repeatedInstall.Output);

        var restored = SwShRoyalCandyAcquisitionPatcher.RestorePlacement(
            installed.Output,
            basePackBytes,
            itemHashBytes);
        Assert.Equal(3, restored.ChangedOccurrenceCount);
        Assert.Equal(3, restored.After.OriginalOccurrenceCount);
        Assert.Equal(0, restored.After.ReplacementOccurrenceCount);
        Assert.Equal(basePackBytes, restored.Output);
        AssertPlacementValues(
            SwShGfPackFile.Parse(restored.Output)
                .GetFileByName(SwShPlacementTestFixtures.AreaMember),
            expectedOwnedHash: RoyalCandyHash,
            expectedOwnedId: 1128);
    }

    [Fact]
    public void PlacementReportsAndPreservesConflictingOwnedSlot()
    {
        var itemHashBytes = CreateItemHashTable();
        var basePackBytes = CreatePlacementPack(RoyalCandyHash);
        var targetPackBytes = CreatePlacementPack(ConflictHash);

        var installed = SwShRoyalCandyAcquisitionPatcher.ApplyPlacement(
            targetPackBytes,
            basePackBytes,
            itemHashBytes);

        Assert.Equal(3, installed.Before.BaseOccurrenceCount);
        Assert.Equal(2, installed.Before.OriginalOccurrenceCount);
        Assert.Equal(1, installed.Before.ConflictOccurrenceCount);
        Assert.Equal(2, installed.ChangedOccurrenceCount);
        var conflict = Assert.Single(installed.After.Conflicts);
        Assert.Equal(ConflictHash, conflict.CurrentValue);

        var output = SwShGfPackFile.Parse(installed.Output);
        var archive = ParsePlacement(output.GetFileByName(SwShPlacementTestFixtures.AreaMember));
        Assert.Equal(ConflictHash, archive.Zones[0].FieldItems[0].ItemHashes[0]);
        Assert.Equal([50u, 50u], archive.Zones[0].FieldItems[0].ItemIds);
        Assert.Equal(
            [RareCandyHash, RareCandyHash],
            archive.Zones[0].HiddenItems[0].Chances.Select(chance => chance.ItemHash));
    }

    [Fact]
    public void PlacementRestorePreservesUnrelatedLayeredMemberChange()
    {
        var itemHashBytes = CreateItemHashTable();
        var basePackBytes = CreatePlacementPack(RoyalCandyHash);
        var layeredPack = SwShGfPackFile.Parse(basePackBytes);
        layeredPack.SetFileByName("ObjectNameHashTable.tbl", [0x99, 0x88, 0x77]);
        var layeredPackBytes = layeredPack.Write();

        var installed = SwShRoyalCandyAcquisitionPatcher.ApplyPlacement(
            layeredPackBytes,
            basePackBytes,
            itemHashBytes);
        var restored = SwShRoyalCandyAcquisitionPatcher.RestorePlacement(
            installed.Output,
            basePackBytes,
            itemHashBytes);

        Assert.NotEqual(basePackBytes, restored.Output);
        var output = SwShGfPackFile.Parse(restored.Output);
        Assert.Equal([0x99, 0x88, 0x77], output.GetFileByName("ObjectNameHashTable.tbl"));
        AssertPlacementValues(
            output.GetFileByName(SwShPlacementTestFixtures.AreaMember),
            expectedOwnedHash: RoyalCandyHash,
            expectedOwnedId: 1128);
    }

    private static byte[] CreateRaidPack()
    {
        var drop = new SwShNestHoleRewardArchive(
        [
            new SwShNestHoleRewardTable(
                0x1111222233334444,
                [
                    new SwShNestHoleReward(10, 1128, [1, 2, 3, 4, 5]),
                    new SwShNestHoleReward(11, 50, [5, 4, 3, 2, 1]),
                ]),
        ]);
        var bonus = new SwShNestHoleRewardArchive(
        [
            new SwShNestHoleRewardTable(
                0xAAAABBBBCCCCDDDD,
                [
                    new SwShNestHoleReward(20, 50, [1, 1, 1, 1, 1]),
                    new SwShNestHoleReward(21, 1128, [2, 2, 2, 2, 2]),
                ]),
        ]);
        return SwShGfPackFile.Create(
        [
            new SwShGfPackNamedFile("nest_hole_drop_rewards.bin", drop.Write()),
            new SwShGfPackNamedFile("nest_hole_bonus_rewards.bin", bonus.Write()),
            new SwShGfPackNamedFile("unrelated.bin", [0x01, 0x23, 0x45, 0x67]),
        ]).Write();
    }

    private static byte[] CreatePlacementPack(ulong ownedFieldItemHash)
    {
        return SwShPlacementTestFixtures.CreatePlacementPack(
            fieldItemHash: ownedFieldItemHash,
            fieldItemRawItems: [1128, 50],
            hiddenItemChances:
            [
                new SwShPlacementHiddenItemChance(
                    ChanceIndex: 0,
                    ItemHash: RoyalCandyHash,
                    ItemId: 1128,
                    Chance: 50,
                    Quantity: 2,
                    ItemHashOffset: 0,
                    ChanceOffset: 0,
                    QuantityOffset: 0),
                new SwShPlacementHiddenItemChance(
                    ChanceIndex: 1,
                    ItemHash: RareCandyHash,
                    ItemId: 50,
                    Chance: 25,
                    Quantity: 1,
                    ItemHashOffset: 0,
                    ChanceOffset: 0,
                    QuantityOffset: 0),
            ]);
    }

    private static byte[] CreateItemHashTable()
    {
        return new SwShItemHashTable(
        [
            new SwShItemHashEntry(50, RareCandyHash),
            new SwShItemHashEntry(1128, RoyalCandyHash),
        ]).Write();
    }

    private static IReadOnlyList<uint> ReadRewardItems(
        SwShGfPackFile pack,
        string memberName)
    {
        return SwShNestHoleRewardArchive.Parse(pack.GetFileByName(memberName))
            .Tables[0]
            .Rewards
            .Select(reward => reward.ItemId)
            .ToArray();
    }

    private static SwShPlacementZoneArchive ParsePlacement(byte[] bytes)
    {
        return SwShPlacementZoneArchive.Parse(
            bytes,
            new Dictionary<ulong, int>
            {
                [RoyalCandyHash] = 1128,
                [RareCandyHash] = 50,
            });
    }

    private static void AssertPlacementValues(
        byte[] memberBytes,
        ulong expectedOwnedHash,
        uint expectedOwnedId)
    {
        var archive = ParsePlacement(memberBytes);
        var fieldItem = archive.Zones[0].FieldItems[0];
        Assert.Equal([expectedOwnedHash], fieldItem.ItemHashes);
        Assert.Equal([expectedOwnedId, 50u], fieldItem.ItemIds);
        Assert.Equal(
            [expectedOwnedHash, RareCandyHash],
            archive.Zones[0].HiddenItems[0].Chances.Select(chance => chance.ItemHash));
    }
}
