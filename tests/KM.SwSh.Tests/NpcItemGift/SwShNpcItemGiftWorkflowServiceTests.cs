// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.NpcItemGift;
using KM.SwSh.Tests.Items;
using Xunit;

namespace KM.SwSh.Tests.NpcItemGift;

public sealed class SwShNpcItemGiftWorkflowServiceTests
{
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
}
