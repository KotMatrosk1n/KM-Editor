/* SPDX-License-Identifier: GPL-3.0-only */

import type { WorkbenchSection } from '../../workbench/workbenchSections';

export const whatChangedPresentationStorageKey = 'km-editor.what-changed.v1';
export const whatChangedPresentationVersion = 1 as const;
export const currentWhatChangedContentRevision = 1;

export type WhatChangedHighlight = {
  actionSection?: WorkbenchSection;
  bodyKey: string;
  titleKey: string;
};

export type WhatChangedEntry = {
  contentRevision: number;
  highlights: readonly WhatChangedHighlight[];
  titleKey: string;
};

export const currentWhatChangedEntry: WhatChangedEntry = {
  contentRevision: currentWhatChangedContentRevision,
  highlights: [
    {
      actionSection: 'workbench',
      bodyKey: 'whatChanged.workspace.body',
      titleKey: 'whatChanged.workspace.title'
    },
    {
      actionSection: 'settings',
      bodyKey: 'whatChanged.accessibility.body',
      titleKey: 'whatChanged.accessibility.title'
    },
    {
      actionSection: 'health',
      bodyKey: 'whatChanged.diagnostics.body',
      titleKey: 'whatChanged.diagnostics.title'
    }
  ],
  titleKey: 'whatChanged.title'
};

export function shouldShowWhatChangedTour() {
  const seenContentRevision = readSeenContentRevision();
  if (seenContentRevision === null) {
    markCurrentWhatChangedContentSeen();
    return false;
  }
  return currentWhatChangedContentRevision > seenContentRevision;
}

export function markCurrentWhatChangedContentSeen() {
  if (typeof window === 'undefined') {
    return;
  }
  try {
    window.localStorage.setItem(
      whatChangedPresentationStorageKey,
      JSON.stringify({
        seenContentRevision: currentWhatChangedContentRevision,
        version: whatChangedPresentationVersion
      })
    );
  } catch {
    // Presentation state is optional when storage is unavailable.
  }
}

function readSeenContentRevision() {
  if (typeof window === 'undefined') {
    return null;
  }
  try {
    const value: unknown = JSON.parse(
      window.localStorage.getItem(whatChangedPresentationStorageKey) ?? 'null'
    );
    if (typeof value !== 'object' || value === null || Array.isArray(value)) {
      return null;
    }
    const candidate = value as Record<string, unknown>;
    return Object.keys(candidate).length === 2 &&
      candidate.version === whatChangedPresentationVersion &&
      typeof candidate.seenContentRevision === 'number' &&
      Number.isInteger(candidate.seenContentRevision) &&
      candidate.seenContentRevision >= 1 &&
      candidate.seenContentRevision <= 1_000_000
      ? candidate.seenContentRevision
      : null;
  } catch {
    return null;
  }
}
