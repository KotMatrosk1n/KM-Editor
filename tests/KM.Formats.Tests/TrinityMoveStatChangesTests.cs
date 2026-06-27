// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Formats.ZA.Generated.GameData;
using Xunit;

namespace KM.Formats.Tests;

public sealed class TrinityMoveStatChangesTests
{
    [Fact]
    public void ScarletVioletReadsStatChangeTripletsInRawOrder()
    {
        var changes = new global::SvMoveStatChanges()
            .__assign(0, new ByteBuffer([9, 1, 10, 0, 0, 0, 0, 0, 0]));

        Assert.Equal(9, changes.Stat1);
        Assert.Equal(1, changes.Stat1Stage);
        Assert.Equal(10, changes.Stat1Chance);
        Assert.Equal(0, changes.Stat2);
        Assert.Equal(0, changes.Stat2Stage);
        Assert.Equal(0, changes.Stat2Chance);
        Assert.Equal(0, changes.Stat3);
        Assert.Equal(0, changes.Stat3Stage);
        Assert.Equal(0, changes.Stat3Chance);
    }

    [Fact]
    public void LegendsZAReadsStatChangeTripletsInRawOrder()
    {
        var changes = new ZaMoveStatChanges()
            .__assign(0, new ByteBuffer([9, 1, 10, 0, 0, 0, 0, 0, 0]));

        Assert.Equal(9, changes.Stat1);
        Assert.Equal(1, changes.Stat1Stage);
        Assert.Equal(10, changes.Stat1Chance);
        Assert.Equal(0, changes.Stat2);
        Assert.Equal(0, changes.Stat2Stage);
        Assert.Equal(0, changes.Stat2Chance);
        Assert.Equal(0, changes.Stat3);
        Assert.Equal(0, changes.Stat3Stage);
        Assert.Equal(0, changes.Stat3Chance);
    }
}
