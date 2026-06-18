/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { App } from '../../App';
import { type PlacementWorkflow } from '../../bridge/contracts';
import { createMockProjectBridge } from '../../testSupport/appTestFixtures';
import { useWorkbenchStore } from '../../workbenchStore';

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

    render(<App bridge={createMockProjectBridge({ loadPlacementWorkflow }, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(await screen.findByRole('button', { name: 'Placement' }));

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
            displayValue: 'ai_area01_01',
            field: 'point.tableKey',
            group: 'Scene Placement',
            isReadOnly: true,
            label: 'Pokemon data key',
            value: 'ai_area01_01'
          },
          {
            displayValue: '1 Bulbasaur',
            field: 'fixed.speciesId',
            group: 'Fixed Symbol Pokemon',
            isReadOnly: false,
            label: 'Species',
            value: '1'
          },
          {
            displayValue: 'Overgrow (Ability 1)',
            field: 'fixed.abilityMode',
            group: 'Fixed Symbol Pokemon',
            isReadOnly: false,
            label: 'Ability mode',
            options: [
              { label: '2 Overgrow (Ability 1)', value: 2 },
              { label: '4 Chlorophyll (Hidden Ability)', value: 4 }
            ],
            value: '2'
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
      }
    ],
    stats: {
      sourceFileCount: 1,
      totalAreaCount: 1,
      totalObjectCount: 1
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
