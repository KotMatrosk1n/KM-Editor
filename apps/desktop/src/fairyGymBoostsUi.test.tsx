/* SPDX-License-Identifier: GPL-3.0-only */

import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { type ApiDiagnostic } from './bridge/contracts';
import {
  type FairyGymBoostSelection,
  type FairyGymBoostsWorkflow
} from './bridge/fairyGymBoostsContracts';
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

async function createFairyGymHarness() {
  const fixtureBridge = createMockProjectBridge({}, true);
  const { workflow } = await fixtureBridge.loadFairyGymBoostsWorkflow({
    paths: projectPaths
  });
  const bridge = createMockProjectBridge({}, true);
  bridge.loadFairyGymBoostsWorkflow = vi.fn(async () => ({ workflow }));
  const fixtureStage = bridge.stageFairyGymBoosts;
  bridge.stageFairyGymBoosts = vi.fn(async (request) => ({
    ...(await fixtureStage(request)),
    workflow
  }));

  useWorkbenchStore.setState({
    activeSection: 'fairyGymBoosts',
    applyResult: null,
    changePlan: null,
    draftPaths: projectPaths,
    editSession: null,
    editValidationDiagnostics: [],
    fairyGymBoostsWorkflow: workflow
  });

  return { bridge, workflow };
}

function renderFairyGym(bridge: ProjectBridge) {
  return render(
    <LocalizationProvider>
      <App bridge={bridge} />
    </LocalizationProvider>
  );
}

async function getMagicUserOutcome(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('tab', { name: 'Opal' }));
  const card = screen.getByText('The magic-user').closest('article');
  expect(card).not.toBeNull();
  return within(card!).getByLabelText('The magic-user outcome');
}

describe('Fairy Gym Boosts UI', () => {
  beforeEach(() => {
    window.localStorage.clear();
    tauriEventMock.listen.mockClear();
    for (const eventName of Object.keys(tauriEventMock.listeners)) {
      delete tauriEventMock.listeners[eventName];
    }
    useWorkbenchStore.setState(useWorkbenchStore.getInitialState(), true);
  });

  it('shows verified ownership and accepts only a canonical staged edit', async () => {
    const user = userEvent.setup();
    const { bridge } = await createFairyGymHarness();
    renderFairyGym(bridge);

    expect(screen.getAllByText('Pokemon Sword').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('96')).toBeInTheDocument();
    expect(screen.getAllByText('0x00001550')).toHaveLength(6);
    expect(screen.getAllByText('0x00001550-0x0000155F')).toHaveLength(6);

    const outcome = await getMagicUserOutcome(user);
    await user.selectOptions(outcome, '0:none');
    await user.click(screen.getByRole('button', { name: 'Stage Fairy Gym Boosts' }));

    await waitFor(() =>
      expect(useWorkbenchStore.getState().editSession?.pendingEdits[0]).toMatchObject({
        field: 'boostSelections',
        recordId: 'fairy-gym-boosts',
        summary: 'Stage Fairy Gym boost outcomes.'
      })
    );
    expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled();

    act(() => {
      const session = useWorkbenchStore.getState().editSession!;
      useWorkbenchStore.setState({
        editSession: {
          ...session,
          pendingEdits: session.pendingEdits.map((edit) => ({
            ...edit,
            sources: edit.sources.map((source) =>
              source.layer === 'pending'
                ? {
                    ...source,
                    relativePath: `pending/fairy-gym-boosts/selections/${'A'.repeat(64)}`
                  }
                : source
            )
          }))
        }
      });
    });

    await waitFor(() => expect(screen.getAllByText('Invalid').length).toBeGreaterThan(0));
    expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
  });

  it('rejects a canonical response whose selections differ from the requested draft', async () => {
    const user = userEvent.setup();
    const { bridge, workflow } = await createFairyGymHarness();
    const canonicalStage = bridge.stageFairyGymBoosts;
    bridge.stageFairyGymBoosts = vi.fn((request) =>
      canonicalStage({
        ...request,
        selections: request.selections.map(
          (selection: FairyGymBoostSelection, index: number) =>
          index === 0
            ? { ...selection, effectId: 2, resultKind: 'increase' as const }
            : selection
        )
      })
    );
    renderFairyGym(bridge);

    const outcome = await getMagicUserOutcome(user);
    await user.selectOptions(outcome, '0:none');
    await user.click(screen.getByRole('button', { name: 'Stage Fairy Gym Boosts' }));

    expect(
      await screen.findByText(
        'Fairy Gym Boosts staging did not match the requested game, session, and selections.'
      )
    ).toBeInTheDocument();
    expect(useWorkbenchStore.getState().editSession).toBeNull();
    expect(useWorkbenchStore.getState().fairyGymBoostsWorkflow).toBe(workflow);
    expect(outcome).toHaveValue('0:none');
    expect(screen.getByRole('button', { name: 'Stage Fairy Gym Boosts' })).toBeEnabled();
  });

  it('rejects an initial staged response with an empty session identifier', async () => {
    const user = userEvent.setup();
    const { bridge, workflow } = await createFairyGymHarness();
    const canonicalStage = bridge.stageFairyGymBoosts;
    bridge.stageFairyGymBoosts = vi.fn(async (request) => {
      const response = await canonicalStage(request);
      return {
        ...response,
        session: { ...response.session, sessionId: '' }
      };
    });
    renderFairyGym(bridge);

    const outcome = await getMagicUserOutcome(user);
    await user.selectOptions(outcome, '0:none');
    await user.click(screen.getByRole('button', { name: 'Stage Fairy Gym Boosts' }));

    expect(
      await screen.findByText(
        'Fairy Gym Boosts staging did not match the requested game, session, and selections.'
      )
    ).toBeInTheDocument();
    expect(useWorkbenchStore.getState().editSession).toBeNull();
    expect(useWorkbenchStore.getState().fairyGymBoostsWorkflow).toBe(workflow);
    expect(outcome).toHaveValue('0:none');
    expect(screen.getByRole('button', { name: 'Stage Fairy Gym Boosts' })).toBeEnabled();
  });

  it.each(['game', 'session', 'pending'] as const)(
    'rejects a canonical restage with mismatched %s truth',
    async (mismatch) => {
      const user = userEvent.setup();
      const { bridge } = await createFairyGymHarness();
      renderFairyGym(bridge);

      const outcome = await getMagicUserOutcome(user);
      await user.selectOptions(outcome, '0:none');
      await user.click(screen.getByRole('button', { name: 'Stage Fairy Gym Boosts' }));
      await waitFor(() =>
        expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled()
      );

      const sessionBeforeRejection = useWorkbenchStore.getState().editSession!;
      const workflowBeforeRejection = useWorkbenchStore.getState()
        .fairyGymBoostsWorkflow!;
      const canonicalStage = bridge.stageFairyGymBoosts;
      bridge.stageFairyGymBoosts = vi.fn(async (request) => {
        const response = await canonicalStage(request);
        return {
          ...response,
          session:
            mismatch === 'session'
              ? { ...response.session, sessionId: 'different-session' }
              : mismatch === 'pending'
                ? { ...response.session, hasPendingChanges: false }
                : response.session,
          workflow:
            mismatch === 'game'
              ? { ...response.workflow, detectedGame: 'shield' as const }
              : response.workflow
        };
      });

      await user.selectOptions(outcome, '5:increase');
      await user.click(screen.getByRole('button', { name: 'Stage Fairy Gym Boosts' }));

      expect(
        await screen.findByText(
          'Fairy Gym Boosts staging did not match the requested game, session, and selections.'
        )
      ).toBeInTheDocument();
      expect(useWorkbenchStore.getState().editSession).toBe(
        sessionBeforeRejection
      );
      expect(useWorkbenchStore.getState().fairyGymBoostsWorkflow).toBe(
        workflowBeforeRejection
      );
      expect(outcome).toHaveValue('5:increase');
    }
  );

  it('preserves the prior workflow, session, plan, and visible draft after rejection', async () => {
    const user = userEvent.setup();
    const rejection: ApiDiagnostic = {
      domain: 'workflow.fairyGymBoosts',
      message: 'Fairy Gym Boosts staging was rejected.',
      severity: 'error'
    };
    const { bridge } = await createFairyGymHarness();
    renderFairyGym(bridge);

    const outcome = await getMagicUserOutcome(user);
    await user.selectOptions(outcome, '0:none');
    await user.click(screen.getByRole('button', { name: 'Stage Fairy Gym Boosts' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());

    const sessionBeforeRejection = useWorkbenchStore.getState().editSession!;
    const workflowBeforeRejection = useWorkbenchStore.getState()
      .fairyGymBoostsWorkflow!;
    bridge.stageFairyGymBoosts = vi.fn(async () => ({
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
        sources: workflowBeforeRejection.sources.map((source) => ({
          ...source,
          ownedRangeHex: 'unknown' as const,
          payloadOffsetHex: 'unknown' as const,
          status: 'blocked' as const
        }))
      } as FairyGymBoostsWorkflow
    }));

    await user.selectOptions(outcome, '5:increase');
    await user.click(screen.getByRole('button', { name: 'Stage Fairy Gym Boosts' }));

    expect(await screen.findByText(rejection.message)).toBeInTheDocument();
    expect(useWorkbenchStore.getState().editSession).toBe(sessionBeforeRejection);
    expect(useWorkbenchStore.getState().fairyGymBoostsWorkflow).toBe(
      workflowBeforeRejection
    );
    expect(screen.getByRole('heading', { name: 'Output Plan' })).toBeInTheDocument();
    expect(outcome).toHaveValue('5:increase');
    expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();

    await user.selectOptions(outcome, '0:none');
    expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled();
  });

  it('locks mutations and ignores a successful response after the editor is discarded', async () => {
    const user = userEvent.setup();
    const { bridge } = await createFairyGymHarness();
    const successfulStage = bridge.stageFairyGymBoosts;
    let resolveLateStage!: () => Promise<void>;
    bridge.stageFairyGymBoosts = vi.fn(
      (request) =>
        new Promise<Awaited<ReturnType<ProjectBridge['stageFairyGymBoosts']>>>(
          (resolve) => {
            resolveLateStage = async () => resolve(await successfulStage(request));
          }
        )
    );
    renderFairyGym(bridge);

    const outcome = await getMagicUserOutcome(user);
    await user.selectOptions(outcome, '0:none');
    await user.click(screen.getByRole('button', { name: 'Stage Fairy Gym Boosts' }));
    await waitFor(() => {
      expect(outcome).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Staging' })).toHaveAttribute(
        'aria-busy',
        'true'
      );
      expect(screen.getByRole('button', { name: 'Restore to Vanilla' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();
    });

    await user.click(screen.getByRole('button', { name: 'Close Editor' }));
    await user.click(await screen.findByRole('button', { name: 'Yes, Discard' }));
    await waitFor(() => expect(useWorkbenchStore.getState().activeSection).toBe('workflows'));

    await act(async () => {
      await resolveLateStage();
    });

    expect(useWorkbenchStore.getState().editSession).toBeNull();
    expect(useWorkbenchStore.getState().fairyGymBoostsWorkflow).toBeNull();
    expect(useWorkbenchStore.getState().activeSection).toBe('workflows');
  });
});
