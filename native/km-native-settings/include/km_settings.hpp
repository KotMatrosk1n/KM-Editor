// SPDX-License-Identifier: GPL-3.0-only
#pragma once

#include "km_runtime.hpp"

namespace km {

enum class SettingsFamily : uint16_t {
    ScarletViolet = 1,
    SwordShield = 2,
    LegendsZA = 3,
};

enum SettingsPresence : uint64_t {
    PresenceExperienceShare = uint64_t{1} << 0,
    PresenceExperienceRate = uint64_t{1} << 1,
    PresenceLevelCap = uint64_t{1} << 2,
};

struct SettingsValues {
    bool experience_share;
    uint32_t experience_rate_basis_points;
    bool level_cap_enabled;
    uint8_t level_cap;
};

struct SettingsState {
    SettingsFamily family;
    uint64_t title_id;
    uint64_t generation;
    uint64_t presence;
    SettingsValues values;
    int32_t active_slot;
    bool writable;
};

constexpr SettingsValues VanillaSettings{true, 10000, false, 100};

bool LoadSettingsJournal(const GuestFilesystemApi& filesystem, const char* path,
                         SettingsFamily family, uint64_t title_id,
                         uint64_t required_presence, SettingsState* output);
bool CommitSettingsJournal(const GuestFilesystemApi& filesystem, const char* path,
                           SettingsFamily family, uint64_t title_id,
                           uint64_t required_presence, const SettingsValues& values,
                           SettingsState* output);

uint64_t PackSettingsSnapshot(const SettingsState& state);
bool UnpackSettingsSnapshot(uint64_t packed, SettingsValues* values,
                            uint64_t* presence = nullptr);

} // namespace km
