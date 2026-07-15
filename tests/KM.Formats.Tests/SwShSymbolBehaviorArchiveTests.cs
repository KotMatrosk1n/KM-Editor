// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using KM.Formats.SwSh;
using Xunit;

namespace KM.Formats.Tests;

public sealed class SwShSymbolBehaviorArchiveTests
{
    [Fact]
    public void WriteRoundTripsSyntheticArchive()
    {
        var parsed = SwShSymbolBehaviorArchive.Parse(CreateArchive().Write());

        Assert.Collection(
            parsed.Entries,
            entry =>
            {
                Assert.Equal(0, entry.Index);
                Assert.Equal(1013, entry.SpeciesId);
                Assert.Equal(1010, entry.Form);
                Assert.Equal("behavior_0", entry.Behavior);
                Assert.Equal("model_0", entry.ModelPart);
                Assert.Equal("ピカチュウ_0", entry.InternalSpeciesName);
                Assert.Equal(16.25f, entry.HitboxRadius);
            },
            entry =>
            {
                Assert.Equal(1, entry.Index);
                Assert.Equal(2013, entry.SpeciesId);
                Assert.Equal(2010, entry.Form);
                Assert.Equal("behavior_1", entry.Behavior);
                Assert.Equal("model_1", entry.ModelPart);
                Assert.Equal("ピカチュウ_1", entry.InternalSpeciesName);
                Assert.Equal(26.25f, entry.HitboxRadius);
            });
    }

    [Fact]
    public void ParsedArchivePreservesExactSourceForWriteEmptyNoOpAndRestoredEdits()
    {
        var source = CreateArchive().Write().Concat(new byte[] { 0xA5, 0x5A, 0xC3 }).ToArray();
        var archive = SwShSymbolBehaviorArchive.Parse(source);

        var write = archive.Write();
        var emptyEdits = archive.WriteEdits([]);
        var noOpEdit = archive.WriteEdits(
        [
            new SwShSymbolBehaviorEdit(0, SwShSymbolBehaviorArchive.SpeciesIdField, "1013"),
        ]);
        var restored = archive.WriteEdits(
        [
            new SwShSymbolBehaviorEdit(0, SwShSymbolBehaviorArchive.SpeciesIdField, "25"),
            new SwShSymbolBehaviorEdit(0, SwShSymbolBehaviorArchive.SpeciesIdField, "1013"),
        ]);

        Assert.Equal(source, write);
        Assert.Equal(source, emptyEdits);
        Assert.Equal(source, noOpEdit);
        Assert.Equal(source, restored);
    }

    [Fact]
    public void WriteEditsPatchesScalarsAndAppendsLongerAndShorterStrings()
    {
        var source = CreateArchive().Write().Concat(new byte[] { 0xDE, 0xAD }).ToArray();
        var archive = SwShSymbolBehaviorArchive.Parse(source);

        var output = archive.WriteEdits(
        [
            new SwShSymbolBehaviorEdit(0, SwShSymbolBehaviorArchive.SpeciesIdField, "777"),
            new SwShSymbolBehaviorEdit(0, SwShSymbolBehaviorArchive.BehaviorField, "a much longer behavior profile"),
            new SwShSymbolBehaviorEdit(0, SwShSymbolBehaviorArchive.ModelPartField, "x"),
        ]);
        var parsed = SwShSymbolBehaviorArchive.Parse(output);

        Assert.True(output.Length > source.Length);
        Assert.Equal(777, parsed.Entries[0].SpeciesId);
        Assert.Equal("a much longer behavior profile", parsed.Entries[0].Behavior);
        Assert.Equal("x", parsed.Entries[0].ModelPart);
        Assert.Equal("ピカチュウ_0", parsed.Entries[0].InternalSpeciesName);
        Assert.Equal(2013, parsed.Entries[1].SpeciesId);
        Assert.Equal(new byte[] { 0xDE, 0xAD }, output.AsSpan(source.Length - 2, 2).ToArray());
    }

    [Fact]
    public void WriteUsesSourcePreservingPathForRecordUpdates()
    {
        var source = CreateArchive().Write().Concat(new byte[] { 0x11, 0x22, 0x33 }).ToArray();
        var archive = SwShSymbolBehaviorArchive.Parse(source);
        var entries = archive.Entries.ToArray();
        entries[0] = entries[0].WithField(SwShSymbolBehaviorArchive.FormField, "9");

        var output = (archive with { Entries = entries }).Write();

        Assert.Equal(source.Length, output.Length);
        Assert.Equal(9, SwShSymbolBehaviorArchive.Parse(output).Entries[0].Form);
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33 }, output[^3..]);
    }

    [Fact]
    public void WriteEditsUsesLastValue()
    {
        var archive = SwShSymbolBehaviorArchive.Parse(CreateArchive().Write());

        var output = archive.WriteEdits(
        [
            new SwShSymbolBehaviorEdit(0, SwShSymbolBehaviorArchive.FormField, "3"),
            new SwShSymbolBehaviorEdit(0, SwShSymbolBehaviorArchive.FormField, "7"),
        ]);

        Assert.Equal(7, SwShSymbolBehaviorArchive.Parse(output).Entries[0].Form);
    }

    [Fact]
    public void SyntheticArchiveWriteEditsRetainsConstructionPath()
    {
        var output = CreateArchive().WriteEdits(
        [
            new SwShSymbolBehaviorEdit(1, SwShSymbolBehaviorArchive.SpeciesIdField, "25"),
            new SwShSymbolBehaviorEdit(1, SwShSymbolBehaviorArchive.BehaviorField, "Common"),
        ]);
        var parsed = SwShSymbolBehaviorArchive.Parse(output);

        Assert.Equal(25, parsed.Entries[1].SpeciesId);
        Assert.Equal("Common", parsed.Entries[1].Behavior);
        Assert.Equal(1013, parsed.Entries[0].SpeciesId);
    }

    [Fact]
    public void WriteEditsIsolatesAliasedEntryTables()
    {
        var source = CreateArchive().Write();
        var firstTableOffset = GetEntryOffset(source, entryIndex: 0);
        PatchUOffset(source, GetEntryVectorElementOffset(source, entryIndex: 1), firstTableOffset);
        var archive = SwShSymbolBehaviorArchive.Parse(source);
        Assert.Equal(1013, archive.Entries[1].SpeciesId);

        var output = archive.WriteEdits(
        [
            new SwShSymbolBehaviorEdit(0, SwShSymbolBehaviorArchive.SpeciesIdField, "777"),
            new SwShSymbolBehaviorEdit(0, SwShSymbolBehaviorArchive.BehaviorField, "isolated"),
        ]);
        var parsed = SwShSymbolBehaviorArchive.Parse(output);

        Assert.Equal(777, parsed.Entries[0].SpeciesId);
        Assert.Equal("isolated", parsed.Entries[0].Behavior);
        Assert.Equal(1013, parsed.Entries[1].SpeciesId);
        Assert.Equal("behavior_0", parsed.Entries[1].Behavior);
    }

    [Fact]
    public void WriteEditsIsolatesSharedVtableWhenMaterializingOmittedField()
    {
        var source = CreateArchive().Write();
        var firstTableOffset = GetEntryOffset(source, entryIndex: 0);
        var secondTableOffset = GetEntryOffset(source, entryIndex: 1);
        var sharedVtableOffset = GetVtableOffset(source, firstTableOffset);
        BinaryPrimitives.WriteInt32LittleEndian(
            source.AsSpan(secondTableOffset, sizeof(int)),
            checked(secondTableOffset - sharedVtableOffset));
        OmitField(source, firstTableOffset, fieldIndex: 10);
        var archive = SwShSymbolBehaviorArchive.Parse(source);
        Assert.All(archive.Entries, entry => Assert.Equal(0, entry.Form));

        var output = archive.WriteEdits(
        [
            new SwShSymbolBehaviorEdit(0, SwShSymbolBehaviorArchive.FormField, "7"),
        ]);
        var parsed = SwShSymbolBehaviorArchive.Parse(output);

        Assert.Equal(7, parsed.Entries[0].Form);
        Assert.Equal(0, parsed.Entries[1].Form);
    }

    [Fact]
    public void WriteEditsIsolatesAliasedStrings()
    {
        var source = CreateArchive().Write();
        var firstBehaviorFieldOffset = GetFieldValueOffset(
            source,
            GetEntryOffset(source, entryIndex: 0),
            fieldIndex: 31);
        var secondBehaviorOffset = GetStringOffset(
            source,
            GetEntryOffset(source, entryIndex: 1),
            fieldIndex: 31);
        PatchUOffset(source, firstBehaviorFieldOffset, secondBehaviorOffset);
        var archive = SwShSymbolBehaviorArchive.Parse(source);
        Assert.All(archive.Entries, entry => Assert.Equal("behavior_1", entry.Behavior));

        var output = archive.WriteEdits(
        [
            new SwShSymbolBehaviorEdit(0, SwShSymbolBehaviorArchive.BehaviorField, "isolated"),
        ]);
        var parsed = SwShSymbolBehaviorArchive.Parse(output);

        Assert.Equal("isolated", parsed.Entries[0].Behavior);
        Assert.Equal("behavior_1", parsed.Entries[1].Behavior);
    }

    [Fact]
    public void WriteEditsMaterializesOmittedKnownFormAndRadius()
    {
        var source = CreateArchive().Write();
        var tableOffset = GetEntryOffset(source, entryIndex: 0);
        OmitField(source, tableOffset, fieldIndex: 10);
        OmitField(source, tableOffset, fieldIndex: 27);
        var archive = SwShSymbolBehaviorArchive.Parse(source);
        Assert.Equal(0, archive.Entries[0].Form);
        Assert.Equal(0, archive.Entries[0].GrassShakeRadius);

        var output = archive.WriteEdits(
        [
            new SwShSymbolBehaviorEdit(0, SwShSymbolBehaviorArchive.FormField, "12"),
            new SwShSymbolBehaviorEdit(0, SwShSymbolBehaviorArchive.GrassShakeRadiusField, "3.5"),
        ]);
        var parsed = SwShSymbolBehaviorArchive.Parse(output);

        Assert.True(output.Length > source.Length);
        Assert.Equal(12, parsed.Entries[0].Form);
        Assert.Equal(3.5f, parsed.Entries[0].GrassShakeRadius);
        Assert.Equal("behavior_0", parsed.Entries[0].Behavior);
    }

    [Fact]
    public void WriteEditsPreservesFutureFieldsAndTrailingDataForPresentScalarEdit()
    {
        var (extended, _, unknownValueOffset) = CreateSourceWithExtendedEntry(omitForm: false);
        var source = extended.Concat(new byte[] { 0xFA, 0xCE }).ToArray();
        var archive = SwShSymbolBehaviorArchive.Parse(source);

        var output = archive.WriteEdits(
        [
            new SwShSymbolBehaviorEdit(0, SwShSymbolBehaviorArchive.SpeciesIdField, "777"),
        ]);

        Assert.Equal(source.Length, output.Length);
        Assert.Equal(0xA5, output[unknownValueOffset]);
        Assert.Equal(new byte[] { 0xFA, 0xCE }, output[^2..]);
        Assert.Equal(777, SwShSymbolBehaviorArchive.Parse(output).Entries[0].SpeciesId);
    }

    [Fact]
    public void WriteEditsRejectsMaterializingOmittedFieldWithFutureMaterializedField()
    {
        var (source, _, _) = CreateSourceWithExtendedEntry(omitForm: true);
        var archive = SwShSymbolBehaviorArchive.Parse(source);
        Assert.Equal(0, archive.Entries[0].Form);

        var exception = Assert.Throws<InvalidDataException>(
            () => archive.WriteEdits(
            [
                new SwShSymbolBehaviorEdit(0, SwShSymbolBehaviorArchive.FormField, "7"),
            ]));

        Assert.Contains("unknown materialized fields", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsRequiredStringOmission()
    {
        var source = CreateArchive().Write();
        OmitField(source, GetEntryOffset(source, entryIndex: 0), fieldIndex: 31);

        Assert.Throws<InvalidDataException>(() => SwShSymbolBehaviorArchive.Parse(source));
    }

    [Fact]
    public void ParseRejectsInvalidUtf8()
    {
        var source = CreateArchive().Write();
        var stringOffset = GetStringOffset(source, GetEntryOffset(source, entryIndex: 0), fieldIndex: 31);
        source[stringOffset + sizeof(uint)] = 0xFF;

        Assert.Throws<InvalidDataException>(() => SwShSymbolBehaviorArchive.Parse(source));
    }

    [Fact]
    public void ParseRejectsMissingStringTerminator()
    {
        var source = CreateArchive().Write();
        var stringOffset = GetStringOffset(source, GetEntryOffset(source, entryIndex: 0), fieldIndex: 31);
        var byteCount = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(stringOffset, sizeof(uint)));
        source[checked(stringOffset + sizeof(uint) + (int)byteCount)] = 0x7F;

        Assert.Throws<InvalidDataException>(() => SwShSymbolBehaviorArchive.Parse(source));
    }

    [Theory]
    [InlineData(0x7FC00000u)]
    [InlineData(0x7F800000u)]
    [InlineData(0xFF800000u)]
    public void ParseRejectsNonFiniteSingleValues(uint bits)
    {
        var source = CreateArchive().Write();
        var fieldOffset = GetFieldValueOffset(
            source,
            GetEntryOffset(source, entryIndex: 0),
            fieldIndex: 0);
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(fieldOffset, sizeof(uint)), bits);

        var exception = Assert.Throws<InvalidDataException>(
            () => SwShSymbolBehaviorArchive.Parse(source));

        Assert.Contains("field 0", exception.Message, StringComparison.Ordinal);
        Assert.Contains("non-finite", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsOddVtableLength()
    {
        var source = CreateArchive().Write();
        var vtableOffset = GetVtableOffset(source, GetEntryOffset(source, entryIndex: 0));
        BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(vtableOffset, sizeof(ushort)), 95);

        Assert.Throws<InvalidDataException>(() => SwShSymbolBehaviorArchive.Parse(source));
    }

    [Fact]
    public void ParseRejectsOverflowingVtableDisplacement()
    {
        var source = CreateArchive().Write();
        var tableOffset = GetEntryOffset(source, entryIndex: 0);
        BinaryPrimitives.WriteInt32LittleEndian(source.AsSpan(tableOffset, sizeof(int)), int.MinValue);

        Assert.Throws<InvalidDataException>(() => SwShSymbolBehaviorArchive.Parse(source));
    }

    [Fact]
    public void ParseRejectsKnownFieldOutsideObjectBounds()
    {
        var source = CreateArchive().Write();
        var tableOffset = GetEntryOffset(source, entryIndex: 0);
        var vtableOffset = GetVtableOffset(source, tableOffset);
        var objectLength = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(vtableOffset + sizeof(ushort), sizeof(ushort)));
        SetFieldOffset(source, tableOffset, fieldIndex: 13, checked((ushort)(objectLength - 2)));

        Assert.Throws<InvalidDataException>(() => SwShSymbolBehaviorArchive.Parse(source));
    }

    [Fact]
    public void ParseRejectsOverlappingKnownFields()
    {
        var source = CreateArchive().Write();
        var tableOffset = GetEntryOffset(source, entryIndex: 0);
        var speciesOffset = GetObjectFieldOffset(source, tableOffset, fieldIndex: 13);
        SetFieldOffset(source, tableOffset, fieldIndex: 10, speciesOffset);

        Assert.Throws<InvalidDataException>(() => SwShSymbolBehaviorArchive.Parse(source));
    }

    [Fact]
    public void ParseRejectsFutureFieldAliasingKnownStorage()
    {
        var (source, tableOffset, _) = CreateSourceWithExtendedEntry(omitForm: false);
        var speciesOffset = GetObjectFieldOffset(source, tableOffset, fieldIndex: 13);
        SetFieldOffset(source, tableOffset, fieldIndex: 46, speciesOffset);

        Assert.Throws<InvalidDataException>(() => SwShSymbolBehaviorArchive.Parse(source));
    }

    [Fact]
    public void ParseRejectsMisalignedRootTarget()
    {
        var source = CreateArchive().Write();
        var rootOffset = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(0, sizeof(uint)));
        BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(0, sizeof(uint)), rootOffset + 2);

        Assert.Throws<InvalidDataException>(() => SwShSymbolBehaviorArchive.Parse(source));
    }

    [Fact]
    public void ParseRejectsShortEntryVectorStorage()
    {
        var source = CreateArchive().Write();
        var vectorOffset = GetEntryVectorOffset(source);
        var shortSource = source[..(vectorOffset + sizeof(uint) + (2 * sizeof(uint)))];

        Assert.Throws<InvalidDataException>(() => SwShSymbolBehaviorArchive.Parse(shortSource));
    }

    [Fact]
    public void ParseNormalizesOversizedVectorToInvalidDataException()
    {
        var source = CreateArchive().Write();
        BinaryPrimitives.WriteUInt32LittleEndian(
            source.AsSpan(GetEntryVectorOffset(source), sizeof(uint)),
            uint.MaxValue);

        Assert.Throws<InvalidDataException>(() => SwShSymbolBehaviorArchive.Parse(source));
    }

    [Fact]
    public void ParseRejectsOverflowingRootAndStringOffsets()
    {
        var rootSource = CreateArchive().Write();
        BinaryPrimitives.WriteUInt32LittleEndian(rootSource.AsSpan(0, sizeof(uint)), uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => SwShSymbolBehaviorArchive.Parse(rootSource));

        var stringSource = CreateArchive().Write();
        var behaviorFieldOffset = GetFieldValueOffset(
            stringSource,
            GetEntryOffset(stringSource, entryIndex: 0),
            fieldIndex: 31);
        BinaryPrimitives.WriteUInt32LittleEndian(
            stringSource.AsSpan(behaviorFieldOffset, sizeof(uint)),
            uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => SwShSymbolBehaviorArchive.Parse(stringSource));
    }

    [Fact]
    public void ParseRejectsStringOverlappingAnotherStructure()
    {
        var source = CreateArchive().Write();
        var firstTableOffset = GetEntryOffset(source, entryIndex: 0);
        var behaviorFieldOffset = GetFieldValueOffset(source, firstTableOffset, fieldIndex: 31);
        var secondTableOffset = GetEntryOffset(source, entryIndex: 1);
        PatchUOffset(source, behaviorFieldOffset, secondTableOffset);

        Assert.Throws<InvalidDataException>(() => SwShSymbolBehaviorArchive.Parse(source));
    }

    [Fact]
    public void WriteNormalizesInvalidUtf16ToInvalidDataException()
    {
        var archive = CreateArchive();
        var entries = archive.Entries.ToArray();
        entries[0] = entries[0] with
        {
            Fields = entries[0].Fields
                .Select(field => field.Field == SwShSymbolBehaviorArchive.BehaviorField
                    ? field with { Value = "\uD800" }
                    : field)
                .ToArray(),
        };

        Assert.Throws<InvalidDataException>(() => new SwShSymbolBehaviorArchive(entries).Write());
    }

    [Fact]
    public void SyntheticWriteRejectsNonFiniteSingleValue()
    {
        var archive = CreateArchive();
        var entries = archive.Entries.ToArray();
        entries[0] = entries[0] with
        {
            Fields = entries[0].Fields
                .Select(field => field.FieldIndex == 0
                    ? field with { Value = float.NaN }
                    : field)
                .ToArray(),
        };

        var exception = Assert.Throws<InvalidDataException>(
            () => new SwShSymbolBehaviorArchive(entries).Write());

        Assert.Contains("field 0", exception.Message, StringComparison.Ordinal);
        Assert.Contains("non-finite", exception.Message, StringComparison.Ordinal);
    }

    private static SwShSymbolBehaviorArchive CreateArchive()
    {
        return new SwShSymbolBehaviorArchive([CreateEntry(0), CreateEntry(1)]);
    }

    private static SwShSymbolBehaviorEntry CreateEntry(int index)
    {
        var values = SwShSymbolBehaviorArchive.FieldSpecs
            .Select(spec => new SwShSymbolBehaviorFieldValue(
                spec.Field,
                spec.FieldIndex,
                spec.FieldType,
                CreateValue(spec, index)))
            .ToArray();
        return new SwShSymbolBehaviorEntry(index, values);
    }

    private static object CreateValue(SwShSymbolBehaviorFieldSpec spec, int index)
    {
        return spec.FieldType switch
        {
            SwShSymbolBehaviorFieldType.Single => ((index + 1) * 10) + spec.FieldIndex + 0.25f,
            SwShSymbolBehaviorFieldType.Int32 => ((index + 1) * 1000) + spec.FieldIndex,
            SwShSymbolBehaviorFieldType.Byte => checked((byte)(index + spec.FieldIndex + 1)),
            SwShSymbolBehaviorFieldType.UInt64 => 0x0102030405060708UL + checked((ulong)(index + spec.FieldIndex)),
            SwShSymbolBehaviorFieldType.String when spec.Field == SwShSymbolBehaviorArchive.ModelPartField => $"model_{index}",
            SwShSymbolBehaviorFieldType.String when spec.Field == SwShSymbolBehaviorArchive.InternalSpeciesNameField => $"ピカチュウ_{index}",
            SwShSymbolBehaviorFieldType.String => $"behavior_{index}",
            _ => throw new ArgumentOutOfRangeException(nameof(spec)),
        };
    }

    private static (byte[] Source, int TableOffset, int UnknownValueOffset) CreateSourceWithExtendedEntry(
        bool omitForm)
    {
        var canonical = CreateArchive().Write();
        var logicalEntry = SwShSymbolBehaviorArchive.Parse(canonical).Entries[0];
        var entryVectorElementOffset = GetEntryVectorElementOffset(canonical, entryIndex: 0);
        var output = new List<byte>(canonical);
        const int fieldCount = 47;
        const int vtableLength = (sizeof(ushort) * 2) + (fieldCount * sizeof(ushort));
        const int objectLength = 200;
        while (((output.Count + vtableLength) % sizeof(ulong)) != 0)
        {
            output.Add(0);
        }

        var vtableOffset = output.Count;
        var vtable = new byte[vtableLength];
        BinaryPrimitives.WriteUInt16LittleEndian(vtable.AsSpan(0, sizeof(ushort)), vtableLength);
        BinaryPrimitives.WriteUInt16LittleEndian(vtable.AsSpan(sizeof(ushort), sizeof(ushort)), objectLength);
        foreach (var spec in SwShSymbolBehaviorArchive.FieldSpecs)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                vtable.AsSpan((sizeof(ushort) * 2) + (spec.FieldIndex * sizeof(ushort)), sizeof(ushort)),
                omitForm && spec.FieldIndex == 10 ? (ushort)0 : spec.ObjectOffset);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(
            vtable.AsSpan((sizeof(ushort) * 2) + (46 * sizeof(ushort)), sizeof(ushort)),
            196);
        output.AddRange(vtable);

        var tableOffset = output.Count;
        var table = new byte[objectLength];
        BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(0, sizeof(int)), tableOffset - vtableOffset);
        var values = logicalEntry.Fields.ToDictionary(field => field.Field, StringComparer.Ordinal);
        foreach (var spec in SwShSymbolBehaviorArchive.FieldSpecs.Where(
                     spec => spec.FieldType != SwShSymbolBehaviorFieldType.String))
        {
            WriteScalar(table, spec.ObjectOffset, spec.FieldType, values[spec.Field].Value);
        }

        const int unknownObjectOffset = 196;
        table[unknownObjectOffset] = 0xA5;
        output.AddRange(table);
        foreach (var spec in SwShSymbolBehaviorArchive.FieldSpecs.Where(
                     spec => spec.FieldType == SwShSymbolBehaviorFieldType.String))
        {
            var stringOffset = AppendString(output, (string)values[spec.Field].Value);
            PatchUOffset(output, tableOffset + spec.ObjectOffset, stringOffset);
        }

        PatchUOffset(output, entryVectorElementOffset, tableOffset);
        return (output.ToArray(), tableOffset, tableOffset + unknownObjectOffset);
    }

    private static void WriteScalar(
        Span<byte> table,
        int offset,
        SwShSymbolBehaviorFieldType fieldType,
        object value)
    {
        switch (fieldType)
        {
            case SwShSymbolBehaviorFieldType.Single:
                BinaryPrimitives.WriteInt32LittleEndian(
                    table.Slice(offset, sizeof(int)),
                    BitConverter.SingleToInt32Bits((float)value));
                break;
            case SwShSymbolBehaviorFieldType.Int32:
                BinaryPrimitives.WriteInt32LittleEndian(table.Slice(offset, sizeof(int)), (int)value);
                break;
            case SwShSymbolBehaviorFieldType.Byte:
                table[offset] = (byte)value;
                break;
            case SwShSymbolBehaviorFieldType.UInt64:
                BinaryPrimitives.WriteUInt64LittleEndian(table.Slice(offset, sizeof(ulong)), (ulong)value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fieldType));
        }
    }

    private static int AppendString(List<byte> output, string value)
    {
        while ((output.Count % sizeof(uint)) != 0)
        {
            output.Add(0);
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        var offset = output.Count;
        var prefix = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, checked((uint)bytes.Length));
        output.AddRange(prefix);
        output.AddRange(bytes);
        output.Add(0);
        return offset;
    }

    private static int GetEntryVectorOffset(byte[] data)
    {
        var rootTableOffset = ReadUOffset(data, offset: 0);
        return ReadUOffset(data, GetFieldValueOffset(data, rootTableOffset, fieldIndex: 0));
    }

    private static int GetEntryVectorElementOffset(byte[] data, int entryIndex)
    {
        return GetEntryVectorOffset(data) + sizeof(uint) + (entryIndex * sizeof(uint));
    }

    private static int GetEntryOffset(byte[] data, int entryIndex)
    {
        return ReadUOffset(data, GetEntryVectorElementOffset(data, entryIndex));
    }

    private static int GetStringOffset(byte[] data, int tableOffset, int fieldIndex)
    {
        return ReadUOffset(data, GetFieldValueOffset(data, tableOffset, fieldIndex));
    }

    private static int GetFieldValueOffset(byte[] data, int tableOffset, int fieldIndex)
    {
        var fieldOffset = GetObjectFieldOffset(data, tableOffset, fieldIndex);
        if (fieldOffset == 0)
        {
            throw new InvalidDataException($"FlatBuffer field {fieldIndex} is omitted.");
        }

        return tableOffset + fieldOffset;
    }

    private static ushort GetObjectFieldOffset(byte[] data, int tableOffset, int fieldIndex)
    {
        var vtableOffset = GetVtableOffset(data, tableOffset);
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(vtableOffset, sizeof(ushort)));
        var vtableFieldOffset = (sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort));
        if (vtableFieldOffset + sizeof(ushort) > vtableLength)
        {
            return 0;
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(
            data.AsSpan(vtableOffset + vtableFieldOffset, sizeof(ushort)));
    }

    private static int GetVtableOffset(byte[] data, int tableOffset)
    {
        return tableOffset - BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableOffset, sizeof(int)));
    }

    private static void OmitField(byte[] data, int tableOffset, int fieldIndex)
    {
        SetFieldOffset(data, tableOffset, fieldIndex, 0);
    }

    private static void SetFieldOffset(byte[] data, int tableOffset, int fieldIndex, ushort fieldOffset)
    {
        var vtableOffset = GetVtableOffset(data, tableOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(vtableOffset + (sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort)), sizeof(ushort)),
            fieldOffset);
    }

    private static int ReadUOffset(byte[] data, int offset)
    {
        return checked(offset + (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, sizeof(uint))));
    }

    private static void PatchUOffset(byte[] data, int sourceOffset, int targetOffset)
    {
        Assert.True(targetOffset > sourceOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(sourceOffset, sizeof(uint)),
            checked((uint)(targetOffset - sourceOffset)));
    }

    private static void PatchUOffset(List<byte> data, int sourceOffset, int targetOffset)
    {
        Assert.True(targetOffset > sourceOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(
            CollectionsMarshal.AsSpan(data).Slice(sourceOffset, sizeof(uint)),
            checked((uint)(targetOffset - sourceOffset)));
    }
}
