// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "km_swsh_support_abilities.hpp"

namespace km::restoration {

struct NativeRuinAbilityApi {
    uint32_t (*event_value)(void*, uint32_t);
    void* (*battler)(void*, uint32_t);
    uint32_t (*battler_value)(void*, uint32_t);
    uint32_t (*select_battlers)(void*, const NativeBattlerSelector*, uint8_t*);
    void (*chain_modifier)(void*, uint32_t, uint32_t);
    bool (*begin_ability)(void*, uint32_t);
    bool (*end_ability)(void*, uint32_t);
};

void RuinEntry(void*, uint32_t, const NativeRuinAbilityApi&) noexcept;
bool RuinStat(void*, uint32_t, uint32_t ability, bool defense, uint32_t category,
    const NativeRuinAbilityApi&) noexcept;

} // namespace km::restoration
