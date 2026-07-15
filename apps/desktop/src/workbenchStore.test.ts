/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import { type PokemonWorkflow } from './bridge/contracts';
import { useWorkbenchStore } from './workbenchStore';

describe('workbench store', () => {
  it('preserves item search and skips the placeholder item when selecting after refresh', () => {
    useWorkbenchStore.setState({
      itemSearchText: 'Potion',
      selectedItemId: 0
    });

    useWorkbenchStore.getState().setItemsWorkflow({
      items: [{ itemId: 0 }, { itemId: 2 }]
    } as never);

    expect(useWorkbenchStore.getState().itemSearchText).toBe('Potion');
    expect(useWorkbenchStore.getState().selectedItemId).toBe(2);
  });

  it('preserves Text search and selection when committing a refreshed workflow', () => {
    useWorkbenchStore.setState({
      selectedTextKey: 'romfs/bin/message/English/common/story.dat#1',
      textSearchText: 'Pikachu'
    });

    useWorkbenchStore.getState().setTextWorkflow({
      entries: [
        { textKey: 'romfs/bin/message/English/common/story.dat#0' },
        { textKey: 'romfs/bin/message/English/common/story.dat#1' }
      ]
    } as never);

    expect(useWorkbenchStore.getState().textSearchText).toBe('Pikachu');
    expect(useWorkbenchStore.getState().selectedTextKey).toBe(
      'romfs/bin/message/English/common/story.dat#1'
    );
  });

  it('invalidates the committed project session when a project path changes', () => {
    useWorkbenchStore.setState({
      activeSection: 'pokemon',
      applyResult: { writtenFiles: ['old-output/romfs/bin/personal'] },
      changePlan: { sessionId: 'old-session' },
      draftPaths: {
        baseExeFsPath: 'old-exefs',
        baseRomFsPath: 'old-romfs',
        outputRootPath: 'old-output',
        pokemonLegendsZASupportFolderPath: '',
        saveFilePath: '',
        scarletVioletSupportFolderPath: '',
        selectedGame: 'sword'
      },
      editSession: { sessionId: 'old-session' },
      openProject: { projectId: 'old-project' },
      pokemonWorkflow: { pokemon: [{ personalId: 1 }] },
      projectStatus: 'open',
      workflows: [{ id: 'pokemon' }]
    } as never);

    useWorkbenchStore.getState().setDraftPath('outputRootPath', 'new-output');

    const state = useWorkbenchStore.getState();
    expect(state.draftPaths.outputRootPath).toBe('new-output');
    expect(state.activeSection).toBe('health');
    expect(state.openProject).toBeNull();
    expect(state.projectStatus).toBe('idle');
    expect(state.workflows).toEqual([]);
    expect(state.pokemonWorkflow).toBeNull();
    expect(state.editSession).toBeNull();
    expect(state.changePlan).toBeNull();
    expect(state.applyResult).toBeNull();
  });

  it('preserves the committed project session when a path value does not change', () => {
    const openProject = { projectId: 'current-project' };
    useWorkbenchStore.setState({
      draftPaths: {
        ...useWorkbenchStore.getState().draftPaths,
        outputRootPath: 'current-output'
      },
      openProject,
      projectStatus: 'open'
    } as never);

    useWorkbenchStore.getState().setDraftPath('outputRootPath', 'current-output');

    expect(useWorkbenchStore.getState().openProject).toBe(openProject);
    expect(useWorkbenchStore.getState().projectStatus).toBe('open');
  });

  it('clears every store-owned workflow when the project session resets', () => {
    const state = useWorkbenchStore.getState();
    const workflowKeys = Object.keys(state).filter(
      (key) => key.endsWith('Workflow') && !key.startsWith('set')
    );
    const loadedMarker = { loadedFromOldOutputRoot: true };
    const loadedWorkflows = Object.fromEntries(
      workflowKeys.map((key) => [key, loadedMarker])
    );

    useWorkbenchStore.setState(loadedWorkflows as never);
    useWorkbenchStore.getState().resetProjectSession();

    for (const key of workflowKeys) {
      expect(useWorkbenchStore.getState()[key as keyof ReturnType<typeof useWorkbenchStore.getState>])
        .toBeNull();
    }
  });

  it('clears loaded workflows without dropping the validated project', () => {
    const state = useWorkbenchStore.getState();
    const workflowKeys = Object.keys(state).filter(
      (key) => key.endsWith('Workflow') && !key.startsWith('set')
    );
    const loadedMarker = { loadedFromValidatedProject: true };
    const loadedWorkflows = Object.fromEntries(
      workflowKeys.map((key) => [key, loadedMarker])
    );
    const openProject = { projectId: 'validated-project' };
    const workflows = [{ id: 'pokemon' }];

    useWorkbenchStore.setState({
      ...loadedWorkflows,
      activeSection: 'pokemon',
      openProject,
      projectStatus: 'open',
      workflows
    } as never);
    useWorkbenchStore.getState().resetLoadedWorkflowData();

    expect(useWorkbenchStore.getState().activeSection).toBe('pokemon');
    expect(useWorkbenchStore.getState().openProject).toBe(openProject);
    expect(useWorkbenchStore.getState().projectStatus).toBe('open');
    expect(useWorkbenchStore.getState().workflows).toBe(workflows);
    for (const key of workflowKeys) {
      expect(useWorkbenchStore.getState()[key as keyof ReturnType<typeof useWorkbenchStore.getState>])
        .toBeNull();
    }
  });

  it('selectively evicts payloads without losing pending edits or unrelated workflows', () => {
    const editSession = {
      hasPendingChanges: true,
      pendingEdits: [{ domain: 'workflow.pokemon' }],
      sessionId: 'pending-session'
    };
    const changePlan = { sessionId: 'pending-session' };
    const itemsWorkflow = { items: [{ itemId: 1 }] };

    useWorkbenchStore.setState({
      changePlan,
      editSession,
      itemsWorkflow,
      pokemonWorkflow: { pokemon: [{ personalId: 1 }] },
      spreadsheetImportPreview: { rows: Array.from({ length: 100 }, (_, index) => index) },
      spreadsheetImportWorkflow: { profiles: [] }
    } as never);

    useWorkbenchStore
      .getState()
      .evictLoadedWorkflowSections(['pokemon', 'spreadsheetImport']);

    const state = useWorkbenchStore.getState();
    expect(state.pokemonWorkflow).toBeNull();
    expect(state.spreadsheetImportWorkflow).toBeNull();
    expect(state.spreadsheetImportPreview).toBeNull();
    expect(state.itemsWorkflow).toBe(itemsWorkflow);
    expect(state.editSession).toBe(editSession);
    expect(state.changePlan).toBe(changePlan);
  });

  it('preserves Pokemon search and defaults selection to the first real Pokemon instead of Egg', () => {
    useWorkbenchStore.setState({
      activeSection: 'health',
      pokemonSearchText: 'grass',
      pokemonWorkflow: null,
      selectedPokemonPersonalId: null
    });

    useWorkbenchStore.getState().setPokemonWorkflow({
      diagnostics: [],
      editableFields: [],
      evolutionMethodOptions: [],
      learnsetMoveOptions: [],
      pokemon: [
        { name: 'Egg', personalId: 0 },
        { name: 'Bulbasaur', personalId: 1 }
      ],
      stats: {
        presentPokemonCount: 1,
        totalLearnsetMoveCount: 1,
        totalPokemonCount: 2
      },
      summary: {
        availability: 'available',
        description: 'Pokemon personal stats, forms, evolutions, learnsets, and source provenance.',
        diagnostics: [],
        id: 'pokemon',
        label: 'Pokemon'
      }
    } as unknown as PokemonWorkflow);

    expect(useWorkbenchStore.getState().pokemonSearchText).toBe('grass');
    expect(useWorkbenchStore.getState().selectedPokemonPersonalId).toBe(1);
  });
});
