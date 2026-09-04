// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { globSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import {
  promoteRecentRecordTab,
  resolveRetainedRecordTabLabel
} from '../src/workbench/workspaceShellViewModels.ts';
import { getNextOutstandingEditorDraftKey } from '../src/components/localEditorDraftState.ts';

const desktopRoot = fileURLToPath(new URL('../', import.meta.url));

function read(relativePath) {
  return readFileSync(new URL(relativePath, `file:///${desktopRoot.replaceAll('\\', '/')}/`), 'utf8')
    .replace(/\r\n?/gu, '\n');
}

function between(source, start, end) {
  const startIndex = source.indexOf(start);
  const endIndex = source.indexOf(end, startIndex + start.length);
  assert.ok(startIndex >= 0, `Missing contract start: ${start}`);
  assert.ok(endIndex > startIndex, `Missing contract end: ${end}`);
  return source.slice(startIndex, endIndex);
}

function cssBlock(source, selector) {
  return between(source, `${selector} {`, '\n}');
}

const insertionOrderedDraftKeys = ['draft-third', 'draft-first', 'draft-second'];
assert.equal(
  getNextOutstandingEditorDraftKey(insertionOrderedDraftKeys, null),
  'draft-third',
  'Draft review without a current key must start at the first retained draft.'
);
assert.equal(
  getNextOutstandingEditorDraftKey(insertionOrderedDraftKeys, 'draft-third'),
  'draft-first',
  'Draft review must retain caller-provided insertion order.'
);
assert.equal(
  getNextOutstandingEditorDraftKey(insertionOrderedDraftKeys, 'draft-second'),
  'draft-third',
  'Draft review must wrap to the first retained draft.'
);
assert.equal(
  getNextOutstandingEditorDraftKey(insertionOrderedDraftKeys, 'not-retained'),
  'draft-third',
  'Draft review with a non-retained current key must start at the first retained draft.'
);
assert.equal(getNextOutstandingEditorDraftKey(['only-draft'], 'only-draft'), null);
assert.equal(getNextOutstandingEditorDraftKey([], null), null);

const app = read('src/App.tsx');
const coalescedTextInputState = read('src/components/useCoalescedTextInputState.ts');
const adaptiveInspector = read('src/features/workbench/AdaptiveInspector.tsx');
const fashionCatalog = read('src/features/fashion-catalog/FashionCatalogSection.tsx');
const styles = read('src/styles.css');
const tabRail = read('src/features/workbench/RecordTabRail.tsx');
const tabStyles = read('src/features/workbench/workbench.css');
const trainerIdentityActions = read('src/features/trainers/ZaTrainerIdentityActions.tsx');
const locationAdapters = read('src/workbench/locationAdapterRegistry.ts');
const navigationController = read('src/workbench/navigationController.ts');
assert.match(
  coalescedTextInputState,
  /pendingValueRef\.current = nextValue[\s\S]*?if \(queuedRef\.current\)[\s\S]*?queueMicrotask\([\s\S]*?setValue\(\(currentValue\) =>/u,
  'Same-task controlled text-input bursts must coalesce to the latest value before committing React state.'
);
assert.match(
  coalescedTextInputState,
  /lifecycleRevisionRef\.current \+= 1[\s\S]*?lifecycleRevision !== lifecycleRevisionRef\.current/u,
  'A queued text-input commit must be invalidated when its editor unmounts.'
);
assert.equal(
  [...app.matchAll(/const \[compatibilitySearchText, setCompatibilitySearchText\] =[\s\S]{0,80}?useCoalescedTextInputState\(\);/gu)].length,
  2,
  'Items and Pokemon compatibility searches must both use the shared burst-safe input state.'
);
assert.doesNotMatch(
  app,
  /const \[compatibilitySearchText, setCompatibilitySearchText\] = useState\(''\);/u,
  'Compatibility searches must not regress to an uncoalesced local state setter.'
);
for (const [section, handler] of [
  ['behavior', 'handleBehaviorSearchChange'],
  ['dynamaxAdventures', 'handleDynamaxAdventureSearchChange'],
  ['encounters', 'handleEncounterSearchChange'],
  ['exefsPatches', 'handleExeFsPatchSearchChange'],
  ['flagworkSave', 'handleFlagworkSaveSearchChange'],
  ['giftPokemon', 'handleGiftPokemonSearchChange'],
  ['placement', 'handlePlacementSearchChange'],
  ['raidBattles', 'handleRaidBattleSearchChange'],
  ['raidBonusRewards', 'handleRaidBonusRewardSearchChange'],
  ['raidRewards', 'handleRaidRewardSearchChange'],
  ['rentalPokemon', 'handleRentalPokemonSearchChange'],
  ['royalCandy', 'handleRoyalCandySearchChange'],
  ['shops', 'handleShopSearchChange'],
  ['staticEncounters', 'handleStaticEncounterSearchChange'],
  ['teraRaids', 'handleTeraRaidSearchChange'],
  ['tradePokemon', 'handleTradePokemonSearchChange']
]) {
  assert.match(
    app,
    new RegExp(
      `const ${handler} = useCallback\\([\\s\\S]*?commitOrdinaryEditorSearch\\('${section}'`,
      'u'
    ),
    `${section} search must share the latest-only ordinary-editor commit guard.`
  );
}
assert.match(
  app,
  /const handleSwShPlacementSearchChange = useCallback\([\s\S]*?commitOrdinaryEditorSearch\('placement',[\s\S]*?setPlacementSearchText\(value\)[\s\S]*?setSwShPlacementOffset\(0\)/u,
  'Paged Sword and Shield placement search must coalesce its query and page reset together.'
);
assert.doesNotMatch(
  app,
  /onSearchChange=\{set[A-Za-z]*SearchText\}/u,
  'No ordinary editor may bypass the shared latest-only search guard with a direct store setter.'
);
for (const [relativePath, expectedHookCalls] of [
  ['src/App.tsx', 2],
  ['src/components/SearchableOptionInput.tsx', 1],
  ['src/features/balance-lab/BalanceLabCharts.tsx', 1],
  ['src/features/change-sets/AdvancedAuthoringPanel.tsx', 1],
  ['src/features/change-sets/ChangeSetWorkspacePanel.tsx', 1],
  ['src/features/dex-layout/ZaDexLayoutSection.tsx', 1],
  ['src/features/fashion-catalog/FashionCatalogSection.tsx', 1],
  ['src/features/game-dump/GameDumpSection.tsx', 1],
  ['src/features/game-modules/GameModuleComparison.tsx', 1],
  ['src/features/guided-design/GuidedDesignSection.tsx', 1],
  ['src/features/habitat-coordinates/HabitatCoordinatesSection.tsx', 1],
  ['src/features/research-lab/ResearchCatalogViews.tsx', 1],
  ['src/features/research-lab/ResearchComparisonView.tsx', 1],
  ['src/features/semantic-explore/SemanticExploreSection.tsx', 4],
  ['src/features/semantic-merge/SemanticMergeSection.tsx', 3],
  ['src/features/workbench/CommandPalette.tsx', 1],
  ['src/features/workbench/ShortcutOverlay.tsx', 1],
  ['src/features/workflows/WorkflowsSection.tsx', 1]
]) {
  const source = relativePath === 'src/App.tsx' ? app : read(relativePath);
  assert.equal(
    [...source.matchAll(/\buseCoalescedTextInputState\(/gu)].length,
    expectedHookCalls,
    `${relativePath} must keep every audited local search on the shared burst-safe state.`
  );
}
const habitatCoordinates = read(
  'src/features/habitat-coordinates/HabitatCoordinatesSection.tsx'
);
assert.match(
  habitatCoordinates,
  /onChange=\{\(event\) => \{[\s\S]*?searchDraftRef\.current = event\.target\.value;[\s\S]*?setSearchDraft\(event\.target\.value\);/u,
  'Habitat search input must publish its newest value synchronously before the coalesced render.'
);
assert.match(
  habitatCoordinates,
  /const submitSearch = \(event: FormEvent\) => \{[\s\S]*?search: searchDraftRef\.current\.trim\(\)\.slice\(0, 80\)/u,
  'Habitat search submit must use the synchronously current value during same-task input bursts.'
);
const retainedDraftLocalizationResources = Object.fromEntries(
  ['en', 'de', 'es', 'fr', 'ru', 'uk', 'zh'].map((locale) => [
    locale,
    JSON.parse(read(`src/localization/resources/${locale}.json`)).keys
  ])
);
const retainedDraftLocalizationKeys = Object.keys(
  retainedDraftLocalizationResources.en
).filter((key) => key.startsWith('editorDrafts.')).sort();
const getLocalizationPlaceholders = (value) =>
  [...value.matchAll(/\{([A-Za-z][A-Za-z0-9]*)\}/gu)]
    .map((match) => match[1])
    .sort();
for (const [locale, resource] of Object.entries(retainedDraftLocalizationResources)) {
  assert.deepEqual(
    Object.keys(resource).filter((key) => key.startsWith('editorDrafts.')).sort(),
    retainedDraftLocalizationKeys,
    `${locale} retained-draft localization keys must match English.`
  );
  for (const key of retainedDraftLocalizationKeys) {
    assert.equal(typeof resource[key], 'string', `${locale} is missing ${key}.`);
    assert.ok(resource[key].trim(), `${locale} must not leave ${key} blank.`);
    assert.deepEqual(
      getLocalizationPlaceholders(resource[key]),
      getLocalizationPlaceholders(retainedDraftLocalizationResources.en[key]),
      `${locale} must preserve the placeholders in ${key}.`
    );
  }
}
const stagedChangesNavigation = between(
  navigationController,
  'const isMovingCleanStagedSessionToChanges =',
  'const hasDexLayoutBoundaryEdits ='
);
assert.match(
  stagedChangesNavigation,
  /destinationSection === 'changes'[\s\S]*?state\.pendingEditCount > 0[\s\S]*?!state\.activeEditorHasLocalDrafts/u,
  'A clean staged session must be recognized as safe to open in Changes.'
);
assert.match(
  stagedChangesNavigation,
  /activeSection === 'dexLayout' && isMovingCleanStagedSessionToChanges/u,
  'Dex Layout must retain its existing clean staged navigation exemption.'
);
assert.match(
  between(
    navigationController,
    'const isLeavingAdvancedEditorForChanges =',
    'if (isLeavingActiveEditSession || isLeavingAdvancedEditorForChanges)'
  ),
  /state\.activeSectionOwnsAdvancedEditSession[\s\S]*?!isMovingCleanStagedSessionToChanges/u,
  'Advanced editors must open clean staged changes directly without weakening local-draft protection.'
);
assert.match(
  navigationController,
  /if \(state\.activeSectionIsEditor && state\.activeEditorHasLocalDrafts\)[\s\S]*?kind: 'prompt'/u,
  'Genuinely unstaged editor input must remain protected when navigating away.'
);
const selectedPokemonPanel = between(
  app,
  'function SelectedPokemonPanel({',
  'function MovesSection({'
);
const pokemonDraftRecordAggregation = between(
  selectedPokemonPanel,
  'const outstandingPokemonDraftRecordKeys = useMemo(',
  'const nextPokemonDraftRecordKey = getNextOutstandingEditorDraftKey('
);
for (const retainedPokemonRegistry of [
  'personalDraftsByPokemonId',
  'alphaMoveDraftsByPokemonKey',
  'evolutionDraftsByPokemonId',
  'learnsetDraftsByPokemonId',
  'newEvolutionDraftsByPokemonId',
  'newLearnsetDraftsByPokemonId',
  'dexSwapDraftsByPokemonId'
]) {
  assert.match(
    pokemonDraftRecordAggregation,
    new RegExp(`Object\\.(?:keys|entries)\\(${retainedPokemonRegistry}\\)`, 'u'),
    `Pokemon draft aggregation must include ${retainedPokemonRegistry}.`
  );
}
assert.match(
  pokemonDraftRecordAggregation,
  /pokemonDraftKeyByAlphaMoveKey[\s\S]*?candidate\.speciesId[\s\S]*?candidate\.form[\s\S]*?candidate\.personalId/u,
  'Alpha-move draft identities must resolve safely back to a Pokemon personal record.'
);
assert.match(
  pokemonDraftRecordAggregation,
  /addedKeys\.has\(candidateKey\)[\s\S]*?addedKeys\.add\(candidateKey\)[\s\S]*?outstandingKeys\.push\(candidateKey\)/u,
  'Pokemon draft aggregation must count each Pokemon only once across embedded registries.'
);
assert.match(
  selectedPokemonPanel,
  /const nextPokemonDraftRecordKey = getNextOutstandingEditorDraftKey\([\s\S]*?outstandingPokemonDraftRecordKeys,[\s\S]*?pokemonDraftRecordKey/u,
  'Pokemon draft review must advance deterministically from the selected Pokemon.'
);
assert.match(
  selectedPokemonPanel,
  /t\('editorDrafts\.summary\.pokemon',[\s\S]*?count: outstandingPokemonDraftRecordKeys\.length/u,
  'Pokemon actions must report the aggregate outstanding Pokemon-record count.'
);
const pokemonDraftReviewAction = between(
  selectedPokemonPanel,
  '{nextPokemonDraftTarget ? (',
  ') : null}'
);
assert.match(pokemonDraftReviewAction, /editorDrafts\.reviewNext/u);
assert.match(
  pokemonDraftReviewAction,
  /onSelectPokemon\(nextPokemonDraftTarget\.personalId\)/u,
  'Pokemon draft review must navigate through the existing Pokemon selection callback.'
);
assert.doesNotMatch(
  pokemonDraftReviewAction,
  /\b(?:set[A-Za-z]*Draft[A-Za-z]*|onUpdate[A-Za-z]*|onStage[A-Za-z]*|cancelActiveEditSession)\s*\(/u,
  'Pokemon draft review must select only; it must not stage, clear, or discard drafts.'
);
const zaPokemonDexPlacementEditor = between(
  app,
  'function ZaPokemonDexPlacementEditor({',
  'function formatPokemonDexNumber('
);
assert.match(
  zaPokemonDexPlacementEditor,
  /draft: ZaPokemonDexSwapDraftSnapshot \| null[\s\S]*?onDraftChange: \(draft: ZaPokemonDexSwapDraftSnapshot \| null\) => void/u,
  'Z-A Pokedex placement input must be controlled by the parent keyed draft registry.'
);
assert.match(
  zaPokemonDexPlacementEditor,
  /retainedDraft = draft\?\.personalId === pokemon\.personalId[\s\S]*?retainedDraft\?\.destinationDexKind[\s\S]*?retainedDraft\?\.target/u,
  'Returning to a Pokemon must restore its retained Pokedex destination and target inputs.'
);
assert.doesNotMatch(
  zaPokemonDexPlacementEditor,
  /scopeChanged[\s\S]*?setTargetSpeciesIdDraft\(''\)|previousScopeRef/u,
  'Changing Pokemon must not reset a retained Pokedex placement draft.'
);
const zaPokemonDexStageAction = between(
  zaPokemonDexPlacementEditor,
  'const submittedDraft: ZaPokemonDexSwapDraftSnapshot =',
  'type="button"'
);
assert.match(
  zaPokemonDexStageAction,
  /isSubmittedDraftCurrent[\s\S]*?didStage && isSubmittedDraftCurrent\(\)[\s\S]*?onDraftChange\(null\)/u,
  'Pokedex staging may clear only the unchanged submitted keyed draft.'
);
assert.match(
  selectedPokemonPanel,
  /draft=\{selectedDexSwapDraft\}[\s\S]*?onDraftChange=\{updateSelectedDexSwapDraft\}/u,
  'The selected Pokemon must bind the Pokedex editor to its keyed parent draft.'
);
assert.match(
  selectedPokemonPanel,
  /setDexSwapDraftsByPokemonId\(\{\}\)/u,
  'Canceling the Pokemon edit session must explicitly clear all retained Pokedex swap drafts.'
);
assert.match(
  selectedPokemonPanel,
  /const learnsetRowAccessibleLabel = \[[\s\S]*?displayMove\.levelLabel[\s\S]*?displayMove\.masteryLabel[\s\S]*?displayMove\.moveName[\s\S]*?aria-label=\{learnsetRowAccessibleLabel\}/u,
  'Learnset row menu targets must retain their visible move and level details in the accessible name.'
);

const selectedTrainerPanel = between(
  app,
  'function SelectedTrainerPanel({',
  'function getPokemonStatTotalState('
);
assert.match(
  selectedTrainerPanel,
  /isEmptySlot \? \(\s*<span\s+aria-hidden="true"\s+className="trainer-party-sprite trainer-party-sprite-empty"\s*\/>\s*\) : \(\s*<PokemonSprite[\s\S]*?speciesId=\{projectedPokemon\.speciesId\}[\s\S]*?spriteName=\{projectedPokemon\.spriteName \?\? pokemonSpriteLabel\}[\s\S]*?\/>\s*\)/u,
  'Empty Trainer party slots must reserve blank space without resolving a Pokemon sprite.'
);
assert.match(
  selectedTrainerPanel,
  /const partyCardAccessibleLabel = \[[\s\S]*?pokemonLabel[\s\S]*?projectedPokemon\.level[\s\S]*?projectedHeldItemLabel[\s\S]*?hasCardDrafts[\s\S]*?aria-label=\{partyCardAccessibleLabel\}/u,
  'Trainer party targets must retain Pokemon, level, held-item, and draft details in the accessible name.'
);
assert.match(
  cssBlock(styles, '.trainer-party-sprite-empty'),
  /display:\s*block/u,
  'Empty Trainer party slot artwork must remain visually blank while preserving card alignment.'
);
const trainerAggregateStagePlan = between(
  selectedTrainerPanel,
  'const trainerStagePlan = useMemo(',
  'const canStageTrainerChanges ='
);
assert.match(
  trainerAggregateStagePlan,
  /for \(const targetTrainer of \[\.\.\.trainerRecords\]\.sort\([\s\S]*?trainerDraftsByTrainerId\[trainerKey\][\s\S]*?canonicalPokemonDraftsByTrainerSlot\[partyKey\]/u,
  'Trainer staging must aggregate retained Trainer and party drafts across every loaded Trainer.'
);
assert.match(
  trainerAggregateStagePlan,
  /updates: orderTrainerFieldUpdates\(updates\)/u,
  'Trainer staging must preserve deterministic aggregate update ordering.'
);
assert.match(
  selectedTrainerPanel,
  /t\('editorDrafts\.summary\.trainers',[\s\S]*?identityCount: outstandingTrainerIdentityDraftCount[\s\S]*?partyCount: outstandingPartySlotDraftCount[\s\S]*?scopeCount: outstandingTrainerDraftScopeCount[\s\S]*?trainerCount: outstandingTrainerDraftCount/u,
  'Trainer actions must expose exact aggregate counts for all retained draft scopes.'
);
const trainerAggregateStageAction = between(
  selectedTrainerPanel,
  'const stageTrainerDrafts = async () => {',
  'const stageTrainerMaxIvs = async () => {'
);
assert.match(
  trainerAggregateStageAction,
  /const submittedPlan = trainerStagePlan;[\s\S]*?onUpdateTrainerFields\(submittedPlan\.updates\)/u,
  'The single Trainer Stage action must submit the complete aggregate plan.'
);
assert.doesNotMatch(
  selectedTrainerPanel,
  /nextTrainerDraftTarget|onReviewDraftTarget|editorDrafts\.reviewNext/u,
  'Trainer actions must not require a separate Review Next Draft step before aggregate staging.'
);
assert.match(
  trainerIdentityActions,
  /onDraftKeysChange\?\.\(Object\.keys\(selectedPairIds\)\)/u,
  'Z-A trainer identity drafts must expose their exact retained trainer keys to the parent editor.'
);
assert.doesNotMatch(
  app,
  /handleReviewTrainerDraftLocation|onReviewTrainerDraftTarget/u,
  'The removed Trainer-only Review Next Draft navigation must not remain wired through App.'
);
const selectedEncounterClipboardPanel = between(
  app,
  'function SelectedEncounterPanel({',
  'function ZaEncounterPlayerPartnerEditor({'
);
assert.match(
  selectedEncounterClipboardPanel,
  /const encounterSlotAccessibleLabel = \[[\s\S]*?slotBadge[\s\S]*?slotLabel[\s\S]*?slot\.isAlpha[\s\S]*?slotCompletionStatus[\s\S]*?slotSummary[\s\S]*?aria-label=\{encounterSlotAccessibleLabel\}/u,
  'Encounter slot targets must retain species, badge, level, weight, and status details in the accessible name.'
);
assert.match(
  app,
  /isOpeningChangesWithLocalDraft[\s\S]*?editorDrafts\.openChanges\.title[\s\S]*?editorDrafts\.openChanges\.confirm/u,
  'Opening Changes with a genuine local draft must explain the exact discard boundary.'
);

for (const {
  countName,
  currentDraftKey,
  draftCollection,
  end,
  label,
  nextDraftKey,
  reviewGuard,
  selection,
  start,
  summaryKey
} of [
  {
    countName: 'outstandingGiftDraftCount',
    currentDraftKey: /gift\?\.giftIndex\.toString\(\)/u,
    draftCollection: 'giftDraftsByIndex',
    end: 'function TradePokemonSection({',
    label: 'gift',
    nextDraftKey: 'nextGiftDraftKey',
    reviewGuard: 'nextGiftDraftKey',
    selection: /onSelectGift\(Number\.parseInt\(nextGiftDraftKey, 10\)\)/u,
    start: 'function SelectedGiftPokemonPanel({',
    summaryKey: 'gifts'
  },
  {
    countName: 'outstandingTradeDraftCount',
    currentDraftKey: /trade\?\.tradeIndex\.toString\(\)/u,
    draftCollection: 'tradeDraftsByIndex',
    end: 'function RentalPokemonSection({',
    label: 'trade',
    nextDraftKey: 'nextTradeDraftKey',
    reviewGuard: 'nextTradeDraftKey',
    selection: /onSelectTrade\(Number\.parseInt\(nextTradeDraftKey, 10\)\)/u,
    start: 'function SelectedTradePokemonPanel({',
    summaryKey: 'trades'
  },
  {
    countName: 'outstandingRentalDraftCount',
    currentDraftKey: /rental\?\.rentalIndex\.toString\(\)/u,
    draftCollection: 'rentalDraftsByIndex',
    end: 'function DynamaxAdventuresSection({',
    label: 'rental',
    nextDraftKey: 'nextRentalDraftKey',
    reviewGuard: 'nextRentalDraftKey',
    selection: /onSelectRental\(Number\.parseInt\(nextRentalDraftKey, 10\)\)/u,
    start: 'function SelectedRentalPokemonPanel({',
    summaryKey: 'rentals'
  },
  {
    countName: 'outstandingAdventureDraftCount',
    currentDraftKey: /encounter\?\.entryIndex\.toString\(\)/u,
    draftCollection: 'draftsByEntryIndex',
    end: 'function StaticEncountersSection({',
    label: 'adventure',
    nextDraftKey: 'nextAdventureDraftKey',
    reviewGuard: 'nextAdventureDraftKey',
    selection: /onSelectAdventure\(Number\.parseInt\(nextAdventureDraftKey, 10\)\)/u,
    start: 'function SelectedDynamaxAdventurePanel({',
    summaryKey: 'adventures'
  },
  {
    countName: 'outstandingEncounterDraftCount',
    currentDraftKey: /encounterDraftKey/u,
    draftCollection: 'encounterDraftsByIndex',
    end: 'function ShopsSection({',
    label: 'encounter',
    nextDraftKey: 'nextEncounterDraftKey',
    reviewGuard: 'nextEncounterDraftIndex',
    selection: /onSelectEncounter\(nextEncounterDraftIndex\)/u,
    start: 'function SelectedStaticEncounterPanel({',
    summaryKey: 'staticEncounters'
  }
]) {
  const panel = between(app, start, end);
  assert.match(
    panel,
    new RegExp(
      `const ${countName} = countFieldDraftRecords\\(${draftCollection}\\);`,
      'u'
    ),
    `${label} drafts must expose an aggregate outstanding-record count.`
  );
  const nextDraftResolution = between(
    panel,
    `const ${nextDraftKey} = getNextOutstandingEditorDraftKey(`,
    ');'
  );
  assert.match(
    nextDraftResolution,
    new RegExp(`Object\\.keys\\(${draftCollection}\\)`, 'u'),
    `${label} draft review must consider the entire retained draft collection.`
  );
  assert.match(
    nextDraftResolution,
    currentDraftKey,
    `${label} draft review must advance relative to the current selection.`
  );
  assert.match(
    panel,
    new RegExp(
      `t\\('editorDrafts\\.summary\\.${summaryKey}',[\\s\\S]*?count: ${countName}`,
      'u'
    ),
    `${label} actions must report the aggregate outstanding-record count.`
  );

  const reviewAction = between(
    panel,
    `{${reviewGuard} !== null ? (`,
    ') : null}'
  );
  assert.match(reviewAction, /editorDrafts\.reviewNext/u);
  assert.match(
    reviewAction,
    selection,
    `${label} draft review must navigate through the existing selection callback.`
  );
  assert.doesNotMatch(
    reviewAction,
    /\b(?:set[A-Za-z]*Draft[A-Za-z]*|onUpdate[A-Za-z]*|onStage[A-Za-z]*|clearSubmitted[A-Za-z]*|deleteFieldDraftRecord|cancelActiveEditSession)\s*\(/u,
    `${label} draft review must select only; it must not stage, clear, or discard drafts.`
  );
}

const selectedItemPanel = between(
  app,
  'function SelectedItemPanel({',
  'function isPlaceholderPokemonRecord('
);
const itemDraftKeyUnion = between(
  selectedItemPanel,
  'const outstandingItemDraftKeys = [',
  '];'
);
assert.match(
  itemDraftKeyUnion,
  /new Set\([\s\S]*?Object\.keys\(fieldDraftsByItemId\)[\s\S]*?Object\.keys\(compatibilityDraftsByItemId\)/u,
  'Item actions must union field and compatibility draft keys without double-counting records.'
);
const nextItemDraftResolution = between(
  selectedItemPanel,
  'const nextItemDraftKey = getNextOutstandingEditorDraftKey(',
  ');'
);
assert.match(nextItemDraftResolution, /outstandingItemDraftKeys/u);
assert.match(nextItemDraftResolution, /itemDraftKey/u);
assert.match(
  selectedItemPanel,
  /items\.find\(\(candidate\) => getItemStorageDraftKey\(candidate\) === nextItemDraftKey\)/u,
  'Item draft storage keys must resolve through loaded item records before selection.'
);
assert.match(
  selectedItemPanel,
  /t\('editorDrafts\.summary\.items',[\s\S]*?count: outstandingItemDraftKeys\.length/u,
  'Item actions must report the aggregate outstanding-record count.'
);
const itemDraftReviewAction = between(
  selectedItemPanel,
  '{nextItemDraftTarget ? (',
  ') : null}'
);
assert.match(itemDraftReviewAction, /editorDrafts\.reviewNext/u);
assert.match(
  itemDraftReviewAction,
  /onSelectItem\(nextItemDraftTarget\.itemId\)/u,
  'Item draft review must navigate through the existing selection callback.'
);
assert.doesNotMatch(
  itemDraftReviewAction,
  /\b(?:set[A-Za-z]*Draft[A-Za-z]*|onUpdate[A-Za-z]*|onStage[A-Za-z]*|clearSubmitted[A-Za-z]*|deleteFieldDraftRecord|cancelActiveEditSession)\s*\(/u,
  'Item draft review must select only; it must not stage, clear, or discard drafts.'
);

const selectedMovePanel = between(
  app,
  'function SelectedMovePanel({',
  'const textCategoryLocalizationKeys'
);
assert.match(
  selectedMovePanel,
  /const outstandingMoveDraftKeys = Object\.keys\(moveDraftsByMoveId\)\.filter\(/u,
  'Move actions must count every retained non-empty move draft.'
);
const nextMoveDraftResolution = between(
  selectedMovePanel,
  'const nextMoveDraftKey = getNextOutstandingEditorDraftKey(',
  ');'
);
assert.match(nextMoveDraftResolution, /outstandingMoveDraftKeys/u);
assert.match(nextMoveDraftResolution, /moveDraftStorageKey/u);
assert.match(
  selectedMovePanel,
  /moves\.find\(\(candidate\) => candidate\.moveId\.toString\(\) === nextMoveDraftKey\)/u,
  'Move draft storage keys must resolve through loaded move records before selection.'
);
assert.match(
  selectedMovePanel,
  /t\('editorDrafts\.summary\.moves',[\s\S]*?count: outstandingMoveDraftKeys\.length/u,
  'Move actions must report the aggregate outstanding-record count.'
);
const moveDraftReviewAction = between(
  selectedMovePanel,
  '{nextMoveDraftTarget ? (',
  ') : null}'
);
assert.match(moveDraftReviewAction, /editorDrafts\.reviewNext/u);
assert.match(
  moveDraftReviewAction,
  /onSelectMove\(nextMoveDraftTarget\.moveId\)/u,
  'Move draft review must navigate through the existing selection callback.'
);
assert.doesNotMatch(
  moveDraftReviewAction,
  /\b(?:set[A-Za-z]*Draft[A-Za-z]*|onUpdate[A-Za-z]*|onStage[A-Za-z]*|clearSubmitted[A-Za-z]*|deleteFieldDraftRecord|cancelActiveEditSession)\s*\(/u,
  'Move draft review must select only; it must not stage, clear, or discard drafts.'
);

const textSection = between(
  app,
  'function TextSection({',
  'function SelectedTextPanel({'
);
assert.match(
  textSection,
  /const \[reviewedDraftEntry, setReviewedDraftEntry\] = useState<TextEntryRecord \| null>\(null\)/u,
  'Text review must retain an exact record snapshot for drafts outside the active result page.'
);
assert.match(
  textSection,
  /overlayPendingTextValues\(\[reviewedDraftEntry\], editSession\)\[0\]/u,
  'An off-page reviewed Text record must overlay its latest staged session value.'
);
assert.match(
  textSection,
  /reviewedDraftEntryWithPendingValue\?\.textKey === selectedTextKey[\s\S]*?reviewedDraftEntryWithPendingValue/u,
  'Text selection must surface the retained off-page draft target without replacing the active result page.'
);
const textDraftTargetReview = between(
  textSection,
  'const handleReviewTextDraftTarget = useCallback(',
  'const canEditText ='
);
assert.match(
  textDraftTargetReview,
  /setReviewedDraftEntry\(target\)[\s\S]*?onSelectTextEntry\(target\.textKey\)/u,
  'Text draft review must retain the exact target before selecting its stable text key.'
);
assert.doesNotMatch(
  textDraftTargetReview,
  /on(?:Search|Category|Language|Page)Change|scroll(?:IntoView|To)|setDraftsByTextKey|onUpdateTextEntry/u,
  'Text draft review must not reset the active query, page, scroll, or mutate retained drafts.'
);
assert.match(
  textSection,
  /onReviewTextDraftTarget=\{handleReviewTextDraftTarget\}/u,
  'The selected Text panel must use the exact-target review callback.'
);
assert.match(
  textSection,
  /const handleReviewedTextEntryStaged = useCallback\([\s\S]*?currentEntry\?\.textKey === textKey[\s\S]*?\{ \.\.\.currentEntry, value \}[\s\S]*?onReviewedTextEntryStaged=\{handleReviewedTextEntryStaged\}/u,
  'A successful off-page Text stage must advance the retained source snapshot, including a restore that removes its pending edit.'
);
const exactTextMutationQuery = between(
  app,
  'function createExactTextMutationQuery(',
  'function parseTextStableLocationTarget('
);
assert.match(
  exactTextMutationQuery,
  /language: target\.language[\s\S]*?limit: 1[\s\S]*?offset: target\.lineIndex[\s\S]*?searchText: target\.sourceFile/u,
  'Text staging must address the exact retained source row independently of the visible search, category, language, and page.'
);
const textStableLocationParser = between(
  app,
  'function parseTextStableLocationTarget(',
  'const githubReleasesApiUrl ='
);
assert.match(
  textStableLocationParser,
  /normalizedSegment === 'message' \|\| normalizedSegment\.endsWith\('_message'\)/u,
  'Exact Text navigation must recognize both ordinary message roots and the Z-A ik_message root.'
);
const textUpdateHandler = between(
  app,
  'const handleUpdateTextEntry = async (',
  'const handleUpdateTrainerField = async ('
);
assert.match(
  textUpdateHandler,
  /query: createExactTextMutationQuery\(selectedGame, sourceEntry\)/u,
  'Text staging must submit the frozen source record through the exact mutation query.'
);
assert.doesNotMatch(
  textUpdateHandler,
  /commitTextWorkflow/u,
  'An exact off-filter Text mutation response must not replace the visible workflow or reset its query context.'
);

const selectedTextPanel = between(
  app,
  'function SelectedTextPanel({',
  'function TrainersSection({'
);
assert.match(
  selectedTextPanel,
  /draftTargetByTextKeyRef\.current\[entry\.textKey\] = entry/u,
  'Text drafts must retain their source record so off-page draft keys resolve safely.'
);
const nextTextDraftResolution = between(
  selectedTextPanel,
  'const nextTextDraftKey = getNextOutstandingEditorDraftKey(',
  ');'
);
assert.match(nextTextDraftResolution, /Object\.keys\(draftsByTextKey\)/u);
assert.match(nextTextDraftResolution, /entry\?\.textKey/u);
assert.match(
  selectedTextPanel,
  /draftTargetByTextKeyRef\.current\[nextTextDraftKey\] \?\? null/u,
  'Text draft keys must resolve through their retained source records before selection.'
);
assert.match(
  selectedTextPanel,
  /const submittedTextKey = entry\.textKey[\s\S]*?draftTargetByTextKeyRef\.current\[submittedTextKey\] \?\? entry[\s\S]*?onUpdateTextEntry\([\s\S]*?submittedTextKey,[\s\S]*?draftValueBeforeStage,[\s\S]*?submittedTarget[\s\S]*?sourceMutation\.kind === 'source-mutated'[\s\S]*?onReviewedTextEntryStaged\(submittedTextKey, draftValueBeforeStage\)[\s\S]*?currentDrafts\[submittedTextKey\] === draftValueBeforeStage[\s\S]*?deleteFieldDraftRecord/u,
  'Text staging must update the effective off-page source while preserving a newer draft typed during the request.'
);
assert.match(
  selectedTextPanel,
  /t\('editorDrafts\.summary\.text', \{ count: textDraftCount \}\)/u,
  'Text actions must report the aggregate outstanding-record count.'
);
const textDraftReviewAction = between(
  selectedTextPanel,
  '{nextTextDraftTarget ? (',
  ') : null}'
);
assert.match(textDraftReviewAction, /editorDrafts\.reviewNext/u);
assert.match(
  textDraftReviewAction,
  /onReviewTextDraftTarget\(nextTextDraftTarget\)/u,
  'Text draft review must navigate through the exact off-page target callback.'
);
assert.doesNotMatch(
  textDraftReviewAction,
  /\b(?:set[A-Za-z]*Draft[A-Za-z]*|onUpdate[A-Za-z]*|onStage[A-Za-z]*|clearSubmitted[A-Za-z]*|deleteFieldDraftRecord|cancelActiveEditSession)\s*\(/u,
  'Text draft review must select only; it must not stage, clear, or discard drafts.'
);

const fashionDraftTargetResolution = between(
  fashionCatalog,
  'const draftTargetsByKey = useMemo(',
  'const pendingCount ='
);
assert.match(
  fashionDraftTargetResolution,
  /parseFashionCatalogDraftKey\(key\)[\s\S]*?getRows\(workflow, parsed\.catalogFile\)[\s\S]*?getFieldDefinitions\(parsed\.catalogFile, t, targetRow\)/u,
  'Fashion draft review must resolve each retained key to an exact loaded row and field.'
);
assert.match(
  fashionDraftTargetResolution,
  /const reviewableDraftKeys = outstandingDraftKeys\.filter[\s\S]*?draftTargetsByKey\.has\(key\)[\s\S]*?const unavailableDraftKeys = outstandingDraftKeys\.filter[\s\S]*?!draftTargetsByKey\.has\(key\)/u,
  'Fashion must distinguish reviewable retained drafts from unavailable source keys.'
);
assert.match(
  fashionDraftTargetResolution,
  /getNextOutstandingEditorDraftKey\([\s\S]*?reviewableDraftKeys,[\s\S]*?draftKey/u,
  'Fashion draft review must cycle deterministically through every resolvable retained key.'
);
assert.match(
  fashionCatalog,
  /useEffect\(\(\) => \{[\s\S]*?setPage\(0\);[\s\S]*?\}, \[search\]\);/u,
  'Fashion search changes may reset paging independently of catalog-file review navigation.'
);
assert.doesNotMatch(
  fashionCatalog,
  /useEffect\(\(\) => \{[\s\S]*?setPage\(0\);[\s\S]*?\}, \[catalogFile, search\]\);/u,
  'Fashion cross-catalog draft review must not be forced back to page zero.'
);
const fashionDraftReview = between(
  fashionCatalog,
  'const reviewNextDraft = () => {',
  'const discardUnavailableDrafts = () => {'
);
assert.match(
  fashionDraftReview,
  /draftTargetsByKey\.get\(nextDraftKey\)[\s\S]*?targetFilteredRows\.findIndex[\s\S]*?setCatalogFile\(target\.catalogFile\)[\s\S]*?setPage\(Math\.floor\(targetVisibleIndex \/ pageSize\)\)[\s\S]*?setSelectedRowId\(target\.row\.physicalRowId\)[\s\S]*?setSelectedField\(target\.field\.field\)/u,
  'Fashion draft review must reveal the exact target row and field without resetting search.'
);
assert.doesNotMatch(
  fashionDraftReview,
  /setSearch\(|scroll(?:IntoView|To)|onStageFieldEdit|setDraftValues\(/u,
  'Fashion draft review must not reset search or scroll, stage, clear, or discard drafts.'
);
const discardUnavailableFashionDrafts = between(
  fashionCatalog,
  'const discardUnavailableDrafts = () => {',
  'return ('
);
assert.match(
  discardUnavailableFashionDrafts,
  /window\.confirm\([\s\S]*?new Set\(unavailableDraftKeys\)[\s\S]*?setDraftValues\([\s\S]*?!unavailableKeys\.has\(key\)/u,
  'Unavailable Fashion drafts must remain counted until the user explicitly confirms their discard.'
);
assert.match(
  fashionCatalog,
  /draftSummary[\s\S]*?count: outstandingDraftCount[\s\S]*?unavailableDraftKeys\.length/u,
  'Fashion actions must report both the aggregate retained count and unavailable subset.'
);

const shopsSection = between(
  app,
  'function ShopsSection({',
  'function SelectedShopPanel({'
);
assert.match(
  shopsSection,
  /const selectedShop =\s*workflow\?\.shops\.find\(\(shop\) => shop\.shopId === selectedShopId\)/u,
  'Shop selection must resolve from the unfiltered workflow so review does not reset search.'
);
const selectedShopPanel = between(
  app,
  'function SelectedShopPanel({',
  'function ShopRowFieldInput({'
);
assert.match(
  selectedShopPanel,
  /const outstandingShopDraftKeys = Object\.keys\(inventoryDraftsByShopId\);/u,
  'Shop actions must count every retained shop draft.'
);
const nextShopDraftResolution = between(
  selectedShopPanel,
  'const nextShopDraftKey = getNextOutstandingEditorDraftKey(',
  ');'
);
assert.match(nextShopDraftResolution, /outstandingShopDraftKeys/u);
assert.match(nextShopDraftResolution, /shop\?\.shopId/u);
assert.match(
  selectedShopPanel,
  /t\('editorDrafts\.summary\.shops',[\s\S]*?count: outstandingShopDraftKeys\.length/u,
  'Shop actions must report the aggregate outstanding-record count.'
);
assert.match(
  selectedShopPanel,
  /clearSubmittedKeyedEditorDraft\([\s\S]*?currentDrafts,[\s\S]*?shop\.shopId,[\s\S]*?submittedDraft,[\s\S]*?areJsonSerializableDraftsEqual/u,
  'A delayed shop stage must clear only its exact submitted snapshot.'
);
const shopDraftReviewAction = between(
  selectedShopPanel,
  '{nextShopDraftKey !== null ? (',
  ') : null}'
);
assert.match(shopDraftReviewAction, /editorDrafts\.reviewNext/u);
assert.match(
  shopDraftReviewAction,
  /onSelectShop\(nextShopDraftKey\)/u,
  'Shop draft review must navigate through the existing selection callback.'
);
assert.doesNotMatch(
  shopDraftReviewAction,
  /\b(?:set[A-Za-z]*Draft[A-Za-z]*|onSearchChange|onUpdate[A-Za-z]*|onStage[A-Za-z]*|clearSubmitted[A-Za-z]*|cancelActiveEditSession)\s*\(/u,
  'Shop draft review must select only; it must not reset search, stage, clear, or discard drafts.'
);

const behaviorSection = between(
  app,
  'function BehaviorSection({',
  'function SelectedBehaviorPanel({'
);
assert.match(
  behaviorSection,
  /const selectedEntry =\s*workflow\?\.entries\.find\(\(entry\) => entry\.entryId === selectedEntryId\) \?\? null;/u,
  'Behavior selection must resolve from the unfiltered workflow so review does not reset search.'
);
const selectedBehaviorPanel = between(
  app,
  'function SelectedBehaviorPanel({',
  'function SwShPlacementSection('
);
assert.match(
  selectedBehaviorPanel,
  /const outstandingBehaviorDraftKeys = Object\.keys\(draftsByEntryId\);/u,
  'Behavior actions must count every retained behavior draft.'
);
const nextBehaviorDraftResolution = between(
  selectedBehaviorPanel,
  'const nextBehaviorDraftKey = getNextOutstandingEditorDraftKey(',
  ');'
);
assert.match(nextBehaviorDraftResolution, /outstandingBehaviorDraftKeys/u);
assert.match(nextBehaviorDraftResolution, /entry\?\.entryId/u);
assert.match(
  selectedBehaviorPanel,
  /t\('editorDrafts\.summary\.behavior',[\s\S]*?count: outstandingBehaviorDraftKeys\.length/u,
  'Behavior actions must report the aggregate outstanding-record count.'
);
assert.match(
  selectedBehaviorPanel,
  /clearSubmittedKeyedEditorDraft\([\s\S]*?currentDrafts,[\s\S]*?entry\.entryId,[\s\S]*?submittedDraft,[\s\S]*?areFieldDraftsEqual/u,
  'A delayed behavior stage must clear only its exact submitted snapshot.'
);
const behaviorDraftReviewAction = between(
  selectedBehaviorPanel,
  '{nextBehaviorDraftKey !== null ? (',
  ') : null}'
);
assert.match(behaviorDraftReviewAction, /editorDrafts\.reviewNext/u);
assert.match(
  behaviorDraftReviewAction,
  /onSelectEntry\(nextBehaviorDraftKey\)/u,
  'Behavior draft review must navigate through the existing selection callback.'
);
assert.doesNotMatch(
  behaviorDraftReviewAction,
  /\b(?:set[A-Za-z]*Draft[A-Za-z]*|onSearchChange|onUpdate[A-Za-z]*|onStage[A-Za-z]*|clearSubmitted[A-Za-z]*|cancelActiveEditSession)\s*\(/u,
  'Behavior draft review must select only; it must not reset search, stage, clear, or discard drafts.'
);

const selectedPlacementPanel = between(
  app,
  'function SelectedPlacementPanel({',
  'function PlacementObjectGroupBrowser({'
);
assert.match(
  selectedPlacementPanel,
  /const outstandingPlacementDraftKeys = Object\.keys\(draftsByObjectId\);/u,
  'Placement actions must count every retained object draft, including off-page drafts.'
);
const nextPlacementDraftResolution = between(
  selectedPlacementPanel,
  'const nextPlacementDraftKey = getNextOutstandingEditorDraftKey(',
  ');'
);
assert.match(nextPlacementDraftResolution, /reviewablePlacementDraftKeys/u);
assert.match(nextPlacementDraftResolution, /placedObject\?\.objectId/u);
assert.match(
  selectedPlacementPanel,
  /t\('editorDrafts\.summary\.placement',[\s\S]*?count: outstandingPlacementDraftKeys\.length/u,
  'Placement actions must report the aggregate outstanding-record count.'
);
assert.match(
  selectedPlacementPanel,
  /clearSubmittedKeyedEditorDraft\([\s\S]*?currentDrafts,[\s\S]*?placedObject\.objectId,[\s\S]*?submittedDraft,[\s\S]*?areFieldDraftsEqual/u,
  'A delayed placement stage must clear only its exact submitted snapshot.'
);
const placementDraftReviewAction = between(
  selectedPlacementPanel,
  '{nextPlacementDraftKey !== null ? (',
  ') : null}'
);
assert.match(placementDraftReviewAction, /editorDrafts\.reviewNext/u);
assert.match(
  placementDraftReviewAction,
  /reviewNextPlacementDraft\(\)/u,
  'Placement draft review actions must use the guarded exact-target review operation.'
);
assert.doesNotMatch(
  placementDraftReviewAction,
  /\b(?:set[A-Za-z]*Draft[A-Za-z]*|onSearchChange|onSelectObject|onUpdate[A-Za-z]*|onStage[A-Za-z]*|clearSubmitted[A-Za-z]*|cancelActiveEditSession)\s*\(/u,
  'Placement draft review must not use page-bound selection, reset search, stage, clear, or discard drafts.'
);
const guardedPlacementDraftReview = between(
  selectedPlacementPanel,
  'const reviewNextPlacementDraft = async () => {',
  'const discardUnavailablePlacementDrafts = () => {'
);
assert.match(
  guardedPlacementDraftReview,
  /placementDraftReviewPendingRef\.current[\s\S]*?placementDraftReviewPendingRef\.current = true;[\s\S]*?onReviewDraftTarget\(reviewedDraftKey\)[\s\S]*?finally \{[\s\S]*?placementDraftReviewPendingRef\.current = false;/u,
  'Placement Review Next Draft must synchronously admit one exact-target resolution and always release it.'
);
assert.match(
  guardedPlacementDraftReview,
  /Object\.hasOwn\(draftsByObjectIdRef\.current, reviewedDraftKey\)[\s\S]*?result === 'unavailable'[\s\S]*?addStringSetValue\(currentKeys, reviewedDraftKey\)/u,
  'Placement may mark unavailable only the exact reviewed key while that retained draft is still current.'
);
assert.match(
  selectedPlacementPanel,
  /const unavailablePlacementDraftKeys = outstandingPlacementDraftKeys\.filter\([\s\S]*?const reviewablePlacementDraftKeys = outstandingPlacementDraftKeys\.filter/u,
  'Placement must preserve the raw draft count while excluding confirmed unavailable keys from Review Next.'
);
assert.match(
  selectedPlacementPanel,
  /const capturedUnavailableDraftKeys = \[\.\.\.unavailablePlacementDraftKeys\];[\s\S]*?confirmDiscardUnavailable\.placement[\s\S]*?deleteFieldDraftRecords\(currentDrafts, capturedUnavailableDraftKeySet\)/u,
  'Placement unavailable-draft recovery must require confirmation and remove only captured unavailable keys.'
);
assert.match(
  selectedPlacementPanel,
  /editorDrafts\.summary\.unavailable[\s\S]*?editorDrafts\.discardUnavailable/u,
  'Placement must visibly report and recover retained unavailable drafts even without a selected object.'
);
const placementDraftLocationReview = between(
  app,
  'const handleReviewPlacementDraftLocation = useCallback(',
  'const handleSelectBehaviorLocation = useCallback('
);
assert.match(
  placementDraftLocationReview,
  /prepareStableLocationCommit\(destination\)[\s\S]*?preparation\.kind === 'unavailable'[\s\S]*?return 'unavailable'[\s\S]*?preparation\.kind !== 'ready'/u,
  'Placement review must distinguish an exact unavailable target from an aborted navigation before committing.'
);
assert.match(
  placementDraftLocationReview,
  /handleNavigateLocation\([\s\S]*?preparation\.onCommit,[\s\S]*?'replace',[\s\S]*?rememberRecent: false[\s\S]*?preserveSameSectionDraftScope: true/u,
  'Placement review must preserve retained drafts without changing recent tabs or resetting the current editor scope.'
);
assert.doesNotMatch(
  placementDraftLocationReview,
  /setPlacementSearchText|setSwShPlacementOffset|scroll(?:IntoView|To)|onUpdatePlacementObjectFields|setDraftsByObjectId/u,
  'Placement draft review must not reset search, page offset, scroll, stage, or mutate draft state.'
);
const coldPlacementDraftResolution = between(
  app,
  "selection.section === 'placement' &&",
  'const preloadTransition ='
);
assert.match(
  coldPlacementDraftResolution,
  /querySwShPlacementCatalog\([\s\S]*?categoryId: swShPlacementCategoryId[\s\S]*?offset: swShPlacementOffset[\s\S]*?searchText: placementSearchText/u,
  'Off-page Placement review must retain the active category, page offset, and search query.'
);
assert.match(
  coldPlacementDraftResolution,
  /loadSwShPlacementObject\([\s\S]*?objectId: value[\s\S]*?const workflowObjects = \[[\s\S]*?detailResponse\.object,[\s\S]*?pageResponse\.objects\.filter/u,
  'Off-page Placement review must load and retain the exact draft object alongside the current result page.'
);

const teraRaids = between(
  app,
  'function TeraRaidsSection({',
  'function TeraRaidDraftPanel({'
);
assert.match(
  teraRaids,
  /t\('editorDrafts\.summary\.teraRaids',[\s\S]*?count: countFieldDraftRecords\(draftsByRecordId\)[\s\S]*?unavailableCount: unavailableTeraRaidDraftKeys\.length/u,
  'Tera Raid actions must report the aggregate outstanding boss and reward draft count.'
);
const nextTeraRaidDraftResolution = between(
  teraRaids,
  'const nextHiddenTeraRaidDraftKey = getNextOutstandingEditorDraftKey(',
  ');'
);
assert.match(
  nextTeraRaidDraftResolution,
  /reviewableTeraRaidDraftKeys[\s\S]*?currentTeraRaidDraftKey/u,
  'Tera Raid draft review must inspect the entire retained draft collection.'
);
assert.match(
  teraRaids,
  /for \(const raid of workflow\?\.raids \?\? \[\]\)[\s\S]*?targets\.set\(raid\.recordId,[\s\S]*?fixedRewardTables[\s\S]*?lotteryRewardTables[\s\S]*?targets\.set\(reward\.recordId/u,
  'Tera Raid review targets must cover every raid boss plus every fixed and lottery reward table reachable from every raid.'
);
assert.match(
  teraRaids,
  /const currentTeraRaidDraftKey =[\s\S]*?const reviewableTeraRaidDraftKeys = Object\.keys\(draftsByRecordId\)\.filter\([\s\S]*?teraRaidDraftTargets\.has\(draftKey\)[\s\S]*?draftKey === currentTeraRaidDraftKey[\s\S]*?draftKey !== selectedRaid\?\.recordId && draftKey !== selectedReward\?\.recordId/u,
  'Tera Raid review must retain one visible cursor, skip the other visible records, and keep every globally reachable hidden target in cycle order.'
);
assert.match(
  teraRaids,
  /const unavailableTeraRaidDraftKeys = Object\.keys\(draftsByRecordId\)\.filter\([\s\S]*?!teraRaidDraftTargets\.has\(draftKey\)[\s\S]*?countFieldDraftRecords\(draftsByRecordId\)[\s\S]*?unavailableTeraRaidDraftKeys\.length/u,
  'Tera Raid actions must keep unresolved raw draft records visible in the aggregate summary.'
);
assert.match(
  teraRaids,
  /const discardUnavailableTeraRaidDrafts = \(\) => \{[\s\S]*?window\.confirm\([\s\S]*?new Set\(unavailableTeraRaidDraftKeys\)[\s\S]*?setDraftsByRecordId\(\(currentDrafts\) =>[\s\S]*?deleteFieldDraftRecords\(currentDrafts, unavailableDraftKeys\)/u,
  'Tera Raid unavailable drafts must be recoverable only through an explicit confirmed discard.'
);
const teraRaidReviewAction = between(
  teraRaids,
  '{nextHiddenTeraRaidDraftTarget ? (',
  ') : null}'
);
assert.match(teraRaidReviewAction, /editorDrafts\.reviewNext/u);
assert.match(
  teraRaidReviewAction,
  /onSelectRaid\(nextHiddenTeraRaidDraftTarget\.raidRecordId, \(\) => \{[\s\S]*?setRewardKind\(nextHiddenTeraRaidDraftTarget\.rewardKind\)[\s\S]*?setSelectedRewardRecordId\(nextHiddenTeraRaidDraftTarget\.recordId\)/u,
  'Tera Raid draft review must atomically select the owning raid before revealing its boss or reward draft.'
);
assert.doesNotMatch(
  teraRaidReviewAction,
  /\b(?:setDraftsByRecordId|onSearchChange|onUpdate[A-Za-z]*|onStage[A-Za-z]*|clearSubmitted[A-Za-z]*|cancelActiveEditSession)\s*\(/u,
  'Tera Raid draft review must not stage, clear, or discard any draft.'
);

const selectedEncounterPanel = between(
  app,
  'function SelectedEncounterPanel({',
  'function ZaEncounterPlayerPartnerEditor({'
);
for (const draftCollection of [
  'draftsBySlotKey',
  'zaSlotDraftsBySlotKey',
  'zaAppearanceDraftsByTableId',
  'levelDraftsByScopeKey',
  'playerPartnerDraftsByKey',
  'scriptedBossDraftsBySelectorId'
]) {
  assert.match(
    selectedEncounterPanel,
    new RegExp(`addDraftIdentities\\([^\\n]*Object\\.keys\\(${draftCollection}\\)`, 'u'),
    `Wild Encounter aggregate review must include ${draftCollection}.`
  );
}
assert.match(
  selectedEncounterPanel,
  /const visibleOutstandingDraftIdentities = \[[\s\S]*?new Set\([\s\S]*?visibleDraftIdentities\.filter[\s\S]*?while \(true\)[\s\S]*?bestUncoveredCount/u,
  'Wild Encounter draft maps must collapse into deterministic review destinations that cover overlapping slot, appearance, level, boss, and partner scopes once.'
);
assert.match(
  selectedEncounterPanel,
  /getNextOutstandingEditorDraftKey\([\s\S]*?outstandingEncounterDraftTargets\.map[\s\S]*?selectedEncounterDraftTargetKey/u,
  'Wild Encounter draft review must advance relative to the selected normalized destination.'
);
assert.match(
  selectedEncounterPanel,
  /const outstandingEncounterDraftCount =[\s\S]*?outstandingEncounterDraftTargets\.length \+ unavailableEncounterDraftIdentities\.length[\s\S]*?t\('editorDrafts\.summary\.encounters',[\s\S]*?count: outstandingEncounterDraftCount[\s\S]*?unavailableCount: unavailableEncounterDraftIdentities\.length/u,
  'Wild Encounter actions must report both normalized review destinations and unresolved retained records in the aggregate count.'
);
assert.match(
  selectedEncounterPanel,
  /unavailableEncounterDraftIdentities: \[\.\.\.outstandingDraftIdentities\]\.filter\([\s\S]*?!coveredDraftIdentities\.has\(identity\)[\s\S]*?const discardUnavailableEncounterDrafts = \(\) => \{[\s\S]*?window\.confirm\([\s\S]*?setDraftsBySlotKey[\s\S]*?setZaSlotDraftsBySlotKey[\s\S]*?setZaAppearanceDraftsByTableId[\s\S]*?setLevelDraftsByScopeKey[\s\S]*?setPlayerPartnerDraftsByKey[\s\S]*?setScriptedBossDraftsBySelectorId/u,
  'Wild Encounter unresolved draft identities must remain counted and require an explicit confirmed recovery action across every retained draft map.'
);
const encounterReviewAction = between(
  selectedEncounterPanel,
  '{nextEncounterDraftTarget ? (',
  ') : null}'
);
assert.match(
  encounterReviewAction,
  /onReviewDraftTarget\([\s\S]*?nextEncounterDraftTarget\.tableId,[\s\S]*?nextEncounterDraftTarget\.slot/u,
  'Wild Encounter review must select the exact owning table and slot.'
);
assert.doesNotMatch(
  encounterReviewAction,
  /\b(?:set[A-Za-z]*Draft[A-Za-z]*|onSearchChange|onUpdate[A-Za-z]*|onStage[A-Za-z]*|clearSubmitted[A-Za-z]*|cancelActiveEditSession)\s*\(/u,
  'Wild Encounter review must select only; it must not stage, clear, discard, or reset filters.'
);
const encounterDraftLocationHandler = between(
  app,
  'const handleReviewEncounterDraftLocation = useCallback(',
  'const handleSelectTeraRaidLocation = useCallback('
);
assert.match(
  encounterDraftLocationHandler,
  /setSelectedEncounterTableId\(tableId\);[\s\S]*?setSelectedEncounterSlot\(slot\);[\s\S]*?preserveSameSectionDraftScope: true/u,
  'Wild Encounter draft review must commit table and slot together while preserving same-section local drafts.'
);

const selectedRaidBattlePanel = between(
  app,
  'function SelectedRaidBattlePanel({',
  'function RaidRewardsSection({'
);
assert.match(
  selectedRaidBattlePanel,
  /for \(const candidateTable of tables\)[\s\S]*?for \(const candidateSlot of candidateTable\.slots\)[\s\S]*?raidBattleDraftTargetsByKey\.has\(draftKey\)/u,
  'Raid Battle draft review must resolve retained slots across every loaded table.'
);
assert.match(
  selectedRaidBattlePanel,
  /getNextOutstandingEditorDraftKey\([\s\S]*?outstandingRaidBattleDraftKeys,[\s\S]*?raidBattleDraftKey/u,
  'Raid Battle draft review must advance relative to the selected slot.'
);
assert.match(
  selectedRaidBattlePanel,
  /const unavailableRaidBattleDraftKeys = Object\.keys\(draftsBySlotKey\)\.filter\([\s\S]*?!raidBattleDraftTargetsByKey\.has\(draftKey\)[\s\S]*?const outstandingRaidBattleDraftCount = countFieldDraftRecords\(draftsBySlotKey\)[\s\S]*?t\('editorDrafts\.summary\.raidBattles',[\s\S]*?count: outstandingRaidBattleDraftCount[\s\S]*?unavailableCount: unavailableRaidBattleDraftKeys\.length/u,
  'Raid Battle actions must report every retained draft slot, including unresolved records.'
);
assert.match(
  selectedRaidBattlePanel,
  /const discardUnavailableRaidBattleDrafts = \(\) => \{[\s\S]*?window\.confirm\([\s\S]*?new Set\(unavailableRaidBattleDraftKeys\)[\s\S]*?deleteFieldDraftRecords\(currentDrafts, unavailableDraftKeys\)/u,
  'Raid Battle unavailable drafts must be recoverable only through an explicit confirmed discard.'
);
const raidBattleReviewAction = between(
  selectedRaidBattlePanel,
  '{nextRaidBattleDraftTarget ? (',
  ') : null}'
);
assert.match(
  raidBattleReviewAction,
  /onReviewDraftTarget\([\s\S]*?nextRaidBattleDraftTarget\.tableId,[\s\S]*?nextRaidBattleDraftTarget\.slot/u,
  'Raid Battle review must select the exact owning table and slot.'
);
assert.doesNotMatch(
  raidBattleReviewAction,
  /\b(?:setDraftsBySlotKey|onSearchChange|onUpdate[A-Za-z]*|onStage[A-Za-z]*|clearSubmitted[A-Za-z]*|cancelActiveEditSession)\s*\(/u,
  'Raid Battle review must select only; it must not stage, clear, discard, or reset filters.'
);

const selectedRaidRewardPanel = between(
  app,
  'function SelectedRaidRewardPanel({',
  'function getRaidRewardFieldForKind('
);
assert.match(
  selectedRaidRewardPanel,
  /for \(const candidateTable of tables\)[\s\S]*?for \(const candidateReward of candidateTable\.rewards\)[\s\S]*?raidRewardDraftTargetsByKey\.has\(draftKey\)/u,
  'Raid Reward draft review must resolve retained slots across every loaded table.'
);
assert.match(
  selectedRaidRewardPanel,
  /getNextOutstandingEditorDraftKey\([\s\S]*?outstandingRaidRewardDraftKeys,[\s\S]*?raidRewardDraftKey/u,
  'Raid Reward draft review must advance relative to the selected slot.'
);
assert.match(
  selectedRaidRewardPanel,
  /const unavailableRaidRewardDraftKeys = Object\.keys\(draftsBySlotKey\)\.filter\([\s\S]*?!raidRewardDraftTargetsByKey\.has\(draftKey\)[\s\S]*?const outstandingRaidRewardDraftCount = countFieldDraftRecords\(draftsBySlotKey\)[\s\S]*?t\('editorDrafts\.summary\.raidRewards',[\s\S]*?count: outstandingRaidRewardDraftCount[\s\S]*?unavailableCount: unavailableRaidRewardDraftKeys\.length/u,
  'Raid Reward and Bonus Reward actions must report every retained draft slot, including unresolved records.'
);
assert.match(
  selectedRaidRewardPanel,
  /const discardUnavailableRaidRewardDrafts = \(\) => \{[\s\S]*?window\.confirm\([\s\S]*?new Set\(unavailableRaidRewardDraftKeys\)[\s\S]*?deleteFieldDraftRecords\(currentDrafts, unavailableDraftKeys\)/u,
  'Raid Reward and Bonus Reward unavailable drafts must be recoverable only through an explicit confirmed discard.'
);
const raidRewardReviewAction = between(
  selectedRaidRewardPanel,
  '{nextRaidRewardDraftTarget ? (',
  ') : null}'
);
assert.match(
  raidRewardReviewAction,
  /onReviewDraftTarget\([\s\S]*?nextRaidRewardDraftTarget\.tableId,[\s\S]*?nextRaidRewardDraftTarget\.slot/u,
  'Raid Reward review must select the exact owning table and slot.'
);
assert.doesNotMatch(
  raidRewardReviewAction,
  /\b(?:setDraftsBySlotKey|onSearchChange|onUpdate[A-Za-z]*|onStage[A-Za-z]*|clearSubmitted[A-Za-z]*|cancelActiveEditSession)\s*\(/u,
  'Raid Reward review must select only; it must not stage, clear, discard, or reset filters.'
);

const pokemonSelection = between(
  app,
  'function PokemonSection({',
  'const canEditPokemon ='
);
assert.match(
  pokemonSelection,
  /: pokemon\.find\([\s\S]*?selectedPokemonPersonalId/u,
  'Pokemon search must resolve an explicit selection from the unfiltered source.'
);
assert.doesNotMatch(
  pokemonSelection,
  /useEffect\([\s\S]*?onSelectPokemon\(/u,
  'Typing Pokemon search must not select or navigate to each intermediate match.'
);

const exeFsSelection = between(
  app,
  'function ExeFsPatchSection({',
  'const royalCandyWorkflowNameKeys'
);
assert.match(
  exeFsSelection,
  /workflow\?\.patches\.find\(\(patch\) => patch\.patchId === selectedPatchId\)[\s\S]*?explicitlySelectedCheck/u,
  'ExeFS search must retain explicit patch and compatibility-check selections from the full source.'
);
assert.doesNotMatch(
  exeFsSelection,
  /useEffect\([\s\S]*?onSelect(?:Check|Patch)\(/u,
  'Typing ExeFS search must not select, navigate to, or promote intermediate results.'
);
assert.match(
  between(
    app,
    'const handleSelectExeFsCheckLocation = useCallback(',
    'const handleSelectExeFsPatchLocation = useCallback('
  ),
  /setSelectedExeFsCheckId\(checkId\)[\s\S]*?setSelectedExeFsPatchId\(patchId\)/u,
  'Explicit ExeFS-check selection must keep its owning patch without a second navigation.'
);

const stableLocationResolver = between(
  app,
  'const resolveStableLocationCommit = useCallback(',
  'const coldStableLocationCommitResolverRef'
);
assert.doesNotMatch(
  stableLocationResolver,
  /set(?:Item|Pokemon|Moves|Trainer)SearchText\(''\)/u,
  'Opening a stable record must not clear any editor search.'
);
for (const selectionContract of [
  /loadedItems\.find\(\(item\) => item\.itemId === selectedItemId\)/u,
  /pokemon\.find\([\s\S]*?candidate\.personalId/u,
  /moves\.find\(\(candidate\) => candidate\.moveId === selectedMoveId\)/u,
  /entries\.find\(\(entry\) => entry\.textKey === selectedTextKey\)/u,
  /trainers\.find\(\(trainer\) => trainer\.trainerId === selectedTrainerId\)/u
]) {
  assert.match(
    app,
    selectionContract,
    'Stable tab activation must retain the requested detail when the active search hides its row.'
  );
}

const stableSelectionHandler = between(
  app,
  'const handleSelectStableLocation = useCallback(',
  'const handleSelectItemLocation = useCallback('
);
assert.match(
  stableSelectionHandler,
  /tabEligible: isStableLocationTabEligible/u,
  'Record-row clicks must create or promote an eligible record tab.'
);
const encounterSlotSelectionHandler = between(
  app,
  'const handleSelectEncounterSlotLocation = useCallback(',
  'const handleSelectTeraRaidLocation = useCallback('
);
assert.match(
  encounterSlotSelectionHandler,
  /retainInRecordTab[\s\S]*?protectedTabKeys:[\s\S]*?rememberRecent: false,[\s\S]*?tabEligible: isStableLocationTabEligible/u,
  'Explicit encounter-slot selection must retain the slot in its bounded eligible record tab.'
);
const encounterSection = between(
  app,
  'function EncountersSection({',
  'function SelectedEncounterPanel({'
);
assert.match(
  encounterSection,
  /useEffect\(\(\) => \{[\s\S]*?onSelectSlot\(null, false\)[\s\S]*?onSelectSlot\(selectedTable\.slots\[0\]\?\.slot \?\? null, false\)/u,
  'Automatic encounter-slot synchronization must not create or promote a record tab.'
);
const tabActivationHandler = between(
  app,
  'const handleActivateWorkspaceTab = useCallback(',
  'const handleCloseWorkspaceTab = useCallback('
);
assert.match(
  tabActivationHandler,
  /rememberRecent: false,[\s\S]*?tabEligible: isStableLocationTabEligible/u,
  'Activating an existing tab must promote its MRU entry.'
);

const workspaceShell = read('src/workbench/workspaceShellController.ts');
const workspaceTabKeyContract = between(
  workspaceShell,
  'export function workspaceTabKey(',
  'export function serializeWorkbenchLocationHash('
);
assert.match(
  workspaceTabKeyContract,
  /subrecordId: null/u,
  'A record and its evolution, party, or other subrecord must share one quick-link identity.'
);

const firstTab = { key: 'pokemon:1', label: 'Bulbasaur' };
const secondTab = { key: 'pokemon:2', label: 'Ivysaur' };
assert.deepEqual(
  promoteRecentRecordTab([firstTab], secondTab).map((tab) => tab.key),
  ['pokemon:2', 'pokemon:1'],
  'A newly opened record must appear leftmost.'
);
assert.deepEqual(
  promoteRecentRecordTab([secondTab, firstTab], firstTab).map((tab) => tab.key),
  ['pokemon:1', 'pokemon:2'],
  'An activated record must move left without duplication.'
);
assert.deepEqual(
  resolveRetainedRecordTabLabel(
    { label: 'Pokemon', labelIsRawData: false },
    'Chikorita'
  ),
  { label: 'Chikorita', labelIsRawData: true },
  'Workflow eviction must not replace a retained record name with a generic editor label.'
);
assert.deepEqual(
  resolveRetainedRecordTabLabel(
    { label: 'Bayleef', labelIsRawData: true },
    'Chikorita'
  ),
  { label: 'Bayleef', labelIsRawData: true },
  'A live renamed record must supersede its retained tab name.'
);

const workspaceContentOpening = between(
  app,
  'className="workspace-content"',
  '{activeSection === \'health\''
);
assert.match(
  workspaceContentOpening,
  /inert=\{isProjectScopeTransitioning \? true : undefined\}/u,
  'The retained editor surface must reject interaction while project scope is changing.'
);
assert.match(
  workspaceContentOpening,
  /<Fragment[\s\S]*?activeSection[\s\S]*?workspaceContentDiscardRevision/u,
  'The editor surface may remount for a section change or a confirmed discard.'
);
assert.doesNotMatch(
  workspaceContentOpening,
  /activeLocation|workspaceTabKey|recordId/u,
  'Ordinary record clicks must not remount the editor surface.'
);
assert.match(
  app,
  /discardLocalEditorDraftProtection = useCallback\([\s\S]*?setWorkspaceContentDiscardRevision/u,
  'Confirmed local-draft discard must explicitly reset the section surface.'
);

const labelResolver = between(
  app,
  'function resolveWorkspaceLocationLabel(',
  'function createWorkspaceTargetViewModel('
);
for (const section of [
  'items',
  'pokemon',
  'moves',
  'text',
  'trainers',
  'shops',
  'encounters',
  'teraRaids',
  'raidBattles',
  'raidRewards',
  'raidBonusRewards',
  'placement',
  'behavior',
  'flagworkSave',
  'exefsPatches',
  'royalCandy',
  'startingItems',
  'spreadsheetImport'
]) {
  assert.match(labelResolver, new RegExp(`case '${section}'`, 'u'));
}
for (const recordKind of [
  'flag',
  'save-block',
  'exefs-check',
  'exefs-patch',
  'royal-candy-check',
  'royal-candy-workflow'
]) {
  assert.match(labelResolver, new RegExp(`recordKind === '${recordKind}'`, 'u'));
}
assert.doesNotMatch(
  labelResolver,
  /location\.entity\?\.recordId|location\.entity\.recordId|label\s*=\s*value/u,
  'A missing record name must fall back to a human editor label, never the opaque entity id.'
);
assert.match(
  labelResolver,
  /table\?\.location \|\| table\?\.area,[\s\S]*?table\?\.tableLabel/u,
  'Encounter Inspector targets must name the human location and spawner instead of the stable table id.'
);
assert.match(
  labelResolver,
  /summaryParts = \[[\s\S]*?table\?\.tableDetails,[\s\S]*?table\?\.locationDetails,[\s\S]*?table\?\.encounterType/u,
  'Encounter Inspector targets must retain useful record context.'
);
assert.match(
  adaptiveInspector,
  /<h2[\s\S]*?target\.label[\s\S]*?<p className="km-inspector-target-scope">\{target\.scopeLabels\.join\(' · '\)\}<\/p>/u,
  'The Inspector heading must lead with the human target and show its game/editor scope.'
);
assert.doesNotMatch(
  adaptiveInspector,
  /<h2[^>]*>\{t\('workbench\.inspector\.title'\)\}<\/h2>/u,
  'The Inspector heading must not replace the target name with the generic word Inspector.'
);
assert.match(
  adaptiveInspector,
  /target\.summary[\s\S]*?km-inspector-target-summary/u,
  'The Inspector heading must show available target-specific context.'
);
assert.match(
  between(app, 'const workspaceRecordTabs = useMemo(', 'const workspaceCommands = useMemo('),
  /resolveWorkspaceLocationLabel/u,
  'Record tabs must use the shared human-label resolver.'
);
assert.match(
  between(app, 'const workspaceRecordTabs = useMemo(', 'const workspaceCommands = useMemo('),
  /workspaceRecordLabelCacheRef[\s\S]*?scopeKey[\s\S]*?resolveRetainedRecordTabLabel[\s\S]*?liveLabel\.labelIsRawData/u,
  'Record tabs must retain live names across workflow eviction and reset the cache by project scope.'
);
for (const adapterContract of [
  /adapter\('items', 'item'/u,
  /adapter\('pokemon', 'pokemon-personal'/u,
  /adapter\('moves', 'move'/u,
  /adapter\('text', 'text-entry'/u,
  /adapter\('trainers', 'trainer'/u,
  /adapter\('shops', 'shop'/u,
  /adapter\('encounters', 'encounter-table'/u,
  /adapter\('teraRaids', 'tera-raid'/u,
  /adapter\('raidBattles', 'raid-table'/u,
  /adapter\('raidRewards', 'raid-reward-table'/u,
  /adapter\('raidBonusRewards', 'raid-bonus-reward-table'/u,
  /adapter\('placement', 'placed-object'/u,
  /adapter\('behavior', 'behavior-entry'/u,
  /adapter\('flagworkSave', 'flag', 'string', 'readOnly'\)/u,
  /adapter\('flagworkSave', 'save-block', 'string', 'readOnly'\)/u,
  /adapter\('exefsPatches', 'exefs-check', 'string', 'readOnly'\)/u,
  /adapter\('exefsPatches', 'exefs-patch'/u,
  /adapter\('royalCandy', 'royal-candy-workflow'/u,
  /adapter\('royalCandy', 'royal-candy-check'/u,
  /adapter\('startingItems', 'starting-item-slot'/u,
  /adapter\('spreadsheetImport', 'import-profile'/u
]) {
  assert.match(locationAdapters, adapterContract);
}
assert.equal(
  [...locationAdapters.matchAll(/^  adapter\(/gmu)].length,
  21,
  'Inspector target coverage must be revisited whenever the stable adapter registry changes.'
);

assert.match(tabRail, /if \(tabs\.length === 0\)/u);
assert.match(tabRail, /title=\{tab\.label\}/u);
assert.match(
  tabRail,
  /<ol[\s\S]*?aria-label=\{t\('workbench\.tabs\.label'\)\}[\s\S]*?className="km-record-tab-rail"[\s\S]*?>[\s\S]*?tabs\.map/u,
  'Record quick links must be exposed as an ordered recent-record list.'
);
assert.match(
  tabRail,
  /<li className="km-record-tab-item" key=\{tab\.key\}>[\s\S]*?aria-current=\{isActive \? 'page' : undefined\}[\s\S]*?className="km-record-tab"/u,
  'Each quick link must be a list item whose active navigation button uses aria-current.'
);
assert.doesNotMatch(
  tabRail,
  /role="tablist"|role="tab"|aria-selected=/u,
  'Record quick links are navigation history, not a tab widget with composite-tab semantics.'
);
assert.match(
  tabRail,
  /const firstTabKey = tabs\[0\]\?\.key[\s\S]*?useLayoutEffect\([\s\S]*?railRef\.current\.scrollLeft = 0;[\s\S]*?\[firstTabKey\]/u,
  'A newly promoted leftmost MRU must be revealed by scrolling only its tab rail.'
);
assert.doesNotMatch(
  tabRail,
  /scrollIntoView|document\.|window\.scroll/u,
  'Revealing an MRU tab must never scroll the editor or page.'
);
assert.match(cssBlock(tabStyles, '.km-record-tab-item'), /max-width: min\(220px, 72vw\)/u);
assert.match(cssBlock(tabStyles, '.km-record-tab span:first-child'), /text-overflow: ellipsis/u);

const virtualTableBody = between(app, 'function VirtualTableBody<T>(', 'function HealthSection(');
const interactiveTableRow = between(
  app,
  'type InteractiveTableRowProps =',
  'function VirtualTableBody<T>('
);
const virtualTableGeometry = between(
  app,
  'function calculateVirtualTableScrollMargin({',
  'const observeVirtualTableElementRect'
);
assert.match(
  interactiveTableRow,
  /HTMLAttributes<HTMLDivElement>[\s\S]*?<div[\s\S]*?className=\{`\$\{className \?\? ''\} interactive-table-row`\.trim\(\)\}[\s\S]*?role="row"[\s\S]*?tabIndex=\{isDisabled \? -1 : \(tabIndex \?\? 0\)\}/u,
  'Interactive editor rows must use the shared focusable div-row primitive instead of native buttons with an incompatible row role.'
);
assert.match(
  interactiveTableRow,
  /event\.nativeEvent\.isComposing[\s\S]*?event\.key !== 'Enter'[\s\S]*?event\.key !== ' '[\s\S]*?event\.preventDefault\(\)[\s\S]*?event\.currentTarget\.click\(\)/u,
  'The shared row primitive must preserve button-equivalent Enter and Space activation without firing during composition.'
);
assert.match(
  cssBlock(styles, '.interactive-table-row'),
  /justify-content: start/u,
  'Interactive editor rows must anchor oversized column grids to the logical start.'
);
for (const sourcePath of globSync('src/**/*.tsx', { cwd: desktopRoot })) {
  assert.doesNotMatch(
    read(sourcePath),
    /<button\b[^>]*\brole\s*=\s*["']row["'][^>]*>/u,
    `${sourcePath} must use InteractiveTableRow instead of assigning role=row to a native button.`
  );
}
assert.ok(
  [...app.matchAll(/<InteractiveTableRow\b/gu)].length > 0,
  'Interactive editor rows must be routed through the shared accessibility primitive.'
);
assert.match(
  virtualTableGeometry,
  /Math\.max\(0, bodyTop - scrollViewportTop - clientTop \+ scrollTop\)/u,
  'The virtual-table margin must stay in the parent scroller coordinate system.'
);
assert.match(
  virtualTableBody,
  /bodyRef\.current\?\.parentElement[\s\S]*?getScrollElement,[\s\S]*?scrollMargin,/u,
  'Virtual rows and their sticky heading must use the same parent scroll viewport.'
);
assert.match(
  virtualTableBody,
  /getBoundingClientRect\(\)[\s\S]*?calculateVirtualTableScrollMargin\([\s\S]*?clientTop:[\s\S]*?scrollTop:/u,
  'Virtual row offsets must be measured in the parent scroller coordinate system.'
);
assert.match(
  virtualTableBody,
  /transform: `translateY\(\$\{virtualRow\.start - scrollMargin\}px\)`/u,
  'Virtual row translation must subtract the sticky heading scroll margin.'
);
assert.match(
  virtualTableBody,
  /previousElementSibling[\s\S]*?resizeObserver\?\.observe\(headingElement\)/u,
  'Virtual scroll margin must track heading-size changes across layouts.'
);
assert.equal(
  [...app.matchAll(/<VirtualTableBody\b/gu)].length,
  11,
  'Every current VirtualTableBody owner must share the parent-scroller contract.'
);
assert.match(
  between(styles, '.items-row.items-row-heading,', '.items-row span {'),
  /position: sticky;[\s\S]*?top: 0/u
);
assert.match(
  between(styles, '.items-table,', '.items-table {'),
  /overflow: auto;[\s\S]*?scrollbar-gutter: stable/u
);
assert.match(cssBlock(styles, '.placement-object-table'), /overflow: auto/u);
assert.match(
  cssBlock(styles, '.placement-object-table .raid-rewards-row-heading'),
  /position: sticky;[\s\S]*?top: 0/u
);

for (const tableSelector of [
  '.shops-table',
  '.encounters-table',
  '.raid-rewards-table',
  '.flagwork-table',
  '.exefs-table'
]) {
  const block = cssBlock(styles, tableSelector);
  assert.match(block, /min-width: 0/u, `${tableSelector} must be shrink-safe in Classic layout.`);
  assert.match(block, /overflow: auto/u, `${tableSelector} must own any local table overflow.`);
}
for (const stackSelector of ['.shops-table-stack', '.flagwork-stack', '.placement-browser-stack']) {
  assert.match(
    cssBlock(styles, stackSelector),
    /min-width: 0/u,
    `${stackSelector} must not widen its Classic detail pane.`
  );
}
for (const layoutSelector of [
  '.items-layout',
  '.moves-layout',
  '.text-layout',
  '.trainers-layout',
  '.shops-layout',
  '.encounters-layout',
  '.flagwork-layout',
  '.placement-layout',
  '.behavior-layout',
  '.swsh-pokemon-layout',
  '.sv-pokemon-layout',
  '.za-pokemon-layout'
]) {
  assert.match(
    styles,
    new RegExp(`@container workspace \\(max-width: 1300px\\)[\\s\\S]*?${layoutSelector.replace('.', '\\.')}[\\s\\S]*?grid-template-columns: 1fr`, 'u'),
    `${layoutSelector} must stack before Classic columns become unusable.`
  );
}
assert.match(
  styles,
  /@container workspace \(max-width: 1300px\)[\s\S]*?\.za-items-section \.items-layout,[\s\S]*?grid-template-columns: 1fr/u,
  'The Classic breakpoint must override the more-specific Z-A Items two-column rule.'
);

const headingHeight = 63;
const scrollViewportTop = 120;
const clientTop = 1;
const calculateExpectedVirtualTableScrollMargin = ({
  bodyTop,
  clientTop: borderTop,
  scrollTop,
  scrollViewportTop: viewportTop
}) => Math.max(0, bodyTop - viewportTop - borderTop + scrollTop);
assert.equal(
  calculateExpectedVirtualTableScrollMargin({
    bodyTop: scrollViewportTop + clientTop + headingHeight,
    clientTop,
    scrollTop: 0,
    scrollViewportTop
  }),
  headingHeight,
  'A table below other page content must use only its own sticky heading as the row margin.'
);
const scrolledTop = 240;
const scrolledBodyTop = scrollViewportTop + clientTop + headingHeight - scrolledTop;
const retainedMargin = calculateExpectedVirtualTableScrollMargin({
  bodyTop: scrolledBodyTop,
  clientTop,
  scrollTop: scrolledTop,
  scrollViewportTop
});
assert.equal(retainedMargin, headingHeight);
assert.equal(
  Math.floor((scrolledTop - retainedMargin) / 48),
  3,
  'The visible virtual row index must advance from the parent scroll offset, not the page offset.'
);

console.log('Record-tab, Inspector target, no-interruption, and Classic table contracts passed.');
