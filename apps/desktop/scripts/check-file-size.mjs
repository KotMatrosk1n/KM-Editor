// SPDX-License-Identifier: GPL-3.0-only

// Historical compatibility entry point. App file-size limits were retired because
// ownership and testability matter more than arbitrary line counts. Static UI
// contract checks remain attached here so the existing typecheck command runs them.
import './check-analysis-preparation-contract.mjs';
import './check-analysis-selector-contract.mjs';
import './check-editor-field-lock-contract.mjs';
import './check-editor-interaction-contract.mjs';
import './check-editor-surface-inventory.mjs';
import './check-gameplay-input-contracts.mjs';
import './check-local-editor-draft-contract.mjs';
import './check-project-async-policy.mjs';
import './check-performance-diagnostics-contract.mjs';
import './check-focused-editor-contracts.mjs';
import './check-record-tab-interaction-contract.mjs';
import './check-semantic-inspector-contract.mjs';
import './check-swsh-encounter-area-copy-contract.mjs';
import './check-trainer-navigation-contract.mjs';
import './check-trainer-naming-contract.mjs';
import './check-visual-theme-contract.mjs';
import './check-za-items-tm-stage-contract.mjs';

import { readFileSync } from 'node:fs';
import { checkControlTheme } from './check-control-theme.mjs';

checkControlTheme();

const sidecarPublishScript = readFileSync(
  new URL('./publish-sidecar.mjs', import.meta.url),
  'utf8'
);
const sidecarFramingVerifier = readFileSync(
  new URL('./verify-sidecar-framing.mjs', import.meta.url),
  'utf8'
);

for (const requiredContract of [
  '-p:IncludeAllContentForSelfExtract=true',
  'verifyPublishedBridge(publishedBinary);',
  "execFileSync(executablePath, ['bridge-once']",
  "const sidecarFramingVerificationScript = resolve(scriptDirectory, 'verify-sidecar-framing.mjs');",
  'execFileSync(process.execPath, [sidecarFramingVerificationScript, executablePath]'
]) {
  if (!sidecarPublishScript.includes(requiredContract)) {
    throw new Error(`Packaged bridge contract is missing: ${requiredContract}`);
  }
}

console.log('Packaged sidecar extraction and startup contract passed.');

for (const requiredFramingProbe of [
  '59_169',
  'await sendInvalidUtf8Probe();',
  "'KM-SIDECAR-FRAMING-RECOVERY'",
  "'KM-SIDECAR-FRAMING-AFTER-CRLF'"
]) {
  if (!sidecarFramingVerifier.includes(requiredFramingProbe)) {
    throw new Error(`Persistent sidecar framing probe is missing: ${requiredFramingProbe}`);
  }
}

console.log('Persistent sidecar framing contract passed.');
