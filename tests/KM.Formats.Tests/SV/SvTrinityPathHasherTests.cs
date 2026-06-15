// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SV;
using Xunit;

namespace KM.Formats.Tests.SV;

public sealed class SvTrinityPathHasherTests
{
    [Theory]
    [InlineData("world/data/battle/plib_item_conversion/plib_item_conversion_array.bin", 0xABE8276CA78BEAF8)]
    [InlineData("world/data/event/eventTradePokemon/eventTradePokemon_array.bin", 0xA938D7AAAA1A35C6)]
    [InlineData("avalon/data/tokusei_array.bin", 0xACD40B49C2F91CCD)]
    public void HashPathMatchesKnownScarletVioletFileHashes(string path, ulong expectedHash)
    {
        Assert.Equal(expectedHash, SvTrinityPathHasher.HashPath(path));
    }

    [Fact]
    public void HashPathNormalizesWindowsSeparators()
    {
        Assert.Equal(
            SvTrinityPathHasher.HashPath("avalon/data/tokusei_array.bin"),
            SvTrinityPathHasher.HashPath("avalon\\data\\tokusei_array.bin"));
    }
}
