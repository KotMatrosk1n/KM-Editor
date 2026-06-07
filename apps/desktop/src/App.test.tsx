/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from './App';
import { type ItemsWorkflow, type ProjectFileGraph, type ProjectHealth } from './bridge/contracts';
import { type ProjectBridge } from './bridge/projectBridge';
import { useWorkbenchStore } from './workbenchStore';

describe('App', () => {
  beforeEach(() => {
    useWorkbenchStore.setState({
      activeSection: 'health',
      draftPaths: {
        baseExeFsPath: '',
        baseRomFsPath: '',
        outputRootPath: ''
      },
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
    expect(screen.getByText('Read-only')).toBeInTheDocument();
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

function createMockProjectBridge(overrides: Partial<ProjectBridge> = {}): ProjectBridge {
  const health: ProjectHealth = {
    canOpenEditableWorkflows: false,
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
        path: null,
        role: 'outputRoot',
        status: 'notSet'
      }
    ],
    state: 'readOnlyReady'
  };
  const fileGraph: ProjectFileGraph = {
    entries: [],
    summary: health.fileGraph
  };
  const itemsWorkflow: ItemsWorkflow = {
    diagnostics: [],
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
      availability: 'readOnly',
      description: 'Item records, names, and source provenance.',
      diagnostics: [],
      id: 'items',
      label: 'Items'
    }
  };

  return {
    listWorkflows: () =>
      Promise.resolve({
        workflows: [itemsWorkflow.summary]
      }),
    loadItemsWorkflow: () =>
      Promise.resolve({
        workflow: itemsWorkflow
      }),
    openProject: () =>
      Promise.resolve({
        fileGraph,
        health,
        projectId: 'project-1'
      }),
    refreshFileGraph: () => Promise.resolve({ fileGraph }),
    validateProject: () => Promise.resolve({ health }),
    ...overrides
  };
}
