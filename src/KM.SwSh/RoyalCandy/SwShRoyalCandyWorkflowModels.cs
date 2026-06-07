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

public sealed record SwShRoyalCandyWorkflowCheckRecord(
    string CheckId,
    string WorkflowId,
    string Status,
    string Area,
    string Target,
    string Message,
    SwShRoyalCandyProvenance Provenance);

public sealed record SwShRoyalCandyOutputRecord(
    string OutputId,
    string WorkflowId,
    string RelativePath,
    string SourceFile,
    string OutputKind,
    string Status,
    string Description,
    SwShRoyalCandyProvenance Provenance);

public sealed record SwShRoyalCandyWorkflowRecord(
    string WorkflowId,
    string Name,
    string Category,
    string Target,
    string Mode,
    int ItemId,
    int TemplateItemId,
    string Status,
    string Description,
    IReadOnlyList<SwShRoyalCandyWorkflowStepRecord> Steps,
    SwShRoyalCandyProvenance Provenance);

public sealed record SwShRoyalCandyWorkflowStats(
    int TotalWorkflowCount,
    int TotalStepCount,
    int TotalCheckCount,
    int PassCount,
    int WarningCount,
    int FailCount,
    int OutputCount,
    int SourceFileCount);

public sealed record SwShRoyalCandyWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShRoyalCandyWorkflowRecord> Workflows,
    IReadOnlyList<SwShRoyalCandyWorkflowCheckRecord> Checks,
    IReadOnlyList<SwShRoyalCandyOutputRecord> Outputs,
    SwShRoyalCandyWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
