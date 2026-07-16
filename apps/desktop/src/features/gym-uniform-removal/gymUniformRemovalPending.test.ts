/* SPDX-License-Identifier: GPL-3.0-only */

import { type EditSession } from '../../bridge/contracts';
import {
  type GymUniformRemovalAction,
  type GymUniformRemovalWorkflow,
  getGymUniformRemovalIpsRelativePath
} from '../../bridge/gymUniformRemovalContracts';
import { createGymUniformRemovalWorkflow } from '../../testSupport/gymUniformRemovalTestFixtures';
import { calculatePendingPayloadSha256 } from '../../utils/pendingPayloadHash';
import { getCanonicalGymUniformRemovalPendingAction } from './gymUniformRemovalPending';

describe('Gym Uniform Removal pending identity', () => {
  it.each(['install', 'uninstall'] as const)(
    'recognizes exact base-only %s sources',
    (action) => {
      const workflow = createGymUniformRemovalWorkflow(
        'sword',
        action === 'uninstall'
      );
      expect(getCanonicalGymUniformRemovalPendingAction(
        createSession(workflow, action),
        workflow
      )).toBe(action);
    }
  );

  it('uses optional readable Layered only for install and never for IPS-only uninstall', () => {
    const base = createGymUniformRemovalWorkflow('shield', true);
    const workflow: GymUniformRemovalWorkflow = {
      ...base,
      provenance: {
        fileState: 'layeredOverride',
        sourceFile: 'exefs/main',
        sourceLayer: 'layered'
      },
      stats: { ...base.stats, sourceFileCount: 3 }
    };
    const installSession = createSession(workflow, 'install');

    expect(installSession.pendingEdits[0]?.sources.map((source) => source.layer)).toEqual([
      'base',
      'layered',
      'pending',
      'generated'
    ]);
    expect(getCanonicalGymUniformRemovalPendingAction(installSession, workflow)).toBe('install');

    const uninstallSession = createSession(workflow, 'uninstall');
    expect(uninstallSession.pendingEdits[0]?.sources.map((source) => source.layer)).toEqual([
      'base',
      'pending',
      'generated'
    ]);
    expect(getCanonicalGymUniformRemovalPendingAction(uninstallSession, workflow))
      .toBe('uninstall');
  });

  it.each([
    ['sessionId', ''],
    ['hasPendingChanges', false]
  ] as const)('rejects false session truth in %s', (field, value) => {
    const workflow = createGymUniformRemovalWorkflow();
    expect(getCanonicalGymUniformRemovalPendingAction({
      ...createSession(workflow, 'install'),
      [field]: value
    }, workflow)).toBeNull();
  });

  it.each([
    ['domain', 'workflow.fashionUnlock'],
    ['recordId', 'gym-uniform-removal-v1-uninstall'],
    ['field', 'uninstall'],
    ['newValue', 'TRUE'],
    ['summary', 'Stage another patch.']
  ] as const)('rejects forged %s metadata', (field, value) => {
    const workflow = createGymUniformRemovalWorkflow();
    const session = createSession(workflow, 'install');
    expect(getCanonicalGymUniformRemovalPendingAction({
      ...session,
      pendingEdits: [{ ...session.pendingEdits[0]!, [field]: value }]
    }, workflow)).toBeNull();
  });

  it('rejects extra, reordered, wrong-layer, wrong-path, and wrong-hash sources', () => {
    const workflow = createGymUniformRemovalWorkflow();
    const canonical = createSession(workflow, 'install');
    const sources = canonical.pendingEdits[0]!.sources;
    const candidates = [
      [...sources, { layer: 'base' as const, relativePath: 'exefs/extra' }],
      [sources[1]!, sources[0]!, sources[2]!],
      [{ ...sources[0]!, layer: 'layered' as const }, sources[1]!, sources[2]!],
      [{ ...sources[0]!, relativePath: 'romfs/main' }, sources[1]!, sources[2]!],
      [
        sources[0]!,
        {
          ...sources[1]!,
          relativePath: `pending/gym-uniform-removal/install/${'A'.repeat(64)}`
        },
        sources[2]!
      ]
    ];

    for (const candidate of candidates) {
      expect(getCanonicalGymUniformRemovalPendingAction({
        ...canonical,
        pendingEdits: [{ ...canonical.pendingEdits[0]!, sources: candidate }]
      }, workflow)).toBeNull();
    }
  });

  it('rejects multiple edits, missing workflow, and unverified provenance', () => {
    const workflow = createGymUniformRemovalWorkflow();
    const session = createSession(workflow, 'install');
    expect(getCanonicalGymUniformRemovalPendingAction({
      ...session,
      pendingEdits: [...session.pendingEdits, session.pendingEdits[0]!]
    }, workflow)).toBeNull();
    expect(getCanonicalGymUniformRemovalPendingAction(session, null)).toBeNull();
    expect(getCanonicalGymUniformRemovalPendingAction(session, {
      ...workflow,
      detectedGame: null,
      provenance: {
        fileState: 'baseOnly',
        sourceFile: 'exefs/main',
        sourceLayer: 'generated'
      }
    })).toBeNull();
  });
});

function createSession(
  workflow: GymUniformRemovalWorkflow,
  action: GymUniformRemovalAction
): EditSession {
  const game = workflow.detectedGame ?? 'sword';
  return {
    hasPendingChanges: true,
    pendingEdits: [
      {
        domain: 'workflow.gymUniformRemoval',
        field: action,
        newValue: 'true',
        recordId: `gym-uniform-removal-v1-${action}`,
        sources: [
          { layer: 'base', relativePath: 'exefs/main' },
          ...(action === 'install' && workflow.provenance.sourceLayer === 'layered'
            ? [{ layer: 'layered' as const, relativePath: 'exefs/main' }]
            : []),
          {
            layer: 'pending',
            relativePath:
              `pending/gym-uniform-removal/${action}/${calculatePendingPayloadSha256('true')}`
          },
          {
            layer: 'generated',
            relativePath: getGymUniformRemovalIpsRelativePath(game)
          }
        ],
        summary: `Stage Gym Uniform Removal ${action}.`
      }
    ],
    sessionId: 'session-gym-uniform-removal'
  };
}
