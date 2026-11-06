// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KM.Formats.Audio;

public sealed record SoundBankObject(byte Type, uint Id, byte[] Payload);
public sealed record SoundBankMedia(uint Id, byte[] Bytes);

/// <summary>Composes embedded media and objects without collapsing duplicate native object IDs.</summary>
public sealed class SoundBankDocument
{
    private const int MaximumBytes = 512 * 1024 * 1024;
    private readonly byte[] source;
    private readonly List<(string Tag, byte[] Bytes)> chunks = [];
    public IReadOnlyList<SoundBankObject> Objects { get; }
    public IReadOnlyList<SoundBankMedia> Media { get; }
    public uint Version => U32(Chunk("BKHD"), 0);

    public SoundBankDocument(byte[] bytes)
    {
        if (bytes.Length is < 16 or > MaximumBytes) throw new InvalidDataException("Sound bank size is unsupported.");
        source = bytes.ToArray();
        for (var at = 0; at < bytes.Length;)
        {
            Check(bytes, at, 8);
            var tag = Encoding.ASCII.GetString(bytes, at, 4);
            var length = checked((int)U32(bytes, at + 4)); Check(bytes, at + 8, length);
            if (chunks.Any(chunk => chunk.Tag == tag)) throw new InvalidDataException("Sound bank chunks are duplicated.");
            chunks.Add((tag, bytes.AsSpan(at + 8, length).ToArray())); at = checked(at + 8 + length);
        }
        if (chunks[0].Tag != "BKHD") throw new InvalidDataException("Sound bank header is missing.");
        var hirc = Chunk("HIRC");
        var count = checked((int)U32(hirc, 0));
        if (count > 200000) throw new InvalidDataException("Sound object count exceeds its limit.");
        var objects = new List<SoundBankObject>(); var position = 4;
        for (var i = 0; i < count; i++)
        {
            Check(hirc, position, 9); var size = checked((int)U32(hirc, position + 1));
            if (size < 4) throw new InvalidDataException("Sound object payload is invalid.");
            Check(hirc, position + 5, size);
            objects.Add(new(hirc[position], U32(hirc, position + 5), hirc.AsSpan(position + 9, size - 4).ToArray()));
            position = checked(position + 5 + size);
        }
        if (position != hirc.Length) throw new InvalidDataException("Sound object stream has trailing data.");
        Objects = objects;
        var hasIndex = chunks.Any(c => c.Tag == "DIDX"); var hasData = chunks.Any(c => c.Tag == "DATA");
        if (hasIndex != hasData) throw new InvalidDataException("Sound bank embedded media chunks are incomplete.");
        var index = hasIndex ? Chunk("DIDX") : []; var data = hasData ? Chunk("DATA") : [];
        if (index.Length % 12 != 0 || index.Length / 12 > 200000) throw new InvalidDataException("Sound media index is invalid.");
        var media = new List<SoundBankMedia>(); var ids = new HashSet<uint>();
        for (var at = 0; at < index.Length; at += 12)
        {
            var id = U32(index, at); var offset = checked((int)U32(index, at + 4)); var size = checked((int)U32(index, at + 8));
            if (!ids.Add(id)) throw new InvalidDataException("Sound media identity is duplicated.");
            Check(data, offset, size); media.Add(new(id, data.AsSpan(offset, size).ToArray()));
        }
        Media = media;
    }

    public byte[] Compose(IReadOnlyList<SoundBankObject> addedObjects, IReadOnlyList<SoundBankMedia> addedMedia,
        IReadOnlyList<SoundBankObject>? removedObjects = null, IReadOnlyList<SoundBankMedia>? removedMedia = null)
    {
        removedObjects ??= []; removedMedia ??= [];
        if (addedObjects.Count + addedMedia.Count + removedObjects.Count + removedMedia.Count == 0) return source.ToArray();
        if (!chunks.Any(c => c.Tag == "DIDX") || !chunks.Any(c => c.Tag == "DATA"))
            throw new InvalidDataException("This bank has no embedded media layout for composition.");
        var objects = Objects.ToList(); var media = Media.ToDictionary(item => item.Id);
        foreach (var item in removedObjects)
        {
            var matches = objects.Select((value, index) => (value, index)).Where(pair => pair.value.Type == item.Type && pair.value.Id == item.Id).ToArray();
            if (matches.Length != 1 || !matches[0].value.Payload.AsSpan().SequenceEqual(item.Payload))
                throw new InvalidDataException("A sound object selected for removal changed or is ambiguous.");
            objects.RemoveAt(matches[0].index);
        }
        foreach (var item in removedMedia)
        {
            if (!media.TryGetValue(item.Id, out var current) || !current.Bytes.AsSpan().SequenceEqual(item.Bytes))
                throw new InvalidDataException("Sound media selected for removal has changed.");
            media.Remove(item.Id);
        }
        foreach (var item in addedObjects)
        {
            if (objects.Any(current => current.Id == item.Id)) throw new InvalidDataException("A new sound object identifier collides with existing content.");
            objects.Add(item);
        }
        foreach (var item in addedMedia)
            if (!media.TryAdd(item.Id, item)) throw new InvalidDataException("A new sound media identifier collides with existing content.");
        if (objects.Count > 200000 || media.Count > 200000) throw new InvalidDataException("Sound bank entry limit exceeded.");
        using var hircStream = new MemoryStream(); using var hircWriter = new BinaryWriter(hircStream);
        hircWriter.Write(objects.Count);
        foreach (var item in objects)
        {
            hircWriter.Write(item.Type); hircWriter.Write(checked(item.Payload.Length + 4)); hircWriter.Write(item.Id); hircWriter.Write(item.Payload);
        }
        using var dataStream = new MemoryStream(); using var indexStream = new MemoryStream(); using var indexWriter = new BinaryWriter(indexStream);
        // Keep every media identity while storing identical immutable samples once.
        // DIDX addresses DATA by offset and length, independently of the media ID.
        var extents = new Dictionary<string, List<(byte[] Bytes, uint Offset)>>(StringComparer.Ordinal);
        foreach (var item in media.Values.OrderBy(item => item.Id))
        {
            var hash = Convert.ToHexString(SHA256.HashData(item.Bytes));
            if (!extents.TryGetValue(hash, out var candidates)) extents.Add(hash, candidates = []);
            var existing = candidates.FindIndex(candidate => candidate.Bytes.AsSpan().SequenceEqual(item.Bytes));
            uint offset;
            if (existing >= 0) offset = candidates[existing].Offset;
            else
            {
                while ((dataStream.Length & 15) != 0) dataStream.WriteByte(0);
                offset = checked((uint)dataStream.Length);
                if (dataStream.Length + item.Bytes.Length > MaximumBytes) throw new InvalidDataException("Sound bank media exceeds its size limit.");
                dataStream.Write(item.Bytes);
                candidates.Add((item.Bytes, offset));
            }
            indexWriter.Write(item.Id); indexWriter.Write(offset); indexWriter.Write(checked((uint)item.Bytes.Length));
        }
        var replacements = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["HIRC"] = hircStream.ToArray(), ["DIDX"] = indexStream.ToArray(), ["DATA"] = dataStream.ToArray(),
        };
        var dataOffset = 8;
        foreach (var chunk in chunks)
        {
            if (chunk.Tag == "DATA") break;
            dataOffset = checked(dataOffset + 8 + replacements.GetValueOrDefault(chunk.Tag, chunk.Bytes).Length);
        }
        var header = Chunk("BKHD"); var padding = (16 - dataOffset % 16) % 16;
        replacements["BKHD"] = [.. header, .. new byte[padding]];
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        foreach (var chunk in chunks)
        {
            var payload = replacements.GetValueOrDefault(chunk.Tag, chunk.Bytes);
            if (output.Length + payload.Length + 8 > MaximumBytes) throw new InvalidDataException("Sound bank output exceeds its size limit.");
            writer.Write(Encoding.ASCII.GetBytes(chunk.Tag)); writer.Write(payload.Length); writer.Write(payload);
        }
        var result = output.ToArray(); _ = new SoundBankDocument(result); return result;
    }

    public static byte[] EncodePcm16(ReadOnlySpan<float> samples, int channels, int sampleRate)
    {
        if (channels is < 1 or > 2 || sampleRate is < 6000 or > 192000 || samples.Length % channels != 0 || samples.Length > 32 * 1024 * 1024)
            throw new InvalidDataException("Cry PCM format is unsupported.");
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(checked(60 + samples.Length * 2));
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(40); writer.Write((ushort)0xFFFE); writer.Write((ushort)channels);
        writer.Write(sampleRate); writer.Write(checked(sampleRate * channels * 2)); writer.Write((ushort)(channels * 2)); writer.Write((ushort)16);
        writer.Write((ushort)22); writer.Write((ushort)16); writer.Write(channels == 1 ? 4 : 3);
        writer.Write(Convert.FromHexString("0100000000001000800000aa00389b71"));
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(checked(samples.Length * 2));
        foreach (var value in samples)
        {
            if (!float.IsFinite(value)) throw new InvalidDataException("Cry PCM samples are not finite.");
            writer.Write((short)Math.Round(Math.Clamp(value, -1, 1) * 32767));
        }
        return output.ToArray();
    }
    private byte[] Chunk(string tag) => chunks.FirstOrDefault(chunk => chunk.Tag == tag).Bytes
        ?? throw new InvalidDataException("Required sound bank chunk is missing.");
    private static uint U32(byte[] bytes, int at) { Check(bytes, at, 4); return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at)); }
    private static void Check(byte[] bytes, int at, int size)
    {
        if (at < 0 || size < 0 || at > bytes.Length - size) throw new InvalidDataException("Sound bank entry is outside its bounds.");
    }
}
