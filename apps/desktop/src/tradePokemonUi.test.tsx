/* SPDX-License-Identifier: GPL-3.0-only */

import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { App } from './App';
import { type ProjectBridge } from './bridge/projectBridge';
import { createMockProjectBridge } from './testSupport/appTestFixtures';
import { useWorkbenchStore } from './workbenchStore';

function resetWorkbench() {
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
}

async function openValidatedProject(bridge: ProjectBridge, game: 'Pokemon Sword' | 'Pokemon Legends Z-A') {
  const user = userEvent.setup();
  const view = render(<App bridge={bridge} />);

  await user.click(screen.getByRole('button', { name: game }));
  await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
  await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
  await user.type(screen.getByLabelText('Output Root'), 'output');
  await user.click(screen.getByRole('button', { name: 'Validate Paths' }));

  return { user, view };
}

describe('Trade Pokemon UI regressions', () => {
  beforeEach(resetWorkbench);

  it('preserves mapped Trade state across filtered drafts, rejected batches, and game-specific shiny resets', async () => {
    const bridge = createMockProjectBridge({}, true);
    const originalLoadPokemonWorkflow = bridge.loadPokemonWorkflow.bind(bridge);
    const originalLoadTradePokemonWorkflow = bridge.loadTradePokemonWorkflow.bind(bridge);
    let loadedTradeWorkflow: Awaited<
      ReturnType<ProjectBridge['loadTradePokemonWorkflow']>
    >['workflow'] | null = null;

    bridge.loadPokemonWorkflow = vi.fn(async (request) => {
      const response = await originalLoadPokemonWorkflow(request);
      const bulbasaur = response.workflow.pokemon[0]!;
      return {
        workflow: {
          ...response.workflow,
          pokemon: [
            ...response.workflow.pokemon,
            {
              ...bulbasaur,
              abilities: {
                ...bulbasaur.abilities,
                ability2: 0,
                ability2Label: 'None',
                hiddenAbility: 0,
                hiddenAbilityLabel: 'None'
              },
              genderRatio: 0,
              genderRatioLabel: 'Male only',
              name: 'Grookey',
              personal: {
                ...bulbasaur.personal,
                genderRatio: 0,
                modelId: 810
              },
              personalId: 810,
              speciesId: 810
            }
          ]
        }
      };
    });
    bridge.loadTradePokemonWorkflow = vi.fn(async (request) => {
      const response = await originalLoadTradePokemonWorkflow(request);
      const sourceTrade = response.workflow.trades[0]!;
      const speciesField = response.workflow.editableFields.find(
        (field) => field.field === 'species'
      )!;
      const customTrade = {
        ...sourceTrade,
        ability: 3,
        abilityLabel: 'Hidden Ability - Chlorophyll',
        abilityOptions: [
          { label: 'Default - Overgrow', value: 0 },
          { label: 'Ability 1 - Overgrow', value: 1 },
          { label: 'Hidden Ability - Chlorophyll', value: 3 }
        ],
        flawlessIvCount: null,
        form: 0,
        gender: 2,
        genderLabel: 'Female',
        genderOptions: [
          { label: 'Random', value: 0 },
          { label: 'Male', value: 1 },
          { label: 'Female', value: 2 }
        ],
        ivSummary: 'Custom IVs',
        ivs: {
          attack: -1,
          defense: -1,
          hp: -1,
          specialAttack: -1,
          specialDefense: -1,
          speed: -1
        },
        label: 'Trade 001: Eevee -> Bulbasaur Lv. 5',
        level: 5,
        relearnMoves: [
          { move: null, moveId: 0, slot: 2 },
          { move: 'Scratch', moveId: 1, slot: 0 },
          { move: null, moveId: 0, slot: 3 },
          { move: null, moveId: 0, slot: 1 }
        ],
        species: 'Bulbasaur',
        speciesId: 1
      };
      loadedTradeWorkflow = {
        ...response.workflow,
        editableFields: response.workflow.editableFields.map((field) =>
          field.field === 'species'
            ? {
                ...field,
                options: [
                  { label: '001 Bulbasaur', value: 1 },
                  ...speciesField.options
                ]
              }
            : field
        ),
        stats: { ...response.workflow.stats, totalTradeCount: 2 },
        trades: [
          customTrade,
          {
            ...customTrade,
            ability: 0,
            abilityLabel: 'Default - Overgrow',
            abilityOptions: [
              { label: 'Default - Overgrow', value: 0 },
              { label: 'Ability 1 - Overgrow', value: 1 }
            ],
            gender: 1,
            genderLabel: 'Male',
            genderOptions: [
              { label: 'Random', value: 0 },
              { label: 'Male', value: 1 }
            ],
            label: 'Trade 002: Eevee -> Grookey Lv. 5',
            species: 'Grookey',
            speciesId: 810,
            tradeIndex: 1
          }
        ]
      };

      return { workflow: loadedTradeWorkflow };
    });
    const updateTradePokemonFields = vi.fn(async (request) => {
      const workflow = loadedTradeWorkflow;
      if (!request.session || !workflow) {
        throw new Error('Trade workflow and edit session must be loaded before staging.');
      }

      return {
        diagnostics: [
          {
            domain: 'workflow.tradePokemon',
            field: 'level',
            message: 'Rejected trade edit.',
            severity: 'error' as const
          }
        ],
        session: { ...request.session, sessionId: 'rejected-session' },
        workflow: {
          ...workflow,
          trades: workflow.trades.map((trade) =>
            trade.tradeIndex === 0 ? { ...trade, level: 99 } : trade
          )
        }
      };
    });
    bridge.updateTradePokemonFields = updateTradePokemonFields;
    const { user } = await openValidatedProject(bridge, 'Pokemon Sword');

    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    await user.click(screen.getByRole('button', { name: 'Trade Pokemon' }));

    await waitFor(() =>
      expect(
        useWorkbenchStore
          .getState()
          .pokemonWorkflow?.pokemon.some((pokemon) => pokemon.speciesId === 810)
      ).toBe(true)
    );

    const searchInput = await screen.findByRole('searchbox', { name: 'Search trade Pokemon' });
    const inspector = screen.getByLabelText('Selected trade Pokemon provenance');
    await user.type(searchInput, 'Grookey');
    expect(within(inspector).getByText('Grookey Lv. 5')).toBeInTheDocument();
    await user.clear(searchInput);
    await user.type(searchInput, 'Bulbasaur');
    expect(within(inspector).getByText('Bulbasaur Lv. 5')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    const speciesInput = screen.getByLabelText('Species');
    const abilityInput = screen.getByLabelText('Ability slot');
    const genderInput = screen.getByLabelText('Gender');
    const attackIvInput = screen.getByLabelText('Attack IV');
    expect(abilityInput).toHaveValue('Hidden Ability - Chlorophyll');
    expect(genderInput).toHaveValue('Female');
    expect(attackIvInput).toHaveValue(-1);
    expect(screen.getByLabelText('Relearn move 1')).toHaveValue('001 Scratch');

    await user.clear(speciesInput);
    await user.type(speciesInput, '001 Bulbasaur');
    await user.tab();
    expect(abilityInput).toHaveValue('Hidden Ability - Chlorophyll');
    expect(genderInput).toHaveValue('Female');

    await user.clear(speciesInput);
    await user.type(speciesInput, '810 Grookey');
    await user.tab();
    expect(abilityInput).toHaveValue('Default - Overgrow');
    expect(genderInput).toHaveValue('Random');
    expect(screen.getByLabelText('Form')).toHaveValue('Base');

    await user.clear(speciesInput);
    await user.type(speciesInput, '001 Bulbasaur');
    await user.tab();
    await user.clear(abilityInput);
    await user.type(abilityInput, 'Hidden Ability - Chlorophyll');
    await user.tab();
    await user.clear(genderInput);
    await user.type(genderInput, 'Female');
    await user.tab();

    const levelInput = screen.getByLabelText('Level');
    const stageButton = screen.getByRole('button', { name: 'Stage' });
    await user.clear(attackIvInput);
    await user.type(attackIvInput, '32');
    expect(screen.getByText('Maximum value is 31.')).toBeInTheDocument();
    expect(stageButton).toBeDisabled();
    await user.clear(attackIvInput);
    await user.type(attackIvInput, '-1');
    await user.clear(levelInput);
    await user.type(levelInput, '6');
    expect(stageButton).toBeEnabled();
    await user.click(stageButton);

    await waitFor(() => {
      expect(updateTradePokemonFields).toHaveBeenCalledWith(
        expect.objectContaining({
          updates: [{ field: 'level', tradeIndex: 0, value: '6' }]
        })
      );
      expect(stageButton).toBeEnabled();
    });

    const state = useWorkbenchStore.getState();
    expect(levelInput).toHaveValue(6);
    expect(attackIvInput).toHaveValue(-1);
    expect(searchInput).toHaveValue('Bulbasaur');
    expect(state.editSession?.sessionId).toBe('session-1');
    expect(state.tradePokemonWorkflow?.trades[0]?.level).toBe(5);
    expect(state.editValidationDiagnostics).toEqual([
      expect.objectContaining({ message: 'Rejected trade edit.', severity: 'error' })
    ]);

    cleanup();
    resetWorkbench();

    const zaBridge = createMockProjectBridge({}, true);
    const originalLoadZaTradePokemonWorkflow = zaBridge.loadTradePokemonWorkflow.bind(zaBridge);
    let zaWorkflow: Awaited<
      ReturnType<ProjectBridge['loadTradePokemonWorkflow']>
    >['workflow'] | null = null;
    zaBridge.loadTradePokemonWorkflow = vi.fn(async (request) => {
      const response = await originalLoadZaTradePokemonWorkflow(request);
      zaWorkflow = {
        ...response.workflow,
        editableFields: response.workflow.editableFields.map((field) =>
          field.field === 'shinyLock'
            ? {
                ...field,
                maximumValue: 0x3fffffff,
                minimumValue: 0x1fffffff,
                options: [
                  { label: 'Not shiny', value: 0x1fffffff },
                  { label: 'Forced shiny', value: 0x2fffffff },
                  { label: 'Default shiny roll', value: 0x3fffffff }
                ]
              }
            : field
        ),
        editorFamily: 'za',
        trades: response.workflow.trades.map((trade) => ({
          ...trade,
          editorFamily: 'za',
          shinyLock: 0x1fffffff,
          shinyLockLabel: 'Not shiny'
        }))
      };
      return { workflow: zaWorkflow };
    });
    const updateZaTradePokemonFields = vi.fn(async (request) => {
      if (!request.session || !zaWorkflow) {
        throw new Error('Z-A Trade workflow and edit session must be loaded before staging.');
      }
      return {
        diagnostics: [],
        session: {
          ...request.session,
          hasPendingChanges: true,
          pendingEdits: request.updates.map((update: {
            field: string;
            tradeIndex: number;
            value: string;
          }) => ({
            domain: 'workflow.tradePokemon',
            field: update.field,
            newValue: update.value,
            recordId: `trade:${update.tradeIndex}`,
            sources: [],
            summary: 'Reset trade shiny mode.'
          }))
        },
        workflow: {
          ...zaWorkflow,
          trades: zaWorkflow.trades.map((trade) => ({
            ...trade,
            shinyLock: 0x3fffffff,
            shinyLockLabel: 'Default shiny roll'
          }))
        }
      };
    });
    zaBridge.updateTradePokemonFields = updateZaTradePokemonFields;
    const zaProject = await openValidatedProject(zaBridge, 'Pokemon Legends Z-A');

    await zaProject.user.click(
      screen.getByRole('button', { name: 'Encounters & Pokemon Sources' })
    );
    await zaProject.user.click(screen.getByRole('button', { name: 'Trade Pokemon' }));
    const zaSearchInput = await screen.findByRole('searchbox', {
      name: 'Search trade Pokemon'
    });
    await zaProject.user.type(zaSearchInput, 'Farfetch');
    await zaProject.user.click(await screen.findByRole('button', { name: 'Edit' }));
    await zaProject.user.click(screen.getByRole('button', { name: 'Remove Trade Shiny Lock' }));
    const dialog = screen.getByRole('dialog', { name: 'Remove Trade Shiny Lock?' });
    expect(within(dialog).getAllByText(/Default shiny roll/)).toHaveLength(2);
    await zaProject.user.click(
      within(dialog).getByRole('button', { name: 'Remove Trade Shiny Lock' })
    );

    await waitFor(() =>
      expect(updateZaTradePokemonFields).toHaveBeenCalledWith(
        expect.objectContaining({
          updates: [
            expect.objectContaining({ field: 'shinyLock', value: '1073741823' })
          ]
        })
      )
    );
    expect(zaSearchInput).toHaveValue('Farfetch');
  }, 15_000);
});
