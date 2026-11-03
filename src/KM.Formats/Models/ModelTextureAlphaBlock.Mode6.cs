// SPDX-License-Identifier: GPL-3.0-only
using BCnEncoder.Shared;

namespace KM.Formats.Models;

internal static partial class ModelTextureAlphaBlock
{
    private static ReadOnlySpan<int> Mode6Weights => [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

    // Observed min/max alpha endpoints can miss representable intermediate values.
    // Search the legal endpoint range and fit RGB with the same mode-6 indices and
    // shared endpoint parity. Retain the existing three-unit RGB RMS bound.
    internal static byte[]? FitMode6(ColorRgba32[] pixels, ushort visible = ushort.MaxValue)
    {
        if (pixels.Length != 16 || visible == 0) throw new InvalidDataException("Alpha fit requires a visible 4x4 block.");
        bool Visible(int i) => (visible & (1 << i)) != 0;
        var minimum = Enumerable.Range(0, 16).Where(Visible).Min(i => (int)pixels[i].a);
        var maximum = Enumerable.Range(0, 16).Where(Visible).Max(i => (int)pixels[i].a);
        var colorSamples = Enumerable.Range(0, 16).Count(Visible) * 3;
        var indices = new int[16]; var palette = new int[16]; var ends = new int[8];
        byte[]? best = null; var bestError = double.PositiveInfinity;
        for (var low = 0; low <= Math.Min(255, minimum + 1); low++)
        {
            if (minimum == 0 && low != 0) continue;
            for (var high = Math.Max(low, maximum - 1); high <= 255; high++)
            {
                if (maximum == 255 && high != 255) continue;
                for (var j = 0; j < 16; j++) palette[j] = Mode6Interpolate(low, high, j);
                var valid = true;
                for (var i = 0; i < 16; i++)
                {
                    indices[i] = 0; if (!Visible(i)) continue;
                    var error = int.MaxValue;
                    for (var j = 0; j < 16; j++)
                    {
                        if (pixels[i].a is 0 or 255 && palette[j] != pixels[i].a) continue;
                        var distance = Math.Abs(palette[j] - pixels[i].a);
                        if (distance < error) { indices[i] = j; error = distance; }
                    }
                    if (error > 1) { valid = false; break; }
                }
                if (!valid) continue;
                ends[6] = low; ends[7] = high;
                for (var channel = 0; channel < 3; channel++)
                {
                    double aa = 0, ab = 0, bb = 0, av = 0, bv = 0, sum = 0; var count = 0;
                    for (var i = 0; i < 16; i++)
                    {
                        if (!Visible(i)) continue;
                        var b = Mode6Weights[indices[i]] / 64d; var a = 1 - b; var value = Mode6Channel(pixels[i], channel);
                        aa += a * a; ab += a * b; bb += b * b; av += a * value; bv += b * value; sum += value; count++;
                    }
                    var determinant = aa * bb - ab * ab;
                    var first = determinant > 1e-10 ? (av * bb - bv * ab) / determinant : sum / count;
                    var last = determinant > 1e-10 ? (bv * aa - av * ab) / determinant : sum / count;
                    ends[channel * 2] = Mode6Quantize(first, low & 1);
                    ends[channel * 2 + 1] = Mode6Quantize(last, high & 1);
                }
                double colorError = 0;
                for (var i = 0; i < 16 && valid; i++)
                {
                    if (!Visible(i)) continue;
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var difference = Mode6Interpolate(ends[channel * 2], ends[channel * 2 + 1], indices[i]) - Mode6Channel(pixels[i], channel);
                        colorError += difference * difference;
                    }
                }
                if (colorError > colorSamples * 9 || colorError >= bestError) continue;
                best = Mode6Pack(ends, indices); bestError = colorError;
                if (bestError == 0) return best;
            }
        }
        return best;
    }

    private static int Mode6Channel(ColorRgba32 p, int channel) => channel switch { 0 => p.r, 1 => p.g, _ => p.b };
    private static int Mode6Interpolate(int first, int last, int index) => ((64 - Mode6Weights[index]) * first + Mode6Weights[index] * last + 32) >> 6;
    private static int Mode6Quantize(double value, int parity) => Math.Clamp((int)Math.Round((Math.Clamp(value, 0, 255) - parity) / 2, MidpointRounding.AwayFromZero), 0, 127) * 2 + parity;

    private static byte[] Mode6Pack(int[] endpoints, int[] selectors)
    {
        var ends = endpoints.ToArray(); var indices = selectors.ToArray();
        if (indices[0] >= 8)
        {
            for (var c = 0; c < 4; c++) (ends[c * 2], ends[c * 2 + 1]) = (ends[c * 2 + 1], ends[c * 2]);
            for (var i = 0; i < 16; i++) indices[i] = 15 - indices[i];
        }
        var bytes = new byte[16]; var bit = 0;
        Put(64, 7);
        foreach (var endpoint in ends) Put(endpoint >> 1, 7);
        Put(ends[6] & 1, 1); Put(ends[7] & 1, 1);
        for (var i = 0; i < 16; i++) Put(indices[i], i == 0 ? 3 : 4);
        if (bit != 128) throw new InvalidDataException("Mode 6 alpha block length is invalid.");
        return bytes;
        void Put(int value, int count) { for (var i = 0; i < count; i++, bit++) bytes[bit / 8] |= (byte)(((value >> i) & 1) << (bit % 8)); }
    }
}
