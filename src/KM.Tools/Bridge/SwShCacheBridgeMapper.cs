// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.SwShCache;
using KM.SwSh.Workflows;

namespace KM.Tools.Bridge;

public static class SwShCacheBridgeMapper
{
    public static SwShCacheMode ToCore(SwShCacheModeDto mode)
    {
        return mode switch
        {
            SwShCacheModeDto.Minimal => SwShCacheMode.Minimal,
            SwShCacheModeDto.Balanced => SwShCacheMode.Balanced,
            SwShCacheModeDto.Performance => SwShCacheMode.Performance,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    public static SwShCacheStatusResponse ToDto(SwShCacheStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new SwShCacheStatusResponse(new SwShCacheStatusDto(
            new SwShCacheSettingsDto(ToDto(status.Settings.Mode), status.Settings.MaxCacheSizeBytes),
            status.CacheSizeBytes,
            status.WarmupCompleted,
            status.WarmupTotal,
            status.ProgressPercent,
            status.Phase,
            status.Message,
            status.IsActiveProjectPreserved));
    }

    private static SwShCacheModeDto ToDto(SwShCacheMode mode)
    {
        return mode switch
        {
            SwShCacheMode.Minimal => SwShCacheModeDto.Minimal,
            SwShCacheMode.Balanced => SwShCacheModeDto.Balanced,
            SwShCacheMode.Performance => SwShCacheModeDto.Performance,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }
}
