// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;

namespace KM.Formats.Models;

/// <summary>Lossless compressed faces and mips for the supported signed BC6 cube layout.</summary>
internal static class PreviewBc6Cube
{
    internal const uint Format = 0x1f05;
    private const int Width = 128;
    private const int Faces = 6;
    private const int Levels = 8;
    private const int ImageOffset = 4096;
    private const int FaceStride = 24576;
    private static readonly int[] MipOffsets = [0, 16384, 20480, 21504, 22016, 22528, 23040, 23552];

    internal static PreviewTexture Read(byte[] bytes, int? level = null)
    {
        if (bytes.Length != 151664 || level is not null && (uint)level.Value >= Levels)
            throw new InvalidDataException("Signed BC6 cube resource or level is unsupported.");
        var data = new ModelBuffer(bytes);
        int Pointer(int at)
        {
            var pointer = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(at, 8));
            if (pointer > int.MaxValue) throw new InvalidDataException("Signed BC6 cube pointer is unsupported.");
            return (int)pointer;
        }
        if (!data.Slice(0, 4).SequenceEqual("BNTX"u8) || data.U16(12) != 0xfeff
            || data.U32(28) != bytes.Length || data.U32(36) != 1)
            throw new InvalidDataException("Signed BC6 cube container is unsupported.");
        var table = Pointer(40);
        if (table < 64 || table > ImageOffset - 8)
            throw new InvalidDataException("Signed BC6 cube texture table overlaps image data.");
        var info = Pointer(table);
        if (info < 64 || info > ImageOffset - 120 || table < info + 120 && table + 8 > info)
            throw new InvalidDataException("Signed BC6 cube information overlaps image data.");
        if (!data.Slice(info, 4).SequenceEqual("BRTI"u8) || data.U32(info + 28) != Format
            || data.U16(info + 18) != 0 || data.U16(info + 22) != Levels
            || data.U32(info + 36) != Width || data.U32(info + 40) != Width
            || data.U32(info + 44) != 1 || data.U32(info + 48) != Faces
            || data.U32(info + 52) != 2 || data.U32(info + 80) != FaceStride * Faces
            || data.U32(info + 84) != 512 || data.U32(info + 88) != 0x01040302
            || data.U32(info + 92) != 3)
            throw new InvalidDataException("Signed BC6 cube needs another layout adapter.");
        var pointers = Pointer(info + 112);
        if (pointers < info + 120 || pointers > ImageOffset - Levels * 8
            || table >= pointers && table < pointers + Levels * 8)
            throw new InvalidDataException("Signed BC6 cube mip table overlaps metadata or image data.");
        data.Slice(ImageOffset, FaceStride * Faces);
        for (var mip = 0; mip < Levels; mip++)
            if (Pointer(pointers + mip * 8) != ImageOffset + MipOffsets[mip])
                throw new InvalidDataException("Signed BC6 cube mip allocation is inconsistent.");

        var firstLevel = level ?? 0;
        var count = level.HasValue ? 1 : Levels;
        var payloads = new byte[count][];
        for (var mip = firstLevel; mip < firstLevel + count; mip++)
        {
            var blocksWide = (Math.Max(1, Width >> mip) + 3) / 4;
            var stride = blocksWide * 16;
            var faceBytes = stride * blocksWide;
            var payload = new byte[faceBytes * Faces];
            var groupHeight = 4;
            while (groupHeight > 1 && blocksWide <= groupHeight * 4) groupHeight /= 2;
            var groupsWide = (stride + 63) / 64;
            var allocation = (mip + 1 < Levels ? MipOffsets[mip + 1] : FaceStride) - MipOffsets[mip];
            for (var face = 0; face < Faces; face++)
                for (var y = 0; y < blocksWide; y++)
                    for (var x = 0; x < stride; x++)
                    {
                        var offset = y / (8 * groupHeight) * groupsWide * 512 * groupHeight
                            + x / 64 * 512 * groupHeight + y % (8 * groupHeight) / 8 * 512
                            + x % 64 / 32 * 256 + y % 8 / 2 * 64 + x % 32 / 16 * 32 + y % 2 * 16 + x % 16;
                        if (offset >= allocation)
                            throw new InvalidDataException("Signed BC6 block exceeds its face mip allocation.");
                        payload[face * faceBytes + y * stride + x] = data.U8(ImageOffset + face * FaceStride + MipOffsets[mip] + offset);
                    }
            payloads[mip - firstLevel] = payload;
        }
        // BC6 samples linear signed RGB and an implicit alpha of one, matching
        // the declared RGB1 mapping. Keep every compressed block and face order.
        return new(Math.Max(1, Width >> firstLevel), Math.Max(1, Width >> firstLevel), Format, payloads[0])
        { Mips = payloads.Skip(1).ToArray() };
    }

    internal static void Validate(PreviewTexture texture)
    {
        if (texture.Format != Format || texture.Width != Width || texture.Height != Width
            || texture.Blocks is null || texture.Mips is not { Length: Levels - 1 })
            throw new InvalidDataException("Preview BC6 cube requires six faces and eight authored levels.");
        for (var level = 0; level < Levels; level++)
        {
            var blocks = (Math.Max(1, Width >> level) + 3) / 4;
            var bytes = level == 0 ? texture.Blocks : texture.Mips[level - 1];
            if (bytes is null || bytes.Length != blocks * blocks * 16 * Faces)
                throw new InvalidDataException("Preview BC6 cube face mip size is inconsistent.");
        }
    }
}
