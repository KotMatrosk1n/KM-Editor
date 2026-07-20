// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;
using System.Text.Json.Serialization;

namespace KM.Api.Trainers;

public sealed record LoadTrainersWorkflowRequest(ProjectPathsDto Paths);

public sealed record TrainerProvenanceDto(
    string SourceFile,
    string TeamSourceFile,
    string? ClassSourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileLayerDto TeamSourceLayer,
    ProjectFileLayerDto? ClassSourceLayer,
    ProjectFileGraphEntryStateDto FileState,
    ProjectFileGraphEntryStateDto TeamFileState,
    ProjectFileGraphEntryStateDto? ClassFileState);

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
    string GenderLabel,
    int Ability,
    string AbilityLabel,
    int Nature,
    string NatureLabel,
    TrainerPokemonStatsDto Evs,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? DynamaxLevel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? CanGigantamax,
    TrainerPokemonStatsDto Ivs,
    bool Shiny,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? CanDynamax,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? TeraType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TeraTypeLabel = null)
{
    public IReadOnlyList<TrainerEditableFieldOptionDto> AbilityOptions { get; init; } =
        Array.Empty<TrainerEditableFieldOptionDto>();

    public IReadOnlyList<TrainerEditableFieldOptionDto> FormOptions { get; init; } =
        Array.Empty<TrainerEditableFieldOptionDto>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpriteName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TrainerPokemonStatsDto? BaseStats { get; init; }
}

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
    IReadOnlyList<int> ItemIds,
    IReadOnlyList<string> Items,
    int AiFlags,
    IReadOnlyList<TrainerAiFlagStateDto> AiFlagStates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? CanTerastallize,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TeraTarget,
    bool Heal,
    int Money,
    int Gift,
    int? ClassBallId,
    string? ClassBall,
    bool CanEditClassBall,
    string ClassBallScope,
    IReadOnlyList<TrainerPokemonRecordDto> Team,
    TrainerProvenanceDto Provenance)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ZaRank { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ZaMegaEvolution { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ZaLastHand { get; init; }
}

public sealed record TrainerAiFlagStateDto(
    int Bit,
    int Mask,
    string Label,
    string Description,
    bool Enabled);

public sealed record TrainerEditableFieldDto(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<TrainerEditableFieldOptionDto> Options);

public sealed record TrainerEditableFieldOptionDto(
    int Value,
    string Label)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TrainerEditableFieldOptionDto>? FormOptions { get; init; }
}

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

public sealed record TrainerFieldUpdateDto(
    int TrainerId,
    int? Slot,
    string Field,
    string Value);

public sealed record UpdateTrainerFieldsRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    IReadOnlyList<TrainerFieldUpdateDto> Updates);

public sealed record UpdateTrainerFieldResponse(
    TrainersWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record UpdateTrainerFieldsResponse(
    TrainersWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
