/* SPDX-License-Identifier: GPL-3.0-only */

import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { App } from './App';
import {
  createHealthForValidatedPaths,
  createMockDesktopServices,
  createMockProjectBridge,
  createNativeUpdate,
  createWrongGameHealth
} from './testSupport/appTestFixtures';
import {
  type BagHookWorkflow,
  type BehaviorWorkflow,
  type CatchCapWorkflow,
  type ChangePlan,
  type DynamaxAdventuresWorkflow,
  type EditSession,
  type EncountersWorkflow,
  type EncounterTableRecord,
  type ExeFsPatchWorkflow,
  type FlagworkSaveWorkflow,
  type GiftPokemonWorkflow,
  type GymUniformRemovalWorkflow,
  type HyperTrainingWorkflow,
  type IvScreenWorkflow,
  type ItemRecord,
  type ItemsWorkflow,
  type ModMergerPreview,
  type ModMergerWorkflow,
  type MovesWorkflow,
  type PlacementWorkflow,
  type PokemonWorkflow,
  type ProjectFileGraph,
  type ProjectHealth,
  type RaidBattlesWorkflow,
  type RaidRewardsWorkflow,
  type RentalPokemonWorkflow,
  type RoyalCandyWorkflow,
  type ShopsWorkflow,
  type SpreadsheetImportWorkflow,
  type StartingItemsWorkflow,
  type SvModMergerPreview,
  type SvModMergerSource,
  type SvModMergerWorkflow,
  type StaticEncountersWorkflow,
  type TextWorkflow,
  type TradePokemonWorkflow,
  type TrainersWorkflow,
  type TypeChartWorkflow,
  type WorkflowSummary
} from './bridge/contracts';
import { type ProjectBridge } from './bridge/projectBridge';
import { type DesktopServices, type NativeUpdate } from './desktopServices';
import { useWorkbenchStore } from './workbenchStore';

const windowCloseRequestedEvent = 'km-editor://window-close-requested';

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
        selectedGame: 'sword'
      },
      editSession: null,
      editValidationDiagnostics: [],
      bagHookWorkflow: null,
      encounterSearchText: '',
      encountersWorkflow: null,
      catchCapWorkflow: null,
      hyperTrainingWorkflow: null,
      exeFsPatchSearchText: '',
      exeFsPatchWorkflow: null,
      flagworkSaveSearchText: '',
      flagworkSaveWorkflow: null,
      giftPokemonSearchText: '',
      giftPokemonWorkflow: null,
      tradePokemonSearchText: '',
      tradePokemonWorkflow: null,
      rentalPokemonSearchText: '',
      rentalPokemonWorkflow: null,
      dynamaxAdventureSearchText: '',
      dynamaxAdventuresWorkflow: null,
      staticEncounterSearchText: '',
      staticEncountersWorkflow: null,
      itemSearchText: '',
      itemsWorkflow: null,
      movesSearchText: '',
      movesWorkflow: null,
      openProject: null,
      placementSearchText: '',
      placementWorkflow: null,
      pokemonSearchText: '',
      pokemonWorkflow: null,
      projectStatus: 'idle',
      raidBattleSearchText: '',
      raidBattlesWorkflow: null,
      raidRewardSearchText: '',
      raidRewardsWorkflow: null,
      royalCandySearchText: '',
      royalCandyWorkflow: null,
      startingItemsWorkflow: null,
      spreadsheetImportPreview: null,
      spreadsheetImportSearchText: '',
      spreadsheetImportSourcePath: '',
      spreadsheetImportWorkflow: null,
      selectedEncounterTableId: null,
      selectedBagHookSlot: null,
      selectedExeFsCheckId: null,
      selectedExeFsPatchId: null,
      selectedCatchCapBadgeCount: null,
      selectedGiftPokemonIndex: null,
      selectedTradePokemonIndex: null,
      selectedRentalPokemonIndex: null,
      selectedDynamaxAdventureEntryIndex: null,
      selectedStaticEncounterIndex: null,
      selectedRoyalCandyCheckId: null,
      selectedRoyalCandyWorkflowId: null,
      selectedStartingItemSlot: null,
      selectedSpreadsheetImportProfileId: null,
      selectedFlagId: null,
      selectedItemId: null,
      selectedMoveId: null,
      selectedPlacementObjectId: null,
      selectedPokemonPersonalId: null,
      selectedRaidBattleTableId: null,
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
    expect(screen.queryByRole('button', { name: 'Open Project' })).not.toBeInTheDocument();
    expect(screen.queryByPlaceholderText('Search project')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Viewers' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Editors' })).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Encounters & Pokemon Sources' })
    ).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Economy' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Tools' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Advanced Editors' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Pokemon' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Workflows' })).not.toBeInTheDocument();
  });

  it('asks for the game before showing the workbench', async () => {
    const user = userEvent.setup();
    useWorkbenchStore.setState({
      draftPaths: {
        baseExeFsPath: '',
        baseRomFsPath: '',
        outputRootPath: '',
        saveFilePath: '',
        selectedGame: null
      }
    });

    render(<App />);

    expect(
      screen.getByRole('heading', { name: 'Which game are you using?' })
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Pokemon Sword' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Pokemon Shield' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Pokemon Scarlet' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Pokemon Violet' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Project Setup' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Pokemon Scarlet' }));

    expect(screen.getByRole('heading', { name: 'Project Setup' })).toBeInTheDocument();
    expect(screen.getByText('Pokemon Scarlet Editor')).toBeInTheDocument();
    expect(window.localStorage.getItem('km-editor.project-path-draft.v1') ?? '').not.toContain(
      'selectedGame'
    );
  });

  it('remembers validated path sets separately for each selected game', async () => {
    const user = userEvent.setup();
    const validateProject = vi.fn(async (request) => ({
      health: createHealthForValidatedPaths(
        request.paths.baseRomFsPath ?? '',
        request.paths.baseExeFsPath ?? '',
        request.paths.outputRootPath ?? '',
        request.paths.saveFilePath
      )
    }));

    render(<App bridge={createMockProjectBridge({ validateProject })} />);

    await user.clear(screen.getByLabelText('Base RomFS'));
    await user.type(screen.getByLabelText('Base RomFS'), 'sword-romfs');
    await user.clear(screen.getByLabelText('Base ExeFS'));
    await user.type(screen.getByLabelText('Base ExeFS'), 'sword-exefs');
    await user.clear(screen.getByLabelText('Output Root'));
    await user.type(screen.getByLabelText('Output Root'), 'sword-output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));

    await waitFor(() => expect(validateProject).toHaveBeenCalledTimes(1));
    expect(await screen.findByRole('button', { name: 'Editors' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Change Game' }));
    await user.click(screen.getByRole('button', { name: 'Pokemon Shield' }));

    expect(screen.getByLabelText('Base RomFS')).toHaveValue('');
    expect(screen.getByLabelText('Base ExeFS')).toHaveValue('');
    expect(screen.getByLabelText('Output Root')).toHaveValue('');
    expect(screen.queryByRole('button', { name: 'Editors' })).not.toBeInTheDocument();

    await user.type(screen.getByLabelText('Base RomFS'), 'shield-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'shield-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'shield-output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));

    await waitFor(() => expect(validateProject).toHaveBeenCalledTimes(2));
    expect(await screen.findByRole('button', { name: 'Editors' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Change Game' }));
    await user.click(screen.getByRole('button', { name: 'Pokemon Sword' }));

    expect(screen.getByLabelText('Base RomFS')).toHaveValue('sword-romfs');
    expect(screen.getByLabelText('Base ExeFS')).toHaveValue('sword-exefs');
    expect(screen.getByLabelText('Output Root')).toHaveValue('sword-output');
    expect(screen.queryByRole('button', { name: 'Editors' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));

    await waitFor(() => expect(validateProject).toHaveBeenCalledTimes(3));
    expect(validateProject).toHaveBeenLastCalledWith({
      paths: {
        baseExeFsPath: 'sword-exefs',
        baseRomFsPath: 'sword-romfs',
        outputRootPath: 'sword-output',
        saveFilePath: null,
        selectedGame: 'sword'
      }
    });
    expect(await screen.findByRole('button', { name: 'Editors' })).toBeInTheDocument();
  });

  it('shows the renamed workflow categories in sidebar order', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

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
    expect(screen.queryByRole('button', { name: 'Workflows' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Viewers' }));
    expect(screen.getByRole('button', { name: 'Flagwork / Save' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Text' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Editors' }));
    expect(screen.getByRole('button', { name: 'Pokemon' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Trainers' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Moves' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Items' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Placement' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    expect(screen.getByRole('button', { name: 'Wild Encounters' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Raid Battles' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Rental Pokemon' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Economy' }));
    expect(screen.getByRole('button', { name: 'Shops' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Raid Rewards' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Tools' }));
    expect(screen.getByRole('button', { name: 'Spreadsheet Import' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Hooks' }));
    expect(screen.getByRole('button', { name: 'Bag Hook' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Advanced Editors' }));
    expect(within(navigation).getByRole('button', { name: 'Catch Cap' })).toBeInTheDocument();
    expect(within(navigation).getByRole('button', { name: 'IV Screen' })).toBeInTheDocument();
    expect(
      within(navigation)
        .getAllByRole('button')
        .filter((button) => button.classList.contains('nav-child-button'))
        .map((button) => button.textContent)
        .slice(-11)
    ).toEqual([
      'Royal Candy',
      'Starting Items',
      'Catch Cap',
      'IV Screen',
      'Hyper Training',
      'Shiny Rate',
      'Type Chart',
      'Fairy Gym Boosts',
      'Fashion Unlock',
      'Gym Uniform Removal',
      'Dynamax Adventures'
    ]);
  });

  it('shows only the enabled Scarlet/Violet regular editors and tools', async () => {
    const user = userEvent.setup();
    const createWorkflowSummary = (id: string, label: string): WorkflowSummary => ({
      availability: 'available',
      description: `${label} S/V test workflow.`,
      diagnostics: [],
      id,
      label
    });
    const listWorkflows = vi.fn(async () => ({
      workflows: [
        createWorkflowSummary('items', 'Items'),
        createWorkflowSummary('pokemon', 'Pokemon Data'),
        createWorkflowSummary('trainers', 'Trainers'),
        createWorkflowSummary('encounters', 'Wild Encounters'),
        createWorkflowSummary('modMerger', 'S/V Mod Merger')
      ]
    }));
    useWorkbenchStore.setState({
      draftPaths: {
        baseExeFsPath: '',
        baseRomFsPath: '',
        outputRootPath: '',
        saveFilePath: '',
        selectedGame: 'scarlet'
      }
    });

    render(<App bridge={createMockProjectBridge({ listWorkflows }, true)} />);

    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await waitFor(() => expect(listWorkflows).toHaveBeenCalledTimes(1));

    const navigation = screen.getByRole('navigation', { name: 'Workspace' });
    const topLevelLabels = within(navigation)
      .getAllByRole('button')
      .filter((button) => !button.classList.contains('nav-child-button'))
      .map((button) => button.textContent);

    expect(topLevelLabels).toEqual([
      'Project Setup',
      'Editors',
      'Encounters & Pokemon Sources',
      'Tools',
      'Changes',
      'Settings'
    ]);

    await user.click(screen.getByRole('button', { name: 'Editors' }));
    expect(within(navigation).getByRole('button', { name: 'Pokemon' })).toBeInTheDocument();
    expect(within(navigation).getByRole('button', { name: 'Trainers' })).toBeInTheDocument();
    expect(within(navigation).getByRole('button', { name: 'Items' })).toBeInTheDocument();
    expect(within(navigation).queryByRole('button', { name: 'Moves' })).not.toBeInTheDocument();
    expect(within(navigation).queryByRole('button', { name: 'Placement' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    expect(
      within(navigation).getByRole('button', { name: 'Wild Encounters' })
    ).toBeInTheDocument();
    expect(
      within(navigation).queryByRole('button', { name: 'Raid Battles' })
    ).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Tools' }));
    expect(within(navigation).getByRole('button', { name: 'Mod Merger' })).toBeInTheDocument();
    expect(
      within(navigation).queryByRole('button', { name: 'Spreadsheet Import' })
    ).not.toBeInTheDocument();
    expect(within(navigation).queryByRole('button', { name: 'Randomizer' })).not.toBeInTheDocument();
  });

  it('resets Type Chart draft values to vanilla before staging', async () => {
    const user = userEvent.setup();
    const baseBridge = createMockProjectBridge({}, true);
    const stageTypeChart = vi.fn(baseBridge.stageTypeChart);
    const loadTypeChartWorkflow = vi.fn(async (request) => {
      const response = await baseBridge.loadTypeChartWorkflow(request);
      return {
        workflow: {
          ...response.workflow,
          cells: response.workflow.cells.map((cell, index) =>
            index === 0
              ? {
                  ...cell,
                  effectiveness: 8 as const,
                  vanillaEffectiveness: 4 as const
                }
              : cell
          )
        }
      };
    });

    render(
      <App
        bridge={{
          ...baseBridge,
          loadTypeChartWorkflow,
          stageTypeChart
        }}
      />
    );

    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Advanced Editors' }));
    await user.click(screen.getByRole('button', { name: 'Type Chart' }));

    const resetButton = await screen.findByRole('button', { name: 'Reset to Vanilla Chart' });
    expect(resetButton).toBeEnabled();
    await user.click(resetButton);
    await user.click(screen.getByRole('button', { name: 'Stage Type Chart' }));

    await waitFor(() => expect(stageTypeChart).toHaveBeenCalledTimes(1));
    const request = stageTypeChart.mock.calls[0]?.[0];
    expect(request?.values[0]).toBe(4);
  });

  it('shows clear Randomizer controls and applies shared seeds directly', async () => {
    const user = userEvent.setup();
    const writeText = vi.fn(async () => undefined);
    vi.stubGlobal('navigator', {
      ...navigator,
      clipboard: {
        writeText
      }
    });
    const applyRandomizer = vi.fn(async (request) =>
      Promise.resolve({
        applyResult: {
          applyId: 'randomizer-apply-1',
          diagnostics: [
            {
              message: 'Randomizer applied selected output.',
              severity: 'info' as const
            }
          ],
          writtenFiles: request.config.options.randomizePokemonStats
            ? ['romfs/bin/pml/personal/personal_total.bin']
            : []
        },
        seed: `KM1-MOCK-${request.config.userSeed || 'generated'}`
      })
    );
    render(<App bridge={createMockProjectBridge({ applyRandomizer }, true)} />);

    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(await screen.findByRole('button', { name: 'Tools' }));
    await user.click(screen.getByRole('button', { name: 'Randomizer' }));

    expect(screen.getByRole('button', { name: 'Catch Rates' })).toHaveAttribute(
      'title',
      expect.stringContaining('catch rates')
    );
    expect(screen.getByRole('button', { name: 'Moves' })).toBeInTheDocument();
    expect(screen.queryByText('Randomize Misc')).not.toBeInTheDocument();
    expect(screen.queryByText('Randomize Compatibility')).not.toBeInTheDocument();

    const statsPanel = screen.getByRole('heading', { name: 'Stats' }).closest('section');
    expect(statsPanel).not.toBeNull();
    expect(within(statsPanel!).getByLabelText('HP')).not.toBeChecked();
    expect(within(statsPanel!).getByLabelText('HP')).toBeDisabled();

    const typeChartPanel = screen.getByRole('heading', { name: 'Type Chart' }).closest('section');
    expect(typeChartPanel).not.toBeNull();
    expect(within(typeChartPanel!).getByLabelText('No immunities')).toBeDisabled();
    await user.click(within(typeChartPanel!).getByLabelText('Randomize Type Chart'));
    await user.click(within(typeChartPanel!).getByLabelText('No immunities'));
    expect(within(typeChartPanel!).getByLabelText('No immunities')).toBeChecked();
    await user.click(within(typeChartPanel!).getByLabelText('No more than one immunity per type'));
    expect(within(typeChartPanel!).getByLabelText('No immunities')).not.toBeChecked();
    expect(within(typeChartPanel!).getByLabelText('No more than one immunity per type')).toBeChecked();

    await user.click(screen.getByRole('button', { name: 'Learnsets' }));
    expect(screen.getByLabelText('Expand learnsets to 25 moves')).toHaveAttribute(
      'title',
      expect.stringContaining('25 move slots')
    );

    await user.click(screen.getByRole('button', { name: 'Moves' }));
    expect(screen.getByLabelText('Randomize Move Compatibility')).toBeInTheDocument();

    await user.type(screen.getByLabelText('Shared Randomization Seed'), 'KMR1.friend.seed');
    await user.click(screen.getByRole('button', { name: 'Apply Randomization Seed' }));
    await user.click(await screen.findByRole('button', { name: 'Confirm Apply Seed' }));

    await waitFor(() => expect(applyRandomizer).toHaveBeenCalledTimes(1));
    expect(applyRandomizer.mock.calls[0]?.[0].config.rollSeed).toBe('mock-roll');
    expect(applyRandomizer.mock.calls[0]?.[0].config.outputHash).toBe('mock-output');
    expect(await screen.findByText('Randomizer applied selected output.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Copy Seed' }));
    expect(writeText).toHaveBeenCalledWith('KM1-MOCK-mock-seed');
    expect(await screen.findByRole('button', { name: 'Copied' })).toBeInTheDocument();
  });

  it('replaces existing editable field contents when typing after click', async () => {
    const user = userEvent.setup();
    useWorkbenchStore.setState({
      draftPaths: {
        baseExeFsPath: '',
        baseRomFsPath: 'C:/old/romfs',
        outputRootPath: '',
        saveFilePath: '',
        selectedGame: 'sword'
      }
    });

    render(<App />);

    const romFsInput = screen.getByLabelText('Base RomFS') as HTMLInputElement;
    await user.click(romFsInput);
    await waitFor(() => {
      expect(romFsInput.selectionStart).toBe(0);
      expect(romFsInput.selectionEnd).toBe(romFsInput.value.length);
    });
    await user.keyboard('C:/new/romfs');

    expect(romFsInput).toHaveValue('C:/new/romfs');
  });

  it('switches workbench sections', async () => {
    const user = userEvent.setup();
    render(<App />);

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByRole('heading', { name: 'Changes' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Settings' }));

    expect(screen.getByRole('heading', { level: 1, name: 'Settings' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Check for Updates' })).toBeInTheDocument();
  });

  it('validates and opens a read-only project shell state', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, false)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));

    expect(await screen.findAllByText('View Only')).toHaveLength(2);

    expect(screen.queryByRole('button', { name: 'Workflows' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Editors' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Items' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Pokemon' })).not.toBeInTheDocument();
  });

  it('blocks editors when validated ExeFS belongs to the wrong selected game', async () => {
    const user = userEvent.setup();
    const listWorkflows = vi.fn(async () => ({ workflows: [] }));
    const wrongGameHealth = createWrongGameHealth();

    useWorkbenchStore.setState({
      draftPaths: {
        baseExeFsPath: '',
        baseRomFsPath: '',
        outputRootPath: '',
        saveFilePath: '',
        selectedGame: 'shield'
      }
    });

    render(
      <App
        bridge={createMockProjectBridge(
          {
            listWorkflows,
            validateProject: (request) => {
              expect(request.paths.selectedGame).toBe('shield');
              return Promise.resolve({ health: wrongGameHealth });
            }
          },
          true
        )}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));

    expect(await screen.findByText(/Base ExeFS contains Pokemon Sword/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Editors' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Pokemon' })).not.toBeInTheDocument();
    expect(listWorkflows).not.toHaveBeenCalled();
  });

  it('opens Items, searches records, and shows selected provenance', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge()} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Items' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Items' })).toBeInTheDocument();
    expect(screen.getAllByText('Potion').length).toBeGreaterThan(0);

    await user.type(screen.getByLabelText('Search items'), 'antidote');
    await user.click(screen.getByText('Antidote'));

    expect(screen.queryByText('Potion')).not.toBeInTheDocument();
    expect(screen.getByText('romfs/bin/pml/item/item.dat')).toBeInTheDocument();
    expect(screen.getAllByText('Base only').length).toBeGreaterThan(0);
    expect(screen.getByRole('heading', { level: 4, name: 'Field Use' })).toBeInTheDocument();
    expect(screen.getByText('Restore HP')).toBeInTheDocument();
    expect(screen.getByText('20 HP')).toBeInTheDocument();

    const itemSearch = screen.getByLabelText('Search items');
    await user.clear(itemSearch);
    await user.type(itemSearch, 'tm');

    const tm02 = await screen.findByText('TM02 (Razor Leaf)');
    const tm10 = screen.getByText('TM10 (Magical Leaf)');
    expect(Boolean(tm02.compareDocumentPosition(tm10) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(
      true
    );
    expect(screen.queryByText('TR02 (Growl)')).not.toBeInTheDocument();

    await user.clear(itemSearch);
    await user.type(itemSearch, 'tr');

    const tr02 = await screen.findByText('TR02 (Growl)');
    const tr10 = screen.getByText('TR10 (Magical Leaf)');
    expect(Boolean(tr02.compareDocumentPosition(tr10) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(
      true
    );
    expect(screen.queryByText('TM02 (Razor Leaf)')).not.toBeInTheDocument();
  });

  it('opens Pokemon, searches records, and shows selected details', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge()} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Pokemon' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Pokemon' })).toBeInTheDocument();
    expect(screen.getAllByText('Bulbasaur').length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: /Tackle/ })).toBeInTheDocument();
    const pokemonTable = screen.getByRole('table', { name: 'Pokemon' });
    expect(within(pokemonTable).getByRole('columnheader', { name: 'ID' })).toBeInTheDocument();
    expect(within(pokemonTable).getByRole('columnheader', { name: 'Name' })).toBeInTheDocument();
    expect(within(pokemonTable).getByRole('columnheader', { name: 'Types' })).toBeInTheDocument();
    expect(within(pokemonTable).queryByRole('columnheader', { name: 'Form' })).not.toBeInTheDocument();
    expect(within(pokemonTable).queryByRole('columnheader', { name: 'HP' })).not.toBeInTheDocument();
    expect(within(pokemonTable).queryByRole('columnheader', { name: 'BST' })).not.toBeInTheDocument();
    expect(within(pokemonTable).queryByRole('columnheader', { name: 'Evo' })).not.toBeInTheDocument();
    expect(within(pokemonTable).queryByRole('columnheader', { name: 'Learn' })).not.toBeInTheDocument();
    act(() => {
      useWorkbenchStore.setState({ selectedPokemonPersonalId: 0 });
    });
    expect(screen.getByRole('button', { name: /Tackle/ })).toBeInTheDocument();
    expect(screen.queryByText('No level-up moves.')).not.toBeInTheDocument();

    await user.clear(screen.getByLabelText('Search Pokemon'));
    await user.type(screen.getByLabelText('Search Pokemon'), 'char');

    expect(screen.queryByText('Bulbasaur')).not.toBeInTheDocument();
    expect(screen.getAllByText('Charmander').length).toBeGreaterThan(0);
    expect(within(pokemonTable).getByText('Fire')).toBeInTheDocument();

    await user.clear(screen.getByLabelText('Search Pokemon'));
    await user.type(screen.getByLabelText('Search Pokemon'), 'fire');

    expect(within(pokemonTable).queryByText('Charmander')).not.toBeInTheDocument();
    expect(within(pokemonTable).queryByText('Fire')).not.toBeInTheDocument();
  });

  it('starts a Pokemon edit session and saves a personal stat change', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Pokemon' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Pokemon' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Edit' }));
    const tm00 = screen.getByRole('checkbox', { name: /TM00 Mega Punch/ });
    expect(tm00).not.toBeChecked();
    await user.click(tm00);
    await waitFor(() => expect(tm00).toBeChecked());
    expect(screen.getByLabelText('Type 1')).toHaveDisplayValue('Grass');
    expect(screen.getByLabelText('Ability 1')).toHaveDisplayValue('065 Overgrow');
    expect(screen.getByLabelText('Held Item 50%')).toHaveDisplayValue('000 None');
    const hpInput = screen.getByLabelText('HP');
    await user.clear(hpInput);
    await user.type(hpInput, '99');
    await user.click(screen.getByRole('button', { name: 'Save Changes' }));

    expect(await screen.findByDisplayValue('99')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Bulbasaur hp to 99.')).toBeInTheDocument();
    const pendingRow = screen.getByText('Set Bulbasaur hp to 99.').closest('li');
    expect(pendingRow).not.toBeNull();
    expect(within(pendingRow!).getAllByText('Pokemon').length).toBeGreaterThan(0);
    expect(within(pendingRow!).getByText('Bulbasaur (#1)')).toBeInTheDocument();
    expect(within(pendingRow!).getByText('HP')).toBeInTheDocument();
    expect(within(pendingRow!).getByText('99')).toBeInTheDocument();
    expect(
      within(pendingRow!).getByText('romfs/bin/pml/personal/personal_total.bin (Base)')
    ).toBeInTheDocument();
  });

  it('removes one pending change from Changes without discarding the rest', async () => {
    const user = userEvent.setup();
    const validateEditSession = vi.fn(
      (request: Parameters<ProjectBridge['validateEditSession']>[0]) =>
        Promise.resolve({
          diagnostics: [],
          isValid: true,
          session: request.session
        })
    );

    render(
      <App
        bridge={createMockProjectBridge(
          {
            startEditSession: () =>
              Promise.resolve({
                session: {
                  hasPendingChanges: true,
                  pendingEdits: [
                    {
                      domain: 'workflow.pokemon',
                      field: 'hp',
                      newValue: '99',
                      recordId: '1',
                      sources: [
                        {
                          layer: 'base',
                          relativePath: 'romfs/bin/pml/personal/personal_total.bin'
                        }
                      ],
                      summary: 'Set Bulbasaur hp to 99.'
                    },
                    {
                      domain: 'workflow.pokemon',
                      field: 'attack',
                      newValue: '88',
                      recordId: '1',
                      sources: [
                        {
                          layer: 'base',
                          relativePath: 'romfs/bin/pml/personal/personal_total.bin'
                        }
                      ],
                      summary: 'Set Bulbasaur attack to 88.'
                    }
                  ],
                  sessionId: 'session-1'
                }
              }),
            validateEditSession
          },
          true
        )}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Pokemon' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Pokemon' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Edit' }));
    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Bulbasaur hp to 99.')).toBeInTheDocument();
    expect(screen.getByText('Set Bulbasaur attack to 88.')).toBeInTheDocument();

    await user.click(
      screen.getByRole('button', { name: /Remove pending change 1: Set Bulbasaur hp to 99\./ })
    );

    await waitFor(() =>
      expect(screen.queryByText('Set Bulbasaur hp to 99.')).not.toBeInTheDocument()
    );
    expect(screen.getByText('Set Bulbasaur attack to 88.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Changes' }));

    await waitFor(() => expect(validateEditSession).toHaveBeenCalledTimes(1));
    expect(validateEditSession.mock.calls[0]![0].session.pendingEdits).toEqual([
      expect.objectContaining({
        field: 'attack',
        newValue: '88',
        summary: 'Set Bulbasaur attack to 88.'
      })
    ]);
  });

  it('starts a Pokemon edit session and saves a learnset row change', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Pokemon' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Pokemon' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Edit' }));
    await user.click(screen.getByRole('button', { name: /Growl/ }));
    const learnsetBlock = screen
      .getByRole('heading', { level: 4, name: 'Learnset' })
      .closest('.inspector-block') as HTMLElement | null;
    expect(learnsetBlock).not.toBeNull();
    const learnsetMoveInput = within(learnsetBlock!).getByLabelText('Move');
    await user.clear(learnsetMoveInput);
    await user.type(learnsetMoveInput, '345');
    const learnsetLevelInput = within(learnsetBlock!).getAllByLabelText('Level')[0]!;
    await user.clear(learnsetLevelInput);
    await user.type(learnsetLevelInput, '9');
    expect(screen.queryByRole('button', { name: 'Save learnset row' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Save Changes' }));

    expect(await within(learnsetBlock!).findByDisplayValue('345 Magical Leaf')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Bulbasaur learnset slot 1 to Lv. 9 Magical Leaf.')).toBeInTheDocument();
    const pendingRow = screen
      .getByText('Set Bulbasaur learnset slot 1 to Lv. 9 Magical Leaf.')
      .closest('li');
    expect(pendingRow).not.toBeNull();
    expect(within(pendingRow!).getByText('Learnset slot #2 Update')).toBeInTheDocument();
    expect(within(pendingRow!).getByText('Lv. 9 345 Magical Leaf')).toBeInTheDocument();
  });

  it('keeps an unsaved learnset move draft when selecting another row', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Pokemon' }));

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    const learnsetBlock = screen
      .getByRole('heading', { level: 4, name: 'Learnset' })
      .closest('.inspector-block') as HTMLElement | null;
    expect(learnsetBlock).not.toBeNull();
    const learnsetMoveInput = within(learnsetBlock!).getByLabelText('Move');
    await user.clear(learnsetMoveInput);
    await user.type(learnsetMoveInput, 'Razor');

    await user.click(within(learnsetBlock!).getByRole('button', { name: /Growl/ }));

    expect(within(learnsetBlock!).getByRole('button', { name: /Razor Leaf/ })).toBeInTheDocument();

    await user.click(within(learnsetBlock!).getByRole('button', { name: /Razor Leaf/ }));

    expect(within(learnsetBlock!).getByLabelText('Move')).toHaveDisplayValue('075 Razor Leaf');
  });

  it('reorders Pokemon learnset rows by dragging one move onto another', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Pokemon' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Pokemon' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Edit' }));
    const learnsetBlock = screen
      .getByRole('heading', { level: 4, name: 'Learnset' })
      .closest('.inspector-block') as HTMLElement | null;
    expect(learnsetBlock).not.toBeNull();
    const tackleRow = within(learnsetBlock!).getByLabelText('Move').closest('li');
    const growlRow = within(learnsetBlock!).getByRole('button', { name: /Growl/ }).closest('li');
    expect(tackleRow).not.toBeNull();
    expect(growlRow).not.toBeNull();
    const dragData = new Map<string, string>();
    const dataTransfer = {
      dropEffect: '',
      effectAllowed: '',
      getData: (format: string) => dragData.get(format) ?? '',
      setData: (format: string, data: string) => {
        dragData.set(format, data);
      }
    } as unknown as DataTransfer;

    fireEvent.dragStart(growlRow!, { dataTransfer });
    fireEvent.dragOver(tackleRow!, { dataTransfer });
    fireEvent.drop(tackleRow!, { dataTransfer });
    fireEvent.dragEnd(growlRow!, { dataTransfer });

    expect(await within(learnsetBlock!).findByDisplayValue('045 Growl')).toBeInTheDocument();
    expect(within(learnsetBlock!).getByRole('button', { name: /Tackle/ })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Move Bulbasaur learnset slot 1 to slot 0.')).toBeInTheDocument();
  });

  it('keeps the moved Pokemon learnset row selected after move buttons', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Pokemon' }));

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    const learnsetBlock = screen
      .getByRole('heading', { level: 4, name: 'Learnset' })
      .closest('.inspector-block') as HTMLElement | null;
    expect(learnsetBlock).not.toBeNull();
    await user.click(within(learnsetBlock!).getByRole('button', { name: /Growl/ }));

    await user.click(within(learnsetBlock!).getByRole('button', { name: 'Move learnset row up' }));

    expect(await within(learnsetBlock!).findByDisplayValue('045 Growl')).toBeInTheDocument();
    expect(within(learnsetBlock!).getByLabelText('Level')).toHaveDisplayValue('1');
    expect(within(learnsetBlock!).getByRole('button', { name: /Tackle/ })).toBeInTheDocument();
  });

  it('keeps the moved Pokemon learnset row selected after moving down', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Pokemon' }));

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    const learnsetBlock = screen
      .getByRole('heading', { level: 4, name: 'Learnset' })
      .closest('.inspector-block') as HTMLElement | null;
    expect(learnsetBlock).not.toBeNull();

    await user.click(within(learnsetBlock!).getByRole('button', { name: 'Move learnset row down' }));

    expect(await within(learnsetBlock!).findByDisplayValue('033 Tackle')).toBeInTheDocument();
    expect(within(learnsetBlock!).getByLabelText('Level')).toHaveDisplayValue('3');
    expect(within(learnsetBlock!).getByRole('button', { name: /Growl/ })).toBeInTheDocument();
  });

  it('starts a Pokemon edit session and saves an evolution row change', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Pokemon' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Pokemon' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Edit' }));
    await user.click(screen.getByRole('button', { name: /002 Ivysaur/ }));
    const evolutionsBlock = screen
      .getByRole('heading', { level: 4, name: 'Evolutions' })
      .closest('.inspector-block') as HTMLElement | null;
    expect(evolutionsBlock).not.toBeNull();
    const evolutionMethodInput = within(evolutionsBlock!).getByLabelText('Method');
    await user.clear(evolutionMethodInput);
    await user.type(evolutionMethodInput, '8');
    await user.click(within(evolutionsBlock!).getByRole('button', { name: 'Show Item options' }));
    expect(within(evolutionsBlock!).getByRole('option', { name: '001 Potion' })).toBeInTheDocument();
    await user.click(within(evolutionsBlock!).getByRole('option', { name: '025 Thunder Stone' }));
    const newEvolutionMethodInput = within(evolutionsBlock!).getByLabelText('New method');
    await user.clear(newEvolutionMethodInput);
    await user.type(newEvolutionMethodInput, '8');
    const itemOptionButtons = within(evolutionsBlock!).getAllByRole('button', {
      name: 'Show Item options'
    });
    await user.click(itemOptionButtons[itemOptionButtons.length - 1]!);
    expect(within(evolutionsBlock!).getByRole('option', { name: '001 Potion' })).toBeInTheDocument();
    expect(
      within(evolutionsBlock!).getByRole('option', { name: '025 Thunder Stone' })
    ).toBeInTheDocument();
    await user.keyboard('{Escape}');
    await user.clear(within(evolutionsBlock!).getByLabelText('Form'));
    await user.type(within(evolutionsBlock!).getByLabelText('Form'), '1');
    const evolutionLevelInput = within(evolutionsBlock!).getAllByLabelText('Level')[0]!;
    await user.clear(evolutionLevelInput);
    await user.type(evolutionLevelInput, '32');
    await user.click(screen.getByRole('button', { name: 'Save Changes' }));

    expect(await screen.findByRole('button', { name: /008 Use Item/ })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Bulbasaur evolution slot 0 to species 2 at level 32.')).toBeInTheDocument();
  });

  it('shows evolution species names when the target is outside filtered species options', async () => {
    const baseBridge = createMockProjectBridge({}, true);
    const pokemonResponse = await baseBridge.loadPokemonWorkflow({
      paths: {
        baseExeFsPath: 'base-exefs',
        baseRomFsPath: 'base-romfs',
        outputRootPath: 'output',
        saveFilePath: null,
        selectedGame: 'sword'
      }
    });
    const bulbasaur = pokemonResponse.workflow.pokemon.find((pokemon) => pokemon.personalId === 1)!;
    const venusaur = {
      ...bulbasaur,
      evolutions: [],
      name: 'Venusaur',
      personalId: 3,
      speciesId: 3
    };
    const user = userEvent.setup();

    render(
      <App
        bridge={{
          ...baseBridge,
          loadPokemonWorkflow: async () => ({
            workflow: {
              ...pokemonResponse.workflow,
              editableFields: pokemonResponse.workflow.editableFields.map((field) =>
                field.field === 'hatchedSpecies'
                  ? {
                      ...field,
                      options: field.options.filter((option) => option.value !== 3)
                    }
                  : field
              ),
              pokemon: [
                {
                  ...bulbasaur,
                  evolutions: [
                    {
                      ...bulbasaur.evolutions[0]!,
                      species: 3
                    }
                  ]
                },
                ...pokemonResponse.workflow.pokemon.filter(
                  (pokemon) => pokemon.personalId !== bulbasaur.personalId
                ),
                venusaur
              ]
            }
          })
        }}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Pokemon' }));

    const evolutionsBlock = await screen
      .findByRole('heading', { level: 4, name: 'Evolutions' })
      .then((heading) => heading.closest('.inspector-block') as HTMLElement | null);
    expect(evolutionsBlock).not.toBeNull();
    expect(within(evolutionsBlock!).getByRole('button', { name: /003 Venusaur/ })).toBeInTheDocument();
    expect(within(evolutionsBlock!).queryByText('Species 3')).not.toBeInTheDocument();
    expect(within(evolutionsBlock!).getByLabelText('Species')).toHaveDisplayValue('003 Venusaur');
  });

  it('keeps Pokemon evolution row drafts when switching rows until Save Changes', async () => {
    const baseBridge = createMockProjectBridge({}, true);
    const pokemonResponse = await baseBridge.loadPokemonWorkflow({
      paths: {
        baseExeFsPath: 'base-exefs',
        baseRomFsPath: 'base-romfs',
        outputRootPath: 'output',
        saveFilePath: null,
        selectedGame: 'sword'
      }
    });
    const bulbasaur = pokemonResponse.workflow.pokemon.find((pokemon) => pokemon.personalId === 1)!;
    const updatePokemonEvolution = vi.fn(baseBridge.updatePokemonEvolution);
    const user = userEvent.setup();
    render(
      <App
        bridge={{
          ...baseBridge,
          loadPokemonWorkflow: async () => ({
            workflow: {
              ...pokemonResponse.workflow,
              pokemon: pokemonResponse.workflow.pokemon.map((pokemon) =>
                pokemon.personalId === bulbasaur.personalId
                  ? {
                      ...pokemon,
                      evolutions: [
                        ...pokemon.evolutions,
                        {
                          argument: 0,
                          argumentKind: 'level',
                          argumentLabel: 'Level',
                          argumentValue: 'None',
                          form: 0,
                          level: 32,
                          method: 4,
                          methodName: 'Level Up',
                          slot: 1,
                          species: 3
                        }
                      ]
                    }
                  : pokemon
              )
            }
          }),
          updatePokemonEvolution
        }}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Pokemon' }));

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    const evolutionsBlock = screen
      .getByRole('heading', { level: 4, name: 'Evolutions' })
      .closest('.inspector-block') as HTMLElement | null;
    expect(evolutionsBlock).not.toBeNull();
    const levelInputs = within(evolutionsBlock!).getAllByLabelText('Level');
    const levelInput = levelInputs[levelInputs.length - 1]!;
    await user.clear(levelInput);
    await user.type(levelInput, '24');

    await user.click(within(evolutionsBlock!).getByRole('button', { name: /003 Venusaur/ }));
    await user.click(within(evolutionsBlock!).getByRole('button', { name: /002 Ivysaur/ }));

    const restoredLevelInputs = within(evolutionsBlock!).getAllByLabelText('Level');
    expect(restoredLevelInputs[restoredLevelInputs.length - 1]).toHaveValue(24);

    await user.click(screen.getByRole('button', { name: 'Save Changes' }));

    await waitFor(() => expect(updatePokemonEvolution).toHaveBeenCalled());
    expect(updatePokemonEvolution).toHaveBeenCalledWith(
      expect.objectContaining({
        action: 'upsert',
        level: 24,
        personalId: 1,
        slot: 0
      })
    );
  });

  it('confirms Pokemon EXP and EV Yield bulk actions before staging them', async () => {
    const baseBridge = createMockProjectBridge({}, true);
    const updatePokemonField = vi.fn(baseBridge.updatePokemonField);
    const user = userEvent.setup();
    render(
      <App
        bridge={{
          ...baseBridge,
          updatePokemonField
        }}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Pokemon' }));

    await user.click(await screen.findByRole('button', { name: 'Remove EXP Yield' }));

    expect(await screen.findByRole('dialog', { name: 'Remove EXP Yield?' })).toBeInTheDocument();
    expect(
      screen.getByText(
        'Remove EXP Yield will set every Pokemon Base EXP yield to 0. This stages one pending Pokemon change and does not write files until you review and save it from Changes.'
      )
    ).toBeInTheDocument();
    expect(updatePokemonField).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog', { name: 'Remove EXP Yield?' })).not.toBeInTheDocument();
    expect(updatePokemonField).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Remove EXP Yield' }));
    await user.click(await screen.findByRole('button', { name: 'Confirm Remove EXP Yield' }));

    await waitFor(() => expect(updatePokemonField).toHaveBeenCalledTimes(1));
    expect(updatePokemonField).toHaveBeenLastCalledWith(
      expect.objectContaining({
        field: 'expYieldAll',
        personalId: 0,
        value: 'remove'
      })
    );

    await user.click(screen.getByRole('button', { name: 'Restore EXP Yield' }));

    expect(await screen.findByRole('dialog', { name: 'Restore EXP Yield?' })).toBeInTheDocument();
    expect(
      screen.getByText(
        'Restore EXP Yield will copy every Pokemon Base EXP yield back from vanilla personal data. Any custom EXP yields currently staged or already in the output will be overwritten and are not restorable from KM Editor after this is saved.'
      )
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Confirm Restore EXP Yield' }));

    await waitFor(() => expect(updatePokemonField).toHaveBeenCalledTimes(2));
    expect(updatePokemonField).toHaveBeenLastCalledWith(
      expect.objectContaining({
        field: 'expYieldAll',
        personalId: 0,
        value: 'restore'
      })
    );

    await user.click(await screen.findByRole('button', { name: 'Remove EV Yield' }));

    expect(await screen.findByRole('dialog', { name: 'Remove EV Yield?' })).toBeInTheDocument();
    expect(
      screen.getByText(
        'Remove EV Yield will set every EV yield stat on every Pokemon to 0. This stages one pending Pokemon change and does not write files until you review and save it from Changes.'
      )
    ).toBeInTheDocument();
    expect(updatePokemonField).toHaveBeenCalledTimes(2);

    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog', { name: 'Remove EV Yield?' })).not.toBeInTheDocument();
    expect(updatePokemonField).toHaveBeenCalledTimes(2);

    await user.click(screen.getByRole('button', { name: 'Remove EV Yield' }));
    await user.click(await screen.findByRole('button', { name: 'Confirm Remove EV Yield' }));

    await waitFor(() => expect(updatePokemonField).toHaveBeenCalledTimes(3));
    expect(updatePokemonField).toHaveBeenLastCalledWith(
      expect.objectContaining({
        field: 'evYieldAll',
        personalId: 0,
        value: 'remove'
      })
    );

    await user.click(screen.getByRole('button', { name: 'Restore EV Yield' }));

    expect(await screen.findByRole('dialog', { name: 'Restore EV Yield?' })).toBeInTheDocument();
    expect(
      screen.getByText(
        'Restore EV Yield will copy every Pokemon EV yield back from vanilla personal data. Any custom EV yields currently staged or already in the output will be overwritten and are not restorable from KM Editor after this is saved.'
      )
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Confirm Restore EV Yield' }));

    await waitFor(() => expect(updatePokemonField).toHaveBeenCalledTimes(4));
    expect(updatePokemonField).toHaveBeenLastCalledWith(
      expect.objectContaining({
        field: 'evYieldAll',
        personalId: 0,
        value: 'restore'
      })
    );
  });

  it('opens Moves, searches records, and shows selected details', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge()} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Moves' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Moves' })).toBeInTheDocument();
    expect(screen.getAllByText('Tackle').length).toBeGreaterThan(0);
    expect(screen.getByText('Makes Contact')).toBeInTheDocument();

    await user.clear(screen.getByLabelText('Search moves'));
    await user.type(screen.getByLabelText('Search moves'), 'burn');

    expect(screen.queryByText('Tackle')).not.toBeInTheDocument();
    expect(screen.getAllByText('Ember').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Fire').length).toBeGreaterThan(0);
    expect(screen.getByText('Burn')).toBeInTheDocument();
    expect(screen.getByText('romfs/bin/pml/waza/waza_052.bin')).toBeInTheDocument();
    expect(screen.getByText('Base only')).toBeInTheDocument();
  });

  it('starts a Moves edit session, saves a power change, reviews a move plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Moves' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Moves' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Edit' }));
    const powerInput = screen.getByLabelText('Power');
    await user.clear(powerInput);
    await user.type(powerInput, '80');
    await user.click(screen.getByRole('button', { name: 'Save Move' }));

    expect(await screen.findByDisplayValue('80')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Tackle power to 80.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Changes' }));

    expect(await screen.findByText('Pending move change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('heading', { name: 'Output Plan' })).toBeInTheDocument();
    expect(screen.getAllByText('romfs/bin/pml/waza/waza_033.bin').length).toBeGreaterThan(0);

    expect(await screen.findByRole('heading', { name: 'Save Result' })).toBeInTheDocument();
    expect(
      screen.getByText('Applied Moves change plan to the configured LayeredFS output root.')
    ).toBeInTheDocument();
  });

  it('keeps large Items workflows in a bounded rendered row window', async () => {
    const user = userEvent.setup();
    const baseBridge = createMockProjectBridge();
    const baseItemsResponse = await baseBridge.loadItemsWorkflow({
      paths: {
        baseExeFsPath: 'base-exefs',
        baseRomFsPath: 'base-romfs',
        outputRootPath: null,
        saveFilePath: null,
        selectedGame: 'sword'
      }
    });
    const seedItem = baseItemsResponse.workflow.items[0]!;
    const largeItems = Array.from({ length: 1_000 }, (_, index) => ({
      ...seedItem,
      alternatePrice: index + 3,
      buyPrice: index * 10,
      category: index % 2 === 0 ? 'Medicine' : 'Battle',
      itemId: index,
      name: `Item ${index.toString().padStart(4, '0')}`,
      sellPrice: index * 5,
      sharedItemIds: [index],
      wattsPrice: index
    }));
    const bridge: ProjectBridge = {
      ...baseBridge,
      loadItemsWorkflow: async () => ({
        workflow: {
          ...baseItemsResponse.workflow,
          items: largeItems,
          stats: {
            ...baseItemsResponse.workflow.stats,
            totalItemCount: largeItems.length
          }
        }
      })
    };

    render(<App bridge={bridge} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Items' }));

    expect((await screen.findAllByText('Item 0001')).length).toBeGreaterThan(0);
    expect(screen.queryByText('Item 0000')).not.toBeInTheDocument();
    expect(screen.queryByText('Item 0999')).not.toBeInTheDocument();
    expect(screen.getAllByRole('row').length).toBeLessThan(100);

    await user.type(screen.getByLabelText('Search items'), 'Item 0999');

    expect((await screen.findAllByText('Item 0999')).length).toBeGreaterThan(0);
    expect(screen.getAllByRole('row').length).toBeLessThan(20);
  });

  it('lazy loads workflow data from direct section navigation without stealing focus', async () => {
    const user = userEvent.setup();
    const baseBridge = createMockProjectBridge();
    let loadItemsCount = 0;
    let lastRequest: Parameters<ProjectBridge['loadItemsWorkflow']>[0] | null = null;
    let resolveItemsWorkflow!: (
      value: Awaited<ReturnType<ProjectBridge['loadItemsWorkflow']>>
    ) => void;
    const itemsWorkflowPromise = new Promise<
      Awaited<ReturnType<ProjectBridge['loadItemsWorkflow']>>
    >((resolve) => {
      resolveItemsWorkflow = resolve;
    });
    const bridge: ProjectBridge = {
      ...baseBridge,
      loadItemsWorkflow: (request) => {
        loadItemsCount += 1;
        lastRequest = request;
        return itemsWorkflowPromise;
      }
    };
    render(
      <App bridge={bridge} />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Items' }));

    expect(await screen.findByText('Loading backend workflow data.')).toBeInTheDocument();
    expect(loadItemsCount).toBe(1);

    await user.click(screen.getByRole('button', { name: 'Project Setup' }));
    await act(async () => {
      resolveItemsWorkflow(await baseBridge.loadItemsWorkflow(lastRequest!));
    });

    expect(screen.getByRole('heading', { name: 'Project Setup' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Items' }));

    expect(screen.getAllByText('Potion').length).toBeGreaterThan(0);
    expect(loadItemsCount).toBe(1);
  });

  it('previews a spreadsheet import into an Items edit session', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Tools' }));
    await user.click(screen.getByRole('button', { name: 'Spreadsheet Import' }));

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Spreadsheet Import' })
    ).toBeInTheDocument();
    expect(screen.getAllByText('Items Price CSV/TSV').length).toBeGreaterThan(0);

    await user.type(screen.getByLabelText('CSV or TSV source path'), 'items.csv');
    await user.click(screen.getByRole('button', { name: 'Preview Import' }));

    expect(await screen.findByText('Potion: Buy price -> 450.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Changes' }));
    expect(screen.getByText('Set Potion buy price to 450.')).toBeInTheDocument();
  });

  it('starts an Items edit session, saves a pending buy price, validates it, reviews a plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Items' }));
    await user.click(await screen.findByRole('button', { name: 'Edit' }));

    const buyPriceInput = screen.getByLabelText('Buy price');
    expect(screen.getByLabelText('Sell price')).toBeInTheDocument();
    await user.clear(buyPriceInput);
    await user.type(buyPriceInput, '450');
    await user.click(screen.getByRole('button', { name: 'Save Item' }));

    expect(await screen.findByDisplayValue('450')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Potion buy price to 450.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Changes' }));

    expect(await screen.findByText('Pending item change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('heading', { name: 'Output Plan' })).toBeInTheDocument();
    expect(screen.getAllByText('romfs/bin/pml/item/item.dat').length).toBeGreaterThan(0);

    expect(await screen.findByRole('heading', { name: 'Save Result' })).toBeInTheDocument();
    expect(screen.getByText('Applied Items change plan to the configured LayeredFS output root.')).toBeInTheDocument();
  });

  it('asks before canceling editor changes and preserves drafts when declined', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Items' }));
    await user.click(await screen.findByRole('button', { name: 'Edit' }));

    const buyPriceInput = screen.getByLabelText('Buy price');
    await user.clear(buyPriceInput);
    await user.type(buyPriceInput, '450');
    await user.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(
      await screen.findByRole('dialog', { name: 'Discard All Changes?' })
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'No' }));

    expect(screen.queryByRole('dialog', { name: 'Discard All Changes?' })).not.toBeInTheDocument();
    expect(screen.getByLabelText('Buy price')).toHaveDisplayValue('450');
    expect(screen.getByRole('button', { name: 'Save Item' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    await user.click(await screen.findByRole('button', { name: 'Yes, Discard' }));

    expect(screen.queryByRole('dialog', { name: 'Discard All Changes?' })).not.toBeInTheDocument();
    expect(screen.getByLabelText('Buy price')).toHaveDisplayValue('300');
    expect(screen.getByRole('button', { name: 'Edit' })).toBeInTheDocument();
  });

  it('reloads imported editor data after canceling already-staged pending changes', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Items' }));
    await user.click(await screen.findByRole('button', { name: 'Edit' }));

    const buyPriceInput = screen.getByLabelText('Buy price');
    await user.clear(buyPriceInput);
    await user.type(buyPriceInput, '450');
    await user.click(screen.getByRole('button', { name: 'Save Item' }));

    expect(await screen.findByDisplayValue('450')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    await user.click(await screen.findByRole('button', { name: 'Yes, Discard' }));

    await waitFor(() => expect(screen.getByLabelText('Buy price')).toHaveDisplayValue('300'));
    expect(screen.getByRole('button', { name: 'Edit' })).toBeInTheDocument();
  });

  it('saves item metadata edits from backend-provided selectors', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Items' }));
    await user.click(await screen.findByRole('button', { name: 'Edit' }));

    const pouchInput = screen.getByLabelText('Pouch');
    await user.clear(pouchInput);
    await user.type(pouchInput, '4');
    await user.click(screen.getByRole('button', { name: 'Save Item' }));

    expect(await screen.findByText('Items (4)')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Changes' }));
    expect(screen.getByText('Set Potion pouch to 4.')).toBeInTheDocument();
  });

  it('opens Text from Viewers with editable text control helpers', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Viewers' }));
    await user.click(screen.getByRole('button', { name: 'Text' }));

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Text and Dialogue Map' })
    ).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/message/English/common/story.dat').length
    ).toBeGreaterThan(0);

    expect(screen.getByLabelText('Text value')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Edit' })).not.toBeDisabled();

    await user.click(screen.getByRole('button', { name: 'Edit' }));

    const textValue = screen.getByLabelText('Text value') as HTMLTextAreaElement;
    expect(textValue).not.toBeDisabled();
    expect(screen.getByRole('button', { name: 'Insert Line break \\n' })).not.toBeDisabled();
    expect(screen.getByRole('button', { name: 'Insert Wait + clear \\c\\n' })).not.toBeDisabled();
    expect(screen.getByRole('button', { name: 'Insert Wait + scroll \\r\\n' })).not.toBeDisabled();

    await user.click(screen.getByRole('button', { name: 'Insert Line break \\n' }));
    await user.click(screen.getByRole('button', { name: 'Insert Wait + clear \\c\\n' }));
    await user.click(screen.getByRole('button', { name: 'Insert Wait + scroll \\r\\n' }));

    expect(textValue.value).toContain('\\n');
    expect(textValue.value).toContain('\\c\\n');
    expect(textValue.value).toContain('\\r\\n');
  });

  it('opens Trainers, edits a party level, reviews a trainer plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Trainers' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Trainers' })).toBeInTheDocument();
    expect(screen.getAllByText('Avery').length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: /Grookey/ })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    expect(screen.getByLabelText('Trainer class ID')).toHaveDisplayValue('005 Pokemon Trainer');
    expect(screen.getByLabelText('Class ball')).toHaveDisplayValue('4 Poke Ball');
    expect(screen.getByLabelText('Battle type')).toHaveDisplayValue('1 Doubles');
    expect(screen.getByLabelText('Trainer item 1 ID')).toHaveDisplayValue('001 Potion');
    expect(screen.getByText('AI Flags')).toBeInTheDocument();
    expect(screen.getByLabelText(/Basic/)).toBeChecked();
    expect(screen.getByLabelText(/Fire Gym \(1\)/)).not.toBeChecked();
    expect(screen.getByLabelText(/Fire Gym \(2\)/)).not.toBeChecked();
    expect(screen.getByLabelText(/Fire Gym \(3\)/)).not.toBeChecked();
    expect(screen.getByLabelText('Heal flag')).toHaveDisplayValue('Yes');
    expect(screen.getByLabelText('Prize money')).toHaveDisplayValue('$1,152 (rate 24)');
    expect(screen.getByLabelText('Gift ID')).toHaveDisplayValue('007 Rare Candy');
    expect(screen.getByLabelText('Gift ID')).toBeDisabled();
    expect(screen.getByLabelText('Species ID')).toHaveDisplayValue('810 Grookey');
    expect(screen.getByLabelText('Held item ID')).toHaveDisplayValue('001 Potion');
    expect(screen.getByLabelText('Move 1 ID')).toHaveDisplayValue('001 Scratch');
    expect(screen.getByLabelText('Gender')).toHaveDisplayValue('Male');
    expect(screen.getByLabelText('Ability')).toHaveDisplayValue('Ability 2 - 065 Overgrow');
    expect(screen.getByLabelText('Nature')).toHaveDisplayValue('Jolly (+Spe/-Sp.Atk)');
    expect(screen.getByLabelText('Can Gigantamax')).toHaveDisplayValue('Yes');
    expect(screen.getByLabelText('Can Dynamax')).toHaveDisplayValue('No');
    expect(screen.getByLabelText('Dynamax level')).toBeDisabled();
    expect(screen.getByLabelText('Can Gigantamax')).toBeDisabled();
    await user.selectOptions(screen.getByLabelText('Can Dynamax'), '1');
    expect(screen.getByLabelText('Dynamax level')).not.toBeDisabled();
    expect(screen.getByLabelText('Can Gigantamax')).not.toBeDisabled();
    await user.selectOptions(screen.getByLabelText('Can Dynamax'), '0');
    expect(screen.getByLabelText('Dynamax level')).toBeDisabled();
    expect(screen.getByLabelText('Can Gigantamax')).toBeDisabled();
    const abilityInput = screen.getByLabelText('Ability');
    await user.clear(abilityInput);
    expect(screen.getByText('Enter a value.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Show Ability options' }));
    await user.click(screen.getByRole('option', { name: 'Ability 2 - 065 Overgrow' }));
    await user.click(screen.getByRole('button', { name: 'Show Gender options' }));
    expect(screen.getByRole('option', { name: 'Random' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Female' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Genderless' })).toBeInTheDocument();
    await user.click(screen.getByRole('option', { name: 'Male' }));
    await user.click(screen.getByRole('button', { name: 'Show Nature options' }));
    expect(screen.getByRole('option', { name: 'Hardy' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Adamant (+Atk/-Sp.Atk)' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Serious' })).toBeInTheDocument();
    await user.click(screen.getByRole('option', { name: 'Jolly (+Spe/-Sp.Atk)' }));
    const natureInput = screen.getByLabelText('Nature');
    await user.clear(natureInput);
    await user.type(natureInput, 'Ser');
    expect(screen.getByRole('option', { name: 'Serious' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Show Nature options' }));
    expect(screen.getByRole('option', { name: 'Hardy' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Adamant (+Atk/-Sp.Atk)' })).toBeInTheDocument();
    await user.click(screen.getByRole('option', { name: 'Jolly (+Spe/-Sp.Atk)' }));
    const levelInput = screen.getByLabelText('Level');
    await user.clear(levelInput);
    await user.type(levelInput, '25');
    expect(screen.getByLabelText('Prize money')).toHaveDisplayValue('$2,400 (rate 24)');
    await user.click(screen.getByRole('button', { name: 'Save Pokemon' }));

    expect(await screen.findByDisplayValue('25')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Avery slot 1 level to 25.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Changes' }));

    expect(await screen.findByText('Pending trainer change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('heading', { name: 'Output Plan' })).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/trainer/trainer_poke/trainer_010.bin').length
    ).toBeGreaterThan(0);

    expect(await screen.findByRole('heading', { name: 'Save Result' })).toBeInTheDocument();
    expect(
      screen.getByText('Applied Trainers change plan to the configured LayeredFS output root.')
    ).toBeInTheDocument();
  }, 10000);

  it('keeps unsaved trainer and party Pokemon drafts when selecting other trainer records', async () => {
    const baseBridge = createMockProjectBridge({}, true);
    const trainersResponse = await baseBridge.loadTrainersWorkflow({
      paths: {
        baseExeFsPath: 'base-exefs',
        baseRomFsPath: 'base-romfs',
        outputRootPath: 'output',
        saveFilePath: null,
        selectedGame: 'sword'
      }
    });
    const avery = trainersResponse.workflow.trainers[0]!;
    const secondPartyPokemon = {
      ...avery.team[0]!,
      abilityOptions: [
        { label: 'Default - 007 Torrent', value: 0 },
        { label: 'Ability 1 - 007 Torrent', value: 1 },
        { label: 'Ability 2 - 007 Torrent', value: 2 },
        { label: 'Hidden Ability - 000 None', value: 3 }
      ],
      level: 8,
      slot: 2,
      species: 'Sobble',
      speciesId: 816
    };
    const hop = {
      ...avery,
      location: 'Trainer 11',
      name: 'Hop',
      team: [{ ...avery.team[0]!, level: 5, slot: 1, species: 'Scorbunny', speciesId: 813 }],
      trainerId: 11
    };
    const workflow = {
      ...trainersResponse.workflow,
      stats: {
        ...trainersResponse.workflow.stats,
        totalPokemonCount: 3,
        totalTrainerCount: 2
      },
      trainers: [
        { ...avery, team: [avery.team[0]!, secondPartyPokemon] },
        hop
      ]
    };
    const user = userEvent.setup();
    render(
      <App
        bridge={{
          ...baseBridge,
          loadTrainersWorkflow: async () => ({ workflow })
        }}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Trainers' }));
    await user.click(screen.getByRole('button', { name: 'Edit' }));

    await user.selectOptions(screen.getByLabelText('Heal flag'), '0');
    const levelInput = screen.getByLabelText('Level');
    await user.clear(levelInput);
    await user.type(levelInput, '25');

    await user.click(screen.getByRole('button', { name: /Sobble/ }));
    const secondLevelInput = screen.getByLabelText('Level');
    await user.clear(secondLevelInput);
    await user.type(secondLevelInput, '30');

    await user.click(screen.getByRole('row', { name: /11Hop/ }));
    expect(screen.getByLabelText('Heal flag')).toHaveDisplayValue('Yes');
    expect(screen.getByLabelText('Level')).toHaveDisplayValue('5');

    await user.click(screen.getByRole('row', { name: /10Avery/ }));
    expect(screen.getByLabelText('Heal flag')).toHaveDisplayValue('No');
    expect(screen.getByLabelText('Level')).toHaveDisplayValue('25');

    await user.click(screen.getByRole('button', { name: /Sobble/ }));
    expect(screen.getByLabelText('Level')).toHaveDisplayValue('30');
  });

  it('warns before switching editors with unsaved local editor drafts', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Trainers' }));
    await user.click(screen.getByRole('button', { name: 'Edit' }));

    const levelInput = screen.getByLabelText('Level');
    await user.clear(levelInput);
    await user.type(levelInput, '25');

    await user.click(screen.getByRole('button', { name: 'Items' }));

    expect(await screen.findByRole('dialog', { name: 'Switch Editors?' })).toBeInTheDocument();
    expect(
      screen.getByText(
        'This editor has unsaved changes. Switching editors now will revert those edits.'
      )
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Stay Here' }));
    expect(screen.getByRole('heading', { level: 2, name: 'Trainers' })).toBeInTheDocument();
    expect(screen.getByLabelText('Level')).toHaveDisplayValue('25');

    await user.click(screen.getByRole('button', { name: 'Items' }));
    await user.click(await screen.findByRole('button', { name: 'Switch and Revert' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Items' })).toBeInTheDocument();
  });

  it('warns before switching editors with an active edit session', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    await user.click(screen.getByRole('button', { name: 'Wild Encounters' }));

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Wild Encounters' })
    ).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Edit' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Items' }));

    expect(await screen.findByRole('dialog', { name: 'Switch Editors?' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Stay Here' }));
    expect(screen.getByRole('heading', { level: 2, name: 'Wild Encounters' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Items' }));
    await user.click(await screen.findByRole('button', { name: 'Switch and Revert' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Items' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Edit' })).toBeInTheDocument();
  });

  it('allows reviewing staged changes in Changes and returning to the source editor', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Pokemon' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Pokemon' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Edit' }));
    const learnsetBlock = screen
      .getByRole('heading', { level: 4, name: 'Learnset' })
      .closest('.inspector-block') as HTMLElement | null;
    expect(learnsetBlock).not.toBeNull();

    await user.click(within(learnsetBlock!).getByRole('button', { name: 'Move learnset row down' }));
    expect(await within(learnsetBlock!).findByDisplayValue('033 Tackle')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Move Bulbasaur learnset slot 0 down.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Pokemon' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Pokemon' })).toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: 'Switch Editors?' })).not.toBeInTheDocument();
  });

  it('opens Gift Pokemon, edits IVs, reviews a gift plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    await user.click(screen.getByRole('button', { name: 'Gift Pokemon' }));

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Gift Pokemon' })
    ).toBeInTheDocument();
    expect(screen.getAllByText('Bulbasaur').length).toBeGreaterThan(0);
    expect(screen.getByText('3 guaranteed perfect IVs')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    expect(screen.getByLabelText('Ability slot')).toHaveDisplayValue('Ability 1 - 065 Overgrow');
    const giftIvPresetInput = screen.getByLabelText('IV preset');
    await user.clear(giftIvPresetInput);
    await user.type(giftIvPresetInput, 'Custom');
    await waitFor(() => expect(screen.getByLabelText('HP IV')).not.toBeDisabled());
    const hpIvInput = screen.getByLabelText('HP IV');
    expect(hpIvInput).toHaveDisplayValue('-4');
    await user.clear(hpIvInput);
    await user.type(hpIvInput, '80');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save Gift' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Save Gift' }));

    await waitFor(() => expect(screen.getByLabelText('HP IV')).toHaveDisplayValue('31'));

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Gift 001 ivHp to 31.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Changes' }));

    expect(await screen.findByText('Pending gift Pokemon change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('heading', { name: 'Output Plan' })).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/script_event_data/add_poke.bin').length
    ).toBeGreaterThan(0);

    expect(await screen.findByRole('heading', { name: 'Save Result' })).toBeInTheDocument();
    expect(
      screen.getByText('Applied Gift Pokemon change plan to the configured LayeredFS output root.')
    ).toBeInTheDocument();
  });

  it('opens Trade Pokemon, edits IVs, reviews a trade plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    await user.click(screen.getByRole('button', { name: 'Trade Pokemon' }));

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Trade Pokemon' })
    ).toBeInTheDocument();
    expect(screen.getAllByText('Meowth (Galarian)').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Farfetch’d (Galarian)').length).toBeGreaterThan(0);
    expect(screen.queryByText('Meowth-2')).not.toBeInTheDocument();
    expect(screen.queryByText('Farfetch’d-1')).not.toBeInTheDocument();
    expect(screen.getByText('3 guaranteed perfect IVs')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    const tradeIvPresetInput = screen.getByLabelText('IV preset');
    await user.clear(tradeIvPresetInput);
    await user.type(tradeIvPresetInput, 'Custom');
    await waitFor(() => expect(screen.getByLabelText('HP IV')).not.toBeDisabled());
    const hpIvInput = screen.getByLabelText('HP IV');
    expect(hpIvInput).toHaveDisplayValue('-4');
    await user.clear(hpIvInput);
    await user.type(hpIvInput, '-50');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save Trade' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Save Trade' }));

    await waitFor(() => expect(screen.getByLabelText('HP IV')).toHaveDisplayValue('0'));

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Trade 001 ivHp to 0.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Changes' }));

    expect(await screen.findByText('Pending trade Pokemon change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('heading', { name: 'Output Plan' })).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/script_event_data/field_trade.bin').length
    ).toBeGreaterThan(0);

    expect(await screen.findByRole('heading', { name: 'Save Result' })).toBeInTheDocument();
    expect(
      screen.getByText('Applied Trade Pokemon change plan to the configured LayeredFS output root.')
    ).toBeInTheDocument();
  });

  it('opens Static Encounters, edits IVs, reviews a static plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    await user.click(screen.getByRole('button', { name: 'Static Encounters' }));

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Static Encounters' })
    ).toBeInTheDocument();
    expect(screen.getAllByText('Grookey').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Calyrex').length).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    const staticIvPresetInput = screen.getByLabelText('IV preset');
    await user.clear(staticIvPresetInput);
    await user.type(staticIvPresetInput, 'Custom');
    await waitFor(() => expect(screen.getByLabelText('HP IV')).not.toBeDisabled());
    const hpIvInput = screen.getByLabelText('HP IV');
    expect(hpIvInput).toHaveDisplayValue('31');
    await user.clear(hpIvInput);
    await user.type(hpIvInput, '-50');
    await user.click(screen.getByRole('button', { name: 'Save Encounter' }));

    expect(await screen.findByDisplayValue('0')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Static 001 ivHp to 0.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Changes' }));

    expect(await screen.findByText('Pending static encounter change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('heading', { name: 'Output Plan' })).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/script_event_data/event_encount_data.bin').length
    ).toBeGreaterThan(0);

    expect(await screen.findByRole('heading', { name: 'Save Result' })).toBeInTheDocument();
    expect(
      screen.getByText(
        'Applied Static Encounter change plan to the configured LayeredFS output root.'
      )
    ).toBeInTheDocument();
  });

  it('hides unsafe Dynamax Adventures boss rows from the editor', async () => {
    const user = userEvent.setup();
    const health = createHealthForValidatedPaths('base-romfs', 'base-exefs', 'output', null);
    useWorkbenchStore.setState({
      activeSection: 'dynamaxAdventures',
      dynamaxAdventuresWorkflow: {
        diagnostics: [], editableFields: [
          { field: 'species', label: 'Species', maximumValue: 65535, minimumValue: 0, options: [{ label: '467 Magmortar', value: 467 }], valueKind: 'integer' }
        ],
        encounters: [{
          ability: 0, abilityLabel: 'Ability 1', abilityOptions: [], adventureIndex: 101,
          ballItem: 'Poke Ball', ballItemId: 4, bossTargetOptions: [],
          bossTargetSpecies: 'Pikachu', bossTargetSpeciesId: 25, entryIndex: 1, form: 0,
          gigantamaxLabel: 'Normal', gigantamaxOptions: [{ label: 'Normal', value: 1 }], gigantamaxState: 1, guaranteedPerfectIvs: 2,
          isEditable: true,
          isSingleCapture: false, isStoryProgressGated: false,
          ivs: { attack: -1, defense: -1, hp: -2, specialAttack: -1, specialDefense: -1, speed: -1 },
          ivSummary: '2 guaranteed perfect / Atk Random / Def Random / SpA Random / SpD Random / Spe Random',
          label: '001 / 101 - Pikachu', level: 60,
          moveOptions: [], moves: [], otGender: 1, otGenderLabel: 'Female',
          provenance: { fileState: 'baseOnly', sourceFile: 'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin', sourceLayer: 'base' },
          shinyRoll: 1, shinyRollLabel: 'Enabled', singleCaptureFlagBlock: '0x0000000000000001',
          species: 'Pikachu', speciesId: 25, uiMessageId: '0x0000000000000002',
          vanillaPokemon: null, version: 0, versionLabel: 'Both'
        }, {
          ability: 0, abilityLabel: 'Ability 1', abilityOptions: [], adventureIndex: 1003,
          ballItem: 'Poke Ball', ballItemId: 4, bossTargetOptions: [{ adventureIndex: 1004, entryIndex: 227, form: 0, isStoryProgressGated: false, label: 'Adventure 1004: Mewtwo', species: 'Mewtwo', speciesId: 150, version: 0, versionLabel: 'Both' }],
          bossTargetSpecies: 'Articuno', bossTargetSpeciesId: 144, entryIndex: 226, form: 0,
          gigantamaxLabel: 'Normal', gigantamaxOptions: [{ label: 'Normal', value: 1 }], gigantamaxState: 1, guaranteedPerfectIvs: 5,
          isEditable: false,
          isSingleCapture: true, isStoryProgressGated: false,
          ivs: { attack: -1, defense: -1, hp: -5, specialAttack: -1, specialDefense: -1, speed: -1 },
          ivSummary: '5 guaranteed perfect / Atk Random / Def Random / SpA Random / SpD Random / Spe Random',
          label: 'Adventure 1003: Articuno Lv. 70', level: 70,
          moveOptions: [], moves: [], otGender: 1, otGenderLabel: 'Female',
          provenance: { fileState: 'baseOnly', sourceFile: 'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin', sourceLayer: 'base' },
          shinyRoll: 1, shinyRollLabel: 'Enabled', singleCaptureFlagBlock: '0x00000000000000E2',
          species: 'Articuno', speciesId: 144, uiMessageId: '0x0000000000000100',
          vanillaPokemon: null, version: 0, versionLabel: 'Both'
        }],
        safeNormalSpeciesOptions: [],
        stats: { guaranteedPerfectIvEncounterCount: 1, singleCaptureCount: 1, sourceFileCount: 1, storyGatedCount: 0, totalEncounterCount: 1 },
        summary: { availability: 'available', description: 'Dynamax Adventures fixture.', diagnostics: [], id: 'dynamaxAdventures', label: 'Dynamax Adventures' }
      } as unknown as DynamaxAdventuresWorkflow,
      openProject: { fileGraph: { entries: [], summary: health.fileGraph }, health, projectId: 'project-1' },
      projectStatus: 'open',
      selectedDynamaxAdventureEntryIndex: 226
    });
    render(<App bridge={createMockProjectBridge({}, true)} />);

    expect(await screen.findByRole('heading', { level: 2, name: 'Dynamax Adventures' })).toBeInTheDocument();
    expect(screen.getAllByText('001 / 101 - Pikachu').length).toBeGreaterThan(0);
    expect(screen.queryByText('Adventure 1003: Articuno Lv. 70')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Stage Install' })).toBeDisabled();
    expect(screen.getByLabelText('Species')).not.toBeDisabled();
    expect(screen.queryByLabelText('Boss target species')).not.toBeInTheDocument();
  });

  it('refreshes Dynamax Adventures defaults after selecting a replacement species', async () => {
    const baseBridge = createMockProjectBridge({}, true);
    const previewDynamaxAdventureDefaults = vi.fn(
      (request: Parameters<ProjectBridge['previewDynamaxAdventureDefaults']>[0]) =>
        Promise.resolve({
          abilityOptions: [{ label: 'Ability 1 - Flame Body', value: 0 }],
          changes: [
            { field: 'form', value: '0' },
            { field: 'ability', value: '0' },
            { field: 'gigantamaxState', value: '1' },
            { field: 'move0Id', value: '85' },
            { field: 'move1Id', value: '10' },
            { field: 'move2Id', value: '2' },
            { field: 'move3Id', value: '1' }
          ],
          diagnostics: [],
          gigantamaxOptions: [{ label: 'Normal', value: 1 }],
          moveOptions: [{ label: '085 Thunderbolt', value: 85 }]
        })
    );
    const user = userEvent.setup();
    render(<App bridge={{ ...baseBridge, previewDynamaxAdventureDefaults }} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Advanced Editors' }));
    await user.click(screen.getByRole('button', { name: 'Dynamax Adventures' }));
    await user.click(await screen.findByRole('button', { name: 'Show Species options' }));
    await user.click(screen.getByRole('option', { name: '467 Magmortar' }));

    await waitFor(() =>
      expect(previewDynamaxAdventureDefaults).toHaveBeenCalledWith(
        expect.objectContaining({ entryIndex: 0, form: 0, level: 65, species: 467 })
      )
    );
    expect(screen.getByLabelText('Ability roll')).toHaveDisplayValue('Ability 1 - Flame Body');
    expect(screen.getByLabelText('Gigantamax state')).toHaveDisplayValue('Normal');
    expect(screen.getByLabelText('Gigantamax state')).toBeDisabled();
    expect(screen.getByText('Selected Pokemon does not have a Gigantamax form.')).toBeInTheDocument();
  });

  it('reviews and applies staged Dynamax Adventures edits from the editor panel', async () => {
    const baseBridge = createMockProjectBridge({}, true);
    const createChangePlan = vi.fn(
      async (request: Parameters<ProjectBridge['createChangePlan']>[0]) => {
        const response = await baseBridge.createChangePlan(request);

        return {
          changePlan: {
            ...response.changePlan,
            diagnostics: [
              {
                message: 'Dynamax Adventures review stays in the editor panel.',
                severity: 'info' as const
              }
            ]
          }
        };
      }
    );
    const applyChangePlan = vi.fn(baseBridge.applyChangePlan);
    const user = userEvent.setup();
    render(<App bridge={{ ...baseBridge, applyChangePlan, createChangePlan }} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Advanced Editors' }));
    await user.click(screen.getByRole('button', { name: 'Dynamax Adventures' }));
    expect(
      screen.getByText(/Stage and apply one Pokemon at a time/i)
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Ability choices may be limited by the selected row's table layout/i)
    ).toBeInTheDocument();
    await user.click(await screen.findByRole('button', { name: 'Show Species options' }));
    await user.click(screen.getByRole('option', { name: '467 Magmortar' }));

    const stageInstall = screen.getByRole('button', { name: 'Stage Install' });
    await waitFor(() => expect(stageInstall).toBeEnabled());
    await user.click(stageInstall);

    const review = screen.getByRole('button', { name: 'Review' });
    await waitFor(() => expect(review).toBeEnabled());
    await user.click(review);

    await waitFor(() => expect(createChangePlan).toHaveBeenCalled());
    expect(
      screen.getByText('Dynamax Adventures review stays in the editor panel.')
    ).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Changes' }));
    expect(
      screen.queryByText('Dynamax Adventures review stays in the editor panel.')
    ).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Dynamax Adventures' }));
    const apply = screen.getByRole('button', { name: 'Apply' });
    await waitFor(() => expect(apply).toBeEnabled());
    await user.click(apply);

    await waitFor(() => expect(applyChangePlan).toHaveBeenCalled());
    expect(applyChangePlan).toHaveBeenCalledWith(
      expect.objectContaining({
        changePlan: expect.objectContaining({
          writes: expect.arrayContaining([
            expect.objectContaining({
              targetRelativePath: 'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin'
            })
          ])
        }),
        session: expect.objectContaining({
          pendingEdits: expect.arrayContaining([
            expect.objectContaining({ domain: 'workflow.dynamaxAdventures' })
          ])
        })
      })
    );
  });

  it.each([
    {
      buttonName: 'Remove Gift Shiny Lock',
      expectedSummary: 'Set Gift 001 shinyLock to 0.',
      heading: 'Gift Pokemon',
      navName: 'Gift Pokemon'
    },
    {
      buttonName: 'Remove Trade Shiny Lock',
      expectedSummary: 'Set Trade 001 shinyLock to 0.',
      heading: 'Trade Pokemon',
      navName: 'Trade Pokemon'
    },
    {
      buttonName: 'Remove Static Shiny Lock',
      expectedSummary: 'Set Static 001 shinyLock to 0.',
      heading: 'Static Encounters',
      navName: 'Static Encounters'
    }
  ])('stages shiny-lock removal from $heading after confirmation', async ({
    buttonName,
    expectedSummary,
    heading,
    navName
  }) => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    await user.click(screen.getByRole('button', { name: navName }));

    expect(await screen.findByRole('heading', { level: 2, name: heading })).toBeInTheDocument();
    const removeButton = screen.getByRole('button', { name: buttonName });
    expect(removeButton).toBeDisabled();

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    await waitFor(() => expect(removeButton).toBeEnabled());
    await user.click(removeButton);

    const dialog = await screen.findByRole('dialog', { name: `${buttonName}?` });
    expect(within(dialog).getByText(/will be set to Random/)).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: buttonName }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    await waitFor(() => expect(removeButton).toBeDisabled());

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText(expectedSummary)).toBeInTheDocument();
  });

  it('hides shelved Rental Pokemon and shows Dynamax Adventures entry points', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    expect(screen.queryByRole('button', { name: 'Rental Pokemon' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Advanced Editors' }));
    expect(screen.getByRole('button', { name: 'Dynamax Adventures' })).toBeInTheDocument();
  });

  it('opens Shops, edits an inventory item, reviews a shop plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Economy' }));
    await user.click(screen.getByRole('button', { name: 'Shops' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Shops' })).toBeInTheDocument();
    expect(screen.getAllByText('Poke Mart').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Potion, Antidote').length).toBeGreaterThan(0);
    expect(screen.getByLabelText('Shop slot 1 item')).toHaveDisplayValue('0001 Potion (Medicine)');
    expect(screen.getByLabelText('Shop slot 1 price')).toHaveDisplayValue('300');
    expect(screen.getByLabelText('Shop slot 1 stock')).toHaveDisplayValue('None');
    expect(screen.getByLabelText('Shop slot 1 stock')).toBeDisabled();

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    expect(screen.getByLabelText('Shop slot 1 price')).toBeEnabled();
    expect(screen.getByLabelText('Shop slot 1 stock')).toBeDisabled();
    const itemSelect = screen.getByLabelText('Shop slot 1 item');
    await user.clear(itemSelect);
    await user.type(itemSelect, '2');
    expect(screen.queryByRole('button', { name: 'Save shop slot 1' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancel shop slot 1' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Save Changes' }));

    expect(await screen.findByLabelText('Shop slot 1 item')).toHaveDisplayValue(
      '0002 Antidote (Medicine)'
    );

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Poke Mart inventory order to 2 items.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Changes' }));

    expect(await screen.findByText('Pending shop change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('heading', { name: 'Output Plan' })).toBeInTheDocument();
    expect(screen.getAllByText('romfs/bin/appli/shop/bin/shop_data.bin').length).toBeGreaterThan(0);

    expect(await screen.findByRole('heading', { name: 'Save Result' })).toBeInTheDocument();
    expect(
      screen.getByText('Applied Shops change plan to the configured LayeredFS output root.')
    ).toBeInTheDocument();
  });

  it('reorders shop inventory rows before saving', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Economy' }));
    await user.click(screen.getByRole('button', { name: 'Shops' }));

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    await user.click(screen.getByRole('button', { name: 'Move shop slot 2 up' }));

    expect(screen.getByLabelText('Shop slot 1 item')).toHaveDisplayValue(
      '0002 Antidote (Medicine)'
    );
    expect(screen.getByLabelText('Shop slot 2 item')).toHaveDisplayValue(
      '0001 Potion (Medicine)'
    );

    await user.click(screen.getByRole('button', { name: 'Save Changes' }));
    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Poke Mart inventory order to 2 items.')).toBeInTheDocument();
  });

  it('removes None shop inventory rows when saving changes', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Economy' }));
    await user.click(screen.getByRole('button', { name: 'Shops' }));

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    const secondItemInput = screen.getByLabelText('Shop slot 2 item');
    await user.clear(secondItemInput);
    await user.type(secondItemInput, '0');

    expect(secondItemInput).toHaveDisplayValue('0');

    await user.click(screen.getByRole('button', { name: 'Save Changes' }));

    expect(await screen.findByLabelText('Shop slot 1 item')).toHaveDisplayValue(
      '0001 Potion (Medicine)'
    );
    expect(screen.queryByLabelText('Shop slot 2 item')).not.toBeInTheDocument();
  });

  it('edits a shop item price through the item buy price', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Economy' }));
    await user.click(screen.getByRole('button', { name: 'Shops' }));

    await user.click(await screen.findByRole('button', { name: 'Edit' }));
    const priceInput = screen.getByLabelText('Shop slot 1 price');

    expect(priceInput).toBeEnabled();
    expect(screen.getByLabelText('Shop slot 1 stock')).toBeDisabled();

    await user.clear(priceInput);
    await user.type(priceInput, '450');
    await user.click(screen.getByRole('button', { name: 'Save Changes' }));

    expect(await screen.findByLabelText('Shop slot 1 price')).toHaveDisplayValue('450');

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Potion buy price to 450.')).toBeInTheDocument();
    expect(screen.getAllByText('Items').length).toBeGreaterThan(0);
  });

  it('opens a linked shop inventory item in Items', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Economy' }));
    await user.click(screen.getByRole('button', { name: 'Shops' }));

    const openPotionButton = await screen.findByRole('button', { name: 'Open Potion in Items' });
    expect(openPotionButton).toHaveTextContent('Open in Items');
    expect(openPotionButton).toHaveAttribute('title', 'Open in Items');

    await user.click(openPotionButton);

    expect(await screen.findByRole('dialog', { name: 'Open in Items?' })).toBeInTheDocument();
    expect(
      screen.getByText(
        'Navigating out of Shops before pressing Save Changes will permanently discard unsaved inventory edits in this editor.'
      )
    ).toBeInTheDocument();

    const confirmOpenButton = screen.getByRole('button', { name: 'Open in Items' });
    expect(confirmOpenButton).toHaveClass('danger-button');
    await user.click(confirmOpenButton);

    expect(await screen.findByRole('heading', { level: 2, name: 'Items' })).toBeInTheDocument();
    expect(screen.getAllByText('Potion').length).toBeGreaterThan(0);
  });

  it('opens Encounters, edits a slot probability, reviews a wild data plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    await user.click(screen.getByRole('button', { name: 'Wild Encounters' }));

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Wild Encounters' })
    ).toBeInTheDocument();
    expect(screen.getAllByText('Zone 0x1122334455667788').length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: /#1.*Bulbasaur/ })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /#2.*Charmander/ }));
    await user.click(screen.getByRole('button', { name: 'Edit' }));
    const probabilityInput = screen.getByLabelText('Probability');
    await user.clear(probabilityInput);
    await user.type(probabilityInput, '40');
    await user.click(screen.getByRole('button', { name: 'Save Encounter' }));

    expect(await screen.findByDisplayValue('40')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(
      screen.getByText(
        'Set Sword Symbol Zone 0x1122334455667788 Normal slot 2 probability to 40.'
      )
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Changes' }));

    expect(await screen.findByText('Pending encounter change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('heading', { name: 'Output Plan' })).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/archive/field/resident/data_table.gfpak').length
    ).toBeGreaterThan(0);

    expect(await screen.findByRole('heading', { name: 'Save Result' })).toBeInTheDocument();
    expect(
      screen.getByText('Applied Wild Encounters change plan to the configured LayeredFS output root.')
    ).toBeInTheDocument();
  });

  it('applies encounter level drafts to the entire selected zone', async () => {
    const user = userEvent.setup();
    const conditionLabels = [
      'Overcast',
      'Raining',
      'Thunderstorm',
      'Intense Sun',
      'Snowing',
      'Snowstorm',
      'Sandstorm',
      'Heavy Fog',
      'Fishing',
      'Shaking Trees'
    ];
    const sourceWeights = [10, 15, 12, 8, 20, 5, 7, 11, 6, 6];
    const sourceSlots = sourceWeights.map((weight, index) => ({
      form: (index + 1) % 4,
      levelMax: 12,
      levelMin: 8,
      slot: index + 1,
      species: `Species ${101 + index}`,
      speciesId: 101 + index,
      timeOfDay: null,
      weather: 'Normal',
      weight
    }));
    const conditionTableIds = conditionLabels.map(
      (label, index) => `sword:symbol:0:1122334455667788:${index + 1}`
    );
    const workflow: EncountersWorkflow = {
      diagnostics: [],
      editableFields: [
        { field: 'levelMin', label: 'Min Level', maximumValue: 100, minimumValue: 1, valueKind: 'integer' },
        { field: 'levelMax', label: 'Max Level', maximumValue: 100, minimumValue: 1, valueKind: 'integer' }
      ],
      stats: {
        sourceFileCount: 1,
        totalSlotCount: 110,
        totalTableCount: 11
      },
      summary: {
        availability: 'available',
        description: 'Encounter tables, wild slots, levels, weather, and source provenance.',
        diagnostics: [],
        id: 'encounters',
        label: 'Wild Encounters'
      },
      tables: [
        {
          archiveMember: 'encount_symbol_k.bin',
          area: 'Symbol',
          encounterType: 'Normal',
          gameVersion: 'Sword',
          location: 'Rolling Fields',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/archive/field/resident/data_table.gfpak',
            sourceLayer: 'base'
          },
          slots: sourceSlots,
          tableId: 'sword:symbol:0:1122334455667788:0'
        },
        ...conditionLabels.map((label, index) => ({
          archiveMember: 'encount_symbol_k.bin',
          area: 'Symbol',
          encounterType: label,
          gameVersion: 'Sword',
          location: 'Rolling Fields',
          provenance: {
            fileState: 'baseOnly' as const,
            sourceFile: 'romfs/bin/archive/field/resident/data_table.gfpak',
            sourceLayer: 'base' as const
          },
          slots: Array.from({ length: 10 }, (_, slotIndex) => ({
            form: 0,
            levelMax: 10,
            levelMin: 5,
            slot: slotIndex + 1,
            species: 'Empty',
            speciesId: 0,
            timeOfDay: null,
            weather: label,
            weight: 0
          })),
          tableId: conditionTableIds[index]!
        }))
      ]
    };
    const updates: Array<Parameters<ProjectBridge['updateEncounterSlotField']>[0]> = [];
    const updateEncounterSlotField: ProjectBridge['updateEncounterSlotField'] = async (request) => {
      updates.push(request);

      return {
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            ...(request.session?.pendingEdits ?? []),
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
              summary: `Set ${request.tableId} slot ${request.slot} ${request.field} to ${request.value}.`
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-1'
        },
        workflow
      };
    };

    render(
      <App
        bridge={createMockProjectBridge(
          {
            loadEncountersWorkflow: () =>
              Promise.resolve({
                workflow
              }),
            updateEncounterSlotField
          },
          true
        )}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    await user.click(screen.getByRole('button', { name: 'Wild Encounters' }));

    expect((await screen.findAllByText('Rolling Fields')).length).toBeGreaterThan(0);
    expect(screen.getByRole('tab', { name: 'Fishing' })).toBeEnabled();
    expect(screen.getByRole('tab', { name: 'Shaking Trees' })).toBeEnabled();
    const applyToEntireZoneButton = screen.getByRole('button', {
      name: 'Apply to Entire Zone'
    });
    expect(applyToEntireZoneButton).toBeDisabled();

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    const minLevelInput = screen.getByLabelText('Min Level');
    const maxLevelInput = screen.getByLabelText('Max Level');
    await user.clear(minLevelInput);
    await user.type(minLevelInput, '9');
    await user.clear(maxLevelInput);
    await user.type(maxLevelInput, '15');
    expect(applyToEntireZoneButton).toBeEnabled();

    await user.click(applyToEntireZoneButton);

    await waitFor(() => expect(updates).toHaveLength(2));
    expect(updates.map((update) => update.tableId)).toEqual([
      'sword:symbol:0:1122334455667788:0',
      'sword:symbol:0:1122334455667788:0'
    ]);
    expect(updates.map((update) => update.slot)).toEqual([1, 1]);
    expect(updates.map((update) => update.field)).toEqual(['levelMin', 'levelMax']);
    expect(updates.map((update) => update.value)).toEqual(['9', '15']);
  });

  it('groups wild encounter rows by route and toggles symbol, hidden, and weather tables', async () => {
    const user = userEvent.setup();
    const makeTable = (
      area: string,
      encounterType: string,
      tableId: string,
      archiveMember: string,
      species: string,
      location = "Axew's Eye"
    ): EncounterTableRecord => ({
      archiveMember,
      area,
      encounterType,
      gameVersion: 'Sword',
      location,
      provenance: {
        fileState: 'baseOnly',
        sourceFile: 'romfs/bin/archive/field/resident/data_table.gfpak',
        sourceLayer: 'base'
      },
      slots: [
        {
          form: 0,
          levelMax: 12,
          levelMin: 8,
          slot: 1,
          species,
          speciesId: 25,
          timeOfDay: null,
          weather: encounterType,
          weight: 100
        }
      ],
      tableId
    });
    const workflow: EncountersWorkflow = {
      diagnostics: [],
      editableFields: [],
      stats: {
        sourceFileCount: 1,
        totalSlotCount: 5,
        totalTableCount: 5
      },
      summary: {
        availability: 'available',
        description: 'Encounter tables, wild slots, levels, weather, and source provenance.',
        diagnostics: [],
        id: 'encounters',
        label: 'Wild Encounters'
      },
      tables: [
        makeTable(
          'Symbol',
          'Normal',
          'sword:symbol:0:1122334455667788:0',
          'encount_symbol_k.bin',
          'Pikachu'
        ),
        makeTable(
          'Symbol',
          'Raining',
          'sword:symbol:0:1122334455667788:1',
          'encount_symbol_k.bin',
          'Raichu'
        ),
        makeTable(
          'Hidden',
          'Normal',
          'sword:hidden:2:8877665544332211:0',
          'encount_k.bin',
          'Eevee'
        ),
        makeTable(
          'Hidden',
          'Overcast',
          'sword:hidden:2:8877665544332211:1',
          'encount_k.bin',
          'Umbreon'
        ),
        makeTable(
          'Symbol',
          'Normal',
          'sword:symbol:7:ABCDEF0011223344:0',
          'encount_symbol_k.bin',
          'Lapras',
          "Axew's Eye (Surfing)"
        )
      ]
    };

    render(
      <App
        bridge={createMockProjectBridge(
          {
            loadEncountersWorkflow: () => Promise.resolve({ workflow })
          },
          true
        )}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    await user.click(screen.getByRole('button', { name: 'Wild Encounters' }));

    const encounterTable = await screen.findByRole('table', { name: 'Encounter tables' });
    expect(within(encounterTable).getAllByRole('row')).toHaveLength(3);
    expect(
      within(encounterTable)
        .getAllByRole('columnheader')
        .map((header) => header.textContent)
    ).toEqual(['Location', 'Game', 'Areas']);
    expect(
      within(encounterTable).queryByRole('columnheader', { name: 'Weather' })
    ).not.toBeInTheDocument();
    expect(
      within(encounterTable).queryByRole('columnheader', { name: 'Slots' })
    ).not.toBeInTheDocument();
    expect(
      within(encounterTable).queryByRole('columnheader', { name: 'Member' })
    ).not.toBeInTheDocument();
    expect(within(encounterTable).getByText("Axew's Eye")).toBeInTheDocument();
    expect(within(encounterTable).getByText("Axew's Eye (Surfing)")).toBeInTheDocument();
    expect(within(encounterTable).getByText('Symbol / Hidden')).toBeInTheDocument();
    expect(within(encounterTable).queryByText('Normal')).not.toBeInTheDocument();
    expect(within(encounterTable).queryByText('Overcast')).not.toBeInTheDocument();
    expect(within(encounterTable).queryByText('Raining')).not.toBeInTheDocument();

    expect(screen.getByRole('tab', { name: 'Symbol' })).toHaveAttribute(
      'aria-selected',
      'true'
    );
    expect(screen.getByRole('tab', { name: 'Hidden' })).toBeEnabled();
    expect(screen.getByRole('tab', { name: 'Raining' })).toBeEnabled();
    expect(screen.getByText('sword:symbol:0:1122334455667788:0')).toBeInTheDocument();

    await user.click(screen.getByRole('tab', { name: 'Hidden' }));

    expect(screen.getByRole('tab', { name: 'Hidden' })).toHaveAttribute(
      'aria-selected',
      'true'
    );
    expect(screen.getByText('sword:hidden:2:8877665544332211:0')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Overcast' })).toBeEnabled();

    await user.click(screen.getByRole('tab', { name: 'Overcast' }));

    expect(screen.getByText('sword:hidden:2:8877665544332211:1')).toBeInTheDocument();
  });

  it('copies the selected symbol encounter table into hidden encounters after confirmation', async () => {
    const user = userEvent.setup();
    const speciesNames = new Map([
      [25, 'Pikachu'],
      [26, 'Raichu'],
      [133, 'Eevee'],
      [197, 'Umbreon']
    ]);
    const makeSlot = (
      slot: number,
      speciesId: number,
      form: number,
      levelMin: number,
      levelMax: number,
      weight: number
    ) => ({
      form,
      levelMax,
      levelMin,
      slot,
      species: speciesNames.get(speciesId) ?? `Species ${speciesId}`,
      speciesId,
      timeOfDay: null,
      weather: 'Normal',
      weight
    });
    const makeTable = (
      area: string,
      tableId: string,
      archiveMember: string,
      slots: ReturnType<typeof makeSlot>[]
    ): EncounterTableRecord => ({
      archiveMember,
      area,
      encounterType: 'Normal',
      gameVersion: 'Sword',
      location: "Axew's Eye",
      provenance: {
        fileState: 'baseOnly',
        sourceFile: 'romfs/bin/archive/field/resident/data_table.gfpak',
        sourceLayer: 'base'
      },
      slots,
      tableId
    });
    let workflow: EncountersWorkflow = {
      diagnostics: [],
      editableFields: [
        { field: 'speciesId', label: 'Species ID', maximumValue: 65535, minimumValue: 0, valueKind: 'integer' },
        { field: 'form', label: 'Form', maximumValue: 255, minimumValue: 0, valueKind: 'integer' },
        { field: 'probability', label: 'Probability', maximumValue: 100, minimumValue: 0, valueKind: 'integer' },
        { field: 'levelMin', label: 'Min Level', maximumValue: 100, minimumValue: 0, valueKind: 'integer' },
        { field: 'levelMax', label: 'Max Level', maximumValue: 100, minimumValue: 0, valueKind: 'integer' }
      ],
      stats: {
        sourceFileCount: 1,
        totalSlotCount: 4,
        totalTableCount: 3
      },
      summary: {
        availability: 'available',
        description: 'Encounter tables, wild slots, levels, weather, and source provenance.',
        diagnostics: [],
        id: 'encounters',
        label: 'Wild Encounters'
      },
      tables: [
        makeTable('Symbol', 'sword:symbol:0:1122334455667788:0', 'encount_symbol_k.bin', [
          makeSlot(1, 25, 2, 10, 15, 60),
          makeSlot(2, 26, 1, 12, 18, 40)
        ]),
        {
          ...makeTable('Symbol', 'sword:symbol:0:1122334455667788:1', 'encount_symbol_k.bin', [
            makeSlot(1, 26, 0, 20, 25, 100)
          ]),
          encounterType: 'Raining',
          slots: [makeSlot(1, 26, 0, 20, 25, 100)].map((slot) => ({
            ...slot,
            weather: 'Raining'
          }))
        },
        makeTable('Hidden', 'sword:hidden:0:1122334455667788:0', 'encount_k.bin', [
          makeSlot(1, 133, 0, 5, 8, 70)
        ])
      ]
    };
    const updates: Array<Parameters<ProjectBridge['updateEncounterSlotField']>[0]> = [];
    const updateEncounterSlotField: ProjectBridge['updateEncounterSlotField'] = async (request) => {
      updates.push(request);
      workflow = {
        ...workflow,
        tables: workflow.tables.map((table) =>
          table.tableId === request.tableId
            ? {
                ...table,
                slots: table.slots.map((slot) => {
                  if (slot.slot !== request.slot) {
                    return slot;
                  }

                  const nextValue = Number.parseInt(request.value, 10);
                  switch (request.field) {
                    case 'speciesId':
                      return {
                        ...slot,
                        species: speciesNames.get(nextValue) ?? slot.species,
                        speciesId: nextValue
                      };
                    case 'form':
                      return { ...slot, form: nextValue };
                    case 'probability':
                      return { ...slot, weight: nextValue };
                    case 'levelMin':
                      return { ...slot, levelMin: nextValue };
                    case 'levelMax':
                      return { ...slot, levelMax: nextValue };
                    default:
                      return slot;
                  }
                })
              }
            : table
        )
      };

      return {
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            ...(request.session?.pendingEdits ?? []),
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
              summary: `Set ${request.tableId} slot ${request.slot} ${request.field} to ${request.value}.`
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-1'
        },
        workflow
      };
    };

    render(
      <App
        bridge={createMockProjectBridge(
          {
            loadEncountersWorkflow: () => Promise.resolve({ workflow }),
            updateEncounterSlotField
          },
          true
        )}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    await user.click(screen.getByRole('button', { name: 'Wild Encounters' }));

    expect(await screen.findByText('sword:symbol:0:1122334455667788:0')).toBeInTheDocument();
    const applyToHiddenButton = screen.getByRole('button', { name: 'Apply to Hidden' });
    expect(applyToHiddenButton).toBeDisabled();

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    expect(applyToHiddenButton).toBeEnabled();
    await user.click(applyToHiddenButton);

    const dialog = await screen.findByRole('dialog', { name: /Apply to Hidden/ });
    expect(within(dialog).getByText(/from Symbol to Hidden/)).toBeInTheDocument();
    expect(within(dialog).getByText(/Skipped: Raining/)).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Apply to Hidden' }));

    await waitFor(() => expect(updates).toHaveLength(5));
    expect(updates.every((update) => update.tableId === 'sword:hidden:0:1122334455667788:0')).toBe(
      true
    );
    expect(updates.map((update) => update.field)).toEqual([
      'speciesId',
      'form',
      'probability',
      'levelMin',
      'levelMax'
    ]);
    expect(updates.map((update) => update.value)).toEqual([
      '25',
      '2',
      '60',
      '10',
      '15'
    ]);
    expect(screen.getByText('sword:hidden:0:1122334455667788:0')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Hidden' })).toHaveAttribute(
      'aria-selected',
      'true'
    );
    expect(screen.getByRole('button', { name: /#1.*Pikachu/ })).toBeInTheDocument();
    expect(screen.getByText('10-15 / 60%')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Apply to Symbol' })).toBeInTheDocument();
  });

  it('opens Raid Rewards, edits a star value, reviews a reward plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Economy' }));
    await user.click(screen.getByRole('button', { name: 'Raid Rewards' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Raid Rewards' })).toBeInTheDocument();
    expect(screen.getAllByText('0xAABBCCDD00112233').length).toBeGreaterThan(0);
    expect(screen.getByRole('option', { name: 'Slot 1: Exp. Candy L' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    const starValueInput = screen.getByLabelText('5-star drop chance');
    await user.clear(starValueInput);
    await user.type(starValueInput, '77');
    await user.click(screen.getByRole('button', { name: 'Save Reward' }));

    expect(await screen.findByDisplayValue('77')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(
      screen.getByText('Set Drop 0xAABBCCDD00112233 slot 1 5-star drop chance to 77.')
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Changes' }));

    expect(await screen.findByText('Pending raid reward change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('heading', { name: 'Output Plan' })).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/archive/field/resident/data_table.gfpak').length
    ).toBeGreaterThan(0);

    expect(await screen.findByRole('heading', { name: 'Save Result' })).toBeInTheDocument();
    expect(
      screen.getByText('Applied Raid Rewards change plan to the configured LayeredFS output root.')
    ).toBeInTheDocument();
  });

  it('opens Raid Battles, edits guaranteed perfect IVs, reviews a battle plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    await user.click(screen.getByRole('button', { name: 'Raid Battles' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Raid Battles' })).toBeInTheDocument();
    expect(screen.getAllByText('0xAABBCCDD00112233').length).toBeGreaterThan(0);
    expect(screen.getByRole('option', { name: 'Slot 1: Eevee (Form 1)' })).toBeInTheDocument();
    expect(screen.getByText('Any Ability')).toBeInTheDocument();
    expect(
      screen.getByText('Matched: 2 rewards: Exp. Candy L, Rare Candy (drop:0:AABBCCDD00112233)')
    ).toBeInTheDocument();
    expect(
      screen.getByText('Matched: 1 reward: Armorite Ore (bonus:0:1020304050607080)')
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    const raidBattleIvsInput = screen.getByLabelText('Guaranteed perfect IVs');
    await user.clear(raidBattleIvsInput);
    await user.type(raidBattleIvsInput, '6');
    await user.click(screen.getByRole('button', { name: 'Save Battle' }));

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(
      screen.getByText('Set Raid Battles 0xAABBCCDD00112233 slot 1 flawlessIvs to 6.')
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Changes' }));

    expect(await screen.findByText('Pending raid battle change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('heading', { name: 'Output Plan' })).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/archive/field/resident/data_table.gfpak').length
    ).toBeGreaterThan(0);

    expect(await screen.findByRole('heading', { name: 'Save Result' })).toBeInTheDocument();
    expect(
      screen.getByText('Applied Raid Battles change plan to the configured LayeredFS output root.')
    ).toBeInTheDocument();
  });

  it('limits Raid Battle form options to applicable named forms', async () => {
    const user = userEvent.setup();
    const rewardLink = {
      isMatched: false,
      preview: 'No loaded drop table matches this hash',
      rewardItemCount: 0,
      rewardKind: 'drop',
      rewardKindLabel: 'Drop',
      sourceTableHash: '0x0000000000000000',
      tableId: ''
    };
    const workflow: RaidBattlesWorkflow = {
      diagnostics: [],
      editableFields: [
        {
          field: 'form',
          label: 'Form',
          maximumValue: 255,
          minimumValue: 0,
          options: [
            { label: 'Base', value: 0 },
            { label: 'Form 1', value: 1 },
            { label: 'Form 2', value: 2 },
            { label: 'Form 3', value: 3 }
          ],
          valueKind: 'integer'
        }
      ],
      stats: {
        gigantamaxSlotCount: 0,
        sourceFileCount: 2,
        totalSlotCount: 3,
        totalTableCount: 1
      },
      summary: {
        availability: 'available',
        description: 'Raid Pokemon slots, star probabilities, ability rolls, guaranteed perfect IVs, and source provenance.',
        diagnostics: [],
        id: 'raidBattles',
        label: 'Raid Battles'
      },
      tables: [
        {
          denId: 'table_AABBCCDD00112233',
          displayName: 'Sword - 0',
          gameVersion: 'Sword',
          provenance: {
            fileState: 'baseOnly',
            sourceFile: 'romfs/bin/archive/field/resident/data_table.gfpak',
            sourceLayer: 'base'
          },
          slots: [
            {
              ability: 0,
              abilityLabel: 'Ability 1',
              abilityOptions: [],
              bonusTableHash: '0x0000000000000000',
              bonusRewardLink: { ...rewardLink, rewardKind: 'bonus', rewardKindLabel: 'Bonus' },
              dropTableHash: '0x0000000000000000',
              dropRewardLink: rewardLink,
              entryIndex: 0,
              flawlessIvs: 0,
              form: 0,
              formOptions: [
                { label: 'Base', value: 0 },
                { label: 'Galarian', value: 1 }
              ],
              gender: 0,
              genderLabel: 'Random',
              isGigantamax: false,
              levelTableHash: '0x0000000000000000',
              probabilities: [100, 0, 0, 0, 0],
              probabilitySummary: '1-star 100% / 2-star 0% / 3-star 0% / 4-star 0% / 5-star 0%',
              slot: 1,
              species: 'Weezing',
              speciesId: 110
            },
            {
              ability: 0,
              abilityLabel: 'Ability 1',
              abilityOptions: [],
              bonusTableHash: '0x0000000000000000',
              bonusRewardLink: { ...rewardLink, rewardKind: 'bonus', rewardKindLabel: 'Bonus' },
              dropTableHash: '0x0000000000000000',
              dropRewardLink: rewardLink,
              entryIndex: 1,
              flawlessIvs: 0,
              form: 0,
              formOptions: [
                { label: 'Base', value: 0 },
                { label: 'Form 1', value: 1 }
              ],
              gender: 1,
              genderLabel: 'Male',
              isGigantamax: false,
              levelTableHash: '0x0000000000000000',
              probabilities: [100, 0, 0, 0, 0],
              probabilitySummary: '1-star 100% / 2-star 0% / 3-star 0% / 4-star 0% / 5-star 0%',
              slot: 2,
              species: 'Indeedee',
              speciesId: 876
            },
            {
              ability: 0,
              abilityLabel: 'Ability 1',
              abilityOptions: [],
              bonusTableHash: '0x0000000000000000',
              bonusRewardLink: { ...rewardLink, rewardKind: 'bonus', rewardKindLabel: 'Bonus' },
              dropTableHash: '0x0000000000000000',
              dropRewardLink: rewardLink,
              entryIndex: 2,
              flawlessIvs: 0,
              form: 62,
              formOptions: [
                { label: 'Base', value: 0 },
                { label: 'Form 1', value: 1 },
                { label: 'Form 62', value: 62 }
              ],
              gender: 3,
              genderLabel: 'Genderless',
              isGigantamax: false,
              levelTableHash: '0x0000000000000000',
              probabilities: [100, 0, 0, 0, 0],
              probabilitySummary: '1-star 100% / 2-star 0% / 3-star 0% / 4-star 0% / 5-star 0%',
              slot: 3,
              species: 'Alcremie',
              speciesId: 869
            }
          ],
          sourceTableHash: '0xAABBCCDD00112233',
          tableId: 'raid:0:AABBCCDD00112233',
          tableIndex: 0
        }
      ]
    };

    render(
      <App
        bridge={createMockProjectBridge(
          {
            loadRaidBattlesWorkflow: () => Promise.resolve({ workflow })
          },
          true
        )}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    await user.click(screen.getByRole('button', { name: 'Raid Battles' }));
    await user.click(screen.getByRole('button', { name: 'Edit' }));

    expect(screen.getByLabelText('Form')).toHaveDisplayValue('Kanto');

    await user.click(screen.getByRole('button', { name: 'Show Form options' }));

    expect(screen.getByRole('option', { name: 'Kanto' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Galarian' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Form 2' })).not.toBeInTheDocument();

    await user.keyboard('{Escape}');
    await user.click(screen.getByRole('button', { name: /#2.*Indeedee/ }));

    expect(screen.getByLabelText('Form')).toHaveDisplayValue('Male');

    await user.click(screen.getByRole('button', { name: 'Show Form options' }));

    expect(screen.getByRole('option', { name: 'Male' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Female' })).toBeInTheDocument();
    expect(screen.queryByRole('option', { name: 'Form 1' })).not.toBeInTheDocument();

    await user.keyboard('{Escape}');
    await user.click(screen.getByRole('button', { name: /#3.*Alcremie/ }));

    expect(screen.getByLabelText('Form')).toHaveDisplayValue('Rainbow Swirl / Ribbon Sweet');

    await user.click(screen.getByRole('button', { name: 'Show Form options' }));

    expect(
      screen.getByRole('option', { name: 'Vanilla Cream / Strawberry Sweet' })
    ).toBeInTheDocument();
    expect(
      screen.getByRole('option', { name: 'Rainbow Swirl / Ribbon Sweet' })
    ).toBeInTheDocument();
  });

  it('opens Flagwork and Save Inspectors, searches real keys, and shows provenance', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge()} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Viewers' }));
    await user.click(screen.getByRole('button', { name: 'Flagwork / Save' }));

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
    expect(screen.getByText('main')).toBeInTheDocument();
    expect(screen.getByText('4 bytes')).toBeInTheDocument();
    expect(screen.getByText('01020304')).toBeInTheDocument();
  });

  it('opens Bag Hook from Hooks and shows reserved slots without empty owner copy', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge()} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Hooks' }));
    await user.click(screen.getByRole('button', { name: 'Bag Hook' }));

    expect(
      await screen.findByRole('heading', {
        level: 2,
        name: 'Bag Hook'
      })
    ).toBeInTheDocument();
    expect(screen.getAllByText('Royal Candy').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Starting Items').length).toBeGreaterThan(0);
    expect(screen.queryByText('Available for Starting Items')).not.toBeInTheDocument();
    expect(screen.getAllByText('romfs/bin/script/amx/main_event_0020.amx').length).toBeGreaterThan(0);
  });

  it('stages Bag Hook install for review and apply', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output-root');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Hooks' }));
    await user.click(screen.getByRole('button', { name: 'Bag Hook' }));
    await user.click(await screen.findByRole('button', { name: 'Stage Install' }));

    expect(await screen.findByText('Bag Hook V2 install is staged for change-plan review.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Changes' }));
    expect(screen.queryByText('Bag Hook V2 install is staged for change-plan review.')).not.toBeInTheDocument();

    expect(await screen.findByText('Stage Bag Hook install: 20 disabled startup item grant slots.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Bag Hook' }));
    await user.click(screen.getByRole('button', { name: 'Review' }));
    expect(await screen.findByText('Change plan preview contains 1 target file.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Apply' }));

    expect((await screen.findAllByText('romfs/bin/script/amx/main_event_0020.amx')).length).toBeGreaterThan(0);

    expect(await screen.findByText('Installed Bag Hook V2 to the configured LayeredFS output root.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Changes' }));
    expect(screen.queryByText('Installed Bag Hook V2 to the configured LayeredFS output root.')).not.toBeInTheDocument();
  });

  it('stages Bag Hook uninstall for review', async () => {
    const baseBridge = createMockProjectBridge({}, true);
    const stageBagHookUninstall = vi.fn(baseBridge.stageBagHookUninstall);
    const user = userEvent.setup();
    render(
      <App
        bridge={{
          ...baseBridge,
          loadBagHookWorkflow: async (request) => {
            const response = await baseBridge.loadBagHookWorkflow(request);
            return {
              workflow: {
                ...response.workflow,
                installMessage: 'Bag Hook V2 is installed.',
                installStatus: 'installed'
              }
            };
          },
          stageBagHookUninstall
        }}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output-root');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Hooks' }));
    await user.click(screen.getByRole('button', { name: 'Bag Hook' }));

    expect(await screen.findByRole('button', { name: 'Stage Uninstall' })).toBeEnabled();
    await user.click(screen.getByRole('button', { name: 'Stage Uninstall' }));

    await waitFor(() => expect(stageBagHookUninstall).toHaveBeenCalled());
    expect(await screen.findByText('Bag Hook V2 uninstall is staged for change-plan review.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Changes' }));
    expect(screen.queryByText('Bag Hook V2 uninstall is staged for change-plan review.')).not.toBeInTheDocument();

    expect(
      await screen.findByText(
        'Stage Bag Hook uninstall: remove Bag Hook plus dependent Royal Candy and Starting Items outputs.'
      )
    ).toBeInTheDocument();
  });

  it('shows Catch Cap editor entry points and applies from Advanced Editors', async () => {
    const baseBridge = createMockProjectBridge({}, true);
    const stageCatchCap = vi.fn(baseBridge.stageCatchCap);
    const createChangePlan = vi.fn(baseBridge.createChangePlan);
    const applyChangePlan = vi.fn(baseBridge.applyChangePlan);
    const user = userEvent.setup();
    render(
      <App
        bridge={{
          ...baseBridge,
          applyChangePlan,
          createChangePlan,
          stageCatchCap
        }}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output-root');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));

    await user.click(screen.getByRole('button', { name: 'Advanced Editors' }));
    expect(screen.getByRole('button', { name: 'Catch Cap' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Catch Cap' }));
    expect(
      await screen.findByRole('heading', {
        level: 2,
        name: 'Catch Cap Editor'
      })
    ).toBeInTheDocument();

    const finalBadgeInput = await screen.findByLabelText('Catch cap for 8 badges');
    expect(finalBadgeInput).toBeDisabled();
    expect(finalBadgeInput).toHaveValue(100);
    expect(screen.getByText('Locked: full badges catch any level.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Stage Caps' }));

    await waitFor(() => expect(stageCatchCap).toHaveBeenCalled());
    expect(await screen.findByText('Catch Cap Editor values are staged for change-plan review.')).toBeInTheDocument();
    expect(stageCatchCap).toHaveBeenCalledWith(
      expect.objectContaining({
        caps: expect.arrayContaining([{ badgeCount: 8, levelCap: 100 }])
      })
    );
    await user.click(screen.getByRole('button', { name: 'Changes' }));
    expect(screen.queryByText('Catch Cap Editor values are staged for change-plan review.')).not.toBeInTheDocument();
    expect(
      await screen.findByText(
        'Stage Catch Cap Editor values for badge counts 0-7 and the display/runtime hook; eight badges remains Lv.100.'
      )
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Catch Cap' }));
    await user.click(screen.getByRole('button', { name: 'Review' }));

    await waitFor(() => expect(createChangePlan).toHaveBeenCalled());
    expect(await screen.findByText('Change plan preview contains 1 target file.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Changes' }));
    expect(screen.queryByText('Change plan preview contains 1 target file.')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Catch Cap' }));
    await user.click(screen.getByRole('button', { name: 'Apply' }));

    await waitFor(() => expect(applyChangePlan).toHaveBeenCalled());
    expect(await screen.findByText('Applied Catch Cap Editor changes to the configured LayeredFS output root.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Changes' }));
    expect(screen.queryByText('Applied Catch Cap Editor changes to the configured LayeredFS output root.')).not.toBeInTheDocument();
    expect(applyChangePlan).toHaveBeenCalledWith(
      expect.objectContaining({
        changePlan: expect.objectContaining({
          writes: expect.arrayContaining([
            expect.objectContaining({ targetRelativePath: 'exefs/main' })
          ])
        }),
        session: expect.objectContaining({
          pendingEdits: expect.arrayContaining([
            expect.objectContaining({ domain: 'workflow.catchCap' })
          ])
        })
      })
    );
  });

  it('opens Hyper Training from Advanced Editors and stages a clamped cutoff', async () => {
    const baseBridge = createMockProjectBridge({}, true);
    const stageHyperTraining = vi.fn(baseBridge.stageHyperTraining);
    const createChangePlan = vi.fn(baseBridge.createChangePlan);
    const applyChangePlan = vi.fn(baseBridge.applyChangePlan);
    const user = userEvent.setup();
    render(
      <App
        bridge={{
          ...baseBridge,
          applyChangePlan,
          createChangePlan,
          stageHyperTraining
        }}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output-root');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Advanced Editors' }));
    expect(screen.getByRole('button', { name: 'Hyper Training' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Hyper Training' }));

    expect(
      await screen.findByRole('heading', {
        level: 2,
        name: 'Hyper Training'
      })
    ).toBeInTheDocument();
    expect(screen.getByText('Hyper Training is using the vanilla Lv.100 minimum.')).toBeInTheDocument();

    const cutoffInput = screen.getByLabelText('Cutoff');
    await user.clear(cutoffInput);
    await user.type(cutoffInput, '150');
    expect(cutoffInput).toHaveValue(100);
    await user.clear(cutoffInput);
    await user.type(cutoffInput, '42');
    expect(cutoffInput).toHaveValue(42);

    await user.click(screen.getByRole('button', { name: 'Stage Cutoff' }));

    await waitFor(() => expect(stageHyperTraining).toHaveBeenCalled());
    expect(stageHyperTraining).toHaveBeenCalledWith(
      expect.objectContaining({
        minimumLevel: 42
      })
    );

    await user.click(screen.getByRole('button', { name: 'Review' }));

    await waitFor(() => expect(createChangePlan).toHaveBeenCalled());
    await user.click(screen.getByRole('button', { name: 'Apply' }));

    await waitFor(() => expect(applyChangePlan).toHaveBeenCalled());
    expect(applyChangePlan).toHaveBeenCalledWith(
      expect.objectContaining({
        changePlan: expect.objectContaining({
          writes: expect.arrayContaining([
            expect.objectContaining({
              targetRelativePath: 'romfs/bin/script/amx/hyper_training.amx'
            }),
            expect.objectContaining({
              targetRelativePath: 'exefs/main'
            }),
            expect.objectContaining({
              targetRelativePath: 'romfs/bin/message/English/script/sub_event_007.dat'
            })
          ])
        }),
        session: expect.objectContaining({
          pendingEdits: expect.arrayContaining([
            expect.objectContaining({ domain: 'workflow.hyperTraining' })
          ])
        })
      })
    );
  });

  it('opens IV Screen from Advanced Editors and applies the install workflow', async () => {
    const baseBridge = createMockProjectBridge({}, true);
    const stageIvScreenInstall = vi.fn(baseBridge.stageIvScreenInstall);
    const createChangePlan = vi.fn(baseBridge.createChangePlan);
    const applyChangePlan = vi.fn(baseBridge.applyChangePlan);
    const user = userEvent.setup();
    render(
      <App
        bridge={{
          ...baseBridge,
          applyChangePlan,
          createChangePlan,
          stageIvScreenInstall
        }}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output-root');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Advanced Editors' }));
    await user.click(screen.getByRole('button', { name: 'IV Screen' }));

    expect(
      await screen.findByRole('heading', {
        level: 2,
        name: 'IV Screen'
      })
    ).toBeInTheDocument();
    expect(screen.getByText('IV Screen can patch exefs/main.')).toBeInTheDocument();
    expect(
      screen.getByText(
        /press X to toggle from normal stats to raw IV numbers/i
      )
    ).toBeInTheDocument();
    const reservedRangesTable = screen.getByRole('table', {
      name: 'IV Screen reserved ranges'
    });
    expect(
      within(reservedRangesTable).getByRole('columnheader', { name: 'Region' })
    ).toBeInTheDocument();
    expect(
      within(reservedRangesTable).getByRole('columnheader', { name: 'Range' })
    ).toBeInTheDocument();
    expect(
      within(reservedRangesTable).queryByRole('columnheader', { name: 'Rule' })
    ).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Stage Install' }));

    await waitFor(() => expect(stageIvScreenInstall).toHaveBeenCalled());
    await user.click(screen.getByRole('button', { name: 'Review' }));

    await waitFor(() => expect(createChangePlan).toHaveBeenCalled());
    await user.click(screen.getByRole('button', { name: 'Apply' }));

    await waitFor(() => expect(applyChangePlan).toHaveBeenCalled());
    expect(applyChangePlan).toHaveBeenCalledWith(
      expect.objectContaining({
        changePlan: expect.objectContaining({
          writes: expect.arrayContaining([
            expect.objectContaining({ targetRelativePath: 'exefs/main' })
          ])
        }),
        session: expect.objectContaining({
          pendingEdits: expect.arrayContaining([
            expect.objectContaining({ domain: 'workflow.ivScreen' })
          ])
        })
      })
    );
  });

  it('opens Gym Uniform Removal from Advanced Editors and applies the install workflow', async () => {
    const baseBridge = createMockProjectBridge({}, true);
    const stageGymUniformRemovalInstall = vi.fn(baseBridge.stageGymUniformRemovalInstall);
    const createChangePlan = vi.fn(baseBridge.createChangePlan);
    const applyChangePlan = vi.fn(baseBridge.applyChangePlan);
    const user = userEvent.setup();
    render(
      <App
        bridge={{
          ...baseBridge,
          applyChangePlan,
          createChangePlan,
          stageGymUniformRemovalInstall
        }}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output-root');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Advanced Editors' }));
    await user.click(screen.getByRole('button', { name: 'Gym Uniform Removal' }));

    expect(
      await screen.findByRole('heading', {
        level: 2,
        name: 'Gym Uniform Removal'
      })
    ).toBeInTheDocument();
    expect(screen.getByText('Gym Uniform Removal can create a build-ID IPS patch in exefs.')).toBeInTheDocument();
    expect(
      screen.getByText(/keeps gym challenge and gym leader battle scripts/i)
    ).toBeInTheDocument();
    const behaviorTable = screen.getByRole('table', {
      name: 'Gym Uniform Removal behavior summary'
    });
    expect(within(behaviorTable).getByText('Not installed')).toBeInTheDocument();
    expect(within(behaviorTable).getByText('Installed')).toBeInTheDocument();
    expect(
      within(behaviorTable).getByText(/outfit does not change/i)
    ).toBeInTheDocument();
    const reservedRangesTable = screen.getByRole('table', {
      name: 'Gym Uniform Removal reserved ranges'
    });
    expect(within(reservedRangesTable).getByText(/gym outfit handler override/i)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Stage Install' }));

    await waitFor(() => expect(stageGymUniformRemovalInstall).toHaveBeenCalled());
    await user.click(screen.getByRole('button', { name: 'Review' }));

    await waitFor(() => expect(createChangePlan).toHaveBeenCalled());
    await user.click(screen.getByRole('button', { name: 'Apply' }));

    await waitFor(() => expect(applyChangePlan).toHaveBeenCalled());
    expect(applyChangePlan).toHaveBeenCalledWith(
      expect.objectContaining({
        changePlan: expect.objectContaining({
          writes: expect.arrayContaining([
            expect.objectContaining({
              targetRelativePath: 'exefs/A3B75BCD3311385AEED67FBEEB79CBB7BF02F471.ips'
            })
          ])
        }),
        session: expect.objectContaining({
          pendingEdits: expect.arrayContaining([
            expect.objectContaining({ domain: 'workflow.gymUniformRemoval' })
          ])
        })
      })
    );
  });

  it('locks Starting Items key item quantities to one', async () => {
    const baseBridge = createMockProjectBridge({}, true);
    const stageStartingItems = vi.fn(baseBridge.stageStartingItems);
    const createChangePlan = vi.fn(baseBridge.createChangePlan);
    const applyChangePlan = vi.fn(baseBridge.applyChangePlan);
    const user = userEvent.setup();
    render(
      <App
        bridge={{
          ...baseBridge,
          applyChangePlan,
          createChangePlan,
          stageStartingItems
        }}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output-root');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Advanced Editors' }));
    await user.click(screen.getByRole('button', { name: 'Starting Items' }));

    expect(
      await screen.findByRole('heading', {
        level: 2,
        name: 'Starting Items'
      })
    ).toBeInTheDocument();
    expect(screen.queryByRole('columnheader', { name: 'Owner' })).not.toBeInTheDocument();
    expect(screen.queryByText('Available for Starting Items')).not.toBeInTheDocument();

    const slotItemInput = screen.getByLabelText('Item for Bag Hook slot 3');
    await user.clear(slotItemInput);
    await user.type(slotItemInput, 'Bike');
    await user.click(await screen.findByRole('option', { name: 'Bike (#700) [Key]' }));
    expect(screen.getByLabelText('Quantity for Bag Hook slot 3')).toBeDisabled();
    expect(screen.getByLabelText('Quantity for Bag Hook slot 3')).toHaveValue(1);
    await user.click(screen.getByRole('button', { name: 'Stage Items' }));

    await waitFor(() => expect(stageStartingItems).toHaveBeenCalled());
    expect(stageStartingItems).toHaveBeenCalledWith(
      expect.objectContaining({
        grants: expect.arrayContaining([{ itemId: 700, quantity: 1, slot: 3 }])
      })
    );
    expect(screen.getByLabelText('Item for Bag Hook slot 3')).toHaveValue('Bike (#700) [Key]');

    await user.click(screen.getByRole('button', { name: 'Review' }));

    await waitFor(() => expect(createChangePlan).toHaveBeenCalled());
    await user.click(screen.getByRole('button', { name: 'Apply' }));

    await waitFor(() => expect(applyChangePlan).toHaveBeenCalled());
    expect(applyChangePlan).toHaveBeenCalledWith(
      expect.objectContaining({
        changePlan: expect.objectContaining({
          writes: expect.arrayContaining([
            expect.objectContaining({
              targetRelativePath: 'romfs/bin/script/amx/main_event_0020.amx'
            })
          ])
        }),
        session: expect.objectContaining({
          pendingEdits: expect.arrayContaining([
            expect.objectContaining({ domain: 'workflow.startingItems' })
          ])
        })
      })
    );
  });

  it('warns when Starting Items is staged before Bag Hook is installed', async () => {
    const baseBridge = createMockProjectBridge({}, true);
    const stageStartingItems = vi.fn(baseBridge.stageStartingItems);
    const user = userEvent.setup();
    render(
      <App
        bridge={{
          ...baseBridge,
          loadStartingItemsWorkflow: async (request) => {
            const response = await baseBridge.loadStartingItemsWorkflow(request);
            return {
              workflow: {
                ...response.workflow,
                installMessage: 'Install Bag Hook before adding Starting Items.',
                installStatus: 'blocked'
              }
            };
          },
          stageStartingItems
        }}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output-root');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Advanced Editors' }));
    await user.click(screen.getByRole('button', { name: 'Starting Items' }));

    expect(
      await screen.findByRole('heading', {
        level: 2,
        name: 'Starting Items'
      })
    ).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Stage Items' }));

    expect(
      await screen.findByRole('dialog', {
        name: 'Bag Hook Required'
      })
    ).toBeInTheDocument();
    expect(
      screen.getByText('Starting Items cannot be staged until Bag Hook V2 is installed.')
    ).toBeInTheDocument();
    expect(stageStartingItems).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Open Bag Hook' }));

    expect(
      await screen.findByRole('heading', {
        level: 2,
        name: 'Bag Hook'
      })
    ).toBeInTheDocument();
  });

  it('opens Royal Candy workflows, searches checks, and shows planned outputs', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge()} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Advanced Editors' }));
    await user.click(screen.getByRole('button', { name: 'Royal Candy' }));

    expect(
      await screen.findByRole('heading', {
        level: 2,
        name: 'Royal Candy Workflows'
      })
    ).toBeInTheDocument();
    expect(screen.getAllByText('Unlimited Royal Candy').length).toBeGreaterThan(0);
    expect(screen.getAllByText('romfs/bin/pml/item/item.dat').length).toBeGreaterThan(0);
    expect(screen.getAllByText('exefs/main').length).toBeGreaterThan(0);

    await user.type(screen.getByLabelText('Search Royal Candy workflows'), 'code cave');

    expect(screen.getAllByText('patch-code-cave').length).toBeGreaterThan(0);
    expect(screen.getAllByText('ExeFS').length).toBeGreaterThan(0);
  });

  it('stages a Royal Candy workflow for review and apply', async () => {
    const baseBridge = createMockProjectBridge({}, true);
    const stageRoyalCandyWorkflow = vi.fn(baseBridge.stageRoyalCandyWorkflow);
    const createChangePlan = vi.fn(baseBridge.createChangePlan);
    const applyChangePlan = vi.fn(baseBridge.applyChangePlan);
    const user = userEvent.setup();
    render(
      <App
        bridge={{
          ...baseBridge,
          applyChangePlan,
          createChangePlan,
          stageRoyalCandyWorkflow
        }}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output-root');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Advanced Editors' }));
    await user.click(screen.getByRole('button', { name: 'Royal Candy' }));
    await user.click(await screen.findByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(stageRoyalCandyWorkflow).toHaveBeenCalled());
    await user.click(screen.getByRole('button', { name: 'Review' }));

    await waitFor(() => expect(createChangePlan).toHaveBeenCalled());
    await user.click(screen.getByRole('button', { name: 'Apply' }));

    await waitFor(() => expect(applyChangePlan).toHaveBeenCalled());
    expect(applyChangePlan).toHaveBeenCalledWith(
      expect.objectContaining({
        changePlan: expect.objectContaining({
          writes: expect.arrayContaining([
            expect.objectContaining({ targetRelativePath: 'romfs/bin/pml/item/item.dat' })
          ])
        }),
        session: expect.objectContaining({
          pendingEdits: expect.arrayContaining([
            expect.objectContaining({ domain: 'workflow.royalCandy' })
          ])
        })
      })
    );
  });

  it('warns when Royal Candy is staged before Bag Hook is installed', async () => {
    const baseBridge = createMockProjectBridge({}, true);
    const stageRoyalCandyWorkflow = vi.fn(baseBridge.stageRoyalCandyWorkflow);
    const user = userEvent.setup();
    render(
      <App
        bridge={{
          ...baseBridge,
          loadRoyalCandyWorkflow: async (request) => {
            const response = await baseBridge.loadRoyalCandyWorkflow(request);
            const workflow: RoyalCandyWorkflow = response.workflow;
            return {
              workflow: {
                ...workflow,
                checks: [
                  ...workflow.checks,
                  {
                    area: 'Bag Hook',
                    checkId: 'royal-candy-preflight:bag-hook-installed',
                    message: 'Bag Hook V2 must be installed before Royal Candy can claim slot 1.',
                    provenance: {
                      fileState: 'baseOnly',
                      sourceFile: 'romfs/bin/script/amx/main_event_0020.amx',
                      sourceLayer: 'base'
                    },
                    status: 'Fail',
                    target: 'romfs/bin/script/amx/main_event_0020.amx',
                    workflowId: 'royal-candy-preflight'
                  }
                ],
                stats: {
                  ...workflow.stats,
                  failCount: workflow.stats.failCount + 1,
                  totalCheckCount: workflow.stats.totalCheckCount + 1
                },
                workflows: workflow.workflows.map((record) => ({
                  ...record,
                  status: 'blocked'
                }))
              }
            };
          },
          stageRoyalCandyWorkflow
        }}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output-root');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Advanced Editors' }));
    await user.click(screen.getByRole('button', { name: 'Royal Candy' }));

    expect(
      await screen.findByRole('heading', {
        level: 2,
        name: 'Royal Candy Workflows'
      })
    ).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    expect(
      await screen.findByRole('dialog', {
        name: 'Bag Hook Required'
      })
    ).toBeInTheDocument();
    expect(
      screen.getByText('Royal Candy cannot be staged until Bag Hook V2 is installed.')
    ).toBeInTheDocument();
    expect(stageRoyalCandyWorkflow).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Open Bag Hook' }));

    expect(
      await screen.findByRole('heading', {
        level: 2,
        name: 'Bag Hook'
      })
    ).toBeInTheDocument();
  });

  it('shows and stages Royal Candy story level caps', async () => {
    const stageRoyalCandyWorkflow = vi.fn(createMockProjectBridge({}, true).stageRoyalCandyWorkflow);
    const user = userEvent.setup();
    render(
      <App
        bridge={{
          ...createMockProjectBridge({}, true),
          stageRoyalCandyWorkflow
        }}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output-root');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Advanced Editors' }));
    await user.click(screen.getByRole('button', { name: 'Royal Candy' }));
    await user.click(
      await screen.findByRole('row', { name: /Royal Candy with Story Limits/i })
    );

    expect(await screen.findByText('Story Level Caps')).toBeInTheDocument();
    const firstCap = screen.getByLabelText('Level cap after defeating Hop 004/005/006');
    await user.clear(firstCap);
    await user.type(firstCap, '12');
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(stageRoyalCandyWorkflow).toHaveBeenCalled());
    await waitFor(() =>
      expect(screen.getByLabelText('Level cap after defeating Hop 004/005/006')).toHaveValue(12)
    );
    expect(stageRoyalCandyWorkflow).toHaveBeenCalledWith(
      expect.objectContaining({
        levelCaps: [
          { levelCap: 12, slot: 0 },
          { levelCap: 16, slot: 1 }
        ],
        workflowId: 'royal-candy-story-limits'
      })
    );
  });

  it('blocks Royal Candy story caps that drop below the previous milestone', async () => {
    const stageRoyalCandyWorkflow = vi.fn(createMockProjectBridge({}, true).stageRoyalCandyWorkflow);
    const user = userEvent.setup();
    render(
      <App
        bridge={{
          ...createMockProjectBridge({}, true),
          stageRoyalCandyWorkflow
        }}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output-root');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Advanced Editors' }));
    await user.click(screen.getByRole('button', { name: 'Royal Candy' }));
    await user.click(
      await screen.findByRole('row', { name: /Royal Candy with Story Limits/i })
    );

    expect(
      await screen.findByText('Each later story cap must be equal to or higher than the cap before it.')
    ).toBeInTheDocument();
    const secondCap = screen.getByLabelText('Level cap after defeating Hop 007/008/009');
    await user.clear(secondCap);
    await user.type(secondCap, '9');

    expect(await screen.findByText('Must be Lv. 10 or higher.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Stage' })).toBeDisabled();
    expect(stageRoyalCandyWorkflow).not.toHaveBeenCalled();
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
    expect(
      screen.getByText((content) =>
        content.includes('Take a screenshot of this message and report it in GitHub Issues.')
      )
    ).toBeInTheDocument();
    expect(
      screen.getByText((content) => content.includes('Project bridge unavailable.'))
    ).toBeInTheDocument();
  });

  it('checks native updates and installs them in the desktop app', async () => {
    const user = userEvent.setup();
    const install = vi.fn(async (onProgress?: Parameters<NativeUpdate['install']>[0]) => {
      onProgress?.({ data: { contentLength: 42000 }, event: 'Started' });
      onProgress?.({ data: { chunkLength: 42000 }, event: 'Progress' });
      onProgress?.({ event: 'Finished' });
    });
    const relaunchApp = vi.fn(async () => undefined);

    render(
      <App
        desktopServices={createMockDesktopServices({
          checkForNativeUpdate: async () =>
            createNativeUpdate({
              body: 'Maintenance release',
              install,
              version: '0.2.0'
            }),
          relaunchApp
        })}
      />
    );

    await user.click(screen.getByRole('button', { name: 'Settings' }));
    await user.click(screen.getByRole('button', { name: 'Check for Updates' }));

    expect(await screen.findByRole('dialog', { name: 'Update Available' })).toBeInTheDocument();
    expect(screen.getAllByText(/KM Editor v0\.2\.0 is available/).length).toBeGreaterThan(0);
    expect(screen.getByText('Maintenance release')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Install Update' }));

    await waitFor(() => expect(install).toHaveBeenCalledTimes(1));
    expect(relaunchApp).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('dialog', { name: 'Update Available' })).not.toBeInTheDocument();
  });

  it('falls back to the GitHub release page when native update checks fail', async () => {
    const user = userEvent.setup();
    const openExternalUrl = vi.fn(async () => undefined);
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(
          JSON.stringify([
            {
              draft: false,
              html_url: 'https://github.example/releases/tag/v1.4.1',
              name: 'KM Editor v1.4.1',
              prerelease: false,
              tag_name: 'v1.4.1'
            }
          ]),
          { headers: { 'Content-Type': 'application/json' }, status: 200 }
        )
      )
    );

    render(
      <App
        desktopServices={createMockDesktopServices({
          checkForNativeUpdate: async () => {
            throw new Error('latest.json was not found.');
          },
          openExternalUrl
        })}
      />
    );

    await user.click(screen.getByRole('button', { name: 'Settings' }));
    await user.click(screen.getByRole('button', { name: 'Check for Updates' }));

    expect(await screen.findByRole('dialog', { name: 'Update Available' })).toBeInTheDocument();
    expect(screen.getByText(/Native update check was not available/)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Open Release' }));

    await waitFor(() =>
      expect(openExternalUrl).toHaveBeenCalledWith('https://github.example/releases/tag/v1.4.1')
    );
    expect(openExternalUrl).toHaveBeenCalledTimes(1);
  });

  it('reports when KM Editor is already up to date', async () => {
    const user = userEvent.setup();
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        new Response(
          JSON.stringify([
            {
              assets: [],
              draft: false,
              html_url: 'https://github.example/releases/tag/v1.4.0',
              prerelease: false,
              tag_name: 'v1.4.0'
            }
          ]),
          { headers: { 'Content-Type': 'application/json' }, status: 200 }
        )
      )
    );

    render(<App />);

    await user.click(screen.getByRole('button', { name: 'Settings' }));
    await user.click(screen.getByRole('button', { name: 'Check for Updates' }));

    expect(await screen.findByText('KM Editor v1.4.0 is up to date.')).toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: 'Update Available' })).not.toBeInTheDocument();
  });

  it('uses desktop folder pickers and opens the output root', async () => {
    const user = userEvent.setup();
    const openedPaths: string[] = [];
    const desktopServices = createMockDesktopServices({
      openPath: async (path) => {
        openedPaths.push(path);
      },
      pickFile: async ({ title }) => (title === 'Select Save File (Optional)' ? 'picked-save-main' : null),
      pickFolder: async ({ title }) =>
        ({
          'Select Base ExeFS': 'picked-exefs',
          'Select Base RomFS': 'picked-romfs',
          'Select Output Root': 'picked-output'
        })[title] ?? null
    });
    render(<App bridge={createMockProjectBridge()} desktopServices={desktopServices} />);

    await user.click(screen.getByRole('button', { name: 'Browse for Base RomFS' }));
    await user.click(screen.getByRole('button', { name: 'Browse for Base ExeFS' }));
    await user.click(screen.getByRole('button', { name: 'Browse for Output Root' }));
    await user.click(screen.getByRole('button', { name: 'Browse for Save File (Optional)' }));

    expect(screen.getByLabelText('Base RomFS')).toHaveValue('picked-romfs');
    expect(screen.getByLabelText('Base ExeFS')).toHaveValue('picked-exefs');
    expect(screen.getByLabelText('Output Root')).toHaveValue('picked-output');
    expect(screen.getByLabelText('Save File (Optional)')).toHaveValue('picked-save-main');

    await user.click(screen.getByRole('button', { name: 'Open Output Root' }));

    expect(openedPaths).toEqual(['picked-output']);
    expect(window.localStorage.getItem('km-editor.project-path-draft.v1')).toContain(
      'picked-output'
    );
  });

  it('creates a Shield output root folder from sibling base paths', async () => {
    const user = userEvent.setup();
    const createdPaths: string[] = [];
    const validateProject = vi.fn((request) => {
      const outputRootPath = request.paths.outputRootPath;
      const health: ProjectHealth = {
        canOpenEditableWorkflows: outputRootPath !== null,
        canOpenReadOnlyWorkflows: true,
        diagnostics: [],
        fileGraph: {
          baseFileCount: 2,
          layeredFileCount: outputRootPath === null ? 0 : 1,
          layeredOnlyCount: 0,
          overrideCount: 0
        },
        paths: [
          {
            diagnostics: [],
            isRequired: true,
            path: request.paths.baseRomFsPath,
            role: 'baseRomFs',
            status: 'valid'
          },
          {
            diagnostics: [],
            isRequired: true,
            path: request.paths.baseExeFsPath,
            role: 'baseExeFs',
            status: 'valid'
          },
          {
            diagnostics: [],
            isRequired: false,
            path: outputRootPath,
            role: 'outputRoot',
            status: outputRootPath === null ? 'notSet' : 'valid'
          }
        ],
        state: outputRootPath === null ? 'readOnlyReady' : 'editableReady'
      };

      return Promise.resolve({ health });
    });
    const desktopServices = createMockDesktopServices({
      createDirectory: async (path) => {
        createdPaths.push(path);
      }
    });

    useWorkbenchStore.setState({
      draftPaths: {
        baseExeFsPath: '',
        baseRomFsPath: '',
        outputRootPath: '',
        saveFilePath: '',
        selectedGame: 'shield'
      }
    });

    render(
      <App
        bridge={createMockProjectBridge({ validateProject })}
        desktopServices={desktopServices}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'C:\\SH\\romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'C:\\SH\\exefs');
    await user.click(screen.getByRole('button', { name: 'Create Output Root Folder' }));

    await waitFor(() => {
      expect(createdPaths).toEqual(['C:\\SH\\01008DB008C2C000']);
    });
    expect(screen.getByLabelText('Output Root')).toHaveValue('C:\\SH\\01008DB008C2C000');
    expect(screen.getByRole('button', { name: 'Create Output Root Folder' })).toBeDisabled();
    expect(validateProject).toHaveBeenCalledWith({
      paths: {
        baseExeFsPath: 'C:\\SH\\exefs',
        baseRomFsPath: 'C:\\SH\\romfs',
        outputRootPath: null,
        saveFilePath: null,
        selectedGame: 'shield'
      }
    });
  });

  it('does not create an output root folder when the selected game validation fails', async () => {
    const user = userEvent.setup();
    const createDirectory = vi.fn(async () => undefined);

    useWorkbenchStore.setState({
      draftPaths: {
        baseExeFsPath: '',
        baseRomFsPath: '',
        outputRootPath: '',
        saveFilePath: '',
        selectedGame: 'shield'
      }
    });

    render(
      <App
        bridge={createMockProjectBridge({
          validateProject: () => Promise.resolve({ health: createWrongGameHealth() })
        })}
        desktopServices={createMockDesktopServices({ createDirectory })}
      />
    );

    await user.type(screen.getByLabelText('Base RomFS'), 'C:\\SH\\romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'C:\\SH\\exefs');
    await user.click(screen.getByRole('button', { name: 'Create Output Root Folder' }));

    expect(await screen.findByText(/requires Base RomFS and Base ExeFS/)).toBeInTheDocument();
    expect(screen.getByLabelText('Output Root')).toHaveValue('');
    expect(createDirectory).not.toHaveBeenCalled();
  });

  it('shows desktop diagnostics when opening the output root fails', async () => {
    const user = userEvent.setup();
    const desktopServices = createMockDesktopServices({
      openPath: async () => {
        throw 'The folder does not exist.';
      }
    });
    render(<App bridge={createMockProjectBridge()} desktopServices={desktopServices} />);

    await user.type(screen.getByLabelText('Output Root'), 'missing-output-root');
    await user.click(screen.getByRole('button', { name: 'Open Output Root' }));

    expect(
      await screen.findByText('Could not open output root. The folder does not exist.')
    ).toBeInTheDocument();
  });

  it('prompts on desktop window close during an edit session and exits after discard', async () => {
    const user = userEvent.setup();
    const closeGuardStates: boolean[] = [];
    const exitApp = vi.fn(async () => undefined);
    const desktopServices = createMockDesktopServices({
      exitApp,
      setCloseGuardEnabled: async (enabled) => {
        closeGuardStates.push(enabled);
      }
    });
    render(<App bridge={createMockProjectBridge({}, true)} desktopServices={desktopServices} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Pokemon' }));
    await user.click(screen.getByRole('button', { name: 'Edit' }));

    await waitFor(() => expect(closeGuardStates).toContain(true));
    await waitFor(() =>
      expect(tauriEventMock.listeners[windowCloseRequestedEvent]?.length ?? 0).toBeGreaterThan(0)
    );
    await act(async () => {
      tauriEventMock.listeners[windowCloseRequestedEvent]?.at(-1)?.();
    });

    expect(
      await screen.findByRole('dialog', { name: 'Discard Pending Changes?' })
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Yes, Discard' }));

    await waitFor(() => expect(exitApp).toHaveBeenCalledTimes(1));
    expect(closeGuardStates.at(-1)).toBe(false);
  });

  it('keeps the desktop close guard enabled for pending Changes and disables it after Save', async () => {
    const user = userEvent.setup();
    const closeGuardStates: boolean[] = [];
    const desktopServices = createMockDesktopServices({
      setCloseGuardEnabled: async (enabled) => {
        closeGuardStates.push(enabled);
      }
    });
    render(<App bridge={createMockProjectBridge({}, true)} desktopServices={desktopServices} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Editors' }));
    await user.click(screen.getByRole('button', { name: 'Items' }));
    await user.click(await screen.findByRole('button', { name: 'Edit' }));

    const buyPriceInput = screen.getByLabelText('Buy price');
    await user.clear(buyPriceInput);
    await user.type(buyPriceInput, '450');
    await user.click(screen.getByRole('button', { name: 'Save Item' }));

    await waitFor(() => expect(closeGuardStates).toContain(true));

    await user.click(screen.getByRole('button', { name: 'Changes' }));
    await user.click(screen.getByRole('button', { name: 'Validate Pending Changes' }));
    expect(await screen.findByText('Pending item change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('heading', { name: 'Save Result' })).toBeInTheDocument();
    await waitFor(() => expect(closeGuardStates.at(-1)).toBe(false));
  });

  it('keeps the desktop close guard disabled when there is no edit session', async () => {
    const closeGuardStates: boolean[] = [];
    render(
      <App
        bridge={createMockProjectBridge()}
        desktopServices={createMockDesktopServices({
          setCloseGuardEnabled: async (enabled) => {
            closeGuardStates.push(enabled);
          }
        })}
      />
    );

    await waitFor(() => {
      expect(closeGuardStates).toContain(false);
    });
    expect(screen.queryByRole('dialog', { name: 'Discard Pending Changes?' })).not.toBeInTheDocument();
  });
});
