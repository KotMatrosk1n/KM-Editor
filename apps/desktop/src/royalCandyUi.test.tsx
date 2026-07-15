/* SPDX-License-Identifier: GPL-3.0-only */

import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { type ApiDiagnostic, type RoyalCandyWorkflow } from './bridge/contracts';
import { type ProjectBridge } from './bridge/projectBridge';
import { type DesktopServices } from './desktopServices';
import { languageStorageKey, LocalizationProvider } from './localization';
import {
  createMockDesktopServices,
  createMockProjectBridge
} from './testSupport/appTestFixtures';
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

async function createRoyalCandyHarness(
  mutateWorkflow?: (workflow: RoyalCandyWorkflow) => RoyalCandyWorkflow
) {
  const fixtureBridge = createMockProjectBridge({}, true);
  const response = await fixtureBridge.loadRoyalCandyWorkflow({ paths: projectPaths });
  const workflow = mutateWorkflow?.(response.workflow) ?? response.workflow;
  const bridge = createMockProjectBridge({}, true);

  useWorkbenchStore.setState({
    activeSection: 'royalCandy',
    applyResult: null,
    changePlan: null,
    draftPaths: projectPaths,
    editSession: null,
    editValidationDiagnostics: [],
    royalCandySearchText: '',
    royalCandyWorkflow: workflow,
    selectedRoyalCandyCheckId: workflow.checks[0]?.checkId ?? null,
    selectedRoyalCandyWorkflowId: workflow.workflows[0]?.workflowId ?? null
  });

  return { bridge, workflow };
}

function renderRoyalCandy(bridge: ProjectBridge, desktopServices?: DesktopServices) {
  return render(
    <LocalizationProvider>
      <App bridge={bridge} desktopServices={desktopServices} />
    </LocalizationProvider>
  );
}

async function selectStoryLimitsWorkflow(user: ReturnType<typeof userEvent.setup>) {
  await user.click(
    await screen.findByRole('row', { name: /Royal Candy with Story Limits/ })
  );
  return within(screen.getByLabelText('Selected Royal Candy workflow provenance'));
}

describe('Royal Candy UI', () => {
  beforeEach(() => {
    window.localStorage.clear();
    tauriEventMock.listen.mockClear();
    for (const eventName of Object.keys(tauriEventMock.listeners)) {
      delete tauriEventMock.listeners[eventName];
    }
    useWorkbenchStore.setState(useWorkbenchStore.getInitialState(), true);
  });

  it('constrains selection to matches and preserves search on workflow refresh', async () => {
    const user = userEvent.setup();
    const { bridge, workflow } = await createRoyalCandyHarness();
    renderRoyalCandy(bridge);

    const search = await screen.findByRole('searchbox', {
      name: 'Search Royal Candy workflows'
    });
    await user.type(search, 'no matching Royal Candy record');

    expect(screen.getByRole('status')).toHaveTextContent('No matching Royal Candy records.');
    expect(screen.queryByLabelText('Selected Royal Candy workflow provenance')).toBeNull();
    await waitFor(() => {
      expect(useWorkbenchStore.getState().selectedRoyalCandyWorkflowId).toBeNull();
      expect(useWorkbenchStore.getState().selectedRoyalCandyCheckId).toBeNull();
    });

    act(() => useWorkbenchStore.getState().setRoyalCandyWorkflow(workflow));
    expect(search).toHaveValue('no matching Royal Candy record');
  });

  it('shows the failing preflight count', async () => {
    const { bridge } = await createRoyalCandyHarness((workflow) => ({
      ...workflow,
      stats: { ...workflow.stats, failCount: 2 }
    }));
    renderRoyalCandy(bridge);

    const failingLabel = await screen.findByText('Failing');
    expect(failingLabel.parentElement).toHaveTextContent('2');
  });

  it('localizes workflow names, search chrome, and cap validation', async () => {
    window.localStorage.setItem(languageStorageKey, 'es');
    const user = userEvent.setup();
    const { bridge } = await createRoyalCandyHarness();
    renderRoyalCandy(bridge);

    const search = await screen.findByRole('searchbox', {
      name: 'Buscar flujos de Caramelo Royal'
    });
    await user.type(search, 'Caramelo Royal con límites de historia');
    expect(
      screen.getByRole('row', { name: /Caramelo Royal con límites de historia/ })
    ).toBeInTheDocument();
    expect(screen.queryByRole('row', { name: /Caramelo Royal ilimitado/ })).toBeNull();
    await user.click(
      screen.getByRole('row', { name: /Caramelo Royal con límites de historia/ })
    );
    const capInput = screen.getByRole('spinbutton', {
      name: 'Límite de nivel después de derrotar a Hop 004/005/006'
    });

    fireEvent.change(capInput, { target: { value: '10.5' } });
    expect(screen.getByText('Introduce un límite de nivel entero.')).toBeInTheDocument();
  });

  it('confirms before Close Editor discards an unstaged cap draft', async () => {
    const user = userEvent.setup();
    const { bridge } = await createRoyalCandyHarness();
    renderRoyalCandy(bridge);
    const inspector = await selectStoryLimitsWorkflow(user);
    const capInput = inspector.getByRole('spinbutton', {
      name: 'Level cap after defeating Hop 004/005/006'
    });

    fireEvent.change(capInput, { target: { value: '11' } });
    await user.click(screen.getByRole('button', { name: 'Close Editor' }));

    expect(
      await screen.findByRole('heading', { name: 'Discard Pending Changes?' })
    ).toBeInTheDocument();
    expect(useWorkbenchStore.getState().activeSection).toBe('royalCandy');
    await user.click(screen.getByRole('button', { name: 'Stay Here' }));
    expect(capInput).toHaveValue(11);

    await user.click(screen.getByRole('button', { name: 'Close Editor' }));
    await user.click(screen.getByRole('button', { name: 'Yes, Discard' }));
    await waitFor(() => expect(useWorkbenchStore.getState().activeSection).toBe('workflows'));
  });

  it('guards desktop window close and confirms an unstaged cap draft', async () => {
    const user = userEvent.setup();
    const exitApp = vi.fn(async () => undefined);
    const setCloseGuardEnabled = vi.fn(async () => undefined);
    const desktopServices = createMockDesktopServices({ exitApp, setCloseGuardEnabled });
    const { bridge } = await createRoyalCandyHarness();
    renderRoyalCandy(bridge, desktopServices);
    const inspector = await selectStoryLimitsWorkflow(user);
    const capInput = inspector.getByRole('spinbutton', {
      name: 'Level cap after defeating Hop 004/005/006'
    });

    fireEvent.change(capInput, { target: { value: '11' } });
    await waitFor(() => expect(setCloseGuardEnabled).toHaveBeenLastCalledWith(true));
    await waitFor(() =>
      expect(tauriEventMock.listeners['km-editor://window-close-requested']).toHaveLength(1)
    );

    act(() => {
      tauriEventMock.listeners['km-editor://window-close-requested']?.forEach((handler) =>
        handler()
      );
    });

    expect(
      await screen.findByRole('heading', { name: 'Discard Pending Changes?' })
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Stay Here' })).toBeInTheDocument();
    expect(exitApp).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Yes, Discard' }));
    await waitFor(() => expect(exitApp).toHaveBeenCalledTimes(1));
    expect(setCloseGuardEnabled).toHaveBeenLastCalledWith(false);
  });

  it.each(['10.5', '1e2'])('rejects a non-integer cap value of %s', async (value) => {
    const user = userEvent.setup();
    const { bridge } = await createRoyalCandyHarness();
    renderRoyalCandy(bridge);
    const inspector = await selectStoryLimitsWorkflow(user);
    const capInput = inspector.getByRole('spinbutton', {
      name: 'Level cap after defeating Hop 004/005/006'
    });

    fireEvent.change(capInput, { target: { value } });

    expect(inspector.getByText('Enter a whole-number level cap.')).toBeInTheDocument();
    expect(inspector.getByRole('button', { name: 'Stage' })).toBeDisabled();
  });

  it('allows an installed story-limit workflow to be edited and staged', async () => {
    const user = userEvent.setup();
    const { bridge } = await createRoyalCandyHarness((workflow) => ({
      ...workflow,
      workflows: workflow.workflows.map((candidate) =>
        candidate.workflowId === 'royal-candy-story-limits'
          ? { ...candidate, status: 'installed' }
          : candidate
      )
    }));
    const stageRoyalCandyWorkflow = vi.spyOn(bridge, 'stageRoyalCandyWorkflow');
    renderRoyalCandy(bridge);
    const inspector = await selectStoryLimitsWorkflow(user);
    const capInput = inspector.getByRole('spinbutton', {
      name: 'Level cap after defeating Hop 004/005/006'
    });

    expect(capInput).toBeEnabled();
    fireEvent.change(capInput, { target: { value: '11' } });
    await user.click(inspector.getByRole('button', { name: 'Stage' }));

    await waitFor(() => expect(stageRoyalCandyWorkflow).toHaveBeenCalledTimes(1));
    expect(stageRoyalCandyWorkflow.mock.calls[0]?.[0].levelCaps?.[0]).toEqual({
      levelCap: 11,
      slot: 0
    });
  });

  it('disables review and apply when staged caps have newer local edits', async () => {
    const user = userEvent.setup();
    const { bridge } = await createRoyalCandyHarness();
    renderRoyalCandy(bridge);
    const inspector = await selectStoryLimitsWorkflow(user);

    await user.click(inspector.getByRole('button', { name: 'Stage' }));
    await waitFor(() => expect(inspector.getByRole('button', { name: 'Review' })).toBeEnabled());
    await user.click(inspector.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(inspector.getByRole('button', { name: 'Apply' })).toBeEnabled());

    fireEvent.change(
      inspector.getByRole('spinbutton', {
        name: 'Level cap after defeating Hop 004/005/006'
      }),
      { target: { value: '11' } }
    );

    expect(inspector.getByRole('button', { name: 'Stage' })).toBeEnabled();
    expect(inspector.getByRole('button', { name: 'Review' })).toBeDisabled();
    expect(inspector.getByRole('button', { name: 'Apply' })).toBeDisabled();
  });

  it('retains the staged session, workflow, and reviewed plan after backend rejection', async () => {
    const user = userEvent.setup();
    const rejection: ApiDiagnostic = {
      domain: 'workflow.royalCandy',
      message: 'The Royal Candy workflow was not staged.',
      severity: 'error'
    };
    const { bridge } = await createRoyalCandyHarness();
    const successfulStage = bridge.stageRoyalCandyWorkflow;
    const stageRoyalCandyWorkflow = vi
      .fn(successfulStage)
      .mockImplementationOnce(successfulStage);
    bridge.stageRoyalCandyWorkflow = stageRoyalCandyWorkflow;
    renderRoyalCandy(bridge);
    const inspector = await selectStoryLimitsWorkflow(user);
    const capInput = inspector.getByRole('spinbutton', {
      name: 'Level cap after defeating Hop 004/005/006'
    });

    await user.click(inspector.getByRole('button', { name: 'Stage' }));
    await waitFor(() => expect(inspector.getByRole('button', { name: 'Review' })).toBeEnabled());
    await user.click(inspector.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(inspector.getByRole('button', { name: 'Apply' })).toBeEnabled());

    const sessionBeforeRejection = useWorkbenchStore.getState().editSession;
    const workflowBeforeRejection = useWorkbenchStore.getState().royalCandyWorkflow;
    stageRoyalCandyWorkflow.mockImplementationOnce(async () => ({
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
        workflows: workflowBeforeRejection!.workflows.map((candidate) => ({
          ...candidate,
          status: 'blocked' as const
        }))
      }
    }));

    fireEvent.change(capInput, { target: { value: '11' } });
    await user.click(inspector.getByRole('button', { name: 'Stage' }));
    await waitFor(() => expect(stageRoyalCandyWorkflow).toHaveBeenCalledTimes(2));

    expect(useWorkbenchStore.getState().editSession).toEqual(sessionBeforeRejection);
    expect(useWorkbenchStore.getState().royalCandyWorkflow).toBe(workflowBeforeRejection);
    fireEvent.change(capInput, { target: { value: '10' } });
    expect(inspector.getByRole('button', { name: 'Apply' })).toBeEnabled();
  });

  it('retains the staged session, workflow, and reviewed plan after transport failure', async () => {
    const user = userEvent.setup();
    const { bridge } = await createRoyalCandyHarness();
    renderRoyalCandy(bridge);
    const inspector = await selectStoryLimitsWorkflow(user);
    const capInput = inspector.getByRole('spinbutton', {
      name: 'Level cap after defeating Hop 004/005/006'
    });

    await user.click(inspector.getByRole('button', { name: 'Stage' }));
    await waitFor(() => expect(inspector.getByRole('button', { name: 'Review' })).toBeEnabled());
    await user.click(inspector.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(inspector.getByRole('button', { name: 'Apply' })).toBeEnabled());

    const sessionBeforeFailure = useWorkbenchStore.getState().editSession;
    const workflowBeforeFailure = useWorkbenchStore.getState().royalCandyWorkflow;
    const failedStage = vi.fn(async () => {
      throw new Error('Royal Candy stage transport failed.');
    });
    bridge.stageRoyalCandyWorkflow = failedStage;

    fireEvent.change(capInput, { target: { value: '11' } });
    await user.click(inspector.getByRole('button', { name: 'Stage' }));
    await waitFor(() => expect(failedStage).toHaveBeenCalledTimes(1));

    expect(await screen.findByText(/Royal Candy stage transport failed\./)).toBeInTheDocument();
    expect(useWorkbenchStore.getState().editSession).toEqual(sessionBeforeFailure);
    expect(useWorkbenchStore.getState().royalCandyWorkflow).toBe(workflowBeforeFailure);
    fireEvent.change(capInput, { target: { value: '10' } });
    expect(inspector.getByRole('button', { name: 'Apply' })).toBeEnabled();
  });
});
