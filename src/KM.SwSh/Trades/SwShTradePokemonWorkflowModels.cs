// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Pokemon;
using KM.SwSh.Workflows;

namespace KM.SwSh.Trades;

public sealed record SwShTradePokemonProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShTradePokemonIvsRecord(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record SwShTradePokemonEntry(
    int TradeIndex,
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
    int ShinyLock,
    string ShinyLockLabel,
    int DynamaxLevel,
    bool CanGigantamax,
    int RequiredSpeciesId,
    string RequiredSpecies,
    int RequiredForm,
    int RequiredNature,
    string RequiredNatureLabel,
    int UnknownRequirement,
    int TrainerId,
    int OtGender,
    string OtGenderLabel,
    int MemoryCode,
    int MemoryTextVariable,
    int MemoryFeel,
    int MemoryIntensity,
    int Field03,
    ulong Hash0,
    ulong Hash1,
    ulong Hash2,
    IReadOnlyList<SwShTradePokemonMoveRecord> RelearnMoves,
    SwShTradePokemonIvsRecord Ivs,
    int? FlawlessIvCount,
    string IvSummary,
    SwShTradePokemonProvenance Provenance)
{
    public IReadOnlyList<SwShTradePokemonEditableFieldOption> AbilityOptions { get; init; } =
        Array.Empty<SwShTradePokemonEditableFieldOption>();

    public IReadOnlyList<SwShTradePokemonEditableFieldOption> GenderOptions { get; init; } =
        Array.Empty<SwShTradePokemonEditableFieldOption>();

    internal string SourceIdentity { get; init; } = string.Empty;
}

public sealed record SwShTradePokemonMoveRecord(
    int Slot,
    int MoveId,
    string? Move);

public sealed record SwShTradePokemonEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SwShTradePokemonEditableFieldOption> Options)
{
    public SwShTradePokemonEditableField(
        string Field,
        string Label,
        string ValueKind,
        int? MinimumValue,
        int? MaximumValue)
        : this(Field, Label, ValueKind, MinimumValue, MaximumValue, Array.Empty<SwShTradePokemonEditableFieldOption>())
    {
    }
}

public sealed record SwShTradePokemonEditableFieldOption(
    int Value,
    string Label);

public sealed record SwShTradePokemonWorkflowStats(
    int TotalTradeCount,
    int FixedIvTradeCount,
    int SourceFileCount);

public sealed record SwShTradePokemonWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShTradePokemonEntry> Trades,
    IReadOnlyList<SwShTradePokemonEditableField> EditableFields,
    SwShTradePokemonWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics)
{
    internal SwShPokemonAbilityOptionResolver AbilityResolver { get; init; } =
        SwShPokemonAbilityOptionResolver.Empty;
}
