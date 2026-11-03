// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Numerics;

namespace KM.Formats.Models;

/// <summary>Lossless byte planes for the authored single-level probe cube layout.</summary>
public sealed record ModelCubeTexture(int Width, uint Format, byte[][] Faces)
{
    public long ByteLength => Faces.Sum(face => face.LongLength);

    // Keep native pixels and face order. Orienting a cube sample and binding
    // a source probe remain the renderer's work.
    public static ModelCubeTexture Read(byte[] bytes)
    {
        if (bytes.Length > 2 * 1024 * 1024)
            throw new InvalidDataException("Source probe exceeds the texture resource limit.");
        var data = new ModelBuffer(bytes);
        int Pointer(int at) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(at, 8)));
        if (!data.Slice(0, 4).SequenceEqual("BNTX"u8) || data.U16(12) != 0xfeff || data.U32(36) != 1)
            throw new InvalidDataException("Source probe container is unsupported.");
        var info = Pointer(Pointer(40));
        if (!data.Slice(info, 4).SequenceEqual("BRTI"u8) || data.U32(info + 28) != 0x1505
            || data.U16(info + 18) != 0 || data.U16(info + 22) != 1
            || data.U32(info + 44) != 1 || data.U32(info + 48) != 6
            || data.U32(info + 88) != 0x05040302 || data.U32(info + 92) != 3)
            throw new InvalidDataException("Source probe needs another cube layout adapter.");
        var width = checked((int)data.U32(info + 36));
        var height = checked((int)data.U32(info + 40));
        if (width is < 1 or > 512 || width != height)
            throw new InvalidDataException("Source probe dimensions are unsupported.");
        var stride = checked(width * 8);
        var groupHeight = 1 << checked((int)(data.U32(info + 52) & 7));
        while (groupHeight > 1 && height <= groupHeight * 4) groupHeight /= 2;
        var groupsWide = (stride + 63) / 64;
        var faceStride = checked(groupsWide * 512 * groupHeight * ((height + 8 * groupHeight - 1) / (8 * groupHeight)));
        var imageBytes = checked((int)data.U32(info + 80));
        if (imageBytes != checked(faceStride * 6))
            throw new InvalidDataException("Source probe face allocation is inconsistent.");
        var levels = Pointer(info + 112);
        var start = Pointer(levels);
        if (start < Math.Max(info + 120, levels + 8))
            throw new InvalidDataException("Source probe data overlaps metadata.");
        data.Slice(start, imageBytes);
        var faces = new byte[6][];
        for (var face = 0; face < faces.Length; face++)
        {
            var pixels = new byte[checked(stride * height)];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < stride; x++)
                {
                    var offset = y / (8 * groupHeight) * groupsWide * 512 * groupHeight
                        + x / 64 * 512 * groupHeight + y % (8 * groupHeight) / 8 * 512
                        + x % 64 / 32 * 256 + y % 8 / 2 * 64 + x % 32 / 16 * 32 + y % 2 * 16 + x % 16;
                    if (offset >= faceStride)
                        throw new InvalidDataException("Source probe pixel exceeds its face allocation.");
                    pixels[y * stride + x] = data.U8(checked(start + face * faceStride + offset));
                }
            faces[face] = pixels;
        }
        return new(width, data.U32(info + 28), faces);
    }

    /// <summary>Decodes one native RGBA16F face without clamping its linear HDR values.</summary>
    public Vector4[] FacePixels(int face)
    {
        // Format 0x1505 stores four half precision channels per pixel.
        if (Format != 0x1505 || Width is < 1 or > 512 || Faces is not { Length: 6 }
            || Faces.Any(p => p is null || p.Length != checked(Width * Width * 8))
            || (uint)face >= 6)
            throw new InvalidDataException("Source probe requires six complete RGBA16F faces.");
        var source = Faces[face];
        var pixels = new Vector4[checked(Width * Width)];
        for (var i = 0; i < pixels.Length; i++)
        {
            var value = Vector4.Zero;
            for (var channel = 0; channel < 4; channel++)
            {
                var word = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(i * 8 + channel * 2, 2));
                var component = (float)BitConverter.UInt16BitsToHalf(word);
                if (!float.IsFinite(component))
                    throw new InvalidDataException("Source probe contains a nonfinite channel.");
                value[channel] = component;
            }
            pixels[i] = value;
        }
        return pixels;
    }
}
