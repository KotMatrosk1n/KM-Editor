/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { App } from '../../App';
import { type PlacementWorkflow } from '../../bridge/contracts';
import { type UpdatePlacementObjectFieldsRequest } from '../../bridge/svBatchFieldContracts';
import { createMockProjectBridge } from '../../testSupport/appTestFixtures';
import { useWorkbenchStore } from '../../workbenchStore';
import { getPlacementCategories, getPlacementCategoryId } from './placementUi';

const tauriEventMock = vi.hoisted(() => ({
  listen: vi.fn(() => Promise.resolve(() => undefined))
}));

vi.mock('@tauri-apps/api/event', () => ({
  listen: tauriEventMock.listen
}));

describe('PlacementSection', () => {
  beforeEach(() => {
    window.localStorage.clear();
    tauriEventMock.listen.mockClear();
    useWorkbenchStore.setState({
      activeSection: 'health',
      draftPaths: {
        baseExeFsPath: '',
        baseRomFsPath: '',
        outputRootPath: '',
        saveFilePath: '',
        pokemonLegendsZASupportFolderPath: '',
        scarletVioletSupportFolderPath: '',
        selectedGame: 'scarlet'
      },
      editSession: null,
      placementSearchText: '',
      placementWorkflow: null,
      projectStatus: 'idle',
      selectedPlacementObjectId: null,
      workflows: []
    });
  });

  it('shows S/V placement data by category without stale selections', async () => {
    const user = userEvent.setup();
    const loadPlacementWorkflow = vi.fn(async () => ({ workflow: createSvPlacementWorkflow() }));

    const { container } = render(
      <App bridge={createMockProjectBridge({ loadPlacementWorkflow }, true)} />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(await screen.findByRole('button', { name: 'Placement' }));

    expect(container.querySelector('.sv-placement-section')).not.toBeNull();
    expect(container.querySelector('.swsh-placement-section')).toBeNull();

    const table = await screen.findByRole('table', { name: 'Placed objects' });
    expect(within(table).queryByRole('columnheader', { name: 'Category' })).not.toBeInTheDocument();
    expect(within(table).getByRole('columnheader', { name: 'Pokemon / Data' })).toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: /Hidden Items/ }));
    expect(within(table).getByText('Hidden Items - Paldea')).toBeInTheDocument();
    expect(within(table).getAllByText('1001').length).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Edit' }));

    expect(screen.getByLabelText('Item 1')).not.toBeDisabled();
    expect(screen.queryByText(/Allowed range:/)).not.toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: /Visible Items/ }));

    expect(screen.queryByText('No entries in Visible Items.')).not.toBeInTheDocument();
    expect(within(table).getByText('5 TM100')).toBeInTheDocument();
    expect(within(table).getByText('12.5, 20, -7.25')).toBeInTheDocument();
    expect(screen.getAllByText('itemball_test_1').length).toBeGreaterThan(0);
    expect(screen.queryByLabelText('Item 1')).not.toBeInTheDocument();
  });

  it('groups Z-A Pokemon spawner placement rows and switches grouped transforms', async () => {
    const user = userEvent.setup();
    const loadPlacementWorkflow = vi.fn(async () => ({ workflow: createZaPlacementWorkflow() }));

    useWorkbenchStore.setState((state) => ({
      draftPaths: {
        ...state.draftPaths,
        selectedGame: 'za'
      }
    }));

    const { container } = render(
      <App bridge={createMockProjectBridge({ loadPlacementWorkflow }, true)} />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(await screen.findByRole('button', { name: 'Placement' }));

    expect(container.querySelector('.za-placement-section')).not.toBeNull();
    expect(container.querySelector('.swsh-placement-section')).toBeNull();

    const table = await screen.findByRole('table', { name: 'Placed objects' });
    expect(within(table).getAllByRole('row')).toHaveLength(3);
    expect(within(table).getByText('Boss Battle Beedrill (15)')).toBeInTheDocument();
    expect(within(table).getByText('Boss Battle Banette (354)')).toBeInTheDocument();
    expect(
      within(table).queryByText('Boss Battle Beedrill (15) Phase 1 Follower 1')
    ).not.toBeInTheDocument();
    expect(
      within(table).getByText('Phase 1 Follower 1, Simulation Follower 1, Rush Follower 1')
    ).toBeInTheDocument();
    expect(within(table).getByText('3 positions')).toBeInTheDocument();

    const groupBrowser = await screen.findByRole('region', {
      name: 'Z-A placement spawner group'
    });
    expect(within(groupBrowser).getByText('3 transforms')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Phase 1 Follower 1' })).toHaveAttribute(
      'aria-selected',
      'true'
    );

    await user.click(screen.getByRole('tab', { name: 'Simulation Follower 1' }));

    expect(screen.getByRole('tab', { name: 'Simulation Follower 1' })).toHaveAttribute(
      'aria-selected',
      'true'
    );
    const inspector = screen.getByLabelText('Selected placement object provenance');
    expect(
      within(inspector).getAllByText('Boss Battle Beedrill (15) Simulation Follower 1').length
    ).toBeGreaterThan(0);
    expect(
      within(inspector).getByText('Pokemon')
    ).toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: /Item Ball Spawners/ }));

    expect(within(table).getAllByRole('row')).toHaveLength(2);
    expect(within(table).getByText('Bleu District, Sector 1')).toBeInTheDocument();
    expect(within(table).getByText('Item Ball 01: Potion, Item Ball 02: Revive')).toBeInTheDocument();
    expect(within(table).getByText('2 positions')).toBeInTheDocument();
    expect(within(table).queryByText('itb_a0201_01')).not.toBeInTheDocument();

    const itemBallBrowser = await screen.findByRole('region', {
      name: 'Z-A placement spawner group'
    });
    expect(within(itemBallBrowser).getByText('2 transforms')).toBeInTheDocument();
    expect(within(itemBallBrowser).getByRole('tab', { name: /Item Ball 01: Potion/ }))
      .toHaveAttribute('aria-selected', 'true');

    await user.click(within(itemBallBrowser).getByRole('tab', { name: /Item Ball 02: Revive/ }));

    expect(within(itemBallBrowser).getByRole('tab', { name: /Item Ball 02: Revive/ }))
      .toHaveAttribute('aria-selected', 'true');
    expect(screen.getByLabelText('Selected placement object provenance'))
      .toHaveTextContent('Bleu District, Sector 1 Item Ball 02: Revive');
  });

  it('keeps SwSh legacy placement rows out of S/V category metadata', () => {
    const workflow = createSwShPlacementWorkflowWithStaleStructuredCategories();

    expect(workflow.categories?.map((category) => category.objectCount)).toEqual([247, 4378]);
    expect(getPlacementCategories(workflow)).toEqual([
      {
        description: 'Placed object records.',
        id: 'visibleItems',
        label: 'Visible Items',
        objectCount: 1
      },
      {
        description: 'Placed object records.',
        id: 'hiddenItems',
        label: 'Hidden Items',
        objectCount: 1
      }
    ]);
    expect(getPlacementCategoryId(workflow.objects[0]!)).toBe('visibleItems');
    expect(getPlacementCategoryId(workflow.objects[1]!)).toBe('hiddenItems');
  });

  it('does not render S/V placement category tabs for SwSh projects', async () => {
    const user = userEvent.setup();
    const loadPlacementWorkflow = vi.fn(async () => ({
      workflow: createSwShPlacementWorkflowWithStaleStructuredCategories()
    }));

    useWorkbenchStore.setState((state) => ({
      draftPaths: {
        ...state.draftPaths,
        selectedGame: 'sword'
      }
    }));

    const { container } = render(
      <App bridge={createMockProjectBridge({ loadPlacementWorkflow }, true)} />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(await screen.findByRole('button', { name: 'Placement' }));

    expect(container.querySelector('.swsh-placement-section')).not.toBeNull();
    expect(container.querySelector('.sv-placement-section')).toBeNull();

    const table = await screen.findByRole('table', { name: 'Placed objects' });
    expect(screen.queryByRole('tab', { name: /Visible Items/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /Hidden Items/ })).not.toBeInTheDocument();
    expect(within(table).getByText('Field item: Potion')).toBeInTheDocument();
    expect(within(table).getByText('Hidden item: Great Ball')).toBeInTheDocument();
  });

  it('sends only the changed SwSh placement field when saving an item row', async () => {
    const user = userEvent.setup();
    const workflow = createSwShPlacementWorkflowWithStaleStructuredCategories();
    const loadPlacementWorkflow = vi.fn(async () => ({ workflow }));
    const updatePlacementObjectField = vi.fn(async (request) => ({
      diagnostics: [],
      session: {
        hasPendingChanges: true,
        pendingEdits: [
          {
            domain: 'workflow.placement',
            field: request.field,
            newValue: request.value,
            recordId: request.objectId,
            sources: [
              {
                layer: 'base' as const,
                relativePath: 'romfs/bin/archive/field/resident/placement.gfpak'
              }
            ],
            summary: `Set ${request.field} to ${request.value}.`
          }
        ],
        sessionId: 'session-1'
      },
      workflow
    }));

    useWorkbenchStore.setState((state) => ({
      draftPaths: {
        ...state.draftPaths,
        selectedGame: 'sword'
      }
    }));

    render(
      <App bridge={createMockProjectBridge({ loadPlacementWorkflow, updatePlacementObjectField }, true)} />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(await screen.findByRole('button', { name: 'Placement' }));
    await user.click(screen.getByRole('button', { name: 'Edit' }));

    const quantityInput = await screen.findByLabelText('Quantity');
    await user.clear(quantityInput);
    await user.type(quantityInput, '7');
    await user.click(screen.getByRole('button', { name: 'Save Object' }));

    await waitFor(() => expect(updatePlacementObjectField).toHaveBeenCalledTimes(1));
    expect(updatePlacementObjectField).toHaveBeenCalledWith(
      expect.objectContaining({
        field: 'quantity',
        objectId: 'a_test.bin|0|fieldItem|0|-',
        value: '7'
      })
    );
  });

  it('saves S/V hidden item pool slot fields from the structured editor', async () => {
    const user = userEvent.setup();
    const workflow = createSvPlacementWorkflow();
    const loadPlacementWorkflow = vi.fn(async () => ({ workflow }));
    const updatePlacementObjectFields = vi.fn(async (request: UpdatePlacementObjectFieldsRequest) => ({
      diagnostics: [],
      session: {
        hasPendingChanges: true,
        pendingEdits: [
          ...(request.session?.pendingEdits ?? []),
          ...request.updates.map((update) => ({
            domain: 'workflow.placement',
            field: update.field,
            newValue: update.value,
            recordId: update.objectId,
            sources: [
              {
                layer: 'layered' as const,
                relativePath: 'romfs/world/data/item/hiddenItemDataTable/hiddenItemDataTable_array.bin'
              }
            ],
            summary: `Set hidden item ${update.field} to ${update.value}.`
          }))
        ],
        sessionId: 'session-1'
      },
      workflow
    }));

    render(
      <App bridge={createMockProjectBridge({ loadPlacementWorkflow, updatePlacementObjectFields }, true)} />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(await screen.findByRole('button', { name: 'Placement' }));
    await user.click(screen.getByRole('tab', { name: /Hidden Items/ }));
    await user.click(screen.getByRole('button', { name: 'Edit' }));

    const itemInput = screen.getByLabelText('Item 1');
    await user.clear(itemInput);
    await user.type(itemInput, '5');
    const chanceInput = screen.getByLabelText('Emerge value 1');
    await user.clear(chanceInput);
    await user.type(chanceInput, '175');
    const countInput = screen.getByLabelText('Drop count 1');
    await user.clear(countInput);
    await user.type(countInput, '4');
    await user.click(screen.getByRole('button', { name: 'Save Object' }));

    await waitFor(() => expect(updatePlacementObjectFields).toHaveBeenCalledTimes(1));
    expect(updatePlacementObjectFields).toHaveBeenCalledWith(
      expect.objectContaining({
        updates: [
          {
            field: 'hidden.item1.itemId',
            objectId: 'hidden-items:paldea:0',
            value: '5'
          },
          {
            field: 'hidden.item1.chance',
            objectId: 'hidden-items:paldea:0',
            value: '175'
          },
          {
            field: 'hidden.item1.count',
            objectId: 'hidden-items:paldea:0',
            value: '4'
          }
        ]
      })
    );
  });

  it('saves S/V visible item scene fields from the structured editor', async () => {
    const user = userEvent.setup();
    const workflow = createSvPlacementWorkflow();
    const loadPlacementWorkflow = vi.fn(async () => ({ workflow }));
    const updatePlacementObjectFields = vi.fn(async (request: UpdatePlacementObjectFieldsRequest) => ({
      diagnostics: [],
      session: {
        hasPendingChanges: true,
        pendingEdits: request.updates.map((update) => ({
          domain: 'workflow.placement',
          field: update.field,
          newValue: update.value,
          recordId: update.objectId,
          sources: [
            {
              layer: 'base' as const,
              relativePath: 'romfs/world/scene/parts/field/streaming_event/world_item_/world_item_0.trscn'
            }
          ],
          summary: `Set visible item ${update.field} to ${update.value}.`
        })),
        sessionId: 'session-1'
      },
      workflow
    }));

    render(
      <App bridge={createMockProjectBridge({ loadPlacementWorkflow, updatePlacementObjectFields }, true)} />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(await screen.findByRole('button', { name: 'Placement' }));
    await user.click(screen.getByRole('tab', { name: /Visible Items/ }));
    await user.click(screen.getByRole('button', { name: 'Edit' }));

    const itemInput = screen.getByLabelText('Item');
    await user.clear(itemInput);
    await user.type(itemInput, '1');
    const quantityInput = screen.getByLabelText('Quantity');
    await user.clear(quantityInput);
    await user.type(quantityInput, '6');
    await user.click(screen.getByRole('button', { name: 'Save Object' }));

    await waitFor(() => expect(updatePlacementObjectFields).toHaveBeenCalledTimes(1));
    expect(updatePlacementObjectFields).toHaveBeenCalledWith(
      expect.objectContaining({
        updates: [
          {
            field: 'visible.itemId',
            objectId: 'visible-items:paldea:0',
            value: '1'
          },
          {
            field: 'visible.quantity',
            objectId: 'visible-items:paldea:0',
            value: '6'
          }
        ]
      })
    );
  });
});

function createZaPlacementWorkflow(): PlacementWorkflow {
  return {
    categories: [
      {
        description: 'Pokemon spawner transform rows joined to Pokemon spawner table context.',
        id: 'pokemonSpawners',
        label: 'Pokemon Spawners',
        objectCount: 4
      },
      {
        description: 'Item ball spawner transform rows joined to item table context.',
        id: 'itemBallSpawners',
        label: 'Item Ball Spawners',
        objectCount: 2
      }
    ],
    diagnostics: [],
    editableFields: [
      {
        description: 'Spawner transform coordinate or rotation value.',
        field: 'point.positionX',
        group: 'Transform',
        isReadOnly: false,
        label: 'Position X',
        maximumValue: 1000000,
        minimumValue: -1000000,
        options: [],
        valueKind: 'number'
      },
      {
        description: 'Spawner transform coordinate or rotation value.',
        field: 'point.rotationYaw',
        group: 'Transform',
        isReadOnly: false,
        label: 'Rotation Yaw',
        maximumValue: 1000000,
        minimumValue: -1000000,
        options: [],
        valueKind: 'number'
      }
    ],
    objects: [
      createZaPlacementObject(
        0,
        'Boss Battle Beedrill (15) Phase 1 Follower 1',
        'Boss Battle Beedrill (15) Phase 1',
        'spn_boss_0015_01_follower1',
        10
      ),
      createZaPlacementObject(
        1,
        'Boss Battle Beedrill (15) Simulation Follower 1',
        'Boss Battle Beedrill (15) Simulation',
        'spn_boss_0015_sim_follower1',
        12
      ),
      createZaPlacementObject(
        2,
        'Boss Battle Beedrill (15) Rush Follower 1',
        'Boss Battle Beedrill (15) Rush',
        'spn_boss_0015_rus_follower1',
        14
      ),
      createZaPlacementObject(
        3,
        'Boss Battle Banette (354) Phase 1 Follower 1',
        'Boss Battle Banette (354) Phase 1',
        'spn_boss_0354_01_follower1',
        20
      ),
      createZaItemBallPlacementObject(
        4,
        'Bleu District, Sector 1 Item Ball 01: Potion',
        'Bleu District, Sector 1',
        'itb_a0201_01',
        'Potion',
        30
      ),
      createZaItemBallPlacementObject(
        5,
        'Bleu District, Sector 1 Item Ball 02: Revive',
        'Bleu District, Sector 1',
        'itb_a0201_02',
        'Revive',
        32
      )
    ],
    stats: {
      sourceFileCount: 2,
      totalAreaCount: 2,
      totalObjectCount: 6
    },
    summary: {
      availability: 'available',
      description: 'Z-A placement test workflow.',
      diagnostics: [],
      id: 'placement',
      label: 'Placement'
    }
  };
}

function createZaItemBallPlacementObject(
  index: number,
  label: string,
  map: string,
  scriptId: string,
  itemName: string,
  x: number
): PlacementWorkflow['objects'][number] {
  const sourceFile = 'romfs/world/ik_data/field/item_ball/item_ball_spawner_/item_ball_spawner_transform_array.bin';
  const position = x.toString();
  return {
    archiveMember: sourceFile,
    categoryId: 'itemBallSpawners',
    categoryLabel: 'Item Ball Spawners',
    chance: null,
    chanceIndex: null,
    fields: [
      {
        description: 'Scene transform row name.',
        displayValue: scriptId,
        field: 'point.name',
        group: 'Identity',
        isReadOnly: true,
        label: 'Object Name',
        maximumValue: 0,
        minimumValue: 0,
        value: scriptId,
        valueKind: 'text'
      },
      {
        description: 'Spawner transform coordinate or rotation value.',
        displayValue: position,
        field: 'point.positionX',
        group: 'Transform',
        isReadOnly: false,
        label: 'Position X',
        maximumValue: 1000000,
        minimumValue: -1000000,
        value: position,
        valueKind: 'number'
      },
      {
        description: 'Spawner transform coordinate or rotation value.',
        displayValue: '0',
        field: 'point.positionY',
        group: 'Transform',
        isReadOnly: false,
        label: 'Position Y',
        maximumValue: 1000000,
        minimumValue: -1000000,
        value: '0',
        valueKind: 'number'
      },
      {
        description: 'Spawner transform coordinate or rotation value.',
        displayValue: '6',
        field: 'point.positionZ',
        group: 'Transform',
        isReadOnly: false,
        label: 'Position Z',
        maximumValue: 1000000,
        minimumValue: -1000000,
        value: '6',
        valueKind: 'number'
      },
      {
        description: 'Resolved item ball spawner location.',
        displayValue: map,
        field: 'spawner.location',
        group: 'Spawner Context',
        isReadOnly: true,
        label: 'Location',
        maximumValue: 0,
        minimumValue: 0,
        value: map,
        valueKind: 'text'
      },
      {
        description: 'Resolved item table contents.',
        displayValue: itemName,
        field: 'spawner.primaryData',
        group: 'Spawner Context',
        isReadOnly: true,
        label: 'Primary Data',
        maximumValue: 0,
        minimumValue: 0,
        value: itemName,
        valueKind: 'text'
      }
    ],
    itemHash: scriptId,
    itemId: null,
    itemName,
    label,
    map,
    objectId: `itemBallSpawners|${sourceFile}|0|${index}`,
    objectIndex: index,
    objectType: 'Item Ball Spawner',
    provenance: {
      fileState: 'baseOnly',
      sourceFile,
      sourceLayer: 'base'
    },
    quantity: 0,
    rotationY: 90,
    scriptId,
    x,
    y: 0,
    zoneIndex: 0,
    z: 6
  };
}

function createZaPlacementObject(
  index: number,
  label: string,
  map: string,
  scriptId: string,
  x: number
): PlacementWorkflow['objects'][number] {
  const sourceFile = 'romfs/world/ik_data/field/pokemon_spawner/pokemon_spawner_point/pokemon_spawner_point_array.bin';
  const position = x.toString();
  return {
    archiveMember: sourceFile,
    categoryId: 'pokemonSpawners',
    categoryLabel: 'Pokemon Spawners',
    chance: null,
    chanceIndex: null,
    fields: [
      {
        description: 'Scene transform row name.',
        displayValue: scriptId,
        field: 'point.name',
        group: 'Identity',
        isReadOnly: true,
        label: 'Object Name',
        maximumValue: 0,
        minimumValue: 0,
        value: scriptId,
        valueKind: 'text'
      },
      {
        description: 'Spawner transform coordinate or rotation value.',
        displayValue: position,
        field: 'point.positionX',
        group: 'Transform',
        isReadOnly: false,
        label: 'Position X',
        maximumValue: 1000000,
        minimumValue: -1000000,
        value: position,
        valueKind: 'number'
      },
      {
        description: 'Spawner transform coordinate or rotation value.',
        displayValue: '0',
        field: 'point.positionY',
        group: 'Transform',
        isReadOnly: false,
        label: 'Position Y',
        maximumValue: 1000000,
        minimumValue: -1000000,
        value: '0',
        valueKind: 'number'
      },
      {
        description: 'Spawner transform coordinate or rotation value.',
        displayValue: '5',
        field: 'point.positionZ',
        group: 'Transform',
        isReadOnly: false,
        label: 'Position Z',
        maximumValue: 1000000,
        minimumValue: -1000000,
        value: '5',
        valueKind: 'number'
      },
      {
        description: 'Resolved Pokemon spawner id.',
        displayValue: scriptId,
        field: 'spawner.id',
        group: 'Spawner Context',
        isReadOnly: true,
        label: 'Spawner ID',
        maximumValue: 0,
        minimumValue: 0,
        value: scriptId,
        valueKind: 'text'
      }
    ],
    itemHash: scriptId,
    itemId: null,
    itemName: scriptId,
    label,
    map,
    objectId: `pokemonSpawners|${sourceFile}|0|${index}`,
    objectIndex: index,
    objectType: 'Pokemon Spawner',
    provenance: {
      fileState: 'baseOnly',
      sourceFile,
      sourceLayer: 'base'
    },
    quantity: 0,
    rotationY: 90,
    scriptId,
    x,
    y: 0,
    zoneIndex: 0,
    z: 5
  };
}

function createSvPlacementWorkflow(): PlacementWorkflow {
  return {
    categories: [
      {
        description: 'Visible item placement records.',
        id: 'visibleItems',
        label: 'Visible Items',
        objectCount: 1
      },
      {
        description: 'Hidden item pool tables.',
        id: 'hiddenItems',
        label: 'Hidden Items',
        objectCount: 1
      }
    ],
    diagnostics: [],
    editableFields: [
      {
        description: 'Scene-only visible item property sheet.',
        field: 'visible.pointType',
        group: 'Visible Item',
        isReadOnly: true,
        label: 'Point type',
        maximumValue: 0,
        minimumValue: 0,
        options: [],
        valueKind: 'text'
      },
      {
        description: 'Visible item id stored in the scene property sheet.',
        field: 'visible.itemId',
        group: 'Visible Item',
        isReadOnly: false,
        label: 'Item',
        maximumValue: 2147483647,
        minimumValue: 0,
        options: [
          { label: '1 Master Ball', value: 1 },
          { label: '5 TM100', value: 5 }
        ],
        valueKind: 'integer'
      },
      {
        description: 'Visible item quantity stored in the scene property sheet.',
        field: 'visible.quantity',
        group: 'Visible Item',
        isReadOnly: false,
        label: 'Quantity',
        maximumValue: 2147483647,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'hidden.item1.itemId',
        group: 'Hidden Item Slot 1',
        label: 'Item 1',
        maximumValue: 2147483647,
        minimumValue: 0,
        options: [
          { label: '1 Master Ball', value: 1 },
          { label: '5 Potion', value: 5 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'hidden.item1.chance',
        group: 'Hidden Item Slot 1',
        label: 'Emerge value 1',
        maximumValue: 2147483647,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'hidden.item1.count',
        group: 'Hidden Item Slot 1',
        label: 'Drop count 1',
        maximumValue: 2147483647,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      }
    ],
    objects: [
      {
        archiveMember: 'romfs/world/scene/parts/field/streaming_event/world_item_/world_item_0.trscn',
        categoryId: 'visibleItems',
        categoryLabel: 'Visible Items',
        chance: null,
        chanceIndex: null,
        fields: [
          {
            description: 'Scene-only point name.',
            displayValue: 'itemball_test_1',
            field: 'point.name',
            group: 'Scene Placement',
            isReadOnly: true,
            label: 'Point name',
            maximumValue: 0,
            minimumValue: 0,
            value: 'itemball_test_1',
            valueKind: 'text'
          },
          {
            description: 'Scene-only visible item property sheet.',
            displayValue: 'event_category_item',
            field: 'visible.pointType',
            group: 'Visible Item',
            isReadOnly: true,
            label: 'Point type',
            maximumValue: 0,
            minimumValue: 0,
            value: 'event_category_item',
            valueKind: 'text'
          },
          {
            description: 'Visible item id stored in the scene property sheet.',
            displayValue: '5 TM100',
            field: 'visible.itemId',
            group: 'Visible Item',
            isReadOnly: false,
            label: 'Item',
            maximumValue: 2147483647,
            minimumValue: 0,
            options: [
              { label: '1 Master Ball', value: 1 },
              { label: '5 TM100', value: 5 }
            ],
            value: '5',
            valueKind: 'integer'
          },
          {
            description: 'Visible item quantity stored in the scene property sheet.',
            displayValue: '3',
            field: 'visible.quantity',
            group: 'Visible Item',
            isReadOnly: false,
            label: 'Quantity',
            maximumValue: 2147483647,
            minimumValue: 0,
            value: '3',
            valueKind: 'integer'
          },
          {
            description: 'Scene-only TRSCN coordinate.',
            displayValue: '12.5',
            field: 'point.positionX',
            group: 'Scene Placement',
            isReadOnly: true,
            label: 'Position X',
            maximumValue: 0,
            minimumValue: 0,
            value: '12.5',
            valueKind: 'text'
          },
          {
            description: 'Scene-only TRSCN coordinate.',
            displayValue: '20',
            field: 'point.positionY',
            group: 'Scene Placement',
            isReadOnly: true,
            label: 'Position Y',
            maximumValue: 0,
            minimumValue: 0,
            value: '20',
            valueKind: 'text'
          },
          {
            description: 'Scene-only TRSCN coordinate.',
            displayValue: '-7.25',
            field: 'point.positionZ',
            group: 'Scene Placement',
            isReadOnly: true,
            label: 'Position Z',
            maximumValue: 0,
            minimumValue: 0,
            value: '-7.25',
            valueKind: 'text'
          }
        ],
        itemHash: '5',
        itemId: 5,
        itemName: 'TM100',
        label: '5 TM100',
        map: 'Visible Items - Paldea',
        objectId: 'visible-items:paldea:0',
        objectIndex: 0,
        objectType: 'VisibleItemScenePoint',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/world/scene/parts/field/streaming_event/world_item_/world_item_0.trscn',
          sourceLayer: 'base'
        },
        quantity: 3,
        rotationY: 1.5,
        scriptId: 'itemball_test_1',
        x: 12.5,
        y: 20,
        zoneIndex: 0,
        z: -7.25
      },
      {
        archiveMember: 'romfs/world/data/item/hiddenItemDataTable/hiddenItemDataTable_array.bin',
        categoryId: 'hiddenItems',
        categoryLabel: 'Hidden Items',
        chance: null,
        chanceIndex: null,
        fields: [
          {
            description: '',
            displayValue: '1 Master Ball',
            field: 'hidden.item1.itemId',
            group: 'Hidden Item Slot 1',
            isReadOnly: false,
            label: 'Item 1',
            maximumValue: 0,
            minimumValue: 0,
            value: '1',
            valueKind: 'text'
          },
          {
            description: '',
            displayValue: '200',
            field: 'hidden.item1.chance',
            group: 'Hidden Item Slot 1',
            isReadOnly: false,
            label: 'Emerge value 1',
            maximumValue: 0,
            minimumValue: 0,
            value: '200',
            valueKind: 'text'
          },
          {
            description: '',
            displayValue: '1',
            field: 'hidden.item1.count',
            group: 'Hidden Item Slot 1',
            isReadOnly: false,
            label: 'Drop count 1',
            maximumValue: 0,
            minimumValue: 0,
            value: '1',
            valueKind: 'text'
          }
        ],
        itemHash: '1001',
        itemId: null,
        itemName: '1001',
        label: '1001',
        map: 'Hidden Items - Paldea',
        objectId: 'hidden-items:paldea:0',
        objectIndex: 0,
        objectType: 'HiddenItemPool',
        provenance: {
          fileState: 'layeredOverride',
          sourceFile: 'romfs/world/data/item/hiddenItemDataTable/hiddenItemDataTable_array.bin',
          sourceLayer: 'layered'
        },
        quantity: 0,
        rotationY: 0,
        scriptId: '1001',
        x: 0,
        y: 0,
        zoneIndex: 0,
        z: 0
      }
    ],
    stats: {
      sourceFileCount: 2,
      totalAreaCount: 2,
      totalObjectCount: 2
    },
    summary: {
      availability: 'available',
      description: 'S/V placement test workflow.',
      diagnostics: [],
      id: 'placement',
      label: 'Placement'
    }
  };
}

function createSwShPlacementWorkflowWithStaleStructuredCategories(): PlacementWorkflow {
  return {
    categories: [
      {
        description: 'S/V visible item scene placements.',
        id: 'visibleItems',
        label: 'Visible Items',
        objectCount: 247
      },
      {
        description: 'S/V hidden item pool tables.',
        id: 'hiddenItems',
        label: 'Hidden Items',
        objectCount: 4378
      }
    ],
    diagnostics: [],
    editableFields: [
      {
        field: 'itemId',
        label: 'Item',
        maximumValue: 9999,
        minimumValue: 0,
        options: [{ label: '1 Potion', value: 1 }, { label: '2 Great Ball', value: 2 }],
        valueKind: 'integer'
      },
      {
        field: 'quantity',
        label: 'Quantity',
        maximumValue: 999,
        minimumValue: 0,
        valueKind: 'integer'
      }
    ],
    objects: [
      {
        archiveMember: 'a_test.bin',
        categoryId: '',
        categoryLabel: '',
        chance: null,
        chanceIndex: null,
        fields: [],
        itemHash: '0xAABBCCDD00112233',
        itemId: 1,
        itemName: 'Potion',
        label: 'Field item: Potion',
        map: 'Route 1',
        objectId: 'a_test.bin|0|fieldItem|0|-',
        objectIndex: 0,
        objectType: 'FieldItem',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/archive/field/resident/placement.gfpak',
          sourceLayer: 'base'
        },
        quantity: 1,
        rotationY: 90,
        scriptId: 'visible_potion',
        x: 10.5,
        y: 0,
        zoneIndex: 0,
        z: -4.25
      },
      {
        archiveMember: 'a_test.bin',
        categoryId: '',
        categoryLabel: '',
        chance: 35,
        chanceIndex: 0,
        fields: [],
        itemHash: '0xAABBCCDD00112244',
        itemId: 2,
        itemName: 'Great Ball',
        label: 'Hidden item: Great Ball',
        map: 'Route 1',
        objectId: 'a_test.bin|0|hiddenItem|0|0',
        objectIndex: 0,
        objectType: 'HiddenItem',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/archive/field/resident/placement.gfpak',
          sourceLayer: 'base'
        },
        quantity: 1,
        rotationY: 0,
        scriptId: null,
        x: 11,
        y: 0,
        zoneIndex: 0,
        z: -5
      }
    ],
    stats: {
      sourceFileCount: 1,
      totalAreaCount: 1,
      totalObjectCount: 2
    },
    summary: {
      availability: 'available',
      description: 'SwSh placement test workflow.',
      diagnostics: [],
      id: 'placement',
      label: 'Placement'
    }
  };
}
