// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Serialization;
using KM.Api.ChangeSets;
using KM.Api.Diagnostics;
using KM.Api.Projects;
using KM.Api.Semantics;

namespace KM.Api.SemanticMerging;

public static class SemanticMergeContract
{
    public const int SchemaVersion = 1;
    public const int MaximumPageSize = 100;
    public const int MaximumCursorLength = 2_048;
    public const int MaximumExternalRootLength = 4_096;
    public const int MaximumTargets = 128;
    public const int MaximumDomainsPerProposal = 1;
    public const int MaximumResolutions = MaximumTargets * MaximumConflictsPerRow;
    public const int MaximumIndexedRows = 50_000;
    public const int MaximumTargetSelectionWindow = 500;
    public const int MaximumTargetSearchTextLength = 256;
    public const int MaximumConflictsPerRow = 3;
    public const int MaximumReportedConflicts =
        MaximumTargetSelectionWindow * MaximumConflictsPerRow;
    public const int MaximumDiagnostics = 100;
    public const int MaximumChangeSetNameLength = 128;
    public const int MaximumRecipeBytes = 2 * 1_024 * 1_024;
    public const int MaximumRecipeOperations = 128;
    public const int MaximumRecipeSteps = 32;
    public const int MaximumRecipeDependencies = 32;
    public const int MaximumRecipeNameLength = 128;
    public const int MaximumRecipeNotesLength = 4_096;
    public const int MaximumRecipeSeedLength = 128;
}

public enum SemanticMergeFeatureDto
{
    ThreeWayScalarMerge,
    FocusedConflictResolution,
    StableCollectionMerge,
    OpaqueFileFallback,
    RecipeImport,
    RecipeExport,
    CompatibilityReport,
    SeededReproducibility,
    HeadlessAutomation,
}

public enum SemanticMergeConflictKindDto
{
    SameField,
    CurrentTarget,
    PendingTarget,
    DeleteVsEdit,
    Reorder,
    IncompatibleLayout,
    Ownership,
}

public enum SemanticMergeConflictChoiceDto
{
    SourceA,
    SourceB,
    Base,
    KeepCurrent,
}

public enum SemanticMergeRowStateDto
{
    AutoMerged,
    Conflict,
    AlreadyCurrent,
    Unsupported,
}

public enum SemanticMergeFallbackKindDto
{
    None,
    LegacyWorkflowOnly,
    Unavailable,
}

public enum SemanticMergeFallbackTargetDto
{
    LegacyModMerger,
}

public enum KmRecipeCompatibilityStateDto
{
    Compatible,
    AlreadyApplied,
    Conflict,
    Unsupported,
}

public sealed record SemanticMergeDomainCapabilityDto(
    string Domain,
    string RecordKind,
    IReadOnlyList<string> FieldKeys);

public sealed record SemanticMergeCapabilityDto(
    SemanticMergeFeatureDto Feature,
    string ProviderId,
    SemanticCoverageStateDto State,
    SemanticConfidenceDto Confidence,
    string? ReasonCode,
    IReadOnlyList<SemanticMergeDomainCapabilityDto> Domains);

public sealed record ReadSemanticMergeCapabilitiesRequest(SemanticExploreScopeDto Scope);

public sealed record ReadSemanticMergeCapabilitiesResponse(
    SemanticProjectRevisionDto Revision,
    IReadOnlyList<SemanticSourceSnapshotDto> Snapshots,
    IReadOnlyList<SemanticMergeCapabilityDto> Capabilities,
    bool CanOpenLegacyMerger);

public sealed record OpenSemanticMergeSourceRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    string ExternalRootPath);

public sealed record SemanticMergeSourceDto(
    string InstanceId,
    SemanticSourceSnapshotDto Snapshot,
    IReadOnlyList<SemanticProviderCoverageDto> Coverage);

public sealed record OpenSemanticMergeSourceResponse(
    SemanticProjectRevisionDto Revision,
    SemanticMergeSourceDto Source);

public sealed record SemanticMergeFieldRefDto(
    SemanticRecordRefDto Record,
    string FieldKey);

public sealed record SemanticMergeConflictResolutionDto(
    string ConflictId,
    SemanticMergeConflictChoiceDto Choice);

public sealed record SemanticMergeConflictDto(
    string ConflictId,
    SemanticMergeConflictKindDto Kind,
    IReadOnlyList<SemanticMergeConflictChoiceDto> AllowedChoices,
    SemanticMergeConflictChoiceDto? SelectedChoice,
    string ReasonCode);

public sealed record SemanticMergeFallbackActionDto(
    SemanticMergeFallbackKindDto Kind,
    SemanticMergeFallbackTargetDto? Target,
    bool Available,
    string? ReasonCode);

public sealed record SemanticMergeRowDto(
    string RowId,
    SemanticMergeFieldRefDto Target,
    string RecordLabel,
    string FieldLabel,
    SemanticMergeRowStateDto State,
    SemanticScalarValueDto? BaseValue,
    SemanticScalarValueDto? SourceAValue,
    SemanticScalarValueDto? SourceBValue,
    SemanticScalarValueDto? CurrentValue,
    SemanticScalarValueDto? PendingValue,
    SemanticScalarValueDto? ResultValue,
    string ProviderId,
    SemanticCoverageStateDto Coverage,
    SemanticConfidenceDto Confidence,
    IReadOnlyList<SemanticMergeConflictDto> Conflicts,
    SemanticMergeFallbackActionDto Fallback,
    bool Selected);

public sealed record PreviewSemanticMergeRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    string? ExpectedChangeSetETag,
    string SourceAInstanceId,
    string SourceBInstanceId,
    IReadOnlyList<SemanticMergeFieldRefDto> Targets,
    IReadOnlyList<SemanticMergeConflictResolutionDto> Resolutions,
    string? TargetSearchText,
    int Limit,
    string? Cursor = null,
    string? ProposalId = null,
    string? ProposalFingerprint = null);

public sealed record PreviewSemanticMergeResponse(
    SemanticProjectRevisionDto Revision,
    string QueryFingerprint,
    SemanticSourceSnapshotDto BaseSnapshot,
    SemanticSourceSnapshotDto LayeredSnapshot,
    SemanticSourceSnapshotDto PendingSnapshot,
    SemanticSourceSnapshotDto SourceASnapshot,
    SemanticSourceSnapshotDto SourceBSnapshot,
    IReadOnlyList<SemanticMergeCapabilityDto> Capabilities,
    IReadOnlyList<SemanticMergeFieldRefDto> NormalizedTargets,
    IReadOnlyList<SemanticMergeConflictResolutionDto> NormalizedResolutions,
    string AuthoringContextFingerprint,
    string ProposalId,
    string ProposalFingerprint,
    bool CanImport,
    bool SelectionRequired,
    string? NormalizedTargetSearchText,
    bool TargetWindowCapped,
    int TotalMatchingTargetCount,
    int TotalRowCount,
    int TotalConflictCount,
    int TotalMutationCount,
    IReadOnlyList<SemanticMergeRowDto> Rows,
    IReadOnlyList<ApiDiagnostic> Diagnostics,
    string? NextCursor);

public sealed record ImportSemanticMergeRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    string SourceAInstanceId,
    string SourceBInstanceId,
    IReadOnlyList<SemanticMergeFieldRefDto> Targets,
    IReadOnlyList<SemanticMergeConflictResolutionDto> Resolutions,
    string ProposalId,
    string ProposalFingerprint,
    string ChangeSetName,
    string? ExpectedChangeSetETag);

public sealed record SemanticMergeDisabledImportReceiptDto(
    ChangeSetWorkspaceDocumentDto Document,
    [property: JsonPropertyName("etag")]
    string ETag,
    bool CanUndo,
    bool CanRedo,
    string? UndoLabel,
    string? RedoLabel);

public sealed record ImportSemanticMergeResponse(
    SemanticProjectRevisionDto Revision,
    string ProposalId,
    string ProposalFingerprint,
    string ImportedChangeSetId,
    SemanticMergeDisabledImportReceiptDto Receipt,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record KmRecipeScalarDto(
    SemanticValueKindDto Kind,
    string CanonicalValue);

public sealed record KmRecipeMetadataDto(
    string Name,
    string? Notes,
    string? Seed);

public sealed record KmRecipeOperationDto(
    string OperationId,
    SemanticRecordRefDto Record,
    string FieldKey,
    KmRecipeScalarDto ExpectedBaseValue,
    KmRecipeScalarDto ExpectedCurrentValue,
    KmRecipeScalarDto AfterValue,
    string ProviderId);

public sealed record KmRecipeStepDto(
    string StepId,
    int Order,
    IReadOnlyList<string> DependencyStepIds,
    IReadOnlyList<KmRecipeOperationDto> Operations);

public sealed record KmRecipePackageDto(
    int SchemaVersion,
    ProjectGameDto Game,
    string ProviderSchema,
    string SourceCompatibilityFingerprint,
    KmRecipeMetadataDto Metadata,
    IReadOnlyList<KmRecipeStepDto> Steps);

public sealed record KmRecipeArtifactDto(
    int SchemaVersion,
    string MediaType,
    string SuggestedFileName,
    string Sha256,
    string Content);

public sealed record ExportKmRecipeRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    string ExpectedChangeSetETag,
    IReadOnlyList<string> SelectedChangeSetIds,
    string Name,
    string? Notes,
    string? Seed);

public sealed record ExportKmRecipeResponse(
    SemanticProjectRevisionDto Revision,
    string RecipeFingerprint,
    int SelectedChangeSetCount,
    int TotalOperationCount,
    KmRecipeArtifactDto Artifact,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record ValidateKmRecipeRequest(string Content);

public sealed record ValidateKmRecipeResponse(
    string RecipeInstanceId,
    string RecipeFingerprint,
    ProjectGameDto Game,
    KmRecipeMetadataDto Metadata,
    int TotalStepCount,
    int TotalOperationCount,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record KmRecipeCompatibilityRowDto(
    string RowId,
    SemanticMergeFieldRefDto Target,
    KmRecipeCompatibilityStateDto State,
    KmRecipeScalarDto ExpectedBaseValue,
    KmRecipeScalarDto ExpectedCurrentValue,
    SemanticScalarValueDto? ActualBaseValue,
    SemanticScalarValueDto? CurrentValue,
    SemanticScalarValueDto? PendingValue,
    KmRecipeScalarDto AfterValue,
    string ProviderId,
    string? ReasonCode);

public sealed record PreviewKmRecipeRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    string? ExpectedChangeSetETag,
    string RecipeInstanceId,
    string RecipeFingerprint,
    int Limit,
    string? Cursor = null,
    string? ProposalId = null,
    string? ProposalFingerprint = null);

public sealed record PreviewKmRecipeResponse(
    SemanticProjectRevisionDto Revision,
    string QueryFingerprint,
    SemanticSourceSnapshotDto BaseSnapshot,
    SemanticSourceSnapshotDto LayeredSnapshot,
    SemanticSourceSnapshotDto PendingSnapshot,
    KmRecipeMetadataDto Metadata,
    string RecipeInstanceId,
    string RecipeFingerprint,
    string AuthoringContextFingerprint,
    string ProposalId,
    string ProposalFingerprint,
    bool CanImport,
    int TotalCompatibilityCount,
    int TotalMutationCount,
    IReadOnlyList<KmRecipeCompatibilityRowDto> Compatibility,
    IReadOnlyList<ApiDiagnostic> Diagnostics,
    string? NextCursor);

public sealed record ImportKmRecipeRequest(
    SemanticExploreScopeDto Scope,
    SemanticProjectRevisionDto ExpectedRevision,
    string RecipeInstanceId,
    string RecipeFingerprint,
    string ProposalId,
    string ProposalFingerprint,
    string ChangeSetName,
    string? ExpectedChangeSetETag);

public sealed record ImportKmRecipeResponse(
    SemanticProjectRevisionDto Revision,
    string RecipeInstanceId,
    string RecipeFingerprint,
    string ProposalId,
    string ProposalFingerprint,
    string ImportedChangeSetId,
    SemanticMergeDisabledImportReceiptDto Receipt,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
