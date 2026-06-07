// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Placement;

public sealed record LoadPlacementWorkflowRequest(ProjectPathsDto Paths);

public sealed record PlacementProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record PlacedObjectRecordDto(
    string ObjectId,
    string ObjectType,
    string Label,
    string Map,
    double X,
    double Y,
    double Z,
    double RotationY,
    string? ScriptId,
    PlacementProvenanceDto Provenance);

public sealed record PlacementWorkflowStatsDto(
    int TotalObjectCount,
    int SourceFileCount);

public sealed record PlacementWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<PlacedObjectRecordDto> Objects,
    PlacementWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadPlacementWorkflowResponse(PlacementWorkflowDto Workflow);
