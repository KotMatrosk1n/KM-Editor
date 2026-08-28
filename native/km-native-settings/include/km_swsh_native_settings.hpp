// SPDX-License-Identifier: GPL-3.0-only
#pragma once

#include "km_settings.hpp"

namespace km {

enum class SwShNativeSettingsEdition : uint8_t {
    Sword = 1,
    Shield = 2,
};

struct SwShNativeSettingsProfileView {
    SwShNativeSettingsEdition edition;
    uint64_t title_id;
    const char* update_version;
    const char* full_build_id;
    uintptr_t text_size;
    uintptr_t registration_hook_offset;
    uintptr_t native_array_registrar_offset;
    const char* settings_journal_path;
};

const SwShNativeSettingsProfileView* GetSwShNativeSettingsProfile(
    SwShNativeSettingsEdition edition);

// Returns true only when the requested exact runtime-managed title was accepted
// and its complete immutable hook set committed during the serialized subsdk
// startup call. Journal readiness may retry afterward, but that worker can only
// publish authenticated data. A wrong title or preimage mismatch returns false
// without arming any hook.
bool TryInstallSwShNativeSettings(
    SwShNativeSettingsEdition edition,
    const ModuleRange* modules,
    size_t module_count,
    const GuestFilesystemApi& filesystem);

} // namespace km
