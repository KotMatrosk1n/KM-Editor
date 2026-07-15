// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Editing;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
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
        var shopId = GetShopId(temp, "Single", inventoryIndex: 1);

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
        Assert.Contains(edit.Sources, source => source.RelativePath == SwShItemsWorkflowService.ItemDataPath);
        Assert.Equal(2, result.Workflow.Shops[0].Inventory[0].ItemId);
        Assert.Equal("Antidote", result.Workflow.Shops[0].Inventory[0].ItemName);
        Assert.Equal(200, result.Workflow.Shops[0].Inventory[0].Price);
        Assert.True(result.Workflow.Shops[0].Inventory[0].IsKnownItem);
        Assert.Equal("Antidote, Antidote", result.Workflow.Shops[0].InventorySummary);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ApplyChangePlanWritesLayeredShopData()
    {
        using var temp = TemporarySwShProject.Create();
        SwShShopsWorkflowServiceTests.WriteShopFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShShopsEditSessionService();
        var shopId = GetShopId(temp, "Single", inventoryIndex: 1);
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
            "appli",
            "shop",
            "bin",
            "shop_data.bin");
        var output = SwShShopDataFile.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal(2, output.SingleShops[0].Inventory.Items[0]);
        Assert.Equal(2, output.SingleShops[0].Inventory.Items[1]);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyChangePlanCanAddAndRemoveInventoryRows()
    {
        using var temp = TemporarySwShProject.Create();
        SwShShopsWorkflowServiceTests.WriteShopFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShShopsEditSessionService();
        var shopId = GetShopId(temp, "Single", inventoryIndex: 1);
        var add = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            shopId,
            slot: 3,
            field: SwShShopsWorkflowService.AddItemField,
            value: "1");
        var remove = service.UpdateInventoryItem(
            temp.Paths,
            add.Session,
            shopId,
            slot: 2,
            field: SwShShopsWorkflowService.RemoveItemField,
            value: "2");

        var shop = remove.Workflow.Shops.Single(record => record.ShopId == shopId);
        Assert.Collection(
            shop.Inventory,
            item => Assert.Equal(1, item.ItemId),
            item => Assert.Equal(1, item.ItemId));
        var validation = service.Validate(temp.Paths, remove.Session);
        var plan = service.CreateChangePlan(temp.Paths, remove.Session);
        var apply = service.ApplyChangePlan(temp.Paths, remove.Session, plan);

        Assert.True(validation.IsValid);
        Assert.True(plan.CanApply);
        Assert.Equal(SwShShopsWorkflowService.ShopDataPath, Assert.Single(apply.WrittenFiles).RelativePath);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "appli",
            "shop",
            "bin",
            "shop_data.bin");
        var output = SwShShopDataFile.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal([1, 1], output.SingleShops[0].Inventory.Items);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void RepeatedStructuralUpdatesCanonicalizeToOneFinalInventory()
    {
        using var temp = TemporarySwShProject.Create();
        SwShShopsWorkflowServiceTests.WriteShopFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShShopsEditSessionService();
        var shopId = GetShopId(temp, "Single", inventoryIndex: 1);
        var firstRemove = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            shopId,
            slot: 1,
            field: SwShShopsWorkflowService.RemoveItemField,
            value: string.Empty);
        var secondRemove = service.UpdateInventoryItem(
            temp.Paths,
            firstRemove.Session,
            shopId,
            slot: 1,
            field: SwShShopsWorkflowService.RemoveItemField,
            value: string.Empty);

        var edit = Assert.Single(secondRemove.Session.PendingEdits);
        Assert.Equal(SwShShopsWorkflowService.SetInventoryField, edit.Field);
        Assert.Equal(string.Empty, edit.NewValue);
        Assert.Empty(secondRemove.Workflow.Shops.Single(shop => shop.ShopId == shopId).Inventory);

        var plan = service.CreateChangePlan(temp.Paths, secondRemove.Session);
        var apply = service.ApplyChangePlan(temp.Paths, secondRemove.Session, plan);

        Assert.True(plan.CanApply);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "appli",
            "shop",
            "bin",
            "shop_data.bin");
        Assert.Empty(SwShShopDataFile.Parse(File.ReadAllBytes(outputPath)).SingleShops[0].Inventory.Items);
    }

    [Fact]
    public void UpdateRejectsZeroAsANewPhysicalInventoryEntry()
    {
        using var temp = TemporarySwShProject.Create();
        SwShShopsWorkflowServiceTests.WriteShopFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShShopsEditSessionService();
        var shopId = GetShopId(temp, "Single", inventoryIndex: 1);

        var replace = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            shopId,
            slot: 1,
            field: SwShShopsWorkflowService.ItemIdField,
            value: "0");
        var add = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            shopId,
            slot: 1,
            field: SwShShopsWorkflowService.AddItemField,
            value: "0");

        Assert.Empty(replace.Session.PendingEdits);
        Assert.Empty(add.Session.PendingEdits);
        Assert.All(
            replace.Diagnostics.Concat(add.Diagnostics),
            diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
    }

    [Fact]
    public void ApplyChangePlanCanInsertInventoryRowInMiddle()
    {
        using var temp = TemporarySwShProject.Create();
        SwShShopsWorkflowServiceTests.WriteShopFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShShopsEditSessionService();
        var shopId = GetShopId(temp, "Single", inventoryIndex: 1);
        var update = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            shopId,
            slot: 2,
            field: SwShShopsWorkflowService.AddItemField,
            value: "2");

        var shop = update.Workflow.Shops.Single(record => record.ShopId == shopId);
        Assert.Collection(
            shop.Inventory,
            item => Assert.Equal(1, item.ItemId),
            item => Assert.Equal(2, item.ItemId),
            item => Assert.Equal(2, item.ItemId));
        Assert.Equal([1, 2, 3], shop.Inventory.Select(item => item.Slot).ToArray());

        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.True(plan.CanApply);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "appli",
            "shop",
            "bin",
            "shop_data.bin");
        var output = SwShShopDataFile.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal([1, 2, 2], output.SingleShops[0].Inventory.Items);
    }

    [Fact]
    public void ApplyChangePlanWritesInventoryInEditorOrder()
    {
        using var temp = TemporarySwShProject.Create();
        SwShShopsWorkflowServiceTests.WriteShopFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShShopsEditSessionService();
        var shopId = GetShopId(temp, "Single", inventoryIndex: 1);
        var update = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            shopId,
            slot: 1,
            field: SwShShopsWorkflowService.SetInventoryField,
            value: "2,1,2");

        var shop = update.Workflow.Shops.Single(record => record.ShopId == shopId);
        Assert.Equal([2, 1, 2], shop.Inventory.Select(item => item.ItemId).ToArray());
        Assert.Equal([1, 2, 3], shop.Inventory.Select(item => item.Slot).ToArray());

        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.True(plan.CanApply);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "appli",
            "shop",
            "bin",
            "shop_data.bin");
        var output = SwShShopDataFile.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal([2, 1, 2], output.SingleShops[0].Inventory.Items);
    }

    [Fact]
    public void UpdateRejectsNonCanonicalSetInventoryText()
    {
        using var temp = TemporarySwShProject.Create();
        SwShShopsWorkflowServiceTests.WriteShopFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShShopsEditSessionService();
        var shopId = GetShopId(temp, "Single", inventoryIndex: 1);
        var update = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            shopId,
            slot: 1,
            field: SwShShopsWorkflowService.SetInventoryField,
            value: "1,0,2");

        Assert.Empty(update.Session.PendingEdits);
        Assert.Contains(
            update.Diagnostics,
            diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error
                && diagnostic.Field == SwShShopsWorkflowService.ItemIdField);
    }

    [Fact]
    public void ApplyChangePlanCanAddAndRemoveBadgeShopInventoryRows()
    {
        using var temp = TemporarySwShProject.Create();
        SwShShopsWorkflowServiceTests.WriteShopFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShShopsEditSessionService();
        var shopId = GetShopId(temp, "Multi", inventoryIndex: 2);
        var add = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            shopId,
            slot: 2,
            field: SwShShopsWorkflowService.AddItemField,
            value: "1");
        var remove = service.UpdateInventoryItem(
            temp.Paths,
            add.Session,
            shopId,
            slot: 1,
            field: SwShShopsWorkflowService.RemoveItemField,
            value: "2");

        var shop = remove.Workflow.Shops.Single(record => record.ShopId == shopId);
        Assert.Collection(shop.Inventory, item => Assert.Equal(1, item.ItemId));

        var validation = service.Validate(temp.Paths, remove.Session);
        var plan = service.CreateChangePlan(temp.Paths, remove.Session);
        var apply = service.ApplyChangePlan(temp.Paths, remove.Session, plan);

        Assert.True(validation.IsValid);
        Assert.True(plan.CanApply);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error);
        var outputPath = Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "appli",
            "shop",
            "bin",
            "shop_data.bin");
        var output = SwShShopDataFile.Parse(File.ReadAllBytes(outputPath));
        Assert.Equal([1], output.MultiShops[0].Inventories[1].Items);
    }

    [Fact]
    public void UpdateAcceptsUniqueLegacyIdButStagesSignedPhysicalId()
    {
        using var temp = TemporarySwShProject.Create();
        SwShShopsWorkflowServiceTests.WriteShopFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShShopsEditSessionService();
        var legacyShopId = $"single:{0x1F3FF031A3A24490:X16}";

        var result = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            legacyShopId,
            slot: 1,
            field: SwShShopsWorkflowService.ItemIdField,
            value: "2");

        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.StartsWith("single:0:1F3FF031A3A24490:", edit.RecordId, StringComparison.Ordinal);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void DuplicateHashesRejectLegacyIdentityAndSignedIdentityEditsExactPhysicalShop()
    {
        const ulong hash = 0x1F3FF031A3A24490;
        using var temp = TemporarySwShProject.Create();
        WriteSingleShopFixture(temp, (hash, [1]), (hash, [2]));
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShShopsEditSessionService();

        var legacy = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            $"single:{hash:X16}",
            slot: 1,
            field: SwShShopsWorkflowService.ItemIdField,
            value: "2");
        Assert.Empty(legacy.Session.PendingEdits);
        Assert.Contains(legacy.Diagnostics, diagnostic => diagnostic.Message.Contains("ambiguous", StringComparison.OrdinalIgnoreCase));

        var signedId = GetShopId(temp, "Single", inventoryIndex: 1, sourceIndex: 1);
        var update = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            signedId,
            slot: 1,
            field: SwShShopsWorkflowService.ItemIdField,
            value: "1");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);
        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.True(plan.CanApply);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var output = SwShShopDataFile.Parse(File.ReadAllBytes(GetOutputShopPath(temp)));
        Assert.Equal([1], output.SingleShops[0].Inventory.Items);
        Assert.Equal([1], output.SingleShops[1].Inventory.Items);
    }

    [Fact]
    public void ValidateRejectsShopSourceContentAndLayerDrift()
    {
        using var temp = TemporarySwShProject.Create();
        SwShShopsWorkflowServiceTests.WriteShopFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShShopsEditSessionService();
        var shopId = GetShopId(temp, "Single", inventoryIndex: 1);
        var update = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            shopId,
            slot: 1,
            field: SwShShopsWorkflowService.ItemIdField,
            value: "2");

        temp.WriteBaseRomFsFile(
            SwShShopsWorkflowService.ShopDataPath["romfs/".Length..],
            SwShShopsWorkflowServiceTests.CreateShopData([1, 1], [[1], [2]]));
        var contentDrift = service.Validate(temp.Paths, update.Session);
        Assert.False(contentDrift.IsValid);
        Assert.Contains(contentDrift.Diagnostics, diagnostic => diagnostic.Message.Contains("source record changed", StringComparison.OrdinalIgnoreCase));

        SwShShopsWorkflowServiceTests.WriteShopFixture(temp);
        var freshShopId = GetShopId(temp, "Single", inventoryIndex: 1);
        var freshUpdate = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            freshShopId,
            slot: 1,
            field: SwShShopsWorkflowService.ItemIdField,
            value: "2");
        temp.WriteOutputFile(
            SwShShopsWorkflowService.ShopDataPath,
            SwShShopsWorkflowServiceTests.CreateShopData([1, 2], [[1], [2]]));

        var layerDrift = service.Validate(temp.Paths, freshUpdate.Session);
        Assert.False(layerDrift.IsValid);
        Assert.Contains(layerDrift.Diagnostics, diagnostic => diagnostic.Message.Contains("source layer changed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ItemSemanticsAllowLegacyReorderingButRejectNewUnknownIds()
    {
        const ulong unknownShopHash = 0x0102030405060708;
        using var temp = TemporarySwShProject.Create();
        WriteSingleShopFixture(temp, (unknownShopHash, [42, 1]));
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShShopsEditSessionService();
        var shopId = GetShopId(temp, "Single", inventoryIndex: 1);

        var reorder = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            shopId,
            slot: 1,
            field: SwShShopsWorkflowService.SetInventoryField,
            value: "1,42");
        Assert.Single(reorder.Session.PendingEdits);
        Assert.DoesNotContain(reorder.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Null(Assert.Single(reorder.Workflow.Shops).GlobalPriceField);

        var introduce = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            shopId,
            slot: 1,
            field: SwShShopsWorkflowService.SetInventoryField,
            value: "42,42");
        Assert.Empty(introduce.Session.PendingEdits);
        Assert.Contains(introduce.Diagnostics, diagnostic => diagnostic.Message.Contains("cannot be newly introduced", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownShopCanReorderKnownItemsAndWattPreviewUsesWattPrice()
    {
        const ulong unknownHash = 0x0102030405060708;
        const ulong wattHash = 0xD1BEAA2EAAE52D0D;
        using var temp = TemporarySwShProject.Create();
        WriteSingleShopFixture(temp, (unknownHash, [1, 2]), (wattHash, [1]));
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShShopsEditSessionService();

        var unknownId = GetShopId(temp, "Single", inventoryIndex: 1, sourceIndex: 0);
        var reorder = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            unknownId,
            slot: 1,
            field: SwShShopsWorkflowService.SetInventoryField,
            value: "2,1");
        Assert.Equal([2, 1], reorder.Workflow.Shops.Single(shop => shop.SourceIndex == 0).Inventory.Select(item => item.ItemId).ToArray());
        Assert.Equal(200, reorder.Workflow.Shops.Single(shop => shop.SourceIndex == 0).Inventory[0].Price);

        var wattId = GetShopId(temp, "Single", inventoryIndex: 1, sourceIndex: 1);
        var wattUpdate = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            wattId,
            slot: 1,
            field: SwShShopsWorkflowService.ItemIdField,
            value: "2");
        Assert.Equal(10, wattUpdate.Workflow.Shops.Single(shop => shop.SourceIndex == 1).Inventory[0].Price);
    }

    [Fact]
    public void NoOpAndRevertRemoveOnlyShopDomainEditsAndRecomputeStats()
    {
        using var temp = TemporarySwShProject.Create();
        SwShShopsWorkflowServiceTests.WriteShopFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShShopsEditSessionService();
        var shopId = GetShopId(temp, "Single", inventoryIndex: 1);
        var foreignEdit = new PendingEdit(
            "workflow.items",
            "Foreign edit.",
            [],
            RecordId: "item:1",
            Field: "buyPrice",
            NewValue: "999");
        var foreignSession = EditSession.Start().WithPendingEdit(foreignEdit);

        var noOp = service.UpdateInventoryItem(
            temp.Paths,
            foreignSession,
            shopId,
            slot: 1,
            field: SwShShopsWorkflowService.ItemIdField,
            value: "1");
        Assert.Equal([foreignEdit], noOp.Session.PendingEdits);

        var changed = service.UpdateInventoryItem(
            temp.Paths,
            noOp.Session,
            shopId,
            slot: 1,
            field: SwShShopsWorkflowService.SetInventoryField,
            value: "1");
        Assert.Equal(3, changed.Workflow.Stats.TotalInventoryItemCount);
        var reverted = service.UpdateInventoryItem(
            temp.Paths,
            changed.Session,
            shopId,
            slot: 1,
            field: SwShShopsWorkflowService.SetInventoryField,
            value: "1,2");
        Assert.Equal([foreignEdit], reverted.Session.PendingEdits);
        Assert.Equal(4, reverted.Workflow.Stats.TotalInventoryItemCount);
    }

    [Fact]
    public void ApplyRejectsReviewedPlanWhenCanonicalShopEditsDrift()
    {
        using var temp = TemporarySwShProject.Create();
        SwShShopsWorkflowServiceTests.WriteShopFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShShopsEditSessionService();
        var shopId = GetShopId(temp, "Single", inventoryIndex: 1);
        var update = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            shopId,
            slot: 1,
            field: SwShShopsWorkflowService.ItemIdField,
            value: "2");
        var reviewedPlan = service.CreateChangePlan(temp.Paths, update.Session);
        var changedEdit = Assert.Single(update.Session.PendingEdits) with { NewValue = "1" };
        var changedSession = update.Session with { PendingEdits = [changedEdit] };

        var apply = service.ApplyChangePlan(temp.Paths, changedSession, reviewedPlan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Message.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(GetOutputShopPath(temp)));
    }

    [Fact]
    public void ApplyUsesVerifiedAtomicTempWriteAndRollsBackFailure()
    {
        using var temp = TemporarySwShProject.Create();
        SwShShopsWorkflowServiceTests.WriteShopFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShShopsEditSessionService(
            (_, _) => throw new IOException("Injected temporary write failure."));
        var shopId = GetShopId(temp, "Single", inventoryIndex: 1);
        var update = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            shopId,
            slot: 1,
            field: SwShShopsWorkflowService.ItemIdField,
            value: "2");
        var plan = service.CreateChangePlan(temp.Paths, update.Session);

        var apply = service.ApplyChangePlan(temp.Paths, update.Session, plan);

        Assert.Empty(apply.WrittenFiles);
        Assert.Contains(apply.Diagnostics, diagnostic => diagnostic.Message.Contains("rolled back", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(GetOutputShopPath(temp)));
    }

    [Fact]
    public void UpdateDiagnosesNullAndNonCanonicalInputsWithoutMutatingSession()
    {
        using var temp = TemporarySwShProject.Create();
        SwShShopsWorkflowServiceTests.WriteShopFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var service = new SwShShopsEditSessionService();
        var shopId = GetShopId(temp, "Single", inventoryIndex: 1);

        var nullResult = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            shopId: null!,
            slot: 1,
            field: SwShShopsWorkflowService.ItemIdField,
            value: "2");
        var leadingZero = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            shopId,
            slot: 1,
            field: SwShShopsWorkflowService.ItemIdField,
            value: "02");
        var spacedCsv = service.UpdateInventoryItem(
            temp.Paths,
            session: null,
            shopId,
            slot: 1,
            field: SwShShopsWorkflowService.SetInventoryField,
            value: "1, 2");

        Assert.Empty(nullResult.Session.PendingEdits);
        Assert.Empty(leadingZero.Session.PendingEdits);
        Assert.Empty(spacedCsv.Session.PendingEdits);
        Assert.All([nullResult, leadingZero, spacedCsv], result =>
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
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

    private static string GetShopId(
        TemporarySwShProject temp,
        string kind,
        int inventoryIndex,
        int sourceIndex = 0)
    {
        var project = new ProjectWorkspaceService().Open(temp.Paths);
        var workflow = new SwShShopsWorkflowService().Load(project);
        return workflow.Shops.Single(shop => shop.Kind == kind
            && shop.InventoryIndex == inventoryIndex
            && shop.SourceIndex == sourceIndex).ShopId;
    }

    private static void WriteSingleShopFixture(
        TemporarySwShProject temp,
        params (ulong Hash, int[] Items)[] shops)
    {
        SwShItemsWorkflowServiceTests.WriteBaseItems(temp);
        temp.WriteBaseRomFsFile(
            SwShShopsWorkflowService.ShopDataPath["romfs/".Length..],
            new SwShShopDataFile(
                shops.Select(shop => new SwShSingleShopRecord(
                    shop.Hash,
                    new SwShShopInventory(shop.Items))).ToArray(),
                [])
            .Write());
    }

    private static string GetOutputShopPath(TemporarySwShProject temp)
    {
        return Path.Combine(
            temp.OutputRootPath,
            "romfs",
            "bin",
            "appli",
            "shop",
            "bin",
            "shop_data.bin");
    }
}
