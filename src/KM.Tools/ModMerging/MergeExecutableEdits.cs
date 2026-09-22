// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Security.Cryptography;
using KM.Api.ModMerger;
using KM.Formats.Executable;

namespace KM.Tools.ModMerging;

/// <summary>Composes explicit patch writes and vanilla relative executable edits by address.</summary>
internal static class MergeExecutableEdits
{
    private const int MaximumPatchBytes = 1_000_000;
    internal sealed record Source(string Id, string Name, byte[] Bytes);
    internal delegate string? Resolve(string region, IReadOnlyList<MergeValueDto> values);

    internal static bool IsPatch(string path) => path.EndsWith(".ips", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".ips32", StringComparison.OrdinalIgnoreCase);

    internal static SortedDictionary<uint, byte> ReadPatch(byte[] bytes)
    {
        var wide = bytes.AsSpan().StartsWith("IPS32"u8);
        if (!wide && !bytes.AsSpan().StartsWith("PATCH"u8)) throw new InvalidDataException("Unrecognized executable patch header.");
        var width = wide ? 4 : 3;
        ReadOnlySpan<byte> end = wide ? "EEOF"u8 : "EOF"u8;
        var result = new SortedDictionary<uint, byte>();
        var cursor = 5;
        var expanded = 0;
        while (true)
        {
            Require(width);
            if (bytes.AsSpan(cursor, width).SequenceEqual(end))
            {
                cursor += width;
                // Truncation changes the target layout and cannot be composed as a set of writes.
                if (cursor != bytes.Length) throw new InvalidDataException("Patch truncation or trailing data requires a separate complete file choice.");
                return result;
            }
            uint offset = 0;
            for (var index = 0; index < width; index++) offset = (offset << 8) | bytes[cursor++];
            Require(2);
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(cursor)); cursor += 2;
            var repeated = length == 0;
            if (repeated) { Require(3); length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(cursor)); cursor += 2; }
            Require(repeated ? 1 : length);
            if (length == 0 || (ulong)offset + length > (wide ? 0x1_0000_0000UL : 0x100_0000UL)
                || (expanded += length) > MaximumPatchBytes) throw new InvalidDataException("Patch range exceeds its supported address or size limit.");
            for (var index = 0; index < length; index++) result[offset + (uint)index] = bytes[cursor + (repeated ? 0 : index)];
            cursor += repeated ? 1 : length;
        }
        void Require(int count) { if (cursor > bytes.Length - count) throw new InvalidDataException("Truncated executable patch."); }
    }

    internal static byte[] WritePatch(SortedDictionary<uint, byte> bytes)
    {
        using var output = new MemoryStream(); output.Write("IPS32"u8);
        var entries = bytes.ToArray();
        Span<byte> header = stackalloc byte[6];
        for (var index = 0; index < entries.Length;)
        {
            var first = index++;
            while (index < entries.Length && index - first < ushort.MaxValue && entries[index].Key == (ulong)entries[index - 1].Key + 1) index++;
            // A record beginning at the terminator address has no unambiguous IPS32 representation.
            if (entries[first].Key == 0x45454F46) throw new InvalidDataException("Patch record collides with the terminator address.");
            BinaryPrimitives.WriteUInt32BigEndian(header, entries[first].Key);
            BinaryPrimitives.WriteUInt16BigEndian(header[4..], checked((ushort)(index - first)));
            output.Write(header);
            for (var position = first; position < index; position++) output.WriteByte(entries[position].Value);
        }
        output.Write("EEOF"u8); return output.ToArray();
    }

    internal static byte[] CombinePatches(IReadOnlyList<Source> sources, Resolve resolve)
    {
        long expanded = 0;
        var patches = sources.Select(source =>
        {
            var patch = ReadPatch(source.Bytes);
            if ((expanded += patch.Count) > 4_000_000) throw new InvalidDataException("Combined executable patches exceed the merge limit.");
            return patch;
        }).ToArray();
        var merged = new SortedDictionary<uint, byte>();
        var conflicts = new List<(uint Address, int[] Sources)>();
        foreach (var address in patches.SelectMany(patch => patch.Keys).Distinct().Order())
        {
            var writers = Enumerable.Range(0, patches.Length).Where(index => patches[index].ContainsKey(address)).ToArray();
            merged[address] = patches[writers[0]][address];
            if (writers.Any(index => patches[index][address] != merged[address])) conflicts.Add((address, writers));
        }
        ResolveRegions(conflicts, sources, (source, address) => patches[source][address], (address, value) => merged[address] = value, resolve);
        var output = WritePatch(merged);
        if (!ReadPatch(output).SequenceEqual(merged)) throw new InvalidDataException("Reconstructed patch differs from the reviewed writes.");
        return output;
    }

    internal static byte[] CombineImages(byte[] original, IReadOnlyList<Source> sources, Resolve resolve)
    {
        CheckImageSize(original);
        if (sources.Sum(source => (long)CheckImageSize(source.Bytes)) + CheckImageSize(original) > 512L * 1024 * 1024)
            throw new InvalidDataException("Combined executable images exceed the merge memory limit.");
        var baseline = NsoFile.Parse(original);
        var images = sources.Select(source =>
        {
            CheckImageSize(source.Bytes);
            if (!NsoRegisteredRegionCompositionVerifier.HasCompatibleLayoutEnvelope(original, source.Bytes))
                throw new InvalidDataException("Executable builds or layouts differ.");
            var image = NsoFile.Parse(source.Bytes);
            // Restoring only segment data must recover the entire original image, including unknown header and gap data.
            if (!NsoRegisteredRegionCompositionVerifier.SemanticallyMatches(original,
                image.Write(baseline.Text.DecompressedData, baseline.Ro.DecompressedData, baseline.Data.DecompressedData)))
                throw new InvalidDataException("Executable metadata changes cannot be represented as segment edits.");
            return image;
        }).ToArray();
        var result = baseline.Segments.Select(segment => segment.DecompressedData.ToArray()).ToArray();
        for (var segment = 0; segment < result.Length; segment++)
        {
            var index = segment;
            var baseSegment = baseline.Segments[index];
            var data = images.Select(image => image.Segments[index].DecompressedData).ToArray();
            var conflicts = new List<(uint Address, int[] Sources)>();
            for (var offset = 0; offset < result[index].Length; offset++)
            {
                var first = -1; var different = false;
                for (var source = 0; source < data.Length; source++)
                {
                    if (data[source][offset] == baseSegment.DecompressedData[offset]) continue;
                    if (first < 0) first = source;
                    else if (data[source][offset] != data[first][offset]) different = true;
                }
                if (first < 0) continue;
                result[index][offset] = data[first][offset];
                if (different)
                {
                    if (conflicts.Count >= MaximumPatchBytes) throw new InvalidDataException("Executable conflicts exceed the review limit.");
                    conflicts.Add(((uint)offset, Enumerable.Range(0, data.Length).Where(source => data[source][offset] != baseSegment.DecompressedData[offset]).ToArray()));
                }
            }
            ResolveRegions(conflicts, sources, (source, address) => data[source][address], (address, value) => result[index][address] = value,
                (region, values) => resolve(baseSegment.Name + "/" + region, values), (uint)baseSegment.Header.MemoryOffset);
        }
        var output = baseline.Write(result[0], result[1], result[2]);
        var verified = NsoFile.Parse(output);
        if (!verified.Segments.Select((segment, index) => segment.DecompressedData.AsSpan().SequenceEqual(result[index])).All(equal => equal))
            throw new InvalidDataException("Reconstructed executable differs from the reviewed regions.");
        return output;
    }

    internal static int CheckImageSize(byte[] bytes)
    {
        if (bytes.Length < NsoFile.HeaderSize || !bytes.AsSpan().StartsWith("NSO0"u8)) throw new InvalidDataException("Invalid executable image.");
        long total = 0;
        foreach (var offset in new[] { 0x18, 0x28, 0x38 }) total += BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
        if (total > 128 * 1024 * 1024) throw new InvalidDataException("Executable image exceeds the merge limit.");
        return (int)total;
    }

    private static void ResolveRegions(List<(uint Address, int[] Sources)> conflicts, IReadOnlyList<Source> sources,
        Func<int, uint, byte> read, Action<uint, byte> write, Resolve resolve, uint displayOffset = 0)
    {
        for (var index = 0; index < conflicts.Count;)
        {
            var first = index++;
            while (index < conflicts.Count && conflicts[index].Address == (ulong)conflicts[index - 1].Address + 1
                && conflicts[index].Sources.AsSpan().SequenceEqual(conflicts[first].Sources)) index++;
            var start = conflicts[first].Address; var length = index - first;
            var values = conflicts[first].Sources.Select(source =>
            {
                var bytes = Enumerable.Range(0, length).Select(position => read(source, start + (uint)position)).ToArray();
                var value = bytes.Length <= 64 ? Convert.ToHexString(bytes) : Convert.ToHexString(bytes.AsSpan(0, 32)) + "... SHA-256 " + Convert.ToHexStringLower(SHA256.HashData(bytes));
                return new MergeValueDto(sources[source].Id, sources[source].Name, value);
            }).ToArray();
            var choice = resolve($"0x{(ulong)displayOffset + start:X8}..0x{(ulong)displayOffset + start + (uint)length - 1:X8}", values);
            var selected = conflicts[first].Sources.FirstOrDefault(source => sources[source].Id == choice, conflicts[first].Sources[0]);
            for (var position = 0; position < length; position++) write(start + (uint)position, read(selected, start + (uint)position));
        }
    }
}
