/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { App } from './App';
import {
  type EditSession,
  type EncountersWorkflow,
  type EncounterSlotRecord,
  type EncounterTableRecord
} from './bridge/contracts';
import { type ProjectBridge } from './bridge/projectBridge';
import { createMockProjectBridge } from './testSupport/appTestFixtures';
import { useWorkbenchStore } from './workbenchStore';

const normalTableId = 'sword:symbol:0:AAA:0';
const overcastTableId = 'sword:symbol:0:AAA:1';
const rainingTableId = 'sword:symbol:0:AAA:2';
const hiddenTableId = 'sword:hidden:0:AAA:0';

function makeSlot(
  slot: number,
  speciesId: number,
  species: string,
  levelMin: number,
  levelMax: number,
  form = 0
): EncounterSlotRecord {
  return {
    form,
    levelMax,
    levelMin,
    slot,
    species,
    speciesId,
    timeOfDay: null,
    weather: 'Normal',
    weight: slot === 11 ? 0 : 10
  };
}

function makeTable(
  tableId: string,
  location: string,
  area: string,
  encounterType: string,
  slots: EncounterSlotRecord[]
): EncounterTableRecord {
  return {
    archiveMember: `encount_${area.toLocaleLowerCase()}_k.bin`,
    area,
    encounterType,
    gameVersion: 'Sword',
    location,
    provenance: {
      fileState: 'baseOnly',
      sourceFile: 'romfs/bin/archive/field/resident/data_table.gfpak',
      sourceLayer: 'base'
    },
    slots,
    tableId
  };
}

function createSwShEncountersWorkflow(): EncountersWorkflow {
  const normalSlots = [
    makeSlot(1, 1, 'Bulbasaur', 5, 10, 2),
    makeSlot(2, 4, 'Charmander', 5, 10),
    makeSlot(3, 7, 'Squirtle', 5, 10),
    makeSlot(4, 10, 'Caterpie', 5, 10),
    makeSlot(5, 13, 'Weedle', 5, 10),
    makeSlot(6, 16, 'Pidgey', 5, 10),
    makeSlot(7, 19, 'Rattata', 5, 10),
    makeSlot(8, 21, 'Spearow', 5, 10),
    makeSlot(9, 23, 'Ekans', 5, 10),
    makeSlot(10, 25, 'Pikachu', 5, 10),
    makeSlot(11, 27, 'Sandshrew', 5, 10)
  ];
  const tables = [
    makeTable(normalTableId, 'Rolling Fields', 'Symbol', 'Normal', normalSlots),
    makeTable(overcastTableId, 'Rolling Fields', 'Symbol', 'Overcast', [
      makeSlot(1, 43, 'Oddish', 20, 25)
    ]),
    makeTable(rainingTableId, 'Rolling Fields', 'Symbol', 'Raining', [
      makeSlot(1, 60, 'Poliwag', 1, 4)
    ]),
    makeTable(hiddenTableId, 'Rolling Fields', 'Hidden', 'Normal', [
      makeSlot(1, 133, 'Eevee', 5, 10)
    ]),
    makeTable('sword:symbol:0:BBB:0', 'Lake Axewell', 'Symbol', 'Normal', [
      makeSlot(1, 129, 'Magikarp', 8, 12)
    ])
  ];

  return {
    diagnostics: [],
    editableFields: [
      {
        field: 'speciesId',
        label: 'Species ID',
        maximumValue: 65535,
        minimumValue: 0,
        options: [],
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
        field: 'probability',
        label: 'Probability',
        maximumValue: 100,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'levelMin',
        label: 'Min Level',
        maximumValue: 100,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      },
      {
        field: 'levelMax',
        label: 'Max Level',
        maximumValue: 100,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      }
    ],
    stats: {
      sourceFileCount: 2,
      totalSlotCount: tables.reduce((total, table) => total + table.slots.length, 0),
      totalTableCount: tables.length
    },
    summary: {
      availability: 'available',
      description: 'Edit Sword and Shield wild encounter tables.',
      diagnostics: [],
      id: 'encounters',
      label: 'Wild Encounters'
    },
    tables
  };
}

function createPendingEncounterEdit(recordId: string, field = 'levelMin', value = '7') {
  return {
    domain: 'workflow.encounters',
    field,
    newValue: value,
    recordId,
    sources: [
      {
        layer: 'base' as const,
        relativePath: 'romfs/bin/archive/field/resident/data_table.gfpak'
      }
    ],
    summary: `Set ${recordId} ${field} to ${value}.`
  };
}

function createEditSession(recordIds: string[] = []): EditSession {
  return {
    hasPendingChanges: recordIds.length > 0,
    pendingEdits: recordIds.map((recordId) => createPendingEncounterEdit(recordId)),
    sessionId: 'swsh-encounters-session'
  };
}

async function openSwShWildEncounters(
  workflow: EncountersWorkflow,
  bridgeOverrides: Partial<ProjectBridge> = {}
) {
  const user = userEvent.setup();

  render(
    <App
      bridge={createMockProjectBridge(
        {
          loadEncountersWorkflow: () => Promise.resolve({ workflow }),
          ...bridgeOverrides
        },
        true
      )}
    />
  );

  await user.click(screen.getByRole('button', { name: 'Pokemon Sword' }));
  await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
  await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
  await user.type(screen.getByLabelText('Output Root'), 'output');
  await user.click(screen.getByRole('button', { name: 'Validate Paths' }));
  await user.click(screen.getByRole('button', { name: 'Encounters & Pokemon Sources' }));
  await user.click(screen.getByRole('button', { name: 'Wild Encounters' }));

  await screen.findByRole('table', { name: 'Encounter tables' });
  return user;
}

describe('Sword and Shield wild encounters UI', () => {
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
      editSession: null,
      encounterSearchText: '',
      encountersWorkflow: null,
      selectedEncounterTableId: null
    });
  });

  it('keeps table browsing, condition edits, pending state, and batch safety aligned', async () => {
    const workflow = createSwShEncountersWorkflow();
    const seededSession = createEditSession([
      `${overcastTableId}#1`,
      `${hiddenTableId}#1`
    ]);
    const updateEncounterSlotFields = vi.fn(
      async (request: Parameters<ProjectBridge['updateEncounterSlotFields']>[0]) => {
        const isConditionBatch = new Set(request.updates.map((update) => update.tableId)).size > 1;
        if (isConditionBatch) {
          return {
            diagnostics: [
              {
                domain: 'workflow.encounters',
                field: 'levelMin',
                message: 'Condition update rejected.',
                severity: 'error' as const
              }
            ],
            session: request.session ?? createEditSession(),
            workflow
          };
        }

        return {
          diagnostics: [],
          session: {
            hasPendingChanges: true,
            pendingEdits: [
              ...(request.session?.pendingEdits ?? []),
              ...request.updates.map((update) =>
                createPendingEncounterEdit(
                  `${update.tableId}#${update.slot}`,
                  update.field,
                  update.value
                )
              )
            ],
            sessionId: 'swsh-encounters-session'
          },
          workflow
        };
      }
    );
    const user = await openSwShWildEncounters(workflow, {
      startEditSession: () => Promise.resolve({ session: seededSession }),
      updateEncounterSlotFields
    });
    const slotList = screen.getByLabelText('Encounter slot list');

    expect(within(slotList).getAllByRole('button')).toHaveLength(11);
    expect(within(slotList).getByText('#11')).toBeInTheDocument();
    expect(within(slotList).getByText('Sandshrew')).toBeInTheDocument();

    const search = screen.getByLabelText('Search encounters');
    await user.type(search, 'Magikarp');

    const inspector = screen.getByRole('complementary', {
      name: 'Selected encounter provenance'
    });
    const locationDetail = within(inspector).getByText('Location', { selector: 'dt' }).parentElement;
    expect(locationDetail).not.toBeNull();
    expect(within(locationDetail!).getByText('Lake Axewell')).toBeInTheDocument();
    const speciesDetail = within(inspector).getByText('Species', { selector: 'dt' }).parentElement;
    expect(speciesDetail).not.toBeNull();
    expect(within(speciesDetail!).getByText('Magikarp')).toBeInTheDocument();

    await user.clear(search);
    await user.type(search, 'no encounter matches this');

    expect(within(inspector).getByText('No encounter table selected.')).toBeInTheDocument();
    expect(
      within(screen.getByRole('table', { name: 'Encounter tables' })).getAllByRole('row')
    ).toHaveLength(1);

    await user.clear(search);
    await waitFor(() =>
      expect(within(screen.getByLabelText('Encounter slot list')).getByText('#11')).toBeInTheDocument()
    );

    await user.click(screen.getByRole('button', { name: 'Edit' }));

    expect(screen.getByRole('tab', { name: 'Hidden (pending changes)' })).toHaveClass(
      'condition-tab-button-pending'
    );
    expect(screen.getByRole('tab', { name: 'Overcast (pending changes)' })).toHaveClass(
      'condition-tab-button-pending'
    );

    const minLevel = screen.getByLabelText('Min Level');
    await user.clear(minLevel);
    await user.type(minLevel, '7');

    const editableSlotList = screen.getByLabelText('Encounter slot list');
    await user.click(within(editableSlotList).getByText('#2').closest('button')!);
    expect(screen.getByLabelText('Min Level')).toHaveValue(7);
    expect(screen.getByLabelText('Max Level')).toHaveValue(10);

    await user.click(within(editableSlotList).getByText('#1').closest('button')!);
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(updateEncounterSlotFields).toHaveBeenCalledTimes(1));
    expect(updateEncounterSlotFields.mock.calls[0]![0].updates).toEqual([
      { field: 'levelMin', slot: 1, tableId: normalTableId, value: '7' },
      { field: 'levelMax', slot: 1, tableId: normalTableId, value: '10' }
    ]);

    const species = screen.getByLabelText('Species ID');
    await user.clear(species);
    await user.type(species, '133');
    expect(screen.getByLabelText('Form')).toHaveDisplayValue('Base');
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(updateEncounterSlotFields).toHaveBeenCalledTimes(2));
    expect(updateEncounterSlotFields.mock.calls[1]![0].updates).toEqual(
      expect.arrayContaining([
        { field: 'speciesId', slot: 1, tableId: normalTableId, value: '133' },
        { field: 'form', slot: 1, tableId: normalTableId, value: '0' }
      ])
    );

    const restoredSpecies = screen.getByLabelText('Species ID');
    await user.clear(restoredSpecies);
    await user.type(restoredSpecies, '133');
    expect(screen.getByLabelText('Form')).toHaveDisplayValue('Base');
    await user.clear(restoredSpecies);
    await user.type(restoredSpecies, '1');
    expect(screen.getByLabelText('Form')).toHaveValue('2');
    const probability = screen.getByLabelText('Probability');
    await user.clear(probability);
    await user.type(probability, '11');
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(updateEncounterSlotFields).toHaveBeenCalledTimes(3));
    expect(updateEncounterSlotFields.mock.calls[2]![0].updates).toEqual([
      { field: 'probability', slot: 1, tableId: normalTableId, value: '11' }
    ]);

    const batchMinLevel = screen.getByLabelText('Min Level');
    await user.clear(batchMinLevel);
    await user.type(batchMinLevel, '7');
    await user.click(screen.getByRole('button', { name: 'Apply to All Conditions' }));

    await waitFor(() => expect(updateEncounterSlotFields).toHaveBeenCalledTimes(4));
    expect(updateEncounterSlotFields.mock.calls[3]![0].updates).toEqual([
      { field: 'levelMin', slot: 1, tableId: normalTableId, value: '7' },
      { field: 'levelMax', slot: 1, tableId: normalTableId, value: '10' },
      { field: 'levelMin', slot: 1, tableId: overcastTableId, value: '7' },
      { field: 'levelMax', slot: 1, tableId: overcastTableId, value: '10' },
      { field: 'levelMax', slot: 1, tableId: rainingTableId, value: '10' },
      { field: 'levelMin', slot: 1, tableId: rainingTableId, value: '7' }
    ]);
    await waitFor(() =>
      expect(useWorkbenchStore.getState().editValidationDiagnostics).toEqual([
        expect.objectContaining({ message: 'Condition update rejected.', severity: 'error' })
      ])
    );
    expect(screen.getByLabelText('Min Level')).toHaveValue(7);
    expect(screen.getByLabelText('Max Level')).toHaveValue(10);

    const retainedSlotList = screen.getByLabelText('Encounter slot list');
    await user.click(within(retainedSlotList).getByText('#2').closest('button')!);
    const slotProbability = screen.getByLabelText('Probability');
    await user.clear(slotProbability);
    await user.type(slotProbability, '12');
    await user.click(screen.getByRole('button', { name: 'Apply to Hidden' }));
    const areaCopyDialog = screen.getByRole('dialog', { name: 'Apply to Hidden?' });
    await user.click(within(areaCopyDialog).getByRole('button', { name: 'Apply to Hidden' }));

    await waitFor(() => expect(updateEncounterSlotFields).toHaveBeenCalledTimes(5));
    expect(new Set(updateEncounterSlotFields.mock.calls[4]![0].updates.map((update) => update.tableId)))
      .toEqual(new Set([hiddenTableId]));
    await user.click(screen.getByRole('tab', { name: /^Symbol/ }));
    expect(screen.getByLabelText('Min Level')).toHaveValue(7);
    const restoredSlotList = screen.getByLabelText('Encounter slot list');
    await user.click(within(restoredSlotList).getByText('#2').closest('button')!);
    expect(screen.getByLabelText('Probability')).toHaveValue(12);
  }, 30_000);
});
