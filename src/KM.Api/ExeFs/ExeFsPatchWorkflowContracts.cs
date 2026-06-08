// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.ExeFs;

public sealed record LoadExeFsPatchWorkflowRequest(ProjectPathsDto Paths);

public sealed record StageExeFsPatchRequest(ProjectPathsDto Paths, string PatchId, EditSessionDto? Session);

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
    IReadOnlyList<string> Details,
    ExeFsPatchProvenanceDto Provenance);

public sealed record ExeFsSegmentRecordDto(
    string SegmentId,
    string Name,
    string FileOffset,
    string MemoryOffset,
    string DecompressedSize,
    string CompressedSize,
    string Sha256,
    string HashStatus,
    ExeFsPatchProvenanceDto Provenance);

public sealed record ExeFsPatchCheckRecordDto(
    string CheckId,
    string PatchId,
    string Status,
    string Area,
    string Offset,
    string Name,
    string Expected,
    string Actual,
    string Notes,
    ExeFsPatchProvenanceDto Provenance);

public sealed record ExeFsPatchWorkflowStatsDto(
    int TotalPatchCount,
    int TotalCheckCount,
    int PassCount,
    int WarningCount,
    int FailCount,
    int SourceFileCount);

public sealed record ExeFsPatchWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<ExeFsPatchRecordDto> Patches,
    IReadOnlyList<ExeFsSegmentRecordDto> Segments,
    IReadOnlyList<ExeFsPatchCheckRecordDto> Checks,
    ExeFsPatchWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadExeFsPatchWorkflowResponse(ExeFsPatchWorkflowDto Workflow);

public sealed record StageExeFsPatchResponse(
    ExeFsPatchWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
