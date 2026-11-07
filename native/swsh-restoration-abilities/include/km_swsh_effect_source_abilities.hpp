// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "km_swsh_battle_state_abilities.hpp"

namespace km::restoration {
// Submission is synchronous. Keep the initiating effect on the native stack,
// rather than confusing it with the ability receiving a nested event.
struct AbilityEffectSource {
    void* manager;
    uint32_t kind;
    uint32_t identity;
    const AbilityEffectSource* previous;
    AbilityEffectSource(void*, uint32_t, uint32_t) noexcept;
    ~AbilityEffectSource();
};
bool EffectCameFrom(void*, uint32_t, uint32_t = 0xffffffff) noexcept;

struct NativeEffectSourceAbilityApi {
    NativeBattleStateAbilityApi battle;
    uint32_t (*battler_value)(void*, uint32_t);
    uint32_t (*species)(void*);
    void (*status_parameter)(uint32_t, void*, uint64_t*);
    bool (*submit_status)(void*, const NativeStatusRequest*);
};
bool PoisonPuppeteer(void*, uint32_t, const NativeEffectSourceAbilityApi&) noexcept;
bool OpportunistRemember(void*, uint32_t, const NativeEffectSourceAbilityApi&) noexcept;
bool OpportunistApply(void*, uint32_t, const NativeEffectSourceAbilityApi&) noexcept;
} // namespace km::restoration
