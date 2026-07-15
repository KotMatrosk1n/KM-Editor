/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import type { EditSession } from '../../bridge/contracts';
import {
  createCanonicalCatchCapSelections,
  getOwnedCatchCapPendingEdit,
  isCatchCapUninstallPending,
  parseCatchCapPendingCaps
} from './catchCapPending';

const canonicalValue = '0=20;1=25;2=30;3=35;4=40;5=45;6=50;7=55;8=100';

function createSession(
  overrides: Partial<EditSession['pendingEdits'][number]> = {}
): EditSession {
  return {
    hasPendingChanges: true,
    pendingEdits: [
      {
        domain: 'workflow.catchCap',
        field: 'caps',
        newValue: canonicalValue,
        recordId: 'catch-cap-v1',
        sources: [{ layer: 'base', relativePath: 'exefs/main' }],
        summary: 'Stage Catch Cap values.',
        ...overrides
      }
    ],
    sessionId: 'catch-cap-session'
  };
}

describe('Catch Cap pending edit identity', () => {
  it('accepts only the exact owned install or uninstall identity', () => {
    const install = getOwnedCatchCapPendingEdit(createSession());
    expect([...parseCatchCapPendingCaps(install)!.entries()]).toEqual([
      [0, 20],
      [1, 25],
      [2, 30],
      [3, 35],
      [4, 40],
      [5, 45],
      [6, 50],
      [7, 55],
      [8, 100]
    ]);
    expect(isCatchCapUninstallPending(install)).toBe(false);

    const uninstall = getOwnedCatchCapPendingEdit(
      createSession({ field: 'uninstall', newValue: 'true', recordId: 'catch-cap-v1-uninstall' })
    );
    expect(parseCatchCapPendingCaps(uninstall)).toBeNull();
    expect(isCatchCapUninstallPending(uninstall)).toBe(true);

    expect(
      getOwnedCatchCapPendingEdit({
        ...createSession(),
        pendingEdits: [
          ...createSession().pendingEdits,
          { ...createSession().pendingEdits[0]!, domain: 'workflow.items' }
        ]
      })
    ).toBeNull();
  });

  it.each([
    ['wrong field', { field: 'levelCaps' }],
    ['wrong record', { recordId: 'catch-cap-v2' }],
    ['missing badge', { newValue: canonicalValue.replace(';7=55', '') }],
    ['noncanonical order', { newValue: '1=25;0=20;2=30;3=35;4=40;5=45;6=50;7=55;8=100' }],
    ['noncanonical integer', { newValue: canonicalValue.replace('2=30', '2=030') }],
    ['zero cap', { newValue: canonicalValue.replace('0=20', '0=0') }],
    ['raw byte above the editable range', { newValue: canonicalValue.replace('2=30', '2=255') }],
    ['descending cap', { newValue: canonicalValue.replace('2=30', '2=24') }],
    ['editable final badge', { newValue: canonicalValue.replace('8=100', '8=99') }]
  ])('rejects a %s payload', (_label, overrides) => {
    expect(parseCatchCapPendingCaps(getOwnedCatchCapPendingEdit(createSession(overrides)))).toBeNull();
  });

  it('sorts an exact nine-cap request and rejects incomplete or duplicate identities', () => {
    const selections = canonicalValue
      .split(';')
      .map((entry) => {
        const [badgeCount, levelCap] = entry.split('=').map(Number);
        return { badgeCount: badgeCount!, levelCap: levelCap! };
      })
      .reverse();

    expect(createCanonicalCatchCapSelections(selections)).toEqual([...selections].reverse());
    expect(createCanonicalCatchCapSelections(selections.slice(1))).toBeNull();
    expect(
      createCanonicalCatchCapSelections([
        ...selections.slice(0, 8),
        { badgeCount: 7, levelCap: 55 }
      ])
    ).toBeNull();
  });
});
