// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

function read(relativePath) {
  return readFileSync(new URL(relativePath, import.meta.url), 'utf8').replace(/\r\n?/g, '\n');
}

function between(source, start, end) {
  const startIndex = source.indexOf(start);
  const endIndex = source.indexOf(end, startIndex + start.length);
  assert.ok(startIndex >= 0, `Missing contract start: ${start}`);
  assert.ok(endIndex > startIndex, `Missing contract end: ${end}`);
  return source.slice(startIndex, endIndex);
}

const app = read('../src/App.tsx');
const store = read('../src/workbenchStore.ts');
const trainerSection = between(
  app,
  'function TrainersSection({',
  'function ZaTrainerBulkConfirmationModal({'
);
const loadedWorkflowReset = between(
  store,
  'function createLoadedWorkflowResetState()',
  'function createProjectSessionResetState()'
);
const projectSessionReset = between(
  store,
  'function createProjectSessionResetState()',
  'export const useWorkbenchStore'
);
const trainersWorkflowSetter = between(
  store,
  '  setTrainersWorkflow: (trainersWorkflow) =>',
  '  setTrainerPoolsWorkflow:'
);
const categoryButton = between(
  trainerSection,
  '{trainerCategories.map((category) => (',
  '</button>'
);

assert.match(
  store,
  /selectedTrainerCategoryId: TrainerCategoryId;[\s\S]*?setSelectedTrainerCategoryId: \(selectedTrainerCategoryId: TrainerCategoryId\) => void;/,
  'Trainer category state must be owned by the workbench session.'
);
assert.equal(
  [...store.matchAll(/selectedTrainerCategoryId: 'all'/g)].length,
  2,
  'Trainer category state must reset for a new workflow session and initialize safely.'
);
assert.doesNotMatch(
  loadedWorkflowReset,
  /selectedTrainerCategoryId/,
  'Workflow payload eviction must preserve the user-owned Trainer category view.'
);
assert.match(
  projectSessionReset,
  /selectedTrainerCategoryId: 'all'/,
  'A new game or project session must reset the Trainer category safely.'
);
assert.doesNotMatch(
  trainersWorkflowSetter,
  /selectedTrainerCategoryId/,
  'Reloading Trainer data must not reset the user-owned category view.'
);
assert.match(
  store,
  /setSelectedTrainerCategoryId: \(selectedTrainerCategoryId\) =>[\s\S]*?set\(\{ selectedTrainerCategoryId \}\)/,
  'Trainer category state must expose a session-scoped setter.'
);
assert.equal(
  [...app.matchAll(/onTrainerCategoryChange=\{setSelectedTrainerCategoryId\}/g)].length,
  3,
  'Every Trainer editor family must receive the session-owned category setter.'
);
assert.equal(
  [...app.matchAll(/selectedTrainerCategoryId=\{selectedTrainerCategoryId\}/g)].length,
  3,
  'Every Trainer editor family must receive the session-owned category selection.'
);
assert.doesNotMatch(
  trainerSection,
  /const\s*\[\s*selectedTrainerCategoryId\b/,
  'The conditionally mounted Trainer editor must not reset category state locally.'
);
assert.match(
  trainerSection,
  /editorFamily === 'za' &&\s*workflow !== null &&\s*selectedTrainerCategoryId !== 'all'/,
  'An evicted Trainer payload must not normalize the retained category before reload completes.'
);
assert.match(
  categoryButton,
  /onTrainerCategoryChange\(category\.id\)/,
  'Trainer category tabs must update the session-owned category selection.'
);
assert.doesNotMatch(
  categoryButton,
  /onSelectTrainer/,
  'Changing Trainer categories must not race or replace the selected Trainer.'
);
