/* SPDX-License-Identifier: GPL-3.0-only */

import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { type ApiDiagnostic, type TypeChartWorkflow } from './bridge/contracts';
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

async function createTypeChartHarness(
  workflowOverride?: (workflow: TypeChartWorkflow) => TypeChartWorkflow
) {
  const fixtureBridge = createMockProjectBridge({}, true);
  const response = await fixtureBridge.loadTypeChartWorkflow({ paths: projectPaths });
  const workflow = workflowOverride?.(response.workflow) ?? response.workflow;
  const bridge = createMockProjectBridge({}, true);
  bridge.loadTypeChartWorkflow = vi.fn(async () => ({ workflow }));
  const fixtureStage = bridge.stageTypeChart;
  bridge.stageTypeChart = vi.fn(async (request) => ({
    ...(await fixtureStage(request)),
    workflow
  }));

  useWorkbenchStore.setState({
    activeSection: 'typeChart',
    applyResult: null,
    changePlan: null,
    draftPaths: projectPaths,
    editSession: null,
    editValidationDiagnostics: [],
    typeChartWorkflow: workflow
  });

  return { bridge, workflow };
}

function renderTypeChart(bridge: ProjectBridge) {
  return render(
    <LocalizationProvider>
      <App bridge={bridge} />
    </LocalizationProvider>
  );
}

function getNormalVsNormalCell() {
  return screen.getByRole('combobox', {
    name: /Normal attacking Normal/
  });
}

function getDetail(label: string) {
  const term = screen
    .getAllByText(label)
    .find((candidate) => candidate.tagName.toLocaleLowerCase() === 'dt');
  expect(term).toBeDefined();
  return term!.parentElement!;
}

describe('Type Chart UI', () => {
  beforeEach(() => {
    window.localStorage.clear();
    tauriEventMock.listen.mockClear();
    for (const eventName of Object.keys(tauriEventMock.listeners)) {
      delete tauriEventMock.listeners[eventName];
    }
    useWorkbenchStore.setState(useWorkbenchStore.getInitialState(), true);
  });

  it('shows the complete selected-game identity, install state, and honest source ownership', async () => {
    const { bridge, workflow } = await createTypeChartHarness();
    renderTypeChart(bridge);

    expect(screen.getAllByText('Pokemon Sword').length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText(workflow.buildId).length).toBeGreaterThanOrEqual(1);
    expect(getDetail('Offset')).toHaveTextContent(workflow.chartOffsetHex);
    expect(getDetail('Install message')).toHaveTextContent(
      workflow.installMessage
    );
    expect(getDetail('Output files')).toHaveTextContent('0');

    const sourceSummary = screen.getByText('Source').closest('dl');
    expect(sourceSummary).not.toBeNull();
    expect(within(sourceSummary!).getByText('Available')).toBeInTheDocument();
    expect(within(sourceSummary!).getByText('Base')).toBeInTheDocument();
    expect(within(sourceSummary!).getByText('Base only')).toBeInTheDocument();
  });

  it('keeps installed workflow values separate from the canonical staged chart', async () => {
    const user = userEvent.setup();
    const { bridge, workflow } = await createTypeChartHarness();
    renderTypeChart(bridge);

    await user.selectOptions(getNormalVsNormalCell(), '8');
    await user.click(screen.getByRole('button', { name: 'Stage Type Chart' }));

    await waitFor(() =>
      expect(useWorkbenchStore.getState().editSession?.pendingEdits[0]).toMatchObject({
        field: 'effectiveness',
        recordId: 'type-chart',
        summary: 'Stage Type Chart effectiveness table.'
      })
    );
    expect(useWorkbenchStore.getState().typeChartWorkflow).toBe(workflow);
    expect(getNormalVsNormalCell()).toHaveValue('8');
    expect(getDetail('Staged')).toHaveTextContent('Type Chart');
    expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled();
  });

  it('locks every Type Chart action and cell while a delayed stage is pending', async () => {
    const user = userEvent.setup();
    const { bridge } = await createTypeChartHarness();
    const successfulStage = bridge.stageTypeChart;
    let resolveDelayedStage!: () => Promise<void>;
    bridge.stageTypeChart = vi.fn(
      (request) =>
        new Promise<Awaited<ReturnType<ProjectBridge['stageTypeChart']>>>((resolve) => {
          resolveDelayedStage = async () => resolve(await successfulStage(request));
        })
    );
    renderTypeChart(bridge);

    await user.selectOptions(getNormalVsNormalCell(), '8');
    await user.click(screen.getByRole('button', { name: 'Stage Type Chart' }));

    await waitFor(() => {
      expect(getNormalVsNormalCell()).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Staging' })).toHaveAttribute(
        'aria-busy',
        'true'
      );
      expect(screen.getByRole('button', { name: 'Reset to Vanilla Chart' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();
    });

    await act(async () => {
      await resolveDelayedStage();
    });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());
  });

  it('preserves workflow, session, reviewed plan, and visible draft when restaging is rejected', async () => {
    const user = userEvent.setup();
    const rejection: ApiDiagnostic = {
      domain: 'workflow.typeChart',
      message: 'Type Chart staging was rejected.',
      severity: 'error'
    };
    const { bridge } = await createTypeChartHarness();
    renderTypeChart(bridge);

    await user.selectOptions(getNormalVsNormalCell(), '8');
    await user.click(screen.getByRole('button', { name: 'Stage Type Chart' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());

    const sessionBeforeRejection = useWorkbenchStore.getState().editSession!;
    const workflowBeforeRejection = useWorkbenchStore.getState().typeChartWorkflow!;
    bridge.stageTypeChart = vi.fn(async () => ({
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

    await user.selectOptions(getNormalVsNormalCell(), '2');
    await user.click(screen.getByRole('button', { name: 'Stage Type Chart' }));

    expect(await screen.findByText(rejection.message)).toBeInTheDocument();
    expect(useWorkbenchStore.getState().editSession).toBe(sessionBeforeRejection);
    expect(useWorkbenchStore.getState().typeChartWorkflow).toBe(workflowBeforeRejection);
    expect(screen.getByRole('heading', { name: 'Output Plan' })).toBeInTheDocument();
    expect(getNormalVsNormalCell()).toHaveValue('2');
    expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();

    await user.selectOptions(getNormalVsNormalCell(), '8');
    expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled();
  });

  it('recognizes only the exact backend pending source hash', async () => {
    const user = userEvent.setup();
    const { bridge } = await createTypeChartHarness();
    renderTypeChart(bridge);

    await user.selectOptions(getNormalVsNormalCell(), '8');
    await user.click(screen.getByRole('button', { name: 'Stage Type Chart' }));
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
                    relativePath: `pending/type-chart/effectiveness/${'A'.repeat(64)}`
                  }
                : source
            )
          }))
        }
      });
    });

    await waitFor(() => expect(getDetail('Staged')).toHaveTextContent('None'));
    expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
  });

  it('ignores a successful stage response that arrives after the editor is discarded', async () => {
    const user = userEvent.setup();
    const { bridge } = await createTypeChartHarness();
    const successfulStage = bridge.stageTypeChart;
    let resolveLateStage!: () => Promise<void>;
    bridge.stageTypeChart = vi.fn(
      (request) =>
        new Promise<Awaited<ReturnType<ProjectBridge['stageTypeChart']>>>((resolve) => {
          resolveLateStage = async () => resolve(await successfulStage(request));
        })
    );
    renderTypeChart(bridge);

    await user.selectOptions(getNormalVsNormalCell(), '8');
    await user.click(screen.getByRole('button', { name: 'Stage Type Chart' }));
    await waitFor(() => expect(bridge.stageTypeChart).toHaveBeenCalledTimes(1));
    await user.click(screen.getByRole('button', { name: 'Close Editor' }));
    await user.click(await screen.findByRole('button', { name: 'Yes, Discard' }));
    await waitFor(() => expect(useWorkbenchStore.getState().activeSection).toBe('workflows'));

    await act(async () => {
      await resolveLateStage();
    });

    expect(useWorkbenchStore.getState().editSession).toBeNull();
    expect(useWorkbenchStore.getState().typeChartWorkflow).toBeNull();
    expect(useWorkbenchStore.getState().activeSection).toBe('workflows');
  });
});
