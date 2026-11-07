// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_field_abilities.hpp"

namespace km::restoration {
namespace {
bool Valid(void* context, uint32_t owner, const NativeFieldAbilityApi& api) {
    return context && owner < 31 && api.event_value;
}
bool TargetList(void* context, uint32_t owner, const NativeFieldAbilityApi& api) {
    const auto count = api.event_value(context, 5);
    if (count > 6) return false;
    for (uint32_t i = 0; i < count; ++i)
        if (api.event_value(context, 6 + i) == owner) return true;
    return false;
}
bool Hp(void* context, uint32_t owner, const NativeFieldAbilityApi& api,
        uint32_t* current, uint32_t* half) {
    if (!api.battler || !api.battler_value || !api.hp_fraction) return false;
    auto* battler = api.battler(context, owner);
    if (!battler) return false;
    *current = api.battler_value(battler, 13);
    *half = api.hp_fraction(battler, 2, false);
    return true;
}
void RecordHp(void* context, uint32_t owner, const NativeFieldAbilityApi& api) {
    uint32_t current{}, half{};
    if (api.set_work_value && Hp(context, owner, api, &current, &half))
        api.set_work_value(context, 0, current > half ? 1 : 0);
}
bool Sun(uint32_t weather) { return weather == 1 || weather == 6; }
bool Entry(void* context, uint32_t owner, const NativeFieldAbilityApi& api) {
    return Valid(context, owner, api) && api.event_value(context, 2) == owner;
}
bool Damage(void* context, uint32_t owner, uint32_t category, const NativeFieldAbilityApi& api) {
    return Valid(context, owner, api) && api.chain_modifier
        && api.event_value(context, 3) == owner && api.event_value(context, 27) == category;
}
} // namespace

void AngerShellBeforeMove(void* context, uint32_t owner, const NativeFieldAbilityApi& api) noexcept {
    if (Valid(context, owner, api) && TargetList(context, owner, api)) RecordHp(context, owner, api);
}
void AngerShellBeforeDamage(void* context, uint32_t owner, const NativeFieldAbilityApi& api) noexcept {
    if (Valid(context, owner, api) && api.event_value(context, 4) == owner) RecordHp(context, owner, api);
}
bool AngerShellAfterMove(void* context, uint32_t owner, const NativeFieldAbilityApi& api) noexcept {
    if (!Valid(context, owner, api) || !api.work_value || !api.set_work_value
        || !api.secondary_effect_blocked || !api.initialize_message || !api.submit_rank
        || !api.begin_ability || !api.end_ability || api.secondary_effect_blocked(context)
        || !TargetList(context, owner, api) || api.work_value(context, 0) != 1
        || api.event_value(context, 3) >= 31 || api.event_value(context, 3) == owner) return false;
    uint32_t current{}, half{};
    if (!Hp(context, owner, api, &current, &half) || !current || current > half) return false;
    api.set_work_value(context, 0, 0);
    api.begin_ability(context, owner);
    const uint8_t stats[] = {1, 3, 5, 2, 4};
    bool submitted = false;
    for (uint32_t i = 0; i < 5; ++i) {
        NativeRankRequest request{};
        request.source = static_cast<uint8_t>(owner);
        request.target_count = 1;
        request.targets[0] = static_cast<uint8_t>(owner);
        request.stat = stats[i];
        request.stages = i < 3 ? 1 : -1;
        api.initialize_message(request.message);
        submitted = api.submit_rank(context, &request) || submitted;
    }
    api.end_ability(context, owner);
    return submitted;
}

void OrichalcumPulseEntry(void* context, uint32_t owner, const NativeFieldAbilityApi& api) noexcept {
    if (!Entry(context, owner, api) || !api.weather || !api.sun_entry
        || !api.begin_ability || !api.end_ability) return;
    if (Sun(api.weather(context, owner))) {
        api.begin_ability(context, owner);
        api.end_ability(context, owner);
    } else {
        // The qualified Drought callback preserves Heat Rock and native weather
        // replacement rules, using the current ability for its announcement.
        api.sun_entry(context, owner);
    }
}
void HadronEngineEntry(void* context, uint32_t owner, const NativeFieldAbilityApi& api) noexcept {
    if (!Entry(context, owner, api) || !api.terrain || !api.electric_entry
        || !api.begin_ability || !api.end_ability) return;
    if (api.terrain(context) == 3) {
        api.begin_ability(context, owner);
        api.end_ability(context, owner);
    } else {
        api.electric_entry(context, owner);
    }
}
bool OrichalcumPulseDamage(void* context, uint32_t owner, const NativeFieldAbilityApi& api) noexcept {
    if (!Damage(context, owner, 1, api) || !api.weather || !Sun(api.weather(context, owner))) return false;
    api.chain_modifier(context, 56, 0x1555);
    return true;
}
bool HadronEngineDamage(void* context, uint32_t owner, const NativeFieldAbilityApi& api) noexcept {
    if (!Damage(context, owner, 2, api) || !api.terrain || api.terrain(context) != 3) return false;
    api.chain_modifier(context, 56, 0x1555);
    return true;
}

} // namespace km::restoration
