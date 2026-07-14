/* SPDX-License-Identifier: GPL-3.0-only */

import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { App } from './App';
import {
  createHealthForValidatedPaths,
  createMockProjectBridge
} from './testSupport/appTestFixtures';
import { useWorkbenchStore } from './workbenchStore';

vi.mock('@tauri-apps/api/event', () => ({
  listen: vi.fn(() => Promise.resolve(() => undefined))
}));

it('keeps a move-session baseline available for a selective staged revert', async () => {
  const user = userEvent.setup();
  const bridge = createMockProjectBridge({}, true);
  const loadedMoves = await bridge.loadMovesWorkflow({
    paths: {
      baseExeFsPath: 'base-exefs',
      baseRomFsPath: 'base-romfs',
      outputRootPath: 'output',
      saveFilePath: null,
      selectedGame: 'sword'
    }
  });
  const movesWorkflow = {
    ...loadedMoves.workflow,
    editableFields: [
      ...loadedMoves.workflow.editableFields,
      {
        field: 'quality',
        label: 'Quality',
        maximumValue: 13,
        minimumValue: 0,
        options: [
          { label: '000 Damage Only', value: 0 },
          { label: '013 Unique Effect', value: 13 }
        ],
        valueKind: 'integer'
      }
    ],
    moves: loadedMoves.workflow.moves.map((move) =>
      move.moveId === 33 ? { ...move, quality: 14 } : move
    )
  };
  let currentWorkflow = movesWorkflow;
  const baselineMove = movesWorkflow.moves.find((move) => move.moveId === 33)!;
  bridge.updateMoveField = vi.fn(bridge.updateMoveField);
  bridge.updateMoveFields = vi.fn(async (request) => {
    let pendingEdits = [...(request.session?.pendingEdits ?? [])];

    for (const update of request.updates) {
      pendingEdits = pendingEdits.filter(
        (edit) =>
          edit.domain !== 'workflow.moves' ||
          edit.recordId !== update.moveId.toString() ||
          edit.field !== update.field
      );

      const baselineValue =
        update.field === 'quality'
          ? baselineMove.quality.toString()
          : baselineMove.power.toString();
      if (update.value !== baselineValue) {
        pendingEdits.push({
          domain: 'workflow.moves',
          field: update.field,
          newValue: update.value,
          recordId: update.moveId.toString(),
          sources: [
            {
              layer: 'base' as const,
              relativePath: 'romfs/bin/pml/waza/waza_033.bin'
            }
          ],
          summary: `Set Tackle ${update.field} to ${update.value}.`
        });
      }

      const value = Number.parseInt(update.value, 10);
      currentWorkflow = {
        ...currentWorkflow,
        moves: currentWorkflow.moves.map((move) =>
          move.moveId !== update.moveId
            ? move
            : update.field === 'quality'
              ? { ...move, quality: value }
              : { ...move, power: value }
        )
      };
    }

    return {
      diagnostics: [],
      session: {
        hasPendingChanges: pendingEdits.length > 0,
        pendingEdits,
        sessionId: request.session?.sessionId ?? 'move-session'
      },
      workflow: currentWorkflow
    };
  });
  const health = createHealthForValidatedPaths(
    'base-romfs',
    'base-exefs',
    'output',
    null
  );
  useWorkbenchStore.setState({
    activeSection: 'moves',
    draftPaths: {
      baseExeFsPath: 'base-exefs',
      baseRomFsPath: 'base-romfs',
      outputRootPath: 'output',
      saveFilePath: '',
      pokemonLegendsZASupportFolderPath: '',
      scarletVioletSupportFolderPath: '',
      selectedGame: 'sword'
    },
    editSession: {
      hasPendingChanges: false,
      pendingEdits: [],
      sessionId: 'move-session'
    },
    editValidationDiagnostics: [],
    movesWorkflow,
    openProject: {
      fileGraph: { entries: [], summary: health.fileGraph },
      health,
      projectId: 'moves-project'
    },
    selectedMoveId: 33
  });

  render(<App bridge={bridge} />);

  const moveInspector = await screen.findByRole('complementary', {
    name: 'Selected move details'
  });
  const powerInput = within(moveInspector).getByLabelText('Power');
  const qualityInput = within(moveInspector).getByLabelText('Effect quality');
  const stageButton = within(moveInspector).getByRole('button', { name: 'Stage' });
  expect(qualityInput).toHaveValue('14 Effect quality');

  await user.clear(powerInput);
  await user.type(powerInput, '80');
  expect(stageButton).toBeEnabled();
  await user.click(stageButton);

  await waitFor(() => expect(bridge.updateMoveFields).toHaveBeenCalledTimes(1));
  expect(bridge.updateMoveField).not.toHaveBeenCalled();
  await waitFor(() => expect(stageButton).toBeDisabled());
  expect(useWorkbenchStore.getState().movesWorkflow?.moves.find((move) => move.moveId === 33))
    .toMatchObject({ power: 80, quality: 14 });

  await user.clear(qualityInput);
  await user.type(qualityInput, '13');
  expect(stageButton).toBeEnabled();
  await user.click(stageButton);

  await waitFor(() => expect(bridge.updateMoveFields).toHaveBeenCalledTimes(2));
  await waitFor(() => expect(stageButton).toBeDisabled());
  expect(useWorkbenchStore.getState().movesWorkflow?.moves.find((move) => move.moveId === 33))
    .toMatchObject({ power: 80, quality: 13 });

  await user.clear(qualityInput);
  await user.type(qualityInput, '14');
  expect(stageButton).toBeEnabled();
  await user.click(stageButton);

  await waitFor(() => expect(bridge.updateMoveFields).toHaveBeenCalledTimes(3));
  await waitFor(() => expect(stageButton).toBeDisabled());
  expect(vi.mocked(bridge.updateMoveFields).mock.calls.map(([request]) => request.updates)).toEqual([
    [{ field: 'power', moveId: 33, value: '80' }],
    [{ field: 'quality', moveId: 33, value: '13' }],
    [{ field: 'quality', moveId: 33, value: '14' }]
  ]);
  expect(within(moveInspector).getByText('0 changed')).toBeInTheDocument();
  expect(useWorkbenchStore.getState().movesWorkflow?.moves.find((move) => move.moveId === 33))
    .toMatchObject({ power: 80, quality: 14 });
  expect(useWorkbenchStore.getState().editSession).toMatchObject({
    hasPendingChanges: true,
    pendingEdits: [expect.objectContaining({ field: 'power', newValue: '80' })]
  });
});
