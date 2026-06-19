// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Projects;

namespace KM.SwSh.FpsPatch;

public sealed record SwShFpsPatchStatus(
    string Status,
    string Message,
    string? BuildId,
    ProjectGame? DetectedGame,
    int PatchedMainSiteCount,
    int MainSiteCount,
    int PatchedRomFsFileCount,
    int ManagedRomFsFileCount,
    int ConflictingRomFsFileCount,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShFpsPatchApplyResult(
    SwShFpsPatchStatus Status,
    ApplyResult ApplyResult);
