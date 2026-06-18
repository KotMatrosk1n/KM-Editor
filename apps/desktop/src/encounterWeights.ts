// SPDX-License-Identifier: GPL-3.0-only

import type { EncounterSlotRecord } from './bridge/contracts';

export function formatEncounterSlotWeightSummary(
  slot: EncounterSlotRecord,
  totalWeight: number,
  isSvEncounterTable: boolean
) {
  if (!isSvEncounterTable) {
    return `${slot.levelMin}-${slot.levelMax} / ${slot.weight}%`;
  }

  return `${slot.levelMin}-${slot.levelMax} / lot ${slot.weight}`;
}

export function formatEncounterLotWeight(weight: number, totalWeight: number) {
  if (totalWeight <= 0) {
    return weight.toString();
  }

  return `${weight} (${formatEncounterShare(weight, totalWeight)} share)`;
}

function formatEncounterShare(weight: number, totalWeight: number) {
  return `${((weight / totalWeight) * 100).toLocaleString(undefined, {
    maximumFractionDigits: 1
  })}%`;
}
