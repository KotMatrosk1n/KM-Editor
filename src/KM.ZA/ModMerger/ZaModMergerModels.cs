// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.ZA.Workflows;

namespace KM.ZA.ModMerger;

public sealed record ZaModMergerSourceRequest(
    string Path,
    bool IsEnabled = true);

public sealed record ZaModMergerSourceRecord(
    int SourceIndex,
    string Path,
    string Name,
    string Kind,
    bool IsEnabled,
    string Status,
    int FileCount,
    int OverrideCount,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record ZaModMergerWorkflowStats(
    int SourceCount,
    int EnabledSourceCount,
    int SourceFileCount,
    int OutputFileCount,
    int OverrideCount);

public sealed record ZaModMergerWorkflow(
    ZaWorkflowSummary Summary,
    string? OutputRootPath,
    IReadOnlyList<ZaModMergerSourceRecord> Sources,
    ZaModMergerWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record ZaModMergerFilePreviewRecord(
    string RelativePath,
    string OutputRelativePath,
    string SupportKind,
    string Status,
    string MergeKind,
    string Summary,
    int SourceIndex,
    string SourceName,
    int OverrideCount);

public sealed record ZaModMergerPreview(
    bool CanApply,
    string Status,
    int SelectedFileCount,
    int ReadyFileCount,
    int ConflictFileCount,
    int UnresolvedConflictCount,
    IReadOnlyList<ZaModMergerFilePreviewRecord> Files,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record ZaModMergerStageResult(
    ZaModMergerWorkflow Workflow,
    ZaModMergerPreview Preview,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record ZaModMergerApplyResult(
    ZaModMergerWorkflow Workflow,
    ZaModMergerPreview Preview,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
