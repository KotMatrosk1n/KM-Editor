// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

function read(relativePath) {
  return readFileSync(new URL(relativePath, import.meta.url), 'utf8').replace(/\r\n?/g, '\n');
}

const resolver = read('../../../src/KM.ZA/Trainers/ZaTrainerDisplayIdentityResolver.cs');
for (const authoritativeSource of [
  'labels.TrainerTypeByHash',
  'labels.TrainerNameFromText',
  'ZaTrainerNameCatalog.ResolveTrainerNameKeys',
  'ZaLabels.FormatTrainerIdForLookup',
  'ZaTextLabelLookup.NormalizeTrainerName'
]) {
  assert.ok(
    resolver.includes(authoritativeSource),
    `The shared Z-A Trainer display resolver lost ${authoritativeSource}.`
  );
}

const trainersWorkflow = read('../../../src/KM.ZA/Trainers/ZaTrainersWorkflowService.cs');
assert.equal(
  [...trainersWorkflow.matchAll(/ZaTrainerDisplayIdentityResolver\.Resolve\(/g)].length,
  2,
  'Both full and read-only Z-A Trainer record paths must use the shared display resolver.'
);
assert.doesNotMatch(
  trainersWorkflow,
  /labels\.TrainerNameFromText/,
  'Z-A Trainer record paths must not grow a second display-name resolver.'
);

const trainerPoolsWorkflow = read(
  '../../../src/KM.ZA/TrainerPools/ZaTrainerPoolsWorkflowService.cs'
);
assert.match(
  trainerPoolsWorkflow,
  /ReadRoster\(rosterSource\.Bytes, labels, diagnostics\)/,
  'Trainer Pools must resolve names from the exact roster snapshot it validates.'
);
assert.match(
  trainerPoolsWorkflow,
  /ZaTrainerDisplayIdentityResolver\.Resolve\(index, row\.Value, labels\)\.Name/,
  'Trainer Pools must use the authoritative Z-A Trainer display resolver.'
);
assert.doesNotMatch(
  trainerPoolsWorkflow,
  /new ZaTrainersWorkflowService/,
  'Trainer Pools must not reload an independent Trainers workflow for display names.'
);
assert.match(
  trainerPoolsWorkflow,
  /!identities\.TryGetValue\(appearance\.TrainerId, out var identity\)[\s\S]*?identity\.RosterIndex < 0[\s\S]*?diagnostics\.Add\(Error\(/,
  'An unresolved Trainer Pool identity must remain a blocking diagnostic.'
);

const retention = read('../src/workflowRetention.ts');
assert.match(
  retention,
  /text: \['placement', 'trainers', 'trainerPools'\]/,
  'Text writes must refresh retained Trainers and Trainer Pools naming.'
);
assert.match(
  retention,
  /trainers: \['trainerPools'\]/,
  'Trainer writes must refresh retained Trainer Pools naming.'
);
