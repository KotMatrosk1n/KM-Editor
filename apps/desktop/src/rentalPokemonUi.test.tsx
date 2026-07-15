/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen, waitFor, within } from '@testing-library/react';
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

async function openValidatedSwordProject(bridge: ProjectBridge) {
  const user = userEvent.setup();
  render(<App bridge={bridge} />);

  await user.click(screen.getByRole('button', { name: 'Pokemon Sword' }));
  await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
  await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
  await user.type(screen.getByLabelText('Output Root'), 'output');
  await user.click(screen.getByRole('button', { name: 'Validate Paths' }));

  return user;
}

describe('Rental Pokemon UI regressions', () => {
  beforeEach(resetWorkbench);

  it('keeps Rental mappings, contextual drafts, validation, and atomic rejection safe', async () => {
    const bridge = createMockProjectBridge({}, true);
    const originalLoadPokemonWorkflow = bridge.loadPokemonWorkflow.bind(bridge);
    const originalLoadRentalPokemonWorkflow = bridge.loadRentalPokemonWorkflow.bind(bridge);
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

    let loadedRentalWorkflow: Awaited<
      ReturnType<ProjectBridge['loadRentalPokemonWorkflow']>
    >['workflow'] | null = null;
    bridge.loadRentalPokemonWorkflow = vi.fn(async (request) => {
      const response = await originalLoadRentalPokemonWorkflow(request);
      const sourceRental = response.workflow.rentals[0]!;
      const field = (fieldName: string) =>
        response.workflow.editableFields.find((candidate) => candidate.field === fieldName)!;
      const statFieldNames = [
        'Hp',
        'Attack',
        'Defense',
        'SpecialAttack',
        'SpecialDefense',
        'Speed'
      ];
      const evFields = statFieldNames.map((stat) => ({
        ...field('evHp'),
        field: `ev${stat}`,
        label: `${stat} EV`
      }));
      const ivFields = statFieldNames.map((stat) => ({
        ...field('ivHp'),
        field: `iv${stat}`,
        label: `${stat} IV`
      }));
      const moveFields = [0, 1, 2, 3].map((slot) => ({
        ...field('move0Id'),
        field: `move${slot}Id`,
        label: `Move ${slot + 1}`,
        options: [
          { label: '000 None', value: 0 },
          { label: '001 Scratch', value: 1 },
          { label: '002 Growl', value: 2 }
        ]
      }));
      const editableFields = response.workflow.editableFields
        .filter(
          (candidate) =>
            !/^ev/.test(candidate.field) &&
            !/^iv/.test(candidate.field) &&
            !/^move[0-3]Id$/.test(candidate.field)
        )
        .map((candidate) =>
          candidate.field === 'level'
            ? { ...candidate, maximumValue: 100, minimumValue: 1 }
            : candidate
        );
      const bulbasaurRental = {
        ...sourceRental,
        ability: 2,
        abilityLabel: 'Hidden Ability - Chlorophyll',
        abilityOptions: [
          { label: 'Ability 1 - Overgrow', value: 0 },
          { label: 'Hidden Ability - Chlorophyll', value: 2 }
        ],
        gender: 2,
        genderLabel: 'Female',
        genderOptions: [
          { label: 'Random', value: 0 },
          { label: 'Male', value: 1 },
          { label: 'Female', value: 2 }
        ],
        label: 'Rental 001: Bulbasaur Lv. 50',
        moves: [
          { move: 'Growl', moveId: 2, slot: 2 },
          { move: 'Scratch', moveId: 1, slot: 0 },
          { move: null, moveId: 0, slot: 3 },
          { move: null, moveId: 0, slot: 1 }
        ],
        species: 'Bulbasaur',
        speciesId: 1
      };
      loadedRentalWorkflow = {
        ...response.workflow,
        editableFields: [...editableFields, ...moveFields, ...evFields, ...ivFields],
        rentals: [
          bulbasaurRental,
          {
            ...bulbasaurRental,
            ability: 0,
            abilityLabel: 'Ability 1 - Overgrow',
            abilityOptions: [{ label: 'Ability 1 - Overgrow', value: 0 }],
            gender: 1,
            genderLabel: 'Male',
            genderOptions: [
              { label: 'Random', value: 0 },
              { label: 'Male', value: 1 }
            ],
            label: 'Rental 002: Grookey Lv. 50',
            rentalIndex: 1,
            species: 'Grookey',
            speciesId: 810
          }
        ],
        stats: { ...response.workflow.stats, totalRentalCount: 2 }
      };

      return { workflow: loadedRentalWorkflow };
    });

    let rejectNextBatch = true;
    const updateRentalPokemonFields = vi.fn(
      async (request: Parameters<ProjectBridge['updateRentalPokemonFields']>[0]) => {
        const workflow = loadedRentalWorkflow;
        if (!request.session || !workflow) {
          throw new Error('Rental workflow and edit session must be loaded before staging.');
        }

        if (rejectNextBatch) {
          rejectNextBatch = false;
          return {
            diagnostics: [
              {
                domain: 'workflow.rentalPokemon',
                field: 'level',
                message: 'Rejected Rental Pokemon edit.',
                severity: 'error' as const
              }
            ],
            session: { ...request.session, sessionId: 'rejected-session' },
            workflow: {
              ...workflow,
              rentals: workflow.rentals.map((rental) =>
                rental.rentalIndex === 0 ? { ...rental, level: 99 } : rental
              )
            }
          };
        }

        const nextWorkflow = {
          ...workflow,
          rentals: workflow.rentals.map((rental) => {
            if (rental.rentalIndex !== 0) {
              return rental;
            }

            return request.updates.reduce(
              (updated, update) =>
                update.field === 'level'
                  ? { ...updated, level: Number.parseInt(update.value, 10) }
                  : update.field === 'trainerId'
                    ? { ...updated, trainerId: Number.parseInt(update.value, 10) }
                    : updated,
              rental
            );
          })
        };
        loadedRentalWorkflow = nextWorkflow;
        return {
          diagnostics: [],
          session: {
            ...request.session,
            hasPendingChanges: true,
            pendingEdits: request.updates.map((update) => ({
              domain: 'workflow.rentalPokemon',
              field: update.field,
              newValue: update.value,
              recordId: `rental:${update.rentalIndex}`,
              sources: [],
              summary: 'Stage Rental Pokemon edit.'
            }))
          },
          workflow: nextWorkflow
        };
      }
    );
    bridge.updateRentalPokemonFields = updateRentalPokemonFields;

    const user = await openValidatedSwordProject(bridge);
    await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
    await user.click(screen.getByRole('button', { name: 'Rental Pokemon' }));

    await waitFor(() =>
      expect(
        useWorkbenchStore
          .getState()
          .pokemonWorkflow?.pokemon.some((pokemon) => pokemon.speciesId === 810)
      ).toBe(true)
    );
    const table = await screen.findByRole('table', { name: 'Rental Pokemon' });
    expect(within(table).getAllByText('Scratch / Growl')).toHaveLength(2);

    const searchInput = screen.getByRole('searchbox', { name: 'Search rental Pokemon' });
    const inspector = screen.getByLabelText('Selected rental Pokemon provenance');
    await user.type(searchInput, 'Grookey');
    expect(within(inspector).getAllByText(/Grookey/).length).toBeGreaterThan(0);
    await user.clear(searchInput);
    await user.type(searchInput, 'Bulbasaur');
    expect(within(inspector).getByText('Rental #1 | Lv. 50')).toBeInTheDocument();

    await user.click(within(inspector).getByRole('button', { name: 'Edit' }));
    const speciesInput = within(inspector).getByLabelText('Species');
    const abilityInput = within(inspector).getByLabelText('Ability slot');
    const genderInput = within(inspector).getByLabelText('Gender');
    expect(abilityInput).toHaveValue('Hidden Ability - Chlorophyll');
    expect(genderInput).toHaveValue('Female');
    expect(within(inspector).getByLabelText('Move 1')).toHaveValue('001 Scratch');
    expect(within(inspector).getByLabelText('Move 3')).toHaveValue('002 Growl');

    await user.clear(speciesInput);
    await user.type(speciesInput, '810 Grookey');
    await user.tab();
    expect(within(inspector).getByLabelText('Form')).toHaveValue('Base');
    expect(abilityInput).toHaveValue('Ability 1 - Overgrow');
    expect(genderInput).toHaveValue('Random');

    await user.clear(speciesInput);
    await user.type(speciesInput, '001 Bulbasaur');
    await user.tab();
    await user.clear(abilityInput);
    await user.type(abilityInput, 'Hidden Ability - Chlorophyll');
    await user.tab();
    await user.clear(genderInput);
    await user.type(genderInput, 'Female');
    await user.tab();

    const ivPresetInput = within(inspector).getByLabelText('IV preset');
    await user.clear(ivPresetInput);
    await user.type(ivPresetInput, 'Custom');
    await user.tab();
    const hpIvInput = within(inspector).getByLabelText('Hp IV');
    const hpEvInput = within(inspector).getByLabelText('Hp EV');
    const stageButton = within(inspector).getByRole('button', { name: 'Stage' });
    await user.clear(hpIvInput);
    await user.type(hpIvInput, '-1');
    expect(within(inspector).getByText('Minimum value is 0.')).toBeInTheDocument();
    expect(stageButton).toBeDisabled();
    await user.clear(hpIvInput);
    await user.type(hpIvInput, '31');

    await user.clear(hpEvInput);
    await user.type(hpEvInput, '7');
    expect(
      within(inspector).getByText('EV total is 511. Maximum total is 510.')
    ).toBeInTheDocument();
    expect(stageButton).toBeDisabled();
    await user.clear(hpEvInput);
    await user.type(hpEvInput, '4');

    const levelInput = within(inspector).getByLabelText('Level');
    const trainerIdInput = within(inspector).getByLabelText('Trainer ID');
    await user.clear(levelInput);
    await user.type(levelInput, '51');
    await user.clear(trainerIdInput);
    await user.type(trainerIdInput, '54321');
    expect(stageButton).toBeEnabled();
    await user.click(stageButton);

    await waitFor(() =>
      expect(updateRentalPokemonFields).toHaveBeenLastCalledWith(
        expect.objectContaining({
          updates: [
            { field: 'level', rentalIndex: 0, value: '51' },
            { field: 'trainerId', rentalIndex: 0, value: '54321' }
          ]
        })
      )
    );
    expect(stageButton).toBeEnabled();
    expect(levelInput).toHaveValue(51);
    expect(searchInput).toHaveValue('Bulbasaur');
    expect(useWorkbenchStore.getState().editSession?.sessionId).toBe('session-1');
    expect(useWorkbenchStore.getState().rentalPokemonWorkflow?.rentals[0]?.level).toBe(50);
    expect(useWorkbenchStore.getState().editValidationDiagnostics).toEqual([
      expect.objectContaining({ message: 'Rejected Rental Pokemon edit.', severity: 'error' })
    ]);

    await user.click(stageButton);
    await waitFor(() => expect(updateRentalPokemonFields).toHaveBeenCalledTimes(2));
    await waitFor(() =>
      expect(useWorkbenchStore.getState().rentalPokemonWorkflow?.rentals[0]?.level).toBe(51)
    );
    expect(searchInput).toHaveValue('Bulbasaur');
    expect(useWorkbenchStore.getState().editSession?.pendingEdits).toHaveLength(2);
  }, 15_000);
});
