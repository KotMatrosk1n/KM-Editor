// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "km_swsh_battle_state_abilities.hpp"

namespace km::restoration {
struct NativeCommanderApi {
    NativeBattleStateAbilityApi battle;
    uint32_t (*species)(void*);
    bool (*transformed)(void*);
    bool (*set_event_value)(void*, uint32_t, uint32_t);
    void (*visibility)(void*, uint32_t, bool);
};
bool CommanderEntry(void*, uint32_t, const NativeCommanderApi&) noexcept;
void CommanderEvent(void*, uint32_t, const NativeCommanderApi&) noexcept;
} // namespace km::restoration
