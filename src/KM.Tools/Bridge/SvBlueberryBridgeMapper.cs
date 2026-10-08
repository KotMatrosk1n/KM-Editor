// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Blueberry;
using KM.Api.Workflows;
using KM.SV.Blueberry;
using KM.SV.Workflows;

namespace KM.Tools.Bridge;

internal static class SvBlueberryBridgeMapper
{
    internal static BlueberryWorkflowDto ToDto(SvBlueberryWorkflow workflow) => new(
        new WorkflowSummaryDto(workflow.Summary.Id, workflow.Summary.Label, workflow.Summary.Description,
            workflow.Summary.Availability switch { SvWorkflowAvailability.Available => WorkflowAvailabilityDto.Available,
                SvWorkflowAvailability.ReadOnly => WorkflowAvailabilityDto.ReadOnly, _ => WorkflowAvailabilityDto.Disabled },
            workflow.Summary.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray()), workflow.SourceRevision,
        workflow.Rows.Select(entry => new BlueberryRowDto(entry.Record.Id, entry.Label, entry.Record.Group, entry.Record.Difficulty,
            entry.Record.Goal, entry.Record.Species, entry.Record.Values, entry.VanillaValues)).ToArray(),
        workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
}
