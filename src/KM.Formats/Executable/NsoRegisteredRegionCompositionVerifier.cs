// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using K4os.Compression.LZ4;

namespace KM.Formats.Executable;

/// <summary>
/// Performs bounded structural and whole-image semantic comparisons for NSO
/// composition. Derived compression metadata is normalized before comparison;
/// executable identity and all actual segment bytes remain part of the proof.
/// </summary>
public static class NsoRegisteredRegionCompositionVerifier
{
    /// <summary>
    /// Compares the complete semantic NSO image while allowing only
    /// serialization-derived offsets, compressed sizes, and hashes to differ.
    /// This is intended as the final check after game-specific recognizers have
    /// removed every known editor transformation from an untrusted candidate.
    /// </summary>
    public static bool SemanticallyMatches(
        ReadOnlySpan<byte> retailMain,
        ReadOnlySpan<byte> candidateMain)
    {
        if (retailMain.IsEmpty || candidateMain.IsEmpty)
        {
            return false;
        }

        if (retailMain.SequenceEqual(candidateMain))
        {
            return true;
        }

        try
        {
            var retail = NsoFile.Parse(retailMain.ToArray());
            if (!TryGetOpaqueSpans(retailMain, retail, out var retailOpaque)
                || !CandidateHeaderMatchesRetail(
                    candidateMain,
                    retailMain,
                    retail,
                    retailOpaque))
            {
                return false;
            }

            var candidate = NsoFile.Parse(candidateMain.ToArray());
            return retail.Version == candidate.Version
                && retail.Flags == candidate.Flags
                && retail.BuildId.AsSpan().SequenceEqual(candidate.BuildId)
                && NormalizeHeader(retail).AsSpan().SequenceEqual(NormalizeHeader(candidate))
                && retail.Text.DecompressedData.AsSpan()
                    .SequenceEqual(candidate.Text.DecompressedData)
                && retail.Ro.DecompressedData.AsSpan()
                    .SequenceEqual(candidate.Ro.DecompressedData)
                && retail.Data.DecompressedData.AsSpan()
                    .SequenceEqual(candidate.Data.DecompressedData)
                && DeclaredSegmentHashesMatch(retail)
                && CandidateSegmentHashesMatch(retail, candidate)
                && OpaqueSpansMatch(retailMain, retail, candidateMain, candidate);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidDataException or
            OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// Preflights an untrusted candidate against a trusted Base NSO without
    /// parsing or allocating any candidate segment. The candidate must retain
    /// the Base identity and in-memory segment geometry, and its encoded
    /// segments must occupy a bounded standard text, ro, data file layout.
    /// </summary>
    public static bool HasCompatibleLayoutEnvelope(
        ReadOnlySpan<byte> retailMain,
        ReadOnlySpan<byte> candidateMain)
    {
        if (retailMain.IsEmpty || candidateMain.IsEmpty)
        {
            return false;
        }

        try
        {
            var retail = NsoFile.Parse(retailMain.ToArray());
            return TryGetOpaqueSpans(retailMain, retail, out var retailOpaque)
                && CandidateHeaderMatchesRetail(
                    candidateMain,
                    retailMain,
                    retail,
                    retailOpaque);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidDataException or
            OverflowException)
        {
            return false;
        }
    }

    private static bool CandidateHeaderMatchesRetail(
        ReadOnlySpan<byte> candidate,
        ReadOnlySpan<byte> retailBytes,
        NsoFile retail,
        OpaqueSpans retailOpaque)
    {
        if (candidate.Length < NsoFile.HeaderSize
            || BinaryPrimitives.ReadUInt32LittleEndian(candidate[0x00..]) != NsoFile.Magic
            || BinaryPrimitives.ReadUInt32LittleEndian(candidate[0x04..]) != retail.Version
            || BinaryPrimitives.ReadUInt32LittleEndian(candidate[0x0C..]) != (uint)retail.Flags
            || !candidate.Slice(0x40, 0x20).SequenceEqual(retail.BuildId))
        {
            return false;
        }

        return CandidateSegmentHeaderMatches(candidate, 0x10, retail.Text)
            && CandidateSegmentHeaderMatches(candidate, 0x20, retail.Ro)
            && CandidateSegmentHeaderMatches(candidate, 0x30, retail.Data)
            && CandidateFileLayoutIsSafe(
                candidate,
                retailBytes,
                retail,
                retailOpaque);
    }

    private static bool CandidateSegmentHeaderMatches(
        ReadOnlySpan<byte> candidate,
        int headerOffset,
        NsoSegment retailSegment)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(candidate[(headerOffset + 0x04)..])
                == retailSegment.Header.MemoryOffset
            && BinaryPrimitives.ReadInt32LittleEndian(candidate[(headerOffset + 0x08)..])
                == retailSegment.DecompressedData.Length;
    }

    private static bool CandidateFileLayoutIsSafe(
        ReadOnlySpan<byte> candidate,
        ReadOnlySpan<byte> retailBytes,
        NsoFile retail,
        OpaqueSpans retailOpaque)
    {
        var text = ReadRawSegment(candidate, 0x10, 0x60);
        var ro = ReadRawSegment(candidate, 0x20, 0x64);
        var data = ReadRawSegment(candidate, 0x30, 0x68);
        if (!RawSegmentEncodingIsSafe(
                text,
                retail.Text.DecompressedData.Length,
                retail.Flags.HasFlag(NsoFlags.CompressedText))
            || !RawSegmentEncodingIsSafe(
                ro,
                retail.Ro.DecompressedData.Length,
                retail.Flags.HasFlag(NsoFlags.CompressedRo))
            || !RawSegmentEncodingIsSafe(
                data,
                retail.Data.DecompressedData.Length,
                retail.Flags.HasFlag(NsoFlags.CompressedData)))
        {
            return false;
        }

        var textEnd = (long)text.FileOffset + text.EncodedSize;
        var roEnd = (long)ro.FileOffset + ro.EncodedSize;
        var dataEnd = (long)data.FileOffset + data.EncodedSize;
        if (text.FileOffset < NsoFile.HeaderSize
            || textEnd > ro.FileOffset
            || roEnd > data.FileOffset
            || dataEnd > candidate.Length)
        {
            return false;
        }

        var candidateOpaque = new OpaqueSpans(
            new SpanRange(
                NsoFile.HeaderSize,
                text.FileOffset - NsoFile.HeaderSize),
            new SpanRange(
                (int)textEnd,
                ro.FileOffset - (int)textEnd),
            new SpanRange(
                (int)roEnd,
                data.FileOffset - (int)roEnd),
            new SpanRange(
                (int)dataEnd,
                candidate.Length - (int)dataEnd));
        return OpaqueSpansAreCompatible(
            retailBytes,
            retailOpaque,
            candidate,
            candidateOpaque);
    }

    private static RawSegment ReadRawSegment(
        ReadOnlySpan<byte> candidate,
        int segmentHeaderOffset,
        int encodedSizeOffset)
    {
        return new RawSegment(
            BinaryPrimitives.ReadInt32LittleEndian(candidate[segmentHeaderOffset..]),
            BinaryPrimitives.ReadInt32LittleEndian(candidate[encodedSizeOffset..]));
    }

    private static bool RawSegmentEncodingIsSafe(
        RawSegment segment,
        int decompressedSize,
        bool isCompressed)
    {
        if (segment.FileOffset < 0
            || segment.EncodedSize < 0
            || (decompressedSize > 0 && segment.EncodedSize == 0))
        {
            return false;
        }

        return isCompressed
            ? segment.EncodedSize <= LZ4Codec.MaximumOutputSize(decompressedSize)
            : segment.EncodedSize == decompressedSize;
    }

    private static bool DeclaredSegmentHashesMatch(NsoFile nso)
    {
        return DeclaredSegmentHashMatches(
                nso.Text,
                nso.Flags.HasFlag(NsoFlags.CheckHashText))
            && DeclaredSegmentHashMatches(
                nso.Ro,
                nso.Flags.HasFlag(NsoFlags.CheckHashRo))
            && DeclaredSegmentHashMatches(
                nso.Data,
                nso.Flags.HasFlag(NsoFlags.CheckHashData));
    }

    private static bool DeclaredSegmentHashMatches(
        NsoSegment segment,
        bool hashIsDeclared)
    {
        return !hashIsDeclared
            || segment.Hash.AsSpan().SequenceEqual(
                NsoFile.ComputeHash(segment.DecompressedData));
    }

    private static bool CandidateSegmentHashesMatch(
        NsoFile retail,
        NsoFile candidate)
    {
        return CandidateSegmentHashMatches(retail.Text, candidate.Text)
            && CandidateSegmentHashMatches(retail.Ro, candidate.Ro)
            && CandidateSegmentHashMatches(retail.Data, candidate.Data);
    }

    private static bool CandidateSegmentHashMatches(
        NsoSegment retail,
        NsoSegment candidate)
    {
        if (retail.DecompressedData.AsSpan().SequenceEqual(candidate.DecompressedData))
        {
            return retail.Hash.AsSpan().SequenceEqual(candidate.Hash);
        }

        return candidate.Hash.AsSpan().SequenceEqual(
            NsoFile.ComputeHash(candidate.DecompressedData));
    }

    private static bool OpaqueSpansMatch(
        ReadOnlySpan<byte> retailBytes,
        NsoFile retail,
        ReadOnlySpan<byte> candidateBytes,
        NsoFile candidate)
    {
        return TryGetOpaqueSpans(retailBytes, retail, out var retailOpaque)
            && TryGetOpaqueSpans(candidateBytes, candidate, out var candidateOpaque)
            && OpaqueSpansAreCompatible(
                retailBytes,
                retailOpaque,
                candidateBytes,
                candidateOpaque);
    }

    private static bool TryGetOpaqueSpans(
        ReadOnlySpan<byte> bytes,
        NsoFile nso,
        out OpaqueSpans opaque)
    {
        opaque = default;
        var text = new RawSegment(
            nso.Text.Header.FileOffset,
            nso.Text.CompressedSize);
        var ro = new RawSegment(
            nso.Ro.Header.FileOffset,
            nso.Ro.CompressedSize);
        var data = new RawSegment(
            nso.Data.Header.FileOffset,
            nso.Data.CompressedSize);
        if (!RawSegmentEncodingIsSafe(
                text,
                nso.Text.DecompressedData.Length,
                nso.Flags.HasFlag(NsoFlags.CompressedText))
            || !RawSegmentEncodingIsSafe(
                ro,
                nso.Ro.DecompressedData.Length,
                nso.Flags.HasFlag(NsoFlags.CompressedRo))
            || !RawSegmentEncodingIsSafe(
                data,
                nso.Data.DecompressedData.Length,
                nso.Flags.HasFlag(NsoFlags.CompressedData)))
        {
            return false;
        }

        var textEnd = (long)text.FileOffset + text.EncodedSize;
        var roEnd = (long)ro.FileOffset + ro.EncodedSize;
        var dataEnd = (long)data.FileOffset + data.EncodedSize;
        if (text.FileOffset < NsoFile.HeaderSize
            || textEnd > ro.FileOffset
            || roEnd > data.FileOffset
            || dataEnd > bytes.Length)
        {
            return false;
        }

        opaque = new OpaqueSpans(
            new SpanRange(
                NsoFile.HeaderSize,
                text.FileOffset - NsoFile.HeaderSize),
            new SpanRange(
                (int)textEnd,
                ro.FileOffset - (int)textEnd),
            new SpanRange(
                (int)roEnd,
                data.FileOffset - (int)roEnd),
            new SpanRange(
                (int)dataEnd,
                bytes.Length - (int)dataEnd));
        return true;
    }

    private static bool OpaqueSpanMatches(
        ReadOnlySpan<byte> retail,
        SpanRange retailRange,
        ReadOnlySpan<byte> candidate,
        SpanRange candidateRange)
    {
        return retailRange.Length == candidateRange.Length
            && retail.Slice(retailRange.Start, retailRange.Length).SequenceEqual(
                candidate.Slice(candidateRange.Start, candidateRange.Length));
    }

    private static bool OpaqueSpansAreCompatible(
        ReadOnlySpan<byte> retailBytes,
        OpaqueSpans retail,
        ReadOnlySpan<byte> candidateBytes,
        OpaqueSpans candidate)
    {
        return OpaqueSpanMatches(
                retailBytes,
                retail.BeforeText,
                candidateBytes,
                candidate.BeforeText)
            && OpaquePaddingMatches(
                retailBytes,
                retail.BeforeRo,
                candidateBytes,
                candidate.BeforeRo)
            && OpaquePaddingMatches(
                retailBytes,
                retail.BeforeData,
                candidateBytes,
                candidate.BeforeData)
            && OpaquePaddingMatches(
                retailBytes,
                retail.Trailing,
                candidateBytes,
                candidate.Trailing);
    }

    private static bool OpaquePaddingMatches(
        ReadOnlySpan<byte> retail,
        SpanRange retailRange,
        ReadOnlySpan<byte> candidate,
        SpanRange candidateRange)
    {
        var retailPadding = retail.Slice(retailRange.Start, retailRange.Length);
        var candidatePadding = candidate.Slice(
            candidateRange.Start,
            candidateRange.Length);
        // Recompressing a segment can relocate the following segment and make
        // the NSO writer append zero alignment after the opaque bytes it
        // preserves. Require that exact trusted prefix and permit only the
        // writer's zero suffix; never permit shortening or replacement.
        return candidatePadding.Length >= retailPadding.Length
            && candidatePadding[..retailPadding.Length].SequenceEqual(retailPadding)
            && IsAllZero(candidatePadding[retailPadding.Length..]);
    }

    private static bool IsAllZero(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] NormalizeHeader(NsoFile nso)
    {
        if (nso.RawHeader.Length != NsoFile.HeaderSize)
        {
            throw new InvalidDataException("The NSO header is incomplete.");
        }

        var header = nso.RawHeader.ToArray();
        foreach (var offset in new[] { 0x10, 0x20, 0x30 })
        {
            header.AsSpan(offset, sizeof(int)).Clear();
        }

        header.AsSpan(0x60, 3 * sizeof(int)).Clear();
        header.AsSpan(0xA0, 3 * 0x20).Clear();
        return header;
    }

    private readonly record struct OpaqueSpans(
        SpanRange BeforeText,
        SpanRange BeforeRo,
        SpanRange BeforeData,
        SpanRange Trailing);

    private readonly record struct SpanRange(int Start, int Length);

    private readonly record struct RawSegment(int FileOffset, int EncodedSize);
}
