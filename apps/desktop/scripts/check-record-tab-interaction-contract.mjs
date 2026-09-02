// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { globSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import {
  promoteRecentRecordTab,
  resolveRetainedRecordTabLabel
} from '../src/workbench/workspaceShellViewModels.ts';

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

const app = read('src/App.tsx');
const adaptiveInspector = read('src/features/workbench/AdaptiveInspector.tsx');
const styles = read('src/styles.css');
const tabRail = read('src/features/workbench/RecordTabRail.tsx');
const tabStyles = read('src/features/workbench/workbench.css');
const locationAdapters = read('src/workbench/locationAdapterRegistry.ts');

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
