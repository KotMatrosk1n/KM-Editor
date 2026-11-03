// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.Models;
using KM.Formats.SwSh;

namespace KM.Formats.ZA;

/// <summary>Qualifies source-only texture layouts before allocating editable baked images.</summary>
public static class ZaSwShTextureTemplate
{
    public static byte[] Convert(PreviewPrimitive primitive, byte[] bytes, string name)
    {
        if (primitive.Name.StartsWith("pm0485_51_00_", StringComparison.Ordinal)
            && primitive.Material.Name is "l_eye" or "r_eye" && name == "pm0485_51_00_white_msk")
        {
            var metadata = new ModelBuffer(bytes);
            int At(int offset) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(metadata.Slice(offset, 8)));
            var infoAt = At(At(40));
            if (metadata.U32(infoAt + 28) != 0x1d01 || metadata.U32(infoAt + 88) != 0x01020202
                || metadata.U32(infoAt + 36) != 16 || metadata.U32(infoAt + 40) != 16 || metadata.U16(infoAt + 22) != 5)
                throw new InvalidDataException("Mega Heatran scalar eye albedo requires its authored layout.");
            var sampled = PreviewTexture.Read(bytes);
            if (sampled.Format != 0x0b01 || sampled.Mips.Length != 4)
                throw new InvalidDataException("Mega Heatran scalar eye mip chain changed.");
            foreach (var mip in sampled.Mips.Prepend(sampled.Blocks))
                for (var pixel = 0; pixel < mip.Length; pixel += 4)
                    if (mip[pixel] != mip[pixel + 1] || mip[pixel] != mip[pixel + 2] || mip[pixel + 3] != 255)
                        throw new InvalidDataException("Mega Heatran eye albedo is not opaque sampled grayscale.");
            return SwShTextureAllocation.Create(bytes, 16, 16, srgb: true);
        }
        if (!primitive.Name.StartsWith("pm0748_51_00_", StringComparison.Ordinal)
            || primitive.Material.Name != "body_b" || name != "pm0748_51_00_body_b_alb") return bytes;
        var source = new ModelBuffer(bytes);
        int Pointer(int at) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(at, 8)));
        var info = Pointer(Pointer(40));
        if (bytes.Length > 32 * 1024 * 1024 || source.U32(info + 28) != 0x1d01
            || source.U32(info + 36) != 512 || source.U32(info + 40) != 1024
            || source.U32(info + 88) != 0x01020202 || source.U16(info + 22) != 11)
            throw new InvalidDataException("Mega Barbaracle grayscale template requires another adapter.");
        // The donor samples BC4 red as RGB with opaque alpha. Validate every
        // stored mip, then allocate an editable color template. The baker still
        // samples the original donor image; none of its pixels are replaced here.
        var decoded = PreviewTexture.Read(bytes);
        if (decoded.Format != 0x0b01 || decoded.Width != 512 || decoded.Height != 1024 || decoded.Mips.Length != 10)
            throw new InvalidDataException("Mega Barbaracle grayscale channels are unsupported.");
        return SwShTextureAllocation.Create(bytes, decoded.Width, decoded.Height, true);
    }
}
