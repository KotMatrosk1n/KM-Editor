// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Google.FlatBuffers;

namespace KM.ZA.FashionCatalog;

internal static class ZaFashionCatalogFlatBufferSupport
{
    internal const int MaximumCatalogRows = 50_000;
    internal const int MaximumCatalogTextBytes = 4_096;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static void EnsureKnownFields(
        byte[] bytes,
        int tablePosition,
        int expectedFieldCount,
        string label)
    {
        if (tablePosition < sizeof(int) || tablePosition > bytes.Length - sizeof(int))
        {
            throw new InvalidDataException($"{label} has an invalid table position.");
        }

        var vtableDistance = BinaryPrimitives.ReadInt32LittleEndian(
            bytes.AsSpan(tablePosition, sizeof(int)));
        var vtablePositionLong = (long)tablePosition - vtableDistance;
        if (vtableDistance == 0 || vtablePositionLong < 0 || vtablePositionLong > bytes.Length - 4)
        {
            throw new InvalidDataException($"{label} has an invalid virtual table.");
        }

        var vtablePosition = (int)vtablePositionLong;
        var vtableLength = BinaryPrimitives.ReadUInt16LittleEndian(
            bytes.AsSpan(vtablePosition, sizeof(ushort)));
        if (vtableLength < 4
            || (vtableLength & 1) != 0
            || vtablePosition > bytes.Length - vtableLength)
        {
            throw new InvalidDataException($"{label} has an invalid virtual-table length.");
        }

        var fieldCount = (vtableLength - 4) / 2;
        if (fieldCount > expectedFieldCount)
        {
            throw new InvalidDataException(
                $"{label} contains fields outside the supported KM Fashion Catalog schema; the source was left untouched.");
        }
    }

    internal static void EnsureCount(int count, string label)
    {
        if (count < 0 || count > MaximumCatalogRows)
        {
            throw new InvalidDataException(
                $"{label} exceeds the supported physical-row limit of {MaximumCatalogRows}.");
        }
    }

    internal static string ValidateRequiredText(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"{label} must contain text.");
        }

        ValidateText(value, label);
        return value;
    }

    internal static string? ValidateOptionalText(string? value, string label)
    {
        if (value is not null)
        {
            ValidateText(value, label);
        }

        return value;
    }

    internal static string? ValidateOptionalNonEmptyText(string? value, string label)
    {
        if (value is not null && value.Length == 0)
        {
            throw new InvalidDataException($"{label} must be null or contain text.");
        }

        return ValidateOptionalText(value, label);
    }

    internal static VectorOffset CreateOffsetVector(FlatBufferBuilder builder, int[] offsets)
    {
        builder.StartVector(sizeof(int), offsets.Length, sizeof(int));
        for (var index = offsets.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(offsets[index]);
        }

        return builder.EndVector();
    }

    internal static StringOffset CreatePresentString(
        FlatBufferBuilder builder,
        bool present,
        string? value,
        string label)
    {
        if (!present)
        {
            return default;
        }

        if (value is null)
        {
            throw new InvalidDataException($"A materialized {label} has no value.");
        }

        ValidateText(value, label);
        return builder.CreateString(value);
    }

    internal static void AddUInt(FlatBufferBuilder builder, int slot, uint value, bool present)
    {
        if (!present)
        {
            return;
        }

        var original = builder.ForceDefaults;
        builder.ForceDefaults = true;
        builder.AddUint(slot, value, 0);
        builder.ForceDefaults = original;
    }

    internal static void AddInt(FlatBufferBuilder builder, int slot, int value, bool present)
    {
        if (!present)
        {
            return;
        }

        var original = builder.ForceDefaults;
        builder.ForceDefaults = true;
        builder.AddInt(slot, value, 0);
        builder.ForceDefaults = original;
    }

    internal static void AddBool(FlatBufferBuilder builder, int slot, bool value, bool present)
    {
        if (!present)
        {
            return;
        }

        var original = builder.ForceDefaults;
        builder.ForceDefaults = true;
        builder.AddBool(slot, value, false);
        builder.ForceDefaults = original;
    }

    internal static string HashBytes(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static string CreateSourceRevision(
        ReadOnlySpan<byte> dressUpItems,
        ReadOnlySpan<byte> dressUpGroups,
        ReadOnlySpan<byte> hairAndMakeup,
        ReadOnlySpan<byte> fashionShops,
        ReadOnlySpan<byte> dressUpLineups,
        ReadOnlySpan<byte> hairAndMakeupLineups)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "KM.ZA.FashionCatalog.Source.v2");
        Append(hash, "dress-up-items");
        Append(hash, dressUpItems);
        Append(hash, "dress-up-groups");
        Append(hash, dressUpGroups);
        Append(hash, "hair-and-makeup");
        Append(hash, hairAndMakeup);
        Append(hash, "fashion-shops");
        Append(hash, fashionShops);
        Append(hash, "dress-up-lineups");
        Append(hash, dressUpLineups);
        Append(hash, "hair-and-makeup-lineups");
        Append(hash, hairAndMakeupLineups);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static string CreateRowRevision(
        string rowKind,
        Action<IncrementalHash> appendRow)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "KM.ZA.FashionCatalog.Row.v1");
        Append(hash, rowKind);
        appendRow(hash);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static void Append(IncrementalHash hash, string? value)
    {
        if (value is null)
        {
            Append(hash, -1);
            return;
        }

        var bytes = StrictUtf8.GetBytes(value);
        Append(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    internal static void Append(IncrementalHash hash, ReadOnlySpan<byte> bytes)
    {
        Append(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    internal static void Append(IncrementalHash hash, bool value) => Append(hash, value ? 1 : 0);

    internal static void Append(IncrementalHash hash, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    internal static void Append(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void ValidateText(string value, string label)
    {
        if (value.IndexOf('\0') >= 0)
        {
            throw new InvalidDataException($"{label} cannot contain a null character.");
        }

        int byteCount;
        try
        {
            byteCount = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new InvalidDataException($"{label} is not valid Unicode text.", exception);
        }

        if (byteCount > MaximumCatalogTextBytes)
        {
            throw new InvalidDataException(
                $"{label} exceeds the supported UTF-8 length of {MaximumCatalogTextBytes} bytes.");
        }
    }
}
