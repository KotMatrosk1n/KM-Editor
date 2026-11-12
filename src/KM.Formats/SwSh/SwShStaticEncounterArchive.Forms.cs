// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace KM.Formats.SwSh;

public sealed partial record SwShStaticEncounterArchive
{
    private static bool CanExpandForm(byte[] source, int table)
    {
        var vtable = table - BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(table, 4));
        var length = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(vtable, 2));
        // The supported encounter schema contains only scalar fields. Unknown fields may contain offsets.
        return length is >= 4 and <= 68;
    }

    private (byte[] Bytes, int[] Tables) MaterializeFormFields(IReadOnlyList<SwShStaticEncounterRecord> updated)
    {
        var source = SourceData!;
        var tables = SourceEncounterTableOffsets!.ToArray();
        var aliases = tables.GroupBy(t => t).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var output = new List<byte>(source);
        var vector = ReadTableUOffset(source, ReadUOffset(source, 0), 0, required: true);
        for (var index = 0; index < updated.Count; index++)
        {
            if (Encounters[index].Form == updated[index].Form) continue;
            var table = tables[index];
            var missing = ReadTableFieldOffset(source, table, 8) == 0;
            if (!missing && !aliases.Contains(table)) continue;
            if (!CanExpandForm(source, table))
                throw new InvalidDataException("Static encounter form expansion requires a supported scalar table layout.");
            var vtable = table - BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(table, 4));
            var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(vtable, 2));
            var objectLength = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(vtable + 2, 2));
            if (objectLength < 4 || table > source.Length - objectLength || missing && objectLength == ushort.MaxValue)
                throw new InvalidDataException("Static encounter form expansion exceeds its table bounds.");
            var newVtable = new byte[Math.Max(22, (int)vtableLength)];
            source.AsSpan(vtable, vtableLength).CopyTo(newVtable);
            BinaryPrimitives.WriteUInt16LittleEndian(newVtable, checked((ushort)newVtable.Length));
            BinaryPrimitives.WriteUInt16LittleEndian(newVtable.AsSpan(2), checked((ushort)(objectLength + (missing ? 1 : 0))));
            if (missing) BinaryPrimitives.WriteUInt16LittleEndian(newVtable.AsSpan(20), objectLength);
            while (output.Count % 2 != 0) output.Add(0);
            var newVtableOffset = output.Count; output.AddRange(newVtable);
            // Preserve the original table residue so copied 64 bit scalars keep their alignment.
            while (output.Count % 8 != table % 8) output.Add(0);
            var newTable = output.Count; output.AddRange(source.AsSpan(table, objectLength));
            if (missing) output.Add(0);
            var bytes = CollectionsMarshal.AsSpan(output);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(newTable, 4), checked(newTable - newVtableOffset));
            var element = checked(vector + 4 + index * 4);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.Slice(element, 4), checked((uint)(newTable - element)));
            tables[index] = newTable;
        }
        return (output.ToArray(), tables);
    }
}
