/* SPDX-License-Identifier: GPL-3.0-only */

import { type EditSession } from '../../bridge/contracts';
import { createFairyGymBoostsWorkflowFixture } from '../../testSupport/fairyGymBoostsTestFixtures';
import { calculatePendingPayloadSha256 } from '../../utils/pendingPayloadHash';
import {
  decodeFairyGymBoostPendingSelections,
  encodeFairyGymBoostPendingSelections,
  getCanonicalFairyGymBoostPendingSelections
} from './fairyGymBoostsPending';

describe('Fairy Gym Boosts pending identity', () => {
  it('round trips the exact twelve-selection canonical payload', () => {
    const { workflow, selections } = createFixture();
    const payload = encodeFairyGymBoostPendingSelections(selections);
    const session = createSession(workflow, payload);

    expect(decodeFairyGymBoostPendingSelections(payload)).toEqual(selections);
    expect(getCanonicalFairyGymBoostPendingSelections(session, workflow)).toEqual(
      selections
    );
    expect(
      getCanonicalFairyGymBoostPendingSelections(
        { ...session, hasPendingChanges: false },
        workflow
      )
    ).toBeNull();
    expect(
      getCanonicalFairyGymBoostPendingSelections(
        { ...session, sessionId: '' },
        workflow
      )
    ).toBeNull();
  });

  it.each([
    'annette-weakness-poison:01:increase',
    'annette-weakness-poison:1:none',
    'unknown:1:increase'
  ])('rejects a malformed first entry: %s', (firstEntry) => {
    const { selections } = createFixture();
    const entries = encodeFairyGymBoostPendingSelections(selections).split(';');
    entries[0] = firstEntry;

    expect(decodeFairyGymBoostPendingSelections(entries.join(';'))).toBeNull();
  });

  it('rejects missing, duplicate, and reordered selections', () => {
    const { selections } = createFixture();
    expect(
      decodeFairyGymBoostPendingSelections(
        encodeFairyGymBoostPendingSelections(selections.slice(0, -1))
      )
    ).toBeNull();
    expect(
      decodeFairyGymBoostPendingSelections(
        encodeFairyGymBoostPendingSelections([
          selections[0]!,
          selections[0]!,
          ...selections.slice(2)
        ])
      )
    ).toBeNull();
    expect(
      decodeFairyGymBoostPendingSelections(
        encodeFairyGymBoostPendingSelections([
          selections[1]!,
          selections[0]!,
          ...selections.slice(2)
        ])
      )
    ).toBeNull();
  });

  it.each(['recordId', 'field', 'summary', 'payloadHash', 'baseSource'] as const)(
    'rejects a forged %s',
    (part) => {
      const { workflow, selections } = createFixture();
      const payload = encodeFairyGymBoostPendingSelections(selections);
      const session = createSession(workflow, payload);
      const edit = session.pendingEdits[0]!;
      const forged: EditSession = {
        ...session,
        pendingEdits: [
          part === 'recordId'
            ? { ...edit, recordId: 'other' }
            : part === 'field'
              ? { ...edit, field: 'other' }
              : part === 'summary'
                ? { ...edit, summary: 'Other summary.' }
                : part === 'payloadHash'
                  ? {
                      ...edit,
                      sources: edit.sources.map((source) =>
                        source.layer === 'pending'
                          ? {
                              ...source,
                              relativePath: `pending/fairy-gym-boosts/selections/${'A'.repeat(64)}`
                            }
                          : source
                      )
                    }
                  : { ...edit, sources: edit.sources.slice(1) }
        ]
      };

      expect(
        getCanonicalFairyGymBoostPendingSelections(forged, workflow)
      ).toBeNull();
    }
  );

  it('requires the layered source identity when a sequence is layered', () => {
    const { workflow: baseWorkflow, selections } = createFixture();
    const workflow = {
      ...baseWorkflow,
      sources: baseWorkflow.sources.map((source, index) =>
        index === 0
          ? {
              ...source,
              provenance: {
                ...source.provenance,
                fileState: 'layeredOverride' as const,
                sourceLayer: 'layered' as const
              }
            }
          : source
      )
    };
    const payload = encodeFairyGymBoostPendingSelections(selections);
    const session = createSession(workflow, payload);

    expect(getCanonicalFairyGymBoostPendingSelections(session, workflow)).toEqual(
      selections
    );
    expect(
      getCanonicalFairyGymBoostPendingSelections(
        {
          ...session,
          pendingEdits: [
            {
              ...session.pendingEdits[0]!,
              sources: session.pendingEdits[0]!.sources.filter(
                (source) => source.layer !== 'layered'
              )
            }
          ]
        },
        workflow
      )
    ).toBeNull();
  });
});

function createFixture() {
  const { fairyGymBoostsWorkflow: workflow } =
    createFairyGymBoostsWorkflowFixture(true);
  const selections = workflow.trainers.flatMap((trainer) =>
    trainer.boosts.map((boost) => ({
      boostId: boost.boostId,
      effectId: boost.effectId,
      resultKind: boost.resultKind
    }))
  );
  return { workflow, selections };
}

function createSession(
  workflow: ReturnType<typeof createFixture>['workflow'],
  payload: string
): EditSession {
  return {
    hasPendingChanges: true,
    pendingEdits: [
      {
        domain: 'workflow.fairyGymBoosts',
        field: 'boostSelections',
        newValue: payload,
        recordId: 'fairy-gym-boosts',
        sources: [
          ...workflow.sources.map((source) => ({
            layer: 'base' as const,
            relativePath: source.relativePath
          })),
          ...workflow.sources
            .filter((source) => source.provenance.sourceLayer === 'layered')
            .map((source) => ({
              layer: 'layered' as const,
              relativePath: source.relativePath
            })),
          {
            layer: 'pending',
            relativePath: `pending/fairy-gym-boosts/selections/${calculatePendingPayloadSha256(payload)}`
          }
        ],
        summary: 'Stage Fairy Gym boost outcomes.'
      }
    ],
    sessionId: 'session-fairy-gym-boosts'
  };
}
