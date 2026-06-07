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
    int? StockLimit);

public sealed record SwShShopRecord(
    string ShopId,
    string Name,
    string Location,
    string Currency,
    IReadOnlyList<SwShShopInventoryRecord> Inventory,
    SwShShopProvenance Provenance);

public sealed record SwShShopsWorkflowStats(
    int TotalShopCount,
    int TotalInventoryItemCount,
    int SourceFileCount);

public sealed record SwShShopsWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShShopRecord> Shops,
    SwShShopsWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
