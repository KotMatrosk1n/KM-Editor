// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.ZA.Workflows;

namespace KM.ZA.Gifts;

public sealed record ZaGiftPokemonProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record ZaGiftPokemonIvsRecord(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed);

public sealed record ZaGiftPokemonMoveRecord(
    int Slot,
    int MoveId,
    string? Move,
    int PointUps);

public sealed record ZaGiftPokemonEntry(
    int GiftIndex,
    int SourceIndex,
    string Label,
    string EventLabel,
    int SpeciesId,
    string Species,
    int Form,
    int Level,
    int MaxLevel,
    bool IsEgg,
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
    IReadOnlyList<ZaGiftPokemonMoveRecord> Moves,
    ZaGiftPokemonIvsRecord Ivs,
    int? FlawlessIvCount,
    string IvSummary,
    float AlphaProbability,
    int AlphaAdditionalLevel,
    ZaGiftPokemonProvenance Provenance)
{
    public IReadOnlyList<ZaGiftPokemonEditableFieldOption> AbilityOptions { get; init; } =
        Array.Empty<ZaGiftPokemonEditableFieldOption>();
}

public sealed record ZaGiftPokemonEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<ZaGiftPokemonEditableFieldOption> Options);

public sealed record ZaGiftPokemonEditableFieldOption(
    int Value,
    string Label);

public sealed record ZaGiftPokemonWorkflowStats(
    int TotalGiftCount,
    int EggGiftCount,
    int FixedIvGiftCount,
    int SourceFileCount);

public sealed record ZaGiftPokemonWorkflow(
    ZaWorkflowSummary Summary,
    IReadOnlyList<ZaGiftPokemonEntry> Gifts,
    IReadOnlyList<ZaGiftPokemonEditableField> EditableFields,
    ZaGiftPokemonWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record ZaGiftPokemonFieldUpdate(
    int GiftIndex,
    string Field,
    string Value);
