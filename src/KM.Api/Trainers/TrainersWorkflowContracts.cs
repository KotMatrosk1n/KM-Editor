// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Trainers;

public sealed record LoadTrainersWorkflowRequest(ProjectPathsDto Paths);

public sealed record TrainerProvenanceDto(
    string SourceFile,
    string TeamSourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileLayerDto TeamSourceLayer,
    ProjectFileGraphEntryStateDto FileState,
    ProjectFileGraphEntryStateDto TeamFileState);

public sealed record TrainerPokemonRecordDto(
    int Slot,
    int SpeciesId,
    string Species,
    int Form,
    int Level,
    int HeldItemId,
    string? HeldItem,
    IReadOnlyList<int> MoveIds,
    IReadOnlyList<string> Moves,
    int Gender,
    int Ability,
    int Nature,
    TrainerPokemonStatsDto Evs,
    int DynamaxLevel,
    bool CanGigantamax,
    TrainerPokemonStatsDto Ivs,
    bool Shiny,
    bool CanDynamax);

public sealed record TrainerPokemonStatsDto(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record TrainerRecordDto(
    int TrainerId,
    string Name,
    int TrainerClassId,
    string TrainerClass,
    string Location,
    int BattleTypeValue,
    string BattleType,
    IReadOnlyList<TrainerPokemonRecordDto> Team,
    TrainerProvenanceDto Provenance);

public sealed record TrainerEditableFieldDto(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<TrainerEditableFieldOptionDto> Options);

public sealed record TrainerEditableFieldOptionDto(
    int Value,
    string Label);

public sealed record TrainersWorkflowStatsDto(
    int TotalTrainerCount,
    int TotalPokemonCount,
    int SourceFileCount);

public sealed record TrainersWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<TrainerRecordDto> Trainers,
    IReadOnlyList<TrainerEditableFieldDto> EditableFields,
    TrainersWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadTrainersWorkflowResponse(TrainersWorkflowDto Workflow);

public sealed record UpdateTrainerFieldRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    int TrainerId,
    int? Slot,
    string Field,
    string Value);

public sealed record UpdateTrainerFieldResponse(
    TrainersWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
