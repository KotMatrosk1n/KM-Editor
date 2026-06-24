// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.ZA.Workflows;

namespace KM.ZA.Trades;

public sealed record ZaTradePokemonProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record ZaTradePokemonIvsRecord(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record ZaTradePokemonMoveRecord(
    int Slot,
    int MoveId,
    string? Move,
    int PointUps);

public sealed record ZaTradePokemonEntry(
    int TradeIndex,
    int SourceIndex,
    string Label,
    string EventLabel,
    int SpeciesId,
    string Species,
    int Form,
    int Level,
    int MaxLevel,
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
    IReadOnlyList<ZaTradePokemonMoveRecord> Moves,
    ZaTradePokemonIvsRecord Ivs,
    int? FlawlessIvCount,
    string IvSummary,
    float AlphaProbability,
    int AlphaAdditionalLevel,
    ZaTradePokemonProvenance Provenance)
{
    public IReadOnlyList<ZaTradePokemonEditableFieldOption> AbilityOptions { get; init; } =
        Array.Empty<ZaTradePokemonEditableFieldOption>();
}

public sealed record ZaTradePokemonEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<ZaTradePokemonEditableFieldOption> Options);

public sealed record ZaTradePokemonEditableFieldOption(
    int Value,
    string Label);

public sealed record ZaTradePokemonWorkflowStats(
    int TotalTradeCount,
    int FixedIvTradeCount,
    int SourceFileCount);

public sealed record ZaTradePokemonWorkflow(
    ZaWorkflowSummary Summary,
    IReadOnlyList<ZaTradePokemonEntry> Trades,
    IReadOnlyList<ZaTradePokemonEditableField> EditableFields,
    ZaTradePokemonWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record ZaTradePokemonFieldUpdate(
    int TradeIndex,
    string Field,
    string Value);
