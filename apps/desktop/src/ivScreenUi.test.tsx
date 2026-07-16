/* SPDX-License-Identifier: GPL-3.0-only */

import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import type { ApiDiagnostic, EditSession, IvScreenWorkflow } from './bridge/contracts';
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

async function createIvScreenHarness() {
  const fixtureBridge = createMockProjectBridge({}, true);
  const response = await fixtureBridge.loadIvScreenWorkflow({ paths: projectPaths });
  const bridge = createMockProjectBridge({}, true);

  useWorkbenchStore.setState({
    activeSection: 'ivScreen',
    applyResult: null,
    changePlan: null,
    draftPaths: projectPaths,
    editSession: null,
    editValidationDiagnostics: [],
    ivScreenWorkflow: response.workflow
  });

  return { bridge, workflow: response.workflow };
}

function renderIvScreen(bridge: ProjectBridge) {
  return render(
    <LocalizationProvider>
      <App bridge={bridge} />
    </LocalizationProvider>
  );
}

function createInstalledWorkflow(
  workflow: IvScreenWorkflow,
  canUninstall: boolean
): IvScreenWorkflow {
  return {
    ...workflow,
    canUninstall,
    installMessage: 'IV Screen is installed.',
    installStatus: 'installed',
    provenance: {
      fileState: 'layeredOverride',
      sourceFile: 'exefs/main',
      sourceLayer: 'layered'
    }
  };
}

describe('IV Screen UI staging', () => {
  beforeEach(() => {
    window.localStorage.clear();
    tauriEventMock.listen.mockClear();
    for (const eventName of Object.keys(tauriEventMock.listeners)) {
      delete tauriEventMock.listeners[eventName];
    }
    useWorkbenchStore.setState(useWorkbenchStore.getInitialState(), true);
  });

  it('shows the detected build and exact patch sites and trusts authoritative uninstall capability', async () => {
    const { bridge, workflow } = await createIvScreenHarness();
    renderIvScreen(bridge);

    expect(screen.getAllByText('Pokemon Sword').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText(workflow.buildId).length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('Primary value source')).toBeInTheDocument();
    expect(screen.getByText('main.text+0x0138A2B4')).toBeInTheDocument();
    expect(screen.getByText('X-toggle refresh')).toBeInTheDocument();
    expect(screen.getByText('main.text+0x0138B3AC')).toBeInTheDocument();
    expect(screen.queryByText('Hook site')).not.toBeInTheDocument();

    const uninstallButton = screen.getByRole('button', { name: 'Stage Uninstall' });
    expect(uninstallButton).toBeDisabled();

    act(() => {
      useWorkbenchStore.setState({
        ivScreenWorkflow: createInstalledWorkflow(workflow, true)
      });
    });
    expect(uninstallButton).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Stage Reinstall' })).toBeEnabled();

    act(() => {
      useWorkbenchStore.setState({
        ivScreenWorkflow: {
          ...createInstalledWorkflow(workflow, true),
          installMessage: 'Legacy install cannot be migrated until dependencies are restored.',
          installStatus: 'blocked'
        }
      });
    });
    expect(screen.getByRole('button', { name: 'Stage Reinstall' })).toBeDisabled();

    act(() => {
      useWorkbenchStore.setState({
        ivScreenWorkflow: createInstalledWorkflow(workflow, false)
      });
    });
    expect(uninstallButton).toBeDisabled();
  });

  it.each([
    [
      'summary-only mutation',
      (session: EditSession): EditSession => ({
        ...session,
        pendingEdits: session.pendingEdits.map((edit) => ({
          ...edit,
          summary: `${edit.summary} forged`
        }))
      })
    ],
    [
      'source-layer mutation',
      (session: EditSession): EditSession => ({
        ...session,
        pendingEdits: session.pendingEdits.map((edit) => ({
          ...edit,
          sources: edit.sources.map((source, index) =>
            index === 0 ? { ...source, layer: 'generated' as const } : source
          )
        }))
      })
    ]
  ])('invalidates reviewed plan currency after a %s', async (_label, mutateSession) => {
    const user = userEvent.setup();
    const { bridge } = await createIvScreenHarness();
    renderIvScreen(bridge);

    await user.click(screen.getByRole('button', { name: 'Stage Install' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());
    expect(screen.getByRole('heading', { name: 'Output Plan' })).toBeInTheDocument();

    act(() => {
      const currentSession = useWorkbenchStore.getState().editSession;
      useWorkbenchStore.setState({ editSession: mutateSession(currentSession!) });
    });

    await waitFor(() =>
      expect(screen.queryByRole('heading', { name: 'Output Plan' })).not.toBeInTheDocument()
    );
    expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();
  });

  it('recognizes only a canonical one-edit session and preserves state when staging is rejected', async () => {
    const user = userEvent.setup();
    const rejection: ApiDiagnostic = {
      domain: 'workflow.ivScreen',
      message: 'IV Screen uninstall was not staged.',
      severity: 'error'
    };
    const { bridge, workflow } = await createIvScreenHarness();
    const successfulInstall = bridge.stageIvScreenInstall;
    const stagedFixture = await successfulInstall({ paths: projectPaths, session: null });
    const malformedSession: EditSession = {
      ...stagedFixture.session,
      pendingEdits: [
        {
          ...stagedFixture.session.pendingEdits[0]!,
          field: 'uninstall'
        }
      ]
    };
    useWorkbenchStore.setState({ editSession: malformedSession });
    bridge.stageIvScreenInstall = vi.fn(successfulInstall);
    renderIvScreen(bridge);

    expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
    expect(screen.getByText('Staged change').parentElement).toHaveTextContent('None');
    act(() => {
      useWorkbenchStore.setState({ editSession: null });
    });

    await user.click(screen.getByRole('button', { name: 'Stage Install' }));
    await waitFor(() => expect(bridge.stageIvScreenInstall).toHaveBeenCalledTimes(1));
    expect(screen.getByText('Install or refresh')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());

    act(() => {
      useWorkbenchStore.setState({
        ivScreenWorkflow: createInstalledWorkflow(workflow, true)
      });
    });
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Stage Uninstall' })).toBeEnabled()
    );

    const sessionBeforeRejection = useWorkbenchStore.getState().editSession;
    const workflowBeforeRejection = useWorkbenchStore.getState().ivScreenWorkflow;
    const planBeforeRejection = useWorkbenchStore.getState().changePlan;
    bridge.stageIvScreenUninstall = vi.fn(async () => ({
      diagnostics: [rejection],
      session: {
        ...sessionBeforeRejection!,
        pendingEdits: [
          {
            ...sessionBeforeRejection!.pendingEdits[0]!,
            summary: 'Rejected backend mutation.'
          }
        ]
      },
      workflow: {
        ...workflowBeforeRejection!,
        installMessage: 'Rejected backend workflow.',
        installStatus: 'blocked' as const
      }
    }));

    await user.click(screen.getByRole('button', { name: 'Stage Uninstall' }));
    expect(await screen.findByText(rejection.message)).toBeInTheDocument();
    expect(useWorkbenchStore.getState().editSession).toEqual(sessionBeforeRejection);
    expect(useWorkbenchStore.getState().ivScreenWorkflow).toBe(workflowBeforeRejection);
    expect(useWorkbenchStore.getState().changePlan).toBe(planBeforeRejection);
    expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled();
  });

  it('ignores a successful staging response that arrives after the editor session is discarded', async () => {
    const user = userEvent.setup();
    const { bridge, workflow } = await createIvScreenHarness();
    const successfulUninstall = bridge.stageIvScreenUninstall;
    renderIvScreen(bridge);

    await user.click(screen.getByRole('button', { name: 'Stage Install' }));
    await waitFor(() => expect(useWorkbenchStore.getState().editSession).not.toBeNull());
    act(() => {
      useWorkbenchStore.setState({
        ivScreenWorkflow: createInstalledWorkflow(workflow, true)
      });
    });

    let resolveLateStage!: () => Promise<void>;
    bridge.stageIvScreenUninstall = vi.fn(
      (request) =>
        new Promise<Awaited<ReturnType<ProjectBridge['stageIvScreenUninstall']>>>((resolve) => {
          resolveLateStage = async () => resolve(await successfulUninstall(request));
        })
    );

    await user.click(screen.getByRole('button', { name: 'Stage Uninstall' }));
    await waitFor(() => expect(bridge.stageIvScreenUninstall).toHaveBeenCalledTimes(1));
    await user.click(screen.getByRole('button', { name: 'Close Editor' }));
    await user.click(await screen.findByRole('button', { name: 'Yes, Discard' }));
    await waitFor(() => expect(useWorkbenchStore.getState().activeSection).toBe('workflows'));

    await act(async () => {
      await resolveLateStage();
    });

    expect(useWorkbenchStore.getState().editSession).toBeNull();
    expect(useWorkbenchStore.getState().ivScreenWorkflow).toBeNull();
    expect(useWorkbenchStore.getState().activeSection).toBe('workflows');
  });
});
