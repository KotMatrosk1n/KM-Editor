/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { App } from './App';
import { type ProjectBridge } from './bridge/projectBridge';
import { createMockProjectBridge } from './testSupport/appTestFixtures';
import { useWorkbenchStore } from './workbenchStore';

async function openValidatedZaProject(bridge: ProjectBridge) {
  const user = userEvent.setup();

  render(<App bridge={bridge} />);

  await user.click(screen.getByRole('button', { name: 'Pokemon Legends Z-A' }));
  await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
  await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
  await user.type(screen.getByLabelText('Output Root'), 'output');
  await user.click(screen.getByRole('button', { name: 'Validate Paths' }));

  return user;
}

describe('Text and Gift Pokemon UI regressions', () => {
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

  it('preserves filtered Text state and rejected variable drafts while paging results', async () => {
    const bridge = createMockProjectBridge({}, true);
    const originalLoadTextWorkflow = bridge.loadTextWorkflow.bind(bridge);
    const variableTextKey = 'romfs/bin/message/English/common/story.dat#1';
    const variableValue = '[VAR 0100] Pikachu';
    let loadedTextWorkflow: Awaited<
      ReturnType<ProjectBridge['loadTextWorkflow']>
    >['workflow'] | null = null;
    const loadTextWorkflow = vi.fn(async (request) => {
      const response = await originalLoadTextWorkflow(request);
      const sourceEntry = response.workflow.entries[0]!;
      const sourceReference = response.workflow.dialogueReferences[0]!;
      loadedTextWorkflow = {
        ...response.workflow,
        dialogueReferences: [
          sourceReference,
          {
            ...sourceReference,
            dialogueId: 'common/story:1',
            label: 'story #1',
            preview: variableValue,
            textId: 1
          }
        ],
        entries: [
          sourceEntry,
          {
            ...sourceEntry,
            label: 'story #1',
            lineIndex: 1,
            textId: 1,
            textKey: variableTextKey,
            value: variableValue
          }
        ],
        stats: {
          ...response.workflow.stats,
          dialogueReferenceCount: 2,
          totalTextEntryCount: 2
        }
      };

      return { workflow: loadedTextWorkflow };
    });
    const updateTextEntry = vi.fn(async (request) => {
      const workflow = loadedTextWorkflow;
      if (!request.session || !workflow) {
        throw new Error('Text workflow and edit session must be loaded before staging.');
      }

      return {
        diagnostics: [
          {
            domain: 'workflow.text',
            field: 'value',
            message: 'Rejected text edit.',
            severity: 'error' as const
          }
        ],
        session: {
          ...request.session,
          sessionId: 'rejected-session'
        },
        workflow: {
          ...workflow,
          entries: workflow.entries.map((entry) =>
            entry.textKey === request.textKey ? { ...entry, value: 'Server fallback.' } : entry
          )
        }
      };
    });
    bridge.loadTextWorkflow = loadTextWorkflow;
    bridge.updateTextEntry = updateTextEntry;
    const user = await openValidatedZaProject(bridge);

    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Text' }));

    const searchInput = await screen.findByRole('searchbox', { name: 'Search text entries' });
    await user.type(searchInput, 'Pikachu');

    await waitFor(() => {
      expect(loadTextWorkflow).toHaveBeenCalledWith(
        expect.objectContaining({
          query: {
            limit: 500,
            offset: 0,
            searchText: 'Pikachu'
          }
        })
      );
      expect(searchInput).toHaveValue('Pikachu');
    });

    const textarea = screen.getByLabelText('Text value');
    expect(textarea).toHaveValue(variableValue);

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    await waitFor(() => expect(textarea).toBeEnabled());

    const draftValue = `${variableValue} updated`;
    await user.clear(textarea);
    await user.paste(draftValue);
    const stageButton = screen.getByRole('button', { name: 'Stage' });
    expect(stageButton).toBeEnabled();
    await user.click(stageButton);

    await waitFor(() => {
      expect(updateTextEntry).toHaveBeenCalledWith(
        expect.objectContaining({
          textKey: variableTextKey,
          value: draftValue
        })
      );
      expect(stageButton).toBeEnabled();
    });

    const state = useWorkbenchStore.getState();
    expect(textarea).toHaveValue(draftValue);
    expect(searchInput).toHaveValue('Pikachu');
    expect(state.editSession?.sessionId).toBe('session-1');
    expect(
      state.textWorkflow?.entries.find((entry) => entry.textKey === variableTextKey)?.value
    ).toBe(variableValue);
    expect(state.editValidationDiagnostics).toEqual([
      expect.objectContaining({ message: 'Rejected text edit.', severity: 'error' })
    ]);
  });

  it('uses one-based Gift numbers without repeating the species in the table label', async () => {
    const user = await openValidatedZaProject(createMockProjectBridge({}, true));

    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    await user.click(screen.getByRole('button', { name: 'Gift Pokemon' }));

    const table = await screen.findByRole('table', { name: 'Gift Pokemon' });
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((column) => column.textContent)
    ).toEqual(['Gift #', 'Species', 'Level', 'IVs', 'Source']);
    expect(within(table).getByRole('cell', { name: '#1' })).toBeInTheDocument();
    expect(within(table).queryByText('Gift 001: Bulbasaur Lv. 5')).not.toBeInTheDocument();
    expect(screen.getByText('Gift #1 | Lv. 5')).toBeInTheDocument();
  });
});
