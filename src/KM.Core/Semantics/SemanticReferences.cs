// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Immutable;

namespace KM.Core.Semantics;

public sealed record ReferenceRelationshipKind
{
    public ReferenceRelationshipKind(string key, int schemaVersion)
    {
        Key = SemanticContractGuards.ContractKey(key, nameof(key));
        SchemaVersion = SemanticContractGuards.PositiveVersion(schemaVersion, nameof(schemaVersion));
    }

    public string Key { get; }

    public int SchemaVersion { get; }
}

public enum ReferenceConfidence
{
    Unknown = 0,
    Verified = 1,
    Derived = 2,
    Heuristic = 3,
}

public sealed record ReferenceEdge
{
    public ReferenceEdge(
        SemanticRecordRef source,
        SemanticRecordRef target,
        ReferenceRelationshipKind relationship,
        SemanticProviderId providerId,
        ReferenceConfidence confidence,
        SourceSnapshot snapshot)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Relationship = relationship ?? throw new ArgumentNullException(nameof(relationship));
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        Confidence = SemanticContractGuards.DefinedEnum(confidence, nameof(confidence));
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

        if (confidence == ReferenceConfidence.Unknown)
        {
            throw new ArgumentException("A materialized reference edge must declare its confidence.", nameof(confidence));
        }

        if (source.GameFamily != target.GameFamily || source.GameFamily != snapshot.Revision.GameFamily)
        {
            throw new ArgumentException("A reference edge cannot cross game families or source revisions.");
        }
    }

    public SemanticRecordRef Source { get; }

    public SemanticRecordRef Target { get; }

    public ReferenceRelationshipKind Relationship { get; }

    public SemanticProviderId ProviderId { get; }

    public ReferenceConfidence Confidence { get; }

    public SourceSnapshot Snapshot { get; }
}

public enum ReferenceCoverageState
{
    Complete = 1,
    Partial = 2,
    Unavailable = 3,
}

public sealed record ReferenceCoverage
{
    private const int MaximumCoveredDomains = 128;

    public ReferenceCoverage(
        SemanticProviderId providerId,
        SourceSnapshot snapshot,
        ReferenceCoverageState state,
        ReferenceConfidence confidence,
        IEnumerable<SemanticDomainKey>? coveredDomains = null,
        string? reasonCode = null)
    {
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        State = SemanticContractGuards.DefinedEnum(state, nameof(state));
        Confidence = SemanticContractGuards.DefinedEnum(confidence, nameof(confidence));
        CoveredDomains = SemanticContractGuards.DistinctImmutableItems(
            coveredDomains,
            nameof(coveredDomains),
            MaximumCoveredDomains);

        if (state == ReferenceCoverageState.Unavailable && reasonCode is null)
        {
            throw new ArgumentException("Unavailable reference coverage requires a stable reason code.", nameof(reasonCode));
        }

        if (state == ReferenceCoverageState.Unavailable && confidence != ReferenceConfidence.Unknown)
        {
            throw new ArgumentException("Unavailable reference coverage must use unknown confidence.", nameof(confidence));
        }

        if (state != ReferenceCoverageState.Unavailable && confidence == ReferenceConfidence.Unknown)
        {
            throw new ArgumentException("Available reference coverage must declare its confidence.", nameof(confidence));
        }

        ReasonCode = reasonCode is null
            ? null
            : SemanticContractGuards.StableCode(reasonCode, nameof(reasonCode));
    }

    public SemanticProviderId ProviderId { get; }

    public SourceSnapshot Snapshot { get; }

    public ReferenceCoverageState State { get; }

    public ReferenceConfidence Confidence { get; }

    public ImmutableArray<SemanticDomainKey> CoveredDomains { get; }

    public string? ReasonCode { get; }
}

public sealed record ReferenceQueryResult
{
    public const int MaximumEdges = 200;
    public const int MaximumCoverageEntries = 128;

    public ReferenceQueryResult(
        ProjectSourceRevision revision,
        IEnumerable<ReferenceEdge> edges,
        IEnumerable<ReferenceCoverage> coverage)
    {
        Revision = revision ?? throw new ArgumentNullException(nameof(revision));
        Edges = SemanticContractGuards.ImmutableItems(edges, nameof(edges), MaximumEdges);
        Coverage = SemanticContractGuards.ImmutableItems(
            coverage,
            nameof(coverage),
            MaximumCoverageEntries);

        if (Edges.Any(edge => edge.Snapshot.Revision != revision)
            || Coverage.Any(item => item.Snapshot.Revision != revision))
        {
            throw new ArgumentException("Every edge and coverage entry must belong to the query source revision.");
        }
    }

    public ProjectSourceRevision Revision { get; }

    public ImmutableArray<ReferenceEdge> Edges { get; }

    public ImmutableArray<ReferenceCoverage> Coverage { get; }
}
