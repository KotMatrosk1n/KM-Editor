// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Shops;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Shops;

public sealed class SwShShopsWorkflowServiceTests
{
    [Fact]
    public void LoadReadsShopsFromSanitizedBaseReadModel()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/shops.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "shops": [
                {
                  "shopId": "route_1_mart",
                  "name": "Route 1 Mart",
                  "location": "Route 1",
                  "currency": "Money",
                  "inventory": [
                    {
                      "slot": 2,
                      "itemId": 2,
                      "itemName": "Antidote",
                      "price": 200,
                      "stockLimit": 10
                    },
                    {
                      "slot": 1,
                      "itemId": 1,
                      "itemName": "Potion",
                      "price": 300,
                      "stockLimit": null
                    }
                  ]
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShShopsWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        var shop = Assert.Single(workflow.Shops);
        Assert.Equal("route_1_mart", shop.ShopId);
        Assert.Equal("Route 1 Mart", shop.Name);
        Assert.Equal("Route 1", shop.Location);
        Assert.Equal("Money", shop.Currency);
        Assert.Equal(2, shop.Inventory.Count);
        Assert.Equal("Potion", shop.Inventory[0].ItemName);
        Assert.Null(shop.Inventory[0].StockLimit);
        Assert.Equal("Antidote", shop.Inventory[1].ItemName);
        Assert.Equal(10, shop.Inventory[1].StockLimit);
        Assert.Equal(ProjectFileLayer.Base, shop.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, shop.Provenance.FileState);
        Assert.Equal(1, workflow.Stats.TotalShopCount);
        Assert.Equal(2, workflow.Stats.TotalInventoryItemCount);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenReadModelIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/shops.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShShopsWorkflowService().Load(project);

        Assert.Empty(workflow.Shops);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.shops");
    }

    [Fact]
    public void LoadWarnsWhenShopIdsAreDuplicated()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "kmeditor/shops.readmodel.json",
            """
            {
              "schemaVersion": 1,
              "shops": [
                {
                  "shopId": "route_1_mart",
                  "name": "Route 1 Mart",
                  "location": "Route 1",
                  "currency": "Money",
                  "inventory": []
                },
                {
                  "shopId": "route_1_mart",
                  "name": "Route 1 Ingredient Shop",
                  "location": "Route 1",
                  "currency": "Money",
                  "inventory": []
                }
              ]
            }
            """);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShShopsWorkflowService().Load(project);

        Assert.Equal(2, workflow.Shops.Count);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.shops");
    }
}
