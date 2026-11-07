// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_support_abilities.hpp"

namespace km::restoration {
namespace {
bool Hit(void* context, uint32_t owner, const NativeSupportAbilityApi& api) {
    return context && owner < 31 && api.event_value
        && api.event_value(context, 4) == owner && !api.event_value(context, 77);
}
bool Messages(const NativeSupportAbilityApi& api) {
    return api.initialize_message && api.set_message && api.add_message_argument;
}
} // namespace

bool LingeringAroma(void* context, uint32_t owner, const NativeSupportAbilityApi& api) noexcept {
    if (!Hit(context, owner, api) || !Messages(api) || !api.battler || !api.battler_value
        || !api.contact_effect_blocked || !api.cannot_replace_ability || !api.same_side
        || !api.submit_ability || !api.event_value(context, 99)
        || api.contact_effect_blocked(context)) return false;
    const auto attacker = api.event_value(context, 3);
    if (attacker >= 31 || attacker == owner) return false;
    auto* battler = api.battler(context, attacker);
    if (!battler) return false;
    const auto ability = api.battler_value(battler, 18);
    // Form abilities added after Sword/Shield are absent from its immutable
    // replacement property. Preserve their source cantsuppress behavior here.
    if (ability == 268 || ability == 278 || ability == 307 || ability == 308 || ability == 310
        || api.cannot_replace_ability(ability)) return false;
    NativeAbilityChangeRequest request{};
    request.source = static_cast<uint8_t>(owner);
    request.target = static_cast<uint8_t>(attacker);
    request.ability = 268;
    request.flags[0] = 1;
    request.flags[3] = !api.same_side(context, owner, attacker);
    api.initialize_message(request.message);
    // The generic copied-ability announcement takes target, source, ability.
    // Mummy's announcement contains a literal localized ability name.
    api.set_message(request.message, 2, 0x342);
    api.add_message_argument(request.message, attacker);
    api.add_message_argument(request.message, owner);
    api.add_message_argument(request.message, 268);
    return api.submit_ability(context, &request);
}

bool ToxicDebris(void* context, uint32_t owner, const NativeSupportAbilityApi& api) noexcept {
    if (!Hit(context, owner, api) || !Messages(api) || !api.move_category
        || !api.opposing_side || !api.submit_side_condition
        || api.move_category(api.event_value(context, 18)) != 1) return false;
    const auto side = api.opposing_side(context, owner);
    if (side > 1) return false;
    NativeSideConditionRequest request{};
    request.source = static_cast<uint8_t>(owner);
    request.condition = 7;
    request.parameter = PoisonParameter(owner, false);
    // Always the holder's opposing side, including a hit from its own ally.
    request.side = static_cast<uint8_t>(side);
    request.default_flags = 249;
    request.announce_ability = 1;
    api.initialize_message(request.message);
    api.set_message(request.message, 1, 0xad);
    api.add_message_argument(request.message, side);
    // The native side-condition operation owns the two-layer cap and only
    // announces an accepted addition.
    return api.submit_side_condition(context, &request);
}

bool Hospitality(void* context, uint32_t owner, const NativeSupportAbilityApi& api) noexcept {
    if (!context || owner >= 31 || !api.event_value || !Messages(api) || !api.battler
        || !api.position || !api.select_battlers || !api.full_hp || !api.hp_fraction
        || !api.submit_heal || api.event_value(context, 2) != owner) return false;
    NativeBattlerSelector selector{5, static_cast<uint8_t>(api.position(context, owner)), {}};
    uint8_t allies[6]{};
    const auto count = api.select_battlers(context, &selector, allies);
    if (count > 6) return false;
    bool submitted = false;
    for (uint32_t i = 0; i < count; ++i) {
        const auto target = allies[i];
        if (target >= 31 || target == owner) continue;
        auto* battler = api.battler(context, target);
        if (!battler || api.full_hp(battler)) continue;
        NativeHealRequest request{};
        request.source = static_cast<uint8_t>(owner);
        request.target = target;
        request.amount = static_cast<uint16_t>(api.hp_fraction(battler, 4, true));
        request.flags[6 - 4] = 1;
        request.flags[8 - 4] = 1;
        request.flags[9 - 4] = 1;
        api.initialize_message(request.message);
        api.set_message(request.message, 2, 0x20c);
        api.add_message_argument(request.message, target);
        submitted = api.submit_heal(context, &request) || submitted;
    }
    return submitted;
}

} // namespace km::restoration
