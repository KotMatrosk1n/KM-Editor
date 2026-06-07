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
    SwShExeFsPatchProvenance Provenance);

public sealed record SwShExeFsPatchWorkflowStats(
    int TotalPatchCount,
    int SourceFileCount);

public sealed record SwShExeFsPatchWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShExeFsPatchRecord> Patches,
    SwShExeFsPatchWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
