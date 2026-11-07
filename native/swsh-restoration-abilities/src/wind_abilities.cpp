// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_wind_abilities.hpp"

namespace km::restoration {
namespace {
bool RaiseAttack(void* context, uint32_t owner, const NativeWindAbilityApi& api) {
    NativeRankRequest request{};
    request.source = static_cast<uint8_t>(owner);
    request.target_count = 1;
    request.targets[0] = static_cast<uint8_t>(owner);
    request.stat = 1;
    request.stages = 1;
    request.flags[20 - 13] = 1;
    api.initialize_message(request.message);
    api.begin_ability(context, owner);
    const bool accepted = api.submit_rank(context, &request);
    api.end_ability(context, owner);
    return accepted;
}
void* FindCharge(void* manager, uint32_t owner, const NativePersistentChargeApi& api) {
    auto* list = api.work_list(manager);
    for (auto* work = api.first_work(list, 0, owner); work;
            work = api.next_work(list, work, 0)) {
        if (api.work_identity(work) == 268) return work;
    }
    return nullptr;
}
} // namespace

bool IsWindMove(uint32_t move) noexcept {
    // Scarlet/Violet waza_array field 55, including status wind moves.
    switch (move) {
    case 16: case 18: case 59: case 177: case 196: case 201: case 239:
    case 257: case 314: case 366: case 542: case 572: case 584: case 831:
    case 846: case 847: case 848:
        return true;
    default:
        return false;
    }
}

bool WindRiderEntry(void* context, uint32_t owner, const NativeWindAbilityApi& api) noexcept {
    if (!context || owner >= 31 || api.event_value(context, 2) != owner
        || !api.tailwind_active(context, owner)) return false;
    return RaiseAttack(context, owner, api);
}

bool WindRiderImmunity(void* context, uint32_t owner, const NativeWindAbilityApi& api) noexcept {
    if (!context || owner >= 31 || api.event_value(context, 4) != owner
        || api.event_value(context, 3) >= 31 || api.event_value(context, 3) == owner
        || !IsWindMove(api.event_value(context, 18))
        || api.move_has_only_field_effects(api.event_value(context, 18))
        || !api.set_event_value(context, 71, 1)) return false;
    api.absorb_rank(context, owner, 1, 1);
    api.set_event_value(context, 117, 1);
    return true;
}

bool WindChargeHit(void* context, uint32_t owner, bool wind_only, const NativeWindAbilityApi& api) noexcept {
    if (!context || owner >= 31 || api.event_value(context, 4) != owner
        || api.event_value(context, 77)
        || (wind_only && !IsWindMove(api.event_value(context, 18)))) return false;
    return api.grant_charge(context, owner);
}

void WindTailwindStarted(void* context, uint32_t side, const NativeWindAbilityApi& api) noexcept {
    if (!context || side >= 3) return;
    const NativeBattlerSelector all{6, 0, {}};
    uint8_t owners[31]{};
    const auto count = api.select_battlers(context, &all, owners);
    if (count > 31) return;
    for (uint32_t i = 0; i < count; ++i) {
        const auto owner = owners[i];
        if (owner >= 31 || api.side(context, owner) != side) continue;
        auto* battler = api.battler(context, owner);
        if (!battler || !api.battler_value(battler, 13)) continue;
        switch (api.battler_value(battler, 19)) {
        case 274: RaiseAttack(context, owner, api); break;
        case 277: api.grant_charge(context, owner); break;
        }
    }
}

bool GrantPersistentCharge(void* context, uint32_t owner, const NativePersistentChargeApi& api) noexcept {
    if (!context || owner >= 31) return false;
    // This is the retail event manager in the verified 72-byte callback context.
    auto* manager = reinterpret_cast<void**>(context)[6];
    if (!manager) return false;
    auto* work = FindCharge(manager, owner, api);
    if (!work) {
        auto* battler = api.battler(context, owner);
        if (!battler || !api.register_move(manager, battler, 268, 0)) return false;
        work = FindCharge(manager, owner, api);
        if (!work) return false;
    }
    // Refresh the same effect instead of multiplying two separate modifiers.
    api.set_work(work, 0, 1);
    api.set_work(work, 1, PersistentChargeMarker);
    api.set_work(work, 6, 1);
    api.begin_ability(context, owner);
    NativeConditionRequest condition{};
    condition.target = static_cast<uint8_t>(owner);
    condition.condition = 20;
    api.add_condition(context, &condition);
    NativeMessageRequest message{};
    message.source = static_cast<uint8_t>(owner);
    api.initialize_message(message.message);
    api.set_message(message.message, 2, 0x380);
    api.add_message_argument(message.message, owner);
    api.submit_message(context, &message);
    api.end_ability(context, owner);
    return true;
}

void PersistentChargeBeforeMove(void* context, uint32_t owner, const NativePersistentChargeApi& api) noexcept {
    if (api.event_work(context, 1) != PersistentChargeMarker)
        api.original_before_move(context, owner);
}

void PersistentChargeResolvedType(void* context, uint32_t owner, const NativePersistentChargeApi& api) noexcept {
    // Event 43 runs after all type conversions and after the type is locked.
    // It covers Electric status moves as well as damaging Electric moves.
    if (api.event_work(context, 1) == PersistentChargeMarker
        && api.event_value(context, 2) == owner
        && api.event_value(context, 18) != 268
        && api.event_value(context, 22) == 12
        && api.event_work(context, 0) == 1)
        api.set_event_work(context, 0, 2);
}

} // namespace km::restoration
