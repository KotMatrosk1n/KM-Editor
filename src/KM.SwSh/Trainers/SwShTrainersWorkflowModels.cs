// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.Trainers;

public sealed record SwShTrainerProvenance(
    string SourceFile,
    string TeamSourceFile,
    string? ClassSourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileLayer TeamSourceLayer,
    ProjectFileLayer? ClassSourceLayer,
    ProjectFileGraphEntryState FileState,
    ProjectFileGraphEntryState TeamFileState,
    ProjectFileGraphEntryState? ClassFileState);

public sealed record SwShTrainerPokemonRecord(
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
    SwShTrainerPokemonStatsRecord Evs,
    int DynamaxLevel,
    bool CanGigantamax,
    SwShTrainerPokemonStatsRecord Ivs,
    bool Shiny,
    bool CanDynamax)
{
    public IReadOnlyList<SwShTrainerEditableFieldOption> AbilityOptions { get; init; } =
        Array.Empty<SwShTrainerEditableFieldOption>();
}

public sealed record SwShTrainerPokemonStatsRecord(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record SwShTrainerRecord(
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
    IReadOnlyList<SwShTrainerAiFlagState> AiFlagStates,
    bool Heal,
    int Money,
    int Gift,
    int? ClassBallId,
    string? ClassBall,
    bool CanEditClassBall,
    string ClassBallScope,
    IReadOnlyList<SwShTrainerPokemonRecord> Team,
    SwShTrainerProvenance Provenance);

public sealed record SwShTrainerAiFlagState(
    int Bit,
    int Mask,
    string Label,
    string Description,
    bool Enabled);

public sealed record SwShTrainerEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SwShTrainerEditableFieldOption> Options)
{
    public SwShTrainerEditableField(
        string Field,
        string Label,
        string ValueKind,
        int? MinimumValue,
        int? MaximumValue)
        : this(Field, Label, ValueKind, MinimumValue, MaximumValue, Array.Empty<SwShTrainerEditableFieldOption>())
    {
    }
}

public sealed record SwShTrainerEditableFieldOption(
    int Value,
    string Label);

public sealed record SwShTrainersWorkflowStats(
    int TotalTrainerCount,
    int TotalPokemonCount,
    int SourceFileCount);

public sealed record SwShTrainersWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShTrainerRecord> Trainers,
    IReadOnlyList<SwShTrainerEditableField> EditableFields,
    SwShTrainersWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
