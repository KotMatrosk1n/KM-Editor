// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "km_swsh_restoration_abilities.hpp"

namespace km::restoration {
struct NativePartyCureRequest {
    uint8_t source;
    uint8_t reserved[3];
    uint32_t condition;
    uint8_t targets[30];
    uint8_t target_count;
    uint8_t reserved39;
    uint32_t flags;
    uint8_t message[36];
};
static_assert(sizeof(NativePartyCureRequest) == 80);
static_assert(offsetof(NativePartyCureRequest, targets) == 8);
static_assert(offsetof(NativePartyCureRequest, target_count) == 38);
static_assert(offsetof(NativePartyCureRequest, message) == 44);

struct NativePartyCureApi {
    uint32_t (*event_value)(void*, uint32_t);
    // This must use the live registered ability node and the retail event
    // filter, including suppression. Benched/removed nodes do not protect.
    bool (*protected_by_gold)(void*, uint32_t);
    void (*announce_immunity)(void*, uint32_t);
    bool (*submit_cure)(void*, const NativePartyCureRequest*);
};
bool GoodAsGoldPartyCure(void* context, const NativePartyCureRequest* request,
    const NativePartyCureApi& api) noexcept;

} // namespace km::restoration
