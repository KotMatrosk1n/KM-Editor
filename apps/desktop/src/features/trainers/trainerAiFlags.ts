/* SPDX-License-Identifier: GPL-3.0-only */

type TrainerFamily = 'swsh' | 'sv' | 'za';

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
