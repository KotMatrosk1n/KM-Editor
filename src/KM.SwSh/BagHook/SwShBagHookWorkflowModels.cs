// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.BagHook;

public sealed record SwShBagHookProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShBagHookSlotRecord(
    int Slot,
    string Status,
    bool IsReserved,
    string ReservedFor,
    int? ItemId,
    string ItemName,
    int? Quantity,
    string Owner,
    string Notes,
    SwShBagHookProvenance Provenance);

public sealed record SwShBagHookWorkflowStats(
    int TotalSlotCount,
    int OccupiedSlotCount,
    int EmptySlotCount,
    int ReservedSlotCount,
    int SourceFileCount);

public sealed record SwShBagHookWorkflow(
    SwShWorkflowSummary Summary,
    string InstallStatus,
    string InstallMessage,
    IReadOnlyList<SwShBagHookSlotRecord> Slots,
    SwShBagHookWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
