// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.StartingItems;

public sealed record LoadStartingItemsWorkflowRequest(ProjectPathsDto Paths);

public sealed record StageStartingItemsRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    IReadOnlyList<StartingItemGrantSelectionDto> Grants);

public sealed record StartingItemsProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record StartingItemOptionRecordDto(
    int ItemId,
    string Name,
    string Category,
    bool IsKeyItem);

public sealed record StartingItemGrantRecordDto(
    int Slot,
    int? ItemId,
    string ItemName,
    int Quantity,
    bool IsKeyItem,
    string Status,
    string Owner,
    StartingItemsProvenanceDto Provenance);

public sealed record StartingItemGrantSelectionDto(
    int Slot,
    int? ItemId,
    int Quantity);

public sealed record StartingItemsWorkflowStatsDto(
    int TotalGrantSlotCount,
    int OccupiedGrantSlotCount,
    int ItemOptionCount,
    int SourceFileCount);

public sealed record StartingItemsWorkflowDto(
    WorkflowSummaryDto Summary,
    string InstallStatus,
    string InstallMessage,
    IReadOnlyList<StartingItemGrantRecordDto> Grants,
    IReadOnlyList<StartingItemOptionRecordDto> ItemOptions,
    StartingItemsWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadStartingItemsWorkflowResponse(StartingItemsWorkflowDto Workflow);

public sealed record StageStartingItemsResponse(
    StartingItemsWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
