// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace KM.Formats.SwSh;

public sealed record SwShNestHoleReward(
    uint EntryId,
    uint ItemId,
    IReadOnlyList<uint> Values);

public sealed record SwShNestHoleRewardTable(
    ulong TableId,
    IReadOnlyList<SwShNestHoleReward> Rewards);

public enum SwShNestHoleRewardField
{
    ItemId,
    Star1Value,
    Star2Value,
    Star3Value,
    Star4Value,
    Star5Value,
}

public sealed record SwShNestHoleRewardEdit(
    int TableIndex,
    int RewardIndex,
    SwShNestHoleRewardField Field,
    uint Value);

public sealed record SwShNestHoleRewardArchive(IReadOnlyList<SwShNestHoleRewardTable> Tables)
{
    [Obsolete("This is an editor policy, not a reward archive format limit. Use the Sword and Shield workflow policy instead.")]
    public const uint MaximumDropValue = 100;

    [Obsolete("This is an editor policy, not a reward archive format limit. Use the Sword and Shield workflow policy instead.")]
    public const uint MaximumBonusQuantity = 999;

    private const int MinimumRewardValueCount = 5;

    private byte[]? SourceData { get; init; }

    private IReadOnlyList<SourceRewardTableLayout>? SourceTableLayouts { get; init; }

    private IReadOnlyList<SwShNestHoleRewardTable>? SourceTables { get; init; }

    public static SwShNestHoleRewardArchive Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("Nest hole reward archive is too small to contain a FlatBuffer root.");
        }

        var ranges = new StructuralRangeRegistry();
        ranges.Register(offset: 0, sizeof(uint), "root pointer", "root pointer", allowExactAlias: false);
        var rootTableOffset = ReadUOffset(data, offset: 0, targetAlignment: sizeof(uint));
        var rootTable = ReadTableLayout(
            data,
            rootTableOffset,
            "root table",
            "root table",
            alignment: sizeof(uint),
            ranges,
            [new FieldLayout(0, sizeof(uint), sizeof(uint))]);
        var tableVectorOffset = ReadTableUOffset(
            data,
            rootTable,
            fieldIndex: 0,
            required: true,
            targetAlignment: sizeof(uint));
        var tableCount = ReadAndRegisterVector(
            data,
            tableVectorOffset,
            elementSize: sizeof(uint),
            "reward table vector",
            "reward table vector",
            ranges);
        var tables = new SwShNestHoleRewardTable[tableCount];
        var tableLayouts = new SourceRewardTableLayout[tableCount];

        for (var tableIndex = 0; tableIndex < tableCount; tableIndex++)
        {
            var tableVectorElementOffset = checked(
                tableVectorOffset + sizeof(uint) + (tableIndex * sizeof(uint)));
            var tableOffset = ReadUOffset(data, tableVectorElementOffset, targetAlignment: sizeof(uint));
            var table = ReadTableLayout(
                data,
                tableOffset,
                $"reward table {tableIndex}",
                "reward table",
                alignment: sizeof(uint),
                ranges,
                [
                    new FieldLayout(0, sizeof(ulong), sizeof(ulong)),
                    new FieldLayout(1, sizeof(uint), sizeof(uint)),
                ]);
            var rewardsVectorOffset = ReadTableUOffset(
                data,
                table,
                fieldIndex: 1,
                required: true,
                targetAlignment: sizeof(uint));
            var rewardCount = ReadAndRegisterVector(
                data,
                rewardsVectorOffset,
                elementSize: sizeof(uint),
                $"reward table {tableIndex} row vector",
                "reward row vector",
                ranges);
            var rewards = new SwShNestHoleReward[rewardCount];
            var rewardLayouts = new SourceRewardLayout[rewardCount];

            for (var rewardIndex = 0; rewardIndex < rewardCount; rewardIndex++)
            {
                var rewardVectorElementOffset = checked(
                    rewardsVectorOffset + sizeof(uint) + (rewardIndex * sizeof(uint)));
                var rewardOffset = ReadUOffset(data, rewardVectorElementOffset, targetAlignment: sizeof(uint));
                var rewardTable = ReadTableLayout(
                    data,
                    rewardOffset,
                    $"reward table {tableIndex} row {rewardIndex}",
                    "reward row table",
                    alignment: sizeof(uint),
                    ranges,
                    [
                        new FieldLayout(0, sizeof(uint), sizeof(uint)),
                        new FieldLayout(1, sizeof(uint), sizeof(uint)),
                        new FieldLayout(2, sizeof(uint), sizeof(uint)),
                    ]);
                var valuesVectorOffset = ReadTableUOffset(
                    data,
                    rewardTable,
                    fieldIndex: 2,
                    required: true,
                    targetAlignment: sizeof(uint));
                var valueCount = ReadAndRegisterVector(
                    data,
                    valuesVectorOffset,
                    elementSize: sizeof(uint),
                    $"reward table {tableIndex} row {rewardIndex} values",
                    "reward values vector",
                    ranges);
                if (valueCount < MinimumRewardValueCount)
                {
                    throw new InvalidDataException(
                        $"Raid reward table {tableIndex} row {rewardIndex} contains {valueCount} star values; at least {MinimumRewardValueCount} are required.");
                }

                var values = new uint[valueCount];
                for (var valueIndex = 0; valueIndex < values.Length; valueIndex++)
                {
                    values[valueIndex] = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(
                        valuesVectorOffset + sizeof(uint) + (valueIndex * sizeof(uint)),
                        sizeof(uint)));
                }

                var itemFieldOffset = ReadTableFieldOffset(rewardTable, fieldIndex: 1);
                rewards[rewardIndex] = new SwShNestHoleReward(
                    ReadTableUInt32(data, rewardTable, fieldIndex: 0),
                    ReadTableUInt32(data, rewardTable, fieldIndex: 1),
                    values);
                rewardLayouts[rewardIndex] = new SourceRewardLayout(
                    rewardVectorElementOffset,
                    rewardOffset,
                    itemFieldOffset == 0 ? -1 : checked(rewardOffset + itemFieldOffset),
                    checked(rewardOffset + ReadRequiredTableFieldOffset(rewardTable, fieldIndex: 2)),
                    valuesVectorOffset,
                    rewardTable.VtableLength,
                    rewardTable.FieldOffsets.Skip(3).Any(fieldOffset => fieldOffset != 0));
            }

            tables[tableIndex] = new SwShNestHoleRewardTable(
                ReadTableUInt64(data, table, fieldIndex: 0),
                rewards);
            tableLayouts[tableIndex] = new SourceRewardTableLayout(
                tableVectorElementOffset,
                tableOffset,
                rewardsVectorOffset,
                rewardLayouts);
        }

        return new SwShNestHoleRewardArchive(tables)
        {
            SourceData = data.ToArray(),
            SourceTableLayouts = tableLayouts,
            SourceTables = CloneTables(tables),
        };
    }

    public byte[] Write()
    {
        if (SourceData is not null && SourceTables is not null && TablesEqual(Tables, SourceTables))
        {
            return SourceData.ToArray();
        }

        var writer = new RewardFlatBufferWriter();
        writer.Write(this);

        return writer.ToArray();
    }

    public byte[] WriteEdits(IEnumerable<SwShNestHoleRewardEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var materializedEdits = edits.ToArray();
        if (SourceData is not null
            && SourceTableLayouts is not null
            && SourceTables is not null
            && TablesEqual(Tables, SourceTables))
        {
            return WriteSourceEdits(materializedEdits);
        }

        var tables = CloneTables(Tables);

        foreach (var edit in materializedEdits)
        {
            ApplyEdit(tables, edit);
        }

        return new SwShNestHoleRewardArchive(tables).Write();
    }

    private static SwShNestHoleRewardTable[] CloneTables(
        IReadOnlyList<SwShNestHoleRewardTable> source)
    {
        return source
            .Select(table => table with
            {
                Rewards = table.Rewards
                    .Select(reward => reward with { Values = reward.Values.ToArray() })
                    .ToArray(),
            })
            .ToArray();
    }

    private static bool TablesEqual(
        IReadOnlyList<SwShNestHoleRewardTable> current,
        IReadOnlyList<SwShNestHoleRewardTable> source)
    {
        if (current.Count != source.Count)
        {
            return false;
        }

        for (var tableIndex = 0; tableIndex < current.Count; tableIndex++)
        {
            var currentTable = current[tableIndex];
            var sourceTable = source[tableIndex];
            if (currentTable.TableId != sourceTable.TableId
                || currentTable.Rewards.Count != sourceTable.Rewards.Count)
            {
                return false;
            }

            for (var rewardIndex = 0; rewardIndex < currentTable.Rewards.Count; rewardIndex++)
            {
                var currentReward = currentTable.Rewards[rewardIndex];
                var sourceReward = sourceTable.Rewards[rewardIndex];
                if (currentReward.EntryId != sourceReward.EntryId
                    || currentReward.ItemId != sourceReward.ItemId
                    || !currentReward.Values.SequenceEqual(sourceReward.Values))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private byte[] WriteSourceEdits(IReadOnlyList<SwShNestHoleRewardEdit> edits)
    {
        var source = SourceData
            ?? throw new InvalidDataException("Raid reward source bytes are unavailable.");
        var layouts = SourceTableLayouts
            ?? throw new InvalidDataException("Raid reward source layout is unavailable.");
        if (layouts.Count != Tables.Count)
        {
            throw new InvalidDataException("Raid reward source layout does not match the parsed table count.");
        }

        var finalValues = new Dictionary<RewardEditKey, uint>();
        foreach (var edit in edits)
        {
            ValidateEditTarget(edit);
            finalValues[new RewardEditKey(edit.TableIndex, edit.RewardIndex, edit.Field)] = edit.Value;
        }

        var effectiveEdits = finalValues
            .Where(pair => ReadLogicalValue(pair.Key) != pair.Value)
            .OrderBy(pair => pair.Key.TableIndex)
            .ThenBy(pair => pair.Key.RewardIndex)
            .ThenBy(pair => pair.Key.Field)
            .ToArray();
        if (effectiveEdits.Length == 0)
        {
            return source.ToArray();
        }

        var output = new List<byte>(source.Length);
        output.AddRange(source);
        var effectiveTables = layouts
            .Select(layout => new EffectiveRewardTableLayout(layout, delta: 0, isolated: false))
            .ToArray();

        foreach (var tableEdits in effectiveEdits.GroupBy(pair => pair.Key.TableIndex))
        {
            var tableIndex = tableEdits.Key;
            var effectiveTable = effectiveTables[tableIndex];
            if (IsTableAliased(layouts, tableIndex))
            {
                var tableDelta = AppendSourceCopy(output, source);
                PatchUOffset(
                    output,
                    layouts[tableIndex].TableVectorElementOffset,
                    checked(layouts[tableIndex].TableOffset + tableDelta));
                effectiveTable = new EffectiveRewardTableLayout(
                    layouts[tableIndex],
                    tableDelta,
                    isolated: true);
                effectiveTables[tableIndex] = effectiveTable;
            }

            foreach (var rewardEdits in tableEdits.GroupBy(pair => pair.Key.RewardIndex))
            {
                var rewardIndex = rewardEdits.Key;
                var effectiveReward = effectiveTable.Rewards[rewardIndex];
                var sourceReward = layouts[tableIndex].Rewards[rewardIndex];
                var materializeItemField = effectiveReward.ItemValueOffset < 0
                    && rewardEdits.Any(pair => pair.Key.Field == SwShNestHoleRewardField.ItemId);
                if (materializeItemField)
                {
                    if (sourceReward.HasMaterializedUnknownFields)
                    {
                        throw new InvalidDataException(
                            "Raid reward item ID is omitted from a table with unknown materialized fields and cannot be materialized safely.");
                    }

                    effectiveReward = AppendMaterializedReward(
                        output,
                        effectiveReward.RewardVectorElementOffset,
                        sourceReward.VtableLength,
                        Tables[tableIndex].Rewards[rewardIndex]);
                    effectiveTable.Rewards[rewardIndex] = effectiveReward;
                }
                else if (IsRewardAliased(layouts, tableIndex, rewardIndex, effectiveTable.Isolated))
                {
                    var rewardDelta = AppendSourceCopy(output, source);
                    PatchUOffset(
                        output,
                        effectiveReward.RewardVectorElementOffset,
                        checked(sourceReward.RewardOffset + rewardDelta));
                    effectiveReward = new EffectiveRewardLayout(
                        sourceReward,
                        rewardDelta,
                        isolated: true);
                    effectiveTable.Rewards[rewardIndex] = effectiveReward;
                }

                if (!effectiveReward.Isolated
                    && rewardEdits.Any(pair => IsValueField(pair.Key.Field))
                    && IsValuesVectorAliased(layouts, tableIndex, rewardIndex, effectiveTable.Isolated))
                {
                    var valuesDelta = AppendSourceCopy(output, source);
                    PatchUOffset(
                        output,
                        effectiveReward.ValuesFieldOffset,
                        checked(sourceReward.ValuesVectorOffset + valuesDelta));
                    effectiveReward.ValuesVectorOffset = checked(sourceReward.ValuesVectorOffset + valuesDelta);
                }

                foreach (var edit in rewardEdits)
                {
                    PatchSourceEdit(output, effectiveReward, edit.Key.Field, edit.Value);
                }
            }
        }

        return output.ToArray();
    }

    private static EffectiveRewardLayout AppendMaterializedReward(
        List<byte> output,
        int rewardVectorElementOffset,
        int sourceVtableLength,
        SwShNestHoleReward reward)
    {
        var vtableLength = Math.Max(sourceVtableLength, 10);
        while (((output.Count + vtableLength) % sizeof(uint)) != 0)
        {
            output.Add(0);
        }

        var vtableOffset = output.Count;
        var vtable = new byte[vtableLength];
        BinaryPrimitives.WriteUInt16LittleEndian(vtable.AsSpan(0, sizeof(ushort)), checked((ushort)vtableLength));
        BinaryPrimitives.WriteUInt16LittleEndian(vtable.AsSpan(2, sizeof(ushort)), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(vtable.AsSpan(4, sizeof(ushort)), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(vtable.AsSpan(6, sizeof(ushort)), 12);
        BinaryPrimitives.WriteUInt16LittleEndian(vtable.AsSpan(8, sizeof(ushort)), 4);
        output.AddRange(vtable);

        var tableOffset = output.Count;
        var table = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(
            table.AsSpan(0, sizeof(int)),
            checked(tableOffset - vtableOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(8, sizeof(uint)), reward.EntryId);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(12, sizeof(uint)), reward.ItemId);
        output.AddRange(table);

        var valuesVectorOffset = output.Count;
        var valuesVector = new byte[checked(sizeof(uint) + (reward.Values.Count * sizeof(uint)))];
        BinaryPrimitives.WriteUInt32LittleEndian(
            valuesVector.AsSpan(0, sizeof(uint)),
            checked((uint)reward.Values.Count));
        for (var valueIndex = 0; valueIndex < reward.Values.Count; valueIndex++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                valuesVector.AsSpan(sizeof(uint) + (valueIndex * sizeof(uint)), sizeof(uint)),
                reward.Values[valueIndex]);
        }

        output.AddRange(valuesVector);
        var valuesFieldOffset = tableOffset + sizeof(int);
        PatchUOffset(output, valuesFieldOffset, valuesVectorOffset);
        PatchUOffset(output, rewardVectorElementOffset, tableOffset);

        return new EffectiveRewardLayout(
            rewardVectorElementOffset,
            itemValueOffset: tableOffset + 12,
            valuesFieldOffset,
            valuesVectorOffset);
    }

    private void ValidateEditTarget(SwShNestHoleRewardEdit edit)
    {
        if ((uint)edit.TableIndex >= (uint)Tables.Count)
        {
            throw new InvalidDataException($"Raid reward table index {edit.TableIndex} is not present.");
        }

        if ((uint)edit.RewardIndex >= (uint)Tables[edit.TableIndex].Rewards.Count)
        {
            throw new InvalidDataException($"Raid reward index {edit.RewardIndex} is not present.");
        }

        if (!Enum.IsDefined(edit.Field))
        {
            throw new ArgumentOutOfRangeException(nameof(edit), $"Raid reward field '{edit.Field}' is not supported.");
        }
    }

    private uint ReadLogicalValue(RewardEditKey key)
    {
        var reward = Tables[key.TableIndex].Rewards[key.RewardIndex];
        return key.Field switch
        {
            SwShNestHoleRewardField.ItemId => reward.ItemId,
            SwShNestHoleRewardField.Star1Value => reward.Values[0],
            SwShNestHoleRewardField.Star2Value => reward.Values[1],
            SwShNestHoleRewardField.Star3Value => reward.Values[2],
            SwShNestHoleRewardField.Star4Value => reward.Values[3],
            SwShNestHoleRewardField.Star5Value => reward.Values[4],
            _ => throw new ArgumentOutOfRangeException(nameof(key), $"Raid reward field '{key.Field}' is not supported."),
        };
    }

    private static void PatchSourceEdit(
        List<byte> output,
        EffectiveRewardLayout reward,
        SwShNestHoleRewardField field,
        uint value)
    {
        if (field == SwShNestHoleRewardField.ItemId)
        {
            if (reward.ItemValueOffset < 0)
            {
                throw new InvalidDataException(
                    "Raid reward item ID is omitted from the source table and cannot be materialized safely.");
            }

            WriteUInt32At(output, reward.ItemValueOffset, value);
            return;
        }

        var valueIndex = FieldToValueIndex(field);
        WriteUInt32At(
            output,
            checked(reward.ValuesVectorOffset + sizeof(uint) + (valueIndex * sizeof(uint))),
            value);
    }

    private static int FieldToValueIndex(SwShNestHoleRewardField field)
    {
        return field switch
        {
            SwShNestHoleRewardField.Star1Value => 0,
            SwShNestHoleRewardField.Star2Value => 1,
            SwShNestHoleRewardField.Star3Value => 2,
            SwShNestHoleRewardField.Star4Value => 3,
            SwShNestHoleRewardField.Star5Value => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(field), $"Raid reward field '{field}' is not a star value."),
        };
    }

    private static bool IsValueField(SwShNestHoleRewardField field)
    {
        return field is
            SwShNestHoleRewardField.Star1Value or
            SwShNestHoleRewardField.Star2Value or
            SwShNestHoleRewardField.Star3Value or
            SwShNestHoleRewardField.Star4Value or
            SwShNestHoleRewardField.Star5Value;
    }

    private static bool IsTableAliased(IReadOnlyList<SourceRewardTableLayout> layouts, int tableIndex)
    {
        var target = layouts[tableIndex];
        return layouts.Where((_, index) => index != tableIndex).Any(candidate =>
            candidate.TableOffset == target.TableOffset
            || candidate.RewardsVectorOffset == target.RewardsVectorOffset);
    }

    private static bool IsRewardAliased(
        IReadOnlyList<SourceRewardTableLayout> layouts,
        int tableIndex,
        int rewardIndex,
        bool tableIsolated)
    {
        var targetOffset = layouts[tableIndex].Rewards[rewardIndex].RewardOffset;
        var candidates = tableIsolated
            ? layouts[tableIndex].Rewards
            : layouts.SelectMany(layout => layout.Rewards);
        return candidates.Count(candidate => candidate.RewardOffset == targetOffset) > 1;
    }

    private static bool IsValuesVectorAliased(
        IReadOnlyList<SourceRewardTableLayout> layouts,
        int tableIndex,
        int rewardIndex,
        bool tableIsolated)
    {
        var targetOffset = layouts[tableIndex].Rewards[rewardIndex].ValuesVectorOffset;
        var candidates = tableIsolated
            ? layouts[tableIndex].Rewards
            : layouts.SelectMany(layout => layout.Rewards);
        return candidates.Count(candidate => candidate.ValuesVectorOffset == targetOffset) > 1;
    }

    private static int AppendSourceCopy(List<byte> output, byte[] source)
    {
        while ((output.Count % sizeof(ulong)) != 0)
        {
            output.Add(0);
        }

        var delta = output.Count;
        output.AddRange(source);
        return delta;
    }

    private static void PatchUOffset(List<byte> output, int sourceOffset, int targetOffset)
    {
        if (targetOffset <= sourceOffset)
        {
            throw new InvalidDataException("FlatBuffer copy-on-write target must point forward.");
        }

        var relativeOffset = checked((uint)(targetOffset - sourceOffset));
        WriteUInt32At(output, sourceOffset, relativeOffset);
    }

    private static void WriteUInt32At(List<byte> output, int offset, uint value)
    {
        if (offset < 0 || offset > output.Count - sizeof(uint))
        {
            throw new InvalidDataException("FlatBuffer copy-on-write patch points outside the output.");
        }

        BinaryPrimitives.WriteUInt32LittleEndian(
            CollectionsMarshal.AsSpan(output).Slice(offset, sizeof(uint)),
            value);
    }

    private static void ApplyEdit(IReadOnlyList<SwShNestHoleRewardTable> tables, SwShNestHoleRewardEdit edit)
    {
        if ((uint)edit.TableIndex >= (uint)tables.Count)
        {
            throw new InvalidDataException($"Raid reward table index {edit.TableIndex} is not present.");
        }

        var table = tables[edit.TableIndex];
        if ((uint)edit.RewardIndex >= (uint)table.Rewards.Count)
        {
            throw new InvalidDataException($"Raid reward index {edit.RewardIndex} is not present.");
        }

        if (table.Rewards is not SwShNestHoleReward[] rewards)
        {
            throw new InvalidDataException("Raid reward list is not mutable.");
        }

        var reward = rewards[edit.RewardIndex];
        rewards[edit.RewardIndex] = edit.Field switch
        {
            SwShNestHoleRewardField.ItemId => reward with { ItemId = edit.Value },
            SwShNestHoleRewardField.Star1Value => ReplaceValue(reward, valueIndex: 0, edit.Value),
            SwShNestHoleRewardField.Star2Value => ReplaceValue(reward, valueIndex: 1, edit.Value),
            SwShNestHoleRewardField.Star3Value => ReplaceValue(reward, valueIndex: 2, edit.Value),
            SwShNestHoleRewardField.Star4Value => ReplaceValue(reward, valueIndex: 3, edit.Value),
            SwShNestHoleRewardField.Star5Value => ReplaceValue(reward, valueIndex: 4, edit.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(edit), $"Raid reward field '{edit.Field}' is not supported."),
        };

        if (tables is SwShNestHoleRewardTable[] mutableTables)
        {
            mutableTables[edit.TableIndex] = table with { Rewards = rewards };
        }
    }

    private static SwShNestHoleReward ReplaceValue(SwShNestHoleReward reward, int valueIndex, uint value)
    {
        if (reward.Values is not uint[] values)
        {
            throw new InvalidDataException("Raid reward values list is not mutable.");
        }

        if (values.Length < MinimumRewardValueCount)
        {
            throw new InvalidDataException(
                $"Raid reward contains {values.Length} star values; at least {MinimumRewardValueCount} are required.");
        }

        values[valueIndex] = value;

        return reward with { Values = values };
    }

    private static TableLayout ReadTableLayout(
        ReadOnlySpan<byte> data,
        int tableOffset,
        string label,
        string kind,
        int alignment,
        StructuralRangeRegistry ranges,
        IReadOnlyList<FieldLayout> knownFields)
    {
        if ((tableOffset % alignment) != 0)
        {
            throw new InvalidDataException($"{label} is not aligned to {alignment} bytes.");
        }

        EnsureRange(data, tableOffset, sizeof(int));
        var vtableDistance = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(tableOffset, sizeof(int)));
        if (vtableDistance == 0)
        {
            throw new InvalidDataException($"{label} has a zero vtable displacement.");
        }

        var vtableOffsetLong = (long)tableOffset - vtableDistance;
        if (vtableOffsetLong < 0 || vtableOffsetLong > int.MaxValue)
        {
            throw new InvalidDataException($"{label} vtable points outside the archive.");
        }

        var vtableOffset = (int)vtableOffsetLong;
        if ((vtableOffset % sizeof(ushort)) != 0)
        {
            throw new InvalidDataException($"{label} vtable is not 2-byte aligned.");
        }

        EnsureRange(data, vtableOffset, sizeof(ushort) * 2);
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vtableOffset, sizeof(ushort)));
        var objectLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vtableOffset + sizeof(ushort), sizeof(ushort)));
        if (vtableLength < sizeof(ushort) * 2 || (vtableLength % sizeof(ushort)) != 0)
        {
            throw new InvalidDataException($"{label} has an invalid vtable length {vtableLength}.");
        }

        if (objectLength < sizeof(int))
        {
            throw new InvalidDataException($"{label} has an invalid object length {objectLength}.");
        }

        EnsureRange(data, vtableOffset, vtableLength);
        EnsureRange(data, tableOffset, objectLength);
        ranges.Register(vtableOffset, vtableLength, $"{label} vtable", "vtable", allowExactAlias: true);
        ranges.Register(tableOffset, objectLength, label, kind, allowExactAlias: true);

        var fieldCount = (vtableLength - (sizeof(ushort) * 2)) / sizeof(ushort);
        var fieldOffsets = new ushort[fieldCount];
        for (var fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            var fieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(
                vtableOffset + (sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort)),
                sizeof(ushort)));
            if (fieldOffset != 0 && (fieldOffset < sizeof(int) || fieldOffset >= objectLength))
            {
                throw new InvalidDataException(
                    $"{label} field {fieldIndex} points outside its table object.");
            }

            fieldOffsets[fieldIndex] = fieldOffset;
        }

        var knownFieldRanges = new List<FieldRange>(knownFields.Count);
        foreach (var field in knownFields)
        {
            var fieldOffset = field.FieldIndex < fieldOffsets.Length
                ? fieldOffsets[field.FieldIndex]
                : 0;
            if (fieldOffset == 0)
            {
                continue;
            }

            if (fieldOffset > objectLength - field.Size)
            {
                throw new InvalidDataException(
                    $"{label} field {field.FieldIndex} exceeds its table object.");
            }

            if (((tableOffset + fieldOffset) % field.Alignment) != 0)
            {
                throw new InvalidDataException(
                    $"{label} field {field.FieldIndex} is not aligned to {field.Alignment} bytes.");
            }

            foreach (var existing in knownFieldRanges)
            {
                if (RangesOverlap(fieldOffset, field.Size, existing.Offset, existing.Length))
                {
                    throw new InvalidDataException(
                        $"{label} fields {existing.FieldIndex} and {field.FieldIndex} overlap within the table object.");
                }
            }

            knownFieldRanges.Add(new FieldRange(field.FieldIndex, fieldOffset, field.Size));
        }

        var knownFieldIndexes = knownFields.Select(field => field.FieldIndex).ToHashSet();
        var unknownFieldStarts = new HashSet<ushort>();
        for (var fieldIndex = 0; fieldIndex < fieldOffsets.Length; fieldIndex++)
        {
            var fieldOffset = fieldOffsets[fieldIndex];
            if (fieldOffset == 0 || knownFieldIndexes.Contains(fieldIndex))
            {
                continue;
            }

            foreach (var known in knownFieldRanges)
            {
                if (fieldOffset >= known.Offset && fieldOffset < known.Offset + known.Length)
                {
                    throw new InvalidDataException(
                        $"{label} unknown field {fieldIndex} aliases known field {known.FieldIndex}.");
                }
            }

            if (!unknownFieldStarts.Add(fieldOffset))
            {
                throw new InvalidDataException(
                    $"{label} unknown field {fieldIndex} aliases another unknown field.");
            }
        }

        return new TableLayout(tableOffset, vtableOffset, vtableLength, objectLength, fieldOffsets);
    }

    private static int ReadTableUOffset(
        ReadOnlySpan<byte> data,
        TableLayout table,
        int fieldIndex,
        bool required,
        int targetAlignment)
    {
        var fieldOffset = ReadTableFieldOffset(table, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        return ReadUOffset(data, checked(table.TableOffset + fieldOffset), targetAlignment);
    }

    private static uint ReadTableUInt32(ReadOnlySpan<byte> data, TableLayout table, int fieldIndex)
    {
        var fieldOffset = ReadTableFieldOffset(table, fieldIndex);
        return fieldOffset == 0
            ? 0
            : BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(
                checked(table.TableOffset + fieldOffset),
                sizeof(uint)));
    }

    private static ulong ReadTableUInt64(ReadOnlySpan<byte> data, TableLayout table, int fieldIndex)
    {
        var fieldOffset = ReadTableFieldOffset(table, fieldIndex);
        return fieldOffset == 0
            ? 0
            : BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(
                checked(table.TableOffset + fieldOffset),
                sizeof(ulong)));
    }

    private static int ReadRequiredTableFieldOffset(TableLayout table, int fieldIndex)
    {
        var fieldOffset = ReadTableFieldOffset(table, fieldIndex);
        if (fieldOffset == 0)
        {
            throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
        }

        return fieldOffset;
    }

    private static int ReadTableFieldOffset(TableLayout table, int fieldIndex)
    {
        return (uint)fieldIndex < (uint)table.FieldOffsets.Count
            ? table.FieldOffsets[fieldIndex]
            : 0;
    }

    private static int ReadUOffset(ReadOnlySpan<byte> data, int offset, int targetAlignment)
    {
        if ((offset % sizeof(uint)) != 0)
        {
            throw new InvalidDataException("FlatBuffer unsigned offset is not 4-byte aligned.");
        }

        EnsureRange(data, offset, sizeof(uint));
        var relativeOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));
        if (relativeOffset == 0)
        {
            throw new InvalidDataException("FlatBuffer unsigned offset must point forward.");
        }

        var targetOffsetLong = (long)offset + relativeOffset;
        if (targetOffsetLong > int.MaxValue || targetOffsetLong > data.Length - sizeof(uint))
        {
            throw new InvalidDataException("FlatBuffer offset points outside the nest hole reward archive.");
        }

        var targetOffset = (int)targetOffsetLong;
        if ((targetOffset % targetAlignment) != 0)
        {
            throw new InvalidDataException($"FlatBuffer target is not aligned to {targetAlignment} bytes.");
        }

        return targetOffset;
    }

    private static int ReadAndRegisterVector(
        ReadOnlySpan<byte> data,
        int vectorOffset,
        int elementSize,
        string label,
        string kind,
        StructuralRangeRegistry ranges)
    {
        EnsureRange(data, vectorOffset, sizeof(uint));
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(vectorOffset, sizeof(uint)));
        var length = sizeof(uint) + ((long)count * elementSize);
        if (count > int.MaxValue || length > int.MaxValue)
        {
            throw new InvalidDataException($"{label} is too large.");
        }

        EnsureRange(data, vectorOffset, (int)length);
        ranges.Register(vectorOffset, (int)length, label, kind, allowExactAlias: true);
        return (int)count;
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length || length > data.Length - offset)
        {
            throw new InvalidDataException("FlatBuffer offset points outside the nest hole reward archive.");
        }
    }

    private static bool RangesOverlap(int firstOffset, int firstLength, int secondOffset, int secondLength)
    {
        return firstOffset < (long)secondOffset + secondLength
            && secondOffset < (long)firstOffset + firstLength;
    }

    private sealed record FieldLayout(int FieldIndex, int Size, int Alignment);

    private sealed record FieldRange(int FieldIndex, int Offset, int Length);

    private sealed record TableLayout(
        int TableOffset,
        int VtableOffset,
        int VtableLength,
        int ObjectLength,
        IReadOnlyList<ushort> FieldOffsets);

    private sealed record SourceRewardTableLayout(
        int TableVectorElementOffset,
        int TableOffset,
        int RewardsVectorOffset,
        IReadOnlyList<SourceRewardLayout> Rewards);

    private sealed record SourceRewardLayout(
        int RewardVectorElementOffset,
        int RewardOffset,
        int ItemValueOffset,
        int ValuesFieldOffset,
        int ValuesVectorOffset,
        int VtableLength,
        bool HasMaterializedUnknownFields);

    private sealed record RewardEditKey(
        int TableIndex,
        int RewardIndex,
        SwShNestHoleRewardField Field);

    private sealed class EffectiveRewardTableLayout
    {
        public EffectiveRewardTableLayout(SourceRewardTableLayout source, int delta, bool isolated)
        {
            Isolated = isolated;
            Rewards = source.Rewards
                .Select(reward => new EffectiveRewardLayout(reward, delta, isolated: false))
                .ToArray();
        }

        public bool Isolated { get; }

        public EffectiveRewardLayout[] Rewards { get; }
    }

    private sealed class EffectiveRewardLayout
    {
        public EffectiveRewardLayout(
            int rewardVectorElementOffset,
            int itemValueOffset,
            int valuesFieldOffset,
            int valuesVectorOffset)
        {
            RewardVectorElementOffset = rewardVectorElementOffset;
            ItemValueOffset = itemValueOffset;
            ValuesFieldOffset = valuesFieldOffset;
            ValuesVectorOffset = valuesVectorOffset;
            Isolated = true;
        }

        public EffectiveRewardLayout(SourceRewardLayout source, int delta, bool isolated)
        {
            RewardVectorElementOffset = checked(source.RewardVectorElementOffset + delta);
            ItemValueOffset = source.ItemValueOffset < 0
                ? -1
                : checked(source.ItemValueOffset + delta);
            ValuesFieldOffset = checked(source.ValuesFieldOffset + delta);
            ValuesVectorOffset = checked(source.ValuesVectorOffset + delta);
            Isolated = isolated;
        }

        public int RewardVectorElementOffset { get; }

        public int ItemValueOffset { get; }

        public int ValuesFieldOffset { get; }

        public int ValuesVectorOffset { get; set; }

        public bool Isolated { get; }
    }

    private sealed class StructuralRangeRegistry
    {
        private readonly List<StructuralRange> ranges = [];

        public void Register(
            int offset,
            int length,
            string label,
            string kind,
            bool allowExactAlias)
        {
            foreach (var existing in ranges)
            {
                if (!RangesOverlap(offset, length, existing.Offset, existing.Length))
                {
                    continue;
                }

                var exactAlias = offset == existing.Offset && length == existing.Length;
                if (exactAlias
                    && allowExactAlias
                    && existing.AllowExactAlias
                    && string.Equals(kind, existing.Kind, StringComparison.Ordinal))
                {
                    return;
                }

                throw new InvalidDataException(
                    $"FlatBuffer structures '{label}' and '{existing.Label}' overlap unsafely.");
            }

            ranges.Add(new StructuralRange(offset, length, label, kind, allowExactAlias));
        }

        private static bool RangesOverlap(int firstOffset, int firstLength, int secondOffset, int secondLength)
        {
            return (long)firstOffset < (long)secondOffset + secondLength
                && (long)secondOffset < (long)firstOffset + firstLength;
        }
    }

    private sealed record StructuralRange(
        int Offset,
        int Length,
        string Label,
        string Kind,
        bool AllowExactAlias);

    private sealed class RewardFlatBufferWriter
    {
        private readonly List<byte> bytes = [];

        public void Write(SwShNestHoleRewardArchive archive)
        {
            WriteUInt32(0);
            var root = WriteArchiveTable();
            WriteUInt32At(0, checked((uint)root.TableOffset));

            var tableVector = WriteTableVector(archive.Tables.Count);
            PatchUOffset(root.Field0Offset, tableVector.VectorOffset);
            for (var index = 0; index < archive.Tables.Count; index++)
            {
                var tableOffset = WriteRewardTable(archive.Tables[index]);
                PatchUOffset(tableVector.ElementOffsets[index], tableOffset);
            }
        }

        public byte[] ToArray()
        {
            return bytes.ToArray();
        }

        private TableFields WriteArchiveTable()
        {
            AlignForTable(vtableLength: 6, alignment: 4);
            var vtableOffset = Position;
            WriteUInt16(6);
            WriteUInt16(8);
            WriteUInt16(4);
            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            var tableFieldOffset = Position;
            WriteUInt32(0);

            return new TableFields(tableOffset, tableFieldOffset);
        }

        private int WriteRewardTable(SwShNestHoleRewardTable table)
        {
            AlignForTable(vtableLength: 8, alignment: 8);
            var vtableOffset = Position;
            WriteUInt16(8);
            WriteUInt16(16);
            WriteUInt16(8);
            WriteUInt16(4);
            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            var entriesFieldOffset = Position;
            WriteUInt32(0);
            WriteUInt64(table.TableId);

            var rewardVector = WriteTableVector(table.Rewards.Count);
            PatchUOffset(entriesFieldOffset, rewardVector.VectorOffset);
            for (var index = 0; index < table.Rewards.Count; index++)
            {
                var rewardOffset = WriteReward(table.Rewards[index]);
                PatchUOffset(rewardVector.ElementOffsets[index], rewardOffset);
            }

            return tableOffset;
        }

        private int WriteReward(SwShNestHoleReward reward)
        {
            if (reward.Values.Count < MinimumRewardValueCount)
            {
                throw new InvalidDataException(
                    $"Raid reward contains {reward.Values.Count} star values; at least {MinimumRewardValueCount} are required.");
            }

            AlignForTable(vtableLength: 10, alignment: 4);
            var vtableOffset = Position;
            WriteUInt16(10);
            WriteUInt16(16);
            WriteUInt16(8);
            WriteUInt16(12);
            WriteUInt16(4);
            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            var valuesFieldOffset = Position;
            WriteUInt32(0);
            WriteUInt32(reward.EntryId);
            WriteUInt32(reward.ItemId);

            var valuesVector = WriteUIntVector(reward.Values);
            PatchUOffset(valuesFieldOffset, valuesVector);

            return tableOffset;
        }

        private int WriteUIntVector(IReadOnlyList<uint> values)
        {
            Align(4);
            var vectorOffset = Position;
            WriteUInt32(checked((uint)values.Count));
            foreach (var value in values)
            {
                WriteUInt32(value);
            }

            return vectorOffset;
        }

        private VectorFields WriteTableVector(int count)
        {
            Align(4);
            var vectorOffset = Position;
            WriteUInt32(checked((uint)count));
            var elementOffsets = new int[count];
            for (var index = 0; index < count; index++)
            {
                elementOffsets[index] = Position;
                WriteUInt32(0);
            }

            return new VectorFields(vectorOffset, elementOffsets);
        }

        private void PatchUOffset(int sourceOffset, int targetOffset)
        {
            if (targetOffset <= sourceOffset)
            {
                throw new InvalidOperationException("FlatBuffer target offsets must point forward.");
            }

            WriteUInt32At(sourceOffset, checked((uint)(targetOffset - sourceOffset)));
        }

        private void AlignForTable(int vtableLength, int alignment)
        {
            while (((Position + vtableLength) % alignment) != 0)
            {
                bytes.Add(0);
            }
        }

        private void Align(int alignment)
        {
            while ((Position % alignment) != 0)
            {
                bytes.Add(0);
            }
        }

        private int Position => bytes.Count;

        private void WriteUInt16(ushort value)
        {
            var start = Grow(sizeof(ushort));
            BinaryPrimitives.WriteUInt16LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(start, sizeof(ushort)), value);
        }

        private void WriteInt32(int value)
        {
            var start = Grow(sizeof(int));
            BinaryPrimitives.WriteInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(start, sizeof(int)), value);
        }

        private void WriteUInt32(uint value)
        {
            var start = Grow(sizeof(uint));
            BinaryPrimitives.WriteUInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(start, sizeof(uint)), value);
        }

        private void WriteUInt64(ulong value)
        {
            var start = Grow(sizeof(ulong));
            BinaryPrimitives.WriteUInt64LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(start, sizeof(ulong)), value);
        }

        private void WriteUInt32At(int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(offset, sizeof(uint)), value);
        }

        private int Grow(int count)
        {
            var start = bytes.Count;
            for (var index = 0; index < count; index++)
            {
                bytes.Add(0);
            }

            return start;
        }

        private sealed record TableFields(int TableOffset, int Field0Offset);

        private sealed record VectorFields(
            int VectorOffset,
            IReadOnlyList<int> ElementOffsets);
    }
}
