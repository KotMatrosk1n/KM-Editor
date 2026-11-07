// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_battle_state_abilities.hpp"

namespace km::restoration {
namespace {
struct BattleState {
    void* manager;
    uint32_t syrup;
    uint32_t hero_shown;
    uint32_t completed_turns;
    uint8_t faints[31];
    uint8_t commander_hosts[31];
    const void* commander_battlers[31];
    uint32_t commanded;
};
BattleState battles[8]{};
BattleState* Find(void* manager) {
    if (!manager) return nullptr;
    for (auto& battle : battles) if (battle.manager == manager) return &battle;
    return nullptr;
}
BattleState* ForContext(void* context) {
    return context ? Find(reinterpret_cast<void**>(context)[6]) : nullptr;
}
} // namespace

void BeginAbilityBattle(void* manager) noexcept {
    if (!manager) return;
    auto* slot = Find(manager);
    if (!slot) for (auto& battle : battles) if (!battle.manager) { slot = &battle; break; }
    if (slot) { *slot = {}; slot->manager = manager; }
}
void EndAbilityBattle(void* manager) noexcept {
    if (auto* slot = Find(manager)) *slot = {};
}
void RecordAbilityFaint(void* manager, uint32_t owner) noexcept {
    auto* battle = Find(manager);
    if (battle && owner < 31 && battle->faints[owner] < 5) ++battle->faints[owner];
}
uint32_t CompletedAbilityTurns(void* context) noexcept {
    const auto* battle = ForContext(context);
    return battle ? battle->completed_turns : 0;
}
void CompleteAbilityTurn(void* manager) noexcept {
    if (auto* battle = Find(manager)) ++battle->completed_turns;
}
bool MarkHeroEntry(void* context, uint32_t owner) noexcept {
    auto* battle = ForContext(context);
    if (!battle || owner >= 31 || (battle->hero_shown & (uint32_t{1} << owner))) return false;
    battle->hero_shown |= uint32_t{1} << owner;
    return true;
}

bool BeginCommanderPair(void* context, uint32_t commander, uint32_t host, const void* battler) noexcept {
    auto* battle = ForContext(context);
    if (!battle || !battler || commander >= 31 || host >= 31 || commander == host
        || battle->commander_hosts[commander] || (battle->commanded & (uint32_t{1} << host))) return false;
    battle->commander_hosts[commander] = static_cast<uint8_t>(host + 1);
    battle->commander_battlers[commander] = battler;
    battle->commanded |= uint32_t{1} << host;
    return true;
}
bool CommanderHidden(void* manager, uint32_t owner) noexcept {
    const auto* battle = Find(manager);
    return battle && owner < 31 && battle->commander_hosts[owner];
}
bool CommanderHiddenBattler(const void* battler) noexcept {
    if (!battler) return false;
    for (const auto& battle : battles) if (battle.manager)
        for (uint32_t id = 0; id < 31; ++id)
            if (battle.commander_hosts[id] && battle.commander_battlers[id] == battler) return true;
    return false;
}
bool CommanderLocked(void* manager, uint32_t owner) noexcept {
    const auto* battle = Find(manager);
    return battle && owner < 31 && (battle->commander_hosts[owner]
        || (battle->commanded & (uint32_t{1} << owner)));
}
uint32_t ReleaseCommander(void* manager, uint32_t owner) noexcept {
    auto* battle = Find(manager);
    if (!battle || owner >= 31) return 0;
    // Losing Tatsugiri leaves Dondozo commanded until its own field visit ends.
    battle->commander_hosts[owner] = 0;
    battle->commander_battlers[owner] = nullptr;
    battle->commanded &= ~(uint32_t{1} << owner);
    for (uint32_t id = 0; id < 31; ++id) if (battle->commander_hosts[id] == owner + 1) {
        battle->commander_hosts[id] = 0;
        battle->commander_battlers[id] = nullptr;
        return id + 1;
    }
    return 0;
}

bool SupremeOverlordEntry(void* context, uint32_t owner, const NativeBattleStateAbilityApi& api) noexcept {
    if (!context || owner >= 31 || api.event_value(context, 2) != owner) return false;
    auto* battle = ForContext(context);
    uint32_t count = 0;
    if (battle) for (uint32_t id = 0; id < 31 && count < 5; ++id)
        if (battle->faints[id] && api.same_side(context, owner, id)) count += battle->faints[id];
    if (count > 5) count = 5;
    // Snapshot on entry. Later ally knockouts do not increase this field visit.
    api.set_work_value(context, 0, count);
    if (!count) return false;
    api.begin_ability(context, owner);
    api.end_ability(context, owner);
    return true;
}
bool SupremeOverlordPower(void* context, uint32_t owner, const NativeBattleStateAbilityApi& api) noexcept {
    if (!context || owner >= 31 || api.event_value(context, 3) != owner) return false;
    const auto count = api.work_value(context, 0);
    if (!count || count > 5) return false;
    constexpr uint32_t modifiers[] = {4096, 4506, 4915, 5325, 5734, 6144};
    api.chain_modifier(context, 52, modifiers[count]);
    return true;
}
bool SupersweetSyrup(void* context, uint32_t owner, const NativeBattleStateAbilityApi& api) noexcept {
    if (!context || owner >= 31 || api.event_value(context, 2) != owner) return false;
    auto* battle = ForContext(context);
    if (!battle || (battle->syrup & (uint32_t{1} << owner))) return false;
    // The attempt is spent even if every foe has a Substitute or capped evasion.
    battle->syrup |= uint32_t{1} << owner;
    const auto position = api.position(context, owner);
    if (position >= 5) return false;
    const NativeBattlerSelector foes{2, static_cast<uint8_t>(position), {}};
    uint8_t targets[31]{};
    const auto count = api.select_battlers(context, &foes, targets);
    if (count > 31) return false;
    NativeRankRequest request{};
    request.source = static_cast<uint8_t>(owner);
    request.stat = 7;
    request.stages = -1;
    api.initialize_message(request.message);
    for (uint32_t i = 0; i < count && request.target_count < 6; ++i) {
        if (targets[i] >= 31) continue;
        auto* target = api.battler(context, targets[i]);
        if (target && !api.has_substitute(target)) request.targets[request.target_count++] = targets[i];
    }
    if (!request.target_count) return false;
    api.begin_ability(context, owner);
    const bool accepted = api.submit_rank(context, &request);
    api.end_ability(context, owner);
    return accepted;
}
} // namespace km::restoration
