// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.RoyalCandy;

public sealed record LoadRoyalCandyWorkflowRequest(ProjectPathsDto Paths);

public sealed record StageRoyalCandyWorkflowRequest(
    ProjectPathsDto Paths,
    string WorkflowId,
    EditSessionDto? Session);

public sealed record RoyalCandyProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record RoyalCandyWorkflowStepRecordDto(
    int Step,
    string Label,
    string Description);

public sealed record RoyalCandyWorkflowCheckRecordDto(
    string CheckId,
    string WorkflowId,
    string Status,
    string Area,
    string Target,
    string Message,
    RoyalCandyProvenanceDto Provenance);

public sealed record RoyalCandyOutputRecordDto(
    string OutputId,
    string WorkflowId,
    string RelativePath,
    string SourceFile,
    string OutputKind,
    string Status,
    string Description,
    RoyalCandyProvenanceDto Provenance);

public sealed record RoyalCandyWorkflowRecordDto(
    string WorkflowId,
    string Name,
    string Category,
    string Target,
    string Mode,
    int ItemId,
    int TemplateItemId,
    string Status,
    string Description,
    IReadOnlyList<RoyalCandyWorkflowStepRecordDto> Steps,
    RoyalCandyProvenanceDto Provenance);

public sealed record RoyalCandyWorkflowStatsDto(
    int TotalWorkflowCount,
    int TotalStepCount,
    int TotalCheckCount,
    int PassCount,
    int WarningCount,
    int FailCount,
    int OutputCount,
    int SourceFileCount);

public sealed record RoyalCandyWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<RoyalCandyWorkflowRecordDto> Workflows,
    IReadOnlyList<RoyalCandyWorkflowCheckRecordDto> Checks,
    IReadOnlyList<RoyalCandyOutputRecordDto> Outputs,
    RoyalCandyWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadRoyalCandyWorkflowResponse(RoyalCandyWorkflowDto Workflow);

public sealed record StageRoyalCandyWorkflowResponse(
    RoyalCandyWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
