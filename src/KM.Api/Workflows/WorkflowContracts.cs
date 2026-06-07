// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Projects;

namespace KM.Api.Workflows;

public sealed record ListWorkflowsRequest(ProjectPathsDto Paths);

public enum WorkflowAvailabilityDto
{
    Disabled,
    ReadOnly,
    Available,
}

public sealed record WorkflowSummaryDto(
    string Id,
    string Label,
    string Description,
    WorkflowAvailabilityDto Availability,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record ListWorkflowsResponse(IReadOnlyList<WorkflowSummaryDto> Workflows);
