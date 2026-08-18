// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;

namespace KM.Core.Semantics;

internal static class SemanticContractGuards
{
    private const int MaximumContractKeyLength = 128;
    private const int MaximumStableIdLength = 1_024;

    public static T DefinedEnum<T>(T value, string parameterName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, null);
        }

        return value;
    }

    public static int PositiveVersion(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Schema versions must be positive.");
        }

        return value;
    }

    public static string ContractKey(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        if (value.Length is 0 or > MaximumContractKeyLength)
        {
            throw new ArgumentException(
                $"A contract key must contain between 1 and {MaximumContractKeyLength} characters.",
                parameterName);
        }

        if (!IsAsciiLowerAlphaNumeric(value[0]) || !IsAsciiLowerAlphaNumeric(value[^1]))
        {
            throw new ArgumentException(
                "A contract key must start and end with a lowercase ASCII letter or digit.",
                parameterName);
        }

        foreach (var character in value)
        {
            if (!IsAsciiLowerAlphaNumeric(character) && character is not '.' and not '-' and not '_')
            {
                throw new ArgumentException(
                    "A contract key may contain only lowercase ASCII letters, digits, '.', '-', and '_'.",
                    parameterName);
            }
        }

        return value;
    }

    public static string StableId(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        if (value.Length is 0 or > MaximumStableIdLength)
        {
            throw new ArgumentException(
                $"A stable id must contain between 1 and {MaximumStableIdLength} characters.",
                parameterName);
        }

        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]))
        {
            throw new ArgumentException("A stable id cannot have leading or trailing whitespace.", parameterName);
        }

        if (value.Any(char.IsControl))
        {
            throw new ArgumentException("A stable id cannot contain control characters.", parameterName);
        }

        return value;
    }

    public static string StableCode(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        if (value.Length is 0 or > MaximumContractKeyLength)
        {
            throw new ArgumentException(
                $"A stable code must contain between 1 and {MaximumContractKeyLength} characters.",
                parameterName);
        }

        if (!IsAsciiAlphaNumeric(value[0]) || !IsAsciiAlphaNumeric(value[^1]))
        {
            throw new ArgumentException("A stable code must start and end with an ASCII letter or digit.", parameterName);
        }

        foreach (var character in value)
        {
            if (!IsAsciiAlphaNumeric(character) && character is not '.' and not '-' and not '_')
            {
                throw new ArgumentException(
                    "A stable code may contain only ASCII letters, digits, '.', '-', and '_'.",
                    parameterName);
            }
        }

        return value;
    }

    public static string Sha256Fingerprint(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("A fingerprint must be a 64-character SHA-256 hexadecimal value.", parameterName);
        }

        return value.ToLowerInvariant();
    }

    public static ImmutableArray<T> ImmutableItems<T>(
        IEnumerable<T> items,
        string parameterName,
        int maximumCount)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(items, parameterName);
        if (maximumCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        var builder = ImmutableArray.CreateBuilder<T>();
        foreach (var item in items)
        {
            if (item is null)
            {
                throw new ArgumentException("A semantic collection cannot contain null values.", parameterName);
            }

            if (builder.Count == maximumCount)
            {
                throw new ArgumentException(
                    $"A semantic collection cannot contain more than {maximumCount} values.",
                    parameterName);
            }

            builder.Add(item);
        }

        return builder.ToImmutable();
    }

    public static ImmutableArray<T> DistinctImmutableItems<T>(
        IEnumerable<T>? items,
        string parameterName,
        int maximumCount)
        where T : notnull
    {
        if (items is null)
        {
            return ImmutableArray<T>.Empty;
        }

        if (maximumCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        var builder = ImmutableArray.CreateBuilder<T>();
        var seen = new HashSet<T>();
        foreach (var item in items)
        {
            if (item is null)
            {
                throw new ArgumentException("A contract collection cannot contain null values.", parameterName);
            }

            if (!seen.Add(item))
            {
                throw new ArgumentException("A contract collection cannot contain duplicate values.", parameterName);
            }

            if (builder.Count == maximumCount)
            {
                throw new ArgumentException(
                    $"A contract collection cannot contain more than {maximumCount} values.",
                    parameterName);
            }

            builder.Add(item);
        }

        return builder.ToImmutable();
    }

    private static bool IsAsciiLowerAlphaNumeric(char value)
    {
        return value is >= 'a' and <= 'z' or >= '0' and <= '9';
    }

    private static bool IsAsciiAlphaNumeric(char value)
    {
        return value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
    }
}
