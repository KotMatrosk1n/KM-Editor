// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_effect_source_abilities.hpp"

namespace km::restoration {
namespace { const AbilityEffectSource* source{}; }
AbilityEffectSource::AbilityEffectSource(void* event_manager, uint32_t effect_kind, uint32_t effect_identity) noexcept
    : manager(event_manager), kind(effect_kind), identity(effect_identity), previous(source) { source = this; }
AbilityEffectSource::~AbilityEffectSource() { source = previous; }
bool EffectCameFrom(void* context, uint32_t kind, uint32_t identity) noexcept {
    if (!context) return false;
    const auto* manager = reinterpret_cast<void**>(context)[6];
    for (auto* effect = source; effect; effect = effect->previous)
        if (effect->manager == manager)
            return effect->kind == kind && (identity == 0xffffffff || effect->identity == identity);
    return false;
}

bool PoisonPuppeteer(void* context, uint32_t owner, const NativeEffectSourceAbilityApi& api) noexcept {
    const auto& battle = api.battle;
    if (!context || owner >= 31 || battle.event_value(context, 3) != owner
        || battle.event_value(context, 30) != 3 || !EffectCameFrom(context, 0)) return false;
    const auto target = battle.event_value(context, 4);
    if (target >= 31 || target == owner) return false;
    auto* holder = battle.battler(context, owner);
    auto* victim = battle.battler(context, target);
    // Keep the signature ability limited to Pecharunt.
    if (!holder || !victim || api.species(holder) != 1025
        || !api.battler_value(holder, 13) || !api.battler_value(victim, 13)) return false;
    NativeStatusRequest request{};
    request.source = static_cast<uint8_t>(owner);
    request.target = static_cast<uint8_t>(target);
    request.condition = 6;
    request.count = 1;
    api.status_parameter(6, holder, &request.parameter);
    battle.initialize_message(request.message);
    battle.begin_ability(context, owner);
    const bool accepted = api.submit_status(context, &request);
    battle.end_ability(context, owner);
    return accepted;
}

bool OpportunistRemember(void* context, uint32_t owner, const NativeEffectSourceAbilityApi& api) noexcept {
    const auto& battle = api.battle;
    if (!context || owner >= 31 || EffectCameFrom(context, 4, 290)
        || EffectCameFrom(context, 5, 1883)) return false; // Mirror Herb.
    const auto target = battle.event_value(context, 2);
    const auto stat = battle.event_value(context, 34);
    const auto stages = static_cast<int32_t>(battle.event_value(context, 35));
    if (target >= 31 || stat < 1 || stat > 7 || stages <= 0
        || battle.same_side(context, owner, target)) return false;
    auto* holder = battle.battler(context, owner);
    if (!holder || !api.battler_value(holder, 13)) return false;
    const auto saved = battle.work_value(context, stat - 1);
    const auto sum = saved + static_cast<uint32_t>(stages);
    battle.set_work_value(context, stat - 1, sum > 12 ? 12 : sum);
    return true;
}
bool OpportunistApply(void* context, uint32_t owner, const NativeEffectSourceAbilityApi& api) noexcept {
    const auto& battle = api.battle;
    if (!context || owner >= 31) return false;
    uint32_t boosts[7]{};
    bool any = false;
    // Clear before submitting any nested rank changes.
    for (uint32_t stat = 0; stat < 7; ++stat) {
        boosts[stat] = battle.work_value(context, stat);
        battle.set_work_value(context, stat, 0);
        any |= boosts[stat] != 0;
    }
    auto* holder = battle.battler(context, owner);
    if (!any || !holder || !api.battler_value(holder, 13)) return false;
    AbilityEffectSource copied(reinterpret_cast<void**>(context)[6], 4, 290);
    battle.begin_ability(context, owner);
    bool accepted = false;
    for (uint32_t stat = 0; stat < 7; ++stat) if (boosts[stat]) {
        NativeRankRequest request{};
        request.source = request.targets[0] = static_cast<uint8_t>(owner);
        request.target_count = 1;
        request.stat = stat + 1;
        request.stages = static_cast<int8_t>(boosts[stat]);
        battle.initialize_message(request.message);
        accepted |= battle.submit_rank(context, &request);
    }
    battle.end_ability(context, owner);
    return accepted;
}
} // namespace km::restoration
