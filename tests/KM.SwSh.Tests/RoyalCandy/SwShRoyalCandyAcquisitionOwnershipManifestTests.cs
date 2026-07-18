// SPDX-License-Identifier: GPL-3.0-only

using KM.SwSh.RoyalCandy;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace KM.SwSh.Tests.RoyalCandy;

public sealed class SwShRoyalCandyAcquisitionOwnershipManifestTests
{
    private const string ModernShopPath = "romfs/bin/appli/shop/bin/shop_data.bin";
    private const string LegacyShopPath = "romfs/bin/app/shop/shop_data.bin";

    [Fact]
    public void CreateWriteParseAndValidateUseCanonicalVersionOneEncoding()
    {
        byte[] shop = [0x01, 0x02];
        byte[] nest = [0x03, 0x04, 0x05];
        byte[] placement = [0x06];
        byte[] itemHash = [0x07, 0x08, 0x09, 0x0A];
        var manifest = SwShRoyalCandyAcquisitionOwnershipManifest.Create(
            ModernShopPath,
            shop,
            nest,
            placement,
            itemHash);

        var bytes = SwShRoyalCandyAcquisitionOwnershipManifest.Write(manifest);
        var expected = string.Concat(
            "{\"version\":1,\"shopRelativePath\":\"",
            ModernShopPath,
            "\",\"baseShopSha256\":\"",
            Hash(shop),
            "\",\"baseNestSha256\":\"",
            Hash(nest),
            "\",\"basePlacementSha256\":\"",
            Hash(placement),
            "\",\"baseItemHashSha256\":\"",
            Hash(itemHash),
            "\"}");
        Assert.Equal(expected, Encoding.UTF8.GetString(bytes));

        var parsed = SwShRoyalCandyAcquisitionOwnershipManifest.Parse(bytes);
        Assert.Equal(manifest, parsed);
        SwShRoyalCandyAcquisitionOwnershipManifest.Validate(
            parsed,
            ModernShopPath,
            shop,
            nest,
            placement,
            itemHash);
        Assert.Equal(
            manifest,
            SwShRoyalCandyAcquisitionOwnershipManifest.ParseAndValidate(
                bytes,
                ModernShopPath,
                shop,
                nest,
                placement,
                itemHash));
    }

    [Theory]
    [InlineData(ModernShopPath)]
    [InlineData(LegacyShopPath)]
    public void CreateSupportsOnlyCanonicalSwordShieldShopPaths(string shopRelativePath)
    {
        var manifest = SwShRoyalCandyAcquisitionOwnershipManifest.Create(
            shopRelativePath,
            [0x01],
            [0x02],
            [0x03],
            [0x04]);

        Assert.Equal(shopRelativePath, manifest.ShopRelativePath);
    }

    [Fact]
    public void CreateRejectsLocalAndNoncanonicalShopPaths()
    {
        foreach (var path in new[]
        {
            @"C:\mods\shop_data.bin",
            "../shop_data.bin",
            "/romfs/bin/appli/shop/bin/shop_data.bin",
            ModernShopPath.ToUpperInvariant(),
            ModernShopPath + "/",
        })
        {
            Assert.Throws<InvalidDataException>(() =>
                SwShRoyalCandyAcquisitionOwnershipManifest.Create(
                    path,
                    [0x01],
                    [0x02],
                    [0x03],
                    [0x04]));
        }
    }

    [Fact]
    public void ParseRejectsEveryNoncanonicalOrUnknownVersionOneShape()
    {
        var hash = new string('A', SHA256.HashSizeInBytes * 2);
        var canonical = string.Concat(
            "{\"version\":1,\"shopRelativePath\":\"",
            ModernShopPath,
            "\",\"baseShopSha256\":\"",
            hash,
            "\",\"baseNestSha256\":\"",
            hash,
            "\",\"basePlacementSha256\":\"",
            hash,
            "\",\"baseItemHashSha256\":\"",
            hash,
            "\"}");
        var invalid = new[]
        {
            " " + canonical,
            canonical + "\n",
            canonical.Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal),
            canonical.Replace(
                "\"version\":1",
                "\"version\":1,\"version\":1",
                StringComparison.Ordinal),
            canonical.Replace(
                "\"version\":1",
                "\"version\":1,\"unknown\":true",
                StringComparison.Ordinal),
            canonical.Replace(
                string.Concat(",\"baseItemHashSha256\":\"", hash, "\""),
                string.Empty,
                StringComparison.Ordinal),
            canonical.Replace(hash, hash.ToLowerInvariant(), StringComparison.Ordinal),
            canonical.Replace(ModernShopPath, @"C:\\mods\\shop_data.bin", StringComparison.Ordinal),
            canonical.Replace(
                string.Concat(
                    "\"version\":1,\"shopRelativePath\":\"",
                    ModernShopPath,
                    "\""),
                string.Concat(
                    "\"shopRelativePath\":\"",
                    ModernShopPath,
                    "\",\"version\":1"),
                StringComparison.Ordinal),
            "[]",
            "not-json",
        };

        foreach (var json in invalid)
        {
            Assert.Throws<InvalidDataException>(() =>
                SwShRoyalCandyAcquisitionOwnershipManifest.Parse(
                    Encoding.UTF8.GetBytes(json)));
        }
    }

    [Fact]
    public void ValidateRejectsEveryChangedAuthoritativeDependency()
    {
        byte[] shop = [0x01];
        byte[] nest = [0x02];
        byte[] placement = [0x03];
        byte[] itemHash = [0x04];
        var manifest = SwShRoyalCandyAcquisitionOwnershipManifest.Create(
            ModernShopPath,
            shop,
            nest,
            placement,
            itemHash);

        Assert.Throws<InvalidDataException>(() =>
            SwShRoyalCandyAcquisitionOwnershipManifest.Validate(
                manifest,
                LegacyShopPath,
                shop,
                nest,
                placement,
                itemHash));
        Assert.Throws<InvalidDataException>(() =>
            SwShRoyalCandyAcquisitionOwnershipManifest.Validate(
                manifest,
                ModernShopPath,
                [0x11],
                nest,
                placement,
                itemHash));
        Assert.Throws<InvalidDataException>(() =>
            SwShRoyalCandyAcquisitionOwnershipManifest.Validate(
                manifest,
                ModernShopPath,
                shop,
                [0x12],
                placement,
                itemHash));
        Assert.Throws<InvalidDataException>(() =>
            SwShRoyalCandyAcquisitionOwnershipManifest.Validate(
                manifest,
                ModernShopPath,
                shop,
                nest,
                [0x13],
                itemHash));
        Assert.Throws<InvalidDataException>(() =>
            SwShRoyalCandyAcquisitionOwnershipManifest.Validate(
                manifest,
                ModernShopPath,
                shop,
                nest,
                placement,
                [0x14]));
    }

    [Fact]
    public void ParseRejectsOversizedManifest()
    {
        Assert.Throws<InvalidDataException>(() =>
            SwShRoyalCandyAcquisitionOwnershipManifest.Parse(new byte[4097]));
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
