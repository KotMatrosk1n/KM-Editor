// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "km_swsh_native_settings.hpp"

namespace km {
bool PrepareSwShOptions(const ModuleRange& main, SwShNativeSettingsEdition edition,
                        ExecutablePatch (&patches)[2]);
}
