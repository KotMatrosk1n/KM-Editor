/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { type ItemsWorkflow } from './bridge/contracts';
import { createMockProjectBridge } from './testSupport/appTestFixtures';
import { useWorkbenchStore } from './workbenchStore';

const tauriEventMock = vi.hoisted(() => ({
  listen: vi.fn(() => Promise.resolve(() => undefined))
}));

vi.mock('@tauri-apps/api/event', () => ({
  listen: tauriEventMock.listen
}));

describe('Items UI', () => {
  it('keeps shared-row drafts atomic and visible through filtering, errors, and refreshes', async () => {
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
      itemSearchText: '',
      itemsWorkflow: null,
      openProject: null,
      projectStatus: 'idle',
      selectedItemId: null,
      workflows: []
    });

    const user = userEvent.setup();
    const bridge = createMockProjectBridge({}, true);
    const originalLoadItemsWorkflow = bridge.loadItemsWorkflow;
    let currentWorkflow: ItemsWorkflow | null = null;

    bridge.loadItemsWorkflow = vi.fn(async (request) => {
      const response = await originalLoadItemsWorkflow(request);
      currentWorkflow = {
        ...response.workflow,
        items: response.workflow.items.map((item) =>
          item.itemId === 1 || item.itemId === 2
            ? {
                ...item,
                buyPrice: 300,
                sellPrice: 150,
                sharedItemIds: [1, 2],
                wattsPrice: 15
              }
            : item
        )
      };
      return { workflow: currentWorkflow };
    });

    const singularUpdate = vi.fn(bridge.updateItemField);
    bridge.updateItemField = singularUpdate;
    let updateAttempt = 0;
    const updateItemFields = vi.fn(
      async (request: Parameters<typeof bridge.updateItemFields>[0]) => {
        if (!currentWorkflow) {
          throw new Error('Items workflow was not loaded.');
        }

        updateAttempt += 1;
        if (updateAttempt === 1) {
          return {
            diagnostics: [
              {
                message: 'The Items batch was rejected.',
                severity: 'error' as const
              }
            ],
            session: {
              hasPendingChanges: true,
              pendingEdits: [
                {
                  domain: 'workflow.items',
                  field: 'buyPrice',
                  newValue: '999',
                  recordId: '2',
                  sources: [],
                  summary: 'This rejected edit must not replace the active session.'
                }
              ],
              sessionId: 'rejected-session'
            },
            workflow: {
              ...currentWorkflow,
              items: currentWorkflow.items.map((item) =>
                item.itemId === 2 ? { ...item, buyPrice: 999, sellPrice: 499 } : item
              )
            }
          };
        }

        const buyPrice = Number.parseInt(
          request.updates.find((update) => update.field === 'buyPrice')?.value ?? '0',
          10
        );
        const wattsPrice = Number.parseInt(
          request.updates.find((update) => update.field === 'wattsPrice')?.value ?? '0',
          10
        );
        currentWorkflow = {
          ...currentWorkflow,
          items: currentWorkflow.items.map((item) =>
            item.sharedItemIds.includes(request.updates[0]?.itemId ?? -1)
              ? {
                  ...item,
                  buyPrice,
                  sellPrice: Math.floor(buyPrice / 2),
                  wattsPrice
                }
              : item
          )
        };

        return {
          diagnostics: [],
          session: {
            hasPendingChanges: true,
            pendingEdits: request.updates.map((update) => ({
              domain: 'workflow.items',
              field: update.field,
              newValue: update.value,
              recordId: update.itemId.toString(),
              sources: [],
              summary: `Set item ${update.itemId} ${update.field} to ${update.value}.`
            })),
            sessionId: request.session?.sessionId ?? 'session-1'
          },
          workflow: currentWorkflow
        };
      }
    );
    bridge.updateItemFields = updateItemFields;

    render(<App bridge={bridge} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));

    const navigation = await screen.findByRole('navigation', { name: 'Workspace' });
    await user.click(within(navigation).getByRole('button', { name: 'Editors' }));
    await user.click(within(navigation).getByRole('button', { name: 'Items' }));

    const inspector = await screen.findByRole('complementary', {
      name: 'Selected item provenance'
    });
    expect(within(inspector).getByText('Potion')).toBeInTheDocument();
    await user.click(within(inspector).getByRole('button', { name: 'Edit' }));

    const buyPriceInput = within(inspector).getByLabelText('Buy price');
    const sellPriceInput = within(inspector).getByLabelText('Sell price');
    const wattsPriceInput = within(inspector).getByLabelText('Watts price');
    await user.clear(buyPriceInput);
    await user.type(buyPriceInput, '500');
    await user.clear(wattsPriceInput);
    await user.type(wattsPriceInput, '99');
    expect(sellPriceInput).toHaveValue(250);

    const searchInput = screen.getByLabelText('Search items');
    await user.type(searchInput, 'Antidote');
    expect(within(inspector).getByText('Antidote')).toBeInTheDocument();
    expect(within(inspector).getByLabelText('Buy price')).toHaveValue(500);
    expect(within(inspector).getByLabelText('Sell price')).toHaveValue(250);
    expect(within(inspector).getByLabelText('Watts price')).toHaveValue(99);

    const stageButton = within(inspector).getByRole('button', { name: 'Stage' });
    await user.click(stageButton);
    await waitFor(() => expect(updateItemFields).toHaveBeenCalledTimes(1));
    expect(singularUpdate).not.toHaveBeenCalled();
    expect(updateItemFields.mock.calls[0]?.[0].updates).toEqual([
      { field: 'buyPrice', itemId: 2, value: '500' },
      { field: 'wattsPrice', itemId: 2, value: '99' }
    ]);
    await waitFor(() =>
      expect(useWorkbenchStore.getState().editSession).toMatchObject({
        pendingEdits: [],
        sessionId: 'session-1'
      })
    );
    expect(within(inspector).getByLabelText('Buy price')).toHaveValue(500);
    expect(within(inspector).getByLabelText('Sell price')).toHaveValue(250);
    expect(within(inspector).getByLabelText('Watts price')).toHaveValue(99);
    expect(stageButton).toBeEnabled();

    await user.click(stageButton);
    await waitFor(() => expect(updateItemFields).toHaveBeenCalledTimes(2));
    await waitFor(() =>
      expect(useWorkbenchStore.getState().editSession?.pendingEdits).toHaveLength(2)
    );
    expect(updateItemFields.mock.calls[1]?.[0].updates).toEqual(
      updateItemFields.mock.calls[0]?.[0].updates
    );
    expect(searchInput).toHaveValue('Antidote');
    expect(within(inspector).getByText('Antidote')).toBeInTheDocument();
  });
});
