// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Formats.SwSh;

public sealed record SwShItemHashEntry(int ItemId, ulong Hash);

public sealed record SwShItemHashTable(IReadOnlyList<SwShItemHashEntry> Entries)
{
    public static SwShItemHashTable Parse(ReadOnlySpan<byte> data)
    {
        EnsureRange(data, 0, sizeof(int));
        var count = BinaryPrimitives.ReadInt32LittleEndian(data[..sizeof(int)]);
        if (count < 0)
        {
            throw new InvalidDataException("Item hash entry count must not be negative.");
        }

        EnsureRange(data, sizeof(int), checked(count * 0x10));
        var entries = new List<SwShItemHashEntry>(count);
        var seenIds = new HashSet<int>();
        var seenHashes = new HashSet<ulong>();
        var offset = sizeof(int);
        for (var index = 0; index < count; index++)
        {
            var hash = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, sizeof(ulong)));
            var itemId = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + sizeof(ulong), sizeof(int)));
            if (itemId != 0 && seenIds.Add(itemId) && seenHashes.Add(hash))
            {
                entries.Add(new SwShItemHashEntry(itemId, hash));
            }

            offset += 0x10;
        }

        return new SwShItemHashTable(entries);
    }

    public byte[] Write()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(Entries.Count);
        foreach (var entry in Entries)
        {
            writer.Write(entry.Hash);
            writer.Write(entry.ItemId);
            writer.Write(0);
        }

        return stream.ToArray();
    }

    public Dictionary<int, ulong> ToHashByItemId()
    {
        return Entries.ToDictionary(entry => entry.ItemId, entry => entry.Hash);
    }

    public Dictionary<ulong, int> ToItemIdByHash()
    {
        return Entries.ToDictionary(entry => entry.Hash, entry => entry.ItemId);
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length || length > data.Length - offset)
        {
            throw new InvalidDataException("Item hash table data is truncated.");
        }
    }
}
