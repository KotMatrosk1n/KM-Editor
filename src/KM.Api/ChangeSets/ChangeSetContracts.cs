// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Serialization;
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;

namespace KM.Api.ChangeSets;

public static class ChangeSetContract
{
    private const int ProvisionMultiplier = 4;
    private const int HardCeilingMultiplier = 2;

    public const int SchemaVersion = 1;
    public const int AssociationVersion = 1;
    public const int PortableSchemaVersion = 1;
    public const int MaximumChangeSetCount = 64;
    public const int ExpectedOperationCount = 768;
    public const int ProvisionedOperationCount = checked(
        ExpectedOperationCount * ProvisionMultiplier);
    public const int MaximumOperationCount = checked(
        ProvisionedOperationCount * HardCeilingMultiplier);
    public const int MaximumOperationsPerChangeSet = MaximumOperationCount;
    public const int MaximumBuildVariantCount = 32;
    public const int MaximumHistoryCount = 16;
    public const int MaximumTagCount = 32;
    public const int MaximumDependencyCount = 32;
    public const int ExpectedSerializedDocumentBytes = 3 * 1024 * 1024;
    public const int ProvisionedSerializedDocumentBytes = checked(
        ExpectedSerializedDocumentBytes * ProvisionMultiplier);
    public const int MaximumSerializedDocumentBytes = checked(
        ProvisionedSerializedDocumentBytes * HardCeilingMultiplier);
    public const int ExpectedPortablePackageBytes = 4 * 1024 * 1024;
    public const int ProvisionedPortablePackageBytes = checked(
        ExpectedPortablePackageBytes * ProvisionMultiplier);
    public const int MaximumPortablePackageBytes = checked(
        ProvisionedPortablePackageBytes * HardCeilingMultiplier);
}

public sealed record ChangeSetWorkspaceScopeDto(
    string ProjectId,
    ProjectPathsDto Paths);

public enum ChangeSetOperationStorageKindDto
{
    LegacyPendingEdit,
}

public enum ChangeSetSourceBindingKindDto
{
    ReviewedPlan,
    LegacyUnsupported,
}

public sealed record ChangeSetOperationDto(
    string OperationId,
    ChangeSetOperationStorageKindDto Kind,
    PendingEditDto PendingEdit,
    ChangeSetSourceBindingKindDto SourceBindingKind,
    string? SourceFingerprint,
    IReadOnlyList<string> OwnedTargets,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record NamedChangeSetDto(
    string ChangeSetId,
    string Name,
    bool Enabled,
    bool Archived,
    string? Notes,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> DependencyIds,
    IReadOnlyList<ChangeSetOperationDto> Operations,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ChangeSetMetadataDto(
    string Name,
    bool Enabled,
    bool Archived,
    string? Notes,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> DependencyIds);

public sealed record ChangeSetBuildVariantDto(
    string VariantId,
    string Name,
    IReadOnlyList<string> ChangeSetIds,
    string? OutputProfileId,
    ChangePlanOutputModeDto? OutputMode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ChangeSetWorkspaceDocumentDto(
    int SchemaVersion,
    ProjectGameDto Game,
    IReadOnlyList<NamedChangeSetDto> ChangeSets,
    string? ActiveChangeSetId,
    IReadOnlyList<ChangeSetBuildVariantDto> BuildVariants,
    string? ActiveBuildVariantId,
    DateTimeOffset UpdatedAtUtc);

public enum ChangeSetMutationKindDto
{
    CreateSet,
    UpdateSet,
    DeleteSet,
    DuplicateSet,
    ReorderSets,
    ReorderOperations,
    RemoveOperation,
    SetActiveSet,
    CreateVariant,
    UpdateVariant,
    DeleteVariant,
    SetActiveVariant,
    Undo,
    Redo,
}

public sealed record ChangeSetWorkspaceMutationDto(
    ChangeSetMutationKindDto Kind,
    string? ChangeSetId = null,
    string? Name = null,
    ChangeSetMetadataDto? Metadata = null,
    IReadOnlyList<string>? OrderedIds = null,
    string? OperationId = null,
    ChangeSetBuildVariantDto? Variant = null,
    string? VariantId = null);

public enum ChangeSetConflictKindDto
{
    SemanticTarget,
    OwnedOutput,
    MissingDependency,
    DisabledDependency,
    DependencyCycle,
    DependencyOrder,
    SessionTarget,
}

public sealed record ChangeSetConflictDto(
    ChangeSetConflictKindDto Kind,
    string Message,
    IReadOnlyList<string> ChangeSetIds,
    IReadOnlyList<string> OperationIds,
    string? Target);

public enum ChangeSetOperationMaterializationStateDto
{
    Fresh,
    Stale,
    LegacyUnsupported,
    Conflict,
    SessionLocal,
}

public sealed record ChangeSetOperationSummaryDto(
    string OperationId,
    string? ChangeSetId,
    string? ChangeSetName,
    string Title,
    string Target,
    string Description,
    ChangeSetOperationMaterializationStateDto State);

public sealed record ChangeSetMaterializationDto(
    bool CanMaterialize,
    string WorkspaceFingerprint,
    string SourceRevisionFingerprint,
    IReadOnlyList<string> SelectedChangeSetIds,
    string? OutputProfileId,
    ChangePlanOutputModeDto? OutputMode,
    EditSessionDto? Session,
    ChangePlanDto? ChangePlan,
    IReadOnlyList<ChangeSetOperationSummaryDto> Operations,
    IReadOnlyList<ChangeSetConflictDto> Conflicts,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record ChangeSetWorkspaceSnapshotDto(
    ChangeSetWorkspaceDocumentDto Document,
    [property: JsonPropertyName("etag")]
    string? ETag,
    bool CanUndo,
    bool CanRedo,
    string? UndoLabel,
    string? RedoLabel,
    ChangeSetMaterializationDto Effective);

public sealed record ReadChangeSetWorkspaceRequest(
    ChangeSetWorkspaceScopeDto Scope,
    EditSessionDto? Session = null);

public sealed record MutateChangeSetWorkspaceRequest(
    ChangeSetWorkspaceScopeDto Scope,
    string? ExpectedETag,
    ChangeSetWorkspaceMutationDto Mutation,
    EditSessionDto? Session = null);

public sealed record CaptureChangeSetSessionRequest(
    ChangeSetWorkspaceScopeDto Scope,
    string ChangeSetId,
    EditSessionDto? PreviousSession,
    EditSessionDto StagedSession,
    string ExpectedETag);

public sealed record CaptureChangeSetSessionResponse(
    ChangeSetWorkspaceSnapshotDto Snapshot,
    EditSessionDto StagedSession,
    IReadOnlyList<string> CapturedOperationIds,
    IReadOnlyList<string> RemovedOperationIds);

public sealed record MaterializeChangeSetWorkspaceRequest(
    ChangeSetWorkspaceScopeDto Scope,
    string ExpectedETag,
    EditSessionDto? Session = null,
    string? BuildVariantId = null);

public sealed record ExportChangeSetsRequest(
    ChangeSetWorkspaceScopeDto Scope,
    IReadOnlyList<string> ChangeSetIds,
    string ExpectedETag);

public sealed record ExportChangeSetsResponse(
    bool Available,
    string? PackageJson,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record ImportChangeSetsRequest(
    ChangeSetWorkspaceScopeDto Scope,
    string PackageJson,
    bool EnableImported,
    string? ExpectedETag,
    EditSessionDto? Session = null);

public sealed record ImportChangeSetsResponse(ChangeSetWorkspaceSnapshotDto Snapshot);

public sealed record PortableChangeSetPackageDto(
    int SchemaVersion,
    ProjectGameDto Game,
    IReadOnlyList<PortableNamedChangeSetDto> ChangeSets);

public sealed record PortableNamedChangeSetDto(
    string PortableId,
    string Name,
    string? Notes,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> DependencyIds,
    IReadOnlyList<PortableChangeSetOperationDto> Operations);

/// <summary>
/// Versioned portable semantic operation envelope. Adapter payloads are JSON so
/// unsupported adapters can be rejected without weakening the package boundary.
/// </summary>
public sealed record PortableChangeSetOperationDto(
    string AdapterId,
    int AdapterSchemaVersion,
    string SourceFingerprint,
    string PayloadJson);

/// <summary>
/// Strict portable form of a planner-reviewed, field-addressable pending edit.
/// Authoring association, workflow owner, absolute paths, and output roots are
/// deliberately excluded.
/// </summary>
public sealed record PortablePendingEditOperationPayloadDto(
    string Domain,
    string Summary,
    IReadOnlyList<FileProvenanceDto> Sources,
    string RecordId,
    string Field,
    string NewValue,
    IReadOnlyList<string> OwnedTargets);
