// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Items;

public sealed class SwShItemsWorkflowServiceTests
{
    [Fact]
    public void LoadReadsItemsFromRealItemDataAndNames()
    {
        using var temp = TemporarySwShProject.Create();
        WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShItemsWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Equal(3, workflow.Items.Count);
        Assert.Equal(2, workflow.Stats.SourceFileCount);
        var item = workflow.Items[1];
        Assert.Equal("Potion", item.Name);
        Assert.Equal("Medicine", item.Category);
        Assert.Equal(300, item.BuyPrice);
        Assert.Equal(150, item.SellPrice);
        Assert.Equal(15, item.WattsPrice);
        Assert.Equal(3, item.AlternatePrice);
        Assert.Equal(ProjectFileLayer.Base, item.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, item.Provenance.FileState);
        Assert.Equal(SwShItemsWorkflowService.ItemDataPath, item.Provenance.SourceFile);
        Assert.Collection(
            workflow.EditableFields,
            editableField =>
            {
                Assert.Equal(SwShItemsWorkflowService.BuyPriceField, editableField.Field);
                Assert.Equal(SwShItemsWorkflowService.MaximumBuyPrice, editableField.MaximumValue);
            },
            editableField =>
            {
                Assert.Equal(SwShItemsWorkflowService.SellPriceField, editableField.Field);
                Assert.Equal(SwShItemsWorkflowService.MaximumSellPrice, editableField.MaximumValue);
            },
            editableField =>
            {
                Assert.Equal(SwShItemsWorkflowService.WattsPriceField, editableField.Field);
                Assert.Equal(SwShItemsWorkflowService.MaximumWattsPrice, editableField.MaximumValue);
            },
            editableField =>
            {
                Assert.Equal(SwShItemsWorkflowService.AlternatePriceField, editableField.Field);
                Assert.Equal(SwShItemsWorkflowService.MaximumAlternatePrice, editableField.MaximumValue);
            });
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadPrefersLayeredItemDataWhenOutputOverridesBase()
    {
        using var temp = TemporarySwShProject.Create();
        WriteBaseItems(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        temp.WriteOutputFile(
            SwShItemsWorkflowService.ItemDataPath,
            SwShItemTestFixtures.CreateItemTable(
                new ItemFixtureRecord(0, 0, 0, 0, 0, SwShItemPouch.Items),
                new ItemFixtureRecord(1, 1, 500, 25, 7, SwShItemPouch.Medicine)));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShItemsWorkflowService().Load(project);

        var item = workflow.Items[1];
        Assert.Equal("Potion", item.Name);
        Assert.Equal(500, item.BuyPrice);
        Assert.Equal(250, item.SellPrice);
        Assert.Equal(ProjectFileLayer.Layered, item.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.LayeredOverride, item.Provenance.FileState);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadUsesFallbackNamesWhenItemNameTableIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/pml/item/item.dat",
            SwShItemTestFixtures.CreateItemTable(
                new ItemFixtureRecord(0, 0, 0, 0, 0, SwShItemPouch.Items),
                new ItemFixtureRecord(1, 1, 300, 15, 3, SwShItemPouch.Medicine)));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShItemsWorkflowService().Load(project);

        Assert.Equal("Item 1", workflow.Items[1].Name);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.items");
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenItemDataIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/items.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShItemsWorkflowService().Load(project);

        Assert.Empty(workflow.Items);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.items");
    }

    internal static void WriteBaseItems(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/pml/item/item.dat",
            SwShItemTestFixtures.CreateItemTable(
                new ItemFixtureRecord(0, 0, 0, 0, 0, SwShItemPouch.Items),
                new ItemFixtureRecord(1, 1, 300, 15, 3, SwShItemPouch.Medicine),
                new ItemFixtureRecord(2, 2, 200, 10, 5, SwShItemPouch.Medicine)));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames("None", "Potion", "Antidote"));
    }
}
