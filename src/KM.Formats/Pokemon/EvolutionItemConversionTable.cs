// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using Google.FlatBuffers;

namespace KM.Formats.Pokemon;

public sealed record EvolutionItemConversion(int ItemId, int ParameterId);

public static class EvolutionItemConversionTable
{
    public static IReadOnlyList<EvolutionItemConversion> Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var rootOffset = ReadInt32(bytes, 0, "root table");
        var rootTable = ResolveForwardOffset(bytes, 0, rootOffset, "root table");
        var vectorReference = ResolveTableField(bytes, rootTable, fieldIndex: 0, required: true, "conversion rows");
        var vectorOffset = ReadInt32(bytes, vectorReference, "conversion row vector");
        var vector = ResolveForwardOffset(bytes, vectorReference, vectorOffset, "conversion row vector");
        var count = ReadInt32(bytes, vector, "conversion row count");
        if (count < 0 || count > (bytes.Length - vector - sizeof(int)) / sizeof(int))
        {
            throw new InvalidDataException($"Evolution item conversion row count {count} is invalid.");
        }

        var rows = new EvolutionItemConversion[count];
        for (var index = 0; index < count; index++)
        {
            var elementReference = checked(vector + sizeof(int) + (index * sizeof(int)));
            var elementOffset = ReadInt32(bytes, elementReference, $"conversion row {index}");
            var table = ResolveForwardOffset(bytes, elementReference, elementOffset, $"conversion row {index}");
            rows[index] = new EvolutionItemConversion(
                ReadInt32TableField(bytes, table, fieldIndex: 0, defaultValue: 0, $"conversion row {index} item id"),
                ReadInt32TableField(bytes, table, fieldIndex: 1, defaultValue: 0, $"conversion row {index} parameter id"));
        }

        return rows;
    }

    public static byte[] Write(IReadOnlyList<EvolutionItemConversion> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new FlatBufferBuilder(Math.Max(1024, rows.Count * 24));
        builder.ForceDefaults = true;
        var rowOffsets = new int[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            builder.StartTable(2);
            builder.AddInt(1, row.ParameterId, 0);
            builder.AddInt(0, row.ItemId, 0);
            rowOffsets[index] = builder.EndTable();
        }

        builder.StartVector(sizeof(int), rowOffsets.Length, sizeof(int));
        for (var index = rowOffsets.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(rowOffsets[index]);
        }

        var vector = builder.EndVector();
        builder.StartTable(1);
        builder.AddOffset(0, vector.Value, 0);
        var root = builder.EndTable();
        builder.Finish(root);
        return builder.SizedByteArray();
    }

    private static int ReadInt32TableField(
        byte[] bytes,
        int table,
        int fieldIndex,
        int defaultValue,
        string label)
    {
        var field = ResolveTableField(bytes, table, fieldIndex, required: false, label);
        return field == 0 ? defaultValue : ReadInt32(bytes, field, label);
    }

    private static int ResolveTableField(
        byte[] bytes,
        int table,
        int fieldIndex,
        bool required,
        string label)
    {
        var vtableDistance = ReadInt32(bytes, table, $"{label} vtable distance");
        var vtable = checked(table - vtableDistance);
        EnsureRange(bytes, vtable, sizeof(ushort) * 2, $"{label} vtable");
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(vtable, sizeof(ushort)));
        var fieldEntry = checked(vtable + (sizeof(ushort) * 2) + (fieldIndex * sizeof(ushort)));
        if (fieldEntry + sizeof(ushort) > vtable + vtableLength)
        {
            if (required)
            {
                throw new InvalidDataException($"Evolution item conversion table is missing required {label}.");
            }

            return 0;
        }

        var fieldOffset = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(fieldEntry, sizeof(ushort)));
        if (fieldOffset == 0)
        {
            if (required)
            {
                throw new InvalidDataException($"Evolution item conversion table is missing required {label}.");
            }

            return 0;
        }

        var field = checked(table + fieldOffset);
        EnsureRange(bytes, field, sizeof(int), label);
        return field;
    }

    private static int ResolveForwardOffset(byte[] bytes, int origin, int offset, string label)
    {
        if (offset <= 0)
        {
            throw new InvalidDataException($"Evolution item conversion {label} offset {offset} is invalid.");
        }

        var resolvedValue = (long)origin + offset;
        if (resolvedValue > int.MaxValue)
        {
            throw new InvalidDataException(
                $"Evolution item conversion {label} offset {resolvedValue} is invalid.");
        }

        var resolved = (int)resolvedValue;
        EnsureRange(bytes, resolved, sizeof(int), label);
        return resolved;
    }

    private static int ReadInt32(byte[] bytes, int offset, string label)
    {
        EnsureRange(bytes, offset, sizeof(int), label);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)));
    }

    private static void EnsureRange(byte[] bytes, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
        {
            var end = (long)offset + length;
            throw new InvalidDataException(
                $"Evolution item conversion {label} range 0x{offset:X}..0x{end:X} exceeds file length 0x{bytes.Length:X}.");
        }
    }
}
