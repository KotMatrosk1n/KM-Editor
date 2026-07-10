// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SV.Workflows;

namespace KM.SV.Trainers;

public sealed record SvTrainerProvenance(
    string SourceFile,
    string TeamSourceFile,
    string? ClassSourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileLayer TeamSourceLayer,
    ProjectFileLayer? ClassSourceLayer,
    ProjectFileGraphEntryState FileState,
    ProjectFileGraphEntryState TeamFileState,
    ProjectFileGraphEntryState? ClassFileState);

public sealed record SvTrainerPokemonRecord(
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
    SvTrainerPokemonStatsRecord Evs,
    SvTrainerPokemonStatsRecord Ivs,
    bool Shiny,
    int? TeraType = null,
    string? TeraTypeLabel = null)
{
    public IReadOnlyList<SvTrainerEditableFieldOption> AbilityOptions { get; init; } =
        Array.Empty<SvTrainerEditableFieldOption>();

    public string? SpriteName { get; init; }

    public SvTrainerPokemonStatsRecord? BaseStats { get; init; }
}

public sealed record SvTrainerPokemonStatsRecord(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record SvTrainerRecord(
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
    IReadOnlyList<SvTrainerAiFlagState> AiFlagStates,
    bool CanTerastallize,
    string TeraTarget,
    bool Heal,
    int Money,
    int Gift,
    int? ClassBallId,
    string? ClassBall,
    bool CanEditClassBall,
    string ClassBallScope,
    IReadOnlyList<SvTrainerPokemonRecord> Team,
    SvTrainerProvenance Provenance);

public sealed record SvTrainerAiFlagState(
    int Bit,
    int Mask,
    string Label,
    string Description,
    bool Enabled);

public sealed record SvTrainerEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SvTrainerEditableFieldOption> Options)
{
    public SvTrainerEditableField(
        string Field,
        string Label,
        string ValueKind,
        int? MinimumValue,
        int? MaximumValue)
        : this(Field, Label, ValueKind, MinimumValue, MaximumValue, Array.Empty<SvTrainerEditableFieldOption>())
    {
    }
}

public sealed record SvTrainerEditableFieldOption(
    int Value,
    string Label);

public sealed record SvTrainersWorkflowStats(
    int TotalTrainerCount,
    int TotalPokemonCount,
    int SourceFileCount);

public sealed record SvTrainersWorkflow(
    SvWorkflowSummary Summary,
    IReadOnlyList<SvTrainerRecord> Trainers,
    IReadOnlyList<SvTrainerEditableField> EditableFields,
    SvTrainersWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
