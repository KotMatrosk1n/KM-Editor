// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.StartingItems;

public sealed record SwShStartingItemsProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShStartingItemOptionRecord(
    int ItemId,
    string Name,
    string Category,
    bool IsKeyItem);

public sealed record SwShStartingItemGrantRecord(
    int Slot,
    int? ItemId,
    string ItemName,
    int Quantity,
    bool IsKeyItem,
    string Status,
    string Owner,
    SwShStartingItemsProvenance Provenance);

public sealed record SwShStartingItemGrantSelection(
    int Slot,
    int? ItemId,
    int Quantity);

public sealed record SwShStartingItemsWorkflowStats(
    int TotalGrantSlotCount,
    int OccupiedGrantSlotCount,
    int ItemOptionCount,
    int SourceFileCount);

public sealed record SwShStartingItemsWorkflow(
    SwShWorkflowSummary Summary,
    string InstallStatus,
    string InstallMessage,
    string BlockerKind,
    IReadOnlyList<SwShStartingItemGrantRecord> Grants,
    IReadOnlyList<SwShStartingItemOptionRecord> ItemOptions,
    SwShStartingItemsWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
