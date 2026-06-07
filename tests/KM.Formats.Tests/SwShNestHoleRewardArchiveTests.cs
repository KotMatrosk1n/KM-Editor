// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShNestHoleRewardArchiveTests
{
    [Fact]
    public void WriteRoundTripsRewardTables()
    {
        var archive = CreateArchive();

        var parsed = SwShNestHoleRewardArchive.Parse(archive.Write());

        var table = Assert.Single(parsed.Tables);
        Assert.Equal(0xAABBCCDD00112233UL, table.TableId);
        Assert.Collection(
            table.Rewards,
            reward =>
            {
                Assert.Equal(10u, reward.EntryId);
                Assert.Equal(3u, reward.ItemId);
                Assert.Equal([40u, 30u, 20u, 10u, 5u], reward.Values);
            },
            reward =>
            {
                Assert.Equal(11u, reward.EntryId);
                Assert.Equal(2u, reward.ItemId);
                Assert.Equal([5u, 10u, 15u, 20u, 25u], reward.Values);
            });
    }

    [Fact]
    public void WriteEditsUpdatesItemAndStarValues()
    {
        var archive = CreateArchive();

        var output = archive.WriteEdits(
        [
            new SwShNestHoleRewardEdit(0, 1, SwShNestHoleRewardField.ItemId, 4),
            new SwShNestHoleRewardEdit(0, 1, SwShNestHoleRewardField.Star5Value, 80),
        ]);

        var parsed = SwShNestHoleRewardArchive.Parse(output);
        var reward = parsed.Tables[0].Rewards[1];
        Assert.Equal(4u, reward.ItemId);
        Assert.Equal(80u, reward.Values[4]);
    }

    private static SwShNestHoleRewardArchive CreateArchive()
    {
        return new SwShNestHoleRewardArchive(
        [
            new SwShNestHoleRewardTable(
                0xAABBCCDD00112233,
                [
                    new SwShNestHoleReward(10, 3, [40, 30, 20, 10, 5]),
                    new SwShNestHoleReward(11, 2, [5, 10, 15, 20, 25]),
                ]),
        ]);
    }
}
