// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Google.FlatBuffers;
using KM.Formats.ZA.Generated.TrainerPools;

namespace KM.ZA.Data;

internal sealed class ZaTrainerPoolDataDocument
{
    private const int MaximumGroups = 256;
    private const int MaximumTables = 10_000;
    private const int MaximumAppearances = 250_000;
    private const int MaximumNestedRecords = 1_000_000;

    public ZaTrainerPoolDataDocument(bool hasGroups, IReadOnlyList<ZaTrainerPoolDataGroup?> groups)
    {
        HasGroups = hasGroups;
        Groups = groups;
    }

    public bool HasGroups { get; }
    public IReadOnlyList<ZaTrainerPoolDataGroup?> Groups { get; }
    public IEnumerable<ZaTrainerPoolDataTable> Tables => Groups
        .Where(group => group is not null)
        .SelectMany(group => group!.Tables)
        .Where(table => table is not null)
        .Select(table => table!);

    public static ZaTrainerPoolDataDocument Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < sizeof(int))
        {
            throw new InvalidDataException("The Trainer Pools FlatBuffer is truncated.");
        }

        try
        {
            var buffer = new ByteBuffer(bytes);
            var root = ZaTrainerPoolDatabaseArray.GetRootAsZaTrainerPoolDatabaseArray(buffer);
            EnsureKnownFields(bytes, root.TablePosition, 1, "Trainer Pools root");
            EnsureCount(root.GroupsLength, MaximumGroups, "Trainer Pools database groups");
            var groups = new List<ZaTrainerPoolDataGroup?>(root.GroupsLength);
            var tableCount = 0;
            var appearanceCount = 0;
            var nestedCount = 0;
            for (var groupIndex = 0; groupIndex < root.GroupsLength; groupIndex++)
            {
                var group = root.Groups(groupIndex);
                if (group is null)
                {
                    groups.Add(null);
                    continue;
                }

                EnsureKnownFields(bytes, group.Value.TablePosition, 1, "Trainer Pools database group");
                tableCount = AddBounded(
                    tableCount,
                    group.Value.TablesLength,
                    MaximumTables,
                    "Trainer Pools tables");
                var tables = new List<ZaTrainerPoolDataTable?>(group.Value.TablesLength);
                for (var tableIndex = 0; tableIndex < group.Value.TablesLength; tableIndex++)
                {
                    var sourceTable = group.Value.Tables(tableIndex);
                    if (sourceTable is null)
                    {
                        tables.Add(null);
                        continue;
                    }

                    EnsureKnownFields(bytes, sourceTable.Value.TablePosition, 2, "Trainer Pools table");
                    appearanceCount = AddBounded(
                        appearanceCount,
                        sourceTable.Value.AppearancesLength,
                        MaximumAppearances,
                        "Trainer Pools appearances");
                    var appearances = new List<ZaTrainerPoolDataAppearance?>(
                        sourceTable.Value.AppearancesLength);
                    for (var appearanceIndex = 0;
                         appearanceIndex < sourceTable.Value.AppearancesLength;
                         appearanceIndex++)
                    {
                        var appearance = sourceTable.Value.Appearances(appearanceIndex);
                        if (appearance is null)
                        {
                            appearances.Add(null);
                            continue;
                        }

                        EnsureKnownFields(bytes, appearance.Value.TablePosition, 6, "Trainer Pools appearance");
                        nestedCount = AddBounded(
                            nestedCount,
                            appearance.Value.TagsLength + appearance.Value.ActivationConditionsLength,
                            MaximumNestedRecords,
                            "Trainer Pools nested values");
                        appearances.Add(ReadAppearance(bytes, appearance.Value, ref nestedCount));
                    }

                    tables.Add(new ZaTrainerPoolDataTable(
                        sourceTable.Value.HasId,
                        sourceTable.Value.Id,
                        sourceTable.Value.HasAppearances,
                        appearances));
                }

                groups.Add(new ZaTrainerPoolDataGroup(group.Value.HasTables, tables));
            }

            return new ZaTrainerPoolDataDocument(root.HasGroups, groups);
        }
        catch (Exception exception) when (exception is not InvalidDataException)
        {
            throw new InvalidDataException(
                "The Trainer Pools source is not a supported complete FlatBuffer.",
                exception);
        }
    }

    public ZaTrainerPoolDataDocument Clone()
    {
        return new ZaTrainerPoolDataDocument(
            HasGroups,
            Groups.Select(group => group?.DeepClone()).ToArray());
    }

    public byte[] Write()
    {
        var builder = new FlatBufferBuilder(1024);
        var groupOffsets = Groups
            .Select(group => group?.Write(builder) ?? default)
            .ToArray();
        var groupsVector = HasGroups
            ? CreateOffsetVector(builder, groupOffsets.Select(offset => offset.Value).ToArray())
            : default;
        builder.StartTable(1);
        if (HasGroups)
        {
            builder.AddOffset(0, groupsVector.Value, 0);
        }

        var root = new Offset<ZaTrainerPoolDatabaseArray>(builder.EndTable());
        ZaTrainerPoolDatabaseArray.FinishBuffer(builder, root);
        return builder.SizedByteArray();
    }

    public string CreateSemanticFingerprint(bool includeTrainerIds = true)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "KM.ZA.TrainerPools.Semantic.v1");
        Append(hash, HasGroups);
        Append(hash, Groups.Count);
        foreach (var group in Groups)
        {
            Append(hash, group is not null);
            group?.AppendSemantic(hash, includeTrainerIds);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public int CountTrainerReferenceDifferences(ZaTrainerPoolDataDocument other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Groups.Count != other.Groups.Count)
        {
            return int.MaxValue;
        }

        var differences = 0;
        for (var groupIndex = 0; groupIndex < Groups.Count; groupIndex++)
        {
            var leftGroup = Groups[groupIndex];
            var rightGroup = other.Groups[groupIndex];
            if (leftGroup is null || rightGroup is null || leftGroup.Tables.Count != rightGroup.Tables.Count)
            {
                if (leftGroup is not null || rightGroup is not null)
                {
                    return int.MaxValue;
                }

                continue;
            }

            for (var tableIndex = 0; tableIndex < leftGroup.Tables.Count; tableIndex++)
            {
                var leftTable = leftGroup.Tables[tableIndex];
                var rightTable = rightGroup.Tables[tableIndex];
                if (leftTable is null || rightTable is null
                    || leftTable.Appearances.Count != rightTable.Appearances.Count)
                {
                    if (leftTable is not null || rightTable is not null)
                    {
                        return int.MaxValue;
                    }

                    continue;
                }

                for (var appearanceIndex = 0;
                     appearanceIndex < leftTable.Appearances.Count;
                     appearanceIndex++)
                {
                    var left = leftTable.Appearances[appearanceIndex];
                    var right = rightTable.Appearances[appearanceIndex];
                    if (left is null || right is null)
                    {
                        if (left is not null || right is not null)
                        {
                            return int.MaxValue;
                        }

                        continue;
                    }

                    if (left.HasTrainerId != right.HasTrainerId
                        || !string.Equals(left.TrainerId, right.TrainerId, StringComparison.Ordinal))
                    {
                        differences++;
                    }
                }
            }
        }

        return differences;
    }

    private static ZaTrainerPoolDataAppearance ReadAppearance(
        byte[] bytes,
        ZaTrainerPoolAppearance source,
        ref int nestedCount)
    {
        var tags = Enumerable.Range(0, source.TagsLength)
            .Select(source.Tags)
            .ToArray();
        var conditions = new List<ZaTrainerPoolDataActivationCondition?>(
            source.ActivationConditionsLength);
        for (var conditionIndex = 0;
             conditionIndex < source.ActivationConditionsLength;
             conditionIndex++)
        {
            var condition = source.ActivationConditions(conditionIndex);
            if (condition is null)
            {
                conditions.Add(null);
                continue;
            }

            EnsureKnownFields(bytes, condition.Value.TablePosition, 1, "Trainer Pools activation condition");
            nestedCount = AddBounded(
                nestedCount,
                condition.Value.ElementsLength,
                MaximumNestedRecords,
                "Trainer Pools nested values");
            var elements = new List<ZaTrainerPoolDataActivationElement?>(condition.Value.ElementsLength);
            for (var elementIndex = 0; elementIndex < condition.Value.ElementsLength; elementIndex++)
            {
                var element = condition.Value.Elements(elementIndex);
                if (element is null)
                {
                    elements.Add(null);
                    continue;
                }

                EnsureKnownFields(bytes, element.Value.TablePosition, 1, "Trainer Pools activation element");
                nestedCount = AddBounded(
                    nestedCount,
                    element.Value.ParametersLength,
                    MaximumNestedRecords,
                    "Trainer Pools nested values");
                var parameters = new List<ZaTrainerPoolDataActivationParameter?>(
                    element.Value.ParametersLength);
                for (var parameterIndex = 0;
                     parameterIndex < element.Value.ParametersLength;
                     parameterIndex++)
                {
                    var parameter = element.Value.Parameters(parameterIndex);
                    if (parameter is null)
                    {
                        parameters.Add(null);
                        continue;
                    }

                    EnsureKnownFields(bytes, parameter.Value.TablePosition, 3, "Trainer Pools activation parameter");
                    nestedCount = AddBounded(
                        nestedCount,
                        parameter.Value.ParametersLength,
                        MaximumNestedRecords,
                        "Trainer Pools nested values");
                    parameters.Add(new ZaTrainerPoolDataActivationParameter(
                        parameter.Value.HasCondition,
                        parameter.Value.Condition,
                        parameter.Value.HasOp,
                        parameter.Value.Op,
                        parameter.Value.HasParameters,
                        Enumerable.Range(0, parameter.Value.ParametersLength)
                            .Select(parameter.Value.Parameters)
                            .ToArray()));
                }

                elements.Add(new ZaTrainerPoolDataActivationElement(
                    element.Value.HasParameters,
                    parameters));
            }

            conditions.Add(new ZaTrainerPoolDataActivationCondition(
                condition.Value.HasElements,
                elements));
        }

        ZaTrainerPoolDataAiInfo? ai = null;
        if (source.AiInfo is { } sourceAi)
        {
            EnsureKnownFields(bytes, sourceAi.TablePosition, 6, "Trainer Pools AI info");
            nestedCount = AddBounded(
                nestedCount,
                sourceAi.CreateIgnoreFlagsLength,
                MaximumNestedRecords,
                "Trainer Pools nested values");
            ai = new ZaTrainerPoolDataAiInfo(
                sourceAi.HasActionId,
                sourceAi.ActionId,
                sourceAi.HasPointName,
                sourceAi.PointName,
                sourceAi.HasActorName,
                sourceAi.ActorName,
                sourceAi.HasCreateIgnoreFlags,
                Enumerable.Range(0, sourceAi.CreateIgnoreFlagsLength)
                    .Select(sourceAi.CreateIgnoreFlags)
                    .ToArray(),
                sourceAi.HasHomeRange,
                sourceAi.HomeRange,
                sourceAi.HasPopActionId,
                sourceAi.PopActionId);
        }

        return new ZaTrainerPoolDataAppearance(
            source.HasId,
            source.Id,
            source.HasTrainerId,
            source.TrainerId,
            source.HasTags,
            tags,
            source.HasWeight,
            source.Weight,
            source.HasActivationConditions,
            conditions,
            source.HasAiInfo,
            ai);
    }

    private static void EnsureKnownFields(byte[] bytes, int tablePosition, int expectedFieldCount, string label)
    {
        if (tablePosition < sizeof(int) || tablePosition > bytes.Length - sizeof(int))
        {
            throw new InvalidDataException($"{label} has an invalid table position.");
        }

        var vtableDistance = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(tablePosition, sizeof(int)));
        var vtablePositionLong = (long)tablePosition - vtableDistance;
        if (vtableDistance == 0 || vtablePositionLong < 0 || vtablePositionLong > bytes.Length - 4)
        {
            throw new InvalidDataException($"{label} has an invalid virtual table.");
        }

        var vtablePosition = (int)vtablePositionLong;

        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(vtablePosition, 2));
        if (vtableLength < 4 || (vtableLength & 1) != 0 || vtablePosition > bytes.Length - vtableLength)
        {
            throw new InvalidDataException($"{label} has an invalid virtual-table length.");
        }

        var fieldCount = (vtableLength - 4) / 2;
        if (fieldCount > expectedFieldCount)
        {
            throw new InvalidDataException(
                $"{label} contains fields outside the supported KM Trainer Pools schema; the source was left untouched.");
        }
    }

    private static void EnsureCount(int value, int maximum, string label)
    {
        if (value < 0 || value > maximum)
        {
            throw new InvalidDataException($"{label} exceeds the supported semantic record limit.");
        }
    }

    private static int AddBounded(int current, int value, int maximum, string label)
    {
        EnsureCount(value, maximum, label);
        var total = checked(current + value);
        EnsureCount(total, maximum, label);
        return total;
    }

    internal static VectorOffset CreateOffsetVector(FlatBufferBuilder builder, int[] offsets)
    {
        builder.StartVector(4, offsets.Length, 4);
        for (var index = offsets.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(offsets[index]);
        }

        return builder.EndVector();
    }

    internal static StringOffset CreatePresentString(FlatBufferBuilder builder, bool present, string? value)
    {
        if (!present)
        {
            return default;
        }

        if (value is null)
        {
            throw new InvalidDataException("A materialized Trainer Pools string has no value.");
        }

        return builder.CreateString(value);
    }

    internal static void AddInt(FlatBufferBuilder builder, int slot, int value, bool present)
    {
        if (!present)
        {
            return;
        }

        var original = builder.ForceDefaults;
        builder.ForceDefaults = true;
        builder.AddInt(slot, value, 0);
        builder.ForceDefaults = original;
    }

    internal static void AddFloat(FlatBufferBuilder builder, int slot, float value, bool present)
    {
        if (!present)
        {
            return;
        }

        var original = builder.ForceDefaults;
        builder.ForceDefaults = true;
        builder.AddFloat(slot, value, 0);
        builder.ForceDefaults = original;
    }

    internal static void Append(IncrementalHash hash, string? value)
    {
        if (value is null)
        {
            Append(hash, -1);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        Append(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    internal static void Append(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    internal static void Append(IncrementalHash hash, bool value) => Append(hash, value ? 1 : 0);
    internal static void Append(IncrementalHash hash, float value) => Append(hash, BitConverter.SingleToInt32Bits(value));
}

internal sealed class ZaTrainerPoolDataGroup
{
    public ZaTrainerPoolDataGroup(bool hasTables, IReadOnlyList<ZaTrainerPoolDataTable?> tables)
    {
        HasTables = hasTables;
        Tables = tables;
    }

    public bool HasTables { get; }
    public IReadOnlyList<ZaTrainerPoolDataTable?> Tables { get; }

    public ZaTrainerPoolDataGroup DeepClone() => new(
        HasTables,
        Tables.Select(table => table?.DeepClone()).ToArray());

    public Offset<ZaTrainerPoolDatabaseGroup> Write(FlatBufferBuilder builder)
    {
        var tableOffsets = Tables.Select(table => table?.Write(builder).Value ?? 0).ToArray();
        var tablesVector = HasTables
            ? ZaTrainerPoolDataDocument.CreateOffsetVector(builder, tableOffsets)
            : default;
        builder.StartTable(1);
        if (HasTables)
        {
            builder.AddOffset(0, tablesVector.Value, 0);
        }

        return new Offset<ZaTrainerPoolDatabaseGroup>(builder.EndTable());
    }

    public void AppendSemantic(IncrementalHash hash, bool includeTrainerIds)
    {
        ZaTrainerPoolDataDocument.Append(hash, HasTables);
        ZaTrainerPoolDataDocument.Append(hash, Tables.Count);
        foreach (var table in Tables)
        {
            ZaTrainerPoolDataDocument.Append(hash, table is not null);
            table?.AppendSemantic(hash, includeTrainerIds);
        }
    }
}

internal sealed class ZaTrainerPoolDataTable
{
    public ZaTrainerPoolDataTable(
        bool hasId,
        string? id,
        bool hasAppearances,
        IReadOnlyList<ZaTrainerPoolDataAppearance?> appearances)
    {
        HasId = hasId;
        Id = id;
        HasAppearances = hasAppearances;
        Appearances = appearances;
    }

    public bool HasId { get; }
    public string? Id { get; }
    public bool HasAppearances { get; }
    public IReadOnlyList<ZaTrainerPoolDataAppearance?> Appearances { get; }

    public ZaTrainerPoolDataTable DeepClone() => new(
        HasId,
        Id,
        HasAppearances,
        Appearances.Select(appearance => appearance?.DeepClone()).ToArray());

    public Offset<ZaTrainerPoolTable> Write(FlatBufferBuilder builder)
    {
        var idOffset = ZaTrainerPoolDataDocument.CreatePresentString(builder, HasId, Id);
        var appearanceOffsets = Appearances
            .Select(appearance => appearance?.Write(builder).Value ?? 0)
            .ToArray();
        var appearancesVector = HasAppearances
            ? ZaTrainerPoolDataDocument.CreateOffsetVector(builder, appearanceOffsets)
            : default;
        builder.StartTable(2);
        if (HasAppearances)
        {
            builder.AddOffset(1, appearancesVector.Value, 0);
        }

        if (HasId)
        {
            builder.AddOffset(0, idOffset.Value, 0);
        }

        return new Offset<ZaTrainerPoolTable>(builder.EndTable());
    }

    public void AppendSemantic(IncrementalHash hash, bool includeTrainerIds)
    {
        ZaTrainerPoolDataDocument.Append(hash, HasId);
        ZaTrainerPoolDataDocument.Append(hash, Id);
        ZaTrainerPoolDataDocument.Append(hash, HasAppearances);
        ZaTrainerPoolDataDocument.Append(hash, Appearances.Count);
        foreach (var appearance in Appearances)
        {
            ZaTrainerPoolDataDocument.Append(hash, appearance is not null);
            appearance?.AppendSemantic(hash, includeTrainerIds);
        }
    }
}

internal sealed class ZaTrainerPoolDataAppearance
{
    public ZaTrainerPoolDataAppearance(
        bool hasId,
        string? id,
        bool hasTrainerId,
        string? trainerId,
        bool hasTags,
        IReadOnlyList<string?> tags,
        bool hasWeight,
        int weight,
        bool hasActivationConditions,
        IReadOnlyList<ZaTrainerPoolDataActivationCondition?> activationConditions,
        bool hasAiInfo,
        ZaTrainerPoolDataAiInfo? aiInfo)
    {
        HasId = hasId;
        Id = id;
        HasTrainerId = hasTrainerId;
        TrainerId = trainerId;
        HasTags = hasTags;
        Tags = tags;
        HasWeight = hasWeight;
        Weight = weight;
        HasActivationConditions = hasActivationConditions;
        ActivationConditions = activationConditions;
        HasAiInfo = hasAiInfo;
        AiInfo = aiInfo;
    }

    public bool HasId { get; }
    public string? Id { get; }
    public bool HasTrainerId { get; }
    public string? TrainerId { get; set; }
    public bool HasTags { get; }
    public IReadOnlyList<string?> Tags { get; }
    public bool HasWeight { get; }
    public int Weight { get; }
    public bool HasActivationConditions { get; }
    public IReadOnlyList<ZaTrainerPoolDataActivationCondition?> ActivationConditions { get; }
    public bool HasAiInfo { get; }
    public ZaTrainerPoolDataAiInfo? AiInfo { get; }

    public ZaTrainerPoolDataAppearance DeepClone() => new(
        HasId,
        Id,
        HasTrainerId,
        TrainerId,
        HasTags,
        Tags.ToArray(),
        HasWeight,
        Weight,
        HasActivationConditions,
        ActivationConditions.Select(condition => condition?.DeepClone()).ToArray(),
        HasAiInfo,
        AiInfo?.DeepClone());

    public Offset<ZaTrainerPoolAppearance> Write(FlatBufferBuilder builder)
    {
        var idOffset = ZaTrainerPoolDataDocument.CreatePresentString(builder, HasId, Id);
        var trainerIdOffset = ZaTrainerPoolDataDocument.CreatePresentString(builder, HasTrainerId, TrainerId);
        var tagOffsets = Tags
            .Select(tag => tag is null ? default : builder.CreateString(tag))
            .Select(offset => offset.Value)
            .ToArray();
        var tagsVector = HasTags
            ? ZaTrainerPoolDataDocument.CreateOffsetVector(builder, tagOffsets)
            : default;
        var conditionOffsets = ActivationConditions
            .Select(condition => condition?.Write(builder).Value ?? 0)
            .ToArray();
        var conditionsVector = HasActivationConditions
            ? ZaTrainerPoolDataDocument.CreateOffsetVector(builder, conditionOffsets)
            : default;
        var aiOffset = AiInfo?.Write(builder) ?? default;

        builder.StartTable(6);
        if (HasAiInfo)
        {
            if (AiInfo is null)
            {
                throw new InvalidDataException("A materialized Trainer Pools AI table is null.");
            }

            builder.AddOffset(5, aiOffset.Value, 0);
        }

        if (HasActivationConditions)
        {
            builder.AddOffset(4, conditionsVector.Value, 0);
        }

        ZaTrainerPoolDataDocument.AddInt(builder, 3, Weight, HasWeight);
        if (HasTags)
        {
            builder.AddOffset(2, tagsVector.Value, 0);
        }

        if (HasTrainerId)
        {
            builder.AddOffset(1, trainerIdOffset.Value, 0);
        }

        if (HasId)
        {
            builder.AddOffset(0, idOffset.Value, 0);
        }

        return new Offset<ZaTrainerPoolAppearance>(builder.EndTable());
    }

    public void AppendSemantic(IncrementalHash hash, bool includeTrainerIds)
    {
        ZaTrainerPoolDataDocument.Append(hash, HasId);
        ZaTrainerPoolDataDocument.Append(hash, Id);
        ZaTrainerPoolDataDocument.Append(hash, HasTrainerId);
        if (includeTrainerIds)
        {
            ZaTrainerPoolDataDocument.Append(hash, TrainerId);
        }

        ZaTrainerPoolDataDocument.Append(hash, HasTags);
        ZaTrainerPoolDataDocument.Append(hash, Tags.Count);
        foreach (var tag in Tags)
        {
            ZaTrainerPoolDataDocument.Append(hash, tag);
        }

        ZaTrainerPoolDataDocument.Append(hash, HasWeight);
        ZaTrainerPoolDataDocument.Append(hash, Weight);
        ZaTrainerPoolDataDocument.Append(hash, HasActivationConditions);
        ZaTrainerPoolDataDocument.Append(hash, ActivationConditions.Count);
        foreach (var condition in ActivationConditions)
        {
            ZaTrainerPoolDataDocument.Append(hash, condition is not null);
            condition?.AppendSemantic(hash);
        }

        ZaTrainerPoolDataDocument.Append(hash, HasAiInfo);
        ZaTrainerPoolDataDocument.Append(hash, AiInfo is not null);
        AiInfo?.AppendSemantic(hash);
    }
}

internal sealed record ZaTrainerPoolDataActivationCondition(
    bool HasElements,
    IReadOnlyList<ZaTrainerPoolDataActivationElement?> Elements)
{
    public ZaTrainerPoolDataActivationCondition DeepClone() => new(
        HasElements,
        Elements.Select(element => element?.DeepClone()).ToArray());

    public Offset<ZaTrainerPoolActivationCondition> Write(FlatBufferBuilder builder)
    {
        var offsets = Elements.Select(element => element?.Write(builder).Value ?? 0).ToArray();
        var vector = HasElements
            ? ZaTrainerPoolDataDocument.CreateOffsetVector(builder, offsets)
            : default;
        builder.StartTable(1);
        if (HasElements)
        {
            builder.AddOffset(0, vector.Value, 0);
        }

        return new Offset<ZaTrainerPoolActivationCondition>(builder.EndTable());
    }

    public void AppendSemantic(IncrementalHash hash)
    {
        ZaTrainerPoolDataDocument.Append(hash, HasElements);
        ZaTrainerPoolDataDocument.Append(hash, Elements.Count);
        foreach (var element in Elements)
        {
            ZaTrainerPoolDataDocument.Append(hash, element is not null);
            element?.AppendSemantic(hash);
        }
    }
}

internal sealed record ZaTrainerPoolDataActivationElement(
    bool HasParameters,
    IReadOnlyList<ZaTrainerPoolDataActivationParameter?> Parameters)
{
    public ZaTrainerPoolDataActivationElement DeepClone() => new(
        HasParameters,
        Parameters.Select(parameter => parameter?.DeepClone()).ToArray());

    public Offset<ZaTrainerPoolActivationElement> Write(FlatBufferBuilder builder)
    {
        var offsets = Parameters.Select(parameter => parameter?.Write(builder).Value ?? 0).ToArray();
        var vector = HasParameters
            ? ZaTrainerPoolDataDocument.CreateOffsetVector(builder, offsets)
            : default;
        builder.StartTable(1);
        if (HasParameters)
        {
            builder.AddOffset(0, vector.Value, 0);
        }

        return new Offset<ZaTrainerPoolActivationElement>(builder.EndTable());
    }

    public void AppendSemantic(IncrementalHash hash)
    {
        ZaTrainerPoolDataDocument.Append(hash, HasParameters);
        ZaTrainerPoolDataDocument.Append(hash, Parameters.Count);
        foreach (var parameter in Parameters)
        {
            ZaTrainerPoolDataDocument.Append(hash, parameter is not null);
            parameter?.AppendSemantic(hash);
        }
    }
}

internal sealed record ZaTrainerPoolDataActivationParameter(
    bool HasCondition,
    string? Condition,
    bool HasOp,
    int Op,
    bool HasParameters,
    IReadOnlyList<string?> Parameters)
{
    public ZaTrainerPoolDataActivationParameter DeepClone() => this with { Parameters = Parameters.ToArray() };

    public Offset<ZaTrainerPoolActivationParameter> Write(FlatBufferBuilder builder)
    {
        var conditionOffset = ZaTrainerPoolDataDocument.CreatePresentString(builder, HasCondition, Condition);
        var parameterOffsets = Parameters
            .Select(parameter => parameter is null ? default : builder.CreateString(parameter))
            .Select(offset => offset.Value)
            .ToArray();
        var parametersVector = HasParameters
            ? ZaTrainerPoolDataDocument.CreateOffsetVector(builder, parameterOffsets)
            : default;
        builder.StartTable(3);
        if (HasParameters)
        {
            builder.AddOffset(2, parametersVector.Value, 0);
        }

        ZaTrainerPoolDataDocument.AddInt(builder, 1, Op, HasOp);
        if (HasCondition)
        {
            builder.AddOffset(0, conditionOffset.Value, 0);
        }

        return new Offset<ZaTrainerPoolActivationParameter>(builder.EndTable());
    }

    public void AppendSemantic(IncrementalHash hash)
    {
        ZaTrainerPoolDataDocument.Append(hash, HasCondition);
        ZaTrainerPoolDataDocument.Append(hash, Condition);
        ZaTrainerPoolDataDocument.Append(hash, HasOp);
        ZaTrainerPoolDataDocument.Append(hash, Op);
        ZaTrainerPoolDataDocument.Append(hash, HasParameters);
        ZaTrainerPoolDataDocument.Append(hash, Parameters.Count);
        foreach (var parameter in Parameters)
        {
            ZaTrainerPoolDataDocument.Append(hash, parameter);
        }
    }
}

internal sealed record ZaTrainerPoolDataAiInfo(
    bool HasActionId,
    int ActionId,
    bool HasPointName,
    string? PointName,
    bool HasActorName,
    string? ActorName,
    bool HasCreateIgnoreFlags,
    IReadOnlyList<int> CreateIgnoreFlags,
    bool HasHomeRange,
    float HomeRange,
    bool HasPopActionId,
    int PopActionId)
{
    public ZaTrainerPoolDataAiInfo DeepClone() => this with { CreateIgnoreFlags = CreateIgnoreFlags.ToArray() };

    public Offset<ZaTrainerPoolAiInfo> Write(FlatBufferBuilder builder)
    {
        var pointNameOffset = ZaTrainerPoolDataDocument.CreatePresentString(builder, HasPointName, PointName);
        var actorNameOffset = ZaTrainerPoolDataDocument.CreatePresentString(builder, HasActorName, ActorName);
        VectorOffset flagsVector = default;
        if (HasCreateIgnoreFlags)
        {
            builder.StartVector(4, CreateIgnoreFlags.Count, 4);
            for (var index = CreateIgnoreFlags.Count - 1; index >= 0; index--)
            {
                builder.AddInt(CreateIgnoreFlags[index]);
            }

            flagsVector = builder.EndVector();
        }

        builder.StartTable(6);
        ZaTrainerPoolDataDocument.AddInt(builder, 5, PopActionId, HasPopActionId);
        ZaTrainerPoolDataDocument.AddFloat(builder, 4, HomeRange, HasHomeRange);
        if (HasCreateIgnoreFlags)
        {
            builder.AddOffset(3, flagsVector.Value, 0);
        }

        if (HasActorName)
        {
            builder.AddOffset(2, actorNameOffset.Value, 0);
        }

        if (HasPointName)
        {
            builder.AddOffset(1, pointNameOffset.Value, 0);
        }

        ZaTrainerPoolDataDocument.AddInt(builder, 0, ActionId, HasActionId);
        return new Offset<ZaTrainerPoolAiInfo>(builder.EndTable());
    }

    public void AppendSemantic(IncrementalHash hash)
    {
        ZaTrainerPoolDataDocument.Append(hash, HasActionId);
        ZaTrainerPoolDataDocument.Append(hash, ActionId);
        ZaTrainerPoolDataDocument.Append(hash, HasPointName);
        ZaTrainerPoolDataDocument.Append(hash, PointName);
        ZaTrainerPoolDataDocument.Append(hash, HasActorName);
        ZaTrainerPoolDataDocument.Append(hash, ActorName);
        ZaTrainerPoolDataDocument.Append(hash, HasCreateIgnoreFlags);
        ZaTrainerPoolDataDocument.Append(hash, CreateIgnoreFlags.Count);
        foreach (var flag in CreateIgnoreFlags)
        {
            ZaTrainerPoolDataDocument.Append(hash, flag);
        }

        ZaTrainerPoolDataDocument.Append(hash, HasHomeRange);
        ZaTrainerPoolDataDocument.Append(hash, HomeRange);
        ZaTrainerPoolDataDocument.Append(hash, HasPopActionId);
        ZaTrainerPoolDataDocument.Append(hash, PopActionId);
    }
}
