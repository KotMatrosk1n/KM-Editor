// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.RoyalCandy;

public sealed record LoadRoyalCandyWorkflowRequest(ProjectPathsDto Paths);

public sealed record RoyalCandyProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record RoyalCandyWorkflowStepRecordDto(
    int Step,
    string Label,
    string Description);

public sealed record RoyalCandyWorkflowRecordDto(
    string WorkflowId,
    string Name,
    string Category,
    string Target,
    string Status,
    string Description,
    IReadOnlyList<RoyalCandyWorkflowStepRecordDto> Steps,
    RoyalCandyProvenanceDto Provenance);

public sealed record RoyalCandyWorkflowStatsDto(
    int TotalWorkflowCount,
    int TotalStepCount,
    int SourceFileCount);

public sealed record RoyalCandyWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<RoyalCandyWorkflowRecordDto> Workflows,
    RoyalCandyWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadRoyalCandyWorkflowResponse(RoyalCandyWorkflowDto Workflow);
