// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using System.Text.Json.Serialization;

namespace KM.Api.TrainerWhiteout;

public sealed record TrainerWhiteoutRecordDto(int TrainerId, string Name, bool Enabled,
    bool VanillaEnabled, bool? Override, bool Mixed);
public sealed record TrainerWhiteoutChangeDto([property: JsonRequired] int TrainerId, [property: JsonRequired] bool? Enabled);
public sealed record LoadTrainerWhiteoutRequest(ProjectPathsDto Paths);
public sealed record StageTrainerWhiteoutRequest(ProjectPathsDto Paths,
    [property: JsonRequired] IReadOnlyList<TrainerWhiteoutChangeDto> Changes, EditSessionDto? Session);
public sealed record TrainerWhiteoutWorkflowDto(bool CanEdit, IReadOnlyList<TrainerWhiteoutRecordDto> Trainers,
    ProjectGameDto? DetectedGame, IReadOnlyList<ApiDiagnostic> Diagnostics);
public sealed record LoadTrainerWhiteoutResponse(TrainerWhiteoutWorkflowDto Workflow);
public sealed record StageTrainerWhiteoutResponse(TrainerWhiteoutWorkflowDto Workflow, EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
