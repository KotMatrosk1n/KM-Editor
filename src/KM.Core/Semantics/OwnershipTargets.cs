// SPDX-License-Identifier: GPL-3.0-only

using System.Text;

namespace KM.Core.Semantics;

public sealed record RelativeOutputPath
{
    public const int MaximumLength = 4_096;
    public const int MaximumSegmentLength = 255;

    public RelativeOutputPath(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length == 0 || value != value.Trim())
        {
            throw new ArgumentException("An output path cannot be empty or have surrounding whitespace.", nameof(value));
        }

        if (value.Length > MaximumLength)
        {
            throw new ArgumentException($"An output path cannot exceed {MaximumLength} characters.", nameof(value));
        }

        var normalized = value
            .Replace('\\', '/')
            .Normalize(NormalizationForm.FormC);
        if (normalized.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"A normalized output path cannot exceed {MaximumLength} characters.",
                nameof(value));
        }

        if (normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.Contains(':', StringComparison.Ordinal)
            || normalized.Any(character => character is '"' or '<' or '>' or '|' or '?' or '*')
            || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("An output path must be a safe relative path.", nameof(value));
        }

        var segments = normalized.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new ArgumentException("An output path cannot contain empty, current, or parent segments.", nameof(value));
        }

        if (segments.Any(segment => segment.Length > MaximumSegmentLength))
        {
            throw new ArgumentException(
                $"An output path segment cannot exceed {MaximumSegmentLength} characters.",
                nameof(value));
        }

        if (segments.Any(segment => segment.EndsWith('.') || segment.EndsWith(' ')))
        {
            throw new ArgumentException("An output path segment cannot end with a dot or space.", nameof(value));
        }

        if (segments.Any(IsWindowsReservedDeviceAlias))
        {
            throw new ArgumentException("An output path cannot contain a reserved Windows device name.", nameof(value));
        }

        Value = string.Join('/', segments);
        CanonicalKey = Value.ToUpperInvariant();
    }

    public string Value { get; }

    /// <summary>
    /// A Unicode-normalized, case-folded ownership key. It deliberately uses
    /// conservative default Windows semantics so case-only or canonically
    /// equivalent paths cannot become separate ownership claims.
    /// </summary>
    public string CanonicalKey { get; }

    public override string ToString() => Value;

    public bool Equals(RelativeOutputPath? other)
    {
        return ReferenceEquals(this, other)
            || (other is not null
                && string.Equals(CanonicalKey, other.CanonicalKey, StringComparison.Ordinal));
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(CanonicalKey);
    }

    private static bool IsWindowsReservedDeviceAlias(string segment)
    {
        var extensionSeparator = segment.IndexOf('.');
        var alias = extensionSeparator < 0 ? segment : segment[..extensionSeparator];

        if (alias.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || alias.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || alias.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || alias.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || alias.Equals("CLOCK$", StringComparison.OrdinalIgnoreCase)
            || alias.Equals("CONIN$", StringComparison.OrdinalIgnoreCase)
            || alias.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return alias.Length == 4
            && (alias.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || alias.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && (alias[3] is >= '1' and <= '9' or '\u00b9' or '\u00b2' or '\u00b3');
    }
}

public sealed record OwnedArchiveMemberId
{
    public OwnedArchiveMemberId(string value)
    {
        Value = SemanticContractGuards.StableId(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record OwnedByteRange
{
    public OwnedByteRange(long offset, long length)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "A byte-range offset cannot be negative.");
        }

        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "A byte-range length must be positive.");
        }

        if (offset > long.MaxValue - length)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "The byte range exceeds the supported address space.");
        }

        Offset = offset;
        Length = length;
    }

    public long Offset { get; }

    public long Length { get; }

    public long EndExclusive => Offset + Length;
}

public enum OwnedTargetScopeKind
{
    File = 1,
    ArchiveMember = 2,
    Record = 3,
    ByteRange = 4,
}

public sealed record OwnedTargetAddress
{
    public OwnedTargetAddress(
        RelativeOutputPath file,
        OwnedArchiveMemberId? archiveMember = null,
        SemanticRecordRef? record = null,
        OwnedByteRange? byteRange = null)
    {
        File = file ?? throw new ArgumentNullException(nameof(file));
        ArchiveMember = archiveMember;
        Record = record;
        ByteRange = byteRange;

        if (record is not null && byteRange is not null)
        {
            throw new ArgumentException("An ownership address must select either a semantic record or a byte range, not both.");
        }
    }

    public RelativeOutputPath File { get; }

    public OwnedArchiveMemberId? ArchiveMember { get; }

    public SemanticRecordRef? Record { get; }

    public OwnedByteRange? ByteRange { get; }

    public OwnedTargetScopeKind ScopeKind => Record is not null
        ? OwnedTargetScopeKind.Record
        : ByteRange is not null
            ? OwnedTargetScopeKind.ByteRange
            : ArchiveMember is not null
                ? OwnedTargetScopeKind.ArchiveMember
                : OwnedTargetScopeKind.File;
}

public sealed record PreservationRuleDescriptor
{
    public PreservationRuleDescriptor(
        string key,
        int schemaVersion,
        bool preservesUnownedData,
        bool requiresPreimage)
    {
        Key = SemanticContractGuards.ContractKey(key, nameof(key));
        SchemaVersion = SemanticContractGuards.PositiveVersion(schemaVersion, nameof(schemaVersion));
        PreservesUnownedData = preservesUnownedData;
        RequiresPreimage = requiresPreimage;
    }

    public string Key { get; }

    public int SchemaVersion { get; }

    public bool PreservesUnownedData { get; }

    public bool RequiresPreimage { get; }
}

public sealed record OwnedTarget
{
    public OwnedTarget(
        GameFamily gameFamily,
        OwnedTargetAddress address,
        OwnershipOwnerId ownerId,
        PreservationRuleDescriptor preservationRule)
    {
        GameFamily = SemanticContractGuards.DefinedEnum(gameFamily, nameof(gameFamily));
        Address = address ?? throw new ArgumentNullException(nameof(address));
        OwnerId = ownerId ?? throw new ArgumentNullException(nameof(ownerId));
        PreservationRule = preservationRule ?? throw new ArgumentNullException(nameof(preservationRule));

        if (address.Record is not null && address.Record.GameFamily != gameFamily)
        {
            throw new ArgumentException("An owned semantic record must belong to the target game family.", nameof(address));
        }
    }

    public GameFamily GameFamily { get; }

    public OwnedTargetAddress Address { get; }

    public OwnershipOwnerId OwnerId { get; }

    public PreservationRuleDescriptor PreservationRule { get; }
}
