/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { type PokemonWorkflow } from './bridge/contracts';
import { createMockProjectBridge } from './testSupport/appTestFixtures';
import { useWorkbenchStore } from './workbenchStore';

const tauriEventMock = vi.hoisted(() => ({
  listen: vi.fn(() => Promise.resolve(() => undefined))
}));

vi.mock('@tauri-apps/api/event', () => ({
  listen: tauriEventMock.listen
}));

describe('SwSh Pokemon UI', () => {
  it('keeps Pokemon drafts atomic, bounded, and stable through filtering and failed staging', async () => {
    window.localStorage.clear();
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
      openProject: null,
      pokemonSearchText: '',
      pokemonWorkflow: null,
      projectStatus: 'idle',
      selectedPokemonPersonalId: null,
      workflows: []
    });

    const user = userEvent.setup();
    const bridge = createMockProjectBridge({}, true);
    const originalLoadPokemonWorkflow = bridge.loadPokemonWorkflow;
    let currentWorkflow: PokemonWorkflow | null = null;

    bridge.loadPokemonWorkflow = vi.fn(async (request) => {
      const response = await originalLoadPokemonWorkflow(request);
      const bulbasaur = response.workflow.pokemon.find((pokemon) => pokemon.personalId === 1);
      const baseEvolution = bulbasaur?.evolutions[0];
      if (!bulbasaur || !baseEvolution) {
        throw new Error('Bulbasaur Pokemon fixture was not loaded.');
      }

      if (!response.workflow.editableFields.some((field) => field.field === 'color')) {
        response.workflow.editableFields.push({
          field: 'color',
          group: 'Traits',
          label: 'Color',
          maximumValue: 9,
          minimumValue: 0,
          options: [
            { label: '0 Red', value: 0 },
            { label: '5 Green', value: 5 }
          ],
          valueKind: 'integer'
        });
      }

      bulbasaur.personal = { ...bulbasaur.personal, color: 12 };
      bulbasaur.learnset = bulbasaur.learnset.map((move, index) =>
        index === 0 ? { ...move, level: 150 } : move
      );
      bulbasaur.evolutions = [
        { ...baseEvolution, form: 300, level: 150, slot: 2 },
        { ...baseEvolution, level: 32, slot: 5, species: 3 },
        {
          ...baseEvolution,
          argument: 25,
          argumentKind: 'value',
          argumentLabel: 'Argument',
          argumentValue: '25',
          level: 0,
          method: 999,
          methodName: 'Method 999',
          slot: 7,
          species: 1
        }
      ];
      currentWorkflow = response.workflow;
      return response;
    });

    const singularUpdate = vi.fn(bridge.updatePokemonField);
    bridge.updatePokemonField = singularUpdate;
    const originalUpdatePokemonEvolution = bridge.updatePokemonEvolution;
    let failNextEvolution = false;
    const updatePokemonEvolution = vi.fn(
      async (request: Parameters<typeof bridge.updatePokemonEvolution>[0]) => {
        if (!currentWorkflow) {
          throw new Error('Pokemon workflow was not loaded.');
        }

        if (failNextEvolution) {
          failNextEvolution = false;
          return {
            diagnostics: [
              {
                message: 'The evolution update was rejected.',
                severity: 'error' as const
              }
            ],
            session: {
              hasPendingChanges: true,
              pendingEdits: [
                {
                  domain: 'workflow.pokemon',
                  field: 'evolution:upsert:5',
                  newValue: 'poisoned',
                  recordId: request.personalId.toString(),
                  sources: [],
                  summary: 'This rejected evolution must not replace the active session.'
                }
              ],
              sessionId: 'rejected-evolution-session'
            },
            workflow: {
              ...currentWorkflow,
              pokemon: currentWorkflow.pokemon.map((pokemon) =>
                pokemon.personalId === request.personalId
                  ? {
                      ...pokemon,
                      baseStats: { ...pokemon.baseStats, hp: 222 }
                    }
                  : pokemon
              )
            }
          };
        }

        const response = await originalUpdatePokemonEvolution(request);
        currentWorkflow = response.workflow;
        return response;
      }
    );
    bridge.updatePokemonEvolution = updatePokemonEvolution;
    const originalUpdatePokemonLearnset = bridge.updatePokemonLearnset;
    const updatePokemonLearnset = vi.fn(
      async (request: Parameters<typeof bridge.updatePokemonLearnset>[0]) => {
        const response = await originalUpdatePokemonLearnset(request);
        currentWorkflow = response.workflow;
        return response;
      }
    );
    bridge.updatePokemonLearnset = updatePokemonLearnset;
    let updateAttempt = 0;
    const updatePokemonFields = vi.fn(
      async (request: Parameters<typeof bridge.updatePokemonFields>[0]) => {
        if (!currentWorkflow) {
          throw new Error('Pokemon workflow was not loaded.');
        }

        updateAttempt += 1;
        const hpUpdate = request.updates.find((update) => update.field === 'hp');
        if (updateAttempt === 1) {
          return {
            diagnostics: [
              {
                message: 'The Pokemon batch was rejected.',
                severity: 'error' as const
              }
            ],
            session: {
              hasPendingChanges: true,
              pendingEdits: [
                {
                  domain: 'workflow.pokemon',
                  field: 'hp',
                  newValue: '222',
                  recordId: request.updates[0]?.personalId.toString() ?? '1',
                  sources: [],
                  summary: 'This rejected edit must not replace the active session.'
                }
              ],
              sessionId: 'rejected-session'
            },
            workflow: {
              ...currentWorkflow,
              pokemon: currentWorkflow.pokemon.map((pokemon) =>
                pokemon.personalId === request.updates[0]?.personalId
                  ? {
                      ...pokemon,
                      baseStats: { ...pokemon.baseStats, hp: 222 }
                    }
                  : pokemon
              )
            }
          };
        }

        const hp = Number.parseInt(hpUpdate?.value ?? '0', 10);
        currentWorkflow = {
          ...currentWorkflow,
          pokemon: currentWorkflow.pokemon.map((pokemon) =>
            pokemon.personalId === hpUpdate?.personalId
              ? {
                  ...pokemon,
                  baseStats: {
                    ...pokemon.baseStats,
                    hp,
                    total: pokemon.baseStats.total - pokemon.baseStats.hp + hp
                  }
                }
              : pokemon
          )
        };

        return {
          diagnostics: [],
          session: {
            hasPendingChanges: true,
            pendingEdits: [
              ...(request.session?.pendingEdits ?? []),
              ...request.updates.map((update) => ({
                domain: 'workflow.pokemon',
                field: update.field,
                newValue: update.value,
                recordId: update.personalId.toString(),
                sources: [],
                summary: `Set Pokemon ${update.personalId} ${update.field} to ${update.value}.`
              }))
            ],
            sessionId: request.session?.sessionId ?? 'session-1'
          },
          workflow: currentWorkflow
        };
      }
    );
    bridge.updatePokemonFields = updatePokemonFields;

    const { container } = render(<App bridge={bridge} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));

    const navigation = await screen.findByRole('navigation', { name: 'Workspace' });
    await user.click(within(navigation).getByRole('button', { name: 'Editors' }));
    await user.click(within(navigation).getByRole('button', { name: 'Pokemon' }));

    const inspector = await screen.findByRole('complementary', {
      name: 'Selected Pokemon provenance'
    });
    const searchInput = screen.getByLabelText('Search Pokemon');
    await user.type(searchInput, 'Fire');
    await waitFor(() => expect(within(inspector).getAllByText('Charmander')).not.toHaveLength(0));
    expect(useWorkbenchStore.getState().selectedPokemonPersonalId).toBe(4);
    await user.clear(searchInput);
    await user.type(searchInput, 'Grass');
    await waitFor(() => expect(within(inspector).getAllByText('Bulbasaur')).not.toHaveLength(0));
    expect(useWorkbenchStore.getState().selectedPokemonPersonalId).toBe(1);

    await user.click(within(inspector).getByRole('button', { name: 'Edit' }));

    const stageButton = within(inspector).getByRole('button', { name: 'Stage' });
    expect(within(inspector).getByLabelText('Color')).toHaveValue('12 Color');

    const learnsetBlock = container.querySelector('.swsh-pokemon-learnset-block');
    if (!(learnsetBlock instanceof HTMLElement)) {
      throw new Error('SwSh learnset block was not rendered.');
    }
    const learnsetLevelInput = within(learnsetBlock).getByLabelText('Level');
    const moveLearnsetDown = within(learnsetBlock).getByRole('button', {
      name: 'Move learnset row down'
    });
    expect(learnsetLevelInput).toHaveAttribute('max', '100');
    expect(learnsetLevelInput).toHaveValue(150);
    expect(moveLearnsetDown).toBeEnabled();
    await user.clear(learnsetLevelInput);
    await user.type(learnsetLevelInput, '101');
    expect(moveLearnsetDown).toBeDisabled();
    await user.type(within(learnsetBlock).getByLabelText('New level'), '1');
    await user.type(within(learnsetBlock).getByLabelText('New move'), '75');
    expect(
      within(learnsetBlock).getByRole('button', { name: 'Add learnset row' })
    ).toBeDisabled();
    await user.clear(within(learnsetBlock).getByLabelText('New move'));
    await user.clear(within(learnsetBlock).getByLabelText('New level'));
    await user.clear(learnsetLevelInput);
    await user.type(learnsetLevelInput, '150');
    await waitFor(() => expect(moveLearnsetDown).toBeEnabled());

    const learnsetMoveInput = within(learnsetBlock).getByLabelText('Move');
    await user.clear(learnsetMoveInput);
    await user.type(learnsetMoveInput, '0');
    expect(stageButton).toBeDisabled();
    await user.clear(learnsetMoveInput);
    await user.type(learnsetMoveInput, '33');

    const newLearnsetMoveInput = within(learnsetBlock).getByLabelText('New move');
    const newLearnsetLevelInput = within(learnsetBlock).getByLabelText('New level');
    const addLearnsetButton = within(learnsetBlock).getByRole('button', {
      name: 'Add learnset row'
    });
    await user.type(newLearnsetLevelInput, '1');
    await user.type(newLearnsetMoveInput, '0');
    expect(addLearnsetButton).toBeDisabled();
    await user.clear(newLearnsetMoveInput);
    await user.type(newLearnsetMoveInput, '999');
    expect(addLearnsetButton).toBeDisabled();
    await user.clear(newLearnsetMoveInput);
    await user.type(newLearnsetMoveInput, '75');
    expect(addLearnsetButton).toBeEnabled();
    await user.clear(newLearnsetMoveInput);
    await user.clear(newLearnsetLevelInput);

    const evolutionBlock = container.querySelector('.swsh-pokemon-evolutions-block');
    if (!(evolutionBlock instanceof HTMLElement)) {
      throw new Error('SwSh evolution block was not rendered.');
    }
    const moveEvolutionUp = within(evolutionBlock).getByRole('button', {
      name: 'Move evolution row up'
    });
    const moveEvolutionDown = within(evolutionBlock).getByRole('button', {
      name: 'Move evolution row down'
    });
    expect(moveEvolutionUp).toBeDisabled();
    expect(moveEvolutionDown).toBeEnabled();
    await user.click(moveEvolutionDown);
    await waitFor(() => expect(updatePokemonEvolution).toHaveBeenCalledTimes(1));
    await waitFor(() =>
      expect(within(evolutionBlock).getByText('#6').closest('button')).toHaveClass(
        'learnset-row-selected'
      )
    );

    const evolutionLevelInput = within(evolutionBlock)
      .getAllByLabelText('Level')
      .find((input) => input.getAttribute('max') === '100');
    if (!(evolutionLevelInput instanceof HTMLInputElement)) {
      throw new Error('SwSh evolution level input was not rendered.');
    }
    const removeEvolution = within(evolutionBlock).getByRole('button', {
      name: 'Remove evolution row'
    });
    expect(evolutionLevelInput).toHaveAttribute('max', '100');
    expect(evolutionLevelInput).toHaveValue(150);
    expect(removeEvolution).toBeEnabled();
    await user.clear(evolutionLevelInput);
    await user.type(evolutionLevelInput, '101');
    expect(removeEvolution).toBeDisabled();
    await user.type(within(evolutionBlock).getByLabelText('New method'), '4');
    await user.type(within(evolutionBlock).getByLabelText('New species'), '2');
    await user.type(within(evolutionBlock).getByLabelText('New level'), '16');
    expect(
      within(evolutionBlock).getByRole('button', { name: 'Add evolution row' })
    ).toBeDisabled();
    await user.clear(within(evolutionBlock).getByLabelText('New method'));
    await user.clear(within(evolutionBlock).getByLabelText('New species'));
    await user.clear(within(evolutionBlock).getByLabelText('New level'));
    await user.clear(evolutionLevelInput);
    await user.type(evolutionLevelInput, '150');
    await waitFor(() => expect(removeEvolution).toBeEnabled());

    const evolutionMethodInput = within(evolutionBlock).getByLabelText('Method');
    await user.clear(evolutionMethodInput);
    await user.type(evolutionMethodInput, '0');
    expect(stageButton).toBeDisabled();
    await user.clear(evolutionMethodInput);
    await user.type(evolutionMethodInput, '4');

    const unsupportedEvolutionRow = within(evolutionBlock).getByText('#8').closest('button');
    if (!(unsupportedEvolutionRow instanceof HTMLButtonElement)) {
      throw new Error('Unsupported legacy evolution row was not rendered.');
    }
    await user.click(unsupportedEvolutionRow);
    const unsupportedEvolutionArgumentInput = within(evolutionBlock).getByLabelText('Argument');
    await user.clear(unsupportedEvolutionArgumentInput);
    await user.type(unsupportedEvolutionArgumentInput, '26');
    expect(stageButton).toBeDisabled();
    await user.clear(unsupportedEvolutionArgumentInput);
    await user.type(unsupportedEvolutionArgumentInput, '25');
    const selectedSupportedEvolutionRow = within(evolutionBlock).getByText('#6').closest('button');
    if (!(selectedSupportedEvolutionRow instanceof HTMLButtonElement)) {
      throw new Error('Supported evolution row was not rendered.');
    }
    await user.click(selectedSupportedEvolutionRow);

    const newEvolutionMethodInput = within(evolutionBlock).getByLabelText('New method');
    const newEvolutionSpeciesInput = within(evolutionBlock).getByLabelText('New species');
    const newEvolutionLevelInput = within(evolutionBlock).getByLabelText('New level');
    const addEvolutionButton = within(evolutionBlock).getByRole('button', {
      name: 'Add evolution row'
    });
    await user.type(newEvolutionLevelInput, '16');
    await user.type(newEvolutionSpeciesInput, '2');
    await user.type(newEvolutionMethodInput, '0');
    expect(addEvolutionButton).toBeDisabled();
    await user.clear(newEvolutionMethodInput);
    await user.type(newEvolutionMethodInput, '4');
    await user.clear(newEvolutionSpeciesInput);
    await user.type(newEvolutionSpeciesInput, '0');
    expect(addEvolutionButton).toBeDisabled();
    await user.clear(newEvolutionSpeciesInput);
    await user.type(newEvolutionSpeciesInput, '999');
    expect(addEvolutionButton).toBeDisabled();
    await user.clear(newEvolutionSpeciesInput);
    await user.type(newEvolutionSpeciesInput, '2');
    expect(addEvolutionButton).toBeEnabled();
    await user.clear(newEvolutionMethodInput);
    await user.clear(newEvolutionSpeciesInput);
    await user.clear(newEvolutionLevelInput);

    const hpInput = within(inspector).getByLabelText('HP');
    await user.clear(hpInput);
    await user.type(hpInput, '50');
    expect(stageButton).toBeEnabled();
    const sessionBeforeRejectedBatch = useWorkbenchStore.getState().editSession;
    await user.click(stageButton);

    await waitFor(() => expect(updatePokemonFields).toHaveBeenCalledTimes(1));
    expect(singularUpdate).not.toHaveBeenCalled();
    expect(updatePokemonFields.mock.calls[0]?.[0].updates).toEqual([
      { field: 'hp', personalId: 1, value: '50' }
    ]);
    await waitFor(() =>
      expect(useWorkbenchStore.getState().editSession).toBe(sessionBeforeRejectedBatch)
    );
    expect(hpInput).toHaveValue(50);
    expect(stageButton).toBeEnabled();

    await user.click(stageButton);
    await waitFor(() => expect(updatePokemonFields).toHaveBeenCalledTimes(2));
    expect(updatePokemonFields.mock.calls[1]?.[0].session).toBe(sessionBeforeRejectedBatch);
    await waitFor(() =>
      expect(useWorkbenchStore.getState().editSession?.pendingEdits).toHaveLength(
        (sessionBeforeRejectedBatch?.pendingEdits.length ?? 0) + 1
      )
    );
    await waitFor(() => expect(stageButton).toBeDisabled());

    const sessionBeforeLaterFailure = useWorkbenchStore.getState().editSession;
    const workflowBeforeLaterFailure = useWorkbenchStore.getState().pokemonWorkflow;
    await user.clear(hpInput);
    await user.type(hpInput, '51');
    await user.clear(within(learnsetBlock).getByLabelText('Move'));
    await user.type(within(learnsetBlock).getByLabelText('Move'), '45');
    await user.clear(within(evolutionBlock).getByLabelText('Species'));
    await user.type(within(evolutionBlock).getByLabelText('Species'), '3');
    expect(stageButton).toBeEnabled();

    failNextEvolution = true;
    await user.click(stageButton);
    await waitFor(() => expect(updatePokemonFields).toHaveBeenCalledTimes(3));
    await waitFor(() => expect(updatePokemonEvolution).toHaveBeenCalledTimes(2));
    expect(updatePokemonLearnset).not.toHaveBeenCalled();
    await waitFor(() =>
      expect(useWorkbenchStore.getState().editSession).toBe(sessionBeforeLaterFailure)
    );
    expect(useWorkbenchStore.getState().pokemonWorkflow).toBe(workflowBeforeLaterFailure);
    expect(hpInput).toHaveValue(51);
    expect(within(learnsetBlock).getByLabelText('Move')).toHaveValue('045 Growl');
    expect(within(evolutionBlock).getByLabelText('Species')).toHaveValue('003 Venusaur');
    expect(stageButton).toBeEnabled();
    expect(searchInput).toHaveValue('Grass');
    expect(within(inspector).getAllByText('Bulbasaur')).not.toHaveLength(0);
  }, 15_000);
});
