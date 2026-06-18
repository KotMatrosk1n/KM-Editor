/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from './App';
import { type EncountersWorkflow, type EncounterTableRecord } from './bridge/contracts';
import { createMockProjectBridge } from './testSupport/appTestFixtures';

function makeSlot(
  slot: number,
  speciesId: number,
  species: string,
  weight: number,
  levelMin = 10,
  levelMax = 20
) {
  return {
    form: 0,
    levelMax,
    levelMin,
    slot,
    species,
    speciesId,
    timeOfDay: null,
    weather:
      'Biome weights: Grass 60 / Height: 0-0 / Band group: No linked group spawn / Voice class: OTHERS',
    weight
  };
}

function makeTable(
  tableId: string,
  area: string,
  encounterType: string,
  gameVersion: string,
  slots: ReturnType<typeof makeSlot>[],
  location = 'South Province'
): EncounterTableRecord {
  return {
    archiveMember: 'pokedata_array.bin',
    area,
    encounterType,
    gameVersion,
    location,
    provenance: {
      fileState: 'baseOnly',
      sourceFile: 'romfs/world/data/encount/pokedata/pokedata/pokedata_array.bin',
      sourceLayer: 'base'
    },
    slots,
    tableId
  };
}

function createSvEncountersWorkflow(): EncountersWorkflow {
  return {
    diagnostics: [],
    editableFields: [],
    stats: {
      sourceFileCount: 1,
      totalSlotCount: 4,
      totalTableCount: 3
    },
    summary: {
      availability: 'available',
      description: 'Edit Scarlet/Violet wild encounter rows.',
      diagnostics: [],
      id: 'encounters',
      label: 'Wild Encounters'
    },
    tables: [
      makeTable(
        '4|4|Scarlet/Violet|Land|Morning|Grass|no-flag',
        'South Province (Area Two)',
        'Land',
        'Scarlet/Violet',
        [makeSlot(0, 25, 'Pikachu', 75, 10, 30), makeSlot(1, 26, 'Raichu', 25, 40, 50)]
      ),
      makeTable(
        '4|4|Scarlet/Violet|Land|Night|Grass|no-flag',
        'South Province (Area Two)',
        'Land',
        'Scarlet/Violet',
        [makeSlot(0, 39, 'Jigglypuff', 60, 12, 25)]
      ),
      makeTable(
        '11|11|Scarlet/Violet|Water|Morning|Lake|no-flag',
        'West Province (Area Two)',
        'Water',
        'Scarlet/Violet',
        [makeSlot(0, 129, 'Magikarp', 40, 5, 15)],
        'West Province'
      )
    ]
  };
}

describe('Scarlet/Violet wild encounters UI', () => {
  it('shows scoped condition rows with lot shares', async () => {
    const user = userEvent.setup();
    const workflow = createSvEncountersWorkflow();

    render(
      <App
        bridge={createMockProjectBridge(
          {
            loadEncountersWorkflow: () => Promise.resolve({ workflow })
          },
          true
        )}
      />
    );

    await user.click(screen.getByRole('button', { name: 'Pokemon Scarlet' }));
    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    await user.click(screen.getByRole('button', { name: 'Wild Encounters' }));

    const encounterTable = await screen.findByRole('table', { name: 'Encounter tables' });
    expect(within(encounterTable).getAllByRole('row')).toHaveLength(3);
    expect(within(encounterTable).getByText('South Province')).toBeInTheDocument();
    expect(within(encounterTable).getByText('West Province')).toBeInTheDocument();

    const conditionTable = screen.getByRole('table', { name: 'S/V condition rows' });
    expect(within(conditionTable).getAllByRole('row')).toHaveLength(3);
    expect(screen.getByText('2 condition rows in this location')).toBeInTheDocument();
    expect(screen.getByText('2 slots in the selected row')).toBeInTheDocument();
    expect(screen.getByText('Lot weights are relative inside the selected row')).toBeInTheDocument();
    expect(screen.getByText('40-50 / lot 25 (25% share)')).toBeInTheDocument();

    await user.click(within(conditionTable).getByRole('row', { name: /Night/ }));

    expect(screen.getByText('1 slot in the selected row')).toBeInTheDocument();
    expect(screen.getByText('12-25 / lot 60 (100% share)')).toBeInTheDocument();
  });
});
