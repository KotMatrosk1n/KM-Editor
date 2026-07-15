// SPDX-License-Identifier: GPL-3.0-only

using KM.Formats.Executable;

namespace KM.SwSh.ExeFs;

internal static class SwShExeFsMainComparison
{
    public static bool IsSemanticallyEquivalentToBase(byte[] candidateBytes, byte[] baseBytes)
    {
        ArgumentNullException.ThrowIfNull(candidateBytes);
        ArgumentNullException.ThrowIfNull(baseBytes);

        try
        {
            var candidate = NsoFile.Parse(candidateBytes);
            var baseNso = NsoFile.Parse(baseBytes);
            return candidate.Version == baseNso.Version
                && candidate.Flags == baseNso.Flags
                && candidate.BuildId.SequenceEqual(baseNso.BuildId)
                && SegmentsMatch(candidate.Text, baseNso.Text)
                && SegmentsMatch(candidate.Ro, baseNso.Ro)
                && SegmentsMatch(candidate.Data, baseNso.Data)
                && StableHeaderBytesMatch(candidate.RawHeader, baseNso.RawHeader);
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool SegmentsMatch(NsoSegment candidate, NsoSegment baseSegment)
    {
        return candidate.Header.MemoryOffset == baseSegment.Header.MemoryOffset
            && candidate.Header.DecompressedSize == baseSegment.Header.DecompressedSize
            && candidate.DecompressedData.SequenceEqual(baseSegment.DecompressedData);
    }

    internal static bool StableHeaderBytesMatch(byte[] candidateHeader, byte[] baseHeader)
    {
        if (candidateHeader.Length != NsoFile.HeaderSize || baseHeader.Length != NsoFile.HeaderSize)
        {
            return false;
        }

        var normalizedCandidate = candidateHeader.ToArray();
        var normalizedBase = baseHeader.ToArray();
        ClearEncodingDependentHeaderFields(normalizedCandidate);
        ClearEncodingDependentHeaderFields(normalizedBase);
        return normalizedCandidate.SequenceEqual(normalizedBase);
    }

    private static void ClearEncodingDependentHeaderFields(byte[] header)
    {
        Array.Clear(header, 0x10, sizeof(int));
        Array.Clear(header, 0x20, sizeof(int));
        Array.Clear(header, 0x30, sizeof(int));
        Array.Clear(header, 0x60, sizeof(int) * 3);
        Array.Clear(header, 0xA0, 0x60);
    }
}
