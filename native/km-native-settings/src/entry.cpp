// SPDX-License-Identifier: GPL-3.0-only

#include "km_runtime.hpp"
#include "km_sv_runtime.hpp"
#include "km_swsh_native_settings.hpp"
#include "km_za_202.hpp"

namespace {

constexpr uint32_t InfoProgramId = 18;
constexpr uint64_t SwordTitleId = 0x0100ABF008968000ULL;
constexpr uint64_t ShieldTitleId = 0x01008DB008C2C000ULL;
constexpr uint64_t ScarletTitleId = 0x0100A3D008C5C000ULL;
constexpr uint64_t VioletTitleId = 0x01008F6008C5E000ULL;
constexpr uint64_t LegendsZaTitleId = 0x0100F43008C44000ULL;

bool TryActivateNativeMenu() {
    // Game-specific activation is intentionally separated from loader startup:
    // no executable byte is changed until an exact profile and all of its
    // immutable menu/runtime dependencies have passed their preflight.
    km::ModuleRange modules[16]{};
    size_t module_count = 0;
    km::GuestFilesystemApi filesystem{};
    if (!km::FindMappedModules(modules, 16, &module_count)
        || !km::ResolveGuestFilesystem(modules, module_count, &filesystem)) {
        return false;
    }

    uint64_t title_id = 0;
    if (km::km_svc_get_info(&title_id, InfoProgramId,
                            km::CurrentProcessPseudoHandle, 0)
        != km::ResultSuccess) {
        return false;
    }

    if (title_id == ScarletTitleId || title_id == VioletTitleId) {
        return km_try_activate_sv(modules, module_count, &filesystem);
    }
    if (title_id == SwordTitleId) {
        return km::TryInstallSwShNativeSettings(
            km::SwShNativeSettingsEdition::Sword,
            modules,
            module_count,
            filesystem);
    }
    if (title_id == ShieldTitleId) {
        return km::TryInstallSwShNativeSettings(
            km::SwShNativeSettingsEdition::Shield,
            modules,
            module_count,
            filesystem);
    }
    if (title_id == LegendsZaTitleId) {
        return km_try_activate_za_202(modules, module_count, &filesystem);
    }
    return false;
}

} // namespace

extern "C" void km_runtime_start(void*, uint64_t) {
    // The subsdk entry is the one loader-serialized opportunity to inspect and
    // publish immutable hooks. Unsupported titles, missing exports, profile
    // mismatches, and patch rejection are terminal dormancy for this process;
    // retrying those permanent conditions on a live game thread is unsafe.
    // Each title runtime owns an unbounded retry only for authenticated journal
    // readiness, after its immutable hook surface is already installed.
    (void)TryActivateNativeMenu();
}
