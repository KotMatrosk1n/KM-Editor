// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_restoration_abilities.hpp"

namespace km::restoration {
namespace {
bool IsSlicingMove(uint32_t move) {
    // Generation 9 move identities with the slicing flag. Classification stays
    // independent of editable power, type, contact and localized move names.
    // Max moves have their own identities and do not inherit the base flag.
    switch (move) {
    case 15: case 75: case 163: case 210: case 314: case 332: case 348:
    case 400: case 403: case 404: case 427: case 440: case 533: case 534:
    case 548: case 669: case 781: case 830: case 845: case 860: case 869:
    case 875: case 891: case 895: case 910: case 911:
        return true;
    default:
        return false;
    }
}
bool IsAttacker(void* context, uint32_t owner, const NativeDamageAbilityApi& api) {
    return context && owner < 31 && api.event_value && api.chain_modifier
        && api.event_value(context, 3) == owner;
}
} // namespace

bool Sharpness(void* context, uint32_t owner, const NativeDamageAbilityApi& api) noexcept {
    if (!IsAttacker(context, owner, api) || !IsSlicingMove(api.event_value(context, 18))) return false;
    api.chain_modifier(context, 52, 0x1800); // Base power x 1.5 in native fixed point.
    return true;
}

bool RockyPayload(void* context, uint32_t owner, const NativeDamageAbilityApi& api) noexcept {
    if (!IsAttacker(context, owner, api) || api.event_value(context, 22) != 5) return false;
    // The effective Rock type is checked after move type conversion. One native
    // offensive-stat event covers both Attack and Special Attack exactly once.
    api.chain_modifier(context, 56, 0x1800);
    return true;
}

bool FireMane(void* context, uint32_t owner, const NativeDamageAbilityApi& api) noexcept {
    if (!IsAttacker(context, owner, api) || api.event_value(context, 22) != 9) return false;
    api.chain_modifier(context, 56, 0x1800);
    return true;
}
} // namespace km::restoration
