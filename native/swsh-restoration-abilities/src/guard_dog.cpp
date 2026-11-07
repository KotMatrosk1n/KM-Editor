// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_restoration_abilities.hpp"

namespace km::restoration {

bool GuardDogIntimidate(void* context, const NativeRankRequest* request,
    const NativeGuardDogApi& api) noexcept {
    if (!api.submit_rank) return false;
    if (!context || !request || request->target_count > 6 || !api.battler
        || !api.battler_value || !api.initialize_message)
        return api.submit_rank(context, request);
    NativeRankRequest remaining = *request;
    remaining.target_count = 0;
    uint8_t guarded[6]{};
    uint8_t guarded_count = 0;
    for (uint8_t i = 0; i < request->target_count; ++i) {
        const auto target = request->targets[i];
        void* battler = target < 31 ? api.battler(context, target) : nullptr;
        // Field19 is the effective ability, including Gastro Acid and
        // Neutralizing Gas. Field18 would incorrectly include suppressed ones.
        if (battler && api.battler_value(battler, 19) == 275)
            guarded[guarded_count++] = target;
        else remaining.targets[remaining.target_count++] = target;
    }
    bool accepted = remaining.target_count ? api.submit_rank(context, &remaining) : true;
    for (uint8_t i = 0; i < guarded_count; ++i) {
        NativeRankRequest boost{};
        boost.source = guarded[i];
        boost.target_count = 1;
        boost.targets[0] = guarded[i];
        boost.stat = 1;
        boost.stages = 1;
        boost.flags[20 - 13] = 1; // Native ability announcement.
        api.initialize_message(boost.message);
        accepted = api.submit_rank(context, &boost) || accepted;
    }
    return accepted;
}

bool GuardDogForcedSwitch(void* context, uint32_t owner, const NativeGuardDogApi& api) noexcept {
    if (!context || owner >= 31 || !api.event_value || !api.set_event_value
        || !api.begin_ability || !api.end_ability) return false;
    if (api.event_value(context, 4) != owner || !api.set_event_value(context, 72, 1)) return false;
    // Show the actual Guard Dog ability. Suction Cups' literal announcement is
    // deliberately not reused: it describes anatomy this ability does not have.
    (void)api.begin_ability(context, owner);
    (void)api.end_ability(context, owner);
    (void)api.set_event_value(context, 117, 1);
    return true;
}

} // namespace km::restoration
