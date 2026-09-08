// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Starmobiles;
using KM.Api.Workflows;
using KM.SV.Starmobiles;
using KM.SV.Workflows;

namespace KM.Tools.Bridge;

internal static class SvStarmobilesBridgeMapper
{
    public static StarmobilesWorkflowDto ToDto(SvStarmobilesWorkflow workflow) => new(
        new WorkflowSummaryDto(workflow.Summary.Id, workflow.Summary.Label, workflow.Summary.Description,
            workflow.Summary.Availability switch
            {
                SvWorkflowAvailability.Available => WorkflowAvailabilityDto.Available,
                SvWorkflowAvailability.ReadOnly => WorkflowAvailabilityDto.ReadOnly,
                _ => WorkflowAvailabilityDto.Disabled,
            }, workflow.Summary.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray()),
        workflow.SourceRevision,
        workflow.Rows.Select(row => new StarmobileRowDto(row.Id, row.BossType, row.Difficulty,
            row.TrainerId, row.EventId, row.Values)
            { VanillaValues = workflow.VanillaValues.GetValueOrDefault(row.Id) }).ToArray(),
        workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray())
        { AbilityOptions = workflow.AbilityOptions.Select(option => new StarmobileAbilityOptionDto(option.Value, option.Label)).ToArray(),
          MoveOptions = workflow.MoveOptions.Select(option => new StarmobileMoveOptionDto(
            option.Value, option.Label, option.CanSelect)).ToArray() };
}
