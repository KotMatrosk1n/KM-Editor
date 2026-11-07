// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_party_cure_ability.hpp"

namespace km::restoration {
bool GoodAsGoldPartyCure(void* context, const NativePartyCureRequest* request,
    const NativePartyCureApi& api) noexcept {
    if (!context || !request || !api.event_value || !api.protected_by_gold
        || !api.announce_immunity || !api.submit_cure) return false;
    const auto move = api.event_value(context, 18);
    if ((move != 215 && move != 312) || request->condition != 45
        || request->source >= 31 || request->target_count > 30
        || api.event_value(context, 3) != request->source)
        return api.submit_cure(context, request);
    for (uint32_t i = 0; i < request->target_count; ++i)
        if (request->targets[i] >= 31) return api.submit_cure(context, request);

    auto filtered = *request;
    uint32_t retained = 0;
    for (uint32_t i = 0; i < request->target_count; ++i) {
        const auto target = request->targets[i];
        if (target != request->source && api.protected_by_gold(context, target)) {
            api.announce_immunity(context, target);
        } else {
            filtered.targets[retained++] = target;
        }
    }
    if (retained == request->target_count) return api.submit_cure(context, request);
    if (!retained) return false;
    filtered.target_count = static_cast<uint8_t>(retained);
    return api.submit_cure(context, &filtered);
}

} // namespace km::restoration
