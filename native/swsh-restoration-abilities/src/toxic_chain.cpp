// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_restoration_abilities.hpp"

namespace km::restoration {

bool ToxicChain(void* context, uint32_t owner, const NativeAbilityApi& api) noexcept {
    if (!context || owner >= 31 || !api.event_value || !api.percent_chance
        || !api.poison_parameter || !api.initialize_message || !api.set_message
        || !api.add_message_battler || !api.submit_status) return false;

    // Event97 lets Shield Dust set variable78 before event98 is dispatched.
    // Variable77 suppresses the native contact/status reactions as well. Keep
    // both gates, while accepting damaging hits independently of contact(99).
    if (api.event_value(context, 3) != owner
        || api.event_value(context, 77) != 0
        || api.event_value(context, 78) != 0) return false;
    if (!api.percent_chance(30)) return false;
    const auto target = api.event_value(context, 4);
    if (target >= 31 || target == owner) return false;

    NativeStatusRequest request{};
    request.source = static_cast<uint8_t>(owner);
    request.target = static_cast<uint8_t>(target);
    request.condition = 5;
    request.parameter = api.poison_parameter(owner, true);
    request.flags[2] = 1;
    request.count = 1;
    api.initialize_message(request.message);
    // btl_set 324 is the retail badly-poisoned announcement, including its
    // four contextual variants. Poison Touch's 638 announces ordinary poison.
    api.set_message(request.message, 2, 324);
    api.add_message_battler(request.message, target);
    return api.submit_status(context, &request);
}

} // namespace km::restoration
