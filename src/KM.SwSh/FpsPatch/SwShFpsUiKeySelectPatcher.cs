// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.SwSh.FpsPatch;

internal sealed record SwShFpsUiKeySelectAnimationInfo(
    string Name,
    ushort StartFrame,
    ushort EndFrame,
    IReadOnlyList<float> FrameKeys);

internal static class SwShFpsUiKeySelectPatcher
{
    private const double TimelineScale = 2.0d;

    public static bool ContainsKeySelectAnimation(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);

        try
        {
            return EnumerateSarcEntries(source).Any(entry => IsKeySelectAnimation(entry.Name));
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or ArgumentOutOfRangeException or OverflowException)
        {
            return false;
        }
    }

    public static byte[] ConvertArchive(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);

        try
        {
            var output = source.ToArray();
            foreach (var entry in EnumerateSarcEntries(output))
            {
                if (!IsKeySelectAnimation(entry.Name))
                {
                    continue;
                }

                ScaleBflanTimeline(output, entry.StartOffset, entry.EndOffset);
            }

            return output;
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("Invalid battle UI SARC archive.", exception);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("Invalid battle UI animation range.", exception);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Invalid battle UI animation range.", exception);
        }
    }

    internal static IReadOnlyList<SwShFpsUiKeySelectAnimationInfo> InspectArchive(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var animations = new List<SwShFpsUiKeySelectAnimationInfo>();
        foreach (var entry in EnumerateSarcEntries(source))
        {
            if (!IsKeySelectAnimation(entry.Name))
            {
                continue;
            }

            animations.Add(InspectBflan(source, entry.Name, entry.StartOffset, entry.EndOffset));
        }

        return animations;
    }

    private static bool IsKeySelectAnimation(string name)
    {
        return name.StartsWith("anim/", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith("_key_select.bflan", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<SarcEntry> EnumerateSarcEntries(byte[] data)
    {
        EnsureRange(data, 0, 0x14, "SARC header");
        if (!HasMagic(data, 0, "SARC"))
        {
            throw new InvalidDataException("Battle UI archive is not a SARC file.");
        }

        var headerSize = ReadU16(data, 0x04);
        var bom = ReadU16(data, 0x06);
        if (bom != 0xFEFF)
        {
            throw new InvalidDataException("Battle UI SARC archive must be little-endian.");
        }

        var dataOffset = checked((int)ReadU32(data, 0x0C));
        EnsureRange(data, dataOffset, 0, "SARC data offset");
        var sfatOffset = headerSize;
        EnsureRange(data, sfatOffset, 0x0C, "SFAT header");
        if (!HasMagic(data, sfatOffset, "SFAT"))
        {
            throw new InvalidDataException("Battle UI SARC archive is missing SFAT.");
        }

        var sfatHeaderSize = ReadU16(data, sfatOffset + 0x04);
        var nodeCount = ReadU16(data, sfatOffset + 0x06);
        var sfntOffset = checked(sfatOffset + sfatHeaderSize + nodeCount * 0x10);
        EnsureRange(data, sfntOffset, 0x08, "SFNT header");
        if (!HasMagic(data, sfntOffset, "SFNT"))
        {
            throw new InvalidDataException("Battle UI SARC archive is missing SFNT.");
        }

        var sfntHeaderSize = ReadU16(data, sfntOffset + 0x04);
        var nameTableOffset = checked(sfntOffset + sfntHeaderSize);
        var entries = new List<SarcEntry>(nodeCount);
        for (var index = 0; index < nodeCount; index++)
        {
            var nodeOffset = checked(sfatOffset + sfatHeaderSize + index * 0x10);
            EnsureRange(data, nodeOffset, 0x10, "SFAT node");
            var attributes = ReadU32(data, nodeOffset + 0x04);
            var nameOffset = checked(nameTableOffset + (int)(attributes & 0x00FFFFFF) * 4);
            var startOffset = checked(dataOffset + (int)ReadU32(data, nodeOffset + 0x08));
            var endOffset = checked(dataOffset + (int)ReadU32(data, nodeOffset + 0x0C));
            if (endOffset < startOffset)
            {
                throw new InvalidDataException("Battle UI SARC node has an invalid range.");
            }

            EnsureRange(data, startOffset, endOffset - startOffset, "SARC file data");
            entries.Add(new SarcEntry(ReadNullTerminatedString(data, nameOffset), startOffset, endOffset));
        }

        return entries;
    }

    private static void ScaleBflanTimeline(byte[] data, int startOffset, int endOffset)
    {
        EnsureRange(data, startOffset, 0x14, "BFLAN header");
        if (!HasMagic(data, startOffset, "FLAN"))
        {
            throw new InvalidDataException("Battle UI key-select member is not a BFLAN animation.");
        }

        var headerSize = ReadU16(data, startOffset + 0x06);
        var sectionCount = checked((int)ReadU32(data, startOffset + 0x10));
        var sectionOffset = checked(startOffset + headerSize);
        for (var sectionIndex = 0; sectionIndex < sectionCount; sectionIndex++)
        {
            EnsureRange(data, sectionOffset, 0x08, "BFLAN section header");
            var sectionSize = checked((int)ReadU32(data, sectionOffset + 0x04));
            if (sectionSize < 0x08 || sectionOffset + sectionSize > endOffset)
            {
                throw new InvalidDataException("Battle UI BFLAN section has an invalid range.");
            }

            if (HasMagic(data, sectionOffset, "pat1"))
            {
                ScalePat1FrameRange(data, sectionOffset, sectionSize);
            }
            else if (HasMagic(data, sectionOffset, "pai1"))
            {
                ScalePai1FrameKeys(data, sectionOffset, sectionSize);
            }

            sectionOffset = checked(sectionOffset + sectionSize);
        }
    }

    private static SwShFpsUiKeySelectAnimationInfo InspectBflan(byte[] data, string name, int startOffset, int endOffset)
    {
        EnsureRange(data, startOffset, 0x14, "BFLAN header");
        if (!HasMagic(data, startOffset, "FLAN"))
        {
            throw new InvalidDataException("Battle UI key-select member is not a BFLAN animation.");
        }

        var headerSize = ReadU16(data, startOffset + 0x06);
        var sectionCount = checked((int)ReadU32(data, startOffset + 0x10));
        var sectionOffset = checked(startOffset + headerSize);
        ushort startFrame = 0;
        ushort endFrame = 0;
        var frameKeys = new List<float>();
        for (var sectionIndex = 0; sectionIndex < sectionCount; sectionIndex++)
        {
            EnsureRange(data, sectionOffset, 0x08, "BFLAN section header");
            var sectionSize = checked((int)ReadU32(data, sectionOffset + 0x04));
            if (sectionSize < 0x08 || sectionOffset + sectionSize > endOffset)
            {
                throw new InvalidDataException("Battle UI BFLAN section has an invalid range.");
            }

            if (HasMagic(data, sectionOffset, "pat1"))
            {
                EnsureRange(data, sectionOffset, 0x1C, "pat1 frame range");
                startFrame = ReadU16(data, sectionOffset + 0x18);
                endFrame = ReadU16(data, sectionOffset + 0x1A);
            }
            else if (HasMagic(data, sectionOffset, "pai1"))
            {
                frameKeys.AddRange(ReadPai1FrameKeys(data, sectionOffset, sectionSize));
            }

            sectionOffset = checked(sectionOffset + sectionSize);
        }

        return new SwShFpsUiKeySelectAnimationInfo(name, startFrame, endFrame, frameKeys);
    }

    private static void ScalePat1FrameRange(byte[] data, int sectionOffset, int sectionSize)
    {
        if (sectionSize < 0x1C)
        {
            throw new InvalidDataException("Battle UI pat1 section is too small.");
        }

        WriteU16(data, sectionOffset + 0x18, ScaleU16(ReadU16(data, sectionOffset + 0x18)));
        WriteU16(data, sectionOffset + 0x1A, ScaleU16(ReadU16(data, sectionOffset + 0x1A)));
    }

    private static void ScalePai1FrameKeys(byte[] data, int sectionOffset, int sectionSize)
    {
        foreach (var frameOffset in EnumeratePai1FrameKeyOffsets(data, sectionOffset, sectionSize))
        {
            var frame = ReadF32(data, frameOffset);
            if (float.IsFinite(frame) && frame >= 0.0f)
            {
                WriteF32(data, frameOffset, checked((float)(frame * TimelineScale)));
            }
        }
    }

    private static IReadOnlyList<float> ReadPai1FrameKeys(byte[] data, int sectionOffset, int sectionSize)
    {
        return EnumeratePai1FrameKeyOffsets(data, sectionOffset, sectionSize)
            .Select(offset => ReadF32(data, offset))
            .ToArray();
    }

    private static IEnumerable<int> EnumeratePai1FrameKeyOffsets(byte[] data, int sectionOffset, int sectionSize)
    {
        var sectionEnd = checked(sectionOffset + sectionSize);
        for (var offset = sectionOffset + 0x08; offset <= sectionEnd - 0x10; offset++)
        {
            if (!HasMagic(data, offset, "FLPA")
                && !HasMagic(data, offset, "FLVC")
                && !HasMagic(data, offset, "FLVI"))
            {
                continue;
            }

            var targetCount = checked((int)ReadU32(data, offset + 0x04));
            if (targetCount < 0 || targetCount > 0x100)
            {
                throw new InvalidDataException("Battle UI BFLAN key-select track count is invalid.");
            }

            EnsureRange(data, offset + 0x08, targetCount * sizeof(uint), "BFLAN track offset table");
            for (var targetIndex = 0; targetIndex < targetCount; targetIndex++)
            {
                var targetOffset = checked(offset + (int)ReadU32(data, offset + 0x08 + targetIndex * sizeof(uint)));
                EnsureRange(data, targetOffset, 0x0C, "BFLAN animation target");
                if (targetOffset < offset || targetOffset >= sectionEnd)
                {
                    throw new InvalidDataException("Battle UI BFLAN target offset is outside its section.");
                }

                var keyCount = checked((int)ReadU32(data, targetOffset + 0x04));
                var keyOffset = checked(targetOffset + (int)ReadU32(data, targetOffset + 0x08));
                if (keyCount < 0 || keyCount > 0x400)
                {
                    throw new InvalidDataException("Battle UI BFLAN key count is invalid.");
                }

                EnsureRange(data, keyOffset, keyCount * 0x0C, "BFLAN keyframes");
                if (keyOffset < targetOffset || keyOffset + keyCount * 0x0C > sectionEnd)
                {
                    throw new InvalidDataException("Battle UI BFLAN keyframes are outside their section.");
                }

                for (var keyIndex = 0; keyIndex < keyCount; keyIndex++)
                {
                    yield return keyOffset + keyIndex * 0x0C;
                }
            }
        }
    }

    private static ushort ScaleU16(ushort value)
    {
        return checked((ushort)Math.Round(value * TimelineScale, MidpointRounding.AwayFromZero));
    }

    private static bool HasMagic(byte[] data, int offset, string magic)
    {
        if (offset < 0 || offset + magic.Length > data.Length)
        {
            return false;
        }

        for (var index = 0; index < magic.Length; index++)
        {
            if (data[offset + index] != (byte)magic[index])
            {
                return false;
            }
        }

        return true;
    }

    private static string ReadNullTerminatedString(byte[] data, int offset)
    {
        EnsureRange(data, offset, 1, "SARC file name");
        var endOffset = offset;
        while (endOffset < data.Length && data[endOffset] != 0)
        {
            endOffset++;
        }

        if (endOffset >= data.Length)
        {
            throw new InvalidDataException("Battle UI SARC file name is not null-terminated.");
        }

        return System.Text.Encoding.UTF8.GetString(data.AsSpan(offset, endOffset - offset));
    }

    private static void EnsureRange(byte[] data, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset > data.Length || length > data.Length - offset)
        {
            throw new InvalidDataException($"{label} range is outside the file.");
        }
    }

    private static ushort ReadU16(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)));
    }

    private static uint ReadU32(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)));
    }

    private static float ReadF32(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset, sizeof(float)));
    }

    private static void WriteU16(byte[] data, int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)), value);
    }

    private static void WriteF32(byte[] data, int offset, float value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offset, sizeof(float)), value);
    }

    private sealed record SarcEntry(
        string Name,
        int StartOffset,
        int EndOffset);
}
