// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "km_swsh_reactive_abilities.hpp"
#include "km_swsh_support_abilities.hpp"
#include "km_swsh_copy_abilities.hpp"

namespace km::restoration {

struct NativeWindAbilityApi {
    uint32_t (*event_value)(void*, uint32_t);
    bool (*set_event_value)(void*, uint32_t, uint32_t);
    void* (*battler)(void*, uint32_t);
    uint32_t (*battler_value)(void*, uint32_t);
    uint32_t (*side)(void*, uint32_t);
    bool (*tailwind_active)(void*, uint32_t);
    bool (*move_has_only_field_effects)(uint32_t);
    void (*absorb_rank)(void*, uint32_t, uint32_t, int32_t);
    bool (*grant_charge)(void*, uint32_t);
    uint32_t (*select_battlers)(void*, const NativeBattlerSelector*, uint8_t*);
    void (*initialize_message)(void*);
    bool (*submit_rank)(void*, const NativeRankRequest*);
    bool (*begin_ability)(void*, uint32_t);
    bool (*end_ability)(void*, uint32_t);
};

// Both abilities use the native Charge move work. The marker selects the newer
// consumption rule without changing Charge used by an ordinary SwSh Pokemon.
inline constexpr uint32_t PersistentChargeMarker = 0x4b4d4348;
struct NativePersistentChargeApi {
    void* (*work_list)(void*);
    void* (*first_work)(void*, uint32_t, uint32_t);
    void* (*next_work)(void*, void*, uint32_t);
    uint32_t (*work_identity)(void*);
    bool (*register_move)(void*, void*, uint32_t, uint32_t);
    void (*set_work)(void*, uint32_t, uint32_t);
    uint32_t (*work_value)(void*, uint32_t);
    uint32_t (*event_value)(void*, uint32_t);
    void (*set_event_work)(void*, uint32_t, uint32_t);
    uint32_t (*event_work)(void*, uint32_t);
    void* (*battler)(void*, uint32_t);
    bool (*add_condition)(void*, const NativeConditionRequest*);
    void (*initialize_message)(void*);
    void (*set_message)(void*, uint32_t, uint32_t);
    void (*add_message_argument)(void*, uint32_t);
    bool (*submit_message)(void*, const NativeMessageRequest*);
    bool (*begin_ability)(void*, uint32_t);
    bool (*end_ability)(void*, uint32_t);
    void (*original_before_move)(void*, uint32_t);
};

bool IsWindMove(uint32_t) noexcept;
bool WindRiderEntry(void*, uint32_t, const NativeWindAbilityApi&) noexcept;
bool WindRiderImmunity(void*, uint32_t, const NativeWindAbilityApi&) noexcept;
bool WindChargeHit(void*, uint32_t, bool wind_only, const NativeWindAbilityApi&) noexcept;
void WindTailwindStarted(void*, uint32_t side, const NativeWindAbilityApi&) noexcept;
bool GrantPersistentCharge(void*, uint32_t, const NativePersistentChargeApi&) noexcept;
void PersistentChargeBeforeMove(void*, uint32_t, const NativePersistentChargeApi&) noexcept;
void PersistentChargeResolvedType(void*, uint32_t, const NativePersistentChargeApi&) noexcept;

} // namespace km::restoration
