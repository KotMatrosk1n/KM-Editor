/* SPDX-License-Identifier: GPL-3.0-only */

import { useCallback, useEffect, useRef, useState } from 'react';

/**
 * Keeps a controlled text input responsive while collapsing a same-task burst
 * of synthetic/native input events into one React state commit. Ordinary user
 * input still commits in a microtask, before the browser's next paint.
 */
export function useCoalescedTextInputState(initialValue = '') {
  const [value, setValue] = useState(initialValue);
  const pendingValueRef = useRef(initialValue);
  const queuedRef = useRef(false);
  const lifecycleRevisionRef = useRef(0);

  useEffect(
    () => () => {
      lifecycleRevisionRef.current += 1;
      queuedRef.current = false;
    },
    []
  );

  const setCoalescedValue = useCallback((nextValue: string) => {
    pendingValueRef.current = nextValue;
    if (queuedRef.current) {
      return;
    }

    queuedRef.current = true;
    const lifecycleRevision = lifecycleRevisionRef.current;
    queueMicrotask(() => {
      if (lifecycleRevision !== lifecycleRevisionRef.current) {
        return;
      }

      queuedRef.current = false;
      const pendingValue = pendingValueRef.current;
      setValue((currentValue) =>
        currentValue === pendingValue ? currentValue : pendingValue
      );
    });
  }, []);

  return [value, setCoalescedValue] as const;
}
