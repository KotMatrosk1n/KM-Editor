// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.TmMachine;

public sealed record LoadTmMachineControlsRequest(ProjectPathsDto Paths);

public sealed record StageTmRecipeAvailabilityRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    bool AllAvailable);

public sealed record StageTmMaterialVisibilityRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    bool AlwaysVisible);

public sealed record TmMachineControlStateDto(
    string Policy,
    string Status,
    string Message,
    bool CanStage,
    string? StagedPolicy,
    int MatchingRecordCount,
    int TotalRecordCount);

public sealed record TmMachineControlProvenanceDto(
    string Control,
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState,
    string Sha256);

public sealed record TmMachineControlsStatsDto(
    int RecipeCount,
    int SourceFileCount,
    int SupportedBuildCount);

public sealed record TmMachineControlsWorkflowDto(
    WorkflowSummaryDto Summary,
    string SupportedBuild,
    TmMachineControlStateDto RecipeAvailability,
    TmMachineControlStateDto MaterialVisibility,
    IReadOnlyList<TmMachineControlProvenanceDto> Provenance,
    TmMachineControlsStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadTmMachineControlsResponse(TmMachineControlsWorkflowDto Workflow);

public sealed record StageTmRecipeAvailabilityResponse(
    TmMachineControlsWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record StageTmMaterialVisibilityResponse(
    TmMachineControlsWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
