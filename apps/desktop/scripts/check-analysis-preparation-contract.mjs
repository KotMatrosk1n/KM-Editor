// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

function read(relativePath) {
  return readFileSync(new URL(relativePath, import.meta.url), 'utf8');
}

const model = read('../src/features/workbench/analysisPreparation.ts');
for (const unit of [
  'balanceLab: 1',
  'gameModules: 1',
  'guidedDesign: 1',
  'researchLab: 2',
  'semanticMerge: 1',
  'semanticProject: 2'
]) {
  assert.ok(model.includes(unit), `Analysis preparation lost measured unit ${unit}.`);
}
assert.match(
  model,
  /completedUnitCount = statuses\.filter\(\(status\) => status === 'ready'\)\.length/,
  'Analysis preparation must count completed operations from real ready query states.'
);
assert.match(
  model,
  /allRequiredToolsReady \? 100 : Math\.min\(99, measuredPercent\)/,
  'Analysis preparation must reserve 100 percent for complete readiness.'
);
assert.match(
  model,
  /current\.scopeKey === options\.scopeKey[\s\S]*?createAnalysisPreparationScopeState\(options\)/,
  'A changed project or source revision must synchronously resolve to fresh preparation state.'
);

const hook = read('../src/features/workbench/useAnalysisPreparation.ts');
assert.match(
  hook,
  /const visibleScopeState = useMemo\([\s\S]*?resolveAnalysisPreparationScopeState/,
  'The preparation hook must hide stale-scope preload state before effects run.'
);
assert.ok(
  [...hook.matchAll(/current\.scopeKey !== scopeKey/g)].length >= 3,
  'Preparation callbacks and deferred mounts must reject stale project scopes.'
);
assert.match(
  hook,
  /preloadTools: tools,[\s\S]*?progressBySystem:/,
  'Mounting a preparation tool must publish its loading state atomically.'
);

const panel = read('../src/features/workbench/AnalysisPreparationPanel.tsx');
assert.match(
  panel,
  /className="work-progress-track"[\s\S]*?role="progressbar"/,
  'The preparation panel must use the determinate KM progress track.'
);
assert.match(
  panel,
  /style=\{\{ width: `\$\{snapshot\.percent\}%` \}\}/,
  'The visible progress width must come from the measured snapshot.'
);
assert.ok(
  !panel.includes('work-progress-indeterminate'),
  'Measured analysis preparation must not use a scrolling indeterminate animation.'
);

const runtimeSystems = new Map([
  ['../src/features/balance-lab/BalanceLabRuntime.tsx', 'balanceLab'],
  ['../src/features/game-modules/GameModulesRuntime.tsx', 'gameModules'],
  ['../src/features/guided-design/GuidedDesignRuntime.tsx', 'guidedDesign'],
  ['../src/features/research-lab/ResearchLabRuntime.tsx', 'researchLab'],
  ['../src/features/semantic-merge/SemanticMergeRuntime.tsx', 'semanticMerge']
]);
for (const [path, system] of runtimeSystems) {
  assert.ok(
    read(path).includes(`preparationProgressFromQueryStatuses('${system}'`),
    `${system} must report its actual controller query status.`
  );
}

const workbench = read('../src/features/workbench/WorkbenchSection.tsx');
assert.match(
  workbench,
  /preparationScopeKeyRef\.current = preparationScopeKey;[\s\S]*?setMountedTools\(new Set/,
  'Workbench must replace, not union, mounted tools when the preparation scope changes.'
);

const app = read('../src/App.tsx');
assert.ok(
  app.includes("projectSourceRevision.sourceObservationToken ?? 'pending'"),
  'An active project must retain measurable loading and error progress before its source token exists.'
);

const invokedPath = process.argv[1];
if (invokedPath !== undefined && fileURLToPath(import.meta.url) === invokedPath) {
  console.log('Analysis preparation contract passed.');
}
