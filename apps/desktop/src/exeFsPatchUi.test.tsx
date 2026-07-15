/* SPDX-License-Identifier: GPL-3.0-only */

import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import {
  type ApiDiagnostic,
  type ApplyResult,
  type ChangePlan,
  type EditSession,
  type ExeFsPatchWorkflow
} from './bridge/contracts';
import { type ProjectBridge } from './bridge/projectBridge';
import { languageStorageKey, LocalizationProvider } from './localization';
import { createMockProjectBridge } from './testSupport/appTestFixtures';
import { useWorkbenchStore } from './workbenchStore';

const projectPaths = {
  baseExeFsPath: 'base-exefs',
  baseRomFsPath: 'base-romfs',
  outputRootPath: 'output',
  pokemonLegendsZASupportFolderPath: '',
  saveFilePath: '',
  scarletVioletSupportFolderPath: '',
  selectedGame: 'sword' as const
};

const stagedSession: EditSession = {
  hasPendingChanges: true,
  pendingEdits: [
    {
      domain: 'workflow.exefsPatches',
      field: 'patchId',
      newValue: 'exefs/main',
      recordId: 'exefs-main-compatibility',
      sources: [{ layer: 'base', relativePath: 'exefs/main' }],
      summary: 'Stage the Royal Candy executable patch.'
    }
  ],
  sessionId: 'exefs-ui-session'
};

const reviewedPlan: ChangePlan = {
  canApply: true,
  diagnostics: [],
  sessionId: stagedSession.sessionId,
  writes: [
    {
      reason: 'Apply the reviewed Royal Candy executable patch.',
      replacesExistingOutput: false,
      sources: [{ layer: 'base', relativePath: 'exefs/main' }],
      targetRelativePath: 'exefs/main'
    }
  ]
};

const priorApplyResult: ApplyResult = {
  applyId: 'previous-exefs-apply',
  diagnostics: [],
  writtenFiles: ['exefs/main']
};

async function createExeFsHarness(
  canEdit = true,
  mutateWorkflow?: (workflow: ExeFsPatchWorkflow) => ExeFsPatchWorkflow
) {
  const fixtureBridge = createMockProjectBridge({}, canEdit);
  const response = await fixtureBridge.loadExeFsPatchWorkflow({ paths: projectPaths });
  const workflow = mutateWorkflow?.(response.workflow) ?? response.workflow;
  const bridge = createMockProjectBridge({}, canEdit);

  useWorkbenchStore.setState({
    activeSection: 'exefsPatches',
    applyResult: null,
    changePlan: null,
    draftPaths: projectPaths,
    editSession: null,
    editValidationDiagnostics: [],
    exeFsPatchSearchText: '',
    exeFsPatchWorkflow: workflow,
    selectedExeFsCheckId: workflow.checks[0]?.checkId ?? null,
    selectedExeFsPatchId: workflow.patches[0]?.patchId ?? null
  });

  return { bridge, workflow };
}

function renderExeFs(bridge: ProjectBridge) {
  return render(
    <LocalizationProvider>
      <App bridge={bridge} />
    </LocalizationProvider>
  );
}

describe('ExeFS Patch Manager UI', () => {
  beforeEach(() => {
    window.localStorage.clear();
    useWorkbenchStore.setState(useWorkbenchStore.getInitialState(), true);
  });

  it('disables executable patch staging for a read-only workflow', async () => {
    const { bridge } = await createExeFsHarness(false);
    renderExeFs(bridge);

    expect(
      await screen.findByRole('button', { name: 'Stage Royal Candy ExeFS Patch' })
    ).toBeDisabled();
  });

  it('constrains selection and actions to matches while preserving search on refresh', async () => {
    const user = userEvent.setup();
    const { bridge, workflow } = await createExeFsHarness(true, (loadedWorkflow) => ({
      ...loadedWorkflow,
      patches: loadedWorkflow.patches.map((patch) => ({
        ...patch,
        name: 'Royal Candy executable patch'
      }))
    }));
    renderExeFs(bridge);

    const search = await screen.findByRole('searchbox', { name: 'Search ExeFS records' });
    await user.type(search, 'Patch code cave');

    expect(screen.getByText('Royal Candy executable patch')).toBeInTheDocument();
    expect(screen.queryByText('exefs-main-compatibility')).toBeNull();
    expect(
      screen.queryByRole('button', { name: 'Stage Royal Candy ExeFS Patch' })
    ).toBeNull();

    await user.clear(search);
    await user.type(search, 'no matching executable record');

    expect(screen.getByRole('status')).toHaveTextContent('No matching ExeFS records.');
    expect(
      screen.queryByRole('button', { name: 'Stage Royal Candy ExeFS Patch' })
    ).toBeNull();
    await waitFor(() => {
      expect(useWorkbenchStore.getState().selectedExeFsPatchId).toBeNull();
      expect(useWorkbenchStore.getState().selectedExeFsCheckId).toBeNull();
    });

    act(() => useWorkbenchStore.getState().setExeFsPatchWorkflow(workflow));
    expect(search).toHaveValue('no matching executable record');
  });

  it('retains session, workflow, and reviewed outputs when staging is rejected', async () => {
    const user = userEvent.setup();
    const rejection: ApiDiagnostic = {
      domain: 'workflow.exefsPatches',
      message: 'The executable patch was not staged.',
      severity: 'error'
    };
    const harness = await createExeFsHarness();
    const rejectedWorkflow = {
      ...harness.workflow,
      patches: harness.workflow.patches.map((patch) => ({ ...patch, status: 'blocked' }))
    };
    harness.bridge.stageExeFsPatch = vi.fn(async () => ({
      diagnostics: [rejection],
      session: {
        ...stagedSession,
        pendingEdits: [
          ...stagedSession.pendingEdits,
          { ...stagedSession.pendingEdits[0]!, summary: 'Partial backend mutation.' }
        ]
      },
      workflow: rejectedWorkflow
    }));
    useWorkbenchStore.setState({
      applyResult: priorApplyResult,
      changePlan: reviewedPlan,
      editSession: stagedSession
    });
    renderExeFs(harness.bridge);

    await user.click(
      await screen.findByRole('button', { name: 'Stage Royal Candy ExeFS Patch' })
    );

    await waitFor(() => expect(harness.bridge.stageExeFsPatch).toHaveBeenCalledTimes(1));
    expect(useWorkbenchStore.getState().editSession).toEqual(stagedSession);
    expect(useWorkbenchStore.getState().exeFsPatchWorkflow).toBe(harness.workflow);
    expect(useWorkbenchStore.getState().changePlan).toEqual(reviewedPlan);
    expect(useWorkbenchStore.getState().applyResult).toEqual(priorApplyResult);
    expect(useWorkbenchStore.getState().editValidationDiagnostics).toEqual([rejection]);
  });

  it('retains session, workflow, and reviewed outputs when staging transport fails', async () => {
    const user = userEvent.setup();
    const harness = await createExeFsHarness();
    harness.bridge.stageExeFsPatch = vi.fn(async () => {
      throw new Error('ExeFS stage transport failed.');
    });
    useWorkbenchStore.setState({
      applyResult: priorApplyResult,
      changePlan: reviewedPlan,
      editSession: stagedSession
    });
    renderExeFs(harness.bridge);

    await user.click(
      await screen.findByRole('button', { name: 'Stage Royal Candy ExeFS Patch' })
    );

    await waitFor(() => expect(harness.bridge.stageExeFsPatch).toHaveBeenCalledTimes(1));
    expect(await screen.findByText(/ExeFS stage transport failed\./)).toBeInTheDocument();
    expect(useWorkbenchStore.getState().editSession).toEqual(stagedSession);
    expect(useWorkbenchStore.getState().exeFsPatchWorkflow).toBe(harness.workflow);
    expect(useWorkbenchStore.getState().changePlan).toEqual(reviewedPlan);
    expect(useWorkbenchStore.getState().applyResult).toEqual(priorApplyResult);
  });

  it('invalidates reviewed outputs only after successful staging', async () => {
    const user = userEvent.setup();
    const harness = await createExeFsHarness();
    const stageExeFsPatch = vi.spyOn(harness.bridge, 'stageExeFsPatch');
    useWorkbenchStore.setState({
      applyResult: priorApplyResult,
      changePlan: reviewedPlan,
      editSession: stagedSession,
      exeFsPatchSearchText: 'compatibility'
    });
    renderExeFs(harness.bridge);

    await user.click(
      await screen.findByRole('button', { name: 'Stage Royal Candy ExeFS Patch' })
    );

    await waitFor(() => expect(stageExeFsPatch).toHaveBeenCalledTimes(1));
    expect(stageExeFsPatch.mock.calls[0]?.[0].session).toEqual(stagedSession);
    expect(useWorkbenchStore.getState().changePlan).toBeNull();
    expect(useWorkbenchStore.getState().applyResult).toBeNull();
    expect(useWorkbenchStore.getState().exeFsPatchSearchText).toBe('compatibility');
  });

  it('shows complete segment data and localized ExeFS chrome', async () => {
    window.localStorage.setItem(languageStorageKey, 'es');
    const user = userEvent.setup();
    const { bridge } = await createExeFsHarness(true, (workflow) => ({
      ...workflow,
      checks: workflow.checks.map((check, index) =>
        index === 0
          ? {
              ...check,
              actual: 'Unsupported',
              expected: 'Matching safe vanilla base exefs/main for any layered override',
              name: 'Base executable source',
              notes:
                'Layered exefs/main can be inspected, but staging requires a matching safe vanilla base exefs/main.'
            }
          : check
      ),
      patches: workflow.patches.map((patch) => ({
        ...patch,
        description:
          'Installs only the Royal Candy executable portion after exact build and anchor verification. The Royal Candy editor owns the complete data, script, and shop install lifecycle.',
        name: 'Royal Candy executable patch',
        patchKind: 'Executable patch'
      })),
      summary: {
        ...workflow.summary,
        description:
          'Royal Candy executable patch readiness, exact build checks, segment hashes, and source provenance.'
      }
    }));
    renderExeFs(bridge);

    const search = await screen.findByRole('searchbox', { name: 'Buscar registros ExeFS' });
    expect(screen.getAllByText('Parche ejecutable de Royal Candy').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Origen del ejecutable base').length).toBeGreaterThan(0);
    expect(
      screen.getByText(
        'exefs/main base original, seguro y coincidente para cualquier sustitución LayeredFS'
      )
    ).toBeInTheDocument();
    expect(screen.getAllByText('No compatible').length).toBeGreaterThan(0);
    expect(
      screen.getByRole('button', { name: 'Preparar parche ExeFS de Royal Candy' })
    ).toBeEnabled();
    const segments = screen.getByLabelText('Segmentos ExeFS');
    expect(within(segments).getByText('Tamaño comprimido').parentElement).toHaveTextContent(
      '0x7DDA90'
    );
    expect(within(segments).getByText('SHA-256').parentElement).toHaveTextContent('ABCD');

    await user.type(search, 'sin coincidencias');
    expect(screen.getByRole('status')).toHaveTextContent('No hay registros ExeFS coincidentes.');
    expect(
      screen.queryByRole('button', { name: 'Preparar parche ExeFS de Royal Candy' })
    ).toBeNull();
  });
});
