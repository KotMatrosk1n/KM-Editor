// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Rentals;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Rentals;

public sealed class SwShRentalPokemonWorkflowServiceTests
{
    [Fact]
    public void LoadReadsRentalPokemonFromRealSwordShieldRentalTable()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRentalFixture(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRentalPokemonWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Equal(2, workflow.Stats.TotalRentalCount);
        Assert.Equal(1, workflow.Stats.PerfectIvRentalCount);
        Assert.Equal(4, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);

        var firstRental = workflow.Rentals[0];
        Assert.Equal(0, firstRental.RentalIndex);
        Assert.Equal("Rental 001: Eevee (Partner) Lv. 50 | Tackle, Growl", firstRental.Label);
        Assert.Equal(133, firstRental.SpeciesId);
        Assert.Equal("Eevee", firstRental.Species);
        Assert.Equal(1, firstRental.Form);
        Assert.Equal(1, firstRental.HeldItemId);
        Assert.Equal("Potion", firstRental.HeldItem);
        Assert.Equal(4, firstRental.BallItemId);
        Assert.Equal("Poke Ball", firstRental.BallItem);
        Assert.Equal(2, firstRental.Ability);
        Assert.Equal("Hidden Ability", firstRental.AbilityLabel);
        Assert.Equal(13, firstRental.Nature);
        Assert.Equal("Jolly (+Spe/-Sp.Atk)", firstRental.NatureLabel);
        Assert.Equal(1, firstRental.Gender);
        Assert.Equal("Male", firstRental.GenderLabel);
        Assert.Equal(12345u, firstRental.TrainerId);
        Assert.Equal("0x1122334455667788", firstRental.Hash1);
        Assert.Equal("0x8877665544332211", firstRental.Hash2);
        Assert.Equal("Vine Whip", firstRental.Moves[2].Move);
        Assert.Equal(new SwShRentalPokemonStatsRecord(10, 20, 30, 40, 50, 60), firstRental.Evs);
        Assert.Equal(new SwShRentalPokemonStatsRecord(31, 31, 31, 31, 31, 31), firstRental.Ivs);
        Assert.True(firstRental.HasPerfectIvs);
        Assert.Contains("HP 31", firstRental.IvSummary, StringComparison.Ordinal);
        Assert.Equal(ProjectFileLayer.Base, firstRental.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, firstRental.Provenance.FileState);

        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShRentalPokemonWorkflowService.SpeciesField).Options,
            option => option.Value == 133 && option.Label == "133 Eevee");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShRentalPokemonWorkflowService.HeldItemIdField).Options,
            option => option.Value == 1 && option.Label == "001 Potion");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShRentalPokemonWorkflowService.Move1Field).Options,
            option => option.Value == 2 && option.Label == "002 Growl");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShRentalPokemonWorkflowService.NatureField).Options,
            option => option.Value == 3 && option.Label == "Adamant (+Atk/-Sp.Atk)");
        var levelField = workflow.EditableFields.Single(
            field => field.Field == SwShRentalPokemonWorkflowService.LevelField);
        Assert.Equal(SwShRentalPokemonArchive.MinimumPokemonLevel, levelField.MinimumValue);
        Assert.Equal(SwShRentalPokemonArchive.MaximumPokemonLevel, levelField.MaximumValue);
    }

    [Fact]
    public void LoadPrefersLayeredRentalPokemonDataWhenOutputOverridesBase()
    {
        using var temp = TemporarySwShProject.Create();
        WriteRentalFixture(temp);
        temp.WriteOutputFile(
            SwShRentalPokemonWorkflowService.RentalPokemonDataPath,
            CreateRentalTable(new SwShRentalPokemonStats(1, 2, 3, 4, 5, 6)));
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShRentalPokemonWorkflowService().Load(project);

        var rental = workflow.Rentals[0];
        Assert.Equal(ProjectFileLayer.Layered, rental.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.LayeredOverride, rental.Provenance.FileState);
        Assert.False(rental.HasPerfectIvs);
        Assert.Equal(new SwShRentalPokemonStatsRecord(1, 2, 3, 4, 5, 6), rental.Ivs);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenRentalTableIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/rentals.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRentalPokemonWorkflowService().Load(project);

        Assert.Empty(workflow.Rentals);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.rentalPokemon");
    }

    internal static void WriteRentalFixture(TemporarySwShProject temp)
    {
        temp.WriteBaseRomFsFile(
            SwShRentalPokemonWorkflowService.RentalPokemonDataPath["romfs/".Length..],
            CreateRentalTable(new SwShRentalPokemonStats(31, 31, 31, 31, 31, 31)));
        temp.WriteBaseRomFsFile("bin/message/English/common/monsname.dat", CreateTextTable(133, (25, "Pikachu"), (133, "Eevee")));
        temp.WriteBaseRomFsFile("bin/message/English/common/itemname.dat", CreateTextTable(4, (1, "Potion"), (4, "Poke Ball")));
        temp.WriteBaseRomFsFile("bin/message/English/common/wazaname.dat", CreateTextTable(4, (1, "Tackle"), (2, "Growl"), (3, "Vine Whip"), (4, "Razor Leaf")));
    }

    internal static byte[] CreateRentalTable(SwShRentalPokemonStats firstRentalIvs)
    {
        return new SwShRentalPokemonArchive(
        [
            new SwShRentalPokemonRecord(
                0,
                new SwShRentalPokemonStats(10, 20, 30, 40, 50, 60),
                1,
                4,
                0x1122334455667788,
                1,
                50,
                133,
                0x8877665544332211,
                12345,
                13,
                1,
                firstRentalIvs,
                2,
                [1, 2, 3, 4]),
            new SwShRentalPokemonRecord(
                1,
                new SwShRentalPokemonStats(0, 0, 0, 0, 0, 0),
                0,
                4,
                0,
                0,
                1,
                25,
                0,
                0,
                0,
                0,
                new SwShRentalPokemonStats(0, 0, 0, 0, 0, 0),
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
