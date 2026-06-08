// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Rentals;

public sealed record LoadRentalPokemonWorkflowRequest(ProjectPathsDto Paths);

public sealed record UpdateRentalPokemonFieldRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    int RentalIndex,
    string Field,
    string Value);

public sealed record RentalPokemonProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record RentalPokemonStatsDto(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record RentalPokemonRecordDto(
    int RentalIndex,
    string Label,
    int SpeciesId,
    string Species,
    int Form,
    int Level,
    int HeldItemId,
    string? HeldItem,
    int BallItemId,
    string BallItem,
    int Ability,
    string AbilityLabel,
    int Nature,
    string NatureLabel,
    int Gender,
    string GenderLabel,
    uint TrainerId,
    string Hash1,
    string Hash2,
    IReadOnlyList<RentalPokemonMoveRecordDto> Moves,
    RentalPokemonStatsDto Evs,
    RentalPokemonStatsDto Ivs,
    bool HasPerfectIvs,
    string IvSummary,
    RentalPokemonProvenanceDto Provenance);

public sealed record RentalPokemonMoveRecordDto(
    int Slot,
    int MoveId,
    string? Move);

public sealed record RentalPokemonEditableFieldDto(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<RentalPokemonEditableFieldOptionDto> Options);

public sealed record RentalPokemonEditableFieldOptionDto(
    int Value,
    string Label);

public sealed record RentalPokemonWorkflowStatsDto(
    int TotalRentalCount,
    int PerfectIvRentalCount,
    int SourceFileCount);

public sealed record RentalPokemonWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<RentalPokemonRecordDto> Rentals,
    IReadOnlyList<RentalPokemonEditableFieldDto> EditableFields,
    RentalPokemonWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadRentalPokemonWorkflowResponse(RentalPokemonWorkflowDto Workflow);

public sealed record UpdateRentalPokemonFieldResponse(
    RentalPokemonWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
