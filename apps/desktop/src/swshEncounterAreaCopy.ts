/* SPDX-License-Identifier: GPL-3.0-only */

import type { EncounterSlotRecord, EncounterTableRecord } from './bridge/contracts';

const speciesFieldName = 'speciesId';
const formFieldName = 'form';
const probabilityFieldName = 'probability';
const levelMinFieldName = 'levelMin';
const levelMaxFieldName = 'levelMax';

export type EncounterDraftRecords = Readonly<
  Record<string, Readonly<Record<string, string>>>
>;

export type SwShEncounterSlotFieldUpdate = {
  changes: Array<{ field: string; value: string }>;
  slot: number;
  tableId: string;
};

export type SwShEncounterAreaCopyTargetDraftCollision = {
  key: string;
  kind: 'slot' | 'levels';
  slot: number | null;
  tableId: string;
};

export function createProjectedSwShEncounterTableCopyUpdates(
  sourceTable: EncounterTableRecord,
  targetTable: EncounterTableRecord,
  slotDraftsByKey: EncounterDraftRecords,
  levelDraftsByTableId: EncounterDraftRecords
): SwShEncounterSlotFieldUpdate[] {
  const targetSlotNumbers = new Set(targetTable.slots.map((slot) => slot.slot));
  const matchingSourceSlots = sourceTable.slots.filter((slot) =>
    targetSlotNumbers.has(slot.slot)
  );
  const sourceLevelSlot = matchingSourceSlots[0];
  const targetLevelSlot = targetTable.slots[0];
  const sourceLevelDrafts = levelDraftsByTableId[sourceTable.tableId];
  const copiedLevelChanges =
    sourceLevelSlot && targetLevelSlot
      ? getOrderedProjectedLevelChanges(
          targetLevelSlot,
          sourceLevelDrafts?.[levelMinFieldName] ?? sourceLevelSlot.levelMin.toString(),
          sourceLevelDrafts?.[levelMaxFieldName] ?? sourceLevelSlot.levelMax.toString()
        )
      : [];

  return matchingSourceSlots.map((slot, index) => {
    const sourceSlotDrafts = slotDraftsByKey[`${sourceTable.tableId}:${slot.slot}`];
    return {
      changes: [
        {
          field: speciesFieldName,
          value: sourceSlotDrafts?.[speciesFieldName] ?? slot.speciesId.toString()
        },
        {
          field: formFieldName,
          value: sourceSlotDrafts?.[formFieldName] ?? slot.form.toString()
        },
        {
          field: probabilityFieldName,
          value: sourceSlotDrafts?.[probabilityFieldName] ?? slot.weight.toString()
        },
        ...(index === 0 ? copiedLevelChanges : [])
      ],
      slot: slot.slot,
      tableId: targetTable.tableId
    };
  });
}

export function findSwShEncounterAreaCopyTargetDraftCollisions(
  updates: readonly SwShEncounterSlotFieldUpdate[],
  slotDraftsByKey: EncounterDraftRecords,
  levelDraftsByTableId: EncounterDraftRecords
): SwShEncounterAreaCopyTargetDraftCollision[] {
  const collisions: SwShEncounterAreaCopyTargetDraftCollision[] = [];
  const seenKeys = new Set<string>();

  for (const update of updates) {
    const slotKey = `${update.tableId}:${update.slot}`;
    if (
      Object.prototype.hasOwnProperty.call(slotDraftsByKey, slotKey) &&
      !seenKeys.has(`slot:${slotKey}`)
    ) {
      seenKeys.add(`slot:${slotKey}`);
      collisions.push({
        key: slotKey,
        kind: 'slot',
        slot: update.slot,
        tableId: update.tableId
      });
    }

    const copiesLevels = update.changes.some(
      (change) => change.field === levelMinFieldName || change.field === levelMaxFieldName
    );
    if (
      copiesLevels &&
      Object.prototype.hasOwnProperty.call(levelDraftsByTableId, update.tableId) &&
      !seenKeys.has(`levels:${update.tableId}`)
    ) {
      seenKeys.add(`levels:${update.tableId}`);
      collisions.push({
        key: update.tableId,
        kind: 'levels',
        slot: null,
        tableId: update.tableId
      });
    }
  }

  return collisions;
}

function getOrderedProjectedLevelChanges(
  targetSlot: EncounterSlotRecord,
  sourceMinimumValue: string,
  sourceMaximumValue: string
) {
  const nextMinimumLevel = Number.parseInt(sourceMinimumValue, 10);
  const nextMaximumLevel = Number.parseInt(sourceMaximumValue, 10);
  const updateMaximumFirst =
    Number.isInteger(nextMinimumLevel) && nextMinimumLevel > targetSlot.levelMax;
  const updateMinimumFirst =
    Number.isInteger(nextMaximumLevel) && nextMaximumLevel < targetSlot.levelMin;
  const preferredOrder = updateMaximumFirst
    ? [levelMaxFieldName, levelMinFieldName]
    : updateMinimumFirst
      ? [levelMinFieldName, levelMaxFieldName]
      : [levelMinFieldName, levelMaxFieldName];
  const completeChanges = [
    { field: levelMinFieldName, value: sourceMinimumValue },
    { field: levelMaxFieldName, value: sourceMaximumValue }
  ];

  return completeChanges.sort(
    (left, right) => preferredOrder.indexOf(left.field) - preferredOrder.indexOf(right.field)
  );
}
