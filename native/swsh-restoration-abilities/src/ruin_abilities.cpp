// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_ruin_abilities.hpp"

namespace km::restoration {

void RuinEntry(void* context, uint32_t owner, const NativeRuinAbilityApi& api) noexcept {
    if (!context || owner >= 31 || !api.event_value || !api.begin_ability
        || !api.end_ability || api.event_value(context, 2) != owner) return;
    api.begin_ability(context, owner);
    api.end_ability(context, owner);
}

bool RuinStat(void* context, uint32_t owner, uint32_t ability, bool defense,
        uint32_t category, const NativeRuinAbilityApi& api) noexcept {
    if (!context || owner >= 31 || ability < 284 || ability > 287
        || !api.event_value || !api.battler || !api.battler_value
        || !api.select_battlers || !api.chain_modifier
        || api.event_value(context, 27) != category) return false;
    const auto recipient = api.event_value(context, defense ? 4 : 3);
    if (recipient >= 31) return false;
    auto* target = api.battler(context, recipient);
    // Field 19 includes Gastro Acid and Neutralizing Gas suppression. The raw
    // personal ability must not grant immunity to a suppressed Ruin holder.
    if (!target || api.battler_value(target, 19) == ability) return false;

    // Selector 6 covers the entire active field in each retail battle layout.
    // It excludes fainted and benched battlers. Elect one effective holder so
    // multiple copies of the same Ruin ability cannot multiply the reduction.
    const NativeBattlerSelector all{6, 0, {}};
    uint8_t active[6]{};
    const auto count = api.select_battlers(context, &all, active);
    if (count > 6) return false;
    uint32_t elected = 31;
    for (uint32_t i = 0; i < count; ++i) {
        if (active[i] >= elected) continue;
        auto* candidate = api.battler(context, active[i]);
        if (candidate && api.battler_value(candidate, 19) == ability)
            elected = active[i];
    }
    if (elected != owner) return false;
    api.chain_modifier(context, 56, 0xc00);
    return true;
}

} // namespace km::restoration
