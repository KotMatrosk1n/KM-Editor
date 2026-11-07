// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_berry_abilities.hpp"
#include "km_swsh_effect_source_abilities.hpp"

namespace km::restoration {
bool CudChewRemember(void* context, uint32_t owner, const NativeBerryAbilityApi& api) noexcept {
    const auto& battle = api.battle;
    if (!context || owner >= 31 || battle.event_value(context, 2) != owner
        || !battle.event_value(context, 88) || battle.work_value(context, 2)
        || EffectCameFrom(context, 0, 450)) return false;
    const auto item = battle.event_value(context, 48);
    if (!api.is_berry(item)) return false;
    battle.set_work_value(context, 0, item);
    // The turn boundary follows the entire native residual phase, including
    // berries consumed during residual effects. Never advance on a phase resume.
    battle.set_work_value(context, 1, CompletedAbilityTurns(context) + 1);
    return true;
}
bool CudChewRepeat(void* context, uint32_t owner, const NativeBerryAbilityApi& api) noexcept {
    const auto& battle = api.battle;
    if (!context || owner >= 31 || battle.event_value(context, 2) != owner
        || battle.work_value(context, 2)) return false;
    const auto item = battle.work_value(context, 0);
    if (!item || static_cast<int32_t>(CompletedAbilityTurns(context) - battle.work_value(context, 1)) < 0)
        return false;
    auto* holder = battle.battler(context, owner);
    if (!holder || !api.battler_value(holder, 13)) return false;
    battle.set_work_value(context, 0, 0);
    battle.set_work_value(context, 1, 0);
    battle.set_work_value(context, 2, 1);
    const NativeForceEatRequest request{static_cast<uint8_t>(owner), static_cast<uint8_t>(owner),
        static_cast<uint16_t>(item), 0, {}, 0};
    AbilityEffectSource effect(reinterpret_cast<void**>(context)[6], 4, 291);
    battle.begin_ability(context, owner);
    // The temporary item work runs the berry's normal effect and notifications.
    // It leaves a subsequently acquired held item in place.
    const bool accepted = api.force_eat(context, &request);
    battle.end_ability(context, owner);
    battle.set_work_value(context, 2, 0);
    return accepted;
}
void CudChewReset(void* context, uint32_t, const NativeBerryAbilityApi& api) noexcept {
    if (!context) return;
    for (uint32_t i = 0; i < 3; ++i) api.battle.set_work_value(context, i, 0);
}
} // namespace km::restoration
