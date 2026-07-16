/* SPDX-License-Identifier: GPL-3.0-only */

import { type EditSession } from '../../bridge/contracts';
import { calculatePendingPayloadSha256 } from '../../utils/pendingPayloadHash';
import {
  createSvFashionUnlockWorkflow,
  createSwShFashionUnlockWorkflow
} from '../../testSupport/fashionUnlockTestFixtures';
import { getCanonicalFashionUnlockPendingAction } from './fashionUnlockPending';

const payloadHash = calculatePendingPayloadSha256('true');

describe('Fashion Unlock pending identity', () => {
  it('recognizes exact Sword/Shield base and layered install sources', () => {
    const baseWorkflow = createSwShFashionUnlockWorkflow();
    expect(getCanonicalFashionUnlockPendingAction(
      createSession('install', [
        { layer: 'base', relativePath: 'exefs/main' },
        { layer: 'pending', relativePath: `pending/fashion-unlock/install/${payloadHash}` }
      ]),
      baseWorkflow
    )).toBe('install');

    const layeredWorkflow = createSwShFashionUnlockWorkflow('sword', true);
    expect(getCanonicalFashionUnlockPendingAction(
      createSession('install', [
        { layer: 'base', relativePath: 'exefs/main' },
        { layer: 'layered', relativePath: 'exefs/main' },
        { layer: 'pending', relativePath: `pending/fashion-unlock/install/${payloadHash}` }
      ]),
      layeredWorkflow
    )).toBe('install');
  });

  it('recognizes exact Sword/Shield uninstall sources', () => {
    const workflow = createSwShFashionUnlockWorkflow('shield', true);
    expect(getCanonicalFashionUnlockPendingAction(
      createSession('uninstall', [
        { layer: 'base', relativePath: 'exefs/main' },
        { layer: 'layered', relativePath: 'exefs/main' },
        { layer: 'pending', relativePath: `pending/fashion-unlock/uninstall/${payloadHash}` }
      ]),
      workflow
    )).toBe('uninstall');
  });

  it('preserves Scarlet/Violet install and uninstall source identities', () => {
    const baseWorkflow = createSvFashionUnlockWorkflow();
    expect(getCanonicalFashionUnlockPendingAction(
      createSession('install', [{ layer: 'base', relativePath: 'exefs/main' }]),
      baseWorkflow
    )).toBe('install');

    const layeredWorkflow = createSvFashionUnlockWorkflow('violet', true);
    expect(getCanonicalFashionUnlockPendingAction(
      createSession('install', [{ layer: 'layered', relativePath: 'exefs/main' }]),
      layeredWorkflow
    )).toBe('install');
    expect(getCanonicalFashionUnlockPendingAction(
      createSession('uninstall', [
        { layer: 'generated', relativePath: 'exefs/main' },
        { layer: 'base', relativePath: 'exefs/main' }
      ]),
      layeredWorkflow
    )).toBe('uninstall');
  });

  it.each([
    ['sessionId', ''],
    ['hasPendingChanges', false]
  ] as const)('rejects false session truth in %s', (field, value) => {
    const workflow = createSwShFashionUnlockWorkflow();
    expect(getCanonicalFashionUnlockPendingAction(
      { ...createSwShInstallSession(), [field]: value },
      workflow
    )).toBeNull();
  });

  it.each([
    ['domain', 'workflow.typeChart'],
    ['recordId', 'fashion-unlock-v1-uninstall'],
    ['field', 'uninstall'],
    ['newValue', 'TRUE'],
    ['summary', 'Stage something else.']
  ] as const)('rejects a forged %s', (field, value) => {
    const workflow = createSwShFashionUnlockWorkflow();
    const session = createSwShInstallSession();
    expect(getCanonicalFashionUnlockPendingAction({
      ...session,
      pendingEdits: [{ ...session.pendingEdits[0]!, [field]: value }]
    }, workflow)).toBeNull();
  });

  it('rejects extra, reordered, wrong-layer, wrong-path, and wrong-hash sources', () => {
    const workflow = createSwShFashionUnlockWorkflow();
    const canonical = createSwShInstallSession();
    const sources = canonical.pendingEdits[0]!.sources;
    const candidates = [
      [...sources, { layer: 'base' as const, relativePath: 'exefs/extra' }],
      [sources[1]!, sources[0]!],
      [{ ...sources[0]!, layer: 'layered' as const }, sources[1]!],
      [{ ...sources[0]!, relativePath: 'romfs/main' }, sources[1]!],
      [sources[0]!, { ...sources[1]!, relativePath: `pending/fashion-unlock/install/${'A'.repeat(64)}` }]
    ];

    for (const candidate of candidates) {
      expect(getCanonicalFashionUnlockPendingAction({
        ...canonical,
        pendingEdits: [{ ...canonical.pendingEdits[0]!, sources: candidate }]
      }, workflow)).toBeNull();
    }
  });

  it('rejects multiple edits and a missing workflow', () => {
    const session = createSwShInstallSession();
    expect(getCanonicalFashionUnlockPendingAction({
      ...session,
      pendingEdits: [...session.pendingEdits, session.pendingEdits[0]!]
    }, createSwShFashionUnlockWorkflow())).toBeNull();
    expect(getCanonicalFashionUnlockPendingAction(session, null)).toBeNull();
  });
});

function createSwShInstallSession() {
  return createSession('install', [
    { layer: 'base', relativePath: 'exefs/main' },
    { layer: 'pending', relativePath: `pending/fashion-unlock/install/${payloadHash}` }
  ]);
}

function createSession(
  action: 'install' | 'uninstall',
  sources: EditSession['pendingEdits'][number]['sources']
): EditSession {
  return {
    hasPendingChanges: true,
    pendingEdits: [
      {
        domain: 'workflow.fashionUnlock',
        field: action,
        newValue: 'true',
        recordId: `fashion-unlock-v1-${action}`,
        sources,
        summary: `Stage Fashion Unlock ${action}.`
      }
    ],
    sessionId: 'session-fashion-unlock'
  };
}
