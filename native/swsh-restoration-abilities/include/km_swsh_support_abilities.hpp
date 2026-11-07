// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "km_swsh_restoration_abilities.hpp"

namespace km::restoration {

struct alignas(8) NativeAbilityChangeRequest {
    uint8_t source;
    uint8_t target;
    uint16_t reserved;
    uint32_t ability;
    uint8_t flags[4];
    uint8_t message[36];
};
static_assert(sizeof(NativeAbilityChangeRequest) == 48);
static_assert(offsetof(NativeAbilityChangeRequest, message) == 12);

struct alignas(8) NativeSideConditionRequest {
    uint8_t source;
    uint8_t reserved[3];
    uint32_t condition;
    uint64_t parameter;
    uint8_t side;
    uint8_t reserved2;
    uint16_t default_flags;
    uint8_t message[36];
    uint8_t announce_ability;
    uint8_t reserved3[7];
};
static_assert(sizeof(NativeSideConditionRequest) == 64);
static_assert(offsetof(NativeSideConditionRequest, message) == 20);
static_assert(offsetof(NativeSideConditionRequest, announce_ability) == 56);

struct alignas(8) NativeHealRequest {
    uint8_t source;
    uint8_t target;
    uint16_t amount;
    uint8_t flags[7];
    uint8_t reserved;
    uint8_t message[36];
};
static_assert(sizeof(NativeHealRequest) == 48);
static_assert(offsetof(NativeHealRequest, message) == 12);

struct NativeBattlerSelector {
    uint32_t kind;
    uint8_t position;
    uint8_t reserved[3];
};
static_assert(sizeof(NativeBattlerSelector) == 8);

struct NativeSupportAbilityApi {
    uint32_t (*event_value)(void*, uint32_t);
    void* (*battler)(void*, uint32_t);
    uint32_t (*battler_value)(void*, uint32_t);
    bool (*contact_effect_blocked)(void*);
    bool (*cannot_replace_ability)(uint32_t);
    bool (*same_side)(void*, uint32_t, uint32_t);
    uint32_t (*opposing_side)(void*, uint32_t);
    uint32_t (*position)(void*, uint32_t);
    uint32_t (*select_battlers)(void*, const NativeBattlerSelector*, uint8_t*);
    bool (*full_hp)(void*);
    uint32_t (*hp_fraction)(void*, uint32_t, bool);
    uint32_t (*move_category)(uint32_t);
    void (*initialize_message)(void*);
    void (*set_message)(void*, uint32_t, uint32_t);
    void (*add_message_argument)(void*, uint32_t);
    bool (*submit_ability)(void*, const NativeAbilityChangeRequest*);
    bool (*submit_side_condition)(void*, const NativeSideConditionRequest*);
    bool (*submit_heal)(void*, const NativeHealRequest*);
};

bool LingeringAroma(void*, uint32_t, const NativeSupportAbilityApi&) noexcept;
bool ToxicDebris(void*, uint32_t, const NativeSupportAbilityApi&) noexcept;
bool Hospitality(void*, uint32_t, const NativeSupportAbilityApi&) noexcept;

} // namespace km::restoration
