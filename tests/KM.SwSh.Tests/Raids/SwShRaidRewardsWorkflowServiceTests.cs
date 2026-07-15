// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.Core.Projects;
using KM.Formats.SwSh;
using KM.SwSh.Raids;
using KM.SwSh.Tests.Items;
using KM.SwSh.Workflows;
using Xunit;

namespace KM.SwSh.Tests.Raids;

public sealed class SwShRaidRewardsWorkflowServiceTests
{
    [Fact]
    public void LoadReadsRaidRewardTablesFromRealNestDataPack()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRaidRewardTestFixtures.WriteBaseRaidRewards(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidRewardsWorkflowService().Load(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Single(workflow.Tables);
        Assert.Contains(workflow.EditableFields, field => field.Field == SwShRaidRewardsWorkflowService.ItemIdField);
        Assert.All(
            workflow.EditableFields.Where(field => field.Field.EndsWith("Value", StringComparison.Ordinal)),
            field => Assert.Equal(100, field.MaximumValue));
        Assert.Contains(
            workflow.EditableFields.Single(field => field.Field == SwShRaidRewardsWorkflowService.ItemIdField).Options,
            option => option.Value == 0 && option.Label == "000 None");
        var dropTable = workflow.Tables.Single(table => table.RewardKind == "drop");
        Assert.Equal("Drop 000", dropTable.DisplayName);
        Assert.Equal("nest_hole_drop_rewards.bin", dropTable.ArchiveMember);
        Assert.Equal("0xAABBCCDD00112233", dropTable.SourceTableHash);
        Assert.Equal(ProjectFileLayer.Base, dropTable.Provenance.SourceLayer);
        Assert.Equal(ProjectFileGraphEntryState.BaseOnly, dropTable.Provenance.FileState);
        Assert.Equal("Exp. Candy L", dropTable.Rewards[0].ItemName);
        Assert.Equal([40L, 30L, 20L, 10L, 5L], dropTable.Rewards[0].Values);
        Assert.Equal(1, workflow.Stats.TotalTableCount);
        Assert.Equal(2, workflow.Stats.TotalRewardItemCount);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadBonusReadsRaidBonusRewardTablesFromRealNestDataPack()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRaidRewardTestFixtures.WriteBaseRaidRewards(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidRewardsWorkflowService().LoadBonus(project);

        Assert.Equal(SwShWorkflowAvailability.ReadOnly, workflow.Summary.Availability);
        Assert.Single(workflow.Tables);
        Assert.Equal(SwShWorkflowIds.RaidBonusRewards, workflow.Summary.Id);
        Assert.All(
            workflow.EditableFields.Where(field => field.Field.EndsWith("Value", StringComparison.Ordinal)),
            field => Assert.Equal(999, field.MaximumValue));
        var bonusTable = workflow.Tables.Single(table => table.RewardKind == "bonus");
        Assert.Equal("Bonus 000", bonusTable.DisplayName);
        Assert.Equal("nest_hole_bonus_rewards.bin", bonusTable.ArchiveMember);
        Assert.Equal("Armorite Ore", bonusTable.Rewards[0].ItemName);
        Assert.Equal([1L, 2L, 3L, 4L, 5L], bonusTable.Rewards[0].Values);
        Assert.Equal(1, workflow.Stats.TotalTableCount);
        Assert.Equal(1, workflow.Stats.TotalRewardItemCount);
        Assert.Equal(1, workflow.Stats.SourceFileCount);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadUsesRaidBattleUsageLabelsWhenEncounterDataIsAvailable()
    {
        using var temp = TemporarySwShProject.Create();
        SwShRaidBattleTestFixtures.WriteBaseRaidBattles(temp);
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidRewardsWorkflowService().Load(project);
        var bonusWorkflow = new SwShRaidRewardsWorkflowService().LoadBonus(project);

        Assert.Equal(
            "Drop 000 | SW Den 0 Slot 00, 1-5-Star Eevee-1; SW Den 0 Slot 01, 1-5-Star Pikachu",
            Assert.Single(workflow.Tables).DisplayName);
        Assert.Equal(
            "Bonus 000 | SW Den 0 Slot 00, 1-5-Star Eevee-1",
            Assert.Single(bonusWorkflow.Tables).DisplayName);
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenNestDataPackIsMissing()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("data/raid-rewards.bin", "placeholder");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidRewardsWorkflowService().Load(project);

        Assert.Empty(workflow.Tables);
        Assert.Contains(workflow.Diagnostics, diagnostic => diagnostic.Domain == "workflow.raidRewards");
    }

    [Fact]
    public void LoadReturnsDiagnosticWhenNestDataPackIsUnsupported()
    {
        using var temp = TemporarySwShProject.Create();
        temp.WriteBaseRomFsFile("bin/archive/field/resident/data_table.gfpak", "not-a-pack");
        temp.WriteBaseExeFsFile("main", "base-main");
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidRewardsWorkflowService().Load(project);

        Assert.Empty(workflow.Tables);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Domain == "workflow.raidRewards");
    }

    [Fact]
    public void LoadPreservesUnsignedValuesAndAdditionalColumns()
    {
        using var temp = TemporarySwShProject.Create();
        var dropArchive = new SwShNestHoleRewardArchive(
        [
            new SwShNestHoleRewardTable(
                SwShRaidRewardTestFixtures.DropTableId,
                [
                    new SwShNestHoleReward(
                        uint.MaxValue,
                        uint.MaxValue,
                        [uint.MaxValue, 2, 3, 4, 5, 6]),
                ]),
        ]);
        WriteRaidRewards(temp, dropArchive);
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidRewardsWorkflowService().Load(project);

        var reward = Assert.Single(Assert.Single(workflow.Tables).Rewards);
        Assert.Equal((long)uint.MaxValue, reward.EntryId);
        Assert.Equal((long)uint.MaxValue, reward.ItemId);
        Assert.Equal([(long)uint.MaxValue, 2L, 3L, 4L, 5L, 6L], reward.Values);
        Assert.Empty(workflow.Diagnostics);
    }

    [Fact]
    public void LoadRejectsRewardRowsWithFewerThanFiveValues()
    {
        using var temp = TemporarySwShProject.Create();
        var dropArchive = new SwShNestHoleRewardArchive(
        [
            new SwShNestHoleRewardTable(
                SwShRaidRewardTestFixtures.DropTableId,
                [new SwShNestHoleReward(1, 1, [1, 2, 3, 4, 5])]),
        ]);
        WriteRaidRewards(temp, CreateMalformedShortValuesArchive(dropArchive));
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidRewardsWorkflowService().Load(project);

        Assert.Empty(workflow.Tables);
        Assert.Contains(
            workflow.Diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Message.Contains("at least 5 are required", StringComparison.Ordinal));
    }

    [Fact]
    public void TableSourceIdentityIsDeterministicAndDetectsRewardReordering()
    {
        var member = SwShRaidRewardsWorkflowService.KnownArchiveMembers.Single(candidate => candidate.Key == "drop");
        var first = new SwShNestHoleReward(10, 3, [40, 30, 20, 10, 5]);
        var second = new SwShNestHoleReward(11, 2, [5, 10, 15, 20, 25]);
        var table = new SwShNestHoleRewardTable(
            SwShRaidRewardTestFixtures.DropTableId,
            [first, second]);
        var reordered = table with { Rewards = [second, first] };

        var identity = SwShRaidRewardsWorkflowService.CreateTableSourceIdentity(member, 0, table);
        var repeatedIdentity = SwShRaidRewardsWorkflowService.CreateTableSourceIdentity(member, 0, table);
        var reorderedIdentity = SwShRaidRewardsWorkflowService.CreateTableSourceIdentity(member, 0, reordered);
        var tableId = SwShRaidRewardsWorkflowService.CreateTableId(member, 0, table);

        Assert.Equal(identity, repeatedIdentity);
        Assert.NotEqual(identity, reorderedIdentity);
        Assert.True(SwShRaidRewardsWorkflowService.TryParseTableId(
            tableId,
            out var parsedMember,
            out var tableIndex,
            out var sourceTableId,
            out var parsedIdentity,
            out var isLegacy));
        Assert.Equal(member, parsedMember);
        Assert.Equal(0, tableIndex);
        Assert.Equal(table.TableId, sourceTableId);
        Assert.Equal(identity, parsedIdentity);
        Assert.False(isLegacy);

        var legacyTableId = $"{member.Key}:0:{table.TableId:X16}";
        Assert.True(SwShRaidRewardsWorkflowService.TryParseTableId(
            legacyTableId,
            out _,
            out _,
            out _,
            out var legacyIdentity,
            out var parsedLegacy));
        Assert.Empty(legacyIdentity);
        Assert.True(parsedLegacy);
    }

    [Fact]
    public void LoadLabelsItemZeroAsNone()
    {
        using var temp = TemporarySwShProject.Create();
        var dropArchive = new SwShNestHoleRewardArchive(
        [
            new SwShNestHoleRewardTable(
                SwShRaidRewardTestFixtures.DropTableId,
                [new SwShNestHoleReward(1, 0, [1, 2, 3, 4, 5])]),
        ]);
        WriteRaidRewards(temp, dropArchive);
        var project = new ProjectWorkspaceService().Open(temp.Paths with { OutputRootPath = null });

        var workflow = new SwShRaidRewardsWorkflowService().Load(project);

        Assert.Equal("None", Assert.Single(Assert.Single(workflow.Tables).Rewards).ItemName);
    }

    private static void WriteRaidRewards(
        TemporarySwShProject temp,
        SwShNestHoleRewardArchive dropArchive)
    {
        WriteRaidRewards(temp, dropArchive.Write());
    }

    private static void WriteRaidRewards(
        TemporarySwShProject temp,
        byte[] dropArchive)
    {
        temp.WriteBaseRomFsFile(
            "bin/archive/field/resident/data_table.gfpak",
            SwShGfPackFile.Create(
            [
                new SwShGfPackNamedFile("nest_hole_drop_rewards.bin", dropArchive),
                new SwShGfPackNamedFile(
                    "nest_hole_bonus_rewards.bin",
                    SwShRaidRewardTestFixtures.CreateBonusArchive().Write()),
            ]).Write());
        temp.WriteBaseRomFsFile(
            "bin/message/English/common/itemname.dat",
            SwShItemTestFixtures.CreateItemNames("", "Potion", "Rare Candy"));
        temp.WriteBaseExeFsFile("main", "base-main");
    }

    private static byte[] CreateMalformedShortValuesArchive(SwShNestHoleRewardArchive archive)
    {
        var data = archive.Write();
        var rootTableOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data));
        var tableVectorOffset = ReadTableUOffset(data, rootTableOffset, fieldIndex: 0);
        var tableOffset = ReadVectorTableOffset(data, tableVectorOffset, index: 0);
        var rewardVectorOffset = ReadTableUOffset(data, tableOffset, fieldIndex: 1);
        var rewardOffset = ReadVectorTableOffset(data, rewardVectorOffset, index: 0);
        var valuesVectorOffset = ReadTableUOffset(data, rewardOffset, fieldIndex: 2);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(valuesVectorOffset), 4);
        return data;
    }

    private static int ReadTableUOffset(byte[] data, int tableOffset, int fieldIndex)
    {
        var vtableOffset = checked(
            tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableOffset)));
        var fieldEntryOffset = checked(vtableOffset + (sizeof(ushort) * (2 + fieldIndex)));
        var fieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(fieldEntryOffset));
        Assert.NotEqual((ushort)0, fieldOffset);
        var offsetLocation = checked(tableOffset + fieldOffset);
        return checked(
            offsetLocation + (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offsetLocation)));
    }

    private static int ReadVectorTableOffset(byte[] data, int vectorOffset, int index)
    {
        var offsetLocation = checked(vectorOffset + sizeof(uint) + (index * sizeof(uint)));
        return checked(
            offsetLocation + (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offsetLocation)));
    }
}
