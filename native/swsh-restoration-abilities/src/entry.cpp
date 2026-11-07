// SPDX-License-Identifier: GPL-3.0-only
#include "km_swsh_restoration_runtime.hpp"

extern "C" void km_runtime_initialize() {
    // The loader serializes DT_INIT after relocation/BSS initialization. No
    // game thread, filesystem polling or menu initialization is involved.
    uint64_t title{};
    if (km::km_svc_get_info(&title, 18, km::CurrentProcessPseudoHandle, 0)
            != km::ResultSuccess
        || (title != 0x0100ABF008968000ULL && title != 0x01008DB008C2C000ULL)) return;
    km::ModuleRange modules[16]{};
    size_t count{};
    if (!km::FindMappedModules(modules, 16, &count)) return;
    for (size_t i = 0; i < count; ++i) {
        if (km::restoration::TryInstallSwSh132(modules[i])) return;
    }
}
