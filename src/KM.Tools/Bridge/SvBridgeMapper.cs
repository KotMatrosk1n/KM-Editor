// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.ModMerger;
using KM.Api.Workflows;
using KM.SV;
using KM.SwSh.Workflows;

namespace KM.Tools.Bridge;

public static class SvBridgeMapper
{
    public static LoadSvModMergerWorkflowResponse ToDto(SvModMergerWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        return new LoadSvModMergerWorkflowResponse(ToWorkflowDto(workflow));
    }

    public static StageSvModMergeResponse ToDto(SvModMergerStageResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new StageSvModMergeResponse(
            ToWorkflowDto(result.Workflow),
            ToDto(result.Preview),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static ApplySvModMergeResponse ToDto(SvModMergerApplyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ApplySvModMergeResponse(
            ToWorkflowDto(result.Workflow),
            ToDto(result.Preview),
            result.WrittenFiles,
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static SvModMergerWorkflowDto ToWorkflowDto(SvModMergerWorkflow workflow)
    {
        return new SvModMergerWorkflowDto(
            ToDto(workflow.Summary),
            workflow.OutputRootPath,
            workflow.Sources.Select(ToDto).ToArray(),
            new SvModMergerWorkflowStatsDto(
                workflow.Stats.SourceCount,
                workflow.Stats.EnabledSourceCount,
                workflow.Stats.SourceFileCount,
                workflow.Stats.OutputFileCount,
                workflow.Stats.OverrideCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static SvModMergerSourceRecordDto ToDto(SvModMergerSourceRecord source)
    {
        return new SvModMergerSourceRecordDto(
            source.SourceIndex,
            source.Path,
            source.Name,
            source.Kind,
            source.IsEnabled,
            source.Status,
            source.FileCount,
            source.OverrideCount,
            source.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static SvModMergerPreviewDto ToDto(SvModMergerPreview preview)
    {
        return new SvModMergerPreviewDto(
            preview.CanApply,
            preview.Status,
            preview.SelectedFileCount,
            preview.ReadyFileCount,
            preview.ConflictFileCount,
            preview.UnresolvedConflictCount,
            preview.Files.Select(ToDto).ToArray(),
            preview.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static SvModMergerFilePreviewRecordDto ToDto(SvModMergerFilePreviewRecord file)
    {
        return new SvModMergerFilePreviewRecordDto(
            file.RelativePath,
            file.OutputRelativePath,
            file.SupportKind,
            file.Status,
            file.MergeKind,
            file.Summary,
            file.SourceIndex,
            file.SourceName,
            file.OverrideCount);
    }

    private static WorkflowSummaryDto ToDto(SwShWorkflowSummary summary)
    {
        return new WorkflowSummaryDto(
            summary.Id,
            summary.Label,
            summary.Description,
            ToDto(summary.Availability),
            summary.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static WorkflowAvailabilityDto ToDto(SwShWorkflowAvailability availability)
    {
        return availability switch
        {
            SwShWorkflowAvailability.Disabled => WorkflowAvailabilityDto.Disabled,
            SwShWorkflowAvailability.ReadOnly => WorkflowAvailabilityDto.ReadOnly,
            SwShWorkflowAvailability.Available => WorkflowAvailabilityDto.Available,
            _ => throw new ArgumentOutOfRangeException(nameof(availability), availability, null),
        };
    }
}
