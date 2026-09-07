// SPDX-License-Identifier: GPL-3.0-only
using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;

namespace KM.Formats.Models;

public static class PreviewTextureResolution
{
    public static PreviewTexture Select(PreviewTexture texture, int divisor)
    {
        if (divisor is not (1 or 2 or 4)) throw new InvalidDataException("Model preview resolution is invalid.");
        var levels = new List<byte[]> { texture.Blocks };
        levels.AddRange(texture.Mips);
        var width = Math.Max(1, texture.Width >> (levels.Count - 1));
        var height = Math.Max(1, texture.Height >> (levels.Count - 1));
        var format = Compression(texture.Format);
        var encoder = new BcEncoder(format);
        encoder.OutputOptions.Quality = CompressionQuality.Fast;
        encoder.Options.IsParallel = false;
        ColorRgba32[]? pixels = null;
        while (width > 1 || height > 1)
        {
            pixels ??= Decode(levels[^1], width, height, format);
            var w = Math.Max(1, width / 2); var h = Math.Max(1, height / 2);
            pixels = Downsample(pixels, width, height, w, h, (texture.Format & 255) == 6);
            levels.Add(Encode(pixels, w, h, format, encoder)); width = w; height = h;
        }
        var skip = Math.Min(divisor == 4 ? 2 : divisor == 2 ? 1 : 0, levels.Count - 1);
        return texture with { Width = Math.Max(1, texture.Width >> skip), Height = Math.Max(1, texture.Height >> skip),
            Blocks = levels[skip], Mips = levels.Skip(skip + 1).ToArray() };
    }
    private static CompressionFormat Compression(uint format) => format switch
    {
        0x0b01 or 0x0b06 => CompressionFormat.Rgba,
        0x1a01 or 0x1a06 => CompressionFormat.Bc1WithAlpha,
        0x1b01 or 0x1b06 => CompressionFormat.Bc2,
        0x1c01 or 0x1c06 => CompressionFormat.Bc3,
        0x1d01 => CompressionFormat.Bc4,
        0x1e01 => CompressionFormat.Bc5,
        0x2001 or 0x2006 => CompressionFormat.Bc7,
        _ => throw new InvalidDataException("Texture compression is unsupported.")
    };
    private static ColorRgba32[] Decode(byte[] bytes, int width, int height, CompressionFormat format) => format == CompressionFormat.Rgba
        ? Enumerable.Range(0, width * height).Select(i => new ColorRgba32(bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2], bytes[i * 4 + 3])).ToArray()
        : new BcDecoder().DecodeRaw(bytes, width, height, format);

    private static ColorRgba32[] Downsample(ColorRgba32[] source, int width, int height, int w, int h, bool srgb)
    {
        var result = new ColorRgba32[w * h];
        float Linear(byte b) { var v = b / 255f; return srgb ? v <= .04045f ? v / 12.92f : MathF.Pow((v + .055f) / 1.055f, 2.4f) : v; }
        byte Channel(float v) => (byte)Math.Clamp((int)MathF.Round((srgb ? v <= .0031308f ? v * 12.92f : 1.055f * MathF.Pow(v, 1 / 2.4f) - .055f : v) * 255), 0, 255);
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            float r = 0, g = 0, b = 0, alpha = 0, weight = 0; var count = 0;
            for (var sy = y * height / h; sy < (y + 1) * height / h; sy++)
            for (var sx = x * width / w; sx < (x + 1) * width / w; sx++)
            {
                var p = source[sy * width + sx]; var a = srgb ? p.a / 255f : 1;
                r += Linear(p.r) * a; g += Linear(p.g) * a; b += Linear(p.b) * a; weight += a; alpha += p.a; count++;
            }
            result[y * w + x] = new(Channel(weight == 0 ? 0 : r / weight), Channel(weight == 0 ? 0 : g / weight),
                Channel(weight == 0 ? 0 : b / weight), (byte)MathF.Round(alpha / count));
        }
        return result;
    }
    private static byte[] Encode(ColorRgba32[] pixels, int w, int h, CompressionFormat format, BcEncoder encoder)
    {
        if (format == CompressionFormat.Rgba) return pixels.SelectMany(p => new[] { p.r, p.g, p.b, p.a }).ToArray();
        using var output = new MemoryStream();
        var block = new ColorRgba32[16];
        for (var y = 0; y < h; y += 4) for (var x = 0; x < w; x += 4)
        {
            for (var by = 0; by < 4; by++) for (var bx = 0; bx < 4; bx++)
                block[by * 4 + bx] = pixels[Math.Min(h - 1, y + by) * w + Math.Min(w - 1, x + bx)];
            output.Write(encoder.EncodeBlock(block.AsSpan()));
        }
        return output.ToArray();
    }
}
