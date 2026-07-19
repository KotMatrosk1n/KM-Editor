// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.ZA.Workflows;

namespace KM.ZA.Pokemon;

public sealed record ZaPokemonProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record ZaPokemonBaseStats(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed,
    int Total);

public sealed record ZaPokemonAbilitySet(
    int Ability1,
    string Ability1Label,
    int Ability2,
    string Ability2Label,
    int HiddenAbility,
    string HiddenAbilityLabel);

public sealed record ZaPokemonDexPresence(
    bool IsPresentInGame,
    bool IsInAnyDex,
    int RegionalDexIndex,
    int ArmorDexIndex,
    int CrownDexIndex);

public sealed record ZaPokemonPersonalDetails(
    int Type1,
    int Type2,
    int CatchRate,
    int EvolutionStage,
    int EVYieldHP,
    int EVYieldAttack,
    int EVYieldDefense,
    int EVYieldSpecialAttack,
    int EVYieldSpecialDefense,
    int EVYieldSpeed,
    int HeldItem1,
    int HeldItem2,
    int HeldItem3,
    int GenderRatio,
    int HatchCycles,
    int BaseFriendship,
    int ExpGrowth,
    int EggGroup1,
    int EggGroup2,
    int FormStatsIndex,
    int FormCount,
    int Color,
    bool IsPresentInGame,
    bool HasSpriteForm,
    int BaseExperience,
    int Height,
    int Weight,
    uint ModelId,
    int HatchedSpecies,
    int LocalFormIndex,
    bool IsRegionalForm,
    int RegionalDexIndex,
    int Form,
    int ArmorDexIndex,
    int CrownDexIndex);

public sealed record ZaPokemonEvolutionRecord(
    int Slot,
    int Method,
    int Argument,
    int Species,
    int Form,
    int Level,
    string MethodName,
    string ArgumentKind,
    string ArgumentLabel,
    string ArgumentValue);

public sealed record ZaPokemonEvolutionMethodOption(
    int Value,
    string Label,
    string ArgumentKind,
    string ArgumentLabel,
    IReadOnlyList<ZaPokemonEditableFieldOption> ArgumentOptions);

public sealed record ZaPokemonLearnsetMove(
    int Slot,
    int MoveId,
    string MoveName,
    int Level,
    int RawLevel,
    string? LevelLabel);

public sealed record ZaPokemonCompatibilityGroup(
    string GroupId,
    string Label,
    int EnabledCount,
    IReadOnlyList<ZaPokemonCompatibilityEntry> Entries);

public sealed record ZaPokemonCompatibilityEntry(
    int Slot,
    int MoveId,
    string MoveName,
    string Label,
    bool CanLearn);

public sealed record ZaPokemonRecord(
    int PersonalId,
    int SpeciesId,
    int Form,
    string Name,
    string FormLabel,
    string Type1,
    string Type2,
    ZaPokemonBaseStats BaseStats,
    ZaPokemonAbilitySet Abilities,
    ZaPokemonDexPresence DexPresence,
    ZaPokemonPersonalDetails Personal,
    int CatchRate,
    int EvolutionStage,
    int GenderRatio,
    string GenderRatioLabel,
    int BaseExperience,
    int Height,
    int Weight,
    IReadOnlyList<ZaPokemonEvolutionRecord> Evolutions,
    IReadOnlyList<ZaPokemonLearnsetMove> Learnset,
    IReadOnlyList<ZaPokemonCompatibilityGroup> Compatibility,
    ZaPokemonProvenance Provenance,
    string? SpriteName = null);

public sealed record ZaPokemonWorkflowStats(
    int TotalPokemonCount,
    int PresentPokemonCount,
    int TotalEvolutionCount,
    int TotalLearnsetMoveCount,
    int SourceFileCount);

public sealed record ZaPokemonWorkflow(
    ZaWorkflowSummary Summary,
    IReadOnlyList<ZaPokemonRecord> Pokemon,
    ZaPokemonWorkflowStats Stats,
    IReadOnlyList<ZaPokemonEvolutionMethodOption> EvolutionMethodOptions,
    IReadOnlyList<ZaPokemonEditableFieldOption> LearnsetMoveOptions,
    IReadOnlyList<ZaPokemonEditableField> EditableFields,
    IReadOnlyList<ValidationDiagnostic> Diagnostics,
    ZaPokemonDexEditor? DexEditor = null);

public sealed record ZaPokemonEditableField(
    string Field,
    string Label,
    string Group,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<ZaPokemonEditableFieldOption> Options);

public sealed record ZaPokemonEditableFieldOption(
    int Value,
    string Label);

public sealed record ZaPokemonDexPlacement(
    int SpeciesId,
    int InternalIndex,
    string DexKind,
    int DisplayedNumber,
    string Label);

public sealed record ZaPokemonDexEditor(
    bool CanEdit,
    string? BlockedReason,
    int RegularCount,
    int HyperspaceCount,
    IReadOnlyList<ZaPokemonDexPlacement> Placements,
    ZaPokemonProvenance? PersonalProvenance,
    ZaPokemonProvenance? ContentsProvenance);

public sealed record ZaPokemonEditResult(
    ZaPokemonWorkflow Workflow,
    KM.Core.Editing.EditSession Session,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record ZaPokemonFieldUpdate(int PersonalId, string Field, string Value);
