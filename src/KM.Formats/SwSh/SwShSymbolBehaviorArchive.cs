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
    private const int EntryObjectLength = 192;

    public static SwShSymbolBehaviorArchive Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < sizeof(uint))
        {
            throw new InvalidDataException("Symbol behavior archive is too small to contain a FlatBuffer root.");
        }

        var rootTableOffset = ReadUOffset(data, offset: 0);
        var entryVectorOffset = ReadTableUOffset(data, rootTableOffset, fieldIndex: 0, required: true);
        var entries = ReadTableVector(data, entryVectorOffset, ReadEntry);

        return new SwShSymbolBehaviorArchive(entries);
    }

    public byte[] Write()
    {
        var writer = new SymbolBehaviorFlatBufferWriter();
        writer.Write(this);

        return writer.ToArray();
    }

    public byte[] WriteEdits(IEnumerable<SwShSymbolBehaviorEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var entries = Entries.ToArray();
        foreach (var edit in edits)
        {
            if ((uint)edit.EntryIndex >= (uint)entries.Length)
            {
                throw new InvalidDataException($"Symbol behavior entry {edit.EntryIndex} is not present.");
            }

            entries[edit.EntryIndex] = entries[edit.EntryIndex].WithField(edit.Field, edit.Value);
        }

        return new SwShSymbolBehaviorArchive(entries).Write();
    }

    public static SwShSymbolBehaviorFieldSpec GetFieldSpec(string field)
    {
        return FieldSpecs.FirstOrDefault(candidate => string.Equals(candidate.Field, field, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Symbol behavior field '{field}' is not supported.");
    }

    private static SwShSymbolBehaviorEntry ReadEntry(ReadOnlySpan<byte> data, int tableOffset, int index)
    {
        var values = new SwShSymbolBehaviorFieldValue[FieldSpecs.Count];
        for (var specIndex = 0; specIndex < FieldSpecs.Count; specIndex++)
        {
            var spec = FieldSpecs[specIndex];
            values[specIndex] = new SwShSymbolBehaviorFieldValue(
                spec.Field,
                spec.FieldIndex,
                spec.FieldType,
                ReadField(data, tableOffset, spec));
        }

        return new SwShSymbolBehaviorEntry(index, values);
    }

    private static object ReadField(ReadOnlySpan<byte> data, int tableOffset, SwShSymbolBehaviorFieldSpec spec)
    {
        return spec.FieldType switch
        {
            SwShSymbolBehaviorFieldType.Single => ReadTableSingle(data, tableOffset, spec.FieldIndex, required: false),
            SwShSymbolBehaviorFieldType.Int32 => ReadTableInt32(data, tableOffset, spec.FieldIndex, required: false),
            SwShSymbolBehaviorFieldType.Byte => ReadTableByte(data, tableOffset, spec.FieldIndex, required: false),
            SwShSymbolBehaviorFieldType.UInt64 => ReadTableUInt64(data, tableOffset, spec.FieldIndex, required: false),
            SwShSymbolBehaviorFieldType.String => ReadTableString(data, tableOffset, spec.FieldIndex, required: false),
            _ => throw new ArgumentOutOfRangeException(nameof(spec), $"Symbol behavior field type '{spec.FieldType}' is not supported."),
        };
    }

    private static int ReadUOffset(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, sizeof(uint));
        var relativeOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, sizeof(uint)));
        var targetOffset = checked(offset + (int)relativeOffset);
        EnsureRange(data, targetOffset, sizeof(int));

        return targetOffset;
    }

    private static int ReadTableUOffset(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        return ReadUOffset(data, tableOffset + fieldOffset);
    }

    private static string ReadTableString(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var stringOffset = ReadTableUOffset(data, tableOffset, fieldIndex, required);
        return stringOffset == 0 ? string.Empty : ReadString(data, stringOffset);
    }

    private static float ReadTableSingle(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        EnsureRange(data, tableOffset + fieldOffset, sizeof(float));

        return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(tableOffset + fieldOffset, sizeof(int))));
    }

    private static int ReadTableInt32(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        EnsureRange(data, tableOffset + fieldOffset, sizeof(int));

        return BinaryPrimitives.ReadInt32LittleEndian(data.Slice(tableOffset + fieldOffset, sizeof(int)));
    }

    private static byte ReadTableByte(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        EnsureRange(data, tableOffset + fieldOffset, sizeof(byte));

        return data[tableOffset + fieldOffset];
    }

    private static ulong ReadTableUInt64(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex, bool required)
    {
        var fieldOffset = ReadTableFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Required FlatBuffer field {fieldIndex} is missing.");
            }

            return 0;
        }

        EnsureRange(data, tableOffset + fieldOffset, sizeof(ulong));

        return BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(tableOffset + fieldOffset, sizeof(ulong)));
    }

    private static int ReadTableFieldOffset(ReadOnlySpan<byte> data, int tableOffset, int fieldIndex)
    {
        EnsureRange(data, tableOffset, sizeof(int));
        var vtableOffset = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(tableOffset, sizeof(int)));
        var vtableStart = tableOffset - vtableOffset;
        EnsureRange(data, vtableStart, sizeof(ushort) * 2);
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vtableStart, sizeof(ushort)));
        var fieldEntryOffset = sizeof(ushort) * 2 + (fieldIndex * sizeof(ushort));
        if (fieldEntryOffset + sizeof(ushort) > vtableLength)
        {
            return 0;
        }

        EnsureRange(data, vtableStart + fieldEntryOffset, sizeof(ushort));

        return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(vtableStart + fieldEntryOffset, sizeof(ushort)));
    }

    private static SwShSymbolBehaviorEntry[] ReadTableVector(ReadOnlySpan<byte> data, int vectorOffset, Func<ReadOnlySpan<byte>, int, int, SwShSymbolBehaviorEntry> readTable)
    {
        var count = ReadVectorLength(data, vectorOffset);
        var values = new SwShSymbolBehaviorEntry[count];

        for (var index = 0; index < count; index++)
        {
            var elementOffset = vectorOffset + sizeof(uint) + (index * sizeof(uint));
            values[index] = readTable(data, ReadUOffset(data, elementOffset), index);
        }

        return values;
    }

    private static int ReadVectorLength(ReadOnlySpan<byte> data, int vectorOffset)
    {
        EnsureRange(data, vectorOffset, sizeof(uint));
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(vectorOffset, sizeof(uint)));
        if (count > int.MaxValue)
        {
            throw new InvalidDataException("FlatBuffer vector is too large.");
        }

        return (int)count;
    }

    private static string ReadString(ReadOnlySpan<byte> data, int stringOffset)
    {
        var length = ReadVectorLength(data, stringOffset);
        EnsureRange(data, stringOffset + sizeof(uint), length);

        return Encoding.UTF8.GetString(data.Slice(stringOffset + sizeof(uint), length));
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > data.Length || count > data.Length - offset)
        {
            throw new InvalidDataException("FlatBuffer offset points outside the symbol behavior archive.");
        }
    }

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
                    WriteSingleAt(offset, (float)value);
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
            var data = Encoding.UTF8.GetBytes(value);
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
