// SPDX-License-Identifier: GPL-3.0-only

using Google.FlatBuffers;
using KM.Formats.ZA.Generated.GameData;

namespace KM.ZA.Data;

internal sealed class ZaPokemonDataDocument
{
    public ZaPokemonDataDocument(IReadOnlyList<ZaPokemonDataGroup> groups)
    {
        Groups = groups;
    }

    public IReadOnlyList<ZaPokemonDataGroup> Groups { get; }

    public IEnumerable<ZaPokemonDataEntry> Entries => Groups
        .SelectMany(group => group.Rows)
        .OfType<ZaPokemonDataEntry>();

    public static ZaPokemonDataDocument Parse(
        byte[] bytes,
        int? maximumTableRecords = null,
        int? maximumNestedRecords = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var table = ZaPokemonDataDbArray.GetRootAsZaPokemonDataDbArray(new ByteBuffer(bytes));
        EnsureBounded(table.ValuesLength, maximumTableRecords, "The Z-A PokemonData group table");
        var groups = new List<ZaPokemonDataGroup>();
        var sourceIndex = 0;
        var nestedBudget = new ZaPokemonDataRecordBudget(maximumNestedRecords);
        for (var groupIndex = 0; groupIndex < table.ValuesLength; groupIndex++)
        {
            var db = table.Values(groupIndex);
            if (db is null)
            {
                groups.Add(new ZaPokemonDataGroup([null]));
                continue;
            }

            var rows = new List<ZaPokemonDataEntry?>();
            EnsureBounded(db.Value.RootLength, maximumTableRecords, "A Z-A PokemonData group");
            nestedBudget.Add(db.Value.RootLength, "The Z-A PokemonData rows");
            for (var rowIndex = 0; rowIndex < db.Value.RootLength; rowIndex++)
            {
                var row = db.Value.Root(rowIndex);
                if (row is not null)
                {
                    nestedBudget.Add(
                        row.Value.ActivationConditionLength,
                        "The Z-A PokemonData activation-condition vectors");
                    nestedBudget.CountActivationConditionDescendants(
                        row.Value,
                        "The Z-A PokemonData activation-condition descendants");
                }

                rows.Add(row is null ? null : ZaPokemonDataEntry.From(sourceIndex, row.Value));
                sourceIndex++;
            }

            groups.Add(new ZaPokemonDataGroup(rows));
        }

        return new ZaPokemonDataDocument(groups);
    }

    private static void EnsureBounded(int count, int? maximum, string label)
    {
        if (maximum is not null && (count < 0 || count > maximum.Value))
        {
            throw new InvalidDataException($"{label} exceeds the bounded semantic record limit.");
        }
    }

    public byte[] Write()
    {
        var builder = new FlatBufferBuilder(1024);
        var groupOffsets = Groups.Select(group => group.Write(builder)).ToArray();
        var groupsVector = ZaPokemonDataDbArray.CreateValuesVector(builder, groupOffsets);
        var root = ZaPokemonDataDbArray.Create(builder, groupsVector);
        ZaPokemonDataDbArray.FinishBuffer(builder, root);
        return builder.SizedByteArray();
    }
}

internal sealed class ZaPokemonDataGroup
{
    public ZaPokemonDataGroup(IReadOnlyList<ZaPokemonDataEntry?> rows)
    {
        Rows = rows;
    }

    public IReadOnlyList<ZaPokemonDataEntry?> Rows { get; }

    public Offset<ZaPokemonDataDb> Write(FlatBufferBuilder builder)
    {
        var rowOffsets = Rows.Select(row => row?.Write(builder) ?? default).ToArray();
        var rowsVector = ZaPokemonDataDb.CreateRootVector(builder, rowOffsets);
        return ZaPokemonDataDb.Create(builder, rowsVector);
    }
}

internal class ZaPokemonDataEntry
{
    public int SourceIndex { get; init; }
    public string? Id { get; init; }
    public int DevNo { get; set; }
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
    public int Sex { get; set; }
    public int FormNo { get; set; }
    public int Rare { get; set; }
    public int Tokusei { get; set; }
    public int Seikaku { get; set; }
    public int TalentScale { get; set; }
    public int TalentVNum { get; set; }
    public float OyabunProbability { get; set; }
    public int OyabunAdditionalLevel { get; set; }
    public IReadOnlyList<ZaPokemonDataActivationConditionRecord> ActivationConditions { get; init; } =
        Array.Empty<ZaPokemonDataActivationConditionRecord>();
    public ZaPokemonDataStatsRecord? TalentValue { get; set; }
    public ZaPokemonDataMovesRecord? WazaList { get; set; }
    public int? HoldItem { get; set; }

    public static ZaPokemonDataEntry From(int sourceIndex, ZaPokemonDataRow row)
    {
        return new ZaPokemonDataEntry
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
            TalentValue = row.TalentValue is { } talentValue
                ? new ZaPokemonDataStatsRecord(
                    talentValue.Hp,
                    talentValue.Atk,
                    talentValue.Def,
                    talentValue.SpAtk,
                    talentValue.SpDef,
                    talentValue.Agi)
                : null,
            WazaList = row.WazaList is { } wazaList
                ? new ZaPokemonDataMovesRecord(
                    wazaList.Waza1,
                    wazaList.Waza2,
                    wazaList.Waza3,
                    wazaList.Waza4)
                : null,
            HoldItem = row.HoldItem?.HoldItem,
        };
    }

    public Offset<ZaPokemonDataRow> Write(FlatBufferBuilder builder)
    {
        var idOffset = string.IsNullOrEmpty(Id) ? default : builder.CreateString(Id);
        var activationOffsets = ActivationConditions.Select(condition => condition.Write(builder)).ToArray();
        var activationVector = activationOffsets.Length == 0
            ? default
            : ZaPokemonDataRow.CreateActivationConditionVector(builder, activationOffsets);
        var talentValueOffset = TalentValue?.Write(builder) ?? default;
        var wazaListOffset = WazaList?.Write(builder) ?? default;
        var holdItemOffset = HoldItem is null
            ? default
            : ZaPokemonDataHoldItem.Create(builder, HoldItem.Value);

        return ZaPokemonDataRow.Create(
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
            wazaListOffset,
            holdItemOffset);
    }

    private static IReadOnlyList<ZaPokemonDataActivationConditionRecord> ReadActivationConditions(
        ZaPokemonDataRow row)
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
}

internal sealed record ZaPokemonDataStatsRecord(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed)
{
    public static readonly ZaPokemonDataStatsRecord Zero = new(0, 0, 0, 0, 0, 0);

    public Offset<ZaPokemonDataTalentValue> Write(FlatBufferBuilder builder)
    {
        return ZaPokemonDataTalentValue.Create(builder, HP, Attack, Defense, SpecialAttack, SpecialDefense, Speed);
    }
}

internal sealed record ZaPokemonDataMovesRecord(
    int Move1,
    int Move2,
    int Move3,
    int Move4)
{
    public IReadOnlyList<int> Values => [Move1, Move2, Move3, Move4];

    public ZaPokemonDataMovesRecord SetMove(int slot, int moveId)
    {
        return slot switch
        {
            0 => this with { Move1 = moveId },
            1 => this with { Move2 = moveId },
            2 => this with { Move3 = moveId },
            3 => this with { Move4 = moveId },
            _ => this,
        };
    }

    public Offset<ZaPokemonDataWazaList> Write(FlatBufferBuilder builder)
    {
        return ZaPokemonDataWazaList.Create(builder, Move1, Move2, Move3, Move4);
    }
}

internal sealed record ZaPokemonDataActivationConditionRecord(
    IReadOnlyList<ZaPokemonDataActivationConditionElementRecord> Elements)
{
    public static ZaPokemonDataActivationConditionRecord From(ZaPokemonDataActivationCondition row)
    {
        var elements = new List<ZaPokemonDataActivationConditionElementRecord>();
        for (var index = 0; index < row.ElementLength; index++)
        {
            var element = row.Element(index);
            if (element is not null)
            {
                elements.Add(ZaPokemonDataActivationConditionElementRecord.From(element.Value));
            }
        }

        return new ZaPokemonDataActivationConditionRecord(elements);
    }

    public Offset<ZaPokemonDataActivationCondition> Write(FlatBufferBuilder builder)
    {
        var elementOffsets = Elements.Select(element => element.Write(builder)).ToArray();
        var elementVector = elementOffsets.Length == 0
            ? default
            : ZaPokemonDataActivationCondition.CreateElementVector(builder, elementOffsets);
        return ZaPokemonDataActivationCondition.Create(builder, elementVector);
    }
}

internal sealed record ZaPokemonDataActivationConditionElementRecord(
    IReadOnlyList<ZaPokemonDataActivationConditionParamRecord> Params)
{
    public static ZaPokemonDataActivationConditionElementRecord From(ZaPokemonDataActivationConditionElement row)
    {
        var parameters = new List<ZaPokemonDataActivationConditionParamRecord>();
        for (var index = 0; index < row.ParamLength; index++)
        {
            var parameter = row.Param(index);
            if (parameter is not null)
            {
                parameters.Add(ZaPokemonDataActivationConditionParamRecord.From(parameter.Value));
            }
        }

        return new ZaPokemonDataActivationConditionElementRecord(parameters);
    }

    public Offset<ZaPokemonDataActivationConditionElement> Write(FlatBufferBuilder builder)
    {
        var paramOffsets = Params.Select(parameter => parameter.Write(builder)).ToArray();
        var paramVector = paramOffsets.Length == 0
            ? default
            : ZaPokemonDataActivationConditionElement.CreateParamVector(builder, paramOffsets);
        return ZaPokemonDataActivationConditionElement.Create(builder, paramVector);
    }
}

internal sealed record ZaPokemonDataActivationConditionParamRecord(
    string? Condition,
    int Op,
    IReadOnlyList<string?> Params)
{
    public static ZaPokemonDataActivationConditionParamRecord From(ZaPokemonDataActivationConditionParam row)
    {
        var parameters = new List<string?>();
        for (var index = 0; index < row.ParamLength; index++)
        {
            parameters.Add(row.Param(index));
        }

        return new ZaPokemonDataActivationConditionParamRecord(row.Condition, row.Op, parameters);
    }

    public Offset<ZaPokemonDataActivationConditionParam> Write(FlatBufferBuilder builder)
    {
        var conditionOffset = string.IsNullOrEmpty(Condition) ? default : builder.CreateString(Condition);
        var paramOffsets = Params
            .Select(parameter => string.IsNullOrEmpty(parameter) ? default : builder.CreateString(parameter))
            .ToArray();
        var paramVector = paramOffsets.Length == 0
            ? default
            : ZaPokemonDataActivationConditionParam.CreateParamVector(builder, paramOffsets);
        return ZaPokemonDataActivationConditionParam.Create(builder, conditionOffset, Op, paramVector);
    }
}

internal sealed class ZaPokemonDataRecordBudget
{
    private readonly int? maximumRecords;
    private int observedRecords;

    public ZaPokemonDataRecordBudget(int? maximumRecords)
    {
        this.maximumRecords = maximumRecords;
    }

    public void Add(int count, string label)
    {
        if (maximumRecords is null)
        {
            return;
        }

        if (count < 0 || count > maximumRecords.Value - observedRecords)
        {
            throw new InvalidDataException($"{label} exceeds the bounded semantic record limit.");
        }

        observedRecords += count;
    }

    public void CountActivationConditionDescendants(
        ZaPokemonDataRow row,
        string label)
    {
        if (maximumRecords is null)
        {
            return;
        }

        for (var conditionIndex = 0; conditionIndex < row.ActivationConditionLength; conditionIndex++)
        {
            CountCondition(row.ActivationCondition(conditionIndex), label);
        }
    }

    public void CountActivationConditionDescendants(
        ZaEncounterDataRow row,
        string label)
    {
        if (maximumRecords is null)
        {
            return;
        }

        for (var conditionIndex = 0; conditionIndex < row.ActivationConditionLength; conditionIndex++)
        {
            CountCondition(row.ActivationCondition(conditionIndex), label);
        }
    }

    private void CountCondition(
        ZaPokemonDataActivationCondition? condition,
        string label)
    {
        if (condition is null)
        {
            return;
        }

        Add(condition.Value.ElementLength, label);
        for (var elementIndex = 0; elementIndex < condition.Value.ElementLength; elementIndex++)
        {
            var element = condition.Value.Element(elementIndex);
            if (element is null)
            {
                continue;
            }

            Add(element.Value.ParamLength, label);
            for (var parameterIndex = 0; parameterIndex < element.Value.ParamLength; parameterIndex++)
            {
                var parameter = element.Value.Param(parameterIndex);
                if (parameter is not null)
                {
                    Add(parameter.Value.ParamLength, label);
                }
            }
        }
    }
}
