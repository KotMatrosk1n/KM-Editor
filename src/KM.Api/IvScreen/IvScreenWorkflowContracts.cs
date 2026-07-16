// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.IvScreen;

public sealed record LoadIvScreenWorkflowRequest(ProjectPathsDto Paths);

public sealed record StageIvScreenInstallRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session);

public sealed record StageIvScreenUninstallRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session);

public sealed record IvScreenProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record IvScreenReservedRegionDto(
    string RegionId,
    string Label,
    string OffsetLabel,
    int? StartOffset,
    int? Length,
    string Rule);

public sealed record IvScreenWorkflowStatsDto(
    int ReservedMainTextRegionCount,
    int SourceFileCount);

public sealed record IvScreenWorkflowDto(
    WorkflowSummaryDto Summary,
    string InstallStatus,
    string InstallMessage,
    string Marker,
    string BuildId,
    ProjectGameDto? DetectedGame,
    string PrimaryValueSourceOffsetHex,
    string XToggleRefreshOffsetHex,
    string RawIvGetterOffsetHex,
    string HyperTrainingWrapperOffsetHex,
    bool CanUninstall,
    IReadOnlyList<IvScreenReservedRegionDto> ReservedRegions,
    IvScreenProvenanceDto Provenance,
    IvScreenWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadIvScreenWorkflowResponse(IvScreenWorkflowDto Workflow);

public sealed record StageIvScreenInstallResponse(
    IvScreenWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record StageIvScreenUninstallResponse(
    IvScreenWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
