// SPDX-License-Identifier: GPL-3.0-only

import type { EncounterSlotRecord, EncounterTableRecord } from './bridge/contracts';

export type ZaEncounterPlacement = {
  slot: EncounterSlotRecord;
  table: EncounterTableRecord;
};

export type ZaEncounterGroup = {
  key: string;
  outsideScopeSpawnerCount: number;
  placements: ZaEncounterPlacement[];
  slot: EncounterSlotRecord;
  slotCount: number;
  spawnerCount: number;
};

export type ZaWildZoneCompletionState =
  | 'contributing'
  | 'excluded'
  | 'mixed'
  | 'notApplicable';

export function getZaWildZoneCompletionState(
  slots: Array<Pick<EncounterSlotRecord, 'contributesToWildZoneCompletion'>>
): ZaWildZoneCompletionState {
  const applicableSlots = slots
    .map((slot) => slot.contributesToWildZoneCompletion)
    .filter((contributes): contributes is boolean => contributes !== null && contributes !== undefined);
  if (applicableSlots.length === 0) {
    return 'notApplicable';
  }

  const hasContributingSlot = applicableSlots.includes(true);
  const hasExcludedSlot = applicableSlots.includes(false);
  if (hasContributingSlot && hasExcludedSlot) {
    return 'mixed';
  }

  return hasContributingSlot ? 'contributing' : 'excluded';
}

export function getZaEncounterGroupKey(tableId: string, slot: EncounterSlotRecord) {
  const encounterRecordId = normalizeIdentity(slot.encounterRecordId);
  if (encounterRecordId) {
    return `record:${encounterRecordId}`;
  }

  const encounterDataId = normalizeIdentity(slot.encounterDataId);
  if (encounterDataId) {
    return `data:${encounterDataId}`;
  }

  return `slot:${tableId}:${slot.slot}`;
}

export function buildZaEncounterGroups(
  scopeTables: EncounterTableRecord[],
  allTables: EncounterTableRecord[]
): ZaEncounterGroup[] {
  const mutableGroups = new Map<
    string,
    {
      placements: ZaEncounterPlacement[];
      spawnerIds: Set<string>;
    }
  >();

  for (const table of scopeTables) {
    for (const slot of table.slots) {
      const key = getZaEncounterGroupKey(table.tableId, slot);
      const group = mutableGroups.get(key) ?? {
        placements: [],
        spawnerIds: new Set<string>()
      };
      group.placements.push({ slot, table });
      group.spawnerIds.add(table.tableId);
      mutableGroups.set(key, group);
    }
  }

  const scopeSpawnerIds = new Set(scopeTables.map((table) => table.tableId));
  const outsideSpawnerIdsByKey = new Map<string, Set<string>>();

  for (const table of allTables) {
    if (scopeSpawnerIds.has(table.tableId)) {
      continue;
    }

    for (const slot of table.slots) {
      const key = getZaEncounterGroupKey(table.tableId, slot);
      if (!mutableGroups.has(key)) {
        continue;
      }

      const spawnerIds = outsideSpawnerIdsByKey.get(key) ?? new Set<string>();
      spawnerIds.add(table.tableId);
      outsideSpawnerIdsByKey.set(key, spawnerIds);
    }
  }

  return Array.from(mutableGroups.entries()).map(([key, group]) => ({
    key,
    outsideScopeSpawnerCount: outsideSpawnerIdsByKey.get(key)?.size ?? 0,
    placements: group.placements,
    slot: group.placements[0]!.slot,
    slotCount: group.placements.length,
    spawnerCount: group.spawnerIds.size
  }));
}

function normalizeIdentity(identity: string | null | undefined) {
  const normalized = identity?.trim();
  return normalized && normalized.length > 0 ? normalized : null;
}
