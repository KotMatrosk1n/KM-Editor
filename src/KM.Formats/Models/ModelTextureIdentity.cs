// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace KM.Formats.Models;

/// <summary>Updates a single texture's native dictionary identity without relocating its image data.</summary>
public static class ModelTextureIdentity
{
    public static string MaterialName(string original, int materialIndex, char role, char? palette = null, bool lowerCase = false)
    {
        if (materialIndex is < 0 or > 127 || role is < 'a' or > 'z' || palette is not (null or 'n' or 's'))
            throw new InvalidDataException("Material texture identity is outside the supported range.");
        // Keep existing one-digit identities. A non-hex marker separates larger
        // indices from that namespace while retaining the BNTX string allocation.
        var identity = materialIndex < 16
            ? materialIndex.ToString(lowerCase ? "x1" : "X1", System.Globalization.CultureInfo.InvariantCulture)
            : "g" + materialIndex.ToString(lowerCase ? "x2" : "X2", System.Globalization.CultureInfo.InvariantCulture);
        var prefix = role + identity + (palette?.ToString() ?? "");
        if (original.Length <= prefix.Length || original.Any(c => c is < '!' or > '~' || c is '/' or '\\'))
            throw new InvalidDataException("Material texture name cannot retain its native allocation.");
        return prefix + original[prefix.Length..];
    }

    public static byte[] Rename(byte[] source, string oldName, string newName, bool linear = false)
    {
        var data = new ModelBuffer(source);
        int Pointer(int at) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(at, 8)));
        if (!data.Slice(0, 4).SequenceEqual("BNTX"u8) || data.U32(36) != 1)
            throw new InvalidDataException("Texture identity requires a single texture container.");
        var oldBytes = Encoding.UTF8.GetBytes(oldName); var newBytes = Encoding.UTF8.GetBytes(newName);
        if (oldBytes.Length == 0 || newBytes.Length != oldBytes.Length || oldName.Contains('\0') || newName.Contains('\0'))
            throw new InvalidDataException("Texture identity must retain the encoded name length.");
        var info = Pointer(Pointer(40)); var name = Pointer(info + 96);
        if (data.U16(name) != oldBytes.Length || !data.Slice(name + 2, oldBytes.Length).SequenceEqual(oldBytes) || data.U8(name + 2 + oldBytes.Length) != 0)
            throw new InvalidDataException("Texture name does not match its declared identity.");
        var dictionary = Pointer(56);
        if (!data.Slice(dictionary, 4).SequenceEqual("_DIC"u8) || data.U32(dictionary + 4) != 1 || Pointer(dictionary + 32) != name)
            throw new InvalidDataException("Texture dictionary layout is unsupported for renaming.");
        var result = source.ToArray(); newBytes.CopyTo(result, name + 2);
        // A one-entry Patricia tree compares the least significant nonzero bit,
        // counted from the last byte of its key.
        var last = newBytes.Length - 1;
        while (last >= 0 && newBytes[last] == 0) last--;
        if (last < 0) throw new InvalidDataException("Texture identity is empty.");
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(dictionary + 24),
            checked((uint)((newBytes.Length - 1 - last) * 8 + BitOperations.TrailingZeroCount((uint)newBytes[last]))));
        if (linear)
        {
            var format = data.U32(info + 28);
            if ((format & 255) is not (1 or 6)) throw new InvalidDataException("Texture color encoding cannot be converted to linear.");
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(info + 28), (format & ~255u) | 1);
        }
        // Auxiliary masks and two-channel normals have no color editing surface,
        // but retain the same native identity layout as color textures.
        if (data.U32(info + 28) is 0x1d01 or 0x1e01)
            _ = PreviewTexture.ReadBaseLevel(result);
        else if (data.U32(info + 28) == 0x1505)
            _ = ModelCubeTexture.Read(result);
        else _ = new ModelTextureDocument(result);
        return result;
    }
}
