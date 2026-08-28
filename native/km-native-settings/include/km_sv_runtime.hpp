// SPDX-License-Identifier: GPL-3.0-only
#pragma once

#include "km_runtime.hpp"

extern "C" bool km_try_activate_sv(const km::ModuleRange* modules,
                                     size_t module_count,
                                     const km::GuestFilesystemApi* filesystem);
