// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Files;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using Xunit;

namespace KM.SwSh.Tests.Items;

public sealed class SwShItemsEditSessionServiceTests
{
    [Fact]
    public void UpdateFieldAddsPendingEditAndPreviewsWorkflowValue()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");

        Assert.Empty(result.Diagnostics);
        Assert.True(result.Session.HasPendingChanges);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal("workflow.items", edit.Domain);
        Assert.Equal(SwShItemsEditSessionService.BuyPriceField, edit.Field);
        Assert.Equal("1", edit.RecordId);
        Assert.Equal("450", edit.NewValue);
        Assert.Equal(ProjectFileLayer.Base, Assert.Single(edit.Sources).Layer);
        var item = result.Workflow.Items[1];
        Assert.Equal(450, item.BuyPrice);
        Assert.Equal(225, item.SellPrice);
    }

    [Fact]
    public void UpdateFieldAddsPendingSellPriceAndPreviewsDerivedBuyPrice()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.SellPriceField,
            value: "175");

        Assert.Empty(result.Diagnostics);
        Assert.True(result.Session.HasPendingChanges);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal(SwShItemsEditSessionService.SellPriceField, edit.Field);
        Assert.Equal("175", edit.NewValue);
        var item = result.Workflow.Items[1];
        Assert.Equal(350, item.BuyPrice);
        Assert.Equal(175, item.SellPrice);
    }

    [Fact]
    public void UpdateFieldReplacesExistingPendingEditForSameStoredItemField()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var firstResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");

        var secondResult = service.UpdateField(
            temp.Paths,
            firstResult.Session,
            itemId: 1,
            field: SwShItemsWorkflowService.SellPriceField,
            value: "300");

        var edit = Assert.Single(secondResult.Session.PendingEdits);
        Assert.Equal(SwShItemsEditSessionService.SellPriceField, edit.Field);
        Assert.Equal("300", edit.NewValue);
        var item = secondResult.Workflow.Items[1];
        Assert.Equal(600, item.BuyPrice);
        Assert.Equal(300, item.SellPrice);
    }

    [Fact]
    public void UpdateFieldKeepsSeparatePendingEditsForDifferentStoredItemFields()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var buyResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");

        var wattsResult = service.UpdateField(
            temp.Paths,
            buyResult.Session,
            itemId: 1,
            field: SwShItemsWorkflowService.WattsPriceField,
            value: "40");

        Assert.Equal(2, wattsResult.Session.PendingEdits.Count);
        Assert.Contains(
            wattsResult.Session.PendingEdits,
            edit => edit.Field == SwShItemsEditSessionService.BuyPriceField);
        Assert.Contains(
            wattsResult.Session.PendingEdits,
            edit => edit.Field == SwShItemsEditSessionService.WattsPriceField);
        var item = wattsResult.Workflow.Items[1];
        Assert.Equal(450, item.BuyPrice);
        Assert.Equal(225, item.SellPrice);
        Assert.Equal(40, item.WattsPrice);
    }

    [Fact]
    public void UpdateFieldAddsPendingMetadataEditAndPreviewsInspectorDetails()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.PouchField,
            value: "4");

        Assert.Empty(result.Diagnostics);
        Assert.True(result.Session.HasPendingChanges);
        var edit = Assert.Single(result.Session.PendingEdits);
        Assert.Equal(SwShItemsEditSessionService.PouchField, edit.Field);
        Assert.Equal("4", edit.NewValue);
        var item = result.Workflow.Items[1];
        Assert.Equal("Items", item.Category);
        Assert.Equal(4, item.Metadata.Pouch);
        Assert.Contains(
            item.DetailGroups.Single(group => group.Label == "Inventory").Details,
            detail => detail.Label == "Pouch" && detail.Value == "Items (4)");
    }

    [Fact]
    public void UpdateFieldRejectsUnsupportedItemField()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();

        var result = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: "category",
            value: "250");

        Assert.False(result.Session.HasPendingChanges);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ValidateAcceptsPendingBuyPriceForLoadedItem()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var editResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");

        var validation = service.Validate(temp.Paths, editResult.Session);

        Assert.True(validation.IsValid);
        Assert.DoesNotContain(validation.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(validation.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Info);
    }

    [Fact]
    public void CreateChangePlanListsRealItemsTargetFileForPendingBuyPrice()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var editResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");

        var changePlan = service.CreateChangePlan(temp.Paths, editResult.Session);

        Assert.True(changePlan.CanApply);
        var write = Assert.Single(changePlan.Writes);
        Assert.Equal(SwShItemsWorkflowService.ItemDataPath, write.TargetRelativePath);
        Assert.False(write.ReplacesExistingOutput);
        Assert.Contains("Potion", write.Reason);
        Assert.Equal(ProjectFileLayer.Base, Assert.Single(write.Sources).Layer);
        Assert.Contains(changePlan.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Info);
    }

    [Fact]
    public void CreateChangePlanMarksExistingOutputFileReplacement()
    {
        using var temp = CreateEditableProject();
        temp.WriteOutputFile(
            SwShItemsWorkflowService.ItemDataPath,
            SwShItemTestFixtures.CreateItemTable(
                new ItemFixtureRecord(0, 0, 0, 0, 0, SwShItemPouch.Items),
                new ItemFixtureRecord(1, 1, 500, 25, 7, SwShItemPouch.Medicine),
                new ItemFixtureRecord(2, 2, 200, 10, 5, SwShItemPouch.Medicine)));
        var service = new SwShItemsEditSessionService();
        var editResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");

        var write = Assert.Single(service.CreateChangePlan(temp.Paths, editResult.Session).Writes);

        Assert.True(write.ReplacesExistingOutput);
    }

    [Fact]
    public void ApplyChangePlanWritesItemDataToOutputRoot()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var editResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");
        var changePlan = service.CreateChangePlan(temp.Paths, editResult.Session);

        var applyResult = service.ApplyChangePlan(temp.Paths, editResult.Session, changePlan);

        var writtenFile = Assert.Single(applyResult.WrittenFiles);
        Assert.Equal(ProjectFileLayer.Generated, writtenFile.Layer);
        Assert.Equal(SwShItemsWorkflowService.ItemDataPath, writtenFile.RelativePath);
        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat");
        Assert.True(File.Exists(outputPath));
        var item = SwShItemTable.Parse(File.ReadAllBytes(outputPath)).Records[1];
        Assert.Equal(450u, item.BuyPrice);
        Assert.Equal(15u, item.WattsPrice);
        Assert.DoesNotContain(applyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyChangePlanWritesDerivedSellPriceToStoredBuyPrice()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var editResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.SellPriceField,
            value: "175");
        var changePlan = service.CreateChangePlan(temp.Paths, editResult.Session);

        var applyResult = service.ApplyChangePlan(temp.Paths, editResult.Session, changePlan);

        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat");
        var item = SwShItemTable.Parse(File.ReadAllBytes(outputPath)).Records[1];
        Assert.Equal(350u, item.BuyPrice);
        Assert.DoesNotContain(applyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyChangePlanWritesCombinedItemPriceEditsToOutputRoot()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var buyResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");
        var wattsResult = service.UpdateField(
            temp.Paths,
            buyResult.Session,
            itemId: 1,
            field: SwShItemsWorkflowService.WattsPriceField,
            value: "40");
        var changePlan = service.CreateChangePlan(temp.Paths, wattsResult.Session);

        var applyResult = service.ApplyChangePlan(temp.Paths, wattsResult.Session, changePlan);

        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat");
        var item = SwShItemTable.Parse(File.ReadAllBytes(outputPath)).Records[1];
        Assert.Single(changePlan.Writes);
        Assert.Equal(450u, item.BuyPrice);
        Assert.Equal(40u, item.WattsPrice);
        Assert.DoesNotContain(applyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyChangePlanWritesItemMetadataToOutputRoot()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var pouchResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.PouchField,
            value: "4");
        var healResult = service.UpdateField(
            temp.Paths,
            pouchResult.Session,
            itemId: 1,
            field: SwShItemsWorkflowService.HealAmountField,
            value: "254");
        var evResult = service.UpdateField(
            temp.Paths,
            healResult.Session,
            itemId: 1,
            field: SwShItemsWorkflowService.EvAttackField,
            value: "-10");
        var canUseResult = service.UpdateField(
            temp.Paths,
            evResult.Session,
            itemId: 1,
            field: SwShItemsWorkflowService.CanUseOnPokemonField,
            value: "0");
        var changePlan = service.CreateChangePlan(temp.Paths, canUseResult.Session);

        var applyResult = service.ApplyChangePlan(temp.Paths, canUseResult.Session, changePlan);

        var outputPath = Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat");
        var item = SwShItemTable.Parse(File.ReadAllBytes(outputPath)).Records[1];
        Assert.Single(changePlan.Writes);
        Assert.Equal(SwShItemPouch.Items, item.Pouch);
        Assert.Equal(254, item.HealAmount);
        Assert.Equal(-10, item.EvAttack);
        Assert.False(item.CanUseOnPokemon);
        Assert.Equal(300u, item.BuyPrice);
        Assert.DoesNotContain(applyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ApplyChangePlanRejectsStaleReviewedTargets()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();
        var editResult = service.UpdateField(
            temp.Paths,
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");
        var changePlan = service.CreateChangePlan(temp.Paths, editResult.Session);
        var staleWrite = Assert.Single(changePlan.Writes) with { TargetRelativePath = "romfs/bin/pml/item/stale.dat" };
        var stalePlan = new ChangePlan(changePlan.SessionId, [staleWrite], changePlan.Diagnostics);

        var applyResult = service.ApplyChangePlan(temp.Paths, editResult.Session, stalePlan);

        Assert.Empty(applyResult.WrittenFiles);
        Assert.Contains(applyResult.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.False(File.Exists(Path.Combine(temp.OutputRootPath, "romfs", "bin", "pml", "item", "item.dat")));
    }

    [Fact]
    public void UpdateFieldRequiresEditableProjectPaths()
    {
        using var temp = CreateEditableProject();
        var service = new SwShItemsEditSessionService();

        var result = service.UpdateField(
            temp.Paths with { OutputRootPath = null },
            session: null,
            itemId: 1,
            field: SwShItemsWorkflowService.BuyPriceField,
            value: "450");

        Assert.False(result.Session.HasPendingChanges);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static TemporarySwShProject CreateEditableProject()
    {
        var temp = TemporarySwShProject.Create();
        SwShItemsWorkflowServiceTests.WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");

        return temp;
    }
}
