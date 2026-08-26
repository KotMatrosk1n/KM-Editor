// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using KM.Core.Projects;
using KM.Core.Semantics;

namespace KM.Core.Editing;

public static class RowClipboardLimits
{
    public const int EnvelopeSchemaVersion = 1;
    public const int MaximumRows = 128;
    public const int MaximumValuesPerRow = 64;
    public const int MaximumTotalValues = 4_096;
    public const int MaximumDependencies = 512;
    public const int MaximumCanonicalPayloadBytes = 256 * 1_024;
}

public enum RowClipboardPasteMode
{
    Replace = 1,
    Insert = 2,
    Append = 3,
    Merge = 4,
}

public enum RowClipboardValueKind
{
    Boolean = 1,
    SignedInteger = 2,
    UnsignedInteger = 3,
    Decimal = 4,
    String = 5,
    DependencyReference = 6,
}

public sealed record RowClipboardScope
{
    public RowClipboardScope(string projectId, ProjectGame game, string profileId)
    {
        ProjectId = RowClipboardContractGuards.BoundedText(projectId, 128, nameof(projectId));
        Game = RowClipboardContractGuards.DefinedGame(game, nameof(game));
        ProfileId = RowClipboardContractGuards.StableIdentifier(profileId, nameof(profileId));
    }

    public string ProjectId { get; }

    public ProjectGame Game { get; }

    public GameFamily GameFamily => Game.ToGameFamily();

    public string ProfileId { get; }
}

public sealed record RowClipboardEditorSchema
{
    public RowClipboardEditorSchema(string editorId, string rowKind, int rowSchemaVersion)
    {
        EditorId = RowClipboardContractGuards.StableIdentifier(editorId, nameof(editorId));
        RowKind = RowClipboardContractGuards.StableIdentifier(rowKind, nameof(rowKind));
        RowSchemaVersion = RowClipboardContractGuards.PositiveBound(
            rowSchemaVersion,
            ushort.MaxValue,
            nameof(rowSchemaVersion));
    }

    public string EditorId { get; }

    public string RowKind { get; }

    public int RowSchemaVersion { get; }
}

public sealed record RowClipboardLogicalIdentity
{
    public RowClipboardLogicalIdentity(string kind, string key)
    {
        Kind = RowClipboardContractGuards.StableIdentifier(kind, nameof(kind));
        Key = RowClipboardContractGuards.BoundedText(key, 512, nameof(key));
    }

    public string Kind { get; }

    public string Key { get; }
}

public sealed record RowClipboardDependencyReference
{
    public RowClipboardDependencyReference(string kind, string id, string? form = null)
    {
        Kind = RowClipboardContractGuards.StableIdentifier(kind, nameof(kind));
        Id = RowClipboardContractGuards.BoundedText(id, 128, nameof(id));
        Form = form is null
            ? null
            : RowClipboardContractGuards.BoundedText(form, 128, nameof(form));
    }

    public string Kind { get; }

    public string Id { get; }

    public string? Form { get; }

    internal string CanonicalKey => $"{Kind}\0{Id}\0{Form ?? string.Empty}";
}

public abstract record RowClipboardValue
{
    private protected RowClipboardValue()
    {
    }

    public abstract RowClipboardValueKind Kind { get; }
}

public sealed record RowClipboardBooleanValue(bool Value) : RowClipboardValue
{
    public override RowClipboardValueKind Kind => RowClipboardValueKind.Boolean;
}

public sealed record RowClipboardSignedIntegerValue : RowClipboardValue
{
    public RowClipboardSignedIntegerValue(string canonicalValue)
    {
        CanonicalValue = RowClipboardContractGuards.CanonicalSignedInteger(
            canonicalValue,
            nameof(canonicalValue));
    }

    public string CanonicalValue { get; }

    public long Value => long.Parse(CanonicalValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public override RowClipboardValueKind Kind => RowClipboardValueKind.SignedInteger;
}

public sealed record RowClipboardUnsignedIntegerValue : RowClipboardValue
{
    public RowClipboardUnsignedIntegerValue(string canonicalValue)
    {
        CanonicalValue = RowClipboardContractGuards.CanonicalUnsignedInteger(
            canonicalValue,
            nameof(canonicalValue));
    }

    public string CanonicalValue { get; }

    public ulong Value => ulong.Parse(CanonicalValue, NumberStyles.None, CultureInfo.InvariantCulture);

    public override RowClipboardValueKind Kind => RowClipboardValueKind.UnsignedInteger;
}

public sealed record RowClipboardDecimalValue : RowClipboardValue
{
    public RowClipboardDecimalValue(string canonicalValue)
    {
        CanonicalValue = RowClipboardContractGuards.CanonicalDecimal(
            canonicalValue,
            nameof(canonicalValue));
    }

    public string CanonicalValue { get; }

    public override RowClipboardValueKind Kind => RowClipboardValueKind.Decimal;
}

public sealed record RowClipboardStringValue : RowClipboardValue
{
    public RowClipboardStringValue(string value)
    {
        Value = RowClipboardContractGuards.WellFormedString(value, nameof(value));
    }

    public string Value { get; }

    public override RowClipboardValueKind Kind => RowClipboardValueKind.String;
}

public sealed record RowClipboardDependencyValue : RowClipboardValue
{
    public RowClipboardDependencyValue(RowClipboardDependencyReference value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public RowClipboardDependencyReference Value { get; }

    public override RowClipboardValueKind Kind => RowClipboardValueKind.DependencyReference;
}

public sealed record RowClipboardOwnedValue
{
    public RowClipboardOwnedValue(string fieldKey, RowClipboardValue value)
    {
        FieldKey = RowClipboardContractGuards.StableIdentifier(fieldKey, nameof(fieldKey));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string FieldKey { get; }

    public RowClipboardValue Value { get; }
}

public sealed record RowClipboardLogicalRow
{
    public RowClipboardLogicalRow(
        RowClipboardLogicalIdentity sourceIdentity,
        IEnumerable<RowClipboardOwnedValue> values)
    {
        SourceIdentity = sourceIdentity ?? throw new ArgumentNullException(nameof(sourceIdentity));
        ArgumentNullException.ThrowIfNull(values);

        var builder = ImmutableArray.CreateBuilder<RowClipboardOwnedValue>();
        var fieldKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value is null)
            {
                throw new ArgumentException("A logical row cannot contain a null value.", nameof(values));
            }

            if (builder.Count == RowClipboardLimits.MaximumValuesPerRow)
            {
                throw new ArgumentException(
                    $"A logical row cannot contain more than {RowClipboardLimits.MaximumValuesPerRow} values.",
                    nameof(values));
            }

            if (!fieldKeys.Add(value.FieldKey))
            {
                throw new ArgumentException(
                    $"The logical row field '{value.FieldKey}' is duplicated.",
                    nameof(values));
            }

            builder.Add(value);
        }

        if (builder.Count == 0)
        {
            throw new ArgumentException("A logical row must contain at least one owned value.", nameof(values));
        }

        Values = builder
            .OrderBy(value => value.FieldKey, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    public RowClipboardLogicalIdentity SourceIdentity { get; }

    public ImmutableArray<RowClipboardOwnedValue> Values { get; }

    public bool Equals(RowClipboardLogicalRow? other)
    {
        return ReferenceEquals(this, other)
            || (other is not null
                && SourceIdentity == other.SourceIdentity
                && Values.SequenceEqual(other.Values));
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SourceIdentity);
        foreach (var value in Values)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}

public sealed record RowClipboardSource
{
    public RowClipboardSource(
        string projectRevision,
        RowClipboardLogicalIdentity logicalIdentity)
    {
        ProjectRevision = RowClipboardContractGuards.BoundedText(
            projectRevision,
            512,
            nameof(projectRevision));
        LogicalIdentity = logicalIdentity ?? throw new ArgumentNullException(nameof(logicalIdentity));
    }

    public string ProjectRevision { get; }

    public RowClipboardLogicalIdentity LogicalIdentity { get; }
}

public sealed record RowClipboardFieldPolicy
{
    public RowClipboardFieldPolicy(
        string fieldKey,
        IEnumerable<RowClipboardValueKind> valueKinds,
        int? maximumUtf8Bytes = null)
    {
        FieldKey = RowClipboardContractGuards.StableIdentifier(fieldKey, nameof(fieldKey));
        ArgumentNullException.ThrowIfNull(valueKinds);
        var kinds = valueKinds
            .Select(kind => RowClipboardContractGuards.DefinedValueKind(kind, nameof(valueKinds)))
            .ToImmutableArray();
        if (kinds.Length == 0 || kinds.Distinct().Count() != kinds.Length)
        {
            throw new ArgumentException("A field policy requires unique value kinds.", nameof(valueKinds));
        }

        if ((kinds.Contains(RowClipboardValueKind.String)) != maximumUtf8Bytes.HasValue)
        {
            throw new ArgumentException(
                "A string field policy must define its UTF-8 byte limit, and other policies must not define one.",
                nameof(maximumUtf8Bytes));
        }

        ValueKinds = kinds;
        MaximumUtf8Bytes = maximumUtf8Bytes is null
            ? null
            : RowClipboardContractGuards.PositiveBound(
                maximumUtf8Bytes.Value,
                RowClipboardLimits.MaximumCanonicalPayloadBytes,
                nameof(maximumUtf8Bytes));
    }

    public string FieldKey { get; }

    public ImmutableArray<RowClipboardValueKind> ValueKinds { get; }

    public int? MaximumUtf8Bytes { get; }

    public bool Equals(RowClipboardFieldPolicy? other)
    {
        return ReferenceEquals(this, other)
            || (other is not null
                && string.Equals(FieldKey, other.FieldKey, StringComparison.Ordinal)
                && MaximumUtf8Bytes == other.MaximumUtf8Bytes
                && ValueKinds.SequenceEqual(other.ValueKinds));
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(FieldKey, StringComparer.Ordinal);
        hash.Add(MaximumUtf8Bytes);
        foreach (var kind in ValueKinds)
        {
            hash.Add(kind);
        }

        return hash.ToHashCode();
    }
}

public sealed record RowClipboardAdapterSchema
{
    public RowClipboardAdapterSchema(
        RowClipboardEditorSchema editor,
        IEnumerable<ProjectGame> games,
        IEnumerable<string>? profileIds,
        IEnumerable<RowClipboardPasteMode> pasteModes,
        IEnumerable<string> dependencyKinds,
        IEnumerable<RowClipboardFieldPolicy> fieldPolicies,
        int maximumRows = RowClipboardLimits.MaximumRows,
        int maximumValuesPerRow = RowClipboardLimits.MaximumValuesPerRow,
        int maximumTotalValues = RowClipboardLimits.MaximumTotalValues)
    {
        Editor = editor ?? throw new ArgumentNullException(nameof(editor));
        Games = RowClipboardContractGuards.DistinctItems(
            games,
            nameof(games),
            game => RowClipboardContractGuards.DefinedGame(game, nameof(games)));
        if (Games.Length == 0)
        {
            throw new ArgumentException("An adapter schema requires at least one game.", nameof(games));
        }

        ProfileIds = profileIds is null
            ? null
            : RowClipboardContractGuards.DistinctItems(
                profileIds,
                nameof(profileIds),
                value => RowClipboardContractGuards.StableIdentifier(value, nameof(profileIds)));
        if (ProfileIds is { Length: 0 })
        {
            throw new ArgumentException("A profile-restricted adapter requires at least one profile.", nameof(profileIds));
        }

        PasteModes = RowClipboardContractGuards.DistinctItems(
            pasteModes,
            nameof(pasteModes),
            value => RowClipboardContractGuards.DefinedPasteMode(value, nameof(pasteModes)));
        if (PasteModes.Length == 0)
        {
            throw new ArgumentException("An adapter schema requires at least one paste mode.", nameof(pasteModes));
        }

        DependencyKinds = RowClipboardContractGuards.DistinctItems(
                dependencyKinds,
                nameof(dependencyKinds),
                value => RowClipboardContractGuards.StableIdentifier(value, nameof(dependencyKinds)))
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        FieldPolicies = RowClipboardContractGuards.DistinctItems(
                fieldPolicies,
                nameof(fieldPolicies),
                value => value ?? throw new ArgumentException(
                    "An adapter schema cannot contain a null field policy.",
                    nameof(fieldPolicies)),
                RowClipboardLimits.MaximumValuesPerRow)
            .OrderBy(value => value.FieldKey, StringComparer.Ordinal)
            .ToImmutableArray();
        if (FieldPolicies.Length == 0
            || FieldPolicies.Select(value => value.FieldKey).Distinct(StringComparer.Ordinal).Count()
                != FieldPolicies.Length)
        {
            throw new ArgumentException(
                "An adapter schema requires unique field policies.",
                nameof(fieldPolicies));
        }

        MaximumRows = RowClipboardContractGuards.PositiveBound(
            maximumRows,
            RowClipboardLimits.MaximumRows,
            nameof(maximumRows));
        MaximumValuesPerRow = RowClipboardContractGuards.PositiveBound(
            maximumValuesPerRow,
            RowClipboardLimits.MaximumValuesPerRow,
            nameof(maximumValuesPerRow));
        MaximumTotalValues = RowClipboardContractGuards.PositiveBound(
            maximumTotalValues,
            RowClipboardLimits.MaximumTotalValues,
            nameof(maximumTotalValues));
    }

    public RowClipboardEditorSchema Editor { get; }

    public ImmutableArray<ProjectGame> Games { get; }

    public ImmutableArray<string>? ProfileIds { get; }

    public ImmutableArray<RowClipboardPasteMode> PasteModes { get; }

    public ImmutableArray<string> DependencyKinds { get; }

    public ImmutableArray<RowClipboardFieldPolicy> FieldPolicies { get; }

    public int MaximumRows { get; }

    public int MaximumValuesPerRow { get; }

    public int MaximumTotalValues { get; }

    public bool Supports(RowClipboardScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return Games.Contains(scope.Game)
            && (ProfileIds is null || ProfileIds.Value.Contains(scope.ProfileId, StringComparer.Ordinal));
    }
}

public sealed record RowClipboardEnvelopeDraftV1
{
    public RowClipboardEnvelopeDraftV1(
        string producerVersion,
        RowClipboardScope scope,
        RowClipboardEditorSchema editor,
        RowClipboardSource source,
        IEnumerable<RowClipboardDependencyReference> dependencies,
        IEnumerable<RowClipboardLogicalRow> rows)
    {
        ProducerVersion = RowClipboardContractGuards.BoundedText(
            producerVersion,
            64,
            nameof(producerVersion));
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Editor = editor ?? throw new ArgumentNullException(nameof(editor));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Dependencies = RowClipboardContractGuards.Items(
            dependencies,
            nameof(dependencies),
            RowClipboardLimits.MaximumDependencies);
        Rows = RowClipboardContractGuards.Items(
            rows,
            nameof(rows),
            RowClipboardLimits.MaximumRows);
        if (Rows.Length == 0)
        {
            throw new ArgumentException("A row clipboard envelope requires at least one row.", nameof(rows));
        }
    }

    public string ProducerVersion { get; }

    public RowClipboardScope Scope { get; }

    public RowClipboardEditorSchema Editor { get; }

    public RowClipboardSource Source { get; }

    public ImmutableArray<RowClipboardDependencyReference> Dependencies { get; }

    public ImmutableArray<RowClipboardLogicalRow> Rows { get; }
}

public sealed record RowClipboardEnvelopeV1
{
    private static readonly ImmutableArray<string> ExcludedFieldKinds =
        ["identity", "pointer", "archiveOffset", "unknown", "presentation"];

    private RowClipboardEnvelopeV1(
        RowClipboardEnvelopeDraftV1 normalizedDraft,
        string checksum,
        int canonicalPayloadByteCount)
    {
        ProducerVersion = normalizedDraft.ProducerVersion;
        Scope = normalizedDraft.Scope;
        Editor = normalizedDraft.Editor;
        Source = normalizedDraft.Source;
        Dependencies = normalizedDraft.Dependencies;
        Rows = normalizedDraft.Rows;
        Checksum = checksum;
        CanonicalPayloadByteCount = canonicalPayloadByteCount;
    }

    public int EnvelopeSchemaVersion => RowClipboardLimits.EnvelopeSchemaVersion;

    public string ProducerVersion { get; }

    public RowClipboardScope Scope { get; }

    public RowClipboardEditorSchema Editor { get; }

    public RowClipboardSource Source { get; }

    public ImmutableArray<RowClipboardDependencyReference> Dependencies { get; }

    public ImmutableArray<RowClipboardLogicalRow> Rows { get; }

    public string Checksum { get; }

    public int CanonicalPayloadByteCount { get; }

    public static RowClipboardEnvelopeV1 Create(
        RowClipboardEnvelopeDraftV1 draft,
        RowClipboardAdapterSchema adapter)
    {
        return CreateCore(draft, checksum: null, adapter);
    }

    public static RowClipboardEnvelopeV1 Validate(
        RowClipboardEnvelopeDraftV1 draft,
        string checksum,
        RowClipboardAdapterSchema adapter)
    {
        return CreateCore(
            draft,
            RowClipboardContractGuards.UpperSha256(checksum, nameof(checksum)),
            adapter);
    }

    public byte[] GetCanonicalPayloadBytes()
    {
        return RowClipboardCanonicalSerializer.SerializePayload(this);
    }

    private static RowClipboardEnvelopeV1 CreateCore(
        RowClipboardEnvelopeDraftV1 draft,
        string? checksum,
        RowClipboardAdapterSchema adapter)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(adapter);
        ValidateAdapterBoundary(draft, adapter);

        var normalizedDraft = new RowClipboardEnvelopeDraftV1(
            draft.ProducerVersion,
            draft.Scope,
            draft.Editor,
            draft.Source,
            draft.Dependencies
                .OrderBy(value => value.Kind, StringComparer.Ordinal)
                .ThenBy(value => value.Id, StringComparer.Ordinal)
                .ThenBy(value => value.Form ?? string.Empty, StringComparer.Ordinal),
            draft.Rows);
        var candidate = new RowClipboardEnvelopeV1(normalizedDraft, string.Empty, 0);
        var bytes = RowClipboardCanonicalSerializer.SerializePayload(candidate);
        if (bytes.Length > RowClipboardLimits.MaximumCanonicalPayloadBytes)
        {
            throw new ArgumentException(
                $"A row clipboard payload cannot exceed {RowClipboardLimits.MaximumCanonicalPayloadBytes} canonical UTF-8 bytes.",
                nameof(draft));
        }

        var computedChecksum = Convert.ToHexString(SHA256.HashData(bytes));
        if (checksum is not null && !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(checksum),
                Encoding.ASCII.GetBytes(computedChecksum)))
        {
            throw new ArgumentException("The row clipboard checksum does not match its canonical payload.", nameof(checksum));
        }

        return new RowClipboardEnvelopeV1(normalizedDraft, computedChecksum, bytes.Length);
    }

    private static void ValidateAdapterBoundary(
        RowClipboardEnvelopeDraftV1 draft,
        RowClipboardAdapterSchema adapter)
    {
        if (draft.Editor != adapter.Editor)
        {
            throw new ArgumentException("The row clipboard adapter schema is incompatible.", nameof(adapter));
        }

        if (!adapter.Supports(draft.Scope))
        {
            throw new ArgumentException("The row clipboard adapter does not support this project scope.", nameof(adapter));
        }

        if (draft.Rows.Length > adapter.MaximumRows)
        {
            throw new ArgumentException("The row clipboard exceeds the adapter row limit.", nameof(draft));
        }

        var dependencyKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependency in draft.Dependencies)
        {
            if (!adapter.DependencyKinds.Contains(dependency.Kind, StringComparer.Ordinal))
            {
                throw new ArgumentException("The row clipboard contains an unsupported dependency kind.", nameof(draft));
            }

            if (!dependencyKeys.Add(dependency.CanonicalKey))
            {
                throw new ArgumentException("The row clipboard contains a duplicate dependency.", nameof(draft));
            }
        }

        var policies = adapter.FieldPolicies.ToDictionary(
            value => value.FieldKey,
            StringComparer.Ordinal);
        var totalValues = 0;
        foreach (var row in draft.Rows)
        {
            if (row.Values.Length > adapter.MaximumValuesPerRow)
            {
                throw new ArgumentException("A row exceeds the adapter value limit.", nameof(draft));
            }

            totalValues = checked(totalValues + row.Values.Length);
            if (totalValues > adapter.MaximumTotalValues)
            {
                throw new ArgumentException("The row clipboard exceeds the adapter total-value limit.", nameof(draft));
            }

            foreach (var ownedValue in row.Values)
            {
                if (!policies.TryGetValue(ownedValue.FieldKey, out var policy))
                {
                    throw new ArgumentException(
                        $"The row clipboard field '{ownedValue.FieldKey}' is not owned by the adapter.",
                        nameof(draft));
                }

                if (!policy.ValueKinds.Contains(ownedValue.Value.Kind))
                {
                    throw new ArgumentException(
                        $"The row clipboard field '{ownedValue.FieldKey}' has an unsupported value kind.",
                        nameof(draft));
                }

                if (ownedValue.Value is RowClipboardStringValue text
                    && RowClipboardContractGuards.Utf8ByteCount(text.Value) > policy.MaximumUtf8Bytes)
                {
                    throw new ArgumentException(
                        $"The row clipboard field '{ownedValue.FieldKey}' exceeds its UTF-8 limit.",
                        nameof(draft));
                }

                if (ownedValue.Value is RowClipboardDependencyValue dependency
                    && !dependencyKeys.Contains(dependency.Value.CanonicalKey))
                {
                    throw new ArgumentException(
                        $"The row clipboard field '{ownedValue.FieldKey}' references an undeclared dependency.",
                        nameof(draft));
                }
            }
        }
    }

    internal static ImmutableArray<string> GetExcludedFieldKinds() => ExcludedFieldKinds;
}

public sealed record RowClipboardPreviewBinding
{
    public const int PreviewSchemaVersion = 1;

    private RowClipboardPreviewBinding(
        RowClipboardEnvelopeV1 envelope,
        RowClipboardPasteMode mode,
        RowClipboardLogicalIdentity targetIdentity,
        string targetRevision)
    {
        ClipboardChecksum = envelope.Checksum;
        Scope = envelope.Scope;
        Editor = envelope.Editor;
        Mode = mode;
        TargetIdentity = targetIdentity;
        TargetRevision = targetRevision;
        OperationCount = envelope.Rows.Length;
    }

    public string ClipboardChecksum { get; }

    public RowClipboardScope Scope { get; }

    public RowClipboardEditorSchema Editor { get; }

    public RowClipboardPasteMode Mode { get; }

    public RowClipboardLogicalIdentity TargetIdentity { get; }

    public string TargetRevision { get; }

    public int OperationCount { get; }

    public bool AtomicHistoryEvent => true;

    public static RowClipboardPreviewBinding Bind(
        RowClipboardEnvelopeV1 envelope,
        RowClipboardAdapterSchema adapter,
        RowClipboardScope targetScope,
        RowClipboardPasteMode mode,
        RowClipboardLogicalIdentity targetIdentity,
        string targetRevision)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(targetScope);
        ArgumentNullException.ThrowIfNull(targetIdentity);
        RowClipboardContractGuards.DefinedPasteMode(mode, nameof(mode));
        if (envelope.Scope != targetScope)
        {
            throw new ArgumentException(
                "Row clipboard paste is limited to its immutable project, game, and profile scope.",
                nameof(targetScope));
        }

        if (envelope.Editor != adapter.Editor
            || !adapter.Supports(targetScope)
            || !adapter.PasteModes.Contains(mode))
        {
            throw new ArgumentException("The row clipboard paste operation is unavailable.", nameof(adapter));
        }

        return new RowClipboardPreviewBinding(
            envelope,
            mode,
            targetIdentity,
            RowClipboardContractGuards.BoundedText(targetRevision, 512, nameof(targetRevision)));
    }

    public RowClipboardPasteAuthorization RequireFreshTarget(
        RowClipboardEnvelopeV1 envelope,
        string currentTargetRevision)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var revision = RowClipboardContractGuards.BoundedText(
            currentTargetRevision,
            512,
            nameof(currentTargetRevision));
        if (!string.Equals(ClipboardChecksum, envelope.Checksum, StringComparison.Ordinal)
            || Scope != envelope.Scope
            || Editor != envelope.Editor
            || OperationCount != envelope.Rows.Length)
        {
            throw new InvalidOperationException("The row clipboard preview no longer matches its copied snapshot.");
        }

        if (!string.Equals(TargetRevision, revision, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The row clipboard target changed after preview and must be previewed again.");
        }

        return new RowClipboardPasteAuthorization(
            envelope,
            Mode,
            TargetIdentity,
            TargetRevision,
            OperationCount);
    }
}

public sealed record RowClipboardPasteAuthorization
{
    internal RowClipboardPasteAuthorization(
        RowClipboardEnvelopeV1 envelope,
        RowClipboardPasteMode mode,
        RowClipboardLogicalIdentity targetIdentity,
        string targetRevision,
        int operationCount)
    {
        Envelope = envelope;
        Mode = mode;
        TargetIdentity = targetIdentity;
        TargetRevision = targetRevision;
        OperationCount = operationCount;
    }

    public RowClipboardEnvelopeV1 Envelope { get; }

    public RowClipboardPasteMode Mode { get; }

    public RowClipboardLogicalIdentity TargetIdentity { get; }

    public string TargetRevision { get; }

    public int OperationCount { get; }

    public bool AtomicHistoryEvent => true;
}

internal static class RowClipboardCanonicalSerializer
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
        SkipValidation = false,
    };

    public static byte[] SerializePayload(RowClipboardEnvelopeV1 envelope)
    {
        var output = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(output, WriterOptions);
        writer.WriteStartObject();

        writer.WritePropertyName("dependencies");
        writer.WriteStartArray();
        foreach (var dependency in envelope.Dependencies)
        {
            WriteDependency(writer, dependency);
        }

        writer.WriteEndArray();

        writer.WritePropertyName("editor");
        writer.WriteStartObject();
        WriteCanonicalString(writer, "editorId", envelope.Editor.EditorId);
        WriteCanonicalString(writer, "rowKind", envelope.Editor.RowKind);
        writer.WriteNumber("rowSchemaVersion", envelope.Editor.RowSchemaVersion);
        writer.WriteEndObject();

        writer.WriteNumber("envelopeSchemaVersion", RowClipboardLimits.EnvelopeSchemaVersion);

        writer.WritePropertyName("excludedFieldKinds");
        writer.WriteStartArray();
        foreach (var excludedFieldKind in RowClipboardEnvelopeV1.GetExcludedFieldKinds())
        {
            writer.WriteStringValue(excludedFieldKind);
        }

        writer.WriteEndArray();

        WriteCanonicalString(writer, "producerVersion", envelope.ProducerVersion);

        writer.WritePropertyName("rows");
        writer.WriteStartArray();
        foreach (var row in envelope.Rows)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("sourceIdentity");
            WriteIdentity(writer, row.SourceIdentity);
            writer.WritePropertyName("values");
            writer.WriteStartArray();
            foreach (var ownedValue in row.Values)
            {
                writer.WriteStartObject();
                WriteCanonicalString(writer, "fieldKey", ownedValue.FieldKey);
                writer.WritePropertyName("value");
                WriteValue(writer, ownedValue.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WritePropertyName("scope");
        writer.WriteStartObject();
        WriteCanonicalString(writer, "game", GameName(envelope.Scope.Game));
        WriteCanonicalString(writer, "gameFamily", GameFamilyName(envelope.Scope.GameFamily));
        WriteCanonicalString(writer, "profileId", envelope.Scope.ProfileId);
        WriteCanonicalString(writer, "projectId", envelope.Scope.ProjectId);
        writer.WriteEndObject();

        writer.WritePropertyName("source");
        writer.WriteStartObject();
        writer.WritePropertyName("logicalIdentity");
        WriteIdentity(writer, envelope.Source.LogicalIdentity);
        WriteCanonicalString(writer, "projectRevision", envelope.Source.ProjectRevision);
        writer.WriteEndObject();

        writer.WriteEndObject();
        writer.Flush();
        return output.WrittenSpan.ToArray();
    }

    private static void WriteIdentity(Utf8JsonWriter writer, RowClipboardLogicalIdentity identity)
    {
        writer.WriteStartObject();
        WriteCanonicalString(writer, "key", identity.Key);
        WriteCanonicalString(writer, "kind", identity.Kind);
        writer.WriteEndObject();
    }

    private static void WriteDependency(
        Utf8JsonWriter writer,
        RowClipboardDependencyReference dependency)
    {
        writer.WriteStartObject();
        if (dependency.Form is null)
        {
            writer.WriteNull("form");
        }
        else
        {
            WriteCanonicalString(writer, "form", dependency.Form);
        }

        WriteCanonicalString(writer, "id", dependency.Id);
        WriteCanonicalString(writer, "kind", dependency.Kind);
        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, RowClipboardValue value)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case RowClipboardBooleanValue boolean:
                WriteCanonicalString(writer, "kind", "boolean");
                writer.WriteBoolean("value", boolean.Value);
                break;

            case RowClipboardSignedIntegerValue signed:
                WriteCanonicalString(writer, "kind", "signedInteger");
                WriteCanonicalString(writer, "value", signed.CanonicalValue);
                break;

            case RowClipboardUnsignedIntegerValue unsigned:
                WriteCanonicalString(writer, "kind", "unsignedInteger");
                WriteCanonicalString(writer, "value", unsigned.CanonicalValue);
                break;

            case RowClipboardDecimalValue decimalValue:
                WriteCanonicalString(writer, "kind", "decimal");
                WriteCanonicalString(writer, "value", decimalValue.CanonicalValue);
                break;

            case RowClipboardStringValue text:
                WriteCanonicalString(writer, "kind", "string");
                WriteCanonicalString(writer, "value", text.Value);
                break;

            case RowClipboardDependencyValue dependency:
                WriteCanonicalString(writer, "kind", "dependencyReference");
                writer.WritePropertyName("value");
                WriteDependency(writer, dependency.Value);
                break;

            default:
                throw new InvalidOperationException("The row clipboard value kind is unsupported.");
        }

        writer.WriteEndObject();
    }

    private static void WriteCanonicalString(
        Utf8JsonWriter writer,
        string propertyName,
        string value)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteRawValue(CanonicalStringBytes(value), skipInputValidation: false);
    }

    private static byte[] CanonicalStringBytes(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case < ' ':
                    builder.Append("\\u");
                    builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        builder.Append('"');
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static string GameName(ProjectGame game)
    {
        return game switch
        {
            ProjectGame.Sword => "sword",
            ProjectGame.Shield => "shield",
            ProjectGame.Scarlet => "scarlet",
            ProjectGame.Violet => "violet",
            ProjectGame.ZA => "za",
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, null),
        };
    }

    private static string GameFamilyName(GameFamily gameFamily)
    {
        return gameFamily switch
        {
            GameFamily.SwordShield => "swordShield",
            GameFamily.ScarletViolet => "scarletViolet",
            GameFamily.LegendsZA => "legendsZA",
            _ => throw new ArgumentOutOfRangeException(nameof(gameFamily), gameFamily, null),
        };
    }
}

internal static class RowClipboardContractGuards
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex StableIdentifierPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex SignedIntegerPattern = new(
        "^(?:0|-[1-9][0-9]*|[1-9][0-9]*)$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex UnsignedIntegerPattern = new(
        "^(?:0|[1-9][0-9]*)$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex DecimalPattern = new(
        "^(?:0|-?[1-9][0-9]*|-?(?:0|[1-9][0-9]*)\\.[0-9]*[1-9])$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static string StableIdentifier(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (!StableIdentifierPattern.IsMatch(value))
        {
            throw new ArgumentException("Expected a stable row clipboard identifier.", parameterName);
        }

        return value;
    }

    public static string BoundedText(string value, int maximumLength, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length is 0
            || value.Length > maximumLength
            || value != value.Trim()
            || value.Any(char.IsControl))
        {
            throw new ArgumentException("Expected bounded row clipboard text.", parameterName);
        }

        WellFormedString(value, parameterName);
        return value;
    }

    public static string WellFormedString(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        try
        {
            _ = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException error)
        {
            throw new ArgumentException("Expected well-formed Unicode text.", parameterName, error);
        }

        return value;
    }

    public static int Utf8ByteCount(string value)
    {
        return StrictUtf8.GetByteCount(value);
    }

    public static int PositiveBound(int value, int maximum, string parameterName)
    {
        if (value is < 1 || value > maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, null);
        }

        return value;
    }

    public static ProjectGame DefinedGame(ProjectGame value, string parameterName)
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, null);
        }

        return value;
    }

    public static RowClipboardValueKind DefinedValueKind(
        RowClipboardValueKind value,
        string parameterName)
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, null);
        }

        return value;
    }

    public static RowClipboardPasteMode DefinedPasteMode(
        RowClipboardPasteMode value,
        string parameterName)
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, null);
        }

        return value;
    }

    public static string CanonicalSignedInteger(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (!SignedIntegerPattern.IsMatch(value)
            || !long.TryParse(
                value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out _))
        {
            throw new ArgumentException("Expected a canonical signed 64-bit integer.", parameterName);
        }

        return value;
    }

    public static string CanonicalUnsignedInteger(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (!UnsignedIntegerPattern.IsMatch(value)
            || !ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw new ArgumentException("Expected a canonical unsigned 64-bit integer.", parameterName);
        }

        return value;
    }

    public static string CanonicalDecimal(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length > 128
            || !DecimalPattern.IsMatch(value)
            || !double.TryParse(
                value,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed)
            || !double.IsFinite(parsed)
            || (parsed == 0d && !string.Equals(value, "0", StringComparison.Ordinal)))
        {
            throw new ArgumentException("Expected a canonical finite decimal.", parameterName);
        }

        return value;
    }

    public static string UpperSha256(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length != 64
            || value.Any(character => character is not (>= '0' and <= '9' or >= 'A' and <= 'F')))
        {
            throw new ArgumentException("Expected an uppercase SHA-256 checksum.", parameterName);
        }

        return value;
    }

    public static ImmutableArray<T> Items<T>(
        IEnumerable<T> values,
        string parameterName,
        int maximumCount)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var builder = ImmutableArray.CreateBuilder<T>();
        foreach (var value in values)
        {
            if (value is null)
            {
                throw new ArgumentException("A row clipboard collection cannot contain null.", parameterName);
            }

            if (builder.Count == maximumCount)
            {
                throw new ArgumentException(
                    $"A row clipboard collection cannot exceed {maximumCount} values.",
                    parameterName);
            }

            builder.Add(value);
        }

        return builder.ToImmutable();
    }

    public static ImmutableArray<TOutput> DistinctItems<TInput, TOutput>(
        IEnumerable<TInput> values,
        string parameterName,
        Func<TInput, TOutput> selector,
        int maximumCount = RowClipboardLimits.MaximumDependencies)
        where TOutput : notnull
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var builder = ImmutableArray.CreateBuilder<TOutput>();
        var seen = new HashSet<TOutput>();
        foreach (var value in values)
        {
            if (builder.Count == maximumCount)
            {
                throw new ArgumentException(
                    $"A row clipboard collection cannot exceed {maximumCount} values.",
                    parameterName);
            }

            var selected = selector(value);
            if (!seen.Add(selected))
            {
                throw new ArgumentException("A row clipboard collection cannot contain duplicates.", parameterName);
            }

            builder.Add(selected);
        }

        return builder.ToImmutable();
    }
}
