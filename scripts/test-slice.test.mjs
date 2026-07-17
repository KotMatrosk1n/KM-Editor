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

test('Fairy Gym bridge contracts run their specialized desktop test', () => {
  const output = runChangedPrint([
    'apps/desktop/src/bridge/fairyGymBoostsContracts.ts',
  ]);

  assert.match(output, /Run nearby desktop bridge test/);
  assert.match(output, /src\/bridge\/fairyGymBoostsContracts\.test\.ts/);
  assert.match(output, /Run desktop bridge tests/);
});

test('Fashion Unlock bridge contracts run their specialized desktop test', () => {
  const output = runChangedPrint([
    'apps/desktop/src/bridge/fashionUnlockContracts.ts',
  ]);

  assert.match(output, /Run nearby desktop bridge test/);
  assert.match(output, /src\/bridge\/fashionUnlockContracts\.test\.ts/);
  assert.match(output, /Run desktop bridge tests/);
});

test('Gym Uniform Removal bridge contracts run their specialized desktop test', () => {
  const output = runChangedPrint([
    'apps/desktop/src/bridge/gymUniformRemovalContracts.ts',
  ]);

  assert.match(output, /Run nearby desktop bridge test/);
  assert.match(output, /src\/bridge\/gymUniformRemovalContracts\.test\.ts/);
  assert.match(output, /Run desktop bridge tests/);
});

test('shared App changes run focused editor regressions', () => {
  const output = runChangedPrint(['apps/desktop/src/App.tsx']);

  assert.match(output, /Run App regression tests after shell or shared fixture changes/);
  assert.match(output, /Run Fashion Unlock App regressions/);
  assert.match(output, /src\/fashionUnlockUi\.test\.tsx/);
  assert.match(output, /Run Gym Uniform Removal App regressions/);
  assert.match(output, /src\/gymUniformRemovalUi\.test\.tsx/);
  assert.match(output, /Run Z-A Wild Encounters App regressions/);
  assert.match(output, /src\/zaEncountersUi\.test\.tsx/);
});

test('Z-A location label sources run their mapping regressions', () => {
  const output = runChangedPrint([
    'src/KM.ZA/Data/ZaLumioseLocationLabels.cs',
  ]);

  assert.match(output, /Run Pokemon Legends Z-A Data tests/);
  assert.match(output, /FullyQualifiedName~ZaLumioseLocationLabelTests/);
});

test('Fashion Unlock backend sources run workflow and bridge integration coverage', () => {
  const output = runChangedPrint([
    'src/KM.SwSh/FashionUnlock/SwShFashionUnlockEditSessionService.cs',
  ]);

  assert.match(output, /Run Sword and Shield FashionUnlock tests/);
  assert.match(output, /Run FashionUnlock bridge integration tests/);
});

test('Gym Uniform Removal backend sources run workflow and bridge integration coverage', () => {
  const output = runChangedPrint([
    'src/KM.SwSh/GymUniformRemoval/SwShGymUniformRemovalEditSessionService.cs',
  ]);

  assert.match(output, /Run Sword and Shield GymUniformRemoval tests/);
  assert.match(output, /Run GymUniformRemoval bridge integration tests/);
});

test('fast validation retains Fairy Gym, Fashion Unlock, Gym Uniform, and Type Chart pending coverage', () => {
  const output = runPrint(['fast', '--print']);

  assert.match(output, /src\/fairyGymBoostsUi\.test\.tsx/);
  assert.match(output, /src\/features\/fairy-gym-boosts\/fairyGymBoostsPending\.test\.ts/);
  assert.match(output, /src\/bridge\/fashionUnlockContracts\.test\.ts/);
  assert.match(output, /src\/fashionUnlockUi\.test\.tsx/);
  assert.match(output, /src\/features\/fashion-unlock\/FashionUnlockSection\.test\.tsx/);
  assert.match(output, /src\/features\/fashion-unlock\/fashionUnlockPending\.test\.ts/);
  assert.match(output, /src\/bridge\/gymUniformRemovalContracts\.test\.ts/);
  assert.match(output, /src\/gymUniformRemovalUi\.test\.tsx/);
  assert.match(output, /src\/features\/gym-uniform-removal\/GymUniformRemovalSection\.test\.tsx/);
  assert.match(output, /src\/features\/gym-uniform-removal\/gymUniformRemovalPending\.test\.ts/);
  assert.match(output, /src\/features\/type-chart\/typeChartPending\.test\.ts/);
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
