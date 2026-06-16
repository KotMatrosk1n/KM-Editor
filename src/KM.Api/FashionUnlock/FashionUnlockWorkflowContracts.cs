// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.FashionUnlock;

public sealed record LoadFashionUnlockWorkflowRequest(ProjectPathsDto Paths);

public sealed record StageFashionUnlockInstallRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session);

public sealed record StageFashionUnlockUninstallRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session);

public sealed record FashionUnlockProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record FashionUnlockReservedRegionDto(
    string RegionId,
    string Label,
    string OffsetLabel,
    int? StartOffset,
    int? Length,
    string Rule);

public sealed record FashionUnlockWorkflowStatsDto(
    int ReservedMainTextRegionCount,
    int SourceFileCount);

public sealed record FashionUnlockWorkflowDto(
    WorkflowSummaryDto Summary,
    string InstallStatus,
    string InstallMessage,
    string BuildId,
    string DirectGetterOffsetHex,
    string MappedGetterOffsetHex,
    string StubKind,
    ProjectGameDto? DetectedGame,
    IReadOnlyList<FashionUnlockReservedRegionDto> ReservedRegions,
    FashionUnlockProvenanceDto Provenance,
    FashionUnlockWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadFashionUnlockWorkflowResponse(FashionUnlockWorkflowDto Workflow);

public sealed record StageFashionUnlockInstallResponse(
    FashionUnlockWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record StageFashionUnlockUninstallResponse(
    FashionUnlockWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
