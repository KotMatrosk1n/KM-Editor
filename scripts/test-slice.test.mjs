// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import test from 'node:test';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const scriptPath = path.join(repoRoot, 'scripts', 'test-slice.mjs');

test('Royal Candy sources use one build and bounded parallel test shards', () => {
  const output = runChangedPrint([
    'src/KM.SwSh/RoyalCandy/SwShRoyalCandyWorkflowService.cs',
    'src/KM.SwSh/RoyalCandy/SwShRoyalCandyEditSessionService.cs',
  ]);

  assertRoyalCandyPlan(output);
  assert.equal(countOccurrences(output, 'dotnet build KM.Editor.slnx --no-restore --nologo'), 1);
  assert.equal(countOccurrences(output, 'Run bounded Royal Candy validation shards'), 1);
});

test('Royal Candy ExeFS patcher uses the bounded Royal Candy route', () => {
  const output = runChangedPrint([
    'src/KM.SwSh/ExeFs/SwShExeFsRoyalCandyMainPatcher.cs',
  ]);

  assertRoyalCandyPlan(output);
  assert.doesNotMatch(
    output,
    /FullyQualifiedName~ExeFs\|FullyQualifiedName~SwShHookReservationTests/,
  );
});

test('Royal Candy hook test changes split the former mega-class into exhaustive shards', () => {
  const output = runChangedPrint([
    'tests/KM.SwSh.Tests/Hooks/SwShHookReservationTests.cs',
  ]);

  assertRoyalCandyPlan(output);
  assert.doesNotMatch(output, /--filter "FullyQualifiedName~SwShHookReservationTests"/);
});

test('other Sword and Shield editors retain their existing focused routing', () => {
  const output = runChangedPrint([
    'src/KM.SwSh/CatchCap/SwShCatchCapWorkflowService.cs',
  ]);

  assert.match(
    output,
    /--filter "FullyQualifiedName~CatchCap\|FullyQualifiedName~SwShHookReservationTests"/,
  );
  assert.doesNotMatch(output, /Run bounded Royal Candy validation shards/);
  assert.doesNotMatch(output, /dotnet build KM\.Editor\.slnx --no-restore --nologo/);
});

test('Starting Items sources run both workflow and bridge integration coverage', () => {
  const output = runChangedPrint([
    'src/KM.SwSh/StartingItems/SwShStartingItemsEditSessionService.cs',
  ]);

  assert.match(output, /Run Sword and Shield StartingItems tests/);
  assert.match(output, /Run StartingItems bridge integration tests/);
  assert.equal(
    countOccurrences(
      output,
      '--filter "FullyQualifiedName~StartingItems|FullyQualifiedName~SwShHookReservationTests"',
    ),
    2,
  );
});

test('routine full validation still excludes Royal Candy hook shards', () => {
  const output = runPrint(['full', '--print']);

  assert.doesNotMatch(output, /Run Royal Candy cleanup hook tests/);
  assert.doesNotMatch(output, /Run Royal Candy behavior hook tests/);
  assert.doesNotMatch(
    output,
    /FullyQualifiedName~SwShHookReservationTests&FullyQualifiedName~RoyalCandy/,
  );
});

test('test-slice changes select only the focused routing regression test', () => {
  const output = runChangedPrint([
    'scripts/test-slice.mjs',
    'scripts/test-slice.test.mjs',
  ]);

  assert.equal(countOccurrences(output, 'node --test scripts/test-slice.test.mjs'), 1);
  assert.doesNotMatch(output, /Run fast validation shards/);
});

function assertRoyalCandyPlan(output) {
  assert.match(output, /Build backend test projects once for Royal Candy validation/);
  assert.match(output, /Run bounded Royal Candy validation shards/);
  assert.match(output, /Run Royal Candy cleanup hook tests/);
  assert.match(output, /Run Royal Candy behavior hook tests/);
  assert.match(output, /Run direct Royal Candy workflow tests/);
  assert.match(output, /Run direct Royal Candy ExeFS tests/);
  assert.match(output, /Run Royal Candy bridge integration tests/);
  assert.match(output, /Run remaining hook coexistence tests/);
  assert.match(
    output,
    /FullyQualifiedName~SwShHookReservationTests&FullyQualifiedName~RoyalCandy&FullyQualifiedName~Cleanup/,
  );
  assert.match(
    output,
    /FullyQualifiedName~SwShHookReservationTests&FullyQualifiedName~RoyalCandy&FullyQualifiedName!~Cleanup/,
  );
  assert.match(
    output,
    /FullyQualifiedName~SwShHookReservationTests&FullyQualifiedName!~RoyalCandy/,
  );
  assert.match(
    output,
    /FullyQualifiedName~RoyalCandy&FullyQualifiedName!~SwShHookReservationTests/,
  );
  assert.doesNotMatch(
    output,
    /FullyQualifiedName~RoyalCandy\|FullyQualifiedName~SwShHookReservationTests/,
  );

  const noBuildCount = countOccurrences(output, ' --no-build ');
  assert.equal(noBuildCount, 6);
}

function runChangedPrint(files) {
  return runPrint(['changed', '--print'], {
    KM_TEST_CHANGED_FILES_JSON: JSON.stringify(files),
  });
}

function runPrint(args, environment = {}) {
  const result = spawnSync(process.execPath, [scriptPath, ...args], {
    cwd: repoRoot,
    encoding: 'utf8',
    env: {
      ...process.env,
      ...environment,
    },
  });

  assert.equal(
    result.status,
    0,
    `test-slice exited with ${result.status}:\n${result.stdout}\n${result.stderr}`,
  );
  return result.stdout;
}

function countOccurrences(value, needle) {
  return value.split(needle).length - 1;
}
