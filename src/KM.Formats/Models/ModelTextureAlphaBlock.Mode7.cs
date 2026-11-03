// SPDX-License-Identifier: GPL-3.0-only
using BCnEncoder.Decoder;
using BCnEncoder.Shared;

namespace KM.Formats.Models;

internal static partial class ModelTextureAlphaBlock
{
    private sealed record Mode7Layout(int Partition, int[] Members, int[][] Bits, int[] Anchors);
    private sealed record SubsetFit(int[] Ends, int[] Indices, double Error);
    private static readonly Lazy<Mode7Layout[]> Mode7Layouts = new(ReadMode7Layouts);

    internal static byte[]? FitMode7(ColorRgba32[] pixels, ushort visible = ushort.MaxValue)
    {
        if (pixels.Length != 16 || visible == 0) throw new InvalidDataException("Alpha fit requires a visible 4x4 block.");
        byte[]? best = null; var error = double.PositiveInfinity;
        var bound = Enumerable.Range(0, 16).Count(i => (visible & (1 << i)) != 0) * 27;
        foreach (var layout in Mode7Layouts.Value)
        {
            var first = FitSubset(0); if (first is null || first.Error > bound) continue;
            var last = FitSubset(1); if (last is null) continue;
            var candidateError = first.Error + last.Error;
            if (candidateError > bound || candidateError >= error) continue;
            best = PackMode7(layout, first, last); error = candidateError;
            if (error == 0) break;

            SubsetFit? FitSubset(int subset)
            {
                var active = Enumerable.Range(0, 16).Where(i => layout.Members[i] == subset && (visible & (1 << i)) != 0).ToArray();
                if (active.Length == 0) return new(new int[8], new int[16], 0);
                var minimum = active.Min(i => pixels[i].a); var maximum = active.Max(i => pixels[i].a);
                SubsetFit? result = null; var bestColor = double.PositiveInfinity; var allowed = new int[16];
                for (var low = 0; low < 64; low++) for (var high = low; high < 64; high++)
                {
                    var a = ExpandEndpoint6(low); var b = ExpandEndpoint6(high);
                    if (a > minimum + 1 || b < maximum - 1 || minimum == 0 && a != 0 || maximum == 255 && b != 255) continue;
                    Array.Clear(allowed); var valid = true;
                    foreach (var i in active)
                    {
                        for (var j = 0; j < 4; j++)
                        {
                            var value = InterpolateFour(a, b, j);
                            if (Math.Abs(value - pixels[i].a) <= 1 && (pixels[i].a is not (0 or 255) || value == pixels[i].a)) allowed[i] |= 1 << j;
                        }
                        if (allowed[i] == 0) { valid = false; break; }
                    }
                    if (!valid) continue;
                    var color = FitFourRgb(pixels, allowed, (value, end) => ExpandEndpoint6(NearestEndpoint(value, 6, (end == 0 ? low : high) & 1)));
                    if (color.Error >= bestColor) continue;
                    result = new(color.Ends.Concat(new[] { a, b }).ToArray(), color.Indices, color.Error); bestColor = color.Error;
                    if (bestColor == 0) return result;
                }
                return result;
            }
        }
        return best;
    }

    private static byte[] PackMode7(Mode7Layout layout, SubsetFit first, SubsetFit last)
    {
        var fits = new[] { first, last }; var ends = new int[16]; var indices = new int[16];
        for (var subset = 0; subset < 2; subset++) for (var c = 0; c < 4; c++) for (var end = 0; end < 2; end++)
            ends[c * 4 + subset * 2 + end] = NearestEndpoint(fits[subset].Ends[c * 2 + end], 6, null);
        for (var i = 0; i < 16; i++) indices[i] = fits[layout.Members[i]].Indices[i];
        for (var subset = 0; subset < 2; subset++) if (indices[layout.Anchors[subset]] >= 2)
        {
            for (var c = 0; c < 4; c++) (ends[c * 4 + subset * 2], ends[c * 4 + subset * 2 + 1]) = (ends[c * 4 + subset * 2 + 1], ends[c * 4 + subset * 2]);
            for (var i = 0; i < 16; i++) if (layout.Members[i] == subset) indices[i] = 3 - indices[i];
        }
        var bytes = new byte[16]; WriteBc7Field(bytes, 0, 8, 128); WriteBc7Field(bytes, 8, 6, layout.Partition);
        for (var c = 0; c < 4; c++) for (var end = 0; end < 4; end++) WriteBc7Field(bytes, 14 + (c * 4 + end) * 5, 5, ends[c * 4 + end] >> 1);
        for (var end = 0; end < 4; end++) WriteBc7Field(bytes, 94 + end, 1, ends[12 + end] & 1);
        for (var i = 0; i < 16; i++) for (var b = 0; b < layout.Bits[i].Length; b++) WriteBc7Field(bytes, layout.Bits[i][b], 1, (indices[i] >> b) & 1);
        return bytes;
    }

    // Recover standard partition/anchor layouts from decoder probes instead of
    // maintaining another copy of the format's fixed partition tables.
    private static Mode7Layout[] ReadMode7Layouts()
    {
        var decoder = new BcDecoder(); var layouts = new List<Mode7Layout>();
        for (var partition = 0; partition < 64; partition++)
        {
            var probe = new byte[16]; WriteBc7Field(probe, 0, 8, 128); WriteBc7Field(probe, 8, 6, partition);
            for (var end = 0; end < 4; end++)
            {
                WriteBc7Field(probe, 14 + end * 5, 5, end < 2 ? 0 : 31);
                WriteBc7Field(probe, 74 + end * 5, 5, 31);
            }
            var members = decoder.DecodeRaw(probe, 4, 4, CompressionFormat.Bc7).Select(p => p.r > 128 ? 1 : 0).ToArray();
            for (var end = 0; end < 4; end++) WriteBc7Field(probe, 14 + end * 5, 5, end % 2 == 0 ? 0 : 31);
            var bits = Enumerable.Range(0, 16).Select(_ => new[] { -1, -1 }).ToArray();
            for (var bit = 98; bit < 128; bit++)
            {
                var sample = probe.ToArray(); WriteBc7Field(sample, bit, 1, 1);
                var rgba = decoder.DecodeRaw(sample, 4, 4, CompressionFormat.Bc7);
                var changed = Enumerable.Range(0, 16).Where(i => rgba[i].r != 0).ToArray();
                if (changed.Length != 1) throw new InvalidDataException("Mode7 selector probe is ambiguous.");
                var i = changed[0]; var selectorBit = rgba[i].r < 128 ? 0 : 1;
                if (bits[i][selectorBit] != -1) throw new InvalidDataException("Mode7 selector probe is duplicated.");
                bits[i][selectorBit] = bit;
            }
            if (bits.Any(b => b[0] < 0)) throw new InvalidDataException("Mode7 selector probe is incomplete.");
            var anchors = Enumerable.Range(0, 2).Select(subset => Enumerable.Range(0, 16).Single(i => members[i] == subset && bits[i][1] == -1)).ToArray();
            layouts.Add(new(partition, members, bits.Select(b => b.Where(v => v >= 0).ToArray()).ToArray(), anchors));
        }
        return layouts.ToArray();
    }

    private static void WriteBc7Field(byte[] bytes, int at, int count, int value)
    {
        for (var i = 0; i < count; i++) { var bit = at + i; bytes[bit / 8] = (byte)((bytes[bit / 8] & ~(1 << (bit % 8))) | (((value >> i) & 1) << (bit % 8))); }
    }
}
