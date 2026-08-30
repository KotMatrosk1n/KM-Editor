// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { createServer } from 'vite';

function read(relativePath) {
  return readFileSync(new URL(relativePath, import.meta.url), 'utf8').replace(/\r\n?/g, '\n');
}

const diagnostics = read('../src/performanceDiagnostics.ts');
assert.match(
  diagnostics,
  /'success'[\s\S]*?'expected-rejection'[\s\S]*?'unexpected-failure'/,
  'Performance samples must preserve the three stable outcome classes.'
);
assert.match(
  diagnostics,
  /schemaVersion: 2[\s\S]*?sessionOnly: true[\s\S]*?contentBlind: true[\s\S]*?outcomeDefinitions[\s\S]*?outcomeCounts/,
  'The copied schema must describe its privacy boundary and outcome classification.'
);
assert.doesNotMatch(
  diagnostics,
  /semanticCode|apiError|diagnostics:/,
  'Performance samples must not retain bridge payloads, diagnostics, or error details.'
);

const vite = await createServer({
  appType: 'custom',
  logLevel: 'silent',
  root: fileURLToPath(new URL('..', import.meta.url)),
  server: { middlewareMode: true }
});
try {
  const [classificationModule, errorModule, projectBridgeErrorModule] = await Promise.all([
    vite.ssrLoadModule('/src/bridge/projectBridgeErrorClassification.ts'),
    vite.ssrLoadModule('/src/errorCodes.ts'),
    vite.ssrLoadModule('/src/bridge/projectBridgeError.ts')
  ]);
  const {
    gameplaySettingsErrorCodes,
    guidedDesignErrorCodes,
    inGameSettingsPackageErrorCodes,
    kmRecipeErrorCodes,
    projectBridgeErrorCodes,
    researchLabErrorCodes,
    semanticExploreErrorCodes,
    semanticMergeErrorCodes,
    swshDynamaxAdventuresErrorCodes,
    swshPlacementErrorCodes
  } = errorModule;
  const { isExpectedProjectBridgeRejection } = classificationModule;
  const { ProjectBridgeError } = projectBridgeErrorModule;
  const expectedCodes = [
    ...Object.values(gameplaySettingsErrorCodes),
    ...Object.values(guidedDesignErrorCodes),
    ...Object.values(inGameSettingsPackageErrorCodes),
    ...Object.values(kmRecipeErrorCodes),
    ...Object.values(semanticMergeErrorCodes),
    ...Object.values(semanticExploreErrorCodes),
    ...Object.values(researchLabErrorCodes),
    swshDynamaxAdventuresErrorCodes.seedInvalid,
    swshDynamaxAdventuresErrorCodes.seedLimitInvalid,
    swshDynamaxAdventuresErrorCodes.startSeedInvalid,
    swshPlacementErrorCodes.catalogStale,
    projectBridgeErrorCodes.gameMismatch,
    projectBridgeErrorCodes.outputCheckpointConflict,
    projectBridgeErrorCodes.outputCheckpointNotFound,
    projectBridgeErrorCodes.outputConcurrentModification,
    projectBridgeErrorCodes.outputLimitExceeded,
    projectBridgeErrorCodes.outputOwnershipUnproven,
    projectBridgeErrorCodes.outputRecoveryRequired,
    projectBridgeErrorCodes.outputRootBusy,
    projectBridgeErrorCodes.outputUnsafePath,
    projectBridgeErrorCodes.projectRelocationConflict,
    projectBridgeErrorCodes.projectRelocationMismatch,
    projectBridgeErrorCodes.workspaceConcurrentModification
  ];
  const expectedCodeSet = new Set(expectedCodes);
  const createBridgeError = (code) => new ProjectBridgeError({
    code,
    diagnostics: [],
    message: 'Contract probe.'
  });

  for (const code of expectedCodes) {
    assert.equal(
      isExpectedProjectBridgeRejection(createBridgeError(code)),
      true,
      `Expected bridge rejection code ${code} was classified as unexpected.`
    );
  }
  for (const code of [
    ...Object.values(projectBridgeErrorCodes),
    ...Object.values(swshDynamaxAdventuresErrorCodes)
  ].filter((candidate) => !expectedCodeSet.has(candidate))) {
    assert.equal(
      isExpectedProjectBridgeRejection(createBridgeError(code)),
      false,
      `Genuine bridge failure code ${code} was classified as expected.`
    );
  }
  assert.equal(
    isExpectedProjectBridgeRejection(new Error('Not a bridge rejection.')),
    false,
    'Ordinary errors must remain unexpected.'
  );
} finally {
  await vite.close();
}

const request = read('../src/bridge/projectBridgeRequest.ts');
assert.match(
  request,
  /isExpectedProjectBridgeRejection\(error\) \? 'expected-rejection' : 'unexpected-failure'/,
  'Bridge errors must be classified through the shared expected-rejection catalog.'
);
assert.match(
  request,
  /recordBridgePerformanceDiagnostic\([\s\S]*?'success',[\s\S]*?response[\s\S]*?\)/,
  'Successful response objects must be associated with their timing sample.'
);

const gameScope = read('../src/bridge/gameScopedProjectBridge.ts');
assert.equal(
  gameScope.match(/reclassifyBridgePerformanceDiagnostic\([^)]*'expected-rejection'\)/g)?.length,
  2,
  'Both stale success and stale error paths must become expected rejections.'
);

const uiDiagnostics = read('../src/uiErrorDiagnostics.ts');
assert.match(
  uiDiagnostics,
  /isExpectedProjectBridgeRejection\(error\)/,
  'UI and performance diagnostics must share the same expected-rejection catalog.'
);

const panel = read('../src/features/settings/PerformanceDiagnosticsPanel.tsx');
for (const key of ['successes', 'expectedRejections', 'unexpectedFailures']) {
  assert.ok(panel.includes(`settings.performance.summary.${key}`), `Summary table is missing ${key}.`);
}
assert.doesNotMatch(
  panel,
  /settings\.performance\.summary\.failures|summary\.failures/,
  'The summary table must not collapse every rejection into a generic failure count.'
);

const requiredLocaleKeys = [
  'settings.performance.summary.outcomeHelp',
  'settings.performance.summary.successes',
  'settings.performance.summary.expectedRejections',
  'settings.performance.summary.unexpectedFailures'
];
const localeDirectory = new URL('../src/localization/resources/', import.meta.url);
for (const localeFile of readdirSync(localeDirectory).filter((name) => name.endsWith('.json'))) {
  const locale = JSON.parse(readFileSync(new URL(localeFile, localeDirectory), 'utf8')).keys;
  for (const key of requiredLocaleKeys) {
    assert.equal(typeof locale[key], 'string', `${localeFile} is missing ${key}.`);
    assert.ok(locale[key].trim().length > 0, `${localeFile} has an empty ${key}.`);
  }
  assert.equal(
    locale['settings.performance.summary.failures'],
    undefined,
    `${localeFile} retained the ambiguous generic failure label.`
  );
}

const runtimeProgram = String.raw`
  import assert from 'node:assert/strict';
  import {
    clearPerformanceDiagnostics,
    createPerformanceDiagnosticsSummary,
    getPerformanceDiagnosticsSnapshot,
    recordBridgePerformanceDiagnostic,
    reclassifyBridgePerformanceDiagnostic,
    setPerformanceDiagnosticsEnabled,
    summarizePerformanceDiagnostics
  } from ${JSON.stringify(new URL('../src/performanceDiagnostics.ts', import.meta.url).href)};

  setPerformanceDiagnosticsEnabled(true);
  clearPerformanceDiagnostics();
  const staleResponse = {};
  const staleError = new Error();
  recordBridgePerformanceDiagnostic('semantic.entity', 11.4, 'success', staleResponse);
  recordBridgePerformanceDiagnostic('semantic.entity', 19.6, 'unexpected-failure', staleError);
  recordBridgePerformanceDiagnostic('output.recovery.status', 7.1, 'success', {});
  recordBridgePerformanceDiagnostic('project.sourceRevision.read', 29.8, 'unexpected-failure', new Error());
  reclassifyBridgePerformanceDiagnostic(staleResponse, 'expected-rejection');
  reclassifyBridgePerformanceDiagnostic(staleError, 'expected-rejection');

  const snapshot = getPerformanceDiagnosticsSnapshot();
  assert.deepEqual(snapshot.samples.map((sample) => sample.outcome), [
    'expected-rejection',
    'expected-rejection',
    'success',
    'unexpected-failure'
  ]);
  const semantic = summarizePerformanceDiagnostics(snapshot.samples)
    .find((entry) => entry.command === 'semantic.entity');
  assert.deepEqual(
    {
      expectedRejections: semantic.expectedRejections,
      successes: semantic.successes,
      unexpectedFailures: semantic.unexpectedFailures
    },
    { expectedRejections: 2, successes: 0, unexpectedFailures: 0 }
  );

  const summary = JSON.parse(createPerformanceDiagnosticsSummary());
  assert.equal(summary.schemaVersion, 2);
  assert.equal(summary.sessionOnly, true);
  assert.equal(summary.contentBlind, true);
  assert.deepEqual(summary.outcomeCounts, {
    successes: 1,
    expectedRejections: 2,
    unexpectedFailures: 1
  });
  assert.equal(JSON.stringify(summary).includes('Error'), false);
`;
const runtime = spawnSync(
  process.execPath,
  [
    '--experimental-strip-types',
    '--experimental-specifier-resolution=node',
    '--input-type=module',
    '--eval',
    runtimeProgram
  ],
  { encoding: 'utf8' }
);
assert.equal(runtime.status, 0, runtime.stderr || runtime.stdout);

console.log('PASS: performance diagnostics classify outcomes without retaining content.');
