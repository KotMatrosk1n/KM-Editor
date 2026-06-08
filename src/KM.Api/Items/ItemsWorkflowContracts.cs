// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Items;

public sealed record LoadItemsWorkflowRequest(ProjectPathsDto Paths);

public sealed record UpdateItemFieldRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    int ItemId,
    string Field,
    string Value);

public sealed record UpdateItemFieldResponse(
    ItemsWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record ItemProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record ItemDetailDto(
    string Label,
    string Value);

public sealed record ItemDetailGroupDto(
    string Label,
    IReadOnlyList<ItemDetailDto> Details);

public sealed record ItemRecordDto(
    int ItemId,
    string Name,
    string Category,
    int BuyPrice,
    int SellPrice,
    int WattsPrice,
    int AlternatePrice,
    IReadOnlyList<int> SharedItemIds,
    IReadOnlyList<ItemDetailGroupDto> DetailGroups,
    ItemProvenanceDto Provenance);

public sealed record ItemEditableFieldDto(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue);

public sealed record ItemsWorkflowStatsDto(
    int TotalItemCount,
    int SourceFileCount);

public sealed record ItemsWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<ItemRecordDto> Items,
    IReadOnlyList<ItemEditableFieldDto> EditableFields,
    ItemsWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadItemsWorkflowResponse(ItemsWorkflowDto Workflow);
