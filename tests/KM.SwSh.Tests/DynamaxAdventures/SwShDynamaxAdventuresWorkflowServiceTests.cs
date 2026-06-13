// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Tests.Items;
using KM.SwSh.Tests.Pokemon;
using Xunit;

namespace KM.SwSh.Tests.DynamaxAdventures;

public sealed class SwShDynamaxAdventuresWorkflowServiceTests
{
    [Fact]
    public void LoadReadsDynamaxAdventureRecordsFromRealSwordShieldTable()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShDynamaxAdventuresWorkflowService().Load(project);

        Assert.Equal(2, workflow.Stats.TotalEncounterCount);
        Assert.Equal(1, workflow.Stats.SingleCaptureCount);
        Assert.Equal(1, workflow.Stats.StoryGatedCount);
        Assert.Equal(1, workflow.Stats.GuaranteedPerfectIvEncounterCount);

        var first = workflow.Encounters[0];
        Assert.Equal(0, first.EntryIndex);
        Assert.Equal("000 / 100 - Eevee (Form 1) [Sword]", first.Label);
        Assert.Equal(100, first.AdventureIndex);
        Assert.Equal(133, first.SpeciesId);
        Assert.Equal("Eevee", first.Species);
        Assert.Equal(4, first.BallItemId);
        Assert.Equal("Poke Ball", first.BallItem);
        Assert.Equal("Ability 2", first.AbilityLabel);
        Assert.Equal("Normal", first.GigantamaxLabel);
        Assert.Equal("Sword", first.VersionLabel);
        Assert.Equal("Enabled", first.ShinyRollLabel);
        Assert.True(first.IsSingleCapture);
        Assert.Equal("0x1122334455667788", first.SingleCaptureFlagBlock);
        Assert.True(first.IsStoryProgressGated);
        Assert.Equal("0x8877665544332211", first.UiMessageId);
        Assert.Equal("Vine Whip", first.Moves[2].Move);
        Assert.Equal(4, first.GuaranteedPerfectIvs);
        Assert.Equal(-1, first.Ivs.Attack);
        Assert.Contains("4 guaranteed perfect", first.IvSummary, StringComparison.Ordinal);
        Assert.Equal(ProjectFileLayer.Base, first.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, first.Provenance.FileState);
        var vanilla = first.VanillaPokemon;
        Assert.NotNull(vanilla);
        Assert.Equal(133, vanilla!.SpeciesId);
        Assert.Equal(65, vanilla.Level);
        Assert.Equal(20, vanilla.Moves[3].MoveId);

        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShDynamaxAdventuresWorkflowService.SpeciesField).Options,
            option => option.Value == 133 && option.Label == "133 Eevee");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShDynamaxAdventuresWorkflowService.Move3Field).Options,
            option => option.Value == 85 && option.Label == "085 Thunderbolt");
        Assert.DoesNotContain(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.BallItemIdField);
        Assert.DoesNotContain(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.VersionField);
        Assert.DoesNotContain(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.ShinyRollField);
        Assert.DoesNotContain(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.IsSingleCaptureField);
        Assert.DoesNotContain(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.IsStoryProgressGatedField);
        Assert.DoesNotContain(workflow.EditableFields, field => field.Field == SwShDynamaxAdventuresWorkflowService.OtGenderField);
    }

    [Fact]
    public void LoadPrefersLayeredDynamaxAdventureDataWhenOutputOverridesBase()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteOutputFile(
            SwShDynamaxAdventuresWorkflowService.DynamaxAdventureDataPath,
            SwShDynamaxAdventureTestFixtures.CreateArchive().WriteEdits(
            [
                new(0, SwShDynamaxAdventureField.IvAttack, 31),
            ]));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShDynamaxAdventuresWorkflowService().Load(project);

        var first = workflow.Encounters[0];
        Assert.Equal(ProjectFileLayer.Layered, first.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.LayeredOverride, first.Provenance.FileState);
        Assert.Equal(31, first.Ivs.Attack);
        var vanilla = first.VanillaPokemon;
        Assert.NotNull(vanilla);
        Assert.Equal(-1, vanilla!.Ivs.Attack);
    }

    [Fact]
    public void LoadFiltersDynamaxAdventureSpeciesOptionsToPokemonPresentInSwordShield()
    {
        using var temp = TemporarySwShProject.Create();
        SwShDynamaxAdventureTestFixtures.WriteBaseDynamaxAdventures(temp);
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/monsname.dat",
            CreateSpeciesNameTable(133, (16, "Pidgey"), (25, "Pikachu"), (133, "Eevee")));
        var personalRecords = Enumerable.Range(0, 134)
            .Select(_ => SwShPokemonWorkflowServiceTests.CreateEmptyPersonalRecord())
            .ToArray();
        personalRecords[25] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hatchedSpecies: 25);
        personalRecords[133] = SwShPokemonWorkflowServiceTests.CreateBulbasaurPersonalRecord(hatchedSpecies: 133);
        temp.WriteBaseRomFsFile(
            "bin/pml/personal/personal_total.bin",
            SwShPokemonWorkflowServiceTests.CreatePersonalTable(personalRecords));
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShDynamaxAdventuresWorkflowService().Load(project);

        var speciesOptions = workflow.EditableFields.Single(field =>
            field.Field == SwShDynamaxAdventuresWorkflowService.SpeciesField).Options;
        Assert.Contains(speciesOptions, option => option.Value == 25 && option.Label == "025 Pikachu");
        Assert.Contains(speciesOptions, option => option.Value == 133 && option.Label == "133 Eevee");
        Assert.DoesNotContain(speciesOptions, option => option.Value == 16);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenDynamaxAdventureTableIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/adventures.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "main");
        var project = new ProjectWorkspaceService().Open(temp.Paths);

        var workflow = new SwShDynamaxAdventuresWorkflowService().Load(project);

        Assert.Empty(workflow.Encounters);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.dynamaxAdventures");
    }

    private static byte[] CreateSpeciesNameTable(int highestIndex, params (int Index, string Value)[] entries)
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
