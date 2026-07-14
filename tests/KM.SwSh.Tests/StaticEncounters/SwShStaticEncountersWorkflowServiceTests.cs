// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.StaticEncounters;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.StaticEncounters;

public sealed class SwShStaticEncountersWorkflowServiceTests
{
    [Fact]
    public void LoadReadsStaticEncountersFromRealSwordShieldStaticTable()
    {
        using var temp = TemporarySwShProject.Create();
        WriteStaticEncounterFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShStaticEncountersWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Equal(2, workflow.Stats.TotalEncounterCount);
        Assert.Equal(1, workflow.Stats.GigantamaxEncounterCount);
        Assert.Equal(1, workflow.Stats.FixedIvEncounterCount);
        Assert.Equal(4, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);

        var firstEncounter = workflow.Encounters[0];
        Assert.Equal(0, firstEncounter.EncounterIndex);
        Assert.StartsWith("Static 000:", firstEncounter.Label, StringComparison.Ordinal);
        Assert.Contains("Grookey", firstEncounter.Label, StringComparison.Ordinal);
        Assert.Equal("0x0102030405060708", firstEncounter.EncounterId);
        Assert.Equal(810, firstEncounter.SpeciesId);
        Assert.Equal("Grookey", firstEncounter.Species);
        Assert.Equal(1, firstEncounter.Form);
        Assert.Equal(50, firstEncounter.Level);
        Assert.Equal(1, firstEncounter.HeldItemId);
        Assert.Equal("Potion", firstEncounter.HeldItem);
        Assert.Equal(3, firstEncounter.Ability);
        Assert.Equal("Hidden Ability", firstEncounter.AbilityLabel);
        Assert.Equal(25, firstEncounter.Nature);
        Assert.Equal("Random", firstEncounter.NatureLabel);
        Assert.Equal(1, firstEncounter.Gender);
        Assert.Equal("Male", firstEncounter.GenderLabel);
        Assert.Equal(2, firstEncounter.ShinyLock);
        Assert.Equal("Never Shiny", firstEncounter.ShinyLockLabel);
        Assert.Equal(17, firstEncounter.EncounterScenario);
        Assert.Equal("Calyrex", firstEncounter.EncounterScenarioLabel);
        Assert.Equal(10, firstEncounter.DynamaxLevel);
        Assert.True(firstEncounter.CanGigantamax);
        Assert.Equal(new SwShStaticEncounterStatsRecord(1, 2, 3, 4, 5, 6), firstEncounter.Evs);
        Assert.Equal(new SwShStaticEncounterStatsRecord(31, 30, 29, 27, 26, 28), firstEncounter.Ivs);
        Assert.Null(firstEncounter.FlawlessIvCount);
        Assert.Contains("HP 31", firstEncounter.IvSummary, StringComparison.Ordinal);
        Assert.Equal(ProjectFileLayer.Base, firstEncounter.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, firstEncounter.Provenance.FileState);
        Assert.Collection(
            firstEncounter.Moves,
            move => Assert.Equal("Scratch", move.Move),
            move => Assert.Equal("Growl", move.Move),
            move => Assert.Equal("Vine Whip", move.Move),
            move => Assert.Equal("Razor Leaf", move.Move));

        var randomEncounter = workflow.Encounters[1];
        Assert.Equal("Random IVs", randomEncounter.IvSummary);
        Assert.Equal(0, randomEncounter.FlawlessIvCount);
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShStaticEncountersWorkflowService.SpeciesField).Options,
            option => option.Value == 810 && option.Label == "810 Grookey");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShStaticEncountersWorkflowService.HeldItemIdField).Options,
            option => option.Value == 1 && option.Label == "001 Potion");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShStaticEncountersWorkflowService.Move0Field).Options,
            option => option.Value == 2 && option.Label == "002 Growl");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShStaticEncountersWorkflowService.NatureField).Options,
            option => option.Value == 3 && option.Label == "Adamant (+Atk/-Sp.Atk)");
    }

    [Fact]
    public void LoadPrefersLayeredStaticEncounterDataWhenOutputOverridesBase()
    {
        using var temp = TemporarySwShProject.Create();
        WriteStaticEncounterFixture(temp);
        temp.WriteOutputFile(
            SwShStaticEncountersWorkflowService.StaticEncounterDataPath,
            CreateStaticEncounterTable(new SwShStaticEncounterStats(31, 31, 31, 31, 31, 31)));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShStaticEncountersWorkflowService().Load(project);

        var encounter = workflow.Encounters[0];
        Assert.Equal(ProjectFileLayer.Layered, encounter.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.LayeredOverride, encounter.Provenance.FileState);
        Assert.Equal("6 guaranteed perfect IVs", encounter.IvSummary);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenStaticEncounterTableIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/static.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShStaticEncountersWorkflowService().Load(project);

        Assert.Empty(workflow.Encounters);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.staticEncounters");
    }

    [Fact]
    public void LoadDoesNotTreatTheLgpeLegacyPathAsSwordShieldStaticData()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile(
            "bin/script_event_data/event_encount.bin",
            CreateStaticEncounterTable(new SwShStaticEncounterStats(-1, -1, -1, -1, -1, -1)));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShStaticEncountersWorkflowService().Load(project);

        Assert.Empty(workflow.Encounters);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Expected == SwShStaticEncountersWorkflowService.StaticEncounterDataPath);
    }

    internal static void WriteStaticEncounterFixture(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            SwShStaticEncountersWorkflowService.StaticEncounterDataPath["romfs/".Length..],
            CreateStaticEncounterTable(new SwShStaticEncounterStats(31, 30, 29, 27, 26, 28)));
        temp.WriteBaseRomFsFile("bin/message/English/common/monsname.dat", CreateTextTable(810, (25, "Pikachu"), (810, "Grookey")));
        temp.WriteBaseRomFsFile("bin/message/English/common/itemname.dat", CreateTextTable(4, (1, "Potion"), (4, "Poke Ball")));
        temp.WriteBaseRomFsFile("bin/message/English/common/wazaname.dat", CreateTextTable(75, (1, "Scratch"), (2, "Growl"), (22, "Vine Whip"), (75, "Razor Leaf")));
    }

    internal static byte[] CreateStaticEncounterTable(SwShStaticEncounterStats firstEncounterIvs)
    {
        return new SwShStaticEncounterArchive(
        [
            new SwShStaticEncounterRecord(
                0,
                0x1122334455667788,
                0x8877665544332211,
                new SwShStaticEncounterStats(1, 2, 3, 4, 5, 6),
                1,
                10,
                0,
                0x0102030405060708,
                0,
                true,
                1,
                50,
                17,
                810,
                2,
                25,
                1,
                firstEncounterIvs,
                3,
                [1, 2, 22, 75]),
            new SwShStaticEncounterRecord(
                1,
                0,
                0,
                new SwShStaticEncounterStats(0, 0, 0, 0, 0, 0),
                0,
                0,
                0,
                0x1111111111111111,
                0,
                false,
                0,
                25,
                0,
                25,
                0,
                0,
                0,
                new SwShStaticEncounterStats(-1, -1, -1, -1, -1, -1),
                0,
                [0, 0, 0, 0]),
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
