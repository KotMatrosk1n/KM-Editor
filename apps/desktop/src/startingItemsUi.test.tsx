/* SPDX-License-Identifier: GPL-3.0-only */

import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { type ApiDiagnostic, type StartingItemsWorkflow } from './bridge/contracts';
import { type ProjectBridge } from './bridge/projectBridge';
import { languageStorageKey, LocalizationProvider } from './localization';
import { createMockProjectBridge } from './testSupport/appTestFixtures';
import { useWorkbenchStore } from './workbenchStore';

const tauriEventMock = vi.hoisted(() => {
  const listeners: Record<string, Array<() => void>> = {};

  return {
    listen: vi.fn((eventName: string, handler: () => void) => {
      listeners[eventName] = [...(listeners[eventName] ?? []), handler];

      return Promise.resolve(() => {
        listeners[eventName] = (listeners[eventName] ?? []).filter(
          (candidate) => candidate !== handler
        );
      });
    }),
    listeners
  };
});

vi.mock('@tauri-apps/api/event', () => ({
  listen: tauriEventMock.listen
}));

const projectPaths = {
  baseExeFsPath: 'base-exefs',
  baseRomFsPath: 'base-romfs',
  outputRootPath: 'output',
  pokemonLegendsZASupportFolderPath: '',
  saveFilePath: '',
  scarletVioletSupportFolderPath: '',
  selectedGame: 'sword' as const
};

async function createStartingItemsHarness(
  mutateWorkflow?: (workflow: StartingItemsWorkflow) => StartingItemsWorkflow
) {
  const fixtureBridge = createMockProjectBridge({}, true);
  const response = await fixtureBridge.loadStartingItemsWorkflow({ paths: projectPaths });
  const workflow = mutateWorkflow?.(response.workflow) ?? response.workflow;
  const bridge = createMockProjectBridge({}, true);

  useWorkbenchStore.setState({
    activeSection: 'startingItems',
    applyResult: null,
    changePlan: null,
    draftPaths: projectPaths,
    editSession: null,
    editValidationDiagnostics: [],
    selectedStartingItemSlot: workflow.grants[0]?.slot ?? null,
    startingItemsWorkflow: workflow
  });

  return { bridge, workflow };
}

function renderStartingItems(bridge: ProjectBridge) {
  return render(
    <LocalizationProvider>
      <App bridge={bridge} />
    </LocalizationProvider>
  );
}

function getItemInput(slot: number, labelPrefix = 'Item for Bag Hook slot') {
  return screen.getByRole('textbox', { name: `${labelPrefix} ${slot}` });
}

function getQuantityInput(slot: number, labelPrefix = 'Quantity for Bag Hook slot') {
  return screen.getByRole('spinbutton', { name: `${labelPrefix} ${slot}` });
}

describe('Starting Items UI', () => {
  beforeEach(() => {
    window.localStorage.clear();
    tauriEventMock.listen.mockClear();
    for (const eventName of Object.keys(tauriEventMock.listeners)) {
      delete tauriEventMock.listeners[eventName];
    }
    useWorkbenchStore.setState(useWorkbenchStore.getInitialState(), true);
  });

  it('selects an exact numeric item ID on blur and Enter instead of a prefix match', async () => {
    const user = userEvent.setup();
    const { bridge } = await createStartingItemsHarness();
    const stageStartingItems = vi.spyOn(bridge, 'stageStartingItems');
    renderStartingItems(bridge);

    const blurInput = getItemInput(3);
    await user.clear(blurInput);
    await user.type(blurInput, '1');
    await user.tab();
    expect(blurInput).toHaveValue('Master Ball (#1)');

    const enterInput = getItemInput(4);
    await user.clear(enterInput);
    await user.type(enterInput, '1');
    await user.keyboard('{Enter}');
    expect(enterInput).toHaveValue('Master Ball (#1)');

    await user.click(screen.getByRole('button', { name: 'Stage Items' }));
    await waitFor(() => expect(stageStartingItems).toHaveBeenCalledTimes(1));
    const grants = stageStartingItems.mock.calls[0]![0].grants;
    expect(grants.find((grant) => grant.slot === 3)?.itemId).toBe(1);
    expect(grants.find((grant) => grant.slot === 4)?.itemId).toBe(1);
  });

  it.each(['1abc', '1.5', '01'])(
    'rejects the noncanonical item selector value %s',
    async (value) => {
      const { bridge } = await createStartingItemsHarness();
      renderStartingItems(bridge);

      const itemInput = getItemInput(3);
      fireEvent.change(itemInput, { target: { value } });
      fireEvent.blur(itemInput);

      expect(await screen.findByText('Choose a known item.')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Stage Items' })).toBeDisabled();
    }
  );

  it('leaves an ambiguous nonexact numeric-prefix query uncommitted on blur and Enter', async () => {
    const user = userEvent.setup();
    const { bridge } = await createStartingItemsHarness();
    renderStartingItems(bridge);

    const blurInput = getItemInput(3);
    await user.clear(blurInput);
    await user.type(blurInput, '1abc');
    await user.tab();
    expect(blurInput).toHaveValue('1abc');

    const enterInput = getItemInput(4);
    await user.clear(enterInput);
    await user.type(enterInput, '1abc');
    await user.keyboard('{Enter}');
    expect(enterInput).toHaveValue('1abc');
  });

  it('preserves first-match Enter selection for a nonnumeric text query', async () => {
    const user = userEvent.setup();
    const { bridge } = await createStartingItemsHarness();
    renderStartingItems(bridge);

    const itemInput = getItemInput(3);
    await user.clear(itemInput);
    await user.type(itemInput, 'A');
    await user.keyboard('{Enter}');

    expect(itemInput).toHaveValue('Antidote (#10)');
  });

  it('disables review and apply when staged grants have newer local edits', async () => {
    const user = userEvent.setup();
    const { bridge } = await createStartingItemsHarness();
    renderStartingItems(bridge);

    await user.click(screen.getByRole('button', { name: 'Stage Items' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());

    fireEvent.change(getQuantityInput(2), { target: { value: '6' } });

    expect(screen.getByRole('button', { name: 'Stage Items' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();
  });

  it('retains the staged session, workflow, reviewed plan, and local draft after rejection', async () => {
    const user = userEvent.setup();
    const rejection: ApiDiagnostic = {
      domain: 'workflow.startingItems',
      message: 'The Starting Items grants were not staged.',
      severity: 'error'
    };
    const { bridge } = await createStartingItemsHarness();
    const successfulStage = bridge.stageStartingItems;
    const stageStartingItems = vi.fn(successfulStage);
    bridge.stageStartingItems = stageStartingItems;
    renderStartingItems(bridge);

    await user.click(screen.getByRole('button', { name: 'Stage Items' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());

    const sessionBeforeRejection = useWorkbenchStore.getState().editSession;
    const workflowBeforeRejection = useWorkbenchStore.getState().startingItemsWorkflow;
    stageStartingItems.mockImplementationOnce(async () => ({
      diagnostics: [rejection],
      session: {
        ...sessionBeforeRejection!,
        pendingEdits: [
          ...sessionBeforeRejection!.pendingEdits,
          { ...sessionBeforeRejection!.pendingEdits[0]!, summary: 'Partial backend mutation.' }
        ]
      },
      workflow: {
        ...workflowBeforeRejection!,
        blockerKind: 'bagHookDamaged',
        installStatus: 'blocked'
      }
    }));

    const quantityInput = getQuantityInput(2);
    fireEvent.change(quantityInput, { target: { value: '6' } });
    await user.click(screen.getByRole('button', { name: 'Stage Items' }));
    await waitFor(() => expect(stageStartingItems).toHaveBeenCalledTimes(2));

    expect(await screen.findByText('The Starting Items grants were not staged.')).toBeInTheDocument();
    expect(useWorkbenchStore.getState().editSession).toEqual(sessionBeforeRejection);
    expect(useWorkbenchStore.getState().startingItemsWorkflow).toBe(workflowBeforeRejection);
    expect(quantityInput).toHaveValue(6);
    fireEvent.change(quantityInput, { target: { value: '5' } });
    expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled();
  });

  it('retains the staged session, workflow, reviewed plan, and local draft after transport failure', async () => {
    const user = userEvent.setup();
    const { bridge } = await createStartingItemsHarness();
    renderStartingItems(bridge);

    await user.click(screen.getByRole('button', { name: 'Stage Items' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());

    const sessionBeforeFailure = useWorkbenchStore.getState().editSession;
    const workflowBeforeFailure = useWorkbenchStore.getState().startingItemsWorkflow;
    bridge.stageStartingItems = vi.fn(async () => {
      throw new Error('Starting Items stage transport failed.');
    });

    const quantityInput = getQuantityInput(2);
    fireEvent.change(quantityInput, { target: { value: '6' } });
    await user.click(screen.getByRole('button', { name: 'Stage Items' }));

    expect(await screen.findByText(/Starting Items stage transport failed\./)).toBeInTheDocument();
    expect(useWorkbenchStore.getState().editSession).toEqual(sessionBeforeFailure);
    expect(useWorkbenchStore.getState().startingItemsWorkflow).toBe(workflowBeforeFailure);
    expect(quantityInput).toHaveValue(6);
    fireEvent.change(quantityInput, { target: { value: '5' } });
    expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled();
  });

  it('returns an edited empty slot to a semantically clean state when No item is restored', async () => {
    const user = userEvent.setup();
    const { bridge } = await createStartingItemsHarness();
    renderStartingItems(bridge);

    const itemInput = getItemInput(3);
    await user.clear(itemInput);
    await user.type(itemInput, '1');
    await user.tab();
    fireEvent.change(getQuantityInput(3), { target: { value: '5' } });
    await user.clear(itemInput);
    await user.tab();

    expect(itemInput).toHaveValue('No item');
    expect(getQuantityInput(3)).toHaveValue(1);
    await user.click(screen.getByRole('button', { name: 'Close Editor' }));
    await waitFor(() => expect(useWorkbenchStore.getState().activeSection).toBe('workflows'));
    expect(screen.queryByRole('heading', { name: 'Discard Pending Changes?' })).toBeNull();
  });

  it('does not mark an untouched unavailable baseline item as a local draft', async () => {
    const user = userEvent.setup();
    const { bridge } = await createStartingItemsHarness((workflow) => ({
      ...workflow,
      grants: workflow.grants.map((grant) =>
        grant.slot === 2
          ? { ...grant, itemId: 9999, itemName: 'Unavailable item', quantity: 5 }
          : grant
      )
    }));
    renderStartingItems(bridge);

    expect(await screen.findByText('Choose a known item.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Close Editor' }));
    await waitFor(() => expect(useWorkbenchStore.getState().activeSection).toBe('workflows'));
    expect(screen.queryByRole('heading', { name: 'Discard Pending Changes?' })).toBeNull();
  });

  it('opens the Bag Hook dependency modal only for a missing Bag Hook', async () => {
    const user = userEvent.setup();
    const { bridge } = await createStartingItemsHarness((workflow) => ({
      ...workflow,
      blockerKind: 'bagHookMissing',
      installMessage: 'Install Bag Hook before adding Starting Items.',
      installStatus: 'blocked'
    }));
    renderStartingItems(bridge);

    await user.click(screen.getByRole('button', { name: 'Stage Items' }));
    expect(await screen.findByRole('heading', { name: 'Bag Hook Required' })).toBeInTheDocument();
  });

  it('shows a damaged Bag Hook blocker without offering the install dependency modal', async () => {
    const { bridge } = await createStartingItemsHarness((workflow) => ({
      ...workflow,
      blockerKind: 'bagHookDamaged',
      diagnostics: [
        {
          domain: 'workflow.startingItems',
          message: 'The installed Bag Hook slots are damaged.',
          severity: 'error'
        }
      ],
      installMessage: 'Repair the damaged Bag Hook slots before editing Starting Items.',
      installStatus: 'blocked'
    }));
    renderStartingItems(bridge);

    expect(
      await screen.findByText('Repair the damaged Bag Hook slots before editing Starting Items.')
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Stage Items' })).toBeDisabled();
    expect(screen.queryByRole('heading', { name: 'Bag Hook Required' })).toBeNull();
  });

  it('localizes slot controls and key-item state while preserving item names and IDs', async () => {
    window.localStorage.setItem(languageStorageKey, 'es');
    const user = userEvent.setup();
    const { bridge } = await createStartingItemsHarness();
    renderStartingItems(bridge);

    const itemInput = screen.getByRole('textbox', {
      name: 'Objeto para el hueco 3 de Gancho de bolsa'
    });
    await user.clear(itemInput);
    await user.type(itemInput, '700');
    await user.keyboard('{Enter}');

    expect(itemInput).toHaveValue('Bike (#700) [Objeto clave]');
    expect(
      screen.getByRole('spinbutton', {
        name: 'Cantidad para el hueco 3 de Gancho de bolsa'
      })
    ).toBeDisabled();
    expect(screen.getByText('1 (objeto clave)')).toBeInTheDocument();
  });

  it('ignores malformed staged slot prefixes instead of overlaying a valid slot', async () => {
    const user = userEvent.setup();
    const { bridge } = await createStartingItemsHarness();
    renderStartingItems(bridge);
    await user.click(screen.getByRole('button', { name: 'Stage Items' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());

    act(() => {
      useWorkbenchStore.setState({
        editSession: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.startingItems',
              field: 'grants',
              newValue: '2abc:700:1;4:1:5',
              recordId: 'starting-items',
              sources: [
                {
                  layer: 'layered',
                  relativePath: 'romfs/bin/script/amx/main_event_0020.amx'
                }
              ],
              summary: 'Stage Starting Items grants in Bag Hook slots 2-20.'
            }
          ],
          sessionId: 'session-malformed-starting-items'
        }
      });
    });

    await waitFor(() => expect(getItemInput(2)).toHaveValue('No item'));
    await waitFor(() => expect(getItemInput(4)).toHaveValue('Master Ball (#1)'));
    expect(getQuantityInput(4)).toHaveValue(5);
  });
});
