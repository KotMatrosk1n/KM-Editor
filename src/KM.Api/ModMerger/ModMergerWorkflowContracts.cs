// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.ModMerger;

public sealed record LoadModMergerWorkflowRequest(
    ProjectPathsDto Paths,
    string? ModDirectory1,
    string? ModDirectory2);

public sealed record ModMergerConflictResolutionDto(
    string ConflictId,
    string Source);

public sealed record StageModMergeRequest(
    ProjectPathsDto Paths,
    string? ModDirectory1,
    string? ModDirectory2,
    IReadOnlyList<string> SelectedDirectory1Files,
    IReadOnlyList<string> SelectedDirectory2Files,
    IReadOnlyList<ModMergerConflictResolutionDto> Resolutions);

public sealed record ApplyModMergeRequest(
    ProjectPathsDto Paths,
    string? ModDirectory1,
    string? ModDirectory2,
    IReadOnlyList<string> SelectedDirectory1Files,
    IReadOnlyList<string> SelectedDirectory2Files,
    IReadOnlyList<ModMergerConflictResolutionDto> Resolutions);

public sealed record ModMergerFileRecordDto(
    string RelativePath,
    string Name,
    long Size,
    string SupportKind,
    string Status);

public sealed record ModMergerWorkflowStatsDto(
    int Directory1FileCount,
    int Directory2FileCount,
    int MatchingFileCount);

public sealed record ModMergerWorkflowDto(
    WorkflowSummaryDto Summary,
    string? ModDirectory1,
    string? ModDirectory2,
    string? OutputRootPath,
    IReadOnlyList<ModMergerFileRecordDto> Directory1Files,
    IReadOnlyList<ModMergerFileRecordDto> Directory2Files,
    ModMergerWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record ModMergerConflictRecordDto(
    string ConflictId,
    string RelativePath,
    string Label,
    string Description,
    string Directory1Value,
    string Directory2Value,
    string? Resolution);

public sealed record ModMergerFilePreviewRecordDto(
    string RelativePath,
    string OutputRelativePath,
    string SupportKind,
    string Status,
    string Summary,
    int Directory1ChangeCount,
    int Directory2ChangeCount,
    int ConflictCount);

public sealed record ModMergerPreviewDto(
    bool CanApply,
    string Status,
    int SelectedFileCount,
    int ReadyFileCount,
    int ConflictFileCount,
    int UnresolvedConflictCount,
    IReadOnlyList<ModMergerFilePreviewRecordDto> Files,
    IReadOnlyList<ModMergerConflictRecordDto> Conflicts,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadModMergerWorkflowResponse(ModMergerWorkflowDto Workflow);

public sealed record StageModMergeResponse(
    ModMergerWorkflowDto Workflow,
    ModMergerPreviewDto Preview,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record ApplyModMergeResponse(
    ModMergerWorkflowDto Workflow,
    ModMergerPreviewDto Preview,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
