// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;

namespace KM.Api.Output;

public static class OutputSafetyContract
{
    public const int MaximumReturnedEntries = 500;
    public const int MaximumReturnedRecoveryUnknownTargets = 500;
    public const int MaximumRecoveryUnknownTargetUtf8Bytes = 2 * 1024 * 1024;
    public const int MaximumRequestedTargetIds = 512;
    public const int MaximumHistoryPageSize = 100;
    public const int MaximumCheckpointLabelLength = 256;
}

public sealed record OutputScopeDto(string ProjectId, ProjectPathsDto Paths);

public enum OutputTransactionPhaseDto
{
    Preparing,
    Prepared,
    Committing,
    Committed,
    RollingBack,
    RolledBack,
    RecoveryRequired,
    Finalizing,
}

public enum OutputRecoveryDispositionDto
{
    NoAction,
    FinalizeCommit,
    RollBack,
    RecoveryRequired,
}

public sealed record OutputRecoveryTransactionDto(
    string TransactionId,
    OutputTransactionPhaseDto Phase,
    OutputRecoveryDispositionDto Disposition,
    bool JournalReadable,
    int UnknownTargetCount,
    IReadOnlyList<string> UnknownTargets,
    bool UnknownTargetsTruncated);

public sealed record OutputRecoveryStatusDto(
    string Revision,
    bool RequiresRecovery,
    int TransactionCount,
    int PendingReconciliationCount,
    IReadOnlyList<OutputRecoveryTransactionDto> Transactions,
    bool TransactionsTruncated,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record GetOutputRecoveryStatusRequest(OutputScopeDto Scope);

public sealed record GetOutputRecoveryStatusResponse(OutputRecoveryStatusDto Status);

public sealed record ReconcileOutputRecoveryRequest(OutputScopeDto Scope, string ExpectedRevision);

public sealed record ReconcileOutputRecoveryResponse(OutputRecoveryStatusDto Status, int ReconciledCount);

public enum OutputIntegrityClassificationDto
{
    BaseEquivalent,
    KmOwnedCurrent,
    KmOwnedStale,
    Foreign,
    Conflicted,
    Interrupted,
    Unknown,
}

public sealed record OutputIntegrityCountsDto(
    int BaseEquivalent,
    int KmOwnedCurrent,
    int KmOwnedStale,
    int Foreign,
    int Conflicted,
    int Interrupted,
    int Unknown);

public sealed record OutputIntegrityEntryDto(
    string TargetId,
    string RelativePath,
    OutputIntegrityClassificationDto Classification,
    bool CleanupEligible,
    string? SizeBytes,
    IReadOnlyList<string> OwnerIds);

public sealed record ScanOutputIntegrityRequest(OutputScopeDto Scope);

public sealed record ScanOutputIntegrityResponse(
    string ScanId,
    string Revision,
    DateTimeOffset ScannedAtUtc,
    OutputIntegrityCountsDto Counts,
    IReadOnlyList<OutputIntegrityEntryDto> Entries,
    bool Truncated,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record PreviewOutputCleanupRequest(
    OutputScopeDto Scope,
    string ScanId,
    string IntegrityRevision,
    IReadOnlyList<string> TargetIds);

public sealed record OutputCleanupCandidateDto(string TargetId, string RelativePath, string? SizeBytes);

public sealed record PreviewOutputCleanupResponse(
    string PlanId,
    string ExpectedRevision,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<OutputCleanupCandidateDto> Candidates,
    string TotalBytes,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record ApplyOutputCleanupRequest(OutputScopeDto Scope, string PlanId, string ExpectedRevision);

public enum OutputCleanupDispositionDto
{
    Removed,
    NotOwned,
    FingerprintMismatch,
    Missing,
    ApplyNotCommitted,
    ForgotMissing,
}

public sealed record OutputCleanupEntryDto(
    string TargetId,
    string RelativePath,
    OutputCleanupDispositionDto Disposition);

public sealed record ApplyOutputCleanupResponse(
    int RemovedCount,
    int SkippedCount,
    IReadOnlyList<OutputCleanupEntryDto> Entries,
    OutputTransactionResultDto? OutputTransaction,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record ListOutputHistoryRequest(OutputScopeDto Scope, string? Cursor = null, int Limit = 20);

public sealed record OutputApplyOriginDto(string Kind, string Id);

public sealed record OutputHistoryReceiptDto(
    string TransactionId,
    OutputApplyOutcomeDto Outcome,
    DateTimeOffset CompletedAtUtc,
    string OutputMode,
    string SemanticReviewHash,
    int TargetCount,
    IReadOnlyList<OutputApplyOriginDto> Origins,
    string? OutcomeCode);

public sealed record ListOutputHistoryResponse(
    IReadOnlyList<OutputHistoryReceiptDto> Receipts,
    string? NextCursor,
    bool Truncated);

public enum OutputCheckpointCoverageDto
{
    FullOutput,
    KmOwnedOnly,
}

public sealed record OutputCheckpointDto(
    string CheckpointId,
    DateTimeOffset CreatedAtUtc,
    string? Label,
    int FileCount,
    string TotalBytes,
    string ManifestFingerprint,
    string OutputMode,
    OutputCheckpointCoverageDto Coverage);

public sealed record ListOutputCheckpointsRequest(OutputScopeDto Scope);

public sealed record ListOutputCheckpointsResponse(
    string Revision,
    string OutputRevision,
    IReadOnlyList<OutputCheckpointDto> Checkpoints);

public sealed record CreateOutputCheckpointRequest(
    OutputScopeDto Scope,
    string ExpectedOutputRevision,
    string? Label = null);

public sealed record CreateOutputCheckpointResponse(
    string Revision,
    string OutputRevision,
    OutputCheckpointDto Checkpoint,
    IReadOnlyList<OutputCheckpointDto> Checkpoints);

public sealed record PreviewOutputCheckpointRestoreRequest(
    OutputScopeDto Scope,
    string CheckpointId,
    string ManifestFingerprint);

public sealed record PreviewOutputCheckpointRestoreResponse(
    string PlanId,
    bool CanRestore,
    int TargetCount,
    string TotalBytes,
    IReadOnlyList<string> Targets,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record RestoreOutputCheckpointRequest(OutputScopeDto Scope, string PlanId);

public sealed record RestoreOutputCheckpointResponse(
    OutputTransactionResultDto OutputTransaction,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record DeleteOutputCheckpointRequest(
    OutputScopeDto Scope,
    string CheckpointId,
    string ManifestFingerprint,
    string ExpectedRevision);

public sealed record DeleteOutputCheckpointResponse(bool Deleted, string Revision);

public sealed record BuildSupportReportRequest(OutputScopeDto Scope);

public sealed record OutputIntegrityCountDto(OutputIntegrityClassificationDto Classification, int Count);

public sealed record OutputSupportReportDto(
    int SchemaVersion,
    string ApplicationVersion,
    string GameFamily,
    string OutputMode,
    IReadOnlyList<string> DiagnosticCodes,
    IReadOnlyList<OutputTransactionPhaseDto> TransactionPhases,
    IReadOnlyList<OutputIntegrityCountDto> IntegrityCounts,
    int OwnershipFileCount,
    int CheckpointCount,
    int HistoryReceiptCount,
    DateTimeOffset CreatedAtUtc);

public sealed record BuildSupportReportResponse(OutputSupportReportDto Report);

public enum ProjectRelocationDocumentStatusDto
{
    Copy,
    Skip,
    Conflict,
}

public sealed record ProjectRelocationRoleDto(ProjectPathRoleDto Role, ProjectPathStatusDto Status);

public sealed record ProjectRelocationDocumentDto(string DocumentId, ProjectRelocationDocumentStatusDto Status);

public sealed record PreviewProjectRelocationRequest(OutputScopeDto Source, ProjectPathsDto CandidatePaths);

public sealed record PreviewProjectRelocationResponse(
    string ReviewToken,
    string SourceProjectId,
    string? DestinationProjectId,
    bool CanApply,
    IReadOnlyList<ProjectRelocationRoleDto> Roles,
    IReadOnlyList<ProjectRelocationDocumentDto> WorkspaceDocuments,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record ApplyProjectRelocationRequest(
    OutputScopeDto Source,
    ProjectPathsDto CandidatePaths,
    string ReviewToken);

public sealed record ApplyProjectRelocationResponse(
    string ProjectId,
    ProjectHealthDto Health,
    IReadOnlyList<string> MigratedDocumentIds,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
