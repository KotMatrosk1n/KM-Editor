// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Text;

namespace KM.Formats.PLA;

public sealed class PlaFieldArchive
{
    private readonly byte[] bytes;
    private readonly Dictionary<ulong, Entry> entries = [];
    private readonly List<(int Start, int End, bool Payload)> ranges = [];
    private readonly Dictionary<string, byte[]> decoded = new(StringComparer.Ordinal);
    private readonly int metadataEnd;
    private sealed record Entry(int Header, int Offset, int PackedSize, int DecodedSize);
    public int Count => entries.Count;

    public PlaFieldArchive(ReadOnlySpan<byte> data)
    {
        if (data.Length is < 40 or > PlaFlatBuffer.MaximumLength)
            throw new InvalidDataException("The PLA field archive size is unsupported.");
        bytes = data.ToArray();
        if (!data[..8].SequenceEqual("GFLXPACK"u8) || I32(8) != 0x1000 || I32(12) != 0)
            throw new InvalidDataException("The PLA field archive header is unsupported.");
        var count = I32(16); var folders = I32(20);
        if (count is < 1 or > 16384 || folders is < 1 or > 4096)
            throw new InvalidDataException("The PLA archive directory size is unsupported.");
        Range(0, 24 + 8 * (2 + folders));
        var table = Offset(24); var hashes = Offset(32);
        Range(table, count * 24); Range(hashes, count * 8);
        var directoryOwners = new HashSet<int>();
        for (var i = 0; i < folders; i++)
        {
            var folder = Offset(40 + i * 8);
            Bounds(folder, 16);
            var files = I32(folder + 8);
            if (files < 0 || files > count) throw new InvalidDataException("The PLA archive folder size is invalid.");
            Range(folder, 16 + files * 16);
            var names = new HashSet<ulong>();
            for (var j = 0; j < files; j++)
            {
                var entry = folder + 16 + j * 16; var index = I32(entry + 8);
                if (index < 0 || index >= count || !directoryOwners.Add(index) || !names.Add(U64(entry)))
                    throw new InvalidDataException("The PLA archive directory has an ambiguous member identity.");
            }
        }
        if (directoryOwners.Count != count) throw new InvalidDataException("The PLA archive directory is incomplete.");
        metadataEnd = ranges.Max(range => range.End);
        for (var i = 0; i < count; i++)
        {
            var header = table + i * 24;
            var size = I32(header + 4); var packed = I32(header + 8); var offset = Offset(header + 16);
            if (bytes[header + 2] != 3 || size is < 1 or > PlaFlatBuffer.MaximumLength || packed < 1 || offset % 16 != 0)
                throw new InvalidDataException("The PLA archive member layout or codec is unsupported.");
            Range(offset, packed, true);
            if (!entries.TryAdd(U64(hashes + i * 8), new(header, offset, packed, size)))
                throw new InvalidDataException("The PLA archive member identity is ambiguous.");
        }
    }
    private void Bounds(int start, int length)
    {
        if (start < 0 || length < 0 || start > bytes.Length - length)
            throw new InvalidDataException("A PLA archive range is outside the file.");
    }
    private void Range(int start, int length, bool payload = false)
    {
        Bounds(start, length);
        var end = checked(start + length);
        foreach (var prior in ranges)
            if (start < prior.End && end > prior.Start && !(payload && prior.Payload && start == prior.Start && end == prior.End))
                throw new InvalidDataException("PLA archive ranges overlap.");
        ranges.Add((start, end, payload));
    }
    private int I32(int at) { Bounds(at, 4); return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at)); }
    private ulong U64(int at) { Bounds(at, 8); return BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(at)); }
    private int Offset(int at) => U64(at) <= int.MaxValue ? (int)U64(at) : throw new InvalidDataException("The PLA archive offset is unsupported.");
    public static ulong HashPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 1024 || path.Contains('\\') || path.Contains(':') || path.Any(char.IsControl)
            || path.Split('/').Any(part => part is "" or "." or "..")) throw new InvalidDataException("The PLA archive member path is invalid.");
        return Utf8PathHash.Compute(path);
    }
    public bool Contains(string path) => entries.ContainsKey(HashPath(path));
    public int DecodedSize(string path) => entries.TryGetValue(HashPath(path), out var entry) ? entry.DecodedSize
        : throw new InvalidDataException("The PLA archive member is unavailable.");
    public bool HasSameDirectory(PlaFieldArchive other)
    {
        if (metadataEnd != other.metadataEnd || !entries.Keys.SequenceEqual(other.entries.Keys)) return false;
        var left = bytes.AsSpan(0, metadataEnd).ToArray(); var right = other.bytes.AsSpan(0, metadataEnd).ToArray();
        foreach (var entry in entries.Values)
        {
            left.AsSpan(entry.Header + 4, 8).Clear(); right.AsSpan(entry.Header + 4, 8).Clear();
            left.AsSpan(entry.Header + 16, 4).Clear(); right.AsSpan(entry.Header + 16, 4).Clear();
        }
        return left.AsSpan().SequenceEqual(right);
    }
    public byte[] Read(string path, PlaCompressionRuntime runtime)
    {
        if (decoded.TryGetValue(path, out var result)) return result;
        if (!entries.TryGetValue(HashPath(path), out var entry)) throw new InvalidDataException("The PLA archive member is unavailable.");
        return decoded[path] = runtime.Decompress(bytes.AsSpan(entry.Offset, entry.PackedSize), entry.DecodedSize);
    }
    public byte[] Compose(IReadOnlyDictionary<string, byte[]> changes, PlaCompressionRuntime runtime)
    {
        using var output = new MemoryStream(); output.Write(bytes);
        foreach (var (path, value) in changes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (value.AsSpan().SequenceEqual(Read(path, runtime))) continue;
            var entry = entries[HashPath(path)]; var packed = runtime.Compress(value);
            while (output.Length % 16 != 0) output.WriteByte(0);
            var at = checked((int)output.Length);
            if (at > PlaFlatBuffer.MaximumLength - packed.Length) throw new InvalidDataException("The edited PLA field archive exceeds the supported size limit.");
            output.Write(packed);
            var header = output.GetBuffer().AsSpan(entry.Header, 24);
            BinaryPrimitives.WriteInt32LittleEndian(header[4..], value.Length);
            BinaryPrimitives.WriteInt32LittleEndian(header[8..], packed.Length);
            BinaryPrimitives.WriteInt32LittleEndian(header[16..], at);
        }
        return output.ToArray();
    }
}
