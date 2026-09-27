// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;

namespace KM.Api.HeldItemChance;

public sealed record LoadHeldItemChanceRequest(ProjectPathsDto Paths);
public sealed record StageHeldItemChanceRequest(ProjectPathsDto Paths, IReadOnlyList<int> Rates, EditSessionDto? Session);
public sealed record HeldItemChanceWorkflowDto(bool CanEdit, ProjectGameDto? DetectedGame,
    IReadOnlyList<int> Rates, string SourceLayer, IReadOnlyList<ApiDiagnostic> Diagnostics);
public sealed record LoadHeldItemChanceResponse(HeldItemChanceWorkflowDto Workflow);
public sealed record StageHeldItemChanceResponse(HeldItemChanceWorkflowDto Workflow, EditSessionDto Session, IReadOnlyList<ApiDiagnostic> Diagnostics);
