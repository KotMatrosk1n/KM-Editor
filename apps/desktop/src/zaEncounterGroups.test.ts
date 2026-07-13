/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import type { EncounterSlotRecord, EncounterTableRecord } from './bridge/contracts';
import { buildZaEncounterGroups, getZaEncounterGroupKey } from './zaEncounterGroups';

type TestEncounterSlot = EncounterSlotRecord;

function makeSlot(
  slot: number,
  overrides: Partial<TestEncounterSlot> = {}
): TestEncounterSlot {
  return {
    encounterDataId: `encounter-${slot}`,
    encounterRecordId: `record-${slot}`,
    form: 0,
    levelMax: 10,
    levelMin: 5,
    slot,
    species: 'Fletchling',
    speciesId: 661,
    timeOfDay: null,
    weather: 'Any',
    weight: 100,
    ...overrides
  };
}

function makeTable(tableId: string, slots: TestEncounterSlot[]): EncounterTableRecord {
  return {
    archiveMember: 'pokemon_spawner_data_array.bin',
    area: 'Wild Zone 1',
    encounterType: 'Wild',
    gameVersion: 'Pokemon Legends Z-A',
    location: 'Wild Zone 1',
    provenance: {
      fileState: 'baseOnly',
      sourceFile: 'romfs/world/data/encount/pokemon_spawner_data_array.bin',
      sourceLayer: 'base'
    },
    slots,
    tableId
  };
}

describe('zaEncounterGroups', () => {
  it('consolidates three spawners that share one encounter record', () => {
    const tables = ['spawner-a', 'spawner-b', 'spawner-c'].map((tableId, index) =>
      makeTable(
        tableId,
        [
          makeSlot(index, {
            encounterDataId: 'fletchling',
            encounterRecordId: 'encounter-row-42'
          })
        ]
      )
    );

    const groups = buildZaEncounterGroups(tables, tables);

    expect(groups).toHaveLength(1);
    expect(groups[0]).toMatchObject({
      key: 'record:encounter-row-42',
      outsideScopeSpawnerCount: 0,
      slotCount: 3,
      spawnerCount: 3
    });
    expect(groups[0]!.placements.map(({ table, slot }) => [table.tableId, slot.slot])).toEqual([
      ['spawner-a', 0],
      ['spawner-b', 1],
      ['spawner-c', 2]
    ]);
    expect(groups[0]!.slot).toBe(tables[0]!.slots[0]);
  });

  it('counts multiple shared slots in one spawner without duplicating the spawner count', () => {
    const table = makeTable('spawner-a', [
      makeSlot(4, { encounterDataId: 'shared', encounterRecordId: 'shared-row' }),
      makeSlot(7, { encounterDataId: 'shared', encounterRecordId: 'shared-row' })
    ]);

    const groups = buildZaEncounterGroups([table], [table]);

    expect(groups[0]).toMatchObject({ slotCount: 2, spawnerCount: 1 });
    expect(groups[0]!.placements.map(({ slot }) => slot.slot)).toEqual([4, 7]);
  });

  it('uses shared record identity across normal and Alpha encounter data IDs', () => {
    const normal = makeTable('normal-spawner', [
      makeSlot(0, {
        encounterDataId: 'fletchling',
        encounterRecordId: 'encounter-row-42',
        isAlpha: false
      })
    ]);
    const alpha = makeTable('alpha-spawner', [
      makeSlot(0, {
        encounterDataId: 'fletchling_alpha',
        encounterRecordId: 'encounter-row-42',
        isAlpha: true
      })
    ]);

    const groups = buildZaEncounterGroups([normal, alpha], [normal, alpha]);

    expect(groups).toHaveLength(1);
    expect(groups[0]).toMatchObject({ slotCount: 2, spawnerCount: 2 });
  });

  it('keeps the same species separate when its encounter records are distinct', () => {
    const table = makeTable('spawner-a', [
      makeSlot(0, { encounterDataId: 'first', encounterRecordId: 'record-a' }),
      makeSlot(1, { encounterDataId: 'second', encounterRecordId: 'record-b' })
    ]);

    const groups = buildZaEncounterGroups([table], [table]);

    expect(groups.map((group) => group.key)).toEqual(['record:record-a', 'record:record-b']);
  });

  it('uses encounter data identity before unique table and slot fallbacks', () => {
    const first = makeTable('spawner-a', [
      makeSlot(0, { encounterDataId: 'shared-data', encounterRecordId: null }),
      makeSlot(1, { encounterDataId: null, encounterRecordId: null })
    ]);
    const second = makeTable('spawner-b', [
      makeSlot(0, { encounterDataId: 'shared-data', encounterRecordId: null }),
      makeSlot(1, { encounterDataId: null, encounterRecordId: null })
    ]);

    const groups = buildZaEncounterGroups([first, second], [first, second]);

    expect(groups.map((group) => group.key)).toEqual([
      'data:shared-data',
      'slot:spawner-a:1',
      'slot:spawner-b:1'
    ]);
    expect(groups[0]).toMatchObject({ slotCount: 2, spawnerCount: 2 });
    expect(getZaEncounterGroupKey('spawner-a', first.slots[1]!)).toBe('slot:spawner-a:1');
  });

  it('reports distinct spawners outside the accepted scope without adding their placements', () => {
    const selected = makeTable('selected-spawner', [
      makeSlot(0, { encounterDataId: 'fletchling', encounterRecordId: 'shared-row' })
    ]);
    const outsideWithTwoSlots = makeTable('outside-a', [
      makeSlot(0, { encounterDataId: 'fletchling', encounterRecordId: 'shared-row' }),
      makeSlot(1, { encounterDataId: 'fletchling', encounterRecordId: 'shared-row' })
    ]);
    const outsideWithOneSlot = makeTable('outside-b', [
      makeSlot(0, { encounterDataId: 'fletchling', encounterRecordId: 'shared-row' })
    ]);
    const unrelatedSameSpecies = makeTable('outside-c', [
      makeSlot(0, { encounterDataId: 'other', encounterRecordId: 'other-row' })
    ]);

    const groups = buildZaEncounterGroups(
      [selected],
      [selected, outsideWithTwoSlots, outsideWithOneSlot, unrelatedSameSpecies]
    );

    expect(groups[0]).toMatchObject({
      outsideScopeSpawnerCount: 2,
      slotCount: 1,
      spawnerCount: 1
    });
    expect(groups[0]!.placements).toEqual([{ slot: selected.slots[0], table: selected }]);
  });
});
