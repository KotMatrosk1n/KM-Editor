// SPDX-License-Identifier: GPL-3.0-only
#pragma once

#include "km_runtime.hpp"

extern "C" bool km_try_activate_za_202(
    const km::ModuleRange* modules,
    size_t module_count,
    const km::GuestFilesystemApi* filesystem);

// Exact Pokemon Legends Z-A 2.0.2 stock-page hooks.
extern "C" uint32_t km_za_202_row_count(void* model);

struct KmZa202RowDescriptor {
    uint32_t kind;
    uint32_t flags;
    uint32_t value;
    uint32_t reserved;
};
static_assert(sizeof(KmZa202RowDescriptor) == 0x10);

extern "C" KmZa202RowDescriptor km_za_202_get_row(void* page, uint32_t row);
extern "C" void km_za_202_apply_row(void* page, uint32_t row, uint32_t value);
extern "C" void km_za_202_render_row(
    void* text_view,
    void* row_handle,
    uint32_t category,
    uint32_t row,
    uint32_t value);
extern "C" void km_za_202_render_description(
    void* text_view,
    uint32_t category,
    uint32_t row);
extern "C" void km_za_202_reset(void* model);
extern "C" void km_za_202_back(void* callback);
extern "C" bool km_za_202_step_right(void* page);
extern "C" bool km_za_202_step_left(void* page);

// Assembly bridges which preserve the retail call-site ABI.
extern "C" void km_za_202_rate_bridge();
extern "C" void km_za_202_back_original(void* callback);
extern "C" km::Result km_za_202_sleep_thread(int64_t nanoseconds);

extern "C" uint32_t km_za_202_share_callback(
    float scalar,
    void* first,
    void* second,
    uint32_t eligible);
extern "C" void km_za_202_rate_callback(
    void* recipient,
    void* result,
    uint32_t award);

// Read by the exact retail back trampoline.
extern "C" uintptr_t km_za_202_main_base;
