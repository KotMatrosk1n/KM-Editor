/* SPDX-License-Identifier: GPL-3.0-only */

import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import type {
  ApiDiagnostic,
  DynamaxAdventuresWorkflow,
  EditSession,
  UpdateDynamaxAdventureFieldResponse
} from './bridge/contracts';
import type { ProjectBridge } from './bridge/projectBridge';
import { LocalizationProvider } from './localization';
import {
  createHealthForValidatedPaths,
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

vi.mock('@tauri-apps/api/event', () => ({ listen: tauriEventMock.listen }));

const projectPaths = {
  baseExeFsPath: 'base-exefs',
  baseRomFsPath: 'base-romfs',
  outputRootPath: 'output',
  pokemonLegendsZASupportFolderPath: '',
  saveFilePath: '',
  scarletVioletSupportFolderPath: '',
  selectedGame: 'sword' as const
};

describe('Dynamax Adventures UI', () => {
  beforeEach(() => {
    window.localStorage.clear();
    tauriEventMock.listen.mockClear();
    for (const eventName of Object.keys(tauriEventMock.listeners)) {
      delete tauriEventMock.listeners[eventName];
    }
    useWorkbenchStore.setState(useWorkbenchStore.getInitialState(), true);
  });

  it('keeps the Dynamax Adventures editor safe across representative workflows', async () => {
    for (const scenario of [
      verifyReadOnlyTableAndSlots,
      verifyRandomIvStaging,
      verifyFixedHpIvControls,
      verifyNormalGigantamaxStateControl,
      verifySelectedRestore,
      verifyAtomicRejection,
      verifyStaleApply,
      verifyRefreshFailureAfterSuccessfulApply,
      verifyRepairFlow,
      verifyLegacyBossTargetRepairGate,
      verifyTableRestoreFlow,
      verifyBlockedRecoveryProjection,
      verifyPendingOwnerPlan,
      verifyInvalidRevalidation,
      verifyNonzeroFormPreview,
      verifyPreviewRecovery
    ]) {
      await scenario();
      cleanup();
    }
  }, 30_000);

  async function verifyReadOnlyTableAndSlots() {
    const user = userEvent.setup();
    const fixtureBridge = createMockProjectBridge({}, true);
    const { workflow } = await fixtureBridge.loadDynamaxAdventuresWorkflow({
      paths: projectPaths
    });
    const readOnlyWorkflow = structuredClone(workflow);
    readOnlyWorkflow.encounters[0]!.isEditable = false;
    readOnlyWorkflow.encounters[0]!.layoutWritableFields = [];
    const swappedMoves = readOnlyWorkflow.encounters[0]!.moves;
    [swappedMoves[0], swappedMoves[1]] = [swappedMoves[1]!, swappedMoves[0]!];
    renderDynamaxAdventures(createMockProjectBridge({}, true), readOnlyWorkflow);

    const table = screen.getByRole('table', { name: 'Dynamax Adventure encounters' });
    expect(table).toHaveAttribute('aria-colcount', '6');
    expect(within(table).getAllByRole('columnheader').map((header) => header.textContent)).toEqual([
      'Index',
      'Pokemon',
      'Level',
      'IVs',
      'Moves',
      'Source'
    ]);
    const firstRowCells = within(table).getAllByRole('row')[1]!.querySelectorAll('[role="cell"]');
    expect(firstRowCells).toHaveLength(6);
    expect(firstRowCells[0]).toHaveTextContent('0');
    expect(firstRowCells[2]).toHaveTextContent('65');
    expect(screen.queryByText('Boss target species')).not.toBeInTheDocument();

    const technicalDetailsSummary = screen.getByText('Technical details', {
      selector: 'summary'
    });
    const technicalDetails = technicalDetailsSummary.closest('details');
    expect(technicalDetails).not.toBeNull();
    expect(technicalDetails).not.toHaveAttribute('open');
    expect(within(technicalDetails!).getByText('Build ID')).toBeInTheDocument();
    expect(within(technicalDetails!).getByText(workflow.buildId)).toBeInTheDocument();
    expect(
      within(technicalDetails!).getByText(workflow.reservedRegions[0]!.label)
    ).toBeInTheDocument();
    await user.click(technicalDetailsSummary);
    expect(technicalDetails).toHaveAttribute('open');

    const selectedSummary = screen.getByRole('complementary', {
      name: 'Selected Dynamax Adventure provenance'
    });
    const selectedEditor = screen.getByRole('region', {
      name: 'Selected Adventure'
    });
    expect(table).toAppearBefore(selectedSummary);
    expect(selectedSummary).toAppearBefore(selectedEditor);
    expect(selectedSummary).toHaveClass('dynamax-adventure-summary');
    expect(selectedEditor).toHaveClass('dynamax-adventure-editor');
    for (const groupName of ['Pokemon', 'Traits', 'Moves', 'Stats - IVs']) {
      expect(
        within(selectedEditor).getByRole('group', { name: groupName })
      ).toBeInTheDocument();
    }

    expect(screen.getByLabelText('Species')).toBeDisabled();
    expect(screen.getByLabelText('Move 1')).toHaveValue('001 Scratch');
    expect(screen.getByLabelText('Move 2')).toHaveValue('002 Growl');
    expect(screen.getByRole('button', { name: 'Stage Changes' })).toBeDisabled();
  }

  async function verifyRandomIvStaging() {
    const user = userEvent.setup();
    const fixtureBridge = createMockProjectBridge({}, true);
    const { workflow } = await fixtureBridge.loadDynamaxAdventuresWorkflow({
      paths: projectPaths
    });
    const randomIvWorkflow = structuredClone(workflow);
    randomIvWorkflow.encounters[0]!.ivs.attack = 31;
    randomIvWorkflow.encounters[0]!.ivSummary =
      '5 guaranteed perfect / Atk 31 / Def Random / SpA Random / SpD Random / Spe Random';
    const bridge = createMockProjectBridge({}, true);
    const update = vi.spyOn(bridge, 'updateDynamaxAdventureField');
    renderDynamaxAdventures(bridge, randomIvWorkflow);

    await user.click(screen.getByRole('button', { name: 'Show Species options' }));
    expect(screen.getByRole('option', { name: '001 Bulbasaur' })).toBeInTheDocument();
    const search = screen.getByRole('searchbox', { name: 'Search Dynamax Adventures' });
    await user.type(search, 'Groo');
    await selectSearchableOption(user, 'Guaranteed perfect IVs', 'Custom');
    await selectSearchableOption(user, 'Attack IV override', 'Random');
    await user.click(screen.getByRole('button', { name: 'Stage Changes' }));
    await waitFor(() => expect(update).toHaveBeenCalledTimes(1));
    expect(update).toHaveBeenCalledWith(
      expect.objectContaining({ entryIndex: 0, field: 'ivAttack', value: '-1' })
    );
    expect(search).toHaveValue('Groo');
    expect(useWorkbenchStore.getState().dynamaxAdventureSearchText).toBe('Groo');
    expect(useWorkbenchStore.getState().selectedDynamaxAdventureEntryIndex).toBe(0);
  }

  async function verifyFixedHpIvControls() {
    const fixtureBridge = createMockProjectBridge({}, true);
    const { workflow } = await fixtureBridge.loadDynamaxAdventuresWorkflow({
      paths: projectPaths
    });
    const fixedHpWorkflow = structuredClone(workflow);
    const encounter = fixedHpWorkflow.encounters[0]!;
    encounter.guaranteedPerfectIvs = 0;
    encounter.ivs.hp = 17;
    encounter.ivSummary =
      'HP 17 / Atk Random / Def Random / SpA Random / SpD Random / Spe Random';
    encounter.layoutWritableFields = encounter.layoutWritableFields.filter(
      (field) => field !== 'guaranteedPerfectIvs'
    );
    renderDynamaxAdventures(createMockProjectBridge({}, true), fixedHpWorkflow);

    expect(screen.getByLabelText('Guaranteed perfect IVs')).toHaveValue('Custom');
    expect(screen.getByLabelText('Guaranteed perfect IVs')).toBeDisabled();
    for (const label of [
      'Attack IV override',
      'Defense IV override',
      'Sp. Atk IV override',
      'Sp. Def IV override',
      'Speed IV override'
    ]) {
      expect(screen.getByLabelText(label)).toBeEnabled();
    }
  }

  async function verifyNormalGigantamaxStateControl() {
    const fixtureBridge = createMockProjectBridge({}, true);
    const { workflow } = await fixtureBridge.loadDynamaxAdventuresWorkflow({
      paths: projectPaths
    });
    const normalStateWorkflow = structuredClone(workflow);
    const encounter = normalStateWorkflow.encounters[0]!;
    encounter.gigantamaxState = 0;
    encounter.gigantamaxLabel = 'Unknown';
    encounter.gigantamaxOptions = [
      { label: 'Unknown', value: 0 },
      { label: 'Normal', value: 1 }
    ];
    renderDynamaxAdventures(createMockProjectBridge({}, true), normalStateWorkflow);

    expect(screen.getByLabelText('Gigantamax state')).toHaveValue('Unknown');
    expect(screen.getByLabelText('Gigantamax state')).toBeEnabled();
  }

  async function verifySelectedRestore() {
    const user = userEvent.setup();
    const fixtureBridge = createMockProjectBridge({}, true);
    const { workflow } = await fixtureBridge.loadDynamaxAdventuresWorkflow({
      paths: projectPaths
    });
    const twoRowWorkflow = createTwoRowWorkflow(workflow);
    const restoreBridge = createMockProjectBridge({}, true);
    restoreBridge.updateDynamaxAdventureField = vi.fn((request) =>
      Promise.resolve(createStagedResponse(request, twoRowWorkflow))
    );
    renderDynamaxAdventures(restoreBridge, twoRowWorkflow);
    await user.click(screen.getByRole('button', { name: 'Stage Restore' }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Stage Restore' })).toBeEnabled()
    );
    const restoredEntryIndexes = vi
      .mocked(restoreBridge.updateDynamaxAdventureField)
      .mock.calls.map(([request]) => request.entryIndex);
    expect(restoredEntryIndexes.length).toBeGreaterThan(0);
    expect(restoredEntryIndexes.every((entryIndex) => entryIndex === 0)).toBe(true);
  }

  async function verifyAtomicRejection() {
    const user = userEvent.setup();
    const fixtureBridge = createMockProjectBridge({}, true);
    const { workflow } = await fixtureBridge.loadDynamaxAdventuresWorkflow({
      paths: projectPaths
    });
    const rejection: ApiDiagnostic = {
      domain: 'workflow.dynamaxAdventures',
      message: 'Dynamax Adventure staging was rejected.',
      severity: 'error'
    };
    const rejectedBridge = createMockProjectBridge({}, true);
    const currentPreview: Awaited<
      ReturnType<ProjectBridge['previewDynamaxAdventureDefaults']>
    > = {
      abilityOptions: workflow.encounters[0]!.abilityOptions,
      changes: [
        { field: 'form', value: '0' },
        { field: 'ability', value: '0' },
        { field: 'gigantamaxState', value: '1' },
        { field: 'move0Id', value: '1' },
        { field: 'move1Id', value: '2' },
        { field: 'move2Id', value: '0' },
        { field: 'move3Id', value: '0' }
      ],
      diagnostics: [],
      gigantamaxOptions: workflow.encounters[0]!.gigantamaxOptions,
      moveOptions: workflow.encounters[0]!.moveOptions
    };
    rejectedBridge.previewDynamaxAdventureDefaults = vi.fn(() =>
      Promise.resolve(currentPreview)
    );
    const originalRejectedUpdate = rejectedBridge.updateDynamaxAdventureField;
    let releaseFirstUpdate!: () => Promise<void>;
    let updateCallCount = 0;
    rejectedBridge.updateDynamaxAdventureField = vi.fn(async (
      request: Parameters<typeof originalRejectedUpdate>[0]
    ) => {
      updateCallCount += 1;
      if (updateCallCount === 1) {
        return new Promise<Awaited<ReturnType<typeof originalRejectedUpdate>>>((resolve) => {
          releaseFirstUpdate = () => originalRejectedUpdate(request).then(resolve);
        });
      }

      const accepted = await originalRejectedUpdate(request);
      return { ...accepted, diagnostics: [rejection] };
    });
    renderDynamaxAdventures(rejectedBridge, workflow);
    const rejectedSearch = screen.getByRole('searchbox', {
      name: 'Search Dynamax Adventures'
    });
    await user.type(rejectedSearch, 'Groo');
    const levelInput = screen.getByLabelText('Level');
    await user.clear(levelInput);
    await user.type(levelInput, '66');
    await selectSearchableOption(user, 'Guaranteed perfect IVs', 'Custom');
    await selectSearchableOption(user, 'Attack IV override', '31 IV');
    await user.click(screen.getByRole('button', { name: 'Stage Changes' }));
    await waitFor(() =>
      expect(rejectedBridge.updateDynamaxAdventureField).toHaveBeenCalledTimes(1)
    );
    expect(
      screen.getAllByRole('button', { name: 'Staging' }).every((button) => button.hasAttribute('disabled'))
    ).toBe(true);
    await act(async () => releaseFirstUpdate());
    await waitFor(() =>
      expect(rejectedBridge.updateDynamaxAdventureField).toHaveBeenCalledTimes(2)
    );
    await screen.findByText(rejection.message);
    expect(
      vi.mocked(rejectedBridge.updateDynamaxAdventureField).mock.calls.map(
        ([request]) => request.field
      )
    ).toEqual(['level', 'ivAttack']);
    expect(useWorkbenchStore.getState().editSession).toBeNull();
    expect(useWorkbenchStore.getState().dynamaxAdventuresWorkflow).toBe(workflow);
    expect(rejectedSearch).toHaveValue('Groo');
    expect(levelInput).toHaveValue(66);
    expect(screen.getByLabelText('Attack IV override')).toHaveValue('31 IV');
  }

  async function verifyStaleApply() {
    const user = userEvent.setup();
    const fixtureBridge = createMockProjectBridge({}, true);
    const { workflow } = await fixtureBridge.loadDynamaxAdventuresWorkflow({
      paths: projectPaths
    });
    const bridge = createMockProjectBridge({}, true);
    const originalApply = bridge.applyChangePlan;
    const originalLoad = bridge.loadDynamaxAdventuresWorkflow;
    let releaseApply!: () => Promise<void>;
    bridge.applyChangePlan = vi.fn(
      (request: Parameters<typeof originalApply>[0]) =>
        new Promise<Awaited<ReturnType<typeof originalApply>>>((resolve) => {
          releaseApply = () => originalApply(request).then(resolve);
        })
    );
    bridge.loadDynamaxAdventuresWorkflow = vi.fn(originalLoad);
    renderDynamaxAdventures(bridge, workflow);
    await selectSearchableOption(user, 'Guaranteed perfect IVs', 'Custom');
    await selectSearchableOption(user, 'Attack IV override', '31 IV');
    await user.click(screen.getByRole('button', { name: 'Stage Changes' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Apply' }));

    const newerSession = createPendingSession('newer-session', 0, 'level', '70');
    act(() => useWorkbenchStore.setState({ editSession: newerSession }));
    await act(async () => releaseApply());
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: 'Applying' })).not.toBeInTheDocument()
    );
    expect(useWorkbenchStore.getState().editSession).toEqual(newerSession);
    expect(bridge.loadDynamaxAdventuresWorkflow).not.toHaveBeenCalled();
  }

  async function verifyRefreshFailureAfterSuccessfulApply() {
    const user = userEvent.setup();
    const fixtureBridge = createMockProjectBridge({}, true);
    const { workflow } = await fixtureBridge.loadDynamaxAdventuresWorkflow({
      paths: projectPaths
    });
    const bridge = createMockProjectBridge({}, true);
    bridge.applyChangePlan = vi.fn(bridge.applyChangePlan);
    bridge.refreshFileGraph = vi.fn(() =>
      Promise.reject(new Error('Refresh failed after the reviewed files were written.'))
    );
    renderDynamaxAdventures(bridge, workflow, true);

    await selectSearchableOption(user, 'Guaranteed perfect IVs', 'Custom');
    await selectSearchableOption(user, 'Attack IV override', '31 IV');
    await user.click(screen.getByRole('button', { name: 'Stage Changes' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Apply' }));

    await screen.findByRole('heading', { name: 'Apply Result' });
    await screen.findByText(/Refresh failed after the reviewed files were written/);
    expect(useWorkbenchStore.getState().editSession).toBeNull();
    expect(screen.queryByRole('heading', { name: 'Output Plan' })).not.toBeInTheDocument();
    const applyButton = screen.queryByRole('button', { name: 'Apply' });
    expect(applyButton === null || applyButton.hasAttribute('disabled')).toBe(true);
    if (applyButton) {
      fireEvent.click(applyButton);
    }
    expect(bridge.applyChangePlan).toHaveBeenCalledTimes(1);
  }

  async function verifyRepairFlow() {
    const user = userEvent.setup();
    const fixtureBridge = createMockProjectBridge({}, true);
    const { workflow } = await fixtureBridge.loadDynamaxAdventuresWorkflow({
      paths: projectPaths
    });
    const repairableWorkflow = createTwoRowWorkflow({
      ...workflow,
      installMessage: 'Dynamax Adventures executable projection requires repair.',
      installStatus: 'repairable'
    });
    const bridge = createMockProjectBridge({}, true);
    const stageRepair = vi.spyOn(bridge, 'stageDynamaxAdventureRepair');
    renderDynamaxAdventures(bridge, repairableWorkflow);
    const table = screen.getByRole('table', { name: 'Dynamax Adventure encounters' });
    await user.click(within(table).getAllByRole('row')[2]!);
    expect(useWorkbenchStore.getState().selectedDynamaxAdventureEntryIndex).toBe(1);

    await user.click(screen.getByRole('button', { name: 'Stage Repair' }));
    await waitFor(() => expect(stageRepair).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());
    expect(useWorkbenchStore.getState().selectedDynamaxAdventureEntryIndex).toBe(0);
    expect(screen.getByRole('status')).toHaveTextContent(
      'Pending changes belong to Adventure index 0.'
    );
    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());
    const plan = screen.getByRole('heading', { name: 'Output Plan' }).closest('section')!;
    expect(within(plan).getAllByText('exefs/main').length).toBeGreaterThan(0);
    expect(
      within(plan).queryByText(
        'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin'
      )
    ).not.toBeInTheDocument();
  }

  async function verifyLegacyBossTargetRepairGate() {
    const user = userEvent.setup();
    const fixtureBridge = createMockProjectBridge({}, true);
    const { workflow } = await fixtureBridge.loadDynamaxAdventuresWorkflow({
      paths: projectPaths
    });
    const legacyWorkflow: DynamaxAdventuresWorkflow = {
      ...workflow,
      hasLegacyBossTargetPatch: true,
      installMessage:
        'An exact legacy Dynamax Adventures final-boss target remap is installed.',
      installStatus: 'repairable'
    };
    const bridge = createMockProjectBridge({}, true);
    const previewDefaults = vi.spyOn(bridge, 'previewDynamaxAdventureDefaults');
    const stageRepair = vi.spyOn(bridge, 'stageDynamaxAdventureRepair');
    renderDynamaxAdventures(bridge, legacyWorkflow);

    expect(
      screen.getByText(
        'Unsupported legacy final-boss target remap detected. Ordinary row editing and default previews are disabled. Stage Repair or a full vanilla table restore removes it.'
      )
    ).toBeInTheDocument();
    expect(screen.getByLabelText('Species')).toBeDisabled();
    expect(screen.getByLabelText('Level')).toBeDisabled();
    expect(
      screen.getAllByText(
        'Stage Repair or a full vanilla table restore must remove the unsupported legacy final-boss target remap before this field can be edited.'
      ).length
    ).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: 'Stage Changes' })).toBeDisabled();

    fireEvent.change(screen.getByLabelText('Level'), { target: { value: '70' } });
    expect(previewDefaults).not.toHaveBeenCalled();

    const repairButton = screen.getByRole('button', { name: 'Stage Repair' });
    expect(repairButton).toBeEnabled();
    await user.click(repairButton);
    await waitFor(() => expect(stageRepair).toHaveBeenCalledTimes(1));
    expect(previewDefaults).not.toHaveBeenCalled();
  }

  async function verifyTableRestoreFlow() {
    const user = userEvent.setup();
    const fixtureBridge = createMockProjectBridge({}, true);
    const { workflow } = await fixtureBridge.loadDynamaxAdventuresWorkflow({
      paths: projectPaths
    });
    const layoutDiagnostic: ApiDiagnostic = {
      domain: 'workflow.dynamaxAdventures',
      message:
        'Dynamax Adventures source table byte layout differs from the vanilla table. Restore the Adventure table from a clean dump before making new Pokemon edits.',
      severity: 'error'
    };
    const restoreWorkflow = createTwoRowWorkflow({
      ...workflow,
      canRestoreVanillaTable: true,
      diagnostics: [layoutDiagnostic],
      hasLegacyBossTargetPatch: true,
      installStatus: 'repairable',
      restoreVanillaTableMessage:
        'Restore is available. Applying it removes all layered Adventure-table changes and restores the verified vanilla table.',
      summary: {
        ...workflow.summary,
        availability: 'readOnly'
      },
      usesVanillaRecoveryProjection: true
    });
    restoreWorkflow.encounters = restoreWorkflow.encounters.map((encounter) => ({
      ...encounter,
      isEditable: false,
      layoutWritableFields: [],
      provenance: {
        ...encounter.provenance,
        fileState: 'layeredOverride',
        sourceLayer: 'layered'
      },
      vanillaPokemon: createSnapshot(encounter)
    }));

    const bridge = createMockProjectBridge({}, true);
    const owner = restoreWorkflow.encounters[0]!;
    bridge.stageDynamaxAdventureRestore = vi.fn((request) =>
      Promise.resolve({
        diagnostics: [],
        session: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.dynamaxAdventures',
              field: 'level',
              newValue: owner.level.toString(),
              recordId: `dynamaxAdventure:${owner.entryIndex}`,
              sources: [
                {
                  layer: owner.provenance.sourceLayer,
                  relativePath: owner.provenance.sourceFile
                }
              ],
              summary: 'Restore the vanilla Dynamax Adventures table.'
            }
          ],
          sessionId: request.session?.sessionId ?? 'dynamax-session'
        },
        workflow: restoreWorkflow
      })
    );
    renderDynamaxAdventures(bridge, restoreWorkflow);

    expect(screen.getByRole('button', { name: 'Stage Table Restore' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: 'Stage Repair' })).not.toBeInTheDocument();
    expect(
      screen.getByText(
        'Restoring the vanilla table removes all layered Dynamax Adventures table changes.'
      )
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        'Recovery view: the rows below show verified vanilla target values. Current layered values that require recovery are hidden and cannot be edited.'
      )
    ).toBeInTheDocument();
    expect(screen.getByLabelText('Species')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Stage Changes' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Stage Restore' })).toBeDisabled();

    const table = screen.getByRole('table', { name: 'Dynamax Adventure encounters' });
    await user.click(within(table).getAllByRole('row')[2]!);
    expect(useWorkbenchStore.getState().selectedDynamaxAdventureEntryIndex).toBe(1);
    await user.click(screen.getByRole('button', { name: 'Stage Table Restore' }));
    await waitFor(() =>
      expect(bridge.stageDynamaxAdventureRestore).toHaveBeenCalledTimes(1)
    );
    await waitFor(() =>
      expect(useWorkbenchStore.getState().selectedDynamaxAdventureEntryIndex).toBe(0)
    );
    expect(screen.getByRole('status')).toHaveTextContent(
      'Full vanilla-table restore is pending.'
    );
    expect(screen.getByRole('status')).not.toHaveTextContent(
      'Pending changes belong to Adventure index'
    );
    expect(screen.getByLabelText('Species')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Stage Changes' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled();

    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());
    const plan = screen.getByRole('heading', { name: 'Output Plan' }).closest('section')!;
    expect(
      within(plan).getAllByText(
        'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin'
      ).length
    ).toBeGreaterThan(0);
    expect(
      within(plan).getByText(
        'Remove all layered Dynamax Adventures table changes and restore the verified vanilla table.'
      )
    ).toBeInTheDocument();
  }

  async function verifyBlockedRecoveryProjection() {
    const fixtureBridge = createMockProjectBridge({}, true);
    const { workflow } = await fixtureBridge.loadDynamaxAdventuresWorkflow({
      paths: projectPaths
    });
    const blockedWorkflow: DynamaxAdventuresWorkflow = {
      ...workflow,
      canRestoreVanillaTable: false,
      diagnostics: [
        {
          domain: 'workflow.dynamaxAdventures',
          message:
            'Dynamax Adventures row 0 contains species outside the supported API domain.',
          severity: 'error'
        },
        {
          domain: 'workflow.dynamaxAdventures',
          file: 'exefs/main',
          message: 'Dynamax Adventures found a non-owned executable state.',
          severity: 'error'
        }
      ],
      encounters: workflow.encounters.map((encounter) => ({
        ...encounter,
        isEditable: false,
        layoutWritableFields: [],
        provenance: {
          ...encounter.provenance,
          fileState: 'layeredOverride',
          sourceLayer: 'layered'
        },
        vanillaPokemon: createSnapshot(encounter)
      })),
      installMessage: 'Dynamax Adventures executable state is blocked by a conflict.',
      installStatus: 'blocked',
      summary: {
        ...workflow.summary,
        availability: 'readOnly'
      },
      usesVanillaRecoveryProjection: true
    };

    renderDynamaxAdventures(createMockProjectBridge({}, true), blockedWorkflow);

    expect(
      screen.getByText(
        'Recovery view: the rows below show verified vanilla target values. Current layered values that require recovery are hidden and cannot be edited.'
      )
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Stage Table Restore' })
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: 'Stage Repair' })
    ).not.toBeInTheDocument();
    expect(screen.getByLabelText('Species')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Stage Changes' })).toBeDisabled();
  }

  async function verifyPendingOwnerPlan() {
    const user = userEvent.setup();
    const fixtureBridge = createMockProjectBridge({}, true);
    const { workflow } = await fixtureBridge.loadDynamaxAdventuresWorkflow({
      paths: projectPaths
    });
    const twoRowWorkflow = createTwoRowWorkflow(workflow);
    const bridge = createMockProjectBridge({}, true);
    bridge.updateDynamaxAdventureField = vi.fn((request) =>
      Promise.resolve(createStagedResponse(request, twoRowWorkflow))
    );
    renderDynamaxAdventures(bridge, twoRowWorkflow);
    await selectSearchableOption(user, 'Guaranteed perfect IVs', 'Custom');
    await selectSearchableOption(user, 'Attack IV override', '31 IV');
    await user.click(screen.getByRole('button', { name: 'Stage Changes' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());
    expect(screen.getByRole('status')).toHaveTextContent(
      'Pending changes belong to Adventure index 0.'
    );
    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());

    await user.type(
      screen.getByRole('searchbox', { name: 'Search Dynamax Adventures' }),
      'Bulb'
    );
    const table = screen.getByRole('table', { name: 'Dynamax Adventure encounters' });
    expect(within(table).getAllByRole('row')).toHaveLength(3);
    expect(within(table).getAllByRole('row')[1]).toHaveTextContent('Grookey');
    await user.click(within(table).getAllByRole('row')[2]!);
    expect(screen.getByRole('button', { name: 'Review' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();
    expect(screen.getByRole('status')).toHaveTextContent('Select that row to review or apply.');
    await user.click(within(table).getAllByRole('row')[1]!);
    expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled();
  }

  async function verifyInvalidRevalidation() {
    const user = userEvent.setup();
    const fixtureBridge = createMockProjectBridge({}, true);
    const { workflow } = await fixtureBridge.loadDynamaxAdventuresWorkflow({
      paths: projectPaths
    });
    const bridge = createMockProjectBridge({}, true);
    const originalValidate = bridge.validateEditSession;
    let validationCount = 0;
    bridge.validateEditSession = vi.fn(async (request) => {
      validationCount += 1;
      if (validationCount === 1) {
        return originalValidate(request);
      }
      return {
        diagnostics: [
          {
            domain: 'workflow.dynamaxAdventures',
            message: 'Dynamax Adventure revalidation failed.',
            severity: 'error' as const
          }
        ],
        isValid: false,
        session: request.session
      };
    });
    renderDynamaxAdventures(bridge, workflow);
    await selectSearchableOption(user, 'Guaranteed perfect IVs', 'Custom');
    await selectSearchableOption(user, 'Attack IV override', '31 IV');
    await user.click(screen.getByRole('button', { name: 'Stage Changes' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Review' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Review' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Apply' })).toBeEnabled());
    expect(screen.getByRole('heading', { name: 'Output Plan' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Review' }));
    await screen.findByText('Dynamax Adventure revalidation failed.');
    expect(screen.getByRole('button', { name: 'Apply' })).toBeDisabled();
    expect(screen.queryByRole('heading', { name: 'Output Plan' })).not.toBeInTheDocument();
  }

  async function verifyNonzeroFormPreview() {
    const user = userEvent.setup();
    const fixtureBridge = createMockProjectBridge({}, true);
    const { workflow } = await fixtureBridge.loadDynamaxAdventuresWorkflow({
      paths: projectPaths
    });
    const regionalWorkflow = structuredClone(workflow);
    regionalWorkflow.encounters[0]!.form = 1;
    regionalWorkflow.encounters[0]!.vanillaPokemon!.form = 2;
    const bridge = createMockProjectBridge({}, true);
    const originalPreview = bridge.previewDynamaxAdventureDefaults;
    bridge.previewDynamaxAdventureDefaults = vi.fn(originalPreview);
    renderDynamaxAdventures(bridge, regionalWorkflow);

    fireEvent.change(screen.getByLabelText('Level'), { target: { value: '70' } });
    await waitFor(() =>
      expect(bridge.previewDynamaxAdventureDefaults).toHaveBeenCalledTimes(1)
    );
    expect(bridge.previewDynamaxAdventureDefaults).toHaveBeenLastCalledWith(
      expect.objectContaining({ form: 1, level: 70, species: 810 })
    );
    await waitFor(() => expect(screen.getByLabelText('Form')).toHaveValue('1 Form'));

    await selectSearchableOption(user, 'Species', '001 Bulbasaur');
    await waitFor(() =>
      expect(bridge.previewDynamaxAdventureDefaults).toHaveBeenCalledTimes(2)
    );
    expect(bridge.previewDynamaxAdventureDefaults).toHaveBeenLastCalledWith(
      expect.objectContaining({ form: 2, level: 70, species: 1 })
    );
  }

  async function verifyPreviewRecovery() {
    const user = userEvent.setup();
    const fixtureBridge = createMockProjectBridge({}, true);
    const { workflow } = await fixtureBridge.loadDynamaxAdventuresWorkflow({
      paths: projectPaths
    });
    const twoRowWorkflow = createTwoRowWorkflow(workflow);
    const previewBridge = createMockProjectBridge({}, true);
    const originalPreview = previewBridge.previewDynamaxAdventureDefaults;
    const releasePreviews: Array<() => void> = [];
    previewBridge.previewDynamaxAdventureDefaults = vi.fn(
      (_request: Parameters<typeof originalPreview>[0]) =>
        new Promise<Awaited<ReturnType<typeof originalPreview>>>((resolve) => {
          releasePreviews.push(() =>
            resolve({
              abilityOptions: [{ label: 'Hidden Ability', value: 2 }],
              changes: [
                { field: 'form', value: '0' },
                { field: 'ability', value: '2' },
                { field: 'gigantamaxState', value: '1' },
                { field: 'move0Id', value: '1' },
                { field: 'move1Id', value: '2' },
                { field: 'move2Id', value: '1' },
                { field: 'move3Id', value: '2' }
              ],
              diagnostics: [],
              gigantamaxOptions: [{ label: 'Normal', value: 1 }],
              moveOptions: [
                { label: '001 Scratch', value: 1 },
                { label: '002 Growl', value: 2 }
              ]
            })
          );
        })
    );
    renderDynamaxAdventures(previewBridge, twoRowWorkflow);
    await selectSearchableOption(user, 'Species', '467 Magmortar');
    await waitFor(() =>
      expect(previewBridge.previewDynamaxAdventureDefaults).toHaveBeenCalledTimes(1)
    );
    expect(previewBridge.previewDynamaxAdventureDefaults).toHaveBeenLastCalledWith(
      expect.objectContaining({ entryIndex: 0, species: 467 })
    );
    const previewTable = screen.getByRole('table', {
      name: 'Dynamax Adventure encounters'
    });
    await user.click(within(previewTable).getAllByRole('row')[2]!);
    await act(async () => {
      releasePreviews[0]!();
      await Promise.resolve();
    });
    await user.click(within(previewTable).getAllByRole('row')[1]!);
    expect(screen.getByLabelText('Ability roll')).toHaveValue('');
    await selectSearchableOption(user, 'Species', '467 Magmortar');
    await waitFor(() =>
      expect(previewBridge.previewDynamaxAdventureDefaults).toHaveBeenCalledTimes(2)
    );
    expect(
      vi.mocked(previewBridge.previewDynamaxAdventureDefaults).mock.calls.map(
        ([request]) => request.species
      )
    ).toEqual([467, 467]);
    const previewLevel = screen.getByLabelText('Level');
    fireEvent.change(previewLevel, { target: { value: '70' } });
    await waitFor(() =>
      expect(previewBridge.previewDynamaxAdventureDefaults).toHaveBeenCalledTimes(3)
    );
    await act(async () => {
      releasePreviews[1]!();
      await Promise.resolve();
    });
    expect(screen.getByLabelText('Ability roll')).toHaveValue('');
    await act(async () => {
      releasePreviews[2]!();
      await Promise.resolve();
    });
    await waitFor(() => expect(previewLevel).toHaveValue(70));
    expect(screen.getByLabelText('Ability roll')).toHaveValue('Hidden Ability');
    expect(
      vi.mocked(previewBridge.previewDynamaxAdventureDefaults).mock.calls.map(
        ([request]) => [request.species, request.level]
      )
    ).toEqual([
      [467, 65],
      [467, 65],
      [467, 70]
    ]);
  }
});

function renderDynamaxAdventures(
  bridge: ProjectBridge,
  workflow: DynamaxAdventuresWorkflow,
  withOpenProject = false
) {
  useWorkbenchStore.setState(useWorkbenchStore.getInitialState(), true);
  const health = createHealthForValidatedPaths(
    projectPaths.baseRomFsPath,
    projectPaths.baseExeFsPath,
    projectPaths.outputRootPath,
    null
  );
  useWorkbenchStore.setState({
    activeSection: 'dynamaxAdventures',
    draftPaths: projectPaths,
    dynamaxAdventureSearchText: '',
    dynamaxAdventuresWorkflow: workflow,
    editSession: null,
    openProject: withOpenProject
      ? {
          fileGraph: { entries: [], summary: health.fileGraph },
          health,
          projectId: 'dynamax-adventures-project'
        }
      : null,
    selectedDynamaxAdventureEntryIndex: 0,
    workflows: [workflow.summary]
  });
  return render(
    <LocalizationProvider>
      <App bridge={bridge} />
    </LocalizationProvider>
  );
}

async function selectSearchableOption(
  user: ReturnType<typeof userEvent.setup>,
  label: string,
  option: string
) {
  const input = screen.getByLabelText(label);
  await user.clear(input);
  await user.type(input, option);
  await user.keyboard('{Enter}');
}

function createTwoRowWorkflow(
  workflow: DynamaxAdventuresWorkflow
): DynamaxAdventuresWorkflow {
  const first = workflow.encounters[0]!;
  const second = {
    ...structuredClone(first),
    adventureIndex: 1,
    entryIndex: 1,
    label: '001 / 001 - Bulbasaur',
    bossTargetSpecies: 'Bulbasaur',
    bossTargetSpeciesId: 1,
    species: 'Bulbasaur',
    speciesId: 1,
    vanillaPokemon: {
      ...structuredClone(first.vanillaPokemon!),
      level: 55
    }
  };
  return {
    ...workflow,
    encounters: [first, second],
    stats: {
      ...workflow.stats,
      guaranteedPerfectIvEncounterCount: 2,
      singleCaptureCount: 2,
      totalEncounterCount: 2
    }
  };
}

function createSnapshot(encounter: DynamaxAdventuresWorkflow['encounters'][number]) {
  return {
    ability: encounter.ability,
    abilityLabel: encounter.abilityLabel,
    form: encounter.form,
    gigantamaxLabel: encounter.gigantamaxLabel,
    gigantamaxState: encounter.gigantamaxState,
    guaranteedPerfectIvs: encounter.guaranteedPerfectIvs,
    ivs: structuredClone(encounter.ivs),
    ivSummary: encounter.ivSummary,
    level: encounter.level,
    moves: structuredClone(encounter.moves),
    species: encounter.species,
    speciesId: encounter.speciesId
  };
}

function createStagedResponse(
  request: Parameters<ProjectBridge['updateDynamaxAdventureField']>[0],
  workflow: DynamaxAdventuresWorkflow
): UpdateDynamaxAdventureFieldResponse {
  const pendingEdit = {
    domain: 'workflow.dynamaxAdventures',
    field: request.field,
    newValue: request.value,
    recordId: `dynamaxAdventure:${request.entryIndex}`,
    sources: [
      {
        layer: 'base' as const,
        relativePath:
          'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin'
      }
    ],
    summary: `Stage ${request.field}.`
  };
  return {
    diagnostics: [],
    session: {
      hasPendingChanges: true,
      pendingEdits: [...(request.session?.pendingEdits ?? []), pendingEdit],
      sessionId: request.session?.sessionId ?? 'dynamax-session'
    },
    workflow
  };
}

function createPendingSession(
  sessionId: string,
  entryIndex: number,
  field: 'level',
  value: string
): EditSession {
  return {
    hasPendingChanges: true,
    pendingEdits: [
      {
        domain: 'workflow.dynamaxAdventures',
        field,
        newValue: value,
        recordId: `dynamaxAdventure:${entryIndex}`,
        sources: [
          {
            layer: 'base',
            relativePath:
              'romfs/bin/appli/chika/data_table/underground_exploration_poke.bin'
          }
        ],
        summary: `Set level to ${value}.`
      }
    ],
    sessionId
  };
}
