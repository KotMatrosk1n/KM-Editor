// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SV.Workflows;

namespace KM.SV.Trades;

public sealed record SvTradePokemonProvenance(
    string SourceFile,
    string? TradeListSourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SvTradePokemonIvsRecord(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record SvTradePokemonMoveRecord(
    int Slot,
    int MoveId,
    string? Move,
    int PointUps);

public sealed record SvTradePokemonEntry(
    int TradeIndex,
    int? TradeListIndex,
    string Label,
    string EventLabel,
    string TradeListLabel,
    int SpeciesId,
    string Species,
    int Form,
    int Level,
    int HeldItemId,
    string? HeldItem,
    int BallId,
    string Ball,
    int Ability,
    string AbilityLabel,
    int Nature,
    string NatureLabel,
    int Gender,
    string GenderLabel,
    int ShinyLock,
    string ShinyLockLabel,
    int TeraType,
    string TeraTypeLabel,
    int ScaleMode,
    string ScaleModeLabel,
    int ScaleValue,
    int RequiredSpeciesId,
    string RequiredSpecies,
    int RequiredForm,
    long TrainerId,
    int OtGender,
    string OtGenderLabel,
    IReadOnlyList<SvTradePokemonMoveRecord> Moves,
    SvTradePokemonIvsRecord Ivs,
    int? FlawlessIvCount,
    string IvSummary,
    SvTradePokemonProvenance Provenance)
{
    public IReadOnlyList<SvTradePokemonEditableFieldOption> AbilityOptions { get; init; } =
        Array.Empty<SvTradePokemonEditableFieldOption>();
}

public sealed record SvTradePokemonEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SvTradePokemonEditableFieldOption> Options)
{
    public SvTradePokemonEditableField(
        string Field,
        string Label,
        string ValueKind,
        int? MinimumValue,
        int? MaximumValue)
        : this(Field, Label, ValueKind, MinimumValue, MaximumValue, Array.Empty<SvTradePokemonEditableFieldOption>())
    {
    }
}

public sealed record SvTradePokemonEditableFieldOption(
    int Value,
    string Label);

public sealed record SvTradePokemonWorkflowStats(
    int TotalTradeCount,
    int FixedIvTradeCount,
    int SourceFileCount);

public sealed record SvTradePokemonWorkflow(
    SvWorkflowSummary Summary,
    IReadOnlyList<SvTradePokemonEntry> Trades,
    IReadOnlyList<SvTradePokemonEditableField> EditableFields,
    SvTradePokemonWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
