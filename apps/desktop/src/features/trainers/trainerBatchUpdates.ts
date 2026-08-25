/* SPDX-License-Identifier: GPL-3.0-only */

import type { TrainersWorkflow, TrainersWorkflowDelta } from '../../bridge/contracts';

export type TrainerFieldUpdateLike = {
  field: string;
};

export function applyTrainerWorkflowDelta(
  workflow: TrainersWorkflow,
  delta: TrainersWorkflowDelta,
  expectedTrainerIds: readonly number[] = []
): TrainersWorkflow {
  const workflowTrainerIds = new Set(workflow.trainers.map((trainer) => trainer.trainerId));
  const changedTrainers = new Map<number, TrainersWorkflow['trainers'][number]>();

  for (const trainer of delta.trainers) {
    if (!workflowTrainerIds.has(trainer.trainerId)) {
      throw new Error(`Trainer workflow delta contains unknown trainer ${trainer.trainerId}.`);
    }

    if (changedTrainers.has(trainer.trainerId)) {
      throw new Error(`Trainer workflow delta contains duplicate trainer ${trainer.trainerId}.`);
    }

    changedTrainers.set(trainer.trainerId, trainer);
  }

  for (const expectedTrainerId of new Set(expectedTrainerIds)) {
    if (!changedTrainers.has(expectedTrainerId)) {
      throw new Error(
        `Trainer workflow delta is missing expected trainer ${expectedTrainerId}.`
      );
    }
  }

  return {
    ...workflow,
    diagnostics: delta.diagnostics,
    stats: delta.stats,
    trainers: workflow.trainers.map(
      (trainer) => changedTrainers.get(trainer.trainerId) ?? trainer
    )
  };
}

const speciesIdFieldName = 'speciesId';
const formFieldName = 'form';

export function orderTrainerFieldUpdates<T extends TrainerFieldUpdateLike>(
  updates: readonly T[]
): T[] {
  return updates
    .map((update, index) => ({ index, priority: getIdentityFieldPriority(update.field), update }))
    .sort((left, right) => left.priority - right.priority || left.index - right.index)
    .map(({ update }) => update);
}

export function isTrainerSlotOccupiedForMaxIvs(
  sourceSpeciesId: number,
  projectedSpeciesId: number | null
) {
  return (projectedSpeciesId ?? sourceSpeciesId) > 0;
}

export function trainerSlotHasNonMaxIvDrafts(
  drafts: Readonly<Record<string, string>>,
  ivFields: readonly string[],
  maximumIv: number
) {
  const maximumIvText = maximumIv.toString();
  return ivFields.some(
    (field) => drafts[field] !== undefined && drafts[field].trim() !== maximumIvText
  );
}

export function trainerSlotNeedsMaxIvDraft(
  sourceSpeciesId: number,
  projectedSpeciesId: number | null,
  drafts: Readonly<Record<string, string>>,
  ivFields: readonly string[],
  maximumIv: number
) {
  return (
    isTrainerSlotOccupiedForMaxIvs(sourceSpeciesId, projectedSpeciesId) &&
    trainerSlotHasNonMaxIvDrafts(drafts, ivFields, maximumIv)
  );
}

export function reconcileTrainerSlotMaxIvDrafts(
  drafts: Readonly<Record<string, string>>,
  defaults: Readonly<Record<string, string>>,
  ivFields: readonly string[],
  maximumIv: number,
  sourceSlotOccupied: boolean
) {
  const nextDrafts = { ...drafts };
  const nextDefaults = { ...defaults };
  const maximumIvText = maximumIv.toString();

  for (const field of ivFields) {
    nextDrafts[field] = maximumIvText;
    if (sourceSlotOccupied) {
      nextDefaults[field] = maximumIvText;
    }
  }

  return { defaults: nextDefaults, drafts: nextDrafts };
}

function getIdentityFieldPriority(field: string) {
  const normalizedField = field.trim();
  if (normalizedField === speciesIdFieldName) {
    return 0;
  }

  if (normalizedField === formFieldName) {
    return 1;
  }

  return 2;
}
