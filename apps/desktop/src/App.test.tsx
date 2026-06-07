/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from './App';
import {
  type EncountersWorkflow,
  type ExeFsPatchWorkflow,
  type FlagworkSaveWorkflow,
  type ItemsWorkflow,
  type PlacementWorkflow,
  type ProjectFileGraph,
  type ProjectHealth,
  type RaidRewardsWorkflow,
  type ShopsWorkflow,
  type TextWorkflow,
  type TrainersWorkflow,
  type WorkflowSummary
} from './bridge/contracts';
import { type ProjectBridge } from './bridge/projectBridge';
import { useWorkbenchStore } from './workbenchStore';

describe('App', () => {
  beforeEach(() => {
    useWorkbenchStore.setState({
      activeSection: 'health',
      applyResult: null,
      changePlan: null,
      draftPaths: {
        baseExeFsPath: '',
        baseRomFsPath: '',
        outputRootPath: ''
      },
      editSession: null,
      editValidationDiagnostics: [],
      encounterSearchText: '',
      encountersWorkflow: null,
      exeFsPatchSearchText: '',
      exeFsPatchWorkflow: null,
      flagworkSaveSearchText: '',
      flagworkSaveWorkflow: null,
      itemSearchText: '',
      itemsWorkflow: null,
      openProject: null,
      placementSearchText: '',
      placementWorkflow: null,
      projectStatus: 'idle',
      raidRewardSearchText: '',
      raidRewardsWorkflow: null,
      selectedEncounterTableId: null,
      selectedExeFsCheckId: null,
      selectedExeFsPatchId: null,
      selectedFlagId: null,
      selectedItemId: null,
      selectedPlacementObjectId: null,
      selectedRaidRewardTableId: null,
      selectedSaveBlockId: null,
      selectedShopId: null,
      selectedTextKey: null,
      selectedTrainerId: null,
      shopSearchText: '',
      shopsWorkflow: null,
      textSearchText: '',
      textWorkflow: null,
      trainerSearchText: '',
      trainersWorkflow: null,
      workflows: []
    });
  });

  it('renders the project workbench shell', () => {
    render(<App />);

    expect(screen.getByText('KM Editor')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Project Health' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Project Paths' })).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Open Project' }).length).toBeGreaterThan(0);
  });

  it('switches workbench sections', async () => {
    const user = userEvent.setup();
    render(<App />);

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByRole('heading', { name: 'Changes' })).toBeInTheDocument();
  });

  it('validates and opens a read-only project shell state', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge()} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);

    expect(await screen.findAllByText('Read-only ready')).toHaveLength(2);

    await user.click(screen.getByRole('button', { name: 'Workflows' }));

    expect(screen.getByRole('heading', { name: 'Workflow List' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: 'Items' })).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 3, name: 'Text and Dialogue Map' })
    ).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: 'Trainers' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: 'Shops' })).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 3, name: 'Encounters and Wild Data' })
    ).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: 'Raid Rewards' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: 'Placement' })).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 3, name: 'Flagwork and Save Inspectors' })
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 3, name: 'ExeFS Patch Manager' })
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 3, name: 'Royal Candy Workflows' })
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 3, name: 'Spreadsheet Import Tooling' })
    ).toBeInTheDocument();
    expect(screen.getAllByText('Read-only').length).toBeGreaterThan(0);
  });

  it('opens Items, searches records, and shows selected provenance', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge()} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Items' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Items' })).toBeInTheDocument();
    expect(screen.getAllByText('Potion').length).toBeGreaterThan(0);

    await user.type(screen.getByLabelText('Search items'), 'antidote');
    await user.click(screen.getByText('Antidote'));

    expect(screen.queryByText('Potion')).not.toBeInTheDocument();
    expect(screen.getByText('romfs/bin/pml/item/item.dat')).toBeInTheDocument();
    expect(screen.getByText('Base only')).toBeInTheDocument();
  });

  it('starts an Items edit session, saves a pending buy price, validates it, reviews a plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Items' }));
    await user.click(await screen.findByRole('button', { name: 'Start Edit Session' }));

    const buyPriceInput = screen.getByLabelText('Buy price');
    expect(screen.getByLabelText('Sell price')).toBeInTheDocument();
    await user.clear(buyPriceInput);
    await user.type(buyPriceInput, '450');
    await user.click(screen.getByRole('button', { name: 'Save buy price' }));

    expect(await screen.findByDisplayValue('450')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Potion buy price to 450.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Change' }));

    expect(await screen.findByText('Pending item change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Review Change Plan' }));

    expect(await screen.findByRole('heading', { name: 'Change Plan Review' })).toBeInTheDocument();
    expect(screen.getAllByText('romfs/bin/pml/item/item.dat').length).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(await screen.findByRole('heading', { name: 'Apply Result' })).toBeInTheDocument();
    expect(screen.getByText('Applied Items change plan to the configured LayeredFS output root.')).toBeInTheDocument();
  });

  it('opens Text, edits a line, reviews a message table plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Text' }));

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Text and Dialogue Map' })
    ).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/message/English/common/story.dat').length
    ).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Start Edit Session' }));
    const textValue = screen.getByLabelText('Text value');
    await user.clear(textValue);
    await user.type(textValue, 'Hello there.');
    await user.click(screen.getByRole('button', { name: 'Save Text' }));

    expect(await screen.findByDisplayValue('Hello there.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set story #0 to "Hello there.".')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Change' }));

    expect(await screen.findByText('Pending text change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Review Change Plan' }));

    expect(await screen.findByRole('heading', { name: 'Change Plan Review' })).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/message/English/common/story.dat').length
    ).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(await screen.findByRole('heading', { name: 'Apply Result' })).toBeInTheDocument();
    expect(screen.getByText('Applied Text change plan to the configured LayeredFS output root.')).toBeInTheDocument();
  });

  it('opens Trainers, edits a party level, reviews a trainer plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Trainers' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Trainers' })).toBeInTheDocument();
    expect(screen.getAllByText('Avery').length).toBeGreaterThan(0);
    expect(screen.getByRole('option', { name: 'Slot 1: Grookey' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Start Edit Session' }));
    const levelInput = screen.getByLabelText('Level');
    await user.clear(levelInput);
    await user.type(levelInput, '25');
    await user.click(screen.getByRole('button', { name: 'Save level' }));

    expect(await screen.findByDisplayValue('25')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Avery slot 1 level to 25.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Change' }));

    expect(await screen.findByText('Pending trainer change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Review Change Plan' }));

    expect(await screen.findByRole('heading', { name: 'Change Plan Review' })).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/trainer/trainer_poke/trainer_010.bin').length
    ).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(await screen.findByRole('heading', { name: 'Apply Result' })).toBeInTheDocument();
    expect(
      screen.getByText('Applied Trainers change plan to the configured LayeredFS output root.')
    ).toBeInTheDocument();
  });

  it('opens Shops, edits an inventory item, reviews a shop plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Shops' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Shops' })).toBeInTheDocument();
    expect(screen.getAllByText('Poke Mart').length).toBeGreaterThan(0);
    expect(screen.getByRole('option', { name: 'Slot 1: Potion' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Start Edit Session' }));
    const itemIdInput = screen.getByLabelText('Item ID');
    await user.clear(itemIdInput);
    await user.type(itemIdInput, '2');
    await user.click(screen.getByRole('button', { name: 'Save Item' }));

    expect(await screen.findByDisplayValue('2')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Poke Mart slot 1 item ID to 2.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Change' }));

    expect(await screen.findByText('Pending shop change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Review Change Plan' }));

    expect(await screen.findByRole('heading', { name: 'Change Plan Review' })).toBeInTheDocument();
    expect(screen.getAllByText('romfs/bin/app/shop/shop_data.bin').length).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(await screen.findByRole('heading', { name: 'Apply Result' })).toBeInTheDocument();
    expect(
      screen.getByText('Applied Shops change plan to the configured LayeredFS output root.')
    ).toBeInTheDocument();
  });

  it('opens Encounters, edits a slot probability, reviews a wild data plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Encounters' }));

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Encounters and Wild Data' })
    ).toBeInTheDocument();
    expect(screen.getAllByText('Zone 0x1122334455667788').length).toBeGreaterThan(0);
    expect(screen.getByRole('option', { name: 'Slot 1: Bulbasaur' })).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Encounter slot'), '2');
    await user.click(screen.getByRole('button', { name: 'Start Edit Session' }));
    const probabilityInput = screen.getByLabelText('Probability');
    await user.clear(probabilityInput);
    await user.type(probabilityInput, '40');
    await user.click(screen.getByRole('button', { name: 'Save probability' }));

    expect(await screen.findByDisplayValue('40')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(
      screen.getByText(
        'Set Sword Symbol Zone 0x1122334455667788 Normal slot 2 probability to 40.'
      )
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Change' }));

    expect(await screen.findByText('Pending encounter change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Review Change Plan' }));

    expect(await screen.findByRole('heading', { name: 'Change Plan Review' })).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/archive/field/resident/data_table.gfpak').length
    ).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(await screen.findByRole('heading', { name: 'Apply Result' })).toBeInTheDocument();
    expect(
      screen.getByText('Applied Encounters change plan to the configured LayeredFS output root.')
    ).toBeInTheDocument();
  });

  it('opens Raid Rewards, edits a star value, reviews a reward plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Raid Rewards' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Raid Rewards' })).toBeInTheDocument();
    expect(screen.getAllByText('0xAABBCCDD00112233').length).toBeGreaterThan(0);
    expect(screen.getByRole('option', { name: 'Slot 1: Exp. Candy L' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Start Edit Session' }));
    const starValueInput = screen.getByLabelText('5-star value');
    await user.clear(starValueInput);
    await user.type(starValueInput, '77');
    await user.click(screen.getByRole('button', { name: 'Save 5-star value' }));

    expect(await screen.findByDisplayValue('77')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(
      screen.getByText('Set Drop 0xAABBCCDD00112233 slot 1 5-star value to 77.')
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Change' }));

    expect(await screen.findByText('Pending raid reward change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Review Change Plan' }));

    expect(await screen.findByRole('heading', { name: 'Change Plan Review' })).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/archive/field/resident/data_table.gfpak').length
    ).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(await screen.findByRole('heading', { name: 'Apply Result' })).toBeInTheDocument();
    expect(
      screen.getByText('Applied Raid Rewards change plan to the configured LayeredFS output root.')
    ).toBeInTheDocument();
  });

  it('opens Flagwork and Save Inspectors, searches real keys, and shows provenance', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge()} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Flagwork' }));

    expect(
      await screen.findByRole('heading', {
        level: 2,
        name: 'Flagwork and Save Inspectors'
      })
    ).toBeInTheDocument();
    expect(screen.getAllByText('FE_TEST_FLAG').length).toBeGreaterThan(0);
    expect(screen.getAllByText('0x55667788').length).toBeGreaterThan(0);

    await user.type(screen.getByLabelText('Search flagwork and save keys'), 'scene');

    expect(screen.getAllByText('WK_SCENE_MAIN').length).toBeGreaterThan(0);
    expect(screen.getAllByText('0xDDEEFF00').length).toBeGreaterThan(0);
    expect(screen.getAllByText('romfs/bin/flagwork/scene_work.tbl').length).toBeGreaterThan(0);
  });

  it('opens ExeFS Patch Manager, searches compatibility checks, and shows provenance', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge()} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open ExeFS' }));

    expect(
      await screen.findByRole('heading', {
        level: 2,
        name: 'ExeFS Patch Manager'
      })
    ).toBeInTheDocument();
    expect(screen.getAllByText('ExeFS main compatibility').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Patch code cave').length).toBeGreaterThan(0);
    expect(screen.getAllByText('exefs/main').length).toBeGreaterThan(0);

    await user.type(screen.getByLabelText('Search ExeFS compatibility checks'), 'royal');

    expect(screen.getAllByText('Royal Candy immediate scan').length).toBeGreaterThan(0);
    expect(screen.getAllByText('.text').length).toBeGreaterThan(0);
    expect(screen.getAllByText('file+0x100').length).toBeGreaterThan(0);
  });

  it('shows bridge diagnostics when project validation fails before reaching the backend', async () => {
    const user = userEvent.setup();
    render(
      <App
        bridge={createMockProjectBridge({
          validateProject: () => Promise.reject(new Error('Project bridge unavailable.'))
        })}
      />
    );

    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));

    expect(await screen.findByText('Project bridge unavailable.')).toBeInTheDocument();
  });
});

function createMockProjectBridge(
  overrides: Partial<ProjectBridge> = {},
  canEdit = false
): ProjectBridge {
  const health: ProjectHealth = {
    canOpenEditableWorkflows: canEdit,
    canOpenReadOnlyWorkflows: true,
    diagnostics: [],
    fileGraph: {
      baseFileCount: 2,
      layeredFileCount: 0,
      layeredOnlyCount: 0,
      overrideCount: 0
    },
    paths: [
      {
        diagnostics: [],
        isRequired: true,
        path: 'base-romfs',
        role: 'baseRomFs',
        status: 'valid'
      },
      {
        diagnostics: [],
        isRequired: true,
        path: 'base-exefs',
        role: 'baseExeFs',
        status: 'valid'
      },
      {
        diagnostics: [],
        isRequired: false,
        path: canEdit ? 'output' : null,
        role: 'outputRoot',
        status: canEdit ? 'valid' : 'notSet'
      }
    ],
    state: canEdit ? 'editableReady' : 'readOnlyReady'
  };
  const fileGraph: ProjectFileGraph = {
    entries: [],
    summary: health.fileGraph
  };
  const itemsWorkflow: ItemsWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'buyPrice',
        label: 'Buy price',
        maximumValue: 999_999,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'sellPrice',
        label: 'Sell price',
        maximumValue: 499_999,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'wattsPrice',
        label: 'Watts price',
        maximumValue: 999_999,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'alternatePrice',
        label: 'Alternate price',
        maximumValue: 999_999,
        minimumValue: 0,
        valueKind: 'integer'
      }
    ],
    items: [
      {
        alternatePrice: 3,
        buyPrice: 300,
        category: 'Medicine',
        itemId: 1,
        name: 'Potion',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/item/item.dat',
          sourceLayer: 'base'
        },
        sellPrice: 150,
        sharedItemIds: [1],
        wattsPrice: 15
      },
      {
        alternatePrice: 5,
        buyPrice: 200,
        category: 'Medicine',
        itemId: 2,
        name: 'Antidote',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/item/item.dat',
          sourceLayer: 'base'
        },
        sellPrice: 100,
        sharedItemIds: [2],
        wattsPrice: 10
      }
    ],
    stats: {
      sourceFileCount: 2,
      totalItemCount: 2
    },
    summary: {
      availability: canEdit ? 'available' : 'readOnly',
      description: 'Item records, names, and source provenance.',
      diagnostics: [],
      id: 'items',
      label: 'Items'
    }
  };
  const textWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Text entries, dialogue references, and source provenance.',
    diagnostics: [],
    id: 'text',
    label: 'Text and Dialogue Map'
  };
  const textWorkflow: TextWorkflow = {
    diagnostics: [],
    dialogueReferences: [
      {
        context: 'common/story.dat',
        dialogueId: 'common/story:0',
        label: 'story #0',
        preview: 'Welcome to the lab.',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/message/English/common/story.dat',
          sourceLayer: 'base'
        },
        textId: 0
      }
    ],
    editableFields: [
      {
        field: 'value',
        label: 'Text value',
        maximumLength: 4096,
        minimumLength: 0,
        valueKind: 'multilineText'
      }
    ],
    entries: [
      {
        canEdit: true,
        editBlockedReason: null,
        label: 'story #0',
        language: 'English',
        lineIndex: 0,
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/message/English/common/story.dat',
          sourceLayer: 'base'
        },
        sourceFile: 'romfs/bin/message/English/common/story.dat',
        textId: 0,
        textKey: 'romfs/bin/message/English/common/story.dat#0',
        value: 'Welcome to the lab.'
      }
    ],
    stats: {
      dialogueReferenceCount: 1,
      sourceFileCount: 1,
      totalTextEntryCount: 1
    },
    summary: textWorkflowSummary
  };
  const trainersWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Trainer parties, classes, battle types, and source provenance.',
    diagnostics: [],
    id: 'trainers',
    label: 'Trainers'
  };
  const trainersWorkflow: TrainersWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'trainerClassId',
        label: 'Trainer class ID',
        maximumValue: 65535,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'battleType',
        label: 'Battle type',
        maximumValue: 2,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'speciesId',
        label: 'Species ID',
        maximumValue: 65535,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'level',
        label: 'Level',
        maximumValue: 100,
        minimumValue: 1,
        valueKind: 'integer'
      },
      {
        field: 'heldItemId',
        label: 'Held item ID',
        maximumValue: 65535,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'move1Id',
        label: 'Move 1 ID',
        maximumValue: 65535,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'move2Id',
        label: 'Move 2 ID',
        maximumValue: 65535,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'move3Id',
        label: 'Move 3 ID',
        maximumValue: 65535,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'move4Id',
        label: 'Move 4 ID',
        maximumValue: 65535,
        minimumValue: 0,
        valueKind: 'integer'
      }
    ],
    stats: {
      sourceFileCount: 2,
      totalPokemonCount: 1,
      totalTrainerCount: 1
    },
    summary: trainersWorkflowSummary,
    trainers: [
      {
        battleType: 'Doubles',
        battleTypeValue: 1,
        location: 'Trainer 10',
        name: 'Avery',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/trainer/trainer_data/trainer_010.bin',
          sourceLayer: 'base',
          teamFileState: 'baseOnly',
          teamSourceFile: 'romfs/bin/trainer/trainer_poke/trainer_010.bin',
          teamSourceLayer: 'base'
        },
        team: [
          {
            heldItem: 'Potion',
            heldItemId: 1,
            level: 12,
            moveIds: [1, 2, 0, 0],
            moves: ['Scratch', 'Growl', 'None', 'None'],
            slot: 1,
            species: 'Grookey',
            speciesId: 810
          }
        ],
        trainerClass: 'Pokemon Trainer',
        trainerClassId: 5,
        trainerId: 10
      }
    ]
  };
  const shopsWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Shop inventories, item metadata, and source provenance.',
    diagnostics: [],
    id: 'shops',
    label: 'Shops'
  };
  const shopsWorkflow: ShopsWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'itemId',
        label: 'Item ID',
        maximumValue: 65535,
        minimumValue: 0,
        valueKind: 'integer'
      }
    ],
    shops: [
      {
        currency: 'Money',
        inventory: [
          {
            itemId: 1,
            itemName: 'Potion',
            price: 300,
            slot: 1,
            stockLimit: null
          },
          {
            itemId: 2,
            itemName: 'Antidote',
            price: 200,
            slot: 2,
            stockLimit: null
          }
        ],
        location: 'Poke Mart',
        name: 'Poke Mart',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/app/shop/shop_data.bin',
          sourceLayer: 'base'
        },
        shopId: 'single:1F3FF031A3A24490'
      }
    ],
    stats: {
      sourceFileCount: 1,
      totalInventoryItemCount: 2,
      totalShopCount: 1
    },
    summary: shopsWorkflowSummary
  };
  const encountersWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Encounter tables, wild slots, levels, weather, and source provenance.',
    diagnostics: [],
    id: 'encounters',
    label: 'Encounters and Wild Data'
  };
  const encountersWorkflow: EncountersWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'speciesId',
        label: 'Species ID',
        maximumValue: 65535,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'form',
        label: 'Form',
        maximumValue: 255,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'probability',
        label: 'Probability',
        maximumValue: 100,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'levelMin',
        label: 'Min Level',
        maximumValue: 100,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'levelMax',
        label: 'Max Level',
        maximumValue: 100,
        minimumValue: 0,
        valueKind: 'integer'
      }
    ],
    stats: {
      sourceFileCount: 1,
      totalSlotCount: 2,
      totalTableCount: 1
    },
    summary: encountersWorkflowSummary,
    tables: [
      {
        archiveMember: 'encount_symbol_k.bin',
        area: 'Symbol',
        encounterType: 'Normal',
        gameVersion: 'Sword',
        location: 'Zone 0x1122334455667788',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/archive/field/resident/data_table.gfpak',
          sourceLayer: 'base'
        },
        slots: [
          {
            form: 0,
            levelMax: 8,
            levelMin: 3,
            slot: 1,
            speciesId: 1,
            species: 'Bulbasaur',
            timeOfDay: null,
            weather: 'Normal',
            weight: 35
          },
          {
            form: 1,
            levelMax: 8,
            levelMin: 3,
            slot: 2,
            speciesId: 4,
            species: 'Charmander',
            timeOfDay: null,
            weather: 'Normal',
            weight: 15
          }
        ],
        tableId: 'sword:symbol:0:1122334455667788:0'
      }
    ]
  };
  const raidRewardsWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Raid reward tables, den ranks, item quantities, and source provenance.',
    diagnostics: [],
    id: 'raidRewards',
    label: 'Raid Rewards'
  };
  const raidRewardsWorkflow: RaidRewardsWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'itemId',
        label: 'Item ID',
        maximumValue: 65535,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'star5Value',
        label: '5-star value',
        maximumValue: 999,
        minimumValue: 0,
        valueKind: 'integer'
      }
    ],
    stats: {
      sourceFileCount: 1,
      totalRewardItemCount: 1,
      totalTableCount: 1
    },
    summary: raidRewardsWorkflowSummary,
    tables: [
      {
        archiveMember: 'nest_hole_drop_rewards.bin',
        denId: 'table_AABBCCDD00112233',
        gameVersion: 'Sword/Shield',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/archive/field/resident/data_table.gfpak',
          sourceLayer: 'base'
        },
        rank: 0,
        rewardKind: 'drop',
        rewardKindLabel: 'Drop',
        rewards: [
          {
            entryId: 10,
            itemId: 3,
            itemName: 'Exp. Candy L',
            quantity: 0,
            slot: 1,
            values: [40, 30, 20, 10, 5],
            weight: 40
          }
        ],
        sourceTableHash: '0xAABBCCDD00112233',
        tableId: 'drop:0:AABBCCDD00112233',
        tableIndex: 0
      }
    ]
  };
  const placementWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Placed objects, map coordinates, item pickups, and source provenance.',
    diagnostics: [],
    id: 'placement',
    label: 'Placement'
  };
  const placementWorkflow: PlacementWorkflow = {
    diagnostics: [],
    editableFields: [
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
        chance: null,
        chanceIndex: null,
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
      }
    ],
    stats: {
      sourceFileCount: 3,
      totalAreaCount: 1,
      totalObjectCount: 1
    },
    summary: placementWorkflowSummary
  };
  const flagworkSaveWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Flagwork hash tables, save keys, and source provenance.',
    diagnostics: [],
    id: 'flagworkSave',
    label: 'Flagwork and Save Inspectors'
  };
  const flagworkSaveWorkflow: FlagworkSaveWorkflow = {
    diagnostics: [],
    flags: [
      {
        category: 'system_flags',
        defaultValue: 'false',
        description: 'Flag hash 0x1122334455667788 uses save key 0x55667788.',
        flagId: 'system_flags:0000',
        hash: '0x1122334455667788',
        index: 0,
        kind: 'Flag',
        low32Key: '0x55667788',
        name: 'FE_TEST_FLAG',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/flagwork/system_flags.tbl',
          sourceLayer: 'base'
        },
        table: 'system_flags',
        valueKind: 'boolean'
      },
      {
        category: 'scene_work',
        defaultValue: '0',
        description: 'Work hash 0x99AABBCCDDEEFF00 uses save key 0xDDEEFF00.',
        flagId: 'scene_work:0000',
        hash: '0x99AABBCCDDEEFF00',
        index: 0,
        kind: 'Work',
        low32Key: '0xDDEEFF00',
        name: 'WK_SCENE_MAIN',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/flagwork/scene_work.tbl',
          sourceLayer: 'base'
        },
        table: 'scene_work',
        valueKind: 'integer'
      }
    ],
    saveBlocks: [
      {
        blockId: 'scene_work:0000:0xDDEEFF00',
        description: 'Save work key 0xDDEEFF00 is derived from WK_SCENE_MAIN.',
        hash: '0x99AABBCCDDEEFF00',
        key: '0xDDEEFF00',
        kind: 'Work',
        name: 'WK_SCENE_MAIN',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/flagwork/scene_work.tbl',
          sourceLayer: 'base'
        },
        valueKind: 'integer'
      }
    ],
    stats: {
      sourceFileCount: 2,
      totalFlagCount: 2,
      totalSaveBlockCount: 1
    },
    summary: flagworkSaveWorkflowSummary
  };
  const exeFsPatchWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'ExeFS main validation, patch anchors, segment hashes, and source provenance.',
    diagnostics: [],
    id: 'exefsPatches',
    label: 'ExeFS Patch Manager'
  };
  const exeFsPatchWorkflow: ExeFsPatchWorkflow = {
    checks: [
      {
        actual: 'text+0x7BC338',
        area: '.text',
        checkId: 'exefs-main-compatibility:patch-code-cave',
        expected: '12 zero bytes after text+0x7BC338',
        name: 'Patch code cave',
        notes: 'A code cave is available for small stubs.',
        offset: 'text+0x7BC338',
        patchId: 'exefs-main-compatibility',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'exefs/main',
          sourceLayer: 'base'
        },
        status: 'Pass'
      },
      {
        actual: '0',
        area: '.text',
        checkId: 'exefs-main-compatibility:royal-candy-immediate-scan',
        expected: '0 patched CMP immediates in vanilla main',
        name: 'Royal Candy immediate scan',
        notes: 'No obvious item-id 1128 CMP immediates were found in the known route registers.',
        offset: '',
        patchId: 'exefs-main-compatibility',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'exefs/main',
          sourceLayer: 'base'
        },
        status: 'Info'
      }
    ],
    diagnostics: [],
    patches: [
      {
        description:
          'Validates Sword/Shield ExeFS main structure, segment hashes, code-cave availability, and known patch anchors.',
        details: [
          'Build ID: ABABABABABABABABABABABABABABABABABABABABABABABABABABABABABABAB',
          'File size: 0x7DDAB0 bytes',
          'Checks: 26 total, 0 failing, 0 warnings'
        ],
        name: 'ExeFS main compatibility',
        patchId: 'exefs-main-compatibility',
        patchKind: 'NSO signature scan',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'exefs/main',
          sourceLayer: 'base'
        },
        status: 'available',
        targetFile: 'exefs/main'
      }
    ],
    segments: [
      {
        compressedSize: '0x7DDA90',
        decompressedSize: '0x7DDA90',
        fileOffset: 'file+0x100',
        hashStatus: 'Pass',
        memoryOffset: '0x0',
        name: '.text',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'exefs/main',
          sourceLayer: 'base'
        },
        segmentId: 'text',
        sha256: 'ABCD'
      }
    ],
    stats: {
      failCount: 0,
      passCount: 24,
      sourceFileCount: 1,
      totalCheckCount: 26,
      totalPatchCount: 1,
      warningCount: 0
    },
    summary: exeFsPatchWorkflowSummary
  };
  const royalCandyWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Curated batch workflow recipes, targets, steps, and source provenance.',
    diagnostics: [],
    id: 'royalCandy',
    label: 'Royal Candy Workflows'
  };
  const spreadsheetImportWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Spreadsheet import profiles, target workflows, columns, and source provenance.',
    diagnostics: [],
    id: 'spreadsheetImport',
    label: 'Spreadsheet Import Tooling'
  };

  return {
    applyChangePlan: (request) =>
      Promise.resolve({
        applyResult: {
          applyId: 'apply-1',
          diagnostics: [
            {
              message: getApplyMessage(
                request.changePlan.writes[0]?.targetRelativePath ?? '',
                request.session.pendingEdits[0]?.domain
              ),
              severity: 'info'
            }
          ],
          writtenFiles: request.changePlan.writes.map((write) => write.targetRelativePath)
        }
      }),
    createChangePlan: (request) =>
      Promise.resolve({
        changePlan: {
          canApply: true,
          diagnostics: [
            {
              message: 'Change plan preview contains 1 target file.',
              severity: 'info'
            }
          ],
          sessionId: request.session.sessionId,
          writes:
            request.session.pendingEdits[0]?.domain === 'workflow.text'
              ? [
                  {
                    reason: 'Apply pending Text edit: Set story #0 to "Hello there.".',
                    replacesExistingOutput: false,
                    sources: [
                      {
                        layer: 'base',
                        relativePath: 'romfs/bin/message/English/common/story.dat'
                      }
                    ],
                    targetRelativePath: 'romfs/bin/message/English/common/story.dat'
                  }
                ]
              : request.session.pendingEdits[0]?.domain === 'workflow.trainers'
                ? [
                    {
                      reason: 'Apply pending Trainers edit: Set Avery slot 1 level to 25.',
                      replacesExistingOutput: false,
                      sources: [
                        {
                          layer: 'base',
                          relativePath: 'romfs/bin/trainer/trainer_poke/trainer_010.bin'
                        }
                      ],
                      targetRelativePath: 'romfs/bin/trainer/trainer_poke/trainer_010.bin'
                    }
                  ]
                : request.session.pendingEdits[0]?.domain === 'workflow.shops'
                  ? [
                      {
                        reason: 'Apply pending Shops edit: Set Poke Mart slot 1 item ID to 2.',
                        replacesExistingOutput: false,
                        sources: [
                          {
                            layer: 'base',
                            relativePath: 'romfs/bin/app/shop/shop_data.bin'
                          }
                        ],
                        targetRelativePath: 'romfs/bin/app/shop/shop_data.bin'
                      }
                    ]
                  : request.session.pendingEdits[0]?.domain === 'workflow.encounters'
                    ? [
                        {
                          reason:
                            'Apply pending Encounters edit: Set Sword Symbol Zone 0x1122334455667788 Normal slot 2 probability to 40.',
                          replacesExistingOutput: false,
                          sources: [
                            {
                              layer: 'base',
                              relativePath:
                                'romfs/bin/archive/field/resident/data_table.gfpak'
                            }
                          ],
                          targetRelativePath:
                            'romfs/bin/archive/field/resident/data_table.gfpak'
                        }
                      ]
                    : request.session.pendingEdits[0]?.domain === 'workflow.raidRewards'
                      ? [
                          {
                            reason:
                              'Apply pending Raid Rewards edit: Set Drop 0xAABBCCDD00112233 slot 1 5-star value to 77.',
                            replacesExistingOutput: false,
                            sources: [
                              {
                                layer: 'base',
                                relativePath:
                                  'romfs/bin/archive/field/resident/data_table.gfpak'
                              }
                            ],
                            targetRelativePath:
                              'romfs/bin/archive/field/resident/data_table.gfpak'
                          }
                        ]
              : [
                  {
                    reason: 'Apply pending Items edit: Set Potion buy price to 450.',
                    replacesExistingOutput: false,
                    sources: [
                      {
                        layer: 'base',
                        relativePath: 'romfs/bin/pml/item/item.dat'
                      }
                    ],
                    targetRelativePath: 'romfs/bin/pml/item/item.dat'
                  }
                ]
        }
      }),
    listWorkflows: () =>
      Promise.resolve({
        workflows: [
          itemsWorkflow.summary,
          textWorkflowSummary,
          trainersWorkflowSummary,
          shopsWorkflowSummary,
          encountersWorkflowSummary,
          raidRewardsWorkflowSummary,
          placementWorkflowSummary,
          flagworkSaveWorkflowSummary,
          exeFsPatchWorkflowSummary,
          royalCandyWorkflowSummary,
          spreadsheetImportWorkflowSummary
        ]
      }),
    loadEncountersWorkflow: () =>
      Promise.resolve({
        workflow: encountersWorkflow
      }),
    loadFlagworkSaveWorkflow: () =>
      Promise.resolve({
        workflow: flagworkSaveWorkflow
      }),
    loadExeFsPatchWorkflow: () =>
      Promise.resolve({
        workflow: exeFsPatchWorkflow
      }),
    loadRoyalCandyWorkflow: () =>
      Promise.resolve({
        workflow: {
          diagnostics: [],
          stats: {
            sourceFileCount: 0,
            totalStepCount: 0,
            totalWorkflowCount: 0
          },
          summary: royalCandyWorkflowSummary,
          workflows: []
        }
      }),
    loadSpreadsheetImportWorkflow: () =>
      Promise.resolve({
        workflow: {
          diagnostics: [],
          profiles: [],
          stats: {
            sourceFileCount: 0,
            totalColumnCount: 0,
            totalProfileCount: 0
          },
          summary: spreadsheetImportWorkflowSummary
        }
      }),
    loadPlacementWorkflow: () =>
      Promise.resolve({
        workflow: placementWorkflow
      }),
    loadRaidRewardsWorkflow: () =>
      Promise.resolve({
        workflow: raidRewardsWorkflow
      }),
    loadItemsWorkflow: () =>
      Promise.resolve({
        workflow: itemsWorkflow
      }),
    loadTextWorkflow: () =>
      Promise.resolve({
        workflow: textWorkflow
      }),
    loadTrainersWorkflow: () =>
      Promise.resolve({
        workflow: trainersWorkflow
      }),
    loadShopsWorkflow: () =>
      Promise.resolve({
        workflow: shopsWorkflow
      }),
    openProject: () =>
      Promise.resolve({
        fileGraph,
        health,
        projectId: 'project-1'
      }),
    refreshFileGraph: () => Promise.resolve({ fileGraph }),
    startEditSession: () =>
      Promise.resolve({
        session: {
          hasPendingChanges: false,
          pendingEdits: [],
          sessionId: 'session-1'
        }
      }),
    updateItemField: (request) => {
      const fieldLabel = request.field === 'sellPrice' ? 'sell price' : 'buy price';

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.items',
              field: request.field,
              newValue: request.value,
              recordId: request.itemId.toString(),
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/pml/item/item.dat'
                }
              ],
              summary: `Set Potion ${fieldLabel} to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...itemsWorkflow,
          items: itemsWorkflow.items.map((item) => {
            if (item.itemId !== request.itemId) {
              return item;
            }

            const value = Number.parseInt(request.value, 10);
            switch (request.field) {
              case 'sellPrice':
                return { ...item, buyPrice: value * 2, sellPrice: value };
              case 'wattsPrice':
                return { ...item, wattsPrice: value };
              case 'alternatePrice':
                return { ...item, alternatePrice: value };
              default:
                return { ...item, buyPrice: value, sellPrice: Math.floor(value / 2) };
            }
          })
        }
      });
    },
    updateTextEntry: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.text',
              field: 'value',
              newValue: request.value,
              recordId: request.textKey,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/message/English/common/story.dat'
                }
              ],
              summary: `Set story #0 to "${request.value}".`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...textWorkflow,
          dialogueReferences: textWorkflow.dialogueReferences.map((reference) => ({
            ...reference,
            preview: request.value
          })),
          entries: textWorkflow.entries.map((entry) =>
            entry.textKey === request.textKey ? { ...entry, value: request.value } : entry
          )
        }
      }),
    updateTrainerField: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.trainers',
              field: request.field,
              newValue: request.value,
              recordId: request.slot === null ? request.trainerId.toString() : `${request.trainerId}:${request.slot}`,
              sources: [
                {
                  layer: 'base',
                  relativePath:
                    request.slot === null
                      ? 'romfs/bin/trainer/trainer_data/trainer_010.bin'
                      : 'romfs/bin/trainer/trainer_poke/trainer_010.bin'
                }
              ],
              summary:
                request.slot === null
                  ? `Set Avery ${request.field} to ${request.value}.`
                  : `Set Avery slot ${request.slot} level to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...trainersWorkflow,
          trainers: trainersWorkflow.trainers.map((trainer) =>
            trainer.trainerId === request.trainerId
              ? {
                  ...trainer,
                  team: trainer.team.map((pokemon) =>
                    pokemon.slot === request.slot
                      ? { ...pokemon, level: Number.parseInt(request.value, 10) }
                      : pokemon
                  )
                }
              : trainer
            )
        }
      }),
    updateShopInventoryItem: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.shops',
              field: request.field,
              newValue: request.value,
              recordId: `${request.shopId}#${request.slot}`,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/app/shop/shop_data.bin'
                }
              ],
              summary: `Set Poke Mart slot ${request.slot} item ID to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...shopsWorkflow,
          shops: shopsWorkflow.shops.map((shop) =>
            shop.shopId === request.shopId
              ? {
                  ...shop,
                  inventory: shop.inventory.map((item) =>
                    item.slot === request.slot
                      ? {
                          ...item,
                          itemId: Number.parseInt(request.value, 10),
                          itemName: `Item ${request.value}`,
                          price: 0
                        }
                      : item
                  )
                }
              : shop
          )
        }
      }),
    updateEncounterSlotField: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.encounters',
              field: request.field,
              newValue: request.value,
              recordId: `${request.tableId}#${request.slot}`,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/archive/field/resident/data_table.gfpak'
                }
              ],
              summary: `Set Sword Symbol Zone 0x1122334455667788 Normal slot ${request.slot} probability to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...encountersWorkflow,
          tables: encountersWorkflow.tables.map((table) =>
            table.tableId === request.tableId
              ? {
                  ...table,
                  slots: table.slots.map((slot) =>
                    slot.slot === request.slot
                      ? { ...slot, weight: Number.parseInt(request.value, 10) }
                      : slot
                  )
                }
              : table
          )
        }
      }),
    updateRaidRewardField: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.raidRewards',
              field: request.field,
              newValue: request.value,
              recordId: `${request.tableId}#${request.slot}`,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/archive/field/resident/data_table.gfpak'
                }
              ],
              summary: `Set Drop 0xAABBCCDD00112233 slot ${request.slot} 5-star value to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...raidRewardsWorkflow,
          tables: raidRewardsWorkflow.tables.map((table) =>
            table.tableId === request.tableId
              ? {
                  ...table,
                  rewards: table.rewards.map((reward) =>
                    reward.slot === request.slot
                      ? {
                          ...reward,
                          values: reward.values.map((value, index) =>
                            request.field === 'star5Value' && index === 4
                              ? Number.parseInt(request.value, 10)
                              : value
                          )
                        }
                      : reward
                  )
                }
              : table
          )
        }
      }),
    updatePlacementObjectField: (request) =>
      Promise.resolve({
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
                  layer: 'base',
                  relativePath: 'romfs/bin/archive/field/resident/placement.gfpak'
                }
              ],
              summary: `Set Field item: Potion ${request.field} to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...placementWorkflow,
          objects: placementWorkflow.objects.map((placedObject) =>
            placedObject.objectId === request.objectId
              ? { ...placedObject, quantity: Number.parseInt(request.value, 10) }
              : placedObject
          )
        }
      }),
    validateEditSession: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            field: request.session.pendingEdits[0]?.field ?? 'value',
            message: getValidationMessage(request.session.pendingEdits[0]?.domain),
            severity: 'info'
          }
        ],
        isValid: true,
        session: request.session
      }),
    validateProject: () => Promise.resolve({ health }),
    ...overrides
  };
}

function getApplyMessage(targetRelativePath: string, domain: string | undefined) {
  if (targetRelativePath.includes('/message/')) {
    return 'Applied Text change plan to the configured LayeredFS output root.';
  }

  if (targetRelativePath.includes('/trainer/')) {
    return 'Applied Trainers change plan to the configured LayeredFS output root.';
  }

  if (targetRelativePath.includes('/shop/')) {
    return 'Applied Shops change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.raidRewards') {
    return 'Applied Raid Rewards change plan to the configured LayeredFS output root.';
  }

  if (targetRelativePath.includes('/archive/field/resident/')) {
    return 'Applied Encounters change plan to the configured LayeredFS output root.';
  }

  return 'Applied Items change plan to the configured LayeredFS output root.';
}

function getValidationMessage(domain: string | undefined) {
  switch (domain) {
    case 'workflow.text':
      return 'Pending text change is valid.';
    case 'workflow.trainers':
      return 'Pending trainer change is valid.';
    case 'workflow.shops':
      return 'Pending shop change is valid.';
    case 'workflow.encounters':
      return 'Pending encounter change is valid.';
    case 'workflow.raidRewards':
      return 'Pending raid reward change is valid.';
    default:
      return 'Pending item change is valid.';
  }
}
