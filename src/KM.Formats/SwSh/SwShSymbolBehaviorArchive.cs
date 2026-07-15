// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace KM.Formats.SwSh;

public enum SwShSymbolBehaviorFieldType
{
    Single,
    Int32,
    Byte,
    UInt64,
    String,
}

public sealed record SwShSymbolBehaviorFieldSpec(
    string Field,
    int FieldIndex,
    SwShSymbolBehaviorFieldType FieldType,
    ushort ObjectOffset,
    bool IsUnusedDefault = false);

public sealed record SwShSymbolBehaviorFieldValue(
    string Field,
    int FieldIndex,
    SwShSymbolBehaviorFieldType FieldType,
    object Value);

public sealed record SwShSymbolBehaviorEntry(
    int Index,
    IReadOnlyList<SwShSymbolBehaviorFieldValue> Fields)
{
    public int SpeciesId => GetInt32(SwShSymbolBehaviorArchive.SpeciesIdField);
    public int Form => GetInt32(SwShSymbolBehaviorArchive.FormField);
    public string Behavior => GetString(SwShSymbolBehaviorArchive.BehaviorField);
    public string ModelPart => GetString(SwShSymbolBehaviorArchive.ModelPartField);
    public string InternalSpeciesName => GetString(SwShSymbolBehaviorArchive.InternalSpeciesNameField);
    public float HitboxRadius => GetSingle(SwShSymbolBehaviorArchive.HitboxRadiusField);
    public float GrassShakeRadius => GetSingle(SwShSymbolBehaviorArchive.GrassShakeRadiusField);
    public ulong Hash1 => GetUInt64(SwShSymbolBehaviorArchive.Hash1Field);
    public ulong Hash2 => GetUInt64(SwShSymbolBehaviorArchive.Hash2Field);

    public SwShSymbolBehaviorEntry WithField(string field, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentNullException.ThrowIfNull(value);

        var values = Fields.ToArray();
        var index = Array.FindIndex(values, candidate => string.Equals(candidate.Field, field, StringComparison.Ordinal));
        if (index < 0)
        {
            throw new InvalidDataException($"Symbol behavior field '{field}' is not present.");
        }

        var current = values[index];
        values[index] = current with { Value = ParseValue(current.FieldType, value) };

        return this with { Fields = values };
    }

    public string GetStringValue(string field)
    {
        var value = GetField(field).Value;
        return value switch
        {
            float single => single.ToString("R", CultureInfo.InvariantCulture),
            int integer => integer.ToString(CultureInfo.InvariantCulture),
            byte byteValue => byteValue.ToString(CultureInfo.InvariantCulture),
            ulong unsigned => unsigned.ToString(CultureInfo.InvariantCulture),
            string text => text,
            _ => value?.ToString() ?? string.Empty,
        };
    }

    private static object ParseValue(SwShSymbolBehaviorFieldType fieldType, string value)
    {
        return fieldType switch
        {
            SwShSymbolBehaviorFieldType.Single => ParseSingle(value),
            SwShSymbolBehaviorFieldType.Int32 => ParseInt32(value),
            SwShSymbolBehaviorFieldType.Byte => ParseByte(value),
            SwShSymbolBehaviorFieldType.UInt64 => ParseUInt64(value),
            SwShSymbolBehaviorFieldType.String => value,
            _ => throw new ArgumentOutOfRangeException(nameof(fieldType), $"Symbol behavior field type '{fieldType}' is not supported."),
        };
    }

    private static float ParseSingle(string value)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || float.IsNaN(parsed)
            || float.IsInfinity(parsed))
        {
            throw new InvalidDataException($"Symbol behavior value '{value}' is not a finite number.");
        }

        return parsed;
    }

    private static int ParseInt32(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidDataException($"Symbol behavior value '{value}' is not a valid integer.");
        }

        return parsed;
    }

    private static byte ParseByte(string value)
    {
        if (!byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidDataException($"Symbol behavior value '{value}' is not a byte value.");
        }

        return parsed;
    }

    private static ulong ParseUInt64(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (ulong.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            {
                return hex;
            }
        }

        if (!ulong.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidDataException($"Symbol behavior value '{value}' is not an unsigned 64-bit value.");
        }

        return parsed;
    }

    private float GetSingle(string field) => (float)GetField(field).Value;
    private int GetInt32(string field) => (int)GetField(field).Value;
    private ulong GetUInt64(string field) => (ulong)GetField(field).Value;
    private string GetString(string field) => (string)GetField(field).Value;

    private SwShSymbolBehaviorFieldValue GetField(string field)
    {
        return Fields.FirstOrDefault(candidate => string.Equals(candidate.Field, field, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Symbol behavior field '{field}' is not present.");
    }
}

public sealed record SwShSymbolBehaviorEdit(
    int EntryIndex,
    string Field,
    string Value);

public sealed record SwShSymbolBehaviorArchive(
    IReadOnlyList<SwShSymbolBehaviorEntry> Entries)
{
    public const string Field00 = "field00";
    public const string Field01 = "field01";
    public const string ModelPartField = "modelPart";
    public const string Field03 = "field03";
    public const string Hash1Field = "hash1";
    public const string Hash2Field = "hash2";
    public const string HitboxRadiusField = "hitboxRadius";
    public const string Field07 = "field07";
    public const string Field08 = "field08";
    public const string Field09 = "field09";
    public const string FormField = "form";
    public const string Field11 = "field11";
    public const string Field12 = "field12";
    public const string SpeciesIdField = "speciesId";
    public const string Field14 = "field14";
    public const string Field15 = "field15";
    public const string Field16 = "field16";
    public const string Field17 = "field17";
    public const string Field18 = "field18";
    public const string Field19 = "field19";
    public const string Field20 = "field20";
    public const string Field21 = "field21";
    public const string InternalSpeciesNameField = "internalSpeciesName";
    public const string Field23 = "field23";
    public const string Field24 = "field24";
    public const string Field25 = "field25";
    public const string Field26 = "field26";
    public const string GrassShakeRadiusField = "grassShakeRadius";
    public const string Field28 = "field28";
    public const string Field29 = "field29";
    public const string Field30 = "field30";
    public const string BehaviorField = "behavior";
    public const string Field32 = "field32";
    public const string Field33 = "field33";
    public const string Field34 = "field34";
    public const string Field35 = "field35";
    public const string Field36 = "field36";
    public const string Field37 = "field37";
    public const string Field38 = "field38";
    public const string Field39 = "field39";
    public const string Field40 = "field40";
    public const string Field41 = "field41";
    public const string Field42 = "field42";
    public const string Field43 = "field43";
    public const string Field44 = "field44";
    public const string Field45 = "field45";

    public static readonly IReadOnlyList<SwShSymbolBehaviorFieldSpec> FieldSpecs =
    [
        new(Field00, 0, SwShSymbolBehaviorFieldType.Single, 4),
        new(Field01, 1, SwShSymbolBehaviorFieldType.Single, 8),
        new(ModelPartField, 2, SwShSymbolBehaviorFieldType.String, 12),
        new(Field03, 3, SwShSymbolBehaviorFieldType.Single, 16),
        new(Hash1Field, 4, SwShSymbolBehaviorFieldType.UInt64, 24),
        new(Hash2Field, 5, SwShSymbolBehaviorFieldType.UInt64, 32),
        new(HitboxRadiusField, 6, SwShSymbolBehaviorFieldType.Single, 40),
        new(Field07, 7, SwShSymbolBehaviorFieldType.Single, 44),
        new(Field08, 8, SwShSymbolBehaviorFieldType.Single, 48, IsUnusedDefault: true),
        new(Field09, 9, SwShSymbolBehaviorFieldType.Single, 52),
        new(FormField, 10, SwShSymbolBehaviorFieldType.Int32, 56),
        new(Field11, 11, SwShSymbolBehaviorFieldType.Byte, 60),
        new(Field12, 12, SwShSymbolBehaviorFieldType.Byte, 61, IsUnusedDefault: true),
        new(SpeciesIdField, 13, SwShSymbolBehaviorFieldType.Int32, 64),
        new(Field14, 14, SwShSymbolBehaviorFieldType.Byte, 68, IsUnusedDefault: true),
        new(Field15, 15, SwShSymbolBehaviorFieldType.Byte, 69, IsUnusedDefault: true),
        new(Field16, 16, SwShSymbolBehaviorFieldType.Single, 72),
        new(Field17, 17, SwShSymbolBehaviorFieldType.Single, 76),
        new(Field18, 18, SwShSymbolBehaviorFieldType.Int32, 80),
        new(Field19, 19, SwShSymbolBehaviorFieldType.Single, 84),
        new(Field20, 20, SwShSymbolBehaviorFieldType.Single, 88),
        new(Field21, 21, SwShSymbolBehaviorFieldType.Single, 92),
        new(InternalSpeciesNameField, 22, SwShSymbolBehaviorFieldType.String, 96),
        new(Field23, 23, SwShSymbolBehaviorFieldType.Single, 100),
        new(Field24, 24, SwShSymbolBehaviorFieldType.Single, 104),
        new(Field25, 25, SwShSymbolBehaviorFieldType.Single, 108),
        new(Field26, 26, SwShSymbolBehaviorFieldType.Single, 112),
        new(GrassShakeRadiusField, 27, SwShSymbolBehaviorFieldType.Single, 116),
        new(Field28, 28, SwShSymbolBehaviorFieldType.Single, 120, IsUnusedDefault: true),
        new(Field29, 29, SwShSymbolBehaviorFieldType.Int32, 124),
        new(Field30, 30, SwShSymbolBehaviorFieldType.Int32, 128, IsUnusedDefault: true),
        new(BehaviorField, 31, SwShSymbolBehaviorFieldType.String, 132),
        new(Field32, 32, SwShSymbolBehaviorFieldType.Int32, 136),
        new(Field33, 33, SwShSymbolBehaviorFieldType.Int32, 140, IsUnusedDefault: true),
        new(Field34, 34, SwShSymbolBehaviorFieldType.Int32, 144, IsUnusedDefault: true),
        new(Field35, 35, SwShSymbolBehaviorFieldType.Int32, 148, IsUnusedDefault: true),
        new(Field36, 36, SwShSymbolBehaviorFieldType.Int32, 152, IsUnusedDefault: true),
        new(Field37, 37, SwShSymbolBehaviorFieldType.Single, 156),
        new(Field38, 38, SwShSymbolBehaviorFieldType.Single, 160),
        new(Field39, 39, SwShSymbolBehaviorFieldType.Single, 164),
        new(Field40, 40, SwShSymbolBehaviorFieldType.Single, 168),
        new(Field41, 41, SwShSymbolBehaviorFieldType.Single, 172),
        new(Field42, 42, SwShSymbolBehaviorFieldType.Single, 176, IsUnusedDefault: true),
        new(Field43, 43, SwShSymbolBehaviorFieldType.Single, 180, IsUnusedDefault: true),
        new(Field44, 44, SwShSymbolBehaviorFieldType.Single, 184),
        new(Field45, 45, SwShSymbolBehaviorFieldType.Single, 188),
    ];

    private const int EntryFieldCount = 46;
    private const int EntryVtableLength = sizeof(ushort) * 2 + (EntryFieldCount * sizeof(ushort));
    private const int EntryObjectLength = 192;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly HashSet<int> RequiredStringFieldIndexes = [2, 22, 31];

    private static readonly IReadOnlyList<FieldLayout> KnownEntryFieldLayouts = FieldSpecs
        .Select(spec => new FieldLayout(spec.FieldIndex, GetFieldSize(spec.FieldType), GetFieldAlignment(spec.FieldType)))
        .ToArray();

    private byte[]? SourceData { get; init; }

    private IReadOnlyList<SourceBehaviorEntryLayout>? SourceEntryLayouts { get; init; }

    private IReadOnlyList<SwShSymbolBehaviorEntry>? SourceEntries { get; init; }

    public static SwShSymbolBehaviorArchive Parse(ReadOnlySpan<byte> data)
    {
        try
        {
            return ParseCore(data);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                "Symbol behavior archive contains an invalid count, size, or offset.",
                exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Symbol behavior archive contains invalid UTF-8 text.", exception);
        }
    }

    private static SwShSymbolBehaviorArchive ParseCore(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("Symbol behavior archive is too small to contain a FlatBuffer root.");
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
        var entryVectorOffset = ReadTableUOffset(
            data,
            rootTable,
            fieldIndex: 0,
            required: true,
            targetAlignment: sizeof(uint));
        var entryCount = ReadAndRegisterVector(
            data,
            entryVectorOffset,
            elementSize: sizeof(uint),
            "symbol behavior entry vector",
            "symbol behavior entry vector",
            ranges);
        var entries = new SwShSymbolBehaviorEntry[entryCount];
        var entryLayouts = new SourceBehaviorEntryLayout[entryCount];

        for (var entryIndex = 0; entryIndex < entryCount; entryIndex++)
        {
            var vectorElementOffset = checked(
                entryVectorOffset + sizeof(uint) + (entryIndex * sizeof(uint)));
            var tableOffset = ReadUOffset(data, vectorElementOffset, targetAlignment: sizeof(uint));
            var table = ReadTableLayout(
                data,
                tableOffset,
                $"symbol behavior entry {entryIndex}",
                "symbol behavior entry table",
                alignment: sizeof(uint),
                ranges,
                KnownEntryFieldLayouts);
            var values = new SwShSymbolBehaviorFieldValue[FieldSpecs.Count];
            var fieldValueOffsets = new int[FieldSpecs.Count];

            for (var specIndex = 0; specIndex < FieldSpecs.Count; specIndex++)
            {
                var spec = FieldSpecs[specIndex];
                var fieldOffset = ReadTableFieldOffset(table, spec.FieldIndex);
                fieldValueOffsets[specIndex] = fieldOffset == 0
                    ? -1
                    : checked(tableOffset + fieldOffset);
                values[specIndex] = new SwShSymbolBehaviorFieldValue(
                    spec.Field,
                    spec.FieldIndex,
                    spec.FieldType,
                    ReadField(
                        data,
                        table,
                        spec,
                        RequiredStringFieldIndexes.Contains(spec.FieldIndex),
                        ranges,
                        $"symbol behavior entry {entryIndex} field {spec.FieldIndex}"));
            }

            entries[entryIndex] = new SwShSymbolBehaviorEntry(entryIndex, values);
            entryLayouts[entryIndex] = new SourceBehaviorEntryLayout(
                vectorElementOffset,
                tableOffset,
                table.VtableLength,
                fieldValueOffsets,
                table.FieldOffsets.Skip(EntryFieldCount).Any(offset => offset != 0));
        }

        return new SwShSymbolBehaviorArchive(entries)
        {
            SourceData = data.ToArray(),
            SourceEntryLayouts = entryLayouts,
            SourceEntries = CloneEntries(entries),
        };
    }

    public byte[] Write()
    {
        try
        {
            if (SourceData is not null)
            {
                return WriteSourceEdits(CreateSourceDiffEdits());
            }

            return WriteByRebuilding();
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                "Symbol behavior archive contains an invalid count, size, or offset.",
                exception);
        }
        catch (EncoderFallbackException exception)
        {
            throw new InvalidDataException("Symbol behavior archive contains text that cannot be encoded as UTF-8.", exception);
        }
    }

    public byte[] WriteEdits(IEnumerable<SwShSymbolBehaviorEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        try
        {
            var materializedEdits = edits.ToArray();
            if (SourceData is not null)
            {
                return WriteSourceEdits(CreateSourceDiffEdits().Concat(materializedEdits).ToArray());
            }

            var entries = CloneEntries(Entries);
            foreach (var edit in materializedEdits)
            {
                ValidateEntryIndex(edit.EntryIndex, entries.Length);
                entries[edit.EntryIndex] = entries[edit.EntryIndex].WithField(edit.Field, edit.Value);
            }

            return new SwShSymbolBehaviorArchive(entries).WriteByRebuilding();
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                "Symbol behavior archive contains an invalid count, size, or offset.",
                exception);
        }
        catch (EncoderFallbackException exception)
        {
            throw new InvalidDataException("Symbol behavior archive contains text that cannot be encoded as UTF-8.", exception);
        }
    }

    private byte[] WriteByRebuilding()
    {
        var writer = new SymbolBehaviorFlatBufferWriter();
        writer.Write(this);
        return writer.ToArray();
    }

    private IReadOnlyList<SwShSymbolBehaviorEdit> CreateSourceDiffEdits()
    {
        var sourceEntries = SourceEntries
            ?? throw new InvalidDataException("Symbol behavior source entries are unavailable.");
        if (Entries.Count != sourceEntries.Count)
        {
            throw new InvalidDataException("Parsed symbol behavior entries cannot be added or removed safely.");
        }

        var edits = new List<SwShSymbolBehaviorEdit>();
        for (var entryIndex = 0; entryIndex < Entries.Count; entryIndex++)
        {
            var current = Entries[entryIndex];
            var source = sourceEntries[entryIndex];
            if (current.Index != source.Index || current.Fields.Count != source.Fields.Count)
            {
                throw new InvalidDataException("Parsed symbol behavior entry structure was modified and cannot be written safely.");
            }

            for (var fieldIndex = 0; fieldIndex < source.Fields.Count; fieldIndex++)
            {
                var currentField = current.Fields[fieldIndex];
                var sourceField = source.Fields[fieldIndex];
                if (!string.Equals(currentField.Field, sourceField.Field, StringComparison.Ordinal)
                    || currentField.FieldIndex != sourceField.FieldIndex
                    || currentField.FieldType != sourceField.FieldType)
                {
                    throw new InvalidDataException("Parsed symbol behavior field structure was modified and cannot be written safely.");
                }

                if (!Equals(currentField.Value, sourceField.Value))
                {
                    edits.Add(new SwShSymbolBehaviorEdit(
                        entryIndex,
                        currentField.Field,
                        current.GetStringValue(currentField.Field)));
                }
            }
        }

        return edits;
    }

    private byte[] WriteSourceEdits(IReadOnlyList<SwShSymbolBehaviorEdit> edits)
    {
        var source = SourceData
            ?? throw new InvalidDataException("Symbol behavior source bytes are unavailable.");
        var sourceEntries = SourceEntries
            ?? throw new InvalidDataException("Symbol behavior source entries are unavailable.");
        var layouts = SourceEntryLayouts
            ?? throw new InvalidDataException("Symbol behavior source layout is unavailable.");
        if (sourceEntries.Count != layouts.Count)
        {
            throw new InvalidDataException("Symbol behavior source layout does not match the parsed entry count.");
        }

        var finalValues = new Dictionary<BehaviorEditKey, BehaviorEditValue>();
        foreach (var edit in edits)
        {
            ValidateEntryIndex(edit.EntryIndex, sourceEntries.Count);
            var spec = GetFieldSpec(edit.Field);
            var updated = sourceEntries[edit.EntryIndex].WithField(spec.Field, edit.Value);
            var fieldValue = updated.Fields.Single(field =>
                field.FieldIndex == spec.FieldIndex
                && string.Equals(field.Field, spec.Field, StringComparison.Ordinal));
            finalValues[new BehaviorEditKey(edit.EntryIndex, spec.FieldIndex)] = new BehaviorEditValue(
                spec,
                fieldValue.Value);
        }

        var effectiveEdits = finalValues
            .Where(pair => !Equals(
                sourceEntries[pair.Key.EntryIndex].Fields[pair.Key.FieldIndex].Value,
                pair.Value.Value))
            .OrderBy(pair => pair.Key.EntryIndex)
            .ThenBy(pair => pair.Key.FieldIndex)
            .ToArray();
        if (effectiveEdits.Length == 0)
        {
            return source.ToArray();
        }

        var output = new List<byte>(source.Length);
        output.AddRange(source);

        foreach (var entryEdits in effectiveEdits.GroupBy(pair => pair.Key.EntryIndex))
        {
            var entryIndex = entryEdits.Key;
            var sourceLayout = layouts[entryIndex];
            var needsMaterialization = entryEdits.Any(pair =>
                sourceLayout.GetFieldValueOffset(pair.Key.FieldIndex) < 0);
            EffectiveBehaviorEntryLayout effectiveLayout;

            if (needsMaterialization)
            {
                if (sourceLayout.HasMaterializedUnknownFields)
                {
                    throw new InvalidDataException(
                        "Symbol behavior entry contains unknown materialized fields, so an omitted editable field cannot be materialized safely.");
                }

                effectiveLayout = AppendMaterializedEntry(
                    output,
                    sourceLayout.VectorElementOffset,
                    sourceLayout.VtableLength,
                    sourceEntries[entryIndex]);
            }
            else if (IsEntryAliased(layouts, entryIndex))
            {
                var delta = AppendSourceCopy(output, source);
                PatchUOffset(
                    output,
                    sourceLayout.VectorElementOffset,
                    checked(sourceLayout.TableOffset + delta));
                effectiveLayout = new EffectiveBehaviorEntryLayout(sourceLayout, delta);
            }
            else
            {
                effectiveLayout = new EffectiveBehaviorEntryLayout(sourceLayout, delta: 0);
            }

            foreach (var edit in entryEdits)
            {
                PatchSourceEdit(output, effectiveLayout, edit.Value.Spec, edit.Value.Value);
            }
        }

        return output.ToArray();
    }

    private static EffectiveBehaviorEntryLayout AppendMaterializedEntry(
        List<byte> output,
        int vectorElementOffset,
        int sourceVtableLength,
        SwShSymbolBehaviorEntry entry)
    {
        var vtableLength = Math.Max(sourceVtableLength, EntryVtableLength);
        while (((output.Count + vtableLength) % sizeof(ulong)) != 0)
        {
            output.Add(0);
        }

        var vtableOffset = output.Count;
        var vtable = new byte[vtableLength];
        BinaryPrimitives.WriteUInt16LittleEndian(
            vtable.AsSpan(0, sizeof(ushort)),
            checked((ushort)vtableLength));
        BinaryPrimitives.WriteUInt16LittleEndian(
            vtable.AsSpan(sizeof(ushort), sizeof(ushort)),
            EntryObjectLength);
        foreach (var spec in FieldSpecs)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                vtable.AsSpan((sizeof(ushort) * 2) + (spec.FieldIndex * sizeof(ushort)), sizeof(ushort)),
                spec.ObjectOffset);
        }

        output.AddRange(vtable);
        var tableOffset = output.Count;
        var table = new byte[EntryObjectLength];
        BinaryPrimitives.WriteInt32LittleEndian(
            table.AsSpan(0, sizeof(int)),
            checked(tableOffset - vtableOffset));
        var valuesByField = CreateValuesByField(entry);
        foreach (var spec in FieldSpecs.Where(spec => spec.FieldType != SwShSymbolBehaviorFieldType.String))
        {
            WriteValueToSpan(table, spec.ObjectOffset, spec.FieldType, valuesByField[spec.Field].Value);
        }

        output.AddRange(table);
        foreach (var spec in FieldSpecs.Where(spec => spec.FieldType == SwShSymbolBehaviorFieldType.String))
        {
            var stringOffset = AppendString(output, (string)valuesByField[spec.Field].Value);
            PatchUOffset(output, checked(tableOffset + spec.ObjectOffset), stringOffset);
        }

        PatchUOffset(output, vectorElementOffset, tableOffset);
        return new EffectiveBehaviorEntryLayout(
            vectorElementOffset,
            tableOffset,
            FieldSpecs.Select(spec => checked(tableOffset + spec.ObjectOffset)).ToArray());
    }

    private static Dictionary<string, SwShSymbolBehaviorFieldValue> CreateValuesByField(
        SwShSymbolBehaviorEntry entry)
    {
        var values = new Dictionary<string, SwShSymbolBehaviorFieldValue>(StringComparer.Ordinal);
        foreach (var value in entry.Fields)
        {
            if (!values.TryAdd(value.Field, value))
            {
                throw new InvalidDataException(
                    $"Symbol behavior field '{value.Field}' appears more than once in entry {entry.Index}.");
            }
        }

        foreach (var spec in FieldSpecs)
        {
            if (!values.TryGetValue(spec.Field, out var value)
                || value.FieldIndex != spec.FieldIndex
                || value.FieldType != spec.FieldType)
            {
                throw new InvalidDataException(
                    $"Symbol behavior field '{spec.Field}' is missing or has invalid metadata in entry {entry.Index}.");
            }
        }

        return values;
    }

    private static void PatchSourceEdit(
        List<byte> output,
        EffectiveBehaviorEntryLayout entry,
        SwShSymbolBehaviorFieldSpec spec,
        object value)
    {
        var valueOffset = entry.GetFieldValueOffset(spec.FieldIndex);
        if (valueOffset < 0)
        {
            throw new InvalidDataException(
                $"Symbol behavior field '{spec.Field}' is omitted and cannot be materialized safely.");
        }

        if (spec.FieldType == SwShSymbolBehaviorFieldType.String)
        {
            var stringOffset = AppendString(output, (string)value);
            PatchUOffset(output, valueOffset, stringOffset);
            return;
        }

        switch (spec.FieldType)
        {
            case SwShSymbolBehaviorFieldType.Single:
                WriteSingleAt(output, valueOffset, (float)value);
                break;
            case SwShSymbolBehaviorFieldType.Int32:
                WriteInt32At(output, valueOffset, (int)value);
                break;
            case SwShSymbolBehaviorFieldType.Byte:
                WriteByteAt(output, valueOffset, (byte)value);
                break;
            case SwShSymbolBehaviorFieldType.UInt64:
                WriteUInt64At(output, valueOffset, (ulong)value);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(spec),
                    $"Symbol behavior field type '{spec.FieldType}' is not supported.");
        }
    }

    public static SwShSymbolBehaviorFieldSpec GetFieldSpec(string field)
    {
        return FieldSpecs.FirstOrDefault(candidate => string.Equals(candidate.Field, field, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Symbol behavior field '{field}' is not supported.");
    }

    private static object ReadField(
        ReadOnlySpan<byte> data,
        TableLayout table,
        SwShSymbolBehaviorFieldSpec spec,
        bool required,
        StructuralRangeRegistry ranges,
        string label)
    {
        return spec.FieldType switch
        {
            SwShSymbolBehaviorFieldType.Single => ReadTableSingle(data, table, spec.FieldIndex),
            SwShSymbolBehaviorFieldType.Int32 => ReadTableInt32(data, table, spec.FieldIndex),
            SwShSymbolBehaviorFieldType.Byte => ReadTableByte(data, table, spec.FieldIndex),
            SwShSymbolBehaviorFieldType.UInt64 => ReadTableUInt64(data, table, spec.FieldIndex),
            SwShSymbolBehaviorFieldType.String => ReadTableString(
                data,
                table,
                spec.FieldIndex,
                required,
                ranges,
                label),
            _ => throw new ArgumentOutOfRangeException(nameof(spec), $"Symbol behavior field type '{spec.FieldType}' is not supported."),
        };
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

    private static string ReadTableString(
        ReadOnlySpan<byte> data,
        TableLayout table,
        int fieldIndex,
        bool required,
        StructuralRangeRegistry ranges,
        string label)
    {
        var stringOffset = ReadTableUOffset(
            data,
            table,
            fieldIndex,
            required,
            targetAlignment: sizeof(uint));
        return stringOffset == 0 ? string.Empty : ReadString(data, stringOffset, label, ranges);
    }

    private static float ReadTableSingle(ReadOnlySpan<byte> data, TableLayout table, int fieldIndex)
    {
        var fieldOffset = ReadTableFieldOffset(table, fieldIndex);
        if (fieldOffset == 0)
        {
            return 0;
        }

        var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(
            checked(table.TableOffset + fieldOffset),
            sizeof(int))));
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException(
                $"Symbol behavior field {fieldIndex} contains a non-finite floating-point value.");
        }

        return value;
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

    private static byte ReadTableByte(ReadOnlySpan<byte> data, TableLayout table, int fieldIndex)
    {
        var fieldOffset = ReadTableFieldOffset(table, fieldIndex);
        return fieldOffset == 0 ? (byte)0 : data[checked(table.TableOffset + fieldOffset)];
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

    private static int ReadTableFieldOffset(TableLayout table, int fieldIndex)
    {
        return (uint)fieldIndex < (uint)table.FieldOffsets.Count
            ? table.FieldOffsets[fieldIndex]
            : 0;
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
        var objectLength = BinaryPrimitives.ReadUInt16LittleEndian(
            data.Slice(vtableOffset + sizeof(ushort), sizeof(ushort)));
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
                throw new InvalidDataException($"{label} field {fieldIndex} points outside its table object.");
            }

            fieldOffsets[fieldIndex] = fieldOffset;
        }

        var knownRanges = new List<FieldRange>(knownFields.Count);
        foreach (var field in knownFields)
        {
            var fieldOffset = field.FieldIndex < fieldOffsets.Length ? fieldOffsets[field.FieldIndex] : 0;
            if (fieldOffset == 0)
            {
                continue;
            }

            if (fieldOffset > objectLength - field.Size)
            {
                throw new InvalidDataException($"{label} field {field.FieldIndex} exceeds its table object.");
            }

            if (((tableOffset + fieldOffset) % field.Alignment) != 0)
            {
                throw new InvalidDataException(
                    $"{label} field {field.FieldIndex} is not aligned to {field.Alignment} bytes.");
            }

            foreach (var existing in knownRanges)
            {
                if (RangesOverlap(fieldOffset, field.Size, existing.Offset, existing.Length))
                {
                    throw new InvalidDataException(
                        $"{label} fields {existing.FieldIndex} and {field.FieldIndex} overlap within the table object.");
                }
            }

            knownRanges.Add(new FieldRange(field.FieldIndex, fieldOffset, field.Size));
        }

        var knownIndexes = knownFields.Select(field => field.FieldIndex).ToHashSet();
        var unknownStarts = new HashSet<ushort>();
        for (var fieldIndex = 0; fieldIndex < fieldOffsets.Length; fieldIndex++)
        {
            var fieldOffset = fieldOffsets[fieldIndex];
            if (fieldOffset == 0 || knownIndexes.Contains(fieldIndex))
            {
                continue;
            }

            foreach (var known in knownRanges)
            {
                if (fieldOffset >= known.Offset && fieldOffset < known.Offset + known.Length)
                {
                    throw new InvalidDataException(
                        $"{label} unknown field {fieldIndex} aliases known field {known.FieldIndex}.");
                }
            }

            if (!unknownStarts.Add(fieldOffset))
            {
                throw new InvalidDataException($"{label} unknown fields alias the same object offset.");
            }
        }

        return new TableLayout(tableOffset, vtableOffset, vtableLength, objectLength, fieldOffsets);
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
            throw new InvalidDataException("FlatBuffer offset points outside the symbol behavior archive.");
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

    private static string ReadString(
        ReadOnlySpan<byte> data,
        int stringOffset,
        string label,
        StructuralRangeRegistry ranges)
    {
        EnsureRange(data, stringOffset, sizeof(uint));
        var byteCount = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(stringOffset, sizeof(uint)));
        var totalLength = sizeof(uint) + (long)byteCount + sizeof(byte);
        if (byteCount > int.MaxValue || totalLength > int.MaxValue)
        {
            throw new InvalidDataException($"{label} string is too large.");
        }

        EnsureRange(data, stringOffset, (int)totalLength);
        var terminatorOffset = checked(stringOffset + sizeof(uint) + (int)byteCount);
        if (data[terminatorOffset] != 0)
        {
            throw new InvalidDataException($"{label} string is missing its null terminator.");
        }

        ranges.Register(stringOffset, (int)totalLength, $"{label} string", "string", allowExactAlias: true);
        return StrictUtf8.GetString(data.Slice(stringOffset + sizeof(uint), (int)byteCount));
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

    private static int AppendString(List<byte> output, string value)
    {
        while ((output.Count % sizeof(uint)) != 0)
        {
            output.Add(0);
        }

        var bytes = StrictUtf8.GetBytes(value);
        var offset = output.Count;
        var lengthPrefix = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(lengthPrefix, checked((uint)bytes.Length));
        output.AddRange(lengthPrefix);
        output.AddRange(bytes);
        output.Add(0);
        return offset;
    }

    private static void PatchUOffset(List<byte> output, int sourceOffset, int targetOffset)
    {
        if ((sourceOffset % sizeof(uint)) != 0 || (targetOffset % sizeof(uint)) != 0)
        {
            throw new InvalidDataException("FlatBuffer copy-on-write offset is not 4-byte aligned.");
        }

        if (targetOffset <= sourceOffset)
        {
            throw new InvalidDataException("FlatBuffer copy-on-write target must point forward.");
        }

        WriteUInt32At(output, sourceOffset, checked((uint)(targetOffset - sourceOffset)));
    }

    private static void WriteValueToSpan(
        Span<byte> data,
        int offset,
        SwShSymbolBehaviorFieldType fieldType,
        object value)
    {
        switch (fieldType)
        {
            case SwShSymbolBehaviorFieldType.Single:
                BinaryPrimitives.WriteInt32LittleEndian(
                    data.Slice(offset, sizeof(int)),
                    BitConverter.SingleToInt32Bits((float)value));
                break;
            case SwShSymbolBehaviorFieldType.Int32:
                BinaryPrimitives.WriteInt32LittleEndian(data.Slice(offset, sizeof(int)), (int)value);
                break;
            case SwShSymbolBehaviorFieldType.Byte:
                data[offset] = (byte)value;
                break;
            case SwShSymbolBehaviorFieldType.UInt64:
                BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(offset, sizeof(ulong)), (ulong)value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fieldType), $"Field type '{fieldType}' is not scalar.");
        }
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
        EnsureOutputRange(output, offset, sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(
            CollectionsMarshal.AsSpan(output).Slice(offset, sizeof(int)),
            value);
    }

    private static void WriteUInt32At(List<byte> output, int offset, uint value)
    {
        EnsureOutputRange(output, offset, sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(
            CollectionsMarshal.AsSpan(output).Slice(offset, sizeof(uint)),
            value);
    }

    private static void WriteUInt64At(List<byte> output, int offset, ulong value)
    {
        EnsureOutputRange(output, offset, sizeof(ulong));
        BinaryPrimitives.WriteUInt64LittleEndian(
            CollectionsMarshal.AsSpan(output).Slice(offset, sizeof(ulong)),
            value);
    }

    private static void WriteSingleAt(List<byte> output, int offset, float value)
    {
        EnsureOutputRange(output, offset, sizeof(float));
        BinaryPrimitives.WriteInt32LittleEndian(
            CollectionsMarshal.AsSpan(output).Slice(offset, sizeof(int)),
            BitConverter.SingleToInt32Bits(value));
    }

    private static void EnsureOutputRange(List<byte> output, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > output.Count || length > output.Count - offset)
        {
            throw new InvalidDataException("FlatBuffer copy-on-write patch points outside the output.");
        }
    }

    private static int GetFieldSize(SwShSymbolBehaviorFieldType fieldType)
    {
        return fieldType switch
        {
            SwShSymbolBehaviorFieldType.Single => sizeof(float),
            SwShSymbolBehaviorFieldType.Int32 => sizeof(int),
            SwShSymbolBehaviorFieldType.Byte => sizeof(byte),
            SwShSymbolBehaviorFieldType.UInt64 => sizeof(ulong),
            SwShSymbolBehaviorFieldType.String => sizeof(uint),
            _ => throw new ArgumentOutOfRangeException(nameof(fieldType)),
        };
    }

    private static int GetFieldAlignment(SwShSymbolBehaviorFieldType fieldType)
    {
        return fieldType switch
        {
            SwShSymbolBehaviorFieldType.Single or
            SwShSymbolBehaviorFieldType.Int32 or
            SwShSymbolBehaviorFieldType.String => sizeof(uint),
            SwShSymbolBehaviorFieldType.Byte => sizeof(byte),
            SwShSymbolBehaviorFieldType.UInt64 => sizeof(ulong),
            _ => throw new ArgumentOutOfRangeException(nameof(fieldType)),
        };
    }

    private static void ValidateEntryIndex(int entryIndex, int count)
    {
        if ((uint)entryIndex >= (uint)count)
        {
            throw new InvalidDataException($"Symbol behavior entry {entryIndex} is not present.");
        }
    }

    private static bool IsEntryAliased(IReadOnlyList<SourceBehaviorEntryLayout> layouts, int entryIndex)
    {
        var tableOffset = layouts[entryIndex].TableOffset;
        return layouts.Count(layout => layout.TableOffset == tableOffset) > 1;
    }

    private static SwShSymbolBehaviorEntry[] CloneEntries(IReadOnlyList<SwShSymbolBehaviorEntry> entries)
    {
        return entries
            .Select(entry => entry with { Fields = entry.Fields.ToArray() })
            .ToArray();
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > data.Length || count > data.Length - offset)
        {
            throw new InvalidDataException("FlatBuffer offset points outside the symbol behavior archive.");
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

    private sealed record SourceBehaviorEntryLayout(
        int VectorElementOffset,
        int TableOffset,
        int VtableLength,
        IReadOnlyList<int> FieldValueOffsets,
        bool HasMaterializedUnknownFields)
    {
        public int GetFieldValueOffset(int fieldIndex)
        {
            return (uint)fieldIndex < (uint)FieldValueOffsets.Count
                ? FieldValueOffsets[fieldIndex]
                : -1;
        }
    }

    private sealed class EffectiveBehaviorEntryLayout
    {
        public EffectiveBehaviorEntryLayout(
            int vectorElementOffset,
            int tableOffset,
            IReadOnlyList<int> fieldValueOffsets)
        {
            VectorElementOffset = vectorElementOffset;
            TableOffset = tableOffset;
            FieldValueOffsets = fieldValueOffsets;
        }

        public EffectiveBehaviorEntryLayout(SourceBehaviorEntryLayout source, int delta)
            : this(
                source.VectorElementOffset,
                checked(source.TableOffset + delta),
                source.FieldValueOffsets
                    .Select(offset => offset < 0 ? -1 : checked(offset + delta))
                    .ToArray())
        {
        }

        public int VectorElementOffset { get; }

        public int TableOffset { get; }

        public IReadOnlyList<int> FieldValueOffsets { get; }

        public int GetFieldValueOffset(int fieldIndex)
        {
            return (uint)fieldIndex < (uint)FieldValueOffsets.Count
                ? FieldValueOffsets[fieldIndex]
                : -1;
        }
    }

    private sealed record BehaviorEditKey(int EntryIndex, int FieldIndex);

    private sealed record BehaviorEditValue(SwShSymbolBehaviorFieldSpec Spec, object Value);

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

    private sealed class SymbolBehaviorFlatBufferWriter
    {
        private const int EntryVtableLength = sizeof(ushort) * 2 + (EntryFieldCount * sizeof(ushort));
        private readonly List<byte> bytes = [];

        public void Write(SwShSymbolBehaviorArchive archive)
        {
            WriteUInt32(0);
            var root = WriteRootTable();
            WriteUInt32At(0, checked((uint)root.TableOffset));

            var vector = WriteTableVector(archive.Entries.Count);
            PatchUOffset(root.Field0Offset, vector.VectorOffset);
            for (var index = 0; index < archive.Entries.Count; index++)
            {
                var entryOffset = WriteEntry(archive.Entries[index]);
                PatchUOffset(vector.ElementOffsets[index], entryOffset);
            }
        }

        public byte[] ToArray()
        {
            return bytes.ToArray();
        }

        private TableFields WriteRootTable()
        {
            AlignForTable(vtableLength: 6, alignment: 4);
            var vtableOffset = Position;
            WriteUInt16(6);
            WriteUInt16(8);
            WriteUInt16(4);
            var tableOffset = Position;
            WriteInt32(checked(tableOffset - vtableOffset));
            var field0Offset = Position;
            WriteUInt32(0);

            return new TableFields(tableOffset, field0Offset);
        }

        private int WriteEntry(SwShSymbolBehaviorEntry entry)
        {
            var valuesByField = entry.Fields.ToDictionary(field => field.Field, StringComparer.Ordinal);

            AlignForTable(EntryVtableLength, alignment: 8);
            var vtableOffset = Position;
            WriteUInt16(EntryVtableLength);
            WriteUInt16(EntryObjectLength);
            foreach (var spec in FieldSpecs)
            {
                WriteUInt16(spec.ObjectOffset);
            }

            var tableOffset = Position;
            Grow(EntryObjectLength);
            WriteInt32At(tableOffset, checked(tableOffset - vtableOffset));

            foreach (var spec in FieldSpecs)
            {
                if (!valuesByField.TryGetValue(spec.Field, out var value))
                {
                    throw new InvalidDataException($"Symbol behavior field '{spec.Field}' is missing from entry {entry.Index}.");
                }

                WriteField(tableOffset, spec, value.Value);
            }

            return tableOffset;
        }

        private void WriteField(int tableOffset, SwShSymbolBehaviorFieldSpec spec, object value)
        {
            var offset = tableOffset + spec.ObjectOffset;
            switch (spec.FieldType)
            {
                case SwShSymbolBehaviorFieldType.Single:
                    var single = (float)value;
                    if (!float.IsFinite(single))
                    {
                        throw new InvalidDataException(
                            $"Symbol behavior field {spec.FieldIndex} contains a non-finite floating-point value.");
                    }

                    WriteSingleAt(offset, single);
                    break;
                case SwShSymbolBehaviorFieldType.Int32:
                    WriteInt32At(offset, (int)value);
                    break;
                case SwShSymbolBehaviorFieldType.Byte:
                    WriteByteAt(offset, (byte)value);
                    break;
                case SwShSymbolBehaviorFieldType.UInt64:
                    WriteUInt64At(offset, (ulong)value);
                    break;
                case SwShSymbolBehaviorFieldType.String:
                    var stringOffset = WriteString((string)value);
                    PatchUOffset(offset, stringOffset);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(spec), $"Symbol behavior field type '{spec.FieldType}' is not supported.");
            }
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

        private int WriteString(string value)
        {
            Align(4);
            var data = StrictUtf8.GetBytes(value);
            var offset = Position;
            WriteUInt32(checked((uint)data.Length));
            foreach (var valueByte in data)
            {
                WriteByte(valueByte);
            }

            WriteByte(0);

            return offset;
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

        private void WriteByteAt(int offset, byte value)
        {
            bytes[offset] = value;
        }

        private void WriteInt32At(int offset, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(offset, sizeof(int)), value);
        }

        private void WriteUInt32At(int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(offset, sizeof(uint)), value);
        }

        private void WriteUInt64At(int offset, ulong value)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(offset, sizeof(ulong)), value);
        }

        private void WriteSingleAt(int offset, float value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(offset, sizeof(int)), BitConverter.SingleToInt32Bits(value));
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
