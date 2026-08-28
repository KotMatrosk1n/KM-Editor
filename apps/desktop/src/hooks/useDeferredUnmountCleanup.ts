/* SPDX-License-Identifier: GPL-3.0-only */

import { useEffect, useRef } from 'react';
import { DeferredStrictModeCleanup } from '../utils/projectAsyncPolicy';

/**
 * Defers destructive controller cleanup by one task. React Strict Mode replays
 * effects synchronously in development, so the replay cancels the pending
 * cleanup and keeps the original in-flight request usable. A real unmount has
 * no matching setup and therefore still performs cleanup immediately afterward.
 */
export function useDeferredUnmountCleanup(cleanup: () => void) {
  const cleanupRef = useRef(cleanup);
  const deferredCleanupRef = useRef<DeferredStrictModeCleanup | null>(null);
  if (deferredCleanupRef.current === null) {
    deferredCleanupRef.current = new DeferredStrictModeCleanup();
  }
  const deferredCleanup = deferredCleanupRef.current;
  cleanupRef.current = cleanup;

  useEffect(() => {
    deferredCleanup.cancel();
    return () => {
      deferredCleanup.schedule(() => cleanupRef.current());
    };
  }, [deferredCleanup]);
}
