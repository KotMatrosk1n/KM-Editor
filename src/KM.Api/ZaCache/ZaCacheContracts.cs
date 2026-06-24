// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Projects;

namespace KM.Api.ZaCache;

public enum ZaCacheModeDto
{
    Minimal,
    Balanced,
    Performance,
}

public sealed record ZaCacheSettingsDto(
    ZaCacheModeDto Mode,
    long MaxCacheSizeBytes);

public sealed record ZaCacheStatusDto(
    ZaCacheSettingsDto Settings,
    long CacheSizeBytes,
    int WarmupCompleted,
    int WarmupTotal,
    int ProgressPercent,
    string Phase,
    string Message,
    bool IsActiveProjectPreserved);

public sealed record GetZaCacheStatusRequest(ProjectPathsDto? Paths);

public sealed record UpdateZaCacheSettingsRequest(
    ZaCacheModeDto Mode,
    long MaxCacheSizeBytes,
    ProjectPathsDto? Paths);

public sealed record ClearZaCacheRequest(ProjectPathsDto? ActivePaths);

public sealed record WarmupZaCacheStepRequest(
    ProjectPathsDto Paths,
    int StepIndex);

public sealed record ZaCacheStatusResponse(ZaCacheStatusDto Status);
