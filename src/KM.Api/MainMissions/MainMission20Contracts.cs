// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.MainMissions;

public sealed record MainMission20SlotDto(string Id, int Species, int OriginalSpecies);
public sealed record MainMission20SpeciesOptionDto(int Value, string Label);
public sealed record MainMission20WorkflowDto(WorkflowSummaryDto Summary, string SourceRevision,
    string MissionTitle, IReadOnlyList<MainMission20SlotDto> Slots, IReadOnlyList<MainMission20SpeciesOptionDto> SpeciesOptions,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
public sealed record MainMission20UpdateDto(string SlotId, int Species);
public sealed record LoadMainMission20Request(ProjectPathsDto Paths);
public sealed record LoadMainMission20Response(MainMission20WorkflowDto Workflow);
public sealed record StageMainMission20Request(ProjectPathsDto Paths, EditSessionDto? Session, string SourceRevision,
    IReadOnlyList<MainMission20UpdateDto?>? Updates);
public sealed record StageMainMission20Response(EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
