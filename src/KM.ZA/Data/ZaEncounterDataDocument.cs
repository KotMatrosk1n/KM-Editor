// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Formats.ZA.Generated.GameData;

namespace KM.ZA.Data;

internal sealed class ZaEncounterDataDocument
{
    public ZaEncounterDataDocument(IReadOnlyList<ZaEncounterDataGroup> groups)
    {
        Groups = groups;
    }

    public IReadOnlyList<ZaEncounterDataGroup> Groups { get; }

    public IEnumerable<ZaPokemonDataEntry> Entries => Groups
        .SelectMany(group => group.Rows)
        .OfType<ZaEncounterDataEntry>();

    public static ZaEncounterDataDocument Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var table = ZaEncounterDataDbArray.GetRootAsZaEncounterDataDbArray(new ByteBuffer(bytes));
        var groups = new List<ZaEncounterDataGroup>();
        var sourceIndex = 0;
        for (var groupIndex = 0; groupIndex < table.ValuesLength; groupIndex++)
        {
            var db = table.Values(groupIndex);
            if (db is null)
            {
                groups.Add(new ZaEncounterDataGroup([null]));
                continue;
            }

            var rows = new List<ZaEncounterDataEntry?>();
            for (var rowIndex = 0; rowIndex < db.Value.RootLength; rowIndex++)
            {
                var row = db.Value.Root(rowIndex);
                rows.Add(row is null ? null : ZaEncounterDataEntry.From(sourceIndex, row.Value));
                sourceIndex++;
            }

            groups.Add(new ZaEncounterDataGroup(rows));
        }

        return new ZaEncounterDataDocument(groups);
    }

    public byte[] Write()
    {
        var builder = new FlatBufferBuilder(1024);
        var groupOffsets = Groups.Select(group => group.Write(builder)).ToArray();
        var groupsVector = ZaEncounterDataDbArray.CreateValuesVector(builder, groupOffsets);
        var root = ZaEncounterDataDbArray.Create(builder, groupsVector);
        ZaEncounterDataDbArray.FinishBuffer(builder, root);
        return builder.SizedByteArray();
    }
}

internal sealed class ZaEncounterDataGroup
{
    public ZaEncounterDataGroup(IReadOnlyList<ZaEncounterDataEntry?> rows)
    {
        Rows = rows;
    }

    public IReadOnlyList<ZaEncounterDataEntry?> Rows { get; }

    public Offset<ZaEncounterDataDb> Write(FlatBufferBuilder builder)
    {
        var rowOffsets = Rows.Select(row => row?.Write(builder) ?? default).ToArray();
        var rowsVector = ZaEncounterDataDb.CreateRootVector(builder, rowOffsets);
        return ZaEncounterDataDb.Create(builder, rowsVector);
    }
}

internal sealed class ZaEncounterDataEntry : ZaPokemonDataEntry
{
    public ZaPokemonDataStatsRecord? StrengthenValue { get; init; }
    public IReadOnlyList<ZaEncounterItemDropRecord> ItemDrops { get; init; } =
        Array.Empty<ZaEncounterItemDropRecord>();

    public static ZaEncounterDataEntry From(int sourceIndex, ZaEncounterDataRow row)
    {
        return new ZaEncounterDataEntry
        {
            SourceIndex = sourceIndex,
            Id = row.Id,
            DevNo = row.DevNo,
            MinLevel = row.MinLevel,
            MaxLevel = row.MaxLevel,
            Sex = row.Sex,
            FormNo = row.FormNo,
            Rare = row.Rare,
            Tokusei = row.Tokusei,
            Seikaku = row.Seikaku,
            TalentScale = row.TalentScale,
            TalentVNum = row.TalentVNum,
            OyabunProbability = row.OyabunProbability,
            OyabunAdditionalLevel = row.OyabunAdditionalLevel,
            ActivationConditions = ReadActivationConditions(row),
            TalentValue = ReadStats(row.TalentValue),
            StrengthenValue = ReadStats(row.StrengthenValue),
            WazaList = row.WazaList is { } wazaList
                ? new ZaPokemonDataMovesRecord(
                    wazaList.Waza1,
                    wazaList.Waza2,
                    wazaList.Waza3,
                    wazaList.Waza4)
                : null,
            HoldItem = row.HoldItem?.HoldItem,
            ItemDrops = ReadItemDrops(row),
        };
    }

    public new Offset<ZaEncounterDataRow> Write(FlatBufferBuilder builder)
    {
        var idOffset = string.IsNullOrEmpty(Id) ? default : builder.CreateString(Id);
        var activationOffsets = ActivationConditions.Select(condition => condition.Write(builder)).ToArray();
        var activationVector = activationOffsets.Length == 0
            ? default
            : ZaEncounterDataRow.CreateActivationConditionVector(builder, activationOffsets);
        var talentValueOffset = TalentValue?.Write(builder) ?? default;
        var strengthenValueOffset = StrengthenValue?.Write(builder) ?? default;
        var wazaListOffset = WazaList?.Write(builder) ?? default;
        var holdItemOffset = HoldItem is null
            ? default
            : ZaPokemonDataHoldItem.Create(builder, HoldItem.Value);
        var itemDropOffsets = ItemDrops.Select(drop => drop.Write(builder)).ToArray();
        var itemDropVector = itemDropOffsets.Length == 0
            ? default
            : ZaEncounterDataRow.CreateItemDropInfoListVector(builder, itemDropOffsets);

        return ZaEncounterDataRow.Create(
            builder,
            idOffset,
            DevNo,
            MinLevel,
            MaxLevel,
            Sex,
            FormNo,
            Rare,
            Tokusei,
            Seikaku,
            TalentScale,
            TalentVNum,
            OyabunProbability,
            OyabunAdditionalLevel,
            activationVector,
            talentValueOffset,
            strengthenValueOffset,
            wazaListOffset,
            holdItemOffset,
            itemDropVector);
    }

    private static ZaPokemonDataStatsRecord? ReadStats(ZaPokemonDataTalentValue? value)
    {
        return value is { } stats
            ? new ZaPokemonDataStatsRecord(
                stats.Hp,
                stats.Atk,
                stats.Def,
                stats.SpAtk,
                stats.SpDef,
                stats.Agi)
            : null;
    }

    private static IReadOnlyList<ZaPokemonDataActivationConditionRecord> ReadActivationConditions(
        ZaEncounterDataRow row)
    {
        var conditions = new List<ZaPokemonDataActivationConditionRecord>();
        for (var index = 0; index < row.ActivationConditionLength; index++)
        {
            var condition = row.ActivationCondition(index);
            if (condition is not null)
            {
                conditions.Add(ZaPokemonDataActivationConditionRecord.From(condition.Value));
            }
        }

        return conditions;
    }

    private static IReadOnlyList<ZaEncounterItemDropRecord> ReadItemDrops(ZaEncounterDataRow row)
    {
        var drops = new List<ZaEncounterItemDropRecord>();
        for (var index = 0; index < row.ItemDropInfoListLength; index++)
        {
            var drop = row.ItemDropInfoList(index);
            if (drop is null)
            {
                continue;
            }

            var conditions = Enumerable.Range(0, drop.Value.DropConditionListLength)
                .Select(drop.Value.DropConditionList)
                .ToArray();
            drops.Add(new ZaEncounterItemDropRecord(
                drop.Value.ItemTableId,
                conditions,
                drop.Value.DropProbability,
                drop.Value.MinCount,
                drop.Value.MaxCount));
        }

        return drops;
    }
}

internal sealed record ZaEncounterItemDropRecord(
    string? ItemTableId,
    IReadOnlyList<int> DropConditions,
    uint DropProbability,
    uint MinCount,
    uint MaxCount)
{
    public Offset<ZaEncounterItemDropInfo> Write(FlatBufferBuilder builder)
    {
        var itemTableIdOffset = string.IsNullOrEmpty(ItemTableId) ? default : builder.CreateString(ItemTableId);
        var conditions = DropConditions.ToArray();
        var conditionsVector = conditions.Length == 0
            ? default
            : ZaEncounterItemDropInfo.CreateDropConditionListVector(builder, conditions);
        return ZaEncounterItemDropInfo.Create(
            builder,
            itemTableIdOffset,
            conditionsVector,
            DropProbability,
            MinCount,
            MaxCount);
    }
}
