// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using System.Text;

namespace KM.SwSh.CustomGifts;

internal static class SwShRoyalGiftIcon
{
    internal const string TextureMember = "timg/__Combined.bntx";
    internal const string MenuTexture = "xmenu_button_icon_gift_00^s";
    internal const string TitleTexture = "scene_title_mystery^s";
    internal sealed record Region(byte[] Bytes, IReadOnlyList<int> Offsets);
    internal static byte[] Artwork(int size)
    {
        if (size is not (1420 or 220)) throw new InvalidDataException();
        var name = size == 1420 ? "royal-gift-title.bc4" : "royal-gift-icon-220.bc4";
        using var stream = typeof(SwShRoyalGiftIcon).Assembly.GetManifestResourceStream($"KM.SwSh.Resources.{name}")
            ?? throw new InvalidDataException("Royal Gift artwork is unavailable.");
        using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray();
    }

    internal static Region Read(byte[] bytes, string name, int size)
    {
        ReadOnlySpan<byte> Slice(int at, int count)
        { if (at < 0 || count < 0 || at > bytes.Length - count) throw new InvalidDataException("Gift icon texture is truncated."); return bytes.AsSpan(at, count); }
        int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(Slice(at, 2));
        int U32(int at) => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(Slice(at, 4)));
        int Pointer(int at) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(Slice(at, 8)));
        if (bytes.Length > 8 * 1024 * 1024 || !Slice(0, 4).SequenceEqual("BNTX"u8) || U16(12) != 0xFEFF) throw new InvalidDataException();
        var count = U32(36); var table = Pointer(40);
        if (count is < 1 or > 256) throw new InvalidDataException();
        int? found = null; var images = new List<(int Info, int Start, int End)>(); var metadataEnd = checked(table + count * 8);
        for (var i = 0; i < count; i++)
        {
            var info = Pointer(checked(table + i * 8));
            if (!Slice(info, 4).SequenceEqual("BRTI"u8)) throw new InvalidDataException();
            var nameAt = Pointer(info + 96);
            var levels = U16(info + 22); var pointers = Pointer(info + 112);
            if (levels is < 1 or > 32) throw new InvalidDataException();
            _ = Slice(pointers, levels * 8);
            var first = Pointer(pointers); var imageLength = U32(info + 80); _ = Slice(first, imageLength);
            metadataEnd = Math.Max(metadataEnd, Math.Max(info + 120, Math.Max(pointers + levels * 8, checked(nameAt + 2 + U16(nameAt)))));
            images.Add((info, first, checked(first + imageLength)));
            if (Encoding.UTF8.GetString(Slice(nameAt + 2, U16(nameAt))) != name) continue;
            if (found is not null) throw new InvalidDataException(); found = info;
        }
        if (found is not { } texture || U16(texture + 22) != 1 || U32(texture + 28) != 0x1D01
            || U32(texture + 88) != 0x02010101 || U32(texture + 44) != 1 || U32(texture + 48) != 1)
            throw new InvalidDataException("Unsupported gift icon texture.");
        var width = U32(texture + 36); var height = U32(texture + 40);
        if (width != size || height != (size == 1420 ? 128 : 220) || size is not (1420 or 220)) throw new InvalidDataException();
        var start = Pointer(Pointer(texture + 112)); var length = U32(texture + 80);
        _ = Slice(start, length);
        if (start < metadataEnd || images.Any(image => image.Info != texture && image.Start < start + length && start < image.End))
            throw new InvalidDataException("Gift icon storage overlaps another texture or its metadata.");
        var stride = (width + 3) / 4 * 8; var rows = (height + 3) / 4; var columns = (size + 3) / 4 * 8;
        var tile = U16(texture + 18); var group = 1 << (U32(texture + 52) & 7);
        while (group > 1 && rows <= group * 4) group /= 2;
        var offsets = new List<int>();
        for (var y = 0; y < rows; y++) for (var x = 0; x < columns; x++)
        {
            var offset = tile switch
            {
                1 => y * ((stride + 31) / 32 * 32) + x,
                0 => y / (8 * group) * ((stride + 63) / 64) * 512 * group + x / 64 * 512 * group
                    + y % (8 * group) / 8 * 512 + x % 64 / 32 * 256 + y % 8 / 2 * 64 + x % 32 / 16 * 32 + y % 2 * 16 + x % 16,
                _ => throw new InvalidDataException(),
            };
            if (offset >= length) throw new InvalidDataException(); offsets.Add(checked(start + offset));
        }
        return new(offsets.Select(at => bytes[at]).ToArray(), offsets);
    }

    internal static byte[] Write(byte[] bytes, Region region, byte[] replacement)
    {
        if (replacement.Length != region.Offsets.Count) throw new InvalidDataException();
        var result = bytes.ToArray(); for (var i = 0; i < replacement.Length; i++) result[region.Offsets[i]] = replacement[i]; return result;
    }
}
