// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "km_swsh_support_abilities.hpp"

namespace km::restoration {
// These records belong to a native event manager, from construction to cleanup.
// Ability work alone cannot remember an activation after switching or fainting.
void BeginAbilityBattle(void*) noexcept;
void EndAbilityBattle(void*) noexcept;
void RecordAbilityFaint(void*, uint32_t) noexcept;
uint32_t CompletedAbilityTurns(void*) noexcept;
void CompleteAbilityTurn(void*) noexcept;
bool MarkHeroEntry(void*, uint32_t) noexcept;
bool BeginCommanderPair(void*, uint32_t, uint32_t, const void*) noexcept;
bool CommanderHidden(void*, uint32_t) noexcept;
bool CommanderHiddenBattler(const void*) noexcept;
bool CommanderLocked(void*, uint32_t) noexcept;
// Returns a released, living partner id plus one, or zero.
uint32_t ReleaseCommander(void*, uint32_t) noexcept;

struct NativeBattleStateAbilityApi {
    uint32_t (*event_value)(void*, uint32_t);
    uint32_t (*work_value)(void*, uint32_t);
    void (*set_work_value)(void*, uint32_t, uint32_t);
    bool (*same_side)(void*, uint32_t, uint32_t);
    void (*chain_modifier)(void*, uint32_t, uint32_t);
    uint32_t (*position)(void*, uint32_t);
    uint32_t (*select_battlers)(void*, const NativeBattlerSelector*, uint8_t*);
    void* (*battler)(void*, uint32_t);
    bool (*has_substitute)(void*);
    void (*initialize_message)(void*);
    bool (*submit_rank)(void*, const NativeRankRequest*);
    bool (*begin_ability)(void*, uint32_t);
    bool (*end_ability)(void*, uint32_t);
};
bool SupremeOverlordEntry(void*, uint32_t, const NativeBattleStateAbilityApi&) noexcept;
bool SupremeOverlordPower(void*, uint32_t, const NativeBattleStateAbilityApi&) noexcept;
bool SupersweetSyrup(void*, uint32_t, const NativeBattleStateAbilityApi&) noexcept;
} // namespace km::restoration
