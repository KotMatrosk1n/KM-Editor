/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import type { EditSession } from './bridge/contracts';
import { parseEditableIntegerDraft } from './editableFieldHelpers';
import {
  evaluateMoveFieldsUpdate,
  formatMoveAccuracy,
  formatMoveEffectChance,
  formatMoveHealingValue,
  formatMoveHitRange,
  formatMoveInflictedEffectTurns,
  formatMoveRecoilValue,
  getEditableMoveFieldValue,
  getMoveEditableFieldGroup,
  getMoveEditableFieldLabel,
  moveFieldsNotStagedAtomicallyMessage
} from './movesEditor';
import { createMockProjectBridge } from './testSupport/appTestFixtures';

const swordPaths = {
  baseExeFsPath: 'base-exefs',
  baseRomFsPath: 'base-romfs',
  outputRootPath: 'output',
  saveFilePath: null,
  selectedGame: 'sword' as const
};

const currentSession: EditSession = {
  hasPendingChanges: false,
  pendingEdits: [],
  sessionId: 'move-session'
};

const moveChanges = [
  { field: 'power', value: '80' },
  { field: 'makesContact', value: '0' }
];

async function createMoveUpdateResponse() {
  const bridge = createMockProjectBridge({}, true);
  const currentWorkflow = (await bridge.loadMovesWorkflow({ paths: swordPaths })).workflow;
  const baselineMove = currentWorkflow.moves.find((move) => move.moveId === 33)!;
  const baselineValues = Object.fromEntries(
    moveChanges.map((change) => [
      change.field,
      getEditableMoveFieldValue(baselineMove, change.field)
    ])
  );
  const response = await bridge.updateMoveFields({
    paths: swordPaths,
    session: currentSession,
    updates: moveChanges.map((change) => ({ ...change, moveId: 33 }))
  });

  return { baselineValues, currentWorkflow, response };
}

describe('move fields atomic response evaluation', () => {
  it('accepts a complete successful batch and allows the caller to clear its drafts', async () => {
    const { baselineValues, currentWorkflow, response } = await createMoveUpdateResponse();

    const result = evaluateMoveFieldsUpdate({
      baselineValues,
      changes: moveChanges,
      currentSession,
      currentWorkflow,
      moveId: 33,
      response
    });

    expect(result.hasErrors).toBe(false);
    expect(result.shouldClearDrafts).toBe(true);
    expect(result.session).toBe(response.session);
    expect(result.workflow).toBe(response.workflow);
    expect(result.workflow.moves.find((move) => move.moveId === 33)).toMatchObject({
      power: 80
    });
  });

  it('rejects a partial response and retains the current session, workflow, and drafts', async () => {
    const { baselineValues, currentWorkflow, response } = await createMoveUpdateResponse();
    const partialResponse = {
      ...response,
      workflow: {
        ...response.workflow,
        moves: response.workflow.moves.map((move) =>
          move.moveId === 33
            ? {
                ...move,
                flags: move.flags.map((flag) =>
                  flag.field === 'makesContact' ? { ...flag, enabled: true } : flag
                )
              }
            : move
        )
      }
    };

    const result = evaluateMoveFieldsUpdate({
      baselineValues,
      changes: moveChanges,
      currentSession,
      currentWorkflow,
      moveId: 33,
      response: partialResponse
    });

    expect(result.hasErrors).toBe(true);
    expect(result.shouldClearDrafts).toBe(false);
    expect(result.session).toBe(currentSession);
    expect(result.workflow).toBe(currentWorkflow);
    expect(result.diagnostics).toContainEqual(
      expect.objectContaining({
        message: moveFieldsNotStagedAtomicallyMessage,
        severity: 'error'
      })
    );
  });

  it('rejects workflow-only staging when the session omits a requested pending edit', async () => {
    const { baselineValues, currentWorkflow, response } = await createMoveUpdateResponse();
    const incompleteSessionResponse = {
      ...response,
      session: {
        ...response.session,
        pendingEdits: response.session.pendingEdits.filter(
          (edit) => edit.field !== 'makesContact'
        )
      }
    };

    const result = evaluateMoveFieldsUpdate({
      baselineValues,
      changes: moveChanges,
      currentSession,
      currentWorkflow,
      moveId: 33,
      response: incompleteSessionResponse
    });

    expect(result.hasErrors).toBe(true);
    expect(result.shouldClearDrafts).toBe(false);
    expect(result.session).toBe(currentSession);
    expect(result.workflow).toBe(currentWorkflow);
    expect(result.diagnostics).toContainEqual(
      expect.objectContaining({
        message: moveFieldsNotStagedAtomicallyMessage,
        severity: 'error'
      })
    );
  });

  it('accepts a Sword and Shield baseline restore only when its pending edit is absent', async () => {
    const { baselineValues, currentWorkflow, response } = await createMoveUpdateResponse();
    const restoredPower = baselineValues.power!;
    const revertResponse = {
      ...response,
      session: {
        ...response.session,
        pendingEdits: response.session.pendingEdits.filter((edit) => edit.field !== 'power')
      },
      workflow: currentWorkflow
    };

    const result = evaluateMoveFieldsUpdate({
      baselineValues,
      changes: [{ field: 'power', value: restoredPower.toString() }],
      currentSession,
      currentWorkflow,
      moveId: 33,
      response: revertResponse
    });

    expect(result.hasErrors).toBe(false);
    expect(result.shouldClearDrafts).toBe(true);
    expect(result.session).toBe(revertResponse.session);
    expect(result.workflow).toBe(currentWorkflow);
  });

  it('accepts a Trinity baseline restore when its backend retains the pending edit', async () => {
    const { baselineValues, currentWorkflow, response } = await createMoveUpdateResponse();
    const restoredPower = baselineValues.power!;
    const revertResponse = {
      ...response,
      session: {
        ...response.session,
        pendingEdits: response.session.pendingEdits.map((edit) =>
          edit.field === 'power'
            ? { ...edit, newValue: restoredPower.toString() }
            : edit
        )
      },
      workflow: currentWorkflow
    };

    const result = evaluateMoveFieldsUpdate({
      baselineRestoresRemovePendingEdit: false,
      baselineValues,
      changes: [{ field: 'power', value: restoredPower.toString() }],
      currentSession,
      currentWorkflow,
      moveId: 33,
      response: revertResponse
    });

    expect(result.hasErrors).toBe(false);
    expect(result.shouldClearDrafts).toBe(true);
    expect(result.session).toBe(revertResponse.session);
    expect(result.workflow).toBe(currentWorkflow);
  });

  it('preserves an explicit backend error and retains the current session, workflow, and drafts', async () => {
    const { baselineValues, currentWorkflow, response } = await createMoveUpdateResponse();
    const errorResponse = {
      ...response,
      diagnostics: [
        {
          domain: 'workflow.moves',
          message: 'The move batch was rejected.',
          severity: 'error' as const
        }
      ]
    };

    const result = evaluateMoveFieldsUpdate({
      baselineValues,
      changes: moveChanges,
      currentSession,
      currentWorkflow,
      moveId: 33,
      response: errorResponse
    });

    expect(result.hasErrors).toBe(true);
    expect(result.shouldClearDrafts).toBe(false);
    expect(result.session).toBe(currentSession);
    expect(result.workflow).toBe(currentWorkflow);
    expect(result.diagnostics).toEqual(errorResponse.diagnostics);
  });
});

describe('move editor field and display helpers', () => {
  it('uses strict integer drafts', () => {
    expect(parseEditableIntegerDraft('42')).toBe(42);
    expect(parseEditableIntegerDraft('-7')).toBe(-7);
    expect(parseEditableIntegerDraft('1.5')).toBeNull();
    expect(parseEditableIntegerDraft('1e2')).toBeNull();
    expect(parseEditableIntegerDraft('12 turns')).toBeNull();
    expect(parseEditableIntegerDraft('9007199254740992')).toBeNull();
  });

  it('presents special Sword and Shield values semantically', () => {
    expect(formatMoveAccuracy(101)).toBe('Always hits');
    expect(formatMoveHitRange(0, 0)).toBe('Single hit');
    expect(formatMoveInflictedEffectTurns(0, 0)).toBe('Effect-defined');
    expect(formatMoveInflictedEffectTurns(2, 5)).toBe('2-5 turns');
    expect(formatMoveEffectChance(0, true)).toBe(
      'Primary effect (no separate chance roll)'
    );
    expect(formatMoveEffectChance(0, false)).toBe('None');
    expect(formatMoveRecoilValue(50)).toBe('Drain 50%');
    expect(formatMoveRecoilValue(-25)).toBe('Recoil 25%');
    expect(formatMoveHealingValue(50)).toBe('Restore 50% HP');
    expect(formatMoveHealingValue(-33)).toBe('Cost 33% HP');
  });

  it('groups mapped and raw fields without reclassifying flags or unknown move data', () => {
    const createField = (
      field: string,
      options: Array<{ label: string; value: number }> = [],
      valueKind = 'integer'
    ) => ({
      field,
      label: field,
      maximumValue: 255,
      minimumValue: 0,
      options,
      valueKind
    });

    expect(getMoveEditableFieldGroup(createField('power'))).toBe('Core Stats');
    expect(getMoveEditableFieldGroup(createField('turnMin'))).toBe('Secondary Effects');
    expect(getMoveEditableFieldGroup(createField('turnMax'))).toBe('Secondary Effects');
    expect(getMoveEditableFieldGroup(createField('quality'))).toBe('Advanced / Raw');
    expect(
      getMoveEditableFieldGroup(createField('quality', [{ label: 'Damage', value: 0 }]))
    ).toBe('Secondary Effects');
    expect(getMoveEditableFieldGroup(createField('effectSequence'))).toBe('Advanced / Raw');
    expect(getMoveEditableFieldGroup(createField('rawHealing'))).toBe('Advanced / Raw');
    expect(getMoveEditableFieldGroup(createField('canUseMove', [], 'boolean'))).toBe('Flags');
    expect(getMoveEditableFieldGroup(createField('futureRawField'))).toBe('Move Data');
    expect(getMoveEditableFieldLabel(createField('quality'))).toBe('Quality (raw)');
    expect(getMoveEditableFieldLabel(createField('turnMin'))).toBe(
      'Minimum inflicted-effect turns'
    );
  });
});
