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

  it('preserves filtered Text state and rejected variable drafts while paging results', async () => {
    const bridge = createMockProjectBridge({}, true);
    const originalLoadTextWorkflow = bridge.loadTextWorkflow.bind(bridge);
    const variableTextKey = 'romfs/bin/message/English/common/story.dat#1';
    const variableValue = '[VAR 0100] Pikachu';
    let loadedTextWorkflow: Awaited<
      ReturnType<ProjectBridge['loadTextWorkflow']>
    >['workflow'] | null = null;
    const loadTextWorkflow = vi.fn(async (request) => {
      const response = await originalLoadTextWorkflow(request);
      const sourceEntry = response.workflow.entries[0]!;
      const sourceReference = response.workflow.dialogueReferences[0]!;
      loadedTextWorkflow = {
        ...response.workflow,
        dialogueReferences: [
          sourceReference,
          {
            ...sourceReference,
            dialogueId: 'common/story:1',
            label: 'story #1',
            preview: variableValue,
            textId: 1
          }
        ],
        entries: [
          sourceEntry,
          {
            ...sourceEntry,
            label: 'story #1',
            lineIndex: 1,
            textId: 1,
            textKey: variableTextKey,
            value: variableValue
          }
        ],
        stats: {
          ...response.workflow.stats,
          dialogueReferenceCount: 2,
          totalTextEntryCount: 2
        }
      };

      return { workflow: loadedTextWorkflow };
    });
    const updateTextEntry = vi.fn(async (request) => {
      const workflow = loadedTextWorkflow;
      if (!request.session || !workflow) {
        throw new Error('Text workflow and edit session must be loaded before staging.');
      }

      return {
        diagnostics: [
          {
            domain: 'workflow.text',
            field: 'value',
            message: 'Rejected text edit.',
            severity: 'error' as const
          }
        ],
        session: {
          ...request.session,
          sessionId: 'rejected-session'
        },
        workflow: {
          ...workflow,
          entries: workflow.entries.map((entry) =>
            entry.textKey === request.textKey ? { ...entry, value: 'Server fallback.' } : entry
          )
        }
      };
    });
    bridge.loadTextWorkflow = loadTextWorkflow;
    bridge.updateTextEntry = updateTextEntry;
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
      expect(searchInput).toHaveValue('Pikachu');
    });

    const textarea = screen.getByLabelText('Text value');
    expect(textarea).toHaveValue(variableValue);

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    await waitFor(() => expect(textarea).toBeEnabled());

    const draftValue = `${variableValue} updated`;
    await user.clear(textarea);
    await user.paste(draftValue);
    const stageButton = screen.getByRole('button', { name: 'Stage' });
    expect(stageButton).toBeEnabled();
    await user.click(stageButton);

    await waitFor(() => {
      expect(updateTextEntry).toHaveBeenCalledWith(
        expect.objectContaining({
          textKey: variableTextKey,
          value: draftValue
        })
      );
      expect(stageButton).toBeEnabled();
    });

    const state = useWorkbenchStore.getState();
    expect(textarea).toHaveValue(draftValue);
    expect(searchInput).toHaveValue('Pikachu');
    expect(state.editSession?.sessionId).toBe('session-1');
    expect(
      state.textWorkflow?.entries.find((entry) => entry.textKey === variableTextKey)?.value
    ).toBe(variableValue);
    expect(state.editValidationDiagnostics).toEqual([
      expect.objectContaining({ message: 'Rejected text edit.', severity: 'error' })
    ]);
  });

  it('preserves filtered Gift drafts, raw IV sentinels, and workflow state after a rejected batch', async () => {
    const bridge = createMockProjectBridge({}, true);
    const originalLoadGiftPokemonWorkflow = bridge.loadGiftPokemonWorkflow.bind(bridge);
    const originalLoadPokemonWorkflow = bridge.loadPokemonWorkflow.bind(bridge);
    let loadedGiftWorkflow: Awaited<
      ReturnType<ProjectBridge['loadGiftPokemonWorkflow']>
    >['workflow'] | null = null;
    bridge.loadPokemonWorkflow = vi.fn(async (request) => {
      const response = await originalLoadPokemonWorkflow(request);
      const sourcePokemon = response.workflow.pokemon[0]!;
      return {
        workflow: {
          ...response.workflow,
          pokemon: [
            ...response.workflow.pokemon,
            {
              ...sourcePokemon,
              abilities: {
                ...sourcePokemon.abilities,
                ability2: 0,
                ability2Label: 'None',
                hiddenAbility: 0,
                hiddenAbilityLabel: 'None'
              },
              genderRatio: 0,
              genderRatioLabel: 'Male only',
              name: 'Grookey',
              personal: {
                ...sourcePokemon.personal,
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
    const loadGiftPokemonWorkflow = vi.fn(async (request) => {
      const response = await originalLoadGiftPokemonWorkflow(request);
      const sourceGift = response.workflow.gifts[0]!;
      const customIvGift = {
        ...sourceGift,
        ability: 3,
        abilityLabel: 'Hidden Ability',
        flawlessIvCount: null,
        gender: 2,
        genderLabel: 'Female',
        ivSummary: 'Custom IVs'
      };
      loadedGiftWorkflow = {
        ...response.workflow,
        gifts: [
          customIvGift,
          {
            ...customIvGift,
            giftIndex: 1,
            label: 'Gift 002: Grookey Lv. 5',
            species: 'Grookey',
            speciesId: 810
          }
        ],
        stats: {
          ...response.workflow.stats,
          totalGiftCount: 2
        }
      };

      return { workflow: loadedGiftWorkflow };
    });
    const updateGiftPokemonFields = vi.fn(async (request) => {
      const workflow = loadedGiftWorkflow;
      if (!request.session || !workflow) {
        throw new Error('Gift workflow and edit session must be loaded before staging.');
      }

      return {
        diagnostics: [
          {
            domain: 'workflow.giftPokemon',
            field: 'level',
            message: 'Rejected gift edit.',
            severity: 'error' as const
          }
        ],
        session: {
          ...request.session,
          sessionId: 'rejected-session'
        },
        workflow: {
          ...workflow,
          gifts: workflow.gifts.map((gift) =>
            gift.giftIndex === 0 ? { ...gift, level: 99 } : gift
          )
        }
      };
    });
    bridge.loadGiftPokemonWorkflow = loadGiftPokemonWorkflow;
    bridge.updateGiftPokemonFields = updateGiftPokemonFields;
    const user = await openValidatedSwordProject(bridge);

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

    const searchInput = screen.getByRole('searchbox', { name: 'Search gift Pokemon' });
    await user.type(searchInput, 'Grookey');
    expect(screen.getByText('Gift #2 | Lv. 5')).toBeInTheDocument();
    await user.clear(searchInput);
    await user.type(searchInput, 'Bulbasaur');
    expect(screen.getByText('Gift #1 | Lv. 5')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Edit' }));
    const speciesInput = screen.getByLabelText('Species');
    const abilityInput = screen.getByLabelText('Ability slot');
    const genderInput = screen.getByLabelText('Gender');
    expect(abilityInput).toHaveValue('Hidden Ability - Chlorophyll');
    expect(genderInput).toHaveValue('Female');

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
    const attackIvInput = screen.getByLabelText('Attack IV');
    await waitFor(() => expect(attackIvInput).toBeEnabled());
    expect(attackIvInput).toHaveValue(-1);

    const stageButton = screen.getByRole('button', { name: 'Stage' });
    await user.clear(attackIvInput);
    await user.type(attackIvInput, '32');
    expect(screen.getByText('Maximum value is 31.')).toBeInTheDocument();
    expect(stageButton).toBeDisabled();

    await user.clear(attackIvInput);
    await user.type(attackIvInput, '-1');
    const levelInput = screen.getByLabelText('Level');
    await user.clear(levelInput);
    await user.type(levelInput, '6');
    expect(stageButton).toBeEnabled();
    await user.click(stageButton);

    await waitFor(() => {
      expect(updateGiftPokemonFields).toHaveBeenCalledWith(
        expect.objectContaining({
          updates: [{ field: 'level', giftIndex: 0, value: '6' }]
        })
      );
      expect(stageButton).toBeEnabled();
    });

    const state = useWorkbenchStore.getState();
    expect(levelInput).toHaveValue(6);
    expect(attackIvInput).toHaveValue(-1);
    expect(searchInput).toHaveValue('Bulbasaur');
    expect(state.editSession?.sessionId).toBe('session-1');
    expect(state.giftPokemonWorkflow?.gifts[0]?.level).toBe(5);
    expect(state.editValidationDiagnostics).toEqual([
      expect.objectContaining({ message: 'Rejected gift edit.', severity: 'error' })
    ]);
  });
});
