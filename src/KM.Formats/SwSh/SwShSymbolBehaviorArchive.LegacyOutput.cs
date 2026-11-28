// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Formats.SwSh;

public sealed partial record SwShSymbolBehaviorArchive
{
    private static readonly HashSet<int> LegacyEditableFieldIndexes = [2, 6, 10, 13, 27, 31];
    private static SwShSymbolBehaviorFieldSpec[] CreateLegacyFieldSpecs() => FieldSpecs.Take(46)
        .Select(spec => spec.FieldIndex switch
        {
            14 => spec with { FieldType = SwShSymbolBehaviorFieldType.Byte },
            15 => spec with { FieldType = SwShSymbolBehaviorFieldType.Byte, ObjectOffset = 69 },
            18 or 30 or 32 or 33 or 34 or 35 or 36 => spec with { FieldType = SwShSymbolBehaviorFieldType.Int32 },
            _ => spec,
        })
        .ToArray();

    public static SwShSymbolBehaviorArchive RecoverLegacyOutput(ReadOnlySpan<byte> data, ReadOnlySpan<byte> baseData)
    {
        var original = Parse(baseData);
        var originalLayouts = original.SourceEntryLayouts!;
        if (!HasLegacySourcePrefix(data, baseData, originalLayouts))
        {
            throw new InvalidDataException("Behavior output does not match the base archive required for recovery.");
        }

        var output = new List<byte>(data.ToArray());
        var legacyRows = new Dictionary<int, SwShSymbolBehaviorEntry>();
        var legacySpecs = CreateLegacyFieldSpecs();
        var legacyLayouts = legacySpecs
            .Select(spec => new FieldLayout(spec.FieldIndex, GetFieldSize(spec.FieldType), GetFieldAlignment(spec.FieldType)))
            .ToArray();
        var ranges = new StructuralRangeRegistry();
        foreach (var row in original.Entries)
        {
            var sourceLayout = originalLayouts[row.Index];
            var tableOffset = ReadUOffset(data, sourceLayout.VectorElementOffset, targetAlignment: sizeof(uint));
            if (tableOffset < baseData.Length || !HasLegacyMaterializedLayout(data, tableOffset, legacySpecs))
            {
                continue;
            }

            if (sourceLayout.HasMaterializedUnknownFields || sourceLayout.GetFieldValueOffset(46) >= 0
                || !LegacyEditableFieldIndexes.Any(index => sourceLayout.GetFieldValueOffset(index) < 0))
            {
                throw new InvalidDataException($"Behavior row {row.Index} contains fields the old writer could not preserve.");
            }

            var table = ReadTableLayout(
                data, tableOffset, $"legacy Behavior row {row.Index}", "legacy Behavior table",
                alignment: sizeof(ulong), ranges, legacyLayouts);
            var values = row.Fields.ToArray();
            var changedFields = new HashSet<int>();
            foreach (var legacyField in row.GetLegacyIdentityFields())
            {
                var index = legacyField.FieldIndex;
                var spec = legacySpecs[index];
                var value = ReadField(
                    data, table, spec, RequiredStringFieldIndexes.Contains(index),
                    ranges, $"legacy Behavior row {row.Index} field {index}");
                var matchesBase = legacyField.Value is float before && value is float after
                    ? BitConverter.SingleToInt32Bits(before) == BitConverter.SingleToInt32Bits(after)
                    : Equals(legacyField.Value, value);
                if (matchesBase)
                {
                    continue;
                }

                if (!LegacyEditableFieldIndexes.Contains(index))
                {
                    throw new InvalidDataException($"Legacy Behavior row {row.Index} differs from base in field {index}.");
                }

                values[index] = values[index] with { Value = value };
                changedFields.Add(index);
            }

            AppendMaterializedEntry(output, sourceLayout, row with { Fields = values }, changedFields);
            legacyRows.Add(row.Index, row);
        }

        if (legacyRows.Count == 0)
        {
            throw new InvalidDataException("No rows match the old Behavior writer layout.");
        }

        return ParseCore(output.ToArray(), legacyRows);
    }

    private static bool HasLegacySourcePrefix(
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> baseData,
        IReadOnlyList<SourceBehaviorEntryLayout> layouts)
    {
        if (data.Length <= baseData.Length)
        {
            return false;
        }

        // The old writer retains its source bytes and only patches known values and row pointers.
        var prefix = data[..baseData.Length].ToArray();
        foreach (var layout in layouts)
        {
            var elementOffset = layout.VectorElementOffset;
            baseData.Slice(elementOffset, sizeof(uint)).CopyTo(prefix.AsSpan(elementOffset));
            foreach (var index in LegacyEditableFieldIndexes)
            {
                var offset = layout.GetFieldValueOffset(index);
                if (offset >= 0)
                {
                    baseData.Slice(offset, sizeof(uint)).CopyTo(prefix.AsSpan(offset));
                }
            }
        }

        return prefix.AsSpan().SequenceEqual(baseData);
    }

    private static bool HasLegacyMaterializedLayout(
        ReadOnlySpan<byte> data,
        int tableOffset,
        IReadOnlyList<SwShSymbolBehaviorFieldSpec> legacySpecs)
    {
        var vtableLength = BinaryPrimitives.ReadInt32LittleEndian(data[tableOffset..]);
        var minimumVtableLength = sizeof(ushort) * (legacySpecs.Count + 2);
        const int legacyObjectLength = 192;
        if (vtableLength < minimumVtableLength || vtableLength > tableOffset
            || vtableLength % sizeof(ushort) != 0 || tableOffset % sizeof(ulong) != 0)
        {
            return false;
        }

        var vtable = data.Slice(tableOffset - vtableLength, vtableLength);
        if (BinaryPrimitives.ReadUInt16LittleEndian(vtable) != vtableLength
            || BinaryPrimitives.ReadUInt16LittleEndian(vtable[sizeof(ushort)..]) != legacyObjectLength)
        {
            return false;
        }

        foreach (var spec in legacySpecs)
        {
            var index = sizeof(ushort) * (spec.FieldIndex + 2);
            if (BinaryPrimitives.ReadUInt16LittleEndian(vtable[index..]) != spec.ObjectOffset)
            {
                return false;
            }
        }

        return !vtable[minimumVtableLength..].ContainsAnyExcept((byte)0);
    }
}
