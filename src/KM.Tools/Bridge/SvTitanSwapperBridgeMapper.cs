// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.TitanSwapper;
using KM.Api.Workflows;
using KM.SV.TitanSwapper;
using KM.SV.Workflows;
namespace KM.Tools.Bridge;

internal static class SvTitanSwapperBridgeMapper
{
    public static TitanSwapperWorkflowDto ToDto(SvTitanSwapperWorkflow workflow) => new(
        new WorkflowSummaryDto(workflow.Summary.Id, workflow.Summary.Label, workflow.Summary.Description,
            workflow.Summary.Availability switch
            {
                SvWorkflowAvailability.Available => WorkflowAvailabilityDto.Available,
                SvWorkflowAvailability.ReadOnly => WorkflowAvailabilityDto.ReadOnly,
                _ => WorkflowAvailabilityDto.Disabled,
            }, workflow.Summary.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray()),
        workflow.SourceRevision,
        workflow.Rows.Select(row => new TitanSwapperRowDto(row.Id, row.StorySpecies, row.Phase, row.Edition, row.Values)).ToArray(),
        workflow.SpeciesOptions.Select(option => new TitanSpeciesOptionDto(option.Value, option.Label)).ToArray(),
        workflow.Diagnostics.Select(ProjectBridgeMapper.ToDto).ToArray());
}
