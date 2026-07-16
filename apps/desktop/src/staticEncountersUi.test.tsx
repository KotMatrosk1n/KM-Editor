/* SPDX-License-Identifier: GPL-3.0-only */

import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { App } from './App';
import { createMockProjectBridge } from './testSupport/appTestFixtures';
import { useWorkbenchStore } from './workbenchStore';

describe('Static Encounters UI', () => {
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
        selectedGame: 'sword'
      },
      editSession: null,
      staticEncountersWorkflow: null
    });
  });

  it('keeps static encounter selection, drafts, validation, and family semantics safe', async () => {
    const user = userEvent.setup();
    const bridge = createMockProjectBridge({}, true);
    const staticEncountersResponse = await bridge.loadStaticEncountersWorkflow({
      paths: {
        baseExeFsPath: 'base-exefs',
        baseRomFsPath: 'base-romfs',
        outputRootPath: 'output',
        saveFilePath: null,
        selectedGame: 'sword'
      }
    });
    const firstEncounter = staticEncountersResponse.workflow.encounters[0]!;
    const createWorkflow = (editorFamily: 'swsh' | 'sv' | 'za') => ({
      ...staticEncountersResponse.workflow,
      editorFamily,
      encounters: [
        { ...firstEncounter, editorFamily },
        {
          ...firstEncounter,
          editorFamily,
          encounterId: 'second-static-encounter',
          encounterIndex: 1,
          encounterScenarioLabel:
            editorFamily === 'za'
              ? 'Full Course of Battles: High Rolling Battle 5'
              : firstEncounter.encounterScenarioLabel,
          form: 0,
          label: 'Static 001: Bulbasaur Lv. 50',
          scenarioDetails: editorFamily === 'za' ? 'Side Mission 73' : null,
          species: 'Bulbasaur',
          speciesId: 1
        }
      ],
      stats: {
        ...staticEncountersResponse.workflow.stats,
        totalEncounterCount: 2
      }
    });

    render(<App bridge={bridge} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));

    const navigation = await screen.findByRole('navigation', { name: 'Workspace' });
    await user.click(
      within(navigation).getByRole('button', { name: 'Encounters & Pokemon Sources' })
    );
    await user.click(within(navigation).getByRole('button', { name: 'Static Encounters' }));
    await screen.findByRole('table', { name: 'Static Encounters' });

    const cases = [
      {
        editorFamily: 'swsh' as const,
        expectedColumnCount: '6',
        expectedHeaders: ['Index', 'Species', 'Level', 'Scenario', 'IVs', 'Source'],
        expectedSelectedIndex: 1
      },
      {
        editorFamily: 'sv' as const,
        expectedColumnCount: '6',
        expectedHeaders: ['Index', 'Species', 'Level', 'Category', 'IVs', 'Source'],
        expectedSelectedIndex: 2
      },
      {
        editorFamily: 'za' as const,
        expectedColumnCount: '4',
        expectedHeaders: ['Index', 'Species', 'Level', 'Scenario'],
        expectedSelectedIndex: 2
      }
    ];

    for (const testCase of cases) {
      act(() => {
        useWorkbenchStore.setState({ selectedStaticEncounterIndex: null });
        useWorkbenchStore
          .getState()
          .setStaticEncountersWorkflow(createWorkflow(testCase.editorFamily));
      });

      const table = screen.getByRole('table', { name: 'Static Encounters' });
      expect(table).toHaveAttribute('aria-colcount', testCase.expectedColumnCount);
      expect(
        within(table)
          .getAllByRole('columnheader')
          .map((header) => header.textContent)
      ).toEqual(testCase.expectedHeaders);
      expect(within(table).queryByRole('columnheader', { name: 'Encounter' })).toBeNull();

      const bulbasaurRow = within(table).getByRole('row', { name: /Bulbasaur/ });
      expect(within(bulbasaurRow).getByRole('cell', { name: 'Bulbasaur' })).toBeInTheDocument();
      await user.click(bulbasaurRow);

      expect(bulbasaurRow).toHaveClass('trainers-row-selected');
      const inspector = screen.getByRole('complementary', {
        name: 'Selected static encounter provenance'
      });
      expect(
        within(inspector).getByText(`Static #${testCase.expectedSelectedIndex} | Lv. 50`)
      ).toBeInTheDocument();
      expect(within(inspector).getByText('Static 001: Bulbasaur Lv. 50')).toBeInTheDocument();
      if (testCase.editorFamily === 'swsh') {
        expect(within(inspector).getByLabelText('Level')).toBeInTheDocument();
      } else {
        expect(within(inspector).queryByLabelText('Level')).toBeNull();
      }
      if (testCase.editorFamily === 'za') {
        expect(within(inspector).getByText('Side Mission 73')).toBeInTheDocument();
        expect(
          within(inspector).getByText('Full Course of Battles: High Rolling Battle 5')
        ).toBeInTheDocument();
      }
    }

    const search = screen.getByLabelText('Search static encounters');
    await user.type(search, 'Side Mission 73');
    expect(
      within(screen.getByRole('table', { name: 'Static Encounters' })).getByRole('row', {
        name: /Bulbasaur/
      })
    ).toBeInTheDocument();

    await user.clear(search);
    await user.type(search, 'no matching static encounter');
    expect(
      within(
        screen.getByRole('complementary', {
          name: 'Selected static encounter provenance'
        })
      ).getByText('No static encounter selected.')
    ).toBeInTheDocument();

    act(() => {
      useWorkbenchStore.getState().setStaticEncountersWorkflow(createWorkflow('za'));
    });
    expect(search).toHaveValue('no matching static encounter');

    await user.clear(search);
    const editSession = {
      hasPendingChanges: false,
      pendingEdits: [],
      sessionId: 'static-ui-session'
    };
    const moveFieldsWorkflow = createWorkflow('swsh');
    const move0Field = moveFieldsWorkflow.editableFields.find(
      (field) => field.field === 'move0Id'
    )!;
    const moveOptions = [
      { label: '000 None', value: 0 },
      { label: '001 Scratch', value: 1 },
      { label: '002 Growl', value: 2 },
      { label: '003 Tail Whip', value: 3 },
      { label: '004 Leer', value: 4 }
    ];
    moveFieldsWorkflow.editableFields = [
      ...moveFieldsWorkflow.editableFields.filter((field) => field.field !== 'move0Id'),
      ...(['move0Id', 'move1Id', 'move2Id', 'move3Id'] as const).map((field, index) => ({
        ...move0Field,
        field,
        label: `Move ${index + 1}`,
        options: moveOptions
      }))
    ];
    moveFieldsWorkflow.encounters = moveFieldsWorkflow.encounters.map((encounter) => ({
      ...encounter,
      moves: [
        { move: 'Scratch', moveId: 1, slot: 0 },
        { move: 'Growl', moveId: 2, slot: 1 },
        { move: 'Tail Whip', moveId: 3, slot: 2 },
        { move: 'Leer', moveId: 4, slot: 3 }
      ]
    }));
    act(() => {
      useWorkbenchStore.setState({
        editSession,
        selectedStaticEncounterIndex: 0
      });
      useWorkbenchStore.getState().setStaticEncountersWorkflow(moveFieldsWorkflow);
    });
    const moveInspector = screen.getByRole('complementary', {
      name: 'Selected static encounter provenance'
    });
    expect(within(moveInspector).getByLabelText('Move 1')).toHaveValue('001 Scratch');
    expect(within(moveInspector).getByLabelText('Move 2')).toHaveValue('002 Growl');
    expect(within(moveInspector).getByLabelText('Move 3')).toHaveValue('003 Tail Whip');
    expect(within(moveInspector).getByLabelText('Move 4')).toHaveValue('004 Leer');

    const mixedIvWorkflow = createWorkflow('swsh');
    mixedIvWorkflow.encounters = mixedIvWorkflow.encounters.map((encounter) =>
      encounter.encounterIndex === 1
        ? {
            ...encounter,
            flawlessIvCount: null,
            ivs: {
              attack: -1,
              defense: 31,
              hp: -1,
              specialAttack: -1,
              specialDefense: 30,
              speed: 29
            },
            ivSummary: 'HP Random / Atk Random / Def 31 / SpA Random / SpD 30 / Spe 29',
            shinyLock: 2,
            shinyLockLabel: 'Never Shiny'
          }
        : encounter
    );
    const readOnlyWorkflow = {
      ...mixedIvWorkflow,
      editableFields: mixedIvWorkflow.editableFields.map((field) =>
        field.field === 'level'
          ? {
              ...field,
              description: 'Level is locked for this encounter source.',
              isReadOnly: true
            }
          : field
      )
    };

    act(() => {
      useWorkbenchStore.setState({
        editSession,
        selectedStaticEncounterIndex: 1
      });
      useWorkbenchStore.getState().setStaticEncountersWorkflow(readOnlyWorkflow);
    });
    const inspector = screen.getByRole('complementary', {
      name: 'Selected static encounter provenance'
    });
    expect(within(inspector).getByLabelText('Level')).toBeDisabled();
    expect(
      within(inspector).getByText('Level is locked for this encounter source.')
    ).toBeInTheDocument();

    const batchUpdateSpy = vi.spyOn(bridge, 'updateStaticEncounterFields');
    act(() => {
      useWorkbenchStore.getState().setStaticEncountersWorkflow(mixedIvWorkflow);
    });
    const levelInput = within(inspector).getByLabelText('Level');
    await user.clear(levelInput);
    await user.type(levelInput, '51');

    const refreshedWorkflow = {
      ...mixedIvWorkflow,
      encounters: mixedIvWorkflow.encounters.map((encounter) =>
        encounter.encounterIndex === 1
          ? {
              ...encounter,
              level: 55,
              shinyLock: 0,
              shinyLockLabel: 'Random'
            }
          : encounter
      )
    };
    act(() => {
      useWorkbenchStore.getState().setStaticEncountersWorkflow(refreshedWorkflow);
    });
    expect(within(inspector).getByLabelText('Level')).toHaveValue(51);
    expect(within(inspector).getByLabelText('Shiny lock')).toHaveValue('Random');

    const sourceSwapWorkflow = {
      ...refreshedWorkflow,
      encounters: refreshedWorkflow.encounters.map((encounter) =>
        encounter.encounterIndex === 1
          ? { ...encounter, encounterId: 'replacement-at-the-same-index' }
          : encounter
      )
    };
    act(() => {
      useWorkbenchStore.getState().setStaticEncountersWorkflow(sourceSwapWorkflow);
    });
    expect(within(inspector).getByLabelText('Level')).toHaveValue(55);
    act(() => {
      useWorkbenchStore.getState().setStaticEncountersWorkflow(refreshedWorkflow);
    });
    expect(within(inspector).getByLabelText('Level')).toHaveValue(51);

    await user.click(within(inspector).getByRole('button', { name: 'Stage' }));
    await vi.waitFor(() => expect(batchUpdateSpy).toHaveBeenCalledTimes(1));
    expect(batchUpdateSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        updates: [
          {
            encounterId: 'second-static-encounter',
            encounterIndex: 1,
            field: 'level',
            value: '51'
          }
        ]
      })
    );

    const sentinelWorkflow = createWorkflow('swsh');
    sentinelWorkflow.encounters = sentinelWorkflow.encounters.map((encounter) => ({
      ...encounter,
      flawlessIvCount: null,
      ivs: {
        attack: -1,
        defense: -1,
        hp: 31,
        specialAttack: -1,
        specialDefense: -1,
        speed: -1
      }
    }));
    act(() => {
      useWorkbenchStore.setState({ selectedStaticEncounterIndex: 0 });
      useWorkbenchStore.getState().setStaticEncountersWorkflow(sentinelWorkflow);
    });
    const hpIvInput = within(inspector).getByLabelText('HP IV');
    const attackIvInput = within(inspector).getByLabelText('Attack IV');
    await user.clear(hpIvInput);
    await user.type(hpIvInput, '-4');
    expect(within(inspector).queryByText(/Use -1 for a random IV/)).toBeNull();
    expect(within(inspector).queryByText('HP IV -4 requires every other IV to be -1.')).toBeNull();
    await user.clear(attackIvInput);
    await user.type(attackIvInput, '0');
    expect(
      within(inspector).getAllByText('HP IV -4 requires every other IV to be -1.')
    ).toHaveLength(2);
    await user.clear(attackIvInput);
    await user.type(attackIvInput, '-1');
    await user.clear(hpIvInput);
    await user.type(hpIvInput, '-2');
    expect(
      within(inspector).getByText(
        'Use -1 for a random IV, -4 for the 3 perfect IV preset, or 0 through 31.'
      )
    ).toBeInTheDocument();
    await user.clear(hpIvInput);
    await user.type(hpIvInput, '31');

    const evFieldLabels = [
      ['evHp', 'HP EV'],
      ['evAttack', 'Attack EV'],
      ['evDefense', 'Defense EV'],
      ['evSpecialAttack', 'Sp. Atk EV'],
      ['evSpecialDefense', 'Sp. Def EV'],
      ['evSpeed', 'Speed EV']
    ] as const;
    const evWorkflow = createWorkflow('swsh');
    evWorkflow.editableFields = [
      ...evWorkflow.editableFields,
      ...evFieldLabels.map(([field, label]) => ({
        description: '',
        field,
        group: 'Stats - EVs',
        isReadOnly: false,
        label,
        maximumValue: 252,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      }))
    ];
    evWorkflow.encounters = evWorkflow.encounters.map((encounter) => ({
      ...encounter,
      evs: {
        attack: 252,
        defense: 0,
        hp: 252,
        specialAttack: 0,
        specialDefense: 0,
        speed: 0
      }
    }));
    act(() => {
      useWorkbenchStore.setState({ selectedStaticEncounterIndex: 0 });
      useWorkbenchStore.getState().setStaticEncountersWorkflow(evWorkflow);
    });
    const speedEvInput = within(inspector).getByLabelText('Speed EV');
    await user.clear(speedEvInput);
    await user.type(speedEvInput, '7');
    expect(speedEvInput).toHaveValue(7);
    expect(
      within(inspector).getByText('EV total is 511. Maximum total is 510.')
    ).toBeInTheDocument();
    expect(within(inspector).getByRole('button', { name: 'Stage' })).toBeDisabled();
    await user.clear(speedEvInput);
    await user.type(speedEvInput, '0');

    const fallbackSession = {
      hasPendingChanges: false,
      pendingEdits: [],
      sessionId: 'fallback-session'
    };
    const fallbackWorkflow = createWorkflow('sv');
    fallbackWorkflow.encounters = fallbackWorkflow.encounters.map((encounter) => ({
      ...encounter,
      supportedFields: ['level', 'dynamaxLevel']
    }));
    const partialSession = {
      hasPendingChanges: true,
      pendingEdits: [
        {
          domain: 'workflow.staticEncounters',
          field: 'level',
          newValue: '51',
          recordId: 'static:0:0x0102030405060708',
          sources: [],
          summary: 'Partial fallback update.'
        }
      ],
      sessionId: 'fallback-partial-session'
    };
    const partialWorkflow = {
      ...fallbackWorkflow,
      encounters: fallbackWorkflow.encounters.map((encounter) =>
        encounter.encounterIndex === 0 ? { ...encounter, level: 51 } : encounter
      )
    };
    const fallbackUpdateSpy = vi
      .spyOn(bridge, 'updateStaticEncounterField')
      .mockResolvedValueOnce({
        diagnostics: [],
        session: partialSession,
        workflow: partialWorkflow
      })
      .mockResolvedValueOnce({
        diagnostics: [
          {
            domain: 'workflow.staticEncounters',
            field: 'dynamaxLevel',
            message: 'Fallback update rejected.',
            severity: 'error'
          }
        ],
        session: partialSession,
        workflow: partialWorkflow
      });
    act(() => {
      useWorkbenchStore.setState({
        editSession: fallbackSession,
        selectedStaticEncounterIndex: 0
      });
      useWorkbenchStore.getState().setStaticEncountersWorkflow(fallbackWorkflow);
    });
    const fallbackLevelInput = within(inspector).getByLabelText('Level');
    const fallbackDynamaxInput = within(inspector).getByLabelText('Dynamax level');
    await user.clear(fallbackLevelInput);
    await user.type(fallbackLevelInput, '51');
    await user.clear(fallbackDynamaxInput);
    await user.type(fallbackDynamaxInput, '9');
    await user.click(within(inspector).getByRole('button', { name: 'Stage' }));
    await vi.waitFor(() => expect(fallbackUpdateSpy).toHaveBeenCalledTimes(2));
    expect(useWorkbenchStore.getState().editSession).toEqual(fallbackSession);
    expect(
      useWorkbenchStore.getState().staticEncountersWorkflow?.encounters[0]?.level
    ).toBe(50);
    expect(fallbackLevelInput).toHaveValue(51);
    expect(fallbackDynamaxInput).toHaveValue(9);
    fallbackUpdateSpy.mockRestore();

    const zaShinyWorkflow = createWorkflow('za');
    zaShinyWorkflow.editableFields = zaShinyWorkflow.editableFields.map((field) =>
      field.field === 'shinyLock'
        ? {
            ...field,
            maximumValue: 0x3fffffff,
            options: [
              { label: 'Never shiny', value: 0 },
              { label: 'Forced shiny', value: 1 },
              { label: 'Default shiny roll', value: 0x3fffffff }
            ]
          }
        : field
    );
    zaShinyWorkflow.encounters = zaShinyWorkflow.encounters.map((encounter) =>
      encounter.encounterIndex === 0
        ? {
            ...encounter,
            shinyLock: 0x3fffffff,
            shinyLockLabel: 'Default shiny roll',
            supportedFields: ['shinyLock']
          }
        : {
            ...encounter,
            shinyLock: 0,
            shinyLockLabel: 'Never shiny',
            supportedFields: ['shinyLock']
          }
    );
    const singleUpdateSpy = vi.spyOn(bridge, 'updateStaticEncounterField');
    act(() => {
      useWorkbenchStore.getState().setStaticEncountersWorkflow(zaShinyWorkflow);
    });
    await user.click(screen.getByRole('button', { name: 'Remove Static Shiny Lock' }));
    const confirmation = screen.getByRole('dialog', { name: 'Remove Static Shiny Lock?' });
    expect(within(confirmation).getByText(/1 static encounter/)).toBeInTheDocument();
    expect(within(confirmation).getAllByText(/Default shiny roll/)).toHaveLength(2);
    await user.click(
      within(confirmation).getByRole('button', { name: 'Remove Static Shiny Lock' })
    );
    await vi.waitFor(() => expect(singleUpdateSpy).toHaveBeenCalledTimes(1));
    expect(singleUpdateSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        encounterId: 'second-static-encounter',
        encounterIndex: 1,
        field: 'shinyLock',
        value: '1073741823'
      })
    );
  });
});
