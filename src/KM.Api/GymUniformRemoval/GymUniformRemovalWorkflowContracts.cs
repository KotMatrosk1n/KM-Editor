// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.GymUniformRemoval;

public sealed record LoadGymUniformRemovalWorkflowRequest(ProjectPathsDto Paths);

public sealed record StageGymUniformRemovalInstallRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session);

public sealed record StageGymUniformRemovalUninstallRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session);

public sealed record GymUniformRemovalProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record GymUniformRemovalReservedRegionDto(
    string RegionId,
    string Label,
    string OffsetLabel,
    int? StartOffset,
    int? Length,
    string Rule);

public sealed record GymUniformRemovalWorkflowStatsDto(
    int ReservedMainTextRegionCount,
    int SourceFileCount);

public sealed record GymUniformRemovalWorkflowDto(
    WorkflowSummaryDto Summary,
    string InstallStatus,
    string InstallMessage,
    string BuildId,
    string PatchOffsetHex,
    string StubKind,
    IReadOnlyList<GymUniformRemovalReservedRegionDto> ReservedRegions,
    GymUniformRemovalProvenanceDto Provenance,
    GymUniformRemovalWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadGymUniformRemovalWorkflowResponse(GymUniformRemovalWorkflowDto Workflow);

public sealed record StageGymUniformRemovalInstallResponse(
    GymUniformRemovalWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record StageGymUniformRemovalUninstallResponse(
    GymUniformRemovalWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
