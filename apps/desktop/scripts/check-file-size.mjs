// SPDX-License-Identifier: GPL-3.0-only

// Historical compatibility entry point. App file-size limits were retired because
// ownership and testability matter more than arbitrary line counts. Static UI
// contract checks remain attached here so the existing typecheck command runs them.
import './check-analysis-preparation-contract.mjs';
import './check-focused-editor-contracts.mjs';

import { checkControlTheme } from './check-control-theme.mjs';

checkControlTheme();
