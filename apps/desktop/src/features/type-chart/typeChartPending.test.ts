/* SPDX-License-Identifier: GPL-3.0-only */

import { type EditSession, type ProjectGame } from '../../bridge/contracts';
import {
  calculateTypeChartPayloadSha256,
  decodeTypeChartPendingValues,
  encodeTypeChartPendingValues,
  getCanonicalTypeChartPendingState,
  type TypeChartEffectivenessValue
} from './typeChartPending';

const values = Array.from(
  { length: 18 * 18 },
  (_, index) => [0, 2, 4, 8][index % 4] as TypeChartEffectivenessValue
);
const payload = encodeTypeChartPendingValues(values);

describe('Type Chart pending identity', () => {
  it('encodes and decodes the exact uppercase 324-byte payload', () => {
    expect(payload).toHaveLength(18 * 18 * 2);
    expect(payload).toMatch(/^[0248]+$/);
    expect(decodeTypeChartPendingValues(payload)).toEqual(values);
    expect(decodeTypeChartPendingValues(`${payload}04`)).toBeNull();
    expect(decodeTypeChartPendingValues(payload.replace('08', '01'))).toBeNull();
  });

  it('calculates the exact SHA-256 used by the pending payload source', async () => {
    const expectedBytes = await crypto.subtle.digest(
      'SHA-256',
      new TextEncoder().encode(payload)
    );
    const expected = Array.from(new Uint8Array(expectedBytes), (value) =>
      value.toString(16).padStart(2, '0')
    )
      .join('')
      .toUpperCase();

    expect(calculateTypeChartPayloadSha256(payload)).toBe(expected);
  });

  it.each(['sword', 'shield'] satisfies ProjectGame[])(
    'accepts only the canonical %s Base, optional Layered, and Pending SHA sources',
    (game) => {
      const baseSession = createChartSession(game);
      expect(getCanonicalTypeChartPendingState(baseSession, game)).toEqual({
        kind: 'chart',
        values
      });

      const layeredSession = createChartSession(game, true);
      expect(getCanonicalTypeChartPendingState(layeredSession, game)).toEqual({
        kind: 'chart',
        values
      });

      expect(
        getCanonicalTypeChartPendingState(
          updateOnlyEdit(layeredSession, (edit) => ({
            ...edit,
            sources: edit.sources.map((source) =>
              source.layer === 'pending'
                ? {
                    ...source,
                    relativePath: `pending/type-chart/effectiveness/${'A'.repeat(64)}`
                  }
                : source
            )
          })),
          game
        )
      ).toBeNull();
    }
  );

  it.each([
    ['scarlet', 'sv-type-chart'],
    ['violet', 'sv-type-chart'],
    ['za', 'za-type-chart']
  ] satisfies Array<[ProjectGame, string]>)(
    'preserves the existing %s chart identity',
    (game, recordId) => {
      const session = createChartSession(game);
      expect(session.pendingEdits[0]?.recordId).toBe(recordId);
      expect(getCanonicalTypeChartPendingState(session, game)).toEqual({
        kind: 'chart',
        values
      });
    }
  );

  it.each([
    ['scarlet', 'sv-type-chart-v1-uninstall'],
    ['violet', 'sv-type-chart-v1-uninstall'],
    ['za', 'za-type-chart-v1-uninstall']
  ] satisfies Array<[ProjectGame, string]>)(
    'preserves the existing %s uninstall identity',
    (game, recordId) => {
      const session = createUninstallSession(recordId);
      expect(getCanonicalTypeChartPendingState(session, game)).toEqual({
        kind: 'uninstall'
      });
    }
  );

  it('rejects noncanonical case, summary, family, sources, and mixed sessions', () => {
    const session = createChartSession('sword');
    const malformedSessions = [
      updateOnlyEdit(session, (edit) => ({ ...edit, summary: 'Stage values.' })),
      updateOnlyEdit(session, (edit) => ({ ...edit, recordId: 'sv-type-chart' })),
      updateOnlyEdit(session, (edit) => ({ ...edit, field: 'chart' })),
      updateOnlyEdit(session, (edit) => ({ ...edit, sources: edit.sources.slice(1) })),
      {
        ...session,
        pendingEdits: [...session.pendingEdits, session.pendingEdits[0]!]
      }
    ];

    for (const malformed of malformedSessions) {
      expect(getCanonicalTypeChartPendingState(malformed, 'sword')).toBeNull();
    }
    expect(getCanonicalTypeChartPendingState(session, 'scarlet')).toBeNull();
  });
});

function createChartSession(game: ProjectGame, includeLayered = false): EditSession {
  const sources: EditSession['pendingEdits'][number]['sources'] =
    game === 'sword' || game === 'shield'
      ? [
          { layer: 'base', relativePath: 'exefs/main' },
          ...(includeLayered
            ? [{ layer: 'layered' as const, relativePath: 'exefs/main' }]
            : []),
          {
            layer: 'pending',
            relativePath: `pending/type-chart/effectiveness/${calculateTypeChartPayloadSha256(payload)}`
          }
        ]
      : [{ layer: includeLayered ? 'layered' : 'base', relativePath: 'exefs/main' }];
  const recordId =
    game === 'sword' || game === 'shield'
      ? 'type-chart'
      : game === 'za'
        ? 'za-type-chart'
        : 'sv-type-chart';

  return {
    hasPendingChanges: true,
    pendingEdits: [
      {
        domain: 'workflow.typeChart',
        field: 'effectiveness',
        newValue: payload,
        recordId,
        sources,
        summary: 'Stage Type Chart effectiveness table.'
      }
    ],
    sessionId: `session-${game}`
  };
}

function createUninstallSession(recordId: string): EditSession {
  return {
    hasPendingChanges: true,
    pendingEdits: [
      {
        domain: 'workflow.typeChart',
        field: 'uninstall',
        newValue: 'true',
        recordId,
        sources: [
          { layer: 'generated', relativePath: 'exefs/main' },
          { layer: 'base', relativePath: 'exefs/main' }
        ],
        summary: 'Stage Type Chart uninstall.'
      }
    ],
    sessionId: `session-${recordId}`
  };
}

function updateOnlyEdit(
  session: EditSession,
  update: (edit: EditSession['pendingEdits'][number]) => EditSession['pendingEdits'][number]
): EditSession {
  return {
    ...session,
    pendingEdits: [update(session.pendingEdits[0]!)]
  };
}
