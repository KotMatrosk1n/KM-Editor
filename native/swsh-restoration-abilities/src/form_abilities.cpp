// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_form_abilities.hpp"

namespace km::restoration {
namespace {
void* Eligible(void* context, uint32_t owner, uint32_t species, uint8_t form,
               const NativeFormAbilityApi& api) {
    if (!context || owner >= 31 || api.event_value(context, 2) != owner) return nullptr;
    auto* battler = api.battler(context, owner);
    if (!battler || api.species(battler) != species || api.transformed(battler)
        || static_cast<uint8_t*>(battler)[0x33d] != form) return nullptr;
    return battler;
}
} // namespace
bool ZeroToHeroWithdraw(void* context, uint32_t owner, const NativeFormAbilityApi& api) noexcept {
    if (!Eligible(context, owner, 964, 0, api)) return false;
    NativeFormRequest request{};
    request.owner = static_cast<uint8_t>(owner);
    request.form = 1;
    api.initialize_message(request.message);
    // Event 107 runs before withdrawal. The Hero model first appears on return.
    return api.change_form(context, &request);
}
bool ZeroToHeroEntry(void* context, uint32_t owner, const NativeFormAbilityApi& api) noexcept {
    if (!Eligible(context, owner, 964, 1, api) || !MarkHeroEntry(context, owner)) return false;
    api.begin_ability(context, owner);
    api.end_ability(context, owner);
    return true;
}
bool TeraShift(void* context, uint32_t owner, const NativeFormAbilityApi& api) noexcept {
    if (!Eligible(context, owner, 1024, 0, api)) return false;
    NativeFormRequest request{};
    request.owner = static_cast<uint8_t>(owner);
    request.form = 1;
    request.refresh_model = 1;
    request.show_message = 1;
    api.initialize_message(request.message);
    api.set_message(request.message, 1, 130);
    api.add_message_battler(request.message, owner);
    api.begin_ability(context, owner);
    const bool accepted = api.change_form(context, &request);
    api.end_ability(context, owner);
    if (!accepted) return false;
    // Native form changes refresh personal data and stats but preserve the raw
    // battle ability. Replace that ability and its event work explicitly.
    auto* field = reinterpret_cast<void**>(context)[3];
    api.remove_ability(field, owner);
    api.set_ability(field, owner, 308);
    api.register_ability(field, owner, 308);
    return true;
}
} // namespace km::restoration
