// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_effectiveness_abilities.hpp"

namespace km::restoration {
bool TeraShell(void* context, uint32_t owner, const NativeEffectivenessAbilityApi& api) noexcept {
    if (!context || owner >= 31 || api.event_value(context, 4) != owner
        || api.event_value(context, 22) >= 18 || !api.event_value(context, 61)) return false;
    auto* defender = api.battler(context, owner);
    if (!defender || !api.full_hp(defender)) return false;
    // Native values: 0 immune, 7 neutral, 6 half. Override the combined matchup,
    // preserving immunity and avoiding a separate half modifier for each type.
    return api.set_event_value(context, 61, 6);
}
} // namespace km::restoration
