// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "km_swsh_restoration_abilities.hpp"

namespace km::restoration {

struct NativeConversionAbilityApi {
    uint32_t (*event_value)(void*, uint32_t);
    bool (*set_event_value)(void*, uint32_t, uint32_t);
    uint32_t (*work_value)(void*, uint32_t);
    void (*set_work_value)(void*, uint32_t, uint32_t);
    void (*chain_modifier)(void*, uint32_t, uint32_t);
};

bool DragonizeType(void*, uint32_t, const NativeConversionAbilityApi&) noexcept;
bool DragonizePower(void*, uint32_t, const NativeConversionAbilityApi&) noexcept;
void DragonizeReset(void*, const NativeConversionAbilityApi&) noexcept;
bool DragonizeMaxMove(void*, uint32_t, const NativeConversionAbilityApi&) noexcept;
bool SpicySpray(void*, uint32_t, const NativeAbilityApi&) noexcept;
bool AuraGuard(void*, uint32_t, const NativeDamageAbilityApi&) noexcept;

} // namespace km::restoration
