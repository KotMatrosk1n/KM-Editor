/* SPDX-License-Identifier: GPL-3.0-only */

import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { type ApiDiagnostic, type EditSession } from './bridge/contracts';
import {
  type GymUniformRemovalAction,
  type GymUniformRemovalWorkflow,
  getGymUniformRemovalIpsRelativePath
} from './bridge/gymUniformRemovalContracts';
import { type ProjectBridge } from './bridge/projectBridge';
import { LocalizationProvider } from './localization';
import { createMockProjectBridge } from './testSupport/appTestFixtures';
import { createGymUniformRemovalWorkflow } from './testSupport/gymUniformRemovalTestFixtures';
import { calculatePendingPayloadSha256 } from './utils/pendingPayloadHash';
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

vi.mock('@tauri-apps/api/event', () => ({ listen: tauriEventMock.listen }));

describe('Gym Uniform Removal UI', () => {
  beforeEach(() => {
    window.localStorage.clear();
    tauriEventMock.listen.mockClear();
    for (const eventName of Object.keys(tauriEventMock.listeners)) {
      delete tauriEventMock.listeners[eventName];
    }
    useWorkbenchStore.setState(useWorkbenchStore.getInitialState(), true);
  });

  it('commits only canonical selected-game staging and retains prior state through every rejected or stale response', async () => {
    const user = userEvent.setup();
    const workflow = createGymUniformRemovalWorkflow('sword', true);
    const bridge = createGymHarness(workflow);
    const rendered = renderGym(bridge);

    await user.click(screen.getByRole('button', { name: 'Stage Reinstall' }));
    await waitFor(() => {
      expect(useWorkbenchStore.getState().editSession?.pendingEdits[0]).toMatchObject({
        domain: 'workflow.gymUniformRemoval',
        field: 'install',
        newValue: 'true',
        recordId: 'gym-uniform-removal-v1-install',
        summary: 'Stage Gym Uniform Removal install.'
      });
    });
    expect(useWorkbenchStore.getState().editSession?.pendingEdits[0]?.sources)
      .toEqual(createSession(workflow, 'install').pendingEdits[0]?.sources);
    expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled();

    await user.click(screen.getByRole('button', { name: 'Stage Uninstall' }));
    await waitFor(() => {
      expect(useWorkbenchStore.getState().editSession?.pendingEdits[0]?.field)
        .toBe('uninstall');
    });
    act(() => useWorkbenchStore.getState().setActiveSection('changes'));
    expect(await screen.findByText('Delete generated IPS')).toBeInTheDocument();
    act(() => useWorkbenchStore.getState().setActiveSection('gymUniformRemoval'));

    await user.click(screen.getByRole('button', { name: 'Stage Reinstall' }));
    await waitFor(() => {
      expect(useWorkbenchStore.getState().editSession?.pendingEdits[0]?.field)
        .toBe('install');
    });
    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());
    const priorSession = useWorkbenchStore.getState().editSession!;
    const priorPlan = useWorkbenchStore.getState().changePlan;

    const rejection: ApiDiagnostic = {
      domain: 'workflow.gymUniformRemoval',
      message: 'Gym Uniform Removal uninstall staging was rejected.',
      severity: 'error'
    };
    bridge.stageGymUniformRemovalUninstall = vi.fn(async () => ({
      diagnostics: [rejection],
      session: {
        ...priorSession,
        pendingEdits: priorSession.pendingEdits.map((edit) => ({
          ...edit,
          summary: 'Rejected backend edit.'
        }))
      },
      workflow: { ...workflow, installMessage: 'Rejected backend workflow.' }
    }));
    await user.click(screen.getByRole('button', { name: 'Stage Uninstall' }));
    expect(await screen.findByText(rejection.message)).toBeInTheDocument();
    expectRetainedState(workflow, priorSession, priorPlan);

    await stageDeferredUninstall(bridge, user, {
      diagnostics: [],
      session: createSession(workflow, 'uninstall', 'another-session'),
      workflow
    });
    expect(await screen.findByText(canonicalFailureMessage)).toBeInTheDocument();
    expectRetainedState(workflow, priorSession, priorPlan);

    const wrongGameWorkflow = createGymUniformRemovalWorkflow('shield', true);
    await stageDeferredUninstall(bridge, user, {
      diagnostics: [],
      session: createSession(wrongGameWorkflow, 'uninstall', priorSession.sessionId),
      workflow: wrongGameWorkflow
    });
    expect(await screen.findByText(canonicalFailureMessage)).toBeInTheDocument();
    expectRetainedState(workflow, priorSession, priorPlan);

    await stageDeferredUninstall(bridge, user, {
      diagnostics: [],
      session: {
        ...createSession(workflow, 'uninstall', priorSession.sessionId),
        sessionId: ''
      },
      workflow
    });
    expect(await screen.findByText(canonicalFailureMessage)).toBeInTheDocument();
    expectRetainedState(workflow, priorSession, priorPlan);

    const canonicalStage = async () => ({
      diagnostics: [
        {
          message: 'Gym Uniform Removal uninstall is staged for change-plan review.',
          severity: 'info' as const
        }
      ],
      session: createSession(workflow, 'uninstall', priorSession.sessionId),
      workflow
    });
    let resolveStage!: () => Promise<void>;
    bridge.stageGymUniformRemovalUninstall = vi.fn(
      () =>
        new Promise<Awaited<ReturnType<ProjectBridge['stageGymUniformRemovalUninstall']>>>(
          (resolve) => {
            resolveStage = async () => resolve(await canonicalStage());
          }
        )
    );
    await user.click(screen.getByRole('button', { name: 'Stage Uninstall' }));
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Staging uninstall' }))
        .toHaveAttribute('aria-busy', 'true');
      expect(screen.getByRole('button', { name: 'Close Editor' })).toBeEnabled();
      expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();
    });

    act(() => useWorkbenchStore.getState().setActiveSection('health'));
    await user.clear(screen.getByLabelText('Base RomFS'));
    await user.type(screen.getByLabelText('Base RomFS'), 'replacement-romfs');
    expect(useWorkbenchStore.getState().gymUniformRemovalWorkflow).toBeNull();

    await act(async () => resolveStage());
    expect(useWorkbenchStore.getState().editSession).toBeNull();
    expect(useWorkbenchStore.getState().gymUniformRemovalWorkflow).toBeNull();
    expect(useWorkbenchStore.getState().activeSection).toBe('health');

    rendered.unmount();
    useWorkbenchStore.setState(useWorkbenchStore.getInitialState(), true);
    const rejectingBridge = createGymHarness(workflow);
    let rejectStage!: () => void;
    rejectingBridge.stageGymUniformRemovalUninstall = vi.fn(
      () =>
        new Promise<never>((_resolve, reject) => {
          rejectStage = () => reject(new Error('stale Gym Uniform Removal failure'));
        })
    );
    renderGym(rejectingBridge);

    await user.click(screen.getByRole('button', { name: 'Stage Uninstall' }));
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Staging uninstall' }))
        .toHaveAttribute('aria-busy', 'true');
    });
    act(() => useWorkbenchStore.getState().setActiveSection('health'));
    await user.clear(screen.getByLabelText('Base RomFS'));
    await user.type(screen.getByLabelText('Base RomFS'), 'another-romfs');

    await act(async () => rejectStage());
    expect(screen.queryByText(/stale Gym Uniform Removal failure/i))
      .not.toBeInTheDocument();
    expect(useWorkbenchStore.getState().editSession).toBeNull();
    expect(useWorkbenchStore.getState().gymUniformRemovalWorkflow).toBeNull();
  });
});

const canonicalFailureMessage =
  'Gym Uniform Removal staging did not match the requested action, game, session, source, and IPS artifact state.';

async function stageDeferredUninstall(
  bridge: ProjectBridge,
  user: ReturnType<typeof userEvent.setup>,
  response: Awaited<ReturnType<ProjectBridge['stageGymUniformRemovalUninstall']>>
) {
  let resolveStage!: () => void;
  const stage = vi.fn(
    () =>
      new Promise<Awaited<ReturnType<ProjectBridge['stageGymUniformRemovalUninstall']>>>(
        (resolve) => {
          resolveStage = () => resolve(response);
        }
      )
  );
  bridge.stageGymUniformRemovalUninstall = stage;

  await user.click(screen.getByRole('button', { name: 'Stage Uninstall' }));
  await waitFor(() => {
    expect(stage).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('button', { name: 'Staging uninstall' }))
      .toHaveAttribute('aria-busy', 'true');
  });
  expect(screen.queryByText(canonicalFailureMessage)).not.toBeInTheDocument();

  await act(async () => resolveStage());
  await waitFor(() => {
    expect(screen.getByRole('button', { name: 'Stage Uninstall' })).toBeEnabled();
  });
}

function expectRetainedState(
  workflow: GymUniformRemovalWorkflow,
  session: EditSession,
  plan: ReturnType<typeof useWorkbenchStore.getState>['changePlan']
) {
  expect(useWorkbenchStore.getState().editSession).toBe(session);
  expect(useWorkbenchStore.getState().gymUniformRemovalWorkflow).toBe(workflow);
  expect(useWorkbenchStore.getState().changePlan).toBe(plan);
  expect(screen.getByRole('heading', { name: 'Output Plan' })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled();
}

function createGymHarness(workflow: GymUniformRemovalWorkflow) {
  const bridge = createMockProjectBridge({}, true);
  bridge.loadGymUniformRemovalWorkflow = vi.fn(async () => ({ workflow }));
  bridge.stageGymUniformRemovalInstall = vi.fn(async (request) => ({
    diagnostics: [
      {
        message: 'Gym Uniform Removal install is staged for change-plan review.',
        severity: 'info' as const
      }
    ],
    session: createSession(workflow, 'install', request.session?.sessionId),
    workflow
  }));
  bridge.stageGymUniformRemovalUninstall = vi.fn(async (request) => ({
    diagnostics: [
      {
        message: 'Gym Uniform Removal uninstall is staged for change-plan review.',
        severity: 'info' as const
      }
    ],
    session: createSession(workflow, 'uninstall', request.session?.sessionId),
    workflow
  }));

  useWorkbenchStore.setState({
    activeSection: 'gymUniformRemoval',
    applyResult: null,
    changePlan: null,
    draftPaths: createPaths('sword'),
    editSession: null,
    editValidationDiagnostics: [],
    gymUniformRemovalWorkflow: workflow
  });
  return bridge;
}

function renderGym(bridge: ProjectBridge) {
  return render(
    <LocalizationProvider>
      <App bridge={bridge} />
    </LocalizationProvider>
  );
}

function createSession(
  workflow: GymUniformRemovalWorkflow,
  action: GymUniformRemovalAction,
  existingSessionId?: string
): EditSession {
  const game = workflow.detectedGame ?? 'sword';
  return {
    hasPendingChanges: true,
    pendingEdits: [
      {
        domain: 'workflow.gymUniformRemoval',
        field: action,
        newValue: 'true',
        recordId: `gym-uniform-removal-v1-${action}`,
        sources: [
          { layer: 'base', relativePath: 'exefs/main' },
          {
            layer: 'pending',
            relativePath:
              `pending/gym-uniform-removal/${action}/${calculatePendingPayloadSha256('true')}`
          },
          {
            layer: 'generated',
            relativePath: getGymUniformRemovalIpsRelativePath(game)
          }
        ],
        summary: `Stage Gym Uniform Removal ${action}.`
      }
    ],
    sessionId: existingSessionId ?? `session-gym-uniform-removal-${action}`
  };
}

function createPaths(selectedGame: 'sword' | 'shield') {
  return {
    baseExeFsPath: 'base-exefs',
    baseRomFsPath: 'base-romfs',
    outputRootPath: 'output',
    pokemonLegendsZASupportFolderPath: '',
    saveFilePath: '',
    scarletVioletSupportFolderPath: '',
    selectedGame
  };
}
