/* SPDX-License-Identifier: GPL-3.0-only */

import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import {
  type ApiDiagnostic,
  type EditSession,
  type RaidBattlesWorkflow
} from './bridge/contracts';
import { type ProjectBridge } from './bridge/projectBridge';
import { languageStorageKey, LocalizationProvider } from './localization';
import { createMockProjectBridge } from './testSupport/appTestFixtures';
import { useWorkbenchStore } from './workbenchStore';

const projectPaths = {
  baseExeFsPath: 'base-exefs',
  baseRomFsPath: 'base-romfs',
  outputRootPath: 'output',
  pokemonLegendsZASupportFolderPath: '',
  saveFilePath: '',
  scarletVioletSupportFolderPath: '',
  selectedGame: 'sword' as const
};

const editSession: EditSession = {
  hasPendingChanges: false,
  pendingEdits: [],
  sessionId: 'raid-battles-ui-session'
};

async function createRaidBattlesHarness(
  mutateWorkflow?: (workflow: RaidBattlesWorkflow) => RaidBattlesWorkflow,
  createOverrides?: (workflow: RaidBattlesWorkflow) => Partial<ProjectBridge>
) {
  const fixtureBridge = createMockProjectBridge({}, true);
  const response = await fixtureBridge.loadRaidBattlesWorkflow({ paths: projectPaths });
  const workflow = mutateWorkflow?.(response.workflow) ?? response.workflow;
  const bridge = createMockProjectBridge(createOverrides?.(workflow) ?? {}, true);

  useWorkbenchStore.setState({
    activeSection: 'raidBattles',
    draftPaths: projectPaths,
    editSession,
    editValidationDiagnostics: [],
    raidBattleSearchText: '',
    raidBattlesWorkflow: workflow,
    selectedRaidBattleTableId: workflow.tables[0]?.tableId ?? null
  });

  return { bridge, workflow };
}

function renderRaidBattles(bridge: ProjectBridge) {
  return render(
    <LocalizationProvider>
      <App bridge={bridge} />
    </LocalizationProvider>
  );
}

describe('Raid Battles UI', () => {
  beforeEach(() => {
    window.localStorage.clear();
    useWorkbenchStore.setState(useWorkbenchStore.getInitialState(), true);
  });

  it('stages coordinated table fields through one atomic request and preserves search context', async () => {
    const user = userEvent.setup();
    const { bridge } = await createRaidBattlesHarness();
    const updateRaidBattleSlotFields = vi.spyOn(bridge, 'updateRaidBattleSlotFields');
    act(() => useWorkbenchStore.getState().setRaidBattleSearchText('Eevee'));
    renderRaidBattles(bridge);

    const flawlessIvsInput = await screen.findByLabelText('Guaranteed perfect IVs');
    await user.clear(flawlessIvsInput);
    await user.type(flawlessIvsInput, '6{Enter}');
    const probabilityInput = screen.getByLabelText('5-star probability');
    await user.clear(probabilityInput);
    await user.type(probabilityInput, '40');
    await user.selectOptions(screen.getByLabelText('Raid battle slot'), '2');
    const secondProbabilityInput = screen.getByLabelText('5-star probability');
    await user.clear(secondProbabilityInput);
    await user.type(secondProbabilityInput, '60');
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(updateRaidBattleSlotFields).toHaveBeenCalledTimes(1));
    expect(updateRaidBattleSlotFields.mock.calls[0]?.[0]).toMatchObject({
      updates: [
        {
          field: 'flawlessIvs',
          slot: 1,
          tableId: 'raid:0:AABBCCDD00112233',
          value: '6'
        },
        {
          field: 'star5Probability',
          slot: 1,
          tableId: 'raid:0:AABBCCDD00112233',
          value: '40'
        },
        {
          field: 'star5Probability',
          slot: 2,
          tableId: 'raid:0:AABBCCDD00112233',
          value: '60'
        }
      ]
    });
    expect(screen.getByRole('searchbox', { name: 'Search raid battles' })).toHaveValue('Eevee');
  });

  it('retains the whole draft when an atomic batch is rejected', async () => {
    const user = userEvent.setup();
    const rejection: ApiDiagnostic = {
      domain: 'workflow.raidBattles',
      field: 'star5Probability',
      message: 'Raid battle 5-star probability must be between 0 and 100.',
      severity: 'error'
    };
    const harness = await createRaidBattlesHarness();
    const updateRaidBattleSlotFields = vi.fn(
      async (request: Parameters<ProjectBridge['updateRaidBattleSlotFields']>[0]) => ({
        diagnostics: [rejection],
        session: request.session ?? editSession,
        workflow: harness.workflow
      })
    );
    harness.bridge.updateRaidBattleSlotFields = updateRaidBattleSlotFields;
    renderRaidBattles(harness.bridge);

    const probabilityInput = await screen.findByLabelText('5-star probability');
    await user.clear(probabilityInput);
    await user.type(probabilityInput, '80');
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(updateRaidBattleSlotFields).toHaveBeenCalledTimes(1));
    expect(probabilityInput).toHaveValue(80);
    expect(screen.getByRole('button', { name: 'Stage' })).toBeEnabled();
    expect(useWorkbenchStore.getState().editValidationDiagnostics).toEqual([rejection]);
    expect(useWorkbenchStore.getState().editSession).toEqual(editSession);
  });

  it('allows a species and target-specific form to be staged together', async () => {
    const user = userEvent.setup();
    const { bridge } = await createRaidBattlesHarness();
    const updateRaidBattleSlotFields = vi.spyOn(bridge, 'updateRaidBattleSlotFields');
    renderRaidBattles(bridge);

    await user.selectOptions(await screen.findByLabelText('Raid battle slot'), '2');
    const speciesInput = screen.getByLabelText('Species');
    await user.clear(speciesInput);
    await user.type(speciesInput, '133 Eevee');
    await user.tab();

    const formInput = screen.getByLabelText('Form');
    expect(formInput).toHaveAttribute('type', 'number');
    await user.clear(formInput);
    await user.type(formInput, '2');
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(updateRaidBattleSlotFields).toHaveBeenCalledTimes(1));
    expect(updateRaidBattleSlotFields.mock.calls[0]?.[0]).toMatchObject({
      updates: [
        {
          field: 'species',
          slot: 2,
          tableId: 'raid:0:AABBCCDD00112233',
          value: '133'
        },
        {
          field: 'form',
          slot: 2,
          tableId: 'raid:0:AABBCCDD00112233',
          value: '2'
        }
      ]
    });
  });

  it('selects only localized filtered rows and shows a localized no-match state', async () => {
    window.localStorage.setItem(languageStorageKey, 'es');
    const user = userEvent.setup();
    const { bridge } = await createRaidBattlesHarness((workflow) => {
      const firstTable = workflow.tables[0]!;
      return {
        ...workflow,
        stats: { ...workflow.stats, totalSlotCount: 4, totalTableCount: 2 },
        tables: [
          firstTable,
          {
            ...firstTable,
            displayName: 'Raid Battles',
            gameVersion: 'Shield',
            sourceTableHash: '0xBBCCDDEE11223344',
            tableId: 'raid:1:BBCCDDEE11223344',
            tableIndex: 1
          }
        ]
      };
    });
    renderRaidBattles(bridge);

    const search = await screen.findByRole('searchbox', { name: 'Buscar incursiones' });
    await user.type(search, 'Incursiones');

    const table = screen.getByRole('table', { name: 'Tablas de incursiones' });
    expect(within(table).queryByText('Sword - 0')).toBeNull();
    const matchingRow = within(table).getByRole('row', { name: /Incursiones/ });
    expect(matchingRow).toHaveAttribute('aria-selected', 'true');

    await user.clear(search);
    await user.type(search, 'sin coincidencias');
    expect(screen.getByRole('status')).toHaveTextContent(
      'No hay tablas de incursiones coincidentes.'
    );
    expect(screen.getByText('No hay tabla de incursiones seleccionada.')).toBeInTheDocument();
  });

  it('shows display names in the table list and renders every slot tab', async () => {
    const { bridge } = await createRaidBattlesHarness((workflow) => {
      const firstTable = workflow.tables[0]!;
      const firstSlot = firstTable.slots[0]!;
      const slots = Array.from({ length: 12 }, (_, index) => ({
        ...firstSlot,
        entryIndex: index,
        slot: index + 1
      }));
      return {
        ...workflow,
        stats: { ...workflow.stats, totalSlotCount: slots.length },
        tables: [{ ...firstTable, slots }]
      };
    });
    renderRaidBattles(bridge);

    const table = await screen.findByRole('table', { name: 'Raid battle tables' });
    expect(within(table).getByText('Sword - 0')).toBeInTheDocument();
    expect(within(table).queryByText('0xAABBCCDD00112233')).toBeNull();
    const slotList = screen.getByLabelText('Raid battle slot list');
    expect(within(slotList).getAllByRole('button')).toHaveLength(12);
  });
});
