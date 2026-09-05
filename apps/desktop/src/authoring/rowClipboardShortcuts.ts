/* SPDX-License-Identifier: GPL-3.0-only */

import type { KeyboardEvent } from 'react';

export function handleRowClipboardShortcut(
  event: KeyboardEvent<HTMLElement>,
  copy: () => void,
  paste: () => void
): boolean {
  const key = event.key.toLowerCase();
  if (!(event.ctrlKey || event.metaKey) || event.altKey || event.shiftKey ||
      event.nativeEvent.isComposing || (key !== 'c' && key !== 'v')) {
    return false;
  }
  const target = event.target;
  if (target instanceof HTMLElement &&
      (target.isContentEditable || target.closest('input, textarea, select, [role="combobox"]'))) {
    return false;
  }
  if (key === 'c' && window.getSelection()?.toString()) {
    return false;
  }
  event.preventDefault();
  event.stopPropagation();
  if (!event.repeat) {
    (key === 'c' ? copy : paste)();
  }
  return true;
}
