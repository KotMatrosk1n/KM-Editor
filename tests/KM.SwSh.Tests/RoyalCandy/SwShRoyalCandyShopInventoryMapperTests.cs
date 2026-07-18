// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.SwSh;
using KM.SwSh.RoyalCandy;
using Xunit;

namespace KM.SwSh.Tests.RoyalCandy;

public sealed class SwShRoyalCandyShopInventoryMapperTests
{
    private const ulong ShopHash = 0x1122334455667788;

    [Fact]
    public void AnalyzeClassifiesOriginalOwnedReplacementAndLegacyMissing()
    {
        var original = SwShRoyalCandyShopInventoryMapper.Analyze(
            [1, 1128, 2],
            [1, 1128, 2]);
        var replacement = SwShRoyalCandyShopInventoryMapper.Analyze(
            [1, 1128, 2],
            [1, 50, 2]);
        var legacyMissing = SwShRoyalCandyShopInventoryMapper.Analyze(
            [1, 1128, 2],
            [1, 2]);

        Assert.Equal([1], original.OriginalTargetSlots);
        Assert.Empty(original.OwnedReplacementTargetSlots);
        Assert.Empty(original.LegacyMissingOccurrences);

        Assert.Empty(replacement.OriginalTargetSlots);
        Assert.Equal([1], replacement.OwnedReplacementTargetSlots);
        Assert.Empty(replacement.LegacyMissingOccurrences);

        Assert.Empty(legacyMissing.OriginalTargetSlots);
        Assert.Empty(legacyMissing.OwnedReplacementTargetSlots);
        var missing = Assert.Single(legacyMissing.LegacyMissingOccurrences);
        Assert.Equal(1, missing.BaseSlot);
        Assert.Equal(1, missing.TargetSlot);
    }

    [Fact]
    public void InstallAndUninstallChangeOnlyOwnedSlotBetweenNaturalRareCandyAnchors()
    {
        var baseData = CreateShopData([50, 1128, 50]);
        var originalMapping = SwShRoyalCandyShopPatchMapper.Analyze(baseData, baseData);

        Assert.Equal(1, originalMapping.BaseOccurrences);
        Assert.Equal(1, originalMapping.OriginalOccurrences);
        Assert.Equal(0, originalMapping.OwnedReplacementOccurrences);
        Assert.Equal(0, originalMapping.LegacyMissingOccurrences);
        var installed = Apply(baseData, originalMapping.InstallEdits);
        Assert.Equal([50, 50, 50], GetItems(installed));

        var installedMapping = SwShRoyalCandyShopPatchMapper.Analyze(installed, baseData);

        Assert.Equal(0, installedMapping.OriginalOccurrences);
        Assert.Equal(1, installedMapping.OwnedReplacementOccurrences);
        Assert.Equal(0, installedMapping.LegacyMissingOccurrences);
        Assert.Empty(installedMapping.InstallEdits);
        var restored = Apply(installed, installedMapping.UninstallEdits);
        Assert.Equal([50, 1128, 50], GetItems(restored));
    }

    [Fact]
    public void LegacyMissingProducesRareCandyInstallAndExpCandyUninstallEdits()
    {
        var baseData = CreateShopData([1, 1128, 2]);
        var layeredData = CreateShopData([99, 1, 2, 100]);

        var mapping = SwShRoyalCandyShopPatchMapper.Analyze(layeredData, baseData);

        Assert.Equal(1, mapping.LegacyMissingOccurrences);
        Assert.Equal([99, 1, 50, 2, 100], GetItems(Apply(layeredData, mapping.InstallEdits)));
        Assert.Equal([99, 1, 1128, 2, 100], GetItems(Apply(layeredData, mapping.UninstallEdits)));
    }

    [Fact]
    public void MappingPreservesUnrelatedShiftedInventoryValues()
    {
        var baseData = CreateShopData([1, 1128, 2]);
        var layeredData = CreateShopData([99, 1, 50, 2, 100]);

        var mapping = SwShRoyalCandyShopPatchMapper.Analyze(layeredData, baseData);

        Assert.Equal(1, mapping.OwnedReplacementOccurrences);
        Assert.Empty(mapping.InstallEdits);
        Assert.Equal(
            [99, 1, 1128, 2, 100],
            GetItems(Apply(layeredData, mapping.UninstallEdits)));
    }

    [Fact]
    public void MappingRejectsIndistinguishableInsertedRareCandy()
    {
        var exception = Assert.Throws<SwShRoyalCandyShopMappingException>(() =>
            SwShRoyalCandyShopInventoryMapper.Analyze(
                [50, 1128],
                [50, 50, 50]));

        Assert.Contains("cannot be classified uniquely", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyRemovalKeepsNaturalRareCandyAnchors()
    {
        var baseData = CreateShopData([50, 1128, 50]);
        var layeredData = CreateShopData([50, 50]);

        var mapping = SwShRoyalCandyShopPatchMapper.Analyze(layeredData, baseData);

        Assert.Equal(1, mapping.LegacyMissingOccurrences);
        Assert.Equal([50, 50, 50], GetItems(Apply(layeredData, mapping.InstallEdits)));
        Assert.Equal([50, 1128, 50], GetItems(Apply(layeredData, mapping.UninstallEdits)));
    }

    private static SwShShopDataFile CreateShopData(IReadOnlyList<int> items)
    {
        return new SwShShopDataFile(
            [new SwShSingleShopRecord(ShopHash, new SwShShopInventory(items))],
            []);
    }

    private static SwShShopDataFile Apply(
        SwShShopDataFile source,
        IReadOnlyList<SwShShopInventoryEdit> edits)
    {
        return edits.Count == 0
            ? source
            : SwShShopDataFile.Parse(source.WriteEdits(edits));
    }

    private static IReadOnlyList<int> GetItems(SwShShopDataFile data)
    {
        return Assert.Single(data.SingleShops).Inventory.Items;
    }
}
