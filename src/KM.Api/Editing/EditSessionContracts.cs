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
    IReadOnlyList<PendingEditDto> PendingEdits);

public sealed record PendingEditDto(
    string Domain,
    string Summary,
    IReadOnlyList<FileProvenanceDto> Sources,
    string? RecordId = null,
    string? Field = null,
    string? NewValue = null);

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
    string Reason);

public sealed record ApplyResultDto(
    string ApplyId,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public enum ChangePlanOutputModeDto
{
    Standalone,
    TrinityModManager,
}
