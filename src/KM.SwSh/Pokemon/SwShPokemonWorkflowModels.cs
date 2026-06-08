// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SwSh.Workflows;

namespace KM.SwSh.Pokemon;

public sealed record SwShPokemonProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SwShPokemonBaseStats(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed,
    int Total);

public sealed record SwShPokemonAbilitySet(
    int Ability1,
    string Ability1Label,
    int Ability2,
    string Ability2Label,
    int HiddenAbility,
    string HiddenAbilityLabel);

public sealed record SwShPokemonDexPresence(
    bool IsPresentInGame,
    bool IsInAnyDex,
    int RegionalDexIndex,
    int ArmorDexIndex,
    int CrownDexIndex);

public sealed record SwShPokemonPersonalDetails(
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
    bool CanNotDynamax,
    int RegionalDexIndex,
    int Form,
    int ArmorDexIndex,
    int CrownDexIndex);

public sealed record SwShPokemonEvolutionRecord(
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

public sealed record SwShPokemonEvolutionMethodOption(
    int Value,
    string Label,
    string ArgumentKind,
    string ArgumentLabel,
    IReadOnlyList<SwShPokemonEditableFieldOption> ArgumentOptions);

public sealed record SwShPokemonLearnsetMove(
    int Slot,
    int MoveId,
    string MoveName,
    int Level);

public sealed record SwShPokemonCompatibilityGroup(
    string GroupId,
    string Label,
    int EnabledCount,
    IReadOnlyList<SwShPokemonCompatibilityEntry> Entries);

public sealed record SwShPokemonCompatibilityEntry(
    int Slot,
    int MoveId,
    string MoveName,
    string Label,
    bool CanLearn);

public sealed record SwShPokemonRecord(
    int PersonalId,
    int SpeciesId,
    int Form,
    string Name,
    string FormLabel,
    string Type1,
    string Type2,
    SwShPokemonBaseStats BaseStats,
    SwShPokemonAbilitySet Abilities,
    SwShPokemonDexPresence DexPresence,
    SwShPokemonPersonalDetails Personal,
    int CatchRate,
    int EvolutionStage,
    int GenderRatio,
    string GenderRatioLabel,
    int BaseExperience,
    int Height,
    int Weight,
    IReadOnlyList<SwShPokemonEvolutionRecord> Evolutions,
    IReadOnlyList<SwShPokemonLearnsetMove> Learnset,
    IReadOnlyList<SwShPokemonCompatibilityGroup> Compatibility,
    SwShPokemonProvenance Provenance);

public sealed record SwShPokemonWorkflowStats(
    int TotalPokemonCount,
    int PresentPokemonCount,
    int TotalEvolutionCount,
    int TotalLearnsetMoveCount,
    int SourceFileCount);

public sealed record SwShPokemonWorkflow(
    SwShWorkflowSummary Summary,
    IReadOnlyList<SwShPokemonRecord> Pokemon,
    SwShPokemonWorkflowStats Stats,
    IReadOnlyList<SwShPokemonEvolutionMethodOption> EvolutionMethodOptions,
    IReadOnlyList<SwShPokemonEditableFieldOption> LearnsetMoveOptions,
    IReadOnlyList<SwShPokemonEditableField> EditableFields,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SwShPokemonEditableField(
    string Field,
    string Label,
    string Group,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SwShPokemonEditableFieldOption> Options);

public sealed record SwShPokemonEditableFieldOption(
    int Value,
    string Label);
