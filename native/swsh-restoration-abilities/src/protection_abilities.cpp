// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_protection_abilities.hpp"
namespace km::restoration {
namespace {
bool Obstructing(void* context, uint32_t target, const NativeProtectionAbilityApi& api) {
    auto* list = api.work.work_list(reinterpret_cast<void**>(context)[6]);
    for (auto* work = api.work.first_work(list, 0, target); work;
         work = api.work.next_work(list, work, false)) {
        const auto flags = *reinterpret_cast<const uint32_t*>(static_cast<const uint8_t*>(work) + 0x3c);
        if (!(flags & ((1u << 27) | (1u << 29)))
            && api.work.work_identity(work) == 743 && api.work.work_value(work, 6)) return true;
    }
    return false;
}
}
void PiercingDrillBegin(void* context, uint32_t owner, const NativeProtectionAbilityApi& api) noexcept {
    if (!context || owner >= 31 || api.event_value(context, 3) != owner) return;
    api.set_work(context, 0, 0);
    if (!api.event_value(context, 99)) return;
    const auto move = api.event_value(context, 18);
    uint32_t mask = 0;
    // Capture each target before the retail Unseen Fist operation temporarily
    // disables side shields. A spread move may hit protected and exposed foes.
    if (api.move_flag(move, 3)) {
        const auto count = api.event_value(context, 5);
        if (count <= 31) for (uint32_t i = 0; i < count; ++i) {
            const auto target = api.event_value(context, 6 + i);
            if (target >= 31 || target == owner) continue;
            auto* battler = api.battler(context, target);
            if (!battler) continue;
            const bool damaging = api.damaging_move(move);
            bool protected_target = api.turn_flag(battler, 7)
                && (!api.turn_flag(battler, 16) || damaging);
            protected_target |= damaging && Obstructing(context, target, api);
            const auto side = api.side(context, target);
            const auto targets = api.event_value(context, 28);
            protected_target |= (targets | 1u) == 5 && api.side_condition(context, side, 9);
            protected_target |= static_cast<int32_t>(api.event_value(context, 25)) >= 1
                && api.side_condition(context, side, 10);
            protected_target |= damaging && api.side_condition(context, side, 15);
            protected_target |= !damaging && targets != 6 && targets != 8
                && api.side_condition(context, side, 16);
            if (protected_target) mask |= uint32_t{1} << target;
        }
    }
    api.set_work(context, 0, mask);
    api.bypass_begin(context, owner);
}
void PiercingDrillEnd(void* context, uint32_t owner, const NativeProtectionAbilityApi& api) noexcept {
    if (!context || owner >= 31 || api.event_value(context, 3) != owner) return;
    api.bypass_end(context, owner);
    api.set_work(context, 0, 0);
}
void PiercingDrillDamage(void* context, uint32_t owner, bool fixed, const NativeProtectionAbilityApi& api) noexcept {
    if (!context || owner >= 31 || api.event_value(context, 3) != owner
        || !api.event_value(context, 99)) return;
    const auto target = api.event_value(context, 4);
    if (target >= 31 || !(api.work_value(context, 0) & (uint32_t{1} << target))) return;
    // Use the native protection multiplier for fixed damage, and the final
    // damage modifier for ordinary attacks. Never stack two quarter penalties.
    if (fixed) api.set_event(context, 57, 1024);
    else api.chain_modifier(context, 56, 1024);
}
}
