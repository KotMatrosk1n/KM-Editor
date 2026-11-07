// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "km_swsh_battle_state_abilities.hpp"

namespace km::restoration {
struct NativeFormRequest {
    uint8_t owner, form, reset_ability, allow_fainted, refresh_model, show_message, reserved[2];
    uint8_t message[36];
};
static_assert(sizeof(NativeFormRequest) == 44);
struct NativeFormAbilityApi {
    uint32_t (*event_value)(void*, uint32_t);
    void* (*battler)(void*, uint32_t);
    uint32_t (*species)(void*);
    bool (*transformed)(void*);
    bool (*change_form)(void*, const NativeFormRequest*);
    void (*remove_ability)(void*, uint32_t);
    void (*set_ability)(void*, uint32_t, uint32_t);
    void (*register_ability)(void*, uint32_t, uint32_t);
    void (*initialize_message)(void*);
    void (*set_message)(void*, uint32_t, uint32_t);
    void (*add_message_battler)(void*, uint32_t);
    bool (*begin_ability)(void*, uint32_t);
    bool (*end_ability)(void*, uint32_t);
};
bool ZeroToHeroWithdraw(void*, uint32_t, const NativeFormAbilityApi&) noexcept;
bool ZeroToHeroEntry(void*, uint32_t, const NativeFormAbilityApi&) noexcept;
bool TeraShift(void*, uint32_t, const NativeFormAbilityApi&) noexcept;
} // namespace km::restoration
