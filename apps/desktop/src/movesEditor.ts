/* SPDX-License-Identifier: GPL-3.0-only */

import type {
  ApiDiagnostic,
  EditSession,
  MoveEditableField,
  MoveRecord,
  MovesWorkflow
} from './bridge/contracts';
import type { UpdateMoveFieldsResponse } from './bridge/svBatchFieldContracts';

export type MoveFieldChange = { field: string; value: string };

export type NumericMoveEditableField = {
  field: string;
  label: string;
  maximumValue: number | null;
  minimumValue: number | null;
  options: Array<{ label: string; value: number }>;
  valueKind: string;
};

export const moveFieldsNotStagedAtomicallyMessage =
  'Move changes were not staged atomically. No move drafts were cleared.';

export function evaluateMoveFieldsUpdate({
  baselineValues,
  baselineRestoresRemovePendingEdit = true,
  changes,
  currentSession,
  currentWorkflow,
  moveId,
  response
}: {
  baselineValues: Readonly<Record<string, number | null>>;
  baselineRestoresRemovePendingEdit?: boolean;
  changes: MoveFieldChange[];
  currentSession: EditSession;
  currentWorkflow: MovesWorkflow;
  moveId: number;
  response: UpdateMoveFieldsResponse;
}) {
  const stagedMove = response.workflow.moves.find((candidate) => candidate.moveId === moveId);
  const workflowStagedAllChanges =
    stagedMove !== undefined &&
    changes.every(
      (change) =>
        getEditableMoveFieldValue(stagedMove, change.field)?.toString() === change.value
    );
  const sessionStagedAllChanges = changes.every((change) => {
    const matchingPendingEdits = response.session.pendingEdits.filter(
      (edit) =>
        edit.domain === 'workflow.moves' &&
        edit.recordId === moveId.toString() &&
        edit.field === change.field
    );
    const baselineValue = baselineValues[change.field];
    const restoresBaseline =
      baselineValue !== null &&
      baselineValue !== undefined &&
      baselineValue.toString() === change.value;

    return restoresBaseline && baselineRestoresRemovePendingEdit
      ? matchingPendingEdits.length === 0
      : matchingPendingEdits.length === 1 &&
          matchingPendingEdits[0]?.newValue === change.value;
  });
  const stagedAllChanges = workflowStagedAllChanges && sessionStagedAllChanges;
  const responseHasErrors = response.diagnostics.some(
    (diagnostic) => diagnostic.severity === 'error'
  );
  const diagnostics: ApiDiagnostic[] =
    responseHasErrors || stagedAllChanges
      ? response.diagnostics
      : [
          ...response.diagnostics,
          {
            domain: 'workflow.moves',
            message: moveFieldsNotStagedAtomicallyMessage,
            severity: 'error'
          }
        ];
  const hasErrors = diagnostics.some((diagnostic) => diagnostic.severity === 'error');

  return {
    diagnostics,
    hasErrors,
    session: hasErrors ? currentSession : response.session,
    shouldClearDrafts: !hasErrors,
    workflow: hasErrors ? currentWorkflow : response.workflow
  };
}

export function getEditableMoveFieldValue(move: MoveRecord, field: string) {
  switch (field) {
    case 'canUseMove':
      return move.canUseMove ? 1 : 0;
    case 'type':
      return move.type;
    case 'quality':
      return move.quality;
    case 'category':
      return move.category;
    case 'power':
      return move.power;
    case 'accuracy':
      return move.accuracy;
    case 'pp':
      return move.pp;
    case 'priority':
      return move.priority;
    case 'critStage':
      return move.critStage;
    case 'maxMovePower':
      return move.maxMovePower;
    case 'target':
      return move.target;
    case 'hitMin':
      return move.hitMin;
    case 'hitMax':
      return move.hitMax;
    case 'turnMin':
      return move.turnMin;
    case 'turnMax':
      return move.turnMax;
    case 'inflict':
      return move.inflict;
    case 'inflictPercent':
      return move.inflictPercent;
    case 'rawInflictCount':
      return move.rawInflictCount;
    case 'flinch':
      return move.flinch;
    case 'effectSequence':
      return move.effectSequence;
    case 'recoil':
      return move.recoil;
    case 'rawHealing':
      return move.rawHealing;
    case 'stat1':
      return move.statChanges.find((stat) => stat.slot === 1)?.stat ?? null;
    case 'stat1Stage':
      return move.statChanges.find((stat) => stat.slot === 1)?.stage ?? null;
    case 'stat1Percent':
      return move.statChanges.find((stat) => stat.slot === 1)?.percent ?? null;
    case 'stat2':
      return move.statChanges.find((stat) => stat.slot === 2)?.stat ?? null;
    case 'stat2Stage':
      return move.statChanges.find((stat) => stat.slot === 2)?.stage ?? null;
    case 'stat2Percent':
      return move.statChanges.find((stat) => stat.slot === 2)?.percent ?? null;
    case 'stat3':
      return move.statChanges.find((stat) => stat.slot === 3)?.stat ?? null;
    case 'stat3Stage':
      return move.statChanges.find((stat) => stat.slot === 3)?.stage ?? null;
    case 'stat3Percent':
      return move.statChanges.find((stat) => stat.slot === 3)?.percent ?? null;
    default: {
      const flag = move.flags.find((candidate) => candidate.field === field);
      return flag ? (flag.enabled ? 1 : 0) : null;
    }
  }
}

export function getMoveEditableFieldLabel(field: MoveEditableField) {
  switch (field.field) {
    case 'turnMin':
      return 'Minimum inflicted-effect turns';
    case 'turnMax':
      return 'Maximum inflicted-effect turns';
    case 'quality':
      return field.options.length > 0 ? 'Effect quality' : 'Quality (raw)';
    case 'rawHealing':
      return 'HP recovery / cost (raw)';
    default:
      return field.label;
  }
}

export function getMoveEditableFieldGroup(field: NumericMoveEditableField) {
  if (
    field.field === 'type' ||
    field.field === 'category' ||
    field.field === 'power' ||
    field.field === 'accuracy' ||
    field.field === 'pp' ||
    field.field === 'priority' ||
    field.field === 'critStage' ||
    field.field === 'maxMovePower'
  ) {
    return 'Core Stats';
  }

  if (field.field === 'target' || field.field === 'hitMin' || field.field === 'hitMax') {
    return 'Targeting';
  }

  if (
    field.field === 'inflict' ||
    field.field === 'inflictPercent' ||
    field.field === 'rawInflictCount' ||
    field.field === 'turnMin' ||
    field.field === 'turnMax' ||
    field.field === 'flinch' ||
    field.field === 'recoil' ||
    (field.field === 'quality' && field.options.length > 0)
  ) {
    return 'Secondary Effects';
  }

  if (
    field.field === 'effectSequence' ||
    field.field === 'rawHealing' ||
    field.field === 'quality'
  ) {
    return 'Advanced / Raw';
  }

  if (field.field.startsWith('stat')) {
    return 'Stat Changes';
  }

  if (field.valueKind === 'boolean' || field.field === 'canUseMove') {
    return 'Flags';
  }

  return 'Move Data';
}

export function formatMoveAccuracy(accuracy: number) {
  if (accuracy === 0) {
    return '-';
  }

  return accuracy === 101 ? 'Always hits' : accuracy.toString();
}

export function formatMoveHitRange(hitMin: number, hitMax: number) {
  if (hitMin === 0 && hitMax === 0) {
    return 'Single hit';
  }

  if (hitMin === hitMax) {
    return `${hitMin} ${hitMin === 1 ? 'hit' : 'hits'}`;
  }

  return `${hitMin}-${hitMax} hits`;
}

export function formatMoveInflictedEffectTurns(turnMin: number, turnMax: number) {
  if (turnMin === 0 && turnMax === 0) {
    return 'Effect-defined';
  }

  if (turnMin === turnMax) {
    return `${turnMin} ${turnMin === 1 ? 'turn' : 'turns'}`;
  }

  return `${turnMin}-${turnMax} turns`;
}

export function formatMoveEffectChance(percent: number, hasEffect: boolean) {
  if (percent === 0) {
    return hasEffect ? 'Primary effect (no separate chance roll)' : 'None';
  }

  return `${percent}%`;
}

export function formatMoveRecoilValue(recoil: number) {
  if (recoil === 0) {
    return 'None';
  }

  return recoil > 0 ? `Drain ${recoil}%` : `Recoil ${Math.abs(recoil)}%`;
}

export function formatMoveHealingValue(rawHealing: number) {
  if (rawHealing === 0) {
    return 'None';
  }

  return rawHealing > 0
    ? `Restore ${rawHealing}% HP`
    : `Cost ${Math.abs(rawHealing)}% HP`;
}
