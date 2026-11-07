// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "km_swsh_support_abilities.hpp"
#include "km_swsh_reactive_abilities.hpp"

namespace km::restoration {

struct NativeCopyRankRequest {
    uint8_t target;
    uint8_t stages[7];
    uint8_t critical_stage;
};
static_assert(sizeof(NativeCopyRankRequest) == 9);

struct NativeConditionRequest {
    uint8_t target;
    uint8_t reserved[3];
    uint32_t condition;
};
static_assert(sizeof(NativeConditionRequest) == 8);

struct alignas(8) NativeStatusCureRequest {
    uint8_t source;
    uint8_t reserved[3];
    uint32_t condition;
    uint8_t targets[30];
    uint8_t target_count;
    uint8_t reserved2;
    uint16_t message_style;
    uint8_t announce_ability;
    uint8_t custom_message;
    uint8_t message[36];
};
static_assert(sizeof(NativeStatusCureRequest) == 80);
static_assert(offsetof(NativeStatusCureRequest, target_count) == 38);
static_assert(offsetof(NativeStatusCureRequest, message) == 44);

struct NativeCopyAbilityApi {
    uint32_t (*event_value)(void*, uint32_t);
    void* (*battler)(void*, uint32_t);
    uint32_t (*battler_value)(void*, uint32_t);
    uint32_t (*position)(void*, uint32_t);
    uint32_t (*select_battlers)(void*, const NativeBattlerSelector*, uint8_t*);
    uint32_t (*critical_stage)(void*);
    bool (*condition)(void*, uint32_t);
    bool (*status)(void*, uint32_t);
    uint64_t (*status_parameter)(void*, uint32_t);
    bool (*submit_copy_rank)(void*, const NativeCopyRankRequest*);
    bool (*add_condition)(void*, const NativeConditionRequest*);
    bool (*remove_condition)(void*, const NativeConditionRequest*);
    bool (*submit_cure)(void*, const NativeStatusCureRequest*);
    bool (*submit_status)(void*, const NativeStatusRequest*);
    void (*initialize_message)(void*);
    void (*set_message)(void*, uint32_t, uint32_t);
    void (*add_message_argument)(void*, uint32_t);
    bool (*submit_message)(void*, const NativeMessageRequest*);
    bool (*begin_ability)(void*, uint32_t);
    bool (*end_ability)(void*, uint32_t);
};

// Entry/gained-ability events, after ordinary entry stat changes. Uses native
// copying rather than rank increments, bypassing Contrary, Simple and caps.
bool Costar(void*, uint32_t, const NativeCopyAbilityApi&) noexcept;

} // namespace km::restoration
