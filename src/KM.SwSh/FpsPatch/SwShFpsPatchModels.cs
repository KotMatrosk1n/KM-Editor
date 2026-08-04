// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Editing;
using KM.Core.Projects;

namespace KM.SwSh.FpsPatch;

public sealed record SwShFpsPatchRomFsCategoryStatus(
    string Category,
    int ManagedFileCount,
    int PatchedFileCount,
    int StaleOwnedFileCount,
    int ConflictingFileCount);

public sealed record SwShFpsPatchStatus(
    string Status,
    string Message,
    string? BuildId,
    ProjectGame? DetectedGame,
    int PatchedMainSiteCount,
    int MainSiteCount,
    int PatchedRomFsFileCount,
    int ManagedRomFsFileCount,
    int StaleOwnedRomFsFileCount,
    int ConflictingRomFsFileCount,
    IReadOnlyList<string> StaleOwnedRomFsFiles,
    IReadOnlyList<string> ConflictingRomFsFiles,
    IReadOnlyList<SwShFpsPatchRomFsCategoryStatus> RomFsCategories,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShFpsPatchApplyResult(
    SwShFpsPatchStatus Status,
    ApplyResult ApplyResult);
