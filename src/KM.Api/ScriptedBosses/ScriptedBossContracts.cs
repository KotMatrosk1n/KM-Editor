// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Api.ScriptedBosses;

public sealed record ScriptedBossHeatAvailabilityDto(
    int HeatLevel,
    string State);

public sealed record ScriptedBossActionDto(
    string Key,
    string Kind,
    int? SelectorActionId,
    int? MoveId,
    int? VanillaMoveId,
    int? RuntimeMoveId,
    string Name,
    bool UsesBattleParameters,
    bool UsesTimingParameters,
    bool CanEdit,
    string RuntimeState,
    string? LockReason,
    IReadOnlyList<ScriptedBossHeatAvailabilityDto> HeatAvailability,
    string? HeatContext);

public sealed record ScriptedBossProfileDto(
    string Key,
    string LineageKey,
    int SpeciesId,
    int Form,
    string Name,
    string Scope,
    IReadOnlyList<ScriptedBossActionDto> Actions);

public sealed record ScriptedBossMoveOptionDto(
    int MoveId,
    int RuntimeMoveId,
    string Name);
