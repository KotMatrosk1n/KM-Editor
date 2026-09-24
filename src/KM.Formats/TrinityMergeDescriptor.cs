// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;

namespace KM.Formats;

/// <summary>Removes routing entries without rebuilding or discarding unknown descriptor tables.</summary>
public static class TrinityMergeDescriptor
{
    /// <summary>Allows semantic comparison only when every reachable descriptor table has a known schema.</summary>
    public static bool HasOnlyKnownFields(byte[] source)
    {
        try
        {
            if (source.Length > 64 * 1024 * 1024) return false;
            var root = ReadInt(source, 0);
            if (!KnownTable(root, 4)) return false;
            foreach (var slot in new[] { 8, 10 })
            {
                var field = Field(root, slot);
                if (field == 0) continue;
                var vector = checked(field + ReadInt(source, field));
                var count = ReadInt(source, vector);
                if (count < 0 || count > 1_000_000 || (long)vector + 4 + count * 4L > source.Length) return false;
                for (var index = 0; index < count; index++)
                {
                    var entry = checked(vector + 4 + index * 4);
                    var table = checked(entry + ReadInt(source, entry));
                    if (!KnownTable(table, 2)) return false;
                    if (slot == 8 && Field(table, 6) is var unknown && unknown != 0
                        && !KnownTable(checked(unknown + ReadInt(source, unknown)), 0)) return false;
                }
            }
            return true;

            bool KnownTable(int table, int fields)
            {
                var vtable = checked(table - ReadInt(source, table));
                var length = ReadUshort(source, vtable);
                if (length < 4 || length % 2 != 0 || (long)vtable + length > source.Length) return false;
                for (var offset = 4 + fields * 2; offset < length; offset += 2)
                    if (ReadUshort(source, checked(vtable + offset)) != 0) return false;
                return true;
            }
            int Field(int table, int slot)
            {
                var vtable = checked(table - ReadInt(source, table));
                if (slot >= ReadUshort(source, vtable)) return 0;
                var offset = ReadUshort(source, checked(vtable + slot));
                return offset == 0 ? 0 : checked(table + offset);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or IndexOutOfRangeException)
        { return false; }
    }

    public static byte[] RemoveFileHashes(byte[] source, IReadOnlySet<ulong> removed)
    {
        if (source.Length > 64 * 1024 * 1024) throw new InvalidDataException("Descriptor exceeds the supported size.");
        var root = ReadInt(source, 0);
        var vtable = checked(root - ReadInt(source, root));
        var vtableLength = ReadUshort(source, vtable);
        if (vtableLength < 10) throw new InvalidDataException("Descriptor routing vectors are missing.");
        var hashes = Vector(4, 8);
        var files = Vector(8, 4);
        if (hashes.Count != files.Count) throw new InvalidDataException("Descriptor routing vectors disagree.");
        var result = source.ToArray();
        var count = 0;
        for (var index = 0; index < hashes.Count; index++)
        {
            var hash = BinaryPrimitives.ReadUInt64LittleEndian(source.AsSpan(hashes.Start + index * 8, 8));
            if (removed.Contains(hash)) continue;
            BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(hashes.Start + count * 8, 8), hash);
            var oldPosition = files.Start + index * 4;
            var target = checked(oldPosition + ReadInt(source, oldPosition));
            if (target < 0 || target > source.Length - 4) throw new InvalidDataException("Descriptor entry points outside the source.");
            var newPosition = files.Start + count * 4;
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(newPosition, 4), checked(target - newPosition));
            count++;
        }
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(hashes.Start - 4, 4), count);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(files.Start - 4, 4), count);
        return result;

        (int Start, int Count) Vector(int slot, int elementSize)
        {
            var offset = ReadUshort(source, checked(vtable + slot));
            if (offset == 0) throw new InvalidDataException("Descriptor routing vector is absent.");
            var field = checked(root + offset);
            var start = checked(field + ReadInt(source, field) + 4);
            var length = ReadInt(source, start - 4);
            if (length < 0 || length > 1_000_000 || start < 0 || (long)start + (long)length * elementSize > source.Length)
                throw new InvalidDataException("Descriptor routing vector is invalid.");
            return (start, length);
        }
    }

    private static int ReadInt(byte[] bytes, int position) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position, 4));
    private static ushort ReadUshort(byte[] bytes, int position) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position, 2));
}
