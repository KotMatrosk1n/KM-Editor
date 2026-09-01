// SPDX-License-Identifier: GPL-3.0-only

// Historical compatibility entry point. App file-size limits were retired because
// ownership and testability matter more than arbitrary line counts. Static UI
// contract checks remain attached here so the existing typecheck command runs them.
import './check-analysis-preparation-contract.mjs';
import './check-project-async-policy.mjs';
import './check-performance-diagnostics-contract.mjs';
import './check-focused-editor-contracts.mjs';
import './check-record-tab-interaction-contract.mjs';
import './check-semantic-inspector-contract.mjs';
import './check-trainer-navigation-contract.mjs';
import './check-trainer-naming-contract.mjs';
import './check-visual-theme-contract.mjs';

import { checkControlTheme } from './check-control-theme.mjs';

checkControlTheme();
