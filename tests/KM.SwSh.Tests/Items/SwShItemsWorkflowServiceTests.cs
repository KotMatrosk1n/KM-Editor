// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
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
        Assert.Equal(2, item.Metadata.BattlePouch);
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
            item.DetailGroups.Single(group => group.Label == "Field Use").Details,
            detail => detail.Label == "Battle pouch" && detail.Value == "Use (2)");
        var battlePouch = workflow.EditableFields.Single(
            field => field.Field == SwShItemsWorkflowService.BattlePouchField);
        Assert.Equal([0, 1, 2], battlePouch.Options.Select(option => option.Value));
        Assert.DoesNotContain(
            workflow.EditableFields,
            field => field.Field == SwShItemsWorkflowService.FieldFlagsField);
        foreach (var rawField in new[]
        {
            SwShItemsWorkflowService.CureStatusFlagsField,
            SwShItemsWorkflowService.UseFlags1Field,
            SwShItemsWorkflowService.UseFlags2Field,
        })
        {
            var field = workflow.EditableFields.Single(candidate => candidate.Field == rawField);
            Assert.True(field.IsReadOnly);
            Assert.Equal(SwShItemsWorkflowService.RawFlagsReadOnlyReason, field.ReadOnlyReason);
        }
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

    [Theory]
    [InlineData("es", "Spanish", "Pocion")]
    [InlineData("zh", "Simp_Chinese", "伤药")]
    [InlineData("zh-Hant", "Trad_Chinese", "傷藥")]
    public void LoadUsesSelectedLanguageItemNamesWhenAvailable(
        string languageCode,
        string messageFolder,
        string expectedItemName)
    {
        using var temp = TemporarySwShProject.Create();
        WriteBaseItems(temp);
        temp.WriteBaseRomFsFile(
            $"bin/message/{messageFolder}/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames("None", expectedItemName, "Antidote"));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with
        {
            GameTextLanguage = languageCode,
            OutputRootPath = null,
        });

        var workflow = new SwShItemsWorkflowService().Load(project);

        Assert.Equal(expectedItemName, workflow.Items[1].Name);
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

    [Fact]
    public void LoadReturnsDiagnosticInsteadOfThrowingForPriceAboveSupportedRange()
    {
        using var temp = TemporarySwShProject.Create();
        var data = SwShItemTestFixtures.CreateItemTable(
            new ItemFixtureRecord(0, 0, 0, 0, 0, SwShItemPouch.Items),
            new ItemFixtureRecord(1, 1, 300, 15, 3, SwShItemPouch.Medicine));
        var rowsStart = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x40));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(rowsStart + 0x30), uint.MaxValue);
        temp.WriteBaseRomFsFile("bin/pml/item/item.dat", data);
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames("None", "Potion"));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShItemsWorkflowService().Load(project);

        Assert.Empty(workflow.Items);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == KM.Core.Diagnostics.DiagnosticSeverity.Error
                && diagnostic.Message.Contains("outside the editor's supported range", StringComparison.Ordinal));
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
                    BattlePouch: 2,
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
