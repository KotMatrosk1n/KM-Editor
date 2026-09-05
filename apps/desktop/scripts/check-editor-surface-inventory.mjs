// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';
import {
  areStringSetsEqual,
  clearSubmittedKeyedEditorDraft,
  reconcileEligibleDraftSelection,
  reconcileKeyedSourceBackedEditorDrafts,
  reconcileSourceBackedDraft,
  resolveSubmittedEditorDraft,
  resolveSubmittedKeyedEditorDraft
} from '../src/components/localEditorDraftState.ts';
import {
  clearStagedFashionCatalogDraftValue,
  createFashionCatalogDraftKey,
  setFashionCatalogDraftValue
} from '../src/features/fashion-catalog/fashionCatalogDraftState.ts';
import {
  clearStagedHabitatCoordinateDraftValue,
  createHabitatCoordinateDraftKey,
  setHabitatCoordinateDraftValue
} from '../src/features/habitat-coordinates/habitatCoordinateDraftState.ts';
import {
  clearStagedTrainerIdentityDraftValue,
  setTrainerIdentityDraftValue
} from '../src/features/trainers/trainerIdentityDraftState.ts';
import {
  clearSavedResearchAnnotationEditorDraft,
  discardResearchAnnotationEditorDraft,
  setResearchAnnotationEditorDraft
} from '../src/features/research-lab/researchAnnotationDraftState.ts';
import {
  diagnosticListFingerprint,
  mergeEditorDiagnostics
} from '../src/components/commonEditorDiagnosticsState.ts';

const desktopRoot = fileURLToPath(new URL('../', import.meta.url));
const sourceRoot = join(desktopRoot, 'src');

function read(relativePath) {
  return readFileSync(join(desktopRoot, relativePath), 'utf8').replace(/\r\n?/gu, '\n');
}

function between(source, start, end) {
  const startIndex = source.indexOf(start);
  const endIndex = source.indexOf(end, startIndex + start.length);
  assert.ok(startIndex >= 0, `Missing contract start: ${start}`);
  assert.ok(endIndex > startIndex, `Missing contract end: ${end}`);
  return source.slice(startIndex, endIndex);
}

function sourceFilesUnder(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) return sourceFilesUnder(path);
    return entry.isFile() && path.endsWith('.tsx') ? [path] : [];
  });
}

function findFunctionLike(sourceFile, name) {
  let result = null;
  const visit = (node) => {
    if (
      (ts.isFunctionDeclaration(node) && node.name?.text === name) ||
      (ts.isVariableDeclaration(node) &&
        ts.isIdentifier(node.name) &&
        node.name.text === name &&
        node.initializer &&
        (ts.isArrowFunction(node.initializer) || ts.isFunctionExpression(node.initializer)))
    ) {
      result = ts.isVariableDeclaration(node) ? node.initializer : node;
      return;
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return result;
}

function findVariableDeclaration(sourceFile, name) {
  let result = null;
  const visit = (node) => {
    if (
      ts.isVariableDeclaration(node) &&
      ts.isIdentifier(node.name) &&
      node.name.text === name
    ) {
      result = node;
      return;
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return result;
}

/**
 * Every navigable surface lives here, including read-only and utility surfaces.
 * Adding a section without classifying its interaction policy makes this gate fail.
 *
 * Policies:
 * - ordinary-session-local: ordinary editor input stays in component memory until an explicit Stage action.
 * - raw-local-draft: free-form/number input remains raw text until validation/staging.
 * - choice-local-draft: choices are staged locally; no partial free-form commit exists.
 * - action-only: user actions have no partially valid field draft.
 * - utility-input: configuration/query input, not an editable game record.
 * - read-only: deliberately exposes no mutation control.
 */
const editorSurfaceInventory = {
  health: ['src/App.tsx', 'HealthSection', 'utility-input'],
  workbench: ['src/features/workbench/WorkbenchSection.tsx', 'WorkbenchSection', 'utility-input'],
  workflows: ['src/features/workflows/WorkflowsSection.tsx', 'WorkflowsSection', 'utility-input'],
  items: ['src/App.tsx', 'ItemsSection', 'ordinary-session-local'],
  pokemon: ['src/App.tsx', 'PokemonSection', 'ordinary-session-local'],
  dexLayout: ['src/features/dex-layout/ZaDexLayoutSection.tsx', 'ZaDexLayoutSection', 'raw-local-draft'],
  moves: ['src/App.tsx', 'MovesSection', 'ordinary-session-local'],
  text: ['src/App.tsx', 'TextSection', 'ordinary-session-local'],
  trainers: ['src/App.tsx', 'TrainersSection', 'ordinary-session-local'],
  trainerPools: ['src/features/trainer-pools/TrainerPoolsSection.tsx', 'TrainerPoolsSection', 'choice-local-draft'],
  fashionCatalog: ['src/features/fashion-catalog/FashionCatalogSection.tsx', 'FashionCatalogSection', 'raw-local-draft'],
  giftPokemon: ['src/App.tsx', 'GiftPokemonSection', 'raw-local-draft'],
  tradePokemon: ['src/App.tsx', 'TradePokemonSection', 'raw-local-draft'],
  staticEncounters: ['src/App.tsx', 'StaticEncountersSection', 'raw-local-draft'],
  rentalPokemon: ['src/App.tsx', 'RentalPokemonSection', 'raw-local-draft'],
  dynamaxAdventures: ['src/App.tsx', 'DynamaxAdventuresSection', 'raw-local-draft'],
  shops: ['src/App.tsx', 'ShopsSection', 'raw-local-draft'],
  battleCafeRewards: ['src/features/battle-cafe-rewards/BattleCafeRewardsSection.tsx', 'BattleCafeRewardsSection', 'raw-local-draft'],
  tmMachineControls: ['src/features/tm-machine-controls/TmMachineControlsSection.tsx', 'TmMachineControlsSection', 'choice-local-draft'],
  habitatCoordinates: ['src/features/habitat-coordinates/HabitatCoordinatesSection.tsx', 'HabitatCoordinatesSection', 'choice-local-draft'],
  encounters: ['src/App.tsx', 'EncountersSection', 'raw-local-draft'],
  teraRaids: ['src/App.tsx', 'TeraRaidsSection', 'raw-local-draft'],
  raidBattles: ['src/App.tsx', 'RaidBattlesSection', 'raw-local-draft'],
  raidRewards: ['src/App.tsx', 'RaidRewardsSection', 'raw-local-draft'],
  raidBonusRewards: ['src/App.tsx', 'RaidRewardsSection', 'raw-local-draft'],
  placement: ['src/App.tsx', 'PlacementSection', 'raw-local-draft'],
  behavior: ['src/App.tsx', 'BehaviorSection', 'raw-local-draft'],
  flagworkSave: ['src/App.tsx', 'FlagworkSaveSection', 'read-only'],
  bagHook: ['src/App.tsx', 'BagHookSection', 'action-only'],
  catchCap: ['src/App.tsx', 'CatchCapSection', 'raw-local-draft'],
  hyperTraining: ['src/App.tsx', 'HyperTrainingSection', 'raw-local-draft'],
  shinyRate: ['src/features/shiny-rate/ShinyRateSection.tsx', 'ShinyRateSection', 'raw-local-draft'],
  typeChart: ['src/features/type-chart/TypeChartSection.tsx', 'TypeChartSection', 'choice-local-draft'],
  angeFight: ['src/features/ange-fight/AngeFightSection.tsx', 'AngeFightSection', 'raw-local-draft'],
  fairyGymBoosts: ['src/features/fairy-gym-boosts/FairyGymBoostsSection.tsx', 'FairyGymBoostsSection', 'choice-local-draft'],
  fashionUnlock: ['src/features/fashion-unlock/FashionUnlockSection.tsx', 'FashionUnlockSection', 'action-only'],
  gymUniformRemoval: ['src/features/gym-uniform-removal/GymUniformRemovalSection.tsx', 'GymUniformRemovalSection', 'action-only'],
  hyperspaceBypass: ['src/features/hyperspace-bypass/HyperspaceBypassSection.tsx', 'HyperspaceBypassSection', 'action-only'],
  ivScreen: ['src/App.tsx', 'IvScreenSection', 'action-only'],
  exefsPatches: ['src/App.tsx', 'ExeFsPatchSection', 'choice-local-draft'],
  royalCandy: ['src/App.tsx', 'RoyalCandySection', 'raw-local-draft'],
  startingItems: ['src/App.tsx', 'StartingItemsSection', 'raw-local-draft'],
  npcItemGift: ['src/features/npc-item-gift/NpcItemGiftSection.tsx', 'NpcItemGiftSection', 'raw-local-draft'],
  spreadsheetImport: ['src/App.tsx', 'SpreadsheetImportSection', 'utility-input'],
  modMerger: ['src/App.tsx', 'ModMergerSection', 'utility-input'],
  fpsPatch: ['src/App.tsx', 'FpsPatchSection', 'choice-local-draft'],
  profanityFilter: ['src/App.tsx', 'ProfanityFilterSection', 'action-only'],
  randomizer: ['src/features/randomizer/RandomizerSection.tsx', 'RandomizerSection', 'utility-input'],
  gameDump: ['src/features/game-dump/GameDumpSection.tsx', 'GameDumpSection', 'utility-input'],
  gameplaySettings: ['src/features/gameplay-settings/GameplaySettingsSection.tsx', 'GameplaySettingsSection', 'choice-local-draft'],
  changes: ['src/App.tsx', 'ChangesSection', 'utility-input'],
  history: ['src/features/output-safety/OutputHistoryPage.tsx', 'OutputHistoryPage', 'utility-input'],
  settings: ['src/App.tsx', 'SettingsSection', 'utility-input']
};

/**
 * User-editable/query surfaces embedded inside a top-level workbench section.
 * These are inventoried separately because their native fields are not declared
 * by the route renderer itself. The native-control coverage assertion below
 * makes an unclassified input-bearing TSX file a test failure.
 */
const embeddedEditableSurfaceInventory = {
  balanceLab: ['src/features/balance-lab/BalanceLabSection.tsx', 'BalanceLabSection', 'utility-input'],
  balanceLabCharts: ['src/features/balance-lab/BalanceLabCharts.tsx', 'BalanceLabChart', 'utility-input'],
  dynamaxAdventureSeedPlanner: ['src/features/dynamax-adventures/DynamaxAdventureSeedPlanner.tsx', 'DynamaxAdventureSeedPlanner', 'raw-local-draft'],
  changeSetWorkspace: ['src/features/change-sets/ChangeSetWorkspacePanel.tsx', 'ChangeSetWorkspacePanel', 'raw-local-draft'],
  advancedAuthoring: ['src/features/change-sets/AdvancedAuthoringPanel.tsx', 'AdvancedAuthoringPanel', 'raw-local-draft'],
  guidedDesign: ['src/features/guided-design/GuidedDesignSection.tsx', 'GuidedDesignSection', 'raw-local-draft'],
  inGameSettingsPackage: ['src/features/gameplay-settings/InGameSettingsPackagePanel.tsx', 'InGameSettingsPackagePanel', 'choice-local-draft'],
  researchAnnotations: ['src/features/research-lab/ResearchAnnotationsView.tsx', 'ResearchAnnotationsView', 'raw-local-draft'],
  researchCatalog: ['src/features/research-lab/ResearchCatalogViews.tsx', 'ResearchOwnershipView', 'utility-input'],
  researchComparison: ['src/features/research-lab/ResearchComparisonView.tsx', 'ResearchComparisonView', 'utility-input'],
  semanticExplore: ['src/features/semantic-explore/SemanticExploreSection.tsx', 'SemanticExploreSection', 'utility-input'],
  semanticMerge: ['src/features/semantic-merge/SemanticMergeSection.tsx', 'SemanticMergeSection', 'raw-local-draft'],
  gameModuleComparison: ['src/features/game-modules/GameModuleComparison.tsx', 'GameModuleComparison', 'utility-input'],
  outputSafety: ['src/features/output-safety/OutputSafetyPanel.tsx', 'OutputSafetyPanel', 'utility-input'],
  projectRelocation: ['src/features/output-safety/ProjectRelocationPanel.tsx', 'ProjectRelocationPanel', 'utility-input'],
  personalizationSettings: ['src/features/settings/PersonalizationSettingsPanel.tsx', 'PersonalizationSettingsPanel', 'choice-local-draft'],
  performanceDiagnostics: ['src/features/settings/PerformanceDiagnosticsPanel.tsx', 'PerformanceDiagnosticsPanel', 'choice-local-draft'],
  betaEditorsSettings: ['src/features/settings/BetaEditorsSettings.tsx', 'BetaEditorsSettings', 'choice-local-draft'],
  processMemorySettings: ['src/features/settings/ProcessMemoryPanel.tsx', 'ProcessMemoryPanel', 'choice-local-draft'],
  trainerIdentity: ['src/features/trainers/ZaTrainerIdentityActions.tsx', 'ZaTrainerIdentityActions', 'choice-local-draft'],
  commandPalette: ['src/features/workbench/CommandPalette.tsx', 'CommandPalette', 'utility-input'],
  shortcutOverlay: ['src/features/workbench/ShortcutOverlay.tsx', 'ShortcutOverlay', 'utility-input'],
  workspaceBrowserToolbar: ['src/features/workbench/WorkspaceBrowserToolbar.tsx', 'WorkspaceBrowserToolbar', 'utility-input']
};

/**
 * Shared controls can own native inputs without being independently navigable
 * editor surfaces. Keep them explicit so extracting a reusable KM control does
 * not weaken the native-control inventory gate.
 */
const sharedNativeControlInventory = {
  searchableOptionInput: ['src/components/SearchableOptionInput.tsx', 'SearchableOptionInput']
};

const allowedPolicies = new Set([
  'ordinary-session-local',
  'raw-local-draft',
  'choice-local-draft',
  'action-only',
  'utility-input',
  'read-only'
]);

/**
 * Every field-bearing surface is assigned one concrete draft lifecycle. This
 * list is intentionally independent from route registration so an editor
 * cannot be added under a generic policy without choosing its state contract.
 */
const editorDraftContractInventory = {
  items: 'ordinary-session-local',
  pokemon: 'ordinary-session-local',
  moves: 'ordinary-session-local',
  text: 'ordinary-session-local',
  trainers: 'ordinary-session-local',
  dexLayout: 'source-reconciled',
  trainerPools: 'captured-snapshot',
  fashionCatalog: 'keyed-exact-submit',
  giftPokemon: 'keyed-sparse-exact-submit',
  tradePokemon: 'keyed-sparse-exact-submit',
  staticEncounters: 'keyed-sparse-exact-submit',
  rentalPokemon: 'keyed-sparse-exact-submit',
  dynamaxAdventures: 'keyed-sparse-exact-submit',
  shops: 'keyed-sparse-exact-submit',
  battleCafeRewards: 'source-reconciled',
  tmMachineControls: 'immediate-choice',
  habitatCoordinates: 'keyed-exact-submit',
  encounters: 'keyed-sparse-exact-submit',
  teraRaids: 'keyed-sparse-exact-submit',
  raidBattles: 'keyed-sparse-exact-submit',
  raidRewards: 'keyed-sparse-exact-submit',
  raidBonusRewards: 'keyed-sparse-exact-submit',
  placement: 'keyed-sparse-exact-submit',
  behavior: 'keyed-sparse-exact-submit',
  catchCap: 'keyed-source-reconciled',
  hyperTraining: 'source-reconciled',
  shinyRate: 'source-reconciled',
  typeChart: 'source-reconciled',
  angeFight: 'source-reconciled',
  fairyGymBoosts: 'source-reconciled',
  exefsPatches: 'immediate-choice',
  fpsPatch: 'source-reconciled',
  royalCandy: 'keyed-source-reconciled',
  startingItems: 'keyed-source-reconciled',
  npcItemGift: 'source-reconciled',
  gameplaySettings: 'captured-review',
  dynamaxAdventureSeedPlanner: 'captured-snapshot',
  changeSetWorkspace: 'source-reconciled-exact-submit',
  advancedAuthoring: 'source-reconciled',
  guidedDesign: 'source-reconciled',
  inGameSettingsPackage: 'captured-review',
  researchAnnotations: 'keyed-exact-submit',
  semanticMerge: 'captured-snapshot',
  personalizationSettings: 'immediate-choice',
  betaEditorsSettings: 'immediate-choice',
  processMemorySettings: 'immediate-choice',
  performanceDiagnostics: 'immediate-choice',
  trainerIdentity: 'keyed-exact-submit'
};

const allowedDraftContracts = new Set([
  'ordinary-session-local',
  'source-reconciled',
  'keyed-source-reconciled',
  'keyed-exact-submit',
  'keyed-sparse-exact-submit',
  'source-reconciled-exact-submit',
  'captured-snapshot',
  'captured-review',
  'immediate-choice'
]);

const expectedDraftContractSurfaces = [
  ...Object.entries(editorSurfaceInventory),
  ...Object.entries(embeddedEditableSurfaceInventory)
]
  .filter(([, inventory]) => [
    'ordinary-session-local',
    'raw-local-draft',
    'choice-local-draft'
  ].includes(inventory[2]))
  .map(([surface]) => surface)
  .sort();
assert.deepEqual(
  Object.keys(editorDraftContractInventory).sort(),
  expectedDraftContractSurfaces,
  'Every field-bearing editor surface must declare an explicit draft lifecycle contract.'
);
for (const [surface, contract] of Object.entries(editorDraftContractInventory)) {
  assert.ok(
    allowedDraftContracts.has(contract),
    `${surface} has an unknown editor draft lifecycle contract.`
  );
}

const sectionSource = read('src/workbench/workbenchSections.ts');
const registeredSections = [...sectionSource.matchAll(/^  '([^']+)',?$/gmu)].map((match) => match[1]);
assert.deepEqual(
  Object.keys(editorSurfaceInventory).sort(),
  registeredSections.sort(),
  'Every workbench section must have an audited editor-surface interaction policy.'
);

for (const [section, [relativePath, renderer, policy]] of Object.entries(editorSurfaceInventory)) {
  const source = read(relativePath);
  assert.match(
    source,
    new RegExp(`\\b${renderer}\\b`, 'u'),
    `${section} is missing its audited renderer ${renderer} in ${relativePath}.`
  );
  assert.ok(
    allowedPolicies.has(policy),
    `${section} has an unknown editor interaction policy.`
  );
  const sourceFile = ts.createSourceFile(
    relativePath,
    source,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TSX
  );
  const rendererNode = findFunctionLike(sourceFile, renderer);
  assert.ok(rendererNode, `${section} is missing the ${renderer} component declaration.`);
  assert.match(
    rendererNode.getText(sourceFile),
    /\bwide-panel\b|\bFocusedEditorWorkspace\b|\bproject-gate\b/u,
    `${section} must opt into a full-width editor root.`
  );
}

for (const [surface, [relativePath, renderer, policy]] of Object.entries(embeddedEditableSurfaceInventory)) {
  const source = read(relativePath);
  assert.ok(allowedPolicies.has(policy), `${surface} has an unknown embedded interaction policy.`);
  const sourceFile = ts.createSourceFile(
    relativePath,
    source,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TSX
  );
  assert.ok(
    findFunctionLike(sourceFile, renderer),
    `${surface} is missing its audited embedded renderer ${renderer} in ${relativePath}.`
  );
}

for (const [control, [relativePath, renderer]] of Object.entries(sharedNativeControlInventory)) {
  const source = read(relativePath);
  const sourceFile = ts.createSourceFile(
    relativePath,
    source,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TSX
  );
  assert.ok(
    findFunctionLike(sourceFile, renderer),
    `${control} is missing its audited shared KM control ${renderer} in ${relativePath}.`
  );
}

const nativeControlFiles = sourceFilesUnder(sourceRoot)
  .filter((path) => /<(?:input|select|textarea)\b/u.test(readFileSync(path, 'utf8')))
  .map((path) => relative(desktopRoot, path).replaceAll('\\', '/'))
  .sort();
const inventoriedNativeControlFiles = new Set([
  ...Object.values(editorSurfaceInventory).map(([relativePath]) => relativePath),
  ...Object.values(embeddedEditableSurfaceInventory).map(([relativePath]) => relativePath),
  ...Object.values(sharedNativeControlInventory).map(([relativePath]) => relativePath)
]);
assert.deepEqual(
  nativeControlFiles,
  [...inventoriedNativeControlFiles]
    .filter((path) => /<(?:input|select|textarea)\b/u.test(read(path)))
    .sort(),
  'Every TSX file with a native user control must belong to an audited top-level or embedded surface.'
);

assert.deepEqual(
  Object.entries(editorSurfaceInventory)
    .filter(([, inventory]) => inventory[2] === 'ordinary-session-local')
    .map(([section]) => section)
    .sort(),
  ['items', 'moves', 'pokemon', 'text', 'trainers'],
  'The session-local ordinary-editor policy must cover all and only its five consumers.'
);

const app = read('src/App.tsx');
assert.equal(
  [...app.matchAll(/useSessionLocalEditorDraftBinding\(/gu)].length,
  5,
  'Every ordinary editor must use one session-local binding.'
);
assert.equal(
  [...app.matchAll(/useOrdinaryEditorDraft\(/gu)].length,
  0,
  'Ordinary editors must not mount the durable autosave hook.'
);
assert.doesNotMatch(
  app,
  /\bdraft\.update\s*\(/u,
  'Ordinary editor typing must not write to a persisted draft.'
);
assert.doesNotMatch(
  app,
  /\bOrdinaryEditorDraftStatus\b/u,
  'Ordinary editors must not render durable draft lifecycle UI.'
);
for (const section of ['items', 'pokemon', 'moves', 'text', 'trainers']) {
  assert.equal(
    [...app.matchAll(new RegExp(`useRegisterEditorDraftDirty\\(\\s*['"]${section}['"]`, 'gu'))].length,
    1,
    `${section} must register its session-local dirty state exactly once.`
  );
}
assert.doesNotMatch(
  app,
  /<OrdinaryEditorDraftProvider\b|useOrdinaryEditorDraftProtection|ordinaryEditorDraftProtectionSnapshot/u,
  'Ordinary editor runtime state must not depend on the durable draft provider or protection index.'
);

const appKeyedDraftOwners = {
  giftPokemon: ['SelectedGiftPokemonPanel', /\bset(?:Sparse)?FieldDraftRecord\(/u],
  tradePokemon: ['SelectedTradePokemonPanel', /\bset(?:Sparse)?FieldDraftRecord\(/u],
  staticEncounters: ['SelectedStaticEncounterPanel', /\bset(?:Sparse)?FieldDraftRecord\(/u],
  rentalPokemon: ['SelectedRentalPokemonPanel', /\bset(?:Sparse)?FieldDraftRecord\(/u],
  dynamaxAdventures: ['SelectedDynamaxAdventurePanel', /\bset(?:Sparse)?FieldDraftRecord\(/u],
  shops: ['SelectedShopPanel', /\bsetInventoryDraftsByShopId\(/u],
  encounters: ['SelectedEncounterPanel', /\bset(?:Sparse)?FieldDraftRecord\(/u],
  teraRaids: ['TeraRaidDraftPanel', /\bset(?:Sparse)?FieldDraftRecord\(/u],
  raidBattles: ['SelectedRaidBattlePanel', /\bset(?:Sparse)?FieldDraftRecord\(/u],
  raidRewards: ['SelectedRaidRewardPanel', /\bset(?:Sparse)?FieldDraftRecord\(/u],
  raidBonusRewards: ['SelectedRaidRewardPanel', /\bset(?:Sparse)?FieldDraftRecord\(/u],
  placement: ['SelectedPlacementPanel', /\bset(?:Sparse)?FieldDraftRecord\(/u],
  behavior: ['SelectedBehaviorPanel', /\bset(?:Sparse)?FieldDraftRecord\(/u]
};
const appSourceFile = ts.createSourceFile(
  'src/App.tsx',
  app,
  ts.ScriptTarget.Latest,
  true,
  ts.ScriptKind.TSX
);
assert.deepEqual(
  Object.keys(appKeyedDraftOwners).sort(),
  Object.entries(editorDraftContractInventory)
    .filter(([, contract]) => contract === 'keyed-sparse-exact-submit')
    .map(([surface]) => surface)
    .sort(),
  'Every inline keyed/sparse editor must have a component-level exact-submit audit owner.'
);
for (const [surface, [owner, keyedDraftMarker]] of Object.entries(appKeyedDraftOwners)) {
  const ownerNode = findFunctionLike(appSourceFile, owner);
  assert.ok(ownerNode, `${surface} is missing inline draft owner ${owner}.`);
  const ownerSource = ownerNode.getText(appSourceFile);
  assert.match(
    ownerSource,
    keyedDraftMarker,
    `${surface} must retain a keyed or sparse local field draft instead of committing partial input.`
  );
  assert.match(
    ownerSource,
    /\bclearSubmittedKeyedEditorDraft\(/u,
    `${surface} must clear only the exact keyed snapshot submitted successfully.`
  );
}

assert.equal(
  [...app.matchAll(/reconcileKeyedSourceBackedEditorDrafts\(/gu)].length,
  3,
  'Catch Cap, Royal Candy, and Starting Items must all use keyed source reconciliation.'
);
const hyperTrainingNode = findFunctionLike(appSourceFile, 'HyperTrainingSection');
const hyperTrainingSource = hyperTrainingNode?.getText(appSourceFile) ?? '';
assert.match(
  hyperTrainingSource,
  /\breconcileSourceBackedDraft\(/u,
  'Hyper Training must preserve a locally edited or blank cutoff across source refresh.'
);
for (const controlId of ['hyper-training-cutoff']) {
  assert.doesNotMatch(
    hyperTrainingSource,
    new RegExp(
      `<input\\b(?=[^>]*id="${controlId}")(?=[^>]*disabled=\\{[^}]*\\b(?:isStaging|isChangePlanCreating|isChangePlanApplying)\\b)[^>]*>`,
      'u'
    ),
    `${controlId} must remain editable while an earlier cutoff is staging or saving.`
  );
}
assert.doesNotMatch(
  hyperTrainingSource,
  /<input\b(?=[^>]*type="range")(?=[^>]*disabled=\{[^}]*\b(?:isStaging|isChangePlanCreating|isChangePlanApplying)\b)[^>]*>/u,
  'Hyper Training range input must remain editable while an earlier cutoff is staging or saving.'
);

const fpsPatchNode = findFunctionLike(appSourceFile, 'FpsPatchSection');
const fpsPatchSource = fpsPatchNode?.getText(appSourceFile) ?? '';
assert.doesNotMatch(
  fpsPatchSource,
  /<select\b(?=[^>]*id=\{`fps-patch-\$\{component\.id\}-desired`\})(?=[^>]*disabled=\{[^}]*\bisBusy\b)[^>]*>/u,
  '60FPS timing choices must remain editable while an earlier snapshot is applying or refreshing.'
);
assert.match(
  app,
  /setFpsPatchDesiredAnimationTimingComponentIds\(\(currentSelection\) =>[\s\S]*?reconcileFpsPatchAnimationTimingSelection\(/u,
  '60FPS timing refresh and apply completion must reconcile rather than overwrite newer choices.'
);

for (const path of sourceFilesUnder(sourceRoot)) {
  const source = readFileSync(path, 'utf8');
  const sourceFile = ts.createSourceFile(path, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const visit = (node) => {
    if (
      (ts.isJsxSelfClosingElement(node) || ts.isJsxOpeningElement(node)) &&
      ['input', 'select', 'textarea'].includes(node.tagName.getText(sourceFile))
    ) {
      const attributes = new Map(
        node.attributes.properties
          .filter(ts.isJsxAttribute)
          .map((attribute) => [attribute.name.getText(sourceFile), attribute])
      );
      const type = attributes.get('type')?.initializer?.getText(sourceFile) ?? '';
      const inputMode = attributes.get('inputMode')?.initializer?.getText(sourceFile) ?? '';
      const role = attributes.get('role')?.initializer?.getText(sourceFile) ?? '';
      const onChange = attributes.get('onChange')?.initializer?.getText(sourceFile) ?? '';
      const disabled = attributes.get('disabled')?.initializer?.getText(sourceFile) ?? '';
      const value = attributes.get('value')?.initializer?.getText(sourceFile) ?? '';
      if (type === '"number"' || inputMode === '"numeric"') {
        assert.doesNotMatch(
          onChange,
          /\b(?:Number|parseInt|parseFloat|Number\.parseInt|Number\.parseFloat)\s*\(/u,
          `${path}: numeric input must retain raw text during onChange.`
        );
        assert.doesNotMatch(
          value,
          /\b(?:Number|parseInt|parseFloat|Number\.parseInt|Number\.parseFloat)\s*\(|(?:\|\||\?\?)\s*0\b/u,
          `${path}: numeric input value must remain the user's raw text, including an intentional blank.`
        );
      }
      assert.doesNotMatch(
        disabled,
        /\b(?:invalid|isValid|validationError|draftError|draftState|hasInputError|hasLocalDrafts?)\b/iu,
        `${path}: transient validation or a local draft must not disable its own editable control.`
      );
      if (/\bon(?:Update|Stage)[A-Za-z0-9]*\s*\(/u.test(onChange)) {
        assert.ok(
          node.tagName.getText(sourceFile) === 'select' || type === '"checkbox"' || type === '"radio"',
          `${path}: free-form input must not invoke a staging/update bridge on each partial keystroke.`
        );
      }
      if (role === '"combobox"') {
        assert.doesNotMatch(
          onChange,
          /\bon(?:Change|Update|Stage)[A-Za-z0-9]*\s*\(/u,
          `${path}: combobox filtering must remain local until an exact/explicit commit.`
        );
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
}

for (const [functionName, requiredCall] of [
  ['saveSelectedEvolution', 'runSessionLocalEditorSourceMutation'],
  ['stageTrainerDrafts', 'runSessionLocalEditorSourceMutation'],
  ['stageTrainerMaxIvs', 'runSessionLocalEditorSourceMutation']
]) {
  const declaration = new RegExp(
    `const ${functionName} = async \\(\\) => \\{([\\s\\S]*?)(?=\\n  const [A-Za-z]|\\n  return \\()`,
    'u'
  ).exec(app)?.[1] ?? '';
  assert.ok(declaration, `Missing partial-stage function ${functionName}.`);
  assert.match(
    declaration,
    new RegExp(`\\b${requiredCall}\\b`, 'u'),
    `${functionName} must preserve unrelated session-local fields across its staged source mutation.`
  );
  assert.doesNotMatch(
    declaration,
    /\.clearDurable\(|\.discard\(/u,
    `${functionName} must not clear the entire local record draft after staging only part of it.`
  );
}

for (const [functionName, requiredDraftSetter] of [
  ['handlePasteLearnsetClipboardRow', 'setLearnsetDraftsByPokemonId'],
  ['pastePartySlot', 'setPokemonDraftsByTrainerSlot'],
  ['handlePasteEncounterClipboardSlot', 'setDraftsBySlotKey']
]) {
  const declaration = new RegExp(
    `const ${functionName} = async \\([^)]*\\) => \\{([\\s\\S]*?)(?=\\n  const [A-Za-z]|\\n  return \\()`,
    'u'
  ).exec(app)?.[1] ?? '';
  assert.ok(declaration, `Missing clipboard draft function ${functionName}.`);
  assert.match(
    declaration,
    new RegExp(`\\b${requiredDraftSetter}\\b`, 'u'),
    `${functionName} must write pasted values into local drafts.`
  );
  assert.doesNotMatch(
    declaration,
    /\brunSessionLocalEditorSourceMutation\b|\bpreviewRowClipboardPaste\b|\bstageRowClipboardPaste\b/u,
    `${functionName} must not preview or stage source mutations during Paste.`
  );
}

const npcItemGift = read('src/features/npc-item-gift/NpcItemGiftSection.tsx');
const searchableOptionInput = read('src/components/SearchableOptionInput.tsx');
const searchableOptionInputQueryHandler = between(
  searchableOptionInput,
  'const handleInputChange = (nextValue: string) => {',
  'const menu = hasMenu ? ('
);
assert.match(
  searchableOptionInputQueryHandler,
  /transitionSearchableOptionInteraction\([\s\S]*?\{ query: nextValue, type: 'input' \}/u,
  'The shared KM option control must keep partial search text in its interaction state.'
);
assert.doesNotMatch(
  searchableOptionInputQueryHandler,
  /\bonChange\s*\(/u,
  'The shared KM option control must not publish partial search text to an editor source.'
);
assert.match(
  between(
    npcItemGift,
    'function NpcItemGiftItemPicker({',
    'function NpcItemGiftSourceSummary({'
  ),
  /<SearchableOptionInput[\s\S]*?onChange=/u,
  'NPC Item Gift option filtering must use the shared local-query KM option control.'
);
const battleCafe = read('src/features/battle-cafe-rewards/BattleCafeRewardsSection.tsx');
assert.match(
  between(
    battleCafe,
    'function BattleCafeItemPicker({',
    'function decodeStagedRows('
  ),
  /<SearchableOptionInput[\s\S]*?onChange=/u,
  'Battle Cafe option filtering must use the shared local-query KM option control.'
);
assert.match(
  battleCafe,
  /field: 'totals',\s*message: workflow === null \|\| totalsAreExact \? null/u,
  'Battle Cafe must not publish derived validation before its workflow loads.'
);

assert.equal(
  reconcileSourceBackedDraft('source-a', 'source-a', 'source-b', Object.is),
  'source-b',
  'A clean source-backed draft must adopt a refreshed source value.'
);
assert.equal(
  reconcileSourceBackedDraft('', 'source-a', 'source-b', Object.is),
  '',
  'An intentional blank draft must survive a source refresh.'
);
assert.deepEqual(
  reconcileEligibleDraftSelection(
    new Set(['kept', 'removed']),
    new Set(['kept', 'removed']),
    new Set(['kept', 'added'])
  ),
  new Set(['kept', 'added']),
  'An eligible selection refresh must retain valid choices, remove stale choices, and adopt new defaults.'
);
assert.equal(
  areStringSetsEqual(new Set(['a', 'b']), new Set(['b', 'a'])),
  true,
  'Set-backed editor drafts must compare by membership instead of insertion order.'
);

const previousKeyedSource = {
  locallyChanged: { value: 'old local source' },
  removedLocallyChanged: { value: 'old removed source' },
  removedUntouched: { value: 'old removed value' },
  untouched: { value: 'old source' }
};
const currentKeyedDrafts = {
  locallyChanged: { value: 'unsaved local edit' },
  locallyIntroduced: { value: 'unsaved new key' },
  removedLocallyChanged: { value: 'unsaved edit for removed key' },
  removedUntouched: previousKeyedSource.removedUntouched,
  untouched: previousKeyedSource.untouched
};
const nextKeyedSource = {
  addedByRefresh: { value: 'new source key' },
  locallyChanged: { value: 'refreshed source behind local edit' },
  untouched: { value: 'refreshed source value' }
};
const keyedDraftEquality = (left, right) => left.value === right.value;
assert.deepEqual(
  reconcileKeyedSourceBackedEditorDrafts(
    currentKeyedDrafts,
    previousKeyedSource,
    nextKeyedSource,
    keyedDraftEquality
  ),
  {
    addedByRefresh: nextKeyedSource.addedByRefresh,
    locallyChanged: currentKeyedDrafts.locallyChanged,
    locallyIntroduced: currentKeyedDrafts.locallyIntroduced,
    removedLocallyChanged: currentKeyedDrafts.removedLocallyChanged,
    untouched: nextKeyedSource.untouched
  },
  'Keyed source refreshes must advance untouched values, seed new keys, remove only untouched deleted keys, and preserve every local edit.'
);
assert.strictEqual(
  reconcileKeyedSourceBackedEditorDrafts(
    currentKeyedDrafts,
    previousKeyedSource,
    currentKeyedDrafts,
    keyedDraftEquality
  ),
  currentKeyedDrafts,
  'A no-op keyed source reconciliation must retain the original collection.'
);
assert.equal(
  resolveSubmittedEditorDraft('submitted name', 'submitted name', ''),
  '',
  'An exact successfully submitted local value may be cleared.'
);
assert.equal(
  resolveSubmittedEditorDraft('newer name', 'submitted name', ''),
  'newer name',
  'A newer local value must survive completion of an earlier submission.'
);

for (const relativePath of [
  'src/features/ange-fight/AngeFightSection.tsx',
  'src/features/battle-cafe-rewards/BattleCafeRewardsSection.tsx',
  'src/features/change-sets/ChangeSetWorkspacePanel.tsx',
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
    /\breconcileSourceBackedDraft\(/u,
    `${relativePath} must preserve locally changed source-backed drafts during refresh.`
  );
}

for (const [relativePath, editableGate] of [
  ['src/features/ange-fight/AngeFightSection.tsx', 'canEdit'],
  ['src/features/battle-cafe-rewards/BattleCafeRewardsSection.tsx', 'canEdit'],
  ['src/features/fairy-gym-boosts/FairyGymBoostsSection.tsx', 'canEdit'],
  ['src/features/npc-item-gift/NpcItemGiftSection.tsx', 'canEditWorkflow'],
  ['src/features/shiny-rate/ShinyRateSection.tsx', 'canEdit'],
  ['src/features/type-chart/TypeChartSection.tsx', 'canEdit']
]) {
  const source = read(relativePath);
  const sourceFile = ts.createSourceFile(
    relativePath,
    source,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TSX
  );
  const declaration = findVariableDeclaration(sourceFile, editableGate);
  assert.ok(declaration?.initializer, `${relativePath} is missing ${editableGate}.`);
  assert.doesNotMatch(
    declaration.initializer.getText(sourceFile),
    /\b(?:isBusy|isStaging|isChangePlanCreating|isChangePlanApplying)\b/u,
    `${relativePath}: source-backed draft controls must remain editable while an earlier snapshot stages.`
  );
}

const trainerPools = read('src/features/trainer-pools/TrainerPoolsSection.tsx');
assert.doesNotMatch(
  trainerPools,
  /<PoolSelection\b[\s\S]*?disabled=\{hasPendingSwap\}/u,
  'Trainer Pool selectors must remain editable while an older staged transaction awaits review.'
);
assert.match(
  trainerPools,
  /!hasPendingSwap\s*&&\s*!isStaging/u,
  'Trainer Pool staging must block a conflicting staged transaction without locking selector input.'
);
assert.doesNotMatch(
  trainerPools,
  /<PoolSelection\b[\s\S]*?disabled=\{[^}]*\bisStaging\b[^}]*\}/u,
  'Trainer Pool selectors must remain editable while an earlier captured selection stages.'
);
assert.match(
  trainerPools,
  /const requestedSource = source;[\s\S]*?const requestedDestination = destination;[\s\S]*?sameSelection\(selectionRef\.current\.source, requestedSource\)/u,
  'Trainer Pool staging must compare its captured selection before publishing request feedback.'
);

const semanticMerge = read('src/features/semantic-merge/SemanticMergeSection.tsx');
assert.doesNotMatch(
  semanticMerge,
  /setResolutionDraft\(new Map\(\)\);\s*await controller\.previewMerge/u,
  'Semantic Merge must not discard resolution choices before a preview request succeeds.'
);
const outputSafety = read('src/features/output-safety/OutputSafetyPanel.tsx');
assert.match(
  outputSafety,
  /const submittedLabel = checkpointLabel;[\s\S]*?if \(created\) \{[\s\S]*?current === submittedLabel \? '' : current/u,
  'Output Safety must clear only the exact checkpoint label submitted successfully.'
);
const guidedDesign = read('src/features/guided-design/GuidedDesignSection.tsx');
assert.doesNotMatch(
  guidedDesign,
  /<input\b[^>]*aria-invalid=\{!isValid\}[^>]*disabled=\{isBusy\}[^>]*>/u,
  'Guided Design pin values must remain editable while an earlier preview request runs.'
);
const guidedDesignSourceFile = ts.createSourceFile(
  'GuidedDesignSection.tsx',
  guidedDesign,
  ts.ScriptTarget.Latest,
  true,
  ts.ScriptKind.TSX
);
const existingPinConstraint = findFunctionLike(guidedDesignSourceFile, 'ExistingPinConstraint');
assert.ok(existingPinConstraint, 'Guided Design is missing ExistingPinConstraint.');
assert.match(
  existingPinConstraint.getText(guidedDesignSourceFile),
  /usePublishCommonEditorError\(\{\s*domain: 'analysis\.guidedDesign',\s*field: `pins\.\$\{key\}\.canonicalValue`,\s*message: isValid \? null : t\('guidedDesign\.diff\.pinValueInvalid'\)/u,
  'Every invalid existing Guided Design pin value must publish its exact field diagnostic to the common bottom diagnostics.'
);
const mutationPinControl = findFunctionLike(guidedDesignSourceFile, 'MutationPinControl');
assert.ok(mutationPinControl, 'Guided Design is missing MutationPinControl.');
assert.match(
  mutationPinControl.getText(guidedDesignSourceFile),
  /usePublishCommonEditorError\(\{\s*domain: 'analysis\.guidedDesign',\s*field: `pins\.\$\{diagnosticPinKey\}\.canonicalValue`,\s*message: hasPinTarget && !isValid \? t\('guidedDesign\.diff\.pinValueInvalid'\) : null/u,
  'Every invalid proposed Guided Design pin value must publish its exact field diagnostic to the common bottom diagnostics.'
);
const canonicalExports = findFunctionLike(guidedDesignSourceFile, 'CanonicalExports');
assert.ok(canonicalExports, 'Guided Design is missing CanonicalExports.');
assert.match(
  canonicalExports.getText(guidedDesignSourceFile),
  /usePublishCommonEditorError\(\{\s*domain: 'analysis\.guidedDesign',\s*field: 'exports',\s*message: errorMessage/u,
  'Guided Design copy and download failures must publish their exact action diagnostic to the common bottom diagnostics.'
);
assert.equal(
  [...guidedDesign.matchAll(/aria-invalid=\{!isValid\}/gu)].length,
  2,
  'Every Guided Design canonical pin input must remain covered by a function-scoped common diagnostic publisher.'
);

const dynamaxAdventureSeedPlanner = read(
  'src/features/dynamax-adventures/DynamaxAdventureSeedPlanner.tsx'
);
const dynamaxAdventureSeedPlannerSourceFile = ts.createSourceFile(
  'DynamaxAdventureSeedPlanner.tsx',
  dynamaxAdventureSeedPlanner,
  ts.ScriptTarget.Latest,
  true,
  ts.ScriptKind.TSX
);
const dynamaxAdventureSeedPlannerOwner = findFunctionLike(
  dynamaxAdventureSeedPlannerSourceFile,
  'DynamaxAdventureSeedPlanner'
);
assert.ok(
  dynamaxAdventureSeedPlannerOwner,
  'Dynamax Adventure Seed Planner is missing its audited renderer.'
);
const dynamaxAdventureSeedPlannerOwnerSource = dynamaxAdventureSeedPlannerOwner.getText(
  dynamaxAdventureSeedPlannerSourceFile
);
for (const [field, messageExpression] of [
  ['seed', 'seedError'],
  ['requiredRows', 'parsedRows\\.error'],
  ['searchLimit', 'searchLimitError'],
  ['maximumResults', 'maximumResultsError']
]) {
  assert.match(
    dynamaxAdventureSeedPlannerOwnerSource,
    new RegExp(
      `usePublishCommonEditorError\\(\\{\\s*domain: 'workflow\\.dynamaxAdventures',` +
      `\\s*field: '${field}',\\s*message: ${messageExpression}\\s*\\}\\);`,
      'u'
    ),
    `Dynamax Adventure Seed Planner ${field} validation must publish its exact field diagnostic to the common bottom diagnostics.`
  );
}
assert.equal(
  [...dynamaxAdventureSeedPlanner.matchAll(/aria-invalid=\{Boolean\([^)]+\)\}/gu)].length,
  4,
  'Every Dynamax Adventure Seed Planner invalid field must remain covered by an exact common diagnostic publisher.'
);

const advancedAuthoring = read('src/features/change-sets/AdvancedAuthoringPanel.tsx');
assert.match(
  advancedAuthoring,
  /const controllerChanged = controllerRef\.current !== controller;[\s\S]*?candidate\.adapterId === adapterIdRef\.current[\s\S]*?candidate\.fieldKey === fieldKeyRef\.current[\s\S]*?semanticRecordRefKey\(record\.record\) === sourceRecordKeyRef\.current/u,
  'Advanced Authoring must retain still-eligible editor, field, and source-record choices across source revisions.'
);
assert.match(
  advancedAuthoring,
  /if \(controllerChanged\) \{\s*setRecordQuery\(''\);\s*\}/u,
  'Advanced Authoring must retain a partial record query across a same-project source revision.'
);

const changeSetWorkspace = read('src/features/change-sets/ChangeSetWorkspacePanel.tsx');
const changeSetWorkspaceTypes = read('src/features/change-sets/changeSetWorkspaceTypes.ts');
const changeSetWorkspaceController = read(
  'src/features/change-sets/useChangeSetWorkspaceController.ts'
);
assert.match(
  changeSetWorkspaceTypes,
  /onCreate: \(name: string\) => Promise<boolean>;/u,
  'Change Set creation must expose confirmed success to its draft owner.'
);
assert.match(
  changeSetWorkspaceTypes,
  /onCreateBuildVariant:[\s\S]*?\) => Promise<boolean>;/u,
  'Build Variant creation must expose confirmed success to its draft owner.'
);
assert.match(
  changeSetWorkspaceController,
  /const runControllerAction = useCallback\(async[\s\S]*?await operation\(\);\s*return true;[\s\S]*?return false;/u,
  'Change Set actions must resolve with an explicit success result.'
);
assert.match(
  changeSetWorkspaceController,
  /if \(!created\) \{\s*throw new Error\('The change-set create response did not contain the new change set\.'\);/u,
  'Change Set creation must confirm the created record in the returned snapshot.'
);
assert.match(
  changeSetWorkspaceController,
  /if \(!next\.document\.buildVariants\.some\(\(variant\) => variant\.variantId === variantId\)\) \{\s*throw new Error\('The build-variant create response did not contain the new variant\.'\);/u,
  'Build Variant creation must confirm the created record in the returned snapshot.'
);
assert.equal(
  [...changeSetWorkspace.matchAll(/const submittedName = (?:newName|name);/gu)].length,
  2,
  'Change Set and Build Variant creation must capture the exact submitted name draft.'
);
assert.equal(
  [...changeSetWorkspace.matchAll(/if \(created\) \{\s*set(?:NewName|Name)\(\(current\) =>\s*resolveSubmittedEditorDraft\(current, submittedName, ''\)\s*\);\s*\}/gu)].length,
  2,
  'Change Set and Build Variant names must clear only after confirmed success and only when unchanged.'
);
for (const inputId of ['change-set-new-name', 'change-set-new-variant']) {
  assert.doesNotMatch(
    changeSetWorkspace,
    new RegExp(
      `<input\\b(?=[^>]*id="${inputId}")(?=[^>]*disabled=\\{isBusy\\})[^>]*>`,
      'u'
    ),
    `${inputId} must remain editable while its earlier submitted name is creating.`
  );
}

const workbench = read('src/features/workbench/WorkbenchSection.tsx');
assert.equal(
  [...workbench.matchAll(/onCreate(?:Bookmark|OutputProfile)\?: \([^)]*\) => Promise<boolean>;/gu)].length,
  2,
  'Workbench bookmark and output-profile creation must expose confirmed success.'
);
assert.match(
  workbench,
  /const submittedName = name;[\s\S]*?const created = await onCreate\(normalizedName\);[\s\S]*?resolveSubmittedEditorDraft\(current, submittedName, ''\)/u,
  'WorkspaceCreateControl must clear only an unchanged name after confirmed creation.'
);
assert.match(
  workbench,
  /const submittedScopeKey = scopeKeyRef\.current;[\s\S]*?if \(created && scopeKeyRef\.current === submittedScopeKey\)/u,
  'WorkspaceCreateControl must not clear a create-name draft after the active project scope changes.'
);
assert.equal(
  [...workbench.matchAll(/scopeKey=\{preparationScopeKey\}/gu)].length,
  2,
  'Bookmark and output-profile creation must both use the active preparation project scope.'
);
assert.doesNotMatch(
  workbench,
  /<input\b(?=[^>]*value=\{name\})(?=[^>]*disabled=\{isCreating\})[^>]*>/u,
  'WorkspaceCreateControl must let a newer name be typed while an earlier name is creating.'
);

const dexLayout = read('src/features/dex-layout/ZaDexLayoutSection.tsx');
assert.match(
  dexLayout,
  /reconcileSourceBackedDraft\(\s*resizeDraftRef\.current/u,
  'Dex Layout source refreshes must preserve a locally changed resize draft.'
);
assert.match(
  dexLayout,
  /reconcileSourceBackedDraft\(\s*moveDraftRef\.current/u,
  'Dex Layout source refreshes must preserve a locally changed placement draft.'
);
for (const control of [
  'DexSizeControl',
  'za-dex-layout-destination-dex',
  'za-dex-layout-destination-number'
]) {
  const controlPattern = control === 'DexSizeControl'
    ? /<DexSizeControl\b[^>]*disabled=\{[^}]*\bisWorkflowActionBusy\b[^}]*\}/u
    : new RegExp(
        `<(?:input|select)\\b[^>]*disabled=\\{[^}]*\\bisWorkflowActionBusy\\b[^}]*\\}[^>]*id="${control}"`,
        'u'
      );
  assert.doesNotMatch(
    dexLayout,
    controlPattern,
    `Dex Layout ${control} must remain editable while an earlier snapshot stages.`
  );
}

const fashionCatalog = read('src/features/fashion-catalog/FashionCatalogSection.tsx');
assert.doesNotMatch(
  fashionCatalog,
  /setDraftValue\(selectedRow[\s\S]*?\[field, selectedRow\]/u,
  'Fashion Catalog must not erase an un-staged value when the visible row or field changes.'
);
assert.match(
  fashionCatalog,
  /clearStagedFashionCatalogDraftValue\([\s\S]*?stagedDraftKey,[\s\S]*?stagedDraftValue/u,
  'Fashion Catalog must clear only the exact draft value that was staged successfully.'
);
assert.match(
  fashionCatalog,
  /onDirtyStateChange\?\.\(Object\.keys\(draftValues\)\.length > 0\)/u,
  'Fashion Catalog must expose its keyed local-draft state to workbench navigation.'
);
assert.match(
  fashionCatalog,
  /const outstandingDraftCount = outstandingDraftKeys\.length;[\s\S]*?const reviewableDraftKeys = outstandingDraftKeys\.filter[\s\S]*?getNextOutstandingEditorDraftKey\([\s\S]*?reviewableDraftKeys,[\s\S]*?draftKey/u,
  'Fashion Catalog must summarize every retained field draft and choose the next resolvable one deterministically.'
);
assert.match(
  fashionCatalog,
  /fashionCatalog\.editor\.draftSummary[\s\S]*?fashionCatalog\.editor\.reviewNextDraft/u,
  'Fashion Catalog must expose its aggregate draft count and a Review Next Draft action.'
);
assert.match(
  fashionCatalog,
  /const reviewNextDraft = \(\) => \{[\s\S]*?setCatalogFile\(target\.catalogFile\);[\s\S]*?setSelectedRowId\(target\.row\.physicalRowId\);[\s\S]*?setSelectedField\(target\.field\.field\);/u,
  'Fashion Catalog Review Next Draft must select the exact retained file, row, and field.'
);
assert.match(
  fashionCatalog,
  /getElementById\('fashion-catalog-value'\)\?\.focus\(\{ preventScroll: true \}\)/u,
  'Fashion Catalog Review Next Draft must focus without scrolling the workspace.'
);
assert.doesNotMatch(
  between(
    fashionCatalog,
    'const reviewNextDraft = () => {',
    'const discardUnavailableDrafts = () => {'
  ),
  /\bsetSearch\s*\(/u,
  'Fashion Catalog Review Next Draft must preserve the current row search.'
);
assert.match(
  fashionCatalog,
  /emptyOptionDisabled=\{!field\.optional\}[\s\S]*?emptyOptionLabel=\{field\.optional/u,
  'Fashion Catalog optional option fields must let users return a local choice to blank.'
);
assert.match(
  fashionCatalog,
  /if \(sourceValue\.length === 0\) \{\s*updateDraftValue\(''\);/u,
  'Fashion Catalog must discard an un-staged optional value whose source is already blank.'
);

const fashionKeyA = createFashionCatalogDraftKey('dressUpItems', 'row-a', 'itemId');
const fashionKeyB = createFashionCatalogDraftKey('dressUpItems', 'row-b', 'colorId');
let fashionDrafts = setFashionCatalogDraftValue({}, fashionKeyA, '', '100');
fashionDrafts = setFashionCatalogDraftValue(fashionDrafts, fashionKeyB, '9', '2');
assert.deepEqual(
  fashionDrafts,
  { [fashionKeyA]: '', [fashionKeyB]: '9' },
  'Fashion Catalog must retain blank and unrelated per-row/per-field drafts.'
);
const changedWhileStaging = setFashionCatalogDraftValue(
  fashionDrafts,
  fashionKeyA,
  '101',
  '100'
);
assert.equal(
  clearStagedFashionCatalogDraftValue(changedWhileStaging, fashionKeyA, '')[fashionKeyA],
  '101',
  'A delayed Fashion Catalog stage must not erase a newer value for the same field.'
);
fashionDrafts = clearStagedFashionCatalogDraftValue(fashionDrafts, fashionKeyA, '');
assert.deepEqual(
  fashionDrafts,
  { [fashionKeyB]: '9' },
  'A successful Fashion Catalog stage must preserve unrelated row and field drafts.'
);
assert.deepEqual(
  setFashionCatalogDraftValue(fashionDrafts, fashionKeyB, '2', '2'),
  {},
  'Returning one Fashion Catalog field to its source value must clear only that draft.'
);

const habitatCoordinates = read('src/features/habitat-coordinates/HabitatCoordinatesSection.tsx');
assert.doesNotMatch(
  habitatCoordinates,
  /setCoordinateDraft\(effectiveCoordinate[\s\S]*?\[selectedKey/u,
  'Habitat Coordinates must not erase an un-staged coordinate when selection or source changes.'
);
assert.match(
  habitatCoordinates,
  /clearStagedHabitatCoordinateDraftValue\([\s\S]*?stagedDraftKey,[\s\S]*?stagedDraftValue/u,
  'Habitat Coordinates must clear only the exact draft value staged successfully.'
);
assert.match(
  habitatCoordinates,
  /onDirtyStateChange\?\.\(Object\.keys\(coordinateDrafts\)\.length > 0\)/u,
  'Habitat Coordinates must expose its keyed local-draft state to workbench navigation.'
);
assert.match(
  habitatCoordinates,
  /const outstandingDraftCount = outstandingDraftKeys\.length;[\s\S]*?const reviewableDraftKeys = outstandingDraftKeys\.filter\([\s\S]*?getNextOutstandingEditorDraftKey\([\s\S]*?reviewableDraftKeys,[\s\S]*?coordinateDraftKey/u,
  'Habitat Coordinates must count every retained coordinate draft while choosing only a reviewable next target deterministically.'
);
assert.match(
  habitatCoordinates,
  /habitatCoordinates\.editor\.draftSummary[\s\S]*?habitatCoordinates\.editor\.reviewNextDraft/u,
  'Habitat Coordinates must expose its aggregate draft count and a Review Next Draft action.'
);
assert.match(
  habitatCoordinates,
  /\[coordinateDraftKey\]: \{[\s\S]*?query: queryFromPage\(page, \{\}\),[\s\S]*?rowKey: rowKey\(selectedRecord\),[\s\S]*?value/u,
  'Habitat Coordinates must retain the exact query and row needed to revisit an off-page draft.'
);
assert.match(
  habitatCoordinates,
  /currentTargets\[stagedDraftKey\]\?\.value === stagedDraftValue[\s\S]*?removeDraftReviewTarget\(currentTargets, stagedDraftKey\)/u,
  'A delayed Habitat stage must not erase the review target for a newer draft value.'
);
assert.match(
  habitatCoordinates,
  /const reviewNextDraft = async \(\) => \{[\s\S]*?loadQuery\(nextDraftTarget\.query, \{\s*preserveSearchDraft: true\s*\}\)/u,
  'Habitat Coordinates Review Next Draft must restore the retained draft query without clearing the current search draft.'
);
assert.match(
  habitatCoordinates,
  /const unavailableDraftKeys = outstandingDraftKeys\.filter\([\s\S]*?!draftReviewTargets\[draftKey\][\s\S]*?confirmedUnavailableDraftKeys\.has\(draftKey\)/u,
  'Habitat Coordinates must keep retained keys visible when their exact review metadata or source row is unavailable.'
);
assert.match(
  habitatCoordinates,
  /const isPendingDraftCurrent =[\s\S]*?Object\.hasOwn\(coordinateDraftsRef\.current, pendingTarget\.draftKey\)[\s\S]*?if \(isPendingDraftCurrent\) \{[\s\S]*?addSetValue\(currentKeys, pendingTarget\.draftKey\)/u,
  'A completed exact Habitat query may mark only the still-current retained draft unavailable when its row is absent.'
);
assert.match(
  habitatCoordinates,
  /reviewLoadPendingRef\.current[\s\S]*?reviewLoadPendingRef\.current = true;[\s\S]*?finally \{\s*reviewLoadPendingRef\.current = false;/u,
  'Habitat Review Next Draft must synchronously admit only one exact-query review at a time.'
);
assert.match(
  habitatCoordinates,
  /const capturedUnavailableDraftKeys = \[\.\.\.unavailableDraftKeys\];[\s\S]*?confirmDiscardUnavailable\.habitatCoordinates[\s\S]*?removeRecordKeys\(currentDrafts, capturedUnavailableDraftKeySet\)[\s\S]*?removeRecordKeys\(currentTargets, capturedUnavailableDraftKeySet\)/u,
  'Habitat unavailable-draft recovery must require confirmation and remove only the captured unavailable keys.'
);
assert.match(
  habitatCoordinates,
  /editorDrafts\.summary\.unavailable[\s\S]*?editorDrafts\.discardUnavailable/u,
  'Habitat Coordinates must surface unavailable draft counts and an explicit recovery action.'
);
assert.match(
  habitatCoordinates,
  /getElementById\('habitat-coordinate-value'\)\?\.focus\(\{ preventScroll: true \}\)/u,
  'Habitat Coordinates Review Next Draft must focus without scrolling the workspace.'
);
assert.doesNotMatch(
  habitatCoordinates,
  /<select\b[^>]*disabled=\{[^}]*\bisStaging\b[^}]*\}[^>]*id="habitat-coordinate-value"/u,
  'Habitat Coordinates must remain editable while an earlier coordinate is staging.'
);
assert.doesNotMatch(
  habitatCoordinates,
  /<input\b(?=[^>]*id="habitat-coordinate-search")(?=[^>]*disabled=\{[^}]*\bisLoading\b)[^>]*>/u,
  'Habitat Coordinates search text must remain editable while an earlier query is loading.'
);
assert.doesNotMatch(
  habitatCoordinates,
  /<select\b(?=[^>]*id="habitat-coordinate-value")(?=[^>]*disabled=\{[^}]*\bisLoading\b)[^>]*>/u,
  'Habitat Coordinates coordinate choice must remain editable while an earlier query is loading.'
);
const habitatRecord = {
  binding: {
    devNo: 25,
    formNo: 0,
    outerGroupOccurrence: 3,
    rowOccurrence: 7,
    sourceFile: 'world/data/example.bin',
    sourceRevision: 'a'.repeat(64)
  }
};
const habitatKeyA = createHabitatCoordinateDraftKey('paldea', habitatRecord);
const habitatKeyAfterRefresh = createHabitatCoordinateDraftKey('paldea', {
  ...habitatRecord,
  binding: {
    ...habitatRecord.binding,
    rowPreimageSha256: 'b'.repeat(64),
    sourceRevision: 'c'.repeat(64)
  }
});
assert.equal(
  habitatKeyAfterRefresh,
  habitatKeyA,
  'Habitat draft identity must remain stable when a workflow refresh changes source metadata.'
);
const habitatKeyB = createHabitatCoordinateDraftKey('kitakami', habitatRecord);
let habitatDrafts = setHabitatCoordinateDraftValue({}, habitatKeyA, '6,16', '5,16');
habitatDrafts = setHabitatCoordinateDraftValue(habitatDrafts, habitatKeyB, '8,9', '7,9');
const habitatChangedWhileStaging = setHabitatCoordinateDraftValue(
  habitatDrafts,
  habitatKeyA,
  '7,17',
  '5,16'
);
assert.equal(
  clearStagedHabitatCoordinateDraftValue(
    habitatChangedWhileStaging,
    habitatKeyA,
    '6,16'
  )[habitatKeyA],
  '7,17',
  'A delayed Habitat stage must not erase a newer coordinate for the same row.'
);
habitatDrafts = clearStagedHabitatCoordinateDraftValue(
  habitatDrafts,
  habitatKeyA,
  '6,16'
);
assert.deepEqual(
  habitatDrafts,
  { [habitatKeyB]: '8,9' },
  'A successful Habitat stage must preserve unrelated row and region drafts.'
);

const trainerIdentity = read('src/features/trainers/ZaTrainerIdentityActions.tsx');
assert.doesNotMatch(
  trainerIdentity,
  /setSelectedPairId\(trainer\.classPairId/u,
  'Trainer Identity must not erase an un-staged class-pair choice on source refresh.'
);
assert.match(
  trainerIdentity,
  /effectiveDraftIdentityKey = trainer\.trainerId\.toString\(\)/u,
  'Trainer Identity drafts must use the stable trainer ID.'
);
assert.match(
  trainerIdentity,
  /clearStagedTrainerIdentityDraftValue\([\s\S]*?stagedDraftKey,[\s\S]*?stagedPairId/u,
  'Trainer Identity must clear only the exact class-pair draft staged successfully.'
);
assert.doesNotMatch(
  trainerIdentity,
  /<select\b[^>]*disabled=\{[^}]*\bisUpdating\b[^}]*\}[^>]*id="za-trainer-class-pair"/u,
  'Trainer Identity must remain editable while an earlier class choice is staging.'
);
let trainerIdentityDrafts = setTrainerIdentityDraftValue({}, '2983', 'pair-b', 'pair-a');
trainerIdentityDrafts = setTrainerIdentityDraftValue(
  trainerIdentityDrafts,
  '3301',
  'pair-d',
  'pair-c'
);
const trainerChangedWhileStaging = setTrainerIdentityDraftValue(
  trainerIdentityDrafts,
  '2983',
  'pair-e',
  'pair-a'
);
assert.equal(
  clearStagedTrainerIdentityDraftValue(
    trainerChangedWhileStaging,
    '2983',
    'pair-b'
  )['2983'],
  'pair-e',
  'A delayed class-pair stage must not erase a newer choice for the same trainer.'
);
trainerIdentityDrafts = clearStagedTrainerIdentityDraftValue(
  trainerIdentityDrafts,
  '2983',
  'pair-b'
);
assert.deepEqual(
  trainerIdentityDrafts,
  { '3301': 'pair-d' },
  'A successful class-pair stage must preserve another trainer draft.'
);

const researchAnnotations = read('src/features/research-lab/ResearchAnnotationsView.tsx');
assert.doesNotMatch(
  researchAnnotations,
  /previousDocumentIdentityRef|setTags\(''\)[\s\S]*?setText\(''\)/u,
  'Research Annotations must not erase unsaved text when its document ETag changes.'
);
assert.match(
  researchAnnotations,
  /clearSavedResearchAnnotationEditorDraft\([\s\S]*?draftKey,[\s\S]*?stagedEditorDraft/u,
  'Research Annotations must clear only the exact draft saved successfully.'
);
assert.match(
  researchAnnotations,
  /onDirtyStateChange\?\.\(Object\.keys\(editorDrafts\)\.length > 0\)/u,
  'Research Annotations must expose its keyed draft state to its owning workspace.'
);
assert.match(
  researchAnnotations,
  /outstandingDraftKeys = Object\.keys\(editorDrafts\)[\s\S]*?getNextOutstandingEditorDraftKey\(outstandingDraftKeys, draftKey\)/u,
  'Research Annotations must expose an aggregate count and deterministic next retained draft.'
);
const researchReviewNextDraft = between(
  researchAnnotations,
  'const reviewNextDraft = () => {',
  '\n\n  return ('
);
assert.match(
  researchReviewNextDraft,
  /nextDraft\.annotationId[\s\S]*?setEditingId\(nextDraft\.annotationId\)[\s\S]*?setReviewedDraftKey\(nextDraftKey\)/u,
  'Research Annotations must make both saved-annotation and new-target drafts reachable.'
);
assert.doesNotMatch(
  researchReviewNextDraft,
  /setEditorDrafts|upsertAnnotation|discardResearchAnnotationEditorDraft|clearSavedResearchAnnotationEditorDraft/u,
  'Reviewing another Research Annotation draft must never save, clear, or discard draft state.'
);
assert.doesNotMatch(
  researchAnnotations,
  /<(?:input|textarea)\b[^>]*disabled=\{controller\.annotations\.isSaving\}[^>]*>/u,
  'Research Annotation text and tags must remain editable while an earlier snapshot saves.'
);
assert.match(
  researchAnnotations,
  /const submittedDraftIsStillCurrent =[\s\S]*?if \(!submittedDraftIsStillCurrent\) \{\s*return;\s*\}\s*setEditingId\(null\)/u,
  'Research Annotations must remain open when a newer edit supersedes an in-flight save.'
);
const researchDeleteAnnotation = between(
  researchAnnotations,
  'const deleteAnnotation = async (annotationId: string) => {',
  '\n  const outstandingDraftKeys ='
);
assert.match(
  researchDeleteAnnotation,
  /const submittedEditorDraft = editorDraftsRef\.current\[deletedDraftKey\];[\s\S]*?await controller\.deleteAnnotation\(annotationId\)/u,
  'Research Annotation delete must capture its exact draft snapshot before the async request.'
);
assert.match(
  researchDeleteAnnotation,
  /clearSavedResearchAnnotationEditorDraft\([\s\S]*?latestDrafts,[\s\S]*?deletedDraftKey,[\s\S]*?submittedEditorDraft/u,
  'Research Annotation delete must clear only the unchanged snapshot that existed when deletion began.'
);
assert.match(
  researchDeleteAnnotation,
  /const newerDraft = resolvedDrafts === latestDrafts[\s\S]*?\[deletedDraftKey\]: \{[\s\S]*?\.\.\.newerDraft,[\s\S]*?annotationId: null/u,
  'Text typed during deletion must be preserved as a new-annotation draft for the retained target.'
);
assert.match(
  researchDeleteAnnotation,
  /setEditingId\(null\);[\s\S]*?setReviewedDraftKey\(newerDraft \? deletedDraftKey : null\)/u,
  'A newer draft must remain selected after its source annotation is deleted.'
);
assert.doesNotMatch(
  researchDeleteAnnotation,
  /discardResearchAnnotationEditorDraft\(/u,
  'Async annotation delete must not unconditionally discard the latest keyed draft.'
);
const researchTarget = { kind: 'semanticRecord' };
const researchDraftA = {
  annotationId: 'annotation-a',
  tags: 'balance, note',
  target: researchTarget,
  text: 'Unsaved analysis'
};
const researchDraftB = {
  annotationId: 'annotation-b',
  tags: '',
  target: researchTarget,
  text: 'Another draft'
};
let researchDrafts = setResearchAnnotationEditorDraft(
  {},
  'annotation:annotation-a',
  researchDraftA,
  { tags: '', text: 'Original' }
);
researchDrafts = setResearchAnnotationEditorDraft(
  researchDrafts,
  'annotation:annotation-b',
  researchDraftB,
  { tags: '', text: 'Other original' }
);
const researchChangedWhileSaving = setResearchAnnotationEditorDraft(
  researchDrafts,
  'annotation:annotation-a',
  { ...researchDraftA, text: 'Newer analysis' },
  { tags: '', text: 'Original' }
);
assert.equal(
  clearSavedResearchAnnotationEditorDraft(
    researchChangedWhileSaving,
    'annotation:annotation-a',
    researchDraftA
  )['annotation:annotation-a']?.text,
  'Newer analysis',
  'A delayed annotation save must not erase newer text for the same target.'
);
researchDrafts = clearSavedResearchAnnotationEditorDraft(
  researchDrafts,
  'annotation:annotation-a',
  researchDraftA
);
assert.deepEqual(
  researchDrafts,
  { 'annotation:annotation-b': researchDraftB },
  'Saving one annotation must preserve another annotation draft.'
);
assert.deepEqual(
  discardResearchAnnotationEditorDraft(
    researchDrafts,
    'annotation:annotation-b'
  ),
  {},
  'Explicit annotation cancel must discard only the active draft.'
);

const researchComparison = read('src/features/research-lab/ResearchComparisonView.tsx');
assert.doesNotMatch(
  researchComparison,
  /disabled=\{controller\.isBusy \|\| selectedPaths\.length === 0\}/u,
  'Research Comparison must let users clear or replace a selection while an earlier comparison is loading.'
);

const exactSnapshotEquality = (left, right) =>
  left.value === right.value && left.note === right.note;
const submittedKeyedDraft = { note: 'submitted', value: '25' };
const unrelatedKeyedDraft = { note: 'keep', value: '30' };
const exactSnapshotDrafts = {
  active: submittedKeyedDraft,
  unrelated: unrelatedKeyedDraft
};
assert.deepEqual(
  clearSubmittedKeyedEditorDraft(
    exactSnapshotDrafts,
    'active',
    submittedKeyedDraft,
    exactSnapshotEquality
  ),
  { unrelated: unrelatedKeyedDraft },
  'A successful async operation must clear only its exact submitted keyed draft.'
);
const changedKeyedDraft = { note: 'newer edit', value: '26' };
const draftsChangedWhileSaving = {
  ...exactSnapshotDrafts,
  active: changedKeyedDraft
};
assert.strictEqual(
  clearSubmittedKeyedEditorDraft(
    draftsChangedWhileSaving,
    'active',
    submittedKeyedDraft,
    exactSnapshotEquality
  ),
  draftsChangedWhileSaving,
  'A successful async operation must retain the original collection when its keyed draft changed.'
);
const replacementDraft = { note: 'remaining local work', value: '27' };
assert.deepEqual(
  resolveSubmittedKeyedEditorDraft(
    exactSnapshotDrafts,
    'active',
    submittedKeyedDraft,
    replacementDraft,
    exactSnapshotEquality
  ),
  { active: replacementDraft, unrelated: unrelatedKeyedDraft },
  'A successful partial operation may replace only its exact submitted keyed draft.'
);
const localEditorDraftState = read('src/components/localEditorDraftState.ts');
assert.match(
  localEditorDraftState,
  /if \(latestDraft === undefined \|\| !equals\(latestDraft, submittedDraft\)\) \{\s*return drafts;/u,
  'The shared async draft resolver must leave a newer keyed draft untouched.'
);
assert.match(
  localEditorDraftState,
  /if \(resolvedDraft === undefined\) \{\s*delete nextDrafts\[normalizedKey\];/u,
  'The shared async draft resolver must delete only the exact submitted key.'
);

const styles = read('src/styles.css');
assert.match(styles, /\.wide-panel \{\s*grid-column: 1 \/ -1;\s*\}/u);
assert.match(
  styles,
  /\.focused-editor-workspace > \* \{[\s\S]*?grid-column: 1 \/ -1;[\s\S]*?inline-size: 100%;[\s\S]*?max-inline-size: none;/u,
  'Focused editor children must use the complete DPI-adjusted workspace width.'
);
assert.match(
  styles,
  /\.focused-editor-workspace :where\(button\):not\(:disabled, \[aria-disabled='true'\]\) \{\s*cursor: pointer;/u,
  'Every focused editor button must share the interactive cursor contract.'
);
assert.doesNotMatch(
  styles,
  /--focused-editor-browser-width|max-width:\s*var\(--focused-editor-browser-width\)/u,
  'Focused editor browsers and tables must use the complete available width instead of a fixed cap.'
);
assert.match(
  styles,
  /\.editor-layout-focused :is\([\s\S]*?\) \{\s*width: 100%;\s*max-width: none;\s*justify-self: stretch;/u,
  'Focused editor browser surfaces must stretch across their responsive workspace column.'
);
assert.match(
  styles,
  /\.editor-layout-focused \.pokemon-table \{[\s\S]*?width: 100%;\s*max-width: none;\s*justify-self: stretch;/u,
  'The focused Pokemon browser must stretch across its responsive workspace column.'
);

const workflowPanels = read('src/components/workflowPanels.tsx');
assert.match(
  workflowPanels,
  /export function DiagnosticsSection[\s\S]*?usePublishCommonEditorDiagnostics\(diagnostics\);[\s\S]*?return null;/u,
  'Every shared DiagnosticsSection must publish its failures to the common bottom diagnostics.'
);
assert.match(
  workflowPanels,
  /function CommonBottomDiagnosticsSection[\s\S]*?mergeEditorDiagnostics\(diagnostics, publishedDiagnostics\)[\s\S]*?<DiagnosticsPanel/u,
  'The single common bottom presentation must merge editor-local diagnostics without recursively publishing itself.'
);
assert.match(
  workflowPanels,
  /function DiagnosticsPanel[\s\S]*?const headingId = useId\(\);[\s\S]*?aria-labelledby=\{headingId\} className="panel wide-panel"[\s\S]*?<h2 id=\{headingId\}>/u,
  'Every shared Diagnostics instance must use a unique accessible heading in the full-width panel.'
);
assert.doesNotMatch(
  workflowPanels,
  /(?:aria-labelledby|id)="diagnostics-heading"/u,
  'Diagnostics instances must not reuse a document-wide static heading ID.'
);

const commonEditorDiagnostics = read('src/components/CommonEditorDiagnostics.tsx');
assert.match(
  commonEditorDiagnostics,
  /export function CommonEditorDiagnosticsProvider[\s\S]*?mergeEditorDiagnostics\(\.\.\.diagnosticsBySource\.values\(\)\)/u,
  'The common diagnostics provider must retain and deduplicate every mounted editor source.'
);
assert.match(
  commonEditorDiagnostics,
  /useEffect\(\(\) => \{[\s\S]*?registration\.publish\(sourceId, latestDiagnosticsRef\.current\);[\s\S]*?return \(\) => registration\.withdraw\(sourceId\);/u,
  'Published editor diagnostics must register on mount and withdraw on unmount or replacement.'
);
assert.match(
  app,
  /<CommonEditorDiagnosticsProvider>[\s\S]*?<main\b[\s\S]*?<CommonBottomDiagnosticsSection diagnostics=\{bottomDiagnostics\} \/>[\s\S]*?<\/main>[\s\S]*?<\/CommonEditorDiagnosticsProvider>/u,
  'The app must keep one common diagnostics provider around the editor stack and its bottom diagnostics.'
);
assert.match(
  app,
  /const bottomDiagnostics = useMemo\([\s\S]*?\.\.\.bridgeDiagnostics,[\s\S]*?\.\.\.editValidationDiagnostics/u,
  'The common bottom diagnostics must retain bridge and local edit-validation failures.'
);
assert.doesNotMatch(
  app,
  /getOrdinaryEditorDraftDiagnostics/u,
  'Removed ordinary draft persistence must not inject storage lifecycle failures into bottom diagnostics.'
);
assert.doesNotMatch(
  app,
  /activeSection !== 'health' \? \([\s\S]{0,1500}?<CommonBottomDiagnosticsSection/u,
  'The common bottom diagnostics must not disappear on Project Setup.'
);
const workProgressModal = findFunctionLike(appSourceFile, 'WorkProgressModal');
assert.ok(workProgressModal, 'The shared work progress modal must exist.');
const workProgressModalText = workProgressModal.getText(appSourceFile);
assert.match(
  workProgressModalText,
  /<LoadingProgress[\s\S]*?label=\{progress\.detail\}/u,
  'Long-running output must use KM Editor\'s shared accessible progress presentation.'
);
assert.match(
  workProgressModalText,
  /status-pill status-pill-info[\s\S]*?Phase \{progress\.step\} of \{progress\.totalSteps\}/u,
  'Long-running output must expose its current phase in the established status pill.'
);
assert.match(
  workProgressModalText,
  /changes-progress work-progress-phases/u,
  'Long-running output phases must use the established KM progress-step cards.'
);
const publishedInlineEditorError = findFunctionLike(appSourceFile, 'PublishedInlineEditorError');
assert.ok(
  publishedInlineEditorError &&
    /usePublishCommonEditorDiagnostics\(diagnostics\)/u.test(
      publishedInlineEditorError.getText(appSourceFile)
    ),
  'Ordinary editor stage failures must publish through the common diagnostics channel.'
);
assert.equal(
  [...app.matchAll(/<PublishedInlineEditorError\b/gu)].length,
  5,
  'Every ordinary editor must publish its explicit Stage failure to the common diagnostics.'
);
assert.match(
  app,
  /const learnsetClipboardDiagnostics = useMemo<ApiDiagnostic\[\]>[\s\S]*?usePublishCommonEditorDiagnostics\(learnsetClipboardDiagnostics\);/u,
  'Pokemon learnset clipboard failures must publish to the common bottom diagnostics.'
);

function assertFunctionPublishesDiagnostics(relativePath, functionName, patterns) {
  const source = read(relativePath);
  const sourceFile = ts.createSourceFile(
    relativePath,
    source,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TSX
  );
  const owner = findFunctionLike(sourceFile, functionName);
  assert.ok(owner, `${relativePath} is missing diagnostics owner ${functionName}.`);
  const ownerSource = owner.getText(sourceFile);
  for (const [pattern, message] of patterns) {
    assert.match(ownerSource, pattern, `${relativePath}:${functionName} ${message}`);
  }
}

const diagnosticPublisherCoverage = [
  [
    'src/features/fashion-catalog/FashionCatalogSection.tsx',
    'FashionCatalogSection',
    [
      [/field: field\?\.field \?\? 'value',[\s\S]*?message: numericDraftError/u,
        'must publish invalid numeric field drafts.'],
      [/aria-invalid=\{numericDraftError \? true : undefined\}[\s\S]*?role="alert"/u,
        'must retain inline invalid numeric field feedback.'],
      [/field: 'stage',[\s\S]*?feedback\?\.kind === 'error'/u,
        'must publish failed stage actions.']
    ]
  ],
  [
    'src/App.tsx',
    'SelectedEncounterPanel',
    [
      [/usePublishCommonEditorDiagnostics\(encounterClipboardDiagnostics\)/u,
        'must publish encounter clipboard failures.'],
      [/encounterClipboardFeedback[\s\S]*?role=\{encounterClipboardFeedback\.isError \? 'alert' : 'status'\}/u,
        'must retain inline encounter clipboard feedback.']
    ]
  ],
  [
    'src/features/balance-lab/BalanceLabSection.tsx',
    'DiagnosticList',
    [[/usePublishCommonEditorDiagnostics\(diagnostics\)/u,
      'must publish its custom API diagnostic list.']]
  ],
  [
    'src/features/balance-lab/BalanceLabSection.tsx',
    'BalanceLabStatusPanel',
    [[/usePublishCommonEditorError\([\s\S]*?kind === 'error' \? label : null/u,
      'must publish an initial load failure.']]
  ],
  [
    'src/features/balance-lab/BalanceLabSection.tsx',
    'InlineError',
    [[/usePublishCommonEditorError\(\{ domain: 'analysis\.balanceLab', message \}\)/u,
      'must publish a refresh or continuation failure.']]
  ],
  [
    'src/features/guided-design/GuidedDesignSection.tsx',
    'DiagnosticList',
    [[/usePublishCommonEditorDiagnostics\(diagnostics\)/u,
      'must publish preview, import, and receipt diagnostics.']]
  ],
  [
    'src/features/guided-design/GuidedDesignSection.tsx',
    'StatusPanel',
    [[/usePublishCommonEditorError\([\s\S]*?message: errorMessage/u,
      'must publish an initial query failure.']]
  ],
  [
    'src/features/guided-design/GuidedDesignSection.tsx',
    'InlineError',
    [[/usePublishCommonEditorError\(\{ domain: 'analysis\.guidedDesign', message \}\)/u,
      'must publish a refresh or preview failure.']]
  ],
  [
    'src/features/game-modules/GameModuleResults.tsx',
    'GameModuleDiagnostics',
    [
      [/usePublishCommonEditorDiagnostics\(diagnostics\)/u,
        'must publish module query diagnostics.'],
      [/const headingId = useId\(\)[\s\S]*?aria-labelledby=\{headingId\}[\s\S]*?id=\{headingId\}/u,
        'must give every repeated module diagnostic block a unique heading ID.']
    ]
  ],
  [
    'src/features/game-modules/GameModulesSection.tsx',
    'StatusPanel',
    [[/usePublishCommonEditorError\([\s\S]*?message: errorMessage/u,
      'must publish an initial module load failure.']]
  ],
  [
    'src/features/game-modules/GameModulesSection.tsx',
    'InlineError',
    [[/usePublishCommonEditorError\(\{ domain: 'analysis\.gameModules', message \}\)/u,
      'must publish a module refresh or continuation failure.']]
  ],
  [
    'src/features/game-modules/GameModulesRuntime.tsx',
    'GameModulesRuntime',
    [[/<PublishCommonEditorError[\s\S]*?domain="analysis\.gameModules"/u,
      'must publish capability bootstrap failures.']]
  ],
  [
    'src/features/semantic-merge/SemanticMergeSection.tsx',
    'Diagnostics',
    [[/usePublishCommonEditorDiagnostics\(diagnostics\)/u,
      'must publish merge and recipe preview diagnostics.']]
  ],
  [
    'src/features/semantic-merge/SemanticMergeSection.tsx',
    'QueryError',
    [[/<PublishCommonEditorError[\s\S]*?field="query"/u,
      'must publish source, preview, export, and validation request failures.']]
  ],
  [
    'src/features/semantic-merge/SemanticMergeSection.tsx',
    'MergeSurface',
    [[/<PublishCommonEditorError[\s\S]*?field="proposalImport"/u,
      'must publish invalid merge proposal imports.']]
  ],
  [
    'src/features/semantic-merge/SemanticMergeSection.tsx',
    'RecipeSurface',
    [
      [/<PublishCommonEditorError[\s\S]*?field="recipeFile"/u,
        'must publish invalid recipe files.'],
      [/<PublishCommonEditorError[\s\S]*?field="recipeImport"/u,
        'must publish invalid recipe proposal imports.']
    ]
  ],
  [
    'src/features/semantic-merge/SemanticMergeSection.tsx',
    'RecipeArtifactCard',
    [
      [/field: 'recipeExport',[\s\S]*?status === 'error'/u,
        'must publish recipe copy and download failures.'],
      [/role=\{status === 'error' \? 'alert' : 'status'\}/u,
        'must retain inline recipe copy and download feedback.']
    ]
  ],
  [
    'src/App.tsx',
    'FpsPatchDiagnosticList',
    [[/usePublishCommonEditorDiagnostics\(diagnostics\)/u,
      'must publish nested FPS input and restore diagnostics.']]
  ],
  [
    'src/features/randomizer/RandomizerSection.tsx',
    'RandomizerSection',
    [
      [/field: 'seedClipboard',[\s\S]*?copySeedStatus === 'failed'/u,
        'must publish seed clipboard failures.'],
      [/copySeedStatus === 'failed'[\s\S]*?'Copy Failed'/u,
        'must retain inline seed clipboard feedback.']
    ]
  ],
  [
    'src/features/settings/PerformanceDiagnosticsPanel.tsx',
    'PerformanceDiagnosticsPanel',
    [
      [/field: 'clipboard',[\s\S]*?copyState === 'failed'/u,
        'must publish performance-summary clipboard failures.'],
      [/<p aria-live="polite" className="km-settings-status">[\s\S]*?copyState === 'failed'/u,
        'must retain inline performance-summary clipboard feedback.']
    ]
  ],
  [
    'src/features/output-safety/OutputSafetyPanel.tsx',
    'OutputSafetyPanel',
    [
      [/field: 'supportReportClipboard',[\s\S]*?supportReportCopyState === 'failed'/u,
        'must publish support-report clipboard failures.'],
      [/\.catch\(\(\) => setSupportReportCopyState\('failed'\)\)/u,
        'must retain rejected clipboard writes instead of swallowing them.'],
      [/supportReportCopyState === 'failed'[\s\S]*?translateLiteral\('The support report could not be copied\.'\)/u,
        'must retain inline support-report clipboard feedback.']
    ]
  ],
  [
    'src/App.tsx',
    'UpdateStatusDiagnostics',
    [
      [/status\.kind === 'error'[\s\S]*?field: 'check'/u,
        'must publish update failures.'],
      [/update\?\.kind === 'releasePage'[\s\S]*?field: 'nativeFallback'[\s\S]*?update\.fallbackReason/u,
        'must publish native-update fallback diagnostics.']
    ]
  ],
  [
    'src/App.tsx',
    'SettingsSection',
    [
      [/field: 'cache',[\s\S]*?hasSvCacheRequestError/u,
        'must publish cache-settings failures.']
    ]
  ]
];
for (const [path, owner, patterns] of diagnosticPublisherCoverage) {
  assertFunctionPublishesDiagnostics(path, owner, patterns);
}

assert.match(
  app,
  /advancedAuthoringHistorySyncError && activeSection === 'changes'[\s\S]*?<PublishCommonEditorError[\s\S]*?field="advancedAuthoringHistory"/u,
  'Advanced Authoring history synchronization failures must reach common bottom diagnostics.'
);
assert.match(
  app,
  /catch \{\s*setAdvancedAuthoringHistorySyncError\([\s\S]{0,500}?controller\.syncStagedHistory\(null\);/u,
  'Advanced Authoring must not silently discard an invalid staged-history snapshot.'
);
assert.match(
  app,
  /personalWorkspaceError && activeSection === 'workbench'[\s\S]*?<PublishCommonEditorError[\s\S]*?field="storage"/u,
  'Workbench personal-state failures must reach common bottom diagnostics.'
);

const localValidationPublisher = findFunctionLike(
  appSourceFile,
  'PublishedLocalEditorValidationDiagnostics'
);
assert.ok(
  localValidationPublisher &&
    /usePublishCommonEditorDiagnostics\(diagnostics\)/u.test(
      localValidationPublisher.getText(appSourceFile)
    ),
  'Local draft validation must publish through the common diagnostics channel.'
);
const appLocalValidationOwners = {
  items: ['SelectedItemPanel', 'workflow.items'],
  pokemon: ['SelectedPokemonPanel', 'workflow.pokemon'],
  moves: ['SelectedMovePanel', 'workflow.moves'],
  text: ['SelectedTextPanel', 'workflow.text'],
  trainers: ['SelectedTrainerPanel', 'workflow.trainers'],
  giftPokemon: ['SelectedGiftPokemonPanel', 'workflow.giftPokemon'],
  tradePokemon: ['SelectedTradePokemonPanel', 'workflow.tradePokemon'],
  rentalPokemon: ['SelectedRentalPokemonPanel', 'workflow.rentalPokemon'],
  dynamaxAdventures: ['SelectedDynamaxAdventurePanel', 'workflow.dynamaxAdventures'],
  staticEncounters: ['SelectedStaticEncounterPanel', 'workflow.staticEncounters'],
  shops: ['SelectedShopPanel', 'workflow.shops'],
  encounters: ['SelectedEncounterPanel', 'workflow.encounters'],
  encounterPlayerPartner: ['ZaEncounterPlayerPartnerEditor', 'workflow.encounters'],
  teraRaids: ['TeraRaidDraftPanel', 'workflow.teraRaids'],
  raidBattles: ['SelectedRaidBattlePanel', 'workflow.raidBattles'],
  raidRewards: ['SelectedRaidRewardPanel', 'workflow.raidRewards'],
  behavior: ['SelectedBehaviorPanel', 'workflow.behavior'],
  placement: ['SelectedPlacementPanel', 'workflow.placement'],
  hyperTraining: ['HyperTrainingSection', 'workflow.hyperTraining'],
  catchCap: ['CatchCapSection', 'workflow.catchCap'],
  royalCandy: ['SelectedRoyalCandyPanel', 'workflow.royalCandy'],
  startingItems: ['StartingItemsSection', 'workflow.startingItems']
};
for (const [surface, [owner, domain]] of Object.entries(appLocalValidationOwners)) {
  const ownerNode = findFunctionLike(appSourceFile, owner);
  assert.ok(ownerNode, `${surface} is missing local validation owner ${owner}.`);
  const ownerSource = ownerNode.getText(appSourceFile);
  assert.match(
    ownerSource,
    new RegExp(
      `<PublishedLocalEditorValidationDiagnostics\\b[\\s\\S]*?domain="${domain.replaceAll('.', '\\.')}"`,
      'u'
    ),
    `${surface} must publish invalid local drafts to the common bottom diagnostics.`
  );
}

for (const [surface, owner] of Object.entries({
  items: 'SelectedItemPanel',
  pokemon: 'SelectedPokemonPanel',
  moves: 'SelectedMovePanel',
  trainers: 'SelectedTrainerPanel',
  giftPokemon: 'SelectedGiftPokemonPanel',
  tradePokemon: 'SelectedTradePokemonPanel',
  rentalPokemon: 'SelectedRentalPokemonPanel',
  staticEncounters: 'SelectedStaticEncounterPanel',
  encounters: 'SelectedEncounterPanel',
  encounterPlayerPartner: 'ZaEncounterPlayerPartnerEditor',
  teraRaids: 'TeraRaidDraftPanel',
  raidBattles: 'SelectedRaidBattlePanel',
  raidRewards: 'SelectedRaidRewardPanel'
})) {
  const ownerNode = findFunctionLike(appSourceFile, owner);
  const ownerSource = ownerNode?.getText(appSourceFile) ?? '';
  assert.doesNotMatch(
    ownerSource,
    /<(?:TrainerDraftField|GiftPokemonDraftField|PokemonPersonalFieldInput|SearchableOptionInput)\b[\s\S]{0,1000}?disabled=\{[^}]*\b(?:isBusy|isStaging|is[A-Za-z]+Updating)\b[^}]*\}/u,
    `${surface} draft fields must remain editable while an earlier snapshot is being processed.`
  );
}

assert.match(
  trainerPools,
  /usePublishCommonEditorError\([\s\S]*?field: 'identitySwap'[\s\S]*?message: actionFeedback\?\.kind === 'error'/u,
  'Trainer Pool staging failures must publish to common bottom diagnostics.'
);
assert.match(
  trainerPools,
  /try \{[\s\S]*?await onStageSwap\([\s\S]*?\} catch(?: \([^)]*\))? \{[\s\S]*?didSucceed = false;[\s\S]*?kind: didSucceed \? 'success' : 'error'/u,
  'Trainer Pool staging must convert thrown bridge failures into reportable editor feedback.'
);
assert.match(
  changeSetWorkspace,
  /usePublishCommonEditorError\(\{\s*domain: 'workflow\.changeSets',\s*field: 'tags',\s*message: parsedTags \? null : t\('changeSets\.tagsInvalid'\)/u,
  'Change Set tag validation must publish to common bottom diagnostics.'
);
assert.match(
  dexLayout,
  /usePublishCommonEditorError\(\{\s*domain: 'workflow\.pokemon\.dexLayout',\s*field: 'sizes',\s*message:/u,
  'Dex size validation must publish to common bottom diagnostics.'
);
assert.match(
  dexLayout,
  /usePublishCommonEditorError\(\{\s*domain: 'workflow\.pokemon\.dexLayout',\s*field: 'destination',\s*message:/u,
  'Dex destination validation must publish to common bottom diagnostics.'
);
assert.match(
  read('src/features/shiny-rate/ShinyRateSection.tsx'),
  /usePublishCommonEditorError\(\{\s*domain: 'workflow\.shinyRate',\s*field: 'customDenominator',\s*message:\s*workflow !== null && customCalculation === null/u,
  'Shiny Rate denominator validation must publish to common bottom diagnostics only after its workflow loads.'
);

const inlineAlertPublishingExemptions = new Set([
  'src/components/ReportableErrorScreen.tsx'
]);
for (const path of sourceFilesUnder(sourceRoot)) {
  const relativePath = relative(desktopRoot, path).replaceAll('\\', '/');
  const source = readFileSync(path, 'utf8');
  if (
    inlineAlertPublishingExemptions.has(relativePath) ||
    !/role=(?:"alert"|'alert'|\{[^}]{0,100}(?:"alert"|'alert')[^}]*\})/u.test(source)
  ) {
    continue;
  }
  assert.match(
    source,
    /DiagnosticsSection|usePublishCommonEditor(?:Diagnostics|Error)|PublishCommonEditorError|PublishedInlineEditorError/u,
    `${relativePath} renders an inline error but does not publish editor failures to the common bottom diagnostics.`
  );
}

const duplicateDiagnostic = {
  code: null,
  domain: 'workflow.test',
  expected: null,
  field: 'value',
  file: null,
  message: 'The value is invalid.',
  severity: 'error'
};
const distinctDiagnostic = {
  ...duplicateDiagnostic,
  field: 'other',
  message: 'The other value is invalid.'
};
assert.deepEqual(
  mergeEditorDiagnostics(
    [duplicateDiagnostic],
    [duplicateDiagnostic, distinctDiagnostic]
  ),
  [duplicateDiagnostic, distinctDiagnostic],
  'Common bottom diagnostics must deduplicate a failure rendered by more than one editor surface.'
);
assert.equal(
  diagnosticListFingerprint([duplicateDiagnostic]),
  diagnosticListFingerprint([{ ...duplicateDiagnostic }]),
  'Diagnostic publishing must remain stable when a renderer creates a fresh equivalent array.'
);
