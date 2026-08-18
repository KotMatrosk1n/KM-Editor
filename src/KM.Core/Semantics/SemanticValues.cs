// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;

namespace KM.Core.Semantics;

public enum SemanticValueKind
{
    Null = 1,
    Boolean = 2,
    SignedInteger = 3,
    UnsignedInteger = 4,
    Decimal = 5,
    Float32 = 6,
    Float64 = 7,
    Text = 8,
    Binary = 9,
    Enum = 10,
    OrderedList = 11,
    Structured = 12,
}

/// <summary>
/// Hard per-value limits for semantic envelopes. Adapters must carry semantic
/// deltas, never whole source files, archives, message corpora, or other bulk data.
/// Application pages and persistent documents impose their own aggregate limits.
/// </summary>
public static class SemanticValueLimits
{
    public const int MaximumTextCharacters = 262_144;
    public const int MaximumBinaryBytes = 1_048_576;
    public const int MaximumListItems = 8_192;
    public const int MaximumStructuredFields = 1_024;
    public const int MaximumNestingDepth = 24;
    public const int MaximumTreeNodes = 32_768;
    public const int MaximumTreeTextCharacters = 524_288;
    public const int MaximumTreeBinaryBytes = 1_048_576;
}

/// <summary>
/// A display-independent semantic value. Game-domain adapters own its meaning,
/// validation, formatting, canonical equality, and conversion to writable data.
/// </summary>
public abstract record SemanticValue
{
    private protected SemanticValue()
    {
    }

    public abstract SemanticValueKind Kind { get; }
}

public sealed record SemanticNullValue : SemanticValue
{
    private SemanticNullValue()
    {
    }

    public static SemanticNullValue Instance { get; } = new();

    public override SemanticValueKind Kind => SemanticValueKind.Null;
}

public sealed record SemanticBooleanValue(bool Value) : SemanticValue
{
    public override SemanticValueKind Kind => SemanticValueKind.Boolean;
}

public sealed record SemanticSignedIntegerValue(long Value) : SemanticValue
{
    public override SemanticValueKind Kind => SemanticValueKind.SignedInteger;
}

public sealed record SemanticUnsignedIntegerValue(ulong Value) : SemanticValue
{
    public override SemanticValueKind Kind => SemanticValueKind.UnsignedInteger;
}

public sealed record SemanticDecimalValue(decimal Value) : SemanticValue
{
    public override SemanticValueKind Kind => SemanticValueKind.Decimal;
}

public sealed record SemanticFloat32Value : SemanticValue
{
    public SemanticFloat32Value(float value)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A semantic floating-point value must be finite.");
        }

        Value = value;
    }

    public float Value { get; }

    public override SemanticValueKind Kind => SemanticValueKind.Float32;
}

public sealed record SemanticFloat64Value : SemanticValue
{
    public SemanticFloat64Value(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A semantic floating-point value must be finite.");
        }

        Value = value;
    }

    public double Value { get; }

    public override SemanticValueKind Kind => SemanticValueKind.Float64;
}

public sealed record SemanticTextValue : SemanticValue
{
    public SemanticTextValue(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        if (value.Length > SemanticValueLimits.MaximumTextCharacters)
        {
            throw new ArgumentException(
                $"Semantic text cannot exceed {SemanticValueLimits.MaximumTextCharacters} characters.",
                nameof(value));
        }
    }

    public string Value { get; }

    public override SemanticValueKind Kind => SemanticValueKind.Text;
}

public sealed record SemanticBinaryValue : SemanticValue
{
    public SemanticBinaryValue(IEnumerable<byte> value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = ImmutableArray.CreateBuilder<byte>();
        foreach (var item in value)
        {
            if (builder.Count == SemanticValueLimits.MaximumBinaryBytes)
            {
                throw new ArgumentException(
                    $"Semantic binary data cannot exceed {SemanticValueLimits.MaximumBinaryBytes} bytes.",
                    nameof(value));
            }

            builder.Add(item);
        }

        Value = builder.ToImmutable();
    }

    public ImmutableArray<byte> Value { get; }

    public override SemanticValueKind Kind => SemanticValueKind.Binary;

    public bool Equals(SemanticBinaryValue? other)
    {
        return ReferenceEquals(this, other)
            || (other is not null && Value.AsSpan().SequenceEqual(other.Value.AsSpan()));
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Value)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}

public sealed record SemanticEnumValue : SemanticValue
{
    public SemanticEnumValue(string enumType, string member, long? numericValue = null)
    {
        EnumType = SemanticContractGuards.ContractKey(enumType, nameof(enumType));
        Member = SemanticContractGuards.StableId(member, nameof(member));
        NumericValue = numericValue;
    }

    public string EnumType { get; }

    public string Member { get; }

    public long? NumericValue { get; }

    public override SemanticValueKind Kind => SemanticValueKind.Enum;
}

public sealed record SemanticOrderedListValue : SemanticValue
{
    public SemanticOrderedListValue(IEnumerable<SemanticValue> items)
    {
        Items = SemanticContractGuards.ImmutableItems(
            items,
            nameof(items),
            SemanticValueLimits.MaximumListItems);
        SemanticValueBudget.Validate(this, nameof(items));
    }

    public ImmutableArray<SemanticValue> Items { get; }

    public override SemanticValueKind Kind => SemanticValueKind.OrderedList;

    public bool Equals(SemanticOrderedListValue? other)
    {
        return ReferenceEquals(this, other)
            || (other is not null && Items.SequenceEqual(other.Items));
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}

public sealed record SemanticStructuredValue : SemanticValue
{
    public SemanticStructuredValue(
        string typeKey,
        int schemaVersion,
        IEnumerable<KeyValuePair<string, SemanticValue>> fields)
    {
        TypeKey = SemanticContractGuards.ContractKey(typeKey, nameof(typeKey));
        SchemaVersion = SemanticContractGuards.PositiveVersion(schemaVersion, nameof(schemaVersion));
        ArgumentNullException.ThrowIfNull(fields);

        var builder = ImmutableSortedDictionary.CreateBuilder<string, SemanticValue>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (builder.Count == SemanticValueLimits.MaximumStructuredFields)
            {
                throw new ArgumentException(
                    $"A structured semantic value cannot contain more than "
                    + $"{SemanticValueLimits.MaximumStructuredFields} fields.",
                    nameof(fields));
            }

            var fieldKey = SemanticContractGuards.ContractKey(field.Key, nameof(fields));
            var fieldValue = field.Value ?? throw new ArgumentException(
                "A structured semantic value cannot contain a null field value.",
                nameof(fields));

            if (!builder.TryAdd(fieldKey, fieldValue))
            {
                throw new ArgumentException($"The structured semantic field '{fieldKey}' is duplicated.", nameof(fields));
            }
        }

        Fields = builder.ToImmutable();
        SemanticValueBudget.Validate(this, nameof(fields));
    }

    public string TypeKey { get; }

    public int SchemaVersion { get; }

    public ImmutableSortedDictionary<string, SemanticValue> Fields { get; }

    public override SemanticValueKind Kind => SemanticValueKind.Structured;

    public bool Equals(SemanticStructuredValue? other)
    {
        return ReferenceEquals(this, other)
            || (other is not null
                && SchemaVersion == other.SchemaVersion
                && string.Equals(TypeKey, other.TypeKey, StringComparison.Ordinal)
                && Fields.SequenceEqual(other.Fields));
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TypeKey, StringComparer.Ordinal);
        hash.Add(SchemaVersion);
        foreach (var field in Fields)
        {
            hash.Add(field.Key, StringComparer.Ordinal);
            hash.Add(field.Value);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// A canonical adapter-owned payload. The fingerprint covers the adapter's
/// canonical serialization of <see cref="Root"/>; the common layer does not
/// reinterpret or rewrite that representation.
/// </summary>
public sealed record SemanticPayload
{
    public SemanticPayload(
        SemanticAdapterId adapterId,
        int schemaVersion,
        SemanticValue root,
        string canonicalFingerprint)
    {
        AdapterId = adapterId ?? throw new ArgumentNullException(nameof(adapterId));
        SchemaVersion = SemanticContractGuards.PositiveVersion(schemaVersion, nameof(schemaVersion));
        Root = root ?? throw new ArgumentNullException(nameof(root));
        SemanticValueBudget.Validate(root, nameof(root));
        CanonicalFingerprint = SemanticContractGuards.Sha256Fingerprint(
            canonicalFingerprint,
            nameof(canonicalFingerprint));
    }

    public SemanticAdapterId AdapterId { get; }

    public int SchemaVersion { get; }

    public SemanticValue Root { get; }

    public string CanonicalFingerprint { get; }
}

internal static class SemanticValueBudget
{
    public static void Validate(SemanticValue root, string parameterName)
    {
        var nodeCount = 0;
        var textCharacters = 0L;
        var binaryBytes = 0L;
        var pending = new Stack<(SemanticValue Value, int Depth)>();
        pending.Push((root, 1));

        while (pending.TryPop(out var current))
        {
            nodeCount++;
            if (nodeCount > SemanticValueLimits.MaximumTreeNodes)
            {
                throw new ArgumentException(
                    $"A semantic value tree cannot contain more than {SemanticValueLimits.MaximumTreeNodes} nodes.",
                    parameterName);
            }

            if (current.Depth > SemanticValueLimits.MaximumNestingDepth)
            {
                throw new ArgumentException(
                    $"A semantic value tree cannot exceed {SemanticValueLimits.MaximumNestingDepth} levels.",
                    parameterName);
            }

            switch (current.Value)
            {
                case SemanticNullValue:
                case SemanticBooleanValue:
                case SemanticSignedIntegerValue:
                case SemanticUnsignedIntegerValue:
                case SemanticDecimalValue:
                case SemanticFloat32Value:
                case SemanticFloat64Value:
                case SemanticEnumValue:
                    break;

                case SemanticTextValue text:
                    textCharacters += text.Value.Length;
                    if (textCharacters > SemanticValueLimits.MaximumTreeTextCharacters)
                    {
                        throw new ArgumentException(
                            $"A semantic value tree cannot contain more than "
                            + $"{SemanticValueLimits.MaximumTreeTextCharacters} text characters.",
                            parameterName);
                    }

                    break;

                case SemanticBinaryValue binary:
                    binaryBytes += binary.Value.Length;
                    if (binaryBytes > SemanticValueLimits.MaximumTreeBinaryBytes)
                    {
                        throw new ArgumentException(
                            $"A semantic value tree cannot contain more than "
                            + $"{SemanticValueLimits.MaximumTreeBinaryBytes} binary bytes.",
                            parameterName);
                    }

                    break;

                case SemanticOrderedListValue list:
                    for (var index = list.Items.Length - 1; index >= 0; index--)
                    {
                        pending.Push((list.Items[index], current.Depth + 1));
                    }

                    break;

                case SemanticStructuredValue structured:
                    foreach (var value in structured.Fields.Values.Reverse())
                    {
                        pending.Push((value, current.Depth + 1));
                    }

                    break;

                default:
                    throw new ArgumentException("The semantic value tree contains an unsupported value type.", parameterName);
            }
        }
    }
}
