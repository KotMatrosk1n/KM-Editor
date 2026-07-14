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

  it('reloads Pokemon Legends Z-A text results with the debounced search query', async () => {
    const bridge = createMockProjectBridge({}, true);
    const loadTextWorkflow = vi.fn(bridge.loadTextWorkflow);
    bridge.loadTextWorkflow = loadTextWorkflow;
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
    });
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
