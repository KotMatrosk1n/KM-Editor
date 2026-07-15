/* SPDX-License-Identifier: GPL-3.0-only */

import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { type TrainerRecord, type TrainersWorkflow } from './bridge/contracts';
import { createMockProjectBridge } from './testSupport/appTestFixtures';
import { useWorkbenchStore } from './workbenchStore';

const tauriEventMock = vi.hoisted(() => ({
  listen: vi.fn(() => Promise.resolve(() => undefined))
}));

vi.mock('@tauri-apps/api/event', () => ({
  listen: tauriEventMock.listen
}));

describe('Trainers UI', () => {
  it('keeps SwSh Trainer staging atomic, error-aware, and correctly identified', async () => {
    window.localStorage.clear();
    useWorkbenchStore.setState({
      activeSection: 'health',
      applyResult: null,
      changePlan: null,
      draftPaths: {
        baseExeFsPath: '',
        baseRomFsPath: '',
        outputRootPath: '',
        saveFilePath: '',
        pokemonLegendsZASupportFolderPath: '',
        scarletVioletSupportFolderPath: '',
        selectedGame: 'sword'
      },
      editSession: null,
      editValidationDiagnostics: [],
      openProject: null,
      projectStatus: 'idle',
      selectedTrainerId: null,
      trainerSearchText: '',
      trainersWorkflow: null,
      workflows: []
    });

    const user = userEvent.setup();
    const bridge = createMockProjectBridge({}, true);
    const originalLoadTrainersWorkflow = bridge.loadTrainersWorkflow;
    let loadedWorkflow: TrainersWorkflow | null = null;

    bridge.loadTrainersWorkflow = vi.fn(async (request) => {
      const response = await originalLoadTrainersWorkflow(request);
      loadedWorkflow = {
        ...response.workflow,
        editableFields: [
          ...response.workflow.editableFields,
          {
            field: 'ivHp',
            label: 'HP IV',
            maximumValue: 255,
            minimumValue: 0,
            options: [],
            valueKind: 'integer'
          },
          {
            field: 'evHp',
            label: 'HP EV',
            maximumValue: 255,
            minimumValue: 0,
            options: [],
            valueKind: 'integer'
          },
          {
            field: 'evAttack',
            label: 'Attack EV',
            maximumValue: 255,
            minimumValue: 0,
            options: [],
            valueKind: 'integer'
          }
        ]
      };
      return { workflow: loadedWorkflow };
    });

    type UpdateTrainerResponse = Awaited<ReturnType<typeof bridge.updateTrainerField>>;
    type UpdateTrainerRequest = Parameters<typeof bridge.updateTrainerField>[0];
    type UpdateMode = 'ai-error' | 'ev-batch' | 'iv-error' | 'trainer-batch';
    let updateMode: UpdateMode = 'ai-error';

    const getLoadedWorkflow = () => {
      if (!loadedWorkflow) {
        throw new Error('Trainers workflow was not loaded.');
      }

      return loadedWorkflow;
    };
    const withTrainerUpdate = (update: Partial<TrainerRecord>) => {
      const workflow = getLoadedWorkflow();
      return {
        ...workflow,
        trainers: workflow.trainers.map((trainer) =>
          trainer.trainerId === 10 ? { ...trainer, ...update } : trainer
        )
      };
    };
    const withPokemonUpdate = (
      update: (pokemon: TrainerRecord['team'][number]) => TrainerRecord['team'][number]
    ) => {
      const workflow = getLoadedWorkflow();
      return {
        ...workflow,
        trainers: workflow.trainers.map((trainer) =>
          trainer.trainerId === 10
            ? { ...trainer, team: trainer.team.map((pokemon) => update(pokemon)) }
            : trainer
        )
      };
    };
    const createPendingEdit = (request: UpdateTrainerRequest) => ({
      domain: 'workflow.trainers' as const,
      field: request.field,
      newValue: request.value,
      recordId:
        request.slot === null
          ? request.trainerId.toString()
          : `${request.trainerId}:${request.slot}`,
      sources: [],
      summary: `Set Trainer ${request.trainerId} ${request.field} to ${request.value}.`
    });
    const createSuccessResponse = (
      request: UpdateTrainerRequest,
      workflow: TrainersWorkflow
    ): UpdateTrainerResponse => ({
      diagnostics: [],
      session: {
        hasPendingChanges: true,
        pendingEdits: [...(request.session?.pendingEdits ?? []), createPendingEdit(request)],
        sessionId: request.session?.sessionId ?? 'session-1'
      },
      workflow
    });
    const createErrorResponse = (
      request: UpdateTrainerRequest,
      message: string,
      workflow: TrainersWorkflow
    ): UpdateTrainerResponse => ({
      diagnostics: [{ message, severity: 'error' }],
      session: {
        hasPendingChanges: true,
        pendingEdits: [createPendingEdit(request)],
        sessionId: 'rejected-session'
      },
      workflow
    });

    const updateTrainerField = vi.fn(
      async (request: UpdateTrainerRequest): Promise<UpdateTrainerResponse> => {
        if (updateMode === 'ai-error') {
          return createErrorResponse(
            request,
            'The AI flag edit was rejected.',
            withTrainerUpdate({
              aiFlags: Number.parseInt(request.value, 10),
              aiFlagStates: getLoadedWorkflow().trainers[0]!.aiFlagStates.map((flag) =>
                flag.bit === 0 ? { ...flag, enabled: false } : flag
              )
            })
          );
        }

        if (updateMode === 'trainer-batch') {
          if (request.field === 'battleType') {
            return createSuccessResponse(
              request,
              withTrainerUpdate({ battleType: 'Singles', battleTypeValue: 0 })
            );
          }

          if (request.field === 'money') {
            return createErrorResponse(
              request,
              'The Trainer batch was rejected.',
              withTrainerUpdate({ battleType: 'Singles', battleTypeValue: 0, money: 99 })
            );
          }
        }

        if (updateMode === 'iv-error' && request.field === 'ivHp') {
          return createErrorResponse(
            request,
            'HP IV must be between 0 and 31.',
            withPokemonUpdate((pokemon) => ({
              ...pokemon,
              ivs: { ...pokemon.ivs, hp: Number.parseInt(request.value, 10) }
            }))
          );
        }

        if (updateMode === 'ev-batch') {
          if (request.field === 'evHp') {
            return createSuccessResponse(
              request,
              withPokemonUpdate((pokemon) => ({
                ...pokemon,
                evs: { ...pokemon.evs, hp: Number.parseInt(request.value, 10) }
              }))
            );
          }

          if (request.field === 'evAttack') {
            return createErrorResponse(
              request,
              'Pokemon EV total must not exceed 510.',
              withPokemonUpdate((pokemon) => ({
                ...pokemon,
                evs: {
                  ...pokemon.evs,
                  attack: Number.parseInt(request.value, 10),
                  hp: 252
                }
              }))
            );
          }
        }

        throw new Error(`Unexpected ${updateMode} update for ${request.field}.`);
      }
    );
    bridge.updateTrainerField = updateTrainerField;

    render(<App bridge={bridge} />);

    await user.type(screen.getByLabelText('Base RomFS'), 'base-romfs');
    await user.type(screen.getByLabelText('Base ExeFS'), 'base-exefs');
    await user.type(screen.getByLabelText('Output Root'), 'output');
    await user.click(screen.getByRole('button', { name: 'Validate Paths' }));

    const navigation = await screen.findByRole('navigation', { name: 'Workspace' });
    await user.click(within(navigation).getByRole('button', { name: 'Editors' }));
    await user.click(within(navigation).getByRole('button', { name: 'Trainers' }));

    const inspector = await screen.findByRole('complementary', {
      name: 'Selected trainer provenance'
    });
    const searchInput = screen.getByLabelText('Search trainers');
    await user.type(searchInput, 'Avery');
    await user.click(within(inspector).getByRole('button', { name: 'Edit' }));
    await waitFor(() => expect(useWorkbenchStore.getState().editSession?.sessionId).toBe('session-1'));
    expect(within(inspector).getByLabelText('Unknown header flag')).toBeDisabled();
    expect(within(inspector).getByLabelText('Unknown header value')).toBeDisabled();

    const basicAiFlag = within(inspector).getByRole('checkbox', { name: /Basic/ });
    expect(basicAiFlag).toBeChecked();
    await user.click(basicAiFlag);
    await waitFor(() => expect(updateTrainerField).toHaveBeenCalledTimes(1));
    await waitFor(() =>
      expect(useWorkbenchStore.getState().editValidationDiagnostics).toEqual([
        { message: 'The AI flag edit was rejected.', severity: 'error' }
      ])
    );
    expect(basicAiFlag).toBeChecked();
    expect(useWorkbenchStore.getState().editSession?.pendingEdits).toEqual([]);
    expect(useWorkbenchStore.getState().trainersWorkflow?.trainers[0]?.aiFlags).toBe(77);

    updateMode = 'trainer-batch';
    const battleTypeInput = within(inspector).getByLabelText('Battle type');
    const moneyInput = within(inspector).getByLabelText('Prize money rate');
    await user.clear(battleTypeInput);
    await user.type(battleTypeInput, '0{Enter}');
    await user.clear(moneyInput);
    await user.type(moneyInput, '99');

    const getStageButtons = () =>
      within(inspector).getAllByRole('button', { name: 'Stage' });
    await user.click(getStageButtons()[0]!);
    await waitFor(() => expect(updateTrainerField).toHaveBeenCalledTimes(3));
    expect(updateTrainerField.mock.calls.slice(1, 3).map(([request]) => request.field)).toEqual([
      'battleType',
      'money'
    ]);
    expect(updateTrainerField.mock.calls[2]?.[0].session?.pendingEdits).toHaveLength(1);
    expect(useWorkbenchStore.getState().editSession?.pendingEdits).toEqual([]);
    expect(useWorkbenchStore.getState().trainersWorkflow?.trainers[0]).toMatchObject({
      battleTypeValue: 1,
      money: 24
    });
    expect(battleTypeInput).toHaveValue('0 Singles');
    expect(moneyInput).toHaveValue(99);
    expect(getStageButtons()[0]).toBeEnabled();
    expect(searchInput).toHaveValue('Avery');

    updateMode = 'iv-error';
    const hpIvInput = within(inspector).getByLabelText('HP IV');
    await user.clear(hpIvInput);
    await user.type(hpIvInput, '99');
    await user.click(getStageButtons()[1]!);
    await waitFor(() => expect(updateTrainerField).toHaveBeenCalledTimes(4));
    expect(updateTrainerField.mock.calls[3]?.[0]).toMatchObject({ field: 'ivHp', value: '99' });
    expect(hpIvInput).toHaveValue(99);
    expect(useWorkbenchStore.getState().editSession?.pendingEdits).toEqual([]);

    await user.clear(hpIvInput);
    await user.type(hpIvInput, '1');
    updateMode = 'ev-batch';
    const hpEvInput = within(inspector).getByLabelText('HP EV');
    const attackEvInput = within(inspector).getByLabelText('Attack EV');
    await user.clear(hpEvInput);
    await user.type(hpEvInput, '252');
    await user.clear(attackEvInput);
    await user.type(attackEvInput, '252');
    await user.click(getStageButtons()[1]!);
    await waitFor(() => expect(updateTrainerField).toHaveBeenCalledTimes(6));
    expect(
      updateTrainerField.mock.calls.slice(4, 6).map(([request]) => ({
        field: request.field,
        value: request.value
      }))
    ).toEqual([
      { field: 'evHp', value: '252' },
      { field: 'evAttack', value: '252' }
    ]);
    expect(useWorkbenchStore.getState().editSession?.pendingEdits).toEqual([]);
    expect(useWorkbenchStore.getState().trainersWorkflow?.trainers[0]?.team[0]?.evs).toMatchObject(
      {
        attack: 20,
        hp: 10
      }
    );
    expect(hpEvInput).toHaveValue(252);
    expect(attackEvInput).toHaveValue(252);

    act(() => {
      useWorkbenchStore.setState({
        activeSection: 'changes',
        editSession: {
          hasPendingChanges: true,
          pendingEdits: [
            {
              domain: 'workflow.trainers',
              field: 'classBallId',
              newValue: '3',
              recordId: '5',
              sources: [],
              summary: 'Set the Pokemon Trainer class ball to Great Ball.'
            },
            {
              domain: 'workflow.trainers',
              field: 'level',
              newValue: '25',
              recordId: '10:1',
              sources: [],
              summary: 'Set Avery party slot 1 level to 25.'
            }
          ],
          sessionId: 'session-1'
        }
      });
    });

    expect(
      await screen.findByText('Avery (#10) party slot #1: Grookey')
    ).toBeInTheDocument();
    act(() => useWorkbenchStore.getState().setActiveSection('trainers'));
    const trainerTable = await screen.findByRole('table', { name: 'Trainers' });
    const trainerRow = within(trainerTable).getByRole('row', { name: /Avery/ });
    await waitFor(() => expect(trainerRow).toHaveClass('trainers-row-pending'));
    expect(screen.getByLabelText('Search trainers')).toHaveValue('Avery');
  });
});
