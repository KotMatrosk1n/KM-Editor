/* SPDX-License-Identifier: GPL-3.0-only */

type TrainerFamily = 'swsh' | 'sv' | 'za';

// Display groups only. The stored masks and the S/V doubles alias are owned by the flag controls.
export function getTrainerAiFlagGroup(family: TrainerFamily, bit: number) {
  if (family === 'swsh' && [6, 7, 8, 11].includes(bit)) {
    return 'trainers.layout.aiFireGym';
  }
  if (bit <= 2 || (family !== 'za' && bit === 5)) {
    return 'trainers.layout.aiMoveScoring';
  }
  if (family === 'swsh' ? [3, 4, 9].includes(bit) : family === 'sv' && bit === 3) {
    return 'trainers.layout.aiBattleBehavior';
  }
  return 'trainers.layout.aiTrainerActions';
}

export function buildZaTrainerChangeUpdates(trainers: readonly { trainerId: number; aiFlags: number }[]) {
  return trainers.flatMap(({ trainerId, aiFlags }) => {
    const enabled = aiFlags | (1 << 7);
    return enabled === aiFlags ? [] : [{
      trainerId,
      slot: null,
      field: 'aiFlags',
      value: enabled.toString()
    }];
  });
}

function selectionMask(family: TrainerFamily, mask: number): number {
  // Both stored S/V doubles flags select the same trainer AI option.
  return family === 'sv' && mask === 0x08 ? 0x18 : mask;
}

export function trainerAiFlagEnabled(family: TrainerFamily, flags: number, mask: number): boolean {
  return (flags & selectionMask(family, mask)) !== 0;
}

export function toggleTrainerAiFlag(family: TrainerFamily, flags: number, mask: number, enabled: boolean): number {
  if (enabled) {
    return trainerAiFlagEnabled(family, flags, mask) ? flags : flags | mask;
  }
  return flags & ~selectionMask(family, mask);
}
