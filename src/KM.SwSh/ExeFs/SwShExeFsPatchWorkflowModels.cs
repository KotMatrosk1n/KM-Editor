// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.ExeFs;

public sealed record SwShExeFsPatchProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShExeFsPatchRecord(
    string PatchId,
    string Name,
    string TargetFile,
    string PatchKind,
    string Status,
    string Description,
    IReadOnlyList<string> Details,
    SwShExeFsPatchProvenance Provenance);

public sealed record SwShExeFsSegmentRecord(
    string SegmentId,
    string Name,
    string FileOffset,
    string MemoryOffset,
    string DecompressedSize,
    string CompressedSize,
    string Sha256,
    string HashStatus,
    SwShExeFsPatchProvenance Provenance);

public sealed record SwShExeFsPatchCheckRecord(
    string CheckId,
    string PatchId,
    string Status,
    string Area,
    string Offset,
    string Name,
    string Expected,
    string Actual,
    string Notes,
    SwShExeFsPatchProvenance Provenance);

public sealed record SwShExeFsPatchWorkflowStats(
    int TotalPatchCount,
    int TotalCheckCount,
    int PassCount,
    int WarningCount,
    int FailCount,
    int SourceFileCount);

public sealed record SwShExeFsPatchWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShExeFsPatchRecord> Patches,
    IReadOnlyList<SwShExeFsSegmentRecord> Segments,
    IReadOnlyList<SwShExeFsPatchCheckRecord> Checks,
    SwShExeFsPatchWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
