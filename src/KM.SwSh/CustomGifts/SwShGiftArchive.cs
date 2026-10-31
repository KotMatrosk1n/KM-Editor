// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Text;

namespace KM.SwSh.CustomGifts;

internal sealed class SwShGiftArchive
{
    private readonly byte[] source;
    private readonly int payload;
    private readonly List<(string Name, int Node, byte[] Data)> members = [];

    internal SwShGiftArchive(byte[] bytes)
    {
        source = bytes;
        if (bytes.Length < 20 || bytes.Length > 8 * 1024 * 1024 || !bytes.AsSpan(0, 4).SequenceEqual("SARC"u8)
            || U16(6) != 0xFEFF || U32(8) != bytes.Length) throw new InvalidDataException("Unsupported gift archive.");
        payload = U32(12);
        var fat = U16(4);
        if (!Slice(fat, 4).SequenceEqual("SFAT"u8) || U16(fat + 4) != 12) throw new InvalidDataException();
        var count = U16(fat + 6);
        var names = checked(fat + 12 + count * 16);
        if (!Slice(names, 4).SequenceEqual("SFNT"u8) || U16(names + 4) != 8 || payload < names + 8 || payload > bytes.Length)
            throw new InvalidDataException();
        var ranges = new List<(int Start, int End)>();
        for (var i = 0; i < count; i++)
        {
            var node = fat + 12 + i * 16;
            var flags = U32(node + 4);
            if ((flags >> 24) != 1) throw new InvalidDataException();
            var nameAt = checked(names + 8 + (flags & 0xFFFFFF) * 4);
            if (nameAt < names + 8 || nameAt >= payload) throw new InvalidDataException();
            var endName = Array.IndexOf(bytes, (byte)0, nameAt, payload - nameAt);
            if (endName < 0) throw new InvalidDataException();
            var name = new UTF8Encoding(false, true).GetString(bytes, nameAt, endName - nameAt);
            var start = checked(payload + U32(node + 8));
            var end = checked(payload + U32(node + 12));
            if (start < payload || end < start || members.Any(member => member.Name == name)) throw new InvalidDataException();
            members.Add((name, node, Slice(start, end - start).ToArray()));
            ranges.Add((start, end));
        }
        var ordered = ranges.OrderBy(range => range.Start).ToArray();
        if (ordered.Zip(ordered.Skip(1)).Any(pair => pair.First.End > pair.Second.Start)) throw new InvalidDataException();
    }

    internal byte[] Get(string name) => members.SingleOrDefault(member => member.Name == name).Data
        ?? throw new InvalidDataException("The gift archive layout is missing.");

    internal byte[] Replace(string name, byte[] value)
    {
        if (Get(name).AsSpan().SequenceEqual(value)) return source;
        using var output = new MemoryStream();
        output.Write(source.AsSpan(0, payload));
        var offsets = new List<(int Node, int Start, int End)>();
        foreach (var member in members)
        {
            while ((output.Position & 4095) != 0) output.WriteByte(0);
            var start = checked((int)output.Position - payload);
            output.Write(member.Name == name ? value : member.Data);
            offsets.Add((member.Node, start, checked((int)output.Position - payload)));
        }
        var result = output.ToArray();
        Put(result, 8, result.Length);
        foreach (var (node, start, end) in offsets) { Put(result, node + 8, start); Put(result, node + 12, end); }
        _ = new SwShGiftArchive(result);
        return result;
    }

    internal bool SameMembers(SwShGiftArchive other) => members.Count == other.members.Count
        && members.All(member => other.members.Any(candidate => candidate.Name == member.Name && candidate.Data.AsSpan().SequenceEqual(member.Data)));
    private ReadOnlySpan<byte> Slice(int at, int size)
    {
        if (at < 0 || size < 0 || at > source.Length - size) throw new InvalidDataException("The gift archive is truncated.");
        return source.AsSpan(at, size);
    }
    private int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(Slice(at, 2));
    private int U32(int at) => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(Slice(at, 4)));
    private static void Put(byte[] bytes, int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), value);
}
