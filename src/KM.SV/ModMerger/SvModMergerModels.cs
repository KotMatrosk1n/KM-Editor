// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.SwSh.Workflows;

namespace KM.SV.ModMerger;

public sealed record SvModMergerSourceRequest(
    string Path,
    bool IsEnabled = true);

public sealed record SvModMergerSourceRecord(
    int SourceIndex,
    string Path,
    string Name,
    string Kind,
    bool IsEnabled,
    string Status,
    int FileCount,
    int OverrideCount,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SvModMergerWorkflowStats(
    int SourceCount,
    int EnabledSourceCount,
    int SourceFileCount,
    int OutputFileCount,
    int OverrideCount);

public sealed record SvModMergerWorkflow(
    SwShWorkflowSummary Summary,
    string? OutputRootPath,
    IReadOnlyList<SvModMergerSourceRecord> Sources,
    SvModMergerWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SvModMergerFilePreviewRecord(
    string RelativePath,
    string OutputRelativePath,
    string SupportKind,
    string Status,
    string MergeKind,
    string Summary,
    int SourceIndex,
    string SourceName,
    int OverrideCount);

public sealed record SvModMergerPreview(
    bool CanApply,
    string Status,
    int SelectedFileCount,
    int ReadyFileCount,
    int ConflictFileCount,
    int UnresolvedConflictCount,
    IReadOnlyList<SvModMergerFilePreviewRecord> Files,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SvModMergerStageResult(
    SvModMergerWorkflow Workflow,
    SvModMergerPreview Preview,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SvModMergerApplyResult(
    SvModMergerWorkflow Workflow,
    SvModMergerPreview Preview,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
