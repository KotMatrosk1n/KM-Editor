// SPDX-License-Identifier: GPL-3.0-only
using System.Text;

namespace KM.Formats.Audio;

public sealed record SoundBankItem(string Identifier, string Kind, long Offset, long Length, string Bank,
    string Codec = "", int Channels = 0, int SampleRate = 0, string Status = "sample", uint[]? Media = null);

/// <summary>Reads bounded bank indexes and media headers without decoding audio.</summary>
public sealed class SoundBankReader(Stream stream, CancellationToken cancellationToken)
{
    private readonly BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
    private const int MaxItems = 200_000;
    private readonly List<SoundBankItem> items = [];
    public IReadOnlyList<SoundBankItem> Read(string name)
    {
        var magic = Tag(0);
        if (magic == "AKPK") Package(name);
        else if (magic == "BKHD") Bank(0, stream.Length, name);
        else items.Add(Media(0, stream.Length, Path.GetFileName(name), name));
        return items;
    }
    private void Check(long offset, long size, long end)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (offset < 0 || size < 0 || offset > end || size > end - offset || end > stream.Length || items.Count >= MaxItems)
            throw new InvalidDataException("Audio index exceeds its source bounds.");
    }
    private uint U32(long offset) { Check(offset, 4, stream.Length); stream.Position = offset; return reader.ReadUInt32(); }
    private string Tag(long offset) { Check(offset, 4, stream.Length); stream.Position = offset; return Encoding.ASCII.GetString(reader.ReadBytes(4)); }
    private void Package(string name)
    {
        if (U32(8) != 1) throw new InvalidDataException("Audio package byte order is unsupported.");
        var end = checked(8L + U32(4)); Check(0, 28, end);
        long table = 28L + U32(12);
        for (var group = 0; group < 3; group++)
        {
            var size = U32(16 + group * 4); Check(table, size, end);
            if (size == 0) continue;
            var count = U32(table); var stride = group == 2 ? 24 : 20;
            if (count > MaxItems || 4L + count * stride != size) throw new InvalidDataException("Audio package table is invalid.");
            for (var i = 0; i < count; i++)
            {
                var row = table + 4 + i * stride; var id = U32(row).ToString();
                if (group == 2) { id = ((ulong)U32(row + 4) << 32 | U32(row)).ToString(); row += 4; }
                var alignment = U32(row + 4); var length = U32(row + 8); var offset = checked((long)U32(row + 12) * alignment);
                var language = U32(row + 16); Check(offset, length, stream.Length);
                if (alignment == 0 || offset < end) throw new InvalidDataException("Audio package media range is invalid.");
                var bank = language == 0 ? name : $"{name} [{language}]";
                if (group == 0) Bank(offset, length, $"{bank}/{id}.bnk");
                else items.Add(Media(offset, length, id, bank));
            }
            table += size;
        }
    }
    private void Bank(long start, long length, string name)
    {
        var end = checked(start + length); Check(start, length, stream.Length);
        var chunks = new Dictionary<string, (long Offset, uint Size)>();
        for (var pos = start; pos < end;)
        {
            Check(pos, 8, end); var tag = Tag(pos); var size = U32(pos + 4); Check(pos + 8, size, end);
            if (!chunks.TryAdd(tag, (pos + 8, size))) throw new InvalidDataException("Duplicate audio bank chunk.");
            pos += 8L + size;
        }
        if (!chunks.TryGetValue("BKHD", out var header) || header.Size < 4) throw new InvalidDataException("Audio bank header is missing.");
        var version = U32(header.Offset);
        if (chunks.TryGetValue("DIDX", out var index) && chunks.TryGetValue("DATA", out var data))
        {
            if (index.Size % 12 != 0 || index.Size / 12 > MaxItems) throw new InvalidDataException("Audio bank media table is invalid.");
            for (var pos = index.Offset; pos < index.Offset + index.Size; pos += 12)
            {
                var id = U32(pos); var offset = U32(pos + 4); var size = U32(pos + 8);
                Check(data.Offset + offset, size, data.Offset + data.Size);
                items.Add(Media(data.Offset + offset, size, id.ToString(), name));
            }
        }
        if (!chunks.TryGetValue("HIRC", out var hirc)) return;
        var objects = new List<(uint Id, byte Type, long Offset, uint Size)>();
        var hEnd = hirc.Offset + hirc.Size; Check(hirc.Offset, 4, hEnd); var count = U32(hirc.Offset); long p = hirc.Offset + 4;
        if (count > MaxItems) throw new InvalidDataException("Audio bank object count is invalid.");
        for (var i = 0; i < count; i++)
        {
            Check(p, 9, hEnd); stream.Position = p; var type = reader.ReadByte(); var size = U32(p + 1);
            Check(p + 5, size, hEnd); if (size < 4) throw new InvalidDataException("Audio object is truncated.");
            objects.Add((U32(p + 5), type, p + 9, size - 4)); p += 5L + size;
        }
        var media = new Dictionary<uint, uint>();
        if (version is 128 or 140 or 145)
            foreach (var obj in objects.Where(o => o.Type == 2 && o.Size >= 9))
            {
                if ((U32(obj.Offset) & 0xf) == 1) media.TryAdd(obj.Id, U32(obj.Offset + 5));
                else items.Add(new(obj.Id.ToString(), "event", 0, 0, name, Status: "procedural"));
            }
        var sources = media.Values.Distinct().ToArray();
        foreach (var source in sources)
            if (!items.Any(item => item.Identifier == source.ToString() && item.Bank == name && item.Kind == "sample"))
                items.Add(new(source.ToString(), "reference", 0, 0, name, Status: "missing"));
        foreach (var obj in objects.Where(o => o.Type is 4 or 15))
            items.Add(new(obj.Id.ToString(), "event", 0, 0, name, Status: "event"));
        if (version is not (128 or 140 or 145))
            items.Add(new($"bank:{version}", "bank", 0, 0, name, Status: "bankVersion"));
    }
    private SoundBankItem Media(long offset, long length, string id, string bank)
    {
        var status = length > 64L * 1024 * 1024 ? "tooLarge" : "sample";
        var end = checked(offset + length); Check(offset, length, stream.Length);
        var result = new SoundBankItem(id, "sample", offset, length, bank, Status: status);
        if (length < 12 || Tag(offset) != "RIFF" || Tag(offset + 8) != "WAVE") return result with { Status = "unsupported" };
        if (U32(offset + 4) + 8L > length) status = "prefetch";
        var codec = ""; var channels = 0; var rate = 0;
        for (var p = offset + 12; p + 8 <= end;)
        {
            var tag = Tag(p); var size = U32(p + 4);
            if (tag == "data") break;
            Check(p + 8, size, end);
            if (tag == "fmt " && size >= 16)
            {
                stream.Position = p + 8; var format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32();
                codec = format switch { 0xffff => "Vorbis", 0x8311 => "PTADPCM", 0x3039 => "Opus", 2 => "ADPCM", 1 or 0xfffe => "PCM", _ => $"0x{format:X4}" };
                if (format is not (0xffff or 0x8311 or 0x3039 or 2 or 1 or 0xfffe)) status = "unsupported";
                if (channels is < 1 or > 8 || rate is < 4000 or > 192000) status = "unsupported";
            }
            p += 8L + size + (size & 1);
        }
        return result with { Codec = codec, Channels = channels, SampleRate = rate, Status = codec.Length == 0 ? "damaged" : status };
    }
}
