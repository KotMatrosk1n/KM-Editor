#!/usr/bin/env node
// SPDX-License-Identifier: GPL-3.0-only

import { spawnSync } from 'node:child_process';
import path from 'node:path';

const rawArgs = process.argv.slice(2);
const modeArg = rawArgs.find((arg) => !arg.startsWith('-')) ?? 'changed';
const flags = new Set(rawArgs.filter((arg) => arg.startsWith('-')));
const printOnly = flags.has('--print') || flags.has('--dry-run');

let repoRoot = process.cwd();
repoRoot = execCapture('git rev-parse --show-toplevel').trim();
if (!repoRoot) {
  console.error('Could not find the git repository root.');
  process.exit(1);
}

process.chdir(repoRoot);

const commands = [];
const commandKeys = new Set();

const dotnetLogger = '--logger "console;verbosity=minimal"';
const appTimeout = '--testTimeout=30000';

const swshFeatureFilters = new Map([
  ['BagHook', 'BagHook|SwShHookReservationTests'],
  ['Behavior', 'Behavior'],
  ['CatchCap', 'CatchCap|SwShHookReservationTests'],
  ['DynamaxAdventures', 'DynamaxAdventure'],
  ['Encounters', 'Encounters'],
  ['ExeFs', 'ExeFs|SwShHookReservationTests'],
  ['FairyGymBoosts', 'FairyGymBoosts'],
  ['FashionUnlock', 'FashionUnlock|SwShHookReservationTests'],
  ['Flagwork', 'Flagwork'],
  ['Gifts', 'GiftPokemon'],
  ['GymUniformRemoval', 'GymUniformRemoval|SwShHookReservationTests'],
  ['HyperTraining', 'HyperTraining|SwShHookReservationTests'],
  ['Items', 'Items'],
  ['IvScreen', 'IvScreen|SwShHookReservationTests'],
  ['ModMerger', 'ModMerger'],
  ['Moves', 'Moves'],
  ['Placement', 'Placement'],
  ['Pokemon', 'Pokemon'],
  ['Raids', 'Raid'],
  ['Randomizer', 'Randomizer'],
  ['Rentals', 'RentalPokemon'],
  ['RoyalCandy', 'RoyalCandy|SwShHookReservationTests'],
  ['ScarletViolet', 'ScarletViolet'],
  ['Shops', 'Shops'],
  ['SpreadsheetImport', 'SpreadsheetImport'],
  ['StartingItems', 'StartingItems|SwShHookReservationTests'],
  ['StaticEncounters', 'StaticEncounters'],
  ['Text', 'Text'],
  ['Trades', 'TradePokemon'],
  ['Trainers', 'Trainers'],
  ['TypeChart', 'TypeChart|SwShHookReservationTests'],
  ['Workflows', 'WorkflowService|ParsedDataCache'],
]);

const appFeatureFilters = new Map([
  ['dynamax-adventures', 'Dynamax Adventures'],
  ['fairy-gym-boosts', 'Fairy Gym Boosts'],
  ['fashion-unlock', 'Fashion Unlock'],
  ['randomizer', 'Randomizer'],
  ['type-chart', 'Type Chart'],
  ['workflows', 'workflow categories|switches workbench sections'],
]);

const ignoredPrefixes = [
  '.cache/',
  '.scratch/',
  '.tmp/',
  'bin/',
  'build/',
  'coverage/',
  'dist/',
  'handoff/',
  'node_modules/',
  'obj/',
  'out/',
  'scratch',
  'target/',
  'temp/',
  'tmp/',
];

const ignoredExact = new Set([
  'KM_Editor_Handoff.md',
]);

if (!['changed', 'fast', 'full'].includes(modeArg)) {
  console.error(`Unknown test slice "${modeArg}". Use changed, fast, or full.`);
  process.exit(1);
}

if (modeArg === 'fast') {
  addFastCommands();
} else if (modeArg === 'full') {
  addFullCommands();
} else {
  addChangedCommands();
}

if (commands.length === 0) {
  console.log('No test commands selected.');
  process.exit(0);
}

console.log(`Selected ${commands.length} command(s) for "${modeArg}".`);
for (const [index, item] of commands.entries()) {
  console.log(`${index + 1}. ${item.label}`);
  console.log(`   ${item.command}`);
}

if (printOnly) {
  process.exit(0);
}

const totalStarted = Date.now();
for (const item of commands) {
  console.log('');
  console.log(`> ${item.label}`);
  const started = Date.now();
  const result = spawnSync(item.command, {
    cwd: repoRoot,
    encoding: 'utf8',
    shell: true,
    stdio: 'inherit',
  });
  const elapsedSeconds = ((Date.now() - started) / 1000).toFixed(2);
  console.log(`elapsed: ${elapsedSeconds}s`);

  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }
}

console.log(`total elapsed: ${((Date.now() - totalStarted) / 1000).toFixed(2)}s`);

function addFastCommands() {
  add('workspace-path-check', 'Check workspace path hygiene', 'node scripts/check-workspace-paths.mjs');
  add('diff-check', 'Check whitespace and patch hygiene', 'git diff --check');
  add('desktop-typecheck', 'Typecheck desktop app', 'pnpm --filter @km-editor/desktop typecheck');
  add(
    'desktop-unit-smoke',
    'Run focused desktop unit and bridge tests',
    `pnpm --dir apps/desktop test:run src/AppErrorBoundary.test.tsx src/diagnostics.test.ts src/errorReporting.test.ts src/pokemonSprites.test.ts src/workbenchStore.test.ts src/bridge/contracts.test.ts src/bridge/projectBridge.test.ts src/features/fairy-gym-boosts/FairyGymBoostsSection.test.tsx ${appTimeout}`,
  );
  add(
    'desktop-app-smoke',
    'Run App shell and advanced editor smoke tests',
    `pnpm --dir apps/desktop test:run src/App.test.tsx -t "project workbench shell|workflow categories|Dynamax Adventures|Bag Hook|Catch Cap" ${appTimeout}`,
  );
  add('core-tests', 'Run core backend tests', dotnetProject('tests/KM.Core.Tests/KM.Core.Tests.csproj'));
  add('formats-tests', 'Run format backend tests', dotnetProject('tests/KM.Formats.Tests/KM.Formats.Tests.csproj'));
  add(
    'swsh-risk-smoke',
    'Run high risk Sword and Shield workflow smoke tests',
    dotnetProject(
      'tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj',
      'FullyQualifiedName~DynamaxAdventure|FullyQualifiedName~ReservedMainRegionsDoNotOverlapBetweenFeatureFamiliesInSameSegment|FullyQualifiedName~SwShRoyalCandyWorkflowServiceTests|FullyQualifiedName~TypeChartWorkflowTests|FullyQualifiedName~SwShFairyGymBoostsBseqPatcherTests',
    ),
  );
  add(
    'integration-bridge-smoke',
    'Run bridge serialization and Dynamax Adventures integration smoke tests',
    dotnetProject(
      'tests/KM.Integration.Tests/KM.Integration.Tests.csproj',
      'FullyQualifiedName~BridgeJson|FullyQualifiedName~BridgeResponse|FullyQualifiedName~DynamaxAdventure',
    ),
  );
}

function addFullCommands() {
  add('workspace-path-check', 'Check workspace path hygiene', 'node scripts/check-workspace-paths.mjs');
  add('diff-check', 'Check whitespace and patch hygiene', 'git diff --check');
  add('desktop-tests', 'Run all desktop Vitest tests', 'pnpm --dir apps/desktop test:run');
  add('backend-tests', 'Run all backend tests', dotnetProject('KM.Editor.slnx'));
}

function addChangedCommands() {
  add('workspace-path-check', 'Check workspace path hygiene', 'node scripts/check-workspace-paths.mjs');
  add('diff-check', 'Check whitespace and patch hygiene', 'git diff --check');

  const changedFiles = getChangedFiles();
  const relevantFiles = changedFiles.filter((file) => !isIgnored(file));

  if (relevantFiles.length === 0) {
    console.log('No relevant source or test changes detected after private scratch paths were ignored.');
    return;
  }

  console.log('Relevant changed files:');
  for (const file of relevantFiles) {
    console.log(`  ${file}`);
  }

  for (const file of relevantFiles) {
    mapChangedFile(file);
  }
}

function mapChangedFile(file) {
  if (file === 'package.json' || file === 'pnpm-lock.yaml') {
    addFastCommands();
    return;
  }

  if (file === 'Directory.Build.props' || file.endsWith('.slnx') || file.endsWith('.csproj')) {
    add('backend-tests', 'Run all backend tests after project configuration changes', dotnetProject('KM.Editor.slnx'));
    return;
  }

  if (file === 'tests/README.md' || file.startsWith('docs/') || file.endsWith('.md')) {
    return;
  }

  if (file.endsWith('TestsAssembly.cs')) {
    mapTestAssemblyTraitChange(file);
    return;
  }

  if (file.startsWith('tests/KM.SwSh.Tests/Performance/')) {
    add(
      'swsh-slow-list',
      'List slow Sword and Shield performance baselines',
      'dotnet test tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj --no-restore --filter "Kind=Slow" --list-tests',
    );
    return;
  }

  if (file.startsWith('apps/desktop/')) {
    mapDesktopChange(file);
    return;
  }

  if (file.startsWith('src/KM.Core/') || file.startsWith('tests/KM.Core.Tests/')) {
    add('core-tests', 'Run core backend tests', dotnetProject('tests/KM.Core.Tests/KM.Core.Tests.csproj', testFilterForChangedTest(file)));
    return;
  }

  if (file.startsWith('src/KM.Formats/') || file.startsWith('tests/KM.Formats.Tests/')) {
    add('formats-tests', 'Run format backend tests', dotnetProject('tests/KM.Formats.Tests/KM.Formats.Tests.csproj', testFilterForChangedTest(file)));
    return;
  }

  if (file.startsWith('src/KM.SwSh/') || file.startsWith('tests/KM.SwSh.Tests/')) {
    mapSwShChange(file);
    return;
  }

  if (file.startsWith('src/KM.SV/') || file.startsWith('tests/KM.Integration.Tests/SV/')) {
    add('sv-integration-tests', 'Run Scarlet and Violet integration tests', dotnetProject('tests/KM.Integration.Tests/KM.Integration.Tests.csproj', 'FullyQualifiedName~ScarletViolet|FullyQualifiedName~Sv'));
    return;
  }

  if (file.startsWith('src/KM.Api/') || file.startsWith('src/KM.Tools/') || file.startsWith('tests/KM.Integration.Tests/')) {
    mapIntegrationChange(file);
    return;
  }

  addFastCommands();
}

function mapDesktopChange(file) {
  if (file === 'apps/desktop/package.json' || file.includes('/tsconfig') || file.includes('/vite') || file.includes('/vitest')) {
    add('desktop-typecheck', 'Typecheck desktop app', 'pnpm --filter @km-editor/desktop typecheck');
    add('desktop-tests', 'Run all desktop Vitest tests after desktop config changes', 'pnpm --dir apps/desktop test:run');
    return;
  }

  if (file.startsWith('apps/desktop/src-tauri/')) {
    add('desktop-typecheck', 'Typecheck desktop app', 'pnpm --filter @km-editor/desktop typecheck');
    return;
  }

  if (file.endsWith('.test.ts') || file.endsWith('.test.tsx')) {
    add(`desktop-test:${file}`, `Run changed desktop test ${file}`, `pnpm --dir apps/desktop test:run ${toDesktopPath(file)} ${appTimeout}`);
    return;
  }

  if (file.startsWith('apps/desktop/src/bridge/')) {
    add('desktop-typecheck', 'Typecheck desktop app', 'pnpm --filter @km-editor/desktop typecheck');
    add('desktop-bridge-tests', 'Run desktop bridge tests', `pnpm --dir apps/desktop test:run src/bridge/contracts.test.ts src/bridge/projectBridge.test.ts ${appTimeout}`);
    add('integration-dispatcher', 'Run bridge dispatcher integration tests', dotnetProject('tests/KM.Integration.Tests/KM.Integration.Tests.csproj', 'FullyQualifiedName~ProjectBridgeDispatcherTests'));
    return;
  }

  add('desktop-typecheck', 'Typecheck desktop app', 'pnpm --filter @km-editor/desktop typecheck');

  if (file === 'apps/desktop/src/App.tsx' || file.startsWith('apps/desktop/src/testSupport/')) {
    add('desktop-app-tests', 'Run App regression tests after shell or shared fixture changes', `pnpm --dir apps/desktop test:run src/App.test.tsx ${appTimeout}`);
    return;
  }

  for (const [segment, filter] of appFeatureFilters) {
    if (file.includes(`/features/${segment}/`)) {
      const nearbyTest = nearbyDesktopTest(file);
      if (nearbyTest) {
        add(`desktop-test:${nearbyTest}`, `Run nearby desktop feature test ${nearbyTest}`, `pnpm --dir apps/desktop test:run ${nearbyTest} ${appTimeout}`);
      } else {
        add(`desktop-app-feature:${segment}`, `Run App tests for ${filter}`, `pnpm --dir apps/desktop test:run src/App.test.tsx -t "${filter}" ${appTimeout}`);
      }
      return;
    }
  }

  add('desktop-app-smoke', 'Run App shell smoke tests', `pnpm --dir apps/desktop test:run src/App.test.tsx -t "project workbench shell|switches workbench sections" ${appTimeout}`);
}

function mapSwShChange(file) {
  const changedTestFilter = testFilterForChangedTest(file);
  if (changedTestFilter) {
    add(`swsh-test:${changedTestFilter}`, `Run changed Sword and Shield test ${changedTestFilter}`, dotnetProject('tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj', changedTestFilter));
    return;
  }

  const feature = getPathPartAfter(file, file.startsWith('src/KM.SwSh/') ? 'src/KM.SwSh/' : 'tests/KM.SwSh.Tests/');
  const filterText = swshFeatureFilters.get(feature);

  if (filterText) {
    add(`swsh-feature:${feature}`, `Run Sword and Shield ${feature} tests`, dotnetProject('tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj', expandFilter(filterText)));
    if (['DynamaxAdventures', 'FairyGymBoosts', 'RoyalCandy', 'TypeChart'].includes(feature)) {
      add(
        `integration-feature:${feature}`,
        `Run ${feature} bridge integration tests`,
        dotnetProject('tests/KM.Integration.Tests/KM.Integration.Tests.csproj', expandFilter(filterText)),
      );
    }
    return;
  }

  add('swsh-tests', 'Run all Sword and Shield tests', dotnetProject('tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj'));
}

function mapIntegrationChange(file) {
  const changedTestFilter = testFilterForChangedTest(file);
  if (changedTestFilter) {
    add(`integration-test:${changedTestFilter}`, `Run changed integration test ${changedTestFilter}`, dotnetProject('tests/KM.Integration.Tests/KM.Integration.Tests.csproj', changedTestFilter));
    return;
  }

  if (file.includes('DynamaxAdventure')) {
    add('integration-da', 'Run Dynamax Adventures integration tests', dotnetProject('tests/KM.Integration.Tests/KM.Integration.Tests.csproj', 'FullyQualifiedName~DynamaxAdventure'));
    return;
  }

  if (file.includes('Bridge')) {
    add('integration-bridge', 'Run bridge integration tests', dotnetProject('tests/KM.Integration.Tests/KM.Integration.Tests.csproj', 'FullyQualifiedName~Bridge|FullyQualifiedName~ProjectBridgeDispatcherTests'));
    return;
  }

  add('integration-tests', 'Run all integration tests', dotnetProject('tests/KM.Integration.Tests/KM.Integration.Tests.csproj'));
}

function mapTestAssemblyTraitChange(file) {
  if (file.startsWith('tests/KM.Core.Tests/')) {
    add('core-layer-trait', 'Verify core unit test layer trait', dotnetProject('tests/KM.Core.Tests/KM.Core.Tests.csproj', 'Layer=Unit'));
    return;
  }

  if (file.startsWith('tests/KM.Formats.Tests/')) {
    add('formats-layer-trait', 'Verify format test layer trait', dotnetProject('tests/KM.Formats.Tests/KM.Formats.Tests.csproj', 'Layer=Format'));
    return;
  }

  if (file.startsWith('tests/KM.SwSh.Tests/')) {
    add(
      'swsh-layer-trait',
      'Verify Sword and Shield workflow layer trait',
      dotnetProject('tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj', 'Layer=Workflow&FullyQualifiedName~DynamaxAdventure'),
    );
    return;
  }

  if (file.startsWith('tests/KM.Integration.Tests/')) {
    add(
      'integration-layer-trait',
      'Verify integration test layer trait',
      dotnetProject('tests/KM.Integration.Tests/KM.Integration.Tests.csproj', 'Layer=Integration&FullyQualifiedName~BridgeJson'),
    );
  }
}

function add(key, label, command) {
  if (commandKeys.has(key) || commandKeys.has(command)) {
    return;
  }

  commandKeys.add(key);
  commandKeys.add(command);
  commands.push({ label, command });
}

function dotnetProject(project, filter) {
  if (!filter) {
    return `dotnet test ${project} --no-restore ${dotnetLogger}`;
  }

  return `dotnet test ${project} --no-restore --filter "${filter}" ${dotnetLogger}`;
}

function expandFilter(text) {
  return text
    .split('|')
    .map((part) => part.trim())
    .filter(Boolean)
    .map((part) => `FullyQualifiedName~${part}`)
    .join('|');
}

function testFilterForChangedTest(file) {
  if (!file.endsWith('.cs')) {
    return '';
  }

  const name = path.basename(file, '.cs');
  if (!name.endsWith('Tests')) {
    return '';
  }

  return `FullyQualifiedName~${name}`;
}

function nearbyDesktopTest(file) {
  const desktopPath = toDesktopPath(file);
  const parsed = path.posix.parse(desktopPath);
  const candidate = `${parsed.dir}/${parsed.name}.test${parsed.ext}`;
  if (execStatus(`git ls-files --error-unmatch apps/desktop/${candidate}`) === 0) {
    return candidate;
  }

  return '';
}

function toDesktopPath(file) {
  return file.replace(/^apps\/desktop\//, '');
}

function getPathPartAfter(file, prefix) {
  const rest = file.slice(prefix.length);
  return rest.split('/')[0] ?? '';
}

function getChangedFiles() {
  const files = new Set();
  const mergeBase = execCapture('git merge-base HEAD origin/master').trim() || 'HEAD';
  const commandsToRead = [
    `git diff --name-only --diff-filter=ACMRTUXB ${mergeBase}...HEAD`,
    'git diff --name-only --diff-filter=ACMRTUXB',
    'git diff --cached --name-only --diff-filter=ACMRTUXB',
    'git ls-files --others --exclude-standard',
  ];

  for (const command of commandsToRead) {
    for (const file of execCapture(command).split(/\r?\n/)) {
      const normalized = file.trim().replaceAll('\\', '/');
      if (normalized) {
        files.add(normalized);
      }
    }
  }

  return [...files].sort((a, b) => a.localeCompare(b));
}

function isIgnored(file) {
  if (ignoredExact.has(file)) {
    return true;
  }

  return ignoredPrefixes.some((prefix) => file === prefix.slice(0, -1) || file.startsWith(prefix));
}

function execCapture(command) {
  const result = spawnSync(command, {
    cwd: repoRoot || process.cwd(),
    encoding: 'utf8',
    shell: true,
  });

  if (result.status !== 0) {
    return '';
  }

  return result.stdout;
}

function execStatus(command) {
  const result = spawnSync(command, {
    cwd: repoRoot,
    encoding: 'utf8',
    shell: true,
    stdio: 'ignore',
  });

  return result.status ?? 1;
}
