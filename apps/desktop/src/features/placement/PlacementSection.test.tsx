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
