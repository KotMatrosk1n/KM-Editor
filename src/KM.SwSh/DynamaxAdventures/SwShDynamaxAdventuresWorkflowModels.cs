// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.DynamaxAdventures;

public sealed record SwShDynamaxAdventureProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShDynamaxAdventureEditableFieldOption(
    int Value,
    string Label);

public sealed record SwShDynamaxAdventureEditableField(
    string Field,
    string Label,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> Options);

public sealed record SwShDynamaxAdventureMoveRecord(
    int Slot,
    int MoveId,
    string Move);

public sealed record SwShDynamaxAdventureIvsRecord(
    int Hp,
    int Attack,
    int Defense,
    int Speed,
    int SpecialAttack,
    int SpecialDefense);

public sealed record SwShDynamaxAdventurePokemonSnapshot(
    int SpeciesId,
    string Species,
    int Form,
    int Level,
    int Ability,
    string AbilityLabel,
    int GigantamaxState,
    string GigantamaxLabel,
    IReadOnlyList<SwShDynamaxAdventureMoveRecord> Moves,
    SwShDynamaxAdventureIvsRecord Ivs,
    int GuaranteedPerfectIvs,
    string IvSummary);

public sealed record SwShDynamaxAdventureBossTargetOption(
    int EntryIndex,
    int AdventureIndex,
    int SpeciesId,
    string Species,
    int Form,
    int Version,
    string VersionLabel,
    bool IsStoryProgressGated,
    string Label);

public sealed record SwShDynamaxAdventureEntry(
    int EntryIndex,
    bool IsEditable,
    string Label,
    int AdventureIndex,
    int SpeciesId,
    string Species,
    int Form,
    int Level,
    int BallItemId,
    string BallItem,
    int Ability,
    string AbilityLabel,
    int GigantamaxState,
    string GigantamaxLabel,
    int Version,
    string VersionLabel,
    int ShinyRoll,
    string ShinyRollLabel,
    bool IsSingleCapture,
    string SingleCaptureFlagBlock,
    bool IsStoryProgressGated,
    string UiMessageId,
    int OtGender,
    string OtGenderLabel,
    IReadOnlyList<SwShDynamaxAdventureMoveRecord> Moves,
    SwShDynamaxAdventureIvsRecord Ivs,
    int GuaranteedPerfectIvs,
    string IvSummary,
    SwShDynamaxAdventureProvenance Provenance)
{
    public IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> AbilityOptions { get; init; } =
        Array.Empty<SwShDynamaxAdventureEditableFieldOption>();

    public IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> MoveOptions { get; init; } =
        Array.Empty<SwShDynamaxAdventureEditableFieldOption>();

    public IReadOnlyList<SwShDynamaxAdventureBossTargetOption> BossTargetOptions { get; init; } =
        Array.Empty<SwShDynamaxAdventureBossTargetOption>();

    public int BossTargetSpeciesId { get; init; } = SpeciesId;

    public string BossTargetSpecies { get; init; } = Species;

    public SwShDynamaxAdventurePokemonSnapshot? VanillaPokemon { get; init; }
}

public sealed record SwShDynamaxAdventuresWorkflowStats(
    int TotalEncounterCount,
    int SingleCaptureCount,
    int StoryGatedCount,
    int GuaranteedPerfectIvEncounterCount,
    int SourceFileCount);

public sealed record SwShDynamaxAdventuresWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShDynamaxAdventureEntry> Encounters,
    IReadOnlyList<SwShDynamaxAdventureEditableField> EditableFields,
    IReadOnlyList<SwShDynamaxAdventureEditableFieldOption> SafeNormalSpeciesOptions,
    SwShDynamaxAdventuresWorkflowStats Stats,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);
