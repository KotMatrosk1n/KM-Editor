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

export type MoveRelationalValidationIssue = {
  fields: string[];
  message: string;
};

export type MoveRuntimeTimingSelection = {
  occurrence?: number | null;
  timingMoveId?: number | null;
};

export type MoveRuntimeView = {
  battleRow: MoveRecord['runtimeVariants'][number] | null;
  playerDamageRows: MoveRecord['playerDamageRows'];
  timingRow: MoveRecord['timingRows'][number] | null;
  variant: number;
};

export type MoveRuntimeStatRecipient =
  | { kind: 'hit-targets' }
  | { kind: 'move-target'; targetName: string }
  | { kind: 'scripted' }
  | { kind: 'user' };

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
    changes.every((change) => {
      const actualValue = getEditableMoveFieldValue(stagedMove, change.field);
      const expectedValue = Number(change.value);
      return actualValue !== null &&
        Number.isFinite(expectedValue) &&
        Math.abs(actualValue - expectedValue) <= 0.00001;
    });
  const sessionStagedAllChanges = changes.every((change) => {
    const matchingPendingEdits = response.session.pendingEdits.filter(
      (edit) =>
        edit.domain === 'workflow.moves' &&
        edit.recordId === moveId.toString() &&
        edit.field === change.field
    );
    const baselineValue = baselineValues[change.field];
    const parsedChangeValue = Number(change.value);
    const restoresBaseline =
      baselineValue !== null &&
      baselineValue !== undefined &&
      Number.isFinite(parsedChangeValue) &&
      Math.abs(baselineValue - parsedChangeValue) <= 0.00001;

    return restoresBaseline && baselineRestoresRemovePendingEdit
      ? matchingPendingEdits.length === 0
      : matchingPendingEdits.length === 1 &&
          Number.isFinite(Number(matchingPendingEdits[0]?.newValue)) &&
          Number.isFinite(parsedChangeValue) &&
          Math.abs(Number(matchingPendingEdits[0]?.newValue) - parsedChangeValue) <= 0.00001;
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
  const playerDamageAttackId = parseMovePlayerDamageField(field);
  if (playerDamageAttackId !== null) {
    return move.playerDamageRows.find((row) => row.attackId === playerDamageAttackId)
      ?.playerDamage ?? null;
  }

  if (field.startsWith('battle.')) {
    const [, variantText, member] = field.split('.', 3);
    const variant = move.runtimeVariants.find(
      (candidate) => candidate.variant === Number(variantText)
    );
    if (!variant || !member) {
      return null;
    }

    const statMatch = /^stat([123])(Stage|Percent)?$/.exec(member);
    if (statMatch) {
      const stat = variant.statChanges.find((candidate) => candidate.slot === Number(statMatch[1]));
      if (!stat) {
        return null;
      }

      return statMatch[2] === 'Stage'
        ? stat.stage
        : statMatch[2] === 'Percent'
          ? stat.percent
          : stat.stat;
    }

    const runtimeValues: Record<string, number> = {
      allowedWhileHealBlocked: variant.allowedWhileHealBlocked ? 1 : 0,
      appliesCondition: variant.appliesCondition ? 1 : 0,
      blockedByProtect: variant.blockedByProtect ? 1 : 0,
      bypassesSubstitute: variant.bypassesSubstitute ? 1 : 0,
      callableByMetronome: variant.callableByMetronome ? 1 : 0,
      cannotKnockOut: variant.cannotKnockOut ? 1 : 0,
      conditionCount: variant.conditionCount,
      conditionId: variant.conditionId,
      conditionPercent: variant.conditionPercent,
      conditionTurnMax: variant.conditionTurnMax,
      conditionTurnMin: variant.conditionTurnMin,
      criticalRank: variant.criticalRank,
      damageDrainRatio: variant.damageDrainRatio,
      damageRecoverRatio: variant.damageRecoverRatio,
      damageType: variant.damageType,
      effectCategory: variant.effectCategory,
      hpRecoverRatio: variant.hpRecoverRatio,
      isAvoidedByFloating: variant.isAvoidedByFloating ? 1 : 0,
      isGuard: variant.isGuard ? 1 : 0,
      isSlicing: variant.isSlicing ? 1 : 0,
      isWind: variant.isWind ? 1 : 0,
      makesContact: variant.makesContact ? 1 : 0,
      power: variant.power,
      restoresHp: variant.restoresHp ? 1 : 0,
      shrinkPercent: variant.shrinkPercent,
      thawsUser: variant.thawsUser ? 1 : 0,
      type: variant.type,
      valueEffectRatio: variant.valueEffectRatio
    };
    return runtimeValues[member] ?? null;
  }

  const timingField = parseMoveTimingField(field);
  if (timingField) {
    const selectedTimingMoveId = timingField.timingMoveId ?? move.moveId;
    const timingRows = move.timingRows.filter(
      (candidate) => candidate.timingMoveId === selectedTimingMoveId
    );
    const timing = timingField.occurrence === null
      ? timingRows[0] ?? null
      : timingRows.find((candidate) => candidate.occurrence === timingField.occurrence) ?? null;
    if (!timing) {
      return null;
    }

    const timingValues: Record<string, number> = {
      attackLoopFrames: timing.attackLoopFrames,
      chargeFrames: timing.chargeFrames,
      cooldown: timing.cooldown,
      effectTime: timing.effectTime,
      effectValue: timing.effectValue,
      effectiveRange: timing.effectiveRange,
      heightTolerance: timing.heightTolerance,
      hitPercent: timing.hitPercent,
      impactMotionSpeed: timing.impactMotionSpeed,
      megaPowerBonus: timing.megaPowerBonus,
      movementType: timing.movementType,
      overwriteProjectile1: timing.overwriteProjectile1,
      overwriteProjectile2: timing.overwriteProjectile2,
      overwriteProjectile3: timing.overwriteProjectile3,
      overwriteProjectile4: timing.overwriteProjectile4,
      overwriteProjectile5: timing.overwriteProjectile5,
      playedMotionSpeed: timing.playedMotionSpeed,
      projectileCorrectionScale: timing.projectileCorrectionScale,
      projectileCountMax: timing.projectileCountMax,
      projectileCountMin: timing.projectileCountMin,
      rangeMax: timing.rangeMax,
      rangeMin: timing.rangeMin,
      replacementProjectile1: timing.replacementProjectile1,
      replacementProjectile2: timing.replacementProjectile2,
      replacementProjectile3: timing.replacementProjectile3,
      replacementProjectile4: timing.replacementProjectile4,
      replacementProjectile5: timing.replacementProjectile5,
      shotDirection: timing.shotDirection,
      spawnLocator: timing.spawnLocatorOption,
      spawnLocatorOption: timing.spawnLocatorOption,
      spawnOffsetX: timing.spawnOffsetX,
      spawnOffsetY: timing.spawnOffsetY,
      spawnOffsetZ: timing.spawnOffsetZ,
      spawnOrigin: timing.spawnOrigin,
      targetCorrectionType: timing.targetCorrectionType
    };
    return timingValues[timingField.member] ?? null;
  }

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
  const playerDamageAttackId = parseMovePlayerDamageField(field.field);
  if (playerDamageAttackId !== null) {
    return `Attack ${playerDamageAttackId} player damage`;
  }

  const fieldName =
    parseMoveTimingField(field.field)?.member ?? field.field.replace(/^battle\.\d+\./, '');

  switch (fieldName) {
    case 'turnMin':
      return 'Minimum inflicted-effect turns';
    case 'turnMax':
      return 'Maximum inflicted-effect turns';
    case 'quality':
      return field.options.length > 0 ? 'Effect quality' : 'Quality (raw)';
    case 'rawHealing':
      return 'HP recovery / cost (raw)';
    case 'damageRecoverRatio':
      return 'Damage recovery / recoil (%)';
    case 'damageDrainRatio':
      return 'Drained HP (%)';
    case 'cooldown':
      return 'Cooldown (seconds)';
    default:
      return field.label;
  }
}

export function getMoveEditableFieldGroup(field: NumericMoveEditableField) {
  if (parseMovePlayerDamageField(field.field) !== null) {
    return 'Boss Player Damage';
  }

  const timingField = parseMoveTimingField(field.field);
  if (timingField) {
    if (timingField.occurrence === null) {
      return 'Timing';
    }

    if (
      timingField.member === 'chargeFrames' ||
      timingField.member === 'attackLoopFrames' ||
      timingField.member === 'impactMotionSpeed' ||
      timingField.member === 'movementType' ||
      timingField.member === 'playedMotionSpeed'
    ) {
      return 'Advanced Animation and Motion';
    }

    if (
      timingField.member === 'spawnOrigin' ||
      timingField.member === 'spawnLocator' ||
      timingField.member === 'spawnLocatorOption' ||
      timingField.member.startsWith('spawnOffset') ||
      timingField.member === 'shotDirection' ||
      timingField.member === 'targetCorrectionType'
    ) {
      return 'Advanced Spawn and Direction';
    }

    if (
      timingField.member === 'rangeMin' ||
      timingField.member === 'rangeMax' ||
      timingField.member === 'heightTolerance' ||
      timingField.member === 'effectiveRange'
    ) {
      return 'Advanced Targeting and Range';
    }

    if (
      timingField.member === 'projectileCountMin' ||
      timingField.member === 'projectileCountMax' ||
      timingField.member === 'projectileCorrectionScale'
    ) {
      return 'Advanced Projectiles';
    }

    if (
      timingField.member.startsWith('overwriteProjectile') ||
      timingField.member.startsWith('replacementProjectile')
    ) {
      return 'Advanced Projectile Replacements';
    }

    if (
      timingField.member === 'effectTime' ||
      timingField.member === 'effectValue' ||
      timingField.member === 'megaPowerBonus'
    ) {
      return 'Advanced Effect Timing';
    }

    return 'Advanced Timing';
  }

  if (field.field.startsWith('battle.')) {
    const [, , member = ''] = field.field.split('.', 3);
    if (member === 'effectCategory') {
      return 'Runtime Behavior';
    }

    if (member.startsWith('stat')) {
      return 'Runtime Stat Changes';
    }

    if (member === 'appliesCondition' || member === 'valueEffectRatio') {
      return 'Advanced Battle Behavior';
    }

    if (field.valueKind === 'boolean') {
      return 'Runtime Flags';
    }

    if (
      member.startsWith('condition') ||
      member.includes('Recover') ||
      member.includes('Drain') ||
      member === 'hpRecoverRatio'
    ) {
      return 'Runtime Effects';
    }

    return 'Runtime Core';
  }

  if (field.field === 'flinch') {
    return 'Conventional Effects';
  }

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

export function parseMoveTimingField(field: string) {
  if (field === 'timing.hitPercent' || field === 'timing.cooldown') {
    return {
      isLegacyTemplate: true,
      member: field.slice('timing.'.length),
      occurrence: null,
      timingMoveId: null
    };
  }

  const exactAdvancedMatch =
    /^timing\.(\d+)\.(\d+)\.([A-Za-z][A-Za-z0-9]*)$/.exec(field);
  if (exactAdvancedMatch) {
    return {
      isLegacyTemplate: false,
      member: exactAdvancedMatch[3],
      occurrence: Number(exactAdvancedMatch[2]),
      timingMoveId: Number(exactAdvancedMatch[1])
    };
  }

  const sharedOrLegacyAdvancedMatch =
    /^timing\.(\d+)\.([A-Za-z][A-Za-z0-9]*)$/.exec(field);
  if (!sharedOrLegacyAdvancedMatch) {
    return null;
  }

  const member = sharedOrLegacyAdvancedMatch[2];
  const isSharedMember = member === 'hitPercent' || member === 'cooldown';
  return {
    isLegacyTemplate: !isSharedMember,
    member,
    occurrence: isSharedMember ? null : Number(sharedOrLegacyAdvancedMatch[1]),
    timingMoveId: isSharedMember ? Number(sharedOrLegacyAdvancedMatch[1]) : null
  };
}

export function resolveMoveTimingEditableField(
  field: string,
  timingMoveId: number,
  occurrence: number
) {
  const timingField = parseMoveTimingField(field);
  if (!timingField || !timingField.isLegacyTemplate) {
    return field;
  }

  return timingField.occurrence === null
    ? `timing.${timingMoveId}.${timingField.member}`
    : `timing.${timingMoveId}.${occurrence}.${timingField.member}`;
}

export function isMoveProjectileField(field: string) {
  const member = parseMoveTimingField(field)?.member ?? '';
  return member.startsWith('overwriteProjectile') || member.startsWith('replacementProjectile');
}

export function getMoveRelationalValidationIssues(
  move: MoveRecord,
  fields: NumericMoveEditableField[],
  drafts: Readonly<Record<string, string>>
): MoveRelationalValidationIssue[] {
  const visibleFieldNames = new Set(fields.map((field) => field.field));
  const projectedValue = (field: string) => {
    const draft = drafts[field]?.trim();
    if (draft !== undefined && draft.length > 0) {
      const parsed = Number(draft);
      if (Number.isFinite(parsed)) {
        return parsed;
      }
    }

    return getEditableMoveFieldValue(move, field);
  };
  const issues: MoveRelationalValidationIssue[] = [];
  const addOrderedPairIssue = (minimumField: string, maximumField: string, label: string) => {
    if (!visibleFieldNames.has(minimumField) || !visibleFieldNames.has(maximumField)) {
      return;
    }

    const minimum = projectedValue(minimumField);
    const maximum = projectedValue(maximumField);
    if (minimum !== null && maximum !== null && minimum > maximum) {
      issues.push({
        fields: [minimumField, maximumField],
        message: `${label} minimum cannot be greater than its maximum.`
      });
    }
  };

  for (const field of fields) {
    const battleMatch = /^battle\.(\d+)\.conditionTurnMin$/.exec(field.field);
    if (battleMatch) {
      const prefix = `battle.${battleMatch[1]}.`;
      addOrderedPairIssue(
        `${prefix}conditionTurnMin`,
        `${prefix}conditionTurnMax`,
        'Condition turn'
      );
    }
  }

  const battleVariants = new Set(
    fields.flatMap((field) => {
      const match = /^battle\.(\d+)\./.exec(field.field);
      return match ? [Number(match[1])] : [];
    })
  );
  for (const variant of battleVariants) {
    const prefix = `battle.${variant}.`;
    const stats = [1, 2, 3].map((slot) => ({
      fields: [
        `${prefix}stat${slot}`,
        `${prefix}stat${slot}Stage`,
        `${prefix}stat${slot}Percent`
      ],
      percent: projectedValue(`${prefix}stat${slot}Percent`),
      slot,
      stage: projectedValue(`${prefix}stat${slot}Stage`),
      stat: projectedValue(`${prefix}stat${slot}`)
    }));
    const statFieldsAreVisible = stats.every((stat) =>
      stat.fields.every((field) => visibleFieldNames.has(field))
    );
    if (!statFieldsAreVisible) {
      continue;
    }

    const occupiedStats = stats.filter((stat) => stat.stat !== null && stat.stat !== 0);
    for (const stat of stats) {
      if (stat.stat === null || stat.stage === null || stat.percent === null) {
        continue;
      }

      const isValidUnused = stat.stat === 0 && stat.stage === 0 && stat.percent === 0;
      const isValidOccupied = stat.stat !== 0 && stat.stage !== 0;
      if (!isValidUnused && !isValidOccupied) {
        issues.push({
          fields: stat.fields,
          message: `Stat-change slot ${stat.slot} must be completely empty or include a stat and stage change.`
        });
      }
    }

    const firstEmptySlot = stats.findIndex((stat) => stat.stat === 0);
    if (
      firstEmptySlot >= 0 &&
      stats.slice(firstEmptySlot + 1).some((stat) => stat.stat !== null && stat.stat !== 0)
    ) {
      issues.push({
        fields: stats.flatMap((stat) => stat.fields),
        message: 'Stat changes must use consecutive slots without an empty gap.'
      });
    }

    const duplicateStats = occupiedStats.filter(
      (stat, index) => occupiedStats.findIndex((candidate) => candidate.stat === stat.stat) !== index
    );
    if (duplicateStats.length > 0) {
      issues.push({
        fields: occupiedStats.flatMap((stat) => stat.fields),
        message: 'Each stat can appear in only one stat-change slot.'
      });
    }

    if (occupiedStats.some((stat) => stat.stat === 9) && occupiedStats.length > 1) {
      issues.push({
        fields: occupiedStats.flatMap((stat) => stat.fields),
        message: 'All Stats must be the only occupied stat-change slot.'
      });
    }
  }

  const timingPrefixes = new Set(fields.flatMap((field) => {
    const timingField = parseMoveTimingField(field.field);
    if (!timingField || timingField.occurrence === null) {
      return [];
    }

    return [
      timingField.timingMoveId === null
        ? `timing.${timingField.occurrence}.`
        : `timing.${timingField.timingMoveId}.${timingField.occurrence}.`
    ];
  }));
  for (const prefix of timingPrefixes) {
    addOrderedPairIssue(`${prefix}rangeMin`, `${prefix}rangeMax`, 'Range');
    addOrderedPairIssue(
      `${prefix}projectileCountMin`,
      `${prefix}projectileCountMax`,
      'Projectile count'
    );

    let foundEmptyPair = false;
    for (let pair = 1; pair <= 5; pair += 1) {
      const overwriteField = `${prefix}overwriteProjectile${pair}`;
      const replacementField = `${prefix}replacementProjectile${pair}`;
      if (!visibleFieldNames.has(overwriteField) || !visibleFieldNames.has(replacementField)) {
        continue;
      }

      const overwrite = projectedValue(overwriteField);
      const replacement = projectedValue(replacementField);
      if (overwrite === null || replacement === null) {
        continue;
      }

      const overwriteIsEmpty = overwrite === 0;
      const replacementIsEmpty = replacement === 0;
      if (overwriteIsEmpty !== replacementIsEmpty) {
        issues.push({
          fields: [overwriteField, replacementField],
          message: `Projectile replacement ${pair} must specify both projectiles or leave both as None.`
        });
      }

      const pairIsEmpty = overwriteIsEmpty && replacementIsEmpty;
      if (!pairIsEmpty && foundEmptyPair) {
        issues.push({
          fields: [overwriteField, replacementField],
          message: `Projectile replacement ${pair} cannot follow an empty replacement slot.`
        });
      }
      foundEmptyPair ||= pairIsEmpty;
    }
  }

  return issues;
}

export function getMoveRuntimeStatRecipient(
  effectCategory: number,
  targetName: string
): MoveRuntimeStatRecipient {
  switch (effectCategory) {
    case 7:
      return { kind: 'user' };
    case 6:
      return { kind: 'hit-targets' };
    case 0:
    case 2:
    case 5:
      return { kind: 'move-target', targetName };
    default:
      return { kind: 'scripted' };
  }
}

export function formatMoveAccuracy(accuracy: number) {
  if (accuracy === 0) {
    return '-';
  }

  return accuracy === 101 ? 'Always hits' : accuracy.toString();
}

export function formatMoveRuntimeVariantLabel(variant: number) {
  switch (variant) {
    case 0:
      return 'Normal Move';
    case 1:
      return 'Plus Move';
    case 2:
      return 'Boss Move';
    default:
      return `Variant ${variant}`;
  }
}

export function getMoveRuntimeVariants(move: MoveRecord) {
  return Array.from(
    new Set([
      ...move.runtimeVariants.map((variant) => variant.variant),
      ...move.timingRows.map((timing) => timing.variant),
      ...(move.playerDamageRows.length > 0 ? [2] : [])
    ])
  ).sort((left, right) => left - right);
}

export function resolveMoveRuntimeVariant(
  move: MoveRecord,
  requestedVariant?: number | null
) {
  const variants = getMoveRuntimeVariants(move);
  return requestedVariant !== null &&
    requestedVariant !== undefined &&
    variants.includes(requestedVariant)
    ? requestedVariant
    : variants[0] ?? null;
}

export function getMoveRuntimeView(
  move: MoveRecord,
  variant: number | null,
  timingSelection?: MoveRuntimeTimingSelection
): MoveRuntimeView | null {
  if (variant === null || !getMoveRuntimeVariants(move).includes(variant)) {
    return null;
  }

  const battleRow =
    move.runtimeVariants.find((candidate) => candidate.variant === variant) ?? null;
  const timingRows = move.timingRows.filter((candidate) => candidate.variant === variant);
  const timingMoveId = timingSelection?.timingMoveId;
  const matchingTimingRows =
    timingMoveId === null
      ? []
      : timingMoveId === undefined
        ? timingRows
        : timingRows.filter((candidate) => candidate.timingMoveId === timingMoveId);
  const occurrence = timingSelection?.occurrence;
  const timingRow =
    occurrence === null
      ? null
      : occurrence === undefined
        ? matchingTimingRows[0] ?? null
        : matchingTimingRows.find((candidate) => candidate.occurrence === occurrence) ?? null;

  const playerDamageRows = variant === 2 ? move.playerDamageRows : [];
  return { battleRow, playerDamageRows, timingRow, variant };
}

export function parseMovePlayerDamageField(field: string) {
  const match = /^playerDamage\.([1-9]\d*)$/.exec(field);
  if (!match) {
    return null;
  }

  const attackId = Number(match[1]);
  return Number.isSafeInteger(attackId) ? attackId : null;
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
