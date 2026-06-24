// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Workflows;
using KM.Api.ZaCache;
using KM.ZA.Workflows;

namespace KM.Tools.Bridge;

public static class ZaBridgeMapper
{
    public static ZaCacheMode ToCore(ZaCacheModeDto mode)
    {
        return mode switch
        {
            ZaCacheModeDto.Minimal => ZaCacheMode.Minimal,
            ZaCacheModeDto.Balanced => ZaCacheMode.Balanced,
            ZaCacheModeDto.Performance => ZaCacheMode.Performance,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    public static ZaCacheStatusResponse ToDto(ZaCacheStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new ZaCacheStatusResponse(new ZaCacheStatusDto(
            new ZaCacheSettingsDto(ToDto(status.Settings.Mode), status.Settings.MaxCacheSizeBytes),
            status.CacheSizeBytes,
            status.WarmupCompleted,
            status.WarmupTotal,
            status.ProgressPercent,
            status.Phase,
            status.Message,
            status.IsActiveProjectPreserved));
    }

    public static ListWorkflowsResponse ToDto(ZaWorkflowList workflowList)
    {
        ArgumentNullException.ThrowIfNull(workflowList);

        return new ListWorkflowsResponse(workflowList.Workflows.Select(ToDto).ToArray());
    }

    private static WorkflowSummaryDto ToDto(ZaWorkflowSummary summary)
    {
        return new WorkflowSummaryDto(
            summary.Id,
            summary.Label,
            summary.Description,
            ToDto(summary.Availability),
            summary.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static ZaCacheModeDto ToDto(ZaCacheMode mode)
    {
        return mode switch
        {
            ZaCacheMode.Minimal => ZaCacheModeDto.Minimal,
            ZaCacheMode.Balanced => ZaCacheModeDto.Balanced,
            ZaCacheMode.Performance => ZaCacheModeDto.Performance,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    private static WorkflowAvailabilityDto ToDto(ZaWorkflowAvailability availability)
    {
        return availability switch
        {
            ZaWorkflowAvailability.Disabled => WorkflowAvailabilityDto.Disabled,
            ZaWorkflowAvailability.ReadOnly => WorkflowAvailabilityDto.ReadOnly,
            ZaWorkflowAvailability.Available => WorkflowAvailabilityDto.Available,
            _ => throw new ArgumentOutOfRangeException(nameof(availability), availability, null),
        };
    }
}
