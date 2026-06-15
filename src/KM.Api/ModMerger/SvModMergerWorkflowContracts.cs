// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.ModMerger;

public sealed record SvModMergerSourceDto(
    string Path,
    bool IsEnabled);

public sealed record LoadSvModMergerWorkflowRequest(
    ProjectPathsDto Paths,
    IReadOnlyList<SvModMergerSourceDto> ModSources);

public sealed record StageSvModMergeRequest(
    ProjectPathsDto Paths,
    IReadOnlyList<SvModMergerSourceDto> ModSources);

public sealed record ApplySvModMergeRequest(
    ProjectPathsDto Paths,
    IReadOnlyList<SvModMergerSourceDto> ModSources);

public sealed record SvModMergerSourceRecordDto(
    int SourceIndex,
    string Path,
    string Name,
    string Kind,
    bool IsEnabled,
    string Status,
    int FileCount,
    int OverrideCount,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record SvModMergerWorkflowStatsDto(
    int SourceCount,
    int EnabledSourceCount,
    int SourceFileCount,
    int OutputFileCount,
    int OverrideCount);

public sealed record SvModMergerWorkflowDto(
    WorkflowSummaryDto Summary,
    string? OutputRootPath,
    IReadOnlyList<SvModMergerSourceRecordDto> Sources,
    SvModMergerWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record SvModMergerFilePreviewRecordDto(
    string RelativePath,
    string OutputRelativePath,
    string SupportKind,
    string Status,
    string MergeKind,
    string Summary,
    int SourceIndex,
    string SourceName,
    int OverrideCount);

public sealed record SvModMergerPreviewDto(
    bool CanApply,
    string Status,
    int SelectedFileCount,
    int ReadyFileCount,
    int ConflictFileCount,
    int UnresolvedConflictCount,
    IReadOnlyList<SvModMergerFilePreviewRecordDto> Files,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadSvModMergerWorkflowResponse(SvModMergerWorkflowDto Workflow);

public sealed record StageSvModMergeResponse(
    SvModMergerWorkflowDto Workflow,
    SvModMergerPreviewDto Preview,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record ApplySvModMergeResponse(
    SvModMergerWorkflowDto Workflow,
    SvModMergerPreviewDto Preview,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
