// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
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
        Assert.Equal(3, workflow.Stats.SourceFileCount);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Message.Contains(
                    "do not form an expected Sword and Shield pair",
                    StringComparison.Ordinal));

        var table = workflow.Tables[0];
        Assert.Equal("table_AABBCCDD00112233", table.DenId);
        Assert.Equal("Sword - Encounter Table 0", table.DisplayName);
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
        Assert.StartsWith("Any Ability", slot.AbilityLabel, StringComparison.Ordinal);
        Assert.True(slot.IsGigantamax);
        Assert.Equal(1, slot.Gender);
        Assert.Equal("Male", slot.GenderLabel);
        Assert.Equal(4, slot.FlawlessIvs);
        Assert.Equal([100, 20, 30, 40, 50], slot.Probabilities);
        Assert.Contains("5-star 50%", slot.ProbabilitySummary, StringComparison.Ordinal);
        Assert.Contains(slot.FormOptions, option => option.Value == 2 && option.Label == "Form 2");
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
    public void LoadReadsFourByteAlignedEncounterTables()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRaidBattleTestFixtures.WriteBaseRaidBattles(temp);
        temp.WriteBaseRomFsFile(
            SwShRaidRewardsWorkflowService.NestDataPath["romfs/".Length..],
            SwShRaidBattleTestFixtures.CreateFourByteAlignedRaidBattlePack());
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidBattlesWorkflowService().Load(project);

        var table = Assert.Single(workflow.Tables);
        var slot = Assert.Single(table.Slots);
        Assert.Equal(25, slot.SpeciesId);
        Assert.Equal("Pikachu", slot.Species);
        Assert.DoesNotContain(
            workflow.Diagnostics,
            diagnostic => diagnostic.Message.Contains("not aligned", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadSharesDenNumberOnlyForExpectedSwordShieldPairs()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRaidBattleTestFixtures.WriteBaseRaidBattles(
            temp,
            SwShRaidBattleTestFixtures.CreatePairedArchive());
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidBattlesWorkflowService().Load(project);

        Assert.Collection(
            workflow.Tables,
            table => Assert.Equal("Sword - 0", table.DisplayName),
            table => Assert.Equal("Shield - 0", table.DisplayName));
        Assert.DoesNotContain(
            workflow.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "do not form an expected Sword and Shield pair",
                StringComparison.Ordinal));
    }

    [Fact]
    public void LoadUsesPhysicalTableLabelsForMalformedVersionPairs()
    {
        using var temp = TemporarySwShProject.Create();
        var source = SwShRaidBattleTestFixtures.CreatePairedArchive();
        var malformedArchive = source with
        {
            Tables =
            [
                source.Tables[0],
                source.Tables[1] with { GameVersion = 1 },
            ],
        };
        SwShRaidBattleTestFixtures.WriteBaseRaidBattles(temp, malformedArchive);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidBattlesWorkflowService().Load(project);

        Assert.Collection(
            workflow.Tables,
            table => Assert.Equal("Sword - Encounter Table 0", table.DisplayName),
            table => Assert.Equal("Sword - Encounter Table 1", table.DisplayName));
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning
                && diagnostic.Message.Contains(
                    "tables 0 and 1 do not form an expected Sword and Shield pair",
                    StringComparison.Ordinal)
                && diagnostic.Message.Contains(
                    "physical encounter table indexes",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void LoadRejectsEncounterEntriesWithFewerThanFiveProbabilities()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRaidBattleTestFixtures.WriteBaseRaidBattlesWithFirstProbabilityCount(temp, 4);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidBattlesWorkflowService().Load(project);

        Assert.Empty(workflow.Tables);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains(
                    "contains 4 star probabilities; at least 5 are required",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void LoadDisplaysExtraProbabilitiesWithoutExposingExtraEditableFields()
    {
        using var temp = TemporarySwShProject.Create();
        var archive = ReplaceFirstProbabilities([100, 20, 30, 40, 50, 60]);
        SwShRaidBattleTestFixtures.WriteBaseRaidBattles(temp, archive);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidBattlesWorkflowService().Load(project);

        var slot = workflow.Tables[0].Slots[0];
        Assert.Equal([100, 20, 30, 40, 50, 60], slot.Probabilities);
        Assert.Contains("6-star 60%", slot.ProbabilitySummary, StringComparison.Ordinal);
        Assert.Equal(
            [
                SwShRaidBattlesWorkflowService.Star1ProbabilityField,
                SwShRaidBattlesWorkflowService.Star2ProbabilityField,
                SwShRaidBattlesWorkflowService.Star3ProbabilityField,
                SwShRaidBattlesWorkflowService.Star4ProbabilityField,
                SwShRaidBattlesWorkflowService.Star5ProbabilityField,
            ],
            workflow.EditableFields
                .Where(field => field.Field.Contains("Probability", StringComparison.Ordinal))
                .Select(field => field.Field));
    }

    [Fact]
    public void LoadReportsProbabilityValuesOutsideTheDisplayRangeAsUnsupportedData()
    {
        using var temp = TemporarySwShProject.Create();
        var archive = ReplaceFirstProbabilities([uint.MaxValue, 20, 30, 40, 50]);
        SwShRaidBattleTestFixtures.WriteBaseRaidBattles(temp, archive);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidBattlesWorkflowService().Load(project);

        Assert.Empty(workflow.Tables);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("4294967295", StringComparison.Ordinal)
                && diagnostic.Message.Contains("exceeds the supported display range", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadMarksDuplicateRewardTableLinksAsAmbiguous()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRaidBattleTestFixtures.WriteBaseRaidBattles(temp);
        var duplicateBonusArchive = new SwShNestHoleRewardArchive(
        [
            new SwShNestHoleRewardTable(
                SwShRaidRewardTestFixtures.BonusTableId,
                [new SwShNestHoleReward(20, 4, [1, 2, 3, 4, 5])]),
            new SwShNestHoleRewardTable(
                SwShRaidRewardTestFixtures.BonusTableId,
                [new SwShNestHoleReward(21, 2, [5, 4, 3, 2, 1])]),
        ]);
        temp.WriteBaseRomFsFile(
            SwShRaidRewardsWorkflowService.NestDataPath["romfs/".Length..],
            SwShGfPackFile.Create(
            [
                new SwShGfPackNamedFile(
                    SwShRaidBattlesWorkflowService.EncounterMemberName,
                    SwShRaidBattleTestFixtures.CreateArchive().Write()),
                new SwShGfPackNamedFile(
                    "nest_hole_drop_rewards.bin",
                    SwShRaidRewardTestFixtures.CreateDropArchive().Write()),
                new SwShGfPackNamedFile(
                    "nest_hole_bonus_rewards.bin",
                    duplicateBonusArchive.Write()),
            ]).Write());
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidBattlesWorkflowService().Load(project);

        var link = Assert.Single(workflow.Tables).Slots[0].BonusRewardLink;
        Assert.False(link.IsMatched);
        Assert.Empty(link.TableId);
        Assert.Equal(0, link.RewardItemCount);
        Assert.Contains("Ambiguous: 2 loaded Bonus tables share this hash", link.Preview, StringComparison.Ordinal);
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

    private static SwShEncounterNestArchive ReplaceFirstProbabilities(IReadOnlyList<uint> probabilities)
    {
        var source = SwShRaidBattleTestFixtures.CreatePairedArchive();
        var swordTable = source.Tables[0];
        var entries = swordTable.Entries.ToArray();
        entries[0] = entries[0] with { Probabilities = probabilities };

        return source with
        {
            Tables =
            [
                swordTable with { Entries = entries },
                source.Tables[1],
            ],
        };
    }
}
