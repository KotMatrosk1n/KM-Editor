/* SPDX-License-Identifier: GPL-3.0-only */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  createAnalysisPreparationProgress,
  createAnalysisPreparationScopeState,
  createAnalysisPreparationSnapshot,
  mergeAnalysisPreparationProgress,
  nextAnalysisPreloadTool,
  resolveAnalysisPreparationScopeState,
  type AnalysisLoadingMode,
  type AnalysisPreparationProgress,
  type AnalysisPreparationState,
  type AnalysisSystemId,
  type AnalysisToolId
} from './analysisPreparation';

type IdleWindow = Window & typeof globalThis & {
  cancelIdleCallback?: (handle: number) => void;
  requestIdleCallback?: (callback: () => void, options?: { timeout: number }) => number;
};

export function useAnalysisPreparation(options: {
  deferBackgroundWork: boolean;
  mode: AnalysisLoadingMode;
  scopeKey: string | null;
  semanticProgress: AnalysisPreparationProgress;
}) {
  const [scopeState, setScopeState] = useState(() => (
    createAnalysisPreparationScopeState(options)
  ));
  const scopeGenerationRef = useRef(0);
  const visibleScopeState = useMemo(
    () => resolveAnalysisPreparationScopeState(scopeState, options),
    [
      options.scopeKey,
      options.semanticProgress.completedUnitCount,
      options.semanticProgress.state,
      options.semanticProgress.totalUnitCount,
      scopeState
    ]
  );
  const { preloadTools, progressBySystem } = visibleScopeState;

  useEffect(() => {
    scopeGenerationRef.current += 1;
    setScopeState((current) => resolveAnalysisPreparationScopeState(current, options));
  }, [options.scopeKey]);

  useEffect(() => {
    const scopeKey = options.scopeKey;
    setScopeState((current) => {
      if (current.scopeKey !== scopeKey) return current;
      const next = mergeAnalysisPreparationProgress(
        current.progressBySystem.semanticProject,
        options.semanticProgress
      );
      return next.state === current.progressBySystem.semanticProject.state &&
        next.completedUnitCount === current.progressBySystem.semanticProject.completedUnitCount
        ? current
        : {
            ...current,
            progressBySystem: { ...current.progressBySystem, semanticProject: next }
          };
    });
  }, [
    options.scopeKey,
    options.semanticProgress.completedUnitCount,
    options.semanticProgress.state,
    options.semanticProgress.totalUnitCount
  ]);

  useEffect(() => {
    if (!options.scopeKey || options.semanticProgress.state !== 'ready') return;
    const states = Object.fromEntries(
      Object.entries(progressBySystem).map(([system, progress]) => [system, progress.state])
    ) as Record<AnalysisSystemId, AnalysisPreparationState>;
    const nextTool = nextAnalysisPreloadTool({
      deferBackgroundWork: options.deferBackgroundWork,
      mode: options.mode,
      preloadTools,
      semanticState: options.semanticProgress.state,
      states
    });
    if (!nextTool) return;
    const generation = scopeGenerationRef.current;
    const scopeKey = options.scopeKey;
    const idleWindow = window as IdleWindow;
    let timeoutHandle: number | null = null;
    let idleHandle: number | null = null;
    const mountNext = () => {
      if (scopeGenerationRef.current !== generation) return;
      setScopeState((current) => {
        if (current.scopeKey !== scopeKey) return current;
        const progress = current.progressBySystem[nextTool].state === 'waiting'
          ? createAnalysisPreparationProgress(nextTool, 'loading')
          : current.progressBySystem[nextTool];
        const tools = current.preloadTools.includes(nextTool)
          ? current.preloadTools
          : [...current.preloadTools, nextTool];
        return progress === current.progressBySystem[nextTool]
          && tools === current.preloadTools
          ? current
          : {
              ...current,
              preloadTools: tools,
              progressBySystem: { ...current.progressBySystem, [nextTool]: progress }
            };
      });
    };

    if (options.mode === 'balanced' && idleWindow.requestIdleCallback) {
      idleHandle = idleWindow.requestIdleCallback(mountNext, { timeout: 1_000 });
    } else {
      timeoutHandle = window.setTimeout(mountNext, 0);
    }

    return () => {
      if (idleHandle !== null) idleWindow.cancelIdleCallback?.(idleHandle);
      if (timeoutHandle !== null) window.clearTimeout(timeoutHandle);
    };
  }, [
    options.deferBackgroundWork,
    options.mode,
    options.scopeKey,
    options.semanticProgress.state,
    preloadTools,
    progressBySystem
  ]);

  const reportProgress = useCallback((
    system: AnalysisSystemId,
    progress: AnalysisPreparationProgress
  ) => {
    const scopeKey = options.scopeKey;
    setScopeState((current) => {
      if (current.scopeKey !== scopeKey) return current;
      const next = mergeAnalysisPreparationProgress(
        current.progressBySystem[system],
        progress
      );
      return next.state === current.progressBySystem[system].state &&
        next.completedUnitCount === current.progressBySystem[system].completedUnitCount
        ? current
        : {
            ...current,
            progressBySystem: { ...current.progressBySystem, [system]: next }
          };
    });
  }, [options.scopeKey]);
  const requestTool = useCallback((tool: AnalysisToolId) => {
    const scopeKey = options.scopeKey;
    setScopeState((current) => {
      if (current.scopeKey !== scopeKey
          || current.progressBySystem[tool].state !== 'waiting') {
        return current;
      }
      return {
        ...current,
        progressBySystem: {
          ...current.progressBySystem,
          [tool]: createAnalysisPreparationProgress(tool, 'loading')
        }
      };
    });
  }, [options.scopeKey]);

  const snapshot = useMemo(
    () => createAnalysisPreparationSnapshot({ mode: options.mode, progressBySystem }),
    [options.mode, progressBySystem]
  );

  return { preloadTools, reportProgress, requestTool, snapshot };
}
