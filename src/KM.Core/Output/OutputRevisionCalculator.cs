// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KM.Core.Output;

internal static class OutputRevisionCalculator
{
    public static OutputStateRevision FromTokens(string domain, IEnumerable<string?> tokens)
    {
        return FromTokens(domain, tokens, OutputLimits.MaximumRevisionTokens);
    }

    public static OutputStateRevision FromTokens(
        string domain,
        IEnumerable<string?> tokens,
        int maximumTokens)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(tokens);
        if (maximumTokens <= 0 || maximumTokens > OutputLimits.MaximumJournalRevisionTokens)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumTokens));
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, domain);
        var count = 0;
        foreach (var token in tokens)
        {
            if (count == maximumTokens)
            {
                throw new OutputLimitExceededException("Output revision material exceeds its entry limit.");
            }

            Append(hash, token);
            count++;
        }

        AppendInt32(hash, count);
        return new OutputStateRevision(Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    public static IEnumerable<string?> FileStateTokens(OutputFileState? state)
    {
        if (state is null)
        {
            yield return null;
            yield break;
        }

        yield return state.Exists ? "1" : "0";
        yield return state.Sha256;
        yield return state.LengthBytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void Append(IncrementalHash hash, string? value)
    {
        if (value is null)
        {
            AppendInt32(hash, -1);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        AppendInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        hash.AppendData(buffer);
    }
}
