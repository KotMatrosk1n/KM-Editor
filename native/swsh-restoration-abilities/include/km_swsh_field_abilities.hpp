// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "km_swsh_restoration_abilities.hpp"

namespace km::restoration {

struct NativeFieldAbilityApi {
    uint32_t (*event_value)(void*, uint32_t);
    uint32_t (*work_value)(void*, uint32_t);
    void (*set_work_value)(void*, uint32_t, uint32_t);
    void* (*battler)(void*, uint32_t);
    uint32_t (*battler_value)(void*, uint32_t);
    uint32_t (*hp_fraction)(void*, uint32_t, bool);
    bool (*secondary_effect_blocked)(void*);
    void (*initialize_message)(void*);
    bool (*begin_ability)(void*, uint32_t);
    bool (*end_ability)(void*, uint32_t);
    bool (*submit_rank)(void*, const NativeRankRequest*);
    uint32_t (*weather)(void*, uint32_t);
    uint32_t (*terrain)(void*);
    void (*chain_modifier)(void*, uint32_t, uint32_t);
    void (*sun_entry)(void*, uint32_t);
    void (*electric_entry)(void*, uint32_t);
};

// The pre-damage events preserve the native Berserk timing around multi-hit
// moves and item activation. The final event performs Anger Shell's five ranks.
void AngerShellBeforeMove(void*, uint32_t, const NativeFieldAbilityApi&) noexcept;
void AngerShellBeforeDamage(void*, uint32_t, const NativeFieldAbilityApi&) noexcept;
bool AngerShellAfterMove(void*, uint32_t, const NativeFieldAbilityApi&) noexcept;
void OrichalcumPulseEntry(void*, uint32_t, const NativeFieldAbilityApi&) noexcept;
void HadronEngineEntry(void*, uint32_t, const NativeFieldAbilityApi&) noexcept;
bool OrichalcumPulseDamage(void*, uint32_t, const NativeFieldAbilityApi&) noexcept;
bool HadronEngineDamage(void*, uint32_t, const NativeFieldAbilityApi&) noexcept;

} // namespace km::restoration
