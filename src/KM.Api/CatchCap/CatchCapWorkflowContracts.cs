// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.CatchCap;

public sealed record LoadCatchCapWorkflowRequest(ProjectPathsDto Paths);

public sealed record StageCatchCapRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    IReadOnlyList<CatchCapSelectionDto> Caps);

public sealed record StageCatchCapUninstallRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session);

public sealed record CatchCapProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record CatchCapRecordDto(
    int BadgeCount,
    string Label,
    int LevelCap,
    int MinimumLevelCap,
    int MaximumLevelCap);

public sealed record CatchCapSelectionDto(
    int BadgeCount,
    int LevelCap);

public sealed record CatchCapWorkflowStatsDto(
    int TotalCapCount,
    int SourceFileCount);

public sealed record CatchCapWorkflowDto(
    WorkflowSummaryDto Summary,
    string InstallStatus,
    string InstallMessage,
    string LogicExpression,
    string CapLogicSha256,
    IReadOnlyList<CatchCapRecordDto> Caps,
    CatchCapProvenanceDto Provenance,
    CatchCapWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadCatchCapWorkflowResponse(CatchCapWorkflowDto Workflow);

public sealed record StageCatchCapResponse(
    CatchCapWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record StageCatchCapUninstallResponse(
    CatchCapWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
