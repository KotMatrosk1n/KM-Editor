// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.ExeFs;

public sealed record LoadExeFsPatchWorkflowRequest(ProjectPathsDto Paths);

public sealed record ExeFsPatchProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record ExeFsPatchRecordDto(
    string PatchId,
    string Name,
    string TargetFile,
    string PatchKind,
    string Status,
    string Description,
    ExeFsPatchProvenanceDto Provenance);

public sealed record ExeFsPatchWorkflowStatsDto(
    int TotalPatchCount,
    int SourceFileCount);

public sealed record ExeFsPatchWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<ExeFsPatchRecordDto> Patches,
    ExeFsPatchWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadExeFsPatchWorkflowResponse(ExeFsPatchWorkflowDto Workflow);
