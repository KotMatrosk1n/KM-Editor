// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Api.ScriptedBosses;

public sealed record ScriptedBossPhaseDto(
    string Key,
    int Stage,
    int HpPhase,
    int SpeciesId,
    int Form,
    string StageName,
    int MinimumHpPercent,
    int MaximumHpPercent);

public sealed record ScriptedBossPhaseModelDto(
    string State,
    string Kind,
    IReadOnlyList<ScriptedBossPhaseDto> Phases);

public sealed record ScriptedBossPhaseAvailabilityDto(
    string PhaseKey,
    string State);

public sealed record ScriptedBossActionDto(
    string Key,
    string Kind,
    int? SelectorActionId,
    int? MoveId,
    int? VanillaMoveId,
    int? RuntimeMoveId,
    int? Variant,
    string Name,
    bool UsesBattleParameters,
    bool UsesTimingParameters,
    bool CanEdit,
    string RuntimeState,
    string? LockReason,
    IReadOnlyList<ScriptedBossPhaseAvailabilityDto> PhaseAvailability,
    string? PhaseContext);

public sealed record ScriptedBossProfileDto(
    string Key,
    string LineageKey,
    int SpeciesId,
    int Form,
    string Name,
    string Scope,
    ScriptedBossPhaseModelDto PhaseModel,
    IReadOnlyList<ScriptedBossActionDto> Actions);

public sealed record ScriptedBossMoveOptionDto(
    int MoveId,
    int RuntimeMoveId,
    int Variant,
    string Name);
