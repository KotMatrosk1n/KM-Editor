/* SPDX-License-Identifier: GPL-3.0-only */

import type {
  ItemFieldUpdate,
  MoveFieldUpdate,
  PokemonFieldUpdate,
  TrainerFieldUpdate
} from '../bridge/svBatchFieldContracts';
import type { ChangeSetWorkspaceSnapshot } from '../bridge/changeSetContracts';
import type { EditSession } from '../bridge/contracts';
import {
  projectGameToFamily,
  semanticFieldRefKey,
  type SemanticRecordRef
} from '../workbench/semanticContracts';
import {
  AdvancedAuthoringError,
  advancedAuthoringMaximumMutationCount,
  type AdvancedAuthoringSourceBinding,
  type AuthoringStageRequest,
  type AuthoringStagedCommitMetadata,
  type AuthoringStagedHistoryState
} from './advancedAuthoringTypes';
import { getAdvancedAuthoringAdapter } from './authoringAdapterRegistry';

export type ItemAuthoringEditSessionBatch = {
  activeChangeSetId: string;
  adapterId: 'items.scalar.v1';
  kind: 'items';
  updates: readonly ItemFieldUpdate[];
};

export type PokemonAuthoringEditSessionBatch = {
  activeChangeSetId: string;
  adapterId: 'pokemon.personal.v1';
  kind: 'pokemon';
  updates: readonly PokemonFieldUpdate[];
};

export type MoveAuthoringEditSessionBatch = {
  activeChangeSetId: string;
  adapterId: 'moves.core.v1';
  kind: 'moves';
  updates: readonly MoveFieldUpdate[];
};

export type TrainerPartyAuthoringEditSessionBatch = {
  activeChangeSetId: string;
  adapterId: 'trainers.party.v1';
  kind: 'trainerParty';
  updates: readonly TrainerFieldUpdate[];
};

export type AuthoringEditSessionBatch =
  | ItemAuthoringEditSessionBatch
  | PokemonAuthoringEditSessionBatch
  | MoveAuthoringEditSessionBatch
  | TrainerPartyAuthoringEditSessionBatch;

export type AtomicAuthoringStageExecution = {
  batch: AuthoringEditSessionBatch;
  request: AuthoringStageRequest;
};

export type AtomicAuthoringStagePreparation = {
  previousSession: EditSession | null;
  stagedSession: EditSession;
};

export type AtomicAuthoringStageExecutor = {
  capture: (
    execution: AtomicAuthoringStageExecution & AtomicAuthoringStagePreparation
  ) => Promise<AuthoringStagedCommitMetadata>;
  stageBatch: (
    execution: AtomicAuthoringStageExecution
  ) => Promise<AtomicAuthoringStagePreparation>;
};

export async function executeAtomicAuthoringStage(
  request: AuthoringStageRequest,
  executor: AtomicAuthoringStageExecutor
) {
  const execution = { batch: createAuthoringEditSessionBatch(request), request };
  const preparation = await executor.stageBatch(execution);
  validatePreparedSession(request, preparation.stagedSession);
  if (preparation.previousSession) {
    validatePreparedSession(request, preparation.previousSession);
  }
  return executor.capture({ ...execution, ...preparation });
}

export function createAuthoringStagedHistoryState(
  activeChangeSetId: string,
  snapshot: ChangeSetWorkspaceSnapshot
): AuthoringStagedHistoryState {
  if (
    !validAssociationId(activeChangeSetId) ||
    snapshot.document.activeChangeSetId !== activeChangeSetId ||
    snapshot.etag === null
  ) {
    throw new AdvancedAuthoringError('history-conflict');
  }
  return {
    activeChangeSetId,
    canRedo: snapshot.canRedo,
    canUndo: snapshot.canUndo,
    changeSetETag: snapshot.etag,
    redoLabel: snapshot.redoLabel,
    undoLabel: snapshot.undoLabel
  };
}

export function createAuthoringStagedCommitMetadata(options: {
  activeChangeSetId: string;
  capturedOperationIds: readonly string[];
  previousSourceBinding: AdvancedAuthoringSourceBinding;
  removedOperationIds: readonly string[];
  snapshot: ChangeSetWorkspaceSnapshot;
}): AuthoringStagedCommitMetadata {
  const affectedOperationIds = [
    ...options.capturedOperationIds,
    ...options.removedOperationIds
  ];
  if (affectedOperationIds.length === 0) {
    throw new AdvancedAuthoringError('no-effective-change');
  }
  if (
    affectedOperationIds.length > advancedAuthoringMaximumMutationCount ||
    new Set(affectedOperationIds).size !== affectedOperationIds.length ||
    affectedOperationIds.some((operationId) => !validAssociationId(operationId))
  ) {
    throw new AdvancedAuthoringError('history-conflict');
  }
  const nextSourceBinding = options.snapshot.effective.session?.authoringBinding;
  if (
    !options.snapshot.effective.canMaterialize ||
    !nextSourceBinding ||
    options.snapshot.etag === null ||
    nextSourceBinding.workspaceETag !== options.snapshot.etag ||
    nextSourceBinding.workspaceETag === options.previousSourceBinding.workspaceETag ||
    !/^[A-Fa-f0-9]{64}$/u.test(
      options.snapshot.effective.sourceRevisionFingerprint
    ) ||
    !isAuthorizedCaptureTransition(
      options.previousSourceBinding,
      nextSourceBinding
    )
  ) {
    throw new AdvancedAuthoringError('source-assumption-changed');
  }
  return {
    ...createAuthoringStagedHistoryState(
      options.activeChangeSetId,
      options.snapshot
    ),
    capturedOperationIds: [...options.capturedOperationIds],
    removedOperationIds: [...options.removedOperationIds],
    sourceTransition: {
      nextSourceBinding,
      previousSourceBinding: options.previousSourceBinding,
      sourceRevisionFingerprint:
        options.snapshot.effective.sourceRevisionFingerprint
    }
  };
}

function isAuthorizedCaptureTransition(
  previous: AdvancedAuthoringSourceBinding,
  next: AdvancedAuthoringSourceBinding
) {
  return (
    previous.version === 1 &&
    next.version === previous.version &&
    next.projectId === previous.projectId &&
    next.outputProfileId === previous.outputProfileId &&
    next.outputMode === previous.outputMode &&
    next.outputRootFingerprint === previous.outputRootFingerprint &&
    next.workspacePersonalStateETag === previous.workspacePersonalStateETag &&
    next.selectedChangeSetIds.length === previous.selectedChangeSetIds.length &&
    next.selectedChangeSetIds.every(
      (changeSetId, index) => changeSetId === previous.selectedChangeSetIds[index]
    )
  );
}

export function createAuthoringEditSessionBatch(
  request: AuthoringStageRequest
): AuthoringEditSessionBatch {
  validateRequest(request);
  switch (request.adapterId) {
    case 'items.scalar.v1':
      return {
        activeChangeSetId: request.activeChangeSetId,
        adapterId: request.adapterId,
        kind: 'items',
        updates: request.mutations.map((mutation) => ({
          field: mutation.field.fieldKey,
          itemId: parseRootIntegerRecord(mutation.field.record, 'item'),
          value: formatValue(mutation.afterValue)
        }))
      };
    case 'pokemon.personal.v1':
      return {
        activeChangeSetId: request.activeChangeSetId,
        adapterId: request.adapterId,
        kind: 'pokemon',
        updates: request.mutations.map((mutation) => ({
          field: mutation.field.fieldKey,
          personalId: parseRootIntegerRecord(mutation.field.record, 'pokemon-personal'),
          value: formatValue(mutation.afterValue)
        }))
      };
    case 'moves.core.v1':
      return {
        activeChangeSetId: request.activeChangeSetId,
        adapterId: request.adapterId,
        kind: 'moves',
        updates: request.mutations.map((mutation) => ({
          field: mutation.field.fieldKey,
          moveId: parseRootIntegerRecord(mutation.field.record, 'move'),
          value: formatValue(mutation.afterValue)
        }))
      };
    case 'trainers.party.v1':
      return {
        activeChangeSetId: request.activeChangeSetId,
        adapterId: request.adapterId,
        kind: 'trainerParty',
        updates: request.mutations.map((mutation) => {
          const { slot, trainerId } = parseTrainerPartyRecord(mutation.field.record);
          return {
            field: mutation.field.fieldKey,
            slot,
            trainerId,
            value: formatValue(mutation.afterValue)
          };
        })
      };
    default:
      throw new AdvancedAuthoringError('adapter-unavailable');
  }
}

function validateRequest(request: AuthoringStageRequest) {
  const registration = getAdvancedAuthoringAdapter(request.adapterId);
  const binding = request.sourceBinding;
  if (
    !registration ||
    request.schemaVersion !== 1 ||
    !validAssociationId(request.activeChangeSetId) ||
    !validProjectId(request.projectId) ||
    request.mutations.length === 0 ||
    request.mutations.length > advancedAuthoringMaximumMutationCount ||
    binding.version !== 1 ||
    binding.projectId !== request.projectId ||
    !/^[A-Fa-f0-9]{64}$/u.test(binding.workspaceETag) ||
    !/^[A-Fa-f0-9]{64}$/u.test(binding.workspaceFingerprint) ||
    !/^[A-Fa-f0-9]{64}$/u.test(binding.outputRootFingerprint) ||
    !validOutputMode(binding.outputMode) ||
    binding.selectedChangeSetIds.length > 64 ||
    new Set(binding.selectedChangeSetIds).size !== binding.selectedChangeSetIds.length ||
    binding.selectedChangeSetIds.some((id) => !validAssociationId(id)) ||
    (binding.outputProfileId !== null && !validAssociationId(binding.outputProfileId)) ||
    (binding.outputProfileId === null
      ? binding.workspacePersonalStateETag !== null
      : !/^[A-Fa-f0-9]{64}$/u.test(
          binding.workspacePersonalStateETag ?? ''
        )) ||
    !registration.games.includes(request.game)
  ) {
    throw new AdvancedAuthoringError('invalid-scope');
  }

  const policiesByField = new Map(
    registration.fieldPolicies.map((field) => [field.fieldKey, field])
  );
  const seenFields = new Set<string>();
  for (const mutation of request.mutations) {
    const record = mutation.field.record;
    const policy = policiesByField.get(mutation.field.fieldKey);
    if (
      record.domain !== registration.domain ||
      record.gameFamily !== projectGameToFamily(request.game) ||
      record.recordKind.key !== registration.recordKind ||
      record.recordKind.schemaVersion !== registration.recordKindSchemaVersion ||
      !policy ||
      !Number.isFinite(mutation.beforeValue) ||
      !Number.isFinite(mutation.afterValue) ||
      ((policy.valueKind === 'integer' ||
        policy.valueKind === 'enum' ||
        policy.valueKind === 'boolean') &&
        (!Number.isSafeInteger(mutation.beforeValue) ||
          !Number.isSafeInteger(mutation.afterValue))) ||
      (policy.valueKind === 'boolean' &&
        (![0, 1].includes(mutation.beforeValue) ||
          ![0, 1].includes(mutation.afterValue)))
    ) {
      throw new AdvancedAuthoringError('invalid-field-value');
    }
    const key = semanticFieldRefKey(mutation.field);
    if (seenFields.has(key)) {
      throw new AdvancedAuthoringError('invalid-field-value');
    }
    seenFields.add(key);
  }
}

function validatePreparedSession(request: AuthoringStageRequest, session: EditSession) {
  const binding = session.authoringBinding;
  if (
    !binding ||
    binding.version !== request.sourceBinding.version ||
    binding.projectId !== request.sourceBinding.projectId ||
    binding.workspaceETag !== request.sourceBinding.workspaceETag ||
    binding.workspaceFingerprint !== request.sourceBinding.workspaceFingerprint ||
    binding.outputProfileId !== request.sourceBinding.outputProfileId ||
    binding.outputMode !== request.sourceBinding.outputMode ||
    binding.outputRootFingerprint !== request.sourceBinding.outputRootFingerprint ||
    binding.workspacePersonalStateETag !==
      request.sourceBinding.workspacePersonalStateETag ||
    binding.selectedChangeSetIds.length !==
      request.sourceBinding.selectedChangeSetIds.length ||
    binding.selectedChangeSetIds.some(
      (changeSetId, index) =>
        changeSetId !== request.sourceBinding.selectedChangeSetIds[index]
    )
  ) {
    throw new AdvancedAuthoringError('source-assumption-changed');
  }
}

function parseRootIntegerRecord(record: SemanticRecordRef, expectedKind: string) {
  if (record.recordKind.key !== expectedKind || record.subrecordId !== null) {
    throw new AdvancedAuthoringError('record-unavailable');
  }
  return parseNonNegativeInteger(record.recordId);
}

function parseTrainerPartyRecord(record: SemanticRecordRef) {
  if (record.recordKind.key !== 'trainer') {
    throw new AdvancedAuthoringError('record-unavailable');
  }
  const match = /^party-slot:([0-5])$/u.exec(record.subrecordId ?? '');
  if (!match) {
    throw new AdvancedAuthoringError('record-unavailable');
  }
  return {
    slot: parseNonNegativeInteger(match[1]!),
    trainerId: parseNonNegativeInteger(record.recordId)
  };
}

function parseNonNegativeInteger(value: string) {
  if (!/^(?:0|[1-9][0-9]*)$/u.test(value)) {
    throw new AdvancedAuthoringError('record-unavailable');
  }
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed) || parsed < 0) {
    throw new AdvancedAuthoringError('record-unavailable');
  }
  return parsed;
}

function formatValue(value: number) {
  if (!Number.isFinite(value)) {
    throw new AdvancedAuthoringError('invalid-field-value');
  }
  return Object.is(value, -0) ? '0' : value.toString();
}

function validAssociationId(value: string) {
  return (
    value.length >= 1 &&
    value.length <= 128 &&
    /^[A-Za-z0-9][A-Za-z0-9._-]*$/u.test(value)
  );
}

function validProjectId(value: string) {
  return (
    value.length >= 1 &&
    value.length <= 128 &&
    value === value.trim() &&
    !/\p{Cc}/u.test(value)
  );
}

function validOutputMode(
  value: AuthoringStageRequest['sourceBinding']['outputMode']
) {
  return (
    value === null ||
    value === 'standalone' ||
    value === 'trinityModManager' ||
    value === 'trinityBypass'
  );
}
