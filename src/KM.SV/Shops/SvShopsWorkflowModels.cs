// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SV.Workflows;

namespace KM.SV.Shops;

public sealed record SvShopProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SvShopInventoryRecord(
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
    bool CanEditPrice,
    int SourceIndex);

public sealed record SvShopRecord(
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
    IReadOnlyList<SvShopInventoryRecord> Inventory,
    SvShopProvenance Provenance,
    bool CanEditInventoryOrder);

public sealed record SvShopEditableFieldOption(
    int Value,
    string Label,
    string ItemName,
    int Price);

public sealed record SvShopEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SvShopEditableFieldOption> Options);

public sealed record SvShopsWorkflowStats(
    int TotalShopCount,
    int TotalInventoryItemCount,
    int SourceFileCount);

public sealed record SvShopsWorkflow(
    SvWorkflowSummary Summary,
    IReadOnlyList<SvShopRecord> Shops,
    IReadOnlyList<SvShopEditableField> EditableFields,
    SvShopsWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
