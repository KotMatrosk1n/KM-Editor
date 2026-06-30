// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.ZA.Workflows;

namespace KM.ZA.Trainers;

public sealed record ZaTrainerProvenance(
    string SourceFile,
    string TeamSourceFile,
    string? ClassSourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileLayer TeamSourceLayer,
    ProjectFileLayer? ClassSourceLayer,
    ProjectFileGraphEntryState FileState,
    ProjectFileGraphEntryState TeamFileState,
    ProjectFileGraphEntryState? ClassFileState);

public sealed record ZaTrainerPokemonRecord(
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
    ZaTrainerPokemonStatsRecord Evs,
    ZaTrainerPokemonStatsRecord Ivs,
    bool Shiny)
{
    public IReadOnlyList<ZaTrainerEditableFieldOption> AbilityOptions { get; init; } =
        Array.Empty<ZaTrainerEditableFieldOption>();
}

public sealed record ZaTrainerPokemonStatsRecord(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record ZaTrainerRecord(
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
    IReadOnlyList<ZaTrainerAiFlagState> AiFlagStates,
    bool Heal,
    int Money,
    int Gift,
    int? ClassBallId,
    string? ClassBall,
    bool CanEditClassBall,
    string ClassBallScope,
    IReadOnlyList<ZaTrainerPokemonRecord> Team,
    ZaTrainerProvenance Provenance,
    int Rank,
    bool MegaEvolution,
    bool LastHand);

public sealed record ZaTrainerAiFlagState(
    int Bit,
    int Mask,
    string Label,
    string Description,
    bool Enabled);

public sealed record ZaTrainerEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<ZaTrainerEditableFieldOption> Options)
{
    public ZaTrainerEditableField(
        string Field,
        string Label,
        string ValueKind,
        int? MinimumValue,
        int? MaximumValue)
        : this(Field, Label, ValueKind, MinimumValue, MaximumValue, Array.Empty<ZaTrainerEditableFieldOption>())
    {
    }
}

public sealed record ZaTrainerEditableFieldOption(int Value, string Label);

public sealed record ZaTrainersWorkflowStats(
    int TotalTrainerCount,
    int TotalPokemonCount,
    int SourceFileCount);

public sealed record ZaTrainersWorkflow(
    ZaWorkflowSummary Summary,
    IReadOnlyList<ZaTrainerRecord> Trainers,
    IReadOnlyList<ZaTrainerEditableField> EditableFields,
    ZaTrainersWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
