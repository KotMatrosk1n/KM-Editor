// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Formats.Models;

public sealed record PreviewTexture(int Width, int Height, uint Format, byte[] Blocks)
{
    public string? SourcePath { get; init; }
    public byte[][] Mips { get; init; } = [];
    public long ByteLength => Blocks.LongLength + Mips.Sum(m => m.LongLength);
    public static PreviewTexture Read(byte[] bytes) => ModelDerivedCache<PreviewTexture>.Get(bytes, "",
        () => PreviewTextureResolution.Select(ReadUncached(bytes), 1), texture => texture.ByteLength);
    private static PreviewTexture ReadUncached(byte[] bytes)
    {
        var texture = ReadLevel(bytes, 0);
        var data = new ModelBuffer(bytes);
        var info = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(checked((int)BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(40, 8))), 8)));
        var count = data.U16(info + 22);
        if (count < 1 || count > 1 + (int)Math.Log2(Math.Max(texture.Width, texture.Height)))
            throw new InvalidDataException("Texture mip count is invalid.");
        return texture with { Mips = Enumerable.Range(1, count - 1).Select(i => ReadLevel(bytes, i).Blocks).ToArray() };
    }
    private static PreviewTexture ReadLevel(byte[] bytes, int level)
    {
        var data = new ModelBuffer(bytes);
        if (!data.Slice(0, 4).SequenceEqual("BNTX"u8) || data.U16(12) != 0xfeff || data.U32(36) != 1)
            throw new InvalidDataException("Texture container layout is unsupported.");
        int Pointer(int at) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(at, 8)));
        var info = Pointer(Pointer(40));
        if (!data.Slice(info, 4).SequenceEqual("BRTI"u8)) throw new InvalidDataException("Texture information is missing.");
        var format = data.U32(info + 28);
        var blockBytes = format switch
        {
            0x0b01 or 0x0b06 => 4,
            0x1a01 or 0x1a06 or 0x1d01 => 8,
            0x1b01 or 0x1b06 or 0x1c01 or 0x1c06 or 0x1e01 or 0x2001 or 0x2006 => 16,
            _ => throw new InvalidDataException("Texture compression is not supported by this preview.")
        };
        var width = checked((int)data.U32(info + 36)); var height = checked((int)data.U32(info + 40));
        if (width is < 1 or > 4096 || height is < 1 or > 4096
            || data.U32(info + 44) != 1 || data.U32(info + 48) != 1)
            throw new InvalidDataException("Texture dimensions are unsupported.");
        width = Math.Max(1, width >> level); height = Math.Max(1, height >> level);
        var blockSize = format is 0x0b01 or 0x0b06 ? 1 : 4;
        var blocksWide = (width + blockSize - 1) / blockSize; var blocksHigh = (height + blockSize - 1) / blockSize;
        var stride = checked(blocksWide * blockBytes);
        var result = new byte[checked(stride * blocksHigh)];
        var pointers = Pointer(info + 112);
        var source = Pointer(pointers + level * 8);
        var end = level + 1 < data.U16(info + 22) ? Pointer(pointers + (level + 1) * 8)
            : checked(Pointer(pointers) + (int)data.U32(info + 80));
        var imageSize = checked(end - source);
        if (imageSize <= 0 || source < Pointer(pointers)) throw new InvalidDataException("Texture mip ranges are invalid.");
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
        return new PreviewTexture(width, height, format, result);
    }
}
