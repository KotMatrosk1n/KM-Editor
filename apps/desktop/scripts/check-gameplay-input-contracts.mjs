// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import ts from 'typescript';

const desktopRoot = new URL('../', import.meta.url);

function read(relativePath) {
  return readFileSync(new URL(relativePath, desktopRoot), 'utf8');
}

const helperSource = read('src/features/gameplayInputDrafts.ts');
const transpiled = ts.transpileModule(helperSource, {
  compilerOptions: {
    module: ts.ModuleKind.ESNext,
    target: ts.ScriptTarget.ES2022
  },
  reportDiagnostics: true
});
assert.deepEqual(
  transpiled.diagnostics?.map((diagnostic) => diagnostic.messageText) ?? [],
  [],
  'Gameplay input helpers must transpile without diagnostics.'
);
const helpers = await import(
  `data:text/javascript;base64,${Buffer.from(transpiled.outputText).toString('base64')}`
);
const {
  filterAndRankSearchableOptions,
  findExactSearchableOption,
  parseBoundedWholeNumberDraft
} = helpers;

for (const [draft, minimum, maximum, expected] of [
  ['', 0, 100, null],
  ['-', 0, 100, null],
  ['1.5', 0, 100, null],
  [' 25 ', 0, 100, null],
  ['00', 0, 100, 0],
  ['100', 0, 100, 100],
  ['101', 0, 100, null],
  ['1', 1, 1_000, 1],
  ['1000', 1, 1_000, 1_000],
  ['1001', 1, 1_000, null]
]) {
  assert.equal(
    parseBoundedWholeNumberDraft(draft, minimum, maximum),
    expected,
    `Unexpected bounded whole-number result for ${JSON.stringify(draft)}.`
  );
}

const options = [
  ...Array.from({ length: 20 }, (_, index) => ({
    category: 'Drink',
    id: index + 1,
    label: `Mint Tea ${index + 1}`
  })),
  { category: 'Ingredient', id: 500, label: 'Mint' }
];
const getId = (option) => option.id;
const getLabel = (option) => option.label;
const getSearchValues = (option) => [option.label, option.category, option.id.toString()];
assert.equal(
  filterAndRankSearchableOptions(
    options,
    'Mint',
    20,
    getId,
    getLabel,
    getSearchValues
  )[0]?.id,
  500,
  'An exact label must rank ahead of earlier substring matches and the result cap.'
);
assert.equal(
  findExactSearchableOption(options, ' 500 ', getId, getLabel)?.id,
  500,
  'A trimmed exact numeric ID must resolve to its option.'
);
assert.equal(
  findExactSearchableOption(
    [...options, { category: 'Other', id: 501, label: 'Mint' }],
    'mint',
    getId,
    getLabel
  ),
  null,
  'An ambiguous exact label must not resolve silently.'
);

const battleCafe = read('src/features/battle-cafe-rewards/BattleCafeRewardsSection.tsx');
assert.match(
  battleCafe,
  /onChange=\{\(event\) => onChange\(\{ \[owner\]: event\.currentTarget\.value \}\)\}/,
  'Battle Cafe percentages must retain raw input text while the user edits.'
);
assert.match(
  battleCafe,
  /<SearchableOptionInput[\s\S]*?data-km-source-site="battle-cafe-reward-item"[\s\S]*?isFiniteCatalog[\s\S]*?localizeOptions=\{false\}[\s\S]*?maximumVisibleOptions=\{maximumVisibleItemOptions\}/,
  'Battle Cafe items must use the shared finite-catalog KM combobox.'
);
assert.match(
  battleCafe,
  /groupLabel: `\$\{option\.category\} · #\$\{option\.itemId\}`[\s\S]*?searchAliases: \[option\.category\][\s\S]*?value: option\.itemId/,
  'Battle Cafe item options must retain category search and semantic item IDs.'
);
assert.doesNotMatch(
  battleCafe,
  /battle-cafe-combobox|battle-cafe-item-results|aria-activedescendant=|onKeyDown=/,
  'Battle Cafe must not maintain a second bespoke combobox interaction model.'
);
assert.match(
  battleCafe,
  /const canStage =\s*canEdit &&\s*!hasInvalidStagedChange &&\s*!isBusy &&\s*hasDirtyDraft &&\s*percentagesAreValid;/,
  'Battle Cafe must not stage an incomplete or out-of-range percentage draft.'
);
assert.match(
  battleCafe,
  /const canEdit = workflow\?\.summary\.availability === 'available';/,
  'Battle Cafe must keep local fields correctable when an older staged payload is invalid.'
);

const npcItemGift = read('src/features/npc-item-gift/NpcItemGiftSection.tsx');
assert.match(
  npcItemGift,
  /<SearchableOptionInput[\s\S]*?data-km-source-site="npc-item-gift-item-picker"[\s\S]*?isFiniteCatalog[\s\S]*?maximumVisibleOptions=\{100\}/,
  'NPC Item Gift must use the shared viewport-safe finite-catalog KM combobox.'
);
assert.match(
  npcItemGift,
  /disabled: option\.isUnavailable[\s\S]*?searchAliases: \[option\.name, option\.category\][\s\S]*?value: option\.itemId/,
  'NPC Item Gift options must preserve unavailable rows, item-name search, category search, and semantic IDs.'
);
assert.doesNotMatch(
  npcItemGift,
  /function getSmartItemMatches|function findExactItemOption|npc-item-gift-options-/,
  'NPC Item Gift must not retain a second bespoke combobox interaction model.'
);

const placementUiSource = read('src/features/placement/placementUi.ts');
const transpiledPlacementUi = ts.transpileModule(placementUiSource, {
  compilerOptions: {
    module: ts.ModuleKind.ESNext,
    target: ts.ScriptTarget.ES2022
  },
  reportDiagnostics: true
});
assert.deepEqual(
  transpiledPlacementUi.diagnostics?.map((diagnostic) => diagnostic.messageText) ?? [],
  [],
  'Placement UI helpers must transpile without diagnostics.'
);
const placementHelpers = await import(
  `data:text/javascript;base64,${Buffer.from(transpiledPlacementUi.outputText).toString('base64')}`
);
assert.equal(
  placementHelpers.getDefaultPlacementCategoryId([
    { id: 'visibleItems', objectCount: 0 },
    { id: 'hiddenItems', objectCount: 24 },
    { id: 'rummagingPoints', objectCount: 3 }
  ]),
  'hiddenItems',
  'Placement must initially select the first populated category.'
);
assert.equal(
  placementHelpers.getDefaultPlacementCategoryId([
    { id: 'visibleItems', objectCount: 0 },
    { id: 'hiddenItems', objectCount: 0 }
  ]),
  'visibleItems',
  'Placement must retain a deterministic fallback when every category is empty.'
);
assert.equal(
  placementHelpers.getDefaultPlacementCategoryId([]),
  null,
  'Placement must handle an empty category catalog.'
);

const inGameSettingsPackage = read(
  'src/features/gameplay-settings/InGameSettingsPackagePanel.tsx'
);
assert.match(
  inGameSettingsPackage,
  /const installationTargetSelectionBusy = busy !== null;[\s\S]*?aria-busy=\{installationTargetSelectionBusy \|\| undefined\}[\s\S]*?disabled=\{installationTargetSelectionBusy\}/,
  'Gameplay Settings installation targets must expose and honor request-busy state.'
);
assert.match(
  inGameSettingsPackage,
  /aria-controls="in-game-settings-installation-detail"[\s\S]*?aria-pressed=\{installationTarget === target\}/,
  'Gameplay Settings installation targets must identify the detail region they update.'
);

const seedPlanner = read('src/features/dynamax-adventures/DynamaxAdventureSeedPlanner.tsx');
assert.match(
  seedPlanner,
  /const \[maximumResults, setMaximumResults\] = useState\('25'\);/,
  'Maximum Results must use a raw string draft.'
);
assert.match(
  seedPlanner,
  /maxResults,/,
  'Seed searches must submit the validated Maximum Results integer.'
);
assert.doesNotMatch(
  seedPlanner,
  /setMaximumResults\(Number\(/,
  'Maximum Results must not coerce partial input during onChange.'
);
