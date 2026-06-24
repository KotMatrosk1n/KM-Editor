/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { App } from '../../App';
import { createMockProjectBridge } from '../../testSupport/appTestFixtures';
import { useWorkbenchStore } from '../../workbenchStore';

const tauriEventMock = vi.hoisted(() => ({
  listen: vi.fn(() => Promise.resolve(() => undefined))
}));

vi.mock('@tauri-apps/api/event', () => ({
  listen: tauriEventMock.listen
}));

describe('PokemonSection', () => {
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
        pokemonLegendsZASupportFolderPath: '',
        scarletVioletSupportFolderPath: '',
        selectedGame: 'scarlet'
      },
      editSession: null,
      pokemonSearchText: '',
      pokemonWorkflow: null,
      projectStatus: 'idle',
      selectedPokemonPersonalId: null,
      workflows: []
    });
  });

  it('keeps S/V Pokemon diagnostics outside the main Pokemon panel', async () => {
    const user = userEvent.setup();
    const { container } = render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(await screen.findByRole('button', { name: 'Pokemon' }));

    await screen.findByRole('table', { name: 'Pokemon' });
    const diagnosticsHeading = screen.getByRole('heading', { level: 2, name: 'Diagnostics' });
    const pokemonSection = screen
      .getByRole('heading', { level: 2, name: 'Pokemon' })
      .closest('section');

    expect(pokemonSection).toHaveClass('sv-pokemon-section');
    expect(container.querySelector('.swsh-pokemon-section')).toBeNull();
    expect(pokemonSection?.querySelector('.sv-pokemon-diagnostics-row')).toBeNull();
    expect(container.querySelector('.sv-pokemon-diagnostics-row')).not.toBeNull();
    expect(container.querySelector('.swsh-pokemon-diagnostics-row')).toBeNull();
    expect(diagnosticsHeading).toBeInTheDocument();
  });

  it('mounts SwSh Pokemon on the SwSh UI surface only', async () => {
    const user = userEvent.setup();

    useWorkbenchStore.setState((state) => ({
      draftPaths: {
        ...state.draftPaths,
        selectedGame: 'sword'
      }
    }));

    const { container } = render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(await screen.findByRole('button', { name: 'Pokemon' }));

    await screen.findByRole('table', { name: 'Pokemon' });
    const pokemonSection = screen
      .getByRole('heading', { level: 2, name: 'Pokemon' })
      .closest('section');

    expect(pokemonSection).toHaveClass('swsh-pokemon-section');
    expect(container.querySelector('.sv-pokemon-section')).toBeNull();
    expect(container.querySelector('.swsh-pokemon-diagnostics-row')).not.toBeNull();
    expect(container.querySelector('.sv-pokemon-diagnostics-row')).toBeNull();
  });
});
