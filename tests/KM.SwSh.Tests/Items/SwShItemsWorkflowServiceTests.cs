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
        Assert.Contains(
            item.DetailGroups.Single(group => group.Label == "Inventory").Details,
            detail => detail.Label == "Sprite" && detail.Value == "12");
        Assert.Contains(
            item.DetailGroups.Single(group => group.Label == "Field Use").Details,
            detail => detail.Label == "Field use type" && detail.Value == "Medicine (1)");
        Assert.Contains(
            item.DetailGroups.Single(group => group.Label == "Field Use").Details,
            detail => detail.Label == "Use flags 1 (decoded)" && detail.Value == "Restore HP");
        Assert.Contains(
            item.DetailGroups.Single(group => group.Label == "Battle").Details,
            detail => detail.Label == "Fling power" && detail.Value == "30");
        Assert.Contains(
            item.DetailGroups.Single(group => group.Label == "Pokemon Effects").Details,
            detail => detail.Label == "Heal" && detail.Value == "20 HP");
        Assert.Contains(
            item.DetailGroups.Single(group => group.Label == "Pokemon Effects").Details,
            detail => detail.Label == "Friendship gains" && detail.Value == "+1 / +1 / 0");
        Assert.Equal(ProjectFileLayer.Base, item.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, item.Provenance.FileState);
        Assert.Equal(SwShItemsWorkflowService.ItemDataPath, item.Provenance.SourceFile);
        Assert.Equal(0, item.Metadata.Pouch);
        Assert.True(item.Metadata.CanUseOnPokemon);
        Assert.Equal(20, item.Metadata.HealAmount);
        Assert.Contains(
            workflow.EditableFields,
            editableField => editableField.Field == SwShItemsWorkflowService.BuyPriceField
                && editableField.MaximumValue == SwShItemsWorkflowService.MaximumBuyPrice);
        Assert.Contains(
            workflow.EditableFields,
            editableField => editableField.Field == SwShItemsWorkflowService.SellPriceField
                && editableField.MaximumValue == SwShItemsWorkflowService.MaximumSellPrice);
        Assert.Contains(
            workflow.EditableFields,
            editableField => editableField.Field == SwShItemsWorkflowService.PouchField
                && editableField.Options.Any(option => option.Value == 0 && option.Label == "Medicine"));
        Assert.Contains(
            workflow.EditableFields,
            editableField => editableField.Field == SwShItemsWorkflowService.FieldUseTypeField
                && editableField.Options.Any(option => option.Value == 1 && option.Label == "Medicine"));
        Assert.Contains(
            workflow.EditableFields,
            editableField => editableField.Field == SwShItemsWorkflowService.EvHpField
                && editableField.MinimumValue == SwShItemsWorkflowService.MinimumSignedByteValue
                && editableField.MaximumValue == SwShItemsWorkflowService.MaximumSignedByteValue);
        Assert.Contains(
            workflow.EditableFields,
            editableField => editableField.Field == SwShItemsWorkflowService.CureBurnField
                && editableField.ValueKind == "boolean"
                && editableField.Options.Any(option => option.Value == 1 && option.Label == "Yes"));
        Assert.Contains(
            workflow.EditableFields,
            editableField => editableField.Field == SwShItemsWorkflowService.AttackBoostField
                && editableField.MaximumValue == SwShItemsWorkflowService.MaximumBoostValue);
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
    public void LoadReadsMachineMoveLinkageAndMoveOptions()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/pml/item/item.dat",
            SwShItemTestFixtures.CreateItemTableWithMachineMoves(
                new Dictionary<int, int> { [10] = 345 },
                new ItemFixtureRecord(0, 0, 0, 0, 0, SwShItemPouch.Items),
                new ItemFixtureRecord(
                    1,
                    1,
                    0,
                    0,
                    0,
                    SwShItemPouch.TMs,
                    FieldUseType: 2,
                    GroupType: 4,
                    GroupIndex: 10)));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames("None", "TM10"));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/wazaname.dat",
            CreateIndexedText(346, (345, "Magical Leaf")));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShItemsWorkflowService().Load(project);

        var item = workflow.Items[1];
        Assert.Equal(3, workflow.Stats.SourceFileCount);
        Assert.Equal("TM10 (Magical Leaf)", item.Name);
        Assert.Equal(10, item.Metadata.MachineSlot);
        Assert.Equal(345, item.Metadata.MachineMoveId);
        Assert.Equal("Magical Leaf", item.Metadata.MachineMoveName);
        Assert.Contains(
            item.DetailGroups.Single(group => group.Label == "Inventory").Details,
            detail => detail.Label == "Machine" && detail.Value == "TM10 (slot 10) -> Magical Leaf (345)");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShItemsWorkflowService.MachineMoveIdField).Options,
            option => option.Value == 345 && option.Label == "345 Magical Leaf");
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
                new ItemFixtureRecord(
                    1,
                    1,
                    300,
                    15,
                    3,
                    SwShItemPouch.Medicine,
                    FlingPower: 30,
                    FieldUseType: 1,
                    FieldFlags: 2,
                    CanUseOnPokemon: true,
                    ItemType: 9,
                    SortIndex: 5,
                    ItemSprite: 12,
                    UseFlags1: 4,
                    HealAmount: 20,
                    FriendshipGain1: 1,
                    FriendshipGain2: 1),
                new ItemFixtureRecord(2, 2, 200, 10, 5, SwShItemPouch.Medicine)));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames("None", "Potion", "Antidote"));
    }

    private static byte[] CreateIndexedText(int count, params (int Index, string Text)[] entries)
    {
        var values = Enumerable.Repeat(string.Empty, count).ToArray();
        foreach (var (index, text) in entries)
        {
            values[index] = text;
        }

        return SwShItemTestFixtures.CreateItemNames(values);
    }
}
