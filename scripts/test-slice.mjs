#!/usr/bin/env node
// SPDX-License-Identifier: GPL-3.0-only

import { spawn, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';

const rawArgs = process.argv.slice(2);
const positionalArgs = rawArgs.filter((arg) => !arg.startsWith('-'));
const modeArg = positionalArgs[0] ?? 'changed';
const shardArg = positionalArgs[1] ?? '';
const flags = new Set(rawArgs.filter((arg) => arg.startsWith('-')));
const printOnly = flags.has('--print') || flags.has('--dry-run');

let repoRoot = process.cwd();
repoRoot = execCapture('git rev-parse --show-toplevel').trim();
if (!repoRoot) {
  console.error('Could not find the git repository root.');
  process.exit(1);
}

process.chdir(repoRoot);

const budgets = JSON.parse(readFileSync(path.join(repoRoot, 'scripts/test-budgets.json'), 'utf8'));
const totalBudgetMs = positiveInteger(process.env.KM_TEST_TOTAL_TIMEOUT_MS, budgets.totalMs);
const commandBudgetMs = positiveInteger(process.env.KM_TEST_COMMAND_TIMEOUT_MS, budgets.commandMs);
const maxLocalWorkers = positiveInteger(process.env.KM_TEST_MAX_WORKERS, budgets.maxLocalWorkers);
const runBudgetMs = modeArg === 'shard' && budgets.shards[shardArg]
  ? Math.min(totalBudgetMs, budgets.shards[shardArg])
  : totalBudgetMs;
const timingReportPath = path.join(repoRoot, 'TestResults', 'test-timings.json');
const timingEntries = [];
const activeChildren = new Set();
let totalStarted = 0;

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => {
    terminateActiveChildren();
    process.exit(signal === 'SIGINT' ? 130 : 143);
  });
}

const commands = [];
const commandKeys = new Set();

const dotnetLogger = '--logger "console;verbosity=minimal"';
const appTimeout = '--testTimeout=30000';
const tauriRustTests = 'powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-tauri-rust-tests.ps1';
const broadChangeFileThreshold = 20;
const swshHookFilters = Object.freeze({
  royalAll: 'FullyQualifiedName~SwShHookReservationTests&FullyQualifiedName~RoyalCandy',
  royalCleanup: 'FullyQualifiedName~SwShHookReservationTests&FullyQualifiedName~RoyalCandy&FullyQualifiedName~Cleanup',
  royalBehavior: 'FullyQualifiedName~SwShHookReservationTests&FullyQualifiedName~RoyalCandy&FullyQualifiedName!~Cleanup',
  otherAll: 'FullyQualifiedName~SwShHookReservationTests&FullyQualifiedName!~RoyalCandy',
  otherCleanup: 'FullyQualifiedName~SwShHookReservationTests&FullyQualifiedName!~RoyalCandy&FullyQualifiedName~Cleanup',
  otherBehavior: 'FullyQualifiedName~SwShHookReservationTests&FullyQualifiedName!~RoyalCandy&FullyQualifiedName!~Cleanup',
});

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

const zaFeatureFilters = new Map([
  [
    'Data',
    'PokemonLegendsZAProjectLoadsPokemonData|PokemonLegendsZAProjectLoadsGiftPokemonData|PokemonLegendsZAProjectLoadsTradePokemonData|PokemonLegendsZAProjectLoadsGameDefaultGiftPokemonSentinels|PokemonLegendsZALoadsSignedDefaultPokemonDataFields',
  ],
  ['DumpImport', 'PokemonLegendsZAGameDumpWritesImplementedCategoryFiles'],
  [
    'Encounters',
    'PokemonLegendsZAWildEncountersEditWritesTrinityEncountDataTable|PokemonLegendsZAWildEncountersSynchronizeSlotsLinkedToTheSameDataRow|PokemonLegendsZAWildEncountersDescribeAlphaAndRawSpawnerLocations|PokemonLegendsZAWildEncountersRejectAlphaChanceForMixedLinkedPlacements|PokemonLegendsZAWildEncountersPreserveFractionalAlphaChanceAsReadOnly|PokemonLegendsZAWildEncountersPreserveUnsafeAlphaLevelBonusAsReadOnly',
  ],
  [
    'EvolutionItems',
    'PokemonLegendsZAEvolutionItemsUseEligibleItemIds|PokemonLegendsZAEvolutionItemAllocationUsesOnlyApprovedPriorityTiers|PokemonLegendsZAEvolutionItemAllocationProtectsActivePersonalParameters|PokemonLegendsZAEvolutionItemAllocationFailsClosedWithoutReadablePersonalData|PokemonLegendsZAEvolutionItemBatchAllocationSortsByItemId|PokemonLegendsZAEvolutionItemConversionRejectsExistingDirectUseEffects|PokemonLegendsZAEvolutionItemConversionRejectsPendingDirectUseEffects|PokemonLegendsZAItemEditWritesTrinityItemTable|PokemonLegendsZALegacyMintSentinelOutputIsRecoveredBeforeItemApply|PokemonLegendsZAPartialLegacyMintSentinelPatternFailsClosed|PokemonLegendsZADisablingEvolutionItemRetainsItsAllocatedMapping|PokemonLegendsZAEvolutionItemCapacityFailurePlansNoWrites|PokemonLegendsZAMalformedEvolutionItemTablePlansNoWrites|PokemonLegendsZAHeldItemEvolutionsUseItemIds|PokemonLegendsZAReservedParameter50DisplaysHistoricalRazorFang|PokemonLegendsZAConversionBackedUseItemMethodsRoundTripThroughConversionTable|PokemonLegendsZALegacyRawEvolutionItemArgumentsAreMigrated',
  ],
  ['ExeFs', 'PokemonLegendsZATypeChart'],
  ['GameDump', 'PokemonLegendsZAGameDumpWritesImplementedCategoryFiles'],
  [
    'Gifts',
    'PokemonLegendsZAProjectLoadsGiftPokemonData|PokemonLegendsZAProjectLoadsGameDefaultGiftPokemonSentinels|PokemonLegendsZAGiftPokemonEditWritesTrinityPokemonDataTable|PokemonLegendsZAUnavailableSpeciesUpdatesAreRejectedOutsidePokemonEditor',
  ],
  [
    'Items',
    'PokemonLegendsZAProjectLoadsItemData|PokemonLegendsZAItemEditWritesTrinityItemTable|PokemonLegendsZALegacyMintSentinelOutputIsRecoveredBeforeItemApply|PokemonLegendsZAPartialLegacyMintSentinelPatternFailsClosed|PokemonLegendsZAEvolutionItemAllocationUsesOnlyApprovedPriorityTiers|PokemonLegendsZAEvolutionItemAllocationProtectsActivePersonalParameters|PokemonLegendsZAEvolutionItemAllocationFailsClosedWithoutReadablePersonalData|PokemonLegendsZAEvolutionItemBatchAllocationSortsByItemId|PokemonLegendsZAEvolutionItemConversionRejectsExistingDirectUseEffects|PokemonLegendsZAEvolutionItemConversionRejectsPendingDirectUseEffects|PokemonLegendsZADisablingEvolutionItemRetainsItsAllocatedMapping|PokemonLegendsZAEvolutionItemCapacityFailurePlansNoWrites|PokemonLegendsZAMalformedEvolutionItemTablePlansNoWrites',
  ],
  ['ModMerger', 'PokemonLegendsZAModMergerStagesAndAppliesTrinityRomFsMods'],
  ['Moves', 'PokemonLegendsZAProjectLoadsMoveData|PokemonLegendsZAMoveEditWritesTrinityMoveTable'],
  ['Placement', 'PokemonLegendsZAPlacementEditWritesSpawnerTransformTable'],
  [
    'Pokemon',
    'PokemonLegendsZAProjectLoadsPokemonData|PokemonLegendsZAPokemonEditWritesStandalonePersonalTable|PokemonLegendsZANonPokemonSpeciesPickersExcludeUnavailablePokemon|PokemonLegendsZAUnavailableSpeciesUpdatesAreRejectedOutsidePokemonEditor|PokemonLegendsZAEvolutionItemsUseEligibleItemIds|PokemonLegendsZAHeldItemEvolutionsUseItemIds|PokemonLegendsZAReservedParameter50DisplaysHistoricalRazorFang|PokemonLegendsZAConversionBackedUseItemMethodsRoundTripThroughConversionTable|PokemonLegendsZALegacyRawEvolutionItemArgumentsAreMigrated|PokemonLegendsZAEvolutionItemAllocationProtectsActivePersonalParameters',
  ],
  ['Shops', 'PokemonLegendsZAProjectLoadsShopData|PokemonLegendsZAShopEditWritesTrinityLineupTable'],
  ['StaticEncounters', 'PokemonLegendsZAStaticEncountersEditWritesTrinityEncountDataTable'],
  ['Trainers', 'PokemonLegendsZAProjectLoadsTrainerData|PokemonLegendsZATrainerEditWritesTrinityTrainerTable'],
  [
    'Trades',
    'PokemonLegendsZAProjectLoadsTradePokemonData|PokemonLegendsZATradePokemonEditWritesTrinityPokemonDataTable|PokemonLegendsZAUnavailableSpeciesUpdatesAreRejectedOutsidePokemonEditor',
  ],
  ['TypeChart', 'PokemonLegendsZATypeChart'],
  ['Workflows', 'PokemonLegendsZA|ZaCacheManagerTests'],
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
const ignoredExtensions = new Set(['.docx', '.pdf', '.pyc']);

if (!['changed', 'fast', 'full', 'shard'].includes(modeArg)) {
  console.error(`Unknown test slice "${modeArg}". Use changed, fast, full, or shard.`);
  process.exit(1);
}

if (modeArg === 'fast') {
  addFastCommands();
} else if (modeArg === 'full') {
  addFullCommands();
} else if (modeArg === 'shard') {
  addShardCommands(shardArg);
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
  if (item.kind === 'parallel') {
    for (const child of item.commands) {
      console.log(`   - ${child.label}`);
      console.log(`     ${child.command}`);
    }
  } else {
    console.log(`   ${item.command}`);
  }
}

if (printOnly) {
  process.exit(0);
}

totalStarted = Date.now();
const exitCode = await runSelectedCommands();
const totalElapsedMs = Date.now() - totalStarted;
writeTimingReport(totalElapsedMs, exitCode);
console.log(`total elapsed: ${(totalElapsedMs / 1000).toFixed(2)}s / ${(runBudgetMs / 1000).toFixed(0)}s budget`);
process.exit(exitCode);

function addFastCommands() {
  add('workspace-path-check', 'Check workspace path hygiene', 'node scripts/check-workspace-paths.mjs');
  add('diff-check', 'Check whitespace and patch hygiene', 'git diff --check');
  addParallel('fast-tests', 'Run fast validation shards', [
    { label: 'Typecheck desktop app', command: 'pnpm --filter @km-editor/desktop typecheck' },
    {
      label: 'Run focused desktop unit and bridge tests',
      command: `pnpm --dir apps/desktop test:run src/AppErrorBoundary.test.tsx src/diagnostics.test.ts src/errorReporting.test.ts src/pokemonSprites.test.ts src/workbenchStore.test.ts src/bridge/contracts.test.ts src/bridge/projectBridge.test.ts src/features/fairy-gym-boosts/FairyGymBoostsSection.test.tsx ${appTimeout}`,
    },
    {
      label: 'Run App shell and advanced editor smoke tests',
      command: `pnpm --dir apps/desktop test:run src/App.test.tsx -t "project workbench shell|workflow categories|Dynamax Adventures|Bag Hook|Catch Cap" ${appTimeout}`,
    },
    { label: 'Run core backend tests', command: dotnetProject('tests/KM.Core.Tests/KM.Core.Tests.csproj') },
    { label: 'Run format backend tests', command: dotnetProject('tests/KM.Formats.Tests/KM.Formats.Tests.csproj') },
    {
      label: 'Run high risk Sword and Shield workflow smoke tests',
      command: dotnetProject(
        'tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj',
        'FullyQualifiedName~DynamaxAdventure|FullyQualifiedName~ReservedMainRegionsDoNotOverlapBetweenFeatureFamiliesInSameSegment|FullyQualifiedName~SwShRoyalCandyWorkflowServiceTests|FullyQualifiedName~TypeChartWorkflowTests|FullyQualifiedName~SwShFairyGymBoostsBseqPatcherTests',
      ),
    },
    {
      label: 'Run bridge serialization and Dynamax Adventures integration smoke tests',
      command: dotnetProject(
        'tests/KM.Integration.Tests/KM.Integration.Tests.csproj',
        'FullyQualifiedName~BridgeJson|FullyQualifiedName~BridgeResponse|FullyQualifiedName~DynamaxAdventure',
      ),
    },
  ]);
}

function addFullCommands() {
  add('workspace-path-check', 'Check workspace path hygiene', 'node scripts/check-workspace-paths.mjs');
  add('diff-check', 'Check whitespace and patch hygiene', 'git diff --check');
  add('backend-build', 'Build backend test projects once', 'dotnet build KM.Editor.slnx --no-restore --nologo');
  addParallel('full-tests', 'Run full validation shards', [
    { label: 'Typecheck desktop app', command: 'pnpm --filter @km-editor/desktop typecheck' },
    { label: 'Run all desktop Vitest tests', command: `pnpm --dir apps/desktop test:run ${appTimeout}` },
    { label: 'Run native desktop Rust tests', command: tauriRustTests },
    { label: 'Run core backend tests', command: dotnetProject('tests/KM.Core.Tests/KM.Core.Tests.csproj', '', { noBuild: true }) },
    { label: 'Run format backend tests', command: dotnetProject('tests/KM.Formats.Tests/KM.Formats.Tests.csproj', '', { noBuild: true }) },
    {
      label: 'Run Sword and Shield general tests',
      command: dotnetProject('tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj', 'FullyQualifiedName!~SwShHookReservationTests&Kind!=Slow', { noBuild: true }),
    },
    {
      label: 'Run Sword and Shield other cleanup hook tests',
      command: dotnetProject(
        'tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj',
        swshHookFilters.otherCleanup,
        { noBuild: true },
      ),
    },
    {
      label: 'Run Sword and Shield other behavior hook tests',
      command: dotnetProject(
        'tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj',
        swshHookFilters.otherBehavior,
        { noBuild: true },
      ),
    },
    {
      label: 'Run Scarlet and Violet integration tests',
      command: dotnetProject('tests/KM.Integration.Tests/KM.Integration.Tests.csproj', 'FullyQualifiedName~ScarletViolet|FullyQualifiedName~.SV.', { noBuild: true }),
    },
    {
      label: 'Run Pokemon Legends Z-A integration tests',
      command: dotnetProject('tests/KM.Integration.Tests/KM.Integration.Tests.csproj', 'FullyQualifiedName~PokemonLegendsZA|FullyQualifiedName~.ZA.', { noBuild: true }),
    },
    {
      label: 'Run shared bridge integration tests',
      command: dotnetProject(
        'tests/KM.Integration.Tests/KM.Integration.Tests.csproj',
        'FullyQualifiedName!~ScarletViolet&FullyQualifiedName!~.SV.&FullyQualifiedName!~PokemonLegendsZA&FullyQualifiedName!~.ZA.',
        { noBuild: true },
      ),
    },
    {
      label: 'Run performance baselines',
      command: dotnetProject('tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj', 'Kind=Slow', { noBuild: true }),
    },
  ]);
}

function addShardCommands(shard) {
  const knownShards = Object.keys(budgets.shards);
  if (!knownShards.includes(shard)) {
    console.error(`Unknown test shard "${shard}". Use one of: ${knownShards.join(', ')}.`);
    process.exit(1);
  }

  add('workspace-path-check', 'Check workspace path hygiene', 'node scripts/check-workspace-paths.mjs');
  add('diff-check', 'Check whitespace and patch hygiene', 'git diff --check');

  if (shard === 'desktop') {
    addParallel('desktop-shard', 'Run desktop validation', [
      { label: 'Typecheck desktop app', command: 'pnpm --filter @km-editor/desktop typecheck' },
      { label: 'Run all desktop Vitest tests', command: `pnpm --dir apps/desktop test:run ${appTimeout}` },
      { label: 'Run native desktop Rust tests', command: tauriRustTests },
    ]);
    return;
  }

  if (shard === 'core-formats') {
    addParallel('core-formats-shard', 'Run core and format tests', [
      { label: 'Run core backend tests', command: dotnetProject('tests/KM.Core.Tests/KM.Core.Tests.csproj') },
      { label: 'Run format backend tests', command: dotnetProject('tests/KM.Formats.Tests/KM.Formats.Tests.csproj') },
    ]);
    return;
  }

  if (shard === 'swsh-general') {
    add('swsh-general', 'Run Sword and Shield general tests', dotnetProject(
      'tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj',
      'FullyQualifiedName!~SwShHookReservationTests&Kind!=Slow',
    ));
    return;
  }

  if (shard === 'swsh-hooks') {
    add(
      'swsh-hooks-build',
      'Build Sword and Shield hook tests once',
      'dotnet build tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj --no-restore --nologo',
    );
    addParallel('swsh-hooks', 'Run Sword and Shield hook coexistence tests', [
      {
        label: 'Run Royal Candy hook coexistence tests',
        command: dotnetProject(
          'tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj',
          swshHookFilters.royalAll,
          { noBuild: true },
        ),
      },
      {
        label: 'Run remaining hook coexistence tests',
        command: dotnetProject(
          'tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj',
          swshHookFilters.otherAll,
          { noBuild: true },
        ),
      },
    ]);
    return;
  }

  if (shard === 'swsh-hooks-royal') {
    add('swsh-hooks-royal', 'Run Royal Candy hook coexistence tests', dotnetProject(
      'tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj',
      swshHookFilters.royalAll,
    ));
    return;
  }

  if (shard === 'swsh-hooks-royal-cleanup') {
    add('swsh-hooks-royal-cleanup', 'Run Royal Candy cleanup hook tests', dotnetProject(
      'tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj',
      swshHookFilters.royalCleanup,
    ));
    return;
  }

  if (shard === 'swsh-hooks-royal-behavior') {
    add('swsh-hooks-royal-behavior', 'Run Royal Candy behavior hook tests', dotnetProject(
      'tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj',
      swshHookFilters.royalBehavior,
    ));
    return;
  }

  if (shard === 'swsh-hooks-other') {
    add('swsh-hooks-other', 'Run remaining hook coexistence tests', dotnetProject(
      'tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj',
      swshHookFilters.otherAll,
    ));
    return;
  }

  if (shard === 'swsh-hooks-other-cleanup') {
    add('swsh-hooks-other-cleanup', 'Run other cleanup hook tests', dotnetProject(
      'tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj',
      swshHookFilters.otherCleanup,
    ));
    return;
  }

  if (shard === 'swsh-hooks-other-behavior') {
    add('swsh-hooks-other-behavior', 'Run other behavior hook tests', dotnetProject(
      'tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj',
      swshHookFilters.otherBehavior,
    ));
    return;
  }

  if (shard === 'integration-sv') {
    add('integration-sv', 'Run Scarlet and Violet integration tests', dotnetProject(
      'tests/KM.Integration.Tests/KM.Integration.Tests.csproj',
      'FullyQualifiedName~ScarletViolet|FullyQualifiedName~.SV.',
    ));
    return;
  }

  if (shard === 'performance') {
    add('performance', 'Run performance baselines', dotnetProject(
      'tests/KM.SwSh.Tests/KM.SwSh.Tests.csproj',
      'Kind=Slow',
    ));
    return;
  }

  if (shard === 'integration-za') {
    add('integration-za', 'Run Pokemon Legends Z-A integration tests', dotnetProject(
      'tests/KM.Integration.Tests/KM.Integration.Tests.csproj',
      'FullyQualifiedName~PokemonLegendsZA|FullyQualifiedName~.ZA.',
    ));
    return;
  }

  add('integration-bridge', 'Run shared bridge integration tests', dotnetProject(
    'tests/KM.Integration.Tests/KM.Integration.Tests.csproj',
    'FullyQualifiedName!~ScarletViolet&FullyQualifiedName!~.SV.&FullyQualifiedName!~PokemonLegendsZA&FullyQualifiedName!~.ZA.',
  ));
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

  if (relevantFiles.length >= broadChangeFileThreshold) {
    console.log(
      `Broad change detected (${relevantFiles.length} relevant files); using the bounded full validation plan instead of accumulating overlapping focused commands.`,
    );
    addFullCommands();
    return;
  }

  for (const file of relevantFiles) {
    mapChangedFile(file);
  }
}

function mapChangedFile(file) {
  if (file === 'scripts/run-tauri-rust-tests.ps1') {
    add('tauri-rust-tests', 'Run native desktop Rust tests', tauriRustTests);
    return;
  }

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

  if (file.startsWith('src/KM.ZA/') || file.startsWith('tests/KM.Integration.Tests/ZA/')) {
    mapZaChange(file);
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
    add('desktop-tests', 'Run all desktop Vitest tests after desktop config changes', `pnpm --dir apps/desktop test:run ${appTimeout}`);
    return;
  }

  if (file.startsWith('apps/desktop/src-tauri/')) {
    add('desktop-typecheck', 'Typecheck desktop app', 'pnpm --filter @km-editor/desktop typecheck');
    add('tauri-rust-tests', 'Run native desktop Rust tests', tauriRustTests);
    return;
  }

  if (file.endsWith('.test.ts') || file.endsWith('.test.tsx')) {
    if (!existsSync(path.join(repoRoot, file))) {
      add('desktop-typecheck', 'Typecheck desktop app', 'pnpm --filter @km-editor/desktop typecheck');
      add(
        'desktop-tests',
        'Run all desktop Vitest tests after a desktop test was removed',
        `pnpm --dir apps/desktop test:run ${appTimeout}`,
      );
      return;
    }

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

  const nearbyTest = nearbyDesktopTest(file);
  if (nearbyTest) {
    add(`desktop-test:${nearbyTest}`, `Run nearby desktop test ${nearbyTest}`, `pnpm --dir apps/desktop test:run ${nearbyTest} ${appTimeout}`);
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

function mapZaChange(file) {
  const changedTestFilter = testFilterForChangedTest(file);
  if (changedTestFilter) {
    add(`za-test:${changedTestFilter}`, `Run changed Pokemon Legends Z-A test ${changedTestFilter}`, dotnetProject('tests/KM.Integration.Tests/KM.Integration.Tests.csproj', changedTestFilter));
    return;
  }

  if (file.includes('ZaCacheManager')) {
    add('za-cache-tests', 'Run Pokemon Legends Z-A cache manager tests', dotnetProject('tests/KM.Integration.Tests/KM.Integration.Tests.csproj', 'FullyQualifiedName~ZaCacheManagerTests'));
    return;
  }

  const feature = getPathPartAfter(file, file.startsWith('src/KM.ZA/') ? 'src/KM.ZA/' : 'tests/KM.Integration.Tests/ZA/');
  const filterText = zaFeatureFilters.get(feature);

  if (filterText) {
    add(`za-feature:${feature}`, `Run Pokemon Legends Z-A ${feature} tests`, dotnetProject('tests/KM.Integration.Tests/KM.Integration.Tests.csproj', expandFilter(filterText)));
    return;
  }

  add('za-integration-tests', 'Run Pokemon Legends Z-A integration tests', dotnetProject('tests/KM.Integration.Tests/KM.Integration.Tests.csproj', 'FullyQualifiedName~PokemonLegendsZA|FullyQualifiedName~ZaCacheManagerTests'));
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

function addParallel(key, label, childCommands) {
  if (commandKeys.has(key)) {
    return;
  }

  const uniqueChildren = [];
  for (const child of childCommands) {
    if (commandKeys.has(child.command)) {
      continue;
    }

    commandKeys.add(child.command);
    uniqueChildren.push(child);
  }

  if (uniqueChildren.length === 0) {
    return;
  }

  commandKeys.add(key);
  commands.push({ kind: 'parallel', label, commands: uniqueChildren });
}

async function runSelectedCommands() {
  for (const item of commands) {
    if (item.kind === 'parallel') {
      const status = await runParallelGroup(item);
      if (status !== 0) {
        return status;
      }
      continue;
    }

    console.log('');
    console.log(`> ${item.label}`);
    const result = await runCommandStreaming(item.command, item.label, availableCommandBudgetMs());
    recordTiming(item.label, result);
    const elapsedSeconds = (result.elapsedMs / 1000).toFixed(2);
    console.log(`elapsed: ${elapsedSeconds}s`);

    if (result.status !== 0) {
      terminateActiveChildren();
      return result.status ?? 1;
    }
  }

  return 0;
}

async function runParallelGroup(item) {
  console.log('');
  console.log(`> ${item.label}`);
  const groupStarted = Date.now();
  const running = new Set(item.commands.map((child) => child.label));
  const heartbeat = setInterval(() => {
    if (running.size > 0) {
      console.log(`still running: ${[...running].join(', ')}`);
    }
  }, 30000);

  const results = [];
  let nextIndex = 0;
  let failedStatus = 0;
  const workerCount = Math.min(maxLocalWorkers, item.commands.length);

  async function runWorker() {
    while (failedStatus === 0) {
      const index = nextIndex;
      nextIndex += 1;
      if (index >= item.commands.length) {
        return;
      }

      const child = item.commands[index];
      console.log(`starting: ${child.label}`);
      const result = await runCommandStreaming(child.command, child.label, availableCommandBudgetMs());
      running.delete(child.label);
      const elapsedSeconds = (result.elapsedMs / 1000).toFixed(2);
      console.log(`finished: ${child.label} (${elapsedSeconds}s)`);
      recordTiming(child.label, result);
      results[index] = { ...child, ...result, elapsedSeconds };

      if (result.status !== 0) {
        failedStatus = result.status ?? 1;
        terminateActiveChildren();
      }
    }
  }

  await Promise.all(Array.from({ length: workerCount }, () => runWorker()));

  clearInterval(heartbeat);

  const elapsedSeconds = ((Date.now() - groupStarted) / 1000).toFixed(2);
  console.log(`parallel elapsed: ${elapsedSeconds}s`);
  return failedStatus;
}

function runCommandStreaming(command, label, timeoutMs) {
  const started = Date.now();
  return new Promise((resolve) => {
    const child = spawn(command, {
      cwd: repoRoot,
      shell: true,
      env: process.env,
      stdio: 'inherit',
      detached: process.platform !== 'win32',
    });
    activeChildren.add(child);
    let timedOut = false;
    let settled = false;
    const timeout = setTimeout(() => {
      timedOut = true;
      console.error(`${label} exceeded its ${(timeoutMs / 1000).toFixed(0)}s budget. Terminating its process tree.`);
      terminateProcessTree(child);
    }, timeoutMs);

    const finish = (status) => {
      if (settled) {
        return;
      }

      settled = true;
      clearTimeout(timeout);
      activeChildren.delete(child);
      resolve({
        status: timedOut ? 124 : (status ?? 1),
        elapsedMs: Date.now() - started,
        timedOut,
      });
    };

    child.on('error', (error) => {
      console.error(`${label} failed to start: ${error.message}`);
      finish(1);
    });
    child.on('close', finish);
  });
}

function availableCommandBudgetMs() {
  const elapsedMs = totalStarted === 0 ? 0 : Date.now() - totalStarted;
  return Math.max(1, Math.min(commandBudgetMs, runBudgetMs - elapsedMs));
}

function recordTiming(label, result) {
  timingEntries.push({
    label,
    elapsedMs: result.elapsedMs,
    status: result.status,
    timedOut: result.timedOut,
  });
}

function writeTimingReport(totalElapsedMs, status) {
  mkdirSync(path.dirname(timingReportPath), { recursive: true });
  writeFileSync(timingReportPath, `${JSON.stringify({
    schemaVersion: 1,
    mode: modeArg,
    shard: shardArg || null,
    budgetMs: runBudgetMs,
    totalElapsedMs,
    status,
    commands: timingEntries,
  }, null, 2)}\n`);
}

function terminateActiveChildren() {
  for (const child of [...activeChildren]) {
    terminateProcessTree(child);
  }
}

function terminateProcessTree(child) {
  if (!child || child.pid === undefined || child.exitCode !== null) {
    return;
  }

  if (process.platform === 'win32') {
    spawnSync('taskkill', ['/pid', String(child.pid), '/T', '/F'], { stdio: 'ignore' });
    return;
  }

  try {
    process.kill(-child.pid, 'SIGKILL');
  } catch {
    child.kill('SIGKILL');
  }
}

function positiveInteger(value, fallback) {
  const parsed = Number.parseInt(String(value ?? ''), 10);
  return Number.isInteger(parsed) && parsed > 0 ? parsed : fallback;
}

function dotnetProject(project, filter, options = {}) {
  const noBuild = options.noBuild ? ' --no-build' : '';
  if (!filter) {
    return `dotnet test ${project} --no-restore${noBuild} ${dotnetLogger}`;
  }

  return `dotnet test ${project} --no-restore${noBuild} --filter "${filter}" ${dotnetLogger}`;
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
    `git diff --name-only --diff-filter=ACMRTUXBD ${mergeBase}...HEAD`,
    'git diff --name-only --diff-filter=ACMRTUXBD',
    'git diff --cached --name-only --diff-filter=ACMRTUXBD',
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

  if (ignoredExtensions.has(path.extname(file).toLowerCase())) {
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
