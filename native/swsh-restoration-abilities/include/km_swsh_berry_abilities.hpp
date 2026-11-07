// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "km_swsh_battle_state_abilities.hpp"

namespace km::restoration {
struct NativeForceEatRequest {
    uint8_t source;
    uint8_t target;
    uint16_t item;
    uint8_t clear_berry_record;
    uint8_t reserved[3];
    uint32_t cleanup_policy;
};
static_assert(sizeof(NativeForceEatRequest) == 12);
struct NativeBerryAbilityApi {
    NativeBattleStateAbilityApi battle;
    uint32_t (*battler_value)(void*, uint32_t);
    bool (*is_berry)(uint32_t);
    bool (*force_eat)(void*, const NativeForceEatRequest*);
};
bool CudChewRemember(void*, uint32_t, const NativeBerryAbilityApi&) noexcept;
bool CudChewRepeat(void*, uint32_t, const NativeBerryAbilityApi&) noexcept;
void CudChewReset(void*, uint32_t, const NativeBerryAbilityApi&) noexcept;
} // namespace km::restoration
