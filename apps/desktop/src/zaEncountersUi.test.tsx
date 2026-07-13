/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from './App';
import {
  type EncountersWorkflow,
  type EncounterSlotRecord,
  type EncounterTableRecord
} from './bridge/contracts';
import { createMockProjectBridge } from './testSupport/appTestFixtures';
import { useWorkbenchStore } from './workbenchStore';

function makeSlot(
  slot: number,
  speciesId: number,
  species: string,
  encounterRecordId: string,
  weight: number,
  isAlpha = false
): EncounterSlotRecord {
  return {
    encounterDataId: encounterRecordId,
    encounterKind: 'Wild',
    encounterRecordId,
    form: 0,
    isAlpha,
    levelMax: 10,
    levelMin: 5,
    slot,
    species,
    speciesId,
    timeOfDay: null,
    weather: 'Any weather',
    weight
  };
}

function makeTable(
  tableId: string,
  tableLabel: string,
  locationKey: string,
  slots: EncounterSlotRecord[]
): EncounterTableRecord {
  return {
    archiveMember: 'pokemon_spawner_data_array.bin',
    area: 'Pokemon Spawner',
    encounterType: 'Wild Pokemon',
    gameVersion: 'Pokemon Legends ZA',
    location: locationKey === 'a0102_w01' ? 'Wild Zone 1' : 'Wild Zone 2',
    locationKey,
    provenance: {
      fileState: 'baseOnly',
      sourceFile: 'romfs/world/field/pokemon_spawner_data_array.bin',
      sourceLayer: 'base'
    },
    slots,
    tableDetails: slots.map((slot) => slot.species).join(', '),
    tableId,
    tableLabel
  };
}

function createZaEncountersWorkflow(): EncountersWorkflow {
  const fletchlingRecordId = 'encount-data:42';
  const tables = [
    makeTable('zone-1-spawner-1', 'Spawner 1', 'a0102_w01', [
      makeSlot(0, 661, 'Fletchling', fletchlingRecordId, 40),
      makeSlot(1, 659, 'Bunnelby', 'encount-data:43', 60)
    ]),
    makeTable('zone-1-spawner-10', 'Spawner 10', 'a0102_w01', [
      makeSlot(0, 661, 'Fletchling', fletchlingRecordId, 50)
    ]),
    makeTable('zone-1-spawner-2', 'Spawner 2', 'a0102_w01', [
      makeSlot(0, 661, 'Fletchling', fletchlingRecordId, 100, true)
    ]),
    makeTable('zone-2-spawner-1', 'Spawner 1', 'a0201_w01', [
      makeSlot(0, 661, 'Fletchling', fletchlingRecordId, 100)
    ])
  ];

  return {
    diagnostics: [],
    editableFields: [
      {
        field: 'levelMin',
        label: 'Min Level',
        maximumValue: 100,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      }
    ],
    stats: {
      sourceFileCount: 2,
      totalSlotCount: 5,
      totalTableCount: tables.length
    },
    summary: {
      availability: 'available',
      description: 'Edit Pokemon Legends Z-A wild encounters.',
      diagnostics: [],
      id: 'encounters',
      label: 'Wild Encounters'
    },
    tables
  };
}

function createZaCategoryWorkflow(): EncountersWorkflow {
  const sharedRecordId = 'encount-data:70';
  const tables = [
    {
      ...makeTable('outzone-group', 'Lumiose Outskirts Spawn Group 1', 'outzone_lumiose', [
        makeSlot(0, 661, 'Fletchling', sharedRecordId, 60),
        makeSlot(1, 664, 'Scatterbug', 'encount-data:71', 40)
      ]),
      location: 'Lumiose Outskirts'
    },
    {
      ...makeTable('outzone-point', 'Lumiose Outskirts Spawn Point 1', 'outzone_lumiose', [
        makeSlot(0, 661, 'Fletchling', sharedRecordId, 50),
        makeSlot(1, 659, 'Bunnelby', 'encount-data:72', 50)
      ]),
      location: 'Lumiose Outskirts'
    }
  ];

  return {
    ...createZaEncountersWorkflow(),
    stats: {
      sourceFileCount: 2,
      totalSlotCount: 4,
      totalTableCount: tables.length
    },
    tables
  };
}

async function openZaWildEncounters(workflow: EncountersWorkflow) {
  const user = userEvent.setup();

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

  await user.click(screen.getByRole('button', { name: 'Pokemon Legends Z-A' }));
  await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
  await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
  await user.type(screen.getByLabelText('Output Root'), 'output');
  await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
  await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
  await user.click(screen.getByRole('button', { name: 'Wild Encounters' }));

  return user;
}

describe('Pokemon Legends Z-A wild encounters UI', () => {
  beforeEach(() => {
    window.localStorage.clear();
    useWorkbenchStore.getState().resetProjectSession();
    useWorkbenchStore.setState({
      applyResult: null,
      changePlan: null,
      draftPaths: {
        baseExeFsPath: '',
        baseRomFsPath: '',
        outputRootPath: '',
        pokemonLegendsZASupportFolderPath: '',
        saveFilePath: '',
        scarletVioletSupportFolderPath: '',
        selectedGame: null
      },
      editSession: null
    });
  });

  it('consolidates shared encounter rows and keeps their spawner placements selectable', async () => {
    const workflow = createZaEncountersWorkflow();
    const user = await openZaWildEncounters(workflow);

    const encounterTable = await screen.findByRole('table', { name: 'Z-A linked encounters' });
    expect(within(encounterTable).getAllByRole('row')).toHaveLength(3);
    expect(within(encounterTable).getAllByRole('row', { name: /^Fletchling,/ })).toHaveLength(1);
    expect(
      within(encounterTable).getByRole('row', {
        name: 'Fletchling, 3 spawners, 1 more elsewhere, levels 5 to 10, source Base'
      })
    ).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByText('2 encounters')).toBeInTheDocument();
    expect(
      screen.getByText(
        'Linked placements share this Pokemon entry. It is also used by 1 spawner outside this view. Saving changes updates every linked placement.'
      )
    ).toBeInTheDocument();

    const placements = screen.getByRole('table', { name: 'Fletchling linked spawners' });
    expect(within(placements).getAllByRole('row')).toHaveLength(4);
    const placementRows = within(placements).getAllByRole('row').slice(1);
    expect(placementRows[0]).toHaveAccessibleName(
      'Spawner 1, slot 1, probability 40, Any time, Any weather'
    );
    expect(placementRows[1]).toHaveAccessibleName(
      'Spawner 2, slot 1, probability 100, Any time, Any weather, Alpha'
    );
    expect(placementRows[2]).toHaveAccessibleName(
      'Spawner 10, slot 1, probability 50, Any time, Any weather'
    );
    const tenthPlacement = within(placements).getByRole('row', {
      name: 'Spawner 10, slot 1, probability 50, Any time, Any weather'
    });
    await user.click(tenthPlacement);
    expect(tenthPlacement).toHaveAttribute('aria-pressed', 'true');
    const spawnerDetail = screen.getByText('Spawner', { selector: 'dt' }).parentElement;
    expect(spawnerDetail).not.toBeNull();
    expect(within(spawnerDetail!).getByText('Spawner 10')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    const minLevel = screen.getByLabelText('Min Level');
    await user.clear(minLevel);
    await user.type(minLevel, '7');
    await user.click(
      within(placements).getByRole('row', {
        name: 'Spawner 1, slot 1, probability 40, Any time, Any weather'
      })
    );
    expect(screen.getByLabelText('Min Level')).toHaveValue(7);
    expect(
      within(placements).getByRole('row', {
        name: 'Spawner 2, slot 1, probability 100, Any time, Any weather, Alpha'
      })
    ).toBeInTheDocument();

    await user.click(
      within(encounterTable).getByRole('row', {
        name: 'Bunnelby, 1 spawner, levels 5 to 10, source Base'
      })
    );
    expect(screen.queryByRole('table', { name: 'Bunnelby linked spawners' })).not.toBeInTheDocument();
  }, 30_000);

  it('keeps spawner category tabs as visible encounter filters', async () => {
    const user = await openZaWildEncounters(createZaCategoryWorkflow());
    const encounterTable = await screen.findByRole('table', { name: 'Z-A linked encounters' });

    expect(screen.getByRole('tab', { name: 'Spawn Groups' })).toHaveAttribute(
      'aria-selected',
      'true'
    );
    expect(within(encounterTable).getByRole('row', { name: /^Fletchling,/ })).toBeInTheDocument();
    expect(within(encounterTable).getByRole('row', { name: /^Scatterbug/ })).toBeInTheDocument();
    expect(within(encounterTable).queryByRole('row', { name: /^Bunnelby,/ })).not.toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: 'Spawn Points' }));

    expect(screen.getByRole('tab', { name: 'Spawn Points' })).toHaveAttribute(
      'aria-selected',
      'true'
    );
    expect(within(encounterTable).getByRole('row', { name: /^Fletchling,/ })).toBeInTheDocument();
    expect(within(encounterTable).getByRole('row', { name: /^Bunnelby,/ })).toBeInTheDocument();
    expect(
      within(encounterTable).queryByRole('row', { name: /^Scatterbug/ })
    ).not.toBeInTheDocument();
    expect(
      within(screen.getByRole('table', { name: 'Fletchling linked spawners' })).getAllByRole('row')
    ).toHaveLength(3);
  }, 30_000);
});
