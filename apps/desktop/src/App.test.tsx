/* SPDX-License-Identifier: GPL-3.0-only */

import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from './App';
import {
  type EncountersWorkflow,
  type ExeFsPatchWorkflow,
  type FlagworkSaveWorkflow,
  type GiftPokemonWorkflow,
  type ItemsWorkflow,
  type MovesWorkflow,
  type PlacementWorkflow,
  type PokemonWorkflow,
  type ProjectFileGraph,
  type ProjectHealth,
  type RaidBattlesWorkflow,
  type RaidRewardsWorkflow,
  type RentalPokemonWorkflow,
  type ShopsWorkflow,
  type SpreadsheetImportWorkflow,
  type StaticEncountersWorkflow,
  type TextWorkflow,
  type TradePokemonWorkflow,
  type TrainersWorkflow,
  type WorkflowSummary
} from './bridge/contracts';
import { type ProjectBridge } from './bridge/projectBridge';
import { type DesktopServices } from './desktopServices';
import { useWorkbenchStore } from './workbenchStore';

describe('App', () => {
  beforeEach(() => {
    window.localStorage.clear();
    useWorkbenchStore.setState({
      activeSection: 'health',
      applyResult: null,
      changePlan: null,
      draftPaths: {
        baseExeFsPath: '',
        baseRomFsPath: '',
        outputRootPath: '',
        saveFilePath: ''
      },
      editSession: null,
      editValidationDiagnostics: [],
      encounterSearchText: '',
      encountersWorkflow: null,
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
      spreadsheetImportPreview: null,
      spreadsheetImportSearchText: '',
      spreadsheetImportSourcePath: '',
      spreadsheetImportWorkflow: null,
      selectedEncounterTableId: null,
      selectedExeFsCheckId: null,
      selectedExeFsPatchId: null,
      selectedGiftPokemonIndex: null,
      selectedTradePokemonIndex: null,
      selectedRentalPokemonIndex: null,
      selectedStaticEncounterIndex: null,
      selectedRoyalCandyCheckId: null,
      selectedRoyalCandyWorkflowId: null,
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
    expect(screen.getByRole('heading', { level: 3, name: 'Pokemon Data' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: 'Moves Data' })).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 3, name: 'Text and Dialogue Map' })
    ).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: 'Trainers' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: 'Gift Pokemon' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: 'Trade Pokemon' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: 'Static Encounters' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: 'Rental Pokemon' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: 'Shops' })).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 3, name: 'Encounters and Wild Data' })
    ).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: 'Raid Rewards' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 3, name: 'Placement' })).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 3, name: 'Flagwork and Save Inspectors' })
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 3, name: 'ExeFS Patch Manager' })
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 3, name: 'Royal Candy Workflows' })
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 3, name: 'Spreadsheet Import' })
    ).toBeInTheDocument();
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
    expect(screen.getByText('romfs/bin/pml/item/item.dat')).toBeInTheDocument();
    expect(screen.getByText('Base only')).toBeInTheDocument();
  });

  it('opens Pokemon Data, searches records, and shows selected details', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge()} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Pokemon' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Pokemon Data' })).toBeInTheDocument();
    expect(screen.getAllByText('Bulbasaur').length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: /Tackle/ })).toBeInTheDocument();

    await user.clear(screen.getByLabelText('Search Pokemon'));
    await user.type(screen.getByLabelText('Search Pokemon'), 'fire');

    expect(screen.queryByText('Bulbasaur')).not.toBeInTheDocument();
    expect(screen.getAllByText('Charmander').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Fire').length).toBeGreaterThan(0);
    expect(screen.getByText('romfs/bin/pml/personal/personal_total.bin')).toBeInTheDocument();
    expect(screen.getByText('Base only')).toBeInTheDocument();
  });

  it('starts a Pokemon edit session and saves a personal stat change', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Pokemon' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Pokemon Data' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Start Edit Session' }));
    const tm00 = screen.getByRole('checkbox', { name: /TM00 Mega Punch/ });
    expect(tm00).not.toBeChecked();
    await user.click(tm00);
    await waitFor(() => expect(tm00).toBeChecked());
    await user.selectOptions(screen.getByLabelText('Pokemon edit field'), 'type1');
    expect(screen.getByLabelText('Type 1')).toHaveDisplayValue('Grass');
    await user.selectOptions(screen.getByLabelText('Pokemon edit field'), 'ability1');
    expect(screen.getByLabelText('Ability 1')).toHaveDisplayValue('065 Overgrow');
    await user.selectOptions(screen.getByLabelText('Pokemon edit field'), 'heldItem1');
    expect(screen.getByLabelText('Held Item 50%')).toHaveDisplayValue('000 None');
    await user.selectOptions(screen.getByLabelText('Pokemon edit field'), 'hp');
    const hpInput = screen.getByLabelText('HP');
    await user.clear(hpInput);
    await user.type(hpInput, '99');
    await user.click(screen.getByRole('button', { name: 'Save HP' }));

    expect(await screen.findByDisplayValue('99')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Bulbasaur hp to 99.')).toBeInTheDocument();
  });

  it('starts a Pokemon edit session and saves a learnset row change', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Pokemon' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Pokemon Data' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Start Edit Session' }));
    await user.click(screen.getByRole('button', { name: /Growl/ }));
    await user.clear(screen.getByLabelText('Move ID'));
    await user.type(screen.getByLabelText('Move ID'), '345');
    const learnsetBlock = screen
      .getByRole('heading', { level: 4, name: 'Learnset' })
      .closest('.inspector-block') as HTMLElement | null;
    expect(learnsetBlock).not.toBeNull();
    const learnsetLevelInput = within(learnsetBlock!).getAllByLabelText('Level')[0]!;
    await user.clear(learnsetLevelInput);
    await user.type(learnsetLevelInput, '9');
    await user.click(screen.getByRole('button', { name: 'Save learnset row' }));

    expect(await screen.findByRole('button', { name: /Magical Leaf/ })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Bulbasaur learnset slot 1 to Lv. 9 Magical Leaf.')).toBeInTheDocument();
  });

  it('starts a Pokemon edit session and saves an evolution row change', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Pokemon' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Pokemon Data' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Start Edit Session' }));
    await user.click(screen.getByRole('button', { name: /002 Ivysaur/ }));
    await user.selectOptions(screen.getByLabelText('Method'), '8');
    await user.selectOptions(screen.getByLabelText('Item'), '25');
    await user.clear(screen.getByLabelText('Form'));
    await user.type(screen.getByLabelText('Form'), '1');
    const evolutionLevelInput = screen.getAllByLabelText('Level')[0]!;
    await user.clear(evolutionLevelInput);
    await user.type(evolutionLevelInput, '32');
    await user.click(screen.getByRole('button', { name: 'Save evolution row' }));

    expect(await screen.findByRole('button', { name: /008 Use Item/ })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Bulbasaur evolution slot 0 to species 2 at level 32.')).toBeInTheDocument();
  });

  it('opens Moves Data, searches records, and shows selected details', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge()} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Moves' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Moves Data' })).toBeInTheDocument();
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
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Moves' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Moves Data' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Start Edit Session' }));
    const powerInput = screen.getByLabelText('Power');
    await user.clear(powerInput);
    await user.type(powerInput, '80');
    await user.click(screen.getByRole('button', { name: 'Save power' }));

    expect(await screen.findByDisplayValue('80')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Tackle power to 80.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Change' }));

    expect(await screen.findByText('Pending move change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Review Change Plan' }));

    expect(await screen.findByRole('heading', { name: 'Change Plan Review' })).toBeInTheDocument();
    expect(screen.getAllByText('romfs/bin/pml/waza/waza_033.bin').length).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(await screen.findByRole('heading', { name: 'Apply Result' })).toBeInTheDocument();
    expect(
      screen.getByText('Applied Moves Data change plan to the configured LayeredFS output root.')
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
        saveFilePath: null
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
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Items' }));

    expect((await screen.findAllByText('Item 0000')).length).toBeGreaterThan(0);
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
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Items' }));

    expect(await screen.findByText('Loading backend workflow data.')).toBeInTheDocument();
    expect(loadItemsCount).toBe(1);

    await user.click(screen.getByRole('button', { name: 'Project Health' }));
    await act(async () => {
      resolveItemsWorkflow(await baseBridge.loadItemsWorkflow(lastRequest!));
    });

    expect(screen.getByRole('heading', { name: 'Project Health' })).toBeInTheDocument();

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
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Import' }));

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
    expect(screen.getAllByText('romfs/bin/pml/item/item.dat').length).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(await screen.findByRole('heading', { name: 'Apply Result' })).toBeInTheDocument();
    expect(screen.getByText('Applied Items change plan to the configured LayeredFS output root.')).toBeInTheDocument();
  });

  it('opens Text, edits a line, reviews a message table plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Text' }));

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Text and Dialogue Map' })
    ).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/message/English/common/story.dat').length
    ).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Start Edit Session' }));
    const textValue = screen.getByLabelText('Text value');
    await user.clear(textValue);
    await user.type(textValue, 'Hello there.');
    await user.click(screen.getByRole('button', { name: 'Save Text' }));

    expect(await screen.findByDisplayValue('Hello there.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set story #0 to "Hello there.".')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Change' }));

    expect(await screen.findByText('Pending text change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Review Change Plan' }));

    expect(await screen.findByRole('heading', { name: 'Change Plan Review' })).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/message/English/common/story.dat').length
    ).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(await screen.findByRole('heading', { name: 'Apply Result' })).toBeInTheDocument();
    expect(screen.getByText('Applied Text change plan to the configured LayeredFS output root.')).toBeInTheDocument();
  });

  it('opens Trainers, edits a party level, reviews a trainer plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Trainers' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Trainers' })).toBeInTheDocument();
    expect(screen.getAllByText('Avery').length).toBeGreaterThan(0);
    expect(screen.getByRole('option', { name: 'Slot 1: Grookey' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Start Edit Session' }));
    expect(screen.getByLabelText('Trainer class ID')).toHaveDisplayValue('005 Pokemon Trainer');
    expect(screen.getByLabelText('Class ball')).toHaveDisplayValue('4 Poke Ball');
    expect(screen.getByLabelText('Battle type')).toHaveDisplayValue('1 Doubles');
    expect(screen.getByLabelText('Trainer item 1 ID')).toHaveDisplayValue('001 Potion');
    expect(screen.getByLabelText('Money')).toHaveDisplayValue('24');
    expect(screen.getByLabelText('Species ID')).toHaveDisplayValue('810 Grookey');
    expect(screen.getByLabelText('Held item ID')).toHaveDisplayValue('001 Potion');
    expect(screen.getByLabelText('Move 1 ID')).toHaveDisplayValue('001 Scratch');
    const levelInput = screen.getByLabelText('Level');
    await user.clear(levelInput);
    await user.type(levelInput, '25');
    await user.click(screen.getByRole('button', { name: 'Save level' }));

    expect(await screen.findByDisplayValue('25')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Avery slot 1 level to 25.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Change' }));

    expect(await screen.findByText('Pending trainer change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Review Change Plan' }));

    expect(await screen.findByRole('heading', { name: 'Change Plan Review' })).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/trainer/trainer_poke/trainer_010.bin').length
    ).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(await screen.findByRole('heading', { name: 'Apply Result' })).toBeInTheDocument();
    expect(
      screen.getByText('Applied Trainers change plan to the configured LayeredFS output root.')
    ).toBeInTheDocument();
  });

  it('opens Gift Pokemon, edits IVs, reviews a gift plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Gifts' }));

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Gift Pokemon' })
    ).toBeInTheDocument();
    expect(screen.getAllByText('Bulbasaur').length).toBeGreaterThan(0);
    expect(screen.getByText('3 guaranteed perfect IVs')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Start Edit Session' }));
    const hpIvInput = screen.getByLabelText('HP IV');
    expect(hpIvInput).toHaveDisplayValue('-4');
    await user.clear(hpIvInput);
    await user.type(hpIvInput, '31');
    await user.click(screen.getByRole('button', { name: 'Save hp iv' }));

    expect(await screen.findByDisplayValue('31')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Gift 001 ivHp to 31.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Change' }));

    expect(await screen.findByText('Pending gift Pokemon change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Review Change Plan' }));

    expect(await screen.findByRole('heading', { name: 'Change Plan Review' })).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/script_event_data/add_poke.bin').length
    ).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(await screen.findByRole('heading', { name: 'Apply Result' })).toBeInTheDocument();
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
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Trades' }));

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Trade Pokemon' })
    ).toBeInTheDocument();
    expect(screen.getAllByText('Eevee').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Grookey').length).toBeGreaterThan(0);
    expect(screen.getByText('3 guaranteed perfect IVs')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Start Edit Session' }));
    const hpIvInput = screen.getByLabelText('HP IV');
    expect(hpIvInput).toHaveDisplayValue('-4');
    await user.clear(hpIvInput);
    await user.type(hpIvInput, '31');
    await user.click(screen.getByRole('button', { name: 'Save hp iv' }));

    expect(await screen.findByDisplayValue('31')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Trade 001 ivHp to 31.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Change' }));

    expect(await screen.findByText('Pending trade Pokemon change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Review Change Plan' }));

    expect(await screen.findByRole('heading', { name: 'Change Plan Review' })).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/script_event_data/field_trade.bin').length
    ).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(await screen.findByRole('heading', { name: 'Apply Result' })).toBeInTheDocument();
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
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Static Encounters' }));

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Static Encounters' })
    ).toBeInTheDocument();
    expect(screen.getAllByText('Grookey').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Calyrex').length).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Start Edit Session' }));
    const hpIvInput = screen.getByLabelText('HP IV');
    expect(hpIvInput).toHaveDisplayValue('31');
    await user.clear(hpIvInput);
    await user.type(hpIvInput, '0');
    await user.click(screen.getByRole('button', { name: 'Save hp iv' }));

    expect(await screen.findByDisplayValue('0')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Static 001 ivHp to 0.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Change' }));

    expect(await screen.findByText('Pending static encounter change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Review Change Plan' }));

    expect(await screen.findByRole('heading', { name: 'Change Plan Review' })).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/script_event_data/event_encount_data.bin').length
    ).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(await screen.findByRole('heading', { name: 'Apply Result' })).toBeInTheDocument();
    expect(
      screen.getByText(
        'Applied Static Encounter change plan to the configured LayeredFS output root.'
      )
    ).toBeInTheDocument();
  });

  it('opens Rental Pokemon, edits IVs, reviews a rental plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Rentals' }));

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Rental Pokemon' })
    ).toBeInTheDocument();
    expect(screen.getAllByText('Grookey').length).toBeGreaterThan(0);
    expect(
      screen.getAllByText('HP 31 / Atk 31 / Def 31 / SpA 31 / SpD 31 / Spe 31').length
    ).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Start Edit Session' }));
    const hpIvInput = screen.getByLabelText('HP IV');
    expect(hpIvInput).toHaveDisplayValue('31');
    await user.clear(hpIvInput);
    await user.type(hpIvInput, '0');
    await user.click(screen.getByRole('button', { name: 'Save hp iv' }));

    await waitFor(() => expect(screen.getByLabelText('HP IV')).toHaveDisplayValue('0'));

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Rental 001 ivHp to 0.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Change' }));

    expect(await screen.findByText('Pending rental Pokemon change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Review Change Plan' }));

    expect(await screen.findByRole('heading', { name: 'Change Plan Review' })).toBeInTheDocument();
    expect(screen.getAllByText('romfs/bin/script_event_data/rental.bin').length).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(await screen.findByRole('heading', { name: 'Apply Result' })).toBeInTheDocument();
    expect(
      screen.getByText('Applied Rental Pokemon change plan to the configured LayeredFS output root.')
    ).toBeInTheDocument();
  });

  it('opens Shops, edits an inventory item, reviews a shop plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Shops' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Shops' })).toBeInTheDocument();
    expect(screen.getAllByText('Poke Mart').length).toBeGreaterThan(0);
    expect(screen.getByRole('option', { name: 'Slot 1: Potion' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Start Edit Session' }));
    const itemIdInput = screen.getByLabelText('Item ID');
    await user.clear(itemIdInput);
    await user.type(itemIdInput, '2');
    await user.click(screen.getByRole('button', { name: 'Save Item' }));

    expect(await screen.findByDisplayValue('2')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(screen.getByText('Set Poke Mart slot 1 item ID to 2.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Change' }));

    expect(await screen.findByText('Pending shop change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Review Change Plan' }));

    expect(await screen.findByRole('heading', { name: 'Change Plan Review' })).toBeInTheDocument();
    expect(screen.getAllByText('romfs/bin/app/shop/shop_data.bin').length).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(await screen.findByRole('heading', { name: 'Apply Result' })).toBeInTheDocument();
    expect(
      screen.getByText('Applied Shops change plan to the configured LayeredFS output root.')
    ).toBeInTheDocument();
  });

  it('opens Encounters, edits a slot probability, reviews a wild data plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Encounters' }));

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Encounters and Wild Data' })
    ).toBeInTheDocument();
    expect(screen.getAllByText('Zone 0x1122334455667788').length).toBeGreaterThan(0);
    expect(screen.getByRole('option', { name: 'Slot 1: Bulbasaur' })).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Encounter slot'), '2');
    await user.click(screen.getByRole('button', { name: 'Start Edit Session' }));
    const probabilityInput = screen.getByLabelText('Probability');
    await user.clear(probabilityInput);
    await user.type(probabilityInput, '40');
    await user.click(screen.getByRole('button', { name: 'Save probability' }));

    expect(await screen.findByDisplayValue('40')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(
      screen.getByText(
        'Set Sword Symbol Zone 0x1122334455667788 Normal slot 2 probability to 40.'
      )
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Change' }));

    expect(await screen.findByText('Pending encounter change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Review Change Plan' }));

    expect(await screen.findByRole('heading', { name: 'Change Plan Review' })).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/archive/field/resident/data_table.gfpak').length
    ).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(await screen.findByRole('heading', { name: 'Apply Result' })).toBeInTheDocument();
    expect(
      screen.getByText('Applied Encounters change plan to the configured LayeredFS output root.')
    ).toBeInTheDocument();
  });

  it('opens Raid Rewards, edits a star value, reviews a reward plan, and applies it', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Raid Rewards' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Raid Rewards' })).toBeInTheDocument();
    expect(screen.getAllByText('0xAABBCCDD00112233').length).toBeGreaterThan(0);
    expect(screen.getByRole('option', { name: 'Slot 1: Exp. Candy L' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Start Edit Session' }));
    const starValueInput = screen.getByLabelText('5-star value');
    await user.clear(starValueInput);
    await user.type(starValueInput, '77');
    await user.click(screen.getByRole('button', { name: 'Save 5-star value' }));

    expect(await screen.findByDisplayValue('77')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(
      screen.getByText('Set Drop 0xAABBCCDD00112233 slot 1 5-star value to 77.')
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Change' }));

    expect(await screen.findByText('Pending raid reward change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Review Change Plan' }));

    expect(await screen.findByRole('heading', { name: 'Change Plan Review' })).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/archive/field/resident/data_table.gfpak').length
    ).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(await screen.findByRole('heading', { name: 'Apply Result' })).toBeInTheDocument();
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
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Raid Battles' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Raid Battles' })).toBeInTheDocument();
    expect(screen.getAllByText('0xAABBCCDD00112233').length).toBeGreaterThan(0);
    expect(screen.getByRole('option', { name: 'Slot 1: Eevee' })).toBeInTheDocument();
    expect(screen.getByText('Any Ability')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Start Edit Session' }));
    await user.selectOptions(screen.getByLabelText('Guaranteed perfect IVs'), '6');
    await user.click(screen.getByRole('button', { name: 'Save guaranteed perfect ivs' }));

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(
      screen.getByText('Set Raid Battles 0xAABBCCDD00112233 slot 1 flawlessIvs to 6.')
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Change' }));

    expect(await screen.findByText('Pending raid battle change is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Review Change Plan' }));

    expect(await screen.findByRole('heading', { name: 'Change Plan Review' })).toBeInTheDocument();
    expect(
      screen.getAllByText('romfs/bin/archive/field/resident/data_table.gfpak').length
    ).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(await screen.findByRole('heading', { name: 'Apply Result' })).toBeInTheDocument();
    expect(
      screen.getByText('Applied Raid Battles change plan to the configured LayeredFS output root.')
    ).toBeInTheDocument();
  });

  it('opens Flagwork and Save Inspectors, searches real keys, and shows provenance', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge()} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Flagwork' }));

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

  it('opens ExeFS Patch Manager, searches compatibility checks, and shows provenance', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge()} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open ExeFS' }));

    expect(
      await screen.findByRole('heading', {
        level: 2,
        name: 'ExeFS Patch Manager'
      })
    ).toBeInTheDocument();
    expect(screen.getAllByText('ExeFS main compatibility').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Patch code cave').length).toBeGreaterThan(0);
    expect(screen.getAllByText('exefs/main').length).toBeGreaterThan(0);

    await user.type(screen.getByLabelText('Search ExeFS compatibility checks'), 'royal');

    expect(screen.getAllByText('Royal Candy immediate scan').length).toBeGreaterThan(0);
    expect(screen.getAllByText('.text').length).toBeGreaterThan(0);
    expect(screen.getAllByText('file+0x100').length).toBeGreaterThan(0);
  });

  it('stages an ExeFS patch for review and apply', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output-root');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open ExeFS' }));
    await user.click(await screen.findByRole('button', { name: 'Stage Patch' }));

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(await screen.findByText('Stage ExeFS patch: ExeFS main compatibility.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Change' }));

    expect(await screen.findByText('Pending ExeFS patch is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Review Change Plan' }));

    expect((await screen.findAllByText('exefs/main')).length).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(
      await screen.findByText('Applied ExeFS patch to the configured LayeredFS output root.')
    ).toBeInTheDocument();
  });

  it('opens Royal Candy workflows, searches checks, and shows planned outputs', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge()} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Candy' }));

    expect(
      await screen.findByRole('heading', {
        level: 2,
        name: 'Royal Candy Workflows'
      })
    ).toBeInTheDocument();
    expect(screen.getAllByText('Install Unlimited Royal Candy').length).toBeGreaterThan(0);
    expect(screen.getAllByText('romfs/bin/pml/item/item.dat').length).toBeGreaterThan(0);
    expect(screen.getAllByText('exefs/main').length).toBeGreaterThan(0);

    await user.type(screen.getByLabelText('Search Royal Candy workflows'), 'code cave');

    expect(screen.getAllByText('patch-code-cave').length).toBeGreaterThan(0);
    expect(screen.getAllByText('ExeFS').length).toBeGreaterThan(0);
  });

  it('stages a Royal Candy workflow for review and apply', async () => {
    const user = userEvent.setup();
    render(<App bridge={createMockProjectBridge({}, true)} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output-root');
    await user.click(screen.getAllByRole('button', { name: 'Open Project' })[1]!);
    await user.click(screen.getByRole('button', { name: 'Workflows' }));
    await user.click(await screen.findByRole('button', { name: 'Open Candy' }));
    await user.click(await screen.findByRole('button', { name: 'Stage Workflow' }));

    await user.click(screen.getByRole('button', { name: 'Changes' }));

    expect(
      await screen.findByText('Stage Royal Candy workflow: Install Unlimited Royal Candy.')
    ).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Validate Pending Change' }));

    expect(await screen.findByText('Pending Royal Candy workflow is valid.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Review Change Plan' }));

    expect((await screen.findAllByText('romfs/bin/pml/item/item.dat')).length).toBeGreaterThan(0);

    await user.click(screen.getByRole('button', { name: 'Apply Plan' }));

    expect(
      await screen.findByText('Applied Royal Candy change plan to the configured LayeredFS output root.')
    ).toBeInTheDocument();
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

  it('uses desktop folder pickers and opens the output root', async () => {
    const user = userEvent.setup();
    const openedPaths: string[] = [];
    const desktopServices = createMockDesktopServices({
      openPath: async (path) => {
        openedPaths.push(path);
      },
      pickFile: async ({ title }) => (title === 'Select Save File' ? 'picked-save-main' : null),
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
    await user.click(screen.getByRole('button', { name: 'Browse for Save File' }));

    expect(screen.getByLabelText('Base RomFS')).toHaveValue('picked-romfs');
    expect(screen.getByLabelText('Base ExeFS')).toHaveValue('picked-exefs');
    expect(screen.getByLabelText('Output Root')).toHaveValue('picked-output');
    expect(screen.getByLabelText('Save File')).toHaveValue('picked-save-main');

    await user.click(screen.getByRole('button', { name: 'Open Output Root' }));

    expect(openedPaths).toEqual(['picked-output']);
    expect(window.localStorage.getItem('km-editor.project-path-draft.v1')).toContain(
      'picked-output'
    );
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

    expect(await screen.findByText('The folder does not exist.')).toBeInTheDocument();
  });
});

function createMockDesktopServices(overrides: Partial<DesktopServices> = {}): DesktopServices {
  return {
    isAvailable: true,
    openPath: async () => undefined,
    pickFile: async () => null,
    pickFolder: async () => null,
    ...overrides
  };
}

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
        valueKind: 'integer'
      },
      {
        field: 'sellPrice',
        label: 'Sell price',
        maximumValue: 499_999,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'wattsPrice',
        label: 'Watts price',
        maximumValue: 999_999,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'alternatePrice',
        label: 'Alternate price',
        maximumValue: 999_999,
        minimumValue: 0,
        valueKind: 'integer'
      }
    ],
    items: [
      {
        alternatePrice: 3,
        buyPrice: 300,
        category: 'Medicine',
        itemId: 1,
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
        itemId: 2,
        name: 'Antidote',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/pml/item/item.dat',
          sourceLayer: 'base'
        },
        sellPrice: 100,
        sharedItemIds: [2],
        wattsPrice: 10
      }
    ],
    stats: {
      sourceFileCount: 2,
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
  const pokemonWorkflowSummary: WorkflowSummary = {
    availability: canEdit ? 'available' : 'readOnly',
    description: 'Pokemon personal stats, forms, evolutions, learnsets, and source provenance.',
    diagnostics: [],
    id: 'pokemon',
    label: 'Pokemon Data'
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
    pokemon: [
      {
        abilities: {
          ability1: 65,
          ability2: 0,
          hiddenAbility: 34
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
          ability2: 0,
          hiddenAbility: 94
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
    label: 'Moves Data'
  };
  const movesWorkflow: MovesWorkflow = {
    diagnostics: [],
    editableFields: [
      {
        field: 'power',
        label: 'Power',
        maximumValue: 255,
        minimumValue: 0,
        valueKind: 'integer'
      },
      {
        field: 'makesContact',
        label: 'Makes contact',
        maximumValue: 1,
        minimumValue: 0,
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
        maximumValue: 2,
        minimumValue: 0,
        options: [
          { label: '0 Singles', value: 0 },
          { label: '1 Doubles', value: 1 },
          { label: '2 Multi', value: 2 }
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
        maximumValue: 255,
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
          { label: '0 Off', value: 0 },
          { label: '1 On', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'money',
        label: 'Money',
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
        options: [],
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
          { label: 'Never Shiny', value: 1 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'dynamaxLevel',
        label: 'Dynamax level',
        maximumValue: 255,
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
        label: 'Special move',
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
          { label: '6 Perfect IVs', value: 6 }
        ],
        valueKind: 'integer'
      }
    ],
    gifts: [
      {
        ability: 1,
        abilityLabel: 'Ability 1',
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
        label: 'Gift 001: Bulbasaur Lv. 5 Form 0',
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
        maximumValue: 255,
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
          { label: '6 Perfect IVs', value: 6 }
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
        ballItem: 'Poke Ball',
        ballItemId: 4,
        canGigantamax: false,
        dynamaxLevel: 0,
        field03: 7,
        flawlessIvCount: 3,
        form: 0,
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
        label: 'Trade 001: Eevee -> Grookey Lv. 15',
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
        requiredForm: 0,
        requiredNature: 25,
        requiredNatureLabel: 'Random',
        requiredSpecies: 'Eevee',
        requiredSpeciesId: 133,
        shinyLock: 2,
        shinyLockLabel: 'Never Shiny',
        species: 'Grookey',
        speciesId: 810,
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
        maximumValue: 255,
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
          { label: '6 Perfect IVs', value: 6 }
        ],
        valueKind: 'integer'
      }
    ],
    encounters: [
      {
        ability: 3,
        abilityLabel: 'Hidden Ability',
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
        label: 'Static 001: Grookey-1 Lv. 50 | Calyrex',
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
        maximumValue: 25,
        minimumValue: 0,
        options: [
          { label: 'Hardy', value: 0 },
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
        label: 'Fixed IV preset',
        maximumValue: 31,
        minimumValue: 0,
        options: [
          { label: '0 IVs', value: 0 },
          { label: '31 IVs', value: 31 }
        ],
        valueKind: 'integer'
      }
    ],
    rentals: [
      {
        ability: 1,
        abilityLabel: 'Ability 1',
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
        label: 'Item ID',
        maximumValue: 65535,
        minimumValue: 0,
        valueKind: 'integer'
      }
    ],
    shops: [
      {
        currency: 'Money',
        inventory: [
          {
            itemId: 1,
            itemName: 'Potion',
            price: 300,
            slot: 1,
            stockLimit: null
          },
          {
            itemId: 2,
            itemName: 'Antidote',
            price: 200,
            slot: 2,
            stockLimit: null
          }
        ],
        location: 'Poke Mart',
        name: 'Poke Mart',
        provenance: {
          fileState: 'baseOnly',
          sourceFile: 'romfs/bin/app/shop/shop_data.bin',
          sourceLayer: 'base'
        },
        shopId: 'single:1F3FF031A3A24490'
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
    label: 'Encounters and Wild Data'
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
        field: 'flawlessIvs',
        label: 'Guaranteed perfect IVs',
        maximumValue: 6,
        minimumValue: 0,
        options: [
          { label: 'Random IVs', value: 0 },
          { label: '4 Guaranteed Perfect IVs', value: 4 },
          { label: '6 Perfect IVs', value: 6 }
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
            bonusTableHash: '0x1020304050607080',
            dropTableHash: '0xAABBCCDD00112233',
            entryIndex: 0,
            flawlessIvs: 4,
            form: 1,
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
            bonusTableHash: '0x0807060504030201',
            dropTableHash: '0xAABBCCDD00112233',
            entryIndex: 1,
            flawlessIvs: 0,
            form: 0,
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
                        reason: 'Apply pending Pokemon Data edit: Set Bulbasaur hp to 99.',
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
                        reason: 'Apply pending Moves Data edit: Set Tackle power to 80.',
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
                : request.session.pendingEdits[0]?.domain === 'workflow.shops'
                  ? [
                      {
                        reason: 'Apply pending Shops edit: Set Poke Mart slot 1 item ID to 2.',
                        replacesExistingOutput: false,
                        sources: [
                          {
                            layer: 'base',
                            relativePath: 'romfs/bin/app/shop/shop_data.bin'
                          }
                        ],
                        targetRelativePath: 'romfs/bin/app/shop/shop_data.bin'
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
                                'Apply pending Raid Rewards edit: Set Drop 0xAABBCCDD00112233 slot 1 5-star value to 77.',
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
          shopsWorkflowSummary,
          encountersWorkflowSummary,
          raidBattlesWorkflowSummary,
          raidRewardsWorkflowSummary,
          placementWorkflowSummary,
          flagworkSaveWorkflowSummary,
          exeFsPatchWorkflowSummary,
          royalCandyWorkflowSummary,
          spreadsheetImportWorkflowSummary
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
            totalStepCount: 2,
            totalWorkflowCount: 1,
            warningCount: 0
          },
          summary: royalCandyWorkflowSummary,
          workflows: [
            {
              category: 'Build',
              description: 'Prepares Royal Candy item 1128 from Rare Candy item 50.',
              itemId: 1128,
              mode: 'unlimited',
              name: 'Install Unlimited Royal Candy',
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
              newValue: 'unlimited',
              recordId: request.workflowId,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/pml/item/item.dat'
                }
              ],
              summary: 'Stage Royal Candy workflow: Install Unlimited Royal Candy.'
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
            totalStepCount: 2,
            totalWorkflowCount: 1,
            warningCount: 0
          },
          summary: royalCandyWorkflowSummary,
          workflows: [
            {
              category: 'Build',
              description: 'Prepares Royal Candy item 1128 from Rare Candy item 50.',
              itemId: 1128,
              mode: 'unlimited',
              name: 'Install Unlimited Royal Candy',
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
            }
          ]
        }
      }),
    loadSpreadsheetImportWorkflow: () =>
      Promise.resolve({
        workflow: spreadsheetImportWorkflow
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
    loadRaidBattlesWorkflow: () =>
      Promise.resolve({
        workflow: raidBattlesWorkflow
      }),
    loadRaidRewardsWorkflow: () =>
      Promise.resolve({
        workflow: raidRewardsWorkflow
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
        workflow: giftPokemonWorkflow
      }),
    loadTradePokemonWorkflow: () =>
      Promise.resolve({
        workflow: tradePokemonWorkflow
      }),
    loadStaticEncountersWorkflow: () =>
      Promise.resolve({
        workflow: staticEncountersWorkflow
      }),
    loadRentalPokemonWorkflow: () =>
      Promise.resolve({
        workflow: rentalPokemonWorkflow
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
            }

            return {
              ...pokemon,
              learnset: learnset.map((move, slot) => ({ ...move, slot }))
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

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.giftPokemon',
              field: request.field,
              newValue: request.value,
              recordId: `gift:${request.giftIndex}`,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/script_event_data/add_poke.bin'
                }
              ],
              summary: `Set Gift 001 ${request.field} to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...giftPokemonWorkflow,
          gifts: giftPokemonWorkflow.gifts.map((gift) =>
            gift.giftIndex === request.giftIndex
              ? {
                  ...gift,
                  ivs:
                    request.field === 'ivHp'
                      ? {
                          ...gift.ivs,
                          hp: value
                        }
                      : gift.ivs,
                  ivSummary:
                    request.field === 'ivHp'
                      ? `HP ${value} / Atk -1 / Def -1 / SpA -1 / SpD -1 / Spe -1`
                      : gift.ivSummary,
                  level: request.field === 'level' ? value : gift.level
                }
              : gift
          )
        }
      });
    },
    updateTradePokemonField: (request) => {
      const value = Number.parseInt(request.value, 10);

      return Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.tradePokemon',
              field: request.field,
              newValue: request.value,
              recordId: `trade:${request.tradeIndex}`,
              sources: [
                {
                  layer: 'base',
                  relativePath: 'romfs/bin/script_event_data/field_trade.bin'
                }
              ],
              summary: `Set Trade 001 ${request.field} to ${request.value}.`
            }
          ],
          sessionId: 'session-1'
        },
        workflow: {
          ...tradePokemonWorkflow,
          trades: tradePokemonWorkflow.trades.map((trade) =>
            trade.tradeIndex === request.tradeIndex
              ? {
                  ...trade,
                  ivs:
                    request.field === 'ivHp'
                      ? {
                          ...trade.ivs,
                          hp: value
                        }
                      : trade.ivs,
                  ivSummary:
                    request.field === 'ivHp'
                      ? `HP ${value} / Atk -1 / Def -1 / SpA -1 / SpD -1 / Spe -1`
                      : trade.ivSummary,
                  level: request.field === 'level' ? value : trade.level
                }
              : trade
          )
        }
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
                  level: request.field === 'level' ? value : encounter.level
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
    updateShopInventoryItem: (request) =>
      Promise.resolve({
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
                  relativePath: 'romfs/bin/app/shop/shop_data.bin'
                }
              ],
              summary: `Set Poke Mart slot ${request.slot} item ID to ${request.value}.`
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
                  inventory: shop.inventory.map((item) =>
                    item.slot === request.slot
                      ? {
                          ...item,
                          itemId: Number.parseInt(request.value, 10),
                          itemName: `Item ${request.value}`,
                          price: 0
                        }
                      : item
                  )
                }
              : shop
          )
        }
      }),
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
              summary: `Set Drop 0xAABBCCDD00112233 slot ${request.slot} 5-star value to ${request.value}.`
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
    return 'Applied Moves Data change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.pokemon') {
    return 'Applied Pokemon Data change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.royalCandy') {
    return 'Applied Royal Candy change plan to the configured LayeredFS output root.';
  }

  if (domain === 'workflow.exefsPatches') {
    return 'Applied ExeFS patch to the configured LayeredFS output root.';
  }

  if (targetRelativePath.includes('/archive/field/resident/')) {
    return 'Applied Encounters change plan to the configured LayeredFS output root.';
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
    case 'workflow.royalCandy':
      return 'Pending Royal Candy workflow is valid.';
    case 'workflow.exefsPatches':
      return 'Pending ExeFS patch is valid.';
    default:
      return 'Pending item change is valid.';
  }
}
