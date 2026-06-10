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
        Assert.Equal("Sword - 0", table.DisplayName);
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
        Assert.True(slot.DropRewardLink.IsMatched);
        Assert.Equal("Drop", slot.DropRewardLink.RewardKindLabel);
        Assert.Equal("0xAABBCCDD00112233", slot.DropRewardLink.SourceTableHash);
        Assert.Equal(2, slot.DropRewardLink.RewardItemCount);
        Assert.Contains("Exp. Candy L", slot.DropRewardLink.Preview, StringComparison.Ordinal);
        Assert.True(slot.BonusRewardLink.IsMatched);
        Assert.Equal("Bonus", slot.BonusRewardLink.RewardKindLabel);
        Assert.Equal("0x1020304050607080", slot.BonusRewardLink.SourceTableHash);
        Assert.Equal(1, slot.BonusRewardLink.RewardItemCount);
        Assert.Contains("Armorite Ore", slot.BonusRewardLink.Preview, StringComparison.Ordinal);

        var unmatchedSlot = table.Slots[1];
        Assert.True(unmatchedSlot.DropRewardLink.IsMatched);
        Assert.False(unmatchedSlot.BonusRewardLink.IsMatched);
        Assert.Equal("0x0807060504030201", unmatchedSlot.BonusRewardLink.SourceTableHash);
        Assert.Contains("No loaded bonus table matches this hash", unmatchedSlot.BonusRewardLink.Preview, StringComparison.Ordinal);

        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShRaidBattlesWorkflowService.SpeciesField).Options,
            option => option.Value == 133 && option.Label == "133 Eevee");
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShRaidBattlesWorkflowService.FlawlessIvsField).Options,
            option => option.Value == 6 && option.Label == "6 Guaranteed Perfect IVs");
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
