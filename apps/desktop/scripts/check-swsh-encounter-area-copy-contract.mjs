// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  createProjectedSwShEncounterTableCopyUpdates,
  findSwShEncounterAreaCopyTargetDraftCollisions
} from '../src/swshEncounterAreaCopy.ts';

function createSlot(slot, speciesId, form, weight, levelMin, levelMax) {
  return { form, levelMax, levelMin, slot, speciesId, weight };
}

function createTable(tableId, slots) {
  return { slots, tableId };
}

const sourceTable = createTable('route-1:symbol:normal', [
  createSlot(0, 10, 0, 30, 10, 15),
  createSlot(1, 11, 1, 70, 10, 15),
  createSlot(2, 12, 0, 5, 10, 15)
]);
const targetTable = createTable('route-1:hidden:normal', [
  createSlot(0, 20, 0, 50, 5, 20),
  createSlot(1, 21, 0, 50, 5, 20)
]);
const sourceSlotDrafts = {
  'route-1:symbol:normal:0': {
    form: '3',
    probability: '12',
    speciesId: '25'
  },
  'route-1:symbol:normal:1': {
    form: '4',
    probability: '88',
    speciesId: '26'
  }
};
const sourceLevelDrafts = {
  'route-1:symbol:normal': {
    levelMax: '60',
    levelMin: '50'
  }
};
const sourceSlotDraftSnapshot = structuredClone(sourceSlotDrafts);
const sourceLevelDraftSnapshot = structuredClone(sourceLevelDrafts);

const projectedUpdates = createProjectedSwShEncounterTableCopyUpdates(
  sourceTable,
  targetTable,
  sourceSlotDrafts,
  sourceLevelDrafts
);

assert.deepEqual(projectedUpdates, [
  {
    changes: [
      { field: 'speciesId', value: '25' },
      { field: 'form', value: '3' },
      { field: 'probability', value: '12' },
      { field: 'levelMax', value: '60' },
      { field: 'levelMin', value: '50' }
    ],
    slot: 0,
    tableId: 'route-1:hidden:normal'
  },
  {
    changes: [
      { field: 'speciesId', value: '26' },
      { field: 'form', value: '4' },
      { field: 'probability', value: '88' }
    ],
    slot: 1,
    tableId: 'route-1:hidden:normal'
  }
]);
assert.deepEqual(
  sourceSlotDrafts,
  sourceSlotDraftSnapshot,
  'Projecting an area copy must not mutate source slot drafts.'
);
assert.deepEqual(
  sourceLevelDrafts,
  sourceLevelDraftSnapshot,
  'Projecting an area copy must not mutate source level drafts.'
);

const vanillaUpdates = createProjectedSwShEncounterTableCopyUpdates(
  sourceTable,
  targetTable,
  {},
  {}
);
assert.deepEqual(
  vanillaUpdates[0]?.changes,
  [
    { field: 'speciesId', value: '10' },
    { field: 'form', value: '0' },
    { field: 'probability', value: '30' },
    { field: 'levelMin', value: '10' },
    { field: 'levelMax', value: '15' }
  ],
  'An untouched source must still copy its current loaded values.'
);
assert.equal(vanillaUpdates.length, 2, 'Source slots absent from the target must be ignored.');

const targetSlotDrafts = {
  'route-1:hidden:normal:0': { probability: '40' },
  'route-1:hidden:normal:1': { speciesId: '99' },
  'unrelated:table:0': { probability: '1' }
};
const targetLevelDrafts = {
  'route-1:hidden:normal': { levelMin: '7' },
  'unrelated:table': { levelMin: '8' }
};
const targetSlotDraftSnapshot = structuredClone(targetSlotDrafts);
const targetLevelDraftSnapshot = structuredClone(targetLevelDrafts);
const collisions = findSwShEncounterAreaCopyTargetDraftCollisions(
  projectedUpdates,
  targetSlotDrafts,
  targetLevelDrafts
);

assert.deepEqual(collisions, [
  {
    key: 'route-1:hidden:normal:0',
    kind: 'slot',
    slot: 0,
    tableId: 'route-1:hidden:normal'
  },
  {
    key: 'route-1:hidden:normal',
    kind: 'levels',
    slot: null,
    tableId: 'route-1:hidden:normal'
  },
  {
    key: 'route-1:hidden:normal:1',
    kind: 'slot',
    slot: 1,
    tableId: 'route-1:hidden:normal'
  }
]);
assert.deepEqual(
  targetSlotDrafts,
  targetSlotDraftSnapshot,
  'Inspecting target collisions must not mutate target slot drafts.'
);
assert.deepEqual(
  targetLevelDrafts,
  targetLevelDraftSnapshot,
  'Inspecting target collisions must not mutate target level drafts.'
);

const appSource = readFileSync(new URL('../src/App.tsx', import.meta.url), 'utf8');
const modalStart = appSource.indexOf('<EncounterAreaCopyConfirmationModal');
const confirmStart = appSource.indexOf('onConfirm={async () => {', modalStart);
const confirmEnd = appSource.indexOf('request={areaCopyRequest}', confirmStart);
assert.ok(modalStart >= 0 && confirmStart > modalStart && confirmEnd > confirmStart);
const confirmBody = appSource.slice(confirmStart, confirmEnd);
const failedSaveGuard = confirmBody.indexOf('if (!didSave)');
const slotDraftCleanup = confirmBody.indexOf('setDraftsBySlotKey');
const levelDraftCleanup = confirmBody.indexOf('setLevelDraftsByScopeKey');
const closeConfirmation = confirmBody.indexOf('setAreaCopyRequest(null)');
assert.ok(failedSaveGuard >= 0, 'Area copy must explicitly stop when staging fails.');
assert.ok(
  failedSaveGuard < slotDraftCleanup && failedSaveGuard < levelDraftCleanup,
  'A failed area copy must not clear target drafts.'
);
assert.ok(
  slotDraftCleanup < closeConfirmation && levelDraftCleanup < closeConfirmation,
  'The area-copy confirmation must close only after successful draft reconciliation.'
);

console.log('SwSh encounter area copy contract passed.');
