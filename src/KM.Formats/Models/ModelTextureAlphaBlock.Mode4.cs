// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using BCnEncoder.Shared;

namespace KM.Formats.Models;

internal static partial class ModelTextureAlphaBlock
{
    private static ReadOnlySpan<int> AlphaWeights8 => [0, 9, 18, 27, 37, 46, 55, 64];
    private sealed record FourColorFit(int[] Ends, int[] Indices, double Error);

    internal static byte[]? FitMode4(ColorRgba32[] pixels, ushort visible = ushort.MaxValue)
    {
        if (pixels.Length != 16 || visible == 0) throw new InvalidDataException("Alpha fit requires a visible 4x4 block.");
        var allowed = Enumerable.Range(0, 16).Select(i => (visible & (1 << i)) != 0 ? 15 : 0).ToArray();
        var color = FitFourRgb(pixels, allowed, (value, _) => ExpandEndpoint5(NearestEndpoint(value, 5, null)));
        if (color.Error > Enumerable.Range(0, 16).Count(i => allowed[i] != 0) * 27) return null;
        var minimum = Enumerable.Range(0, 16).Where(i => allowed[i] != 0).Min(i => pixels[i].a);
        var maximum = Enumerable.Range(0, 16).Where(i => allowed[i] != 0).Max(i => pixels[i].a);
        int[]? bestIndices = null; var bestError = double.PositiveInfinity; var first = 0; var last = 0;
        var selectors = new int[16];
        for (var low = 0; low < 64; low++) for (var high = low; high < 64; high++)
        {
            var a = ExpandEndpoint6(low); var b = ExpandEndpoint6(high);
            if (a > minimum + 1 || b < maximum - 1 || minimum == 0 && a != 0 || maximum == 255 && b != 255) continue;
            double error = 0; var valid = true;
            for (var i = 0; i < 16; i++)
            {
                selectors[i] = 0; if (allowed[i] == 0) continue;
                var distance = int.MaxValue;
                for (var j = 0; j < 8; j++)
                {
                    var value = ((64 - AlphaWeights8[j]) * a + AlphaWeights8[j] * b + 32) >> 6;
                    if (pixels[i].a is 0 or 255 && value != pixels[i].a) continue;
                    var difference = Math.Abs(value - pixels[i].a);
                    if (difference < distance) { selectors[i] = j; distance = difference; }
                }
                if (distance > 1) { valid = false; break; }
                error += distance * distance;
            }
            if (!valid || error >= bestError) continue;
            first = low; last = high; bestError = error; bestIndices = selectors.ToArray();
            if (error == 0) break;
        }
        if (bestIndices is null) return null;
        var ends = color.Ends.Select(e => NearestEndpoint(e, 5, null)).ToArray(); var indices = color.Indices.ToArray();
        if (indices[0] >= 2)
        {
            for (var c = 0; c < 3; c++) (ends[c * 2], ends[c * 2 + 1]) = (ends[c * 2 + 1], ends[c * 2]);
            for (var i = 0; i < 16; i++) indices[i] = 3 - indices[i];
        }
        if (bestIndices[0] >= 4)
        {
            (first, last) = (last, first);
            for (var i = 0; i < 16; i++) bestIndices[i] = 7 - bestIndices[i];
        }
        var bytes = new byte[16]; var bit = 0;
        Put(16, 5); Put(0, 2); Put(0, 1);
        foreach (var end in ends) Put(end, 5);
        Put(first, 6); Put(last, 6);
        for (var i = 0; i < 16; i++) Put(indices[i], i == 0 ? 1 : 2);
        for (var i = 0; i < 16; i++) Put(bestIndices[i], i == 0 ? 2 : 3);
        if (bit != 128) throw new InvalidDataException("Mode 4 alpha block length is invalid.");
        return bytes;
        void Put(int value, int count) { for (var i = 0; i < count; i++, bit++) bytes[bit / 8] |= (byte)(((value >> i) & 1) << (bit % 8)); }
    }

    private static int ExpandEndpoint5(int value) => (value << 3) | (value >> 2);
    private static int ExpandEndpoint6(int value) => (value << 2) | (value >> 4);
    private static int NearestEndpoint(double value, int bits, int? parity)
    {
        var best = 0; var error = double.PositiveInfinity;
        for (var q = 0; q < (1 << bits); q++)
        {
            if (parity is { } p && (q & 1) != p) continue;
            var distance = Math.Abs((bits == 5 ? ExpandEndpoint5(q) : ExpandEndpoint6(q)) - value);
            if (distance < error) { error = distance; best = q; }
        }
        return best;
    }
    private static int InterpolateFour(int first, int last, int index)
    {
        var weight = index switch { 0 => 0, 1 => 21, 2 => 43, _ => 64 };
        return ((64 - weight) * first + weight * last + 32) >> 6;
    }

    // Alpha-constrained selectors also support the two-subset mode-7 fitter.
    private static FourColorFit FitFourRgb(ColorRgba32[] pixels, int[] allowed, Func<double, int, int> quantize)
    {
        var colors = pixels.Select(p => new Vector3(p.r, p.g, p.b)).ToArray();
        var active = Enumerable.Range(0, 16).Where(i => allowed[i] != 0).ToArray();
        if (active.Length == 0) return new(new int[6], new int[16], 0);
        var mean = active.Aggregate(Vector3.Zero, (sum, i) => sum + colors[i]) / active.Length; var axis = Vector3.One;
        for (var step = 0; step < 8; step++)
        {
            var next = active.Aggregate(Vector3.Zero, (sum, i) => sum + (colors[i] - mean) * Vector3.Dot(colors[i] - mean, axis));
            if (next.LengthSquared() < .001f) break; axis = Vector3.Normalize(next);
        }
        var ordered = active.OrderBy(i => Vector3.Dot(colors[i], axis)).ToArray(); var ends = new int[6]; var indices = new int[16];
        for (var c = 0; c < 3; c++) { ends[c * 2] = quantize(colors[ordered[0]][c], 0); ends[c * 2 + 1] = quantize(colors[ordered[^1]][c], 1); }
        for (var step = 0; step < 6; step++)
        {
            Select();
            double aa = 0, ab = 0, bb = 0; var av = new double[3]; var bv = new double[3];
            foreach (var i in active)
            {
                var b = (indices[i] switch { 0 => 0, 1 => 21, 2 => 43, _ => 64 }) / 64d; var a = 1 - b;
                aa += a * a; ab += a * b; bb += b * b;
                for (var c = 0; c < 3; c++) { av[c] += a * colors[i][c]; bv[c] += b * colors[i][c]; }
            }
            var determinant = aa * bb - ab * ab;
            for (var c = 0; c < 3; c++)
            {
                ends[c * 2] = quantize(determinant > 1e-10 ? (av[c] * bb - bv[c] * ab) / determinant : mean[c], 0);
                ends[c * 2 + 1] = quantize(determinant > 1e-10 ? (bv[c] * aa - av[c] * ab) / determinant : mean[c], 1);
            }
        }
        Select(); var squared = Squared();
        // Joint endpoint quantization can beat independently rounded least-squares
        // endpoints, especially when mode7 shares their parity with alpha.
        for (var pass = 0; pass < 4 && squared != 0; pass++)
        {
            var before = squared;
            for (var slot = 0; slot < 6; slot++)
            {
                var original = ends[slot]; var best = original; var error = squared;
                foreach (var delta in new[] { -8, 8 })
                {
                    ends[slot] = quantize(original + delta, slot % 2); Select(); var candidate = Squared();
                    if (candidate < error) { best = ends[slot]; error = candidate; }
                }
                ends[slot] = best; Select(); squared = error;
            }
            if (squared == before) break;
        }
        return new(ends, indices, squared);
        double Squared()
        {
            double result = 0;
            foreach (var i in active) for (var c = 0; c < 3; c++) result += Math.Pow(InterpolateFour(ends[c * 2], ends[c * 2 + 1], indices[i]) - colors[i][c], 2);
            return result;
        }
        void Select()
        {
            foreach (var i in active)
            {
                var error = double.PositiveInfinity;
                for (var j = 0; j < 4; j++)
                {
                    if ((allowed[i] & (1 << j)) == 0) continue;
                    double distance = 0; for (var c = 0; c < 3; c++) distance += Math.Pow(InterpolateFour(ends[c * 2], ends[c * 2 + 1], j) - colors[i][c], 2);
                    if (distance < error) { error = distance; indices[i] = j; }
                }
            }
        }
    }
}
