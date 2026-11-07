// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_paradox_abilities.hpp"
namespace km::restoration {
namespace {
uint32_t BestStat(void* battler, const NativeParadoxAbilityApi& api) {
    // Select from stored battle stats and current stages, before item/ability
    // multipliers. Strict comparison preserves Atk, Def, SpA, SpD, Spe ties.
    const auto* bytes = static_cast<const uint8_t*>(battler);
    auto* field = *reinterpret_cast<void* const*>(bytes + 0x460);
    const bool room = field && api.field_condition(field, 4);
    uint32_t best = 1, highest = 0;
    for (uint32_t stat = 1; stat <= 5; ++stat) {
        uint32_t stage_stat = room && (stat == 2 || stat == 4) ? 6 - stat : stat;
        uint32_t stage = api.battler_value(battler, stage_stat);
        if (stage > 12) stage = 12;
        uint32_t value = *reinterpret_cast<const uint16_t*>(bytes + 0x2a0 + stat * 2);
        value = stage >= 6 ? value * (stage - 4) / 2 : value * 2 / (8 - stage);
        if (value > highest) { highest = value; best = stat; }
    }
    return best;
}
}
void ParadoxRefresh(void* context, uint32_t owner, bool quark, const NativeParadoxAbilityApi& api) noexcept {
    if (!context || owner >= 31) return;
    auto* battler = api.battler(context, owner);
    if (!battler || !api.battler_value(battler, 13)) return;
    const uint32_t ability = quark ? 282 : 281;
    if (api.battler_value(battler, 19) != ability) return;
    const uint32_t state = api.work_value(context, 0);
    if (state == 2 || state == 3) return;
    const auto weather = quark ? 0 : api.weather(context);
    const bool field = quark ? api.terrain(context) == 3 : weather == 1 || weather == 6;
    if (field && state == 1) return;
    if (!field) {
        api.set_work_value(context, 0, 0);
        api.set_work_value(context, 1, 0);
        if (api.held_item(battler) != 1880 || !api.item_usable(context, owner)) return;
    }
    const uint32_t best = BestStat(battler, api);
    if (!field) {
        // Native item removal owns last-used item, consumption and Unburden
        // bookkeeping. Mark the in-progress state before synchronous events.
        api.set_work_value(context, 0, 3);
        NativeConsumeItemRequest request{};
        request.owner = static_cast<uint8_t>(owner);
        api.initialize_message(request.message);
        api.consume_item(context, &request);
        if (api.held_item(battler) == 1880) {
            api.set_work_value(context, 0, 0);
            return;
        }
    }
    api.set_work_value(context, 1, best);
    api.set_work_value(context, 0, field ? 1 : 2);
    api.begin_ability(context, owner);
    api.end_ability(context, owner);
}
void ParadoxReset(void* context, uint32_t owner, const NativeParadoxAbilityApi& api) noexcept {
    if (!context || owner >= 31 || api.event_value(context, 2) != owner) return;
    api.set_work_value(context, 0, 0);
    api.set_work_value(context, 1, 0);
}
void ParadoxStat(void* context, uint32_t owner, uint32_t event, const NativeParadoxAbilityApi& api) noexcept {
    if (!context || owner >= 31 || !api.work_value(context, 0) || api.work_value(context, 0) == 3) return;
    const auto stat = api.work_value(context, 1);
    if (event == 19) {
        if (stat != 5 || api.event_value(context, 2) != owner) return;
    } else {
        const auto category = api.event_value(context, 27);
        if (category != 1 && category != 2) return;
        if (event == 76) {
            if (api.event_value(context, 3) != owner || stat != (category == 1 ? 1u : 3u)) return;
        } else if (event == 77) {
            if (api.event_value(context, 4) != owner || stat != (category == 1 ? 2u : 4u)) return;
        } else return;
    }
    api.chain_modifier(context, 56, stat == 5 ? 6144 : 5325);
}
} // namespace km::restoration
