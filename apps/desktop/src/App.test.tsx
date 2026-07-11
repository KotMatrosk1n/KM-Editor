/* SPDX-License-Identifier: GPL-3.0-only */

import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { App } from './App';
import {
  createHealthForValidatedPaths,
  createMockProjectBridge
} from './testSupport/appTestFixtures';
import { LocalizationProvider } from './localization';
import { useWorkbenchStore } from './workbenchStore';

const tauriEventMock = vi.hoisted(() => {
  const listeners: Record<string, Array<() => void>> = {};

  return {
    listen: vi.fn((eventName: string, handler: () => void) => {
      listeners[eventName] = [...(listeners[eventName] ?? []), handler];

      return Promise.resolve(() => {
        listeners[eventName] = (listeners[eventName] ?? []).filter(
          (candidate) => candidate !== handler
        );
      });
    }),
    listeners
  };
});

vi.mock('@tauri-apps/api/event', () => ({
  listen: tauriEventMock.listen
}));

describe('App', () => {
  beforeEach(() => {
    window.localStorage.clear();
    tauriEventMock.listen.mockClear();
    for (const eventName of Object.keys(tauriEventMock.listeners)) {
      delete tauriEventMock.listeners[eventName];
    }
    useWorkbenchStore.setState({
      activeSection: 'health',
      applyResult: null,
      changePlan: null,
      draftPaths: {
        baseExeFsPath: '',
        baseRomFsPath: '',
        outputRootPath: '',
        saveFilePath: '',
        pokemonLegendsZASupportFolderPath: '',
        scarletVioletSupportFolderPath: '',
        selectedGame: 'sword'
      },
      editSession: null,
      editValidationDiagnostics: [],
      itemsWorkflow: null,
      openProject: null,
      projectStatus: 'idle',
      selectedItemId: null,
      workflows: []
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders the project workbench shell', () => {
    render(<App />);

    expect(screen.getByText('KM Editor')).toBeInTheDocument();
    expect(screen.getByText('Pokemon Sword Editor')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Project Setup' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Project Paths' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Validate Paths' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Editors' })).not.toBeInTheDocument();
  });

  it('asks for the game before showing the workbench', async () => {
    const user = userEvent.setup();
    useWorkbenchStore.setState({
      draftPaths: {
        baseExeFsPath: '',
        baseRomFsPath: '',
        outputRootPath: '',
        saveFilePath: '',
        pokemonLegendsZASupportFolderPath: '',
        scarletVioletSupportFolderPath: '',
        selectedGame: null
      }
    });

    render(<App />);

    expect(
      screen.getByRole('heading', { name: 'Which game are you using?' })
    ).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Project Setup' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Pokemon Scarlet' }));

    expect(screen.getByRole('heading', { name: 'Project Setup' })).toBeInTheDocument();
    expect(screen.getByText('Pokemon Scarlet Editor')).toBeInTheDocument();
  });

  it('shows busy feedback while validating project paths', async () => {
    const user = userEvent.setup();
    let resolveValidateProject!: () => void;
    const validateProject = vi.fn(
      (request: Parameters<ReturnType<typeof createMockProjectBridge>['validateProject']>[0]) =>
        new Promise<Awaited<ReturnType<ReturnType<typeof createMockProjectBridge>['validateProject']>>>(
          (resolve) => {
            resolveValidateProject = () => {
              resolve({
                health: createHealthForValidatedPaths(
                  request.paths.baseRomFsPath ?? '',
                  request.paths.baseExeFsPath ?? '',
                  request.paths.outputRootPath ?? '',
                  request.paths.saveFilePath
                )
              });
            };
          }
        )
    );

    render(<App bridge={createMockProjectBridge({ validateProject })} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));

    const validatingButton = await screen.findByRole('button', { name: 'Validating' });
    expect(validatingButton).toHaveAttribute('aria-busy', 'true');
    expect(validatingButton.querySelector('.button-busy-icon')).not.toBeNull();

    await act(async () => {
      resolveValidateProject();
    });

    expect(await screen.findByRole('button', { name: 'Editors' })).toBeInTheDocument();
  });

  it('shows the renamed workflow categories in sidebar order', async () => {
    const user = userEvent.setup();
    const bridge = createMockProjectBridge({}, true);
    const originalLoadPokemonWorkflow = bridge.loadPokemonWorkflow;
    const originalLoadTrainersWorkflow = bridge.loadTrainersWorkflow;
    const loadPokemonWorkflow = vi.fn(
      async (request: Parameters<typeof originalLoadPokemonWorkflow>[0]) => {
        const response = await originalLoadPokemonWorkflow(request);
        const localizedName =
          request.paths.gameTextLanguage === 'zh' ? '中文宝可梦' : 'Bulbasaur';

        return {
          workflow: {
            ...response.workflow,
            pokemon: response.workflow.pokemon.map((pokemon, index) =>
              index === 0 ? { ...pokemon, name: localizedName } : pokemon
            )
          }
        };
      }
    );
    const loadTrainersWorkflow = vi.fn(
      async (request: Parameters<typeof originalLoadTrainersWorkflow>[0]) => {
        const response = await originalLoadTrainersWorkflow(request);
        const localizedName =
          request.paths.gameTextLanguage === 'zh' ? '中文训练家' : 'Avery';

        return {
          workflow: {
            ...response.workflow,
            trainers: response.workflow.trainers.map((trainer, index) =>
              index === 0 ? { ...trainer, name: localizedName } : trainer
            )
          }
        };
      }
    );
    bridge.loadPokemonWorkflow = loadPokemonWorkflow;
    bridge.loadTrainersWorkflow = loadTrainersWorkflow;

    render(
      <LocalizationProvider>
        <App bridge={bridge} />
      </LocalizationProvider>
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));

    const navigation = screen.getByRole('navigation', { name: 'Workspace' });
    const topLevelLabels = within(navigation)
      .getAllByRole('button')
      .filter((button) => !button.classList.contains('nav-child-button'))
      .map((button) => button.textContent);

    expect(topLevelLabels).toEqual([
      'Project Setup',
      'Viewers',
      'Editors',
      'Encounters & Pokemon Sources',
      'Economy',
      'Tools',
      'Hooks',
      'Advanced Editors',
      'Changes',
      'Settings'
    ]);

    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(within(navigation).getByRole('button', { name: 'Pokemon' }));
    await waitFor(() => expect(loadPokemonWorkflow).toHaveBeenCalled());
    expect(loadPokemonWorkflow.mock.calls.at(-1)?.[0].paths.gameTextLanguage).toBe('en');
    await waitFor(() => expect(screen.getAllByText('Bulbasaur').length).toBeGreaterThan(0));

    await user.click(screen.getByRole('button', { name: 'Trainers' }));
    await waitFor(() => expect(loadTrainersWorkflow).toHaveBeenCalled());
    expect(loadTrainersWorkflow.mock.calls.at(-1)?.[0].paths.gameTextLanguage).toBe('en');
    await waitFor(() => expect(screen.getAllByText('Avery').length).toBeGreaterThan(0));
    const trainerSearch = screen.getByPlaceholderText('Search trainers');
    await user.type(trainerSearch, 'Trainer 10');
    expect(screen.getAllByText('Avery').length).toBeGreaterThan(0);
    await user.clear(trainerSearch);

    await user.click(screen.getByRole('button', { name: 'Settings' }));
    await user.click(screen.getByRole('radio', { name: /Simplified Chinese|简体中文/ }));

    const pokemonCallsBeforeChineseReload = loadPokemonWorkflow.mock.calls.length;
    await user.click(within(navigation).getByRole('button', { name: /^(Pokemon|宝可梦)$/ }));
    await waitFor(() =>
      expect(loadPokemonWorkflow).toHaveBeenCalledTimes(pokemonCallsBeforeChineseReload + 1)
    );
    expect(loadPokemonWorkflow.mock.calls.at(-1)?.[0].paths.gameTextLanguage).toBe('zh');
    await waitFor(() => expect(screen.getAllByText('中文宝可梦').length).toBeGreaterThan(0));
    expect(screen.queryByText('Bulbasaur')).not.toBeInTheDocument();

    const trainerCallsBeforeChineseReload = loadTrainersWorkflow.mock.calls.length;
    await user.click(within(navigation).getByRole('button', { name: /^(Trainers|训练家)$/ }));
    await waitFor(() =>
      expect(loadTrainersWorkflow).toHaveBeenCalledTimes(trainerCallsBeforeChineseReload + 1)
    );
    expect(loadTrainersWorkflow.mock.calls.at(-1)?.[0].paths.gameTextLanguage).toBe('zh');
    await waitFor(() => expect(screen.getAllByText('中文训练家').length).toBeGreaterThan(0));
    expect(screen.queryByText('Avery')).not.toBeInTheDocument();

    await user.click(within(navigation).getByRole('button', { name: /^(Settings|设置)$/ }));
    await user.click(await screen.findByRole('radio', { name: /English|英语/ }));

    const pokemonCallsBeforeEnglishReload = loadPokemonWorkflow.mock.calls.length;
    await user.click(within(navigation).getByRole('button', { name: /^(Pokemon|宝可梦)$/ }));
    await waitFor(() =>
      expect(loadPokemonWorkflow).toHaveBeenCalledTimes(pokemonCallsBeforeEnglishReload + 1)
    );
    expect(loadPokemonWorkflow.mock.calls.at(-1)?.[0].paths.gameTextLanguage).toBe('en');
    await waitFor(() => expect(screen.getAllByText('Bulbasaur').length).toBeGreaterThan(0));
    expect(screen.queryByText('中文宝可梦')).not.toBeInTheDocument();

    const trainerCallsBeforeEnglishReload = loadTrainersWorkflow.mock.calls.length;
    await user.click(within(navigation).getByRole('button', { name: /^(Trainers|训练家)$/ }));
    await waitFor(() =>
      expect(loadTrainersWorkflow).toHaveBeenCalledTimes(trainerCallsBeforeEnglishReload + 1)
    );
    expect(loadTrainersWorkflow.mock.calls.at(-1)?.[0].paths.gameTextLanguage).toBe('en');
    await waitFor(() => expect(screen.getAllByText('Avery').length).toBeGreaterThan(0));
    expect(screen.queryByText('中文训练家')).not.toBeInTheDocument();
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

    expect(
      await screen.findByText((content) =>
        content.includes('KM Editor hit an unexpected bridge error.')
      )
    ).toBeInTheDocument();
    expect(
      screen.getByText((content) => content.includes('Error code: KM-BRIDGE-'))
    ).toBeInTheDocument();
  });
});
