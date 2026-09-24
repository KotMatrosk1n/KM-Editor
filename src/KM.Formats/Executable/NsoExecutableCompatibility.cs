// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;

namespace KM.Formats.Executable;

/// <summary>Checks executable identity while allowing bounded growth of the code section.</summary>
public static class NsoExecutableCompatibility
{
    public static void EnsureCompatibleBaseLayout(NsoFile baseline, NsoFile effective, string operation)
    {
        if (!BaseLayoutMatches(baseline.RawHeader, effective.RawHeader, out var mismatch))
        {
            throw new InvalidDataException($"{operation} requires a compatible base executable: {mismatch}");
        }
    }

    public static bool BaseLayoutMatches(byte[] baseHeader, byte[] effectiveHeader, out string mismatch)
    {
        mismatch = "executable identity or stable header metadata differs.";
        if (baseHeader.Length != NsoFile.HeaderSize || effectiveHeader.Length != NsoFile.HeaderSize)
        {
            return false;
        }

        var baseTextSize = BinaryPrimitives.ReadInt32LittleEndian(baseHeader.AsSpan(0x18));
        var effectiveTextSize = BinaryPrimitives.ReadInt32LittleEndian(effectiveHeader.AsSpan(0x18));
        var normalizedEffective = effectiveHeader.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(normalizedEffective.AsSpan(0x18), baseTextSize);
        if (!StableHeaderBytesMatch(baseHeader, normalizedEffective))
        {
            return false;
        }

        if (baseTextSize < 0 || effectiveTextSize < baseTextSize)
        {
            mismatch = "the effective code section is shorter than the base code section.";
            return false;
        }

        var textAddress = BinaryPrimitives.ReadInt32LittleEndian(effectiveHeader.AsSpan(0x14));
        var textEnd = (long)textAddress + effectiveTextSize;
        foreach (var headerOffset in new[] { 0x20, 0x30 })
        {
            var address = BinaryPrimitives.ReadInt32LittleEndian(effectiveHeader.AsSpan(headerOffset + 4));
            var size = BinaryPrimitives.ReadInt32LittleEndian(effectiveHeader.AsSpan(headerOffset + 8));
            if (textAddress < 0 || address < 0 || size < 0 || size > 0 && textEnd > address)
            {
                mismatch = "the code section overlaps another mapped segment or has an invalid layout.";
                return false;
            }
        }

        mismatch = string.Empty;
        return true;
    }

    private static bool StableHeaderBytesMatch(byte[] baseline, byte[] effective)
    {
        baseline = baseline.ToArray();
        effective = effective.ToArray();
        foreach (var header in new[] { baseline, effective })
        {
            foreach (var offset in new[] { 0x10, 0x20, 0x30 }) header.AsSpan(offset, sizeof(int)).Clear();
            header.AsSpan(0x60, sizeof(int) * 3).Clear();
            header.AsSpan(0xA0, 0x60).Clear();
        }
        return baseline.AsSpan().SequenceEqual(effective);
    }
}
