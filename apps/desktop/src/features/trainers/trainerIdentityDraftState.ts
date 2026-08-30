// SPDX-License-Identifier: GPL-3.0-only

export type TrainerIdentityDraftValues = Record<string, string>;

export function setTrainerIdentityDraftValue(
  drafts: TrainerIdentityDraftValues,
  trainerKey: string,
  value: string,
  sourceValue: string
) {
  if (value === sourceValue) {
    return removeTrainerIdentityDraftValue(drafts, trainerKey);
  }

  if (drafts[trainerKey] === value) {
    return drafts;
  }

  return { ...drafts, [trainerKey]: value };
}

export function clearStagedTrainerIdentityDraftValue(
  drafts: TrainerIdentityDraftValues,
  trainerKey: string,
  stagedValue: string
) {
  return drafts[trainerKey] === stagedValue
    ? removeTrainerIdentityDraftValue(drafts, trainerKey)
    : drafts;
}

function removeTrainerIdentityDraftValue(
  drafts: TrainerIdentityDraftValues,
  trainerKey: string
) {
  if (!(trainerKey in drafts)) {
    return drafts;
  }

  const nextDrafts = { ...drafts };
  delete nextDrafts[trainerKey];
  return nextDrafts;
}
