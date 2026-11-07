// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_conversion_abilities.hpp"

namespace km::restoration {
namespace {
bool CanConvert(uint32_t move) {
    // Moves whose own mechanics determine their type keep that type even when
    // it resolves to Normal. The Max move event supplies a separate identity.
    switch (move) {
    case 311: case 363: case 449: case 546: case 686: case 718: case 805:
        return false;
    default:
        return true;
    }
}
bool Valid(void* context, uint32_t owner, const NativeConversionAbilityApi& api) {
    return context && owner < 31 && api.event_value && api.set_event_value
        && api.work_value && api.set_work_value && api.chain_modifier;
}
} // namespace

bool DragonizeType(void* context, uint32_t owner, const NativeConversionAbilityApi& api) noexcept {
    if (!Valid(context, owner, api)) return false;
    api.set_work_value(context, 0, 0);
    if (api.event_value(context, 2) != owner || api.event_value(context, 22) != 0
        || !CanConvert(api.event_value(context, 18))) return false;
    const bool changed = api.set_event_value(context, 22, 15);
    api.set_work_value(context, 0, changed ? 1 : 0);
    return changed;
}

bool DragonizePower(void* context, uint32_t owner, const NativeConversionAbilityApi& api) noexcept {
    if (!Valid(context, owner, api) || api.event_value(context, 3) != owner
        || api.event_value(context, 22) != 15 || !api.work_value(context, 0)) return false;
    api.chain_modifier(context, 52, 0x1333); // Native -ate fixed-point power boost.
    return true;
}

void DragonizeReset(void* context, const NativeConversionAbilityApi& api) noexcept {
    if (context && api.set_work_value) api.set_work_value(context, 0, 0);
}

bool DragonizeMaxMove(void* context, uint32_t owner, const NativeConversionAbilityApi& api) noexcept {
    if (!Valid(context, owner, api) || api.event_value(context, 2) != owner
        || api.event_value(context, 18) != 760) return false;
    // Match the retail -ate Max mapping: Max Strike becomes Max Wyrmwind.
    // Max moves gain the Dragon identity and do not inherit the power boost.
    return api.set_event_value(context, 18, 768);
}

bool SpicySpray(void* context, uint32_t owner, const NativeAbilityApi& api) noexcept {
    if (!context || owner >= 31 || !api.event_value || !api.initialize_message
        || !api.submit_status || api.event_value(context, 4) != owner
        || api.event_value(context, 77)) return false;
    const auto attacker = api.event_value(context, 3);
    if (attacker >= 31 || attacker == owner) return false;
    NativeStatusRequest status{};
    status.source = static_cast<uint8_t>(owner);
    status.target = static_cast<uint8_t>(attacker);
    status.condition = 4;
    status.parameter = 1; // Native burn constructor's duration sentinel.
    status.flags[0] = status.flags[2] = 1;
    status.count = 1;
    api.initialize_message(status.message);
    // Native status processing owns burn immunity and existing conditions.
    // Contact and Shield Dust are irrelevant to this guaranteed hit response.
    return api.submit_status(context, &status);
}

bool AuraGuard(void* context, uint32_t owner, const NativeDamageAbilityApi& api) noexcept {
    if (!context || owner >= 31 || !api.event_value || !api.chain_modifier
        || api.event_value(context, 4) != owner || !api.event_value(context, 99)) return false;
    // The resolved contact flag includes Long Reach and move-specific rules.
    api.chain_modifier(context, 56, 0x800);
    return true;
}

} // namespace km::restoration
