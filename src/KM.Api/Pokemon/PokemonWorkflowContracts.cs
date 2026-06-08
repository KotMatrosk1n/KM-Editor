// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Pokemon;

public sealed record LoadPokemonWorkflowRequest(ProjectPathsDto Paths);

public sealed record PokemonProvenanceDto(
    string SourceFile,
    ProjectFileLayerDto SourceLayer,
    ProjectFileGraphEntryStateDto FileState);

public sealed record PokemonBaseStatsDto(
    int HP,
    int Attack,
    int Defense,
    int SpecialAttack,
    int SpecialDefense,
    int Speed,
    int Total);

public sealed record PokemonAbilitySetDto(
    int Ability1,
    int Ability2,
    int HiddenAbility);

public sealed record PokemonDexPresenceDto(
    bool IsPresentInGame,
    bool IsInAnyDex,
    int RegionalDexIndex,
    int ArmorDexIndex,
    int CrownDexIndex);

public sealed record PokemonEvolutionRecordDto(
    int Method,
    int Argument,
    int Species,
    int Form,
    int Level);

public sealed record PokemonLearnsetMoveDto(
    int MoveId,
    string MoveName,
    int Level);

public sealed record PokemonRecordDto(
    int PersonalId,
    int SpeciesId,
    int Form,
    string Name,
    string FormLabel,
    string Type1,
    string Type2,
    PokemonBaseStatsDto BaseStats,
    PokemonAbilitySetDto Abilities,
    PokemonDexPresenceDto DexPresence,
    int CatchRate,
    int EvolutionStage,
    int GenderRatio,
    int BaseExperience,
    int Height,
    int Weight,
    IReadOnlyList<PokemonEvolutionRecordDto> Evolutions,
    IReadOnlyList<PokemonLearnsetMoveDto> Learnset,
    PokemonProvenanceDto Provenance);

public sealed record PokemonWorkflowStatsDto(
    int TotalPokemonCount,
    int PresentPokemonCount,
    int TotalEvolutionCount,
    int TotalLearnsetMoveCount,
    int SourceFileCount);

public sealed record PokemonWorkflowDto(
    WorkflowSummaryDto Summary,
    IReadOnlyList<PokemonRecordDto> Pokemon,
    PokemonWorkflowStatsDto Stats,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadPokemonWorkflowResponse(PokemonWorkflowDto Workflow);
