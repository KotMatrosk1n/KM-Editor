// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;

namespace KM.Api.FpsPatch;

public sealed record LoadFpsPatchRequest(
    ProjectPathsDto Paths);

public sealed record ApplyFpsPatchRequest(
    ProjectPathsDto Paths);

public sealed record RestoreFpsPatchRequest(
    ProjectPathsDto Paths);

public sealed record FpsPatchStatusDto(
    string Status,
    string Message,
    string? BuildId,
    ProjectGameDto? DetectedGame,
    int PatchedMainSiteCount,
    int MainSiteCount,
    int PatchedRomFsFileCount,
    int ManagedRomFsFileCount,
    int ConflictingRomFsFileCount,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadFpsPatchResponse(
    FpsPatchStatusDto Status);

public sealed record ApplyFpsPatchResponse(
    FpsPatchStatusDto Status,
    ApplyResultDto ApplyResult);

public sealed record RestoreFpsPatchResponse(
    FpsPatchStatusDto Status,
    ApplyResultDto ApplyResult);
