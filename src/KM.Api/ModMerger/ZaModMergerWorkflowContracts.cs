// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.ModMerger;

public sealed record ZaModMergerSourceDto(
    string Path,
    bool IsEnabled);

public sealed record LoadZaModMergerWorkflowRequest(
    ProjectPathsDto Paths,
    IReadOnlyList<ZaModMergerSourceDto> ModSources);

public sealed record StageZaModMergeRequest(
    ProjectPathsDto Paths,
    IReadOnlyList<ZaModMergerSourceDto> ModSources);

public sealed record ApplyZaModMergeRequest(
    ProjectPathsDto Paths,
    IReadOnlyList<ZaModMergerSourceDto> ModSources);

public sealed record ZaModMergerSourceRecordDto(
    int SourceIndex,
    string Path,
    string Name,
    string Kind,
    bool IsEnabled,
    string Status,
    int FileCount,
    int OverrideCount,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record ZaModMergerWorkflowStatsDto(
    int SourceCount,
    int EnabledSourceCount,
    int SourceFileCount,
    int OutputFileCount,
    int OverrideCount);

public sealed record ZaModMergerWorkflowDto(
    WorkflowSummaryDto Summary,
    string? OutputRootPath,
    IReadOnlyList<ZaModMergerSourceRecordDto> Sources,
    ZaModMergerWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record ZaModMergerFilePreviewRecordDto(
    string RelativePath,
    string OutputRelativePath,
    string SupportKind,
    string Status,
    string MergeKind,
    string Summary,
    int SourceIndex,
    string SourceName,
    int OverrideCount);

public sealed record ZaModMergerPreviewDto(
    bool CanApply,
    string Status,
    int SelectedFileCount,
    int ReadyFileCount,
    int ConflictFileCount,
    int UnresolvedConflictCount,
    IReadOnlyList<ZaModMergerFilePreviewRecordDto> Files,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadZaModMergerWorkflowResponse(ZaModMergerWorkflowDto Workflow);

public sealed record StageZaModMergeResponse(
    ZaModMergerWorkflowDto Workflow,
    ZaModMergerPreviewDto Preview,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record ApplyZaModMergeResponse(
    ZaModMergerWorkflowDto Workflow,
    ZaModMergerPreviewDto Preview,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
