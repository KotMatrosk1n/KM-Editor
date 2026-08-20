// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Editing;
using KM.Api.Diagnostics;
using KM.Api.Projects;

namespace KM.Api.Semantics;

public static class SemanticExploreContract
{
    public const int SchemaVersion = 1;
    public const int MaximumPageSize = 100;
    public const int MaximumCursorLength = 2_048;
    public const int MaximumSearchTextLength = 256;
    public const int MaximumExternalRootLength = 4_096;
}

public enum SemanticGameFamilyDto
{
    SwordShield,
    ScarletViolet,
    LegendsZA,
}

public enum SemanticSourceLayerKindDto
{
    Base,
    Layered,
    Pending,
    ComparedMod,
}

public enum SemanticCoverageStateDto
{
    Complete,
    Partial,
    Unavailable,
}

public enum SemanticConfidenceDto
{
    Unknown,
    Verified,
    Derived,
}

public enum SemanticFeatureDto
{
    Search,
    Entity,
    Compare,
    References,
    Impact,
    Ownership,
    ExternalCompare,
    Changes,
}

public enum SemanticValueKindDto
{
    Null,
    Boolean,
    SignedInteger,
    UnsignedInteger,
    Decimal,
    Text,
    Enum,
}

public enum SemanticDifferenceKindDto
{
    Added,
    Removed,
    Reordered,
    Changed,
    Inherited,
    Unavailable,
    Undecodable,
}

public enum SemanticReferenceDirectionDto
{
    Incoming,
    Outgoing,
}

public enum SemanticImpactSeverityDto
{
    Info,
    Warning,
}

public enum SemanticImpactActionabilityDto
{
    ReadOnly,
}

public enum SemanticOwnershipNodeKindDto
{
    Entity,
    Provider,
    File,
    PendingOperation,
}

public enum SemanticOwnershipEdgeKindDto
{
    Owns,
    Targets,
    References,
    Conflicts,
}

public enum SemanticChangeFormatDto
{
    Structured,
    CanonicalText,
}

public sealed record SemanticExploreScopeDto(
    string ProjectId,
    ProjectPathsDto Paths,
    EditSessionDto? PendingSession = null);

public sealed record SemanticProjectRevisionDto(
    string ProjectId,
    SemanticGameFamilyDto GameFamily,
    string Generation,
    string Fingerprint);

public sealed record SemanticSourceLayerDto(
    SemanticSourceLayerKindDto Kind,
    string? InstanceId);

public sealed record SemanticSourceSnapshotDto(
    SemanticSourceLayerDto Layer,
    SemanticProjectRevisionDto Revision,
    string Fingerprint);

public sealed record SemanticRecordKindDto(string Key, int SchemaVersion);

public sealed record SemanticRecordRefDto(
    SemanticGameFamilyDto GameFamily,
    string Domain,
    SemanticRecordKindDto RecordKind,
    string RecordId,
    string? SubrecordId);

public sealed record SemanticScalarValueDto(
    SemanticValueKindDto Kind,
    string? CanonicalValue,
    string DisplayValue);

public sealed record SemanticProviderCoverageDto(
    string ProviderId,
    IReadOnlyList<string> Domains,
    SemanticCoverageStateDto State,
    SemanticConfidenceDto Confidence,
    string? ReasonCode);

public sealed record SemanticProviderDescriptorDto(
    string ProviderId,
    IReadOnlyList<string> Domains,
    IReadOnlyList<SemanticFeatureDto> Features,
    SemanticProviderCoverageDto Coverage);

public sealed record ReadSemanticCapabilitiesRequest(SemanticExploreScopeDto Scope);

public sealed record ReadSemanticCapabilitiesResponse(
    SemanticProjectRevisionDto Revision,
    IReadOnlyList<SemanticSourceSnapshotDto> Snapshots,
    IReadOnlyList<SemanticProviderDescriptorDto> Providers);

public sealed record SemanticPagedRequestDto(int Limit, string? Cursor = null);

public sealed record SearchSemanticRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    string SearchText,
    SemanticSourceLayerKindDto Layer,
    int Limit,
    IReadOnlyList<string>? Domains = null,
    string? Cursor = null);

public sealed record SemanticSearchResultDto(
    SemanticRecordRefDto Record,
    string DisplayName,
    string? Description,
    string DomainLabel,
    SemanticSourceSnapshotDto Snapshot,
    SemanticDifferenceKindDto? ChangeKind);

public sealed record SearchSemanticResponse(
    SemanticProjectRevisionDto Revision,
    string QueryFingerprint,
    IReadOnlyList<SemanticSearchResultDto> Items,
    IReadOnlyList<SemanticProviderCoverageDto> Coverage,
    string? NextCursor);

public sealed record ReadSemanticEntityRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    SemanticRecordRefDto Record,
    SemanticSourceLayerKindDto Layer);

public sealed record SemanticEntityFieldDto(
    string Key,
    string Label,
    string Group,
    SemanticScalarValueDto Value,
    string OwnerId);

public sealed record SemanticEntityFeaturesDto(
    bool Compare,
    bool References,
    bool Impact,
    bool Ownership);

public sealed record SemanticEntityDto(
    SemanticRecordRefDto Record,
    string Title,
    string? Summary,
    SemanticSourceSnapshotDto Snapshot,
    IReadOnlyList<SemanticEntityFieldDto> Fields,
    SemanticEntityFeaturesDto Features);

public sealed record ReadSemanticEntityResponse(
    SemanticProjectRevisionDto Revision,
    string QueryFingerprint,
    SemanticEntityDto Entity,
    IReadOnlyList<SemanticProviderCoverageDto> Coverage);

public sealed record CompareSemanticRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    SemanticSourceLayerKindDto Left,
    SemanticSourceLayerKindDto Right,
    int Limit,
    SemanticRecordRefDto? Record = null,
    string? Cursor = null);

public sealed record SemanticDifferenceDto(
    SemanticRecordRefDto Record,
    string FieldKey,
    string Label,
    SemanticDifferenceKindDto Kind,
    SemanticScalarValueDto? Left,
    SemanticScalarValueDto? Right,
    string OwnerId);

public sealed record CompareSemanticResponse(
    SemanticProjectRevisionDto Revision,
    string QueryFingerprint,
    SemanticSourceSnapshotDto LeftSnapshot,
    SemanticSourceSnapshotDto RightSnapshot,
    IReadOnlyList<SemanticDifferenceDto> Items,
    IReadOnlyList<SemanticProviderCoverageDto> Coverage,
    string? NextCursor);

public sealed record QuerySemanticReferencesRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    SemanticRecordRefDto Record,
    SemanticReferenceDirectionDto Direction,
    SemanticSourceLayerKindDto Layer,
    int Limit,
    string? Cursor = null);

public sealed record SemanticReferenceDto(
    SemanticRecordRefDto Source,
    SemanticRecordRefDto Target,
    string RelationshipKey,
    string RelationshipLabel,
    SemanticConfidenceDto Confidence,
    string ProviderId,
    string SourceTitle,
    string TargetTitle,
    SemanticSourceSnapshotDto Snapshot);

public sealed record QuerySemanticReferencesResponse(
    SemanticProjectRevisionDto Revision,
    string QueryFingerprint,
    IReadOnlyList<SemanticReferenceDto> Items,
    IReadOnlyList<SemanticProviderCoverageDto> Coverage,
    string? NextCursor);

public sealed record QuerySemanticImpactRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    SemanticRecordRefDto Record,
    SemanticSourceLayerKindDto Layer,
    int Limit,
    string? Cursor = null);

public sealed record SemanticImpactDto(
    string RelationshipKey,
    string SourceDomain,
    int Count,
    SemanticImpactSeverityDto Severity,
    SemanticImpactActionabilityDto Actionability,
    string Summary);

public sealed record QuerySemanticImpactResponse(
    SemanticProjectRevisionDto Revision,
    string QueryFingerprint,
    IReadOnlyList<SemanticImpactDto> Items,
    IReadOnlyList<SemanticProviderCoverageDto> Coverage,
    string? NextCursor);

public sealed record QuerySemanticOwnershipRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    int Limit,
    SemanticRecordRefDto? Record = null,
    string? Cursor = null);

public sealed record SemanticOwnershipNodeDto(
    string NodeId,
    SemanticOwnershipNodeKindDto Kind,
    string Label,
    SemanticRecordRefDto? Record,
    string? OwnerId);

public sealed record SemanticOwnershipEdgeDto(
    string SourceNodeId,
    string TargetNodeId,
    SemanticOwnershipEdgeKindDto Kind);

public sealed record SemanticOwnershipConflictDto(
    string ConflictId,
    string Label,
    SemanticImpactSeverityDto Severity,
    IReadOnlyList<string> NodeIds);

public sealed record QuerySemanticOwnershipResponse(
    SemanticProjectRevisionDto Revision,
    string QueryFingerprint,
    IReadOnlyList<SemanticOwnershipNodeDto> Nodes,
    IReadOnlyList<SemanticOwnershipEdgeDto> Edges,
    IReadOnlyList<SemanticOwnershipConflictDto> Conflicts,
    IReadOnlyList<SemanticProviderCoverageDto> Coverage,
    string? NextCursor);

public sealed record CompareExternalSemanticRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    SemanticSourceLayerKindDto Left,
    int Limit,
    string? ExternalRootPath = null,
    string? ComparedModInstanceId = null,
    SemanticRecordRefDto? Record = null,
    string? Cursor = null);

public sealed record QuerySemanticChangesRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    SemanticSourceLayerKindDto From,
    SemanticSourceLayerKindDto To,
    SemanticChangeFormatDto Format,
    int Limit,
    string? Cursor = null);

public sealed record SemanticChangeDto(
    string Path,
    SemanticRecordRefDto Record,
    string FieldKey,
    SemanticDifferenceKindDto Kind,
    SemanticScalarValueDto? Before,
    SemanticScalarValueDto? After,
    string Line);

public sealed record QuerySemanticChangesResponse(
    SemanticProjectRevisionDto Revision,
    string QueryFingerprint,
    IReadOnlyList<SemanticChangeDto> Items,
    IReadOnlyList<SemanticProviderCoverageDto> Coverage,
    string? NextCursor);

public enum BalanceLabStudyDto
{
    TrainerProgression,
    EncounterDistribution,
    MoveBalance,
    Economy,
    PokedexEvolution,
}

public enum BalanceLabFindingSeverityDto
{
    Info,
    Warning,
}

public sealed record BalanceLabStudyCapabilityDto(
    BalanceLabStudyDto Study,
    string ProviderId,
    SemanticCoverageStateDto State,
    SemanticConfidenceDto Confidence,
    string? ReasonCode);

public sealed record BalanceLabFactDto(
    string FactId,
    string Label,
    SemanticScalarValueDto Value,
    string? Unit,
    SemanticConfidenceDto Confidence,
    string ProviderId,
    IReadOnlyList<SemanticRecordRefDto> Evidence);

public sealed record BalanceLabChartPointDto(
    string PointId,
    string SeriesKey,
    string Label,
    SemanticRecordRefDto Record,
    IReadOnlyList<BalanceLabFactDto> Facts);

public sealed record BalanceLabFindingDto(
    string FindingId,
    string RuleId,
    BalanceLabFindingSeverityDto Severity,
    SemanticConfidenceDto Confidence,
    string Title,
    string Summary,
    SemanticRecordRefDto Record,
    IReadOnlyList<SemanticRecordRefDto> RelatedRecords,
    IReadOnlyList<BalanceLabFactDto> Facts);

public sealed record QueryBalanceLabRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    BalanceLabStudyDto Study,
    SemanticSourceLayerKindDto Layer,
    int Limit,
    string? Cursor = null);

public sealed record QueryBalanceLabResponse(
    SemanticProjectRevisionDto Revision,
    string QueryFingerprint,
    SemanticSourceSnapshotDto Snapshot,
    IReadOnlyList<BalanceLabStudyCapabilityDto> Capabilities,
    IReadOnlyList<BalanceLabChartPointDto> Points,
    IReadOnlyList<BalanceLabFindingDto> Findings,
    IReadOnlyList<ApiDiagnostic> Diagnostics,
    string? NextCursor);
