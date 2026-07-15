// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Shops;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Shops;

public sealed class SwShShopsWorkflowServiceTests
{
    private const ulong SingleShopHash = 0x1F3FF031A3A24490;
    private const ulong MultiShopHash = 0x66CA73B2966BB871;

    [Fact]
    public void LoadReadsShopsFromRealSwordShieldShopData()
    {
        using var temp = TemporarySwShProject.Create();
        WriteShopFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShShopsWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Equal(3, workflow.Shops.Count);
        Assert.Equal(4, workflow.Stats.TotalInventoryItemCount);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        Assert.Collection(
            workflow.EditableFields,
            editableField =>
            {
                Assert.Equal(SwShShopsWorkflowService.ItemIdField, editableField.Field);
                Assert.Equal(SwShShopsWorkflowService.MaximumItemId, editableField.MaximumValue);
                Assert.Contains(
                    editableField.Options,
                    option => option.Value == 2
                        && option.Label == "0002 Antidote (Medicine)"
                        && option.ItemName == "Antidote"
                        && option.Price == 200
                        && option.Prices[SwShItemsWorkflowService.BuyPriceField] == 200
                        && option.Prices[SwShItemsWorkflowService.WattsPriceField] == 10
                        && option.Prices[SwShItemsWorkflowService.AlternatePriceField] == 5);
            });

        var singleShop = workflow.Shops[0];
        Assert.StartsWith($"single:0:{SingleShopHash:X16}:", singleShop.ShopId, StringComparison.Ordinal);
        Assert.Equal(64, singleShop.ShopId.Split(':')[3].Length);
        Assert.Equal("Single", singleShop.Kind);
        Assert.Equal("Inventory", singleShop.InventoryLabel);
        Assert.Equal(1, singleShop.InventoryIndex);
        Assert.Equal(1, singleShop.InventoryCount);
        Assert.Equal($"0x{SingleShopHash:X16}", singleShop.SourceHash);
        Assert.Equal("Potion, Antidote", singleShop.InventorySummary);
        Assert.Equal("Poke Mart", singleShop.Location);
        Assert.Equal("Money", singleShop.Currency);
        Assert.Equal(SwShItemsWorkflowService.BuyPriceField, singleShop.GlobalPriceField);
        Assert.Equal(0, singleShop.SourceIndex);
        Assert.Equal(ProjectFileLayer.Base, singleShop.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, singleShop.Provenance.FileState);
        Assert.Equal(SwShShopsWorkflowService.ShopDataPath, singleShop.Provenance.SourceFile);

        Assert.Collection(
            singleShop.Inventory,
            item =>
            {
                Assert.Equal(1, item.Slot);
                Assert.Equal(1, item.ItemId);
                Assert.Equal("Potion", item.ItemName);
                Assert.Equal(300, item.Price);
                Assert.True(item.IsKnownItem);
                Assert.Null(item.StockLimit);
            },
            item =>
            {
                Assert.Equal(2, item.Slot);
                Assert.Equal(2, item.ItemId);
                Assert.Equal("Antidote", item.ItemName);
                Assert.Equal(200, item.Price);
                Assert.True(item.IsKnownItem);
            });
        var multiShop = workflow.Shops.Single(shop => shop.Kind == "Multi" && shop.InventoryIndex == 2);
        Assert.StartsWith($"multi:0:{MultiShopHash:X16}:1:", multiShop.ShopId, StringComparison.Ordinal);
        Assert.Equal("Multi", multiShop.Kind);
        Assert.Equal("Inventory 2 of 2", multiShop.InventoryLabel);
        Assert.Equal(2, multiShop.InventoryIndex);
        Assert.Equal(2, multiShop.InventoryCount);
        Assert.Equal("Antidote", multiShop.InventorySummary);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadUsesExplicitCurrencyMetadataAndPhysicalSourceIndexes()
    {
        const ulong moneyHash = 0x00CBD669D8735701;
        const ulong wattsHash = 0xD1BEAA2EAAE52D0D;
        const ulong bpHash = 0x3FD7A44219BF30BB;
        const ulong dyniteHash = 0xF49C86F8683842BF;
        const ulong unknownHash = 0x0102030405060708;
        const ulong wattsMultiHash = 0x5870BD165650F18C;

        using var temp = TemporarySwShProject.Create();
        SwShItemsWorkflowServiceTests.WriteBaseItems(temp);
        temp.WriteBaseRomFsFile(
            SwShShopsWorkflowService.ShopDataPath["romfs/".Length..],
            new SwShShopDataFile(
                [
                    new SwShSingleShopRecord(dyniteHash, new SwShShopInventory([1])),
                    new SwShSingleShopRecord(unknownHash, new SwShShopInventory([1])),
                    new SwShSingleShopRecord(moneyHash, new SwShShopInventory([1])),
                    new SwShSingleShopRecord(wattsHash, new SwShShopInventory([1])),
                    new SwShSingleShopRecord(bpHash, new SwShShopInventory([1])),
                ],
                [new SwShMultiShopRecord(wattsMultiHash, [new SwShShopInventory([1])])])
            .Write());
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShShopsWorkflowService().Load(project);

        AssertShop(moneyHash, "Money", SwShItemsWorkflowService.BuyPriceField, 300, sourceIndex: 2);
        AssertShop(wattsHash, "Watts", SwShItemsWorkflowService.WattsPriceField, 15, sourceIndex: 3);
        AssertShop(bpHash, "BP", SwShItemsWorkflowService.AlternatePriceField, 3, sourceIndex: 4);
        AssertShop(dyniteHash, "Dynite Ore", SwShItemsWorkflowService.AlternatePriceField, 3, sourceIndex: 0);
        AssertShop(unknownHash, "Unknown", expectedPriceField: null, 300, sourceIndex: 1);

        var wattsMulti = workflow.Shops.Single(shop => shop.SourceHash == $"0x{wattsMultiHash:X16}");
        Assert.Equal("Watts", wattsMulti.Currency);
        Assert.Equal(SwShItemsWorkflowService.WattsPriceField, wattsMulti.GlobalPriceField);
        Assert.Equal(15, Assert.Single(wattsMulti.Inventory).Price);

        void AssertShop(
            ulong hash,
            string currency,
            string? expectedPriceField,
            int expectedPrice,
            int sourceIndex)
        {
            var shop = workflow.Shops.Single(candidate => candidate.SourceHash == $"0x{hash:X16}");
            Assert.Equal(currency, shop.Currency);
            Assert.Equal(expectedPriceField, shop.GlobalPriceField);
            Assert.Equal(expectedPrice, Assert.Single(shop.Inventory).Price);
            Assert.Equal(sourceIndex, shop.SourceIndex);
        }
    }

    [Fact]
    public void LoadAssignsUniqueSignedIdsToDuplicatePhysicalHashes()
    {
        using var temp = TemporarySwShProject.Create();
        SwShItemsWorkflowServiceTests.WriteBaseItems(temp);
        temp.WriteBaseRomFsFile(
            SwShShopsWorkflowService.ShopDataPath["romfs/".Length..],
            new SwShShopDataFile(
                [
                    new SwShSingleShopRecord(SingleShopHash, new SwShShopInventory([1])),
                    new SwShSingleShopRecord(SingleShopHash, new SwShShopInventory([2])),
                ],
                [])
            .Write());
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShShopsWorkflowService().Load(project);

        Assert.Equal([0, 1], workflow.Shops.Select(shop => shop.SourceIndex).ToArray());
        Assert.Equal(2, workflow.Shops.Select(shop => shop.ShopId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(workflow.Shops, shop => Assert.Contains($":{SingleShopHash:X16}:", shop.ShopId, StringComparison.Ordinal));
    }

    [Fact]
    public void LoadPrefersLayeredShopDataWhenOutputOverridesBase()
    {
        using var temp = TemporarySwShProject.Create();
        WriteShopFixture(temp);
        temp.WriteOutputFile(
            SwShShopsWorkflowService.ShopDataPath,
            CreateShopData([2], [[1]]));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShShopsWorkflowService().Load(project);

        var shop = workflow.Shops[0];
        Assert.Equal(ProjectFileLayer.Layered, shop.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.LayeredOverride, shop.Provenance.FileState);
        Assert.Equal(2, Assert.Single(shop.Inventory).ItemId);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadUsesFallbackItemMetadataWhenItemsAreMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(SwShShopsWorkflowService.ShopDataPath["romfs/".Length..], CreateShopData([42], []));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShShopsWorkflowService().Load(project);

        var item = Assert.Single(Assert.Single(workflow.Shops).Inventory);
        Assert.Equal("Item 42", item.ItemName);
        Assert.Equal(0, item.Price);
        Assert.False(item.IsKnownItem);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.shops");
    }

    [Fact]
    public void LoadWarnsWhenShopItemIdIsNotResolvedByLoadedItems()
    {
        using var temp = TemporarySwShProject.Create();
        SwShItemsWorkflowServiceTests.WriteBaseItems(temp);
        temp.WriteBaseRomFsFile(SwShShopsWorkflowService.ShopDataPath["romfs/".Length..], CreateShopData([42], []));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShShopsWorkflowService().Load(project);

        var item = Assert.Single(Assert.Single(workflow.Shops).Inventory);
        Assert.False(item.IsKnownItem);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Domain == "workflow.shops"
                && diagnostic.Message.Contains("item ID 42", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenShopDataIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/shops.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShShopsWorkflowService().Load(project);

        Assert.Empty(workflow.Shops);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.shops");
    }

    internal static void WriteShopFixture(TemporarySwShProject temp)
    {
        SwShItemsWorkflowServiceTests.WriteBaseItems(temp);
        temp.WriteBaseRomFsFile(
            SwShShopsWorkflowService.ShopDataPath["romfs/".Length..],
            CreateShopData([1, 2], [[1], [2]]));
    }

    internal static byte[] CreateShopData(int[] singleShopItems, int[][] multiShopInventories)
    {
        return new SwShShopDataFile(
            singleShopItems.Length == 0
                ? Array.Empty<SwShSingleShopRecord>()
                : [new SwShSingleShopRecord(SingleShopHash, new SwShShopInventory(singleShopItems))],
            multiShopInventories.Length == 0
                ? Array.Empty<SwShMultiShopRecord>()
                : [new SwShMultiShopRecord(
                    MultiShopHash,
                    multiShopInventories
                        .Select(items => new SwShShopInventory(items))
                        .ToArray())])
            .Write();
    }
}
