// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SV.Workflows;

namespace KM.SV.Gifts;

public sealed record SvGiftPokemonProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SvGiftPokemonIvsRecord(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record SvGiftPokemonMoveRecord(
    int Slot,
    int MoveId,
    string? Move,
    int PointUps);

public sealed record SvGiftPokemonEntry(
    int GiftIndex,
    string Label,
    string EventLabel,
    int SpeciesId,
    string Species,
    int Form,
    int Level,
    bool IsEgg,
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
    IReadOnlyList<SvGiftPokemonMoveRecord> Moves,
    SvGiftPokemonIvsRecord Ivs,
    int? FlawlessIvCount,
    string IvSummary,
    SvGiftPokemonProvenance Provenance)
{
    public IReadOnlyList<SvGiftPokemonEditableFieldOption> AbilityOptions { get; init; } =
        Array.Empty<SvGiftPokemonEditableFieldOption>();
}

public sealed record SvGiftPokemonEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SvGiftPokemonEditableFieldOption> Options)
{
    public SvGiftPokemonEditableField(
        string Field,
        string Label,
        string ValueKind,
        int? MinimumValue,
        int? MaximumValue)
        : this(Field, Label, ValueKind, MinimumValue, MaximumValue, Array.Empty<SvGiftPokemonEditableFieldOption>())
    {
    }
}

public sealed record SvGiftPokemonEditableFieldOption(
    int Value,
    string Label);

public sealed record SvGiftPokemonWorkflowStats(
    int TotalGiftCount,
    int EggGiftCount,
    int FixedIvGiftCount,
    int SourceFileCount);

public sealed record SvGiftPokemonWorkflow(
    SvWorkflowSummary Summary,
    IReadOnlyList<SvGiftPokemonEntry> Gifts,
    IReadOnlyList<SvGiftPokemonEditableField> EditableFields,
    SvGiftPokemonWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
