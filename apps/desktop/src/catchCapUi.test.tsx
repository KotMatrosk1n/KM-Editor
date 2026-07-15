/* SPDX-License-Identifier: GPL-3.0-only */

import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import type { ApiDiagnostic, CatchCapWorkflow } from './bridge/contracts';
import type { ProjectBridge } from './bridge/projectBridge';
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

async function createCatchCapHarness() {
  const fixtureBridge = createMockProjectBridge({}, true);
  const response = await fixtureBridge.loadCatchCapWorkflow({ paths: projectPaths });
  const bridge = createMockProjectBridge({}, true);

  useWorkbenchStore.setState({
    activeSection: 'catchCap',
    applyResult: null,
    catchCapWorkflow: {
      ...response.workflow,
      caps: [...response.workflow.caps].reverse()
    },
    changePlan: null,
    draftPaths: projectPaths,
    editSession: null,
    editValidationDiagnostics: [],
    selectedCatchCapBadgeCount: 0
  });

  return { bridge, workflow: response.workflow };
}

function renderCatchCap(bridge: ProjectBridge) {
  return render(
    <LocalizationProvider>
      <App bridge={bridge} />
    </LocalizationProvider>
  );
}

describe('Catch Cap UI staging', () => {
  beforeEach(() => {
    window.localStorage.clear();
    tauriEventMock.listen.mockClear();
    for (const eventName of Object.keys(tauriEventMock.listeners)) {
      delete tauriEventMock.listeners[eventName];
    }
    useWorkbenchStore.setState(useWorkbenchStore.getInitialState(), true);
  });

  it('keeps the staged identity visible and safe across draft, rejection, navigation, keyboard, and stale-response flows', async () => {
    const user = userEvent.setup();
    const rejection: ApiDiagnostic = {
      domain: 'workflow.catchCap',
      message: 'Catch Cap values were not staged.',
      severity: 'error'
    };
    const { bridge } = await createCatchCapHarness();
    const successfulStage = bridge.stageCatchCap;
    const stageCatchCap = vi.fn(successfulStage);
    bridge.stageCatchCap = stageCatchCap;
    renderCatchCap(bridge);

    const secondBadgeInput = screen.getByRole('spinbutton', {
      name: 'Catch cap for Second badge'
    });
    await user.click(secondBadgeInput);
    const secondBadgeRow = secondBadgeInput.closest<HTMLElement>('[role="row"]');
    await waitFor(() => expect(secondBadgeRow).toHaveAttribute('aria-selected', 'true'));
    expect(fireEvent.keyDown(secondBadgeInput, { key: 'ArrowUp' })).toBe(true);
    expect(secondBadgeInput).toHaveFocus();
    expect(secondBadgeRow).toHaveAttribute('aria-selected', 'true');

    fireEvent.change(secondBadgeInput, { target: { value: '31' } });
    await user.click(screen.getByRole('button', { name: 'Stage Caps' }));
    await waitFor(() => expect(stageCatchCap).toHaveBeenCalledTimes(1));

    expect(stageCatchCap.mock.calls[0]![0].caps.map((cap) => cap.badgeCount)).toEqual([
      0, 1, 2, 3, 4, 5, 6, 7, 8
    ]);
    expect(secondBadgeInput).toHaveValue(31);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());

    fireEvent.change(secondBadgeInput, { target: { value: '32' } });
    expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();

    const sessionBeforeRejection = useWorkbenchStore.getState().editSession;
    const workflowBeforeRejection = useWorkbenchStore.getState().catchCapWorkflow;
    const planBeforeRejection = useWorkbenchStore.getState().changePlan;
    stageCatchCap.mockImplementationOnce(async () => ({
      diagnostics: [rejection],
      session: {
        ...sessionBeforeRejection!,
        pendingEdits: [
          { ...sessionBeforeRejection!.pendingEdits[0]!, summary: 'Rejected backend mutation.' }
        ]
      },
      workflow: {
        ...workflowBeforeRejection!,
        installMessage: 'Rejected backend workflow.',
        installStatus: 'blocked'
      }
    }));
    await user.click(screen.getByRole('button', { name: 'Stage Caps' }));
    expect(await screen.findByText(rejection.message)).toBeInTheDocument();
    expect(useWorkbenchStore.getState().editSession).toEqual(sessionBeforeRejection);
    expect(useWorkbenchStore.getState().catchCapWorkflow).toBe(workflowBeforeRejection);
    expect(useWorkbenchStore.getState().changePlan).toBe(planBeforeRejection);
    expect(secondBadgeInput).toHaveValue(32);

    fireEvent.change(secondBadgeInput, { target: { value: '31' } });
    expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled();

    const eighthBadgeRow = screen.getByText('Eighth badge').closest<HTMLElement>('[role="row"]');
    expect(eighthBadgeRow).not.toBeNull();
    eighthBadgeRow!.focus();
    await user.keyboard('{Enter}');
    expect(eighthBadgeRow).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('spinbutton', { name: 'Catch cap for Eighth badge' })).toBeDisabled();

    await user.click(screen.getByRole('button', { name: 'Close Editor' }));
    await user.click(await screen.findByRole('button', { name: 'No' }));
    await user.click(await screen.findByRole('button', { name: 'Go To Changes' }));
    await user.click(screen.getByRole('button', { name: 'Open Catch Cap' }));
    expect(
      await screen.findByRole('spinbutton', { name: 'Catch cap for Second badge' })
    ).toHaveValue(31);

    let resolveLateStage!: () => Promise<void>;
    bridge.stageCatchCap = vi.fn(
      (request) =>
        new Promise<Awaited<ReturnType<ProjectBridge['stageCatchCap']>>>((resolve) => {
          resolveLateStage = async () => resolve(await successfulStage(request));
        })
    );
    fireEvent.change(
      screen.getByRole('spinbutton', { name: 'Catch cap for Second badge' }),
      { target: { value: '32' } }
    );
    await user.click(screen.getByRole('button', { name: 'Stage Caps' }));
    await waitFor(() => expect(bridge.stageCatchCap).toHaveBeenCalledTimes(1));
    await user.click(screen.getByRole('button', { name: 'Close Editor' }));
    await user.click(await screen.findByRole('button', { name: 'Yes, Discard' }));
    await waitFor(() => expect(useWorkbenchStore.getState().activeSection).toBe('workflows'));

    await act(async () => {
      await resolveLateStage();
    });

    expect(useWorkbenchStore.getState().editSession).toBeNull();
    expect(useWorkbenchStore.getState().catchCapWorkflow).toBeNull();
    expect(useWorkbenchStore.getState().activeSection).toBe('workflows');
  });

  it('keeps malformed installed bytes repairable and does not let cap errors block uninstall', async () => {
    const user = userEvent.setup();
    const { bridge, workflow } = await createCatchCapHarness();
    const malformedWorkflow: CatchCapWorkflow = {
      ...workflow,
      caps: workflow.caps.map((cap) =>
        cap.badgeCount === 1 ? { ...cap, levelCap: 200 } : cap
      ),
      installMessage: 'Catch Cap Editor hook is installed with malformed owned cap bytes.',
      installStatus: 'installed',
      provenance: {
        fileState: 'layeredOverride',
        sourceFile: 'exefs/main',
        sourceLayer: 'layered'
      }
    };
    useWorkbenchStore.setState({ catchCapWorkflow: malformedWorkflow });
    const successfulUninstall = bridge.stageCatchCapUninstall;
    bridge.stageCatchCapUninstall = vi.fn(async (request) => ({
      ...(await successfulUninstall(request)),
      workflow: malformedWorkflow
    }));
    renderCatchCap(bridge);

    const firstBadgeInput = screen.getByRole('spinbutton', {
      name: 'Catch cap for First badge'
    });
    expect(firstBadgeInput).toHaveValue(200);
    expect(screen.getByText('Use Lv. 1-100.')).toBeInTheDocument();
    expect(screen.getByText('Must be Lv. 200 or higher.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Stage Caps' })).toBeDisabled();

    fireEvent.change(firstBadgeInput, { target: { value: '25' } });
    expect(screen.getByRole('button', { name: 'Stage Caps' })).toBeEnabled();
    fireEvent.change(firstBadgeInput, { target: { value: '200' } });
    expect(screen.getByRole('button', { name: 'Stage Caps' })).toBeDisabled();

    await user.click(screen.getByRole('button', { name: 'Stage Uninstall' }));
    await waitFor(() => expect(bridge.stageCatchCapUninstall).toHaveBeenCalledTimes(1));
    expect(firstBadgeInput).toHaveValue(200);
    expect(screen.getByText('Must be Lv. 200 or higher.')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());

    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());
  });
});
