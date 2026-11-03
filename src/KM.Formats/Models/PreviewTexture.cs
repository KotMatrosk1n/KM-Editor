// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;

namespace KM.Formats.Models;

public sealed record PreviewTexture(int Width, int Height, uint Format, byte[] Blocks)
{
    public string? SourcePath { get; init; }
    public byte[][] Mips { get; init; } = [];
    public long ByteLength => Blocks.LongLength + Mips.Sum(m => m.LongLength);
    public static PreviewTexture ReadBaseLevel(byte[] bytes) => ReadLevel(bytes, 0);
    public static PreviewTexture Read(byte[] bytes) => ModelDerivedCache<PreviewTexture>.Get(bytes, "",
        () => PreviewTextureResolution.Select(ReadUncached(bytes), 1), texture => texture.ByteLength);
    /// <summary>Retains authored compressed channels and legal mip levels for source shader consumers.</summary>
    public static PreviewTexture Read(byte[] bytes, bool preserveCompressedChannels) => !preserveCompressedChannels ? Read(bytes)
        : ModelDerivedCache<PreviewTexture>.Get(bytes, "compressed-channels",
            () => ReadUncached(bytes, true), texture => texture.ByteLength);
    private static PreviewTexture ReadUncached(byte[] bytes, bool preserveCompressedChannels = false)
    {
        var texture = ReadLevel(bytes, 0, preserveCompressedChannels);
        if (texture.Format == 0x1505) return texture;
        if (texture.Format == PreviewBc6Cube.Format) return PreviewBc6Cube.Read(bytes);
        var data = new ModelBuffer(bytes);
        var info = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(checked((int)BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(40, 8))), 8)));
        var count = data.U16(info + 22);
        if (count is < 1 or > 32)
            throw new InvalidDataException("Texture mip count is invalid.");
        var maximum = 1 + (int)Math.Log2(Math.Max(texture.Width, texture.Height));
        if (preserveCompressedChannels && count > maximum)
            throw new InvalidDataException("Source texture has more than its legal authored mip levels.");
        var mips = new List<byte[]>();
        for (var level = 1; level < count; level++)
        {
            var mip = ReadLevel(bytes, level, preserveCompressedChannels);
            // Validate stored 1x1 tails, but send only a legal GPU mip chain.
            // The editing document retains and updates every stored level.
            if (level < maximum) mips.Add(mip.Blocks);
        }
        return texture with { Mips = mips.ToArray() };
    }
    internal static PreviewTexture ReadLevel(byte[] bytes, int level) => ReadLevel(bytes, level, false);
    private static PreviewTexture ReadLevel(byte[] bytes, int level, bool preserveCompressedChannels)
    {
        var data = new ModelBuffer(bytes);
        if (!data.Slice(0, 4).SequenceEqual("BNTX"u8) || data.U16(12) != 0xfeff || data.U32(36) != 1)
            throw new InvalidDataException("Texture container layout is unsupported.");
        int Pointer(int at)
        {
            var pointer = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(at, 8));
            if (pointer > int.MaxValue) throw new InvalidDataException("Texture pointer is unsupported.");
            return (int)pointer;
        }
        var info = Pointer(Pointer(40));
        if (!data.Slice(info, 4).SequenceEqual("BRTI"u8)) throw new InvalidDataException("Texture information is missing.");
        var format = data.U32(info + 28);
        if (format == PreviewBc6Cube.Format) return PreviewBc6Cube.Read(bytes, level);
        if (format == 0x1505)
        {
            if (level != 0) throw new InvalidDataException("Source probe has one authored level.");
            var cube = ModelCubeTexture.Read(bytes);
            for (var face = 0; face < 6; face++) _ = cube.FacePixels(face);
            return new(cube.Width, cube.Width, cube.Format, cube.Faces.SelectMany(f => f).ToArray());
        }
        var blockBytes = format switch
        {
            0x0b01 or 0x0b06 or 0x0c01 or 0x0c06 => 4,
            0x1a01 or 0x1a06 or 0x1d01 => 8,
            0x1b01 or 0x1b06 or 0x1c01 or 0x1c06 or 0x1e01 or 0x2001 or 0x2006 => 16,
            _ => throw new InvalidDataException("Texture compression is not supported by this preview.")
        };
        var width = checked((int)data.U32(info + 36)); var height = checked((int)data.U32(info + 40));
        if (width is < 1 or > 4096 || height is < 1 or > 4096
            || data.U32(info + 44) != 1 || data.U32(info + 48) != 1)
            throw new InvalidDataException("Texture dimensions are unsupported.");
        width = Math.Max(1, width >> level); height = Math.Max(1, height >> level);
        var blockSize = format is 0x0b01 or 0x0b06 or 0x0c01 or 0x0c06 ? 1 : 4;
        var blocksWide = (width + blockSize - 1) / blockSize; var blocksHigh = (height + blockSize - 1) / blockSize;
        var stride = checked(blocksWide * blockBytes);
        var result = new byte[checked(stride * blocksHigh)];
        var pointers = Pointer(info + 112);
        var count = data.U16(info + 22);
        if (count is < 1 or > 32 || level >= count) throw new InvalidDataException("Texture mip count is invalid.");
        data.Slice(pointers, count * 8);
        var first = Pointer(pointers);
        var imageEnd = checked(first + (int)data.U32(info + 80));
        data.Slice(first, imageEnd - first);
        if (first < Math.Max(info + 120, pointers + count * 8)) throw new InvalidDataException("Texture image overlaps metadata.");
        var source = Pointer(pointers + level * 8);
        var end = level + 1 < count ? Pointer(pointers + (level + 1) * 8) : imageEnd;
        var imageSize = checked(end - source);
        if (imageSize <= 0 || source < first || end > imageEnd) throw new InvalidDataException("Texture mip ranges are invalid.");
        data.Slice(source, imageSize);
        var tile = data.U16(info + 18);
        if (tile == 1)
        {
            var pitch = (stride + 31) / 32 * 32;
            if (imageSize < checked(pitch * (blocksHigh - 1) + stride))
                throw new InvalidDataException("Linear texture pitch is unsupported.");
            for (var row = 0; row < blocksHigh; row++)
                data.Slice(source + row * pitch, stride).CopyTo(result.AsSpan(row * stride, stride));
        }
        else if (tile == 0)
        {
            var groupHeight = 1 << checked((int)(data.U32(info + 52) & 7));
            while (groupHeight > 1 && blocksHigh <= groupHeight * 4) groupHeight /= 2;
            var groupsWide = (stride + 63) / 64;
            for (var y = 0; y < blocksHigh; y++)
            for (var x = 0; x < stride; x++)
            {
                var offset = y / (8 * groupHeight) * groupsWide * 512 * groupHeight
                    + x / 64 * 512 * groupHeight + y % (8 * groupHeight) / 8 * 512
                    + x % 64 / 32 * 256 + y % 8 / 2 * 64 + x % 32 / 16 * 32 + y % 2 * 16 + x % 16;
                if (offset >= imageSize) throw new InvalidDataException("Texture block exceeds its image.");
                result[y * stride + x] = data.U8(checked(source + offset));
            }
        }
        else throw new InvalidDataException("Texture tiling is unsupported.");
        if (format is 0x0c01 or 0x0c06)
        {
            for (var i = 0; i < result.Length; i += 4) (result[i], result[i + 2]) = (result[i + 2], result[i]);
            format -= 0x100;
        }
        var channels = data.U32(info + 88);
        if (channels != 0x05040302)
        {
            if ((long)width * height > 4 * 1024 * 1024
                || Enumerable.Range(0, 4).Any(c => (channels >> (c * 8) & 255) > 5))
                throw new InvalidDataException("Texture channel mapping is unsupported.");
            if (preserveCompressedChannels) return new PreviewTexture(width, height, format, result);
            // Compressed storage channels and sampled color channels can differ.
            // Resolve the declared mapping before baking or uploading the preview.
            var compression = format switch
            {
                0x0b01 or 0x0b06 => CompressionFormat.Rgba,
                0x1a01 or 0x1a06 => CompressionFormat.Bc1WithAlpha,
                0x1b01 or 0x1b06 => CompressionFormat.Bc2,
                0x1c01 or 0x1c06 => CompressionFormat.Bc3,
                0x1d01 => CompressionFormat.Bc4,
                0x1e01 => CompressionFormat.Bc5,
                0x2001 or 0x2006 => CompressionFormat.Bc7,
                _ => throw new InvalidDataException("Mapped texture compression is unsupported.")
            };
            var pixels = compression == CompressionFormat.Rgba
                ? Enumerable.Range(0, width * height).Select(i => new ColorRgba32(result[i * 4], result[i * 4 + 1], result[i * 4 + 2], result[i * 4 + 3])).ToArray()
                : new BcDecoder().DecodeRaw(result, width, height, compression);
            result = new byte[checked(width * height * 4)];
            for (var i = 0; i < pixels.Length; i++) for (var c = 0; c < 4; c++)
                result[i * 4 + c] = (channels >> (c * 8) & 255) switch
                {
                    0 => 0, 1 => 255, 2 => pixels[i].r, 3 => pixels[i].g, 4 => pixels[i].b, 5 => pixels[i].a,
                    _ => throw new InvalidDataException("Texture channel selector is unsupported.")
                };
            format = 0x0b00 | (format & 255);
        }
        return new PreviewTexture(width, height, format, result);
    }
}
