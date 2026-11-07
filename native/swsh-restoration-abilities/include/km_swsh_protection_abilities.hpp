// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "km_swsh_wind_abilities.hpp"
namespace km::restoration {
struct NativeProtectionAbilityApi {
    NativePersistentChargeApi work;
    uint32_t (*event_value)(void*, uint32_t);
    void (*set_work)(void*, uint32_t, uint32_t);
    uint32_t (*work_value)(void*, uint32_t);
    void* (*battler)(void*, uint32_t);
    bool (*turn_flag)(void*, uint32_t);
    bool (*move_flag)(uint32_t, uint32_t);
    bool (*damaging_move)(uint32_t);
    uint32_t (*side)(void*, uint32_t);
    bool (*side_condition)(void*, uint32_t, uint32_t);
    void (*bypass_begin)(void*, uint32_t);
    void (*bypass_end)(void*, uint32_t);
    void (*chain_modifier)(void*, uint32_t, uint32_t);
    bool (*set_event)(void*, uint32_t, uint32_t);
};
void PiercingDrillBegin(void*, uint32_t, const NativeProtectionAbilityApi&) noexcept;
void PiercingDrillEnd(void*, uint32_t, const NativeProtectionAbilityApi&) noexcept;
void PiercingDrillDamage(void*, uint32_t, bool, const NativeProtectionAbilityApi&) noexcept;
}
