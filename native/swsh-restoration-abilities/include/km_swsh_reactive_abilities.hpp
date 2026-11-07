// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "km_swsh_restoration_abilities.hpp"

namespace km::restoration {

struct alignas(8) NativeMessageRequest {
    uint8_t source;
    uint8_t flags;
    uint16_t reserved;
    uint8_t message[36];
};
static_assert(sizeof(NativeMessageRequest) == 40);
static_assert(offsetof(NativeMessageRequest, message) == 4);

struct alignas(8) NativeTerrainRequest {
    uint8_t source;
    uint8_t reserved[3];
    uint32_t kind;
    uint8_t terrain;
    uint8_t reserved2[7];
    uint64_t parameter;
    uint16_t default_flags;
    uint16_t reserved3;
    uint8_t message[36];
    uint16_t reserved4;
    uint8_t from_ability;
    uint8_t reserved5[5];
};
static_assert(sizeof(NativeTerrainRequest) == 72);
static_assert(offsetof(NativeTerrainRequest, parameter) == 16);
static_assert(offsetof(NativeTerrainRequest, message) == 28);
static_assert(offsetof(NativeTerrainRequest, from_ability) == 66);

struct NativeReactiveAbilityApi {
    uint32_t (*event_value)(void*, uint32_t);
    bool (*set_event_value)(void*, uint32_t, uint32_t);
    void (*set_work_value)(void*, uint32_t, uint32_t);
    void (*chain_modifier)(void*, uint32_t, uint32_t);
    uint32_t (*move_category)(uint32_t);
    bool (*move_has_only_field_effects)(uint32_t);
    void (*absorb_heal)(void*, uint32_t, uint32_t);
    void (*absorb_rank)(void*, uint32_t, uint32_t, int32_t);
    void (*initialize_message)(void*);
    void (*set_message)(void*, uint32_t, uint32_t);
    void (*add_message_battler)(void*, uint32_t);
    bool (*submit_message)(void*, const NativeMessageRequest*);
    bool (*begin_ability)(void*, uint32_t);
    bool (*end_ability)(void*, uint32_t);
    bool (*submit_rank)(void*, const NativeRankRequest*);
    uint32_t (*terrain)(void*);
    bool (*submit_terrain)(void*, const NativeTerrainRequest*);
};

// Event98 is the retail damaging-hit event; variable77 identifies a Substitute.
bool ThermalExchangeHit(void*, uint32_t, const NativeReactiveAbilityApi&) noexcept;
bool SeedSower(void*, uint32_t, const NativeReactiveAbilityApi&) noexcept;
// Purifying Salt prevents incoming conditions without curing an existing one.
bool PurifyingSaltStatus(void*, uint32_t, const NativeReactiveAbilityApi&) noexcept;
bool PurifyingSaltDamage(void*, uint32_t, const NativeReactiveAbilityApi&) noexcept;
bool WellBakedBody(void*, uint32_t, const NativeReactiveAbilityApi&) noexcept;
bool EarthEater(void*, uint32_t, const NativeReactiveAbilityApi&) noexcept;
bool GoodAsGold(void*, uint32_t, const NativeReactiveAbilityApi&) noexcept;

} // namespace km::restoration
