/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { App } from '../../App';
import { type PlacementWorkflow } from '../../bridge/contracts';
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

  it('shows S/V Pokemon data by category without stale selections', async () => {
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
    expect(within(table).getAllByText('1 Bulbasaur').length).toBeGreaterThan(0);
    expect(screen.getAllByText('1 Bulbasaur').length).toBeGreaterThan(1);

    await user.click(screen.getByRole('button', { name: 'Edit' }));

    expect(screen.getByLabelText('Pokemon data key')).toBeDisabled();
    expect(screen.getByText('Read-only')).toBeInTheDocument();
    expect(screen.queryByText(/Allowed range:/)).not.toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: /Visible Items/ }));

    expect(screen.getByText('No entries in Visible Items.')).toBeInTheDocument();
    expect(screen.getByText('No placement object selected.')).toBeInTheDocument();
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
    const updatePlacementObjectField = vi.fn(async (request) => ({
      diagnostics: [],
      session: {
        hasPendingChanges: true,
        pendingEdits: [
          ...(request.session?.pendingEdits ?? []),
          {
            domain: 'workflow.placement',
            field: request.field,
            newValue: request.value,
            recordId: request.objectId,
            sources: [
              {
                layer: 'layered' as const,
                relativePath: 'romfs/world/data/item/hiddenItemDataTable/hiddenItemDataTable_array.bin'
              }
            ],
            summary: `Set hidden item ${request.field} to ${request.value}.`
          }
        ],
        sessionId: 'session-1'
      },
      workflow
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

    await waitFor(() => expect(updatePlacementObjectField).toHaveBeenCalledTimes(3));
    expect(updatePlacementObjectField).toHaveBeenNthCalledWith(
      1,
      expect.objectContaining({
        field: 'hidden.item1.itemId',
        objectId: 'hidden-items:paldea:0',
        value: '5'
      })
    );
    expect(updatePlacementObjectField).toHaveBeenNthCalledWith(
      2,
      expect.objectContaining({
        field: 'hidden.item1.chance',
        objectId: 'hidden-items:paldea:0',
        value: '175'
      })
    );
    expect(updatePlacementObjectField).toHaveBeenNthCalledWith(
      3,
      expect.objectContaining({
        field: 'hidden.item1.count',
        objectId: 'hidden-items:paldea:0',
        value: '4'
      })
    );
  });
});

function createSvPlacementWorkflow(): PlacementWorkflow {
  return {
    categories: [
      {
        description: 'Static overworld Pokemon symbol tables.',
        id: 'fixedSymbols',
        label: 'Fixed Symbols',
        objectCount: 1
      },
      {
        description: 'Visible item placement records.',
        id: 'visibleItems',
        label: 'Visible Items',
        objectCount: 0
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
        description: 'Scene links a point to fixed-symbol table data.',
        field: 'point.tableKey',
        group: 'Scene Placement',
        isReadOnly: true,
        label: 'Pokemon data key',
        maximumValue: Number.MAX_SAFE_INTEGER,
        minimumValue: Number.MIN_SAFE_INTEGER,
        valueKind: 'text'
      },
      {
        field: 'fixed.speciesId',
        group: 'Fixed Symbol Pokemon',
        label: 'Species',
        maximumValue: 65535,
        minimumValue: 0,
        options: [{ label: '1 Bulbasaur', value: 1 }],
        valueKind: 'integer'
      },
      {
        field: 'fixed.abilityMode',
        group: 'Fixed Symbol Pokemon',
        label: 'Ability mode',
        maximumValue: 4,
        minimumValue: 0,
        options: [
          { label: '0 Random Ability 1 or 2', value: 0 },
          { label: '2 Overgrow (Ability 1)', value: 2 },
          { label: '4 Chlorophyll (Hidden Ability)', value: 4 }
        ],
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
        archiveMember: 'fixed_symbol_table_array.bin',
        categoryId: 'fixedSymbols',
        categoryLabel: 'Fixed Symbols',
        chance: null,
        chanceIndex: null,
        fields: [
          {
            description: '',
            displayValue: 'ai_area01_01',
            field: 'point.tableKey',
            group: 'Scene Placement',
            isReadOnly: true,
            label: 'Pokemon data key',
            maximumValue: 0,
            minimumValue: 0,
            value: 'ai_area01_01',
            valueKind: 'text'
          },
          {
            description: '',
            displayValue: '1 Bulbasaur',
            field: 'fixed.speciesId',
            group: 'Fixed Symbol Pokemon',
            isReadOnly: false,
            label: 'Species',
            maximumValue: 65535,
            minimumValue: 0,
            value: '1',
            valueKind: 'integer'
          },
          {
            description: '',
            displayValue: 'Overgrow (Ability 1)',
            field: 'fixed.abilityMode',
            group: 'Fixed Symbol Pokemon',
            isReadOnly: false,
            label: 'Ability mode',
            maximumValue: 4,
            minimumValue: 0,
            options: [
              { label: '2 Overgrow (Ability 1)', value: 2 },
              { label: '4 Chlorophyll (Hidden Ability)', value: 4 }
            ],
            value: '2',
            valueKind: 'integer'
          }
        ],
        itemHash: 'ai_area01_01',
        itemId: null,
        itemName: 'Bulbasaur',
        label: 'ai_area01_01',
        map: 'Fixed Symbol Table',
        objectId: 'fixed-symbol:0',
        objectIndex: 0,
        objectType: 'FixedSymbol',
        provenance: {
          fileState: 'layeredOverride',
          sourceFile: 'romfs/world/data/field/fixed_symbol/fixed_symbol_table/fixed_symbol_table_array.bin',
          sourceLayer: 'layered'
        },
        quantity: 0,
        rotationY: 0,
        scriptId: 'ai_area01_01',
        x: 0,
        y: 0,
        zoneIndex: 0,
        z: 0
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
