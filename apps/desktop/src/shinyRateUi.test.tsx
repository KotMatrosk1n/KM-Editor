/* SPDX-License-Identifier: GPL-3.0-only */

import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import type { ApiDiagnostic } from './bridge/contracts';
import type { ProjectBridge } from './bridge/projectBridge';
import type { ShinyRateWorkflow } from './bridge/shinyRateContracts';
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

async function createShinyRateHarness(
  workflowOverride?: (workflow: ShinyRateWorkflow) => ShinyRateWorkflow
) {
  const fixtureBridge = createMockProjectBridge({}, true);
  const response = await fixtureBridge.loadShinyRateWorkflow({ paths: projectPaths });
  const workflow = workflowOverride?.(response.workflow) ?? response.workflow;
  const bridge = createMockProjectBridge({}, true);
  bridge.loadShinyRateWorkflow = vi.fn(async () => ({ workflow }));
  const fixtureStage = bridge.stageShinyRate;
  bridge.stageShinyRate = vi.fn(async (request) => ({
    ...(await fixtureStage(request)),
    workflow
  }));

  useWorkbenchStore.setState({
    activeSection: 'shinyRate',
    applyResult: null,
    changePlan: null,
    draftPaths: projectPaths,
    editSession: null,
    editValidationDiagnostics: [],
    shinyRateWorkflow: workflow
  });

  return { bridge, workflow };
}

function renderShinyRate(bridge: ProjectBridge) {
  return render(
    <LocalizationProvider>
      <App bridge={bridge} />
    </LocalizationProvider>
  );
}

function getPreset(name: RegExp | string) {
  return screen.getByRole('button', { name });
}

describe('Shiny Rate UI', () => {
  beforeEach(() => {
    window.localStorage.clear();
    tauriEventMock.listen.mockClear();
    for (const eventName of Object.keys(tauriEventMock.listeners)) {
      delete tauriEventMock.listeners[eventName];
    }
    useWorkbenchStore.setState(useWorkbenchStore.getInitialState(), true);
  });

  it('shows the selected-game identity, all verified offsets, and an honest missing source', async () => {
    const { bridge, workflow } = await createShinyRateHarness((current) => ({
      ...current,
      source: current.source
        ? {
            ...current.source,
            provenance: {
              ...current.source.provenance,
              sourceLayer: 'generated'
            },
            status: 'missing'
          }
        : null
    }));
    renderShinyRate(bridge);

    expect(screen.getAllByText('Pokemon Sword').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText(workflow.buildId).length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('Function offset').parentElement).toHaveTextContent(
      workflow.functionOffsetHex
    );
    expect(screen.getByText('Compare offset').parentElement).toHaveTextContent(
      workflow.compareOffsetHex
    );
    expect(screen.getByText('Break offset').parentElement).toHaveTextContent(
      workflow.breakOffsetHex
    );
    expect(screen.getByText('Install message').parentElement).toHaveTextContent(
      workflow.installMessage
    );

    const sourceSummary = screen.getByText('Source').closest('dl');
    expect(sourceSummary).not.toBeNull();
    expect(within(sourceSummary!).getByText('Missing')).toBeInTheDocument();
    expect(within(sourceSummary!).getAllByText('Unavailable')).toHaveLength(2);
    expect(within(sourceSummary!).queryByText('Generated')).not.toBeInTheDocument();
  });

  it('keeps installed runtime state separate from the canonical staged selection', async () => {
    const user = userEvent.setup();
    const { bridge, workflow } = await createShinyRateHarness();
    renderShinyRate(bridge);

    const eightRollPreset = getPreset(/Masuda \+ Shiny Charm/);
    await user.click(eightRollPreset);
    expect(eightRollPreset).toHaveAttribute('aria-pressed', 'true');
    await user.click(screen.getByRole('button', { name: 'Stage Shiny Rate' }));

    await waitFor(() =>
      expect(useWorkbenchStore.getState().editSession?.pendingEdits[0]?.newValue).toBe(
        'fixed:8'
      )
    );
    expect(useWorkbenchStore.getState().shinyRateWorkflow).toBe(workflow);
    expect(screen.getByText('Draft').parentElement).toHaveTextContent('8 rolls');
    expect(screen.getByText('Runtime').parentElement).toHaveTextContent(
      "Default restores the game's original runtime-dependent reroll count calculation."
    );
    expect(screen.getByText('Staged rate').parentElement).toHaveTextContent('8 rolls');

    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());

    await user.click(getPreset(/^Masuda1\/683/));
    expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();

    await user.click(eightRollPreset);
    expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled();
  });

  it('validates whole-number custom odds and never copies Always Shiny into the custom field', async () => {
    const user = userEvent.setup();
    const { bridge } = await createShinyRateHarness();
    renderShinyRate(bridge);

    const customInput = screen.getByRole('spinbutton', {
      name: 'Custom shiny odds denominator'
    });
    await user.click(getPreset(/Always Shiny/));
    expect(customInput).toHaveValue(4096);

    await user.clear(customInput);
    await user.type(customInput, '2.5');
    expect(customInput).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByRole('button', { name: 'Use Custom' })).toBeDisabled();
    await user.tab();
    expect(customInput).toHaveValue(4096);

    await user.clear(customInput);
    await user.type(customInput, '8192');
    expect(screen.getByText(/Closest input is 1\/4,096/)).toBeInTheDocument();
    await user.tab();
    expect(customInput).toHaveValue(4096);
  });

  it('locks every Shiny Rate control while a delayed stage is pending', async () => {
    const user = userEvent.setup();
    const { bridge } = await createShinyRateHarness();
    const successfulStage = bridge.stageShinyRate;
    let resolveDelayedStage!: () => Promise<void>;
    bridge.stageShinyRate = vi.fn(
      (request) =>
        new Promise<Awaited<ReturnType<ProjectBridge['stageShinyRate']>>>((resolve) => {
          resolveDelayedStage = async () => resolve(await successfulStage(request));
        })
    );
    renderShinyRate(bridge);

    await user.click(getPreset(/Masuda \+ Shiny Charm/));
    await user.click(screen.getByRole('button', { name: 'Stage Shiny Rate' }));

    await waitFor(() => {
      expect(
        screen.getByRole('spinbutton', { name: 'Custom shiny odds denominator' })
      ).toBeDisabled();
      for (const preset of screen.getAllByRole('button').filter((button) =>
        button.classList.contains('shiny-rate-preset')
      )) {
        expect(preset).toBeDisabled();
      }
      expect(screen.getByRole('button', { name: 'Staging' })).toHaveAttribute(
        'aria-busy',
        'true'
      );
    });

    await act(async () => {
      await resolveDelayedStage();
    });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());
  });

  it('preserves workflow, session, reviewed plan, and visible draft when restaging is rejected', async () => {
    const user = userEvent.setup();
    const rejection: ApiDiagnostic = {
      domain: 'workflow.shinyRate',
      message: 'Shiny Rate staging was rejected.',
      severity: 'error'
    };
    const { bridge } = await createShinyRateHarness();
    renderShinyRate(bridge);

    await user.click(getPreset(/Masuda \+ Shiny Charm/));
    await user.click(screen.getByRole('button', { name: 'Stage Shiny Rate' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());

    const sessionBeforeRejection = useWorkbenchStore.getState().editSession!;
    const workflowBeforeRejection = useWorkbenchStore.getState().shinyRateWorkflow!;
    bridge.stageShinyRate = vi.fn(async () => ({
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

    await user.click(getPreset(/^Masuda1\/683/));
    await user.click(screen.getByRole('button', { name: 'Stage Shiny Rate' }));

    expect(await screen.findByText(rejection.message)).toBeInTheDocument();
    expect(useWorkbenchStore.getState().editSession).toBe(sessionBeforeRejection);
    expect(useWorkbenchStore.getState().shinyRateWorkflow).toBe(workflowBeforeRejection);
    expect(screen.getByRole('heading', { name: 'Output Plan' })).toBeInTheDocument();
    expect(screen.getByText('Draft').parentElement).toHaveTextContent('6 rolls');
    expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();

    await user.click(getPreset(/Masuda \+ Shiny Charm/));
    expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled();
  });

  it('recognizes only the backend canonical pending identity', async () => {
    const user = userEvent.setup();
    const { bridge } = await createShinyRateHarness();
    renderShinyRate(bridge);

    await user.click(getPreset(/Masuda \+ Shiny Charm/));
    await user.click(screen.getByRole('button', { name: 'Stage Shiny Rate' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());

    act(() => {
      const currentSession = useWorkbenchStore.getState().editSession!;
      useWorkbenchStore.setState({
        editSession: {
          ...currentSession,
          pendingEdits: currentSession.pendingEdits.map((edit) => ({
            ...edit,
            sources: edit.sources.map((source) =>
              source.layer === 'pending'
                ? {
                    ...source,
                    relativePath:
                      'pending/shiny-rate/rate/37A8EEC1CE19687D132FE29051DCA629D164E2C4958BA141D5F4133A33F0688F'
                  }
                : source
            )
          }))
        }
      });
    });

    await waitFor(() =>
      expect(screen.getByText('Staged rate').parentElement).toHaveTextContent('None')
    );
    expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
  });

  it('stages Default as the restoration path for an installed fixed rate', async () => {
    const user = userEvent.setup();
    const { bridge } = await createShinyRateHarness((workflow) => ({
      ...workflow,
      installMessage: 'Shiny Rate is fixed at 8 PID rolls.',
      installStatus: 'fixed',
      rateRule: {
        ...workflow.rateRule,
        chancePercent: (1 - Math.pow(4095 / 4096, 8)) * 100,
        mode: 'fixed',
        oddsDenominator: 512,
        oddsLabel: '1/512',
        percentLabel: '0.195%',
        rollCount: 8,
        runtimeSummary: 'Fixed writes a global PID roll count for random shiny checks.'
      }
    }));
    renderShinyRate(bridge);

    await user.click(getPreset(/^DefaultDynamicVariable/));
    await user.click(screen.getByRole('button', { name: 'Stage Shiny Rate' }));

    await waitFor(() =>
      expect(useWorkbenchStore.getState().editSession?.pendingEdits[0]?.newValue).toBe(
        'default'
      )
    );
    expect(bridge.stageShinyRate).toHaveBeenCalledWith(
      expect.objectContaining({ mode: 'default', rollCount: null })
    );
  });

  it('ignores a successful stage response that arrives after the editor is discarded', async () => {
    const user = userEvent.setup();
    const { bridge } = await createShinyRateHarness();
    const successfulStage = bridge.stageShinyRate;
    let resolveLateStage!: () => Promise<void>;
    bridge.stageShinyRate = vi.fn(
      (request) =>
        new Promise<Awaited<ReturnType<ProjectBridge['stageShinyRate']>>>((resolve) => {
          resolveLateStage = async () => resolve(await successfulStage(request));
        })
    );
    renderShinyRate(bridge);

    await user.click(getPreset(/Masuda \+ Shiny Charm/));
    await user.click(screen.getByRole('button', { name: 'Stage Shiny Rate' }));
    await waitFor(() => expect(bridge.stageShinyRate).toHaveBeenCalledTimes(1));
    await user.click(screen.getByRole('button', { name: 'Close Editor' }));
    await user.click(await screen.findByRole('button', { name: 'Yes, Discard' }));
    await waitFor(() => expect(useWorkbenchStore.getState().activeSection).toBe('workflows'));

    await act(async () => {
      await resolveLateStage();
    });

    expect(useWorkbenchStore.getState().editSession).toBeNull();
    expect(useWorkbenchStore.getState().shinyRateWorkflow).toBeNull();
    expect(useWorkbenchStore.getState().activeSection).toBe('workflows');
  });
});
