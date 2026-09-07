// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Numerics;
using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;

namespace KM.Formats.Models;

public sealed record ModelTextureColorChange(string From, string To, int Tolerance);
public sealed record ModelTextureEncoding(byte[] Bytes, int ChangedPixels, double ColorError, int AlphaError);

/// <summary>Preserves texture containers while replacing bounded color image payloads.</summary>
public sealed class ModelTextureDocument
{
    private readonly byte[] original;
    private readonly Level[] levels;
    private readonly int tile;
    private readonly int blockHeight;
    private readonly int blockSize;
    private readonly int blockBytes;
    private readonly CompressionFormat compression;
    public int Width { get; }
    public int Height { get; }
    public uint Format { get; }
    public int MipCount => levels.Length;
    public bool Srgb => (Format & 0xff) == 6;
    private sealed record Level(int Width, int Height, int Offset, int End);

    public ModelTextureDocument(byte[] bytes)
    {
        if (bytes.Length > 32 * 1024 * 1024) throw new InvalidDataException("Texture exceeds the editing budget.");
        original = bytes.ToArray();
        var data = new ModelBuffer(original);
        int Pointer(int at) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(at, 8)));
        if (!data.Slice(0, 4).SequenceEqual("BNTX"u8) || data.U16(12) != 0xfeff || data.U32(36) != 1)
            throw new InvalidDataException("Texture container is unsupported for editing.");
        var info = Pointer(Pointer(40));
        if (!data.Slice(info, 4).SequenceEqual("BRTI"u8)) throw new InvalidDataException("Texture information is missing.");
        Format = data.U32(info + 28);
        compression = Format switch
        {
            0x0b01 or 0x0b06 => CompressionFormat.Rgba,
            0x1a01 or 0x1a06 => CompressionFormat.Bc1WithAlpha,
            0x1b01 or 0x1b06 => CompressionFormat.Bc2,
            0x1c01 or 0x1c06 => CompressionFormat.Bc3,
            0x2001 or 0x2006 => CompressionFormat.Bc7,
            _ => throw new InvalidDataException("Select a supported color texture for recoloring.")
        };
        blockSize = compression == CompressionFormat.Rgba ? 1 : 4;
        blockBytes = compression == CompressionFormat.Rgba ? 4 : compression == CompressionFormat.Bc1WithAlpha ? 8 : 16;
        Width = checked((int)data.U32(info + 36)); Height = checked((int)data.U32(info + 40));
        if (Width is < 1 or > 4096 || Height is < 1 or > 4096 || (long)Width * Height > 4 * 1024 * 1024
            || data.U32(info + 44) != 1 || data.U32(info + 48) != 1 || data.U32(info + 88) != 0x05040302)
            throw new InvalidDataException("Texture dimensions or channel mapping are unsupported for editing.");
        tile = data.U16(info + 18);
        blockHeight = 1 << checked((int)(data.U32(info + 52) & 7));
        if (tile is not (0 or 1)) throw new InvalidDataException("Texture tiling is unsupported.");
        var count = data.U16(info + 22);
        if (count < 1 || count > 1 + (int)Math.Log2(Math.Max(Width, Height)))
            throw new InvalidDataException("Texture mip count is invalid.");
        var pointers = Pointer(info + 112);
        data.Slice(pointers, count * 8);
        var first = Pointer(pointers);
        var end = checked(first + (int)data.U32(info + 80));
        data.Slice(first, end - first);
        if (first < Math.Max(info + 120, pointers + count * 8))
            throw new InvalidDataException("Texture image overlaps metadata.");
        levels = new Level[count];
        for (var mip = 0; mip < count; mip++)
        {
            var offset = Pointer(pointers + mip * 8);
            var next = mip + 1 < count ? Pointer(pointers + (mip + 1) * 8) : end;
            if (offset < first || next <= offset || next > end) throw new InvalidDataException("Texture mip ranges are invalid.");
            levels[mip] = new(Math.Max(1, Width >> mip), Math.Max(1, Height >> mip), offset, next);
            _ = Linear(levels[mip], original);
        }
    }

    public byte[] Pixels(int mip = 0)
    {
        if ((uint)mip >= levels.Length) throw new ArgumentOutOfRangeException(nameof(mip));
        return PixelBytes(Decode(levels[mip], original));
    }

    public byte[] Preview(IReadOnlyList<ModelTextureColorChange> changes) => PixelBytes(RecoloredPixels(changes, out _, default));

    private static byte[] PixelBytes(ColorRgba32[] pixels)
    {
        var result = new byte[pixels.Length * 4];
        for (var i = 0; i < pixels.Length; i++)
        {
            result[i * 4] = pixels[i].r; result[i * 4 + 1] = pixels[i].g;
            result[i * 4 + 2] = pixels[i].b; result[i * 4 + 3] = pixels[i].a;
        }
        return result;
    }

    private ColorRgba32[] RecoloredPixels(IReadOnlyList<ModelTextureColorChange> changes, out int changed, CancellationToken cancellationToken)
    {
        if (changes.Count > 32) throw new InvalidDataException("Too many texture color changes.");
        var rules = changes.Select(change => (From: Color(change.From), To: Color(change.To), change.Tolerance)).ToArray();
        if (rules.Any(rule => rule.Tolerance is < 0 or > 100)) throw new InvalidDataException("Color tolerance is invalid.");
        var pixels = Decode(levels[0], original);
        changed = 0;
        for (var i = 0; i < pixels.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            var before = pixels[i];
            foreach (var rule in rules)
            {
                if (pixels[i].a == 0 || rule.From == rule.To) continue;
                var value = new Vector3(pixels[i].r, pixels[i].g, pixels[i].b) / 255f;
                if (Vector3.Distance(value, rule.From) > rule.Tolerance / 100f * MathF.Sqrt(3) + 0.00001f) continue;
                // Offset the chosen color while retaining local shading differences.
                value = Vector3.Clamp(value + rule.To - rule.From, Vector3.Zero, Vector3.One);
                pixels[i] = new(Byte(value.X), Byte(value.Y), Byte(value.Z), before.a);
            }
            if (!pixels[i].Equals(before)) changed++;
        }
        return pixels;
    }

    public ModelTextureEncoding Recolor(IReadOnlyList<ModelTextureColorChange> changes, CancellationToken cancellationToken = default)
    {
        var pixels = RecoloredPixels(changes, out var changed, cancellationToken);
        if (changed == 0) return new(original.ToArray(), 0, 0, 0);
        var output = original.ToArray();
        var encoder = new BcEncoder(compression);
        encoder.OutputOptions.Quality = CompressionQuality.BestQuality;
        encoder.OutputOptions.GenerateMipMaps = false;
        encoder.Options.IsParallel = false;
        double squared = 0; long samples = 0; var alphaError = 0;
        for (var mip = 0; mip < levels.Length; mip++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var level = levels[mip];
            if (mip > 0)
            {
                var next = Downsample(pixels, levels[mip - 1], level);
                var authored = Decode(level, original);
                for (var i = 0; i < next.Length; i++) next[i].a = authored[i].a;
                pixels = next;
            }
            Encode(level, pixels, output, encoder, cancellationToken);
            var decoded = Decode(level, output);
            for (var i = 0; i < pixels.Length; i++)
            {
                var a = Math.Abs(pixels[i].a - decoded[i].a); alphaError = Math.Max(alphaError, a);
                // Keep cutout coverage exact; permit at most one byte of translucent rounding.
                if (a > 1 || (pixels[i].a == 0 && decoded[i].a != 0) || (pixels[i].a == 255 && decoded[i].a != 255))
                    throw new InvalidDataException($"Texture encoding could not preserve transparency at mip {mip}, pixel {i}: {pixels[i].a} became {decoded[i].a}.");
                if (pixels[i].a == 0) continue;
                squared += Math.Pow(pixels[i].r - decoded[i].r, 2) + Math.Pow(pixels[i].g - decoded[i].g, 2) + Math.Pow(pixels[i].b - decoded[i].b, 2);
                samples += 3;
            }
        }
        var error = samples == 0 ? 0 : Math.Sqrt(squared / samples);
        if (error > 24) throw new InvalidDataException("Texture encoding exceeded the color error limit.");
        _ = new ModelTextureDocument(output);
        return new(output, changed, error, alphaError);
    }

    private ColorRgba32[] Decode(Level level, byte[] bytes)
    {
        var linear = Linear(level, bytes);
        if (compression != CompressionFormat.Rgba)
            return new BcDecoder().DecodeRaw(linear, level.Width, level.Height, compression);
        return Enumerable.Range(0, level.Width * level.Height).Select(i => new ColorRgba32(linear[i * 4], linear[i * 4 + 1], linear[i * 4 + 2], linear[i * 4 + 3])).ToArray();
    }

    private byte[] Linear(Level level, byte[] bytes)
    {
        var stride = (level.Width + blockSize - 1) / blockSize * blockBytes;
        var rows = (level.Height + blockSize - 1) / blockSize;
        var result = new byte[checked(stride * rows)];
        for (var y = 0; y < rows; y++) for (var x = 0; x < stride; x++) result[y * stride + x] = bytes[Address(level, x, y, stride, rows)];
        return result;
    }

    private int Address(Level level, int x, int y, int stride, int rows)
    {
        var group = blockHeight;
        while (group > 1 && rows <= group * 4) group /= 2;
        var offset = tile == 1 ? y * ((stride + 31) / 32 * 32) + x
            : y / (8 * group) * ((stride + 63) / 64) * 512 * group + x / 64 * 512 * group
                + y % (8 * group) / 8 * 512 + x % 64 / 32 * 256 + y % 8 / 2 * 64 + x % 32 / 16 * 32 + y % 2 * 16 + x % 16;
        if (offset < 0 || offset >= level.End - level.Offset) throw new InvalidDataException("Texture mip payload is truncated.");
        return level.Offset + offset;
    }

    private void Encode(Level level, ColorRgba32[] pixels, byte[] output, BcEncoder encoder, CancellationToken token)
    {
        var before = Decode(level, original);
        var linear = Linear(level, original);
        var columns = (level.Width + blockSize - 1) / blockSize;
        var rows = (level.Height + blockSize - 1) / blockSize;
        for (var by = 0; by < rows; by++)
        {
            token.ThrowIfCancellationRequested();
            for (var bx = 0; bx < columns; bx++)
            {
                var block = new ColorRgba32[blockSize * blockSize]; var different = false;
                for (var y = 0; y < blockSize; y++) for (var x = 0; x < blockSize; x++)
                {
                    var index = Math.Min(level.Height - 1, by * blockSize + y) * level.Width + Math.Min(level.Width - 1, bx * blockSize + x);
                    block[y * blockSize + x] = pixels[index];
                    different |= !pixels[index].Equals(before[index]);
                }
                if (!different) continue;
                var encoded = compression == CompressionFormat.Rgba ? new[] { block[0].r, block[0].g, block[0].b, block[0].a }
                    : compression == CompressionFormat.Bc7 ? [] : encoder.EncodeBlock(block.AsSpan());
                if (compression == CompressionFormat.Bc7)
                {
                    double ColorError(byte[] candidate)
                    {
                        var decoded = new BcDecoder().DecodeRaw(candidate, 4, 4, compression);
                        double error = 0; var count = 0;
                        for (var i = 0; i < block.Length; i++)
                        {
                            if (block[i].a == 0) continue;
                            error += Math.Pow(decoded[i].r - block[i].r, 2) + Math.Pow(decoded[i].g - block[i].g, 2) + Math.Pow(decoded[i].b - block[i].b, 2); count += 3;
                        }
                        return count == 0 ? 0 : Math.Sqrt(error / count);
                    }
                    bool AlphaMatches(byte[] candidate) => !new BcDecoder().DecodeRaw(candidate, 4, 4, compression)
                        .Where((p, i) => Math.Abs(p.a - block[i].a) > 1 || (block[i].a is 0 or 255 && p.a != block[i].a)).Any();
                    var refitted = ModelTextureAlphaBlock.RefitSource(block, linear.AsSpan((by * columns + bx) * blockBytes, blockBytes));
                    if (refitted is not null && ColorError(refitted) <= 3 && AlphaMatches(refitted)) encoded = refitted;
                    else
                    {
                        var bestError = double.PositiveInfinity;
                        void Consider(byte[] candidate)
                        {
                            if (!AlphaMatches(candidate)) return;
                            var error = ColorError(candidate);
                            if (error < bestError) { bestError = error; encoded = candidate; }
                        }
                        if (refitted is not null) Consider(refitted);
                        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
                        Consider(encoder.EncodeBlock(block.AsSpan()));
                        Consider(ModelTextureAlphaBlock.Encode(block));
                        if (bestError > 12)
                        {
                            encoder.OutputOptions.Quality = CompressionQuality.BestQuality;
                            Consider(encoder.EncodeBlock(block.AsSpan()));
                        }
                        if (double.IsPositiveInfinity(bestError)) throw new InvalidDataException("Texture encoding could not preserve transparency.");
                    }
                }
                if (encoded.Length != blockBytes) throw new InvalidDataException("Texture block length changed.");
                if (compression is CompressionFormat.Bc2 or CompressionFormat.Bc3)
                    linear.AsSpan((by * columns + bx) * blockBytes, 8).CopyTo(encoded);
                for (var b = 0; b < blockBytes; b++) output[Address(level, bx * blockBytes + b, by, columns * blockBytes, rows)] = encoded[b];
            }
        }
    }

    private ColorRgba32[] Downsample(ColorRgba32[] input, Level source, Level target)
    {
        var result = new ColorRgba32[target.Width * target.Height];
        for (var y = 0; y < target.Height; y++) for (var x = 0; x < target.Width; x++)
        {
            var startX = x * source.Width / (float)target.Width; var endX = (x + 1) * source.Width / (float)target.Width;
            var startY = y * source.Height / (float)target.Height; var endY = (y + 1) * source.Height / (float)target.Height;
            var sum = Vector3.Zero; float weight = 0;
            for (var sy = (int)startY; sy < Math.Ceiling(endY); sy++) for (var sx = (int)startX; sx < Math.Ceiling(endX); sx++)
            {
                var pixel = input[sy * source.Width + sx];
                var w = (Math.Min(sx + 1, endX) - Math.Max(sx, startX)) * (Math.Min(sy + 1, endY) - Math.Max(sy, startY)) * pixel.a / 255f;
                sum += new Vector3(LinearColor(pixel.r), LinearColor(pixel.g), LinearColor(pixel.b)) * w; weight += w;
            }
            var color = weight == 0 ? Vector3.Zero : sum / weight;
            result[y * target.Width + x] = new(EncodedColor(color.X), EncodedColor(color.Y), EncodedColor(color.Z), 255);
        }
        return result;
    }
    private float LinearColor(byte value) { var c = value / 255f; return !Srgb ? c : c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f); }
    private byte EncodedColor(float c) => Byte(!Srgb ? c : c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f);
    private static byte Byte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
    private static Vector3 Color(string color)
    {
        if (color is not { Length: 7 } || color[0] != '#' || !color.AsSpan(1).ToArray().All(char.IsAsciiHexDigit))
            throw new InvalidDataException("Use a six-digit HEX color.");
        return new Vector3(Convert.ToByte(color[1..3], 16), Convert.ToByte(color[3..5], 16), Convert.ToByte(color[5..7], 16)) / 255f;
    }
}
