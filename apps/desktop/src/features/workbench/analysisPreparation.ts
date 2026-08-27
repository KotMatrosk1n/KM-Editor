/* SPDX-License-Identifier: GPL-3.0-only */

export const analysisLoadingModes = ['reduced', 'balanced', 'fastest'] as const;
export type AnalysisLoadingMode = (typeof analysisLoadingModes)[number];

export const analysisToolIds = [
  'balanceLab',
  'guidedDesign',
  'semanticMerge',
  'gameModules',
  'researchLab'
] as const;
export type AnalysisToolId = (typeof analysisToolIds)[number];

export const analysisSystemIds = ['semanticProject', ...analysisToolIds] as const;
export type AnalysisSystemId = (typeof analysisSystemIds)[number];
export type AnalysisPreparationState = 'waiting' | 'loading' | 'ready' | 'error';

export type AnalysisPreparationProgress = {
  completedUnitCount: number;
  state: AnalysisPreparationState;
  totalUnitCount: number;
};

export type AnalysisPreparationProgressBySystem = Record<
  AnalysisSystemId,
  AnalysisPreparationProgress
>;

export type AnalysisPreparationScopeState = {
  preloadTools: readonly AnalysisToolId[];
  progressBySystem: AnalysisPreparationProgressBySystem;
  scopeKey: string | null;
};

export type ObservedOutputRecoveryRevision = {
  revision: string | null;
  scopeKey: string | null;
};

export type OutputRecoveryRevisionObservation = {
  next: ObservedOutputRecoveryRevision;
  shouldInvalidateAnalysis: boolean;
};

export type AnalysisPreparationSnapshot = {
  completedUnitCount: number;
  errorCount: number;
  percent: number;
  readyCount: number;
  progressBySystem: AnalysisPreparationProgressBySystem;
  states: Record<AnalysisSystemId, AnalysisPreparationState>;
  targetCount: number;
  targetSystems: readonly AnalysisSystemId[];
  totalUnitCount: number;
};

// Each unit is one real asynchronous preparation operation already exposed by the
// owning controller. Project analysis measures source observation and index
// capability materialization. Research Lab measures capabilities and annotations.
// The remaining tools each have one required initial query.
export const analysisPreparationUnitCounts: Readonly<Record<AnalysisSystemId, number>> = {
  balanceLab: 1,
  gameModules: 1,
  guidedDesign: 1,
  researchLab: 2,
  semanticMerge: 1,
  semanticProject: 2
};

const analysisLoadingModeStorageKey = 'km-editor.analysis-loading-mode.v1';

export function observeOutputRecoveryRevision(
  current: ObservedOutputRecoveryRevision,
  scopeKey: string | null,
  revision: string | null
): OutputRecoveryRevisionObservation {
  if (scopeKey === null) {
    return {
      next: { revision: null, scopeKey: null },
      shouldInvalidateAnalysis: false
    };
  }
  if (current.scopeKey !== scopeKey) {
    return {
      next: { revision, scopeKey },
      shouldInvalidateAnalysis: false
    };
  }
  if (revision === null || current.revision === revision) {
    return { next: current, shouldInvalidateAnalysis: false };
  }
  if (current.revision === null) {
    return {
      next: { revision, scopeKey },
      shouldInvalidateAnalysis: false
    };
  }
  return {
    next: { revision, scopeKey },
    shouldInvalidateAnalysis: true
  };
}

export function readAnalysisLoadingMode(): AnalysisLoadingMode {
  if (typeof window === 'undefined') return 'balanced';
  try {
    const value = window.localStorage.getItem(analysisLoadingModeStorageKey);
    return analysisLoadingModes.includes(value as AnalysisLoadingMode)
      ? value as AnalysisLoadingMode
      : 'balanced';
  } catch {
    return 'balanced';
  }
}

export function writeAnalysisLoadingMode(mode: AnalysisLoadingMode) {
  if (typeof window === 'undefined') return;
  try {
    window.localStorage.setItem(analysisLoadingModeStorageKey, mode);
  } catch {
    // Keep the in-memory preference when browser storage is unavailable.
  }
}

export function analysisPreloadOrder(
  mode: AnalysisLoadingMode
): readonly AnalysisToolId[] {
  return mode === 'reduced' ? [] : analysisToolIds;
}

export function nextAnalysisPreloadTool(options: {
  deferBackgroundWork: boolean;
  mode: AnalysisLoadingMode;
  preloadTools: readonly AnalysisToolId[];
  semanticState: AnalysisPreparationState;
  states: Record<AnalysisSystemId, AnalysisPreparationState>;
}): AnalysisToolId | null {
  if (options.semanticState !== 'ready') return null;
  const order = analysisPreloadOrder(options.mode);
  const nextIndex = order.findIndex((tool) => !options.preloadTools.includes(tool));
  if (nextIndex < 0) return null;
  if (order.slice(0, nextIndex).some((tool) => (
    options.states[tool] !== 'ready' && options.states[tool] !== 'error'
  ))) return null;
  if (analysisToolIds.some((tool) => options.states[tool] === 'loading')) return null;
  if (options.mode === 'balanced' && options.deferBackgroundWork) return null;
  return order[nextIndex] ?? null;
}

export function createAnalysisPreparationProgress(
  system: AnalysisSystemId,
  state: AnalysisPreparationState,
  completedUnitCount = state === 'ready' ? analysisPreparationUnitCounts[system] : 0
): AnalysisPreparationProgress {
  const totalUnitCount = analysisPreparationUnitCounts[system];
  if (!Number.isInteger(completedUnitCount) || completedUnitCount < 0) {
    throw new RangeError('Analysis preparation progress must be a nonnegative integer.');
  }
  const boundedCompletedUnitCount = Math.min(totalUnitCount, completedUnitCount);
  if (state === 'ready' && boundedCompletedUnitCount !== totalUnitCount) {
    throw new RangeError('Ready analysis preparation must include every required operation.');
  }
  return {
    completedUnitCount: boundedCompletedUnitCount,
    state,
    totalUnitCount
  };
}

export function preparationProgressFromQueryStatuses(
  system: AnalysisSystemId,
  statuses: readonly ('idle' | 'loading' | 'ready' | 'error')[]
): AnalysisPreparationProgress {
  const totalUnitCount = analysisPreparationUnitCounts[system];
  if (statuses.length !== totalUnitCount) {
    throw new RangeError(
      `${system} preparation requires ${totalUnitCount} measured query statuses.`
    );
  }
  const completedUnitCount = statuses.filter((status) => status === 'ready').length;
  const state: AnalysisPreparationState = statuses.some((status) => status === 'error')
    ? 'error'
    : completedUnitCount === totalUnitCount
      ? 'ready'
      : 'loading';
  return { completedUnitCount, state, totalUnitCount };
}

export function mergeAnalysisPreparationProgress(
  current: AnalysisPreparationProgress,
  next: AnalysisPreparationProgress
): AnalysisPreparationProgress {
  if (current.totalUnitCount !== next.totalUnitCount) {
    throw new RangeError('Analysis preparation progress totals cannot change within a project revision.');
  }
  return {
    completedUnitCount: Math.max(current.completedUnitCount, next.completedUnitCount),
    state: current.state === 'ready' && next.state === 'loading'
      ? 'ready'
      : next.state,
    totalUnitCount: current.totalUnitCount
  };
}

export function emptyAnalysisPreparationProgress(): AnalysisPreparationProgressBySystem {
  return {
    balanceLab: createAnalysisPreparationProgress('balanceLab', 'waiting'),
    gameModules: createAnalysisPreparationProgress('gameModules', 'waiting'),
    guidedDesign: createAnalysisPreparationProgress('guidedDesign', 'waiting'),
    researchLab: createAnalysisPreparationProgress('researchLab', 'waiting'),
    semanticMerge: createAnalysisPreparationProgress('semanticMerge', 'waiting'),
    semanticProject: createAnalysisPreparationProgress('semanticProject', 'waiting')
  };
}

export function createAnalysisPreparationScopeState(options: {
  scopeKey: string | null;
  semanticProgress: AnalysisPreparationProgress;
}): AnalysisPreparationScopeState {
  return {
    preloadTools: [],
    progressBySystem: {
      ...emptyAnalysisPreparationProgress(),
      semanticProject: options.scopeKey
        ? options.semanticProgress
        : createAnalysisPreparationProgress('semanticProject', 'waiting')
    },
    scopeKey: options.scopeKey
  };
}

export function resolveAnalysisPreparationScopeState(
  current: AnalysisPreparationScopeState,
  options: {
    scopeKey: string | null;
    semanticProgress: AnalysisPreparationProgress;
  }
): AnalysisPreparationScopeState {
  return current.scopeKey === options.scopeKey
    ? current
    : createAnalysisPreparationScopeState(options);
}

export function createAnalysisPreparationSnapshot(options: {
  mode: AnalysisLoadingMode;
  progressBySystem: AnalysisPreparationProgressBySystem;
}): AnalysisPreparationSnapshot {
  const targetSystems: readonly AnalysisSystemId[] = [
    'semanticProject',
    ...analysisPreloadOrder(options.mode)
  ];
  const readyCount = targetSystems.filter(
    (system) => options.progressBySystem[system].state === 'ready'
  ).length;
  const errorCount = targetSystems.filter(
    (system) => options.progressBySystem[system].state === 'error'
  ).length;
  const targetCount = targetSystems.length;
  const completedUnitCount = targetSystems.reduce(
    (total, system) => total + options.progressBySystem[system].completedUnitCount,
    0
  );
  const totalUnitCount = targetSystems.reduce(
    (total, system) => total + options.progressBySystem[system].totalUnitCount,
    0
  );
  const allRequiredToolsReady = readyCount === targetCount &&
    completedUnitCount === totalUnitCount;
  const measuredPercent = totalUnitCount === 0
    ? 100
    : Math.floor((completedUnitCount / totalUnitCount) * 100);
  return {
    completedUnitCount,
    errorCount,
    percent: allRequiredToolsReady ? 100 : Math.min(99, measuredPercent),
    progressBySystem: options.progressBySystem,
    readyCount,
    states: Object.fromEntries(analysisSystemIds.map((system) => (
      [system, options.progressBySystem[system].state]
    ))) as Record<AnalysisSystemId, AnalysisPreparationState>,
    targetCount,
    targetSystems,
    totalUnitCount
  };
}
