// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace KM.Core.RuntimeSettings;

/// <summary>
/// Strict reader and writer for the per-title cheat selection document. The
/// runtime is allowed to change boolean values, but never the KM-owned name set.
/// </summary>
public static class AtmosphereCheatToggleDocument
{
    public const int MaximumEntryCount = 127;
    public const int MaximumNameUtf8Bytes = 63;
    public const int MaximumDocumentBytes = 32 * 1024;

    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static byte[] Create(IEnumerable<KeyValuePair<string, bool>> entries)
    {
        var normalized = ValidateEntries(entries);
        var builder = new StringBuilder();
        foreach (var entry in normalized)
        {
            builder.Append('[')
                .Append(entry.Key)
                .Append("]\n")
                .Append(entry.Value ? "true\n" : "false\n");
        }

        var bytes = Utf8.GetBytes(builder.ToString());
        if (bytes.Length is < 1 or > MaximumDocumentBytes)
        {
            throw new ArgumentException("The cheat selection document is empty or too large.", nameof(entries));
        }

        return bytes;
    }

    public static ImmutableArray<KeyValuePair<string, bool>> Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 1 or > MaximumDocumentBytes
            || bytes[^1] != (byte)'\n'
            || bytes.Contains((byte)'\r'))
        {
            throw new InvalidDataException(
                "The cheat selection document does not use the canonical text envelope.");
        }

        string text;
        try
        {
            text = Utf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("The cheat selection document is not valid UTF-8.", exception);
        }

        var lines = text.Split('\n');
        if (lines[^1].Length != 0
            || lines.Length < 3
            || ((lines.Length - 1) & 1) != 0)
        {
            throw new InvalidDataException("The cheat selection document has an incomplete entry.");
        }

        var entries = ImmutableArray.CreateBuilder<KeyValuePair<string, bool>>((lines.Length - 1) / 2);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < lines.Length - 1; index += 2)
        {
            var header = lines[index];
            if (header.Length < 3 || header[0] != '[' || header[^1] != ']')
            {
                throw new InvalidDataException("A cheat selection name is malformed.");
            }

            var name = header[1..^1];
            ValidateName(name, nameof(bytes));
            if (!seen.Add(name) || entries.Count == MaximumEntryCount)
            {
                throw new InvalidDataException(
                    "The cheat selection document contains a duplicate or excessive name inventory.");
            }

            var value = lines[index + 1] switch
            {
                "true" => true,
                "false" => false,
                _ => throw new InvalidDataException(
                    "A cheat selection value must be canonical true or false text."),
            };
            entries.Add(new KeyValuePair<string, bool>(name, value));
        }

        if (entries.Count == 0)
        {
            throw new InvalidDataException("The cheat selection document has no entries.");
        }

        return entries.ToImmutable();
    }

    public static string ComputeInventoryIdentity(ReadOnlySpan<byte> bytes)
    {
        return ComputeInventoryIdentity(Parse(bytes).Select(entry => entry.Key));
    }

    public static string ComputeInventoryIdentity(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var normalized = ValidateNames(names);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("km-cheat-toggle-inventory-v1\0"u8);
        Span<byte> length = stackalloc byte[sizeof(ushort)];
        foreach (var name in normalized.OrderBy(name => name, StringComparer.Ordinal))
        {
            var bytes = Utf8.GetBytes(name);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
                length,
                checked((ushort)bytes.Length));
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static bool HasExactInventory(ReadOnlySpan<byte> bytes, string inventoryIdentity)
    {
        try
        {
            return string.Equals(
                ComputeInventoryIdentity(bytes),
                inventoryIdentity,
                StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            return false;
        }
    }

    public static byte[] PreserveSelections(
        ReadOnlySpan<byte> currentBytes,
        ReadOnlySpan<byte> nextDefaults)
    {
        var current = Parse(currentBytes).ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        var next = Parse(nextDefaults);
        return Create(next.Select(entry => new KeyValuePair<string, bool>(
            entry.Key,
            current.GetValueOrDefault(entry.Key, entry.Value))));
    }

    private static ImmutableArray<KeyValuePair<string, bool>> ValidateEntries(
        IEnumerable<KeyValuePair<string, bool>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var result = ImmutableArray.CreateBuilder<KeyValuePair<string, bool>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            ValidateName(entry.Key, nameof(entries));
            if (!seen.Add(entry.Key) || result.Count == MaximumEntryCount)
            {
                throw new ArgumentException(
                    "The cheat selection inventory is duplicated or exceeds its limit.",
                    nameof(entries));
            }

            result.Add(entry);
        }

        if (result.Count == 0)
        {
            throw new ArgumentException("The cheat selection inventory cannot be empty.", nameof(entries));
        }

        return result.ToImmutable();
    }

    private static ImmutableArray<string> ValidateNames(IEnumerable<string> names)
    {
        var result = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            ValidateName(name, nameof(names));
            if (!seen.Add(name) || result.Count == MaximumEntryCount)
            {
                throw new ArgumentException(
                    "The cheat selection inventory is duplicated or exceeds its limit.",
                    nameof(names));
            }

            result.Add(name);
        }

        if (result.Count == 0)
        {
            throw new ArgumentException("The cheat selection inventory cannot be empty.", nameof(names));
        }

        return result.ToImmutable();
    }

    private static void ValidateName(string name, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Any(character => character is < ' ' or > '~' or '[' or ']')
            || Utf8.GetByteCount(name) > MaximumNameUtf8Bytes)
        {
            throw new ArgumentException(
                "A cheat selection name contains unsupported text or exceeds its limit.",
                parameterName);
        }
    }
}
