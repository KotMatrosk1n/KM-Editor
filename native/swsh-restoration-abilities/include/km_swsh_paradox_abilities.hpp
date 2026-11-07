// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "km_swsh_support_abilities.hpp"
namespace km::restoration {
struct NativeConsumeItemRequest {
    uint8_t owner, skip_animation, skip_eat_notification, reserved;
    uint8_t message[36];
};
static_assert(sizeof(NativeConsumeItemRequest) == 40);
struct NativeParadoxAbilityApi {
    uint32_t (*event_value)(void*, uint32_t);
    uint32_t (*work_value)(void*, uint32_t);
    void (*set_work_value)(void*, uint32_t, uint32_t);
    void* (*battler)(void*, uint32_t);
    uint32_t (*battler_value)(void*, uint32_t);
    bool (*field_condition)(void*, uint32_t);
    uint32_t (*weather)(void*);
    uint32_t (*terrain)(void*);
    uint32_t (*held_item)(void*);
    bool (*item_usable)(void*, uint32_t);
    void (*consume_item)(void*, const NativeConsumeItemRequest*);
    void (*initialize_message)(void*);
    bool (*begin_ability)(void*, uint32_t);
    bool (*end_ability)(void*, uint32_t);
    void (*chain_modifier)(void*, uint32_t, uint32_t);
};
void ParadoxRefresh(void*, uint32_t, bool, const NativeParadoxAbilityApi&) noexcept;
void ParadoxReset(void*, uint32_t, const NativeParadoxAbilityApi&) noexcept;
void ParadoxStat(void*, uint32_t, uint32_t, const NativeParadoxAbilityApi&) noexcept;
} // namespace km::restoration
