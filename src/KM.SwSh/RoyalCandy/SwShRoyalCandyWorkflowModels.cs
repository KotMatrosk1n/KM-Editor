// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.RoyalCandy;

public sealed record SwShRoyalCandyProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShRoyalCandyWorkflowStepRecord(
    int Step,
    string Label,
    string Description);

public sealed record SwShRoyalCandyWorkflowRecord(
    string WorkflowId,
    string Name,
    string Category,
    string Target,
    string Status,
    string Description,
    IReadOnlyList<SwShRoyalCandyWorkflowStepRecord> Steps,
    SwShRoyalCandyProvenance Provenance);

public sealed record SwShRoyalCandyWorkflowStats(
    int TotalWorkflowCount,
    int TotalStepCount,
    int SourceFileCount);

public sealed record SwShRoyalCandyWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShRoyalCandyWorkflowRecord> Workflows,
    SwShRoyalCandyWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
