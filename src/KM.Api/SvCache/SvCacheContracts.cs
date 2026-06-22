// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Projects;

namespace KM.Api.SvCache;

public enum SvCacheModeDto
{
    Minimal,
    Balanced,
    Performance,
}

public sealed record SvCacheSettingsDto(
    SvCacheModeDto Mode,
    long MaxCacheSizeBytes);

public sealed record SvCacheStatusDto(
    SvCacheSettingsDto Settings,
    long CacheSizeBytes,
    int WarmupCompleted,
    int WarmupTotal,
    int ProgressPercent,
    string Phase,
    string Message,
    bool IsActiveProjectPreserved);

public sealed record GetSvCacheStatusRequest(ProjectPathsDto? Paths);

public sealed record UpdateSvCacheSettingsRequest(
    SvCacheModeDto Mode,
    long MaxCacheSizeBytes,
    ProjectPathsDto? Paths);

public sealed record ClearSvCacheRequest(ProjectPathsDto? ActivePaths);

public sealed record WarmupSvCacheStepRequest(
    ProjectPathsDto Paths,
    int StepIndex);

public sealed record SvCacheStatusResponse(SvCacheStatusDto Status);
