/* SPDX-License-Identifier: GPL-3.0-only */

import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import {
  type ApiDiagnostic,
  type EditSession,
  type RaidRewardsWorkflow
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
  sessionId: 'raid-rewards-ui-session'
};

async function createRaidRewardsHarness(
  mutateWorkflow?: (workflow: RaidRewardsWorkflow) => RaidRewardsWorkflow,
  createOverrides?: (workflow: RaidRewardsWorkflow) => Partial<ProjectBridge>
) {
  const fixtureBridge = createMockProjectBridge({}, true);
  const response = await fixtureBridge.loadRaidRewardsWorkflow({ paths: projectPaths });
  const workflow = mutateWorkflow?.(response.workflow) ?? response.workflow;
  const bridge = createMockProjectBridge(createOverrides?.(workflow) ?? {}, true);

  useWorkbenchStore.setState({
    activeSection: 'raidRewards',
    draftPaths: projectPaths,
    editSession,
    editValidationDiagnostics: [],
    raidRewardSearchText: '',
    raidRewardsWorkflow: workflow,
    selectedRaidRewardTableId: workflow.tables[0]?.tableId ?? null
  });

  return { bridge, workflow };
}

async function createRaidBonusRewardsHarness(
  createOverrides?: (workflow: RaidRewardsWorkflow) => Partial<ProjectBridge>
) {
  const fixtureBridge = createMockProjectBridge({}, true);
  const response = await fixtureBridge.loadRaidBonusRewardsWorkflow({ paths: projectPaths });
  const workflow = response.workflow;
  const bridge = createMockProjectBridge(createOverrides?.(workflow) ?? {}, true);

  useWorkbenchStore.setState({
    activeSection: 'raidBonusRewards',
    draftPaths: projectPaths,
    editSession,
    editValidationDiagnostics: [],
    raidBonusRewardSearchText: '',
    raidBonusRewardsWorkflow: workflow,
    selectedRaidBonusRewardTableId: workflow.tables[0]?.tableId ?? null
  });

  return { bridge, workflow };
}

function renderRaidRewards(bridge: ProjectBridge) {
  return render(
    <LocalizationProvider>
      <App bridge={bridge} />
    </LocalizationProvider>
  );
}

describe('Raid Rewards UI', () => {
  beforeEach(() => {
    window.localStorage.clear();
    useWorkbenchStore.setState(useWorkbenchStore.getInitialState(), true);
  });

  it('stages all changed fields through one atomic request and preserves search context', async () => {
    const user = userEvent.setup();
    const { bridge } = await createRaidRewardsHarness();
    const updateRaidRewardFields = vi.spyOn(bridge, 'updateRaidRewardFields');
    act(() => useWorkbenchStore.getState().setRaidRewardSearchText('Candy'));
    renderRaidRewards(bridge);

    const itemInput = await screen.findByLabelText('Item ID');
    await user.clear(itemInput);
    await user.type(itemInput, '004{Enter}');
    const dropChanceInput = screen.getByLabelText('5-star drop chance');
    await user.clear(dropChanceInput);
    await user.type(dropChanceInput, '6');
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(updateRaidRewardFields).toHaveBeenCalledTimes(1));
    expect(updateRaidRewardFields.mock.calls[0]?.[0].updates).toEqual([
      {
        field: 'itemId',
        slot: 1,
        tableId: 'drop:0:AABBCCDD00112233',
        value: '4'
      },
      {
        field: 'star5Value',
        slot: 1,
        tableId: 'drop:0:AABBCCDD00112233',
        value: '6'
      }
    ]);
    expect(screen.getByRole('searchbox', { name: 'Search raid reward tables...' })).toHaveValue(
      'Candy'
    );
  });

  it('keeps an existing legacy item ID available only for its selected reward', async () => {
    const user = userEvent.setup();
    const { bridge } = await createRaidRewardsHarness((workflow) => ({
      ...workflow,
      tables: workflow.tables.map((table, tableIndex) =>
        tableIndex === 0
          ? {
              ...table,
              rewards: [
                {
                  ...table.rewards[0]!,
                  itemId: 4_294_967_295,
                  itemName: 'Item 4294967295'
                },
                {
                  ...table.rewards[0]!,
                  entryId: 11,
                  itemId: 3,
                  itemName: 'Exp. Candy L',
                  slot: 2
                }
              ]
            }
          : table
      )
    }));
    renderRaidRewards(bridge);

    const itemInput = await screen.findByLabelText('Item ID');
    expect(itemInput).toHaveValue('4294967295 Item 4294967295');
    await user.click(itemInput);
    expect(
      screen.getByRole('option', { name: '4294967295 Item 4294967295' })
    ).toBeInTheDocument();

    await user.keyboard('{Escape}');
    await user.selectOptions(screen.getByLabelText('Raid reward slot'), '2');
    await waitFor(() => expect(itemInput).toHaveValue('003 Exp. Candy L'));
    await user.click(itemInput);
    expect(
      screen.queryByRole('option', { name: '4294967295 Item 4294967295' })
    ).toBeNull();
  });

  it('retains the whole draft when an atomic batch is rejected', async () => {
    const user = userEvent.setup();
    const rejection: ApiDiagnostic = {
      domain: 'workflow.raidRewards',
      field: 'star5Value',
      message: 'Raid reward star5Value must be between 0 and 100.',
      severity: 'error'
    };
    const harness = await createRaidRewardsHarness();
    const updateRaidRewardFields = vi.fn(
      async (request: Parameters<ProjectBridge['updateRaidRewardFields']>[0]) => ({
        diagnostics: [rejection],
        session: request.session ?? editSession,
        workflow: harness.workflow
      })
    );
    harness.bridge.updateRaidRewardFields = updateRaidRewardFields;
    renderRaidRewards(harness.bridge);

    const dropChanceInput = await screen.findByLabelText('5-star drop chance');
    await user.clear(dropChanceInput);
    await user.type(dropChanceInput, '6');
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(updateRaidRewardFields).toHaveBeenCalledTimes(1));
    expect(dropChanceInput).toHaveValue(6);
    expect(screen.getByRole('button', { name: 'Stage' })).toBeEnabled();
    expect(useWorkbenchStore.getState().editValidationDiagnostics).toEqual([rejection]);
    expect(useWorkbenchStore.getState().editSession).toEqual(editSession);
  });

  it('selects only filtered Drop rows and uses localized neutral no-match copy', async () => {
    window.localStorage.setItem(languageStorageKey, 'es');
    const user = userEvent.setup();
    const { bridge } = await createRaidRewardsHarness((workflow) => {
      const firstTable = workflow.tables[0]!;
      return {
        ...workflow,
        stats: {
          ...workflow.stats,
          totalRewardItemCount: 2,
          totalTableCount: 2
        },
        tables: [
          firstTable,
          {
            ...firstTable,
            displayName: 'Raid Rewards',
            rewards: [
              {
                ...firstTable.rewards[0]!,
                itemId: 4,
                itemName: 'Exp. Candy XL'
              }
            ],
            sourceTableHash: '0xBBCCDDEE11223344',
            tableId: 'drop:1:BBCCDDEE11223344',
            tableIndex: 1
          }
        ]
      };
    });
    renderRaidRewards(bridge);

    const search = await screen.findByRole('searchbox', {
      name: 'Buscar tablas de recompensas de incursión...'
    });
    await user.type(search, 'Recompensas de incursión');

    const table = screen.getByRole('table', { name: 'Recompensas de incursión' });
    expect(
      within(table).queryByText('Drop 000 | SW Den 0 Slot 00, 1-5-Star Eevee-1')
    ).toBeNull();
    const matchingRow = within(table).getByRole('row', { name: /Recompensas de incursión/ });
    expect(matchingRow).toHaveAttribute('aria-selected', 'true');
    expect(
      within(
        screen.getByRole('complementary', {
          name: 'Procedencia de la recompensa de incursión seleccionada'
        })
      ).getAllByText(/Exp\. Candy XL/).length
    ).toBeGreaterThan(0);

    await user.clear(search);
    await user.type(search, 'sin coincidencias');
    expect(screen.getByRole('status')).toHaveTextContent(
      'No hay tablas de recompensas coincidentes.'
    );
    expect(
      screen.getByText('No hay ninguna tabla de recompensas seleccionada.')
    ).toBeInTheDocument();
  });

  it('uses neutral no-match copy for Bonus reward tables', async () => {
    const user = userEvent.setup();
    const { bridge } = await createRaidBonusRewardsHarness();
    renderRaidRewards(bridge);

    const search = await screen.findByRole('searchbox', {
      name: 'Search raid bonus reward tables...'
    });
    await user.type(search, 'no bonus matches');

    expect(screen.getByRole('status')).toHaveTextContent('No matching reward tables.');
    expect(screen.getByText('No reward table selected.')).toBeInTheDocument();
  });

  it('stages all changed Bonus fields atomically and preserves search context', async () => {
    const user = userEvent.setup();
    const { bridge } = await createRaidBonusRewardsHarness();
    const updateRaidBonusRewardFields = vi.spyOn(bridge, 'updateRaidBonusRewardFields');
    act(() => useWorkbenchStore.getState().setRaidBonusRewardSearchText('Armorite'));
    renderRaidRewards(bridge);

    const oneStarQuantityInput = await screen.findByLabelText('1-star quantity');
    await user.clear(oneStarQuantityInput);
    await user.type(oneStarQuantityInput, '6');
    const fiveStarQuantityInput = screen.getByLabelText('5-star quantity');
    await user.clear(fiveStarQuantityInput);
    await user.type(fiveStarQuantityInput, '7');
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(updateRaidBonusRewardFields).toHaveBeenCalledTimes(1));
    expect(updateRaidBonusRewardFields.mock.calls[0]?.[0].updates).toEqual([
      {
        field: 'star1Value',
        slot: 1,
        tableId: 'bonus:0:1020304050607080',
        value: '6'
      },
      {
        field: 'star5Value',
        slot: 1,
        tableId: 'bonus:0:1020304050607080',
        value: '7'
      }
    ]);
    expect(
      screen.getByRole('searchbox', { name: 'Search raid bonus reward tables...' })
    ).toHaveValue('Armorite');
  });

  it('retains the whole Bonus draft when an atomic batch is rejected', async () => {
    const user = userEvent.setup();
    const rejection: ApiDiagnostic = {
      domain: 'workflow.raidBonusRewards',
      field: 'star5Value',
      message: 'Raid bonus reward star5Value must be between 0 and 999.',
      severity: 'error'
    };
    const harness = await createRaidBonusRewardsHarness((loadedWorkflow) => {
      return {
        updateRaidBonusRewardFields: async (request) => ({
          diagnostics: [rejection],
          session: request.session ?? editSession,
          workflow: loadedWorkflow
        })
      };
    });
    const updateRaidBonusRewardFields = vi.spyOn(
      harness.bridge,
      'updateRaidBonusRewardFields'
    );
    renderRaidRewards(harness.bridge);

    const itemInput = await screen.findByLabelText('Item ID');
    await user.clear(itemInput);
    await user.type(itemInput, '003{Enter}');
    const fiveStarQuantityInput = await screen.findByLabelText('5-star quantity');
    await user.clear(fiveStarQuantityInput);
    await user.type(fiveStarQuantityInput, '7');
    await user.click(screen.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(updateRaidBonusRewardFields).toHaveBeenCalledTimes(1));
    expect(updateRaidBonusRewardFields.mock.calls[0]?.[0].updates).toEqual([
      {
        field: 'itemId',
        slot: 1,
        tableId: 'bonus:0:1020304050607080',
        value: '3'
      },
      {
        field: 'star5Value',
        slot: 1,
        tableId: 'bonus:0:1020304050607080',
        value: '7'
      }
    ]);
    expect(itemInput).toHaveValue('003 Exp. Candy L');
    expect(fiveStarQuantityInput).toHaveValue(7);
    expect(screen.getByRole('button', { name: 'Stage' })).toBeEnabled();
    expect(useWorkbenchStore.getState().editValidationDiagnostics).toEqual([rejection]);
    expect(useWorkbenchStore.getState().editSession).toEqual(editSession);
  });

  it('keeps the Bonus quantity policy separate from the Drop chance cap', async () => {
    const user = userEvent.setup();
    const { bridge } = await createRaidBonusRewardsHarness();
    renderRaidRewards(bridge);

    const quantityInput = await screen.findByLabelText('5-star quantity');
    expect(quantityInput).toHaveAttribute('max', '999');
    await user.clear(quantityInput);
    await user.type(quantityInput, '1000');

    expect(screen.getByText('Maximum value is 999.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Stage' })).toBeDisabled();
  });

  it('defensively caps drop chances at 100 in the editor', async () => {
    const user = userEvent.setup();
    const { bridge } = await createRaidRewardsHarness();
    renderRaidRewards(bridge);

    const dropChanceInput = await screen.findByLabelText('5-star drop chance');
    expect(dropChanceInput).toHaveAttribute('max', '100');
    await user.clear(dropChanceInput);
    await user.type(dropChanceInput, '101');

    expect(screen.getByText('Maximum value is 100.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Stage' })).toBeDisabled();
  });
});
