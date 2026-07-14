/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { App } from './App';
import {
  type EncountersWorkflow,
  type EncounterSlotRecord,
  type EncounterTableRecord
} from './bridge/contracts';
import { type ProjectBridge } from './bridge/projectBridge';
import { createMockProjectBridge } from './testSupport/appTestFixtures';
import { useWorkbenchStore } from './workbenchStore';

function makeSlot(
  slot: number,
  speciesId: number,
  species: string,
  encounterRecordId: string,
  weight: number,
  isAlpha = false,
  contributesToWildZoneCompletion: boolean | null = true,
  alphaChancePercent?: number | null,
  alphaLevelBonus?: number | null
): EncounterSlotRecord {
  return {
    ...(alphaChancePercent === undefined ? {} : { alphaChancePercent }),
    ...(alphaLevelBonus === undefined ? {} : { alphaLevelBonus }),
    contributesToWildZoneCompletion,
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
      makeSlot(0, 661, 'Fletchling', fletchlingRecordId, 40, false, true, 25, 2),
      makeSlot(1, 659, 'Bunnelby', 'encount-data:43', 50, false, false, 0, 4),
      makeSlot(2, 16, 'Pidgey', 'encount-data:44', 10, false, true, null, null),
      makeSlot(3, 25, 'Pikachu', 'encount-data:45', 5, false, true, 100, 3)
    ]),
    makeTable('zone-1-spawner-10', 'Spawner 10', 'a0102_w01', [
      makeSlot(0, 661, 'Fletchling', fletchlingRecordId, 50, false, true, 25, 2)
    ]),
    makeTable('zone-1-spawner-2', 'Spawner 2', 'a0102_w01', [
      makeSlot(0, 661, 'Fletchling', fletchlingRecordId, 100, true, false, 25, 2)
    ]),
    makeTable('zone-2-spawner-1', 'Spawner 1', 'a0201_w01', [
      makeSlot(0, 661, 'Fletchling', fletchlingRecordId, 100, false, true, 25, 2)
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
      },
      {
        field: 'alphaChancePercent',
        label: 'Alpha Chance (%)',
        maximumValue: 100,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'alphaLevelBonus',
        label: 'Alpha Level Bonus',
        maximumValue: 100,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      }
    ],
    stats: {
      sourceFileCount: 2,
      totalSlotCount: 7,
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

function createTwoLinkedZaEncountersWorkflow(): EncountersWorkflow {
  const sharedRecordId = 'encount-data:80';
  const tables = [
    makeTable('zone-1-spawner-1', 'Spawner 1', 'a0102_w01', [
      makeSlot(0, 661, 'Fletchling', sharedRecordId, 60, false, true, 15, 3)
    ]),
    makeTable('zone-1-spawner-2', 'Spawner 2', 'a0102_w01', [
      makeSlot(0, 661, 'Fletchling', sharedRecordId, 40, false, true, 15, 3)
    ])
  ];

  return {
    ...createZaEncountersWorkflow(),
    stats: {
      sourceFileCount: 1,
      totalSlotCount: 2,
      totalTableCount: 2
    },
    tables
  };
}

function createZaCategoryWorkflow(): EncountersWorkflow {
  const sharedRecordId = 'encount-data:70';
  const tables = [
    {
      ...makeTable('outzone-group', 'Lumiose Outskirts Spawn Group 1', 'outzone_lumiose', [
        makeSlot(0, 661, 'Fletchling', sharedRecordId, 60, false, null),
        makeSlot(1, 664, 'Scatterbug', 'encount-data:71', 40, false, null)
      ]),
      location: 'Lumiose Outskirts'
    },
    {
      ...makeTable('outzone-point', 'Lumiose Outskirts Spawn Point 1', 'outzone_lumiose', [
        makeSlot(0, 661, 'Fletchling', sharedRecordId, 50, false, null),
        makeSlot(1, 659, 'Bunnelby', 'encount-data:72', 50, false, null)
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

function createZaMissionWorkflow(): EncountersWorkflow {
  const tables = [
    {
      ...makeTable(
        'restaurant-4-battle-1',
        'Full Course of Battles: High Rolling Battle 1 Spawn Point 001',
        'id_rest4',
        [makeSlot(0, 25, 'Pikachu', 'encount-data:80', 100, false, null)]
      ),
      location: 'Full Course of Battles: High Rolling',
      locationDetails: 'Side Mission 73',
      locationSort: 73
    },
    {
      ...makeTable(
        'defenseless-dodger-1',
        'Be a Defenseless Dodger! Spawn Point 002B',
        'id_spn_subq147',
        [makeSlot(0, 26, 'Raichu', 'encount-data:81', 100, false, null)]
      ),
      location: 'Be a Defenseless Dodger!',
      locationDetails: 'Side Mission 173',
      locationSort: 173
    }
  ];

  return {
    ...createZaEncountersWorkflow(),
    stats: {
      sourceFileCount: 2,
      totalSlotCount: 2,
      totalTableCount: tables.length
    },
    tables
  };
}

function createZaNamedDungeonWorkflow(): EncountersWorkflow {
  const tables = [
    {
      ...makeTable('lysandre-labs', 'Lysandre Labs Spawn Point 001', 'd01', [
        makeSlot(0, 25, 'Pikachu', 'encount-data:90', 100, false, null)
      ]),
      location: 'Lysandre Labs'
    },
    {
      ...makeTable('old-building', 'Old Building Spawn Point 001', 'd03', [
        makeSlot(0, 26, 'Raichu', 'encount-data:91', 100, false, null)
      ]),
      location: 'Old Building'
    }
  ];

  return {
    ...createZaEncountersWorkflow(),
    stats: {
      sourceFileCount: 2,
      totalSlotCount: 2,
      totalTableCount: tables.length
    },
    tables
  };
}

async function openZaWildEncounters(
  workflow: EncountersWorkflow,
  bridgeOverrides: Partial<ProjectBridge> = {}
) {
  const user = userEvent.setup();

  render(
    <App
      bridge={createMockProjectBridge(
        {
          loadEncountersWorkflow: () => Promise.resolve({ workflow }),
          ...bridgeOverrides
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
    expect(within(encounterTable).getAllByRole('row')).toHaveLength(5);
    expect(within(encounterTable).getAllByRole('row', { name: /^Fletchling,/ })).toHaveLength(1);
    expect(
      within(encounterTable).getByRole('row', {
        name: 'Fletchling, 3 spawners, 1 more elsewhere, levels 5 to 10, source Base, Map silhouette Mixed'
      })
    ).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByText('4 encounters')).toBeInTheDocument();
    expect(
      screen.getByText(
        'This view shows 3 linked placements that share one Pokemon entry. Species, form, base levels, Alpha chance, and Alpha level bonus are shared; saving editable shared fields updates every linked placement. Alpha chance is repeated per placement for clarity and is read-only because this group mixes ordinary and special Alpha references. Spawner, slot, spawn probability, conditions, and map silhouette remain specific to each placement. Each linked placement rolls the shared Alpha chance independently for each spawn. A 100% Alpha chance is guaranteed and does not roll. This Pokemon entry is also used by 1 spawner outside this view.'
      )
    ).toBeInTheDocument();
    expect(screen.getByLabelText('Map silhouette')).toHaveTextContent(
      'Mixed: Only included placements add this Pokemon species to the Wild Zone completion card. Map-marked placements do not add another silhouette.'
    );

    const placements = screen.getByRole('table', { name: 'Fletchling linked spawners' });
    expect(within(placements).getAllByRole('row')).toHaveLength(4);
    const placementRows = within(placements).getAllByRole('row').slice(1);
    expect(placementRows[0]).toHaveAccessibleName(
      'Spawner 1, slot 1, probability 40, Any time, Any weather, Alpha chance 25% per spawn, Map silhouette Included'
    );
    expect(placementRows[1]).toHaveAccessibleName(
      'Spawner 2, slot 1, probability 100, Any time, Any weather, Alpha, Alpha chance 25% per spawn, Map silhouette Not included'
    );
    expect(placementRows[2]).toHaveAccessibleName(
      'Spawner 10, slot 1, probability 50, Any time, Any weather, Alpha chance 25% per spawn, Map silhouette Included'
    );
    const tenthPlacement = within(placements).getByRole('row', {
      name: 'Spawner 10, slot 1, probability 50, Any time, Any weather, Alpha chance 25% per spawn, Map silhouette Included'
    });
    await user.click(tenthPlacement);
    expect(tenthPlacement).toHaveAttribute('aria-pressed', 'true');
    const spawnerDetail = screen.getByText('Spawner', { selector: 'dt' }).parentElement;
    expect(spawnerDetail).not.toBeNull();
    expect(within(spawnerDetail!).getByText('Spawner 10')).toBeInTheDocument();
    expect(
      within(screen.getByText('Alpha chance', { selector: 'dt' }).parentElement!).getByText(
        '25% per spawn'
      )
    ).toBeInTheDocument();
    expect(
      within(screen.getByText('Alpha level bonus', { selector: 'dt' }).parentElement!).getByText(
        '+2 levels'
      )
    ).toBeInTheDocument();
    expect(
      within(screen.getByText('Alpha level range', { selector: 'dt' }).parentElement!).getByText(
        '7-12 (base 5-10 + 2)'
      )
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    expect(screen.getByLabelText('Alpha Chance (%)')).toBeDisabled();
    expect(screen.getByLabelText('Alpha Level Bonus')).toBeEnabled();
    expect(
      screen.getByText(
        'This group mixes ordinary and special Alpha references. Because special references can only be saved at 100%, Alpha chance is read-only for every placement in the group.'
      )
    ).toBeInTheDocument();
    const minLevel = screen.getByLabelText('Min Level');
    await user.clear(minLevel);
    await user.type(minLevel, '7');
    await user.click(
      within(placements).getByRole('row', {
        name: 'Spawner 2, slot 1, probability 100, Any time, Any weather, Alpha, Alpha chance 25% per spawn, Map silhouette Not included'
      })
    );
    expect(screen.getByLabelText('Min Level')).toHaveValue(7);
    expect(screen.getByLabelText('Alpha Chance (%)')).toBeDisabled();
    expect(screen.getByLabelText('Alpha Chance (%)')).toHaveValue(25);
    expect(screen.getByLabelText('Alpha Level Bonus')).toBeEnabled();
    await user.click(
      within(placements).getByRole('row', {
        name: 'Spawner 1, slot 1, probability 40, Any time, Any weather, Alpha chance 25% per spawn, Map silhouette Included'
      })
    );
    expect(screen.getByLabelText('Min Level')).toHaveValue(7);
    expect(screen.getByLabelText('Alpha Chance (%)')).toBeDisabled();
    expect(screen.getByLabelText('Alpha Chance (%)')).toHaveValue(25);
    expect(
      within(placements).getByRole('row', {
        name: 'Spawner 2, slot 1, probability 100, Any time, Any weather, Alpha, Alpha chance 25% per spawn, Map silhouette Not included'
      })
    ).toBeInTheDocument();

    await user.click(
      within(encounterTable).getByRole('row', {
        name: 'Bunnelby, 1 spawner, levels 5 to 10, source Base, Map silhouette Not included'
      })
    );
    expect(screen.queryByRole('table', { name: 'Bunnelby linked spawners' })).not.toBeInTheDocument();
    expect(
      within(screen.getByText('Alpha chance', { selector: 'dt' }).parentElement!).getByText('None')
    ).toBeInTheDocument();
    expect(
      within(screen.getByText('Alpha level bonus', { selector: 'dt' }).parentElement!).getByText(
        '+4 (inactive while Alpha chance is None)'
      )
    ).toBeInTheDocument();
    expect(screen.queryByText('Alpha level range', { selector: 'dt' })).not.toBeInTheDocument();
    expect(screen.getByLabelText('Alpha Chance (%)')).toBeEnabled();
    expect(screen.getByLabelText('Alpha Chance (%)')).toHaveAttribute('max', '99');
    expect(screen.getByLabelText('Map silhouette')).toHaveTextContent(
      'Not included: These slots are individually map-marked and do not add a silhouette to the Wild Zone completion card.'
    );

    await user.click(
      within(encounterTable).getByRole('row', {
        name: 'Pidgey, 1 spawner, levels 5 to 10, source Base, Map silhouette Included'
      })
    );
    expect(
      within(screen.getByText('Alpha chance', { selector: 'dt' }).parentElement!).getByText(
        'Unavailable'
      )
    ).toBeInTheDocument();
    expect(
      within(screen.getByText('Alpha level bonus', { selector: 'dt' }).parentElement!).getByText(
        'Unavailable'
      )
    ).toBeInTheDocument();
    expect(screen.queryByText('Alpha level range', { selector: 'dt' })).not.toBeInTheDocument();
    expect(screen.getByLabelText('Alpha Chance (%)')).toBeDisabled();
    expect(screen.getByLabelText('Alpha Chance (%)')).toHaveValue(null);
    expect(screen.getByLabelText('Alpha Level Bonus')).toBeDisabled();
    expect(screen.getByLabelText('Alpha Level Bonus')).toHaveValue(null);
    expect(
      screen.getByText(
        'This Alpha chance could not be read safely from the source data, so it is read-only.'
      )
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        'This Alpha level bonus could not be read safely from the source data, so it is read-only.'
      )
    ).toBeInTheDocument();
    expect(screen.getByLabelText('Map silhouette')).toHaveTextContent(
      'Included: Included slots add this Pokemon species to the Wild Zone completion card. The game shows each species once, so duplicate spawners and alternate forms share one silhouette.'
    );

    await user.click(
      within(encounterTable).getByRole('row', {
        name: 'Pikachu, 1 spawner, levels 5 to 10, source Base, Map silhouette Included'
      })
    );
    expect(
      within(screen.getByText('Alpha chance', { selector: 'dt' }).parentElement!).getByText(
        'Guaranteed Alpha'
      )
    ).toBeInTheDocument();
    expect(screen.getByLabelText('Alpha Chance (%)')).toBeDisabled();
    expect(screen.getByLabelText('Alpha Chance (%)')).toHaveValue(100);
    expect(screen.getByLabelText('Alpha Level Bonus')).toBeEnabled();
    expect(
      screen.getByText(
        'This Pokemon entry has a 100% Alpha chance, so the setting is guaranteed and read-only.'
      )
    ).toBeInTheDocument();
  }, 30_000);

  it('edits shared Alpha settings for two ordinary linked placements', async () => {
    const workflow = createTwoLinkedZaEncountersWorkflow();
    let shouldRejectUpdate = true;
    const updateEncounterSlotFields = vi.fn(
      async (request: Parameters<ProjectBridge['updateEncounterSlotFields']>[0]) => {
        if (shouldRejectUpdate) {
          shouldRejectUpdate = false;
          return {
            diagnostics: [
              {
                domain: 'workflow.encounters',
                field: 'alphaLevelBonus',
                message: 'Alpha level range cannot exceed 100.',
                severity: 'error' as const
              }
            ],
            session: request.session ?? {
              hasPendingChanges: false,
              pendingEdits: [],
              sessionId: 'session-alpha'
            },
            workflow
          };
        }

        return {
          diagnostics: [],
          session: {
            hasPendingChanges: true,
            pendingEdits: request.updates.map((update) => ({
              domain: 'workflow.encounters',
              field: update.field,
              newValue: update.value,
              recordId: 'encount-data:80',
              sources: [
                {
                  layer: 'base' as const,
                  relativePath:
                    'romfs/world/ik_data/field/pokemon/encount_data/encount_data/encount_data_array.bin'
                }
              ],
              summary: `Set ${update.field} to ${update.value}.`
            })),
            sessionId: 'session-alpha'
          },
          workflow
        };
      }
    );
    const user = await openZaWildEncounters(workflow, { updateEncounterSlotFields });

    expect(
      screen.getByText(
        'This view shows 2 linked placements that share one Pokemon entry. Species, form, base levels, Alpha chance, and Alpha level bonus are shared; saving them updates every linked placement. Spawner, slot, spawn probability, conditions, and map silhouette remain specific to each placement. Each linked placement rolls the shared Alpha chance independently for each spawn. A 100% Alpha chance is guaranteed and does not roll.'
      )
    ).toBeInTheDocument();
    expect(
      within(screen.getByText('Alpha level range', { selector: 'dt' }).parentElement!).getByText(
        '8-13 (base 5-10 + 3)'
      )
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    const alphaChance = screen.getByLabelText('Alpha Chance (%)');
    const alphaLevelBonus = screen.getByLabelText('Alpha Level Bonus');
    expect(alphaChance).toBeEnabled();
    expect(alphaChance).toHaveAttribute('max', '99');
    expect(alphaLevelBonus).toBeEnabled();

    await user.clear(alphaChance);
    await user.type(alphaChance, '20');
    await user.clear(screen.getByLabelText('Alpha Level Bonus'));
    await user.type(screen.getByLabelText('Alpha Level Bonus'), '4');
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(updateEncounterSlotFields).toHaveBeenCalledTimes(1));
    expect(screen.getByLabelText('Alpha Chance (%)')).toHaveValue(20);
    expect(screen.getByLabelText('Alpha Level Bonus')).toHaveValue(4);

    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(updateEncounterSlotFields).toHaveBeenCalledTimes(2));
    expect(updateEncounterSlotFields).toHaveBeenLastCalledWith(
      expect.objectContaining({
        updates: expect.arrayContaining([
          expect.objectContaining({
            field: 'alphaChancePercent',
            slot: 0,
            tableId: 'zone-1-spawner-1',
            value: '20'
          }),
          expect.objectContaining({
            field: 'alphaLevelBonus',
            slot: 0,
            tableId: 'zone-1-spawner-1',
            value: '4'
          })
        ])
      })
    );
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

  it('uses title-only side mission tabs and keeps mission numbers in selected details', async () => {
    const user = await openZaWildEncounters(createZaMissionWorkflow());

    expect(
      screen.getByRole('tab', { name: 'Full Course of Battles: High Rolling' })
    ).toHaveAttribute('aria-selected', 'true');
    expect(screen.queryByRole('tab', { name: /Mission 73/ })).not.toBeInTheDocument();
    expect(
      within(screen.getByText('Mission', { selector: 'dt' }).parentElement!).getByText(
        'Side Mission 73'
      )
    ).toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: 'Be a Defenseless Dodger!' }));

    expect(screen.getByRole('tab', { name: 'Be a Defenseless Dodger!' })).toHaveAttribute(
      'aria-selected',
      'true'
    );
    expect(
      within(screen.getByText('Mission', { selector: 'dt' }).parentElement!).getByText(
        'Side Mission 173'
      )
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/Be a Defenseless Dodger!, Be a Defenseless Dodger!/)
    ).not.toBeInTheDocument();
  }, 30_000);

  it('keeps exact named dungeon roots in the dungeon browser', async () => {
    const user = await openZaWildEncounters(createZaNamedDungeonWorkflow());
    const table = screen.getByRole('table', { name: 'Encounter tables' });

    expect(within(table).getByRole('row', { name: 'Dungeons' })).toBeInTheDocument();
    expect(within(table).getByRole('row', { name: /Lysandre Labs/ })).toHaveClass(
      'encounters-row-selected'
    );
    expect(within(table).getByRole('row', { name: /Old Building/ })).toBeInTheDocument();

    await user.click(within(table).getByRole('row', { name: /Old Building/ }));

    expect(within(table).getByRole('row', { name: /Old Building/ })).toHaveClass(
      'encounters-row-selected'
    );
  }, 30_000);
});
