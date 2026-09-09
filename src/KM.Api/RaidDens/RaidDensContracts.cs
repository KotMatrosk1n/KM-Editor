// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using System.Text.Json.Serialization;

namespace KM.Api.RaidDens;

public sealed record LoadRaidDensRequest(ProjectPathsDto Paths);
public sealed record StageRaidDensRequest(ProjectPathsDto Paths, [property: JsonRequired] bool Disabled, EditSessionDto? Session);
public sealed record RaidDensWorkflowDto(bool CanEdit, bool? Disabled, string SourceLayer,
    ProjectGameDto? DetectedGame, IReadOnlyList<ApiDiagnostic> Diagnostics);
public sealed record LoadRaidDensResponse(RaidDensWorkflowDto Workflow);
public sealed record StageRaidDensResponse(RaidDensWorkflowDto Workflow, EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
