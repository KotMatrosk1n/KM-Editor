// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <stddef.h>
#include <stdint.h>

namespace km::restoration {

// Native queued status operation. Message storage has 36 meaningful bytes and
// four bytes of alignment, as used by the retail battle callbacks.
struct alignas(8) NativeStatusRequest {
    uint8_t source;
    uint8_t target;
    uint16_t reserved;
    uint32_t condition;
    uint64_t parameter;
    uint8_t flags[4];
    uint32_t count;
    uint8_t message[40];
};
static_assert(sizeof(NativeStatusRequest) == 64);
static_assert(offsetof(NativeStatusRequest, condition) == 4);
static_assert(offsetof(NativeStatusRequest, parameter) == 8);
static_assert(offsetof(NativeStatusRequest, flags) == 16);
static_assert(offsetof(NativeStatusRequest, count) == 20);
static_assert(offsetof(NativeStatusRequest, message) == 24);

struct NativeAbilityApi {
    uint32_t (*event_value)(void* context, uint32_t variable);
    bool (*percent_chance)(uint32_t percentage);
    // The binding must preserve the native poison severity bits; it is not a
    // constructor for an unrelated linked battle condition.
    uint64_t (*poison_parameter)(uint32_t source, bool severe);
    void (*initialize_message)(void* message);
    void (*set_message)(void* message, uint32_t kind, uint32_t text);
    void (*add_message_battler)(void* message, uint32_t battler);
    bool (*submit_status)(void* context, const NativeStatusRequest* request);
};

// Retail constructors 7ed120/7ed1b0 preserve the five-bit source and use the
// poison duration sentinel 15 for progressive poison. The age starts at zero;
// the native status engine maintains it after the request is accepted.
constexpr uint64_t PoisonParameter(uint32_t source, bool severe) noexcept {
    return ((static_cast<uint64_t>(source) << 3) & 0xf8)
        | (severe ? 0xf01ULL : 1ULL);
}

// Called only for the native damaging-hit event (98), with ability priority 6.
// Native status processing owns type/ability immunity, existing status, Safeguard,
// fainting, attribution, announcements and the progressive poison counter.
bool ToxicChain(void* context, uint32_t owner, const NativeAbilityApi& api) noexcept;

struct NativeDamageAbilityApi {
    uint32_t (*event_value)(void* context, uint32_t variable);
    void (*chain_modifier)(void* context, uint32_t variable, uint32_t multiplier);
};

// Register at the native base-power event (72) and offensive-stat event (76),
// respectively. The engine owns suppression, damage categories and rounding.
bool Sharpness(void* context, uint32_t owner, const NativeDamageAbilityApi& api) noexcept;
bool RockyPayload(void* context, uint32_t owner, const NativeDamageAbilityApi& api) noexcept;
bool FireMane(void* context, uint32_t owner, const NativeDamageAbilityApi& api) noexcept;

// Retail stat-stage operation, as queued by Intimidate and Defiant. The message
// begins at byte36; no native message storage is omitted from the operation.
struct alignas(8) NativeRankRequest {
    uint8_t source;
    uint8_t target_count;
    uint8_t targets[6];
    uint32_t stat;
    int8_t stages;
    uint8_t flags[23];
    uint8_t message[36];
};
static_assert(sizeof(NativeRankRequest) == 72);
static_assert(offsetof(NativeRankRequest, stat) == 8);
static_assert(offsetof(NativeRankRequest, stages) == 12);
static_assert(offsetof(NativeRankRequest, message) == 36);

struct NativeGuardDogApi {
    uint32_t (*event_value)(void*, uint32_t);
    bool (*set_event_value)(void*, uint32_t, uint32_t);
    void (*initialize_message)(void*);
    bool (*submit_rank)(void*, const NativeRankRequest*);
    bool (*begin_ability)(void*, uint32_t);
    bool (*end_ability)(void*, uint32_t);
    void* (*battler)(void*, uint32_t);
    uint32_t (*battler_value)(void*, uint32_t);
};

// Called at Intimidate's rank submission, before Mist and stat caps can discard
// its targets. Other rank submissions continue to use the native entrypoint.
bool GuardDogIntimidate(void* context, const NativeRankRequest* request,
    const NativeGuardDogApi& api) noexcept;
// Event176 is the native forced-switch check. Native ability filtering must
// classify Guard Dog as breakable before either callback is registered.
bool GuardDogForcedSwitch(void* context, uint32_t owner, const NativeGuardDogApi& api) noexcept;

} // namespace km::restoration
