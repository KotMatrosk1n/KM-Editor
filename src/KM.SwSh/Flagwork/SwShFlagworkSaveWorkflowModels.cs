// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.Flagwork;

public sealed record SwShFlagworkSaveProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShFlagRecord(
    string FlagId,
    string Name,
    string Category,
    string ValueKind,
    string DefaultValue,
    string Description,
    SwShFlagworkSaveProvenance Provenance);

public sealed record SwShSaveBlockRecord(
    string BlockId,
    string Name,
    int Offset,
    int Length,
    string Description,
    SwShFlagworkSaveProvenance Provenance);

public sealed record SwShFlagworkSaveWorkflowStats(
    int TotalFlagCount,
    int TotalSaveBlockCount,
    int SourceFileCount);

public sealed record SwShFlagworkSaveWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShFlagRecord> Flags,
    IReadOnlyList<SwShSaveBlockRecord> SaveBlocks,
    SwShFlagworkSaveWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
