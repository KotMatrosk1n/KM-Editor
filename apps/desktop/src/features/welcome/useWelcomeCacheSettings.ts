/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState } from 'react';
import type { ProjectBridge } from '../../bridge/projectBridge';
import type { ProjectGame } from '../../bridge/contracts';
import type { SvCacheMode, SvCacheStatus } from '../../bridge/svCacheContracts';
import type { ZaCacheMode, ZaCacheStatus } from '../../bridge/zaCacheContracts';
import type { SwShCacheMode, SwShCacheStatus } from '../../bridge/swShCacheContracts';

type CacheMode = SvCacheMode | ZaCacheMode | SwShCacheMode;
type CacheStatus = SvCacheStatus | ZaCacheStatus | SwShCacheStatus;

// Welcome settings never provide project paths or start a cache warmup.
export function useWelcomeCacheSettings(bridge: ProjectBridge, game: ProjectGame, enabled: boolean, onError: (error: unknown) => void) {
  const [state, setState] = useState<{ game: ProjectGame; status: CacheStatus | null; busy: boolean; error: boolean }>({ game, status: null, busy: false, error: false });
  const generation = useRef(0);
  const inFlight = useRef(false);
  useEffect(() => { generation.current++; setState({ game, status: null, busy: false, error: false }); return () => { generation.current++; }; }, [game, enabled]);
  async function run(update?: { mode: CacheMode; maxCacheSizeBytes: number } | 'clear') {
    if (!enabled || inFlight.current) return;
    const epoch = generation.current;
    inFlight.current = true;
    setState(current => ({ ...current, game, busy: true, error: false }));
    try {
      const swsh = game === 'sword' || game === 'shield';
      let response;
      if (update === 'clear') {
        response = swsh ? await bridge.clearSwShCache({ activePaths: null })
          : game === 'za' ? await bridge.clearZaCache({ activePaths: null }) : await bridge.clearSvCache({ activePaths: null });
      } else if (update) {
        const request = { ...update, paths: null };
        response = swsh ? await bridge.updateSwShCacheSettings(request)
          : game === 'za' ? await bridge.updateZaCacheSettings(request) : await bridge.updateSvCacheSettings(request);
      } else {
        response = swsh ? await bridge.getSwShCacheStatus({ paths: null })
          : game === 'za' ? await bridge.getZaCacheStatus({ paths: null }) : await bridge.getSvCacheStatus({ paths: null });
      }
      if (epoch === generation.current) setState({ game, status: response.status, busy: false, error: false });
    } catch (error) {
      if (epoch === generation.current) {
        setState({ game, status: null, busy: false, error: true });
        onError(error);
      }
    } finally { inFlight.current = false; }
  }
  const status = state.game === game ? state.status : null;
  return {
    status, busy: state.busy, error: state.game === game && state.error,
    refresh: () => void run(),
    clear: () => run('clear'),
    changeMode: (mode: CacheMode) => { if (status) void run({ mode, maxCacheSizeBytes: status.settings.maxCacheSizeBytes }); },
    changeLimit: (maxCacheSizeBytes: number) => { if (status) void run({ mode: status.settings.mode, maxCacheSizeBytes }); }
  };
}
