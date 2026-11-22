// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text;
using KM.Formats.Executable;

namespace KM.ZA.ExeFs;

public static class ZaRecompExecutablePatch
{
    public const string RelativePath = "exefs/km-editor-main.pchtxt";

    public static byte[]? Create(byte[] original, byte[] edited)
    {
        if (original.AsSpan().SequenceEqual(edited))
            return null;

        if (!NsoRegisteredRegionCompositionVerifier.HasCompatibleLayoutEnvelope(original, edited))
            throw new InvalidDataException("ZA Recomp code patches require the original executable build and segment layout.");

        var baseline = NsoFile.Inspect(original);
        var candidate = NsoFile.Parse(edited);
        var segments = candidate.Segments;
        var baseSegments = baseline.Segments;
        if (!NsoRegisteredRegionCompositionVerifier.SemanticallyMatches(original,
                candidate.Write(baseline.Text.DecompressedData, baseline.Ro.DecompressedData, baseline.Data.DecompressedData)))
        {
            throw new InvalidDataException("ZA Recomp code patches cannot represent executable metadata changes.");
        }

        return Write(baseline.BuildId, CompareSegments());

        IEnumerable<KeyValuePair<uint, byte>> CompareSegments()
        {
            for (var index = 0; index < segments.Count; index++)
            {
                var before = baseSegments[index].DecompressedData;
                var after = segments[index].DecompressedData;
                var offset = (uint)segments[index].Header.MemoryOffset;
                for (var position = 0; position < before.Length; position++)
                {
                    if (before[position] != after[position])
                        yield return new KeyValuePair<uint, byte>(checked(offset + (uint)position), after[position]);
                }
            }
        }
    }

    public static byte[]? CreateFromIpsOffsets(byte[] original, string build, IEnumerable<KeyValuePair<uint, byte>> patch)
    {
        var baseline = NsoFile.Inspect(original);
        if (!Convert.ToHexString(baseline.BuildId).StartsWith(build, StringComparison.OrdinalIgnoreCase) || build.Length < 16)
            throw new InvalidDataException("The executable patch targets a different ZA Recomp game build.");

        var segments = baseline.Segments;
        return Write(baseline.BuildId, ReadChanges());

        IEnumerable<KeyValuePair<uint, byte>> ReadChanges()
        {
            foreach (var (address, value) in patch.OrderBy(entry => entry.Key))
            {
                var offset = (long)address - NsoFile.HeaderSize;
                var segment = segments.FirstOrDefault(part =>
                    offset >= part.Header.MemoryOffset
                    && offset < (long)part.Header.MemoryOffset + part.DecompressedData.Length);
                if (segment is null)
                    throw new InvalidDataException("The executable patch writes outside the mapped game image.");
                if (segment.DecompressedData[offset - segment.Header.MemoryOffset] != value)
                    yield return new KeyValuePair<uint, byte>((uint)offset, value);
            }
        }
    }

    private static byte[]? Write(byte[] buildId, IEnumerable<KeyValuePair<uint, byte>> changes)
    {
        var output = new StringBuilder()
            .Append("@nsobid-").Append(Convert.ToHexString(buildId)).Append('\n')
            .Append("@flag offset_shift 0x100\n\n@enabled\n");
        Span<byte> run = stackalloc byte[64];
        var length = 0;
        uint start = 0;
        foreach (var (offset, value) in changes)
        {
            if (length > 0 && (length == run.Length || offset != start + length))
            {
                AppendRun(output, start, run[..length]);
                length = 0;
            }

            if (length == 0)
                start = offset;
            run[length++] = value;
        }

        if (length == 0)
            return null;

        AppendRun(output, start, run[..length]);
        output.Append("@stop\n");
        return Encoding.UTF8.GetBytes(output.ToString());
    }

    private static void AppendRun(StringBuilder output, uint offset, ReadOnlySpan<byte> bytes) =>
        output.Append(offset.ToString("X8", CultureInfo.InvariantCulture)).Append(' ')
            .Append(Convert.ToHexString(bytes)).Append('\n');
}
