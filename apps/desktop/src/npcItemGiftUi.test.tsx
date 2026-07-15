/* SPDX-License-Identifier: GPL-3.0-only */

import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { type ApiDiagnostic } from './bridge/contracts';
import { type ProjectBridge } from './bridge/projectBridge';
import { LocalizationProvider } from './localization';
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

async function createNpcItemGiftHarness() {
  const fixtureBridge = createMockProjectBridge({}, true);
  const response = await fixtureBridge.loadNpcItemGiftWorkflow({ paths: projectPaths });
  const bridge = createMockProjectBridge({}, true);

  useWorkbenchStore.setState({
    activeSection: 'npcItemGift',
    applyResult: null,
    changePlan: null,
    draftPaths: projectPaths,
    editSession: null,
    editValidationDiagnostics: [],
    npcItemGiftWorkflow: response.workflow
  });

  return { bridge, workflow: response.workflow };
}

function renderNpcItemGift(bridge: ProjectBridge) {
  return render(
    <LocalizationProvider>
      <App bridge={bridge} />
    </LocalizationProvider>
  );
}

describe('NPC Item Gift UI staging', () => {
  beforeEach(() => {
    window.localStorage.clear();
    tauriEventMock.listen.mockClear();
    for (const eventName of Object.keys(tauriEventMock.listeners)) {
      delete tauriEventMock.listeners[eventName];
    }
    useWorkbenchStore.setState(useWorkbenchStore.getInitialState(), true);
  });

  it('retains the workflow, session, and visible draft when staging is rejected', async () => {
    const user = userEvent.setup();
    const rejection: ApiDiagnostic = {
      domain: 'workflow.npcItemGift',
      message: 'The NPC Item Gift changes were not staged.',
      severity: 'error'
    };
    const { bridge, workflow } = await createNpcItemGiftHarness();
    bridge.stageNpcItemGift = vi.fn(async () => ({
      diagnostics: [rejection],
      session: {
        hasPendingChanges: true,
        pendingEdits: [],
        sessionId: 'backend-partial-session'
      },
      workflow: {
        ...workflow,
        itemOptions: [],
        stats: { ...workflow.stats, itemOptionCount: 0 }
      }
    }));
    renderNpcItemGift(bridge);

    await user.click(screen.getByRole('button', { name: 'Sonia' }));
    const quantityInput = screen.getByLabelText('Sonia (Stow-on-Side) amount');
    fireEvent.change(quantityInput, { target: { value: '7' } });
    await user.click(screen.getByRole('button', { name: 'Stage NPC' }));

    expect(await screen.findByText(rejection.message)).toBeInTheDocument();
    expect(useWorkbenchStore.getState().editSession).toBeNull();
    expect(useWorkbenchStore.getState().npcItemGiftWorkflow).toBe(workflow);
    expect(quantityInput).toHaveValue('7');
    expect(screen.getByRole('button', { name: 'Stage NPC' })).toBeEnabled();
  });

  it('retains the workflow and visible draft after a stage transport failure', async () => {
    const user = userEvent.setup();
    const { bridge, workflow } = await createNpcItemGiftHarness();
    bridge.stageNpcItemGift = vi.fn(async () => {
      throw new Error('NPC Item Gift stage transport failed.');
    });
    renderNpcItemGift(bridge);

    await user.click(screen.getByRole('button', { name: 'Sonia' }));
    const quantityInput = screen.getByLabelText('Sonia (Stow-on-Side) amount');
    fireEvent.change(quantityInput, { target: { value: '7' } });
    await user.click(screen.getByRole('button', { name: 'Stage NPC' }));

    expect(await screen.findByText(/NPC Item Gift stage transport failed\./)).toBeInTheDocument();
    expect(useWorkbenchStore.getState().editSession).toBeNull();
    expect(useWorkbenchStore.getState().npcItemGiftWorkflow).toBe(workflow);
    expect(quantityInput).toHaveValue('7');
    expect(screen.getByRole('button', { name: 'Stage NPC' })).toBeEnabled();
  });

  it('ignores a late stage response after the draft is discarded and the editor closes', async () => {
    const user = userEvent.setup();
    const { bridge } = await createNpcItemGiftHarness();
    const successfulStage = bridge.stageNpcItemGift;
    let resolveStage!: () => Promise<void>;
    bridge.stageNpcItemGift = vi.fn(
      (request) =>
        new Promise<Awaited<ReturnType<ProjectBridge['stageNpcItemGift']>>>((resolve) => {
          resolveStage = async () => resolve(await successfulStage(request));
        })
    );
    renderNpcItemGift(bridge);

    await user.click(screen.getByRole('button', { name: 'Sonia' }));
    fireEvent.change(screen.getByLabelText('Sonia (Stow-on-Side) amount'), {
      target: { value: '7' }
    });
    await user.click(screen.getByRole('button', { name: 'Stage NPC' }));
    await waitFor(() => expect(bridge.stageNpcItemGift).toHaveBeenCalledTimes(1));

    await user.click(screen.getByRole('button', { name: 'Close Editor' }));
    await user.click(await screen.findByRole('button', { name: 'Yes, Discard' }));
    await waitFor(() => expect(useWorkbenchStore.getState().activeSection).toBe('workflows'));

    await act(async () => {
      await resolveStage();
    });

    expect(useWorkbenchStore.getState().editSession).toBeNull();
    expect(useWorkbenchStore.getState().npcItemGiftWorkflow).toBeNull();
    expect(useWorkbenchStore.getState().activeSection).toBe('workflows');
  });
});
