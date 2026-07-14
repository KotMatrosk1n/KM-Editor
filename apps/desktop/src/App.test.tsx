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
      staticEncountersWorkflow: null,
      workflows: []
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('asks for the game before rendering the project workbench shell', async () => {
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

    expect(screen.getByText('KM Editor')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Project Setup' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Project Paths' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Validate Paths' })).toBeInTheDocument();
    expect(screen.getByText('Pokemon Scarlet Editor')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Editors' })).not.toBeInTheDocument();
  });

  it('shows busy feedback while validating project paths', async () => {
    const user = userEvent.setup();
    const fixtureBridge = createMockProjectBridge({}, true);
    const existingStaticEncounters = await fixtureBridge.loadStaticEncountersWorkflow({
      paths: {
        baseExeFsPath: 'base-exefs',
        baseRomFsPath: 'base-romfs',
        outputRootPath: 'default-output',
        saveFilePath: null,
        selectedGame: 'sword'
      }
    });
    const existingHealth = createHealthForValidatedPaths(
      'base-romfs',
      'base-exefs',
      'default-output',
      null
    );
    useWorkbenchStore.setState({
      openProject: {
        fileGraph: { entries: [], summary: existingHealth.fileGraph },
        health: existingHealth,
        projectId: 'default-output-project'
      },
      staticEncountersWorkflow: existingStaticEncounters.workflow
    });
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
    const bridge = createMockProjectBridge({ validateProject });
    const loadStaticEncountersWorkflow = vi.fn(bridge.loadStaticEncountersWorkflow);
    bridge.loadStaticEncountersWorkflow = loadStaticEncountersWorkflow;

    render(<App bridge={bridge} />);

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
    expect(useWorkbenchStore.getState().openProject?.projectId).toBe('pending-project');
    expect(useWorkbenchStore.getState().staticEncountersWorkflow).toBeNull();

    const navigation = screen.getByRole('navigation', { name: 'Workspace' });
    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    await user.click(within(navigation).getByRole('button', { name: 'Static Encounters' }));
    await waitFor(() => expect(loadStaticEncountersWorkflow).toHaveBeenCalledTimes(1));
    expect(loadStaticEncountersWorkflow.mock.calls[0]?.[0].paths.outputRootPath).toBe('output');
    expect(useWorkbenchStore.getState().staticEncountersWorkflow).not.toBeNull();

    act(() => {
      useWorkbenchStore.setState({
        editSession: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.bagHook',
              field: 'install',
              newValue: 'enabled',
              recordId: 'bag-hook',
              sources: [{ layer: 'base', relativePath: 'romfs/bin/mock' }],
              summary: 'Stage a test editor change.'
            }
          ],
          sessionId: 'switch-and-revert-session'
        }
      });
    });

    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(within(navigation).getByRole('button', { name: 'Pokemon' }));
    expect(await screen.findByRole('heading', { name: 'Switch Editors?' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Switch and Revert' }));

    await waitFor(() => expect(useWorkbenchStore.getState().activeSection).toBe('pokemon'));
    expect(useWorkbenchStore.getState().openProject?.projectId).toBe('pending-project');
    expect(screen.getByRole('button', { name: 'Editors' })).toBeInTheDocument();

    act(() => {
      useWorkbenchStore.getState().setActiveSection('health');
    });
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    expect(await screen.findByRole('button', { name: 'Validating' })).toBeDisabled();

    const outputRootInput = screen.getByLabelText('Output Root');
    await user.clear(outputRootInput);
    await user.type(outputRootInput, 'new-output');

    expect(screen.getByRole('button', { name: 'Validate Paths' })).toBeEnabled();
    expect(useWorkbenchStore.getState().projectStatus).toBe('idle');
    expect(useWorkbenchStore.getState().openProject).toBeNull();

    await act(async () => {
      resolveValidateProject();
    });

    await waitFor(() => expect(validateProject).toHaveBeenCalledTimes(2));
    expect(useWorkbenchStore.getState().openProject).toBeNull();
    expect(useWorkbenchStore.getState().projectStatus).toBe('idle');
    expect(screen.queryByRole('button', { name: 'Editors' })).not.toBeInTheDocument();
  });

  it('keeps a newer change-plan request busy when an older project request finishes', async () => {
    const user = userEvent.setup();
    const bridge = createMockProjectBridge({}, true);
    const originalCreateChangePlan = bridge.createChangePlan;
    const changePlanResolvers: Array<() => Promise<void>> = [];
    bridge.createChangePlan = vi.fn(
      (request: Parameters<typeof originalCreateChangePlan>[0]) =>
        new Promise<Awaited<ReturnType<typeof originalCreateChangePlan>>>((resolve) => {
          changePlanResolvers.push(() => originalCreateChangePlan(request).then(resolve));
        })
    );
    useWorkbenchStore.setState({
      activeSection: 'changes',
      editSession: {
        hasPendingChanges: true,
        pendingEdits: [
          {
            domain: 'workflow.items',
            field: 'price',
            newValue: '500',
            recordId: '1',
            sources: [{ layer: 'base', relativePath: 'romfs/bin/item' }],
            summary: 'Set item price.'
          }
        ],
        sessionId: 'old-plan-session'
      }
    } as never);

    render(<App bridge={bridge} />);

    const navigation = screen.getByRole('navigation', { name: 'Workspace' });
    const projectSetupNavigationButton = within(navigation).getByRole('button', {
      name: 'Project Setup'
    });

    await user.click(screen.getByRole('button', { name: 'Validate Pending Changes' }));
    await waitFor(() => expect(bridge.createChangePlan).toHaveBeenCalledTimes(1));
    expect(screen.getByRole('button', { name: 'Validating' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    expect(projectSetupNavigationButton).toBeDisabled();
    await user.click(projectSetupNavigationButton);
    expect(useWorkbenchStore.getState().activeSection).toBe('changes');

    act(() => {
      useWorkbenchStore.getState().setActiveSection('health');
    });
    await user.type(await screen.findByLabelText('Output Root'), 'new-output');
    expect(useWorkbenchStore.getState().editSession).toBeNull();

    act(() => {
      useWorkbenchStore.setState({
        activeSection: 'changes',
        editSession: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.rentalPokemon',
              field: 'level',
              newValue: '65',
              recordId: 'rental:0',
              sources: [{ layer: 'base', relativePath: 'romfs/bin/rental' }],
              summary: 'Set rental level.'
            }
          ],
          sessionId: 'new-plan-session'
        }
      } as never);
    });

    const validateButton = await screen.findByRole('button', {
      name: 'Validate Pending Changes'
    });
    await user.click(validateButton);
    await waitFor(() => expect(bridge.createChangePlan).toHaveBeenCalledTimes(2));
    expect(screen.getByRole('button', { name: 'Validating' })).toBeDisabled();

    await act(async () => {
      await changePlanResolvers[0]!();
    });

    expect(screen.getByRole('button', { name: 'Validating' })).toBeDisabled();
    expect(useWorkbenchStore.getState().editSession?.sessionId).toBe('new-plan-session');

    await act(async () => {
      await changePlanResolvers[1]!();
    });

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Validate Pending Changes' })).toBeEnabled()
    );
    expect(useWorkbenchStore.getState().editSession?.sessionId).toBe('new-plan-session');
  });

  it('shows the renamed workflow categories in sidebar order', async () => {
    const user = userEvent.setup();
    const bridge = createMockProjectBridge({}, true);
    const originalLoadPokemonWorkflow = bridge.loadPokemonWorkflow;
    const originalLoadRentalPokemonWorkflow = bridge.loadRentalPokemonWorkflow;
    const originalLoadTrainersWorkflow = bridge.loadTrainersWorkflow;
    const originalStageModMerge = bridge.stageModMerge;
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
    const stageModMerge = vi.fn(
      async (request: Parameters<typeof originalStageModMerge>[0]) => {
        const response = await originalStageModMerge(request);
        const resolution =
          request.resolutions.find((candidate) => candidate.conflictId === 'shared-price')
            ?.source ?? 'mod1';

        return {
          ...response,
          preview: {
            ...response.preview,
            canApply: true,
            conflictFileCount: 1,
            conflicts: [
              {
                conflictId: 'shared-price',
                description: 'Both mods change the same shop price.',
                directory1Value: '500',
                directory2Value: '750',
                label: 'Potion price',
                relativePath: 'romfs/bin/shop_data.bin',
                resolution
              }
            ],
            reviewToken: `review-${resolution}`,
            unresolvedConflictCount: 0
          }
        };
      }
    );
    bridge.loadPokemonWorkflow = loadPokemonWorkflow;
    const loadRentalPokemonWorkflow = vi.fn(originalLoadRentalPokemonWorkflow);
    bridge.loadRentalPokemonWorkflow = loadRentalPokemonWorkflow;
    bridge.loadTrainersWorkflow = loadTrainersWorkflow;
    bridge.stageModMerge = stageModMerge;

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

    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    await user.click(within(navigation).getByRole('button', { name: 'Rental Pokemon' }));
    await waitFor(() => expect(loadRentalPokemonWorkflow).toHaveBeenCalledTimes(1));
    expect(screen.getAllByRole('heading', { name: 'Rental Pokemon' })).toHaveLength(2);

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

    await user.click(within(navigation).getByRole('button', { name: 'Tools' }));
    await user.click(within(navigation).getByRole('button', { name: 'Mod Merger' }));
    await user.click(await screen.findByRole('button', { name: 'Select All' }));
    await user.click(screen.getByRole('button', { name: 'Stage Merge' }));
    await waitFor(() => expect(stageModMerge).toHaveBeenCalledTimes(1));
    expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled();

    await user.click(screen.getByRole('button', { name: 'Use Mod Directory 2' }));
    expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled();

    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(stageModMerge).toHaveBeenCalledTimes(2));
    expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled();

    const trainer = useWorkbenchStore.getState().trainersWorkflow?.trainers[0]!;
    act(() => {
      useWorkbenchStore.setState({
        activeSection: 'changes',
        editSession: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.trainers',
              field: 'classBallId',
              newValue: '4',
              recordId: trainer.trainerClassId.toString(),
              sources: [{ layer: 'base', relativePath: 'romfs/bin/trainer' }],
              summary: 'Set trainer class ball.'
            }
          ],
          sessionId: 'trainer-class-session'
        }
      } as never);
    });

    expect(
      screen.getByText(`${trainer.trainerClass} class (#${trainer.trainerClassId})`)
    ).toBeInTheDocument();
    expect(screen.queryByText(`${trainer.name} (#${trainer.trainerId})`)).not.toBeInTheDocument();
  });

  it('serializes normal editor mutations and rejects a stale Rental Pokemon response', async () => {
    const user = userEvent.setup();
    const bridge = createMockProjectBridge({}, true);
    const originalUpdateItemField = bridge.updateItemField;
    const originalUpdateRentalPokemonField = bridge.updateRentalPokemonField;
    let resolveItemUpdate!: () => Promise<void>;
    let resolveStaleRentalUpdate!: () => Promise<void>;
    let rentalUpdateCount = 0;
    bridge.updateItemField = vi.fn(
      (request: Parameters<typeof originalUpdateItemField>[0]) =>
        new Promise<Awaited<ReturnType<typeof originalUpdateItemField>>>((resolve) => {
          resolveItemUpdate = () => originalUpdateItemField(request).then(resolve);
        })
    );
    const updateRentalPokemonField = vi.fn(
      (request: Parameters<typeof originalUpdateRentalPokemonField>[0]) => {
        rentalUpdateCount += 1;
        const response = originalUpdateRentalPokemonField(request).then((updateResponse) => ({
          ...updateResponse,
          session: {
            ...updateResponse.session,
            pendingEdits: [
              ...(request.session?.pendingEdits ?? []),
              ...updateResponse.session.pendingEdits
            ]
          }
        }));
        if (rentalUpdateCount === 1) {
          return response;
        }

        return new Promise<Awaited<ReturnType<typeof originalUpdateRentalPokemonField>>>(
          (resolve) => {
            resolveStaleRentalUpdate = () => response.then(resolve);
          }
        );
      }
    );
    bridge.updateRentalPokemonField = updateRentalPokemonField;

    render(<App bridge={bridge} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));

    const navigation = await screen.findByRole('navigation', { name: 'Workspace' });
    await user.click(within(navigation).getByRole('button', { name: 'Editors' }));
    await user.click(within(navigation).getByRole('button', { name: 'Items' }));
    const itemInspector = await screen.findByRole('complementary', {
      name: 'Selected item provenance'
    });
    await user.click(within(itemInspector).getByRole('button', { name: 'Edit' }));
    const buyPriceInput = await within(itemInspector).findByLabelText('Buy price');
    await user.clear(buyPriceInput);
    await user.type(buyPriceInput, '500');
    await user.click(within(itemInspector).getByRole('button', { name: 'Save Item' }));
    await waitFor(() => expect(bridge.updateItemField).toHaveBeenCalledTimes(1));

    await user.click(within(navigation).getByRole('button', { name: 'Changes' }));
    await user.click(
      within(navigation).getByRole('button', { name: 'Encounters & Pokemon Sources' })
    );
    await user.click(within(navigation).getByRole('button', { name: 'Rental Pokemon' }));

    const rentalInspector = await screen.findByRole('complementary', {
      name: 'Selected rental Pokemon provenance'
    });
    const levelInput = await within(rentalInspector).findByLabelText('Level');
    await user.clear(levelInput);
    await user.type(levelInput, '65');
    await user.click(within(rentalInspector).getByRole('button', { name: 'Save Rental' }));
    expect(updateRentalPokemonField).not.toHaveBeenCalled();

    await act(async () => {
      await resolveItemUpdate();
    });

    await waitFor(() => expect(updateRentalPokemonField).toHaveBeenCalledTimes(1));
    expect(updateRentalPokemonField.mock.calls[0]?.[0].session?.pendingEdits).toEqual(
      expect.arrayContaining([expect.objectContaining({ domain: 'workflow.items' })])
    );
    await waitFor(() =>
      expect(useWorkbenchStore.getState().editSession?.pendingEdits).toEqual(
        expect.arrayContaining([
          expect.objectContaining({ domain: 'workflow.items' }),
          expect.objectContaining({ domain: 'workflow.rentalPokemon' })
        ])
      )
    );

    await user.clear(levelInput);
    await user.type(levelInput, '70');
    await user.click(within(rentalInspector).getByRole('button', { name: 'Save Rental' }));
    await waitFor(() => expect(updateRentalPokemonField).toHaveBeenCalledTimes(2));

    act(() => {
      useWorkbenchStore.getState().setActiveSection('health');
    });
    const outputRootInput = screen.getByLabelText('Output Root');
    await user.clear(outputRootInput);
    await user.type(outputRootInput, 'other-output');
    await user.clear(outputRootInput);
    await user.type(outputRootInput, 'output');
    expect(useWorkbenchStore.getState().rentalPokemonWorkflow).toBeNull();
    expect(useWorkbenchStore.getState().editSession).toBeNull();

    await act(async () => {
      await resolveStaleRentalUpdate();
    });

    expect(useWorkbenchStore.getState().draftPaths.outputRootPath).toBe('output');
    expect(useWorkbenchStore.getState().rentalPokemonWorkflow).toBeNull();
    expect(useWorkbenchStore.getState().editSession).toBeNull();
  });

});
