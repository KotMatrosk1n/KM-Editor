/* SPDX-License-Identifier: GPL-3.0-only */

import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { type EditSession, type TeraRaidsWorkflow } from './bridge/contracts';
import { type ProjectBridge } from './bridge/projectBridge';
import { LocalizationProvider } from './localization';
import { createMockProjectBridge } from './testSupport/appTestFixtures';
import { useWorkbenchStore } from './workbenchStore';

const projectPaths = {
  baseExeFsPath: '',
  baseRomFsPath: 'base-romfs',
  outputRootPath: 'output',
  pokemonLegendsZASupportFolderPath: '',
  saveFilePath: '',
  scarletVioletSupportFolderPath: '',
  selectedGame: 'scarlet' as const
};

const editSession: EditSession = {
  hasPendingChanges: false,
  pendingEdits: [],
  sessionId: 'tera-raids-ui-session'
};

async function createTeraRaidsHarness() {
  const fixtureBridge = createMockProjectBridge({}, true);
  const response = await fixtureBridge.loadTeraRaidsWorkflow({ paths: projectPaths });
  const sourceWorkflow = response.workflow;
  const fixedTable = sourceWorkflow.fixedRewardTables[0]!;
  const fixedReward = fixedTable.rewards[0]!;
  const lotteryTable = sourceWorkflow.lotteryRewardTables[0]!;
  const lotteryReward = lotteryTable.rewards[0]!;
  const workflow: TeraRaidsWorkflow = {
    ...sourceWorkflow,
    editableFields: [
      ...sourceWorkflow.editableFields,
      {
        field: 'fixedItemId',
        label: 'Reward item',
        maximumValue: 4095,
        minimumValue: 0,
        options: [
          { label: 'None', value: 0 },
          { label: 'Exp. Candy L', value: 3 }
        ],
        valueKind: 'integer'
      },
      {
        field: 'fixedCount',
        label: 'Reward count',
        maximumValue: 255,
        minimumValue: 0,
        options: [],
        valueKind: 'integer'
      }
    ],
    fixedRewardTables: [
      {
        ...fixedTable,
        preview: '1 Exp. Candy L, 3 Pokemon material',
        rewardItemCount: 2,
        rewards: [
          fixedReward,
          {
            ...fixedReward,
            category: 0,
            categoryLabel: 'Item',
            count: 0,
            itemId: 0,
            itemName: 'None',
            recordId: 'fixed:0:1',
            slot: 1,
            subjectType: 0,
            subjectTypeLabel: 'All'
          },
          {
            ...fixedReward,
            category: 1,
            categoryLabel: 'Pokemon material',
            count: 3,
            itemId: 0,
            itemName: 'Pokemon material',
            recordId: 'fixed:0:2',
            slot: 2
          }
        ]
      }
    ],
    lotteryRewardTables: [
      {
        ...lotteryTable,
        preview: '1 Rare Candy, 4 Tera shard',
        rewardItemCount: 2,
        rewards: [
          lotteryReward,
          {
            ...lotteryReward,
            category: 0,
            categoryLabel: 'Item',
            count: 0,
            itemId: 0,
            itemName: 'None',
            rareItemFlag: false,
            rate: 0,
            recordId: 'lottery:0:1',
            slot: 1
          },
          {
            ...lotteryReward,
            category: 2,
            categoryLabel: 'Tera shard',
            count: 4,
            itemId: 0,
            itemName: 'Tera shard',
            rate: 25,
            recordId: 'lottery:0:2',
            slot: 2
          }
        ]
      }
    ],
    raids: sourceWorkflow.raids.map((raid) => ({
      ...raid,
      fixedRewardPreview: '1 Exp. Candy L, 3 Pokemon material',
      lotteryRewardPreview: '1 Rare Candy, 4 Tera shard'
    })),
    stats: {
      ...sourceWorkflow.stats,
      totalRewardItemCount: 4
    }
  };
  const promotedWorkflow: TeraRaidsWorkflow = {
    ...workflow,
    fixedRewardTables: workflow.fixedRewardTables.map((table) => ({
      ...table,
      preview: '1 Exp. Candy L, 1 Exp. Candy L, 3 Pokemon material',
      rewardItemCount: 3,
      rewards: table.rewards.map((reward) =>
        reward.recordId === 'fixed:0:1'
          ? {
              ...reward,
              count: 1,
              itemId: 3,
              itemName: 'Exp. Candy L'
            }
          : reward
      )
    })),
    raids: workflow.raids.map((raid) => ({
      ...raid,
      fixedRewardPreview: '1 Exp. Candy L, 1 Exp. Candy L, 3 Pokemon material'
    })),
    stats: {
      ...workflow.stats,
      totalRewardItemCount: 5
    }
  };
  const updateTeraRaidFields = vi.fn(
    async (
      _request: Parameters<ProjectBridge['updateTeraRaidFields']>[0]
    ): ReturnType<ProjectBridge['updateTeraRaidFields']> => ({
      diagnostics: [],
      session: editSession,
      workflow: promotedWorkflow
    })
  );
  const bridge = createMockProjectBridge({ updateTeraRaidFields }, true);

  useWorkbenchStore.setState({
    activeSection: 'teraRaids',
    draftPaths: projectPaths,
    editSession: null,
    editValidationDiagnostics: [],
    selectedTeraRaidRecordId: workflow.raids[0]?.recordId ?? null,
    teraRaidSearchText: '',
    teraRaidsWorkflow: workflow
  });

  return { bridge, updateTeraRaidFields, workflow };
}

function renderTeraRaids(bridge: ProjectBridge) {
  return render(
    <LocalizationProvider>
      <App bridge={bridge} />
    </LocalizationProvider>
  );
}

describe('Tera Raids UI', () => {
  beforeEach(() => {
    window.localStorage.clear();
    useWorkbenchStore.setState(useWorkbenchStore.getInitialState(), true);
  });

  it('hides default reward capacity while keeping empty slots reachable for editing', async () => {
    const user = userEvent.setup();
    const { bridge, updateTeraRaidFields } = await createTeraRaidsHarness();
    renderTeraRaids(bridge);

    const rewardRows = await screen.findByLabelText('Tera raid reward rows');
    expect(within(rewardRows).getByText('Exp. Candy L')).toBeInTheDocument();
    expect(within(rewardRows).getByText('Pokemon material')).toBeInTheDocument();
    expect(within(rewardRows).queryByText('None')).toBeNull();
    expect(within(rewardRows).queryByText('Count 0')).toBeNull();
    expect(screen.queryByText('0 None')).toBeNull();
    expect(screen.queryByLabelText('Add reward in empty slot')).toBeNull();

    act(() => useWorkbenchStore.setState({ editSession }));

    const emptySlotSelect = await screen.findByLabelText('Add reward in empty slot');
    expect(within(emptySlotSelect).getByRole('option', { name: '#1: Empty slot' })).toBeInTheDocument();
    await user.selectOptions(emptySlotSelect, 'fixed:0:1');
    expect(await screen.findByText('Empty slot')).toBeInTheDocument();
    const rewardItemInput = screen.getByLabelText('Reward item');
    const rewardCountInput = screen.getByLabelText('Reward count');
    await user.clear(rewardItemInput);
    await user.type(rewardItemInput, '003{Enter}');
    await user.clear(rewardCountInput);
    await user.type(rewardCountInput, '1');
    const rewardFieldGroups = rewardItemInput.closest('.editable-field-groups');
    const rewardActionRow = rewardFieldGroups?.nextElementSibling;
    expect(rewardActionRow).not.toBeNull();
    await user.click(
      within(rewardActionRow as HTMLElement).getByRole('button', { name: 'Stage' })
    );

    await waitFor(() => expect(updateTeraRaidFields).toHaveBeenCalledTimes(1));
    expect(updateTeraRaidFields.mock.calls[0]?.[0].updates).toEqual([
      { field: 'fixedItemId', recordId: 'fixed:0:1', value: '3' },
      { field: 'fixedCount', recordId: 'fixed:0:1', value: '1' }
    ]);
    expect(screen.queryByLabelText('Add reward in empty slot')).toBeNull();
    const promotedReward = within(rewardRows).getByRole('button', {
      name: /#1\s*Exp\. Candy L\s*Count 1/
    });
    expect(promotedReward).toHaveAttribute('aria-pressed', 'true');

    await user.selectOptions(screen.getByLabelText('Tera raid reward kind'), 'lottery');
    expect(within(rewardRows).getByText('Rare Candy')).toBeInTheDocument();
    expect(within(rewardRows).getByText('Tera shard')).toBeInTheDocument();
    expect(within(rewardRows).queryByText('None')).toBeNull();
    expect(within(rewardRows).queryByText('Count 0')).toBeNull();
    const lotteryEmptySlotSelect = screen.getByLabelText('Add reward in empty slot');
    await user.selectOptions(lotteryEmptySlotSelect, 'lottery:0:1');
    expect(lotteryEmptySlotSelect).toHaveValue('lottery:0:1');
  });
});
