/* SPDX-License-Identifier: GPL-3.0-only */

import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { App, AppErrorBoundary, getPokemonSpriteId, getPokemonSpriteIds } from './App';
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
  it('shows a reportable error code when rendering crashes', () => {
    const consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const BrokenSection = () => {
      throw new Error('render exploded');
    };

    try {
      render(
        <AppErrorBoundary>
          <BrokenSection />
        </AppErrorBoundary>
      );

      expect(screen.getByRole('alert')).toHaveTextContent('KM Editor hit a critical display error.');
      expect(screen.getByText(/^KM-UI-RENDER-/)).toBeInTheDocument();
      expect(screen.getByText(/Take a screenshot/)).toBeInTheDocument();
    } finally {
      consoleErrorSpy.mockRestore();
    }
  });

  it('normalizes known hyphenated Pokemon sprite ids', () => {
    expect(getPokemonSpriteId('Kommo-o')).toBe('kommoo');
    expect(getPokemonSpriteId('Toxtricity (Low Key) (Gigantamax)')).toBe('toxtricity-gmax');
  });

  it('normalizes gendered Nidoran sprite ids to bundled static sprites', () => {
    expect(getPokemonSpriteId('Nidoran♀')).toBe('nidoranf');
    expect(getPokemonSpriteId('Nidoran♂')).toBe('nidoranm');
    expect(getPokemonSpriteId('Nidoran-F')).toBe('nidoranf');
    expect(getPokemonSpriteId('Nidoran-M')).toBe('nidoranm');
    expect(getPokemonSpriteId('Nidoran (Female)')).toBe('nidoranf');
    expect(getPokemonSpriteId('Nidoran (Male)')).toBe('nidoranm');
    expect(getPokemonSpriteId('Nidorina')).toBe('nidorina');
  });

  it('normalizes punctuation and accent Pokemon names to bundled sprite ids', () => {
    expect(getPokemonSpriteId('Flabébé')).toBe('flabebe');
    expect(getPokemonSpriteId('Ho-Oh')).toBe('hooh');
    expect(getPokemonSpriteId('Mime Jr.')).toBe('mimejr');
    expect(getPokemonSpriteId('Mr. Mime')).toBe('mrmime');
    expect(getPokemonSpriteId('Mr. Mime (Galarian)')).toBe('mrmime-galar');
    expect(getPokemonSpriteId('Mr. Rime')).toBe('mrrime');
    expect(getPokemonSpriteId('Tapu Bulu')).toBe('tapubulu');
    expect(getPokemonSpriteId('Tapu Fini')).toBe('tapufini');
    expect(getPokemonSpriteId('Tapu Koko')).toBe('tapukoko');
    expect(getPokemonSpriteId('Tapu Lele')).toBe('tapulele');
    expect(getPokemonSpriteId('Type: Null')).toBe('typenull');
  });

  it('normalizes legendary form labels to bundled sprite ids', () => {
    expect(getPokemonSpriteId('Necrozma (Dusk Mane)')).toBe('necrozma-duskmane');
    expect(getPokemonSpriteId('Necrozma (Dawn Wings)')).toBe('necrozma-dawnwings');
    expect(getPokemonSpriteId('Necrozma (Ultra Necrozma)')).toBe('necrozma-ultra');
    expect(getPokemonSpriteIds('Giratina (Origin Forme)')).toEqual([
      'giratina-origin-forme',
      'giratina-origin',
      'giratina'
    ]);
  });

  it('falls back from form-specific Pokemon sprite ids to the base species id', () => {
    expect(getPokemonSpriteIds('Tornadus (Therian Forme)')).toEqual([
      'tornadus-therian-forme',
      'tornadus-therian',
      'tornadus'
    ]);
  });

  it('maps gender-form Pokemon names to the bundled gender sprite ids', () => {
    expect(getPokemonSpriteId('Frillish (Male)')).toBe('frillish');
    expect(getPokemonSpriteId('Frillish (Female)')).toBe('frillish-f');
    expect(getPokemonSpriteId('Jellicent (Female)')).toBe('jellicent-f');
    expect(getPokemonSpriteId('Indeedee (Female)')).toBe('indeedee-f');
    expect(getPokemonSpriteId('Meowstic (Female)')).toBe('meowstic-f');
    expect(getPokemonSpriteId('Unfezant (Female)')).toBe('unfezant-f');
  });

  it('defaults Pokemon selection to the first real Pokemon instead of Egg', () => {
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

    expect(useWorkbenchStore.getState().selectedPokemonPersonalId).toBe(1);
  });

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
    expect(screen.queryByRole('button', { name: 'Pokemon Scarlet' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Pokemon Violet' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Project Setup' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Pokemon Shield' }));

    expect(screen.getByRole('heading', { name: 'Project Setup' })).toBeInTheDocument();
    expect(screen.getByText('Pokemon Shield Editor')).toBeInTheDocument();
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
        .slice(-7)
    ).toEqual([
      'Catch Cap',
      'Hyper Training',
      'Type Chart',
      'Gym Uniform Removal',
      'IV Screen',
      'Royal Candy',
      'Starting Items'
    ]);
    expect(screen.queryByRole('button', { name: 'Dynamax Adventures' })).not.toBeInTheDocument();
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

  it('hides shelved Rental Pokemon and Dynamax Adventures entry points', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    expect(screen.queryByRole('button', { name: 'Rental Pokemon' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Advanced Editors' }));
    expect(screen.queryByRole('button', { name: 'Dynamax Adventures' })).not.toBeInTheDocument();
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

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(
      await screen.findByText('Stage Bag Hook install: 20 disabled startup item grant slots.')
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Changes' }));

    expect(
      await screen.findByText('Pending Bag Hook install is valid for change-plan review.')
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(
      (await screen.findAllByText('romfs/bin/script/amx/main_event_0020.amx')).length
    ).toBeGreaterThan(0);

    expect(
      await screen.findByText('Installed Bag Hook V2 to the configured LayeredFS output root.')
    ).toBeInTheDocument();
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
    await user.click(screen.getByRole('button', { name: 'Changes' }));

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
    expect(stageCatchCap).toHaveBeenCalledWith(
      expect.objectContaining({
        caps: expect.arrayContaining([{ badgeCount: 8, levelCap: 100 }])
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
              html_url: 'https://github.example/releases/tag/v1.2.1',
              name: 'KM Editor v1.2.1',
              prerelease: false,
              tag_name: 'v1.2.1'
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
      expect(openExternalUrl).toHaveBeenCalledWith('https://github.example/releases/tag/v1.2.1')
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
              html_url: 'https://github.example/releases/tag/v1.2.0',
              prerelease: false,
              tag_name: 'v1.2.0'
            }
          ]),
          { headers: { 'Content-Type': 'application/json' }, status: 200 }
        )
      )
    );

    render(<App />);

    await user.click(screen.getByRole('button', { name: 'Settings' }));
    await user.click(screen.getByRole('button', { name: 'Check for Updates' }));

    expect(await screen.findByText('KM Editor v1.2.0 is up to date.')).toBeInTheDocument();
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

function createNativeUpdate(overrides: Partial<NativeUpdate> = {}): NativeUpdate {
  return {
    close: async () => undefined,
    install: async () => undefined,
    version: '0.2.0',
    ...overrides
  };
}

function createMockDesktopServices(overrides: Partial<DesktopServices> = {}): DesktopServices {
  return {
    checkForNativeUpdate: async () => null,
    createDirectory: async () => undefined,
    exitApp: async () => undefined,
    isAvailable: true,
    openExternalUrl: async () => undefined,
    openPath: async () => undefined,
    pickFile: async () => null,
    pickFolder: async () => null,
    relaunchApp: async () => undefined,
    setCloseGuardEnabled: async () => undefined,
    ...overrides
  };
}

function createHealthForValidatedPaths(
  baseRomFsPath: string,
  baseExeFsPath: string,
  outputRootPath: string,
  saveFilePath: string | null
): ProjectHealth {
  return {
    canOpenEditableWorkflows: true,
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
        path: baseRomFsPath,
        role: 'baseRomFs',
        status: 'valid'
      },
      {
        diagnostics: [],
        isRequired: true,
        path: baseExeFsPath,
        role: 'baseExeFs',
        status: 'valid'
      },
      {
        diagnostics: [],
        isRequired: false,
        path: outputRootPath,
        role: 'outputRoot',
        status: 'valid'
      },
      {
        diagnostics: [],
        isRequired: false,
        path: saveFilePath,
        role: 'saveFile',
        status: saveFilePath ? 'valid' : 'notSet'
      }
    ],
    state: 'editableReady'
  };
}

function createItemDetailGroups(metadata = createItemMetadata()) {
  const pouchLabel = metadata.pouch === 4 ? 'Items (4)' : 'Medicine (0)';
  const healLabel =
    metadata.healAmount === 254
      ? 'Half HP'
      : metadata.healAmount === 255
      ? 'Full HP'
      : `${metadata.healAmount} HP`;

  return [
    {
      details: [
        { label: 'Pouch', value: pouchLabel },
        { label: 'Sprite', value: '12' },
        { label: 'Machine', value: 'No machine link' }
      ],
      label: 'Inventory'
    },
    {
      details: [
        { label: 'Field use type', value: 'Medicine (1)' },
        { label: 'Can use on Pokemon', value: 'Yes' },
        { label: 'Use flags 1 (decoded)', value: 'Restore HP' }
      ],
      label: 'Field Use'
    },
    {
      details: [
        { label: 'Fling power', value: '30' },
        { label: 'Cures status', value: 'None' }
      ],
      label: 'Battle'
    },
    {
      details: [
        { label: 'Heal', value: healLabel },
        { label: 'Friendship gains', value: '+1 / +1 / 0' }
      ],
      label: 'Pokemon Effects'
    }
  ];
}

function createItemMetadata(): ItemRecord['metadata'] {
  return {
    boost0: 0,
    boost1: 0,
    boost2: 0,
    boost3: 0,
    canUseOnPokemon: true,
    cureStatusFlags: 0,
    evAttack: 0,
    evDefense: 0,
    evHp: 0,
    evSpecialAttack: 0,
    evSpecialDefense: 0,
    evSpeed: 0,
    fieldFlags: 2,
    fieldUseType: 1,
    flingPower: 30,
    friendshipGain1: 1,
    friendshipGain2: 1,
    friendshipGain3: 0,
    groupIndex: 0,
    groupType: 0,
    healAmount: 20,
    itemSprite: 12,
    itemType: 9,
    machineMoveId: null,
    machineMoveName: null,
    machineSlot: null,
    pouch: 0,
    pouchFlags: 0,
    ppGain: 0,
    sortIndex: 5,
    useFlags1: 4,
    useFlags2: 0
  };
}

function createMockProjectBridge(
  overrides: Partial<ProjectBridge> = {},
  canEdit = true
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
      },
      {
        diagnostics: [],
        isRequired: false,
        path: null,
        role: 'saveFile',
        status: 'notSet'
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
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'sellPrice',
        label: 'Sell price',
        maximumValue: 499_999,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'wattsPrice',
        label: 'Watts price',
        maximumValue: 999_999,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'alternatePrice',
        label: 'Alternate price',
        maximumValue: 999_999,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'pouch',
        label: 'Pouch',
        maximumValue: 8,
        minimumValue: 0,
        options: [
          { label: 'Medicine', value: 0 },
          { label: 'Items', value: 4 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'healAmount',
        label: 'Heal amount',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'evAttack',
        label: 'Attack EV gain',
        maximumValue: 127,
        minimumValue: -128,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'canUseOnPokemon',
        label: 'Can use on Pokemon',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: 'No', value: 0 },
          { label: 'Yes', value: 1 }
        ],
        valueKind: 'boolean'
      }
    ],
    items: [
      {
        alternatePrice: 3,
        buyPrice: 300,
        category: 'Medicine',
        detailGroups: createItemDetailGroups(),
        itemId: 1,
        metadata: createItemMetadata(),
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
        detailGroups: createItemDetailGroups(),
        itemId: 2,
        metadata: createItemMetadata(),
        name: 'Antidote',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/item/item.dat',
          sourceLayer: 'base'
        },
        sellPrice: 100,
        sharedItemIds: [2],
        wattsPrice: 10
      },
      {
        alternatePrice: 0,
        buyPrice: 1000,
        category: 'TMs',
        detailGroups: createItemDetailGroups(),
        itemId: 335,
        metadata: createItemMetadata(),
        name: 'TM02 (Razor Leaf)',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/item/item.dat',
          sourceLayer: 'base'
        },
        sellPrice: 500,
        sharedItemIds: [335],
        wattsPrice: 0
      },
      {
        alternatePrice: 0,
        buyPrice: 1000,
        category: 'TMs',
        detailGroups: createItemDetailGroups(),
        itemId: 337,
        metadata: createItemMetadata(),
        name: 'TM10 (Magical Leaf)',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/item/item.dat',
          sourceLayer: 'base'
        },
        sellPrice: 500,
        sharedItemIds: [337],
        wattsPrice: 0
      },
      {
        alternatePrice: 0,
        buyPrice: 3000,
        category: 'TRs',
        detailGroups: createItemDetailGroups(),
        itemId: 1120,
        metadata: createItemMetadata(),
        name: 'TR02 (Growl)',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/item/item.dat',
          sourceLayer: 'base'
        },
        sellPrice: 1500,
        sharedItemIds: [1120],
        wattsPrice: 0
      },
      {
        alternatePrice: 0,
        buyPrice: 3000,
        category: 'TRs',
        detailGroups: createItemDetailGroups(),
        itemId: 1128,
        metadata: createItemMetadata(),
        name: 'TR10 (Magical Leaf)',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/item/item.dat',
          sourceLayer: 'base'
        },
        sellPrice: 1500,
        sharedItemIds: [1128],
        wattsPrice: 0
      }
    ],
    stats: {
      sourceFileCount: 2,
      totalItemCount: 6
    },
    summary: {
      availability: canEdit ? 'available' : 'readOnly',
      description: 'Item records, names, and source provenance.',
      diagnostics: [],
      id: 'items',
      label: 'Items'
    }
  };
  const pokemonWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Pokemon personal stats, forms, evolutions, learnsets, and source provenance.',
    diagnostics: [],
    id: 'pokemon',
    label: 'Pokemon'
  };
  const pokemonWorkflow: PokemonWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'hp',
        group: 'Base Stats',
        label: 'HP',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'type1',
        group: 'Traits',
        label: 'Type 1',
        maximumValue: 17,
        minimumValue: 0,
        options: [
          { label: 'Normal', value: 0 },
          { label: 'Grass', value: 11 },
          { label: 'Fire', value: 9 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'heldItem1',
        group: 'Held Items',
        label: 'Held Item 50%',
        maximumValue: 32767,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ability1',
        group: 'Abilities',
        label: 'Ability 1',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '034 Chlorophyll', value: 34 },
          { label: '065 Overgrow', value: 65 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'hatchedSpecies',
        group: 'Forms/Dex',
        label: 'Hatched Species',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Bulbasaur', value: 1 },
          { label: '002 Ivysaur', value: 2 },
          { label: '003 Venusaur', value: 3 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'canNotDynamax',
        group: 'Flags',
        label: 'Cannot Dynamax',
        maximumValue: 1,
        minimumValue: 0,
        options: [],
        valueKind: 'boolean'
      }
    ],
    evolutionMethodOptions: [
      {
        argumentKind: 'level',
        argumentLabel: 'Level',
        argumentOptions: [],
        label: '004 Level Up',
        value: 4
      },
      {
        argumentKind: 'item',
        argumentLabel: 'Item',
        argumentOptions: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 },
          { label: '025 Thunder Stone', value: 25 }
        ],
        label: '008 Use Item',
        value: 8
      }
    ],
    learnsetMoveOptions: [
      { label: '033 Tackle', value: 33 },
      { label: '045 Growl', value: 45 },
      { label: '075 Razor Leaf', value: 75 },
      { label: '345 Magical Leaf', value: 345 }
    ],
    pokemon: [
      {
        abilities: {
          ability1: 65,
          ability1Label: 'Overgrow',
          ability2: 0,
          ability2Label: 'None',
          hiddenAbility: 34,
          hiddenAbilityLabel: 'Chlorophyll'
        },
        baseExperience: 64,
        baseStats: {
          attack: 49,
          defense: 49,
          hp: 45,
          specialAttack: 65,
          specialDefense: 65,
          speed: 45,
          total: 318
        },
        catchRate: 45,
        compatibility: [
          {
            enabledCount: 1,
            entries: [
              {
                canLearn: false,
                label: 'TM00 Mega Punch',
                moveId: 5,
                moveName: 'Mega Punch',
                slot: 0
              },
              {
                canLearn: true,
                label: 'TM10 Magical Leaf',
                moveId: 345,
                moveName: 'Magical Leaf',
                slot: 10
              }
            ],
            groupId: 'tm',
            label: 'TMs'
          },
          {
            enabledCount: 0,
            entries: [
              {
                canLearn: false,
                label: 'TR00 Swords Dance',
                moveId: 14,
                moveName: 'Swords Dance',
                slot: 0
              }
            ],
            groupId: 'tr',
            label: 'TRs'
          }
        ],
        dexPresence: {
          armorDexIndex: 0,
          crownDexIndex: 0,
          isInAnyDex: true,
          isPresentInGame: true,
          regionalDexIndex: 1
        },
        evolutionStage: 1,
        evolutions: [
          {
            argument: 0,
            argumentKind: 'level',
            argumentLabel: 'Level',
            argumentValue: 'None',
            form: 0,
            level: 16,
            method: 4,
            methodName: 'Level Up',
            slot: 0,
            species: 2
          }
        ],
        form: 0,
        formLabel: 'Base',
        genderRatio: 31,
        genderRatioLabel: '031 Male 87.5% / Female 12.5%',
        height: 7,
        learnset: [
          {
            level: 1,
            moveId: 33,
            moveName: 'Tackle',
            slot: 0
          },
          {
            level: 3,
            moveId: 45,
            moveName: 'Growl',
            slot: 1
          }
        ],
        name: 'Bulbasaur',
        personal: {
          baseFriendship: 70,
          canNotDynamax: false,
          catchRate: 45,
          color: 5,
          eggGroup1: 7,
          eggGroup2: 1,
          evYieldAttack: 0,
          evYieldDefense: 0,
          evYieldHP: 0,
          evYieldSpecialAttack: 1,
          evYieldSpecialDefense: 0,
          evYieldSpeed: 0,
          evolutionStage: 1,
          expGrowth: 4,
          form: 0,
          formCount: 1,
          formStatsIndex: 0,
          genderRatio: 31,
          hasSpriteForm: false,
          hatchedSpecies: 1,
          hatchCycles: 20,
          heldItem1: 0,
          heldItem2: 0,
          heldItem3: 0,
          isPresentInGame: true,
          isRegionalForm: false,
          localFormIndex: 0,
          modelId: 1,
          type1: 11,
          type2: 3
        },
        personalId: 1,
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/personal/personal_total.bin',
          sourceLayer: 'base'
        },
        speciesId: 1,
        type1: 'Grass',
        type2: 'Poison',
        weight: 69
      },
      {
        abilities: {
          ability1: 66,
          ability1Label: 'Blaze',
          ability2: 0,
          ability2Label: 'None',
          hiddenAbility: 94,
          hiddenAbilityLabel: 'Solar Power'
        },
        baseExperience: 62,
        baseStats: {
          attack: 52,
          defense: 43,
          hp: 39,
          specialAttack: 60,
          specialDefense: 50,
          speed: 65,
          total: 309
        },
        catchRate: 45,
        compatibility: [
          {
            enabledCount: 1,
            entries: [
              {
                canLearn: true,
                label: 'TM00 Mega Punch',
                moveId: 5,
                moveName: 'Mega Punch',
                slot: 0
              }
            ],
            groupId: 'tm',
            label: 'TMs'
          }
        ],
        dexPresence: {
          armorDexIndex: 0,
          crownDexIndex: 0,
          isInAnyDex: true,
          isPresentInGame: true,
          regionalDexIndex: 4
        },
        evolutionStage: 1,
        evolutions: [],
        form: 0,
        formLabel: 'Base',
        genderRatio: 31,
        genderRatioLabel: '031 Male 87.5% / Female 12.5%',
        height: 6,
        learnset: [
          {
            level: 1,
            moveId: 10,
            moveName: 'Scratch',
            slot: 0
          }
        ],
        name: 'Charmander',
        personal: {
          baseFriendship: 70,
          canNotDynamax: false,
          catchRate: 45,
          color: 8,
          eggGroup1: 7,
          eggGroup2: 7,
          evYieldAttack: 0,
          evYieldDefense: 0,
          evYieldHP: 0,
          evYieldSpecialAttack: 0,
          evYieldSpecialDefense: 0,
          evYieldSpeed: 1,
          evolutionStage: 1,
          expGrowth: 4,
          form: 0,
          formCount: 1,
          formStatsIndex: 0,
          genderRatio: 31,
          hasSpriteForm: false,
          hatchedSpecies: 4,
          hatchCycles: 20,
          heldItem1: 0,
          heldItem2: 0,
          heldItem3: 0,
          isPresentInGame: true,
          isRegionalForm: false,
          localFormIndex: 0,
          modelId: 4,
          type1: 9,
          type2: 9
        },
        personalId: 4,
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/personal/personal_total.bin',
          sourceLayer: 'base'
        },
        speciesId: 4,
        type1: 'Fire',
        type2: 'Fire',
        weight: 85
      }
    ],
    stats: {
      presentPokemonCount: 2,
      sourceFileCount: 5,
      totalEvolutionCount: 1,
      totalLearnsetMoveCount: 3,
      totalPokemonCount: 2
    },
    summary: pokemonWorkflowSummary
  };
  const movesWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Move stats, target behavior, secondary effects, flags, and source provenance.',
    diagnostics: [],
    id: 'moves',
    label: 'Moves'
  };
  const movesWorkflow: MovesWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'power',
        label: 'Power',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'makesContact',
        label: 'Makes contact',
        maximumValue: 1,
        minimumValue: 0,
        options: [],
        valueKind: 'boolean'
      }
    ],
    moves: [
      {
        accuracy: 100,
        canUseMove: true,
        category: 1,
        categoryName: 'Physical',
        critStage: 0,
        description: 'A physical attack in which the user charges and slams into the target.',
        effectSequence: 12,
        flags: [
          {
            enabled: true,
            field: 'makesContact',
            label: 'Makes Contact'
          },
          {
            enabled: true,
            field: 'protect',
            label: 'Blocked By Protect'
          },
          {
            enabled: true,
            field: 'punch',
            label: 'Punch Move'
          }
        ],
        flinch: 0,
        hitMax: 1,
        hitMin: 1,
        inflict: 0,
        inflictName: 'None',
        inflictPercent: 0,
        maxMovePower: 90,
        moveId: 33,
        name: 'Tackle',
        power: 40,
        pp: 35,
        priority: 0,
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/waza/waza_033.bin',
          sourceLayer: 'base'
        },
        quality: 2,
        rawHealing: 0,
        rawInflictCount: 0,
        recoil: 0,
        statChanges: [
          {
            percent: 0,
            slot: 1,
            stage: 0,
            stat: 0,
            statName: 'None'
          },
          {
            percent: 0,
            slot: 2,
            stage: 0,
            stat: 0,
            statName: 'None'
          },
          {
            percent: 0,
            slot: 3,
            stage: 0,
            stat: 0,
            statName: 'None'
          }
        ],
        target: 3,
        targetName: 'Opponent',
        turnMax: 0,
        turnMin: 0,
        type: 0,
        typeName: 'Normal',
        version: 1
      },
      {
        accuracy: 100,
        canUseMove: true,
        category: 2,
        categoryName: 'Special',
        critStage: 0,
        description: 'The target is attacked with small flames. This may also leave the target burned.',
        effectSequence: 22,
        flags: [
          {
            enabled: true,
            field: 'protect',
            label: 'Blocked By Protect'
          },
          {
            enabled: true,
            field: 'metronome',
            label: 'Callable By Metronome'
          }
        ],
        flinch: 0,
        hitMax: 1,
        hitMin: 1,
        inflict: 4,
        inflictName: 'Burn',
        inflictPercent: 10,
        maxMovePower: 90,
        moveId: 52,
        name: 'Ember',
        power: 40,
        pp: 25,
        priority: 0,
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/waza/waza_052.bin',
          sourceLayer: 'base'
        },
        quality: 2,
        rawHealing: 0,
        rawInflictCount: 1,
        recoil: 0,
        statChanges: [
          {
            percent: 0,
            slot: 1,
            stage: 0,
            stat: 0,
            statName: 'None'
          },
          {
            percent: 0,
            slot: 2,
            stage: 0,
            stat: 0,
            statName: 'None'
          },
          {
            percent: 0,
            slot: 3,
            stage: 0,
            stat: 0,
            statName: 'None'
          }
        ],
        target: 3,
        targetName: 'Opponent',
        turnMax: 0,
        turnMin: 0,
        type: 9,
        typeName: 'Fire',
        version: 1
      }
    ],
    stats: {
      activeFlagCount: 5,
      enabledMoveCount: 2,
      sourceFileCount: 4,
      totalMoveCount: 2
    },
    summary: movesWorkflowSummary
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
        options: [
          { label: '005 Pokemon Trainer', value: 5 },
          { label: '006 Gym Leader', value: 6 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'classBallId',
        label: 'Class ball',
        maximumValue: 26,
        minimumValue: 0,
        options: [
          { label: '0 None', value: 0 },
          { label: '3 Great Ball', value: 3 },
          { label: '4 Poke Ball', value: 4 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'battleType',
        label: 'Battle type',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: '0 Singles', value: 0 },
          { label: '1 Doubles', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'trainerItem1Id',
        label: 'Trainer item 1 ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 },
          { label: '002 Antidote', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'trainerItem2Id',
        label: 'Trainer item 2 ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 },
          { label: '002 Antidote', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'trainerItem3Id',
        label: 'Trainer item 3 ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 },
          { label: '002 Antidote', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'trainerItem4Id',
        label: 'Trainer item 4 ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 },
          { label: '002 Antidote', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'aiFlags',
        label: 'AI flags',
        maximumValue: 8191,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'heal',
        label: 'Heal flag',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: 'No', value: 0 },
          { label: 'Yes', value: 1 }
        ],
        valueKind: 'boolean'
      },
      {
        field: 'money',
        label: 'Prize money',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'gift',
        label: 'Gift ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 },
          { label: '007 Rare Candy', value: 7 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'speciesId',
        label: 'Species ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '810 Grookey', value: 810 },
          { label: '821 Rookidee', value: 821 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'level',
        label: 'Level',
        maximumValue: 100,
        minimumValue: 1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'heldItemId',
        label: 'Held item ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'move1Id',
        label: 'Move 1 ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Scratch', value: 1 },
          { label: '002 Growl', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'move2Id',
        label: 'Move 2 ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Scratch', value: 1 },
          { label: '002 Growl', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'move3Id',
        label: 'Move 3 ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Scratch', value: 1 },
          { label: '002 Growl', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'move4Id',
        label: 'Move 4 ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Scratch', value: 1 },
          { label: '002 Growl', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'gender',
        label: 'Gender',
        maximumValue: 3,
        minimumValue: 0,
        options: [
          { label: 'Random', value: 0 },
          { label: 'Male', value: 1 },
          { label: 'Female', value: 2 },
          { label: 'Genderless', value: 3 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ability',
        label: 'Ability',
        maximumValue: 3,
        minimumValue: 0,
        options: [
          { label: 'Default', value: 0 },
          { label: 'Ability 1', value: 1 },
          { label: 'Ability 2', value: 2 },
          { label: 'Hidden Ability', value: 3 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'nature',
        label: 'Nature',
        maximumValue: 24,
        minimumValue: 0,
        options: [
          { label: 'Hardy', value: 0 },
          { label: 'Adamant (+Atk/-Sp.Atk)', value: 3 },
          { label: 'Serious', value: 12 },
          { label: 'Jolly (+Spe/-Sp.Atk)', value: 13 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'dynamaxLevel',
        label: 'Dynamax level',
        maximumValue: 10,
        minimumValue: 0,
        options: [
          { label: '0', value: 0 },
          { label: '7', value: 7 },
          { label: '10', value: 10 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'canGigantamax',
        label: 'Can Gigantamax',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: 'No', value: 0 },
          { label: 'Yes', value: 1 }
        ],
        valueKind: 'boolean'
      },
      {
        field: 'shiny',
        label: 'Shiny',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: 'No', value: 0 },
          { label: 'Yes', value: 1 }
        ],
        valueKind: 'boolean'
      },
      {
        field: 'canDynamax',
        label: 'Can Dynamax',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: 'No', value: 0 },
          { label: 'Yes', value: 1 }
        ],
        valueKind: 'boolean'
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
        aiFlags: 77,
        aiFlagStates: [
          { bit: 0, description: 'Enables basic battle decision logic.', enabled: true, label: 'Basic', mask: 1 },
          { bit: 1, description: 'Enables stronger move and target choices.', enabled: false, label: 'Strong', mask: 2 },
          { bit: 2, description: 'Enables expert battle decision logic.', enabled: true, label: 'Expert', mask: 4 },
          { bit: 3, description: 'Enables double-battle-aware decision logic.', enabled: true, label: 'Double', mask: 8 },
          { bit: 4, description: 'Enables raid-battle-specific decision logic.', enabled: false, label: 'Raid', mask: 16 },
          { bit: 5, description: 'Allows additional AI-controlled action checks.', enabled: false, label: 'Allowance', mask: 32 },
          { bit: 6, description: 'Allows AI-driven Pokemon switching.', enabled: true, label: 'PokeChange', mask: 64 },
          { bit: 7, description: 'Enables the first Fire Gym behavior bit.', enabled: false, label: 'Fire Gym (1)', mask: 128 },
          { bit: 8, description: 'Enables the second Fire Gym behavior bit.', enabled: false, label: 'Fire Gym (2)', mask: 256 },
          { bit: 9, description: 'Reserved trainer AI bit.', enabled: false, label: 'Unused 1', mask: 512 },
          { bit: 10, description: 'Allows AI-driven trainer item usage.', enabled: false, label: 'Item', mask: 1024 },
          { bit: 11, description: 'Enables the third Fire Gym behavior bit.', enabled: false, label: 'Fire Gym (3)', mask: 2048 },
          { bit: 12, description: 'Reserved trainer AI bit.', enabled: false, label: 'Unused 2', mask: 4096 }
        ],
        battleType: 'Doubles',
        battleTypeValue: 1,
        canEditClassBall: true,
        classBall: '4 Poke Ball',
        classBallId: 4,
        classBallScope: 'Unique trainer class: Avery',
        gift: 7,
        heal: true,
        itemIds: [1, 2, 0, 0],
        items: ['Potion', 'Antidote', 'None', 'None'],
        location: 'Trainer 10',
        money: 24,
        name: 'Avery',
        provenance: {
          classFileState: 'baseOnly',
          classSourceFile: 'romfs/bin/trainer/trainer_type/trainer_type_005.bin',
          classSourceLayer: 'base',
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/trainer/trainer_data/trainer_010.bin',
          sourceLayer: 'base',
          teamFileState: 'baseOnly',
          teamSourceFile: 'romfs/bin/trainer/trainer_poke/trainer_010.bin',
          teamSourceLayer: 'base'
        },
        team: [
          {
            ability: 2,
            abilityLabel: 'Ability 2',
            abilityOptions: [
              { label: 'Default - 065 Overgrow', value: 0 },
              { label: 'Ability 1 - 065 Overgrow', value: 1 },
              { label: 'Ability 2 - 065 Overgrow', value: 2 },
              { label: 'Hidden Ability - 000 None', value: 3 }
            ],
            canDynamax: false,
            canGigantamax: true,
            dynamaxLevel: 7,
            evs: {
              attack: 20,
              defense: 30,
              hp: 10,
              specialAttack: 40,
              specialDefense: 50,
              speed: 60
            },
            form: 0,
            gender: 1,
            genderLabel: 'Male',
            heldItem: 'Potion',
            heldItemId: 1,
            ivs: {
              attack: 2,
              defense: 3,
              hp: 1,
              specialAttack: 5,
              specialDefense: 6,
              speed: 4
            },
            level: 12,
            moveIds: [1, 2, 0, 0],
            moves: ['Scratch', 'Growl', 'None', 'None'],
            nature: 13,
            natureLabel: 'Jolly (+Spe/-Sp.Atk)',
            shiny: true,
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
  const giftPokemonWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Scripted gift Pokemon records, IV modes, items, moves, and source provenance.',
    diagnostics: [],
    id: 'giftPokemon',
    label: 'Gift Pokemon'
  };
  const giftPokemonWorkflow: GiftPokemonWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'species',
        label: 'Species',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '001 Bulbasaur', value: 1 },
          { label: '810 Grookey', value: 810 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'form',
        label: 'Form',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'level',
        label: 'Level',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'heldItemId',
        label: 'Held item',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ballItemId',
        label: 'Ball item',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '004 Poke Ball', value: 4 },
          { label: '003 Great Ball', value: 3 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ability',
        label: 'Ability slot',
        maximumValue: 3,
        minimumValue: 0,
        options: [
          { label: 'Default', value: 0 },
          { label: 'Ability 1', value: 1 },
          { label: 'Hidden Ability', value: 3 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'nature',
        label: 'Nature',
        maximumValue: 25,
        minimumValue: 0,
        options: [
          { label: 'Hardy', value: 0 },
          { label: 'Adamant (+Atk/-Sp.Atk)', value: 3 },
          { label: 'Random', value: 25 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'gender',
        label: 'Gender',
        maximumValue: 255,
        minimumValue: 0,
        options: [
          { label: 'Random', value: 0 },
          { label: 'Male', value: 1 },
          { label: 'Female', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'shinyLock',
        label: 'Shiny lock',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: 'Random', value: 0 },
          { label: 'Always Shiny', value: 1 },
          { label: 'Never Shiny', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'dynamaxLevel',
        label: 'Dynamax level',
        maximumValue: 10,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'canGigantamax',
        label: 'Can Gigantamax',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: 'No', value: 0 },
          { label: 'Yes', value: 1 }
        ],
        valueKind: 'boolean'
      },
      {
        field: 'specialMoveId',
        label: 'Special Move',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Scratch', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ivHp',
        label: 'HP IV',
        maximumValue: 31,
        minimumValue: -4,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivAttack',
        label: 'Attack IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivDefense',
        label: 'Defense IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivSpecialAttack',
        label: 'Sp. Atk IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivSpecialDefense',
        label: 'Sp. Def IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivSpeed',
        label: 'Speed IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'flawlessIvCount',
        label: 'IV preset',
        maximumValue: 6,
        minimumValue: 0,
        options: [
          { label: 'Random IVs', value: 0 },
          { label: '3 Guaranteed Perfect IVs', value: 3 },
          { label: '6 Guaranteed Perfect IVs', value: 6 }
        ],
        valueKind: 'integer'
      }
    ],
    gifts: [
      {
        ability: 1,
        abilityLabel: 'Ability 1',
        abilityOptions: [
          { label: 'Default - 065 Overgrow', value: 0 },
          { label: 'Ability 1 - 065 Overgrow', value: 1 },
          { label: 'Ability 2 - 065 Overgrow', value: 2 },
          { label: 'Hidden Ability - 000 None', value: 3 }
        ],
        ballItem: 'Poke Ball',
        ballItemId: 4,
        canGigantamax: false,
        dynamaxLevel: 0,
        flawlessIvCount: 3,
        form: 0,
        gender: 0,
        genderLabel: 'Random',
        giftIndex: 0,
        heldItem: null,
        heldItemId: 0,
        isEgg: false,
        ivs: {
          attack: -1,
          defense: -1,
          hp: -4,
          specialAttack: -1,
          specialDefense: -1,
          speed: -1
        },
        ivSummary: '3 guaranteed perfect IVs',
        label: 'Gift 001: Bulbasaur Lv. 5',
        level: 5,
        nature: 25,
        natureLabel: 'Random',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/script_event_data/add_poke.bin',
          sourceLayer: 'base'
        },
        shinyLock: 1,
        shinyLockLabel: 'Never Shiny',
        specialMove: null,
        specialMoveId: 0,
        species: 'Bulbasaur',
        speciesId: 1
      }
    ],
    stats: {
      eggGiftCount: 0,
      fixedIvGiftCount: 0,
      sourceFileCount: 1,
      totalGiftCount: 1
    },
    summary: giftPokemonWorkflowSummary
  };
  const tradePokemonWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'In-game trade records, requested Pokemon, IV modes, relearn moves, and source provenance.',
    diagnostics: [],
    id: 'tradePokemon',
    label: 'Trade Pokemon'
  };
  const tradePokemonWorkflow: TradePokemonWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'species',
        label: 'Species',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '133 Eevee', value: 133 },
          { label: '810 Grookey', value: 810 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'form',
        label: 'Form',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'level',
        label: 'Level',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'heldItemId',
        label: 'Held item',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ballItemId',
        label: 'Ball item',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '004 Poke Ball', value: 4 },
          { label: '003 Great Ball', value: 3 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'field03',
        label: 'Unknown field 03',
        maximumValue: 65535,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ability',
        label: 'Ability slot',
        maximumValue: 3,
        minimumValue: 0,
        options: [
          { label: 'Default', value: 0 },
          { label: 'Ability 1', value: 1 },
          { label: 'Hidden Ability', value: 3 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'nature',
        label: 'Nature',
        maximumValue: 25,
        minimumValue: 0,
        options: [
          { label: 'Hardy', value: 0 },
          { label: 'Adamant (+Atk/-Sp.Atk)', value: 3 },
          { label: 'Random', value: 25 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'gender',
        label: 'Gender',
        maximumValue: 2,
        minimumValue: 0,
        options: [
          { label: 'Random', value: 0 },
          { label: 'Male', value: 1 },
          { label: 'Female', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'shinyLock',
        label: 'Shiny lock',
        maximumValue: 2,
        minimumValue: 0,
        options: [
          { label: 'Random', value: 0 },
          { label: 'Never Shiny', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'dynamaxLevel',
        label: 'Dynamax level',
        maximumValue: 10,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'canGigantamax',
        label: 'Can Gigantamax',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: 'No', value: 0 },
          { label: 'Yes', value: 1 }
        ],
        valueKind: 'boolean'
      },
      {
        field: 'requiredSpecies',
        label: 'Requested species',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '133 Eevee', value: 133 },
          { label: '810 Grookey', value: 810 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'requiredForm',
        label: 'Requested form',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'requiredNature',
        label: 'Requested nature',
        maximumValue: 25,
        minimumValue: 0,
        options: [
          { label: 'Hardy', value: 0 },
          { label: 'Adamant (+Atk/-Sp.Atk)', value: 3 },
          { label: 'Random', value: 25 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'unknownRequirement',
        label: 'Unknown requirement',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'trainerId',
        label: 'Trainer ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'otGender',
        label: 'OT gender',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: 'Male', value: 0 },
          { label: 'Female', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'memoryCode',
        label: 'Memory code',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'memoryTextVariable',
        label: 'Memory text variable',
        maximumValue: 65535,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'memoryFeel',
        label: 'Memory feeling',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'memoryIntensity',
        label: 'Memory intensity',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'relearnMove0',
        label: 'Relearn move 1',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Scratch', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ivHp',
        label: 'HP IV',
        maximumValue: 31,
        minimumValue: -4,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivAttack',
        label: 'Attack IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivDefense',
        label: 'Defense IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivSpecialAttack',
        label: 'Sp. Atk IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivSpecialDefense',
        label: 'Sp. Def IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivSpeed',
        label: 'Speed IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'flawlessIvCount',
        label: 'IV preset',
        maximumValue: 6,
        minimumValue: 0,
        options: [
          { label: 'Random IVs', value: 0 },
          { label: '3 Guaranteed Perfect IVs', value: 3 },
          { label: '6 Guaranteed Perfect IVs', value: 6 }
        ],
        valueKind: 'integer'
      }
    ],
    stats: {
      fixedIvTradeCount: 0,
      sourceFileCount: 1,
      totalTradeCount: 1
    },
    summary: tradePokemonWorkflowSummary,
    trades: [
      {
        ability: 1,
        abilityLabel: 'Ability 1',
        abilityOptions: [],
        ballItem: 'Poke Ball',
        ballItemId: 4,
        canGigantamax: false,
        dynamaxLevel: 0,
        field03: 7,
        flawlessIvCount: 3,
        form: 1,
        gender: 0,
        genderLabel: 'Random',
        hash0: '0x0000000000000001',
        hash1: '0x0000000000000002',
        hash2: '0x0000000000000003',
        heldItem: null,
        heldItemId: 0,
        ivs: {
          attack: -1,
          defense: -1,
          hp: -4,
          specialAttack: -1,
          specialDefense: -1,
          speed: -1
        },
        ivSummary: '3 guaranteed perfect IVs',
        label: 'Trade 001: Meowth (Galarian) -> Farfetch’d (Galarian) Lv. 15',
        level: 15,
        memoryCode: 12,
        memoryFeel: 3,
        memoryIntensity: 4,
        memoryTextVariable: 99,
        nature: 25,
        natureLabel: 'Random',
        otGender: 0,
        otGenderLabel: 'Male',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/script_event_data/field_trade.bin',
          sourceLayer: 'base'
        },
        relearnMoves: [
          { move: 'Scratch', moveId: 1, slot: 0 },
          { move: null, moveId: 0, slot: 1 },
          { move: null, moveId: 0, slot: 2 },
          { move: null, moveId: 0, slot: 3 }
        ],
        requiredForm: 2,
        requiredNature: 25,
        requiredNatureLabel: 'Random',
        requiredSpecies: 'Meowth',
        requiredSpeciesId: 52,
        shinyLock: 2,
        shinyLockLabel: 'Never Shiny',
        species: 'Farfetch’d',
        speciesId: 83,
        tradeIndex: 0,
        trainerId: 12345,
        unknownRequirement: 0
      }
    ]
  };
  const staticEncountersWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Scripted overworld and story encounter records, IV modes, moves, rules, and source provenance.',
    diagnostics: [],
    id: 'staticEncounters',
    label: 'Static Encounters'
  };
  const staticEncountersWorkflow: StaticEncountersWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'species',
        label: 'Species',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '001 Bulbasaur', value: 1 },
          { label: '810 Grookey', value: 810 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'form',
        label: 'Form',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'level',
        label: 'Level',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'heldItemId',
        label: 'Held item',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ability',
        label: 'Ability slot',
        maximumValue: 3,
        minimumValue: 0,
        options: [
          { label: 'Default', value: 0 },
          { label: 'Ability 1', value: 1 },
          { label: 'Hidden Ability', value: 3 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'nature',
        label: 'Nature',
        maximumValue: 25,
        minimumValue: 0,
        options: [
          { label: 'Hardy', value: 0 },
          { label: 'Adamant (+Atk/-Sp.Atk)', value: 3 },
          { label: 'Random', value: 25 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'gender',
        label: 'Gender',
        maximumValue: 2,
        minimumValue: 0,
        options: [
          { label: 'Random', value: 0 },
          { label: 'Male', value: 1 },
          { label: 'Female', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'shinyLock',
        label: 'Shiny lock',
        maximumValue: 2,
        minimumValue: 0,
        options: [
          { label: 'Random', value: 0 },
          { label: 'Never Shiny', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'encounterScenario',
        label: 'Scenario',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: 'None', value: 0 },
          { label: 'Calyrex', value: 17 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'dynamaxLevel',
        label: 'Dynamax level',
        maximumValue: 10,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'canGigantamax',
        label: 'Can Gigantamax',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: 'No', value: 0 },
          { label: 'Yes', value: 1 }
        ],
        valueKind: 'boolean'
      },
      {
        field: 'move0Id',
        label: 'Move 1',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Scratch', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ivHp',
        label: 'HP IV',
        maximumValue: 31,
        minimumValue: -4,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivAttack',
        label: 'Attack IV',
        maximumValue: 31,
        minimumValue: -1,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'flawlessIvCount',
        label: 'IV preset',
        maximumValue: 6,
        minimumValue: 0,
        options: [
          { label: 'Random IVs', value: 0 },
          { label: '3 Guaranteed Perfect IVs', value: 3 },
          { label: '6 Guaranteed Perfect IVs', value: 6 }
        ],
        valueKind: 'integer'
      }
    ],
    encounters: [
      {
        ability: 3,
        abilityLabel: 'Hidden Ability',
        abilityOptions: [],
        canGigantamax: true,
        dynamaxLevel: 10,
        encounterId: '0x0102030405060708',
        encounterIndex: 0,
        encounterScenario: 17,
        encounterScenarioLabel: 'Calyrex',
        evs: {
          attack: 2,
          defense: 3,
          hp: 1,
          specialAttack: 4,
          specialDefense: 5,
          speed: 6
        },
        flawlessIvCount: null,
        form: 1,
        gender: 1,
        genderLabel: 'Male',
        heldItem: 'Potion',
        heldItemId: 1,
        ivs: {
          attack: 30,
          defense: 29,
          hp: 31,
          specialAttack: 27,
          specialDefense: 26,
          speed: 28
        },
        ivSummary: 'HP 31 / Atk 30 / Def 29 / SpA 27 / SpD 26 / Spe 28',
        label: 'Static 001: Grookey (Form 1) Lv. 50 | Calyrex',
        level: 50,
        moves: [
          { move: 'Scratch', moveId: 1, slot: 0 },
          { move: 'Growl', moveId: 2, slot: 1 },
          { move: null, moveId: 0, slot: 2 },
          { move: null, moveId: 0, slot: 3 }
        ],
        nature: 25,
        natureLabel: 'Random',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/script_event_data/event_encount_data.bin',
          sourceLayer: 'base'
        },
        shinyLock: 2,
        shinyLockLabel: 'Never Shiny',
        species: 'Grookey',
        speciesId: 810
      }
    ],
    stats: {
      fixedIvEncounterCount: 1,
      gigantamaxEncounterCount: 1,
      sourceFileCount: 1,
      totalEncounterCount: 1
    },
    summary: staticEncountersWorkflowSummary
  };
  const rentalPokemonWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Rental Pokemon records, fixed IVs, EVs, items, moves, and source provenance.',
    diagnostics: [],
    id: 'rentalPokemon',
    label: 'Rental Pokemon'
  };
  const rentalPokemonWorkflow: RentalPokemonWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'species',
        label: 'Species',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '001 Bulbasaur', value: 1 },
          { label: '810 Grookey', value: 810 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'form',
        label: 'Form',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'level',
        label: 'Level',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'heldItemId',
        label: 'Held item',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Potion', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ballItemId',
        label: 'Ball',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '004 Poke Ball', value: 4 },
          { label: '005 Great Ball', value: 5 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ability',
        label: 'Ability slot',
        maximumValue: 3,
        minimumValue: 0,
        options: [
          { label: 'Default', value: 0 },
          { label: 'Ability 1', value: 1 },
          { label: 'Hidden Ability', value: 3 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'nature',
        label: 'Nature',
        maximumValue: 24,
        minimumValue: 0,
        options: [
          { label: 'Hardy', value: 0 },
          { label: 'Adamant (+Atk/-Sp.Atk)', value: 3 },
          { label: 'Jolly (+Spe/-Sp.Atk)', value: 13 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'gender',
        label: 'Gender',
        maximumValue: 2,
        minimumValue: 0,
        options: [
          { label: 'Random', value: 0 },
          { label: 'Male', value: 1 },
          { label: 'Female', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'trainerId',
        label: 'Trainer ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'move0Id',
        label: 'Move 1',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Scratch', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'evHp',
        label: 'HP EV',
        maximumValue: 252,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ivHp',
        label: 'HP IV',
        maximumValue: 31,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'fixedIvPreset',
        label: 'IV preset',
        maximumValue: 31,
        minimumValue: 0,
        options: [
          { label: '0 IVs', value: 0 },
          { label: '6 Guaranteed Perfect IVs', value: 31 }
        ],
        valueKind: 'integer'
      }
    ],
    rentals: [
      {
        ability: 1,
        abilityLabel: 'Ability 1',
        abilityOptions: [],
        ballItem: 'Poke Ball',
        ballItemId: 4,
        evs: {
          attack: 252,
          defense: 0,
          hp: 4,
          specialAttack: 0,
          specialDefense: 0,
          speed: 252
        },
        form: 0,
        gender: 1,
        genderLabel: 'Male',
        hash1: '0x0000000000000010',
        hash2: '0x0000000000000020',
        hasPerfectIvs: true,
        heldItem: 'Potion',
        heldItemId: 1,
        ivs: {
          attack: 31,
          defense: 31,
          hp: 31,
          specialAttack: 31,
          specialDefense: 31,
          speed: 31
        },
        ivSummary: 'HP 31 / Atk 31 / Def 31 / SpA 31 / SpD 31 / Spe 31',
        label: 'Rental 001: Grookey Lv. 50',
        level: 50,
        moves: [
          { move: 'Scratch', moveId: 1, slot: 0 },
          { move: null, moveId: 0, slot: 1 },
          { move: null, moveId: 0, slot: 2 },
          { move: null, moveId: 0, slot: 3 }
        ],
        nature: 0,
        natureLabel: 'Hardy',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/script_event_data/rental.bin',
          sourceLayer: 'base'
        },
        rentalIndex: 0,
        species: 'Grookey',
        speciesId: 810,
        trainerId: 12345
      }
    ],
    stats: {
      perfectIvRentalCount: 1,
      sourceFileCount: 1,
      totalRentalCount: 1
    },
    summary: rentalPokemonWorkflowSummary
  };
  const dynamaxAdventuresWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description:
      'Advanced Adventure Pokemon editor that updates the loose table and matching ExeFS mirrors for safe Pokemon fields.',
    diagnostics: [],
    id: 'dynamaxAdventures',
    label: 'Dynamax Adventures'
  };
  const dynamaxAdventuresWorkflow: DynamaxAdventuresWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'species',
        label: 'Species',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '001 Bulbasaur', value: 1 },
          { label: '810 Grookey', value: 810 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'form',
        label: 'Form',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'level',
        label: 'Level',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'ballItemId',
        label: 'Ball item',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '004 Poke Ball', value: 4 },
          { label: '005 Great Ball', value: 5 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ability',
        label: 'Ability roll',
        maximumValue: 4,
        minimumValue: 0,
        options: [
          { label: 'Ability 1', value: 0 },
          { label: 'Hidden Ability', value: 2 },
          { label: 'Any Ability', value: 4 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'gigantamaxState',
        label: 'Gigantamax state',
        maximumValue: 2,
        minimumValue: 0,
        options: [
          { label: 'Normal', value: 1 },
          { label: 'Gigantamax', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'version',
        label: 'Game version',
        maximumValue: 2,
        minimumValue: 0,
        options: [
          { label: 'Both', value: 0 },
          { label: 'Sword', value: 1 },
          { label: 'Shield', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'shinyRoll',
        label: 'Shiny roll',
        maximumValue: 2,
        minimumValue: 0,
        options: [
          { label: 'Enabled', value: 1 },
          { label: 'Disabled', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'move0Id',
        label: 'Move 1',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Scratch', value: 1 },
          { label: '002 Growl', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'guaranteedPerfectIvs',
        label: 'Guaranteed perfect IVs',
        maximumValue: 6,
        minimumValue: 0,
        options: [
          { label: 'Random IVs', value: 0 },
          { label: '5 Guaranteed Perfect IVs', value: 5 },
          { label: '6 Guaranteed Perfect IVs', value: 6 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'ivAttack',
        label: 'Attack IV override',
        maximumValue: 31,
        minimumValue: -1,
        options: [
          { label: 'Random', value: -1 },
          { label: '0 IV', value: 0 },
          { label: '31 IV', value: 31 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'isSingleCapture',
        label: 'Single-capture Pokemon',
        maximumValue: 1,
        minimumValue: 0,
        options: [
          { label: 'No', value: 0 },
          { label: 'Yes', value: 1 }
        ],
        valueKind: 'integer'
      }
    ],
    encounters: [
      {
        ability: 0,
        abilityLabel: 'Ability 1',
        abilityOptions: [],
        adventureIndex: 0,
        ballItem: 'Poke Ball',
        ballItemId: 4,
        entryIndex: 0,
        form: 0,
        gigantamaxLabel: 'Normal',
        gigantamaxState: 1,
        guaranteedPerfectIvs: 5,
        isSingleCapture: true,
        isStoryProgressGated: false,
        ivs: {
          attack: -1,
          defense: -1,
          hp: -5,
          specialAttack: -1,
          specialDefense: -1,
          speed: -1
        },
        ivSummary: '5 guaranteed perfect / Atk Random / Def Random / SpA Random / SpD Random / Spe Random',
        label: 'Adventure 001: Grookey Lv. 65',
        level: 65,
        moves: [
          { move: 'Scratch', moveId: 1, slot: 0 },
          { move: 'Growl', moveId: 2, slot: 1 },
          { move: 'None', moveId: 0, slot: 2 },
          { move: 'None', moveId: 0, slot: 3 }
        ],
        otGender: 0,
        otGenderLabel: 'Male',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin',
          sourceLayer: 'base'
        },
        shinyRoll: 1,
        shinyRollLabel: 'Enabled',
        singleCaptureFlagBlock: '0x0000000000000010',
        species: 'Grookey',
        speciesId: 810,
        uiMessageId: '0x0000000000000020',
        vanillaPokemon: {
          ability: 0,
          abilityLabel: 'Ability 1',
          form: 0,
          gigantamaxLabel: 'Normal',
          gigantamaxState: 1,
          guaranteedPerfectIvs: 6,
          ivs: {
            attack: 31,
            defense: -1,
            hp: -5,
            specialAttack: -1,
            specialDefense: -1,
            speed: -1
          },
          ivSummary:
            '6 guaranteed perfect / Atk 31 / Def Random / SpA Random / SpD Random / Spe Random',
          level: 60,
          moves: [
            { move: 'Growl', moveId: 2, slot: 0 },
            { move: 'None', moveId: 0, slot: 1 },
            { move: 'None', moveId: 0, slot: 2 },
            { move: 'None', moveId: 0, slot: 3 }
          ],
          species: 'Bulbasaur',
          speciesId: 1
        },
        version: 0,
        versionLabel: 'Both'
      }
    ],
    stats: {
      guaranteedPerfectIvEncounterCount: 1,
      singleCaptureCount: 1,
      sourceFileCount: 1,
      storyGatedCount: 0,
      totalEncounterCount: 1
    },
    summary: dynamaxAdventuresWorkflowSummary
  };
  const shopsWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Shop inventories, item metadata, and source provenance.',
    diagnostics: [],
    id: 'shops',
    label: 'Shops'
  };
  const shopsWorkflow: ShopsWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'itemId',
        label: 'Item',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { itemName: 'Potion', label: '0001 Potion (Medicine)', price: 300, value: 1 },
          { itemName: 'Antidote', label: '0002 Antidote (Medicine)', price: 200, value: 2 },
          { itemName: 'None', label: '0000 None (Medicine)', price: 0, value: 0 }
        ],
        valueKind: 'integer'
      }
    ],
    shops: [
      {
        currency: 'Money',
        inventory: [
          {
            isKnownItem: true,
            itemId: 1,
            itemName: 'Potion',
            price: 300,
            slot: 1,
            stockLimit: null
          },
          {
            isKnownItem: true,
            itemId: 2,
            itemName: 'Antidote',
            price: 200,
            slot: 2,
            stockLimit: null
          }
        ],
        inventoryCount: 1,
        inventoryIndex: 1,
        inventoryLabel: 'Inventory',
        inventorySummary: 'Potion, Antidote',
        kind: 'Single',
        location: 'Poke Mart',
        name: 'Poke Mart',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/appli/shop/bin/shop_data.bin',
          sourceLayer: 'base'
        },
        shopId: 'single:1F3FF031A3A24490',
        sourceHash: '0x1F3FF031A3A24490'
      }
    ],
    stats: {
      sourceFileCount: 1,
      totalInventoryItemCount: 2,
      totalShopCount: 1
    },
    summary: shopsWorkflowSummary
  };
  const encountersWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Encounter tables, wild slots, levels, weather, and source provenance.',
    diagnostics: [],
    id: 'encounters',
    label: 'Wild Encounters'
  };
  const encountersWorkflow: EncountersWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'speciesId',
        label: 'Species ID',
        maximumValue: 65535,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'form',
        label: 'Form',
        maximumValue: 255,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'probability',
        label: 'Probability',
        maximumValue: 100,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'levelMin',
        label: 'Min Level',
        maximumValue: 100,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'levelMax',
        label: 'Max Level',
        maximumValue: 100,
        minimumValue: 0,
        valueKind: 'integer'
      }
    ],
    stats: {
      sourceFileCount: 1,
      totalSlotCount: 2,
      totalTableCount: 1
    },
    summary: encountersWorkflowSummary,
    tables: [
      {
        archiveMember: 'encount_symbol_k.bin',
        area: 'Symbol',
        encounterType: 'Normal',
        gameVersion: 'Sword',
        location: 'Zone 0x1122334455667788',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/archive/field/resident/data_table.gfpak',
          sourceLayer: 'base'
        },
        slots: [
          {
            form: 0,
            levelMax: 8,
            levelMin: 3,
            slot: 1,
            speciesId: 1,
            species: 'Bulbasaur',
            timeOfDay: null,
            weather: 'Normal',
            weight: 35
          },
          {
            form: 1,
            levelMax: 8,
            levelMin: 3,
            slot: 2,
            speciesId: 4,
            species: 'Charmander',
            timeOfDay: null,
            weather: 'Normal',
            weight: 15
          }
        ],
        tableId: 'sword:symbol:0:1122334455667788:0'
      }
    ]
  };
  const raidRewardsWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Raid reward tables, den ranks, item quantities, and source provenance.',
    diagnostics: [],
    id: 'raidRewards',
    label: 'Raid Rewards'
  };
  const raidBonusRewardsWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Raid bonus reward tables, item quantities, den usage, and source provenance.',
    diagnostics: [],
    id: 'raidBonusRewards',
    label: 'Raid Bonus Rewards'
  };
  const raidBattlesWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Raid Pokemon slots, star probabilities, ability rolls, guaranteed perfect IVs, and source provenance.',
    diagnostics: [],
    id: 'raidBattles',
    label: 'Raid Battles'
  };
  const raidBattlesWorkflow: RaidBattlesWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'species',
        label: 'Species',
        maximumValue: 65535,
        minimumValue: 0,
        options: [
          { label: '025 Pikachu', value: 25 },
          { label: '133 Eevee', value: 133 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'form',
        label: 'Form',
        maximumValue: 31,
        minimumValue: 0,
        options: [
          { label: 'Base', value: 0 },
          { label: 'Form 1', value: 1 },
          { label: 'Form 2', value: 2 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'flawlessIvs',
        label: 'Guaranteed perfect IVs',
        maximumValue: 6,
        minimumValue: 0,
        options: [
          { label: 'Random IVs', value: 0 },
          { label: '4 Guaranteed Perfect IVs', value: 4 },
          { label: '6 Guaranteed Perfect IVs', value: 6 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'star5Probability',
        label: '5-star probability',
        maximumValue: 100,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      }
    ],
    stats: {
      gigantamaxSlotCount: 1,
      sourceFileCount: 2,
      totalSlotCount: 2,
      totalTableCount: 1
    },
    summary: raidBattlesWorkflowSummary,
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
            ability: 4,
            abilityLabel: 'Any Ability',
            abilityOptions: [],
            bonusTableHash: '0x1020304050607080',
            bonusRewardLink: {
              isMatched: true,
              preview: '1 reward: Armorite Ore',
              rewardItemCount: 1,
              rewardKind: 'bonus',
              rewardKindLabel: 'Bonus',
              sourceTableHash: '0x1020304050607080',
              tableId: 'bonus:0:1020304050607080'
            },
            dropTableHash: '0xAABBCCDD00112233',
            dropRewardLink: {
              isMatched: true,
              preview: '2 rewards: Exp. Candy L, Rare Candy',
              rewardItemCount: 2,
              rewardKind: 'drop',
              rewardKindLabel: 'Drop',
              sourceTableHash: '0xAABBCCDD00112233',
              tableId: 'drop:0:AABBCCDD00112233'
            },
            entryIndex: 0,
            flawlessIvs: 4,
            form: 1,
            formOptions: [
              { label: 'Base', value: 0 },
              { label: 'Form 1', value: 1 }
            ],
            gender: 1,
            genderLabel: 'Male',
            isGigantamax: true,
            levelTableHash: '0x1122334455667788',
            probabilities: [100, 20, 30, 40, 50],
            probabilitySummary: '1-star 100% / 2-star 20% / 3-star 30% / 4-star 40% / 5-star 50%',
            slot: 1,
            species: 'Eevee',
            speciesId: 133
          },
          {
            ability: 0,
            abilityLabel: 'Ability 1',
            abilityOptions: [],
            bonusTableHash: '0x0807060504030201',
            bonusRewardLink: {
              isMatched: false,
              preview: 'No loaded bonus table matches this hash',
              rewardItemCount: 0,
              rewardKind: 'bonus',
              rewardKindLabel: 'Bonus',
              sourceTableHash: '0x0807060504030201',
              tableId: ''
            },
            dropTableHash: '0xAABBCCDD00112233',
            dropRewardLink: {
              isMatched: true,
              preview: '2 rewards: Exp. Candy L, Rare Candy',
              rewardItemCount: 2,
              rewardKind: 'drop',
              rewardKindLabel: 'Drop',
              sourceTableHash: '0xAABBCCDD00112233',
              tableId: 'drop:0:AABBCCDD00112233'
            },
            entryIndex: 1,
            flawlessIvs: 0,
            form: 0,
            formOptions: [{ label: 'Base', value: 0 }],
            gender: 0,
            genderLabel: 'Random',
            isGigantamax: false,
            levelTableHash: '0x2233445566778899',
            probabilities: [5, 10, 15, 20, 25],
            probabilitySummary: '1-star 5% / 2-star 10% / 3-star 15% / 4-star 20% / 5-star 25%',
            slot: 2,
            species: 'Pikachu',
            speciesId: 25
          }
        ],
        sourceTableHash: '0xAABBCCDD00112233',
        tableId: 'raid:0:AABBCCDD00112233',
        tableIndex: 0
      }
    ]
  };
  const raidRewardsWorkflow: RaidRewardsWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'itemId',
        label: 'Item ID',
        maximumValue: 65535,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'star5Value',
        label: '5-star value',
        maximumValue: 999,
        minimumValue: 0,
        valueKind: 'integer'
      }
    ],
    stats: {
      sourceFileCount: 1,
      totalRewardItemCount: 1,
      totalTableCount: 1
    },
    summary: raidRewardsWorkflowSummary,
    tables: [
      {
        archiveMember: 'nest_hole_drop_rewards.bin',
        denId: 'table_AABBCCDD00112233',
        displayName: 'Drop 000 | SW Den 0 Slot 00, 1-5-Star Eevee-1',
        gameVersion: 'Sword/Shield',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/archive/field/resident/data_table.gfpak',
          sourceLayer: 'base'
        },
        rank: 0,
        rewardKind: 'drop',
        rewardKindLabel: 'Drop',
        rewards: [
          {
            entryId: 10,
            itemId: 3,
            itemName: 'Exp. Candy L',
            quantity: 0,
            slot: 1,
            values: [40, 30, 20, 10, 5],
            weight: 40
          }
        ],
        sourceTableHash: '0xAABBCCDD00112233',
        tableId: 'drop:0:AABBCCDD00112233',
        tableIndex: 0
      }
    ]
  };
  const raidBonusRewardsWorkflow: RaidRewardsWorkflow = {
    ...raidRewardsWorkflow,
    summary: raidBonusRewardsWorkflowSummary,
    tables: raidRewardsWorkflow.tables.map((table) => ({
      ...table,
      archiveMember: 'nest_hole_bonus_rewards.bin',
      displayName: 'Bonus 000 | SW Den 0 Slot 00, 1-5-Star Eevee-1',
      rewardKind: 'bonus',
      rewardKindLabel: 'Bonus',
      sourceTableHash: '0x1020304050607080',
      tableId: 'bonus:0:1020304050607080'
    }))
  };
  const placementWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Placed objects, map coordinates, item pickups, and source provenance.',
    diagnostics: [],
    id: 'placement',
    label: 'Placement'
  };
  const placementWorkflow: PlacementWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'quantity',
        label: 'Quantity',
        maximumValue: 999,
        minimumValue: 0,
        valueKind: 'integer'
      }
    ],
    objects: [
      {
        archiveMember: 'a_test.bin',
        chance: null,
        chanceIndex: null,
        itemHash: '0xAABBCCDD00112233',
        itemId: 1,
        itemName: 'Potion',
        label: 'Field item: Potion',
        map: 'Route 1',
        objectId: 'a_test.bin|0|fieldItem|0|-',
        objectIndex: 0,
        objectType: 'FieldItem',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/archive/field/resident/placement.gfpak',
          sourceLayer: 'base'
        },
        quantity: 1,
        rotationY: 90,
        scriptId: 'visible_potion',
        x: 10.5,
        y: 0,
        zoneIndex: 0,
        z: -4.25
      }
    ],
    stats: {
      sourceFileCount: 3,
      totalAreaCount: 1,
      totalObjectCount: 1
    },
    summary: placementWorkflowSummary
  };
  const behaviorWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Symbol encounter behavior parameters and source provenance.',
    diagnostics: [],
    id: 'behavior',
    label: 'Behavior'
  };
  const behaviorWorkflow: BehaviorWorkflow = {
    diagnostics: [],
    entries: [
      {
        behavior: 'Common',
        behaviorLabel: 'Common',
        entryId: 'behavior:0',
        fields: [
          { field: 'speciesId', value: '25' },
          { field: 'form', value: '0' },
          { field: 'behavior', value: 'Common' },
          { field: 'modelPart', value: 'body' },
          { field: 'hitboxRadius', value: '1.5' },
          { field: 'grassShakeRadius', value: '2' },
          { field: 'hash1', value: '0x0000000000000001' }
        ],
        form: 0,
        grassShakeRadius: 2,
        hash1: '0x0000000000000001',
        hash2: '0x0000000000000002',
        hitboxRadius: 1.5,
        index: 0,
        internalSpeciesName: 'PIKACHU',
        label: '#0 Pikachu - Common',
        modelPart: 'body',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/field/param/symbol_encount_mons_param/symbol_encount_mons_param.bin',
          sourceLayer: 'base'
        },
        speciesId: 25,
        speciesName: 'Pikachu'
      }
    ],
    fields: [
      {
        description: 'Species ID used by this symbol encounter behavior entry.',
        field: 'speciesId',
        group: 'Identity',
        isReadOnly: false,
        label: 'Pokemon',
        maximumValue: 999,
        minimumValue: 0,
        options: [{ label: 'Pikachu', value: '25' }],
        valueKind: 'integer'
      },
      {
        description: 'Form index used by this symbol encounter behavior entry.',
        field: 'form',
        group: 'Identity',
        isReadOnly: false,
        label: 'Form',
        maximumValue: 999,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        description: 'Named movement behavior used by this symbol encounter.',
        field: 'behavior',
        group: 'Behavior',
        isReadOnly: false,
        label: 'Behavior',
        maximumValue: 128,
        minimumValue: 0,
        options: [{ label: 'Common', value: 'Common' }],
        valueKind: 'string'
      },
      {
        description: 'Internal reference hash. This is shown for inspection only.',
        field: 'hash1',
        group: 'Internal References',
        isReadOnly: true,
        label: 'Hash 1',
        maximumValue: Number.MAX_SAFE_INTEGER,
        minimumValue: 0,
        valueKind: 'hash'
      }
    ],
    stats: {
      sourceFileCount: 1,
      totalBehaviorCount: 1,
      totalEntryCount: 1
    },
    summary: behaviorWorkflowSummary
  };
  const flagworkSaveWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Flagwork hash tables, save keys, and source provenance.',
    diagnostics: [],
    id: 'flagworkSave',
    label: 'Flagwork and Save Inspectors'
  };
  const flagworkSaveWorkflow: FlagworkSaveWorkflow = {
    diagnostics: [],
    flags: [
      {
        category: 'system_flags',
        defaultValue: 'false',
        description: 'Flag hash 0x1122334455667788 uses save key 0x55667788.',
        flagId: 'system_flags:0000',
        hash: '0x1122334455667788',
        index: 0,
        kind: 'Flag',
        low32Key: '0x55667788',
        name: 'FE_TEST_FLAG',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/flagwork/system_flags.tbl',
          sourceLayer: 'base'
        },
        table: 'system_flags',
        valueKind: 'boolean'
      },
      {
        category: 'scene_work',
        defaultValue: '0',
        description: 'Work hash 0x99AABBCCDDEEFF00 uses save key 0xDDEEFF00.',
        flagId: 'scene_work:0000',
        hash: '0x99AABBCCDDEEFF00',
        index: 0,
        kind: 'Work',
        low32Key: '0xDDEEFF00',
        name: 'WK_SCENE_MAIN',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/flagwork/scene_work.tbl',
          sourceLayer: 'base'
        },
        table: 'scene_work',
        valueKind: 'integer'
      }
    ],
    saveBlocks: [
      {
        blockId: 'scene_work:0000:0xDDEEFF00',
        description: 'Save work key 0xDDEEFF00 is derived from WK_SCENE_MAIN.',
        hash: '0x99AABBCCDDEEFF00',
        key: '0xDDEEFF00',
        kind: 'Work',
        name: 'WK_SCENE_MAIN',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/flagwork/scene_work.tbl',
          sourceLayer: 'base'
        },
        valueKind: 'integer'
      }
    ],
    saveFile: {
      description: 'Save file is configured for read-only inspection.',
      fileName: 'main',
      sha256: '01020304',
      sizeBytes: 4,
      status: 'available'
    },
    stats: {
      hasSaveFile: true,
      sourceFileCount: 2,
      totalFlagCount: 2,
      totalSaveBlockCount: 1
    },
    summary: flagworkSaveWorkflowSummary
  };
  const exeFsPatchWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'ExeFS main validation, patch anchors, segment hashes, and source provenance.',
    diagnostics: [],
    id: 'exefsPatches',
    label: 'ExeFS Patch Manager'
  };
  const bagHookWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description:
      'Installs the shared Bag Hook V2 startup script with 20 disabled grant slots.',
    diagnostics: [],
    id: 'bagHook',
    label: 'Bag Hook'
  };
  const bagHookWorkflow: BagHookWorkflow = {
    diagnostics: [],
    installMessage: 'Bag Hook V2 can be installed with all grant slots disabled.',
    installStatus: canEdit ? 'available' : 'readOnly',
    slots: Array.from({ length: 20 }, (_, index) => {
      const slot = index + 1;
      return {
        isReserved: true,
        itemId: slot === 1 ? 1128 : null,
        itemName: slot === 1 ? 'Royal Candy' : 'None',
        notes: slot === 1 ? 'Royal Candy occupies slot 1.' : 'Disabled empty slot.',
        owner: slot === 1 ? 'Royal Candy' : 'Available for Starting Items',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/script/amx/main_event_0020.amx',
          sourceLayer: 'base'
        },
        quantity: slot === 1 ? 1 : null,
        reservedFor: slot === 1 ? 'Royal Candy' : 'Starting Items',
        slot,
        status: slot === 1 ? 'occupied' : 'empty'
      };
    }),
    stats: {
      emptySlotCount: 19,
      occupiedSlotCount: 1,
      reservedSlotCount: 20,
      sourceFileCount: 1,
      totalSlotCount: 20
    },
    summary: bagHookWorkflowSummary
  };
  const catchCapWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description:
      'Patches the display and runtime capture checks for badge-level catch caps 0-7; eight badges is locked at Lv.100.',
    diagnostics: [],
    id: 'catchCap',
    label: 'Catch Cap Editor'
  };
  const catchCapWorkflow: CatchCapWorkflow = {
    capLogicSha256: 'AABBCC',
    caps: Array.from({ length: 9 }, (_, badgeCount) => ({
      badgeCount,
      label: `${badgeCount} badges`,
      levelCap: badgeCount === 8 ? 100 : 20 + badgeCount * 5,
      maximumLevelCap: 100,
      minimumLevelCap: badgeCount === 8 ? 100 : 1
    })),
    diagnostics: [],
    installMessage: 'Catch Cap Editor can patch display and runtime capture checks in exefs/main.',
    installStatus: canEdit ? 'available' : 'readOnly',
    logicExpression: 'badge_count < 8 ? cap_table[badge_count] : 100',
    provenance: {
      fileState: 'baseOnly',
      sourceFile: 'exefs/main',
      sourceLayer: 'base'
    },
    stats: {
      sourceFileCount: 1,
      totalCapCount: 9
    },
    summary: catchCapWorkflowSummary
  };
  const hyperTrainingWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description:
      'Advanced editor for the Battle Tower Hyper Training NPC minimum level cutoff, matching English dialogue, and picker cutoff checks.',
    diagnostics: [],
    id: 'hyperTraining',
    label: 'Hyper Training'
  };
  const hyperTrainingWorkflow: HyperTrainingWorkflow = {
    diagnostics: [],
    installMessage: 'Hyper Training is using the vanilla Lv.100 minimum.',
    installStatus: canEdit ? 'available' : 'readOnly',
    levelRule: {
      dialogueSummary: 'English dialogue lines 0 and 3 mention the cutoff.',
      maximumAllowedLevel: 100,
      minimumAllowedLevel: 1,
      minimumLevel: 100,
      runtimeSummary:
        'Picker cutoff lives at main.text+0x00F9A314 and related Hyper Training list/detail checks.',
      scriptCell: 'AMX code cell 2294 (RND_TO_FLOOR operand)',
      vanillaMinimumLevel: 100
    },
    sources: [
      {
        label: 'Hyper Training script',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/script/amx/hyper_training.amx',
          sourceLayer: 'base'
        },
        relativePath: 'romfs/bin/script/amx/hyper_training.amx',
        sourceId: 'script',
        status: 'available'
      },
      {
        label: 'English Hyper Training dialogue',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/message/English/script/sub_event_007.dat',
          sourceLayer: 'base'
        },
        relativePath: 'romfs/bin/message/English/script/sub_event_007.dat',
        sourceId: 'dialogue',
        status: 'available'
      },
      {
        label: 'Hyper Training picker runtime',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'exefs/main',
          sourceLayer: 'base'
        },
        relativePath: 'exefs/main',
        sourceId: 'runtime',
        status: 'available'
      }
    ],
    stats: {
      outputFileCount: 3,
      sourceFileCount: 3
    },
    summary: hyperTrainingWorkflowSummary
  };
  const typeChartWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Advanced editor for the Sword/Shield type-effectiveness table in exefs/main.',
    diagnostics: [],
    id: 'typeChart',
    label: 'Type Chart'
  };
  const typeChartTypes = [
    ['NOR', 'Normal', '#A8A878'],
    ['FIR', 'Fire', '#F05030'],
    ['WAT', 'Water', '#6890F0'],
    ['ELE', 'Electric', '#F8D030'],
    ['GRA', 'Grass', '#78C850'],
    ['ICE', 'Ice', '#78C8F0'],
    ['FIG', 'Fighting', '#A05038'],
    ['POI', 'Poison', '#A040A0'],
    ['GRO', 'Ground', '#E0C068'],
    ['FLY', 'Flying', '#8080F0'],
    ['PSY', 'Psychic', '#F85888'],
    ['BUG', 'Bug', '#A8B820'],
    ['ROC', 'Rock', '#B8A038'],
    ['GHO', 'Ghost', '#6060B0'],
    ['DRA', 'Dragon', '#7038F8'],
    ['DAR', 'Dark', '#705848'],
    ['STE', 'Steel', '#B8B8D0'],
    ['FAI', 'Fairy', '#EE99EE']
  ] as const;
  const typeChartWorkflow: TypeChartWorkflow = {
    buildId: 'A3B75BCD3311385AEED67FBEEB79CBB7BF02F471',
    cells: Array.from({ length: 18 * 18 }, (_, index) => ({
      attackTypeIndex: Math.floor(index / 18),
      defenseTypeIndex: index % 18,
      effectiveness: 4 as const,
      vanillaEffectiveness: 4 as const
    })),
    chartOffsetHex: 'main.ro+0x00743600',
    detectedGame: 'sword',
    diagnostics: [],
    installMessage: 'Type Chart is using the vanilla Sword/Shield effectiveness table.',
    installStatus: canEdit ? 'available' : 'readOnly',
    source: {
      label: 'ExeFS main',
      provenance: {
        fileState: 'baseOnly',
        sourceFile: 'exefs/main',
        sourceLayer: 'base'
      },
      relativePath: 'exefs/main',
      sourceId: 'runtime',
      status: 'available'
    },
    stats: {
      chartCellCount: 18 * 18,
      outputFileCount: 1,
      sourceFileCount: 1
    },
    summary: typeChartWorkflowSummary,
    types: typeChartTypes.map(([shortLabel, label, color], typeIndex) => ({
      color,
      label,
      shortLabel,
      typeIndex
    }))
  };
  const gymUniformRemovalWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Keeps the player in their current outfit during gym challenges and gym battles.',
    diagnostics: [],
    id: 'gymUniformRemoval',
    label: 'Gym Uniform Removal'
  };
  const gymUniformRemovalWorkflow: GymUniformRemovalWorkflow = {
    buildId: 'A3B75BCD3311385AEED67FBEEB79CBB7BF02F471',
    diagnostics: [],
    installMessage: 'Gym Uniform Removal can create a build-ID IPS patch in exefs.',
    installStatus: canEdit ? 'available' : 'readOnly',
    patchOffsetHex: 'main.text+0x01472600',
    provenance: {
      fileState: 'baseOnly',
      sourceFile: 'exefs/main',
      sourceLayer: 'base'
    },
    reservedRegions: [
      {
        label: 'Gym Uniform Removal gym outfit handler override',
        length: 8,
        offsetLabel: 'text+0x1472600..0x1472607',
        regionId: 'gym-uniform-removal-sword-handler',
        rule: 'do-not-overwrite',
        startOffset: 0x01472600
      }
    ],
    stats: {
      reservedMainTextRegionCount: 1,
      sourceFileCount: 1
    },
    stubKind: 'vanilla handler',
    summary: gymUniformRemovalWorkflowSummary
  };
  const ivScreenWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Installs the Pokemon Summary raw-IV screen hook.',
    diagnostics: [],
    id: 'ivScreen',
    label: 'IV Screen'
  };
  const ivScreenWorkflow: IvScreenWorkflow = {
    diagnostics: [],
    hookSiteOffsetHex: 'main.text+0x0138F268',
    hyperTrainingWrapperOffsetHex: 'main.text+0x007790D0',
    installMessage: 'IV Screen can patch exefs/main.',
    installStatus: canEdit ? 'available' : 'readOnly',
    marker: 'SWSH_IV_DISPLAY_V1',
    provenance: {
      fileState: 'baseOnly',
      sourceFile: 'exefs/main',
      sourceLayer: 'base'
    },
    rawIvGetterOffsetHex: 'main.text+0x00779070',
    reservedRegions: [
      {
        label: 'IV Screen normal stats graph refresh hook branch site',
        length: 4,
        offsetLabel: 'text+0x138F268..0x138F26B',
        regionId: 'iv-screen-hook-site',
        rule: 'do-not-overwrite',
        startOffset: 0x0138f268
      }
    ],
    stats: {
      reservedMainTextRegionCount: 1,
      sourceFileCount: 1
    },
    summary: ivScreenWorkflowSummary
  };
  const startingItemsWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Adds selected startup item grants through Bag Hook slots 2-20.',
    diagnostics: [],
    id: 'startingItems',
    label: 'Starting Items'
  };
  const startingItemsWorkflow: StartingItemsWorkflow = {
    diagnostics: [],
    grants: Array.from({ length: 19 }, (_, index) => {
      const slot = index + 2;
      return {
        isKeyItem: false,
        itemId: slot === 2 ? 1 : null,
        itemName: slot === 2 ? 'Master Ball' : 'None',
        owner: slot === 2 ? 'Starting Items' : 'Available for Starting Items',
        provenance: {
          fileState: 'layeredOnly',
          sourceFile: 'romfs/bin/script/amx/main_event_0020.amx',
          sourceLayer: 'layered'
        },
        quantity: slot === 2 ? 5 : 1,
        slot,
        status: slot === 2 ? 'occupied' : 'empty'
      };
    }),
    installMessage: 'Starting Items can claim Bag Hook slots 2-20.',
    installStatus: canEdit ? 'available' : 'blocked',
    itemOptions: [
      {
        category: 'Items',
        isKeyItem: false,
        itemId: 1,
        name: 'Master Ball'
      },
      {
        category: 'Key Items',
        isKeyItem: true,
        itemId: 700,
        name: 'Bike'
      }
    ],
    stats: {
      itemOptionCount: 2,
      occupiedGrantSlotCount: 1,
      sourceFileCount: 2,
      totalGrantSlotCount: 19
    },
    summary: startingItemsWorkflowSummary
  };
  const exeFsPatchWorkflow: ExeFsPatchWorkflow = {
    checks: [
      {
        actual: 'text+0x7BC338',
        area: '.text',
        checkId: 'exefs-main-compatibility:patch-code-cave',
        expected: '12 zero bytes after text+0x7BC338',
        name: 'Patch code cave',
        notes: 'A code cave is available for small stubs.',
        offset: 'text+0x7BC338',
        patchId: 'exefs-main-compatibility',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'exefs/main',
          sourceLayer: 'base'
        },
        status: 'Pass'
      },
      {
        actual: '0',
        area: '.text',
        checkId: 'exefs-main-compatibility:royal-candy-immediate-scan',
        expected: '0 patched CMP immediates in vanilla main',
        name: 'Royal Candy immediate scan',
        notes: 'No obvious item-id 1128 CMP immediates were found in the known route registers.',
        offset: '',
        patchId: 'exefs-main-compatibility',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'exefs/main',
          sourceLayer: 'base'
        },
        status: 'Info'
      }
    ],
    diagnostics: [],
    patches: [
      {
        description:
          'Validates Sword/Shield ExeFS main structure, segment hashes, code-cave availability, and known patch anchors.',
        details: [
          'Build ID: ABABABABABABABABABABABABABABABABABABABABABABABABABABABABABABAB',
          'File size: 0x7DDAB0 bytes',
          'Checks: 26 total, 0 failing, 0 warnings'
        ],
        name: 'ExeFS main compatibility',
        patchId: 'exefs-main-compatibility',
        patchKind: 'NSO signature scan',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'exefs/main',
          sourceLayer: 'base'
        },
        status: 'available',
        targetFile: 'exefs/main'
      }
    ],
    segments: [
      {
        compressedSize: '0x7DDA90',
        decompressedSize: '0x7DDA90',
        fileOffset: 'file+0x100',
        hashStatus: 'Pass',
        memoryOffset: '0x0',
        name: '.text',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'exefs/main',
          sourceLayer: 'base'
        },
        segmentId: 'text',
        sha256: 'ABCD'
      }
    ],
    stats: {
      failCount: 0,
      passCount: 24,
      sourceFileCount: 1,
      totalCheckCount: 26,
      totalPatchCount: 1,
      warningCount: 0
    },
    summary: exeFsPatchWorkflowSummary
  };
  const royalCandyWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Royal Candy source readiness, ExeFS compatibility, and LayeredFS output preview.',
    diagnostics: [],
    id: 'royalCandy',
    label: 'Royal Candy Workflows'
  };
  const royalCandyLevelCaps = [
    {
      label: 'Hop 004/005/006',
      levelCap: 10,
      maximumLevelCap: 100,
      milestoneId: '0:A9C039F0598B8A31:0',
      minimumLevelCap: 1,
      progressHash: '0xA9C039F0598B8A31',
      progressKind: 'flag',
      slot: 0,
      workMinimum: null
    },
    {
      label: 'Hop 007/008/009',
      levelCap: 16,
      maximumLevelCap: 100,
      milestoneId: '1:005A329212277F11:0',
      minimumLevelCap: 1,
      progressHash: '0x005A329212277F11',
      progressKind: 'flag',
      slot: 1,
      workMinimum: null
    }
  ];
  const spreadsheetImportWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'CSV and TSV import profiles that execute through backend edit sessions.',
    diagnostics: [],
    id: 'spreadsheetImport',
    label: 'Spreadsheet Import'
  };
  const spreadsheetImportWorkflow: SpreadsheetImportWorkflow = {
    diagnostics: [],
    profiles: [
      {
        columns: [
          {
            column: 1,
            description: 'Existing item ID.',
            header: 'ItemId',
            isRequired: true,
            valueKind: 'integer'
          },
          {
            column: 2,
            description: 'New buy price.',
            header: 'BuyPrice',
            isRequired: false,
            valueKind: 'integer'
          },
          {
            column: 3,
            description: 'New Watts price.',
            header: 'WattsPrice',
            isRequired: false,
            valueKind: 'integer'
          }
        ],
        description: 'Imports item price columns into the Items workflow for change-plan review.',
        name: 'Items Price CSV/TSV',
        profileId: 'items-price-csv',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/item/item.dat',
          sourceLayer: 'base'
        },
        sourceKind: 'csv/tsv',
        status: canEdit ? 'available' : 'readOnly',
        targetWorkflow: 'items'
      }
    ],
    stats: {
      sourceFileCount: 1,
      totalColumnCount: 3,
      totalProfileCount: 1
    },
    summary: spreadsheetImportWorkflowSummary
  };
  const modMergerWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Merge matching RomFS files from two mod folders.',
    diagnostics: [],
    id: 'modMerger',
    label: 'Mod Merger'
  };
  const modMergerWorkflow: ModMergerWorkflow = {
    diagnostics: [],
    directory1Files: [
      {
        name: 'shop_data.bin',
        relativePath: 'romfs/bin/shop_data.bin',
        size: 24,
        status: 'available',
        supportKind: 'Shop data'
      }
    ],
    directory2Files: [
      {
        name: 'shop_data.bin',
        relativePath: 'romfs/bin/shop_data.bin',
        size: 24,
        status: 'available',
        supportKind: 'Shop data'
      }
    ],
    modDirectory1: 'mod-directory-1',
    modDirectory2: 'mod-directory-2',
    outputRootPath: 'output',
    stats: {
      directory1FileCount: 1,
      directory2FileCount: 1,
      matchingFileCount: 1
    },
    summary: modMergerWorkflowSummary
  };
  const createModMergerPreview = (
    selectedDirectory1Files: string[],
    selectedDirectory2Files: string[]
  ): ModMergerPreview => {
    const selectedDirectory2Set = new Set(selectedDirectory2Files);
    const selectedDirectory1Only = selectedDirectory1Files.filter(
      (relativePath) => !selectedDirectory2Set.has(relativePath)
    );
    const selectedDirectory1Set = new Set(selectedDirectory1Files);
    const selectedDirectory2Only = selectedDirectory2Files.filter(
      (relativePath) => !selectedDirectory1Set.has(relativePath)
    );
    const matchedFiles = selectedDirectory1Files.filter((relativePath) =>
      selectedDirectory2Set.has(relativePath)
    );
    const selectedFiles = matchedFiles.map((relativePath) => ({
      conflictCount: 0,
      directory1ChangeCount: 1,
      directory2ChangeCount: 1,
      mergeKind: 'smartMerge',
      outputRelativePath: relativePath,
      relativePath,
      status: 'ready',
      summary: 'Non-overlapping byte changes can be merged safely.',
      supportKind: 'Shop data'
    }));
    const diagnostics =
      selectedDirectory1Only.length === 0 && selectedDirectory2Only.length === 0
        ? []
      : [
          {
            message: 'Files missing from one side were ignored for the merge.',
            severity: 'warning' as const
          }
        ];

    return {
      canApply: selectedFiles.length > 0,
      conflictFileCount: 0,
      conflicts: [],
      diagnostics,
      files: selectedFiles,
      mergeMode: 'smart',
      readyFileCount: selectedFiles.length,
      selectedFileCount: selectedFiles.length,
      status: selectedFiles.length > 0 ? 'ready' : 'empty',
      unresolvedConflictCount: 0
    };
  };
  const svModMergerWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Smart merge ordered Scarlet/Violet RomFS mods.',
    diagnostics: [],
    id: 'modMerger',
    label: 'S/V Mod Merger'
  };
  const createSvModMergerWorkflow = (
    modSources: SvModMergerSource[],
    outputRootPath: string | null
  ): SvModMergerWorkflow => ({
    diagnostics: [],
    outputRootPath,
    sources: modSources.map((source, index) => ({
      diagnostics: [],
      fileCount: source.isEnabled ? 1 : 0,
      isEnabled: source.isEnabled,
      kind: source.path.endsWith('.zip') || source.path.endsWith('.rar') ? 'archive' : 'folder',
      name: source.path.split(/[\\/]/).pop()?.replace(/\.(zip|rar)$/i, '') || `Source ${index + 1}`,
      overrideCount: index > 0 && source.isEnabled ? 1 : 0,
      path: source.path,
      sourceIndex: index,
      status: source.isEnabled ? 'ready' : 'disabled'
    })),
    stats: {
      enabledSourceCount: modSources.filter((source) => source.isEnabled).length,
      outputFileCount: modSources.some((source) => source.isEnabled) ? 1 : 0,
      overrideCount: Math.max(0, modSources.filter((source) => source.isEnabled).length - 1),
      sourceCount: modSources.length,
      sourceFileCount: modSources.filter((source) => source.isEnabled).length
    },
    summary: svModMergerWorkflowSummary
  });
  const createSvModMergerPreview = (
    modSources: SvModMergerSource[]
  ): SvModMergerPreview => {
    const enabledSources = modSources.filter((source) => source.isEnabled);
    const sourceName =
      enabledSources.at(-1)?.path.split(/[\\/]/).pop()?.replace(/\.(zip|rar)$/i, '') ?? '';

    return {
      canApply: enabledSources.length > 0,
      conflictFileCount: 0,
      diagnostics: [],
      files:
        enabledSources.length > 0
          ? [
              {
                mergeKind: enabledSources.length > 1 ? 'smartMerge' : 'singleSource',
                outputRelativePath: 'romfs/bin/mock/data.bin',
                overrideCount: Math.max(0, enabledSources.length - 1),
                relativePath: 'romfs/bin/mock/data.bin',
                sourceIndex: modSources.lastIndexOf(enabledSources.at(-1)!),
                sourceName,
                status: 'ready',
                summary: 'Smart merge preview fixture.',
                supportKind: 'Scarlet/Violet RomFS file'
              }
            ]
          : [],
      readyFileCount: enabledSources.length > 0 ? 1 : 0,
      selectedFileCount: enabledSources.length > 0 ? 1 : 0,
      status: enabledSources.length > 0 ? 'ready' : 'empty',
      unresolvedConflictCount: 0
    };
  };

  let currentGiftPokemonWorkflow = giftPokemonWorkflow;
  let currentTradePokemonWorkflow = tradePokemonWorkflow;
  const createDynamaxAdventurePlanWrites = (session: EditSession): ChangePlan['writes'] => {
    const requiresMainPatch = session.pendingEdits.some((edit) =>
      ['species', 'form', 'gigantamaxState'].includes(edit.field ?? '')
    );
    const writes: ChangePlan['writes'] = [
      {
        reason:
          'Apply pending Dynamax Adventures edit: Set Adventure 001 safe Pokemon fields.',
        replacesExistingOutput: false,
        sources: [
          {
            layer: 'base',
            relativePath:
              'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin'
          }
        ],
        targetRelativePath:
          'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin'
      }
    ];

    return requiresMainPatch
      ? [
          ...writes,
          {
            reason: 'Patch Dynamax Adventures ExeFS mirrors for edited Adventure identity data.',
            replacesExistingOutput: false,
            sources: [
              {
                layer: 'base',
                relativePath: 'exefs/main'
              }
            ],
            targetRelativePath: 'exefs/main'
          }
        ]
      : writes;
  };
  const ivFieldToKey = {
    ivAttack: 'attack',
    ivDefense: 'defense',
    ivHp: 'hp',
    ivSpecialAttack: 'specialAttack',
    ivSpecialDefense: 'specialDefense',
    ivSpeed: 'speed'
  } as const;
  const randomizerOptions = {
    ability1: true,
    ability2: true,
    allowSameType: false,
    compatibilityMachines: true,
    compatibilityRecords: true,
    compatibilityTutors: true,
    hiddenAbility: true,
    learnsetBanFixedDamageMoves: true,
    learnsetExpandTo25: false,
    learnsetRequireDamagingMove: true,
    learnsetStabFirst: true,
    randomizeGiftEncounters: false,
    randomizePokemonAbilities: false,
    randomizePokemonCatchRates: false,
    randomizePokemonCompatibility: false,
    randomizePokemonEvolutions: false,
    randomizePokemonHeldItems: false,
    randomizePokemonLearnsets: false,
    randomizePokemonStats: false,
    randomizePokemonTypes: false,
    randomizeRaidBonusRewards: false,
    randomizeRaidRewards: false,
    randomizeStaticEncounters: false,
    randomizeWildEncounters: false,
    shufflePokemonStats: true,
    statAttack: true,
    statDefense: true,
    statHp: true,
    statSpecialAttack: true,
    statSpecialDefense: true,
    statSpeed: true,
    typePrimary: true,
    typeSecondary: true
  };

  return {
    applyChangePlan: (request) =>
      Promise.resolve({
        applyResult: {
          applyId: 'apply-1',
          diagnostics: [
            {
              message: getApplyMessage(
                request.changePlan.writes[0]?.targetRelativePath ?? '',
                request.session.pendingEdits[0]?.domain
              ),
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
                : request.session.pendingEdits[0]?.domain === 'workflow.pokemon'
                  ? [
                      {
                        reason: 'Apply pending Pokemon edit: Set Bulbasaur hp to 99.',
                        replacesExistingOutput: false,
                        sources: [
                          {
                            layer: 'base',
                            relativePath: 'romfs/bin/pml/personal/personal_total.bin'
                          }
                        ],
                        targetRelativePath: 'romfs/bin/pml/personal/personal_total.bin'
                      }
                    ]
                : request.session.pendingEdits[0]?.domain === 'workflow.moves'
                  ? [
                      {
                        reason: 'Apply pending Moves edit: Set Tackle power to 80.',
                        replacesExistingOutput: false,
                        sources: [
                          {
                            layer: 'base',
                            relativePath: 'romfs/bin/pml/waza/waza_033.bin'
                          }
                        ],
                        targetRelativePath: 'romfs/bin/pml/waza/waza_033.bin'
                      }
                    ]
                : request.session.pendingEdits[0]?.domain === 'workflow.giftPokemon'
                  ? [
                      {
                        reason: 'Apply pending Gift Pokemon edit: Set Gift 001 HP IV to 31.',
                        replacesExistingOutput: false,
                        sources: [
                          {
                            layer: 'base',
                            relativePath: 'romfs/bin/script_event_data/add_poke.bin'
                          }
                        ],
                        targetRelativePath: 'romfs/bin/script_event_data/add_poke.bin'
                      }
                    ]
                : request.session.pendingEdits[0]?.domain === 'workflow.tradePokemon'
                  ? [
                      {
                        reason: 'Apply pending Trade Pokemon edit: Set Trade 001 HP IV to 31.',
                        replacesExistingOutput: false,
                        sources: [
                          {
                            layer: 'base',
                            relativePath: 'romfs/bin/script_event_data/field_trade.bin'
                          }
                        ],
                        targetRelativePath: 'romfs/bin/script_event_data/field_trade.bin'
                      }
                    ]
                : request.session.pendingEdits[0]?.domain === 'workflow.staticEncounters'
                  ? [
                      {
                        reason: 'Apply pending Static Encounter edit: Set Static 001 HP IV to 0.',
                        replacesExistingOutput: false,
                        sources: [
                          {
                            layer: 'base',
                            relativePath:
                              'romfs/bin/script_event_data/event_encount_data.bin'
                          }
                        ],
                        targetRelativePath:
                          'romfs/bin/script_event_data/event_encount_data.bin'
                      }
                    ]
                : request.session.pendingEdits[0]?.domain === 'workflow.rentalPokemon'
                  ? [
                      {
                        reason: 'Apply pending Rental Pokemon edit: Set Rental 001 HP IV to 0.',
                        replacesExistingOutput: false,
                        sources: [
                          {
                            layer: 'base',
                            relativePath: 'romfs/bin/script_event_data/rental.bin'
                          }
                        ],
                        targetRelativePath: 'romfs/bin/script_event_data/rental.bin'
                      }
                    ]
                : request.session.pendingEdits[0]?.domain === 'workflow.dynamaxAdventures'
                  ? createDynamaxAdventurePlanWrites(request.session)
                : request.session.pendingEdits[0]?.domain === 'workflow.shops'
                  ? [
                      {
                        reason: 'Apply pending Shops edit: Set Poke Mart inventory order to 2 items.',
                        replacesExistingOutput: false,
                        sources: [
                          {
                            layer: 'base',
                            relativePath: 'romfs/bin/appli/shop/bin/shop_data.bin'
                          }
                        ],
                        targetRelativePath: 'romfs/bin/appli/shop/bin/shop_data.bin'
                      }
                    ]
                  : request.session.pendingEdits[0]?.domain === 'workflow.encounters'
                    ? [
                        {
                          reason:
                            'Apply pending Encounters edit: Set Sword Symbol Zone 0x1122334455667788 Normal slot 2 probability to 40.',
                          replacesExistingOutput: false,
                          sources: [
                            {
                              layer: 'base',
                              relativePath:
                                'romfs/bin/archive/field/resident/data_table.gfpak'
                            }
                          ],
                          targetRelativePath:
                            'romfs/bin/archive/field/resident/data_table.gfpak'
                        }
                      ]
                    : request.session.pendingEdits[0]?.domain === 'workflow.raidBattles'
                      ? [
                          {
                            reason:
                              'Apply pending Raid Battles edit: Set Raid Battles 0xAABBCCDD00112233 slot 2 guaranteed perfect IVs to 6.',
                            replacesExistingOutput: false,
                            sources: [
                              {
                                layer: 'base',
                                relativePath:
                                  'romfs/bin/archive/field/resident/data_table.gfpak'
                              }
                            ],
                            targetRelativePath:
                              'romfs/bin/archive/field/resident/data_table.gfpak'
                          }
                        ]
                      : request.session.pendingEdits[0]?.domain === 'workflow.raidRewards'
                        ? [
                            {
                              reason:
                                'Apply pending Raid Rewards edit: Set Drop 0xAABBCCDD00112233 slot 1 5-star drop chance to 77.',
                              replacesExistingOutput: false,
                              sources: [
                                {
                                  layer: 'base',
                                  relativePath:
                                    'romfs/bin/archive/field/resident/data_table.gfpak'
                                }
                              ],
                              targetRelativePath:
                                'romfs/bin/archive/field/resident/data_table.gfpak'
                            }
                          ]
                        : request.session.pendingEdits[0]?.domain === 'workflow.bagHook'
                        ? [
                            {
                              reason: 'Install Bag Hook V2 with 20 disabled startup item grant slots.',
                              replacesExistingOutput: false,
                              sources: [
                                {
                                  layer: 'base',
                                  relativePath: 'romfs/bin/script/amx/main_event_0020.amx'
                                }
                              ],
                              targetRelativePath: 'romfs/bin/script/amx/main_event_0020.amx'
                            }
                          ]
                        : request.session.pendingEdits[0]?.domain === 'workflow.catchCap'
                        ? [
                            {
                              reason:
                                'Apply Catch Cap Editor display/runtime hook and badge cap values 0-7 to exefs/main; eight badges remains Lv.100.',
                              replacesExistingOutput: false,
                              sources: [
                                {
                                  layer: 'base',
                                  relativePath: 'exefs/main'
                                }
                              ],
                              targetRelativePath: 'exefs/main'
                            }
                          ]
                        : request.session.pendingEdits[0]?.domain === 'workflow.hyperTraining'
                        ? [
                            {
                              reason:
                                'Set the Battle Tower Hyper Training script minimum level.',
                              replacesExistingOutput: false,
                              sources: [
                                {
                                  layer: 'base',
                                  relativePath: 'romfs/bin/script/amx/hyper_training.amx'
                                }
                              ],
                              targetRelativePath: 'romfs/bin/script/amx/hyper_training.amx'
                            },
                            {
                              reason:
                                'Update the Hyper Training party/box picker cutoff checks in exefs/main.',
                              replacesExistingOutput: false,
                              sources: [
                                {
                                  layer: 'base',
                                  relativePath: 'exefs/main'
                                }
                              ],
                              targetRelativePath: 'exefs/main'
                            },
                            {
                              reason:
                                'Update English Hyper Training NPC dialogue to mention the selected level.',
                              replacesExistingOutput: false,
                              sources: [
                                {
                                  layer: 'base',
                                  relativePath:
                                    'romfs/bin/message/English/script/sub_event_007.dat'
                                }
                              ],
                              targetRelativePath:
                                'romfs/bin/message/English/script/sub_event_007.dat'
                            }
                          ]
                        : request.session.pendingEdits[0]?.domain === 'workflow.gymUniformRemoval'
                        ? [
                            {
                              reason:
                                'Install or refresh Gym Uniform Removal build-ID IPS patch in exefs.',
                              replacesExistingOutput: false,
                              sources: [
                                {
                                  layer: 'base',
                                  relativePath: 'exefs/main'
                                }
                              ],
                              targetRelativePath:
                                'exefs/A3B75BCD3311385AEED67FBEEB79CBB7BF02F471.ips'
                            }
                          ]
                        : request.session.pendingEdits[0]?.domain === 'workflow.ivScreen'
                        ? [
                            {
                              reason: 'Install or refresh IV Screen raw-IV hook in exefs/main.',
                              replacesExistingOutput: false,
                              sources: [
                                {
                                  layer: 'base',
                                  relativePath: 'exefs/main'
                                }
                              ],
                              targetRelativePath: 'exefs/main'
                            }
                          ]
                        : request.session.pendingEdits[0]?.domain === 'workflow.royalCandy'
                        ? [
                            {
                              reason:
                                'Apply Royal Candy workflow: Royal Candy item row patch.',
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
                        : request.session.pendingEdits[0]?.domain === 'workflow.exefsPatches'
                          ? [
                              {
                                reason:
                                  'Apply ExeFS patch: Royal Candy UI route and usage patch.',
                                replacesExistingOutput: false,
                                sources: [
                                  {
                                    layer: 'base',
                                    relativePath: 'exefs/main'
                                  }
                                ],
                              targetRelativePath: 'exefs/main'
                            }
                          ]
                        : request.session.pendingEdits[0]?.domain === 'workflow.startingItems'
                          ? [
                              {
                                reason: 'Update Bag Hook slots 2-20 with reviewed Starting Items grants.',
                                replacesExistingOutput: true,
                                sources: [
                                  {
                                    layer: 'layered',
                                    relativePath: 'romfs/bin/script/amx/main_event_0020.amx'
                                  }
                                ],
                                targetRelativePath: 'romfs/bin/script/amx/main_event_0020.amx'
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
          pokemonWorkflowSummary,
          movesWorkflowSummary,
          textWorkflowSummary,
          trainersWorkflowSummary,
          giftPokemonWorkflowSummary,
          tradePokemonWorkflowSummary,
          staticEncountersWorkflowSummary,
          rentalPokemonWorkflowSummary,
          dynamaxAdventuresWorkflowSummary,
          shopsWorkflowSummary,
          encountersWorkflowSummary,
          raidBattlesWorkflowSummary,
          raidRewardsWorkflowSummary,
          raidBonusRewardsWorkflowSummary,
          placementWorkflowSummary,
          flagworkSaveWorkflowSummary,
          bagHookWorkflowSummary,
          catchCapWorkflowSummary,
          hyperTrainingWorkflowSummary,
          typeChartWorkflowSummary,
          gymUniformRemovalWorkflowSummary,
          ivScreenWorkflowSummary,
          royalCandyWorkflowSummary,
          startingItemsWorkflowSummary,
          spreadsheetImportWorkflowSummary,
          modMergerWorkflowSummary
        ]
      }),
    loadEncountersWorkflow: () =>
      Promise.resolve({
        workflow: encountersWorkflow
      }),
    loadFlagworkSaveWorkflow: () =>
      Promise.resolve({
        workflow: flagworkSaveWorkflow
      }),
    loadBagHookWorkflow: () =>
      Promise.resolve({
        workflow: bagHookWorkflow
      }),
    stageBagHookInstall: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'Bag Hook V2 install is staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.bagHook',
              field: 'install',
              newValue: 'v2-empty',
              recordId: 'bag-hook-v2',
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/script/amx/main_event_0020.amx'
                }
              ],
              summary: 'Stage Bag Hook install: 20 disabled startup item grant slots.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-bag-hook'
        },
        workflow: bagHookWorkflow
      }),
    stageBagHookUninstall: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'Bag Hook V2 uninstall is staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.bagHook',
              field: 'uninstall',
              newValue: 'remove-bag-hook-and-dependents',
              recordId: 'bag-hook-v2-uninstall',
              sources: [
                {
                  layer: 'layered',
                  relativePath: 'romfs/bin/script/amx/main_event_0020.amx'
                }
              ],
              summary:
                'Stage Bag Hook uninstall: remove Bag Hook plus dependent Royal Candy and Starting Items outputs.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-bag-hook-uninstall'
        },
        workflow: {
          ...bagHookWorkflow,
          installMessage: 'Bag Hook V2 is installed.',
          installStatus: 'installed'
        }
      }),
    loadCatchCapWorkflow: () =>
      Promise.resolve({
        workflow: catchCapWorkflow
      }),
    stageCatchCap: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'Catch Cap Editor values are staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.catchCap',
              field: 'caps',
              newValue: request.caps
                .map((cap) => `${cap.badgeCount}=${cap.levelCap}`)
                .join(';'),
              recordId: 'catch-cap-v1',
              sources: [
                {
                  layer: 'base',
                  relativePath: 'exefs/main'
                }
              ],
              summary:
                'Stage Catch Cap Editor values for badge counts 0-7 and the display/runtime hook; eight badges remains Lv.100.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-catch-cap'
        },
        workflow: catchCapWorkflow
      }),
    stageCatchCapUninstall: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'Catch Cap Editor uninstall is staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.catchCap',
              field: 'uninstall',
              newValue: 'true',
              recordId: 'catch-cap-v1-uninstall',
              sources: [
                {
                  layer: 'layered',
                  relativePath: 'exefs/main'
                },
                {
                  layer: 'base',
                  relativePath: 'exefs/main'
                }
              ],
              summary: 'Stage Catch Cap Editor uninstall.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-catch-cap-uninstall'
        },
        workflow: {
          ...catchCapWorkflow,
          installMessage: 'Catch Cap Editor hook is installed for display and runtime capture checks.',
          installStatus: 'installed',
          provenance: {
            fileState: 'layeredOverride',
            sourceFile: 'exefs/main',
            sourceLayer: 'layered'
          }
        }
      }),
    loadHyperTrainingWorkflow: () =>
      Promise.resolve({
        workflow: hyperTrainingWorkflow
      }),
    stageHyperTraining: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: `Hyper Training minimum level Lv.${request.minimumLevel} is staged for change-plan review.`,
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.hyperTraining',
              field: 'minimumLevel',
              newValue: request.minimumLevel.toString(),
              recordId: 'hyper-training-minimum-level',
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/script/amx/hyper_training.amx'
                },
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/message/English/script/sub_event_007.dat'
                },
                {
                  layer: 'base',
                  relativePath: 'exefs/main'
                }
              ],
              summary: `Stage Hyper Training minimum level Lv.${request.minimumLevel}.`
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-hyper-training'
        },
        workflow: {
          ...hyperTrainingWorkflow,
          installMessage: `Hyper Training currently accepts Pokemon at Lv.${request.minimumLevel} or higher.`,
          installStatus:
            request.minimumLevel === 100
              ? hyperTrainingWorkflow.installStatus
              : 'installed',
          levelRule: {
            ...hyperTrainingWorkflow.levelRule,
            minimumLevel: request.minimumLevel
          }
        }
      }),
    loadTypeChartWorkflow: () =>
      Promise.resolve({
        workflow: typeChartWorkflow
      }),
    stageTypeChart: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'Type Chart effectiveness values are staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.typeChart',
              field: 'effectiveness',
              newValue: request.values
                .map((value) => value.toString(16).padStart(2, '0'))
                .join('')
                .toUpperCase(),
              recordId: 'type-chart',
              sources: [
                {
                  layer: 'base',
                  relativePath: 'exefs/main'
                }
              ],
              summary: 'Stage Type Chart effectiveness values.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-type-chart'
        },
        workflow: {
          ...typeChartWorkflow,
          cells: typeChartWorkflow.cells.map((cell, index) => ({
            ...cell,
            effectiveness: request.values[index] ?? cell.effectiveness
          }))
        }
      }),
    loadGymUniformRemovalWorkflow: () =>
      Promise.resolve({
        workflow: gymUniformRemovalWorkflow
      }),
    stageGymUniformRemovalInstall: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'Gym Uniform Removal install is staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.gymUniformRemoval',
              field: 'install',
              newValue: 'true',
              recordId: 'gym-uniform-removal-v1-install',
              sources: [
                {
                  layer: 'generated',
                  relativePath: 'exefs/A3B75BCD3311385AEED67FBEEB79CBB7BF02F471.ips'
                },
                {
                  layer: 'base',
                  relativePath: 'exefs/main'
                }
              ],
              summary: 'Stage Gym Uniform Removal install.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-gym-uniform-install'
        },
        workflow: gymUniformRemovalWorkflow
      }),
    stageGymUniformRemovalUninstall: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'Gym Uniform Removal uninstall is staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.gymUniformRemoval',
              field: 'uninstall',
              newValue: 'true',
              recordId: 'gym-uniform-removal-v1-uninstall',
              sources: [
                {
                  layer: 'generated',
                  relativePath: 'exefs/A3B75BCD3311385AEED67FBEEB79CBB7BF02F471.ips'
                },
                {
                  layer: 'base',
                  relativePath: 'exefs/main'
                }
              ],
              summary: 'Stage Gym Uniform Removal uninstall.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-gym-uniform-uninstall'
        },
        workflow: {
          ...gymUniformRemovalWorkflow,
          installMessage: 'Gym Uniform Removal is installed.',
          installStatus: 'installed',
          provenance: {
            fileState: 'layeredOverride',
            sourceFile: 'exefs/main',
            sourceLayer: 'layered'
          }
        }
      }),
    loadIvScreenWorkflow: () =>
      Promise.resolve({
        workflow: ivScreenWorkflow
      }),
    stageIvScreenInstall: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'IV Screen install is staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.ivScreen',
              field: 'install',
              newValue: 'true',
              recordId: 'iv-screen-v1-install',
              sources: [
                {
                  layer: 'layered',
                  relativePath: 'exefs/main'
                },
                {
                  layer: 'base',
                  relativePath: 'exefs/main'
                }
              ],
              summary: 'Stage IV Screen install.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-iv-screen-install'
        },
        workflow: ivScreenWorkflow
      }),
    stageIvScreenUninstall: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'IV Screen uninstall is staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.ivScreen',
              field: 'uninstall',
              newValue: 'true',
              recordId: 'iv-screen-v1-uninstall',
              sources: [
                {
                  layer: 'layered',
                  relativePath: 'exefs/main'
                },
                {
                  layer: 'base',
                  relativePath: 'exefs/main'
                }
              ],
              summary: 'Stage IV Screen uninstall.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-iv-screen-uninstall'
        },
        workflow: {
          ...ivScreenWorkflow,
          installMessage: 'IV Screen is installed.',
          installStatus: 'installed',
          provenance: {
            fileState: 'layeredOverride',
            sourceFile: 'exefs/main',
            sourceLayer: 'layered'
          }
        }
      }),
    loadExeFsPatchWorkflow: () =>
      Promise.resolve({
        workflow: exeFsPatchWorkflow
      }),
    stageExeFsPatch: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'ExeFS patch is staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.exefsPatches',
              field: 'patchId',
              newValue: 'exefs/main',
              recordId: request.patchId,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'exefs/main'
                }
              ],
              summary: 'Stage ExeFS patch: ExeFS main compatibility.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-exefs'
        },
        workflow: exeFsPatchWorkflow
      }),
    loadRoyalCandyWorkflow: () =>
      Promise.resolve({
        workflow: {
          checks: [
            {
              area: 'RomFS',
              checkId: 'royal-candy-preflight:item-data',
              message: 'Item data found.',
              provenance: {
                fileState: 'baseOnly',
                sourceFile: 'romfs/bin/pml/item/item.dat',
                sourceLayer: 'base'
              },
              status: 'Pass',
              target: 'romfs/bin/pml/item/item.dat',
              workflowId: 'royal-candy-preflight'
            },
            {
              area: 'ExeFS',
              checkId: 'royal-candy-preflight:exefs:exefs-main-compatibility:patch-code-cave',
              message: 'Patch code cave: expected 0xC bytes, actual text+0x7BC338.',
              provenance: {
                fileState: 'baseOnly',
                sourceFile: 'exefs/main',
                sourceLayer: 'base'
              },
              status: 'Pass',
              target: '.text text+0x7BC338',
              workflowId: 'royal-candy-preflight'
            }
          ],
          diagnostics: [],
          outputs: [
            {
              description: 'Royal Candy item row patch.',
              outputId: 'royal-candy-unlimited:romfs/bin/pml/item/item.dat',
              outputKind: 'RomFS data',
              provenance: {
                fileState: 'baseOnly',
                sourceFile: 'romfs/bin/pml/item/item.dat',
                sourceLayer: 'base'
              },
              relativePath: 'romfs/bin/pml/item/item.dat',
              sourceFile: 'romfs/bin/pml/item/item.dat',
              status: canEdit ? 'ready' : 'readOnly',
              workflowId: 'royal-candy-unlimited'
            },
            {
              description: 'Royal Candy ExeFS UI and usage patch.',
              outputId: 'royal-candy-unlimited:exefs/main',
              outputKind: 'ExeFS NSO',
              provenance: {
                fileState: 'baseOnly',
                sourceFile: 'exefs/main',
                sourceLayer: 'base'
              },
              relativePath: 'exefs/main',
              sourceFile: 'exefs/main',
              status: canEdit ? 'ready' : 'readOnly',
              workflowId: 'royal-candy-unlimited'
            }
          ],
          stats: {
            failCount: 0,
            outputCount: 2,
            passCount: 2,
            sourceFileCount: 2,
            totalCheckCount: 2,
            totalStepCount: 4,
            totalWorkflowCount: 2,
            warningCount: 0
          },
          summary: royalCandyWorkflowSummary,
          workflows: [
            {
              category: 'Build',
              description: 'Prepares Royal Candy item 1128 from Rare Candy item 50.',
              itemId: 1128,
              levelCaps: [],
              mode: 'unlimited',
              name: 'Unlimited Royal Candy',
              provenance: {
                fileState: 'baseOnly',
                sourceFile: 'romfs/bin/pml/item/item.dat',
                sourceLayer: 'base'
              },
              status: canEdit ? 'available' : 'readOnly',
              steps: [
                {
                  description: 'Resolve required RomFS files and ExeFS inputs.',
                  label: 'Validate sources',
                  step: 1
                },
                {
                  description: 'Review generated output targets before apply.',
                  label: 'Review LayeredFS output',
                  step: 2
                }
              ],
              target: 'RomFS + ExeFS LayeredFS',
              templateItemId: 50,
              workflowId: 'royal-candy-unlimited'
            },
            {
              category: 'Build',
              description: 'Prepares Royal Candy item 1128 with story-cap checks.',
              itemId: 1128,
              levelCaps: royalCandyLevelCaps,
              mode: 'storyLimits',
              name: 'Royal Candy with Story Limits',
              provenance: {
                fileState: 'baseOnly',
                sourceFile: 'romfs/bin/pml/item/item.dat',
                sourceLayer: 'base'
              },
              status: canEdit ? 'available' : 'readOnly',
              steps: [
                {
                  description: 'Use story-cap flag milestones before enabling higher levels.',
                  label: 'Apply story limits',
                  step: 1
                },
                {
                  description: 'Review generated output targets before apply.',
                  label: 'Review LayeredFS output',
                  step: 2
                }
              ],
              target: 'RomFS + ExeFS LayeredFS',
              templateItemId: 50,
              workflowId: 'royal-candy-story-limits'
            }
          ]
        }
      }),
    stageRoyalCandyWorkflow: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'Royal Candy workflow is staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.royalCandy',
              field: 'workflowId',
              newValue:
                request.workflowId === 'royal-candy-story-limits'
                  ? `storyLimits|${(request.levelCaps ?? [])
                      .map((levelCap) => `${levelCap.slot}=${levelCap.levelCap}`)
                      .join(';')}`
                  : 'unlimited',
              recordId: request.workflowId,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/pml/item/item.dat'
                }
              ],
              summary:
                request.workflowId === 'royal-candy-story-limits'
                  ? 'Stage Royal Candy workflow: Royal Candy with Story Limits.'
                  : 'Stage Royal Candy workflow: Unlimited Royal Candy.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-royal-candy'
        },
        workflow: {
          checks: [
            {
              area: 'RomFS',
              checkId: 'royal-candy-preflight:item-data',
              message: 'Item data found.',
              provenance: {
                fileState: 'baseOnly',
                sourceFile: 'romfs/bin/pml/item/item.dat',
                sourceLayer: 'base'
              },
              status: 'Pass',
              target: 'romfs/bin/pml/item/item.dat',
              workflowId: 'royal-candy-preflight'
            }
          ],
          diagnostics: [],
          outputs: [
            {
              description: 'Royal Candy item row patch.',
              outputId: 'royal-candy-unlimited:romfs/bin/pml/item/item.dat',
              outputKind: 'RomFS data',
              provenance: {
                fileState: 'baseOnly',
                sourceFile: 'romfs/bin/pml/item/item.dat',
                sourceLayer: 'base'
              },
              relativePath: 'romfs/bin/pml/item/item.dat',
              sourceFile: 'romfs/bin/pml/item/item.dat',
              status: 'ready',
              workflowId: 'royal-candy-unlimited'
            }
          ],
          stats: {
            failCount: 0,
            outputCount: 1,
            passCount: 1,
            sourceFileCount: 1,
            totalCheckCount: 1,
            totalStepCount: 4,
            totalWorkflowCount: 2,
            warningCount: 0
          },
          summary: royalCandyWorkflowSummary,
          workflows: [
            {
              category: 'Build',
              description: 'Prepares Royal Candy item 1128 from Rare Candy item 50.',
              itemId: 1128,
              levelCaps: [],
              mode: 'unlimited',
              name: 'Unlimited Royal Candy',
              provenance: {
                fileState: 'baseOnly',
                sourceFile: 'romfs/bin/pml/item/item.dat',
                sourceLayer: 'base'
              },
              status: 'available',
              steps: [
                {
                  description: 'Resolve required RomFS files and ExeFS inputs.',
                  label: 'Validate sources',
                  step: 1
                },
                {
                  description: 'Review generated output targets before apply.',
                  label: 'Review LayeredFS output',
                  step: 2
                }
              ],
              target: 'RomFS + ExeFS LayeredFS',
              templateItemId: 50,
              workflowId: 'royal-candy-unlimited'
            },
            {
              category: 'Build',
              description: 'Prepares Royal Candy item 1128 with story-cap checks.',
              itemId: 1128,
              levelCaps: royalCandyLevelCaps,
              mode: 'storyLimits',
              name: 'Royal Candy with Story Limits',
              provenance: {
                fileState: 'baseOnly',
                sourceFile: 'romfs/bin/pml/item/item.dat',
                sourceLayer: 'base'
              },
              status: 'available',
              steps: [
                {
                  description: 'Use story-cap flag milestones before enabling higher levels.',
                  label: 'Apply story limits',
                  step: 1
                },
                {
                  description: 'Review generated output targets before apply.',
                  label: 'Review LayeredFS output',
                  step: 2
                }
              ],
              target: 'RomFS + ExeFS LayeredFS',
              templateItemId: 50,
              workflowId: 'royal-candy-story-limits'
            }
          ]
        }
      }),
    loadStartingItemsWorkflow: () =>
      Promise.resolve({
        workflow: startingItemsWorkflow
      }),
    stageStartingItems: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'Starting Items grants are staged for change-plan review.',
            severity: 'info'
          }
        ],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.startingItems',
              field: 'grants',
              newValue: request.grants
                .filter((grant) => grant.itemId !== null)
                .map((grant) => `${grant.slot}:${grant.itemId}:${grant.quantity}`)
                .join(';'),
              recordId: 'starting-items',
              sources: [
                {
                  layer: 'layered',
                  relativePath: 'romfs/bin/script/amx/main_event_0020.amx'
                }
              ],
              summary: 'Stage Starting Items grants in Bag Hook slots 2-20.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-starting-items'
        },
        workflow: startingItemsWorkflow
      }),
    loadSpreadsheetImportWorkflow: () =>
      Promise.resolve({
        workflow: spreadsheetImportWorkflow
      }),
    loadModMergerWorkflow: (request) =>
      Promise.resolve({
        workflow: {
          ...modMergerWorkflow,
          modDirectory1: request.modDirectory1,
          modDirectory2: request.modDirectory2
        }
      }),
    stageModMerge: (request) => {
      const preview = createModMergerPreview(
        request.selectedDirectory1Files,
        request.selectedDirectory2Files
      );

      return Promise.resolve({
        diagnostics: preview.diagnostics,
        preview,
        workflow: {
          ...modMergerWorkflow,
          modDirectory1: request.modDirectory1,
          modDirectory2: request.modDirectory2
        }
      });
    },
    applyModMerge: (request) => {
      const preview = createModMergerPreview(
        request.selectedDirectory1Files,
        request.selectedDirectory2Files
      );

      return Promise.resolve({
        diagnostics: preview.diagnostics,
        preview,
        workflow: {
          ...modMergerWorkflow,
          modDirectory1: request.modDirectory1,
          modDirectory2: request.modDirectory2
        },
        writtenFiles: preview.canApply ? preview.files.map((file) => file.relativePath) : []
      });
    },
    loadSvModMergerWorkflow: (request) =>
      Promise.resolve({
        workflow: createSvModMergerWorkflow(request.modSources, request.paths.outputRootPath)
      }),
    stageSvModMerge: (request) => {
      const preview = createSvModMergerPreview(request.modSources);

      return Promise.resolve({
        diagnostics: preview.diagnostics,
        preview,
        workflow: createSvModMergerWorkflow(request.modSources, request.paths.outputRootPath)
      });
    },
    applySvModMerge: (request) => {
      const preview = createSvModMergerPreview(request.modSources);

      return Promise.resolve({
        diagnostics: preview.diagnostics,
        preview,
        workflow: createSvModMergerWorkflow(request.modSources, request.paths.outputRootPath),
        writtenFiles: preview.canApply ? preview.files.map((file) => file.relativePath) : []
      });
    },
    importRandomizerSeed: (request) =>
      Promise.resolve({
        config: {
          options: {
            ...randomizerOptions,
            randomizePokemonStats: true
          },
          outputHash: 'mock-output',
          rollSeed: 'mock-roll',
          userSeed: 'mock-seed'
        },
        diagnostics: [],
        seed: request.seed
      }),
    applyRandomizer: (request) =>
      Promise.resolve({
        applyResult: {
          applyId: 'randomizer-apply-1',
          diagnostics: [
            {
              message: 'Randomizer applied selected output.',
              severity: 'info'
            }
          ],
          writtenFiles: request.config.options.randomizePokemonStats
            ? ['romfs/bin/pml/personal/personal_total.bin']
            : []
        },
        seed: `KM1-MOCK-${request.config.userSeed || 'generated'}`
      }),
    restoreRandomizer: () =>
      Promise.resolve({
        applyResult: {
          applyId: 'randomizer-restore-1',
          diagnostics: [
            {
              message: 'Restore Vanilla Values removed tracked Randomizer output.',
              severity: 'info'
            }
          ],
          writtenFiles: ['romfs/bin/pml/personal/personal_total.bin']
        }
      }),
    previewSpreadsheetImport: (request) =>
      Promise.resolve({
        diagnostics: [
          {
            message: 'Spreadsheet import preview accepted 1 row and rejected 0.',
            severity: 'info'
          }
        ],
        preview: {
          acceptedRowCount: 1,
          profileId: request.profileId,
          rejectedRowCount: 0,
          rows: [
            {
              cells: [
                {
                  field: 'itemId',
                  header: 'ItemId',
                  message: 'Potion',
                  status: 'accepted',
                  value: '1'
                },
                {
                  field: 'buyPrice',
                  header: 'BuyPrice',
                  message: 'Pending edit.',
                  status: 'accepted',
                  value: '450'
                }
              ],
              diagnostics: [],
              recordId: '1',
              rowNumber: 2,
              status: 'accepted',
              summary: 'Potion: Buy price -> 450.'
            }
          ],
          skippedRowCount: 0,
          sourcePath: request.sourcePath,
          totalRowCount: 1
        },
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.items',
              field: 'buyPrice',
              newValue: '450',
              recordId: '1',
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/pml/item/item.dat'
                }
              ],
              summary: 'Set Potion buy price to 450.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'session-import'
        },
        workflow: spreadsheetImportWorkflow
      }),
    loadPlacementWorkflow: () =>
      Promise.resolve({
        workflow: placementWorkflow
      }),
    loadBehaviorWorkflow: () =>
      Promise.resolve({
        workflow: behaviorWorkflow
      }),
    loadRaidBattlesWorkflow: () =>
      Promise.resolve({
        workflow: raidBattlesWorkflow
      }),
    loadRaidRewardsWorkflow: () =>
      Promise.resolve({
        workflow: raidRewardsWorkflow
      }),
    loadRaidBonusRewardsWorkflow: () =>
      Promise.resolve({
        workflow: raidBonusRewardsWorkflow
      }),
    loadItemsWorkflow: () =>
      Promise.resolve({
        workflow: itemsWorkflow
      }),
    loadPokemonWorkflow: () =>
      Promise.resolve({
        workflow: pokemonWorkflow
      }),
    loadMovesWorkflow: () =>
      Promise.resolve({
        workflow: movesWorkflow
      }),
    loadTextWorkflow: () =>
      Promise.resolve({
        workflow: textWorkflow
      }),
    loadTrainersWorkflow: () =>
      Promise.resolve({
        workflow: trainersWorkflow
      }),
    loadGiftPokemonWorkflow: () =>
      Promise.resolve({
        workflow: currentGiftPokemonWorkflow
      }),
    loadTradePokemonWorkflow: () =>
      Promise.resolve({
        workflow: currentTradePokemonWorkflow
      }),
    loadStaticEncountersWorkflow: () =>
      Promise.resolve({
        workflow: staticEncountersWorkflow
      }),
    loadRentalPokemonWorkflow: () =>
      Promise.resolve({
        workflow: rentalPokemonWorkflow
      }),
    loadDynamaxAdventuresWorkflow: () =>
      Promise.resolve({
        workflow: dynamaxAdventuresWorkflow
      }),
    loadShopsWorkflow: () =>
      Promise.resolve({
        workflow: shopsWorkflow
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
      const fieldLabels: Record<string, string> = {
        buyPrice: 'buy price',
        canUseOnPokemon: 'can use on Pokemon',
        evAttack: 'Attack EV gain',
        healAmount: 'heal amount',
        pouch: 'pouch',
        sellPrice: 'sell price',
        wattsPrice: 'Watts price',
        alternatePrice: 'alternate price'
      };
      const fieldLabel = fieldLabels[request.field] ?? request.field;

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
              case 'pouch': {
                const metadata = { ...item.metadata, pouch: value };
                return {
                  ...item,
                  category: value === 4 ? 'Items' : item.category,
                  detailGroups: createItemDetailGroups(metadata),
                  metadata
                };
              }
              case 'healAmount': {
                const metadata = { ...item.metadata, healAmount: value };
                return {
                  ...item,
                  detailGroups: createItemDetailGroups(metadata),
                  metadata
                };
              }
              case 'evAttack':
                return { ...item, metadata: { ...item.metadata, evAttack: value } };
              case 'canUseOnPokemon':
                return {
                  ...item,
                  metadata: { ...item.metadata, canUseOnPokemon: value !== 0 }
                };
              default:
                return { ...item, buyPrice: value, sellPrice: Math.floor(value / 2) };
            }
          })
        }
      });
    },
    updatePokemonField: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.pokemon',
              field: request.field,
              newValue: request.value,
              recordId: request.personalId.toString(),
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/pml/personal/personal_total.bin'
                }
              ],
              summary:
                request.field.startsWith('compatibility:')
                  ? `${request.value === '1' ? 'Enable' : 'Disable'} Bulbasaur ${
                      getMockPokemonCompatibilityLabel(
                        pokemonWorkflow,
                        request.personalId,
                        request.field
                      ) ?? request.field
                    } compatibility.`
                  : request.field === 'canNotDynamax'
                  ? `Set Bulbasaur cannot dynamax to ${
                      request.value === '1' ? 'enabled' : 'disabled'
                    }.`
                  : `Set Bulbasaur ${request.field} to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...pokemonWorkflow,
          pokemon: pokemonWorkflow.pokemon.map((pokemon) => {
            if (pokemon.personalId !== request.personalId) {
              return pokemon;
            }

            const value = Number.parseInt(request.value, 10);
            if (request.field === 'hp') {
              const baseStats = {
                ...pokemon.baseStats,
                hp: value
              };

              return {
                ...pokemon,
                baseStats: {
                  ...baseStats,
                  total:
                    baseStats.hp +
                    baseStats.attack +
                    baseStats.defense +
                    baseStats.specialAttack +
                    baseStats.specialDefense +
                    baseStats.speed
                }
              };
            }

            if (request.field === 'canNotDynamax') {
              return {
                ...pokemon,
                personal: {
                  ...pokemon.personal,
                  canNotDynamax: value !== 0
                }
              };
            }

            if (request.field.startsWith('compatibility:')) {
              const [, groupId, slotText] = request.field.split(':');
              const slot = Number.parseInt(slotText ?? '', 10);
              const compatibility = pokemon.compatibility.map((group) => {
                if (group.groupId !== groupId) {
                  return group;
                }

                const entries = group.entries.map((entry) =>
                  entry.slot === slot ? { ...entry, canLearn: value !== 0 } : entry
                );

                return {
                  ...group,
                  enabledCount: entries.filter((entry) => entry.canLearn).length,
                  entries
                };
              });

              return {
                ...pokemon,
                compatibility
              };
            }

            return pokemon;
          })
        }
      }),
    updatePokemonLearnset: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.pokemon',
              field: `learnset:${request.action === 'add' ? 'upsert' : request.action}:${
                request.action === 'add'
                  ? pokemonWorkflow.pokemon.find((pokemon) => pokemon.personalId === request.personalId)
                      ?.learnset.length ?? 0
                  : request.slot ?? 0
              }`,
              newValue:
                request.moveId !== null && request.level !== null
                  ? `${request.moveId}:${request.level}`
                  : request.action === 'moveTo' && request.moveId !== null
                  ? request.moveId.toString()
                  : '1',
              recordId: request.personalId.toString(),
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/pml/waza_oboe/wazaoboe_total.bin'
                }
              ],
              summary:
                request.action === 'remove'
                  ? `Remove Bulbasaur learnset slot ${request.slot}.`
                  : request.action === 'moveUp'
                  ? `Move Bulbasaur learnset slot ${request.slot} up.`
                  : request.action === 'moveDown'
                  ? `Move Bulbasaur learnset slot ${request.slot} down.`
                  : request.action === 'moveTo'
                  ? `Move Bulbasaur learnset slot ${request.slot} to slot ${request.moveId}.`
                  : `Set Bulbasaur learnset slot ${request.slot ?? 0} to Lv. ${
                      request.level
                    } ${request.moveId === 345 ? 'Magical Leaf' : `Move ${request.moveId}`}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...pokemonWorkflow,
          pokemon: pokemonWorkflow.pokemon.map((pokemon) => {
            if (pokemon.personalId !== request.personalId) {
              return pokemon;
            }

            const learnset = [...pokemon.learnset];
            const keepLevelsWithSlots =
              request.action === 'moveUp' ||
              request.action === 'moveDown' ||
              request.action === 'moveTo';
            const slotLevels = keepLevelsWithSlots
              ? pokemon.learnset.map((move) => move.level)
              : [];
            const targetSlot = request.action === 'add' ? learnset.length : request.slot ?? 0;
            if (
              (request.action === 'upsert' || request.action === 'add') &&
              request.moveId !== null &&
              request.level !== null
            ) {
              const row = {
                level: request.level,
                moveId: request.moveId,
                moveName: request.moveId === 345 ? 'Magical Leaf' : `Move ${request.moveId}`,
                slot: targetSlot
              };
              if (targetSlot < learnset.length) {
                learnset[targetSlot] = row;
              } else {
                learnset.push(row);
              }
            } else if (request.action === 'remove' && targetSlot < learnset.length) {
              learnset.splice(targetSlot, 1);
            } else if (request.action === 'moveUp' && targetSlot > 0) {
              [learnset[targetSlot - 1], learnset[targetSlot]] = [
                learnset[targetSlot]!,
                learnset[targetSlot - 1]!
              ];
            } else if (request.action === 'moveDown' && targetSlot < learnset.length - 1) {
              [learnset[targetSlot + 1], learnset[targetSlot]] = [
                learnset[targetSlot]!,
                learnset[targetSlot + 1]!
              ];
            } else if (
              request.action === 'moveTo' &&
              targetSlot < learnset.length &&
              request.moveId !== null &&
              request.moveId < learnset.length
            ) {
              const [moved] = learnset.splice(targetSlot, 1);
              if (moved) {
                learnset.splice(request.moveId, 0, moved);
              }
            }

            return {
              ...pokemon,
              learnset: learnset.map((move, slot) => ({
                ...move,
                level: keepLevelsWithSlots ? slotLevels[slot] ?? move.level : move.level,
                slot
              }))
            };
          })
        }
      }),
    updatePokemonEvolution: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.pokemon',
              field: `evolution:${request.action === 'add' ? 'upsert' : request.action}:${
                request.action === 'add'
                  ? pokemonWorkflow.pokemon.find((pokemon) => pokemon.personalId === request.personalId)
                      ?.evolutions.length ?? 0
                  : request.slot ?? 0
              }`,
              newValue:
                request.method !== null &&
                request.argument !== null &&
                request.species !== null &&
                request.form !== null &&
                request.level !== null
                  ? `${request.method}:${request.argument}:${request.species}:${request.form}:${request.level}`
                  : '1',
              recordId: request.personalId.toString(),
              sources: [
                {
                  layer: 'base',
                  relativePath: `romfs/bin/pml/evolution/evo_${request.personalId
                    .toString()
                    .padStart(3, '0')}.bin`
                }
              ],
              summary:
                request.action === 'remove'
                  ? `Remove Bulbasaur evolution slot ${request.slot}.`
                  : request.action === 'moveUp'
                  ? `Move Bulbasaur evolution slot ${request.slot} up.`
                  : request.action === 'moveDown'
                  ? `Move Bulbasaur evolution slot ${request.slot} down.`
                  : `Set Bulbasaur evolution slot ${request.slot ?? 0} to species ${
                      request.species
                    } at level ${request.level}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...pokemonWorkflow,
          pokemon: pokemonWorkflow.pokemon.map((pokemon) => {
            if (pokemon.personalId !== request.personalId) {
              return pokemon;
            }

            const evolutions = [...pokemon.evolutions];
            const targetSlot = request.action === 'add' ? evolutions.length : request.slot ?? 0;
            if (
              (request.action === 'upsert' || request.action === 'add') &&
              request.method !== null &&
              request.argument !== null &&
              request.species !== null &&
              request.form !== null &&
              request.level !== null
            ) {
              const methodOption =
                pokemonWorkflow.evolutionMethodOptions.find(
                  (option) => option.value === request.method
                ) ?? null;
              const methodPrefix = request.method.toString().padStart(3, '0');
              const methodName =
                methodOption?.label.startsWith(`${methodPrefix} `)
                  ? methodOption.label.slice(methodPrefix.length + 1)
                  : methodOption?.label ?? `Method ${request.method}`;
              const argumentKind = methodOption?.argumentKind ?? 'value';
              const argumentValue =
                argumentKind === 'none' || argumentKind === 'level'
                  ? 'None'
                  : methodOption?.argumentOptions.find((option) => option.value === request.argument)
                      ?.label ?? request.argument.toString();
              const row = {
                argument: request.argument,
                argumentKind,
                argumentLabel: methodOption?.argumentLabel ?? 'Argument',
                argumentValue,
                form: request.form,
                level: request.level,
                method: request.method,
                methodName,
                slot: targetSlot,
                species: request.species
              };
              if (targetSlot < evolutions.length) {
                evolutions[targetSlot] = row;
              } else {
                evolutions.push(row);
              }
            } else if (request.action === 'remove' && targetSlot < evolutions.length) {
              evolutions.splice(targetSlot, 1);
            } else if (request.action === 'moveUp' && targetSlot > 0) {
              [evolutions[targetSlot - 1], evolutions[targetSlot]] = [
                evolutions[targetSlot]!,
                evolutions[targetSlot - 1]!
              ];
            } else if (request.action === 'moveDown' && targetSlot < evolutions.length - 1) {
              [evolutions[targetSlot + 1], evolutions[targetSlot]] = [
                evolutions[targetSlot]!,
                evolutions[targetSlot + 1]!
              ];
            }

            return {
              ...pokemon,
              evolutions: evolutions.map((evolution, slot) => ({ ...evolution, slot }))
            };
          })
        }
      }),
    updateMoveField: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.moves',
              field: request.field,
              newValue: request.value,
              recordId: request.moveId.toString(),
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/pml/waza/waza_033.bin'
                }
              ],
              summary:
                request.field === 'makesContact'
                  ? `Set Tackle makes contact to ${request.value === '1' ? 'enabled' : 'disabled'}.`
                  : `Set Tackle ${request.field} to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...movesWorkflow,
          moves: movesWorkflow.moves.map((move) => {
            if (move.moveId !== request.moveId) {
              return move;
            }

            const value = Number.parseInt(request.value, 10);
            if (request.field === 'makesContact') {
              return {
                ...move,
                flags: move.flags.map((flag) =>
                  flag.field === 'makesContact' ? { ...flag, enabled: value !== 0 } : flag
                )
              };
            }

            if (request.field === 'power') {
              return { ...move, power: value };
            }

            return move;
          })
        }
      }),
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
    updateGiftPokemonField: (request) => {
      const value = Number.parseInt(request.value, 10);
      const ivKey = ivFieldToKey[request.field as keyof typeof ivFieldToKey] ?? null;
      const pendingEdit = {
        domain: 'workflow.giftPokemon',
        field: request.field,
        newValue: request.value,
        recordId: `gift:${request.giftIndex}`,
        sources: [
          {
            layer: 'base' as const,
            relativePath: 'romfs/bin/script_event_data/add_poke.bin'
          }
        ],
        summary: `Set Gift 001 ${request.field} to ${request.value}.`
      };
      const pendingEdits = [...(request.session?.pendingEdits ?? []), pendingEdit];
      currentGiftPokemonWorkflow = {
        ...currentGiftPokemonWorkflow,
        gifts: currentGiftPokemonWorkflow.gifts.map((gift) =>
          gift.giftIndex === request.giftIndex
            ? {
                ...gift,
                ivs: ivKey
                  ? {
                      ...gift.ivs,
                      [ivKey]: value
                    }
                  : gift.ivs,
                ivSummary: ivKey
                  ? `HP ${ivKey === 'hp' ? value : gift.ivs.hp} / Atk ${
                      ivKey === 'attack' ? value : gift.ivs.attack
                    } / Def ${ivKey === 'defense' ? value : gift.ivs.defense} / SpA ${
                      ivKey === 'specialAttack' ? value : gift.ivs.specialAttack
                    } / SpD ${
                      ivKey === 'specialDefense' ? value : gift.ivs.specialDefense
                    } / Spe ${ivKey === 'speed' ? value : gift.ivs.speed}`
                  : gift.ivSummary,
                level: request.field === 'level' ? value : gift.level,
                shinyLock: request.field === 'shinyLock' ? value : gift.shinyLock,
                shinyLockLabel: request.field === 'shinyLock' ? 'Random' : gift.shinyLockLabel
              }
            : gift
        )
      };

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits,
          sessionId: 'session-1'
        },
        workflow: currentGiftPokemonWorkflow
      });
    },
    updateTradePokemonField: (request) => {
      const value = Number.parseInt(request.value, 10);
      const ivKey = ivFieldToKey[request.field as keyof typeof ivFieldToKey] ?? null;
      const pendingEdit = {
        domain: 'workflow.tradePokemon',
        field: request.field,
        newValue: request.value,
        recordId: `trade:${request.tradeIndex}`,
        sources: [
          {
            layer: 'base' as const,
            relativePath: 'romfs/bin/script_event_data/field_trade.bin'
          }
        ],
        summary: `Set Trade 001 ${request.field} to ${request.value}.`
      };
      const pendingEdits = [...(request.session?.pendingEdits ?? []), pendingEdit];
      currentTradePokemonWorkflow = {
        ...currentTradePokemonWorkflow,
        trades: currentTradePokemonWorkflow.trades.map((trade) =>
          trade.tradeIndex === request.tradeIndex
            ? {
                ...trade,
                ivs: ivKey
                  ? {
                      ...trade.ivs,
                      [ivKey]: value
                    }
                  : trade.ivs,
                ivSummary: ivKey
                  ? `HP ${ivKey === 'hp' ? value : trade.ivs.hp} / Atk ${
                      ivKey === 'attack' ? value : trade.ivs.attack
                    } / Def ${ivKey === 'defense' ? value : trade.ivs.defense} / SpA ${
                      ivKey === 'specialAttack' ? value : trade.ivs.specialAttack
                    } / SpD ${
                      ivKey === 'specialDefense' ? value : trade.ivs.specialDefense
                    } / Spe ${ivKey === 'speed' ? value : trade.ivs.speed}`
                  : trade.ivSummary,
                level: request.field === 'level' ? value : trade.level,
                shinyLock: request.field === 'shinyLock' ? value : trade.shinyLock,
                shinyLockLabel: request.field === 'shinyLock' ? 'Random' : trade.shinyLockLabel
              }
            : trade
        )
      };

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits,
          sessionId: 'session-1'
        },
        workflow: currentTradePokemonWorkflow
      });
    },
    updateStaticEncounterField: (request) => {
      const value = Number.parseInt(request.value, 10);

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.staticEncounters',
              field: request.field,
              newValue: request.value,
              recordId: `static:${request.encounterIndex}`,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/script_event_data/event_encount_data.bin'
                }
              ],
              summary: `Set Static 001 ${request.field} to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...staticEncountersWorkflow,
          encounters: staticEncountersWorkflow.encounters.map((encounter) =>
            encounter.encounterIndex === request.encounterIndex
              ? {
                  ...encounter,
                  ivs:
                    request.field === 'ivHp'
                      ? {
                          ...encounter.ivs,
                          hp: value
                        }
                      : encounter.ivs,
                  ivSummary:
                    request.field === 'ivHp'
                      ? `HP ${value} / Atk 30 / Def 29 / SpA 27 / SpD 26 / Spe 28`
                      : encounter.ivSummary,
                  level: request.field === 'level' ? value : encounter.level,
                  shinyLock: request.field === 'shinyLock' ? value : encounter.shinyLock,
                  shinyLockLabel:
                    request.field === 'shinyLock' ? 'Random' : encounter.shinyLockLabel
                }
              : encounter
          )
        }
      });
    },
    updateRentalPokemonField: (request) => {
      const value = Number.parseInt(request.value, 10);

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.rentalPokemon',
              field: request.field,
              newValue: request.value,
              recordId: `rental:${request.rentalIndex}`,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/script_event_data/rental.bin'
                }
              ],
              summary: `Set Rental 001 ${request.field} to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...rentalPokemonWorkflow,
          rentals: rentalPokemonWorkflow.rentals.map((rental) =>
            rental.rentalIndex === request.rentalIndex
              ? {
                  ...rental,
                  evs:
                    request.field === 'evHp'
                      ? {
                          ...rental.evs,
                          hp: value
                        }
                      : rental.evs,
                  ivs:
                    request.field === 'ivHp'
                      ? {
                          ...rental.ivs,
                          hp: value
                        }
                      : rental.ivs,
                  ivSummary:
                    request.field === 'ivHp'
                      ? `HP ${value} / Atk 31 / Def 31 / SpA 31 / SpD 31 / Spe 31`
                      : rental.ivSummary,
                  level: request.field === 'level' ? value : rental.level
                }
              : rental
          )
        }
      });
    },
    updateDynamaxAdventureField: (request) => {
      const value = Number.parseInt(request.value, 10);
      const recordId = `dynamaxAdventure:${request.entryIndex}`;
      const pendingEdit = {
        domain: 'workflow.dynamaxAdventures',
        field: request.field,
        newValue: request.value,
        recordId,
        sources: [
          {
            layer: 'base' as const,
            relativePath:
              'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin'
          }
        ],
        summary: `Set Adventure 001 ${request.field} to ${request.value}.`
      };
      const pendingEdits = [
        ...(request.session?.pendingEdits.filter(
          (edit) =>
            !(
              edit.domain === pendingEdit.domain &&
              edit.recordId === pendingEdit.recordId &&
              edit.field === pendingEdit.field
            )
        ) ?? []),
        pendingEdit
      ];
      const speciesName =
        value === 1 ? 'Bulbasaur' : value === 810 ? 'Grookey' : `Species ${value}`;
      const moveName = value === 1 ? 'Scratch' : value === 2 ? 'Growl' : 'None';

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits,
          sessionId: 'session-1'
        },
        workflow: {
          ...dynamaxAdventuresWorkflow,
          encounters: dynamaxAdventuresWorkflow.encounters.map((encounter) =>
            encounter.entryIndex === request.entryIndex
              ? {
                  ...encounter,
                  ability: request.field === 'ability' ? value : encounter.ability,
                  abilityLabel:
                    request.field === 'ability'
                      ? value === 2
                        ? 'Hidden Ability'
                        : value === 4
                          ? 'Any Ability'
                          : 'Ability 1'
                      : encounter.abilityLabel,
                  form: request.field === 'form' ? value : encounter.form,
                  gigantamaxLabel:
                    request.field === 'gigantamaxState'
                      ? value === 2
                        ? 'Gigantamax'
                        : 'Normal'
                      : encounter.gigantamaxLabel,
                  gigantamaxState:
                    request.field === 'gigantamaxState' ? value : encounter.gigantamaxState,
                  guaranteedPerfectIvs:
                    request.field === 'guaranteedPerfectIvs'
                      ? value
                      : encounter.guaranteedPerfectIvs,
                  ivs:
                    request.field === 'guaranteedPerfectIvs'
                      ? {
                          ...encounter.ivs,
                          hp: value === 0 ? -1 : -value
                        }
                      : request.field === 'ivAttack'
                      ? {
                          ...encounter.ivs,
                          attack: value
                        }
                      : request.field === 'ivDefense'
                        ? {
                            ...encounter.ivs,
                            defense: value
                          }
                      : request.field === 'ivSpecialAttack'
                        ? {
                            ...encounter.ivs,
                            specialAttack: value
                          }
                      : request.field === 'ivSpecialDefense'
                        ? {
                            ...encounter.ivs,
                            specialDefense: value
                          }
                      : request.field === 'ivSpeed'
                        ? {
                            ...encounter.ivs,
                            speed: value
                          }
                      : encounter.ivs,
                  ivSummary:
                    request.field === 'guaranteedPerfectIvs'
                      ? `${value} guaranteed perfect / Atk Random / Def Random / SpA Random / SpD Random / Spe Random`
                      : encounter.ivSummary,
                  label:
                    request.field === 'species'
                      ? `Adventure 001: ${speciesName} Lv. ${encounter.level}`
                      : encounter.label,
                  level: request.field === 'level' ? value : encounter.level,
                  moves: request.field.startsWith('move')
                    ? encounter.moves.map((move) =>
                        request.field === `move${move.slot}Id`
                          ? {
                              ...move,
                              move: moveName,
                              moveId: value
                            }
                          : move
                      )
                    : encounter.moves,
                  species: request.field === 'species' ? speciesName : encounter.species,
                  speciesId: request.field === 'species' ? value : encounter.speciesId
                }
              : encounter
          )
        }
      });
    },
    updateShopInventoryItem: (request) => {
      const orderedItemIds =
        request.field === 'setInventory'
          ? request.value
              .split(',')
              .filter((value) => value.length > 0)
              .map((value) => Number.parseInt(value, 10))
          : null;
      const formatItem = (itemId: number, slot: number) => {
        const item = itemsWorkflow.items.find((candidate) => candidate.itemId === itemId);

        return {
          isKnownItem: item !== undefined,
          itemId,
          itemName: item?.name ?? `Item ${itemId}`,
          price: item?.buyPrice ?? 0,
          slot,
          stockLimit: null
        };
      };

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.shops',
              field: request.field,
              newValue: request.value,
              recordId: `${request.shopId}#${request.slot}`,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/appli/shop/bin/shop_data.bin'
                }
              ],
              summary:
                request.field === 'setInventory'
                  ? `Set Poke Mart inventory order to ${orderedItemIds?.length ?? 0} items.`
                  : `Set Poke Mart slot ${request.slot} item ID to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...shopsWorkflow,
          shops: shopsWorkflow.shops.map((shop) =>
            shop.shopId === request.shopId
              ? {
                  ...shop,
                  inventory:
                    orderedItemIds !== null
                      ? orderedItemIds.map((itemId, index) => formatItem(itemId, index + 1))
                      : shop.inventory.map((item) =>
                          item.slot === request.slot
                            ? formatItem(Number.parseInt(request.value, 10), item.slot)
                            : item
                        )
                }
              : shop
          )
        }
      });
    },
    updateEncounterSlotField: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
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
              summary: `Set Sword Symbol Zone 0x1122334455667788 Normal slot ${request.slot} probability to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...encountersWorkflow,
          tables: encountersWorkflow.tables.map((table) =>
            table.tableId === request.tableId
              ? {
                  ...table,
                  slots: table.slots.map((slot) =>
                    slot.slot === request.slot
                      ? { ...slot, weight: Number.parseInt(request.value, 10) }
                      : slot
                  )
                }
              : table
          )
        }
      }),
    updateRaidBattleSlotField: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.raidBattles',
              field: request.field,
              newValue: request.value,
              recordId: `${request.tableId}#${request.slot}`,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/archive/field/resident/data_table.gfpak'
                }
              ],
              summary: `Set Raid Battles 0xAABBCCDD00112233 slot ${request.slot} ${request.field} to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...raidBattlesWorkflow,
          tables: raidBattlesWorkflow.tables.map((table) =>
            table.tableId === request.tableId
              ? {
                  ...table,
                  slots: table.slots.map((slot) =>
                    slot.slot === request.slot
                      ? {
                          ...slot,
                          flawlessIvs:
                            request.field === 'flawlessIvs'
                              ? Number.parseInt(request.value, 10)
                              : slot.flawlessIvs,
                          probabilities: slot.probabilities.map((value, index) =>
                            request.field === 'star5Probability' && index === 4
                              ? Number.parseInt(request.value, 10)
                              : value
                          )
                        }
                      : slot
                  )
                }
              : table
          )
        }
      }),
    updateRaidRewardField: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.raidRewards',
              field: request.field,
              newValue: request.value,
              recordId: `${request.tableId}#${request.slot}`,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/archive/field/resident/data_table.gfpak'
                }
              ],
              summary: `Set Drop 0xAABBCCDD00112233 slot ${request.slot} 5-star drop chance to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...raidRewardsWorkflow,
          tables: raidRewardsWorkflow.tables.map((table) =>
            table.tableId === request.tableId
              ? {
                  ...table,
                  rewards: table.rewards.map((reward) =>
                    reward.slot === request.slot
                      ? {
                          ...reward,
                          values: reward.values.map((value, index) =>
                            request.field === 'star5Value' && index === 4
                              ? Number.parseInt(request.value, 10)
                              : value
                          )
                        }
                      : reward
                  )
                }
              : table
          )
        }
      }),
    updateRaidBonusRewardField: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.raidBonusRewards',
              field: request.field,
              newValue: request.value,
              recordId: `${request.tableId}#${request.slot}`,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/archive/field/resident/data_table.gfpak'
                }
              ],
              summary: `Set Bonus 0x1020304050607080 slot ${request.slot} 5-star quantity to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...raidBonusRewardsWorkflow,
          tables: raidBonusRewardsWorkflow.tables.map((table) =>
            table.tableId === request.tableId
              ? {
                  ...table,
                  rewards: table.rewards.map((reward) =>
                    reward.slot === request.slot
                      ? {
                          ...reward,
                          values: reward.values.map((value, index) =>
                            request.field === 'star5Value' && index === 4
                              ? Number.parseInt(request.value, 10)
                              : value
                          )
                        }
                      : reward
                  )
                }
              : table
          )
        }
      }),
    updatePlacementObjectField: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.placement',
              field: request.field,
              newValue: request.value,
              recordId: request.objectId,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/archive/field/resident/placement.gfpak'
                }
              ],
              summary: `Set Field item: Potion ${request.field} to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...placementWorkflow,
          objects: placementWorkflow.objects.map((placedObject) =>
            placedObject.objectId === request.objectId
              ? { ...placedObject, quantity: Number.parseInt(request.value, 10) }
              : placedObject
          )
        }
      }),
    updateBehaviorEntryField: (request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.behavior',
              field: request.field,
              newValue: request.value,
              recordId: request.entryId,
              sources: [
                {
                  layer: 'base',
                  relativePath:
                    'romfs/bin/field/param/symbol_encount_mons_param/symbol_encount_mons_param.bin'
                }
              ],
              summary: `Set Pikachu ${request.field} to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...behaviorWorkflow,
          entries: behaviorWorkflow.entries.map((entry) =>
            entry.entryId === request.entryId
              ? {
                  ...entry,
                  behavior:
                    request.field === 'behavior'
                      ? request.value
                      : entry.behavior,
                  behaviorLabel:
                    request.field === 'behavior'
                      ? request.value
                      : entry.behaviorLabel,
                  fields: entry.fields.map((field) =>
                    field.field === request.field ? { ...field, value: request.value } : field
                  ),
                  speciesId:
                    request.field === 'speciesId'
                      ? Number.parseInt(request.value, 10)
                      : entry.speciesId
                }
              : entry
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

function wrongGameHealthDiagnostic() {
  return [
    {
      domain: 'project',
      expected: '0x01008DB008C2C000 for Pokemon Shield',
      file: 'base-exefs',
      message:
        'Selected Pokemon Shield, but Base ExeFS contains Pokemon Sword title id 0x0100ABF008968000.',
      severity: 'error' as const
    }
  ];
}

function createWrongGameHealth(): ProjectHealth {
  return {
    canOpenEditableWorkflows: false,
    canOpenReadOnlyWorkflows: false,
    diagnostics: wrongGameHealthDiagnostic(),
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
        diagnostics: wrongGameHealthDiagnostic(),
        isRequired: true,
        path: 'base-exefs',
        role: 'baseExeFs',
        status: 'unsafe'
      }
    ],
    state: 'blocked'
  };
}

function getApplyMessage(targetRelativePath: string, domain: string | undefined) {
  if (targetRelativePath.includes('/message/')) {
    return 'Applied Text change plan to the configured LayeredFS output root.';
  }

  if (targetRelativePath.includes('/trainer/')) {
    return 'Applied Trainers change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.giftPokemon') {
    return 'Applied Gift Pokemon change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.tradePokemon') {
    return 'Applied Trade Pokemon change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.staticEncounters') {
    return 'Applied Static Encounter change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.rentalPokemon') {
    return 'Applied Rental Pokemon change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.dynamaxAdventures') {
    return 'Applied Dynamax Adventures change plan to the configured LayeredFS output root.';
  }

  if (targetRelativePath.includes('/shop/')) {
    return 'Applied Shops change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.raidBattles') {
    return 'Applied Raid Battles change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.raidRewards') {
    return 'Applied Raid Rewards change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.moves') {
    return 'Applied Moves change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.pokemon') {
    return 'Applied Pokemon change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.royalCandy') {
    return 'Applied Royal Candy change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.bagHook') {
    return 'Installed Bag Hook V2 to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.catchCap') {
    return 'Applied Catch Cap Editor changes to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.hyperTraining') {
    return 'Applied Hyper Training changes to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.gymUniformRemoval') {
    return 'Applied Gym Uniform Removal changes to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.ivScreen') {
    return 'Applied IV Screen changes to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.startingItems') {
    return 'Applied Starting Items grants to Bag Hook slots 2-20 in the configured LayeredFS output root.';
  }

  if (domain === 'workflow.exefsPatches') {
    return 'Applied ExeFS patch to the configured LayeredFS output root.';
  }

  if (targetRelativePath.includes('/archive/field/resident/')) {
    return 'Applied Wild Encounters change plan to the configured LayeredFS output root.';
  }

  return 'Applied Items change plan to the configured LayeredFS output root.';
}

function getMockPokemonCompatibilityLabel(
  workflow: PokemonWorkflow,
  personalId: number,
  field: string
) {
  const [, groupId, slotText] = field.split(':');
  const slot = Number.parseInt(slotText ?? '', 10);
  const pokemon = workflow.pokemon.find((record) => record.personalId === personalId);

  return pokemon?.compatibility
    .find((group) => group.groupId === groupId)
    ?.entries
    .find((entry) => entry.slot === slot)
    ?.label;
}

function getValidationMessage(domain: string | undefined) {
  switch (domain) {
    case 'workflow.text':
      return 'Pending text change is valid.';
    case 'workflow.trainers':
      return 'Pending trainer change is valid.';
    case 'workflow.giftPokemon':
      return 'Pending gift Pokemon change is valid.';
    case 'workflow.tradePokemon':
      return 'Pending trade Pokemon change is valid.';
    case 'workflow.staticEncounters':
      return 'Pending static encounter change is valid.';
    case 'workflow.rentalPokemon':
      return 'Pending rental Pokemon change is valid.';
    case 'workflow.dynamaxAdventures':
      return 'Pending Dynamax Adventure change is valid.';
    case 'workflow.shops':
      return 'Pending shop change is valid.';
    case 'workflow.encounters':
      return 'Pending encounter change is valid.';
    case 'workflow.raidBattles':
      return 'Pending raid battle change is valid.';
    case 'workflow.raidRewards':
      return 'Pending raid reward change is valid.';
    case 'workflow.moves':
      return 'Pending move change is valid.';
    case 'workflow.bagHook':
      return 'Pending Bag Hook install is valid for change-plan review.';
    case 'workflow.catchCap':
      return 'Pending Catch Cap Editor values are valid for change-plan review.';
    case 'workflow.hyperTraining':
      return 'Pending Hyper Training change is valid for change-plan review.';
    case 'workflow.gymUniformRemoval':
      return 'Pending Gym Uniform Removal change is valid for change-plan review.';
    case 'workflow.ivScreen':
      return 'Pending IV Screen change is valid for change-plan review.';
    case 'workflow.royalCandy':
      return 'Pending Royal Candy workflow is valid.';
    case 'workflow.startingItems':
      return 'Pending Starting Items grants are valid for change-plan review.';
    case 'workflow.exefsPatches':
      return 'Pending ExeFS patch is valid.';
    default:
      return 'Pending item change is valid.';
  }
}
