// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;

namespace KM.Formats.Models;

/// <summary>Reorders existing BC7 endpoints and indices for a partial atlas block.</summary>
internal static class ModelTextureBc7Crop
{
    private static readonly (int[] Subsets, int[] Anchors)[] TwoSubsetLayouts = Enumerable.Range(0, 64).Select(p => Layout(2, p)).ToArray();
    private static readonly (int[] Subsets, int[] Anchors)[] ThreeSubsetLayouts = Enumerable.Range(0, 64).Select(p => Layout(3, p)).ToArray();
    private static readonly int[][] TwoSubsetMappings = SubsetMappings(2);
    private static readonly int[][] ThreeSubsetMappings = SubsetMappings(3);

    internal static byte[] Crop(ReadOnlySpan<byte> source, int x, int y, int width, int height)
    {
        if (source.Length != 16 || x < 0 || y < 0 || width < 1 || height < 1 || x + width > 4 || y + height > 4)
            throw new InvalidDataException("BC7 atlas crop crosses a source block.");
        var original = source.ToArray(); var mode = BitOperations.TrailingZeroCount((uint)original[0]);
        var map = Enumerable.Range(0, 16).Select(i => (y + Math.Min(i / 4, height - 1)) * 4 + x + Math.Min(i % 4, width - 1)).ToArray();
        var bytes = original.ToArray();
        if (mode is 0 or 1 or 2 or 3 or 7)
        {
            // Shift the existing subset assignment without recompressing pixels.
            // BC7 modes differ in endpoint precision, P-bits and anchor indices.
            var (subsetCount, partitionBits, channels, endpointBits, pbits, sharedPbits, indexStart, indexBits) = mode switch
            {
                0 => (3, 4, 3, 4, 77, false, 83, 3),
                1 => (2, 6, 3, 6, 80, true, 82, 3),
                2 => (3, 6, 3, 5, -1, false, 99, 2),
                3 => (2, 6, 3, 7, 94, false, 98, 2),
                _ => (2, 6, 4, 5, 94, false, 98, 2)
            };
            var layouts = subsetCount == 2 ? TwoSubsetLayouts : ThreeSubsetLayouts;
            var mappings = subsetCount == 2 ? TwoSubsetMappings : ThreeSubsetMappings;
            var endpointStart = mode + 1 + partitionBits;
            var sourceLayout = layouts[Get(original, mode + 1, partitionBits)];
            var indices = new int[16]; var sourceCursor = indexStart;
            for (var i = 0; i < 16; i++)
            {
                var count = sourceLayout.Anchors.Contains(i) ? indexBits - 1 : indexBits;
                indices[i] = Get(original, sourceCursor, count); sourceCursor += count;
            }
            for (var partition = 0; partition < 1 << partitionBits; partition++)
            {
                var layout = layouts[partition];
                foreach (var mapping in mappings)
                {
                    bool Visible(int i) => i % 4 < width && i / 4 < height;
                    if (Enumerable.Range(0, 16).Any(i => Visible(i) && mapping[layout.Subsets[i]] != sourceLayout.Subsets[map[i]])) continue;
                    bytes = original.ToArray(); Set(bytes, mode + 1, partitionBits, partition);
                    var reordered = Enumerable.Range(0, 16).Select(i => Visible(i) ? indices[map[i]] : 0).ToArray();
                    for (var subset = 0; subset < subsetCount; subset++)
                    {
                        var anchor = layout.Anchors.Single(i => layout.Subsets[i] == subset);
                        var flip = reordered[anchor] >= 1 << (indexBits - 1);
                        for (var end = 0; end < 2; end++)
                        {
                            var oldEnd = mapping[subset] * 2 + (end ^ (flip ? 1 : 0)); var newEnd = subset * 2 + end;
                            for (var channel = 0; channel < channels; channel++)
                                Set(bytes, endpointStart + (channel * subsetCount * 2 + newEnd) * endpointBits, endpointBits,
                                    Get(original, endpointStart + (channel * subsetCount * 2 + oldEnd) * endpointBits, endpointBits));
                            if (pbits >= 0 && !sharedPbits) Set(bytes, pbits + newEnd, 1, Get(original, pbits + oldEnd, 1));
                        }
                        if (sharedPbits) Set(bytes, pbits + subset, 1, Get(original, pbits + mapping[subset], 1));
                        if (flip) for (var i = 0; i < 16; i++) if (layout.Subsets[i] == subset) reordered[i] = (1 << indexBits) - 1 - reordered[i];
                    }
                    var cursor = indexStart;
                    for (var i = 0; i < 16; i++) { var count = layout.Anchors.Contains(i) ? indexBits - 1 : indexBits; Set(bytes, cursor, count, reordered[i]); cursor += count; }
                    if (Matches(bytes)) return bytes;
                }
            }
            throw new InvalidDataException("The partial BC7 atlas block requires another lossless partition adapter.");
        }
        if (mode == 6)
        {
            Reorder(65, 4, Enumerable.Range(0, 4).Select(c => (7 + c * 14, 7)).ToArray(), 63);
        }
        else if (mode is 4 or 5)
        {
            var colorBits = mode == 4 ? 5 : 7;
            var colorIndexBits = mode == 4 && Get(original, 7, 1) == 1 ? 3 : 2;
            Reorder(mode == 5 ? 66 : colorIndexBits == 3 ? 81 : 50, colorIndexBits,
                Enumerable.Range(0, 3).Select(c => (8 + c * colorBits * 2, colorBits)).ToArray());
            var alphaIndexBits = mode == 5 ? 2 : 5 - colorIndexBits;
            Reorder(mode == 5 ? 97 : alphaIndexBits == 3 ? 81 : 50, alphaIndexBits, [(mode == 5 ? 50 : 38, mode == 5 ? 8 : 6)]);
        }
        else throw new InvalidDataException("The partial BC7 atlas block has an invalid mode.");
        if (!Matches(bytes)) throw new InvalidDataException("BC7 atlas index reordering changed an authored pixel.");
        return bytes;

        void Reorder(int start, int bits, (int Start, int Bits)[] endpoints, int pbits = -1)
        {
            var indices = new int[16]; var cursor = start;
            for (var i = 0; i < 16; i++) { var count = i == 0 ? bits - 1 : bits; indices[i] = Get(original, cursor, count); cursor += count; }
            var reordered = map.Select(i => indices[i]).ToArray();
            if (reordered[0] >= 1 << (bits - 1))
            {
                foreach (var (at, count) in endpoints)
                {
                    Set(bytes, at, count, Get(original, at + count, count)); Set(bytes, at + count, count, Get(original, at, count));
                }
                if (pbits >= 0) { Set(bytes, pbits, 1, Get(original, pbits + 1, 1)); Set(bytes, pbits + 1, 1, Get(original, pbits, 1)); }
                for (var i = 0; i < 16; i++) reordered[i] = (1 << bits) - 1 - reordered[i];
            }
            cursor = start;
            for (var i = 0; i < 16; i++) { var count = i == 0 ? bits - 1 : bits; Set(bytes, cursor, count, reordered[i]); cursor += count; }
        }
        bool Matches(byte[] candidate)
        {
            var before = Decode(original); var after = Decode(candidate);
            return Enumerable.Range(0, 16).Where(i => i % 4 < width && i / 4 < height).All(i => before[map[i]].Equals(after[i]));
        }
    }

    private static int[][] SubsetMappings(int count) => Enumerable.Range(0, (int)Math.Pow(count, count))
        // A partial block can split one original subset across several output
        // subsets. Duplicating its endpoints keeps every visible pixel exact.
        .Select(value => Enumerable.Range(0, count).Select(i => value / (int)Math.Pow(count, i) % count).ToArray()).ToArray();

    private static (int[] Subsets, int[] Anchors) Layout(int subsetCount, int partition)
    {
        // Probe the decoder's standard partition and anchor tables with distinct
        // subset colors and maximum indices. Anchor indices omit their high bit.
        var mode = subsetCount == 2 ? 7 : 2;
        var start = subsetCount == 2 ? 14 : 9;
        var indices = subsetCount == 2 ? 98 : 99;
        var bytes = new byte[16]; Set(bytes, mode, 1, 1); Set(bytes, mode + 1, 6, partition);
        for (var end = 0; end < subsetCount * 2; end++)
        {
            Set(bytes, start + end * 5, 5, end % 2 * 31);
            Set(bytes, start + (subsetCount * 2 + end) * 5, 5, end / 2 * 31 / (subsetCount - 1));
        }
        for (var bit = indices; bit < 128; bit++) Set(bytes, bit, 1, 1);
        var decoded = Decode(bytes);
        return (decoded.Select(p => subsetCount == 2 ? (p.g < 128 ? 0 : 1) : (p.g < 64 ? 0 : p.g < 192 ? 1 : 2)).ToArray(),
            Enumerable.Range(0, 16).Where(i => decoded[i].r < 200).ToArray());
    }
    private static ColorRgba32[] Decode(byte[] bytes) => new BcDecoder().DecodeRaw(bytes, 4, 4, CompressionFormat.Bc7);
    private static int Get(byte[] bytes, int at, int count)
    {
        var value = 0; for (var i = 0; i < count; i++) value |= ((bytes[(at + i) / 8] >> ((at + i) % 8)) & 1) << i; return value;
    }
    private static void Set(byte[] bytes, int at, int count, int value)
    {
        for (var i = 0; i < count; i++) { var bit = at + i; bytes[bit / 8] = (byte)((bytes[bit / 8] & ~(1 << (bit % 8))) | (((value >> i) & 1) << (bit % 8))); }
    }
}
