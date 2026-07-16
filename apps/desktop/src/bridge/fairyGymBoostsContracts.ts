/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import {
  apiDiagnosticSchema,
  editSessionSchema,
  projectFileGraphEntryStateSchema,
  projectFileLayerSchema,
  projectPathsSchema,
  workflowSummarySchema
} from './contracts';

export const loadFairyGymBoostsWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const fairyGymBoostStatSchema = z.enum(['atk', 'def', 'spAtk', 'spDef', 'speed']);
export const fairyGymBoostResultKindSchema = z.enum(['none', 'increase', 'decrease']);
export const fairyGymBoostSourceStatusSchema = z.enum(['available', 'missing', 'blocked']);
const fairyGymBoostGameSchema = z.enum(['sword', 'shield']);

export const fairyGymBoostSelectionSchema = z.strictObject({
  boostId: z.string(),
  effectId: z.number().int().min(0).max(6),
  resultKind: fairyGymBoostResultKindSchema
});

export const fairyGymBoostsProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const fairyGymBoostsSourceRecordSchema = z.strictObject({
  label: z.string(),
  ownedRangeHex: z.union([z.literal('unknown'), z.literal('0x00001550-0x0000155F')]),
  payloadOffsetHex: z.union([z.literal('unknown'), z.literal('0x00001550')]),
  provenance: fairyGymBoostsProvenanceSchema,
  relativePath: z.string(),
  sourceId: z.string(),
  status: fairyGymBoostSourceStatusSchema
});

export const fairyGymBoostRecordSchema = z.strictObject({
  affectedStats: z.array(fairyGymBoostStatSchema),
  answerChoice: z.number().int().min(1).max(2),
  answerText: z.string(),
  boostId: z.string(),
  defaultEffectId: z.number().int().min(0).max(6),
  defaultResultKind: fairyGymBoostResultKindSchema,
  effectId: z.number().int().min(0).max(6),
  effectLabel: z.string(),
  isAvailable: z.boolean(),
  questionText: z.string(),
  resultKind: fairyGymBoostResultKindSchema,
  sequenceFile: z.string(),
  stageAmount: z.number().int().min(0).max(2)
});

export const fairyGymBoostTrainerSchema = z.strictObject({
  boosts: z.array(fairyGymBoostRecordSchema),
  displayOrder: z.number().int().nonnegative(),
  npcName: z.string(),
  trainerId: z.number().int().nonnegative()
});

export const fairyGymBoostsWorkflowStatsSchema = z.strictObject({
  boostCount: z.number().int().nonnegative(),
  ownedByteCount: z.literal(96),
  sourceFileCount: z.number().int().nonnegative(),
  trainerCount: z.number().int().nonnegative()
});

const fairyGymBoostsWorkflowBaseSchema = z.strictObject({
  detectedGame: fairyGymBoostGameSchema.nullable(),
  diagnostics: z.array(apiDiagnosticSchema),
  sources: z.array(fairyGymBoostsSourceRecordSchema),
  stats: fairyGymBoostsWorkflowStatsSchema,
  summary: workflowSummarySchema,
  trainers: z.array(fairyGymBoostTrainerSchema)
});

export const fairyGymBoostsWorkflowSchema =
  fairyGymBoostsWorkflowBaseSchema.superRefine(validateFairyGymBoostsWorkflow);

export const loadFairyGymBoostsWorkflowResponseSchema = z.strictObject({
  workflow: fairyGymBoostsWorkflowSchema
});

export const stageFairyGymBoostsRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  selections: z.array(fairyGymBoostSelectionSchema).length(12),
  session: editSessionSchema.nullable()
}).superRefine((request, context) => {
  if (request.paths.selectedGame !== 'sword' && request.paths.selectedGame !== 'shield') {
    addIssue(context, ['paths', 'selectedGame'], 'Fairy Gym Boosts requires Sword or Shield.');
  }

  request.selections.forEach((selection, index) => {
    if (selection.boostId !== canonicalBoostDefinitions[index]?.boostId) {
      addIssue(
        context,
        ['selections', index, 'boostId'],
        'Fairy Gym Boosts selections must use every canonical answer in order.'
      );
    }

    if (!isSupportedOutcome(selection.effectId, selection.resultKind)) {
      addIssue(
        context,
        ['selections', index, 'resultKind'],
        'Fairy Gym Boosts selection effect and result kind do not match.'
      );
    }
  });
});

export const stageFairyGymBoostsResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: fairyGymBoostsWorkflowSchema
});

export type FairyGymBoostResultKind = z.infer<typeof fairyGymBoostResultKindSchema>;
export type FairyGymBoostSelection = z.infer<typeof fairyGymBoostSelectionSchema>;
export type FairyGymBoostStat = z.infer<typeof fairyGymBoostStatSchema>;
export type FairyGymBoostRecord = z.infer<typeof fairyGymBoostRecordSchema>;
export type FairyGymBoostTrainer = z.infer<typeof fairyGymBoostTrainerSchema>;
export type FairyGymBoostsWorkflow = z.infer<typeof fairyGymBoostsWorkflowBaseSchema>;
export type LoadFairyGymBoostsWorkflowRequest = z.infer<
  typeof loadFairyGymBoostsWorkflowRequestSchema
>;
export type LoadFairyGymBoostsWorkflowResponse = z.infer<
  typeof loadFairyGymBoostsWorkflowResponseSchema
>;
export type StageFairyGymBoostsRequest = z.infer<typeof stageFairyGymBoostsRequestSchema>;
export type StageFairyGymBoostsResponse = z.infer<typeof stageFairyGymBoostsResponseSchema>;

const canonicalSources = [
  ['bk143', 'Annette quiz sequence', 'romfs/bin/battle/waza/sequence/bk143.bseq'],
  ['bk144', 'Teresa quiz sequence', 'romfs/bin/battle/waza/sequence/bk144.bseq'],
  ['bk145', 'Theodora quiz sequence', 'romfs/bin/battle/waza/sequence/bk145.bseq'],
  ['bk171', 'Opal nickname quiz sequence', 'romfs/bin/battle/waza/sequence/bk171.bseq'],
  ['bk173', 'Opal color quiz sequence', 'romfs/bin/battle/waza/sequence/bk173.bseq'],
  ['bk174', 'Opal age quiz sequence', 'romfs/bin/battle/waza/sequence/bk174.bseq']
] as const;

const canonicalTrainerDefinitions = [
  [113, 'Annette', 0, ['annette-weakness-poison', 'annette-weakness-steel']],
  [114, 'Teresa', 1, ['teresa-previous-trainer-annetta', 'teresa-previous-trainer-annette']],
  [115, 'Theodora', 2, ['theodora-breakfast-curry', 'theodora-breakfast-omelets']],
  [108, 'Opal', 3, [
    'opal-nickname-magic-user',
    'opal-nickname-wizard',
    'opal-color-pink',
    'opal-color-purple',
    'opal-age-sixteen',
    'opal-age-eighty-eight'
  ]]
] as const;

const canonicalBoostDefinitions = [
  ['annette-weakness-poison', canonicalSources[0][2], 1, 'Poison type', "Do you know about Fairy type's weaknesses?", 1, 'increase'],
  ['annette-weakness-steel', canonicalSources[0][2], 2, 'Steel type', "Do you know about Fairy type's weaknesses?", 1, 'increase'],
  ['teresa-previous-trainer-annetta', canonicalSources[1][2], 1, 'Annetta', "What was the previous Trainer's name?", 5, 'decrease'],
  ['teresa-previous-trainer-annette', canonicalSources[1][2], 2, 'Annette', "What was the previous Trainer's name?", 5, 'increase'],
  ['theodora-breakfast-curry', canonicalSources[2][2], 1, 'Curry', 'What do I eat for breakfast every morning?', 3, 'decrease'],
  ['theodora-breakfast-omelets', canonicalSources[2][2], 2, 'Omelets', 'What do I eat for breakfast every morning?', 3, 'increase'],
  ['opal-nickname-magic-user', canonicalSources[3][2], 1, 'The magic-user', 'Do you know my nickname?', 6, 'decrease'],
  ['opal-nickname-wizard', canonicalSources[3][2], 2, 'The wizard', 'Do you know my nickname?', 6, 'increase'],
  ['opal-color-pink', canonicalSources[4][2], 1, 'Pink', 'What is my favorite color?', 4, 'decrease'],
  ['opal-color-purple', canonicalSources[4][2], 2, 'Purple', 'What is my favorite color?', 4, 'increase'],
  ['opal-age-sixteen', canonicalSources[5][2], 1, '16 years old', 'How old am I?', 2, 'increase'],
  ['opal-age-eighty-eight', canonicalSources[5][2], 2, '88 years old', 'How old am I?', 2, 'decrease']
].map(([
  boostId,
  sequenceFile,
  answerChoice,
  answerText,
  questionText,
  defaultEffectId,
  defaultResultKind
]) => ({
  answerChoice: answerChoice as number,
  answerText: answerText as string,
  boostId: boostId as string,
  defaultEffectId: defaultEffectId as number,
  defaultResultKind: defaultResultKind as FairyGymBoostResultKind,
  questionText: questionText as string,
  sequenceFile: sequenceFile as string
}));

const effectDefinitions: Record<
  number,
  { affectedStats: FairyGymBoostStat[]; effectLabel: string; stageAmount: number }
> = {
  0: { affectedStats: [], effectLabel: 'No effect', stageAmount: 0 },
  1: { affectedStats: ['atk', 'spAtk'], effectLabel: 'Attack and Sp. Atk', stageAmount: 1 },
  2: { affectedStats: ['atk', 'spAtk'], effectLabel: 'Attack and Sp. Atk', stageAmount: 2 },
  3: { affectedStats: ['def', 'spDef'], effectLabel: 'Defense and Sp. Def', stageAmount: 1 },
  4: { affectedStats: ['def', 'spDef'], effectLabel: 'Defense and Sp. Def', stageAmount: 2 },
  5: { affectedStats: ['speed'], effectLabel: 'Speed', stageAmount: 1 },
  6: { affectedStats: ['speed'], effectLabel: 'Speed', stageAmount: 2 }
};

function validateFairyGymBoostsWorkflow(
  workflow: FairyGymBoostsWorkflow,
  context: z.RefinementCtx
) {
  if (workflow.summary.id !== 'fairyGymBoosts' || workflow.summary.label !== 'Fairy Gym Boosts') {
    addIssue(context, ['summary'], 'Fairy Gym Boosts workflow identity is not canonical.');
  }

  if (workflow.summary.availability !== 'disabled' && workflow.detectedGame === null) {
    addIssue(context, ['detectedGame'], 'An enabled Fairy Gym Boosts workflow needs a detected game.');
  }

  if (workflow.summary.availability !== 'disabled' && workflow.sources.length !== canonicalSources.length) {
    addIssue(context, ['sources'], 'Fairy Gym Boosts must report all six sequence sources.');
  }

  workflow.sources.forEach((source, index) => {
    const expected = canonicalSources[index];
    if (
      !expected ||
      source.sourceId !== expected[0] ||
      source.label !== expected[1] ||
      source.relativePath !== expected[2] ||
      source.provenance.sourceFile !== expected[2]
    ) {
      addIssue(context, ['sources', index], 'Fairy Gym Boosts source mapping is not canonical.');
    }

    const isMapped = source.status === 'available';
    if (
      source.payloadOffsetHex !== (isMapped ? '0x00001550' : 'unknown') ||
      source.ownedRangeHex !== (isMapped ? '0x00001550-0x0000155F' : 'unknown')
    ) {
      addIssue(context, ['sources', index], 'Fairy Gym Boosts source offsets do not match its status.');
    }

    if (!hasValidSourceProvenance(source)) {
      addIssue(
        context,
        ['sources', index, 'provenance'],
        'Fairy Gym Boosts source provenance does not match its status.'
      );
    }
  });

  if (workflow.trainers.length !== canonicalTrainerDefinitions.length) {
    addIssue(context, ['trainers'], 'Fairy Gym Boosts must report all four trainers.');
  }

  const sourceStatusByPath = new Map(
    workflow.sources.map((source) => [source.relativePath, source.status])
  );
  workflow.trainers.forEach((trainer, trainerIndex) => {
    const expectedTrainer = canonicalTrainerDefinitions[trainerIndex];
    if (
      !expectedTrainer ||
      trainer.trainerId !== expectedTrainer[0] ||
      trainer.npcName !== expectedTrainer[1] ||
      trainer.displayOrder !== expectedTrainer[2] ||
      trainer.boosts.length !== expectedTrainer[3].length
    ) {
      addIssue(context, ['trainers', trainerIndex], 'Fairy Gym Boosts trainer mapping is not canonical.');
    }

    trainer.boosts.forEach((boost, boostIndex) => {
      const expectedBoostId = expectedTrainer?.[3][boostIndex];
      const expectedBoost = canonicalBoostDefinitions.find(
        (candidate) => candidate.boostId === expectedBoostId
      );
      if (
        !expectedBoost ||
        boost.boostId !== expectedBoost.boostId ||
        boost.sequenceFile !== expectedBoost.sequenceFile ||
        boost.answerChoice !== expectedBoost.answerChoice ||
        boost.answerText !== expectedBoost.answerText ||
        boost.questionText !== expectedBoost.questionText ||
        boost.defaultEffectId !== expectedBoost.defaultEffectId ||
        boost.defaultResultKind !== expectedBoost.defaultResultKind
      ) {
        addIssue(
          context,
          ['trainers', trainerIndex, 'boosts', boostIndex],
          'Fairy Gym Boosts answer mapping is not canonical.'
        );
      }

      const effect = effectDefinitions[boost.effectId];
      if (
        !effect ||
        !isSupportedOutcome(boost.effectId, boost.resultKind) ||
        boost.effectLabel !== effect.effectLabel ||
        boost.stageAmount !== effect.stageAmount ||
        boost.affectedStats.length !== effect.affectedStats.length ||
        boost.affectedStats.some((stat, index) => stat !== effect.affectedStats[index])
      ) {
        addIssue(
          context,
          ['trainers', trainerIndex, 'boosts', boostIndex],
          'Fairy Gym Boosts effect details do not match the effect id.'
        );
      }

      if (boost.isAvailable !== (sourceStatusByPath.get(boost.sequenceFile) === 'available')) {
        addIssue(
          context,
          ['trainers', trainerIndex, 'boosts', boostIndex, 'isAvailable'],
          'Fairy Gym Boosts answer availability does not match its source.'
        );
      }
    });
  });

  const boosts = workflow.trainers.flatMap((trainer) => trainer.boosts);
  if (
    workflow.stats.trainerCount !== workflow.trainers.length ||
    workflow.stats.boostCount !== boosts.length ||
    workflow.stats.sourceFileCount !==
      workflow.sources.filter((source) => source.status === 'available').length
  ) {
    addIssue(context, ['stats'], 'Fairy Gym Boosts workflow statistics do not match its records.');
  }
}

function isSupportedOutcome(effectId: number, resultKind: FairyGymBoostResultKind) {
  return effectId === 0
    ? resultKind === 'none'
    : effectId >= 1 &&
        effectId <= 6 &&
        (resultKind === 'increase' || resultKind === 'decrease');
}

function hasValidSourceProvenance(
  source: z.infer<typeof fairyGymBoostsSourceRecordSchema>
) {
  const { fileState, sourceLayer } = source.provenance;
  if (source.status === 'available') {
    return (
      (sourceLayer === 'base' && fileState === 'baseOnly') ||
      (sourceLayer === 'layered' && fileState === 'layeredOverride')
    );
  }

  if (source.status === 'missing') {
    return sourceLayer === 'generated' && fileState === 'baseOnly';
  }

  return (
    (sourceLayer === 'base' && fileState === 'baseOnly') ||
    (sourceLayer === 'layered' &&
      (fileState === 'layeredOverride' || fileState === 'layeredOnly'))
  );
}

function addIssue(context: z.RefinementCtx, path: Array<string | number>, message: string) {
  context.addIssue({ code: 'custom', message, path });
}
