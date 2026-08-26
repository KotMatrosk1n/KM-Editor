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

export type AnalysisPreparationStates = Record<
  AnalysisSystemId,
  AnalysisPreparationState
>;

export type AnalysisPreparationSnapshot = {
  completedCount: number;
  errorCount: number;
  percent: number;
  readyCount: number;
  states: AnalysisPreparationStates;
  targetCount: number;
  targetSystems: readonly AnalysisSystemId[];
};

const analysisLoadingModeStorageKey = 'km-editor.analysis-loading-mode.v1';

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
  states: AnalysisPreparationStates;
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

export function emptyAnalysisPreparationStates(): AnalysisPreparationStates {
  return {
    balanceLab: 'waiting',
    gameModules: 'waiting',
    guidedDesign: 'waiting',
    researchLab: 'waiting',
    semanticMerge: 'waiting',
    semanticProject: 'waiting'
  };
}

export function createAnalysisPreparationSnapshot(options: {
  mode: AnalysisLoadingMode;
  states: AnalysisPreparationStates;
}): AnalysisPreparationSnapshot {
  const targetSystems: readonly AnalysisSystemId[] = [
    'semanticProject',
    ...analysisPreloadOrder(options.mode)
  ];
  const completedCount = targetSystems.filter((system) => (
    options.states[system] === 'ready' || options.states[system] === 'error'
  )).length;
  const readyCount = targetSystems.filter(
    (system) => options.states[system] === 'ready'
  ).length;
  const errorCount = targetSystems.filter(
    (system) => options.states[system] === 'error'
  ).length;
  const targetCount = targetSystems.length;
  return {
    completedCount,
    errorCount,
    percent: targetCount === 0 ? 100 : Math.round((completedCount / targetCount) * 100),
    readyCount,
    states: options.states,
    targetCount,
    targetSystems
  };
}

export function preparationStateFromQueryStatus(
  status: 'idle' | 'loading' | 'ready' | 'error'
): AnalysisPreparationState {
  return status === 'idle' ? 'loading' : status;
}
