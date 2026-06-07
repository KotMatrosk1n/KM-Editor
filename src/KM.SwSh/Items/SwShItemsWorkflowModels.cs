// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.Items;

public sealed record SwShItemProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShItemRecord(
    int ItemId,
    string Name,
    string Category,
    int BuyPrice,
    int SellPrice,
    SwShItemProvenance Provenance);

public sealed record SwShItemsWorkflowStats(
    int TotalItemCount,
    int SourceFileCount);

public sealed record SwShItemsWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShItemRecord> Items,
    SwShItemsWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
