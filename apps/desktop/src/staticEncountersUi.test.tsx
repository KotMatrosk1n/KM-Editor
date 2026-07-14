/* SPDX-License-Identifier: GPL-3.0-only */

import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
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

  it('uses index and species instead of a duplicate encounter column for every game family', async () => {
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
  });
});
