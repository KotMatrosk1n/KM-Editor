/* SPDX-License-Identifier: GPL-3.0-only */

import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import type { ApiDiagnostic, HyperTrainingWorkflow } from './bridge/contracts';
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

async function createHyperTrainingHarness(
  workflowOverride?: (workflow: HyperTrainingWorkflow) => HyperTrainingWorkflow
) {
  const fixtureBridge = createMockProjectBridge({}, true);
  const response = await fixtureBridge.loadHyperTrainingWorkflow({ paths: projectPaths });
  const workflow = workflowOverride?.(response.workflow) ?? response.workflow;
  const bridge = createMockProjectBridge({}, true);

  useWorkbenchStore.setState({
    activeSection: 'hyperTraining',
    applyResult: null,
    changePlan: null,
    draftPaths: projectPaths,
    editSession: null,
    editValidationDiagnostics: [],
    hyperTrainingWorkflow: workflow
  });

  return { bridge, workflow };
}

function renderHyperTraining(bridge: ProjectBridge) {
  return render(
    <LocalizationProvider>
      <App bridge={bridge} />
    </LocalizationProvider>
  );
}

async function setCutoffInput(user: ReturnType<typeof userEvent.setup>, value: string) {
  const input = screen.getByRole('spinbutton', { name: 'Cutoff' });
  await user.clear(input);
  await user.type(input, value);
  return input;
}

describe('Hyper Training UI', () => {
  beforeEach(() => {
    window.localStorage.clear();
    tauriEventMock.listen.mockClear();
    for (const eventName of Object.keys(tauriEventMock.listeners)) {
      delete tauriEventMock.listeners[eventName];
    }
    useWorkbenchStore.setState(useWorkbenchStore.getInitialState(), true);
  });

  it('shows structured cutoff identity and does not label a missing source as generated', async () => {
    const { bridge, workflow } = await createHyperTrainingHarness((current) => ({
      ...current,
      levelRule: {
        ...current.levelRule,
        dialogueMinimumLevel: null
      },
      sources: current.sources.map((source) =>
        source.sourceId === 'dialogue'
          ? {
              ...source,
              provenance: {
                ...source.provenance,
                sourceLayer: 'generated'
              },
              status: 'optionalMissing' as const
            }
          : source
      )
    }));
    renderHyperTraining(bridge);

    expect(screen.getAllByText('Pokemon Sword').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText(workflow.buildId).length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('NPC script cutoff').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Picker runtime cutoff').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Cutoff sync').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText('Synchronized').length).toBeGreaterThanOrEqual(1);

    const dialogueRow = screen
      .getByText('English Hyper Training dialogue')
      .closest<HTMLElement>('[role="row"]');
    expect(dialogueRow).not.toBeNull();
    expect(within(dialogueRow!).getByText('Optional missing')).toBeInTheDocument();
    expect(within(dialogueRow!).getByText('Unavailable')).toBeInTheDocument();
    expect(within(dialogueRow!).queryByText('Generated')).not.toBeInTheDocument();
  });

  it('uses the staged cutoff as the baseline and blocks Review and Apply for a different visible draft', async () => {
    const user = userEvent.setup();
    const { bridge } = await createHyperTrainingHarness();
    renderHyperTraining(bridge);

    await setCutoffInput(user, '50');
    await user.click(screen.getByRole('button', { name: 'Stage Cutoff' }));
    await waitFor(() => expect(useWorkbenchStore.getState().editSession).not.toBeNull());
    expect(useWorkbenchStore.getState().hyperTrainingWorkflow?.levelRule.minimumLevel).toBe(100);
    expect(screen.getByText('Staged cutoff').parentElement).toHaveTextContent('Lv. 50');
    expect(screen.getByRole('spinbutton', { name: 'Cutoff' })).toHaveValue(50);

    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());
    expect(screen.getByRole('heading', { name: 'Output Plan' })).toBeInTheDocument();

    await setCutoffInput(user, '60');
    expect(screen.getByRole('button', { name: 'Stage Cutoff' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();
    expect(screen.getByRole('heading', { name: 'Output Plan' })).toBeInTheDocument();

    await setCutoffInput(user, '50');
    expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled();

    act(() => useWorkbenchStore.setState({ activeSection: 'workflows' }));
    await waitFor(() =>
      expect(screen.queryByRole('heading', { name: 'Hyper Training' })).not.toBeInTheDocument()
    );
    act(() => useWorkbenchStore.setState({ activeSection: 'hyperTraining' }));
    expect(await screen.findByRole('spinbutton', { name: 'Cutoff' })).toHaveValue(50);
  });

  it('restores the staged cutoff when an invalid cleared draft loses focus', async () => {
    const user = userEvent.setup();
    const { bridge } = await createHyperTrainingHarness();
    renderHyperTraining(bridge);

    await setCutoffInput(user, '50');
    await user.click(screen.getByRole('button', { name: 'Stage Cutoff' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());

    const input = screen.getByRole('spinbutton', { name: 'Cutoff' });
    await user.clear(input);
    expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();

    await user.tab();

    expect(input).toHaveValue(50);
    expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled();
  });

  it('locks both cutoff controls while a delayed stage is pending', async () => {
    const user = userEvent.setup();
    const { bridge } = await createHyperTrainingHarness();
    const successfulStage = bridge.stageHyperTraining;
    let resolveDelayedStage!: () => Promise<void>;
    bridge.stageHyperTraining = vi.fn(
      (request) =>
        new Promise<Awaited<ReturnType<ProjectBridge['stageHyperTraining']>>>((resolve) => {
          resolveDelayedStage = async () => resolve(await successfulStage(request));
        })
    );
    renderHyperTraining(bridge);

    const numberInput = await setCutoffInput(user, '50');
    const rangeInput = screen.getByRole('slider', {
      name: 'Hyper Training minimum level'
    });
    await user.click(screen.getByRole('button', { name: 'Stage Cutoff' }));

    await waitFor(() => {
      expect(numberInput).toBeDisabled();
      expect(rangeInput).toBeDisabled();
    });
    await user.type(numberInput, '60');
    await user.type(rangeInput, '{arrowup}');
    expect(numberInput).toHaveValue(50);
    expect(rangeInput).toHaveValue('50');

    await act(async () => {
      await resolveDelayedStage();
    });

    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());
    expect(numberInput).toBeEnabled();
    expect(rangeInput).toBeEnabled();
    expect(numberInput).toHaveValue(50);
    expect(rangeInput).toHaveValue('50');
  });

  it('offers a vanilla restore when only the dialogue cutoff is out of sync', async () => {
    const user = userEvent.setup();
    const { bridge, workflow } = await createHyperTrainingHarness((current) => ({
      ...current,
      levelRule: {
        ...current.levelRule,
        dialogueMinimumLevel: 50,
        levelsMatch: false,
        minimumLevel: 100,
        runtimeMinimumLevel: 100,
        scriptMinimumLevel: 100
      }
    }));
    const successfulStage = bridge.stageHyperTraining;
    bridge.stageHyperTraining = vi.fn(async (request) => ({
      ...(await successfulStage(request)),
      workflow
    }));
    renderHyperTraining(bridge);

    const restoreButton = screen.getByRole('button', { name: 'Restore Lv. 100' });
    expect(restoreButton).toBeEnabled();
    await user.click(restoreButton);

    await waitFor(() =>
      expect(useWorkbenchStore.getState().editSession?.pendingEdits[0]?.newValue).toBe(
        '100'
      )
    );
    expect(restoreButton).toBeDisabled();
  });

  it('recognizes only a canonical staged edit', async () => {
    const user = userEvent.setup();
    const { bridge } = await createHyperTrainingHarness();
    renderHyperTraining(bridge);

    await setCutoffInput(user, '50');
    await user.click(screen.getByRole('button', { name: 'Stage Cutoff' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());

    act(() => {
      const currentSession = useWorkbenchStore.getState().editSession!;
      useWorkbenchStore.setState({
        editSession: {
          ...currentSession,
          pendingEdits: currentSession.pendingEdits.map((edit) => ({
            ...edit,
            sources: edit.sources.map((source, index) =>
              index === 0 ? { ...source, layer: 'generated' as const } : source
            ),
            summary: 'Forged Hyper Training summary.'
          }))
        }
      });
    });

    await waitFor(() =>
      expect(screen.getByText('Staged cutoff').parentElement).toHaveTextContent('None')
    );
    expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Stage Cutoff' })).toBeEnabled();
  });

  it('preserves the installed workflow, staged session, reviewed plan, and visible draft when staging is rejected', async () => {
    const user = userEvent.setup();
    const rejection: ApiDiagnostic = {
      domain: 'workflow.hyperTraining',
      message: 'Hyper Training staging was rejected.',
      severity: 'error'
    };
    const { bridge } = await createHyperTrainingHarness();
    renderHyperTraining(bridge);

    await setCutoffInput(user, '50');
    await user.click(screen.getByRole('button', { name: 'Stage Cutoff' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());

    const sessionBeforeRejection = useWorkbenchStore.getState().editSession!;
    const workflowBeforeRejection = useWorkbenchStore.getState().hyperTrainingWorkflow!;
    bridge.stageHyperTraining = vi.fn(async () => ({
      diagnostics: [rejection],
      session: {
        ...sessionBeforeRejection,
        pendingEdits: sessionBeforeRejection.pendingEdits.map((edit) => ({
          ...edit,
          summary: 'Rejected backend mutation.'
        }))
      },
      workflow: {
        ...workflowBeforeRejection,
        installMessage: 'Rejected backend workflow.',
        installStatus: 'blocked' as const
      }
    }));

    await setCutoffInput(user, '60');
    await user.click(screen.getByRole('button', { name: 'Stage Cutoff' }));

    expect(await screen.findByText(rejection.message)).toBeInTheDocument();
    expect(useWorkbenchStore.getState().editSession).toBe(sessionBeforeRejection);
    expect(useWorkbenchStore.getState().hyperTrainingWorkflow).toBe(workflowBeforeRejection);
    expect(screen.getByRole('heading', { name: 'Output Plan' })).toBeInTheDocument();
    expect(screen.getByRole('spinbutton', { name: 'Cutoff' })).toHaveValue(60);
    expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();

    await setCutoffInput(user, '50');
    expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled();
  });

  it('ignores a successful staging response that arrives after the editor is discarded', async () => {
    const user = userEvent.setup();
    const { bridge } = await createHyperTrainingHarness();
    const successfulStage = bridge.stageHyperTraining;
    let resolveLateStage!: () => Promise<void>;
    bridge.stageHyperTraining = vi.fn(
      (request) =>
        new Promise<Awaited<ReturnType<ProjectBridge['stageHyperTraining']>>>((resolve) => {
          resolveLateStage = async () => resolve(await successfulStage(request));
        })
    );
    renderHyperTraining(bridge);

    await setCutoffInput(user, '50');
    await user.click(screen.getByRole('button', { name: 'Stage Cutoff' }));
    await waitFor(() => expect(bridge.stageHyperTraining).toHaveBeenCalledTimes(1));
    await user.click(screen.getByRole('button', { name: 'Close Editor' }));
    await user.click(await screen.findByRole('button', { name: 'Yes, Discard' }));
    await waitFor(() => expect(useWorkbenchStore.getState().activeSection).toBe('workflows'));

    await act(async () => {
      await resolveLateStage();
    });

    expect(useWorkbenchStore.getState().editSession).toBeNull();
    expect(useWorkbenchStore.getState().hyperTrainingWorkflow).toBeNull();
    expect(useWorkbenchStore.getState().activeSection).toBe('workflows');
  });
});
