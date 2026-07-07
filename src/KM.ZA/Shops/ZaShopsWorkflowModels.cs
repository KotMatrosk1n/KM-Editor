// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.ZA.Workflows;

namespace KM.ZA.Shops;

public sealed record ZaShopProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record ZaShopInventoryRecord(
    int Slot,
    int ItemId,
    string ItemName,
    int Price,
    bool IsKnownItem,
    int? StockLimit,
    IReadOnlyDictionary<string, string> FieldValues,
    IReadOnlyDictionary<string, string> FieldDisplayValues,
    IReadOnlyList<string> SupportedFields,
    string? PriceField,
    bool CanEditPrice);

public sealed record ZaShopRecord(
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
    IReadOnlyList<ZaShopInventoryRecord> Inventory,
    ZaShopProvenance Provenance,
    bool CanEditInventoryOrder);

public sealed record ZaShopEditableFieldOption(
    int Value,
    string Label,
    string ItemName,
    int Price);

public sealed record ZaShopEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<ZaShopEditableFieldOption> Options);

public sealed record ZaShopsWorkflowStats(
    int TotalShopCount,
    int TotalInventoryItemCount,
    int SourceFileCount);

public sealed record ZaShopsWorkflow(
    ZaWorkflowSummary Summary,
    IReadOnlyList<ZaShopRecord> Shops,
    IReadOnlyList<ZaShopEditableField> EditableFields,
    ZaShopsWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    public IReadOnlySet<int> KnownItemIds { get; init; } = new HashSet<int>();
}
