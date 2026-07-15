/* SPDX-License-Identifier: GPL-3.0-only */

import type { CatchCapSelection, EditSession } from '../../bridge/contracts';

type CatchCapPendingEdit = EditSession['pendingEdits'][number];

const catchCapCount = 9;

export function getOwnedCatchCapPendingEdit(editSession: EditSession | null) {
  if (
    editSession?.pendingEdits.length !== 1 ||
    editSession.pendingEdits[0]?.domain !== 'workflow.catchCap'
  ) {
    return null;
  }

  return editSession.pendingEdits[0];
}

export function parseCatchCapPendingCaps(edit: CatchCapPendingEdit | null | undefined) {
  if (
    edit?.domain !== 'workflow.catchCap' ||
    edit.recordId !== 'catch-cap-v1' ||
    edit.field !== 'caps' ||
    !edit.newValue
  ) {
    return null;
  }

  const parts = edit.newValue.split(';');
  if (parts.length !== catchCapCount) {
    return null;
  }

  const caps = new Map<number, number>();
  let previousLevelCap = 0;
  for (let badgeCount = 0; badgeCount < parts.length; badgeCount += 1) {
    const match = /^(0|[1-8])=([1-9]|[1-9]\d|100)$/.exec(parts[badgeCount] ?? '');
    if (!match || Number.parseInt(match[1], 10) !== badgeCount) {
      return null;
    }

    const levelCap = Number.parseInt(match[2], 10);
    if (levelCap < previousLevelCap || (badgeCount === 8 && levelCap !== 100)) {
      return null;
    }

    caps.set(badgeCount, levelCap);
    previousLevelCap = levelCap;
  }

  return caps;
}

export function isCatchCapUninstallPending(edit: CatchCapPendingEdit | null | undefined) {
  return (
    edit?.domain === 'workflow.catchCap' &&
    edit.recordId === 'catch-cap-v1-uninstall' &&
    edit.field === 'uninstall' &&
    edit.newValue === 'true'
  );
}

export function createCanonicalCatchCapSelections(
  selections: readonly CatchCapSelection[]
) {
  const sorted = [...selections].sort((left, right) => left.badgeCount - right.badgeCount);
  if (
    sorted.length !== catchCapCount ||
    sorted.some(
      (selection, badgeCount) =>
        selection.badgeCount !== badgeCount ||
        !Number.isInteger(selection.levelCap) ||
        selection.levelCap < 1 ||
        selection.levelCap > 100 ||
        (badgeCount > 0 && selection.levelCap < sorted[badgeCount - 1]!.levelCap) ||
        (badgeCount === catchCapCount - 1 && selection.levelCap !== 100)
    )
  ) {
    return null;
  }

  return sorted;
}
