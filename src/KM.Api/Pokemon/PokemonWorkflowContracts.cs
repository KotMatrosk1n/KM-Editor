// SPDX-License-Identifier: GPL-3.0-only

using KM.Api.Diagnostics;
using KM.Api.Editing;
using KM.Api.Projects;
using KM.Api.Workflows;

namespace KM.Api.Pokemon;

public sealed record LoadPokemonWorkflowRequest(ProjectPathsDto Paths);

public sealed record UpdatePokemonFieldRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    int PersonalId,
    string Field,
    string Value);

public sealed record UpdatePokemonLearnsetRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    int PersonalId,
    string Action,
    int? Slot,
    int? MoveId,
    int? Level);

public sealed record UpdatePokemonEvolutionRequest(
    ProjectPathsDto Paths,
    EditSessionDto? Session,
    int PersonalId,
    string Action,
    int? Slot,
    int? Method,
    int? Argument,
    int? Species,
    int? Form,
    int? Level);

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

public sealed record PokemonPersonalDetailsDto(
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
    uint ModelId,
    int HatchedSpecies,
    int LocalFormIndex,
    bool IsRegionalForm,
    bool CanNotDynamax,
    int Form);

public sealed record PokemonEvolutionRecordDto(
    int Slot,
    int Method,
    int Argument,
    int Species,
    int Form,
    int Level);

public sealed record PokemonLearnsetMoveDto(
    int Slot,
    int MoveId,
    string MoveName,
    int Level);

public sealed record PokemonCompatibilityGroupDto(
    string GroupId,
    string Label,
    int EnabledCount,
    IReadOnlyList<PokemonCompatibilityEntryDto> Entries);

public sealed record PokemonCompatibilityEntryDto(
    int Slot,
    int MoveId,
    string MoveName,
    string Label,
    bool CanLearn);

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
    PokemonPersonalDetailsDto Personal,
    int CatchRate,
    int EvolutionStage,
    int GenderRatio,
    int BaseExperience,
    int Height,
    int Weight,
    IReadOnlyList<PokemonEvolutionRecordDto> Evolutions,
    IReadOnlyList<PokemonLearnsetMoveDto> Learnset,
    IReadOnlyList<PokemonCompatibilityGroupDto> Compatibility,
    PokemonProvenanceDto Provenance);

public sealed record PokemonEditableFieldDto(
    string Field,
    string Label,
    string Group,
    string ValueKind,
    int? MinimumValue,
    int? MaximumValue,
    IReadOnlyList<PokemonEditableFieldOptionDto> Options);

public sealed record PokemonEditableFieldOptionDto(
    int Value,
    string Label);

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
    IReadOnlyList<PokemonEditableFieldDto> EditableFields,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record LoadPokemonWorkflowResponse(PokemonWorkflowDto Workflow);

public sealed record UpdatePokemonFieldResponse(
    PokemonWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record UpdatePokemonLearnsetResponse(
    PokemonWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);

public sealed record UpdatePokemonEvolutionResponse(
    PokemonWorkflowDto Workflow,
    EditSessionDto Session,
    IReadOnlyList<ApiDiagnostic> Diagnostics);
