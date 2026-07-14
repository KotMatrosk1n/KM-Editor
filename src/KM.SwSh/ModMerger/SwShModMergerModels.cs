// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.SwSh.Workflows;

namespace KM.SwSh.ModMerger;

public static class SwShModMergerMergeModes
{
    public const string Smart = "smart";
    public const string PreferMod1 = "preferMod1";
    public const string PreferMod2 = "preferMod2";
}

public sealed record SwShModMergerFileRecord(
    string RelativePath,
    string Name,
    long Size,
    string SupportKind,
    string Status);

public sealed record SwShModMergerWorkflowStats(
    int Directory1FileCount,
    int Directory2FileCount,
    int MatchingFileCount);

public sealed record SwShModMergerWorkflow(
    SwShWorkflowSummary Summary,
    string? ModDirectory1,
    string? ModDirectory2,
    string? OutputRootPath,
    IReadOnlyList<SwShModMergerFileRecord> Directory1Files,
    IReadOnlyList<SwShModMergerFileRecord> Directory2Files,
    SwShModMergerWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShModMergerConflictResolution(
    string ConflictId,
    string Source);

public sealed record SwShModMergerConflictRecord(
    string ConflictId,
    string RelativePath,
    string Label,
    string Description,
    string Directory1Value,
    string Directory2Value,
    string? Resolution);

public sealed record SwShModMergerFilePreviewRecord(
    string RelativePath,
    string OutputRelativePath,
    string SupportKind,
    string Status,
    string MergeKind,
    string Summary,
    int Directory1ChangeCount,
    int Directory2ChangeCount,
    int ConflictCount);

public sealed record SwShModMergerPreview(
    bool CanApply,
    string Status,
    string MergeMode,
    string ReviewToken,
    int SelectedFileCount,
    int ReadyFileCount,
    int ConflictFileCount,
    int UnresolvedConflictCount,
    IReadOnlyList<SwShModMergerFilePreviewRecord> Files,
    IReadOnlyList<SwShModMergerConflictRecord> Conflicts,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShModMergerStageResult(
    SwShModMergerWorkflow Workflow,
    SwShModMergerPreview Preview,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShModMergerApplyResult(
    SwShModMergerWorkflow Workflow,
    SwShModMergerPreview Preview,
    IReadOnlyList<string> WrittenFiles,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
