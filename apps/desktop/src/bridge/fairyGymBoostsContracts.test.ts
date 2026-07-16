/* SPDX-License-Identifier: GPL-3.0-only */

import { createFairyGymBoostsWorkflowFixture } from '../testSupport/fairyGymBoostsTestFixtures';
import {
  fairyGymBoostsWorkflowSchema,
  stageFairyGymBoostsRequestSchema
} from './fairyGymBoostsContracts';

const projectPaths = {
  baseExeFsPath: 'base-exefs',
  baseRomFsPath: 'base-romfs',
  outputRootPath: 'output',
  pokemonLegendsZASupportFolderPath: '',
  saveFilePath: '',
  scarletVioletSupportFolderPath: '',
  selectedGame: 'sword' as const
};

describe('Fairy Gym Boosts bridge contracts', () => {
  it.each(['sword', 'shield'] as const)(
    'accepts the canonical %s source, trainer, answer, and effect mappings',
    (detectedGame) => {
      const { fairyGymBoostsWorkflow } = createFairyGymBoostsWorkflowFixture(true);
      expect(
        fairyGymBoostsWorkflowSchema.parse({
          ...fairyGymBoostsWorkflow,
          detectedGame
        })
      ).toMatchObject({ detectedGame });
    }
  );

  it('rejects missing, reordered, or falsely mapped source records', () => {
    const { fairyGymBoostsWorkflow } = createFairyGymBoostsWorkflowFixture(true);
    expect(() =>
      fairyGymBoostsWorkflowSchema.parse({
        ...fairyGymBoostsWorkflow,
        sources: fairyGymBoostsWorkflow.sources.slice(0, -1)
      })
    ).toThrow(/all six sequence sources/i);
    expect(() =>
      fairyGymBoostsWorkflowSchema.parse({
        ...fairyGymBoostsWorkflow,
        sources: [
          fairyGymBoostsWorkflow.sources[1],
          fairyGymBoostsWorkflow.sources[0],
          ...fairyGymBoostsWorkflow.sources.slice(2)
        ]
      })
    ).toThrow(/source mapping/i);
    expect(() =>
      fairyGymBoostsWorkflowSchema.parse({
        ...fairyGymBoostsWorkflow,
        sources: fairyGymBoostsWorkflow.sources.map((source, index) =>
          index === 0
            ? { ...source, status: 'blocked' as const }
            : source
        )
      })
    ).toThrow(/offsets do not match/i);
  });

  it('accepts an honestly blocked source and unavailable answers', () => {
    const { fairyGymBoostsWorkflow } = createFairyGymBoostsWorkflowFixture(true);
    const blockedPath = fairyGymBoostsWorkflow.sources[0]!.relativePath;
    const blocked = {
      ...fairyGymBoostsWorkflow,
      sources: fairyGymBoostsWorkflow.sources.map((source, index) =>
        index === 0
          ? {
              ...source,
              ownedRangeHex: 'unknown' as const,
              payloadOffsetHex: 'unknown' as const,
              status: 'blocked' as const
            }
          : source
      ),
      stats: {
        ...fairyGymBoostsWorkflow.stats,
        sourceFileCount: fairyGymBoostsWorkflow.stats.sourceFileCount - 1
      },
      trainers: fairyGymBoostsWorkflow.trainers.map((trainer) => ({
        ...trainer,
        boosts: trainer.boosts.map((boost) =>
          boost.sequenceFile === blockedPath
            ? { ...boost, isAvailable: false }
            : boost
        )
      }))
    };

    expect(fairyGymBoostsWorkflowSchema.parse(blocked).sources[0]?.status).toBe(
      'blocked'
    );
  });

  it('rejects source provenance that cannot produce the reported status', () => {
    const { fairyGymBoostsWorkflow } = createFairyGymBoostsWorkflowFixture(true);
    expect(() =>
      fairyGymBoostsWorkflowSchema.parse({
        ...fairyGymBoostsWorkflow,
        sources: fairyGymBoostsWorkflow.sources.map((source, index) =>
          index === 0
            ? {
                ...source,
                provenance: {
                  ...source.provenance,
                  fileState: 'layeredOnly' as const,
                  sourceLayer: 'generated' as const
                }
              }
            : source
        )
      })
    ).toThrow(/provenance does not match/i);
  });

  it('rejects swapped answers and effect details that do not match the effect id', () => {
    const { fairyGymBoostsWorkflow } = createFairyGymBoostsWorkflowFixture(true);
    const annette = fairyGymBoostsWorkflow.trainers[0]!;
    expect(() =>
      fairyGymBoostsWorkflowSchema.parse({
        ...fairyGymBoostsWorkflow,
        trainers: [
          {
            ...annette,
            boosts: [annette.boosts[1], annette.boosts[0]]
          },
          ...fairyGymBoostsWorkflow.trainers.slice(1)
        ]
      })
    ).toThrow(/answer mapping/i);
    expect(() =>
      fairyGymBoostsWorkflowSchema.parse({
        ...fairyGymBoostsWorkflow,
        trainers: fairyGymBoostsWorkflow.trainers.map((trainer, trainerIndex) =>
          trainerIndex === 0
            ? {
                ...trainer,
                boosts: trainer.boosts.map((boost, boostIndex) =>
                  boostIndex === 0
                    ? { ...boost, stageAmount: 2 }
                    : boost
                )
              }
            : trainer
        )
      })
    ).toThrow(/effect details/i);
  });

  it('requires every canonical selection in order with a compatible result kind', () => {
    const { fairyGymBoostsWorkflow } = createFairyGymBoostsWorkflowFixture(true);
    const selections = fairyGymBoostsWorkflow.trainers.flatMap((trainer) =>
      trainer.boosts.map((boost) => ({
        boostId: boost.boostId,
        effectId: boost.effectId,
        resultKind: boost.resultKind
      }))
    );

    expect(
      stageFairyGymBoostsRequestSchema.parse({
        paths: projectPaths,
        selections,
        session: null
      }).selections
    ).toHaveLength(12);
    expect(() =>
      stageFairyGymBoostsRequestSchema.parse({
        paths: projectPaths,
        selections: [selections[1], selections[0], ...selections.slice(2)],
        session: null
      })
    ).toThrow(/canonical answer/i);
    expect(() =>
      stageFairyGymBoostsRequestSchema.parse({
        paths: projectPaths,
        selections: selections.map((selection, index) =>
          index === 0
            ? { ...selection, effectId: 0, resultKind: 'increase' }
            : selection
        ),
        session: null
      })
    ).toThrow(/do not match/i);
    expect(() =>
      stageFairyGymBoostsRequestSchema.parse({
        paths: { ...projectPaths, selectedGame: 'scarlet' },
        selections,
        session: null
      })
    ).toThrow(/requires Sword or Shield/i);
  });
});
