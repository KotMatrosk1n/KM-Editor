// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_commander_ability.hpp"

namespace km::restoration {
bool CommanderEntry(void* context, uint32_t owner, const NativeCommanderApi& api) noexcept {
    if (!context || owner >= 31) return false;
    auto* manager = reinterpret_cast<void**>(context)[6];
    if (CommanderLocked(manager, owner)) return false;
    auto* battler = api.battle.battler(context, owner);
    if (!battler || api.species(battler) != 978 || api.transformed(battler)) return false;
    // The native selector excludes the owner and only returns active allies.
    // Singles have no eligible ally. Do not commandeer a fainted replacement.
    const auto position = api.battle.position(context, owner);
    if (position >= 5) return false;
    const NativeBattlerSelector allies{5, static_cast<uint8_t>(position), {}};
    uint8_t targets[31]{};
    if (api.battle.select_battlers(context, &allies, targets) != 1 || targets[0] >= 31) return false;
    const auto host = targets[0];
    auto* ally = api.battle.battler(context, host);
    if (!ally || api.species(ally) != 977 || api.transformed(ally)
        || !*reinterpret_cast<uint16_t*>(static_cast<uint8_t*>(battler) + 0x74)
        || !*reinterpret_cast<uint16_t*>(static_cast<uint8_t*>(ally) + 0x74)
        || !BeginCommanderPair(context, owner, host, battler)) return false;
    // Publish before submitting ranks: nested native updates must not reapply.
    api.battle.begin_ability(context, owner);
    api.visibility(reinterpret_cast<void**>(context)[3], owner, true);
    for (uint32_t stat = 1; stat <= 5; ++stat) {
        NativeRankRequest rank{};
        rank.source = static_cast<uint8_t>(host);
        rank.target_count = 1;
        rank.targets[0] = static_cast<uint8_t>(host);
        rank.stat = stat;
        rank.stages = 2;
        api.battle.initialize_message(rank.message);
        api.battle.submit_rank(context, &rank);
    }
    api.battle.end_ability(context, owner);
    return true;
}
void CommanderEvent(void* context, uint32_t event, const NativeCommanderApi& api) noexcept {
    if (!context) return;
    auto* manager = reinterpret_cast<void**>(context)[6];
    if (event == 107 || event == 217) {
        const auto released = ReleaseCommander(manager, api.battle.event_value(context, 2));
        if (released) api.visibility(reinterpret_cast<void**>(context)[3], released - 1, false);
        return;
    }
    if (event != 12 && event != 32 && event != 176 && event != 192) return;
    const auto owner = api.battle.event_value(context, (event == 176 || event == 192) ? 4 : 2);
    if (event == 192) {
        if (CommanderHidden(manager, owner)) api.set_event_value(context, 73, 1);
    } else if (event == 32) {
        // This gate precedes PP consumption. The state survives ability removal.
        if (CommanderHidden(manager, owner)) api.set_event_value(context, 37, 19);
    } else if (CommanderLocked(manager, owner)) {
        api.set_event_value(context, 72, 1);
        if (event == 176) api.set_event_value(context, 117, 1);
    }
}
} // namespace km::restoration
