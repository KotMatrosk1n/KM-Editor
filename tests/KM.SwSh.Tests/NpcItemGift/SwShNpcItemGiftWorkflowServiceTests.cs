// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Core.Diagnostics;
using KM.Formats.SwSh;
using KM.SwSh.NpcItemGift;
using KM.SwSh.Scripts;
using KM.SwSh.Tests.Items;
using System.Buffers.Binary;
using Xunit;

namespace KM.SwSh.Tests.NpcItemGift;

public sealed class SwShNpcItemGiftWorkflowServiceTests
{
    private const ushort PawnMagic64 = 0xF1E1;
    private const uint PackedConstantOpcode = 0x000000BC;

    [Fact]
    public void LoadFiltersItemOptionsToRealSelectableItems()
    {
        using var temp = TemporarySwShProject.Create();
        WriteItemOptionsFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShNpcItemGiftWorkflowService().Load(project);

        Assert.Contains(workflow.ItemOptions, item => item.ItemId == 1 && item.Name == "Potion");
        Assert.Contains(workflow.ItemOptions, item => item.ItemId == 5 && item.Name == "Rare Candy");
        Assert.DoesNotContain(workflow.ItemOptions, item => item.ItemId == 0);
        Assert.DoesNotContain(workflow.ItemOptions, item => string.Equals(item.Name, "None", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(workflow.ItemOptions, item => item.Name.StartsWith("Item ", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(workflow.ItemOptions, item => item.Name.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(workflow.ItemOptions, item => item.Name.Contains("Dummy", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(workflow.ItemOptions, item => item.Name.Contains("???", StringComparison.Ordinal));
    }

    [Fact]
    public void StageGiftsRejectsNoneItemId()
    {
        using var temp = TemporarySwShProject.Create();
        WriteItemOptionsFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var gift = SwShNpcItemGiftWorkflowService.GetDefinitionsForGame(ProjectGame.Sword)[0];
        var selection = new SwShNpcItemGiftSelection(
            gift.GiftId,
            gift.Quantity,
            gift.Items
                .Select(slot => new SwShNpcItemGiftItemSelection(slot.SlotId, ItemId: 0))
                .ToArray());

        var result = new SwShNpcItemGiftEditSessionService().StageGifts(
            temp.Paths,
            [selection],
            session: null);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Message.Contains("item 0 is not selectable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadIncludesDocumentedWishingStarStoryGrant()
    {
        using var temp = TemporarySwShProject.Create();
        WriteBroadItemOptionsFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var definition = Assert.Single(
            SwShNpcItemGiftWorkflowService.GetDefinitionsForGame(ProjectGame.Sword),
            gift => gift.GiftId == "hop-postwick-wishing-star");
        WriteGiftScriptFixture(temp, definition);

        var workflow = new SwShNpcItemGiftWorkflowService().Load(new ProjectWorkspaceService().Open(temp.Paths));

        var hop = Assert.Single(workflow.Npcs, npc => npc.NpcId == "hop");
        var gift = Assert.Single(hop.Gifts, gift => gift.GiftId == "hop-postwick-wishing-star");
        Assert.Equal(1, gift.Quantity);
        var item = Assert.Single(gift.Items);
        Assert.Equal(1076, item.ItemId);
        Assert.Equal("Wishing Star", item.ItemName);
    }

    [Fact]
    public void StageAndApplySoniaGiftsWritesReportedReviveScripts()
    {
        using var temp = TemporarySwShProject.Create();
        WriteBroadItemOptionsFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");

        var soniaDefinitions = SwShNpcItemGiftWorkflowService
            .GetDefinitionsForGame(ProjectGame.Sword)
            .Where(gift => gift.NpcId == "sonia")
            .ToArray();
        foreach (var definition in soniaDefinitions)
        {
            WriteGiftScriptFixture(temp, definition);
        }

        var workflow = new SwShNpcItemGiftWorkflowService().Load(new ProjectWorkspaceService().Open(temp.Paths));
        var sonia = Assert.Single(workflow.Npcs, npc => npc.NpcId == "sonia");
        var selections = sonia.Gifts
            .Select(gift => gift.GiftId switch
            {
                "sonia-stow-on-side-revive" => CreateSelection(gift, quantity: 7, itemId: 5),
                "sonia-slumbering-weald-max-revive" => CreateSelection(gift, quantity: 9, itemId: 1),
                _ => CreateSelection(gift, gift.Quantity, gift.Items.Single().ItemId),
            })
            .ToArray();
        var service = new SwShNpcItemGiftEditSessionService();

        var staged = service.StageGifts(temp.Paths, selections, session: null);
        var plan = service.CreateChangePlan(temp.Paths, staged.Session);
        var apply = service.ApplyChangePlan(temp.Paths, staged.Session, plan);

        Assert.DoesNotContain(staged.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(plan.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(apply.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(2, plan.Writes.Count);
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == "romfs/bin/script/amx/main_event_1110.amx");
        Assert.Contains(plan.Writes, write => write.TargetRelativePath == "romfs/bin/script/amx/main_event_1820.amx");

        var stowOutput = ReadOutputScript(temp, "main_event_1110.amx");
        Assert.Equal(7, SwShAmxCellPatcher.ReadCodeCellInt(stowOutput, 5246));
        Assert.Equal(5, SwShAmxCellPatcher.ReadCodeCellInt(stowOutput, 5247));
        Assert.Equal(7, SwShAmxCellPatcher.ReadCodeCellInt(stowOutput, 5130));
        Assert.Equal(5, SwShAmxCellPatcher.ReadCodeCellInt(stowOutput, 5131));
        Assert.Equal(2, SwShAmxCellPatcher.ReadCodeCellInt(ReadBaseScript(temp, "main_event_1110.amx"), 5246));
        Assert.Equal(28, SwShAmxCellPatcher.ReadCodeCellInt(ReadBaseScript(temp, "main_event_1110.amx"), 5247));
        Assert.Equal(2, SwShAmxCellPatcher.ReadCodeCellInt(ReadBaseScript(temp, "main_event_1110.amx"), 5130));
        Assert.Equal(28, SwShAmxCellPatcher.ReadCodeCellInt(ReadBaseScript(temp, "main_event_1110.amx"), 5131));

        var slumberingOutput = ReadOutputScript(temp, "main_event_1820.amx");
        Assert.Equal(9, SwShAmxCellPatcher.ReadCodeCellInt(slumberingOutput, 6775));
        Assert.Equal(1, SwShAmxCellPatcher.ReadCodeCellInt(slumberingOutput, 6776));
        Assert.Equal(9, SwShAmxCellPatcher.ReadCodeCellInt(slumberingOutput, 6335));
        Assert.Equal(1, SwShAmxCellPatcher.ReadCodeCellInt(slumberingOutput, 6336));
        Assert.Equal(3, SwShAmxCellPatcher.ReadCodeCellInt(ReadBaseScript(temp, "main_event_1820.amx"), 6775));
        Assert.Equal(29, SwShAmxCellPatcher.ReadCodeCellInt(ReadBaseScript(temp, "main_event_1820.amx"), 6776));
        Assert.Equal(3, SwShAmxCellPatcher.ReadCodeCellInt(ReadBaseScript(temp, "main_event_1820.amx"), 6335));
        Assert.Equal(29, SwShAmxCellPatcher.ReadCodeCellInt(ReadBaseScript(temp, "main_event_1820.amx"), 6336));

        var reloaded = new SwShNpcItemGiftWorkflowService().Load(new ProjectWorkspaceService().Open(temp.Paths));
        var reloadedSonia = Assert.Single(reloaded.Npcs, npc => npc.NpcId == "sonia");
        var stowGift = Assert.Single(reloadedSonia.Gifts, gift => gift.GiftId == "sonia-stow-on-side-revive");
        var slumberingGift = Assert.Single(reloadedSonia.Gifts, gift => gift.GiftId == "sonia-slumbering-weald-max-revive");
        Assert.Equal(7, stowGift.Quantity);
        Assert.Equal(5, Assert.Single(stowGift.Items).ItemId);
        Assert.Equal(9, slumberingGift.Quantity);
        Assert.Equal(1, Assert.Single(slumberingGift.Items).ItemId);
    }

    private static void WriteItemOptionsFixture(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            "bin/pml/item/item.dat",
            SwShItemTestFixtures.CreateItemTable(
                new ItemFixtureRecord(0, 0, 0, 0, 0, SwShItemPouch.Items),
                new ItemFixtureRecord(1, 1, 300, 15, 3, SwShItemPouch.Medicine),
                new ItemFixtureRecord(2, 2, 0, 0, 0, SwShItemPouch.Medicine),
                new ItemFixtureRecord(3, 3, 0, 0, 0, SwShItemPouch.Items),
                new ItemFixtureRecord(4, 4, 0, 0, 0, SwShItemPouch.Items),
                new ItemFixtureRecord(5, 5, 0, 0, 0, SwShItemPouch.Items),
                new ItemFixtureRecord(6, 6, 0, 0, 0, SwShItemPouch.Items)));
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames(
                "None",
                "Potion",
                string.Empty,
                "Unknown 3",
                "Dummy Gift",
                "Rare Candy",
                "???"));
    }

    private static void WriteBroadItemOptionsFixture(TemporarySwShProject temp)
    {
        const int maxItemId = 1278;
        temp.WriteBaseRomFsFile(
            "bin/pml/item/item.dat",
            SwShItemTestFixtures.CreateItemTable(
                Enumerable.Range(0, maxItemId + 1)
                    .Select(itemId => new ItemFixtureRecord(itemId, itemId, 0, 0, 0, SwShItemPouch.Items))
                    .ToArray()));

        var names = Enumerable.Range(0, maxItemId + 1)
            .Select(itemId => itemId switch
            {
                0 => "None",
                1 => "Potion",
                5 => "Rare Candy",
                28 => "Revive",
                29 => "Max Revive",
                78 => "Escape Rope",
                1076 => "Wishing Star",
                406 => "TM79",
                1075 => "Pokemon Box Link",
                1271 => "Sonia's Book",
                _ => $"Selectable {itemId}",
            })
            .ToArray();
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames(names));
    }

    private static void WriteGiftScriptFixture(TemporarySwShProject temp, SwShNpcItemGiftDefinition definition)
    {
        var maxCell = definition.Items
            .Select(item => item.ItemCell)
            .Concat(definition.Items.SelectMany(item => item.CompanionItemCells))
            .Append(definition.QuantityCell)
            .Concat(definition.CompanionQuantityCells)
            .Max();
        var cells = new ulong[maxCell + 1];
        cells[definition.QuantityCell] = PackConstant(definition.Quantity);
        foreach (var companionQuantityCell in definition.CompanionQuantityCells)
        {
            cells[companionQuantityCell] = PackConstant(definition.Quantity);
        }

        foreach (var item in definition.Items)
        {
            cells[item.ItemCell] = PackConstant(item.ItemId);
            foreach (var companionItemCell in item.CompanionItemCells)
            {
                cells[companionItemCell] = PackConstant(item.ItemId);
            }
        }

        temp.WriteBaseRomFsFile(
            definition.RelativePath["romfs/".Length..],
            CreateExpandedAmx(cells));
    }

    private static SwShNpcItemGiftSelection CreateSelection(
        SwShNpcItemGiftRecord gift,
        int quantity,
        int itemId)
    {
        var item = Assert.Single(gift.Items);
        return new SwShNpcItemGiftSelection(
            gift.GiftId,
            quantity,
            [new SwShNpcItemGiftItemSelection(item.SlotId, itemId)]);
    }

    private static byte[] ReadBaseScript(TemporarySwShProject temp, string fileName)
    {
        return File.ReadAllBytes(Path.Combine(temp.BaseRomFsPath, "bin", "script", "amx", fileName));
    }

    private static byte[] ReadOutputScript(TemporarySwShProject temp, string fileName)
    {
        return File.ReadAllBytes(Path.Combine(temp.OutputRootPath, "romfs", "bin", "script", "amx", fileName));
    }

    private static byte[] CreateExpandedAmx(IReadOnlyList<ulong> codeCells)
    {
        const int headerSize = 0x38;
        const int cellSize = 8;
        var dat = headerSize + codeCells.Count * cellSize;
        var amx = new byte[dat];

        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x00), amx.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(amx.AsSpan(0x04), PawnMagic64);
        amx[0x06] = 12;
        amx[0x07] = 14;
        BinaryPrimitives.WriteInt16LittleEndian(amx.AsSpan(0x08), 0);
        BinaryPrimitives.WriteInt16LittleEndian(amx.AsSpan(0x0A), 8);
        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x0C), headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x10), dat);
        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x14), dat);
        BinaryPrimitives.WriteInt32LittleEndian(amx.AsSpan(0x18), dat);

        for (var i = 0; i < codeCells.Count; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(amx.AsSpan(headerSize + i * cellSize), codeCells[i]);
        }

        return amx;
    }

    private static ulong PackConstant(int value)
    {
        return ((ulong)(uint)value << 32) | PackedConstantOpcode;
    }
}
