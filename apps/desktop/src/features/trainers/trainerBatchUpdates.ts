/* SPDX-License-Identifier: GPL-3.0-only */

import type {
  EditSession,
  TrainersWorkflow,
  TrainersWorkflowDelta
} from '../../bridge/contracts';

export type TrainerFieldUpdateLike = {
  field: string;
};

export type TrainerPartySlotOccupancy = {
  projectedSpeciesId: number | null;
  slot: number;
  sourceSpeciesId: number;
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
const trainersDomain = 'workflow.trainers';

export function canonicalizeTrainerPartySlotDrafts(
  drafts: Readonly<Record<string, string>>,
  defaults: Readonly<Record<string, string>>,
  sourceSpeciesId: number
): Record<string, string> {
  const speciesDraft = Object.prototype.hasOwnProperty.call(drafts, speciesIdFieldName)
    ? drafts[speciesIdFieldName]
    : defaults[speciesIdFieldName] ?? sourceSpeciesId.toString();
  const normalizedSpeciesDraft = speciesDraft?.trim() ?? '';

  // An invalid identity draft must stay visible to field validation. Treating it as
  // an empty slot here would hide the actual input error and erase adjacent work.
  if (!/^\d+$/u.test(normalizedSpeciesDraft)) {
    return { ...drafts };
  }

  const effectiveSpeciesId = Number.parseInt(normalizedSpeciesDraft, 10);
  if (!Number.isSafeInteger(effectiveSpeciesId) || effectiveSpeciesId > 0) {
    return { ...drafts };
  }

  const canonicalDrafts = { ...drafts };
  for (const field of Object.keys(canonicalDrafts)) {
    if (field === speciesIdFieldName) {
      continue;
    }

    if (Object.prototype.hasOwnProperty.call(defaults, field)) {
      canonicalDrafts[field] = defaults[field];
    } else {
      delete canonicalDrafts[field];
    }
  }

  return canonicalDrafts;
}

export function createTrainerPartySourceSpeciesIndex(
  workflow: TrainersWorkflow
): ReadonlyMap<string, number> {
  return new Map(
    workflow.trainers.flatMap((trainer) =>
      trainer.team.map((pokemon) => [
        createTrainerPartySlotKey(trainer.trainerId, pokemon.slot),
        pokemon.speciesId
      ] as const)
    )
  );
}

export function removeTrainerPendingEditWithDependencies(
  pendingEdits: readonly EditSession['pendingEdits'][number][],
  editIndex: number,
  sourceSpeciesBySlot: ReadonlyMap<string, number>
): EditSession['pendingEdits'] {
  const removedEdit = pendingEdits[editIndex];
  if (!removedEdit) {
    return [...pendingEdits];
  }

  const withoutRemovedEdit = pendingEdits.filter((_, index) => index !== editIndex);
  if (
    removedEdit.domain !== trainersDomain ||
    removedEdit.field !== speciesIdFieldName ||
    !isPositiveIntegerText(removedEdit.newValue)
  ) {
    return withoutRemovedEdit;
  }

  const target = parseTrainerPartySlotKey(removedEdit.recordId);
  if (!target || (sourceSpeciesBySlot.get(removedEdit.recordId!) ?? -1) !== 0) {
    return withoutRemovedEdit;
  }

  return withoutRemovedEdit.filter((edit) => {
    if (edit.domain !== trainersDomain) {
      return true;
    }

    const candidate = parseTrainerPartySlotKey(edit.recordId);
    if (!candidate || candidate.trainerId !== target.trainerId) {
      return true;
    }

    if (candidate.slot === target.slot) {
      return false;
    }

    if (candidate.slot < target.slot) {
      return true;
    }

    // A source-occupied later slot is valid even when the vanilla roster is
    // sparse. Only newly-added, source-empty later groups depend on this added
    // parent slot and must leave with it.
    return (sourceSpeciesBySlot.get(edit.recordId!) ?? -1) !== 0;
  });
}

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

export function isTrainerPartySlotBlockedByEarlierEmptySlot(
  slots: readonly TrainerPartySlotOccupancy[],
  targetSlot: number
) {
  const target = slots.find((slot) => slot.slot === targetSlot);
  if (!target || target.sourceSpeciesId > 0) {
    return false;
  }

  return slots.some(
    (slot) =>
      slot.slot < targetSlot &&
      !isTrainerSlotOccupiedForMaxIvs(slot.sourceSpeciesId, slot.projectedSpeciesId)
  );
}

export function isTrainerPartySlotStageBlockedByEarlierUnstagedSlot(
  slots: readonly TrainerPartySlotOccupancy[],
  targetSlot: number
) {
  const target = slots.find((slot) => slot.slot === targetSlot);
  if (!target || target.sourceSpeciesId > 0) {
    return false;
  }

  return slots.some((slot) => slot.slot < targetSlot && slot.sourceSpeciesId <= 0);
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

function createTrainerPartySlotKey(trainerId: number, slot: number) {
  return `${trainerId}:${slot}`;
}

function parseTrainerPartySlotKey(recordId: string | null | undefined) {
  const match = /^(\d+):(\d+)$/u.exec(recordId ?? '');
  if (!match) {
    return null;
  }

  const trainerId = Number.parseInt(match[1], 10);
  const slot = Number.parseInt(match[2], 10);
  return Number.isSafeInteger(trainerId) && Number.isSafeInteger(slot)
    ? { slot, trainerId }
    : null;
}

function isPositiveIntegerText(value: string | null | undefined) {
  const normalizedValue = value?.trim() ?? '';
  return /^\d+$/u.test(normalizedValue) && Number.parseInt(normalizedValue, 10) > 0;
}
