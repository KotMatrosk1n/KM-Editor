// SPDX-License-Identifier: GPL-3.0-only
using System.Numerics;
using BCnEncoder.Shared;

namespace KM.Formats.Models;

/// <summary>BC7 mode 5 keeps alpha endpoints independent from color quantization.</summary>
internal static class ModelTextureAlphaBlock
{
    // Preserve the source's alpha representation and indices when smooth alpha
    // cannot fit the four values in mode 5. Only color endpoints are rewritten.
    internal static byte[]? RefitSource(ColorRgba32[] pixels, ReadOnlySpan<byte> source)
    {
        var bytes = source.ToArray();
        var mode = BitOperations.TrailingZeroCount((uint)bytes[0]);
        int Get(int at, int count) { var value = 0; for (var i = 0; i < count; i++) value |= ((bytes[(at + i) / 8] >> ((at + i) % 8)) & 1) << i; return value; }
        void Set(int at, int count, int value) { for (var i = 0; i < count; i++) { var bit = at + i; bytes[bit / 8] = (byte)((bytes[bit / 8] & ~(1 << (bit % 8))) | (((value >> i) & 1) << (bit % 8))); } }
        if (mode == 7)
        {
            // Decode endpoint probes to recover the existing partition and index
            // assignments without duplicating the format's partition tables.
            for (var end = 0; end < 4; end++)
            {
                Set(14 + end * 5, 5, end % 2 == 0 ? 0 : 31);
                Set(34 + end * 5, 5, end < 2 ? 0 : 31);
            }
            var assignment = new BCnEncoder.Decoder.BcDecoder().DecodeRaw(bytes, 4, 4, CompressionFormat.Bc7);
            bytes = source.ToArray();
            for (var subset = 0; subset < 2; subset++) for (var channel = 0; channel < 3; channel++)
            {
                float aa = 0, ab = 0, bb = 0, av = 0, bv = 0;
                for (var i = 0; i < 16; i++)
                {
                    if (pixels[i].a == 0 || (assignment[i].g < 128 ? 0 : 1) != subset) continue;
                    var index = Enumerable.Range(0, 4).MinBy(n => Math.Abs(assignment[i].r - Weight(n) * 255 / 64));
                    var b = Weight(index) / 64f; var a = 1 - b;
                    var value = channel == 0 ? pixels[i].r : channel == 1 ? pixels[i].g : pixels[i].b;
                    aa += a * a; ab += a * b; bb += b * b; av += a * value; bv += b * value;
                }
                var determinant = aa * bb - ab * ab;
                if (aa + 2 * ab + bb < 0.0001f) continue;
                var average = (av + bv) / (aa + 2 * ab + bb);
                var endpoints = determinant < 0.0001f ? new[] { average, average }
                    : new[] { (av * bb - bv * ab) / determinant, (bv * aa - av * ab) / determinant };
                for (var end = 0; end < 2; end++)
                {
                    var endpoint = subset * 2 + end;
                    var quantized = (int)MathF.Round((endpoints[end] * 63 / 255 - Get(94 + endpoint, 1)) / 2);
                    Set(14 + (channel * 4 + endpoint) * 5, 5, Math.Clamp(quantized, 0, 31));
                }
            }
            return bytes;
        }
        var bits = 7; var endpointStart = 7; var indexStart = 65; var indexBits = 4; var rotation = 0;
        if (mode is 4 or 5)
        {
            rotation = Get(mode + 1, 2);
            bits = mode == 4 ? 5 : 7; endpointStart = 8;
            indexBits = mode == 4 && Get(7, 1) == 1 ? 3 : 2;
            indexStart = mode == 5 ? 66 : indexBits == 3 ? 81 : 50;
        }
        else if (mode != 6) return null;
        int WeightAt(int index) => indexBits switch
        {
            2 => new[] { 0, 21, 43, 64 }[index],
            3 => new[] { 0, 9, 18, 27, 37, 46, 55, 64 }[index],
            _ => new[] { 0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64 }[index]
        };
        var colorBits = bits; var colorStart = endpointStart; var colorIndex = indexStart; var colorIndexBits = indexBits;
        for (var channel = 0; channel < 3; channel++)
        {
            var rotatedColor = rotation == channel + 1;
            bits = rotatedColor ? mode == 4 ? 6 : 8 : colorBits;
            endpointStart = rotatedColor ? mode == 4 ? 38 : 50 : colorStart + channel * 2 * bits;
            indexBits = rotatedColor ? mode == 4 ? 5 - colorIndexBits : 2 : colorIndexBits;
            indexStart = rotatedColor ? mode == 5 ? 97 : indexBits == 3 ? 81 : 50 : colorIndex;
            var weights = new float[16]; var cursor = indexStart;
            for (var i = 0; i < 16; i++) { var count = i == 0 ? indexBits - 1 : indexBits; weights[i] = WeightAt(Get(cursor, count)) / 64f; cursor += count; }
            float aa = 0, ab = 0, bb = 0, av = 0, bv = 0;
            for (var i = 0; i < 16; i++)
            {
                if (pixels[i].a == 0) continue;
                var value = channel == 0 ? pixels[i].r : channel == 1 ? pixels[i].g : pixels[i].b;
                var b = weights[i]; var a = 1 - b;
                aa += a * a; ab += a * b; bb += b * b; av += a * value; bv += b * value;
            }
            var determinant = aa * bb - ab * ab;
            if (aa + 2 * ab + bb < 0.0001f) continue;
            var average = (av + bv) / (aa + 2 * ab + bb);
            var endpoints = determinant < 0.0001f ? new[] { average, average }
                : new[] { (av * bb - bv * ab) / determinant, (bv * aa - av * ab) / determinant };
            for (var end = 0; end < 2; end++)
            {
                var maximum = (1 << bits) - 1;
                var quantized = mode == 6 ? (int)MathF.Round((endpoints[end] - Get(63 + end, 1)) / 2)
                    : (int)MathF.Round(endpoints[end] * maximum / 255);
                Set(endpointStart + end * bits, bits, Math.Clamp(quantized, 0, maximum));
            }
        }
        return bytes;
    }

    internal static byte[] Encode(ColorRgba32[] pixels)
    {
        var colors = pixels.Select(p => new Vector3(p.r, p.g, p.b)).ToArray();
        var mean = colors.Aggregate(Vector3.Zero, (sum, value) => sum + value) / 16;
        var axis = Vector3.One;
        for (var step = 0; step < 8; step++)
        {
            var next = colors.Aggregate(Vector3.Zero, (sum, value) => sum + (value - mean) * Vector3.Dot(value - mean, axis));
            if (next.LengthSquared() < 0.001f) break;
            axis = Vector3.Normalize(next);
        }
        var ordered = colors.OrderBy(value => Vector3.Dot(value, axis)).ToArray();
        var endpoints = new[] { Quantize(ordered[0]), Quantize(ordered[^1]) };
        var indices = new int[16];
        for (var step = 0; step < 4; step++)
        {
            SelectColors();
            if (step == 3) break;
            float aa = 0, ab = 0, bb = 0; var av = Vector3.Zero; var bv = Vector3.Zero;
            for (var i = 0; i < 16; i++)
            {
                var b = Weight(indices[i]) / 64f; var a = 1 - b;
                aa += a * a; ab += a * b; bb += b * b; av += a * colors[i]; bv += b * colors[i];
            }
            var determinant = aa * bb - ab * ab;
            if (Math.Abs(determinant) < 0.0001f) break;
            endpoints[0] = Quantize((av * bb - bv * ab) / determinant);
            endpoints[1] = Quantize((bv * aa - av * ab) / determinant);
        }
        if (indices[0] >= 2)
        {
            (endpoints[0], endpoints[1]) = (endpoints[1], endpoints[0]);
            for (var i = 0; i < 16; i++) indices[i] = 3 - indices[i];
        }
        var alpha = new[] { (int)pixels.Min(p => p.a), (int)pixels.Max(p => p.a) };
        var alphaIndices = pixels.Select(p => Enumerable.Range(0, 4).MinBy(index => Math.Abs(p.a - Interpolate(alpha[0], alpha[1], index)))).ToArray();
        if (alphaIndices[0] >= 2)
        {
            (alpha[0], alpha[1]) = (alpha[1], alpha[0]);
            for (var i = 0; i < 16; i++) alphaIndices[i] = 3 - alphaIndices[i];
        }
        var bytes = new byte[16]; var bit = 0;
        Put(32, 6); Put(0, 2);
        for (var channel = 0; channel < 3; channel++) { Put((int)endpoints[0][channel], 7); Put((int)endpoints[1][channel], 7); }
        Put(alpha[0], 8); Put(alpha[1], 8);
        for (var i = 0; i < 16; i++) Put(indices[i], i == 0 ? 1 : 2);
        for (var i = 0; i < 16; i++) Put(alphaIndices[i], i == 0 ? 1 : 2);
        if (bit != 128) throw new InvalidDataException("Texture alpha block length is invalid.");
        return bytes;
        void Put(int value, int count) { for (var i = 0; i < count; i++, bit++) bytes[bit / 8] |= (byte)(((value >> i) & 1) << (bit % 8)); }
        void SelectColors()
        {
            for (var i = 0; i < 16; i++) indices[i] = Enumerable.Range(0, 4).MinBy(index => Vector3.DistanceSquared(colors[i],
                new(Interpolate(Expand(endpoints[0].X), Expand(endpoints[1].X), index),
                    Interpolate(Expand(endpoints[0].Y), Expand(endpoints[1].Y), index),
                    Interpolate(Expand(endpoints[0].Z), Expand(endpoints[1].Z), index))));
        }
    }
    private static int Weight(int index) => index switch { 0 => 0, 1 => 21, 2 => 43, _ => 64 };
    private static int Interpolate(int a, int b, int index) => ((64 - Weight(index)) * a + Weight(index) * b + 32) >> 6;
    private static int Expand(float value) => ((int)value << 1) | ((int)value >> 6);
    private static Vector3 Quantize(Vector3 value) => new(Q(value.X), Q(value.Y), Q(value.Z));
    private static int Q(float value) => Math.Clamp((int)MathF.Round(value * 127 / 255), 0, 127);
}
