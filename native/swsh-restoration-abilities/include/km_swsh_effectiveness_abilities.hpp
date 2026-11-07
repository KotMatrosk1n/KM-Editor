// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <stdint.h>

namespace km::restoration {
// Event 237 is emitted after the native engine combines all defending types.
// It uses the regular ability dispatcher, including its suppression filters.
inline constexpr uint32_t FinalEffectivenessEvent = 237;
struct NativeEffectivenessAbilityApi {
    uint32_t (*event_value)(void*, uint32_t);
    bool (*set_event_value)(void*, uint32_t, uint32_t);
    void* (*battler)(void*, uint32_t);
    bool (*full_hp)(void*);
};
bool TeraShell(void*, uint32_t, const NativeEffectivenessAbilityApi&) noexcept;
} // namespace km::restoration
