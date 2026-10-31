// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Core.Projects;
using KM.Formats.Executable;

namespace KM.SwSh.CustomGifts;

internal static class SwShCustomGiftModule
{
    internal const string RelativePath = "exefs/subsdk8";
    internal const string CatalogPath = "romfs/bin/km-editor/gifts/catalog.json";
    private static readonly Lazy<byte[]> Runtime = new(() =>
        ReadResource("KM.SwSh.Resources.km-custom-gifts.nso"));
    private static readonly Lazy<byte[]> LegacyRuntime = new(() =>
        ReadResource("KM.SwSh.Resources.km-custom-gifts-v1.nso"));

    private static byte[] ReadResource(string name)
    {
        using var stream = typeof(SwShCustomGiftModule).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidDataException("The custom gift runtime is unavailable.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    internal static byte[] Write(SwShCustomGiftCatalog catalog, ProjectGame game)
        => WriteModule(Runtime.Value, SwShCustomGiftCatalogCodec.Write(catalog, game == ProjectGame.Sword));

    private static byte[] WriteModule(byte[] template, byte[] payload)
    {
        var nso = NsoFile.Parse(template);
        var data = nso.Data.DecompressedData.ToArray();
        var offset = PayloadOffset(data);
        payload.CopyTo(data, offset);
        return nso.Write(dataDecompressedData: data);
    }

    internal static SwShCustomGiftCatalog Read(byte[] bytes, ProjectGame game)
    {
        if (bytes.Length > 2 * 1024 * 1024) throw new InvalidDataException("The gift module is too large.");
        var nso = NsoFile.Parse(bytes);
        foreach (var (template, legacy) in new[] { (Runtime.Value, false), (LegacyRuntime.Value, true) })
        {
            try
            {
                var offset = PayloadOffset(NsoFile.Parse(template).Data.DecompressedData);
                var payload = nso.Data.DecompressedData.AsSpan(offset, SwShCustomGiftCatalogCodec.PayloadSize);
                var catalog = SwShCustomGiftCatalogCodec.Read(payload, game == ProjectGame.Sword, legacy);
                if (WriteModule(template, payload.ToArray()).AsSpan().SequenceEqual(bytes)) return catalog;
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException or System.Text.Json.JsonException) { }
        }
        throw new InvalidDataException("The occupied gift module contains unsupported changes.");
    }

    internal static bool IsCurrent(byte[] bytes) => NsoFile.Parse(bytes).Text.DecompressedData.AsSpan()
        .SequenceEqual(NsoFile.Parse(Runtime.Value).Text.DecompressedData);

    private static int PayloadOffset(byte[] data)
    {
        var offset = data.AsSpan().IndexOf("KMGIFT00"u8);
        if (offset < 0 || offset > data.Length - SwShCustomGiftCatalogCodec.PayloadSize)
            throw new InvalidDataException("The custom gift runtime payload is unavailable.");
        return offset;
    }

    internal static NsoFile VerifyMain(byte[] bytes, ProjectGame game)
    {
        var nso = NsoFile.Inspect(bytes);
        var expected = game == ProjectGame.Sword ? "A3B75BCD3311385AEED67FBEEB79CBB7BF02F471000000000000000000000000"
            : game == ProjectGame.Shield ? "A16802625E7826BF83B6F9708E475B912A9AB7DF000000000000000000000000" : "";
        if (expected.Length == 0 || Convert.ToHexString(nso.BuildId) != expected || nso.Text.Header.MemoryOffset != 0
            || nso.Ro.Header.MemoryOffset != 0x1901000 || nso.Data.Header.MemoryOffset != 0x24DB000)
            throw new InvalidDataException("Custom gifts require the selected game's supported 1.3.2 executable.");
        foreach (var (segment, flag) in new[] { (nso.Text, NsoFlags.CheckHashText), (nso.Ro, NsoFlags.CheckHashRo), (nso.Data, NsoFlags.CheckHashData) })
            if (nso.Flags.HasFlag(flag) && !NsoFile.ComputeHash(segment.DecompressedData).SequenceEqual(segment.Hash))
                throw new InvalidDataException("The executable segment checksum is invalid.");
        var sword = game == ProjectGame.Sword;
        foreach (var guard in new[]
        {
            (Offset: 0xFE0000, Size: 0x40000, Hash: sword ? 0xE1B4CE241FFF6C94UL : 0x1A6328B259B8B92FUL),
            (Offset: sword ? 0x1424D00 : 0x1424D30, Size: 8, Hash: 0xDCB7D1B7A7ECB313UL),
            (Offset: sword ? 0x1438F80 : 0x1438FB0, Size: 40, Hash: 0x0E8F31CC5A27B676UL),
            (Offset: sword ? 0x1449C70 : 0x1449CA0, Size: 0x58, Hash: sword ? 0xD9E99A063061AC94UL : 0xC5569AF56546D10CUL),
            (Offset: sword ? 0x16CBF40 : 0x16CBFD0, Size: 0x1BC, Hash: 0xBAF50B973EFB996FUL),
            (Offset: sword ? 0x16CC140 : 0x16CC1D0, Size: 8, Hash: 0xF2B7C489F0A3701AUL),
        })
        {
            if (guard.Offset > nso.Text.DecompressedData.Length - guard.Size
                || Fnv(nso.Text.DecompressedData.AsSpan(guard.Offset, guard.Size)) != guard.Hash)
                throw new InvalidDataException("The custom gift receiving flow contains incompatible executable changes.");
        }
        VerifyCallbackRelocations(nso, sword);
        return nso;
    }

    private static void VerifyCallbackRelocations(NsoFile nso, bool sword)
    {
        var segments = nso.Segments;
        ReadOnlySpan<byte> At(int offset, int size)
        {
            foreach (var segment in segments)
            {
                var local = (long)offset - segment.Header.MemoryOffset;
                if (local >= 0 && local <= segment.DecompressedData.Length - size)
                    return segment.DecompressedData.AsSpan((int)local, size);
            }
            throw new InvalidDataException("The custom gift relocation metadata is truncated.");
        }
        if (BinaryPrimitives.ReadInt32LittleEndian(At(4, 4)) != 8 || !At(8, 4).SequenceEqual("MOD0"u8)
            || 8L + BinaryPrimitives.ReadInt32LittleEndian(At(12, 4)) != 0x260E7F8)
            throw new InvalidDataException("The executable module metadata is incompatible.");
        var tags = new Dictionary<long, ulong>();
        var terminated = false;
        for (var index = 0; index < 512; index++)
        {
            var entry = At(0x260E7F8 + index * 16, 16);
            var tag = BinaryPrimitives.ReadInt64LittleEndian(entry);
            if (tag == 0) { terminated = true; break; }
            if (tag is 7 or 8 or 9 && !tags.TryAdd(tag, BinaryPrimitives.ReadUInt64LittleEndian(entry[8..])))
                throw new InvalidDataException("The executable relocation metadata is ambiguous.");
        }
        if (!terminated || tags.GetValueOrDefault(7) != 0x1901058 || tags.GetValueOrDefault(8) != 0x2BF068 || tags.GetValueOrDefault(9) != 24)
            throw new InvalidDataException("The executable relocation table is incompatible.");
        var expected = new Dictionary<ulong, ulong> {
            [0x25752C8] = sword ? 0x1008510UL : 0x1008540UL,
            [0x25752C0] = sword ? 0x1008FD0UL : 0x1009000UL,
            [0x25747E0] = sword ? 0xFFCAE0UL : 0xFFCB10UL,
            [0x2624960] = 0x2575230, [0x26248B8] = 0x2574790, [0x2610798] = 0x26364F0,
        };
        var found = new HashSet<ulong>();
        for (var offset = 0; offset < 0x2BF068; offset += 24)
        {
            var entry = At(0x1901058 + offset, 24);
            var target = BinaryPrimitives.ReadUInt64LittleEndian(entry);
            foreach (var (slot, value) in expected)
            {
                if (target >= slot + 8 || target + 8 <= slot) continue;
                if (target != slot || !found.Add(slot) || BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]) != 1027
                    || BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]) != value || BinaryPrimitives.ReadUInt64LittleEndian(At((int)slot, 8)) != 0)
                    throw new InvalidDataException("The custom gift callback or save metadata contains incompatible changes.");
            }
        }
        if (found.Count != expected.Count) throw new InvalidDataException("The custom gift callback relocations are missing.");
    }

    private static ulong Fnv(ReadOnlySpan<byte> bytes)
    {
        ulong hash = 14695981039346656037;
        foreach (var value in bytes) hash = unchecked((hash ^ value) * 1099511628211);
        return hash;
    }
}
