// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Semantics;
using System.Text.Json.Serialization;

namespace KM.Api.Research;

public static class ResearchLabContract
{
    private const int AuthoredProvisionMultiplier = 4;
    private const int AuthoredHardCeilingMultiplier = 2;

    public const int SchemaVersion = 1;
    public const int MaximumRegistrations = 4;
    public const int RequiredComparisonSources = 2;
    public const int RegistrationLifetimeMinutes = 30;
    public const long MaximumFileBytes = 64L * 1024L * 1024L;
    public const long MaximumAggregateBytes = 512L * 1024L * 1024L;
    public const int MaximumEntries = 200_000;
    public const int MaximumDirectories = 50_000;
    public const int MaximumTraversalDepth = 128;
    public const int MaximumSelectedFiles = 128;
    public const int MaximumRangesPerFile = 4_096;
    public const int MaximumAggregateRanges = 50_000;
    public const int MaximumPageSize = 100;
    public const int MaximumCursorLength = 2_048;
    public const int MaximumByteWindowLength = 4_096;
    public const int ResultProvisionMultiplier = 4;
    public const int ResultCacheCeilingMultiplier = 2;
    public const long ExpectedResultSizeBytes = 32L * 1024L * 1024L;
    public const long MaximumResultSizeBytes = checked(
        ExpectedResultSizeBytes * ResultProvisionMultiplier);
    public const long MaximumResultCeilingBytes = checked(
        MaximumResultSizeBytes * ResultCacheCeilingMultiplier);
    public const long MaximumResultCacheBytes = MaximumResultCeilingBytes;
    public const int MaximumRelativePathLength = 4_096;
    public const int MaximumAnnotationCount = 2_048;
    public const int MaximumAnnotationTextLength = 8_192;
    public const int MaximumAnnotationTags = 32;
    public const int ExpectedSerializedAnnotationDocumentBytes = 3 * 1024 * 1024;
    public const int ProvisionedSerializedAnnotationDocumentBytes = checked(
        ExpectedSerializedAnnotationDocumentBytes * AuthoredProvisionMultiplier);
    public const int MaximumSerializedAnnotationDocumentBytes = checked(
        ProvisionedSerializedAnnotationDocumentBytes * AuthoredHardCeilingMultiplier);
    public const int MaximumExtensionDescriptors = 64;
}

public enum ResearchFeatureDto
{
    SourceComparison,
    ByteWindows,
    SemanticProjection,
    Annotations,
    OwnershipEvidence,
    ReadOnlyExtensions,
    WritableExtensions,
}

public enum ResearchExtensionKindDto
{
    HostRegistered,
    DeclarativeData,
}

public enum ResearchFileDifferenceKindDto
{
    Added,
    Removed,
    Changed,
}

public enum ResearchRangeCoverageDto
{
    NotRequested,
    Complete,
    Truncated,
}

public enum ResearchAnnotationTargetKindDto
{
    SemanticRecord,
    RelativeRange,
    Finding,
}

public enum ResearchAnnotationMutationKindDto
{
    Upsert,
    Delete,
}

public sealed record ResearchCapabilityDto(
    ResearchFeatureDto Feature,
    bool CanUse,
    SemanticCoverageStateDto Coverage,
    SemanticConfidenceDto Confidence,
    string? ReasonCode);

public sealed record ResearchExtensionDescriptorDto(
    string ExtensionId,
    ResearchExtensionKindDto Kind,
    int SchemaVersion,
    IReadOnlyList<ResearchFeatureDto> Features,
    IReadOnlyList<SemanticGameFamilyDto> GameFamilies,
    SemanticCoverageStateDto Coverage,
    SemanticConfidenceDto Confidence,
    string? ReasonCode);

public sealed record ResearchLimitsDto(
    int MaximumRegistrations,
    int RequiredComparisonSources,
    int RegistrationLifetimeMinutes,
    long MaximumFileBytes,
    long MaximumAggregateBytes,
    int MaximumEntries,
    int MaximumDirectories,
    int MaximumTraversalDepth,
    int MaximumSelectedFiles,
    int MaximumRangesPerFile,
    int MaximumAggregateRanges,
    int MaximumPageSize,
    int MaximumCursorLength,
    int MaximumByteWindowLength,
    long MaximumResultCacheBytes);

public sealed record ReadResearchLabCapabilitiesRequest(SemanticExploreScopeDto Scope);

public sealed record ReadResearchLabCapabilitiesResponse(
    SemanticProjectRevisionDto Revision,
    IReadOnlyList<SemanticSourceSnapshotDto> Snapshots,
    IReadOnlyList<ResearchCapabilityDto> Capabilities,
    IReadOnlyList<ResearchExtensionDescriptorDto> Extensions,
    ResearchLimitsDto Limits);

public sealed record OpenResearchSourceRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    string RootPath,
    string? ReplaceSourceId = null);

public sealed record OpenResearchSourceResponse(
    SemanticProjectRevisionDto Revision,
    string SourceId,
    DateTimeOffset ExpiresAtUtc);

public sealed record CloseResearchSourceRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    string SourceId);

public sealed record CloseResearchSourceResponse(
    SemanticProjectRevisionDto Revision,
    string SourceId,
    bool Closed);

public sealed record CompareResearchSourcesRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    IReadOnlyList<string> SourceIds,
    IReadOnlyList<string> SelectedRelativePaths,
    int Limit,
    string? Cursor = null);

public sealed record ResearchSourceSnapshotDto(
    string SourceId,
    string Fingerprint,
    int FileCount,
    int DirectoryCount,
    long TotalBytes);

public sealed record ResearchFileSideDto(
    bool Exists,
    long? Length,
    string? ContentSha256);

public sealed record ResearchByteRangeDto(long Offset, int Length);

public sealed record ResearchOwnershipEvidenceDto(
    SemanticCoverageStateDto Coverage,
    SemanticConfidenceDto Confidence,
    string? OwnerId,
    string? ReasonCode);

public sealed record ResearchFileFindingDto(
    string FindingId,
    string RelativePath,
    ResearchFileDifferenceKindDto DifferenceKind,
    ResearchFileSideDto SourceA,
    ResearchFileSideDto SourceB,
    IReadOnlyList<ResearchByteRangeDto> Ranges,
    ResearchRangeCoverageDto RangeCoverage,
    ResearchOwnershipEvidenceDto Ownership);

public sealed record CompareResearchSourcesResponse(
    SemanticProjectRevisionDto Revision,
    string QueryFingerprint,
    string ComparisonId,
    string ComparisonFingerprint,
    IReadOnlyList<ResearchSourceSnapshotDto> Sources,
    IReadOnlyList<ResearchFileFindingDto> Items,
    ResearchCapabilityDto SemanticProjection,
    string? NextCursor);

public sealed record ReadResearchByteWindowRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    string ComparisonId,
    string ExpectedComparisonFingerprint,
    string RelativePath,
    long Offset,
    int Length);

public sealed record ResearchByteWindowSideDto(
    bool Exists,
    long? FileLength,
    string? BytesBase64,
    string? WindowSha256);

public sealed record ReadResearchByteWindowResponse(
    SemanticProjectRevisionDto Revision,
    string ComparisonFingerprint,
    string RelativePath,
    long Offset,
    int RequestedLength,
    ResearchByteWindowSideDto SourceA,
    ResearchByteWindowSideDto SourceB);

public sealed record ResearchRelativeRangeRefDto(
    string ComparisonFingerprint,
    string RelativePath,
    long Offset,
    int Length);

public sealed record ResearchFindingRefDto(
    string ComparisonFingerprint,
    string FindingId,
    string RelativePath);

public sealed record ResearchAnnotationTargetDto(
    ResearchAnnotationTargetKindDto Kind,
    SemanticProjectRevisionDto Revision,
    SemanticSourceSnapshotDto? SemanticSnapshot,
    SemanticRecordRefDto? SemanticRecord,
    ResearchRelativeRangeRefDto? RelativeRange,
    ResearchFindingRefDto? Finding);

public sealed record ResearchAnnotationDto(
    string AnnotationId,
    ResearchAnnotationTargetDto Target,
    string Text,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ResearchAnnotationDocumentDto(
    int SchemaVersion,
    IReadOnlyList<ResearchAnnotationDto> Annotations,
    DateTimeOffset UpdatedAtUtc);

public sealed record ReadResearchAnnotationsRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision);

public sealed record ReadResearchAnnotationsResponse(
    SemanticProjectRevisionDto Revision,
    bool Exists,
    ResearchAnnotationDocumentDto? Document,
    [property: JsonPropertyName("etag")] string? ETag);

public sealed record ResearchAnnotationDraftDto(
    string? AnnotationId,
    ResearchAnnotationTargetDto Target,
    string Text,
    IReadOnlyList<string> Tags);

public sealed record ResearchAnnotationMutationDto(
    ResearchAnnotationMutationKindDto Kind,
    string? AnnotationId,
    ResearchAnnotationDraftDto? Upsert);

public sealed record MutateResearchAnnotationsRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    string? ExpectedETag,
    ResearchAnnotationMutationDto Mutation);

public sealed record MutateResearchAnnotationsResponse(
    SemanticProjectRevisionDto Revision,
    ResearchAnnotationDocumentDto Document,
    DateTimeOffset WrittenAtUtc,
    [property: JsonPropertyName("etag")] string ETag);
