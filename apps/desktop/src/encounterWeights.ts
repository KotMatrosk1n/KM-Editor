// SPDX-License-Identifier: GPL-3.0-only

import type { EncounterSlotRecord } from './bridge/contracts';

export function formatEncounterSlotWeightSummary(
  slot: EncounterSlotRecord,
  totalWeight: number,
  isSvEncounterTable: boolean,
  locale?: string
) {
  if (!isSvEncounterTable) {
    return `${slot.levelMin}-${slot.levelMax} / ${slot.weight}%`;
  }

  return `${slot.levelMin}-${slot.levelMax} / lot ${slot.weight}${
    totalWeight > 0 ? ` (${formatEncounterShare(slot.weight, totalWeight, locale)} share)` : ''
  }`;
}

export function formatEncounterLotWeight(weight: number, totalWeight: number, locale?: string) {
  if (totalWeight <= 0) {
    return weight.toString();
  }

  return `${weight} (${formatEncounterShare(weight, totalWeight, locale)} share)`;
}

export function formatEncounterLotShare(weight: number, totalWeight: number, locale?: string) {
  if (totalWeight <= 0) {
    return 'Unavailable';
  }

  return `${formatEncounterShare(weight, totalWeight, locale)} share`;
}

export function formatEncounterSharePercent(
  weight: number,
  totalWeight: number,
  locale?: string
) {
  return totalWeight > 0 ? formatEncounterShare(weight, totalWeight, locale) : null;
}

function formatEncounterShare(weight: number, totalWeight: number, locale?: string) {
  return `${((weight / totalWeight) * 100).toLocaleString(locale, {
    maximumFractionDigits: 1
  })}%`;
}
