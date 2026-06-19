// SPDX-License-Identifier: GPL-3.0-only

using KM.Core.Diagnostics;
using KM.Core.Files;
using KM.SV.Workflows;

namespace KM.SV.Pokemon;

public sealed record SvPokemonProvenance(
    string SourceFile,
    ProjectFileLayer SourceLayer,
    ProjectFileGraphEntryState FileState);

public sealed record SvPokemonBaseStats(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed,
    int Total);

public sealed record SvPokemonAbilitySet(
    int Ability1,
    string Ability1Label,
    int Ability2,
    string Ability2Label,
    int HiddenAbility,
    string HiddenAbilityLabel);

public sealed record SvPokemonDexPresence(
    bool IsPresentInGame,
    bool IsInAnyDex,
    int RegionalDexIndex,
    int ArmorDexIndex,
    int CrownDexIndex);

public sealed record SvPokemonPersonalDetails(
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

public sealed record SvPokemonEvolutionRecord(
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

public sealed record SvPokemonEvolutionMethodOption(
    int Value,
    string Label,
    string ArgumentKind,
    string ArgumentLabel,
    IReadOnlyList<SvPokemonEditableFieldOption> ArgumentOptions);

public sealed record SvPokemonLearnsetMove(
    int Slot,
    int MoveId,
    string MoveName,
    int Level,
    int RawLevel,
    string? LevelLabel);

public sealed record SvPokemonCompatibilityGroup(
    string GroupId,
    string Label,
    int EnabledCount,
    IReadOnlyList<SvPokemonCompatibilityEntry> Entries);

public sealed record SvPokemonCompatibilityEntry(
    int Slot,
    int MoveId,
    string MoveName,
    string Label,
    bool CanLearn);

public sealed record SvPokemonRecord(
    int PersonalId,
    int SpeciesId,
    int Form,
    string Name,
    string FormLabel,
    string Type1,
    string Type2,
    SvPokemonBaseStats BaseStats,
    SvPokemonAbilitySet Abilities,
    SvPokemonDexPresence DexPresence,
    SvPokemonPersonalDetails Personal,
    int CatchRate,
    int EvolutionStage,
    int GenderRatio,
    string GenderRatioLabel,
    int BaseExperience,
    int Height,
    int Weight,
    IReadOnlyList<SvPokemonEvolutionRecord> Evolutions,
    IReadOnlyList<SvPokemonLearnsetMove> Learnset,
    IReadOnlyList<SvPokemonCompatibilityGroup> Compatibility,
    SvPokemonProvenance Provenance);

public sealed record SvPokemonWorkflowStats(
    int TotalPokemonCount,
    int PresentPokemonCount,
    int TotalEvolutionCount,
    int TotalLearnsetMoveCount,
    int SourceFileCount);

public sealed record SvPokemonWorkflow(
    SvWorkflowSummary Summary,
    IReadOnlyList<SvPokemonRecord> Pokemon,
    SvPokemonWorkflowStats Stats,
    IReadOnlyList<SvPokemonEvolutionMethodOption> EvolutionMethodOptions,
    IReadOnlyList<SvPokemonEditableFieldOption> LearnsetMoveOptions,
    IReadOnlyList<SvPokemonEditableField> EditableFields,
    IReadOnlyList<ValidationDiagnostic> Diagnostics);

public sealed record SvPokemonEditableField(
    string Field,
    string Label,
    string Group,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<SvPokemonEditableFieldOption> Options);

public sealed record SvPokemonEditableFieldOption(
    int Value,
    string Label);
