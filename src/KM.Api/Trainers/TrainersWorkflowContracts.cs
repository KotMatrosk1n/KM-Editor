// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Trainers;

public sealed record LoadTrainersWorkflowRequest(ProjectPathsDto Paths);

public sealed record TrainerProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record TrainerPokemonRecordDto(
    int Slot,
    string Species,
    int Level,
    string? HeldItem,
    IReadOnlyList<string> Moves);

public sealed record TrainerRecordDto(
    int TrainerId,
    string Name,
    string TrainerClass,
    string Location,
    string BattleType,
    IReadOnlyList<TrainerPokemonRecordDto> Team,
    TrainerProvenanceDto Provenance);

public sealed record TrainersWorkflowStatsDto(
    int TotalTrainerCount,
    int TotalPokemonCount,
    int SourceFileCount);

public sealed record TrainersWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<TrainerRecordDto> Trainers,
    TrainersWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadTrainersWorkflowResponse(TrainersWorkflowDto Workflow);
