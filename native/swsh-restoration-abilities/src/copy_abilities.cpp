// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_copy_abilities.hpp"

namespace km::restoration {

bool Costar(void* context, uint32_t owner, const NativeCopyAbilityApi& api) noexcept {
    if (!context || owner >= 31 || !api.event_value || !api.battler || !api.battler_value
        || !api.position || !api.select_battlers || !api.critical_stage || !api.condition
        || !api.status || !api.status_parameter || !api.submit_copy_rank || !api.add_condition
        || !api.remove_condition || !api.submit_cure || !api.submit_status || !api.initialize_message
        || !api.set_message || !api.add_message_argument || !api.submit_message
        || !api.begin_ability || !api.end_ability || api.event_value(context, 2) != owner) return false;
    auto* holder = api.battler(context, owner);
    if (!holder) return false;
    NativeBattlerSelector selector{5, static_cast<uint8_t>(api.position(context, owner)), {}};
    uint8_t allies[6]{};
    const auto count = api.select_battlers(context, &selector, allies);
    if (count > 6) return false;
    for (uint32_t i = 0; i < count; ++i) {
        const auto target = allies[i];
        if (target >= 31 || target == owner) continue;
        auto* ally = api.battler(context, target);
        if (!ally) continue;
        NativeCopyRankRequest ranks{};
        ranks.target = static_cast<uint8_t>(owner);
        for (uint32_t stat = 1; stat <= 7; ++stat)
            ranks.stages[stat - 1] = static_cast<uint8_t>(api.battler_value(ally, stat));
        // The native critical stage includes the G-Max Chi Strike counter.
        ranks.critical_stage = static_cast<uint8_t>(api.critical_stage(ally));
        const bool focus_energy = api.condition(ally, 9);
        const bool laser_focus = api.status(ally, 41);
        const auto laser_parameter = laser_focus ? api.status_parameter(ally, 41) : 0;
        api.begin_ability(context, owner);
        api.submit_copy_rank(context, &ranks);
        NativeConditionRequest condition{static_cast<uint8_t>(owner), {}, 9};
        if (focus_energy) api.add_condition(context, &condition);
        else api.remove_condition(context, &condition);
        // A previous Laser Focus must also disappear when the ally lacks it.
        // Bit conditions and timed statuses use distinct native namespaces.
        if (api.status(holder, 41)) {
            NativeStatusCureRequest cure{};
            cure.source = static_cast<uint8_t>(owner);
            cure.condition = 41;
            cure.targets[0] = static_cast<uint8_t>(owner);
            cure.target_count = 1;
            cure.custom_message = 1;
            api.initialize_message(cure.message);
            api.submit_cure(context, &cure);
        }
        if (laser_focus) {
            NativeStatusRequest status{};
            status.source = status.target = static_cast<uint8_t>(owner);
            status.condition = 41;
            status.parameter = laser_parameter;
            status.count = 1;
            api.initialize_message(status.message);
            api.submit_status(context, &status);
        }
        NativeMessageRequest message{};
        message.source = static_cast<uint8_t>(owner);
        api.initialize_message(message.message);
        api.set_message(message.message, 2, 0x58a);
        api.add_message_argument(message.message, owner);
        api.add_message_argument(message.message, target);
        api.submit_message(context, &message);
        api.end_ability(context, owner);
        return true;
    }
    return false;
}

} // namespace km::restoration
