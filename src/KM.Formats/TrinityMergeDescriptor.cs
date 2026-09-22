// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;

namespace KM.Formats;

/// <summary>Removes routing entries without rebuilding or discarding unknown descriptor tables.</summary>
public static class TrinityMergeDescriptor
{
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
