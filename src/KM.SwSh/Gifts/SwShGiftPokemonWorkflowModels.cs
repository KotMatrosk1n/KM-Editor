// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.Gifts;

public sealed record SwShGiftPokemonProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShGiftPokemonIvsRecord(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record SwShGiftPokemonEntry(
    int GiftIndex,
    string Label,
    int SpeciesId,
    string Species,
    int Form,
    int Level,
    bool IsEgg,
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
    int SpecialMoveId,
    string? SpecialMove,
    SwShGiftPokemonIvsRecord Ivs,
    int? FlawlessIvCount,
    string IvSummary,
    SwShGiftPokemonProvenance Provenance);

public sealed record SwShGiftPokemonEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SwShGiftPokemonEditableFieldOption> Options)
{
    public SwShGiftPokemonEditableField(
        string Field,
        string Label,
        string ValueKind,
        int? MinimumValue,
        int? MaximumValue)
        : this(Field, Label, ValueKind, MinimumValue, MaximumValue, Array.Empty<SwShGiftPokemonEditableFieldOption>())
    {
    }
}

public sealed record SwShGiftPokemonEditableFieldOption(
    int Value,
    string Label);

public sealed record SwShGiftPokemonWorkflowStats(
    int TotalGiftCount,
    int EggGiftCount,
    int FixedIvGiftCount,
    int SourceFileCount);

public sealed record SwShGiftPokemonWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShGiftPokemonEntry> Gifts,
    IReadOnlyList<SwShGiftPokemonEditableField> EditableFields,
    SwShGiftPokemonWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
