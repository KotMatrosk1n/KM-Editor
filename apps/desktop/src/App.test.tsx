/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from './App';
import {
  type ItemsWorkflow,
  type ProjectFileGraph,
  type ProjectHealth,
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
    expect(screen.getByText('romfs/kmeditor/items.readmodel.json')).toBeInTheDocument();
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
    expect(screen.getAllByText('romfs/kmeditor/items.readmodel.json').length).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(await screen.findByRole('heading', { name: 'Apply Result' })).toBeInTheDocument();
    expect(screen.getByText('Applied Items change plan to the configured output root.')).toBeInTheDocument();
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
        maximumValue: 999_999,
        minimumValue: 0,
        valueKind: 'integer'
      }
    ],
    items: [
      {
        buyPrice: 300,
        category: 'Medicine',
        itemId: 1,
        name: 'Potion',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/kmeditor/items.readmodel.json',
          sourceLayer: 'base'
        },
        sellPrice: 150
      },
      {
        buyPrice: 200,
        category: 'Medicine',
        itemId: 2,
        name: 'Antidote',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/kmeditor/items.readmodel.json',
          sourceLayer: 'base'
        },
        sellPrice: 100
      }
    ],
    stats: {
      sourceFileCount: 1,
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
  const trainersWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Trainer parties, classes, battle types, and source provenance.',
    diagnostics: [],
    id: 'trainers',
    label: 'Trainers'
  };

  return {
    applyChangePlan: (request) =>
      Promise.resolve({
        applyResult: {
          applyId: 'apply-1',
          diagnostics: [
            {
              message: 'Applied Items change plan to the configured output root.',
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
          writes: [
            {
              reason: 'Apply pending Items edit: Set Potion buy price to 450.',
              replacesExistingOutput: false,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/kmeditor/items.readmodel.json'
                }
              ],
              targetRelativePath: 'romfs/kmeditor/items.readmodel.json'
            }
          ]
        }
      }),
    listWorkflows: () =>
      Promise.resolve({
        workflows: [itemsWorkflow.summary, textWorkflowSummary, trainersWorkflowSummary]
      }),
    loadItemsWorkflow: () =>
      Promise.resolve({
        workflow: itemsWorkflow
      }),
    loadTextWorkflow: () =>
      Promise.resolve({
        workflow: {
          diagnostics: [],
          dialogueReferences: [],
          entries: [],
          stats: {
            dialogueReferenceCount: 0,
            sourceFileCount: 0,
            totalTextEntryCount: 0
          },
          summary: textWorkflowSummary
        }
      }),
    loadTrainersWorkflow: () =>
      Promise.resolve({
        workflow: {
          diagnostics: [],
          stats: {
            sourceFileCount: 0,
            totalPokemonCount: 0,
            totalTrainerCount: 0
          },
          summary: trainersWorkflowSummary,
          trainers: []
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
                  relativePath: 'romfs/kmeditor/items.readmodel.json'
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

            return request.field === 'sellPrice'
              ? { ...item, sellPrice: Number.parseInt(request.value, 10) }
              : { ...item, buyPrice: Number.parseInt(request.value, 10) };
          })
        }
      });
    },
    validateEditSession: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            field: 'buyPrice',
            message: 'Pending item change is valid.',
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
