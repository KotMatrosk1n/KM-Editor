/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from './App';
import {
  type ItemsWorkflow,
  type ProjectFileGraph,
  type ProjectHealth,
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
      itemSearchText: '',
      itemsWorkflow: null,
      openProject: null,
      projectStatus: 'idle',
      selectedItemId: null,
      selectedTextKey: null,
      selectedTrainerId: null,
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
    description: 'Shop inventories, prices, stock limits, and source provenance.',
    diagnostics: [],
    id: 'shops',
    label: 'Shops'
  };
  const encountersWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Encounter tables, wild slots, levels, weather, and source provenance.',
    diagnostics: [],
    id: 'encounters',
    label: 'Encounters and Wild Data'
  };
  const raidRewardsWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Raid reward tables, den ranks, item quantities, and source provenance.',
    diagnostics: [],
    id: 'raidRewards',
    label: 'Raid Rewards'
  };
  const placementWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Placed objects, map coordinates, script links, and source provenance.',
    diagnostics: [],
    id: 'placement',
    label: 'Placement'
  };
  const flagworkSaveWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Game flags, save blocks, inspector metadata, and source provenance.',
    diagnostics: [],
    id: 'flagworkSave',
    label: 'Flagwork and Save Inspectors'
  };
  const exeFsPatchWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'ExeFS patch definitions, target files, statuses, and source provenance.',
    diagnostics: [],
    id: 'exefsPatches',
    label: 'ExeFS Patch Manager'
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
              message: getApplyMessage(request.changePlan.writes[0]?.targetRelativePath ?? ''),
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
        workflow: {
          diagnostics: [],
          stats: {
            sourceFileCount: 0,
            totalSlotCount: 0,
            totalTableCount: 0
          },
          summary: encountersWorkflowSummary,
          tables: []
        }
      }),
    loadFlagworkSaveWorkflow: () =>
      Promise.resolve({
        workflow: {
          diagnostics: [],
          flags: [],
          saveBlocks: [],
          stats: {
            sourceFileCount: 0,
            totalFlagCount: 0,
            totalSaveBlockCount: 0
          },
          summary: flagworkSaveWorkflowSummary
        }
      }),
    loadExeFsPatchWorkflow: () =>
      Promise.resolve({
        workflow: {
          diagnostics: [],
          patches: [],
          stats: {
            sourceFileCount: 0,
            totalPatchCount: 0
          },
          summary: exeFsPatchWorkflowSummary
        }
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
        workflow: {
          diagnostics: [],
          objects: [],
          stats: {
            sourceFileCount: 0,
            totalObjectCount: 0
          },
          summary: placementWorkflowSummary
        }
      }),
    loadRaidRewardsWorkflow: () =>
      Promise.resolve({
        workflow: {
          diagnostics: [],
          stats: {
            sourceFileCount: 0,
            totalRewardItemCount: 0,
            totalTableCount: 0
          },
          summary: raidRewardsWorkflowSummary,
          tables: []
        }
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
        workflow: {
          diagnostics: [],
          shops: [],
          stats: {
            sourceFileCount: 0,
            totalInventoryItemCount: 0,
            totalShopCount: 0
          },
          summary: shopsWorkflowSummary
        }
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

function getApplyMessage(targetRelativePath: string) {
  if (targetRelativePath.includes('/message/')) {
    return 'Applied Text change plan to the configured LayeredFS output root.';
  }

  if (targetRelativePath.includes('/trainer/')) {
    return 'Applied Trainers change plan to the configured LayeredFS output root.';
  }

  return 'Applied Items change plan to the configured LayeredFS output root.';
}

function getValidationMessage(domain: string | undefined) {
  switch (domain) {
    case 'workflow.text':
      return 'Pending text change is valid.';
    case 'workflow.trainers':
      return 'Pending trainer change is valid.';
    default:
      return 'Pending item change is valid.';
  }
}
