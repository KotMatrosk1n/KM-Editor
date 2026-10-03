// SPDX-License-Identifier: GPL-3.0-only
using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;

namespace KM.Api.GameOptions;

public sealed record LoadGameOptionsRequest(ProjectPathsDto Paths);
public sealed record StageGameOptionsRequest(ProjectPathsDto Paths, IReadOnlyList<int> Selections, EditSessionDto? Session);
public sealed record GameOptionsWorkflowDto(bool CanEdit, ProjectGameDto? DetectedGame,
    IReadOnlyList<int> Selections, string SourceLayer, IReadOnlyList<ApiDiagnostic> Diagnostics);
public sealed record LoadGameOptionsResponse(GameOptionsWorkflowDto Workflow);
public sealed record StageGameOptionsResponse(GameOptionsWorkflowDto Workflow, EditSessionDto Session, IReadOnlyList<ApiDiagnostic> Diagnostics);
