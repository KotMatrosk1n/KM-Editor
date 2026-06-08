// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.DynamaxAdventures;
using KM.SwSh.Tests.Items;
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
        Assert.Equal("000 / 100 - Eevee-1 [Sword]", first.Label);
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

        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShDynamaxAdventuresWorkflowService.SpeciesField).Options,
            option => option.Value == 133 && option.Label == "133 Eevee");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShDynamaxAdventuresWorkflowService.Move3Field).Options,
            option => option.Value == 85 && option.Label == "085 Thunderbolt");
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
}
