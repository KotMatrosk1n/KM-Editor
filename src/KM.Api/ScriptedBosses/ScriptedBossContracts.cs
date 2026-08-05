// SPDX-License-Identifier: GPL-3.0-only

namespace KM.Api.ScriptedBosses;

public sealed record ScriptedBossActionDto(
    string Key,
    string Kind,
    int? MoveId,
    int? RuntimeMoveId,
    string Name,
    bool UsesBattleParameters,
    bool UsesTimingParameters);

public sealed record ScriptedBossProfileDto(
    string Key,
    string LineageKey,
    int SpeciesId,
    int Form,
    string Name,
    string Scope,
    IReadOnlyList<ScriptedBossActionDto> Actions);
