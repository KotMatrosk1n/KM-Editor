// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text;

namespace KM.Formats.SwSh;

public sealed record SwShAhtbEntry(ulong Hash, string Name);

public sealed record SwShAhtbFile(IReadOnlyList<SwShAhtbEntry> Entries)
{
    public const uint Magic = 0x42544841;

    public static SwShAhtbFile Parse(ReadOnlySpan<byte> data)
    {
        EnsureRange(data, 0, sizeof(uint) + sizeof(int));
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(data[..sizeof(uint)]);
        if (magic != Magic)
        {
            throw new InvalidDataException("AHTB magic is not present.");
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4, sizeof(int)));
        if (count < 0)
        {
            throw new InvalidDataException("AHTB entry count must not be negative.");
        }

        var entries = new List<SwShAhtbEntry>(count);
        var offset = 8;
        for (var index = 0; index < count; index++)
        {
            EnsureRange(data, offset, sizeof(ulong) + sizeof(ushort));
            var hash = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, sizeof(ulong)));
            var length = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + sizeof(ulong), sizeof(ushort)));
            if (length == 0)
            {
                throw new InvalidDataException("AHTB names must include a null terminator.");
            }

            var nameOffset = offset + sizeof(ulong) + sizeof(ushort);
            EnsureRange(data, nameOffset, length);
            var nameBytes = data.Slice(nameOffset, length);
            if (nameBytes[^1] != 0)
            {
                throw new InvalidDataException("AHTB name is not null-terminated.");
            }

            entries.Add(new SwShAhtbEntry(hash, Encoding.UTF8.GetString(nameBytes[..^1])));
            offset = checked(nameOffset + length);
        }

        return new SwShAhtbFile(entries);
    }

    public byte[] Write()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(Magic);
        writer.Write(Entries.Count);
        foreach (var entry in Entries)
        {
            writer.Write(entry.Hash);
            var name = Encoding.UTF8.GetBytes(entry.Name);
            writer.Write(checked((ushort)(name.Length + 1)));
            writer.Write(name);
            writer.Write((byte)0);
        }

        return stream.ToArray();
    }

    public Dictionary<ulong, string> ToDictionary()
    {
        var result = new Dictionary<ulong, string>(Entries.Count);
        foreach (var entry in Entries)
        {
            result[entry.Hash] = entry.Name;
        }

        return result;
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length || length > data.Length - offset)
        {
            throw new InvalidDataException("AHTB data is truncated.");
        }
    }
}
