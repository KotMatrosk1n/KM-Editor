// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace KM.Formats.SwSh;

public sealed record SwShEncounterNest(
    int EntryIndex,
    int Species,
    int Form,
    ulong LevelTableId,
    int Ability,
    bool IsGigantamax,
    ulong DropTableId,
    ulong BonusTableId,
    IReadOnlyList<uint> Probabilities,
    int Gender,
    int FlawlessIvs);

public sealed record SwShEncounterNestTable(
    ulong TableId,
    int GameVersion,
    IReadOnlyList<SwShEncounterNest> Entries);

public enum SwShEncounterNestField
{
    Species,
    Form,
    Ability,
    IsGigantamax,
    Star1Probability,
    Star2Probability,
    Star3Probability,
    Star4Probability,
    Star5Probability,
    Gender,
    FlawlessIvs,
}

public sealed record SwShEncounterNestEdit(
    int TableIndex,
    int EntryIndex,
    SwShEncounterNestField Field,
    int Value);

public sealed record SwShEncounterNestArchive(IReadOnlyList<SwShEncounterNestTable> Tables)
{
    public const int MaximumSpeciesId = ushort.MaxValue;
    public const int MaximumForm = byte.MaxValue;
    public const int MaximumAbility = 4;
    public const int MaximumGender = 3;
    public const int MaximumFlawlessIvs = 6;
    public const int MaximumProbability = 100;

    private const int MinimumProbabilityCount = 5;
    private const int KnownEntryFieldCount = 11;
    private const int KnownEntryVtableLength = 26;
    private const int KnownEntryObjectLength = 48;

    private byte[]? SourceData { get; init; }

    private IReadOnlyList<SourceEncounterTableLayout>? SourceTableLayouts { get; init; }

    private IReadOnlyList<SwShEncounterNestTable>? SourceTables { get; init; }

    public static SwShEncounterNestArchive Parse(ReadOnlySpan<byte> data)
    {
        try
        {
            return ParseCore(data);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                "Encounter nest archive contains an invalid count, size, or offset.",
                exception);
        }
    }

    private static SwShEncounterNestArchive ParseCore(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("Encounter nest archive is too small to contain a FlatBuffer root.");
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
            "encounter table vector",
            "encounter table vector",
            ranges);
        var tables = new SwShEncounterNestTable[tableCount];
        var tableLayouts = new SourceEncounterTableLayout[tableCount];

        for (var tableIndex = 0; tableIndex < tableCount; tableIndex++)
        {
            var tableVectorElementOffset = checked(
                tableVectorOffset + sizeof(uint) + (tableIndex * sizeof(uint)));
            var tableOffset = ReadUOffset(data, tableVectorElementOffset, targetAlignment: sizeof(uint));
            var table = ReadTableLayout(
                data,
                tableOffset,
                $"encounter table {tableIndex}",
                "encounter table",
                alignment: sizeof(uint),
                ranges,
                [
                    new FieldLayout(0, sizeof(ulong), sizeof(ulong)),
                    new FieldLayout(1, sizeof(int), sizeof(int)),
                    new FieldLayout(2, sizeof(uint), sizeof(uint)),
                ]);
            var entriesVectorOffset = ReadTableUOffset(
                data,
                table,
                fieldIndex: 2,
                required: true,
                targetAlignment: sizeof(uint));
            var entryCount = ReadAndRegisterVector(
                data,
                entriesVectorOffset,
                elementSize: sizeof(uint),
                $"encounter table {tableIndex} entry vector",
                "encounter entry vector",
                ranges);
            var entries = new SwShEncounterNest[entryCount];
            var entryLayouts = new SourceEncounterLayout[entryCount];

            for (var entryIndex = 0; entryIndex < entryCount; entryIndex++)
            {
                var entryVectorElementOffset = checked(
                    entriesVectorOffset + sizeof(uint) + (entryIndex * sizeof(uint)));
                var entryOffset = ReadUOffset(data, entryVectorElementOffset, targetAlignment: sizeof(uint));
                var entry = ReadTableLayout(
                    data,
                    entryOffset,
                    $"encounter table {tableIndex} entry {entryIndex}",
                    "encounter entry table",
                    alignment: sizeof(uint),
                    ranges,
                    [
                        new FieldLayout(0, sizeof(int), sizeof(int)),
                        new FieldLayout(1, sizeof(int), sizeof(int)),
                        new FieldLayout(2, sizeof(int), sizeof(int)),
                        new FieldLayout(3, sizeof(ulong), sizeof(ulong)),
                        new FieldLayout(4, sizeof(byte), sizeof(byte)),
                        new FieldLayout(5, sizeof(byte), sizeof(byte)),
                        new FieldLayout(6, sizeof(ulong), sizeof(ulong)),
                        new FieldLayout(7, sizeof(ulong), sizeof(ulong)),
                        new FieldLayout(8, sizeof(uint), sizeof(uint)),
                        new FieldLayout(9, sizeof(byte), sizeof(byte)),
                        new FieldLayout(10, sizeof(byte), sizeof(byte)),
                    ]);
                var probabilitiesVectorOffset = ReadTableUOffset(
                    data,
                    entry,
                    fieldIndex: 8,
                    required: true,
                    targetAlignment: sizeof(uint));
                var probabilityCount = ReadAndRegisterVector(
                    data,
                    probabilitiesVectorOffset,
                    elementSize: sizeof(uint),
                    $"encounter table {tableIndex} entry {entryIndex} probability vector",
                    "encounter probability vector",
                    ranges);
                if (probabilityCount < MinimumProbabilityCount)
                {
                    throw new InvalidDataException(
                        $"Raid battle table {tableIndex} entry {entryIndex} contains {probabilityCount} star probabilities; at least {MinimumProbabilityCount} are required.");
                }

                var probabilities = new uint[probabilityCount];
                for (var probabilityIndex = 0; probabilityIndex < probabilities.Length; probabilityIndex++)
                {
                    probabilities[probabilityIndex] = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(
                        checked(probabilitiesVectorOffset + sizeof(uint) + (probabilityIndex * sizeof(uint))),
                        sizeof(uint)));
                }

                var fieldValueOffsets = Enumerable.Range(0, KnownEntryFieldCount)
                    .Select(fieldIndex =>
                    {
                        var fieldOffset = ReadTableFieldOffset(entry, fieldIndex);
                        return fieldOffset == 0 ? -1 : checked(entryOffset + fieldOffset);
                    })
                    .ToArray();
                var probabilitiesFieldOffset = checked(
                    entryOffset + ReadRequiredTableFieldOffset(entry, fieldIndex: 8));

                entries[entryIndex] = new SwShEncounterNest(
                    ReadTableInt32(data, entry, fieldIndex: 0),
                    ReadTableInt32(data, entry, fieldIndex: 1),
                    ReadTableInt32(data, entry, fieldIndex: 2),
                    ReadTableUInt64(data, entry, fieldIndex: 3),
                    ReadTableByte(data, entry, fieldIndex: 4),
                    ReadTableBool(data, entry, fieldIndex: 5),
                    ReadTableUInt64(data, entry, fieldIndex: 6),
                    ReadTableUInt64(data, entry, fieldIndex: 7),
                    probabilities,
                    ReadTableSByte(data, entry, fieldIndex: 9),
                    ReadTableSByte(data, entry, fieldIndex: 10));
                entryLayouts[entryIndex] = new SourceEncounterLayout(
                    entryVectorElementOffset,
                    entryOffset,
                    fieldValueOffsets,
                    probabilitiesFieldOffset,
                    probabilitiesVectorOffset,
                    entry.VtableLength,
                    entry.FieldOffsets.Skip(KnownEntryFieldCount).Any(fieldOffset => fieldOffset != 0));
            }

            tables[tableIndex] = new SwShEncounterNestTable(
                ReadTableUInt64(data, table, fieldIndex: 0),
                ReadTableInt32(data, table, fieldIndex: 1),
                entries);
            tableLayouts[tableIndex] = new SourceEncounterTableLayout(
                tableVectorElementOffset,
                tableOffset,
                entriesVectorOffset,
                entryLayouts);
        }

        return new SwShEncounterNestArchive(tables)
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

        var writer = new EncounterNestFlatBufferWriter();
        writer.Write(this);

        return writer.ToArray();
    }

    public byte[] WriteEdits(IEnumerable<SwShEncounterNestEdit> edits)
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

        return new SwShEncounterNestArchive(tables).Write();
    }

    private static SwShEncounterNestTable[] CloneTables(IReadOnlyList<SwShEncounterNestTable> source)
    {
        return source
            .Select(table => table with
            {
                Entries = table.Entries
                    .Select(entry => entry with { Probabilities = entry.Probabilities.ToArray() })
                    .ToArray(),
            })
            .ToArray();
    }

    private static bool TablesEqual(
        IReadOnlyList<SwShEncounterNestTable> current,
        IReadOnlyList<SwShEncounterNestTable> source)
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
                || currentTable.GameVersion != sourceTable.GameVersion
                || currentTable.Entries.Count != sourceTable.Entries.Count)
            {
                return false;
            }

            for (var entryIndex = 0; entryIndex < currentTable.Entries.Count; entryIndex++)
            {
                var currentEntry = currentTable.Entries[entryIndex];
                var sourceEntry = sourceTable.Entries[entryIndex];
                if (currentEntry.EntryIndex != sourceEntry.EntryIndex
                    || currentEntry.Species != sourceEntry.Species
                    || currentEntry.Form != sourceEntry.Form
                    || currentEntry.LevelTableId != sourceEntry.LevelTableId
                    || currentEntry.Ability != sourceEntry.Ability
                    || currentEntry.IsGigantamax != sourceEntry.IsGigantamax
                    || currentEntry.DropTableId != sourceEntry.DropTableId
                    || currentEntry.BonusTableId != sourceEntry.BonusTableId
                    || !currentEntry.Probabilities.SequenceEqual(sourceEntry.Probabilities)
                    || currentEntry.Gender != sourceEntry.Gender
                    || currentEntry.FlawlessIvs != sourceEntry.FlawlessIvs)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private byte[] WriteSourceEdits(IReadOnlyList<SwShEncounterNestEdit> edits)
    {
        var source = SourceData
            ?? throw new InvalidDataException("Raid battle source bytes are unavailable.");
        var layouts = SourceTableLayouts
            ?? throw new InvalidDataException("Raid battle source layout is unavailable.");
        if (layouts.Count != Tables.Count)
        {
            throw new InvalidDataException("Raid battle source layout does not match the parsed table count.");
        }

        var finalValues = new Dictionary<EncounterEditKey, int>();
        foreach (var edit in edits)
        {
            ValidateEditTarget(edit);
            finalValues[new EncounterEditKey(edit.TableIndex, edit.EntryIndex, edit.Field)] =
                ValidateEditValue(edit.Field, edit.Value);
        }

        var effectiveEdits = finalValues
            .Where(pair => ReadLogicalValue(pair.Key) != pair.Value)
            .OrderBy(pair => pair.Key.TableIndex)
            .ThenBy(pair => pair.Key.EntryIndex)
            .ThenBy(pair => pair.Key.Field)
            .ToArray();
        if (effectiveEdits.Length == 0)
        {
            return source.ToArray();
        }

        var output = new List<byte>(source.Length);
        output.AddRange(source);
        var effectiveTables = layouts
            .Select(layout => new EffectiveEncounterTableLayout(layout, delta: 0, isolated: false))
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
                effectiveTable = new EffectiveEncounterTableLayout(
                    layouts[tableIndex],
                    tableDelta,
                    isolated: true);
                effectiveTables[tableIndex] = effectiveTable;
            }

            foreach (var entryEdits in tableEdits.GroupBy(pair => pair.Key.EntryIndex))
            {
                var entryIndex = entryEdits.Key;
                var effectiveEntry = effectiveTable.Entries[entryIndex];
                var sourceEntry = layouts[tableIndex].Entries[entryIndex];
                var needsMaterializedScalar = entryEdits.Any(pair =>
                    !IsProbabilityField(pair.Key.Field)
                    && effectiveEntry.GetFieldValueOffset(FieldToSchemaFieldIndex(pair.Key.Field)) < 0);
                if (needsMaterializedScalar)
                {
                    if (sourceEntry.HasMaterializedUnknownFields)
                    {
                        throw new InvalidDataException(
                            "Raid battle entry contains unknown materialized fields, so an omitted editable field cannot be materialized safely.");
                    }

                    effectiveEntry = AppendMaterializedEntry(
                        output,
                        effectiveEntry.EntryVectorElementOffset,
                        sourceEntry.VtableLength,
                        Tables[tableIndex].Entries[entryIndex]);
                    effectiveTable.Entries[entryIndex] = effectiveEntry;
                }
                else if (IsEntryAliased(layouts, tableIndex, entryIndex, effectiveTable.Isolated))
                {
                    var entryDelta = AppendSourceCopy(output, source);
                    PatchUOffset(
                        output,
                        effectiveEntry.EntryVectorElementOffset,
                        checked(sourceEntry.EntryOffset + entryDelta));
                    effectiveEntry = new EffectiveEncounterLayout(sourceEntry, entryDelta, isolated: true);
                    effectiveTable.Entries[entryIndex] = effectiveEntry;
                }

                if (!effectiveEntry.Isolated
                    && entryEdits.Any(pair => IsProbabilityField(pair.Key.Field))
                    && IsProbabilitiesVectorAliased(layouts, tableIndex, entryIndex, effectiveTable.Isolated))
                {
                    var probabilitiesDelta = AppendSourceCopy(output, source);
                    PatchUOffset(
                        output,
                        effectiveEntry.ProbabilitiesFieldOffset,
                        checked(sourceEntry.ProbabilitiesVectorOffset + probabilitiesDelta));
                    effectiveEntry.ProbabilitiesVectorOffset = checked(
                        sourceEntry.ProbabilitiesVectorOffset + probabilitiesDelta);
                }

                foreach (var edit in entryEdits)
                {
                    PatchSourceEdit(output, effectiveEntry, edit.Key.Field, edit.Value);
                }
            }
        }

        return output.ToArray();
    }

    private static EffectiveEncounterLayout AppendMaterializedEntry(
        List<byte> output,
        int entryVectorElementOffset,
        int sourceVtableLength,
        SwShEncounterNest entry)
    {
        if (entry.Probabilities.Count < MinimumProbabilityCount)
        {
            throw new InvalidDataException(
                $"Raid battle entry contains {entry.Probabilities.Count} star probabilities; at least {MinimumProbabilityCount} are required.");
        }

        var vtableLength = Math.Max(sourceVtableLength, KnownEntryVtableLength);
        while (((output.Count + vtableLength) % sizeof(ulong)) != 0)
        {
            output.Add(0);
        }

        var vtableOffset = output.Count;
        var vtable = new byte[vtableLength];
        BinaryPrimitives.WriteUInt16LittleEndian(vtable.AsSpan(0, sizeof(ushort)), checked((ushort)vtableLength));
        BinaryPrimitives.WriteUInt16LittleEndian(vtable.AsSpan(2, sizeof(ushort)), KnownEntryObjectLength);
        var canonicalFieldOffsets = new ushort[] { 8, 12, 16, 24, 20, 21, 32, 40, 4, 22, 23 };
        for (var fieldIndex = 0; fieldIndex < canonicalFieldOffsets.Length; fieldIndex++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                vtable.AsSpan((sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort)), sizeof(ushort)),
                canonicalFieldOffsets[fieldIndex]);
        }

        output.AddRange(vtable);
        var tableOffset = output.Count;
        var table = new byte[KnownEntryObjectLength];
        BinaryPrimitives.WriteInt32LittleEndian(
            table.AsSpan(0, sizeof(int)),
            checked(tableOffset - vtableOffset));
        BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(8, sizeof(int)), entry.EntryIndex);
        BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(12, sizeof(int)), entry.Species);
        BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(16, sizeof(int)), entry.Form);
        table[20] = checked((byte)entry.Ability);
        table[21] = entry.IsGigantamax ? (byte)1 : (byte)0;
        table[22] = unchecked((byte)checked((sbyte)entry.Gender));
        table[23] = unchecked((byte)checked((sbyte)entry.FlawlessIvs));
        BinaryPrimitives.WriteUInt64LittleEndian(table.AsSpan(24, sizeof(ulong)), entry.LevelTableId);
        BinaryPrimitives.WriteUInt64LittleEndian(table.AsSpan(32, sizeof(ulong)), entry.DropTableId);
        BinaryPrimitives.WriteUInt64LittleEndian(table.AsSpan(40, sizeof(ulong)), entry.BonusTableId);
        output.AddRange(table);

        var probabilitiesVectorOffset = output.Count;
        var probabilityVector = new byte[checked(sizeof(uint) + (entry.Probabilities.Count * sizeof(uint)))];
        BinaryPrimitives.WriteUInt32LittleEndian(
            probabilityVector.AsSpan(0, sizeof(uint)),
            checked((uint)entry.Probabilities.Count));
        for (var probabilityIndex = 0; probabilityIndex < entry.Probabilities.Count; probabilityIndex++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                probabilityVector.AsSpan(sizeof(uint) + (probabilityIndex * sizeof(uint)), sizeof(uint)),
                entry.Probabilities[probabilityIndex]);
        }

        output.AddRange(probabilityVector);
        var probabilitiesFieldOffset = checked(tableOffset + sizeof(int));
        PatchUOffset(output, probabilitiesFieldOffset, probabilitiesVectorOffset);
        PatchUOffset(output, entryVectorElementOffset, tableOffset);

        return new EffectiveEncounterLayout(
            entryVectorElementOffset,
            tableOffset,
            canonicalFieldOffsets
                .Select(offset => checked(tableOffset + offset))
                .ToArray(),
            probabilitiesFieldOffset,
            probabilitiesVectorOffset);
    }

    private void ValidateEditTarget(SwShEncounterNestEdit edit)
    {
        if ((uint)edit.TableIndex >= (uint)Tables.Count)
        {
            throw new InvalidDataException($"Raid battle table index {edit.TableIndex} is not present.");
        }

        if ((uint)edit.EntryIndex >= (uint)Tables[edit.TableIndex].Entries.Count)
        {
            throw new InvalidDataException($"Raid battle entry index {edit.EntryIndex} is not present.");
        }

        if (!Enum.IsDefined(edit.Field))
        {
            throw new ArgumentOutOfRangeException(nameof(edit), $"Raid battle field '{edit.Field}' is not supported.");
        }
    }

    private static int ValidateEditValue(SwShEncounterNestField field, int value)
    {
        return field switch
        {
            SwShEncounterNestField.Species => ValidateRange(value, 0, MaximumSpeciesId),
            SwShEncounterNestField.Form => ValidateRange(value, 0, MaximumForm),
            SwShEncounterNestField.Ability => ValidateRange(value, 0, MaximumAbility),
            SwShEncounterNestField.IsGigantamax => ValidateRange(value, 0, 1),
            SwShEncounterNestField.Star1Probability or
            SwShEncounterNestField.Star2Probability or
            SwShEncounterNestField.Star3Probability or
            SwShEncounterNestField.Star4Probability or
            SwShEncounterNestField.Star5Probability => ValidateRange(value, 0, MaximumProbability),
            SwShEncounterNestField.Gender => ValidateRange(value, 0, MaximumGender),
            SwShEncounterNestField.FlawlessIvs => ValidateRange(value, 0, MaximumFlawlessIvs),
            _ => throw new ArgumentOutOfRangeException(nameof(field), $"Raid battle field '{field}' is not supported."),
        };
    }

    private long ReadLogicalValue(EncounterEditKey key)
    {
        var entry = Tables[key.TableIndex].Entries[key.EntryIndex];
        return key.Field switch
        {
            SwShEncounterNestField.Species => entry.Species,
            SwShEncounterNestField.Form => entry.Form,
            SwShEncounterNestField.Ability => entry.Ability,
            SwShEncounterNestField.IsGigantamax => entry.IsGigantamax ? 1 : 0,
            SwShEncounterNestField.Star1Probability => entry.Probabilities[0],
            SwShEncounterNestField.Star2Probability => entry.Probabilities[1],
            SwShEncounterNestField.Star3Probability => entry.Probabilities[2],
            SwShEncounterNestField.Star4Probability => entry.Probabilities[3],
            SwShEncounterNestField.Star5Probability => entry.Probabilities[4],
            SwShEncounterNestField.Gender => entry.Gender,
            SwShEncounterNestField.FlawlessIvs => entry.FlawlessIvs,
            _ => throw new ArgumentOutOfRangeException(nameof(key), $"Raid battle field '{key.Field}' is not supported."),
        };
    }

    private static void PatchSourceEdit(
        List<byte> output,
        EffectiveEncounterLayout entry,
        SwShEncounterNestField field,
        int value)
    {
        if (IsProbabilityField(field))
        {
            WriteUInt32At(
                output,
                checked(entry.ProbabilitiesVectorOffset + sizeof(uint) + (FieldToProbabilityIndex(field) * sizeof(uint))),
                checked((uint)value));
            return;
        }

        var fieldIndex = FieldToSchemaFieldIndex(field);
        var valueOffset = entry.GetFieldValueOffset(fieldIndex);
        if (valueOffset < 0)
        {
            throw new InvalidDataException(
                $"Raid battle field '{field}' is omitted from the source table and cannot be materialized safely.");
        }

        switch (field)
        {
            case SwShEncounterNestField.Species:
            case SwShEncounterNestField.Form:
                WriteInt32At(output, valueOffset, value);
                break;
            case SwShEncounterNestField.Ability:
            case SwShEncounterNestField.IsGigantamax:
                WriteByteAt(output, valueOffset, checked((byte)value));
                break;
            case SwShEncounterNestField.Gender:
            case SwShEncounterNestField.FlawlessIvs:
                WriteByteAt(output, valueOffset, unchecked((byte)checked((sbyte)value)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), $"Raid battle field '{field}' is not supported.");
        }
    }

    private static int FieldToSchemaFieldIndex(SwShEncounterNestField field)
    {
        return field switch
        {
            SwShEncounterNestField.Species => 1,
            SwShEncounterNestField.Form => 2,
            SwShEncounterNestField.Ability => 4,
            SwShEncounterNestField.IsGigantamax => 5,
            SwShEncounterNestField.Gender => 9,
            SwShEncounterNestField.FlawlessIvs => 10,
            _ => throw new ArgumentOutOfRangeException(nameof(field), $"Raid battle field '{field}' is not a scalar field."),
        };
    }

    private static int FieldToProbabilityIndex(SwShEncounterNestField field)
    {
        return field switch
        {
            SwShEncounterNestField.Star1Probability => 0,
            SwShEncounterNestField.Star2Probability => 1,
            SwShEncounterNestField.Star3Probability => 2,
            SwShEncounterNestField.Star4Probability => 3,
            SwShEncounterNestField.Star5Probability => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(field), $"Raid battle field '{field}' is not a probability field."),
        };
    }

    private static bool IsProbabilityField(SwShEncounterNestField field)
    {
        return field is
            SwShEncounterNestField.Star1Probability or
            SwShEncounterNestField.Star2Probability or
            SwShEncounterNestField.Star3Probability or
            SwShEncounterNestField.Star4Probability or
            SwShEncounterNestField.Star5Probability;
    }

    private static bool IsTableAliased(IReadOnlyList<SourceEncounterTableLayout> layouts, int tableIndex)
    {
        var target = layouts[tableIndex];
        return layouts.Where((_, index) => index != tableIndex).Any(candidate =>
            candidate.TableOffset == target.TableOffset
            || candidate.EntriesVectorOffset == target.EntriesVectorOffset);
    }

    private static bool IsEntryAliased(
        IReadOnlyList<SourceEncounterTableLayout> layouts,
        int tableIndex,
        int entryIndex,
        bool tableIsolated)
    {
        var targetOffset = layouts[tableIndex].Entries[entryIndex].EntryOffset;
        var candidates = tableIsolated
            ? layouts[tableIndex].Entries
            : layouts.SelectMany(layout => layout.Entries);
        return candidates.Count(candidate => candidate.EntryOffset == targetOffset) > 1;
    }

    private static bool IsProbabilitiesVectorAliased(
        IReadOnlyList<SourceEncounterTableLayout> layouts,
        int tableIndex,
        int entryIndex,
        bool tableIsolated)
    {
        var targetOffset = layouts[tableIndex].Entries[entryIndex].ProbabilitiesVectorOffset;
        var candidates = tableIsolated
            ? layouts[tableIndex].Entries
            : layouts.SelectMany(layout => layout.Entries);
        return candidates.Count(candidate => candidate.ProbabilitiesVectorOffset == targetOffset) > 1;
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

        WriteUInt32At(output, sourceOffset, checked((uint)(targetOffset - sourceOffset)));
    }

    private static void WriteByteAt(List<byte> output, int offset, byte value)
    {
        if ((uint)offset >= (uint)output.Count)
        {
            throw new InvalidDataException("FlatBuffer copy-on-write patch points outside the output.");
        }

        output[offset] = value;
    }

    private static void WriteInt32At(List<byte> output, int offset, int value)
    {
        if (offset < 0 || offset > output.Count - sizeof(int))
        {
            throw new InvalidDataException("FlatBuffer copy-on-write patch points outside the output.");
        }

        BinaryPrimitives.WriteInt32LittleEndian(
            CollectionsMarshal.AsSpan(output).Slice(offset, sizeof(int)),
            value);
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

    private static void ApplyEdit(IReadOnlyList<SwShEncounterNestTable> tables, SwShEncounterNestEdit edit)
    {
        if ((uint)edit.TableIndex >= (uint)tables.Count)
        {
            throw new InvalidDataException($"Raid battle table index {edit.TableIndex} is not present.");
        }

        var table = tables[edit.TableIndex];
        if ((uint)edit.EntryIndex >= (uint)table.Entries.Count)
        {
            throw new InvalidDataException($"Raid battle entry index {edit.EntryIndex} is not present.");
        }

        if (table.Entries is not SwShEncounterNest[] entries)
        {
            throw new InvalidDataException("Raid battle entry list is not mutable.");
        }

        var entry = entries[edit.EntryIndex];
        var value = ValidateEditValue(edit.Field, edit.Value);
        entries[edit.EntryIndex] = edit.Field switch
        {
            SwShEncounterNestField.Species => entry with { Species = value },
            SwShEncounterNestField.Form => entry with { Form = value },
            SwShEncounterNestField.Ability => entry with { Ability = value },
            SwShEncounterNestField.IsGigantamax => entry with { IsGigantamax = value != 0 },
            SwShEncounterNestField.Star1Probability => ReplaceProbability(entry, probabilityIndex: 0, value),
            SwShEncounterNestField.Star2Probability => ReplaceProbability(entry, probabilityIndex: 1, value),
            SwShEncounterNestField.Star3Probability => ReplaceProbability(entry, probabilityIndex: 2, value),
            SwShEncounterNestField.Star4Probability => ReplaceProbability(entry, probabilityIndex: 3, value),
            SwShEncounterNestField.Star5Probability => ReplaceProbability(entry, probabilityIndex: 4, value),
            SwShEncounterNestField.Gender => entry with { Gender = value },
            SwShEncounterNestField.FlawlessIvs => entry with { FlawlessIvs = value },
            _ => throw new ArgumentOutOfRangeException(nameof(edit), $"Raid battle field '{edit.Field}' is not supported."),
        };

        if (tables is SwShEncounterNestTable[] mutableTables)
        {
            mutableTables[edit.TableIndex] = table with { Entries = entries };
        }
    }

    private static SwShEncounterNest ReplaceProbability(
        SwShEncounterNest entry,
        int probabilityIndex,
        int value)
    {
        if (entry.Probabilities is not uint[] probabilities)
        {
            throw new InvalidDataException("Raid battle probability list is not mutable.");
        }

        if (probabilities.Length < MinimumProbabilityCount)
        {
            throw new InvalidDataException(
                $"Raid battle entry contains {probabilities.Length} star probabilities; at least {MinimumProbabilityCount} are required.");
        }

        probabilities[probabilityIndex] = checked((uint)value);

        return entry with { Probabilities = probabilities };
    }

    private static int ValidateRange(int value, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Raid battle value {value} is outside the supported range {minimum}-{maximum}.");
        }

        return value;
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

    private static int ReadTableInt32(ReadOnlySpan<byte> data, TableLayout table, int fieldIndex)
    {
        var fieldOffset = ReadTableFieldOffset(table, fieldIndex);
        return fieldOffset == 0
            ? 0
            : BinaryPrimitives.ReadInt32LittleEndian(data.Slice(
                checked(table.TableOffset + fieldOffset),
                sizeof(int)));
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

    private static int ReadTableByte(ReadOnlySpan<byte> data, TableLayout table, int fieldIndex)
    {
        var fieldOffset = ReadTableFieldOffset(table, fieldIndex);
        return fieldOffset == 0
            ? 0
            : data[checked(table.TableOffset + fieldOffset)];
    }

    private static int ReadTableSByte(ReadOnlySpan<byte> data, TableLayout table, int fieldIndex)
    {
        return unchecked((sbyte)(byte)ReadTableByte(data, table, fieldIndex));
    }

    private static bool ReadTableBool(ReadOnlySpan<byte> data, TableLayout table, int fieldIndex)
    {
        return ReadTableByte(data, table, fieldIndex) != 0;
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
            throw new InvalidDataException("FlatBuffer offset points outside the encounter nest archive.");
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
            throw new InvalidDataException("FlatBuffer offset points outside the encounter nest archive.");
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

    private sealed record SourceEncounterTableLayout(
        int TableVectorElementOffset,
        int TableOffset,
        int EntriesVectorOffset,
        IReadOnlyList<SourceEncounterLayout> Entries);

    private sealed record SourceEncounterLayout(
        int EntryVectorElementOffset,
        int EntryOffset,
        IReadOnlyList<int> FieldValueOffsets,
        int ProbabilitiesFieldOffset,
        int ProbabilitiesVectorOffset,
        int VtableLength,
        bool HasMaterializedUnknownFields);

    private sealed record EncounterEditKey(
        int TableIndex,
        int EntryIndex,
        SwShEncounterNestField Field);

    private sealed class EffectiveEncounterTableLayout
    {
        public EffectiveEncounterTableLayout(SourceEncounterTableLayout source, int delta, bool isolated)
        {
            Isolated = isolated;
            Entries = source.Entries
                .Select(entry => new EffectiveEncounterLayout(entry, delta, isolated: false))
                .ToArray();
        }

        public bool Isolated { get; }

        public EffectiveEncounterLayout[] Entries { get; }
    }

    private sealed class EffectiveEncounterLayout
    {
        public EffectiveEncounterLayout(
            int entryVectorElementOffset,
            int entryOffset,
            IReadOnlyList<int> fieldValueOffsets,
            int probabilitiesFieldOffset,
            int probabilitiesVectorOffset)
        {
            EntryVectorElementOffset = entryVectorElementOffset;
            EntryOffset = entryOffset;
            FieldValueOffsets = fieldValueOffsets;
            ProbabilitiesFieldOffset = probabilitiesFieldOffset;
            ProbabilitiesVectorOffset = probabilitiesVectorOffset;
            Isolated = true;
        }

        public EffectiveEncounterLayout(SourceEncounterLayout source, int delta, bool isolated)
        {
            EntryVectorElementOffset = checked(source.EntryVectorElementOffset + delta);
            EntryOffset = checked(source.EntryOffset + delta);
            FieldValueOffsets = source.FieldValueOffsets
                .Select(offset => offset < 0 ? -1 : checked(offset + delta))
                .ToArray();
            ProbabilitiesFieldOffset = checked(source.ProbabilitiesFieldOffset + delta);
            ProbabilitiesVectorOffset = checked(source.ProbabilitiesVectorOffset + delta);
            Isolated = isolated;
        }

        public int EntryVectorElementOffset { get; }

        public int EntryOffset { get; }

        public IReadOnlyList<int> FieldValueOffsets { get; }

        public int ProbabilitiesFieldOffset { get; }

        public int ProbabilitiesVectorOffset { get; set; }

        public bool Isolated { get; }

        public int GetFieldValueOffset(int fieldIndex)
        {
            return (uint)fieldIndex < (uint)FieldValueOffsets.Count
                ? FieldValueOffsets[fieldIndex]
                : -1;
        }
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
    }

    private sealed record StructuralRange(
        int Offset,
        int Length,
        string Label,
        string Kind,
        bool AllowExactAlias);

    private sealed class EncounterNestFlatBufferWriter
    {
        private readonly List<byte> bytes = [];

        public void Write(SwShEncounterNestArchive archive)
        {
            WriteUInt32(0);
            var root = WriteArchiveTable();
            WriteUInt32At(0, checked((uint)root.TableOffset));

            var tableVector = WriteTableVector(archive.Tables.Count);
            PatchUOffset(root.Field0Offset, tableVector.VectorOffset);
            for (var index = 0; index < archive.Tables.Count; index++)
            {
                var tableOffset = WriteNestTable(archive.Tables[index]);
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

        private int WriteNestTable(SwShEncounterNestTable table)
        {
            AlignForTable(vtableLength: 10, alignment: 8);
            var vtableOffset = Position;
            WriteUInt16(10);
            WriteUInt16(24);
            WriteUInt16(16);
            WriteUInt16(8);
            WriteUInt16(4);
            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            var entriesFieldOffset = Position;
            WriteUInt32(0);
            WriteInt32(table.GameVersion);
            WriteUInt32(0);
            WriteUInt64(table.TableId);

            var entriesVector = WriteTableVector(table.Entries.Count);
            PatchUOffset(entriesFieldOffset, entriesVector.VectorOffset);
            for (var index = 0; index < table.Entries.Count; index++)
            {
                var entryOffset = WriteNestEntry(table.Entries[index]);
                PatchUOffset(entriesVector.ElementOffsets[index], entryOffset);
            }

            return tableOffset;
        }

        private int WriteNestEntry(SwShEncounterNest entry)
        {
            if (entry.Probabilities.Count < MinimumProbabilityCount)
            {
                throw new InvalidDataException(
                    $"Raid battle entry contains {entry.Probabilities.Count} star probabilities; at least {MinimumProbabilityCount} are required.");
            }

            AlignForTable(vtableLength: 26, alignment: 8);
            var vtableOffset = Position;
            WriteUInt16(26);
            WriteUInt16(48);
            WriteUInt16(8);
            WriteUInt16(12);
            WriteUInt16(16);
            WriteUInt16(24);
            WriteUInt16(20);
            WriteUInt16(21);
            WriteUInt16(32);
            WriteUInt16(40);
            WriteUInt16(4);
            WriteUInt16(22);
            WriteUInt16(23);
            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            var probabilitiesFieldOffset = Position;
            WriteUInt32(0);
            WriteInt32(entry.EntryIndex);
            WriteInt32(entry.Species);
            WriteInt32(entry.Form);
            WriteByte(checked((byte)entry.Ability));
            WriteByte(entry.IsGigantamax ? (byte)1 : (byte)0);
            WriteByte(unchecked((byte)(sbyte)entry.Gender));
            WriteByte(unchecked((byte)(sbyte)entry.FlawlessIvs));
            WriteUInt64(entry.LevelTableId);
            WriteUInt64(entry.DropTableId);
            WriteUInt64(entry.BonusTableId);

            var probabilitiesVector = WriteUIntVector(entry.Probabilities);
            PatchUOffset(probabilitiesFieldOffset, probabilitiesVector);

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

        private void WriteByte(byte value)
        {
            bytes.Add(value);
        }

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
