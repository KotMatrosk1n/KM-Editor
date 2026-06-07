// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShGfPackFileTests
{
    [Fact]
    public void WriteRoundTripsNamedFiles()
    {
        var pack = SwShGfPackFile.Create(
        [
            new SwShGfPackNamedFile("encount_symbol_k.bin", [1, 2, 3]),
            new SwShGfPackNamedFile("encount_k.bin", [4, 5]),
        ]);

        var parsed = SwShGfPackFile.Parse(pack.Write());

        Assert.True(parsed.ContainsFileName("encount_symbol_k.bin"));
        Assert.Equal([1, 2, 3], parsed.GetFileByName("encount_symbol_k.bin"));
        Assert.Equal([4, 5], parsed.GetFileByName("encount_k.bin"));
    }

    [Fact]
    public void SetFileByNameRewritesTargetAndPreservesOtherFiles()
    {
        var pack = SwShGfPackFile.Parse(SwShGfPackFile.Create(
        [
            new SwShGfPackNamedFile("encount_symbol_k.bin", [1, 2, 3], SwShGfPackCompressionType.Lz4),
            new SwShGfPackNamedFile("encount_k.bin", [4, 5], SwShGfPackCompressionType.Lz4),
        ]).Write());

        pack.SetFileByName("encount_symbol_k.bin", [9, 8, 7, 6]);

        var parsed = SwShGfPackFile.Parse(pack.Write());
        Assert.Equal([9, 8, 7, 6], parsed.GetFileByName("encount_symbol_k.bin"));
        Assert.Equal([4, 5], parsed.GetFileByName("encount_k.bin"));
    }
}
