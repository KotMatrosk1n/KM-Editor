// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Formats.Models;

public sealed record PreviewTexture(int Width, int Height, uint Format, byte[] Blocks)
{
    public static PreviewTexture Read(byte[] bytes)
    {
        var data = new ModelBuffer(bytes);
        if (!data.Slice(0, 4).SequenceEqual("BNTX"u8) || data.U16(12) != 0xfeff || data.U32(36) != 1)
            throw new InvalidDataException("Texture container layout is unsupported.");
        int Pointer(int at) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(at, 8)));
        var info = Pointer(Pointer(40));
        if (!data.Slice(info, 4).SequenceEqual("BRTI"u8)) throw new InvalidDataException("Texture information is missing.");
        var format = data.U32(info + 28);
        var blockBytes = format switch { 0x1d01 => 8, 0x1e01 or 0x2001 or 0x2006 => 16, _ => throw new InvalidDataException("Texture compression is not supported by this preview.") };
        var width = checked((int)data.U32(info + 36)); var height = checked((int)data.U32(info + 40));
        if (width is < 1 or > 4096 || height is < 1 or > 4096
            || data.U32(info + 44) != 1 || data.U32(info + 48) != 1)
            throw new InvalidDataException("Texture dimensions are unsupported.");
        var blocksWide = (width + 3) / 4; var blocksHigh = (height + 3) / 4;
        var stride = checked(blocksWide * blockBytes);
        var result = new byte[checked(stride * blocksHigh)];
        var source = Pointer(Pointer(info + 112));
        var imageSize = checked((int)data.U32(info + 80));
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
