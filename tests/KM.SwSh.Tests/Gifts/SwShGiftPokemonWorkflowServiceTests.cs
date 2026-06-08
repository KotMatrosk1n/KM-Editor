// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Gifts;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Gifts;

public sealed class SwShGiftPokemonWorkflowServiceTests
{
    [Fact]
    public void LoadReadsGiftPokemonFromRealSwordShieldGiftTable()
    {
        using var temp = TemporarySwShProject.Create();
        WriteGiftFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShGiftPokemonWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Equal(2, workflow.Stats.TotalGiftCount);
        Assert.Equal(1, workflow.Stats.EggGiftCount);
        Assert.Equal(1, workflow.Stats.FixedIvGiftCount);
        Assert.Equal(4, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);

        var firstGift = workflow.Gifts[0];
        Assert.Equal(0, firstGift.GiftIndex);
        Assert.Equal("Gift 001: Grookey (Form 1) Lv. 50", firstGift.Label);
        Assert.Equal(810, firstGift.SpeciesId);
        Assert.Equal("Grookey", firstGift.Species);
        Assert.Equal(1, firstGift.Form);
        Assert.Equal(50, firstGift.Level);
        Assert.False(firstGift.IsEgg);
        Assert.Equal(1, firstGift.HeldItemId);
        Assert.Equal("Potion", firstGift.HeldItem);
        Assert.Equal(4, firstGift.BallItemId);
        Assert.Equal("Poke Ball", firstGift.BallItem);
        Assert.Equal(3, firstGift.Ability);
        Assert.Equal("Hidden Ability", firstGift.AbilityLabel);
        Assert.Equal(25, firstGift.Nature);
        Assert.Equal("Random", firstGift.NatureLabel);
        Assert.Equal(0, firstGift.Gender);
        Assert.Equal("Random", firstGift.GenderLabel);
        Assert.Equal(0, firstGift.ShinyLock);
        Assert.Equal("Random", firstGift.ShinyLockLabel);
        Assert.Equal(10, firstGift.DynamaxLevel);
        Assert.True(firstGift.CanGigantamax);
        Assert.Equal(2, firstGift.SpecialMoveId);
        Assert.Equal("Growl", firstGift.SpecialMove);
        Assert.Equal(new SwShGiftPokemonIvsRecord(31, 30, 29, 27, 26, 28), firstGift.Ivs);
        Assert.Null(firstGift.FlawlessIvCount);
        Assert.Contains("HP 31", firstGift.IvSummary, StringComparison.Ordinal);
        Assert.Equal(ProjectFileLayer.Base, firstGift.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, firstGift.Provenance.FileState);

        var eggGift = workflow.Gifts[1];
        Assert.True(eggGift.IsEgg);
        Assert.Equal("Random IVs", eggGift.IvSummary);
        Assert.Equal(0, eggGift.FlawlessIvCount);
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShGiftPokemonWorkflowService.SpeciesField).Options,
            option => option.Value == 810 && option.Label == "810 Grookey");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShGiftPokemonWorkflowService.HeldItemIdField).Options,
            option => option.Value == 1 && option.Label == "001 Potion");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShGiftPokemonWorkflowService.SpecialMoveIdField).Options,
            option => option.Value == 2 && option.Label == "002 Growl");
    }

    [Fact]
    public void LoadPrefersLayeredGiftPokemonDataWhenOutputOverridesBase()
    {
        using var temp = TemporarySwShProject.Create();
        WriteGiftFixture(temp);
        temp.WriteOutputFile(
            SwShGiftPokemonWorkflowService.GiftPokemonDataPath,
            CreateGiftTable(new SwShGiftPokemonIvs(31, 31, 31, 31, 31, 31)));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShGiftPokemonWorkflowService().Load(project);

        var gift = workflow.Gifts[0];
        Assert.Equal(ProjectFileLayer.Layered, gift.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.LayeredOverride, gift.Provenance.FileState);
        Assert.Equal("6 perfect IVs", gift.IvSummary);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenGiftTableIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/gifts.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShGiftPokemonWorkflowService().Load(project);

        Assert.Empty(workflow.Gifts);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.giftPokemon");
    }

    internal static void WriteGiftFixture(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            SwShGiftPokemonWorkflowService.GiftPokemonDataPath["romfs/".Length..],
            CreateGiftTable(new SwShGiftPokemonIvs(31, 30, 29, 28, 27, 26)));
        temp.WriteBaseRomFsFile("bin/message/English/common/monsname.dat", CreateTextTable(810, (133, "Eevee"), (810, "Grookey")));
        temp.WriteBaseRomFsFile("bin/message/English/common/itemname.dat", CreateTextTable(4, (1, "Potion"), (4, "Poke Ball")));
        temp.WriteBaseRomFsFile("bin/message/English/common/wazaname.dat", CreateTextTable(2, (1, "Scratch"), (2, "Growl")));
    }

    internal static byte[] CreateGiftTable(SwShGiftPokemonIvs firstGiftIvs)
    {
        return new SwShGiftPokemonArchive(
        [
            new KM.Formats.SwSh.SwShGiftPokemonRecord(
                0,
                0,
                1,
                10,
                4,
                0,
                0x1122334455667788,
                true,
                1,
                50,
                810,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                25,
                0,
                firstGiftIvs,
                3,
                2),
            new KM.Formats.SwSh.SwShGiftPokemonRecord(
                1,
                1,
                0,
                0,
                4,
                0,
                0,
                false,
                0,
                1,
                133,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                new SwShGiftPokemonIvs(-1, -1, -1, -1, -1, -1),
                0,
                0),
        ]).Write();
    }

    private static byte[] CreateTextTable(int highestIndex, params (int index, string value)[] entries)
    {
        var lines = Enumerable.Range(0, highestIndex + 1)
            .Select(_ => new SwShGameTextLine(string.Empty, Flags: 0))
            .ToArray();

        foreach (var (index, value) in entries)
        {
            lines[index] = new SwShGameTextLine(value, Flags: 0);
        }

        return SwShGameTextFile.Write(lines);
    }
}
