/* SPDX-License-Identifier: GPL-3.0-only */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  createAnalysisPreparationSnapshot,
  emptyAnalysisPreparationStates,
  nextAnalysisPreloadTool,
  type AnalysisLoadingMode,
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
  semanticState: AnalysisPreparationState;
}) {
  const [preloadTools, setPreloadTools] = useState<readonly AnalysisToolId[]>([]);
  const [states, setStates] = useState(emptyAnalysisPreparationStates);
  const scopeGenerationRef = useRef(0);

  useEffect(() => {
    scopeGenerationRef.current += 1;
    setPreloadTools([]);
    setStates({
      ...emptyAnalysisPreparationStates(),
      semanticProject: options.scopeKey ? options.semanticState : 'waiting'
    });
  }, [options.scopeKey]);

  useEffect(() => {
    setStates((current) => current.semanticProject === options.semanticState
      ? current
      : { ...current, semanticProject: options.semanticState });
  }, [options.semanticState]);

  useEffect(() => {
    if (!options.scopeKey || options.semanticState !== 'ready') return;
    const nextTool = nextAnalysisPreloadTool({
      deferBackgroundWork: options.deferBackgroundWork,
      mode: options.mode,
      preloadTools,
      semanticState: options.semanticState,
      states
    });
    if (!nextTool) return;
    const generation = scopeGenerationRef.current;
    const idleWindow = window as IdleWindow;
    let timeoutHandle: number | null = null;
    let idleHandle: number | null = null;
    const mountNext = () => {
      if (scopeGenerationRef.current !== generation) return;
      setStates((current) => current[nextTool] === 'waiting'
        ? { ...current, [nextTool]: 'loading' }
        : current);
      setPreloadTools((current) => current.includes(nextTool)
        ? current
        : [...current, nextTool]);
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
    options.semanticState,
    preloadTools,
    states
  ]);

  const reportState = useCallback((system: AnalysisSystemId, state: AnalysisPreparationState) => {
    setStates((current) => current[system] === state
      ? current
      : { ...current, [system]: state });
  }, []);
  const requestTool = useCallback((tool: AnalysisToolId) => {
    setStates((current) => current[tool] === 'waiting'
      ? { ...current, [tool]: 'loading' }
      : current);
  }, []);

  const snapshot = useMemo(
    () => createAnalysisPreparationSnapshot({ mode: options.mode, states }),
    [options.mode, states]
  );

  return { preloadTools, reportState, requestTool, snapshot };
}
