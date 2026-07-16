/* SPDX-License-Identifier: GPL-3.0-only */

import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { type ApiDiagnostic, type EditSession, type ProjectGame } from './bridge/contracts';
import {
  type FashionUnlockAction,
  type FashionUnlockWorkflow
} from './bridge/fashionUnlockContracts';
import { type ProjectBridge } from './bridge/projectBridge';
import { LocalizationProvider } from './localization';
import { createMockProjectBridge } from './testSupport/appTestFixtures';
import {
  createSvFashionUnlockWorkflow,
  createSwShFashionUnlockWorkflow
} from './testSupport/fashionUnlockTestFixtures';
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

describe('Fashion Unlock UI', () => {
  beforeEach(() => {
    window.localStorage.clear();
    tauriEventMock.listen.mockClear();
    for (const eventName of Object.keys(tauriEventMock.listeners)) {
      delete tauriEventMock.listeners[eventName];
    }
    useWorkbenchStore.setState(useWorkbenchStore.getInitialState(), true);
  });

  it.each([
    ['sword', () => createSwShFashionUnlockWorkflow('sword')],
    ['shield', () => createSwShFashionUnlockWorkflow('shield')],
    ['scarlet', () => createSvFashionUnlockWorkflow('scarlet')],
    ['violet', () => createSvFashionUnlockWorkflow('violet')]
  ] as const)('stages a canonical install for %s without crossing editor families', async (
    game,
    createWorkflow
  ) => {
    const user = userEvent.setup();
    const workflow = createWorkflow();
    const { bridge } = createFashionHarness(workflow);
    renderFashion(bridge);

    await user.click(screen.getByRole('button', { name: 'Stage Install' }));

    await waitFor(() => {
      expect(useWorkbenchStore.getState().editSession?.pendingEdits[0]).toMatchObject({
        domain: 'workflow.fashionUnlock',
        field: 'install',
        newValue: 'true',
        recordId: 'fashion-unlock-v1-install',
        summary: 'Stage Fashion Unlock install.'
      });
    });
    expect(useWorkbenchStore.getState().fashionUnlockWorkflow?.editorFamily).toBe(
      game === 'sword' || game === 'shield' ? 'swsh' : 'sv'
    );
    expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled();
  });

  it('rejects a malformed first response and preserves the loaded workflow', async () => {
    const user = userEvent.setup();
    const workflow = createSwShFashionUnlockWorkflow();
    const { bridge } = createFashionHarness(workflow);
    const canonicalStage = bridge.stageFashionUnlockInstall;
    bridge.stageFashionUnlockInstall = vi.fn(async (request) => {
      const response = await canonicalStage(request);
      return { ...response, session: { ...response.session, sessionId: '' } };
    });
    renderFashion(bridge);

    await user.click(screen.getByRole('button', { name: 'Stage Install' }));

    expect(await screen.findByText(
      'Fashion Unlock staging did not match the requested action, game, session, and source state.'
    )).toBeInTheDocument();
    expect(useWorkbenchStore.getState().editSession).toBeNull();
    expect(useWorkbenchStore.getState().fashionUnlockWorkflow).toBe(workflow);
    expect(screen.getByRole('button', { name: 'Stage Install' })).toBeEnabled();
  });

  it('preserves the prior workflow, session, and reviewed plan after rejection', async () => {
    const user = userEvent.setup();
    const workflow = createSwShFashionUnlockWorkflow('sword', true);
    const { bridge } = createFashionHarness(workflow);
    renderFashion(bridge);

    await user.click(screen.getByRole('button', { name: 'Stage Reinstall' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());

    const priorSession = useWorkbenchStore.getState().editSession!;
    const rejection: ApiDiagnostic = {
      domain: 'workflow.fashionUnlock',
      message: 'Fashion Unlock uninstall staging was rejected.',
      severity: 'error'
    };
    bridge.stageFashionUnlockUninstall = vi.fn(async () => ({
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
    expect(useWorkbenchStore.getState().editSession).toBe(priorSession);
    expect(useWorkbenchStore.getState().fashionUnlockWorkflow).toBe(workflow);
    expect(screen.getByRole('heading', { name: 'Output Plan' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled();
  });

  it('locks Fashion actions and ignores a response after project scope invalidation', async () => {
    const user = userEvent.setup();
    const workflow = createSwShFashionUnlockWorkflow();
    const { bridge } = createFashionHarness(workflow);
    const canonicalStage = bridge.stageFashionUnlockInstall;
    let resolveStage!: () => Promise<void>;
    bridge.stageFashionUnlockInstall = vi.fn(
      (request) =>
        new Promise<Awaited<ReturnType<ProjectBridge['stageFashionUnlockInstall']>>>(
          (resolve) => {
            resolveStage = async () => resolve(await canonicalStage(request));
          }
        )
    );
    renderFashion(bridge);

    await user.click(screen.getByRole('button', { name: 'Stage Install' }));
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Staging install' })).toHaveAttribute(
        'aria-busy',
        'true'
      );
      expect(screen.getByRole('button', { name: 'Close Editor' })).toBeEnabled();
      expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();
    });

    act(() => useWorkbenchStore.getState().setActiveSection('health'));
    await user.clear(screen.getByLabelText('Base RomFS'));
    await user.type(screen.getByLabelText('Base RomFS'), 'replacement-romfs');
    expect(useWorkbenchStore.getState().fashionUnlockWorkflow).toBeNull();

    await act(async () => resolveStage());

    expect(useWorkbenchStore.getState().editSession).toBeNull();
    expect(useWorkbenchStore.getState().fashionUnlockWorkflow).toBeNull();
    expect(useWorkbenchStore.getState().activeSection).toBe('health');
  });
});

function createFashionHarness(workflow: FashionUnlockWorkflow) {
  const paths = createPaths(workflow.detectedGame!);
  const bridge = createMockProjectBridge({}, true);
  bridge.loadFashionUnlockWorkflow = vi.fn(async () => ({ workflow }));
  bridge.stageFashionUnlockInstall = vi.fn(async (request) => ({
    diagnostics: [
      { message: 'Fashion Unlock install is staged for change-plan review.', severity: 'info' as const }
    ],
    session: createSession(workflow, 'install', request.session?.sessionId),
    workflow
  }));
  bridge.stageFashionUnlockUninstall = vi.fn(async (request) => ({
    diagnostics: [
      { message: 'Fashion Unlock uninstall is staged for change-plan review.', severity: 'info' as const }
    ],
    session: createSession(workflow, 'uninstall', request.session?.sessionId),
    workflow
  }));

  useWorkbenchStore.setState({
    activeSection: 'fashionUnlock',
    applyResult: null,
    changePlan: null,
    draftPaths: paths,
    editSession: null,
    editValidationDiagnostics: [],
    fashionUnlockWorkflow: workflow
  });

  return { bridge, paths };
}

function renderFashion(bridge: ProjectBridge) {
  return render(
    <LocalizationProvider>
      <App bridge={bridge} />
    </LocalizationProvider>
  );
}

function createSession(
  workflow: FashionUnlockWorkflow,
  action: FashionUnlockAction,
  existingSessionId?: string
): EditSession {
  const relativePath = 'exefs/main';
  const sources = workflow.editorFamily === 'swsh'
    ? [
        { layer: 'base' as const, relativePath },
        ...(workflow.provenance.sourceLayer === 'layered'
          ? [{ layer: 'layered' as const, relativePath }]
          : []),
        {
          layer: 'pending' as const,
          relativePath: `pending/fashion-unlock/${action}/${calculatePendingPayloadSha256('true')}`
        }
      ]
    : action === 'install'
      ? [{ layer: workflow.provenance.sourceLayer, relativePath }]
      : [
          { layer: 'generated' as const, relativePath },
          { layer: 'base' as const, relativePath }
        ];
  return {
    hasPendingChanges: true,
    pendingEdits: [
      {
        domain: 'workflow.fashionUnlock',
        field: action,
        newValue: 'true',
        recordId: `fashion-unlock-v1-${action}`,
        sources,
        summary: `Stage Fashion Unlock ${action}.`
      }
    ],
    sessionId: existingSessionId ?? `session-fashion-unlock-${action}`
  };
}

function createPaths(selectedGame: ProjectGame) {
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
