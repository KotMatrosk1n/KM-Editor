// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Editing;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Shops;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.Shops;

public sealed class SwShShopsEditSessionServiceTests
{
    [Fact]
    public void UpdateInventoryItemCreatesPendingShopEdit()
    {
        using var temp = TemporarySwShProject.Create();
        SwShShopsWorkflowServiceTests.WriteShopFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShShopsEditSessionService();
        var shopId = $"single:{0x1F3FF031A3A24490:X16}";

        var result = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            shopId,
            slot: 1,
            field: SwShShopsWorkflowService.ItemIdField,
            value: "2");

        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.shops", edit.Domain);
        Assert.Equal(SwShShopsWorkflowService.ItemIdField, edit.Field);
        Assert.Equal($"{shopId}#1", edit.RecordId);
        Assert.Equal("2", edit.NewValue);
        Assert.Equal(2, result.Workflow.Shops[0].Inventory[0].ItemId);
        Assert.Equal("Antidote", result.Workflow.Shops[0].Inventory[0].ItemName);
        Assert.Equal(200, result.Workflow.Shops[0].Inventory[0].Price);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ApplyChangePlanWritesLayeredShopData()
    {
        using var temp = TemporarySwShProject.Create();
        SwShShopsWorkflowServiceTests.WriteShopFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShShopsEditSessionService();
        var shopId = $"single:{0x1F3FF031A3A24490:X16}";
        var update = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            shopId,
            slot: 1,
            field: SwShShopsWorkflowService.ItemIdField,
            value: "2");

        var validation = service.Validate(temp.Paths, update.Session);
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.True(validation.IsValid);
        Assert.True(plan.CanApply);
        Assert.Equal(SwShShopsWorkflowService.ShopDataPath, Assert.Single(plan.Writes).TargetRelativePath);
        Assert.Equal(SwShShopsWorkflowService.ShopDataPath, Assert.Single(apply.WrittenFiles).RelativePath);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "app",
            "shop",
            "shop_data.bin");
        var output = SwShShopDataFile.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal(2, output.SingleShops[0].Inventory.Items[0]);
        Assert.Equal(2, output.SingleShops[0].Inventory.Items[1]);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void ValidateRejectsUnsupportedShopField()
    {
        using var temp = TemporarySwShProject.Create();
        SwShShopsWorkflowServiceTests.WriteShopFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShShopsEditSessionService();
        var session = EditSession.Start() with
        {
            PendingEdits =
            [
                new PendingEdit(
                    "workflow.shops",
                    "Set unsupported field.",
                    [],
                    RecordId: $"single:{0x1F3FF031A3A24490:X16}#1",
                    Field: "price",
                    NewValue: "100")
            ],
        };

        var validation = service.Validate(temp.Paths, session);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Field == "field");
    }
}
