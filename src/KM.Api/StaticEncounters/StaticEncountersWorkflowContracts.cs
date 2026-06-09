// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.StaticEncounters;

public sealed record LoadStaticEncountersWorkflowRequest(ProjectPathsDto Paths);

public sealed record UpdateStaticEncounterFieldRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    int EncounterIndex,
    string Field,
    string Value);

public sealed record StaticEncounterProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record StaticEncounterStatsDto(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record StaticEncounterMoveDto(
    int Slot,
    int MoveId,
    string? Move);

public sealed record StaticEncounterRecordDto(
    int EncounterIndex,
    string Label,
    string EncounterId,
    int SpeciesId,
    string Species,
    int Form,
    int Level,
    int HeldItemId,
    string? HeldItem,
    int Ability,
    string AbilityLabel,
    int Nature,
    string NatureLabel,
    int Gender,
    string GenderLabel,
    int ShinyLock,
    string ShinyLockLabel,
    int EncounterScenario,
    string EncounterScenarioLabel,
    int DynamaxLevel,
    bool CanGigantamax,
    StaticEncounterStatsDto Evs,
    StaticEncounterStatsDto Ivs,
    int? FlawlessIvCount,
    string IvSummary,
    IReadOnlyList<StaticEncounterMoveDto> Moves,
    StaticEncounterProvenanceDto Provenance)
{
    public IReadOnlyList<StaticEncounterEditableFieldOptionDto> AbilityOptions { get; init; } =
        Array.Empty<StaticEncounterEditableFieldOptionDto>();
}

public sealed record StaticEncounterEditableFieldDto(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<StaticEncounterEditableFieldOptionDto> Options);

public sealed record StaticEncounterEditableFieldOptionDto(
    int Value,
    string Label);

public sealed record StaticEncountersWorkflowStatsDto(
    int TotalEncounterCount,
    int GigantamaxEncounterCount,
    int FixedIvEncounterCount,
    int SourceFileCount);

public sealed record StaticEncountersWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<StaticEncounterRecordDto> Encounters,
    IReadOnlyList<StaticEncounterEditableFieldDto> EditableFields,
    StaticEncountersWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadStaticEncountersWorkflowResponse(StaticEncountersWorkflowDto Workflow);

public sealed record UpdateStaticEncounterFieldResponse(
    StaticEncountersWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
