// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.HyperspaceBypass;

public sealed record LoadHyperspaceBypassWorkflowRequest(ProjectPathsDto Paths);

public sealed record StageHyperspaceBypassInstallRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session);

public sealed record StageHyperspaceBypassUninstallRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session);

public sealed record HyperspaceBypassProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record HyperspaceBypassReservedRegionDto(
    string RegionId,
    string Label,
    string OffsetLabel,
    int? StartOffset,
    int? Length,
    string Rule);

public sealed record HyperspaceBypassWorkflowStatsDto(
    int ReservedMainTextRegionCount,
    int SourceFileCount);

public sealed record HyperspaceBypassWorkflowDto(
    WorkflowSummaryDto Summary,
    string InstallStatus,
    string InstallMessage,
    string BuildId,
    string PatchOffsetHex,
    string StubKind,
    ProjectGameDto? DetectedGame,
    IReadOnlyList<HyperspaceBypassReservedRegionDto> ReservedRegions,
    HyperspaceBypassProvenanceDto Provenance,
    HyperspaceBypassWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadHyperspaceBypassWorkflowResponse(HyperspaceBypassWorkflowDto Workflow);

public sealed record StageHyperspaceBypassInstallResponse(
    HyperspaceBypassWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record StageHyperspaceBypassUninstallResponse(
    HyperspaceBypassWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
