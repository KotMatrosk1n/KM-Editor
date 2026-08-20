// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Projects;

namespace KM.Api.Editing;

public sealed record StartEditSessionRequest(ProjectPathsDto Paths);

public sealed record StartEditSessionResponse(EditSessionDto Session);

public sealed record ValidateEditSessionRequest(
    ProjectPathsDto Paths,
    EditSessionDto Session);

public sealed record ValidateEditSessionResponse(
    EditSessionDto Session,
    bool IsValid,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record CreateChangePlanRequest(
    ProjectPathsDto Paths,
    EditSessionDto Session,
    ChangePlanOutputModeDto? OutputMode = null);

public sealed record CreateChangePlanResponse(ChangePlanDto ChangePlan);

public sealed record ApplyChangePlanRequest(
    ProjectPathsDto Paths,
    EditSessionDto Session,
    ChangePlanDto ChangePlan,
    ChangePlanOutputModeDto? OutputMode = null);

public sealed record ApplyChangePlanResponse(ApplyResultDto ApplyResult);

public sealed record EditSessionDto(
    string SessionId,
    bool HasPendingChanges,
    IReadOnlyList<PendingEditDto> PendingEdits,
    EditSessionAuthoringBindingDto? AuthoringBinding = null);

public sealed record EditSessionAuthoringBindingDto(
    int Version,
    string ProjectId,
    string WorkspaceETag,
    string WorkspaceFingerprint,
    IReadOnlyList<string> SelectedChangeSetIds,
    string? OutputProfileId,
    string OutputRootFingerprint,
    string? WorkspacePersonalStateETag = null,
    ChangePlanOutputModeDto? OutputMode = null);

public sealed record PendingEditDto(
    string Domain,
    string Summary,
    IReadOnlyList<FileProvenanceDto> Sources,
    string? RecordId = null,
    string? Field = null,
    string? NewValue = null,
    string? Owner = null,
    PendingEditAssociationDto? Association = null);

public sealed record PendingEditAssociationDto(
    int Version,
    string ChangeSetId,
    string OperationId);

public enum FileLayerDto
{
    Base,
    Layered,
    Pending,
    Generated,
}

public sealed record FileProvenanceDto(
    FileLayerDto Layer,
    string RelativePath);

public sealed record ChangePlanDto(
    string SessionId,
    bool CanApply,
    IReadOnlyList<PlannedFileWriteDto> Writes,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record PlannedFileWriteDto(
    string TargetRelativePath,
    IReadOnlyList<FileProvenanceDto> Sources,
    bool ReplacesExistingOutput,
    string Reason,
    string? SourceFingerprint = null);

public sealed record ApplyResultDto(
    string ApplyId,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<ApiDiagnostic> Diagnostics,
    OutputTransactionResultDto? OutputTransaction = null);

public enum OutputApplyOutcomeDto
{
    Committed,
    RolledBack,
    RecoveryRequired,
}

public sealed record OutputTransactionResultDto(
    string TransactionId,
    OutputApplyOutcomeDto Outcome,
    DateTimeOffset CompletedAtUtc,
    int TargetCount,
    string? OutcomeCode);

public enum ChangePlanOutputModeDto
{
    Standalone,
    TrinityModManager,
    TrinityBypass,
}
