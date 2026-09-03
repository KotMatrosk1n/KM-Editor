// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import {
  clearSubmittedKeyedEditorDraft,
  pruneSparseFieldDraftRecord,
  reconcileKeyedSourceBackedEditorDrafts,
  reconcileEligibleDraftSelection,
  reconcileSourceBackedDraft,
  resolveSubmittedEditorDraft,
  resolveSubmittedKeyedEditorDraft,
  setSparseFieldDraftRecord,
  setSparseFieldDraftValue
} from '../src/components/localEditorDraftState.ts';
import {
  clearStagedFashionCatalogDraftValue,
  createFashionCatalogDraftKey,
  setFashionCatalogDraftValue
} from '../src/features/fashion-catalog/fashionCatalogDraftState.ts';
import {
  canCommitHabitatCoordinatesLoad,
  createHabitatCoordinatesQueryKey,
  habitatCoordinateStageResponseMatchesRequest,
  reconcileHabitatSearchDraftAfterAcceptedQuery
} from '../src/features/habitat-coordinates/habitatCoordinateDraftState.ts';
import {
  clearStagedTrainerIdentityDraftValue,
  setTrainerIdentityDraftValue
} from '../src/features/trainers/trainerIdentityDraftState.ts';

const desktopRoot = fileURLToPath(new URL('../', import.meta.url));

function read(relativePath) {
  return readFileSync(new URL(relativePath, `file:///${desktopRoot.replaceAll('\\', '/')}/`), 'utf8')
    .replace(/\r\n?/gu, '\n');
}

assert.equal(
  reconcileSourceBackedDraft('source', 'source', 'refreshed', Object.is),
  'refreshed',
  'A clean editor draft must follow refreshed source data.'
);
assert.equal(
  reconcileSourceBackedDraft('', 'source', 'refreshed', Object.is),
  '',
  'A blank in-progress editor draft must survive a source refresh.'
);
assert.equal(
  reconcileSourceBackedDraft('typed', 'source', 'refreshed', Object.is),
  'typed',
  'A partially typed editor draft must survive a source refresh.'
);
assert.equal(
  resolveSubmittedEditorDraft('newer typing', 'submitted', ''),
  'newer typing',
  'Async completion must preserve text entered after the submitted snapshot.'
);
assert.equal(
  resolveSubmittedEditorDraft('submitted', 'submitted', ''),
  '',
  'Async completion may clear the exact submitted snapshot after success.'
);

const recordEquality = (left, right) => left.value === right.value;
assert.deepEqual(
  reconcileKeyedSourceBackedEditorDrafts(
    {
      first: { value: 'newer first-record edit' },
      second: { value: 'second-record edit' }
    },
    {
      first: { value: 'first source' },
      second: { value: 'second source' }
    },
    {
      first: { value: 'refreshed first source' },
      second: { value: 'refreshed second source' }
    },
    recordEquality
  ),
  {
    first: { value: 'newer first-record edit' },
    second: { value: 'second-record edit' }
  },
  'A source refresh after switching records must preserve independent local edits for both records.'
);

const submittedKeyedDraft = { value: 'submitted' };
const keyedDraftsAfterRecordSwitch = {
  first: { value: 'newer after submit' },
  second: { value: 'typed after switching records' }
};
assert.strictEqual(
  clearSubmittedKeyedEditorDraft(
    keyedDraftsAfterRecordSwitch,
    'first',
    submittedKeyedDraft,
    recordEquality
  ),
  keyedDraftsAfterRecordSwitch,
  'A delayed keyed completion must not erase a newer edit or the record selected afterward.'
);
assert.deepEqual(
  resolveSubmittedKeyedEditorDraft(
    { first: submittedKeyedDraft, second: keyedDraftsAfterRecordSwitch.second },
    'first',
    submittedKeyedDraft,
    undefined,
    recordEquality
  ),
  { second: keyedDraftsAfterRecordSwitch.second },
  'An exact keyed completion may clear only the submitted record while preserving the switched record.'
);

const fashionFirstKey = createFashionCatalogDraftKey('catalog.bin', 'row-1', 'price');
const fashionSecondKey = createFashionCatalogDraftKey('catalog.bin', 'row-2', 'price');
let fashionDrafts = setFashionCatalogDraftValue({}, fashionFirstKey, '', '100');
const submittedFashionValue = fashionDrafts[fashionFirstKey];
fashionDrafts = setFashionCatalogDraftValue(
  fashionDrafts,
  fashionSecondKey,
  '250',
  '200'
);
fashionDrafts = setFashionCatalogDraftValue(
  fashionDrafts,
  fashionFirstKey,
  '150',
  '100'
);
assert.deepEqual(
  clearStagedFashionCatalogDraftValue(
    fashionDrafts,
    fashionFirstKey,
    submittedFashionValue
  ),
  fashionDrafts,
  'A delayed keyed field stage must preserve a newer value and the draft created after switching rows.'
);

let trainerIdentityDrafts = setTrainerIdentityDraftValue(
  {},
  'trainer-1',
  'Ace Trainer',
  'Youngster'
);
const submittedTrainerIdentity = trainerIdentityDrafts['trainer-1'];
trainerIdentityDrafts = setTrainerIdentityDraftValue(
  trainerIdentityDrafts,
  'trainer-2',
  'Veteran',
  'Backpacker'
);
trainerIdentityDrafts = setTrainerIdentityDraftValue(
  trainerIdentityDrafts,
  'trainer-1',
  'Ranger',
  'Youngster'
);
assert.deepEqual(
  clearStagedTrainerIdentityDraftValue(
    trainerIdentityDrafts,
    'trainer-1',
    submittedTrainerIdentity
  ),
  trainerIdentityDrafts,
  'A delayed trainer identity stage must not erase either the newer identity or another trainer draft.'
);

assert.deepEqual(
  [...reconcileEligibleDraftSelection(
    new Set(['kept']),
    new Set(['kept', 'deselected', 'removed']),
    new Set(['kept', 'deselected', 'added'])
  )].sort(),
  ['added', 'kept'],
  'Eligibility refreshes must preserve deliberate deselections, remove unavailable IDs, and select only newly eligible IDs.'
);

const compatibilityDefaults = Object.fromEntries(
  Array.from({ length: 200 }, (_, index) => [`tm:${index}`, '0'])
);
let compatibilityDrafts = {};
for (let index = 0; index < 150; index += 1) {
  compatibilityDrafts = setSparseFieldDraftValue(
    compatibilityDrafts,
    25,
    `tm:${index}`,
    '1',
    compatibilityDefaults
  );
}
assert.equal(
  Object.keys(compatibilityDrafts['25']).length,
  150,
  'A same-turn Pokemon compatibility burst must accumulate every sparse field update.'
);
assert.strictEqual(
  pruneSparseFieldDraftRecord(
    compatibilityDrafts,
    25,
    compatibilityDefaults,
    new Set(Object.keys(compatibilityDefaults))
  ),
  compatibilityDrafts,
  'Pruning an already-canonical sparse record must preserve object identity and stop effect loops.'
);
assert.strictEqual(
  setSparseFieldDraftRecord(
    compatibilityDrafts,
    25,
    { ...compatibilityDefaults, ...compatibilityDrafts['25'] },
    compatibilityDefaults
  ),
  compatibilityDrafts,
  'Writing an unchanged sparse record must preserve object identity.'
);
assert.deepEqual(
  setSparseFieldDraftValue({}, 'cleared-field', 'price', '', { price: '100' }),
  { 'cleared-field': { price: '' } },
  'An explicit empty draft must not compare equal to a missing sparse key.'
);

for (const relativePath of [
  'src/features/ange-fight/AngeFightSection.tsx',
  'src/features/battle-cafe-rewards/BattleCafeRewardsSection.tsx',
  'src/features/dex-layout/ZaDexLayoutSection.tsx',
  'src/features/fairy-gym-boosts/FairyGymBoostsSection.tsx',
  'src/features/game-dump/GameDumpSection.tsx',
  'src/features/guided-design/GuidedDesignSection.tsx',
  'src/features/npc-item-gift/NpcItemGiftSection.tsx',
  'src/features/output-safety/projectRelocationDraftState.ts',
  'src/features/shiny-rate/ShinyRateSection.tsx',
  'src/features/type-chart/TypeChartSection.tsx'
]) {
  assert.match(
    read(relativePath),
    /\breconcileSourceBackedDraft\b/u,
    `${relativePath} must preserve an active local draft when its source refreshes.`
  );
}

const dexLayoutSource = read('src/features/dex-layout/ZaDexLayoutSection.tsx');
assert.match(
  dexLayoutSource,
  /const previousSource = previousResizeSourceRef\.current;[\s\S]*?previousSource === null[\s\S]*?\? nextSource[\s\S]*?: reconcileSourceBackedDraft\(\s*resizeDraftRef\.current,\s*previousSource,\s*nextSource,\s*areDexSizeDraftsEqual\s*\)/u,
  'Dex Layout must initialize its resize draft from the first loaded source before preserving later user input.'
);

for (const relativePath of [
  'src/features/shiny-rate/ShinyRateSection.tsx',
  'src/features/battle-cafe-rewards/BattleCafeRewardsSection.tsx'
]) {
  const source = read(relativePath);
  assert.match(
    source,
    /const submittedDraftSignature = draftContextSignature;[\s\S]*?draftContextIsActiveRef\.current &&[\s\S]*?draftContextSignatureRef\.current === submittedDraftSignature/u,
    `${relativePath} must pass an exact, still-mounted submitted-draft guard to App staging.`
  );
}

const appSource = read('src/App.tsx');
assert.equal(
  [...appSource.matchAll(
    /setPersonalDraftsByPokemonId\(\(currentDrafts\) =>\s*setSparseFieldDraftValue\(\s*currentDrafts,/gu
  )].length,
  2,
  'Pokemon personal fields and compatibility toggles must both accumulate from the functional state snapshot.'
);
assert.doesNotMatch(
  appSource,
  /setPersonalDraftsByPokemonId\(\(currentDrafts\) =>\s*setSparseFieldDraftRecord\(\s*currentDrafts,[\s\S]{0,400}?\.\.\.personalDrafts/gu,
  'Pokemon field handlers must not rebuild a sparse record from render-captured drafts.'
);
const writablePokemonFieldSource =
  /const writablePersonalFields = useMemo\([\s\S]*?\n  \);/u.exec(appSource)?.[0] ?? '';
assert.match(
  writablePokemonFieldSource,
  /\.\.\.editableFields/u,
  'Pokemon writable-field membership must come from source metadata, not draft-contextualized field objects.'
);
assert.doesNotMatch(
  writablePokemonFieldSource,
  /contextualPersonalFields/u,
  'Compatibility-only edits must not recreate writable-field membership and retrigger draft pruning.'
);
assert.match(
  appSource,
  /const evolutionDraftsBySlot = pokemon[\s\S]*?emptyPokemonEvolutionDraftsBySlot;[\s\S]*?const learnsetDraftsBySlot = pokemon[\s\S]*?emptyPokemonLearnsetDraftsBySlot;/u,
  'Empty Pokemon evolution and learnset slot maps must retain stable identities across compatibility renders.'
);
assert.match(
  appSource,
  /const newLearnsetCreationDraftSignatureRef = useRef\([\s\S]*?const submittedDraftSignature =\s*newLearnsetCreationDraftSignature;[\s\S]*?didAdd &&[\s\S]*?newLearnsetCreationDraftSignatureRef\.current ===[\s\S]*?submittedDraftSignature[\s\S]*?setNewLearnsetMoveIdDraft\(''\);[\s\S]*?setNewLearnsetLevelDraft\(''\);/u,
  'Add Learnset must clear its whole creation composite only when the latest full raw snapshot is still submitted.'
);
assert.doesNotMatch(
  appSource,
  /setNewLearnset(?:MoveId|Level)Draft\(\(currentDraft\) =>/u,
  'Add Learnset must not independently clear fields from a partially stale creation composite.'
);
assert.match(
  appSource,
  /const newEvolutionCreationDraftSignatureRef = useRef\([\s\S]*?const submittedDraftSignature =\s*newEvolutionCreationDraftSignature;[\s\S]*?didAdd &&[\s\S]*?newEvolutionCreationDraftSignatureRef\.current ===[\s\S]*?submittedDraftSignature[\s\S]*?setNewEvolutionMethodDraft\(''\);[\s\S]*?setNewEvolutionArgumentDraft\('0'\);[\s\S]*?setNewEvolutionSpeciesDraft\(''\);[\s\S]*?setNewEvolutionFormDraft\('0'\);[\s\S]*?setNewEvolutionLevelDraft\(''\);/u,
  'Add Evolution must clear its whole creation composite only when the latest full raw snapshot is still submitted.'
);
assert.doesNotMatch(
  appSource,
  /setNewEvolution(?:Method|Argument|Species|Form|Level)Draft\(\(currentDraft\) =>/u,
  'Add Evolution must not independently clear fields from a partially stale creation composite.'
);

const zaPokemonDexPlacementEditorSource =
  /function ZaPokemonDexPlacementEditor\([\s\S]*?(?=\nfunction formatPokemonDexNumber)/u
    .exec(appSource)?.[0] ?? '';
assert.match(
  zaPokemonDexPlacementEditorSource,
  /const submittedDraft: ZaPokemonDexSwapDraftSnapshot = \{\s*destinationDexKind,\s*personalId: pokemon\.personalId,\s*target: targetSpeciesIdDraft\s*\}/u,
  'Z-A Dex swap must snapshot Pokemon identity, destination Dex, and raw target together.'
);
assert.match(
  zaPokemonDexPlacementEditorSource,
  /areZaPokemonDexSwapDraftSnapshotsEqual\(\s*latestSwapDraftRef\.current,\s*submittedDraft\s*\)[\s\S]*?onStageSwap\([\s\S]*?isSubmittedDraftCurrent[\s\S]*?didStage && isSubmittedDraftCurrent\(\)[\s\S]*?onDraftChange\(null\)/u,
  'Z-A Dex swap must forward and reuse one exact composite-current predicate for diagnostics and clearing.'
);

const dexSwapHandlerSource =
  /const handleSwapPokemonDexPlacement = async \([\s\S]*?(?=\n  const handleMovePokemonDexPlacement)/u
    .exec(appSource)?.[0] ?? '';
assert.match(
  dexSwapHandlerSource,
  /if \(updateResponse\.didSucceed && updateResponse\.workflow\) \{\s*setPokemonWorkflow\(updateResponse\.workflow\);\s*\}\s*if \(isSubmittedDraftCurrent\(\)\) \{\s*setEditValidationDiagnostics/u,
  'A stale Z-A Dex swap must still commit its successful workflow while suppressing stale response diagnostics.'
);
assert.match(
  dexSwapHandlerSource,
  /catch \(error\) \{\s*if \(isSubmittedDraftCurrent\(\)\) \{\s*setBridgeDiagnostics/u,
  'A stale Z-A Dex swap exception must not publish into the newer draft context.'
);

for (const [handlerName, section] of [
  ['handleStageShinyRate', 'shinyRate'],
  ['handleStageBattleCafeRewardRows', 'battleCafeRewards']
]) {
  const handlerSource = new RegExp(
    `const ${handlerName} = async \\([\\s\\S]*?(?=\\n  const handleOpen)`,
    'u'
  ).exec(appSource)?.[0] ?? '';
  assert.ok(handlerSource, `App is missing ${handlerName}.`);
  assert.match(
    handlerSource,
    /if \(isSubmittedDraftCurrent\(\)\) \{\s*setBridgeDiagnostics/u,
    `${handlerName} must suppress stale response and catch diagnostics.`
  );
  assert.match(
    handlerSource,
    new RegExp(
      `if \\(isSubmittedDraftCurrent\\(\\)\\) \\{[\\s\\S]*?setScopedEditorPanelDiagnostics\\([\\s\\S]*?'${section}'`,
      'u'
    ),
    `${handlerName} must publish scoped diagnostics only for the submitted child draft.`
  );
}

const trainerIdentitySource = read(
  'src/features/trainers/ZaTrainerIdentityActions.tsx'
);
assert.match(
  trainerIdentitySource,
  /trainerTextTargetContextRef\.current\.trainerId === requestedTrainerId[\s\S]*?nameTargetSignature ===[\s\S]*?requestedTargetSignature/u,
  'Trainer name navigation failure feedback must be scoped to the requested trainer and text target.'
);
assert.match(
  trainerIdentitySource,
  /trainerTextTargetContextRef\.current\.trainerId === requestedTrainerId[\s\S]*?classTargetSignature ===[\s\S]*?requestedTargetSignature/u,
  'Trainer class navigation failure feedback must be scoped to the requested trainer and text target.'
);

const semanticMerge = read('src/features/semantic-merge/SemanticMergeSection.tsx');
for (const functionName of ['discover', 'generate']) {
  const body = new RegExp(
    `const ${functionName} = async [\\s\\S]*?(?=\\n  const [A-Za-z])`,
    'u'
  ).exec(semanticMerge)?.[0] ?? '';
  assert.ok(body, `Semantic Merge is missing ${functionName}.`);
  assert.doesNotMatch(
    body,
    /setResolutionDraft\(new Map\(\)\)/u,
    `Semantic Merge ${functionName} must not erase conflict choices before an awaited preview succeeds.`
  );
}
assert.match(
  semanticMerge,
  /setResolutionDraft\(\(current\) => reconcileResolutionDraft\(/u,
  'Semantic Merge must reconcile refreshed conflict choices without overwriting a newer local choice.'
);
assert.doesNotMatch(
  semanticMerge,
  /<input\s+disabled=\{isBlocked \|\| controller\.mergePreview\.status === 'loading'\}\s+id="semantic-merge-target-search"/u,
  'Semantic Merge search input must remain editable while an earlier preview request is loading.'
);

const trainerPools = read('src/features/trainer-pools/TrainerPoolsSection.tsx');
assert.match(
  trainerPools,
  /!sameSelection\(selectionRef\.current\.source, requestedSource\)/u,
  'Trainer Pool staging must reject stale completion feedback after the selected source changes.'
);
assert.doesNotMatch(
  trainerPools,
  /disabled=\{hasPendingSwap\}/u,
  'Trainer Pool selectors must remain editable when an older staged swap is pending.'
);
assert.match(
  trainerPools,
  /!hasPendingSwap\s*&&\s*!isStaging/u,
  'Trainer Pool staging must block a conflicting staged swap without locking newer selector input.'
);
assert.doesNotMatch(
  trainerPools,
  /<PoolSelection[\s\S]{0,250}?disabled=\{isStaging/u,
  'Trainer Pool selectors must remain editable while an async stage request is in flight.'
);

const changeSetWorkspace = read('src/features/change-sets/ChangeSetWorkspacePanel.tsx');
assert.doesNotMatch(
  changeSetWorkspace,
  /<select[\s\S]{0,180}?disabled=\{isBusy[^>]*?id="change-set-variant-output-(?:mode|profile)"/u,
  'Build Variant draft selections must remain editable while an earlier variant snapshot is created.'
);

const outputSafety = read('src/features/output-safety/OutputSafetyPanel.tsx');
assert.match(
  outputSafety,
  /const submittedLabel = checkpointLabel;[\s\S]*?const created = await controller\.createCheckpoint\(submittedLabel\);[\s\S]*?current === submittedLabel \? '' : current/u,
  'Checkpoint creation must clear only the exact label that was saved successfully.'
);

const gameDump = read('src/features/game-dump/GameDumpSection.tsx');
assert.match(
  gameDump,
  /const invalidateGeneratedState = useCallback\(\(\) => \{\s*generationRunRef\.current \+= 1;/u,
  'Editing Game Dump controls must invalidate an older generation completion before clearing its output.'
);
assert.match(
  gameDump,
  /await onRememberDestination\(game, destination\);[\s\S]*?setDestinationPersistenceDiagnostics/u,
  'Game Dump must retain destination persistence failures in its shared diagnostics surface.'
);
assert.match(
  gameDump,
  /rememberDestinationRunRef\.current === runId[\s\S]*?destinationFolderRef\.current === destination/u,
  'A delayed Game Dump destination persistence result must match its exact current scope and value.'
);
assert.match(
  gameDump,
  /const preserveExistingSelection = workflowScopeKeyRef\.current === requestedScopeKey/u,
  'Game Dump may preserve category choices only when refreshed data belongs to the same project scope.'
);
const gameDumpLoadWorkflow = gameDump.slice(
  gameDump.indexOf('const loadWorkflow = useCallback'),
  gameDump.indexOf('const handleBrowseDestination')
);
assert.equal(
  [...gameDumpLoadWorkflow.matchAll(
    /loadWorkflowRunRef\.current !== runId \|\|\s*activeScopeKeyRef\.current !== requestedScopeKey \|\|\s*activeWorkflowEligibilityRef\.current !== requestedEligibility/gu
  )].length,
  2,
  'Game Dump load success and failure publication must both reject a response after the rendered project scope or workflow eligibility changes.'
);
assert.match(
  gameDumpLoadWorkflow,
  /loadWorkflowRunRef\.current === runId &&\s*activeScopeKeyRef\.current === requestedScopeKey &&\s*activeWorkflowEligibilityRef\.current === requestedEligibility\s*\) \{\s*setIsLoading\(false\);/u,
  'Game Dump loading cleanup must not publish from an obsolete rendered project scope or workflow eligibility.'
);
assert.match(
  gameDump,
  /activeWorkflowEligibilityRef\.current =\s*health\?\.canOpenReadOnlyWorkflows === true && paths\.selectedGame !== null;/u,
  'Game Dump must update workflow eligibility during render so an old response cannot publish before the invalidating effect runs.'
);
assert.match(
  gameDump,
  /const failureDiagnostics = toProjectBridgeDiagnostics\([\s\S]*?'Game Dump could not be loaded\.'[\s\S]*?if \(!preserveExistingSelection\) \{\s*setWorkflowCategories\(\[\]\);\s*setWorkflowDiagnostics\(\[\]\);\s*setSelectionState\(\{\}\);\s*workflowScopeKeyRef\.current = null;\s*\}/u,
  'A same-scope Game Dump refresh failure must preserve the last good workflow and any selection edits made while loading.'
);
assert.match(
  gameDump,
  /activeScopeKeyRef\.current !== requestedScopeKey/u,
  'Game Dump must ignore generation results that finish after the active project scope changes.'
);
assert.match(
  gameDump,
  /selectedFolder &&\s*destinationPickerOperationRef\.current === pickerOperation &&[\s\S]*?destinationFolderRef\.current === requestedDestination/u,
  'A delayed Game Dump folder picker must not replace a destination typed after it opened.'
);

const fashionCatalog = read('src/features/fashion-catalog/FashionCatalogSection.tsx');
assert.match(
  fashionCatalog,
  /draftContextRef\.current\.key === stagedDraftKey &&\s*draftContextRef\.current\.value === stagedDraftValue[\s\S]*?setFeedback/u,
  'Fashion Catalog may publish delayed stage feedback only for the exact submitted field and value.'
);

const habitatCoordinates = read(
  'src/features/habitat-coordinates/HabitatCoordinatesSection.tsx'
);
assert.match(
  habitatCoordinates,
  /const isSubmittedDraftCurrent = \(\) =>\s*coordinateDraftContextRef\.current\.key === stagedDraftKey &&\s*coordinateDraftContextRef\.current\.value === stagedDraftValue;[\s\S]*?onStageCoordinate\([\s\S]*?isSubmittedDraftCurrent[\s\S]*?if \(isSubmittedDraftCurrent\(\)\) \{\s*setFeedback/u,
  'Habitat Coordinates must guard both App diagnostics and child feedback with the exact submitted record and value.'
);
assert.match(
  habitatCoordinates,
  /const submittedSearch = \{\s*draft: searchDraftRef\.current,\s*source: searchSourceRef\.current\s*\};[\s\S]*?const didLoad = await onLoadQuery\(query\);[\s\S]*?const nextDraft = reconcileHabitatSearchDraftAfterAcceptedQuery\(\s*searchDraftRef\.current,\s*submittedSearch,\s*query\.search\s*\);\s*searchDraftRef\.current = nextDraft;\s*setSearchDraft\(nextDraft\)/u,
  'An accepted Habitat query must reconcile search text against its submitted raw/source snapshot.'
);
assert.match(
  habitatCoordinates,
  /const previousSource = searchSourceRef\.current;[\s\S]*?const nextDraft = reconcileSourceBackedDraft\(\s*searchDraftRef\.current,\s*previousSource,\s*page\.search[\s\S]*?searchDraftRef\.current = nextDraft;\s*setSearchDraft\(nextDraft\)/u,
  'Authoritative Habitat page updates must preserve a locally dirty search draft.'
);

assert.equal(
  reconcileHabitatSearchDraftAfterAcceptedQuery(
    'submitted raw',
    { draft: 'submitted raw', source: 'previous source' },
    'accepted search'
  ),
  'accepted search',
  'An unchanged submitted Habitat search draft must follow the accepted authoritative query.'
);
assert.equal(
  reconcileHabitatSearchDraftAfterAcceptedQuery(
    'newer typing',
    { draft: 'submitted raw', source: 'previous source' },
    'accepted search'
  ),
  'newer typing',
  'Typing performed after Habitat query submission must survive the accepted response.'
);

const habitatQuery = { limit: 50, offset: 0, region: 'paldea', search: 'Pikachu' };
assert.equal(
  createHabitatCoordinatesQueryKey(habitatQuery),
  createHabitatCoordinatesQueryKey({ ...habitatQuery }),
  'Habitat query identity must be stable for the same view.'
);
assert.notEqual(
  createHabitatCoordinatesQueryKey(habitatQuery),
  createHabitatCoordinatesQueryKey({ ...habitatQuery, offset: 50 }),
  'Habitat query identity must distinguish pagination changes.'
);
const currentHabitatLoadGuard = {
  currentSessionSignature: 'session-after-stage',
  currentStageGeneration: 4,
  currentViewGeneration: 7,
  requestedSessionSignature: 'session-after-stage',
  requestedStageGeneration: 4,
  requestedViewGeneration: 7
};
assert.equal(
  canCommitHabitatCoordinatesLoad(currentHabitatLoadGuard),
  true,
  'The exact current Habitat query/session snapshot may commit.'
);
for (const staleSnapshot of [
  { currentViewGeneration: 8 },
  { currentStageGeneration: 5 },
  { currentSessionSignature: 'newer-session' }
]) {
  assert.equal(
    canCommitHabitatCoordinatesLoad({
      ...currentHabitatLoadGuard,
      ...staleSnapshot
    }),
    false,
    'A superseded Habitat query, stage, or session snapshot must not commit.'
  );
}

const habitatBinding = {
  currentX: 100,
  currentY: 200,
  devNo: 25,
  formNo: 0,
  outerGroupOccurrence: 3,
  rowOccurrence: 7,
  rowPreimageSha256: 'a'.repeat(64),
  sourceFile: 'world/data/encount/habitat.bin',
  sourceRevision: 'b'.repeat(64),
  versionA: true,
  versionB: false
};
const habitatStageRequest = {
  binding: habitatBinding,
  coordinate: { x: 300, y: 400 },
  query: habitatQuery,
  region: 'paldea'
};
const habitatStageWorkflow = {
  page: {
    ...habitatQuery,
    records: [
      {
        binding: habitatBinding,
        isStaged: true,
        stagedCoordinate: habitatStageRequest.coordinate
      }
    ]
  }
};
const habitatPendingEdit = {
  domain: 'workflow.habitatCoordinates',
  field: 'coordinate',
  newValue: '300,400',
  recordId: [
    'v1',
    'paldea',
    habitatBinding.outerGroupOccurrence,
    habitatBinding.rowOccurrence,
    habitatBinding.devNo,
    habitatBinding.formNo,
    1,
    0,
    habitatBinding.currentX,
    habitatBinding.currentY,
    habitatBinding.rowPreimageSha256,
    habitatBinding.sourceRevision
  ].join(':')
};
assert.equal(
  habitatCoordinateStageResponseMatchesRequest(
    habitatStageRequest,
    habitatStageWorkflow,
    { pendingEdits: [habitatPendingEdit] }
  ),
  true,
  'Habitat stage evidence must accept the exact query, binding, coordinate, and pending edit.'
);
assert.equal(
  habitatCoordinateStageResponseMatchesRequest(
    habitatStageRequest,
    habitatStageWorkflow,
    {
      pendingEdits: [
        {
          ...habitatPendingEdit,
          recordId: habitatPendingEdit.recordId.replace(':3:7:', ':3:8:')
        }
      ]
    }
  ),
  false,
  'An unrelated existing Habitat pending edit must not masquerade as the requested stage.'
);
assert.equal(
  habitatCoordinateStageResponseMatchesRequest(
    habitatStageRequest,
    {
      page: {
        ...habitatStageWorkflow.page,
        search: 'different query'
      }
    },
    { pendingEdits: [habitatPendingEdit] }
  ),
  false,
  'Habitat stage evidence must reject a workflow for a different exact query.'
);
assert.equal(
  habitatCoordinateStageResponseMatchesRequest(
    habitatStageRequest,
    {
      page: {
        ...habitatStageWorkflow.page,
        records: [
          {
            ...habitatStageWorkflow.page.records[0],
            stagedCoordinate: { x: 301, y: 400 }
          }
        ]
      }
    },
    { pendingEdits: [habitatPendingEdit] }
  ),
  false,
  'Habitat stage evidence must reject a different staged coordinate.'
);

const habitatApp = read('src/App.tsx');
assert.match(
  habitatApp,
  /const requestedViewGeneration = \+\+habitatCoordinatesViewGenerationRef\.current;[\s\S]*?const requestedStageGeneration = habitatCoordinatesStageGenerationRef\.current;[\s\S]*?const requestedSessionSignature = getEditSessionSignature\(requestedSession\);[\s\S]*?canCommitHabitatCoordinatesLoad\(/u,
  'Habitat loads must carry exact view, stage, and session snapshots into their commit guard.'
);
assert.match(
  habitatApp,
  /habitatCoordinatesViewGenerationRef\.current === stagedViewGeneration &&[\s\S]*?createHabitatCoordinatesQueryKey\([\s\S]*?habitatCoordinatesLatestQueryRef\.current[\s\S]*?\) === stagedQueryKey[\s\S]*?setHabitatCoordinatesWorkflow\(stageResponse\.workflow\)/u,
  'A staged Habitat workflow may replace the page only while its original query is still current.'
);
assert.match(
  habitatApp,
  /await handleOpenHabitatCoordinatesWorkflow\(\s*\{ \.\.\.habitatCoordinatesLatestQueryRef\.current \},\s*editSessionRef\.current\s*\);/u,
  'A successful Habitat stage must reload the latest query with the authoritative committed session.'
);
assert.match(
  habitatApp,
  /const matchesRequestedStageEvidence =\s*habitatCoordinateStageResponseMatchesRequest\(\s*input,\s*stageResponse\.workflow,\s*stageResponse\.session\s*\);[\s\S]*?matchesRequestedStageEvidence &&[\s\S]*?!diagnostics\.some/u,
  'Habitat stage success must require exact query, requested binding, coordinate, and pending-edit evidence.'
);
assert.match(
  habitatApp,
  /const canPublishStageDiagnostics = \(\) =>\s*isCurrentStageView\(\) && isSubmittedDraftCurrent\(\);[\s\S]*?if \(response && canPublishStageDiagnostics\(\)\) \{\s*setScopedEditorPanelDiagnostics\([\s\S]*?response\.diagnostics[\s\S]*?catch \(error\) \{\s*if \(canPublishStageDiagnostics\(\)\) \{\s*setScopedEditorPanelDiagnostics\([\s\S]*?toBridgeDiagnostics\(error\)/u,
  'Habitat response diagnostics and caught bridge errors must publish only to the current scoped editor target.'
);

const workbench = read('src/features/workbench/WorkbenchSection.tsx');
assert.doesNotMatch(
  workbench,
  /<textarea\s+disabled=\{note\.isBusy\}/u,
  'Workbench notes must remain editable while a prior note snapshot is being saved.'
);

const shortcutOverlay = read('src/features/workbench/ShortcutOverlay.tsx');
assert.match(
  shortcutOverlay,
  /draftChordRef\.current === submittedDraftChord/u,
  'Shortcut persistence may close an editor only when its exact submitted draft is still current.'
);
assert.doesNotMatch(
  shortcutOverlay,
  /<input\s+autoFocus\s+disabled=\{isMutationBusy\}/u,
  'Shortcut chord input must remain editable while its earlier value is being saved.'
);

const guidedDesign = read('src/features/guided-design/GuidedDesignSection.tsx');
assert.doesNotMatch(
  guidedDesign,
  /disabled=\{importState\.status === 'busy' \|\| importState\.status === 'success'\}/u,
  'Guided Design change-set name must remain editable while its earlier value imports.'
);
assert.doesNotMatch(
  guidedDesign,
  /<fieldset disabled=\{controller\.isQuerying\}>/u,
  'Guided Design raw proposal inputs must remain editable while an earlier preview is loading.'
);
assert.match(
  guidedDesign,
  /setSelectedRecords\(\(current\) => reconcileSourceBackedDraft\(/u,
  'Guided Design exact-target refreshes must preserve a newer local target selection.'
);

const ordinaryEditors = read('src/App.tsx');
const sessionLocalSourceMutation = read(
  'src/components/sessionLocalEditorSourceMutation.ts'
);
assert.equal(
  [...ordinaryEditors.matchAll(/useSessionLocalEditorDraftBinding\(/gu)].length,
  5,
  'Items, Pokemon, Moves, Text, and Trainers must each keep one session-local Stage binding.'
);
assert.equal(
  [...ordinaryEditors.matchAll(/useOrdinaryEditorDraft\(/gu)].length,
  0,
  'Ordinary editor input must never enter the durable autosave hook.'
);
assert.doesNotMatch(
  ordinaryEditors,
  /\bdraft\.update\s*\(|\bOrdinaryEditorDraftStatus\b|<OrdinaryEditorDraftProvider\b/u,
  'Ordinary editors must not persist per keystroke or expose storage-lifecycle UI.'
);
for (const section of ['items', 'pokemon', 'moves', 'text', 'trainers']) {
  assert.equal(
    [...ordinaryEditors.matchAll(
      new RegExp(`useRegisterEditorDraftDirty\\(\\s*['"]${section}['"]`, 'gu')
    )].length,
    1,
    `${section} must register its session-local dirty state exactly once.`
  );
}
assert.match(
  sessionLocalSourceMutation,
  /function runSessionLocalEditorSourceMutation<[\s\S]*?reserveDraftSourceMutation\(\)[\s\S]*?await options\.mutation\(\)[\s\S]*?commitDraftSourceMutation\(/u,
  'Ordinary editor source writes must pass through the session-local Stage reservation helper.'
);
assert.match(
  ordinaryEditors,
  /latestPayloadRef = useRef\(payload\);\s*latestPayloadRef\.current = payload;[\s\S]*?const latestPayload = latestPayloadRef\.current;[\s\S]*?nextPayload = reduceLatestPayload\(latestPayload\);[\s\S]*?currentBindingRef\.current\.applyHydratedPayload\(nextPayload\)/u,
  'Stage completion must reduce the latest in-memory payload instead of replacing typing performed while Stage was running.'
);
const sessionLocalBinding = ordinaryEditors.slice(
  ordinaryEditors.indexOf('function useSessionLocalEditorDraftBinding<TDraft>'),
  ordinaryEditors.indexOf('type FashionCatalogPendingPayload')
);
assert.match(
  sessionLocalBinding,
  /currentBindingRef\.current = \{\s*applyHydratedPayload,\s*sourceTransitionAdapterIdentity,\s*scopeBaseIdentity\s*\};/u,
  'Session-local Stage completion must consult the binding scope from the latest render.'
);
assert.match(
  sessionLocalBinding,
  /const currentBinding = currentBindingRef\.current;[\s\S]*?reservation\.adapterIdentity !== currentBinding\.sourceTransitionAdapterIdentity \|\|\s*reservation\.scopeBaseIdentity !== currentBinding\.scopeBaseIdentity/u,
  'A Stage reservation must be rejected after its selected record or project scope changes.'
);
assert.doesNotMatch(
  sessionLocalBinding,
  /reservation\.scopeBaseIdentity !== scopeBaseIdentity/u,
  'Stage completion must not validate scope against the render-stale closure that created it.'
);
assert.match(
  ordinaryEditors,
  /function removeMatchingFieldDraftValues\([\s\S]*?if \(latestFields\[field\] === submittedValue\) \{\s*delete nextFields\[field\];/u,
  'A successful partial Stage may clear only field values that still equal its submitted snapshot.'
);
assert.doesNotMatch(
  ordinaryEditors,
  /getOrdinaryEditorDraftDiagnostics/u,
  'Removed ordinary draft storage diagnostics must not enter the common bottom panel.'
);
assert.match(
  ordinaryEditors,
  /void projectDraftRegistry\.load\([\s\S]*?advancedAuthoringProjectDraftAdapter[\s\S]*?await projectDraftRegistry\.save\([\s\S]*?advancedAuthoringProjectDraftAdapter/u,
  'Advanced Authoring must retain its explicit saved-project-draft workflow.'
);

const advancedAuthoringHydration = ordinaryEditors.slice(
  ordinaryEditors.indexOf('const [isAdvancedAuthoringDraftPending'),
  ordinaryEditors.indexOf('const saveAdvancedAuthoringDrafts')
);
const advancedAuthoringUnavailableCatch = advancedAuthoringHydration.slice(
  advancedAuthoringHydration.indexOf('const isWaitingForWorkspace'),
  advancedAuthoringHydration.indexOf('(error: unknown) =>')
);
assert.match(
  advancedAuthoringUnavailableCatch,
  /if \(!isWaitingForWorkspace\) \{[\s\S]*?setAdvancedAuthoringUnavailableDraft\(\{\s*draft,\s*protectionScopeKey: protectionScopeKey![\s\S]*?updateAdvancedAuthoringDraftProtection\([\s\S]*?draft\.payload[\s\S]*?setIsAdvancedAuthoringDraftPending\(false\)/u,
  'A decoded Advanced Authoring draft that cannot hydrate must remain captured, countable, and protected instead of becoming invisible.'
);
assert.doesNotMatch(
  advancedAuthoringUnavailableCatch,
  /updateAdvancedAuthoringDraftProtection\([\s\S]*?protectionScopeKey,\s*null/u,
  'Failed Advanced Authoring hydration must not clear protection for the durable unavailable draft.'
);

const advancedAuthoringRecovery = ordinaryEditors.slice(
  ordinaryEditors.indexOf('const discardUnavailableAdvancedAuthoringDraft'),
  ordinaryEditors.indexOf('const saveAdvancedAuthoringDrafts')
);
assert.match(
  advancedAuthoringRecovery,
  /window\.confirm\([\s\S]*?editorDrafts\.confirmDiscardUnavailable\.advancedAuthoring[\s\S]*?projectDraftRegistry\.reconcile\([\s\S]*?advancedAuthoringProjectDraftMatchesCapture\([\s\S]*?\? \{ kind: 'delete', result: true \}[\s\S]*?: \{ kind: 'keep', result: false \}/u,
  'Unavailable Advanced Authoring recovery must confirm and atomically delete only the exact captured persisted entry.'
);
assert.match(
  advancedAuthoringRecovery,
  /advancedAuthoringUnavailableDraftDiscardTokenRef\.current !== null[\s\S]*?advancedAuthoringUnavailableDraftDiscardTokenRef\.current = discardAdmissionToken[\s\S]*?criticalWriteToken = beginCriticalWriteOperation\(\)[\s\S]*?finishCriticalWriteOperation\(criticalWriteToken\)/u,
  'Unavailable Advanced Authoring recovery must synchronously reject double activation and join the global critical-write admission guard.'
);
assert.doesNotMatch(
  advancedAuthoringRecovery,
  /projectDraftRegistry\.delete\(/u,
  'Unavailable Advanced Authoring recovery must never use an unconditional registry delete.'
);
assert.match(
  advancedAuthoringRecovery,
  /if \(!reconciliation\.result\) \{[\s\S]*?setAdvancedAuthoringDraftRecoveryRevision/u,
  'A changed Advanced Authoring draft must be reloaded instead of being deleted or left behind as an invisible no-op.'
);

const advancedAuthoringPanel = read('src/features/change-sets/AdvancedAuthoringPanel.tsx');
assert.match(
  advancedAuthoringPanel,
  /const controlsBusy = operationBusy \|\| unavailableDraft !== null/u,
  'Advanced Authoring controls must remain disabled while an unavailable persisted draft needs recovery.'
);
assert.match(
  advancedAuthoringPanel,
  /editorDrafts\.summary\.unavailable[\s\S]*?changeSets\.authoring\.unavailableDraftHelp[\s\S]*?onClick=\{unavailableDraft\.onDiscard\}/u,
  'Advanced Authoring must visibly count and explain an unavailable draft and expose its explicit recovery action.'
);
assert.match(
  advancedAuthoringPanel,
  /change-set-authoring-summary[\s\S]*?editorDrafts\.summary\.unavailable/u,
  'The Advanced Authoring status summary must keep the unavailable persisted draft count visible.'
);

const advancedAuthoringDraftAdapter = read('src/authoring/advancedAuthoringDraftAdapter.ts');
assert.match(
  advancedAuthoringDraftAdapter,
  /function advancedAuthoringProjectDraftMatchesCapture\([\s\S]*?projectDraftKey\(current\.key\) === projectDraftKey\(captured\.key\)[\s\S]*?current\.updatedAtUtc === captured\.updatedAtUtc[\s\S]*?serializePayload\(current\.payload\)[\s\S]*?serializePayload\(captured\.payload\)/u,
  'Exact unavailable-draft recovery must compare canonical identity, revision metadata, and normalized payload content.'
);

for (const locale of ['de', 'en', 'es', 'fr', 'ru', 'uk', 'zh']) {
  const resources = JSON.parse(read(`src/localization/resources/${locale}.json`));
  assert.match(
    resources.keys['editorDrafts.confirmDiscardUnavailable.advancedAuthoring'],
    /\{fieldCount\}/u,
    `${locale} must localize the exact Advanced Authoring unavailable-draft confirmation.`
  );
  assert.match(
    resources.keys['changeSets.authoring.unavailableDraftHelp'],
    /\{fieldCount\}/u,
    `${locale} must localize the visible Advanced Authoring unavailable-draft explanation.`
  );
}

console.log('Pokemon compatibility burst state contract passed.');
console.log('Local editor draft contracts passed.');
