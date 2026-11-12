// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers;
using System.Text;

namespace KM.Formats;

/// <summary>FNV-1a over UTF-8 paths, without allocating for ASCII game paths.</summary>
internal static class Utf8PathHash
{
    private const ulong Prime = 0x100000001B3;
    private const ulong Basis = 0xCBF29CE484222645;

    internal static ulong Compute(ReadOnlySpan<char> path, bool normalizeSeparators = false)
    {
        var hash = Basis;
        var index = 0;
        for (; index < path.Length; index++)
        {
            var value = path[index];
            if (value > 0x7F) break;
            if (normalizeSeparators && value == '\\') value = '/';
            hash = unchecked((hash ^ value) * Prime);
        }

        if (index == path.Length) return hash;

        // Keep Encoding.UTF8's replacement behavior for malformed UTF-16, too.
        var remaining = path[index..];
        var byteCount = Encoding.UTF8.GetByteCount(remaining);
        byte[]? rented = null;
        Span<byte> bytes = byteCount <= 512
            ? stackalloc byte[byteCount]
            : (rented = ArrayPool<byte>.Shared.Rent(byteCount));
        try
        {
            var written = Encoding.UTF8.GetBytes(remaining, bytes);
            foreach (var value in bytes[..written])
            {
                var normalized = normalizeSeparators && value == (byte)'\\' ? (byte)'/' : value;
                hash = unchecked((hash ^ normalized) * Prime);
            }

            return hash;
        }
        finally
        {
            if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
