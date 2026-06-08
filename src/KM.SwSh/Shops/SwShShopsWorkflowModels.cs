// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.Shops;

public sealed record SwShShopProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShShopInventoryRecord(
    int Slot,
    int ItemId,
    string ItemName,
    int Price,
    bool IsKnownItem,
    int? StockLimit);

public sealed record SwShShopRecord(
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
    IReadOnlyList<SwShShopInventoryRecord> Inventory,
    SwShShopProvenance Provenance);

public sealed record SwShShopEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SwShShopEditableFieldOption> Options);

public sealed record SwShShopEditableFieldOption(
    int Value,
    string Label,
    string ItemName,
    int Price);

public sealed record SwShShopsWorkflowStats(
    int TotalShopCount,
    int TotalInventoryItemCount,
    int SourceFileCount);

public sealed record SwShShopsWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShShopRecord> Shops,
    IReadOnlyList<SwShShopEditableField> EditableFields,
    SwShShopsWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
