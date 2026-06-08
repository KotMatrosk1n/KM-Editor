// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.Rentals;

public sealed record SwShRentalPokemonProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShRentalPokemonStatsRecord(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record SwShRentalPokemonEntry(
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
    IReadOnlyList<SwShRentalPokemonMoveRecord> Moves,
    SwShRentalPokemonStatsRecord Evs,
    SwShRentalPokemonStatsRecord Ivs,
    bool HasPerfectIvs,
    string IvSummary,
    SwShRentalPokemonProvenance Provenance);

public sealed record SwShRentalPokemonMoveRecord(
    int Slot,
    int MoveId,
    string? Move);

public sealed record SwShRentalPokemonEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SwShRentalPokemonEditableFieldOption> Options)
{
    public SwShRentalPokemonEditableField(
        string Field,
        string Label,
        string ValueKind,
        int? MinimumValue,
        int? MaximumValue)
        : this(Field, Label, ValueKind, MinimumValue, MaximumValue, Array.Empty<SwShRentalPokemonEditableFieldOption>())
    {
    }
}

public sealed record SwShRentalPokemonEditableFieldOption(
    int Value,
    string Label);

public sealed record SwShRentalPokemonWorkflowStats(
    int TotalRentalCount,
    int PerfectIvRentalCount,
    int SourceFileCount);

public sealed record SwShRentalPokemonWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShRentalPokemonEntry> Rentals,
    IReadOnlyList<SwShRentalPokemonEditableField> EditableFields,
    SwShRentalPokemonWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
