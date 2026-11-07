// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include "km_runtime.hpp"

namespace km::restoration {

// Installs during loader initialization only. All retail table entries, ABI
// fingerprints and all three dispatch loops must match before one transaction
// publishes the extended table. A mismatch leaves the executable unchanged.
bool TryInstallSwSh132(const ModuleRange& main) noexcept;

} // namespace km::restoration
