// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.FairyGymBoosts;
using KM.Api.Projects;

namespace KM.Api.MarnieBoosts;

public sealed record LoadMarnieBoostsRequest(ProjectPathsDto Paths);
public sealed record StageMarnieBoostsRequest(ProjectPathsDto Paths, IReadOnlyList<FairyGymBoostSelectionDto> Selections, EditSessionDto? Session);
public sealed record MarnieBoostSourceDto(string Path, string Status, string Layer);
public sealed record MarnieBoostsWorkflowDto(bool CanEdit, ProjectGameDto? DetectedGame,
    IReadOnlyList<FairyGymBoostSelectionDto> Selections, IReadOnlyList<MarnieBoostSourceDto> Sources,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
public sealed record LoadMarnieBoostsResponse(MarnieBoostsWorkflowDto Workflow);
public sealed record StageMarnieBoostsResponse(MarnieBoostsWorkflowDto Workflow, EditSessionDto Session, IReadOnlyList<ApiDiagnostic> Diagnostics);
