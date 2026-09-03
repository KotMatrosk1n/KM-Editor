// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { createServer } from 'vite';

const desktopRoot = fileURLToPath(new URL('../', import.meta.url));

function read(relativePath) {
  return readFileSync(new URL(relativePath, `file:///${desktopRoot.replaceAll('\\', '/')}/`), 'utf8')
    .replace(/\r\n?/gu, '\n');
}

function sourceSection(source, startMarker, endMarker) {
  const start = source.indexOf(startMarker);
  assert.notEqual(start, -1, `Missing source marker: ${startMarker}`);
  const end = source.indexOf(endMarker, start + startMarker.length);
  assert.notEqual(end, -1, `Missing source marker after ${startMarker}: ${endMarker}`);
  return source.slice(start, end);
}

const appSource = read('src/App.tsx');
const featureVisibilitySource = read('src/workbench/featureVisibility.ts');
const zaItemsStageHandler = sourceSection(
  appSource,
  'const handleStageZaItemDrafts = async (',
  'const handleStageItemVanilla = async ('
);
const editSessionMutation = sourceSection(
  appSource,
  'const runEditSessionMutation = async <',
  'const advancedAuthoringBinding ='
);

assert.match(
  featureVisibilitySource,
  /namedChangeSets:\s*false/u,
  'Named change sets must remain disabled for the ordinary Z-A Items stage contract.'
);
assert.equal(
  [...zaItemsStageHandler.matchAll(/bridge\.updatePokemonFields\(/gu)].length,
  1,
  'A Z-A Items TM stage must issue exactly one Pokemon batch request.'
);
assert.match(
  zaItemsStageHandler,
  /if \(pokemonChanges\.length > 0\) \{[\s\S]*?bridge\.updatePokemonFields\(\{[\s\S]*?session: nextSession,[\s\S]*?updates: pokemonChanges/u,
  'Z-A Items must pass the current edit session and the complete TM draft batch to one Pokemon update.'
);
assert.doesNotMatch(
  zaItemsStageHandler,
  /captureChangeSetSession|captureStagedSession|changeSets\.captureSession/u,
  'The ordinary Z-A Items handler must not capture a named change set directly.'
);
assert.match(
  editSessionMutation,
  /if \(\s*stageGate\.enabled[\s\S]*?capturedResponse = await stageGate\.captureStagedSession/u,
  'Change-set capture must stay behind the disabled stage gate.'
);
assert.match(
  zaItemsStageHandler,
  /catch \(error\) \{\s*setBridgeDiagnostics\(toBridgeDiagnostics\(error\)\);\s*return false;\s*\} finally \{[\s\S]*?setIsItemUpdating\(false\);[\s\S]*?setIsPokemonUpdating\(false\);/u,
  'A rejected TM batch must resolve as not staged and release both Items and Pokemon busy state.'
);

const vite = await createServer({
  appType: 'custom',
  configFile: false,
  logLevel: 'error',
  root: desktopRoot,
  server: { middlewareMode: true }
});

try {
  const { createSvBatchFieldProjectBridgeApi } = await vite.ssrLoadModule(
    '/src/bridge/svBatchFieldProjectBridge.ts'
  );
  const { runSessionLocalEditorSourceMutation } = await vite.ssrLoadModule(
    '/src/components/sessionLocalEditorSourceMutation.ts'
  );

  const createPendingEdits = (machine, start, count) =>
    Array.from({ length: count }, (_, offset) => ({
      association: null,
      domain: 'workflow.pokemon',
      field: `compatibility:tm:${machine}`,
      newValue: '1',
      recordId: String(start + offset),
      sources: [],
      summary: `Set TM${machine} compatibility.`
    }));
  const firstPendingEdits = createPendingEdits(1, 1, 180);
  const secondPendingEdits = [
    ...firstPendingEdits,
    ...createPendingEdits(2, 181, 180)
  ];
  const createSession = (pendingEdits) => ({
    hasPendingChanges: pendingEdits.length > 0,
    pendingEdits,
    sessionId: 'za-items-tm-stage-session'
  });
  const workflow = {
    diagnostics: [],
    editableFields: [],
    evolutionMethodOptions: [],
    learnsetMoveOptions: [],
    pokemon: [],
    stats: {
      presentPokemonCount: 0,
      sourceFileCount: 0,
      totalEvolutionCount: 0,
      totalLearnsetMoveCount: 0,
      totalPokemonCount: 0
    },
    summary: {
      availability: 'available',
      description: 'Focused Z-A Items TM stage contract.',
      diagnostics: [],
      id: 'pokemon',
      label: 'Pokemon'
    }
  };
  const paths = {
    baseExeFsPath: null,
    baseRomFsPath: null,
    outputRootPath: null,
    saveFilePath: null,
    selectedGame: 'za'
  };
  const transportCalls = [];
  const transport = async (requestJson) => {
    const request = JSON.parse(requestJson);
    const callIndex = transportCalls.length;
    transportCalls.push(request);
    assert.equal(
      request.command,
      'pokemon.fields.update',
      `TM stage ${callIndex + 1} used the wrong bridge command.`
    );
    assert.equal(
      request.payload.updates.length,
      180,
      `TM stage ${callIndex + 1} did not preserve its 180-update batch.`
    );
    if (callIndex === 0) {
      assert.equal(request.payload.session, null, 'TM001 must start from the empty edit session.');
    } else {
      assert.equal(
        request.payload.session.pendingEdits.length,
        180,
        'TM002 must receive TM001\'s complete 180-edit session.'
      );
    }
    const session = createSession(callIndex === 0 ? firstPendingEdits : secondPendingEdits);
    return JSON.stringify({
      payload: { diagnostics: [], session, workflow },
      requestId: request.requestId
    });
  };
  const bridge = createSvBatchFieldProjectBridgeApi(transport);
  const createUpdates = (machine) =>
    Array.from({ length: 180 }, (_, offset) => ({
      field: `compatibility:tm:${machine}`,
      personalId: offset + 1,
      value: '1'
    }));

  const firstResponse = await bridge.updatePokemonFields({
    paths,
    session: null,
    updates: createUpdates(1)
  });
  assert.equal(firstResponse.session.pendingEdits.length, 180);
  const secondResponse = await bridge.updatePokemonFields({
    paths,
    session: firstResponse.session,
    updates: createUpdates(2)
  });
  assert.equal(secondResponse.session.pendingEdits.length, 360);
  assert.deepEqual(
    transportCalls.map((request) => request.command),
    ['pokemon.fields.update', 'pokemon.fields.update'],
    'Two sequential Items TM stages must produce exactly two Pokemon batch invokes and no capture invoke.'
  );

  let activeReservation = null;
  let latestDraft = null;
  let canceledReservations = 0;
  let committedReservations = 0;
  const binding = {
    cancelDraftSourceMutation(reservation) {
      if (activeReservation !== reservation) return false;
      activeReservation = null;
      canceledReservations += 1;
      return true;
    },
    commitDraftSourceMutation(reservation, reduceLatestPayload) {
      if (activeReservation !== reservation) return false;
      latestDraft = reduceLatestPayload(latestDraft);
      activeReservation = null;
      committedReservations += 1;
      return true;
    },
    reserveDraftSourceMutation() {
      if (activeReservation !== null) return null;
      activeReservation = Object.freeze({
        adapterIdentity: 'session-local:items',
        scopeBaseIdentity: 'za-items-tm-stage'
      });
      return activeReservation;
    }
  };

  latestDraft = Object.fromEntries(
    firstPendingEdits.map((edit) => [edit.recordId, edit.newValue])
  );
  const firstSourceMutation = await runSessionLocalEditorSourceMutation({
    binding,
    didMutate: Boolean,
    mutation: async () => true,
    reduceLatestPayload: () => ({})
  });
  assert.equal(firstSourceMutation.kind, 'source-mutated');
  assert.equal(Object.keys(latestDraft).length, 0);
  assert.equal(activeReservation, null);

  latestDraft = Object.fromEntries(
    secondPendingEdits.slice(180).map((edit) => [edit.recordId, edit.newValue])
  );
  const rejectedDraftSnapshot = structuredClone(latestDraft);
  const secondSourceMutation = await runSessionLocalEditorSourceMutation({
    binding,
    didMutate: Boolean,
    mutation: async () => false,
    reduceLatestPayload: () => {
      throw new Error('A rejected stage must never reduce its retained local draft.');
    }
  });
  assert.equal(secondSourceMutation.kind, 'not-mutated');
  assert.deepEqual(
    latestDraft,
    rejectedDraftSnapshot,
    'A rejected TM002 stage must retain all 180 submitted local draft values.'
  );
  assert.equal(activeReservation, null, 'A rejected TM002 stage must release its reservation.');
  assert.equal(canceledReservations, 1);
  assert.equal(committedReservations, 1);
  const retryReservation = binding.reserveDraftSourceMutation();
  assert.notEqual(retryReservation, null, 'TM002 must be immediately reservable for retry.');
  assert.equal(binding.cancelDraftSourceMutation(retryReservation), true);
} finally {
  await vite.close();
}

console.log('Z-A Items sequential TM stage contract passed.');
