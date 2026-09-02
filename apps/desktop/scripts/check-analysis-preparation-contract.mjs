// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

function read(relativePath) {
  return readFileSync(new URL(relativePath, import.meta.url), 'utf8').replace(/\r\n?/g, '\n');
}

const model = read('../src/features/workbench/analysisPreparation.ts');
assert.match(
  model,
  /analysisLoadingModes = \['minimal', 'balanced', 'performance'\]/,
  'Analysis loading must use the same three user-facing mode identities as each game cache.'
);
assert.match(
  model,
  /value === 'reduced'\) return 'minimal';[\s\S]*?value === 'fastest'\) return 'performance';/,
  'Legacy analysis loading preferences must migrate to Minimal and Performance.'
);
for (const unit of [
  'balanceLab: 1',
  'gameModules: 1',
  'guidedDesign: 1',
  'researchLab: 2',
  'semanticMerge: 1',
  'semanticProject: 2'
]) {
  assert.ok(model.includes(unit), `Analysis preparation lost measured unit ${unit}.`);
}
assert.match(
  model,
  /const measured = measureRealQueryUnits\(statuses\)/,
  'Analysis preparation must count completed operations from real ready query states.'
);
assert.match(
  model,
  /allRequiredToolsReady \? 100 : Math\.min\(99, measuredPercent\)/,
  'Analysis preparation must reserve 100 percent for complete readiness.'
);
assert.match(
  model,
  /current\.scopeKey === options\.scopeKey[\s\S]*?createAnalysisPreparationScopeState\(options\)/,
  'A changed project or source revision must synchronously resolve to fresh preparation state.'
);

const hook = read('../src/features/workbench/useAnalysisPreparation.ts');
assert.match(
  hook,
  /const visibleScopeState = useMemo\([\s\S]*?resolveAnalysisPreparationScopeState/,
  'The preparation hook must hide stale-scope preload state before effects run.'
);
assert.ok(
  [...hook.matchAll(/current\.scopeKey !== scopeKey/g)].length >= 3,
  'Preparation callbacks and deferred mounts must reject stale project scopes.'
);
assert.match(
  hook,
  /const nextTools = nextAnalysisPreloadTools\([\s\S]*?preloadTools: \[\.\.\.current\.preloadTools, \.\.\.additions\]/,
  'Preparation must admit the selected runtime batch without fabricating query progress.'
);
assert.match(
  model,
  /options\.mode === 'performance'[\s\S]*?return order\.filter\(\(tool\) => !options\.preloadTools\.includes\(tool\)\)/,
  'Performance preparation must admit every independent tool together after semantic setup.'
);

const panel = read('../src/features/workbench/AnalysisPreparationPanel.tsx');
assert.match(
  panel,
  /className="work-progress-track"[\s\S]*?role="progressbar"/,
  'The preparation panel must use the determinate KM progress track.'
);
assert.match(
  panel,
  /style=\{\{ width: `\$\{snapshot\.percent\}%` \}\}/,
  'The visible progress width must come from the measured snapshot.'
);
assert.ok(
  !panel.includes('work-progress-indeterminate'),
  'Measured analysis preparation must not use a scrolling indeterminate animation.'
);
assert.match(
  panel,
  /aria-valuemax=\{100\}[\s\S]*?aria-valuemin=\{0\}[\s\S]*?aria-valuenow=\{snapshot\.percent\}[\s\S]*?aria-valuetext=/,
  'Measured analysis preparation must expose its real value and localized summary to assistive technology.'
);
const workbenchStyles = read('../src/features/workbench/workbench.css');
assert.match(
  workbenchStyles,
  /\.analysis-preparation-systems \{[\s\S]*?grid-template-columns: repeat\(auto-fit, minmax\(min\(100%, 13rem\), 1fr\)\);/,
  'Analysis status cards must shrink to their actual container at narrow windows and high DPI.'
);
assert.match(
  workbenchStyles,
  /\.analysis-preparation-header \{[\s\S]*?flex-wrap: wrap;/,
  'The analysis status header must wrap instead of clipping localized text at high DPI.'
);
assert.match(
  workbenchStyles,
  /\.analysis-preparation-systems li > small \{[\s\S]*?min-width: 0;[\s\S]*?overflow-wrap: anywhere;/,
  'Long localized analysis states must wrap without forcing horizontal overflow.'
);

const runtimeSystems = new Map([
  ['../src/features/balance-lab/BalanceLabRuntime.tsx', 'balanceLab'],
  ['../src/features/game-modules/GameModulesRuntime.tsx', 'gameModules'],
  ['../src/features/guided-design/GuidedDesignRuntime.tsx', 'guidedDesign'],
  ['../src/features/research-lab/ResearchLabRuntime.tsx', 'researchLab'],
  ['../src/features/semantic-merge/SemanticMergeRuntime.tsx', 'semanticMerge']
]);
for (const [path, system] of runtimeSystems) {
  assert.ok(
    read(path).includes(`preparationProgressFromQueryStatuses('${system}'`),
    `${system} must report its actual controller query status.`
  );
}

const workbench = read('../src/features/workbench/WorkbenchSection.tsx');
assert.match(
  workbench,
  /preparationScopeKeyRef\.current = preparationScopeKey;[\s\S]*?setMountedTools\(new Set/,
  'Workbench must replace, not union, mounted tools when the preparation scope changes.'
);
assert.match(
  workbench,
  /const visibleMountedTools = preparationScopeKeyRef\.current === preparationScopeKey[\s\S]*?visibleMountedTools\.has\(tool\.id\)/,
  'Workbench must stop rendering obsolete-scope preload tools before its reset effect runs.'
);
assert.doesNotMatch(
  workbench,
  /cachePreparation|analysisPreparation/,
  'Workbench must not render project setup preparation status panels.'
);

const app = read('../src/App.tsx');
assert.match(
  app,
  /aria-label=\{t\('settings\.tabs\.label'\)\}[\s\S]*?className="settings-tabs"[\s\S]*?role="tablist"[\s\S]*?settingsTabs\.map/,
  'Settings must expose its existing categories through one accessible tab list.'
);
assert.match(
  app,
  /effectiveActiveSettingsTab === 'analysis'[\s\S]*?role="tabpanel"[\s\S]*?\{analysisLoadingSettings\}/,
  'Analysis loading settings must remain contained inside their own settings tab.'
);
assert.match(
  app,
  /\{translateLiteral\(option\.label\)\}[\s\S]*?translateLiteral\(option\.description\)/,
  'Cache mode labels and descriptions must use the same localized presentation boundary as analysis loading.'
);
const settingsStyles = read('../src/styles.css');
assert.match(
  settingsStyles,
  /\.settings-tabs \{[\s\S]*?overflow-x: auto;[\s\S]*?\.settings-tab\.is-selected/,
  'Settings tabs must remain bounded, horizontally reachable, and visibly selected.'
);
for (const locale of ['de', 'en', 'es', 'fr', 'ru', 'uk', 'zh']) {
  const resource = JSON.parse(read(`../src/localization/resources/${locale}.json`));
  assert.deepEqual(
    [
      resource.keys['analysisLoading.mode.minimal.label'],
      resource.keys['analysisLoading.mode.balanced.label'],
      resource.keys['analysisLoading.mode.performance.label']
    ],
    [
      resource.literals.Minimal,
      resource.literals.Balanced,
      resource.literals.Performance
    ],
    `${locale} analysis and cache modes must use identical names.`
  );
}
const workbenchHomeStart = app.indexOf('<WorkbenchHomeSection');
const workbenchHomeEnd = app.indexOf('/>', workbenchHomeStart);
assert.ok(
  workbenchHomeStart >= 0 && workbenchHomeEnd > workbenchHomeStart,
  'The Workbench home invocation must remain discoverable by the contract.'
);
const workbenchHome = app.slice(workbenchHomeStart, workbenchHomeEnd);
assert.doesNotMatch(
  workbenchHome,
  /cachePreparation|analysisPreparation|SvCacheProgressPanel|AnalysisPreparationPanel/,
  'Workbench must not receive Project Setup cache or analysis status panels.'
);
const projectSetupStart = app.indexOf('function HealthSection({');
const projectSetupEnd = app.indexOf('function SvCacheProgressPanel(', projectSetupStart);
assert.ok(
  projectSetupStart >= 0 && projectSetupEnd > projectSetupStart,
  'Project Setup must remain discoverable by the preparation placement contract.'
);
const projectSetup = app.slice(projectSetupStart, projectSetupEnd);
assert.match(
  projectSetup,
  /<SvCacheProgressPanel[\s\S]*?\{analysisPreparation\}/,
  'Project Setup must retain the cache and analysis preparation status panels in stable order.'
);
assert.ok(
  !app.includes("projectSourceRevision.sourceObservationToken ?? 'pending'"),
  'Analysis preparation must not create a provisional source scope that later looks like a restart.'
);
assert.match(
  app,
  /projectSourceRevision\.status === 'ready' &&\s+projectSourceRevision\.sourceObservationToken/,
  'Analysis preparation must begin its stable scope only after the source identity is resolved.'
);
assert.match(
  app,
  /const projectSourceRevision = useProjectSourceRevision\(\{[\s\S]*?bridge: analysisBridge,[\s\S]*?paths: activeProjectId \? analysisProjectPaths : null,/,
  'Source observation must start with the active project instead of waiting behind cache bookkeeping.'
);
assert.match(
  app,
  /const analysisProjectPathsRef = useRef\(analysisProjectPaths\);[\s\S]*?createGameScopedProjectBridge\(\s*unscopedBridge,\s*\(\) => analysisProjectPathsRef\.current,\s*\(\) => projectScopeGenerationRef\.current\s*\)/,
  'Read-only analysis must use its own current-path bridge while retaining project-generation guards.'
);
assert.match(
  app,
  /useSemanticExploreController\(\{\s*bridge: analysisBridge,\s*scope: semanticExploreScope\s*\}\)/,
  'Semantic analysis requests must use the bridge bound to the sanitized analysis scope.'
);
for (const runtime of [
  'BalanceLabRuntime',
  'GameModulesRuntime',
  'GuidedDesignRuntime',
  'ResearchLabRuntime',
  'SemanticMergeRuntime'
]) {
  const runtimeStart = app.indexOf(`<${runtime}`);
  const runtimeEnd = app.indexOf('/>', runtimeStart);
  assert.ok(
    runtimeStart >= 0 &&
      runtimeEnd > runtimeStart &&
      app.slice(runtimeStart, runtimeEnd).includes('bridge={analysisBridge}'),
    `${runtime} must use the bridge bound to the sanitized analysis scope.`
  );
}
assert.match(
  app,
  /const projectScope = useMemo<OutputSafetyScope \| null>[\s\S]*?const outputSafetyScope = useMemo\([\s\S]*?isValidatedOutputSafetyScope\(projectScope, health\)/,
  'General project reads and verified output-safety operations must use distinct scopes.'
);
assert.match(
  app,
  /const semanticExploreScope = useMemo\([\s\S]*?analysisProjectScope &&[\s\S]*?\.\.\.analysisProjectScope,[\s\S]*?useSemanticExploreController/,
  'Read-only semantic analysis must retain its validated project scope when Output Root is unavailable.'
);
assert.match(
  app,
  /function resolveProjectAnalysisPaths\([\s\S]*?health\.canOpenEditableWorkflows && outputRoot\?\.status === 'valid'[\s\S]*?\? outputRoot\.path[\s\S]*?: null/,
  'Read-only analysis must ignore an absent or invalid Output Root while retaining validated source paths.'
);
assert.match(
  app,
  /useOutputSafetyController\(\{[\s\S]*?scope: outputSafetyScope[\s\S]*?\}\)/,
  'Automatic recovery checks must receive only the verified output-safety scope.'
);
assert.match(
  app,
  /function isValidatedOutputSafetyScope\([\s\S]*?!health\?\.canOpenEditableWorkflows[\s\S]*?requireValid && validation\.status !== 'valid'[\s\S]*?pathMatchesValidation\('baseRomFs',[\s\S]*?pathMatchesValidation\('outputRoot'/,
  'Output safety must remain unavailable for absent, invalid, stale, or read-only Output Root health.'
);
const outputScopeValidatorStart = app.indexOf('function isValidatedOutputSafetyScope(');
const outputScopeValidatorEnd = app.indexOf(
  '\nfunction hasValidTrinitySupportFolder(',
  outputScopeValidatorStart
);
const outputScopeValidator = app.slice(outputScopeValidatorStart, outputScopeValidatorEnd);
assert.equal(
  [...outputScopeValidator.matchAll(/, true\)/g)].length,
  3,
  'Only the two required base roots and Output Root may require valid path status for output safety.'
);
assert.match(
  outputScopeValidator,
  /pathMatchesValidation\(\s*'scarletVioletSupportFolder',[\s\S]*?\)\s*&& pathMatchesValidation\(\s*'pokemonLegendsZASupportFolder'/,
  'Optional support folders must match the active health scope without becoming output-safety prerequisites.'
);
assert.match(
  app,
  /const svCacheScopeKey =\s*activeProjectId && isProjectCacheGame\(selectedGame\)\s*\? createProjectCacheScopeKey\(activeProjectId, gameDumpPaths\)/,
  'Cache status scope must synchronously include the normalized configured project paths.'
);
assert.ok(
  !app.includes('isInitialSvCacheCheckSettled'),
  'Cache status must not gate independent source observation.'
);

const cacheStartupEffectStart = app.indexOf(
  'useEffect(() => {\n    if (!isProjectCacheGame(selectedGame) || svCacheScopeKey === null)'
);
const cacheStartupEffectEnd = app.indexOf(
  'const handleChangeSvCacheMode',
  cacheStartupEffectStart
);
assert.ok(
  cacheStartupEffectStart >= 0 && cacheStartupEffectEnd > cacheStartupEffectStart,
  'Cache startup must have one project-scoped effect owner.'
);
const cacheStartupEffect = app.slice(cacheStartupEffectStart, cacheStartupEffectEnd);
assert.equal(
  [...cacheStartupEffect.matchAll(/\bstartSvCacheWarmup\(/g)].length,
  1,
  'The project-scoped cache effect must start warmup exactly once.'
);
assert.match(
  cacheStartupEffect,
  /svCacheAutomaticStartupRef\.current\?\.key === startupKey[\s\S]*?svCacheAutomaticStartupRef\.current = \{ key: startupKey, request \}/,
  'StrictMode and equivalent rerenders must reuse the one in-flight automatic cache startup.'
);
assert.match(
  cacheStartupEffect,
  /if \(!health\) \{\s*return;\s*\}[\s\S]*?getProjectCacheStatusForGame\(bridge, selectedGame, null\)/,
  'Cache startup must not enqueue a pathless status request while project health is still pending.'
);
assert.match(
  app,
  /const resetLoadedProjectState = useCallback\(\(\) => \{[\s\S]*?svCacheAutomaticStartupRef\.current = null;[\s\S]*?setSvCacheStatus\(null\);/,
  'Resetting a project must release the automatic cache startup owner before clearing status.'
);
assert.match(
  app,
  /const beginProjectScopeTransition = useCallback\([\s\S]*?criticalWriteOperationRef\.current !== null[\s\S]*?projectScopeTransitionRef\.current = transition;[\s\S]*?projectScopeGenerationRef\.current \+= 1;[\s\S]*?return transition;/,
  'Project-scope transitions must claim synchronous admission and invalidate stale project work.'
);
assert.match(
  app,
  /const finishProjectScopeTransition = useCallback\(\(transition: object\) => \{[\s\S]*?projectScopeTransitionRef\.current === transition[\s\S]*?projectScopeTransitionRef\.current = null;/,
  'Only the project-scope transition owner may release project admission.'
);
assert.match(
  app,
  /const recycleProjectBridgeBeforeScopeChange = useCallback\(async \([\s\S]*?transition: object[\s\S]*?projectScopeTransitionRef\.current !== transition[\s\S]*?projectBridgeScopeRecycleInFlightRef\.current[\s\S]*?desktopServices\.recycleProjectBridge\(\)[\s\S]*?return false;[\s\S]*?const scheduleProjectBridgeRecycleAfterDraftChange = useCallback[\s\S]*?beginProjectScopeTransition\(\)[\s\S]*?recycleProjectBridgeBeforeScopeChange\(transition\)[\s\S]*?finishProjectScopeTransition\(transition\)/,
  'Project-scope recycling must require its owner, deduplicate bridge work, and release deferred draft transitions.'
);
assert.match(
  app,
  /const handleSetDraftPath = useCallback\([\s\S]*?const changeRun = projectPathDraftChangeRunRef\.current \+ 1;[\s\S]*?projectPathDraftChangeRunRef\.current !== changeRun[\s\S]*?setDraftPath\(field, value\);[\s\S]*?scheduleProjectBridgeRecycleAfterDraftChange\(\);/,
  'Raw project-path edits must commit latest-wins and debounce stale bridge cancellation.'
);
const cacheSettingsRestartStart = app.indexOf('const handleChangeSvCacheMode');
const cacheSettingsRestartEnd = app.indexOf('const handleRefreshSvCacheStatus');
assert.ok(
  cacheSettingsRestartStart >= 0 && cacheSettingsRestartEnd > cacheSettingsRestartStart,
  'Cache settings restart handlers must remain discoverable by the contract.'
);
const cacheSettingsRestartBlock = app.slice(cacheSettingsRestartStart, cacheSettingsRestartEnd);
assert.equal(
  [...cacheSettingsRestartBlock.matchAll(/\bstartSvCacheWarmup\(/g)].length,
  2,
  'Only the cache mode and size settings may explicitly restart warmup.'
);
const cacheRetryEnd = app.indexOf('const handleConfirmClearSvCache');
assert.ok(
  cacheRetryEnd > cacheSettingsRestartEnd,
  'The cache retry handler must remain discoverable by the contract.'
);
const cacheRetryBlock = app.slice(cacheSettingsRestartEnd, cacheRetryEnd);
assert.match(
  cacheRetryBlock,
  /response\.status\.warmupCompleted < response\.status\.warmupTotal[\s\S]*?startSvCacheWarmup\(paths, health, svCacheScopeKey, response\.status\)/,
  'A successful retry must resume incomplete warmup from the status it just read.'
);
assert.equal(
  [...app.matchAll(/\bstartSvCacheWarmup\(/g)].length,
  5,
  'Project activation paths must not duplicate the automatic startup, Apply recovery, or three user-triggered cache restarts.'
);

assert.match(
  app,
  /type CacheProgressSourceState = 'checking' \| 'error' \| 'ready' \| 'setupRequired';/,
  'Cache progress must model request failure separately from checking.'
);
assert.match(
  app,
  /const isError = sourceState === 'error';[\s\S]*?const isChecking =\s*sourceState === 'checking' \|\| \(sourceState === 'ready' && status === null\);/,
  'A failed cache request must never continue rendering as Checking.'
);
assert.match(
  projectSetup,
  /hasSvCacheRequestError\s*\? 'error'[\s\S]*?status=\{svCacheStatus\}/,
  'Project Setup cache progress must use the current project scope and surface request failure.'
);
assert.ok(
  [...app.matchAll(/svCacheStatus=\{currentSvCacheStatus\}/g)].length >= 2,
  'Project Setup and Settings must not render cache status from a stale project scope.'
);
assert.match(
  app,
  /role=\{isError \|\| isWarmupPaused \? 'alert' : 'status'\}[\s\S]*?Retry cache check/,
  'Cache request and warmup failures must expose a KM-themed retry action and announce the error truthfully.'
);
assert.match(
  app,
  /setHasSvCacheRequestError\(true\);[\s\S]*?setHasSvCacheWarmupError\(true\);/,
  'Status-read failure and cache-build failure must remain distinct states.'
);
assert.match(
  app,
  /const operationGeneration = svCacheOperationGenerationRef\.current \+ 1;[\s\S]*?svCacheScopeKeyRef\.current === operationScopeKey;/,
  'Manual cache operations must reject stale results after the project scope changes.'
);
assert.match(
  cacheSettingsRestartBlock,
  /svCacheSettingsOperationRef\.current !== null[\s\S]*?setIsSvCacheSettingsUpdating\(true\)[\s\S]*?setIsSvCacheSettingsUpdating\(false\)/,
  'Cache settings changes must be mutually exclusive through their full bridge and restart lifecycle.'
);
const cacheWarmupStart = app.indexOf('const startSvCacheWarmup');
const cacheWarmupEnd = app.indexOf('const handleChangeSvCacheMode', cacheWarmupStart);
assert.ok(
  cacheWarmupStart >= 0 && cacheWarmupEnd > cacheWarmupStart,
  'The cache warmup runner must remain discoverable by the contract.'
);
const cacheWarmupBlock = app.slice(cacheWarmupStart, cacheWarmupEnd);
assert.match(
  cacheWarmupBlock,
  /const expectedWarmupTotal = initialStatus\.warmupTotal;[\s\S]*?evaluateCacheWarmupProgressTransition\(\s*previousCompleted,\s*expectedWarmupTotal,\s*nextStatus\.warmupCompleted,\s*nextStatus\.warmupTotal\s*\)/,
  'One warmup run must keep the first measured denominator as its invariant.'
);
assert.match(
  cacheWarmupBlock,
  /if \(progressTransition\.kind !== 'advanced'\) \{[\s\S]*?setHasSvCacheWarmupError\(true\);[\s\S]*?last verified progress[\s\S]*?break;[\s\S]*?latestStatus = nextStatus;[\s\S]*?setSvCacheStatus\(latestStatus\);/,
  'A stalled, regressing, or denominator-changing batch must become retryable before it can publish status.'
);
assert.ok(
  cacheWarmupBlock.indexOf('evaluateCacheWarmupProgressTransition(') <
    cacheWarmupBlock.indexOf('setSvCacheStatus(latestStatus);', cacheWarmupBlock.indexOf('const nextStatus')),
  'A cache batch response must be verified before its progress can reach the visible bar.'
);

const cacheWarmupPolicy = read('../src/cacheWarmupPolicy.ts');
for (const cacheScopeField of [
  'baseExeFsPath',
  'baseRomFsPath',
  'gameTextLanguage',
  'outputRootPath',
  'supportFolderPath',
  'selectedGame'
]) {
  assert.ok(
    cacheWarmupPolicy.includes(cacheScopeField),
    `Cache scope identity lost ${cacheScopeField}.`
  );
}
assert.match(
  cacheWarmupPolicy,
  /nextTotalUnitCount !== expectedTotalUnitCount[\s\S]*?reason: 'total-changed'[\s\S]*?nextCompletedUnitCount < previousCompletedUnitCount[\s\S]*?reason: 'completed-regressed'/,
  'Cache warmup policy must reject denominator changes and completed-count regressions.'
);
assert.ok(
  app.includes('await delay(0);') && !app.includes('await delay(25);'),
  'Cache warmup must yield fairly between batches without imposing a fixed delay on every batch.'
);
for (const cacheManagerPath of [
  '../../../src/KM.SV/Workflows/SvCacheManager.cs',
  '../../../src/KM.ZA/Workflows/ZaCacheManager.cs'
]) {
  const cacheManager = read(cacheManagerPath);
  assert.match(
    cacheManager,
    /if \(extractedPayloads\[waveIndex\] is null\)[\s\S]*?throw new InvalidDataException/,
    `${cacheManagerPath} must reject an indexed entry that cannot be read instead of silently retrying it.`
  );
  assert.ok(
    cacheManager.includes('MaximumPerformanceWarmupParallelism = 8')
      && cacheManager.includes('WarmupCandidateBatchSize = 256')
      && cacheManager.includes('PerformanceWarmupWorkerMemoryBudgetBytes = 576L * 1024L * 1024L')
      && cacheManager.includes('BoundedConcurrencyPolicy PerformanceWarmupPolicy')
      && cacheManager.includes('BoundedConcurrencyPolicy WarmupVerificationPolicy')
      && /BoundedParallel\s*\.Plan\(/.test(cacheManager)
      && cacheManager.includes('BoundedParallel.For(')
      && cacheManager.includes('BoundedParallel.MapOrdered(')
      && cacheManager.includes('MaximumArchiveIndexBytes = 64 * 1024 * 1024')
      && cacheManager.includes('MaximumPerformanceWarmupFileBytes = 64 * 1024 * 1024')
      && cacheManager.includes('MaximumPerformanceWarmupPackBytes = 128L * 1024L * 1024L')
      && cacheManager.includes('MaximumCacheJsonFileBytes = 16L * 1024L * 1024L')
      && cacheManager.includes('MaximumPersistedIndexFileBytes = 256L * 1024L * 1024L'),
    `${cacheManagerPath} must keep its performance worker pool fast and memory bounded.`
  );
  assert.match(
    cacheManager,
    /BuildIndex\(\s*context\.RomFsRootPath,\s*MaximumArchiveIndexBytes\)/,
    `${cacheManagerPath} must bound archive-index reads before parsing them.`
  );
  assert.match(
    cacheManager,
    /OpenJsonReadStream\(indexPath, MaximumPersistedIndexFileBytes\)/,
    `${cacheManagerPath} must bound persisted index JSON before deserializing it.`
  );
  assert.match(
    cacheManager,
    /maximumPackBytes: MaximumPerformanceWarmupPackBytes\)[\s\S]*?MaximumPerformanceWarmupFileBytes,[\s\S]*?out var bytes/,
    `${cacheManagerPath} must apply its pack and payload limits to every parallel extraction worker.`
  );
  const statusStart = cacheManager.indexOf(' GetStatus(');
  const clearStart = cacheManager.indexOf(' Clear(', statusStart);
  const statusBlock = cacheManager.slice(statusStart, clearStart);
  assert.ok(
    statusStart >= 0 && clearStart > statusStart
      && !statusBlock.includes('forceSizeRefresh: true'),
    `${cacheManagerPath} routine status reads must reuse exact mutation accounting instead of rescanning every cache file.`
  );
  assert.ok(
    statusBlock.includes('IReadOnlyList<string>? warmupPlan = null;')
      && /GetWarmupVirtualPaths\([\s\S]*?persistToDisk: !isReadWorker/.test(statusBlock)
      && /CreateStatus\([\s\S]*?activeProjectPreserved: false,[\s\S]*?warmupPlan\)/.test(statusBlock),
    `${cacheManagerPath} must discover the exact warmup plan while read workers avoid persistent publication.`
  );
  assert.match(
    cacheManager,
    /for \(var waveStart = 0;[\s\S]*?BoundedParallel\.For\([\s\S]*?WritePayload\([\s\S]*?extractedPayloads\[waveIndex\] = null;/,
    `${cacheManagerPath} must extract in bounded parallel waves, commit deterministically, and release payload bytes per wave.`
  );
  assert.ok(
    !/(^|[^A-Za-z])Parallel\.For\(/m.test(cacheManager),
    `${cacheManagerPath} must not bypass the shared bounded concurrency scheduler.`
  );
  assert.ok(
    cacheManager.includes('var extractionFailures = new ExceptionDispatchInfo?[waveCount];')
      && /for \(var waveIndex = 0; waveIndex < waveCount; waveIndex\+\+\)[\s\S]*?extractionFailures\[waveIndex\][\s\S]*?extractionFailure\.Throw\(\)/.test(cacheManager),
    `${cacheManagerPath} must report parallel extraction failures in deterministic source order.`
  );
  assert.ok(
    cacheManager.includes('MaximumCacheTraversalEntries = 500_000')
      && cacheManager.includes('MaximumCacheTraversalDepth = 128')
      && cacheManager.includes('EnumerateFileSystemInfos("*", CacheDirectoryEnumeration)')
      && cacheManager.includes('FileAttributes.ReparsePoint'),
    `${cacheManagerPath} must keep exact cache-size traversal structurally bounded and avoid reparses.`
  );
}
const zaWorkflowFileSource = read('../../../src/KM.ZA/Workflows/ZaWorkflowFileSource.cs');
assert.match(
  zaWorkflowFileSource,
  /ReadCurrentSourceFreshCore[\s\S]*?TryGetRomFsMutation\(normalizedVirtualPath, out var deferredBytes\)[\s\S]*?return \(deferredBytes\.ToArray\(\), ProjectFileLayer\.Layered\);[\s\S]*?suppressLayeredOutput = true;/,
  'Z-A fresh source reads must include staged deferred bytes and suppress staged deletes.'
);
for (const workflowServicePath of [
  '../../../src/KM.SV/Workflows/SvWorkflowService.cs',
  '../../../src/KM.ZA/Workflows/ZaWorkflowService.cs'
]) {
  const workflowService = read(workflowServicePath);
  assert.ok(
    workflowService.includes('MaximumSemanticFingerprintParallelism = 8')
      && workflowService.includes('BoundedConcurrencyPolicy SemanticFingerprintPolicy')
      && /BoundedParallel\s*\.Plan\(/.test(workflowService)
      && workflowService.includes('BoundedParallel.For(')
      && !/(^|[^A-Za-z])Parallel\.For\(/m.test(workflowService),
    `${workflowServicePath} must scale semantic hashing with both CPU and available memory.`
  );
}
const semanticExploreService = read(
  '../../../src/KM.Tools/Application/SemanticExploreApplicationService.cs'
);
assert.ok(
  semanticExploreService.includes('MaterializeCorpusConcurrently')
    && semanticExploreService.includes('EstimatedCorpusLoadWorkerBytes')
    && semanticExploreService.includes('BoundedConcurrencyPolicy CorpusLoadPolicy')
    && /BoundedParallel\s*\.Plan\(/.test(semanticExploreService)
    && semanticExploreService.includes('BoundedParallel.For(')
    && !/(^|[^A-Za-z])Parallel\.For\(/m.test(semanticExploreService),
  'Cold semantic domains must use adaptive CPU and memory-bounded materialization.'
);
const gameModuleService = read('../../../src/KM.Tools/Application/GameModuleApplicationService.cs');
assert.match(
  gameModuleService,
  /ReadSourceCacheIdentity\(request\.Scope\)[\s\S]*?game-module-source-revision-v1/,
  'Layered game-module caches must remain source-stable across unrelated pending edits.'
);
const guidedDesignService = read(
  '../../../src/KM.Tools/Application/GuidedDesignApplicationService.cs'
);
assert.ok(
  guidedDesignService.includes('MaximumConcurrentSourceLoads = 4')
    && guidedDesignService.includes('EstimatedSourceLoadWorkerBytes')
    && guidedDesignService.includes('BoundedConcurrencyPolicy SourceLoadPolicy')
    && /BoundedParallel\s*\.Plan\(/.test(guidedDesignService)
    && guidedDesignService.includes('BoundedParallel.For(')
    && !/(^|[^A-Za-z])Parallel\.For\(/m.test(guidedDesignService)
    && guidedDesignService.includes('CapabilityBuildLockCount = 8')
    && guidedDesignService.includes('MaximumCapabilityCacheEntries = 8'),
  'Guided Design must retain adaptive four-domain loading, source-keyed single flight, and bounded LRU reuse.'
);
const zaWorkflowService = read('../../../src/KM.ZA/Workflows/ZaWorkflowService.cs');
assert.ok(
  zaWorkflowService.includes('MaximumGameModuleCapabilityParallelism = 4')
    && zaWorkflowService.includes('EstimatedGameModuleCapabilityWorkerBytes')
    && zaWorkflowService.includes('LoadGameModuleCapabilityBatch')
    && zaWorkflowService.includes('GameModuleCapabilityBatchParallelism')
    && zaWorkflowService.includes('BoundedConcurrencyPolicy GameModuleCapabilityPolicy')
    && zaWorkflowService.includes('BoundedParallel.For(')
    && !/(^|[^A-Za-z])Parallel\.For\(/m.test(zaWorkflowService)
    && zaWorkflowService.includes('HasActiveDeferredOutputBatch'),
  'Z-A Game Tools must retain adaptive grouped reads and the deferred-output single-thread safety boundary.'
);
const projectBridgeDispatcher = read(
  '../../../src/KM.Tools/Bridge/ProjectBridgeDispatcher.cs'
);
const sourceRevisionDispatchStart = projectBridgeDispatcher.indexOf(
  ' DispatchReadProjectSourceRevision('
);
const sourceRevisionDispatchEnd = projectBridgeDispatcher.indexOf(
  ' DispatchLoadTrainerPoolsWorkflow(',
  sourceRevisionDispatchStart
);
const sourceRevisionDispatch = projectBridgeDispatcher.slice(
  sourceRevisionDispatchStart,
  sourceRevisionDispatchEnd
);
assert.ok(
  sourceRevisionDispatchStart >= 0
    && sourceRevisionDispatchEnd > sourceRevisionDispatchStart
    && /ShouldProtectSourceObservationWithOutputSafetyLock\([\s\S]*?\? ExecuteExclusiveOutputOperation\(request\.Payload\.Paths, CaptureObservation\)[\s\S]*?: CaptureObservation\(\)/.test(
      sourceRevisionDispatch
    )
    && /string\.IsNullOrWhiteSpace\(paths\.OutputRootPath\)[\s\S]*?return false;[\s\S]*?!Path\.IsPathFullyQualified\(paths\.OutputRootPath\)[\s\S]*?return true;[\s\S]*?Path\.GetFullPath\(paths\.OutputRootPath\)[\s\S]*?ArgumentException or[\s\S]*?NotSupportedException or[\s\S]*?PathTooLongException or[\s\S]*?System\.Security\.SecurityException[\s\S]*?return true;[\s\S]*?OutputSafetyApplicationService\.ResolveScope\([\s\S]*?new OutputScopeDto\(projectId, paths\)[\s\S]*?return true;[\s\S]*?catch \(OutputScopeMismatchException\)[\s\S]*?return false;/.test(
      sourceRevisionDispatch
    ),
  'Source revision reads must bypass coordinator locking for unavailable or invalid output scopes while preserving fail-closed locking for verified output roots.'
);
const zaGameModuleBatchStart = projectBridgeDispatcher.indexOf(
  ' LoadGameModuleZaCapabilityBatchFresh('
);
const zaGameModuleBatchEnd = projectBridgeDispatcher.indexOf(
  ' LoadGameModuleTrainerArchetypesFresh(',
  zaGameModuleBatchStart
);
assert.ok(
  zaGameModuleBatchStart >= 0
    && zaGameModuleBatchEnd > zaGameModuleBatchStart
    && /LoadGameModuleCapabilityBatch\(paths\)[\s\S]*?BoundedParallel\.For\([\s\S]*?Resolve in the original bridge tuple order/.test(
      projectBridgeDispatcher.slice(zaGameModuleBatchStart, zaGameModuleBatchEnd)
    ),
  'Z-A Game Tools must map the grouped source results concurrently and resolve them deterministically.'
);
assert.ok(
  projectBridgeDispatcher.includes('BoundedConcurrencyPolicy CreateBridgePolicy(')
    && !/(^|[^A-Za-z])Parallel\.For\(/m.test(projectBridgeDispatcher),
  'Bridge-side parallel mapping must use the shared bounded scheduler and host budget.'
);

const boundedParallel = read('../../../src/KM.Core/Concurrency/BoundedParallel.cs');
const boundedHostBudget = read(
  '../../../src/KM.Core/Concurrency/BoundedConcurrencyHostBudget.cs'
);
assert.ok(
  boundedParallel.includes('class BoundedConcurrencyPolicy')
    && boundedParallel.includes('static class BoundedParallel')
    && boundedParallel.includes('new(BoundedConcurrencyHostBudget.Current)')
    && boundedParallel.includes('SerialNestedExecution')
    && boundedParallel.includes('MaximumIndexedFailureSlots')
    && boundedParallel.includes('ProcessWideCoordinator'),
  'Managed parallel work must share bounded, nested-safe process-wide admission.'
);
for (const environmentContract of [
  'KM_MANAGED_CONCURRENCY_CPU_LIMIT',
  'KM_MANAGED_CONCURRENCY_MEMORY_BYTES',
  'KM_MANAGED_READ_WORKER'
]) {
  assert.ok(
    boundedHostBudget.includes(environmentContract),
    `The central concurrency host budget lost ${environmentContract}.`
  );
}
assert.ok(
  boundedHostBudget.includes('Environment.ProcessorCount')
    && boundedHostBudget.includes('GC.GetGCMemoryInfo().TotalAvailableMemoryBytes')
    && boundedHostBudget.includes('InvalidOverrideIgnored')
    && boundedHostBudget.includes('ParseReadWorkerMode')
    && boundedHostBudget.includes('_ => true'),
  'The central host budget must bound CPU and memory, validate overrides, and fail closed for read workers.'
);
const sourceRevisionHook = read('../src/workbench/useProjectSourceRevision.ts');
assert.ok(
  sourceRevisionHook.includes("runIndependentProjectRead(\n      'readProjectSourceRevision'")
    && sourceRevisionHook.includes("new ProjectQueryEpoch<'revision'>()"),
  'StrictMode remounts must coalesce identical in-flight source observations.'
);

const asyncPolicy = read('../src/utils/projectAsyncPolicy.ts');
assert.ok(
  asyncPolicy.includes('class ProjectQueryEpoch')
    && asyncPolicy.includes('controller.abort()')
    && asyncPolicy.includes('ticket.epoch === this.epoch')
    && asyncPolicy.includes('ticket.generation ==='),
  'Project queries must share abort-aware epoch and generation stale-publication guards.'
);
assert.ok(
  asyncPolicy.includes('class ExactKeySingleFlight')
    && asyncPolicy.includes('new WeakMap<object, ExactKeySingleFlight>()')
    && asyncPolicy.includes('void pending.then(removeSettledRequest, removeSettledRequest)')
    && asyncPolicy.includes('maximumInFlightKeysPerOwner = 64')
    && asyncPolicy.includes('maximumKeyBytes = 256 * 1_024')
    && asyncPolicy.includes('projectQueryKeyTextEncoder.encode(exactKey).byteLength')
    && asyncPolicy.includes('ProjectReadAdmissionError')
    && asyncPolicy.includes('ProjectReadKeyTooLargeError'),
  'Independent reads must use bounded exact-key single flight with weak owners and settled cleanup.'
);
assert.ok(
  asyncPolicy.includes('class BoundedLruCache')
    && asyncPolicy.includes('this.entries.size > this.maximumEntries')
    && asyncPolicy.includes('this.totalWeight > this.maximumWeight'),
  'Settled analysis caches must enforce both count and weight bounds through the shared LRU.'
);
assert.ok(
  asyncPolicy.includes('class ProjectSerialTaskQueue')
    && asyncPolicy.includes('maximumPendingOperations = 64')
    && asyncPolicy.includes('this.pendingOperations >= this.maximumPendingOperations')
    && asyncPolicy.includes('ProjectOperationAdmissionError')
    && asyncPolicy.includes('orderedProjectOperations')
    && asyncPolicy.includes('operation(owner)'),
  'Stateful analysis bridge calls must have bounded per-bridge admission and bind the admitted owner.'
);
assert.ok(
  asyncPolicy.includes('class DeferredStrictModeCleanup')
    && read('../src/hooks/useDeferredUnmountCleanup.ts').includes(
      'new DeferredStrictModeCleanup()'
    ),
  'StrictMode cleanup deferral must share the tested project async policy primitive.'
);
assert.ok(
  asyncPolicy.includes('stableProjectQueryKey(operation, exactRequest)')
    && asyncPolicy.includes('(keys as string[]).sort()')
    && asyncPolicy.includes('Reflect.ownKeys(value)')
    && asyncPolicy.includes('Number.isFinite(value)')
    && asyncPolicy.includes('Object.is(value, -0)')
    && asyncPolicy.includes('keys.length !== value.length'),
  'Exact query identity must canonicalize object keys and reject lossy non-JSON identities.'
);
assert.ok(
  asyncPolicy.includes('function measureRealQueryUnits')
    && model.includes('measureRealQueryUnits(statuses)'),
  'Determinate preparation must share the real-operation unit counter.'
);

const analysisControllerPaths = [
  '../src/features/balance-lab/useBalanceLabController.ts',
  '../src/features/game-modules/useGameModuleController.ts',
  '../src/features/guided-design/useGuidedDesignController.ts',
  '../src/features/research-lab/useResearchLabController.ts',
  '../src/features/semantic-explore/useSemanticExploreController.ts',
  '../src/features/semantic-merge/useSemanticMergeController.ts'
];
const bridgeConsumers = new Map(
  [...analysisControllerPaths, '../src/workbench/useProjectSourceRevision.ts']
    .map((path) => [path, read(path)])
);
const usedBridgeOperations = new Set();
for (const [path, source] of bridgeConsumers) {
  for (const match of source.matchAll(/(?:(?:this|options)\.bridge|bridge)\.([A-Za-z0-9_]+)/g)) {
    usedBridgeOperations.add(match[1]);
  }
  if (analysisControllerPaths.includes(path)) {
    assert.ok(
      source.includes('ProjectQueryEpoch') && source.includes('useDeferredUnmountCleanup'),
      `${path} must use the shared freshness and StrictMode cleanup policy.`
    );
    assert.ok(
      !/private (?:epoch|generation|generations|flowGenerations|slotGenerations)\b/.test(source),
      `${path} must not reintroduce a bespoke epoch or generation implementation.`
    );
  }
}
const operationPolicyMatch = asyncPolicy.match(
  /analysisBridgeOperationPolicies = \{([\s\S]*?)\n\} as const/
);
assert.ok(operationPolicyMatch, 'The analysis bridge operation policy must remain discoverable.');
const classifiedOperations = new Map(
  [...operationPolicyMatch[1].matchAll(/^  ([A-Za-z0-9_]+): '([^']+)',?$/gm)]
    .map((match) => [match[1], match[2]])
);
assert.deepEqual(
  [...classifiedOperations.keys()].sort(),
  [...usedBridgeOperations].sort(),
  'Every analysis/source-revision bridge call must have one exhaustive frontend concurrency class.'
);
for (const [operation, classification] of classifiedOperations) {
  const owner = [...bridgeConsumers.entries()].find(([, source]) => (
    source.includes(`.bridge.${operation}(`) || source.includes(`bridge.${operation}(`)
  ));
  assert.ok(owner, `The classified ${operation} operation must have a controller owner.`);
  const expectedRunner = classification === 'independentRead'
    ? 'runIndependentProjectRead'
    : 'runOrderedProjectOperation';
  assert.ok(
    owner[1].includes(`${expectedRunner}(\n        '${operation}'`) ||
      owner[1].includes(`${expectedRunner}(\n      '${operation}'`),
    `${operation} must use ${expectedRunner} for its ${classification} policy.`
  );
  if (classification !== 'independentRead') {
    assert.ok(
      owner[1].includes(`(bridge) => bridge.${operation}(`),
      `${operation} must execute against the exact bridge owner captured at ordered admission.`
    );
  }
}

const balanceController = bridgeConsumers.get(
  '../src/features/balance-lab/useBalanceLabController.ts'
);
const gameModuleController = bridgeConsumers.get(
  '../src/features/game-modules/useGameModuleController.ts'
);
const semanticExploreController = bridgeConsumers.get(
  '../src/features/semantic-explore/useSemanticExploreController.ts'
);
for (const [name, source] of [
  ['Balance Lab', balanceController],
  ['Game Tools', gameModuleController],
  ['Project analysis', semanticExploreController]
]) {
  assert.ok(
    source.includes('new BoundedLruCache'),
    `${name} settled query reuse must use the shared bounded LRU.`
  );
}
assert.ok(
  semanticExploreController.includes('maximumInspectorCacheBytes = 32 * 1_024 * 1_024')
    && semanticExploreController.includes('maximumWeight: maximumInspectorCacheBytes')
    && semanticExploreController.includes('inspectorCacheTextEncoder.encode(JSON.stringify(value)).byteLength'),
  'Project analysis must bound retained inspector responses by encoded byte weight as well as count.'
);
assert.ok(
  hook.includes("new ProjectQueryEpoch<'preload'>()")
    && hook.includes('preloadFreshness.isCurrent(ticket)'),
  'Deferred Workbench admission must reject callbacks from an obsolete project scope.'
);
const nativeBridge = read('../src-tauri/src/lib.rs');
assert.ok(
  nativeBridge.includes('MAX_PROJECT_BRIDGE_PENDING_REQUESTS')
    && nativeBridge.includes('MAX_PROJECT_BRIDGE_PENDING_REQUEST_BYTES')
    && nativeBridge.includes('reserve_pending_request(request_json.len())?'),
  'The ordered native bridge boundary must bound both queued request count and retained request bytes.'
);
assert.ok(
  nativeBridge.includes('PROJECT_BRIDGE_IO_POLL_INTERVAL')
    && nativeBridge.includes('wait_for_project_bridge_stdout_with_cancellation')
    && nativeBridge.includes('PeekNamedPipe')
    && nativeBridge.includes('cancellation.ensure_active()?')
    && nativeBridge.includes('buffered_stdout'),
  'Active bridge reads must remain readiness-polled, cancelable, and correctly framed without waiting for child EOF.'
);
assert.match(
  app,
  /deferBackgroundWork: isBusy \|\| hasCriticalWriteOperation/,
  'Cache warmup must not defer or restart independent analysis preparation.'
);
assert.ok(
  app.includes('observeOutputRecoveryRevision('),
  'Output recovery hydration must use the baseline-aware observation contract.'
);
assert.match(
  app,
  /refreshedRecovery = await outputSafety\.notifyOutputMutation\(\);[\s\S]*?observedSemanticOutputRevisionRef\.current = \{[\s\S]*?revision: refreshedRecovery\.revision[\s\S]*?\};[\s\S]*?semanticExploreController\.invalidate\(\);[\s\S]*?projectSourceRevision\.refresh\(\);/,
  'An output mutation must synchronize the observed recovery revision before the one direct source refresh.'
);

const outputSafetyHook = read('../src/features/output-safety/useOutputSafetyController.ts');
assert.match(
  outputSafetyHook,
  /notifyOutputMutation: \(\) => Promise<OutputRecoveryStatus \| null>/,
  'Output mutation notification must return the committed recovery revision to its caller.'
);
assert.match(
  outputSafetyHook,
  /const notifyOutputMutation = useCallback\(async \(\) => \{[\s\S]*?return refreshedRecovery;/,
  'Output mutation notification must return only the recovery status committed for the active scope.'
);

const invokedPath = process.argv[1];
if (invokedPath !== undefined && fileURLToPath(import.meta.url) === invokedPath) {
  console.log('Analysis preparation contract passed.');
}
