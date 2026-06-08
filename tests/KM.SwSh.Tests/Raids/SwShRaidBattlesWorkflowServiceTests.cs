// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.SwSh.Raids;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Raids;

public sealed class SwShRaidBattlesWorkflowServiceTests
{
    [Fact]
    public void LoadReadsRaidBattleSlotsFromRealNestDataPack()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRaidBattleTestFixtures.WriteBaseRaidBattles(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidBattlesWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Single(workflow.Tables);
        Assert.Equal(2, workflow.Stats.TotalSlotCount);
        Assert.Equal(1, workflow.Stats.GigantamaxSlotCount);
        Assert.Equal(2, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);

        var table = workflow.Tables[0];
        Assert.Equal("table_AABBCCDD00112233", table.DenId);
        Assert.Equal("Sword", table.GameVersion);
        Assert.Equal("0xAABBCCDD00112233", table.SourceTableHash);
        Assert.Equal(ProjectFileLayer.Base, table.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, table.Provenance.FileState);

        var slot = table.Slots[0];
        Assert.Equal(1, slot.Slot);
        Assert.Equal(0, slot.EntryIndex);
        Assert.Equal(133, slot.SpeciesId);
        Assert.Equal("Eevee", slot.Species);
        Assert.Equal(1, slot.Form);
        Assert.Equal(4, slot.Ability);
        Assert.Equal("Any Ability", slot.AbilityLabel);
        Assert.True(slot.IsGigantamax);
        Assert.Equal(1, slot.Gender);
        Assert.Equal("Male", slot.GenderLabel);
        Assert.Equal(4, slot.FlawlessIvs);
        Assert.Equal([100, 20, 30, 40, 50], slot.Probabilities);
        Assert.Contains("5-star 50%", slot.ProbabilitySummary, StringComparison.Ordinal);
        Assert.Equal("0x1122334455667788", slot.LevelTableHash);
        Assert.Equal("0xAABBCCDD00112233", slot.DropTableHash);
        Assert.Equal("0x1020304050607080", slot.BonusTableHash);

        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShRaidBattlesWorkflowService.SpeciesField).Options,
            option => option.Value == 133 && option.Label == "133 Eevee");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShRaidBattlesWorkflowService.FlawlessIvsField).Options,
            option => option.Value == 6 && option.Label == "6 Perfect IVs");
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenEncounterMemberIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRaidRewardTestFixtures.WriteBaseRaidRewards(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidBattlesWorkflowService().Load(project);

        Assert.Empty(workflow.Tables);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Domain == "workflow.raidBattles");
    }
}
