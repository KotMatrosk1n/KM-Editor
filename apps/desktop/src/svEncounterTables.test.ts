/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import type { EncounterTableRecord } from './bridge/contracts';
import {
  buildSvEncounterConditionRows,
  buildSvEncounterFacetControls,
  buildSvEncounterLocationRows
} from './svEncounterTables';

function makeTable(
  tableId: string,
  area: string,
  species: string,
  speciesId: number
): EncounterTableRecord {
  return {
    archiveMember: 'pokedata_array.bin',
    area,
    encounterType: 'Land',
    gameVersion: 'Scarlet/Violet',
    location: 'South Province',
    provenance: {
      fileState: 'baseOnly',
      sourceFile: 'romfs/world/data/encount/pokedata/pokedata_array.bin',
      sourceLayer: 'base'
    },
    slots: [
      {
        form: 0,
        levelMax: 12,
        levelMin: 8,
        slot: 0,
        species,
        speciesId,
        timeOfDay: 'Morning',
        weather: 'Grass',
        weight: 60
      }
    ],
    tableId
  };
}

describe('svEncounterTables', () => {
  it('groups S/V rows by location and scopes facet choices to that location', () => {
    const tables: EncounterTableRecord[] = [
      makeTable('4|4|Scarlet/Violet|Land|Morning|Grass|no-flag', 'South Province (Area Two)', 'Pikachu', 25),
      {
        ...makeTable('4|4|Scarlet/Violet|Land|Noon|Grass|no-flag', 'South Province (Area Two)', 'Raichu', 26),
        slots: [
          {
            form: 0,
            levelMax: 20,
            levelMin: 15,
            slot: 0,
            species: 'Raichu',
            speciesId: 26,
            timeOfDay: 'Noon',
            weather: 'Grass',
            weight: 40
          }
        ]
      },
      makeTable('4|5|Scarlet/Violet|Land|Morning|Grass|no-flag', 'South Province (Area Four)', 'Eevee', 133),
      {
        ...makeTable('11|11|Scarlet/Violet|Water|Morning|Lake|no-flag', 'West Province (Area Two)', 'Magikarp', 129),
        location: 'West Province'
      }
    ];

    const rows = buildSvEncounterLocationRows(tables, tables[0]!);
    expect(rows).toHaveLength(2);
    expect(rows[0]).toMatchObject({
      areaLabel: 'South Province (Area Two) / South Province (Area Four)',
      gameVersion: 'Scarlet/Violet',
      location: 'South Province'
    });
    expect(rows[0]!.tableIds).toHaveLength(3);

    const controls = buildSvEncounterFacetControls(tables[0]!, tables);
    const area = controls.find((control) => control.key === 'area')!;
    const time = controls.find((control) => control.key === 'time')!;
    const terrain = controls.find((control) => control.key === 'terrain')!;
    expect(area.disabled).toBe(false);
    expect(area.choices.map((choice) => choice.label)).toEqual([
      'South Province (Area Two)',
      'South Province (Area Four)'
    ]);
    expect(time.disabled).toBe(false);
    expect(time.choices.map((choice) => choice.label)).toEqual(['Morning', 'Noon']);
    expect(terrain.disabled).toBe(true);
    expect(area.choices).not.toContainEqual(
      expect.objectContaining({ label: 'West Province (Area Two)' })
    );
  });

  it('builds a scoped S/V condition table with slot counts and total lot weights', () => {
    const tables: EncounterTableRecord[] = [
      {
        ...makeTable('4|4|Scarlet/Violet|Land|Morning|Grass|no-flag', 'South Province (Area Two)', 'Pikachu', 25),
        slots: [
          {
            form: 0,
            levelMax: 12,
            levelMin: 8,
            slot: 0,
            species: 'Pikachu',
            speciesId: 25,
            timeOfDay: 'Morning',
            weather: 'Grass',
            weight: 60
          },
          {
            form: 0,
            levelMax: 14,
            levelMin: 9,
            slot: 1,
            species: 'Raichu',
            speciesId: 26,
            timeOfDay: 'Morning',
            weather: 'Grass',
            weight: 40
          }
        ]
      },
      makeTable('4|4|Scarlet/Violet|Land|Night|Grass|no-flag', 'South Province (Area Two)', 'Eevee', 133),
      {
        ...makeTable('11|11|Scarlet/Violet|Water|Morning|Lake|no-flag', 'West Province (Area Two)', 'Magikarp', 129),
        location: 'West Province'
      }
    ];

    const rows = buildSvEncounterConditionRows(tables[0]!, tables);

    expect(rows).toHaveLength(2);
    expect(rows[0]).toMatchObject({
      area: 'South Province (Area Two)',
      biome: 'Grass',
      flag: 'No flag',
      gameVersion: 'Scarlet/Violet',
      slotCount: 2,
      terrain: 'Land',
      time: 'Morning',
      totalWeight: 100
    });
    expect(rows.map((row) => row.tableId)).not.toContain(
      '11|11|Scarlet/Violet|Water|Morning|Lake|no-flag'
    );
  });
});
