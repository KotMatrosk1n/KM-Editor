// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import ts from 'typescript';
import {
  BoundedLruCache,
  DeferredStrictModeCleanup,
  ProjectOperationAdmissionError,
  ProjectQueryEpoch,
  ProjectReadAdmissionError,
  ProjectReadKeyTooLargeError,
  ProjectReadSingleFlight,
  ProjectSerialTaskQueue,
  analysisBridgeOperationPolicies,
  measureRealQueryUnits,
  runIndependentProjectRead,
  runOrderedProjectOperation,
  stableProjectQueryKey
} from '../src/utils/projectAsyncPolicy.ts';
const preparationSource = readFileSync(
  new URL('../src/features/workbench/analysisPreparation.ts', import.meta.url),
  'utf8'
).replace(
  "'../../utils/projectAsyncPolicy'",
  `'${new URL('../src/utils/projectAsyncPolicy.ts', import.meta.url).href}'`
);
const preparationModule = await import(`data:text/javascript;base64,${Buffer.from(
  ts.transpileModule(preparationSource, {
    compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 }
  }).outputText
).toString('base64')}`);
const {
  analysisToolIds,
  createAnalysisPreparationProgress,
  createAnalysisPreparationSnapshot,
  emptyAnalysisPreparationProgress,
  mergeAnalysisPreparationProgress,
  nextAnalysisPreloadTools,
  resolveAnalysisPreparationScopeState
} = preparationModule;

const delay = (milliseconds = 0) => new Promise((resolve) => setTimeout(resolve, milliseconds));

async function checkSingleFlight() {
  const owner = {};
  const reads = new ProjectReadSingleFlight(2);
  let firstResolve;
  let secondResolve;
  let firstCalls = 0;
  const first = reads.run(owner, 'first', () => {
    firstCalls += 1;
    return new Promise((resolve) => { firstResolve = resolve; });
  });
  const duplicate = reads.run(owner, 'first', () => {
    firstCalls += 1;
    return Promise.resolve('duplicate');
  });
  const second = reads.run(owner, 'second', () => (
    new Promise((resolve) => { secondResolve = resolve; })
  ));
  assert.strictEqual(duplicate, first, 'Identical in-flight reads must share one promise.');
  assert.equal(firstCalls, 1, 'Identical in-flight reads must execute once.');
  await assert.rejects(
    reads.run(owner, 'third', () => Promise.resolve('overflow')),
    ProjectReadAdmissionError,
    'Distinct in-flight keys must fail closed at the configured bound.'
  );
  firstResolve('first');
  secondResolve('second');
  assert.deepEqual(await Promise.all([first, second]), ['first', 'second']);

  let rejectedCalls = 0;
  await assert.rejects(reads.run(owner, 'retry', () => {
    rejectedCalls += 1;
    return Promise.reject(new Error('expected rejection'));
  }), /expected rejection/);
  assert.equal(
    await reads.run(owner, 'retry', () => {
      rejectedCalls += 1;
      return Promise.resolve('recovered');
    }),
    'recovered',
    'A rejected read must be removed so the exact key can retry.'
  );
  assert.equal(rejectedCalls, 2);

  const keyBounded = new ProjectReadSingleFlight(2, 8);
  await assert.rejects(
    keyBounded.run(owner, 'ééééé', () => Promise.resolve('oversized')),
    ProjectReadKeyTooLargeError,
    'Canonical in-flight key retention must be bounded by bytes, not UTF-16 length.'
  );
  await assert.rejects(
    runIndependentProjectRead(
      'search',
      {},
      { searchText: 'x'.repeat((256 * 1_024) + 1) },
      () => Promise.resolve('oversized')
    ),
    ProjectReadKeyTooLargeError,
    'The public independent-read helper must reject oversized canonical request keys.'
  );

  const stressed = new ProjectReadSingleFlight(64);
  let releaseStress;
  let stressCalls = 0;
  const stressRequests = Array.from({ length: 1_000 }, () => stressed.run(
    owner,
    'shared-stress-key',
    () => {
      stressCalls += 1;
      return new Promise((resolve) => { releaseStress = resolve; });
    }
  ));
  assert.ok(
    stressRequests.every((request) => request === stressRequests[0]),
    'A StrictMode-like duplicate burst must retain only one exact-key promise.'
  );
  assert.equal(stressCalls, 1);
  releaseStress('stress-complete');
  assert.equal((await Promise.all(stressRequests)).at(-1), 'stress-complete');

  await assert.rejects(
    stressed.run(owner, 'synchronous-fault', () => { throw new Error('synchronous fault'); }),
    /synchronous fault/
  );
  assert.equal(
    await stressed.run(owner, 'synchronous-fault', () => Promise.resolve('retry-complete')),
    'retry-complete',
    'A synchronously rejected factory must release its exact-key admission.'
  );
}

function checkLruBounds() {
  const countBounded = new BoundedLruCache({ maximumEntries: 2 });
  countBounded.set('a', 1);
  countBounded.set('b', 2);
  assert.equal(countBounded.get('a'), 1);
  countBounded.set('c', 3);
  assert.equal(countBounded.get('b'), undefined, 'The least recently used entry must leave first.');
  assert.equal(countBounded.get('a'), 1);
  assert.equal(countBounded.get('c'), 3);

  const weightBounded = new BoundedLruCache({
    maximumEntries: 10,
    maximumWeight: 5,
    weight: (value) => value.length
  });
  weightBounded.set('a', '123');
  weightBounded.set('b', '45');
  weightBounded.set('c', '6');
  assert.equal(weightBounded.get('a'), undefined, 'Weight overflow must evict oldest entries.');
  assert.equal(weightBounded.get('b'), '45');
  assert.equal(weightBounded.get('c'), '6');
  weightBounded.set('oversized', '123456');
  assert.equal(weightBounded.get('oversized'), undefined, 'An oversized value must not be retained.');
}

function checkFreshness() {
  const freshness = new ProjectQueryEpoch();
  const original = freshness.capture('records');
  const replacement = freshness.supersede('records');
  assert.equal(original.signal.aborted, true);
  assert.equal(freshness.isCurrent(original), false);
  assert.equal(freshness.isCurrent(replacement), true);
  freshness.invalidateAll();
  assert.equal(replacement.signal.aborted, true);
  assert.equal(freshness.isCurrent(replacement), false);
}

async function checkDeferredCleanup() {
  const deferred = new DeferredStrictModeCleanup();
  let cleanupCount = 0;
  deferred.schedule(() => { cleanupCount += 1; });
  deferred.cancel();
  await delay(5);
  assert.equal(cleanupCount, 0, 'StrictMode replay must cancel deferred destructive cleanup.');
  deferred.schedule(() => { cleanupCount += 1; });
  await delay(5);
  assert.equal(cleanupCount, 1, 'A real unmount must run deferred cleanup exactly once.');
}

async function checkSerialAdmission() {
  const queue = new ProjectSerialTaskQueue();
  const order = [];
  const first = queue.run(async () => {
    order.push('first-start');
    await delay(2);
    order.push('first-end');
  });
  const second = queue.run(async () => {
    order.push('second');
  });
  await Promise.all([first, second]);
  assert.deepEqual(order, ['first-start', 'first-end', 'second']);

  const faultQueue = new ProjectSerialTaskQueue();
  const failed = faultQueue.run(() => Promise.reject(new Error('expected serial fault')));
  const afterFailure = faultQueue.run(async () => 'continued');
  await assert.rejects(failed, /expected serial fault/);
  assert.equal(await afterFailure, 'continued', 'A failed operation must not poison the serial tail.');

  const boundedQueue = new ProjectSerialTaskQueue(2);
  let releaseBounded;
  const boundedFirst = boundedQueue.run(() => (
    new Promise((resolve) => { releaseBounded = resolve; })
  ));
  const boundedSecond = boundedQueue.run(async () => 'second');
  await assert.rejects(
    boundedQueue.run(async () => 'overflow'),
    ProjectOperationAdmissionError,
    'Ordered operations must fail closed instead of retaining an unbounded queue.'
  );
  releaseBounded('first');
  assert.deepEqual(await Promise.all([boundedFirst, boundedSecond]), ['first', 'second']);
  assert.equal(
    await boundedQueue.run(async () => 'admitted-after-release'),
    'admitted-after-release'
  );

  const admittedOwner = { identity: 'original' };
  let releaseOwner;
  const ownerBlocker = runOrderedProjectOperation(
    'compareExternal',
    admittedOwner,
    async (bridge) => {
      assert.strictEqual(bridge, admittedOwner);
      await new Promise((resolve) => { releaseOwner = resolve; });
    }
  );
  const ownerFollower = runOrderedProjectOperation(
    'compareExternal',
    admittedOwner,
    async (bridge) => bridge.identity
  );
  await delay(0);
  releaseOwner();
  await ownerBlocker;
  assert.equal(
    await ownerFollower,
    'original',
    'Queued work must execute against the bridge owner captured at admission.'
  );
}

function checkStableIdentityAndProgress() {
  assert.equal(
    stableProjectQueryKey('search', { alpha: 1, nested: { beta: 2, gamma: 3 } }),
    stableProjectQueryKey('search', { nested: { gamma: 3, beta: 2 }, alpha: 1 }),
    'Logically identical request objects must have the same exact query key.'
  );
  assert.throws(
    () => stableProjectQueryKey('search', new Date()),
    /only JSON objects and arrays/,
    'Non-JSON request identities must fail closed instead of colliding.'
  );
  const invalidIdentities = [
    undefined,
    () => undefined,
    Symbol('identity'),
    Number.NaN,
    Number.POSITIVE_INFINITY,
    Number.NEGATIVE_INFINITY,
    -0,
    [undefined],
    Array(1),
    Object.assign([1], { extra: true }),
    { missing: undefined }
  ];
  for (const identity of invalidIdentities) {
    assert.throws(
      () => stableProjectQueryKey('search', identity),
      /Project query identity/,
      'Lossy or non-JSON identities must fail closed instead of sharing a key.'
    );
  }
  const symbolObject = {};
  symbolObject[Symbol('hidden')] = true;
  assert.throws(() => stableProjectQueryKey('search', symbolObject), /symbol properties/);
  const accessorObject = {};
  Object.defineProperty(accessorObject, 'value', { enumerable: true, get: () => 1 });
  assert.throws(() => stableProjectQueryKey('search', accessorObject), /data values/);
  const nonEnumerableObject = {};
  Object.defineProperty(nonEnumerableObject, 'value', { value: 1 });
  assert.throws(() => stableProjectQueryKey('search', nonEnumerableObject), /enumerable data values/);
  assert.notEqual(
    stableProjectQueryKey('search', JSON.parse('{"__proto__":"retained"}')),
    stableProjectQueryKey('search', {}),
    'Prototype-looking JSON keys must remain part of the exact identity.'
  );
  assert.deepEqual(
    measureRealQueryUnits(['ready', 'loading', 'error']),
    {
      completedUnitCount: 1,
      hasError: true,
      isReady: false,
      totalUnitCount: 3
    }
  );
  assert.deepEqual(
    measureRealQueryUnits(['ready', 'ready']),
    {
      completedUnitCount: 2,
      hasError: false,
      isReady: true,
      totalUnitCount: 2
    }
  );
}

function checkPreparationModesAndMonotonicity() {
  const waitingStates = Object.fromEntries([
    'semanticProject',
    ...analysisToolIds
  ].map((system) => [system, 'waiting']));
  const readySemanticStates = { ...waitingStates, semanticProject: 'ready' };
  assert.deepEqual(
    nextAnalysisPreloadTools({
      deferBackgroundWork: false,
      mode: 'reduced',
      preloadTools: [],
      semanticState: 'ready',
      states: readySemanticStates
    }),
    [],
    'Reduced mode must retain on-demand admission.'
  );
  assert.deepEqual(
    nextAnalysisPreloadTools({
      deferBackgroundWork: false,
      mode: 'balanced',
      preloadTools: [],
      semanticState: 'ready',
      states: readySemanticStates
    }),
    ['balanceLab'],
    'Balanced mode must admit one real tool at a time.'
  );
  assert.deepEqual(
    nextAnalysisPreloadTools({
      deferBackgroundWork: false,
      mode: 'fastest',
      preloadTools: [],
      semanticState: 'ready',
      states: readySemanticStates
    }),
    analysisToolIds,
    'Fastest mode must admit all independent tools together.'
  );
  assert.deepEqual(
    nextAnalysisPreloadTools({
      deferBackgroundWork: true,
      mode: 'balanced',
      preloadTools: [],
      semanticState: 'ready',
      states: readySemanticStates
    }),
    [],
    'Balanced mode must defer background work while interactive work owns the host.'
  );

  const completed = createAnalysisPreparationProgress('researchLab', 'ready');
  assert.deepEqual(
    mergeAnalysisPreparationProgress(
      completed,
      createAnalysisPreparationProgress('researchLab', 'loading', 0)
    ),
    completed,
    'A remount must not make completed measured work look like it restarted.'
  );
  const almostReady = emptyAnalysisPreparationProgress();
  for (const system of ['semanticProject', ...analysisToolIds]) {
    almostReady[system] = createAnalysisPreparationProgress(system, 'ready');
  }
  almostReady.researchLab = createAnalysisPreparationProgress('researchLab', 'loading', 1);
  const incomplete = createAnalysisPreparationSnapshot({
    mode: 'fastest',
    progressBySystem: almostReady
  });
  assert.equal(incomplete.percent, 87);
  assert.ok(incomplete.percent < 100, 'Incomplete real units must never report 100 percent.');
  const resetScope = resolveAnalysisPreparationScopeState(
    { preloadTools: analysisToolIds, progressBySystem: almostReady, scopeKey: 'old' },
    {
      scopeKey: 'new',
      semanticProgress: createAnalysisPreparationProgress('semanticProject', 'loading')
    }
  );
  assert.equal(resetScope.scopeKey, 'new');
  assert.deepEqual(resetScope.preloadTools, []);
}

function checkOperationPolicy() {
  const expectedOperations = [
    'closeResearchSource',
    'compare',
    'compareExternal',
    'compareResearchSources',
    'exportKmRecipe',
    'getCapabilities',
    'getEntity',
    'getGameModuleCapabilities',
    'getGuidedDesignCapabilities',
    'getImpact',
    'getOwnership',
    'getReferences',
    'getResearchLabCapabilities',
    'getSemanticChanges',
    'getSemanticMergeCapabilities',
    'mutateResearchAnnotations',
    'openResearchSource',
    'openSemanticMergeSource',
    'previewGuidedDesign',
    'previewKmRecipe',
    'previewSemanticMerge',
    'queryBalanceLab',
    'queryGameModule',
    'readProjectSourceRevision',
    'readResearchAnnotations',
    'readResearchByteWindow',
    'search',
    'validateKmRecipe'
  ];
  assert.deepEqual(Object.keys(analysisBridgeOperationPolicies).sort(), expectedOperations);
  assert.equal(analysisBridgeOperationPolicies.mutateResearchAnnotations, 'mutation');
  assert.equal(analysisBridgeOperationPolicies.openResearchSource, 'resourceMutation');
  assert.equal(analysisBridgeOperationPolicies.getCapabilities, 'independentRead');
  assert.equal(analysisBridgeOperationPolicies.compareExternal, 'orderedRead');
  assert.equal(analysisBridgeOperationPolicies.validateKmRecipe, 'orderedRead');
}

await checkSingleFlight();
checkLruBounds();
checkFreshness();
await checkDeferredCleanup();
await checkSerialAdmission();
checkStableIdentityAndProgress();
checkPreparationModesAndMonotonicity();
checkOperationPolicy();

console.log('Project async policy contract passed.');
