/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import type { EditSession } from '../../bridge/contracts';
import { getIvScreenPendingOperation } from './ivScreenPending';

const actionPayloadHash = 'B5BEA41B6C623F7C09F1BF24DCAE58EBAB3C0CDD90AD966BC43A45B44867E12B';

function createSources(operation: 'install' | 'uninstall' = 'install', layered = false) {
  return [
    { layer: 'base' as const, relativePath: 'exefs/main' },
    ...(layered ? [{ layer: 'layered' as const, relativePath: 'exefs/main' }] : []),
    {
      layer: 'pending' as const,
      relativePath: `pending/iv-screen/${operation}/${actionPayloadHash}`
    }
  ];
}

function createSession(
  overrides: Partial<EditSession['pendingEdits'][number]> = {}
): EditSession {
  return {
    hasPendingChanges: true,
    pendingEdits: [
      {
        domain: 'workflow.ivScreen',
        field: 'install',
        newValue: 'true',
        recordId: 'iv-screen-v1-install',
        sources: createSources(),
        summary: 'Stage IV Screen install or refresh.',
        ...overrides
      }
    ],
    sessionId: 'iv-screen-session'
  };
}

describe('IV Screen pending edit identity', () => {
  it('accepts only the exact install and uninstall identities', () => {
    expect(getIvScreenPendingOperation(createSession())).toBe('install');
    expect(
      getIvScreenPendingOperation(
        createSession({
          field: 'uninstall',
          recordId: 'iv-screen-v1-uninstall',
          sources: createSources('uninstall'),
          summary: 'Stage IV Screen uninstall.'
        })
      )
    ).toBe('uninstall');
  });

  it('accepts the canonical optional LayeredFS source in exact order', () => {
    expect(getIvScreenPendingOperation(createSession({ sources: createSources('install', true) })))
      .toBe('install');
  });

  it.each([
    [
      'forged extra source',
      [...createSources(), { layer: 'generated' as const, relativePath: 'exefs/main' }]
    ],
    ['source order', [...createSources()].reverse()],
    [
      'base layer',
      createSources().map((source, index) =>
        index === 0 ? { ...source, layer: 'layered' as const } : source
      )
    ],
    [
      'base path',
      createSources().map((source, index) =>
        index === 0 ? { ...source, relativePath: 'romfs/exefs/main' } : source
      )
    ],
    [
      'LayeredFS path',
      createSources('install', true).map((source, index) =>
        index === 1 ? { ...source, relativePath: 'exefs/subsdk9' } : source
      )
    ],
    [
      'pending action',
      createSources().map((source, index, sources) =>
        index === sources.length - 1
          ? {
              ...source,
              relativePath: `pending/iv-screen/uninstall/${actionPayloadHash}`
            }
          : source
      )
    ],
    [
      'pending fingerprint',
      createSources().map((source, index, sources) =>
        index === sources.length - 1
          ? { ...source, relativePath: 'pending/iv-screen/install/forged' }
          : source
      )
    ]
  ])('rejects forged canonical sources: %s', (_label, sources) => {
    expect(getIvScreenPendingOperation(createSession({ sources }))).toBeNull();
  });

  it.each([
    ['wrong domain', { domain: 'workflow.items' }],
    ['wrong install field', { field: 'uninstall' }],
    ['wrong uninstall field', { field: 'install', recordId: 'iv-screen-v1-uninstall' }],
    ['wrong record', { recordId: 'iv-screen-v2-install' }],
    ['wrong summary', { summary: 'Stage IV Screen install.' }],
    ['false payload', { newValue: 'false' }],
    ['noncanonical payload', { newValue: 'True' }]
  ])('rejects a %s edit', (_label, overrides) => {
    expect(getIvScreenPendingOperation(createSession(overrides))).toBeNull();
  });

  it('rejects missing and mixed-owner sessions', () => {
    expect(getIvScreenPendingOperation(null)).toBeNull();
    expect(
      getIvScreenPendingOperation({
        ...createSession(),
        pendingEdits: [
          ...createSession().pendingEdits,
          { ...createSession().pendingEdits[0]!, domain: 'workflow.items' }
        ]
      })
    ).toBeNull();
  });
});
