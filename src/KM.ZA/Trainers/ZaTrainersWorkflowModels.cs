// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.ZA.Workflows;

namespace KM.ZA.Trainers;

public static class ZaTrainerIdentityDiagnosticCodes
{
    public const string ReassignmentBlocked = "KM-ZA-TRAINER-IDENTITY-REASSIGNMENT-BLOCKED";
    public const string ClassPairUnverified = "KM-ZA-TRAINER-IDENTITY-CLASS-PAIR-UNVERIFIED";
    public const string ClassPairUnchanged = "KM-ZA-TRAINER-IDENTITY-CLASS-PAIR-UNCHANGED";
    public const string PendingEditInvalid = "KM-ZA-TRAINER-IDENTITY-PENDING-EDIT-INVALID";
    public const string PlanStale = "KM-ZA-TRAINER-IDENTITY-PLAN-STALE";
}

public static class ZaTrainerClassReassignmentBlockReasons
{
    public const string HyperspaceArchetype = "hyperspaceArchetype";
    public const string UnresolvedClassPair = "unresolvedClassPair";
}

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

    public IReadOnlyList<ZaTrainerEditableFieldOption> FormOptions { get; init; } =
        Array.Empty<ZaTrainerEditableFieldOption>();

    public string? SpriteName { get; init; }

    public ZaTrainerPokemonStatsRecord? BaseStats { get; init; }
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
    bool LastHand)
{
    public bool IsSharedRivalRoster { get; init; }
    public string? RivalStarterBranch { get; init; }
    public ZaTrainerTextTarget? NameTextTarget { get; init; }
    public ZaTrainerTextTarget? ClassTextTarget { get; init; }
    public string? ClassPairId { get; init; }
    public bool CanReassignClass { get; init; }
    public string? ClassReassignmentBlockedReason { get; init; }
}

public sealed record ZaTrainerTextTarget(
    string MessageKey,
    int LineIndex,
    string Kind,
    int SharedTrainerCount);

public sealed record ZaTrainerClassPairOption(
    string PairId,
    string Label,
    int UsageCount,
    bool PresentationCanaryRequired);

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

public sealed record ZaTrainerEditableFieldOption(int Value, string Label)
{
    public IReadOnlyList<ZaTrainerEditableFieldOption>? FormOptions { get; init; }
}

public sealed record ZaTrainersWorkflowStats(
    int TotalTrainerCount,
    int TotalPokemonCount,
    int SourceFileCount);

public sealed record ZaTrainersWorkflow(
    ZaWorkflowSummary Summary,
    IReadOnlyList<ZaTrainerRecord> Trainers,
    IReadOnlyList<ZaTrainerEditableField> EditableFields,
    ZaTrainersWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    public IReadOnlyList<ZaTrainerClassPairOption> ClassPairOptions { get; init; } =
        Array.Empty<ZaTrainerClassPairOption>();

    internal ZaPokemonAvailability PokemonAvailability { get; init; } =
        ZaPokemonAvailability.Unfiltered;

    internal IReadOnlyDictionary<string, (ulong Primary, ulong Secondary)> ClassPairValues { get; init; } =
        new Dictionary<string, (ulong Primary, ulong Secondary)>(StringComparer.Ordinal);
}
