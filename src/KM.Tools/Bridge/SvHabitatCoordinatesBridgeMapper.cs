// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.HabitatCoordinates;
using KM.Api.Workflows;
using KM.SV.HabitatCoordinates;
using KM.SV.Workflows;

namespace KM.Tools.Bridge;

public static class SvHabitatCoordinatesBridgeMapper
{
    public static LoadHabitatCoordinatesResponse ToDto(SvHabitatCoordinatesWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        return new LoadHabitatCoordinatesResponse(ToWorkflowDto(workflow));
    }

    public static StageHabitatCoordinateResponse ToDto(SvHabitatCoordinatesEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new StageHabitatCoordinateResponse(
            ToWorkflowDto(result.Workflow),
            EditSessionBridgeMapper.ToDto(result.Session),
            result.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    public static SvHabitatCoordinatesQuery? ToCore(HabitatCoordinatesQueryDto? query) =>
        query is null
            ? null
            : new SvHabitatCoordinatesQuery(
                query.Region,
                query.Search,
                query.Offset,
                query.Limit);

    public static SvHabitatRowBinding ToCore(HabitatRowBindingDto binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new SvHabitatRowBinding(
            binding.SourceFile,
            binding.SourceRevision,
            binding.OuterGroupOccurrence,
            binding.RowOccurrence,
            binding.RowPreimageSha256,
            binding.DevNo,
            binding.FormNo,
            binding.VersionA,
            binding.VersionB,
            binding.CurrentX,
            binding.CurrentY);
    }

    public static SvHabitatCoordinateChoice ToCore(HabitatCoordinateChoiceDto coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        return new SvHabitatCoordinateChoice(coordinate.X, coordinate.Y);
    }

    private static HabitatCoordinatesWorkflowDto ToWorkflowDto(
        SvHabitatCoordinatesWorkflow workflow)
    {
        return new HabitatCoordinatesWorkflowDto(
            ToDto(workflow.Summary),
            workflow.SupportedBuild,
            workflow.DetectedBuildId,
            workflow.Regions.Select(region => new HabitatRegionStateDto(
                region.Region,
                region.Label,
                region.SourceFile,
                region.SourceLayer is null ? null : ProjectBridgeMapper.ToDto(region.SourceLayer.Value),
                region.FileState is null ? null : ProjectBridgeMapper.ToDto(region.FileState.Value),
                region.SourceRevision,
                region.CanStage,
                region.OuterGroupCount,
                region.RowCount,
                region.SemanticIdentityCount,
                region.CoordinateChoices.Select(ToDto).ToArray())).ToArray(),
            new HabitatCoordinatePageDto(
                workflow.Page.Region,
                workflow.Page.Search,
                workflow.Page.Offset,
                workflow.Page.Limit,
                workflow.Page.TotalMatches,
                workflow.Page.Records.Select(record => new HabitatCoordinateRecordDto(
                    ToDto(record.Binding),
                    record.SpeciesName,
                    record.FormName,
                    record.X,
                    record.Y,
                    record.IsStaged,
                    record.StagedCoordinate is null ? null : ToDto(record.StagedCoordinate))).ToArray()),
            new HabitatCoordinatesStatsDto(
                workflow.Stats.RegionCount,
                workflow.Stats.ReadyRegionCount,
                workflow.Stats.TotalRowCount,
                workflow.Stats.TotalSemanticIdentityCount),
            workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
    }

    private static HabitatRowBindingDto ToDto(SvHabitatRowBinding binding) =>
        new(
            binding.SourceFile,
            binding.SourceRevision,
            binding.OuterGroupOccurrence,
            binding.RowOccurrence,
            binding.RowPreimageSha256,
            binding.DevNo,
            binding.FormNo,
            binding.VersionA,
            binding.VersionB,
            binding.CurrentX,
            binding.CurrentY);

    private static HabitatCoordinateChoiceDto ToDto(SvHabitatCoordinateChoice coordinate) =>
        new(coordinate.X, coordinate.Y);

    private static WorkflowSummaryDto ToDto(SvWorkflowSummary summary) =>
        new(
            summary.Id,
            summary.Label,
            summary.Description,
            summary.Availability switch
            {
                SvWorkflowAvailability.Disabled => WorkflowAvailabilityDto.Disabled,
                SvWorkflowAvailability.ReadOnly => WorkflowAvailabilityDto.ReadOnly,
                SvWorkflowAvailability.Available => WorkflowAvailabilityDto.Available,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(summary),
                    summary.Availability,
                    null),
            },
            summary.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
}
