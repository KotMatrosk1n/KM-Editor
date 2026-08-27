// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;

namespace KM.Api.FpsPatch;

public sealed record LoadFpsPatchRequest(
    ProjectPathsDto Paths);

public sealed record ApplyFpsPatchRequest(
    ProjectPathsDto Paths,
    IReadOnlyList<string>? EnabledAnimationTimingComponentIds = null);

public sealed record RestoreFpsPatchRequest(
    ProjectPathsDto Paths,
    IReadOnlyList<string>? AnimationTimingComponentIds = null);

public sealed record FpsPatchRomFsCategoryStatusDto(
    string Category,
    int ManagedFileCount,
    int PatchedFileCount,
    int StaleOwnedFileCount,
    int ConflictingFileCount);

public sealed record FpsPatchAnimationTimingComponentStatusDto(
    string Id,
    bool Enabled,
    string InputState,
    IReadOnlyList<ApiDiagnostic> InputDiagnostics,
    int ManagedFileCount,
    int PatchedFileCount,
    int StaleOwnedFileCount,
    int ConflictingFileCount);

public sealed record FpsPatchStatusDto(
    string Status,
    string Message,
    bool GlobalApplyBlocked,
    bool GlobalRestoreBlocked,
    bool HasRemovableKmState,
    IReadOnlyList<ApiDiagnostic> RestoreDiagnostics,
    string? BuildId,
    ProjectGameDto? DetectedGame,
    int PatchedMainSiteCount,
    int MainSiteCount,
    int PatchedRomFsFileCount,
    int ManagedRomFsFileCount,
    int StaleOwnedRomFsFileCount,
    int ConflictingRomFsFileCount,
    IReadOnlyList<string> StaleOwnedRomFsFiles,
    IReadOnlyList<string> ConflictingRomFsFiles,
    IReadOnlyList<FpsPatchRomFsCategoryStatusDto> RomFsCategories,
    IReadOnlyList<FpsPatchAnimationTimingComponentStatusDto> AnimationTimingComponents,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadFpsPatchResponse(
    FpsPatchStatusDto Status);

public sealed record ApplyFpsPatchResponse(
    FpsPatchStatusDto Status,
    ApplyResultDto ApplyResult,
    bool RecoveryRequired);

public sealed record RestoreFpsPatchResponse(
    FpsPatchStatusDto Status,
    ApplyResultDto ApplyResult,
    bool RecoveryRequired);
