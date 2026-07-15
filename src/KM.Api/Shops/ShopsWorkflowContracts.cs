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
    int? StockLimit)
{
    public IReadOnlyDictionary<string, string> FieldValues { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> FieldDisplayValues { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyList<string> SupportedFields { get; init; } = [];

    public string? PriceField { get; init; }

    public bool CanEditPrice { get; init; } = true;

    public string? RowId { get; init; }
}

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
    ShopProvenanceDto Provenance)
{
    public string EditorFamily { get; init; } = "swsh";

    public bool CanEditInventoryOrder { get; init; } = true;

    public string? GlobalPriceField { get; init; }
}

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
    int Price)
{
    public IReadOnlyDictionary<string, int> Prices { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
}

public sealed record ShopsWorkflowStatsDto(
    int TotalShopCount,
    int TotalInventoryItemCount,
    int SourceFileCount);

public sealed record ShopsWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<ShopRecordDto> Shops,
    IReadOnlyList<ShopEditableFieldDto> EditableFields,
    ShopsWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics)
{
    public string EditorFamily { get; init; } = "swsh";
}

public sealed record LoadShopsWorkflowResponse(ShopsWorkflowDto Workflow);

public sealed record UpdateShopInventoryItemRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    string ShopId,
    int Slot,
    string Field,
    string Value)
{
    public string? RowId { get; init; }
}

public sealed record UpdateShopInventoryItemResponse(
    ShopsWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
