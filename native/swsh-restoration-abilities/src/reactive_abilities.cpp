// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_reactive_abilities.hpp"

namespace km::restoration {
namespace {
bool Target(void* context, uint32_t owner, const NativeReactiveAbilityApi& api) {
    return context && owner < 31 && api.event_value
        && api.event_value(context, 4) == owner;
}
bool DamagingHit(void* context, uint32_t owner, const NativeReactiveAbilityApi& api) {
    return Target(context, owner, api) && api.event_value(context, 77) == 0;
}
bool Absorb(void* context, uint32_t owner, uint32_t type, const NativeReactiveAbilityApi& api) {
    return Target(context, owner, api) && api.set_event_value
        && api.move_has_only_field_effects
        && api.event_value(context, 3) < 31 && api.event_value(context, 3) != owner
        && api.event_value(context, 22) == type
        && !api.move_has_only_field_effects(api.event_value(context, 18))
        && api.set_event_value(context, 71, 1);
}
} // namespace

bool ThermalExchangeHit(void* context, uint32_t owner, const NativeReactiveAbilityApi& api) noexcept {
    if (!DamagingHit(context, owner, api) || !api.initialize_message || !api.submit_rank
        || api.event_value(context, 22) != 9) return false;
    NativeRankRequest request{};
    request.source = static_cast<uint8_t>(owner);
    request.target_count = 1;
    request.targets[0] = static_cast<uint8_t>(owner);
    request.stat = 1;
    request.stages = 1;
    request.flags[20 - 13] = 1;
    api.initialize_message(request.message);
    return api.submit_rank(context, &request);
}

bool SeedSower(void* context, uint32_t owner, const NativeReactiveAbilityApi& api) noexcept {
    if (!DamagingHit(context, owner, api) || !api.terrain || !api.submit_terrain
        || !api.initialize_message || api.terrain(context) == 1) return false;
    NativeTerrainRequest request{};
    request.source = static_cast<uint8_t>(owner);
    request.kind = 8;
    request.terrain = 1;
    request.parameter = ((static_cast<uint64_t>(owner) << 3) & 0xf8) | 0x502;
    request.default_flags = 249;
    request.from_ability = 1;
    api.initialize_message(request.message);
    return api.submit_terrain(context, &request);
}

bool PurifyingSaltStatus(void* context, uint32_t owner, const NativeReactiveAbilityApi& api) noexcept {
    if (!context || owner >= 31 || !api.event_value || !api.set_event_value
        || !api.set_work_value) return false;
    const auto condition = api.event_value(context, 30);
    // 2..6 are sleep, paralysis, burn, poison and freeze; 14 is pending Yawn.
    const bool blocked = Target(context, owner, api)
        && ((condition >= 2 && condition <= 6) || condition == 14)
        && api.set_event_value(context, 72, 1);
    api.set_work_value(context, 0, blocked ? 1 : 0);
    return blocked;
}

bool PurifyingSaltDamage(void* context, uint32_t owner, const NativeReactiveAbilityApi& api) noexcept {
    if (!Target(context, owner, api) || !api.chain_modifier
        || api.event_value(context, 22) != 7) return false;
    api.chain_modifier(context, 56, 0x800);
    return true;
}

bool WellBakedBody(void* context, uint32_t owner, const NativeReactiveAbilityApi& api) noexcept {
    if (!api.absorb_rank || !Absorb(context, owner, 9, api)) return false;
    // The retail absorption operation also announces immunity at the stage cap.
    api.absorb_rank(context, owner, 2, 2);
    api.set_event_value(context, 117, 1);
    return true;
}

bool EarthEater(void* context, uint32_t owner, const NativeReactiveAbilityApi& api) noexcept {
    if (!api.absorb_heal || !Absorb(context, owner, 4, api)) return false;
    // Native healing uses base maximum HP and retains immunity at full HP.
    api.absorb_heal(context, owner, 4);
    return true;
}

bool GoodAsGold(void* context, uint32_t owner, const NativeReactiveAbilityApi& api) noexcept {
    if (!Target(context, owner, api) || !api.move_category || !api.set_event_value
        || !api.initialize_message || !api.set_message || !api.add_message_battler
        || !api.submit_message || !api.begin_ability || !api.end_ability
        || api.event_value(context, 3) >= 31 || api.event_value(context, 3) == owner) return false;
    const auto move = api.event_value(context, 18);
    if (!move || move > 65535 || api.move_category(move) != 0
        || !api.set_event_value(context, 71, 1)) return false;
    api.begin_ability(context, owner);
    NativeMessageRequest request{};
    request.source = static_cast<uint8_t>(owner);
    api.initialize_message(request.message);
    api.set_message(request.message, 2, 0x120);
    api.add_message_battler(request.message, owner);
    api.submit_message(context, &request);
    api.end_ability(context, owner);
    api.set_event_value(context, 117, 1);
    return true;
}

} // namespace km::restoration
