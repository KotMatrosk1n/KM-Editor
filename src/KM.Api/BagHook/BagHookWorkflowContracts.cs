// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.BagHook;

public sealed record LoadBagHookWorkflowRequest(ProjectPathsDto Paths);

public sealed record StageBagHookInstallRequest(ProjectPathsDto Paths, EditSessionDto? Session);

public sealed record StageBagHookUninstallRequest(ProjectPathsDto Paths, EditSessionDto? Session);

public sealed record BagHookProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record BagHookSlotRecordDto(
    int Slot,
    string Status,
    bool IsReserved,
    string ReservedFor,
    int? ItemId,
    string ItemName,
    int? Quantity,
    string Owner,
    string Notes,
    BagHookProvenanceDto Provenance);

public sealed record BagHookWorkflowStatsDto(
    int TotalSlotCount,
    int OccupiedSlotCount,
    int EmptySlotCount,
    int ReservedSlotCount,
    int SourceFileCount);

public sealed record BagHookWorkflowDto(
    WorkflowSummaryDto Summary,
    string InstallStatus,
    string InstallMessage,
    IReadOnlyList<BagHookSlotRecordDto> Slots,
    BagHookWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadBagHookWorkflowResponse(BagHookWorkflowDto Workflow);

public sealed record StageBagHookInstallResponse(
    BagHookWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record StageBagHookUninstallResponse(
    BagHookWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
