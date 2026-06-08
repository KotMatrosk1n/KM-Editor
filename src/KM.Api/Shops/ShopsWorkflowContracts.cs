// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Shops;

public sealed record LoadShopsWorkflowRequest(ProjectPathsDto Paths);

public sealed record ShopProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record ShopInventoryRecordDto(
    int Slot,
    int ItemId,
    string ItemName,
    int Price,
    bool IsKnownItem,
    int? StockLimit);

public sealed record ShopRecordDto(
    string ShopId,
    string Name,
    string Kind,
    string InventoryLabel,
    int InventoryIndex,
    int InventoryCount,
    string SourceHash,
    string InventorySummary,
    string Location,
    string Currency,
    IReadOnlyList<ShopInventoryRecordDto> Inventory,
    ShopProvenanceDto Provenance);

public sealed record ShopEditableFieldDto(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<ShopEditableFieldOptionDto> Options);

public sealed record ShopEditableFieldOptionDto(
    int Value,
    string Label,
    string ItemName,
    int Price);

public sealed record ShopsWorkflowStatsDto(
    int TotalShopCount,
    int TotalInventoryItemCount,
    int SourceFileCount);

public sealed record ShopsWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<ShopRecordDto> Shops,
    IReadOnlyList<ShopEditableFieldDto> EditableFields,
    ShopsWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadShopsWorkflowResponse(ShopsWorkflowDto Workflow);

public sealed record UpdateShopInventoryItemRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    string ShopId,
    int Slot,
    string Field,
    string Value);

public sealed record UpdateShopInventoryItemResponse(
    ShopsWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
