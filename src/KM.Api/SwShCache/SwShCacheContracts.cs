// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Projects;

namespace KM.Api.SwShCache;

public enum SwShCacheModeDto
{
    Minimal,
    Balanced,
    Performance,
}

public sealed record SwShCacheSettingsDto(
    SwShCacheModeDto Mode,
    long MaxCacheSizeBytes);

public sealed record SwShCacheStatusDto(
    SwShCacheSettingsDto Settings,
    long CacheSizeBytes,
    int WarmupCompleted,
    int WarmupTotal,
    int ProgressPercent,
    string Phase,
    string Message,
    bool IsActiveProjectPreserved);

public sealed record GetSwShCacheStatusRequest(ProjectPathsDto? Paths);

public sealed record UpdateSwShCacheSettingsRequest(
    SwShCacheModeDto Mode,
    long MaxCacheSizeBytes,
    ProjectPathsDto? Paths);

public sealed record ClearSwShCacheRequest(ProjectPathsDto? ActivePaths);

public sealed record WarmupSwShCacheStepRequest(
    ProjectPathsDto Paths,
    int StepIndex);

public sealed record SwShCacheStatusResponse(SwShCacheStatusDto Status);
