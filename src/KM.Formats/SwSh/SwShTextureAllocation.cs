// SPDX-License-Identifier: GPL-3.0-only
using System.Buffers.Binary;
using KM.Formats.Models;

namespace KM.Formats.SwSh;

/// <summary>Allocates a bounded compressed image chain while preserving native texture metadata.</summary>
public static class SwShTextureAllocation
{
    public static byte[] Create(byte[] template, int width, int height, bool srgb, bool compressed = true)
    {
        if (width is < 4 or > 2048 || height is < 4 or > 2048 || !PowerOfTwo(width) || !PowerOfTwo(height)
            || (long)width * height > 2 * 1024 * 1024)
            throw new InvalidDataException($"Converted texture dimensions exceed the supported allocation: {width}x{height}.");
        var data = new ModelBuffer(template);
        int Pointer(int at) => checked((int)BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(at, 8)));
        if (!data.Slice(0, 4).SequenceEqual("BNTX"u8) || data.U32(36) != 1)
            throw new InvalidDataException("Converted texture requires a single image container.");
        var info = Pointer(Pointer(40)); var block = Pointer(48); var oldPointers = Pointer(info + 112);
        var oldCount = data.U16(info + 22); var relocation = checked((int)data.U32(24));
        if (!data.Slice(info, 4).SequenceEqual("BRTI"u8) || !data.Slice(block, 4).SequenceEqual("BRTD"u8)
            || !data.Slice(relocation, 4).SequenceEqual("_RLT"u8) || data.U32(relocation + 8) != 2
            || data.U32(info + 44) != 1 || data.U32(info + 48) != 1 || oldCount is < 1 or > 16)
            throw new InvalidDataException("Converted texture metadata layout is unsupported.");
        var firstSection = relocation + 16; var imageSection = firstSection + 24;
        var metadataEnd = checked((int)(data.U32(firstSection + 8) + data.U32(firstSection + 12)));
        var entries = imageSection + 24;
        var entryCount = checked((int)(data.U32(imageSection + 16) + data.U32(imageSection + 20)));
        var oldPointersEnd = checked(oldPointers + oldCount * 8);
        if (data.U32(imageSection + 8) != block || metadataEnd > block || entryCount is < 1 or > 256
            || info + 120 > metadataEnd || oldPointers < info + 120 || (oldPointers & 7) != 0
            || oldPointersEnd > metadataEnd)
            throw new InvalidDataException("Converted texture relocation ranges are unsupported.");
        var count = 1 + (int)Math.Log2(Math.Max(width, height));
        var pointers = Align(metadataEnd, 8);
        if (pointers + count * 8 > block)
            throw new InvalidDataException("Converted texture metadata has no room for its mip references.");
        var firstImage = Align(block + 16, 512); var offsets = new int[count]; var end = firstImage;
        for (var mip = 0; mip < count; mip++)
        {
            var w = Math.Max(1, width >> mip); var h = Math.Max(1, height >> mip);
            var rows = compressed ? (h + 3) / 4 : h;
            var stride = compressed ? (w + 3) / 4 * 16 : w * 4; var group = 16;
            while (group > 1 && rows <= group * 4) group /= 2;
            offsets[mip] = end;
            end = checked(end + Align(stride, 64) / 64 * 512 * group * ((rows + 8 * group - 1) / (8 * group)));
        }
        var references = new[] { new List<int>(), new List<int>() }; var replaced = new HashSet<int>();
        for (var section = 0; section < 2; section++)
        {
            var header = firstSection + section * 24;
            var start = checked((int)data.U32(header + 16)); var stop = checked(start + (int)data.U32(header + 20));
            if (start < 0 || stop > entryCount) throw new InvalidDataException("Texture relocation entry range is invalid.");
            for (var i = start; i < stop; i++)
            {
                var entry = entries + i * 8; var position = checked((int)data.U32(entry));
                var structures = data.U16(entry + 4); var pointerCount = data.U8(entry + 6); var padding = data.U8(entry + 7);
                for (var n = 0; n < structures; n++) for (var p = 0; p < pointerCount; p++)
                {
                    var at = checked(position + (n * (pointerCount + padding) + p) * 8);
                    data.Slice(at, 8);
                    if (at < oldPointersEnd && at + 8 > oldPointers)
                    {
                        if (section != 1 || at < oldPointers || at + 8 > oldPointersEnd
                            || (at - oldPointers) % 8 != 0 || !replaced.Add(at))
                            throw new InvalidDataException("Texture mip relocations overlap or have the wrong target section.");
                        continue;
                    }
                    var target = Pointer(at);
                    if (at != info + 112 && target >= oldPointers && target < oldPointersEnd)
                        throw new InvalidDataException("Texture metadata retains a reference to its original mip table.");
                    references[section].Add(at);
                }
            }
        }
        if (replaced.Count != oldCount || references[0].Count(at => at == info + 112) != 1
            || references[1].Contains(info + 112))
            throw new InvalidDataException("Texture mip relocations are incomplete.");
        references[1].AddRange(Enumerable.Range(0, count).Select(i => pointers + i * 8));
        var newRelocation = Align(end, 4096);
        var relocationSize = checked(64 + references.Sum(r => r.Count) * 8);
        var output = new byte[checked(newRelocation + relocationSize)];
        template.AsSpan(0, block + 16).CopyTo(output);
        // Every reference to the original mip table is replaced below. Its
        // retired bytes must not carry template-dependent image offsets into
        // otherwise identical converted containers.
        output.AsSpan(oldPointers, oldCount * 8).Clear();
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(at), value);
        void U64(int at, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(at), value);
        void U16(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(at), value);
        U32(24, (uint)newRelocation); U32(28, (uint)output.Length);
        U16(info + 18, 0); U16(info + 22, (ushort)count);
        U32(info + 28, (compressed ? 0x2000u : 0x0b00u) | (srgb ? 6u : 1u));
        U32(info + 36, (uint)width); U32(info + 40, (uint)height); U32(info + 52, 4);
        U32(info + 80, (uint)(end - firstImage)); U32(info + 84, 512); U32(info + 88, 0x05040302);
        U64(info + 112, (ulong)pointers);
        for (var i = 0; i < count; i++) U64(pointers + i * 8, (ulong)offsets[i]);
        U32(block + 4, 0); U64(block + 8, (ulong)(newRelocation - block));
        "_RLT"u8.CopyTo(output.AsSpan(newRelocation));
        U32(newRelocation + 4, (uint)newRelocation); U32(newRelocation + 8, 2);
        var entryIndex = 0;
        for (var section = 0; section < 2; section++)
        {
            var header = newRelocation + 16 + section * 24;
            U32(header + 8, section == 0 ? 0 : (uint)block);
            U32(header + 12, (uint)(section == 0 ? pointers + count * 8 : newRelocation - block));
            U32(header + 16, (uint)entryIndex); U32(header + 20, (uint)references[section].Count);
            foreach (var reference in references[section])
            {
                var at = newRelocation + 64 + entryIndex++ * 8;
                U32(at, (uint)reference); U16(at + 4, 1); output[at + 6] = 1;
            }
        }
        _ = new ModelTextureDocument(output);
        return output;
    }

    private static bool PowerOfTwo(int value) => (value & (value - 1)) == 0;
    private static int Align(int value, int alignment) => checked((value + alignment - 1) / alignment * alignment);
}
