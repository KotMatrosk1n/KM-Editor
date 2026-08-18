// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Core.Semantics;

public abstract record SemanticMutationTarget
{
    private protected SemanticMutationTarget()
    {
    }

    public abstract SemanticRecordRef Record { get; }
}

public sealed record SemanticRecordMutationTarget : SemanticMutationTarget
{
    public SemanticRecordMutationTarget(SemanticRecordRef record)
    {
        Record = record ?? throw new ArgumentNullException(nameof(record));
    }

    public override SemanticRecordRef Record { get; }
}

public sealed record SemanticFieldMutationTarget : SemanticMutationTarget
{
    public SemanticFieldMutationTarget(SemanticFieldRef field)
    {
        Field = field ?? throw new ArgumentNullException(nameof(field));
    }

    public SemanticFieldRef Field { get; }

    public override SemanticRecordRef Record => Field.Record;
}

public sealed record SemanticOperationDescriptor
{
    public SemanticOperationDescriptor(
        SemanticAdapterId adapterId,
        SemanticOperationKind operationKind,
        int schemaVersion)
    {
        AdapterId = adapterId ?? throw new ArgumentNullException(nameof(adapterId));
        OperationKind = operationKind ?? throw new ArgumentNullException(nameof(operationKind));
        SchemaVersion = SemanticContractGuards.PositiveVersion(schemaVersion, nameof(schemaVersion));
    }

    public SemanticAdapterId AdapterId { get; }

    public SemanticOperationKind OperationKind { get; }

    public int SchemaVersion { get; }
}

public enum SemanticBaselineState
{
    Present = 1,
    Absent = 2,
}

public sealed record ExpectedSemanticBaseline
{
    public ExpectedSemanticBaseline(
        SourceSnapshot source,
        SemanticBaselineState state,
        string? targetFingerprint = null,
        SemanticValue? expectedValue = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        State = SemanticContractGuards.DefinedEnum(state, nameof(state));

        if (state == SemanticBaselineState.Present && targetFingerprint is null)
        {
            throw new ArgumentException("A present semantic baseline requires a target fingerprint.", nameof(targetFingerprint));
        }

        if (state == SemanticBaselineState.Absent && (targetFingerprint is not null || expectedValue is not null))
        {
            throw new ArgumentException("An absent semantic baseline cannot carry a target fingerprint or value.");
        }

        TargetFingerprint = targetFingerprint is null
            ? null
            : SemanticContractGuards.Sha256Fingerprint(targetFingerprint, nameof(targetFingerprint));
        ExpectedValue = expectedValue;
    }

    public SourceSnapshot Source { get; }

    public SemanticBaselineState State { get; }

    public string? TargetFingerprint { get; }

    public SemanticValue? ExpectedValue { get; }
}

public enum MutationProvenanceKind
{
    User = 1,
    Import = 2,
    Recipe = 3,
    Generator = 4,
    Extension = 5,
    Migration = 6,
    System = 7,
}

public sealed record MutationProvenance
{
    public MutationProvenance(
        MutationProvenanceKind kind,
        SemanticProviderId producerId,
        DateTimeOffset createdAt,
        string? originId = null)
    {
        Kind = SemanticContractGuards.DefinedEnum(kind, nameof(kind));
        ProducerId = producerId ?? throw new ArgumentNullException(nameof(producerId));

        if (createdAt == default)
        {
            throw new ArgumentException("Mutation provenance requires a creation timestamp.", nameof(createdAt));
        }

        CreatedAtUtc = createdAt.ToUniversalTime();
        OriginId = originId is null
            ? null
            : SemanticContractGuards.StableId(originId, nameof(originId));
    }

    public MutationProvenanceKind Kind { get; }

    public SemanticProviderId ProducerId { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public string? OriginId { get; }
}

public sealed record SemanticMutation
{
    public SemanticMutation(
        SemanticMutationId id,
        SemanticMutationTarget target,
        SemanticOperationDescriptor operation,
        SemanticPayload payload,
        ExpectedSemanticBaseline expectedBaseline,
        MutationProvenance provenance)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        ExpectedBaseline = expectedBaseline ?? throw new ArgumentNullException(nameof(expectedBaseline));
        Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));

        if (operation.AdapterId != payload.AdapterId)
        {
            throw new ArgumentException("The mutation operation and payload must have the same adapter id.", nameof(payload));
        }

        if (target.Record.GameFamily != expectedBaseline.Source.Revision.GameFamily)
        {
            throw new ArgumentException(
                "The mutation target and expected source baseline must belong to the same game family.",
                nameof(expectedBaseline));
        }
    }

    public SemanticMutationId Id { get; }

    public SemanticMutationTarget Target { get; }

    public SemanticOperationDescriptor Operation { get; }

    public SemanticPayload Payload { get; }

    public ExpectedSemanticBaseline ExpectedBaseline { get; }

    public MutationProvenance Provenance { get; }
}
