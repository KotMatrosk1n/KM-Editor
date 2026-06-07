// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;

namespace KM.Api.Editing;

public sealed record EditSessionDto(
    string SessionId,
    bool HasPendingChanges,
    IReadOnlyList<PendingEditDto> PendingEdits);

public sealed record PendingEditDto(
    string Domain,
    string Summary,
    IReadOnlyList<FileProvenanceDto> Sources);

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
    bool ReplacesExistingOutput,
    string Reason);

public sealed record ApplyResultDto(
    string ApplyId,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
