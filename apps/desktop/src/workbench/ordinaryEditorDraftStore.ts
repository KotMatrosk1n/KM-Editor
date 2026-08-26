/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectGame } from '../bridge/contracts';
import {
  workspaceDraftMaximumCount,
  workspaceDraftMaximumPayloadBytes
} from '../bridge/workspaceDraftContracts';
import type { WorkbenchSection } from '../workbenchStore';
import {
  getWorkbenchCapabilityRegistration,
  isCapabilityRegisteredForGame,
  isRegisteredWorkbenchSection
} from './capabilityRegistry';
import {
  ProjectDraftRegistry,
  ProjectDraftRegistryError,
  projectDraftKey,
  unassignedDraftChangeSetId,
  type ProjectDraft,
  type ProjectDraftAdapter,
  type ProjectDraftEntryInspection,
  type ProjectDraftKey
} from './draftRegistry';
import { PrivateWorkspaceConflictError } from './privateWorkspaceStorage';
import {
  projectGameToFamily,
  semanticRecordRefKey,
  type SemanticRecordRef
} from './semanticContracts';

export type OrdinaryEditorDraftScope = {
  domain: string;
  entity: SemanticRecordRef;
  game: ProjectGame;
  projectId: string;
  section: WorkbenchSection;
  sourceRevisionFingerprint: string;
};

export type OrdinaryEditorDraftErrorCode =
  | 'invalid-scope'
  | 'adapter-mismatch'
  | 'adapter-version-unsupported'
  | 'migration-failed'
  | 'hydration-rejected'
  | 'payload-invalid'
  | 'payload-too-large'
  | 'storage-conflict'
  | 'storage-limit'
  | 'storage-unavailable'
  | 'reconciliation-failed';

export type OrdinaryEditorDraftInspection = {
  adapterId: string;
  adapterSchemaVersion: number;
  adapterStatus: 'current' | 'migration-available' | 'incompatible' | 'unknown';
  domain: string;
  game: ProjectGame;
  payloadBytes: number;
  projectId: string;
  section: WorkbenchSection;
  sourceRevisionFingerprint: string | null;
  sourceStatus: 'current' | 'stale' | 'unknown';
  stableEntityKey: string;
  updatedAtUtc: string;
};

declare const ordinaryEditorDraftRevisionBrand: unique symbol;
export type OrdinaryEditorDraftRevision = {
  readonly [ordinaryEditorDraftRevisionBrand]: true;
};

export type OrdinaryEditorDraftLoadResult<TDraft> =
  | {
      kind: 'ready';
      payload: TDraft;
      revision: OrdinaryEditorDraftRevision;
      updatedAtUtc: string;
      wasMigrated: boolean;
    }
  | { kind: 'missing' }
  | { inspection: OrdinaryEditorDraftInspection; kind: 'stale' }
  | { errorCode: OrdinaryEditorDraftErrorCode; kind: 'error' }
  | { kind: 'cancelled' };

export type OrdinaryEditorDraftListQuery = {
  adapterId?: string;
  domain?: string;
  game?: ProjectGame;
  limit?: number;
  projectId: string;
  section?: WorkbenchSection;
  sourceRevisionFingerprint: string;
};

export type OrdinaryEditorDraftListResult =
  | { entries: readonly OrdinaryEditorDraftInspection[]; kind: 'ready' }
  | { errorCode: OrdinaryEditorDraftErrorCode; kind: 'error' };

export type OrdinaryEditorDraftDiscardQuery = {
  adapterIds?: ReadonlySet<string>;
  game: ProjectGame;
  projectId: string;
  section?: WorkbenchSection;
};

export type OrdinaryEditorDraftDiscardResult =
  | {
      deletedEntries: readonly {
        section: WorkbenchSection;
        stableEntityKey: string;
      }[];
      kind: 'ready';
    }
  | { errorCode: OrdinaryEditorDraftErrorCode; kind: 'error' };

export type OrdinaryEditorDraftInspectionResult =
  | { inspection: OrdinaryEditorDraftInspection; kind: 'ready' }
  | { kind: 'missing' }
  | { errorCode: OrdinaryEditorDraftErrorCode; kind: 'error' };

export type OrdinaryEditorDraftMutationResult<TDraft> =
  | Extract<OrdinaryEditorDraftLoadResult<TDraft>, { kind: 'ready' | 'stale' | 'error' }>
  | { kind: 'missing' };

export type OrdinaryEditorDraftReconciliation<TDraft> =
  | { kind: 'discard' }
  | { kind: 'replace'; payload: TDraft }
  | { kind: 'rebase'; rebase: (stalePayload: TDraft) => TDraft };

type RevisionDetails = {
  adapterId: string;
  adapterSchemaVersion: number;
  payloadJson: string;
  scopeIdentity: string;
  sourceRevisionFingerprint: string;
  updatedAtUtc: string;
};

const revisionDetails = new WeakMap<OrdinaryEditorDraftRevision, RevisionDetails>();
const sha256FingerprintPattern = /^[a-fA-F0-9]{64}$/u;
const defaultOrdinaryDraftListLimit = 50;

class OrdinaryEditorDraftOperationError extends Error {
  public constructor(public readonly code: OrdinaryEditorDraftErrorCode) {
    super(code);
    this.name = 'OrdinaryEditorDraftOperationError';
  }
}

export class OrdinaryEditorDraftStore {
  public constructor(private readonly registry: ProjectDraftRegistry) {}

  public async load<TDraft>(
    scope: OrdinaryEditorDraftScope,
    adapter: ProjectDraftAdapter<TDraft>,
    signal?: AbortSignal
  ): Promise<OrdinaryEditorDraftLoadResult<TDraft>> {
    let key: ProjectDraftKey;
    try {
      key = createProjectDraftKey(scope);
      validateAdapter(adapter);
    } catch {
      return { errorCode: 'invalid-scope', kind: 'error' };
    }
    if (signal?.aborted) {
      return { kind: 'cancelled' };
    }

    let inspection: ProjectDraftEntryInspection | null;
    try {
      inspection = await this.registry.inspect(key);
    } catch {
      return signal?.aborted
        ? { kind: 'cancelled' }
        : { errorCode: 'storage-unavailable', kind: 'error' };
    }
    if (signal?.aborted) {
      return { kind: 'cancelled' };
    }
    if (!inspection) {
      return { kind: 'missing' };
    }

    const adapterError = getAdapterCompatibilityError(inspection, adapter);
    if (adapterError) {
      return { errorCode: adapterError, kind: 'error' };
    }

    let loaded: ProjectDraft<TDraft> | null;
    try {
      loaded = await this.registry.load(key, adapter);
    } catch (error) {
      if (signal?.aborted) {
        return { kind: 'cancelled' };
      }
      if (!(error instanceof ProjectDraftRegistryError)) {
        return { errorCode: 'storage-unavailable', kind: 'error' };
      }
      if (error.code === 'migration-failed' || error.code === 'payload-invalid') {
        return { errorCode: mapOperationError(error, 'payload-invalid'), kind: 'error' };
      }
      try {
        const latestInspection = await this.registry.inspect(key);
        if (!latestInspection) {
          return { kind: 'missing' };
        }
        const latestAdapterError = getAdapterCompatibilityError(
          latestInspection,
          adapter
        );
        if (latestAdapterError) {
          return { errorCode: latestAdapterError, kind: 'error' };
        }
        return {
          errorCode:
            latestInspection.adapterSchemaVersion < adapter.schemaVersion
              ? 'migration-failed'
              : 'payload-invalid',
          kind: 'error'
        };
      } catch {
        return { errorCode: 'storage-unavailable', kind: 'error' };
      }
    }
    if (signal?.aborted) {
      return { kind: 'cancelled' };
    }
    if (!loaded) {
      return { kind: 'missing' };
    }
    if (!sourceRevisionsEqual(loaded.projectSourceRevisionFingerprint, scope)) {
      try {
        return {
          inspection: inspectLoadedDraft(scope, adapter, loaded),
          kind: 'stale'
        };
      } catch (error) {
        return {
          errorCode: mapOperationError(error, 'payload-invalid'),
          kind: 'error'
        };
      }
    }

    if (!loaded.wasMigrated) {
      try {
        return createReadyResult(scope, adapter, loaded, false);
      } catch (error) {
        return {
          errorCode: mapOperationError(error, 'payload-invalid'),
          kind: 'error'
        };
      }
    }

    try {
      const reconciled = await this.registry.reconcile(key, adapter, (current) => {
        if (!current) {
          return { kind: 'keep', result: 'missing' as const };
        }
        if (!sourceRevisionsEqual(current.projectSourceRevisionFingerprint, scope)) {
          return { kind: 'keep', result: 'stale' as const };
        }
        if (!current.wasMigrated) {
          return { kind: 'keep', result: 'ready' as const };
        }
        return {
          kind: 'save',
          payload: current.payload,
          projectSourceRevisionFingerprint: normalizeFingerprint(
            scope.sourceRevisionFingerprint
          ),
          result: 'migrated' as const
        };
      });
      if (reconciled.result === 'missing' || !reconciled.draft) {
        return { kind: 'missing' };
      }
      if (reconciled.result === 'stale') {
        return {
          inspection: inspectLoadedDraft(scope, adapter, reconciled.draft),
          kind: 'stale'
        };
      }
      return createReadyResult(
        scope,
        adapter,
        reconciled.draft,
        reconciled.result === 'migrated'
      );
    } catch (error) {
      return {
        errorCode: mapOperationError(error, 'migration-failed'),
        kind: 'error'
      };
    }
  }

  public async inspect<TDraft>(
    scope: OrdinaryEditorDraftScope,
    adapter: ProjectDraftAdapter<TDraft>
  ): Promise<OrdinaryEditorDraftInspectionResult> {
    let key: ProjectDraftKey;
    try {
      key = createProjectDraftKey(scope);
      validateAdapter(adapter);
    } catch {
      return { errorCode: 'invalid-scope', kind: 'error' };
    }
    try {
      const inspection = await this.registry.inspect(key);
      if (!inspection) {
        return { kind: 'missing' };
      }
      return {
        inspection: toOrdinaryInspection(
          inspection,
          scope.sourceRevisionFingerprint,
          adapter
        ),
        kind: 'ready'
      };
    } catch {
      return { errorCode: 'storage-unavailable', kind: 'error' };
    }
  }

  public async list(
    query: OrdinaryEditorDraftListQuery
  ): Promise<OrdinaryEditorDraftListResult> {
    const limit = query.limit ?? defaultOrdinaryDraftListLimit;
    try {
      validateListQuery(query);
      if (!Number.isSafeInteger(limit) || limit < 1 || limit > workspaceDraftMaximumCount) {
        throw new OrdinaryEditorDraftOperationError('invalid-scope');
      }
      const entries = await this.registry.list(query.projectId, {
        adapterId: query.adapterId,
        domain: query.domain,
        game: query.game,
        limit: workspaceDraftMaximumCount,
        section: query.section
      });
      return {
        entries: entries
          .slice(0, limit)
          .map((entry) =>
            toOrdinaryInspection(
              entry,
              query.sourceRevisionFingerprint,
              null
            )
          ),
        kind: 'ready'
      };
    } catch (error) {
      return {
        errorCode: mapOperationError(error, 'storage-unavailable'),
        kind: 'error'
      };
    }
  }

  public async save<TDraft>(
    scope: OrdinaryEditorDraftScope,
    adapter: ProjectDraftAdapter<TDraft>,
    payload: TDraft,
    expectedRevision: OrdinaryEditorDraftRevision | null
  ): Promise<OrdinaryEditorDraftMutationResult<TDraft>> {
    let key: ProjectDraftKey;
    try {
      key = createProjectDraftKey(scope);
      validateAdapter(adapter);
      validatePayload(adapter, payload);
    } catch (error) {
      return {
        errorCode: mapOperationError(error, 'payload-invalid'),
        kind: 'error'
      };
    }

    try {
      const reconciled = await this.registry.reconcile(key, adapter, (current) => {
        if (current && !sourceRevisionsEqual(current.projectSourceRevisionFingerprint, scope)) {
          return { kind: 'keep', result: 'stale' as const };
        }
        if (!matchesExpectedRevision(scope, adapter, current, expectedRevision)) {
          throw new OrdinaryEditorDraftOperationError('storage-conflict');
        }
        return {
          kind: 'save',
          payload,
          projectSourceRevisionFingerprint: normalizeFingerprint(
            scope.sourceRevisionFingerprint
          ),
          result: 'saved' as const
        };
      });
      if (reconciled.result === 'stale') {
        return reconciled.draft
          ? {
              inspection: inspectLoadedDraft(scope, adapter, reconciled.draft),
              kind: 'stale'
            }
          : { kind: 'missing' };
      }
      return createReadyResult(scope, adapter, reconciled.draft!, false);
    } catch (error) {
      return {
        errorCode: mapOperationError(error, 'storage-unavailable'),
        kind: 'error'
      };
    }
  }

  public async delete<TDraft>(
    scope: OrdinaryEditorDraftScope,
    adapter: ProjectDraftAdapter<TDraft>,
    expectedRevision: OrdinaryEditorDraftRevision | null
  ): Promise<OrdinaryEditorDraftMutationResult<TDraft>> {
    let key: ProjectDraftKey;
    try {
      key = createProjectDraftKey(scope);
      validateAdapter(adapter);
    } catch {
      return { errorCode: 'invalid-scope', kind: 'error' };
    }

    try {
      const reconciled = await this.registry.reconcile(key, adapter, (current) => {
        if (current && !sourceRevisionsEqual(current.projectSourceRevisionFingerprint, scope)) {
          return { kind: 'keep', result: 'stale' as const };
        }
        if (!matchesExpectedRevision(scope, adapter, current, expectedRevision)) {
          throw new OrdinaryEditorDraftOperationError('storage-conflict');
        }
        return {
          kind: current ? ('delete' as const) : ('keep' as const),
          result: 'deleted' as const
        };
      });
      if (reconciled.result === 'stale') {
        return reconciled.draft
          ? {
              inspection: inspectLoadedDraft(scope, adapter, reconciled.draft),
              kind: 'stale'
            }
          : { kind: 'missing' };
      }
      return reconciled.draft
        ? createReadyResult(scope, adapter, reconciled.draft, false)
        : { kind: 'missing' };
    } catch (error) {
      return {
        errorCode: mapOperationError(error, 'storage-unavailable'),
        kind: 'error'
      };
    }
  }

  public async discard(
    scope: OrdinaryEditorDraftScope
  ): Promise<OrdinaryEditorDraftDiscardResult> {
    let key: ProjectDraftKey;
    try {
      key = createProjectDraftKey(scope);
    } catch {
      return { errorCode: 'invalid-scope', kind: 'error' };
    }

    try {
      return {
        deletedEntries: (await this.registry.delete(key))
          ? [{ section: scope.section, stableEntityKey: semanticRecordRefKey(scope.entity) }]
          : [],
        kind: 'ready'
      };
    } catch (error) {
      return {
        errorCode: mapOperationError(error, 'storage-unavailable'),
        kind: 'error'
      };
    }
  }

  public async discardMatching(
    query: OrdinaryEditorDraftDiscardQuery
  ): Promise<OrdinaryEditorDraftDiscardResult> {
    try {
      validateDiscardQuery(query);
      const entries = await this.registry.list(query.projectId, {
        game: query.game,
        limit: workspaceDraftMaximumCount,
        section: query.section
      });
      const deletedEntries: Array<{
        section: WorkbenchSection;
        stableEntityKey: string;
      }> = [];
      for (const entry of entries) {
        if (query.adapterIds && !query.adapterIds.has(entry.adapterId)) {
          continue;
        }
        const entity = parseSemanticRecordRefKey(entry.identity.entityId);
        if (
          entity.domain !== entry.identity.domain ||
          entity.gameFamily !== projectGameToFamily(entry.identity.game)
        ) {
          throw new OrdinaryEditorDraftOperationError('invalid-scope');
        }
        if (
          await this.registry.delete({
            changeSetId: unassignedDraftChangeSetId,
            domain: entry.identity.domain,
            entity,
            game: entry.identity.game,
            projectId: entry.projectId,
            section: entry.identity.section
          })
        ) {
          deletedEntries.push({
            section: entry.identity.section,
            stableEntityKey: entry.identity.entityId
          });
        }
      }
      return { deletedEntries, kind: 'ready' };
    } catch (error) {
      return {
        errorCode: mapOperationError(error, 'storage-unavailable'),
        kind: 'error'
      };
    }
  }

  public async reconcile<TDraft>(
    scope: OrdinaryEditorDraftScope,
    adapter: ProjectDraftAdapter<TDraft>,
    resolution: OrdinaryEditorDraftReconciliation<TDraft>
  ): Promise<OrdinaryEditorDraftMutationResult<TDraft>> {
    let key: ProjectDraftKey;
    try {
      key = createProjectDraftKey(scope);
      validateAdapter(adapter);
      validateResolution(resolution);
      if (resolution.kind === 'replace') {
        validatePayload(adapter, resolution.payload);
      }
    } catch (error) {
      return {
        errorCode: mapOperationError(error, 'reconciliation-failed'),
        kind: 'error'
      };
    }

    try {
      const reconciled = await this.registry.reconcile(key, adapter, (current) => {
        if (!current) {
          if (resolution.kind === 'discard') {
            return { kind: 'keep', result: null };
          }
          if (resolution.kind === 'replace') {
            return {
              kind: 'save',
              payload: resolution.payload,
              projectSourceRevisionFingerprint: normalizeFingerprint(
                scope.sourceRevisionFingerprint
              ),
              result: null
            };
          }
          throw new OrdinaryEditorDraftOperationError('storage-conflict');
        }
        if (sourceRevisionsEqual(current.projectSourceRevisionFingerprint, scope)) {
          throw new OrdinaryEditorDraftOperationError('storage-conflict');
        }
        if (resolution.kind === 'discard') {
          return { kind: 'delete', result: null };
        }
        const nextPayload =
          resolution.kind === 'replace'
            ? resolution.payload
            : resolution.rebase(current.payload);
        validatePayload(adapter, nextPayload);
        return {
          kind: 'save',
          payload: nextPayload,
          projectSourceRevisionFingerprint: normalizeFingerprint(
            scope.sourceRevisionFingerprint
          ),
          result: null
        };
      });
      return reconciled.draft
        ? createReadyResult(scope, adapter, reconciled.draft, false)
        : { kind: 'missing' };
    } catch (error) {
      return {
        errorCode: mapOperationError(error, 'reconciliation-failed'),
        kind: 'error'
      };
    }
  }
}

export function ordinaryEditorDraftScopeIdentity(scope: OrdinaryEditorDraftScope) {
  return JSON.stringify({
    domain: scope.domain,
    entity: semanticRecordRefKey(scope.entity),
    game: scope.game,
    projectId: scope.projectId,
    section: scope.section,
    sourceRevisionFingerprint: normalizeFingerprint(scope.sourceRevisionFingerprint)
  });
}

function createProjectDraftKey(scope: OrdinaryEditorDraftScope): ProjectDraftKey {
  validateFingerprint(scope.sourceRevisionFingerprint);
  const key: ProjectDraftKey = {
    changeSetId: unassignedDraftChangeSetId,
    domain: scope.domain,
    entity: scope.entity,
    game: scope.game,
    projectId: scope.projectId,
    section: scope.section
  };
  projectDraftKey(key);
  return key;
}

function toOrdinaryInspection<TDraft>(
  inspection: ProjectDraftEntryInspection,
  currentSourceRevisionFingerprint: string,
  adapter: ProjectDraftAdapter<TDraft> | null
): OrdinaryEditorDraftInspection {
  const adapterStatus = !adapter
    ? 'unknown'
    : inspection.adapterId !== adapter.adapterId ||
        inspection.adapterSchemaVersion > adapter.schemaVersion
      ? 'incompatible'
      : inspection.adapterSchemaVersion < adapter.schemaVersion
        ? adapter.migratePayload
          ? 'migration-available'
          : 'incompatible'
        : 'current';
  return {
    adapterId: inspection.adapterId,
    adapterSchemaVersion: inspection.adapterSchemaVersion,
    adapterStatus,
    domain: inspection.identity.domain,
    game: inspection.identity.game,
    payloadBytes: inspection.payloadBytes,
    projectId: inspection.projectId,
    section: inspection.identity.section,
    sourceRevisionFingerprint:
      inspection.projectSourceRevisionFingerprint ?? null,
    sourceStatus:
      inspection.projectSourceRevisionFingerprint === null
        ? 'unknown'
        : normalizeFingerprint(inspection.projectSourceRevisionFingerprint) ===
            normalizeFingerprint(currentSourceRevisionFingerprint)
          ? 'current'
          : 'stale',
    stableEntityKey: inspection.identity.entityId,
    updatedAtUtc: inspection.updatedAtUtc
  };
}

function inspectLoadedDraft<TDraft>(
  scope: OrdinaryEditorDraftScope,
  adapter: ProjectDraftAdapter<TDraft>,
  draft: ProjectDraft<TDraft>
): OrdinaryEditorDraftInspection {
  let payloadBytes: number;
  try {
    payloadBytes = new TextEncoder().encode(
      JSON.stringify(adapter.serializePayload(draft.payload))
    ).byteLength;
  } catch (error) {
    throw new OrdinaryEditorDraftOperationError('payload-invalid');
  }
  return {
    adapterId: draft.adapterId,
    adapterSchemaVersion: draft.storedAdapterSchemaVersion,
    adapterStatus: draft.wasMigrated ? 'migration-available' : 'current',
    domain: scope.domain,
    game: scope.game,
    payloadBytes,
    projectId: scope.projectId,
    section: scope.section,
    sourceRevisionFingerprint:
      draft.projectSourceRevisionFingerprint ?? null,
    sourceStatus:
      draft.projectSourceRevisionFingerprint === null
        ? 'unknown'
        : sourceRevisionsEqual(draft.projectSourceRevisionFingerprint, scope)
          ? 'current'
          : 'stale',
    stableEntityKey: semanticRecordRefKey(scope.entity),
    updatedAtUtc: draft.updatedAtUtc
  };
}

function createReadyResult<TDraft>(
  scope: OrdinaryEditorDraftScope,
  adapter: ProjectDraftAdapter<TDraft>,
  draft: ProjectDraft<TDraft>,
  wasMigrated: boolean
): Extract<OrdinaryEditorDraftLoadResult<TDraft>, { kind: 'ready' }> {
  const payloadJson = JSON.stringify(adapter.serializePayload(draft.payload));
  const revision = {} as OrdinaryEditorDraftRevision;
  revisionDetails.set(revision, {
    adapterId: draft.adapterId,
    adapterSchemaVersion: draft.adapterSchemaVersion,
    payloadJson,
    scopeIdentity: ordinaryEditorDraftScopeIdentity(scope),
    sourceRevisionFingerprint: normalizeFingerprint(
      draft.projectSourceRevisionFingerprint ?? ''
    ),
    updatedAtUtc: draft.updatedAtUtc
  });
  return {
    kind: 'ready',
    payload: draft.payload,
    revision,
    updatedAtUtc: draft.updatedAtUtc,
    wasMigrated
  };
}

function matchesExpectedRevision<TDraft>(
  scope: OrdinaryEditorDraftScope,
  adapter: ProjectDraftAdapter<TDraft>,
  current: ProjectDraft<TDraft> | null,
  expectedRevision: OrdinaryEditorDraftRevision | null
) {
  if (!current || !expectedRevision) {
    return current === null && expectedRevision === null;
  }
  const expected = revisionDetails.get(expectedRevision);
  if (!expected) {
    return false;
  }
  return (
    expected.scopeIdentity === ordinaryEditorDraftScopeIdentity(scope) &&
    expected.adapterId === current.adapterId &&
    expected.adapterSchemaVersion === current.adapterSchemaVersion &&
    expected.sourceRevisionFingerprint ===
      normalizeFingerprint(current.projectSourceRevisionFingerprint ?? '') &&
    expected.updatedAtUtc === current.updatedAtUtc &&
    expected.payloadJson === JSON.stringify(adapter.serializePayload(current.payload))
  );
}

function getAdapterCompatibilityError<TDraft>(
  inspection: ProjectDraftEntryInspection,
  adapter: ProjectDraftAdapter<TDraft>
): OrdinaryEditorDraftErrorCode | null {
  if (inspection.adapterId !== adapter.adapterId) {
    return 'adapter-mismatch';
  }
  if (inspection.adapterSchemaVersion > adapter.schemaVersion) {
    return 'adapter-version-unsupported';
  }
  if (
    inspection.adapterSchemaVersion < adapter.schemaVersion &&
    !adapter.migratePayload
  ) {
    return 'adapter-version-unsupported';
  }
  return null;
}

function validateAdapter<TDraft>(adapter: ProjectDraftAdapter<TDraft>) {
  if (
    typeof adapter.adapterId !== 'string' ||
    adapter.adapterId.length === 0 ||
    !Number.isSafeInteger(adapter.schemaVersion) ||
    adapter.schemaVersion < 1 ||
    typeof adapter.parsePayload !== 'function' ||
    typeof adapter.serializePayload !== 'function'
  ) {
    throw new OrdinaryEditorDraftOperationError('invalid-scope');
  }
}

function validatePayload<TDraft>(adapter: ProjectDraftAdapter<TDraft>, payload: TDraft) {
  let serialized;
  try {
    serialized = adapter.serializePayload(payload);
  } catch {
    throw new OrdinaryEditorDraftOperationError('payload-invalid');
  }
  const payloadBytes = new TextEncoder().encode(JSON.stringify(serialized)).byteLength;
  if (payloadBytes > workspaceDraftMaximumPayloadBytes) {
    throw new OrdinaryEditorDraftOperationError('payload-too-large');
  }
  try {
    adapter.parsePayload(serialized);
  } catch {
    throw new OrdinaryEditorDraftOperationError('payload-invalid');
  }
}

function validateResolution<TDraft>(resolution: OrdinaryEditorDraftReconciliation<TDraft>) {
  if (
    typeof resolution !== 'object' ||
    resolution === null ||
    !['discard', 'replace', 'rebase'].includes(resolution.kind) ||
    (resolution.kind === 'rebase' && typeof resolution.rebase !== 'function')
  ) {
    throw new OrdinaryEditorDraftOperationError('reconciliation-failed');
  }
}

function validateFingerprint(fingerprint: string) {
  if (!sha256FingerprintPattern.test(fingerprint)) {
    throw new OrdinaryEditorDraftOperationError('invalid-scope');
  }
}

function validateListQuery(query: OrdinaryEditorDraftListQuery) {
  validateFingerprint(query.sourceRevisionFingerprint);
  validateIdentifier(query.projectId);
  if (query.adapterId !== undefined) {
    validateIdentifier(query.adapterId);
  }
  if (query.domain !== undefined) {
    validateIdentifier(query.domain);
  }
  if (
    query.game !== undefined &&
    !['sword', 'shield', 'scarlet', 'violet', 'za'].includes(query.game)
  ) {
    throw new OrdinaryEditorDraftOperationError('invalid-scope');
  }
  if (query.section !== undefined) {
    if (!isRegisteredWorkbenchSection(query.section)) {
      throw new OrdinaryEditorDraftOperationError('invalid-scope');
    }
    const registration = getWorkbenchCapabilityRegistration(query.section);
    if (
      (query.domain !== undefined && registration.domain !== query.domain) ||
      (query.game !== undefined &&
        !isCapabilityRegisteredForGame(query.section, query.game))
    ) {
      throw new OrdinaryEditorDraftOperationError('invalid-scope');
    }
  }
}

function validateDiscardQuery(query: OrdinaryEditorDraftDiscardQuery) {
  validateIdentifier(query.projectId);
  if (!['sword', 'shield', 'scarlet', 'violet', 'za'].includes(query.game)) {
    throw new OrdinaryEditorDraftOperationError('invalid-scope');
  }
  if (query.section !== undefined) {
    if (
      !isRegisteredWorkbenchSection(query.section) ||
      !isCapabilityRegisteredForGame(query.section, query.game)
    ) {
      throw new OrdinaryEditorDraftOperationError('invalid-scope');
    }
  }
  if (query.adapterIds) {
    if (query.adapterIds.size > workspaceDraftMaximumCount) {
      throw new OrdinaryEditorDraftOperationError('invalid-scope');
    }
    for (const adapterId of query.adapterIds) {
      validateIdentifier(adapterId);
    }
  }
}

function parseSemanticRecordRefKey(value: string): SemanticRecordRef {
  const parts = value.split(':');
  if (parts.length !== 6) {
    throw new OrdinaryEditorDraftOperationError('invalid-scope');
  }
  const schemaVersion = Number(parts[3]);
  const gameFamily = parts[0];
  if (
    gameFamily !== 'swordShield' &&
    gameFamily !== 'scarletViolet' &&
    gameFamily !== 'legendsZA'
  ) {
    throw new OrdinaryEditorDraftOperationError('invalid-scope');
  }
  try {
    const subrecordId = decodeURIComponent(parts[5]!);
    const entity: SemanticRecordRef = {
      domain: decodeURIComponent(parts[1]!),
      gameFamily,
      recordId: decodeURIComponent(parts[4]!),
      recordKind: {
        key: decodeURIComponent(parts[2]!),
        schemaVersion
      },
      subrecordId: subrecordId.length === 0 ? null : subrecordId
    };
    semanticRecordRefKey(entity);
    return entity;
  } catch {
    throw new OrdinaryEditorDraftOperationError('invalid-scope');
  }
}

function validateIdentifier(value: string) {
  if (
    typeof value !== 'string' ||
    value.length === 0 ||
    value.length > 256 ||
    value.trim() !== value ||
    /[\u0000-\u001f\u007f-\u009f]/u.test(value)
  ) {
    throw new OrdinaryEditorDraftOperationError('invalid-scope');
  }
}

function normalizeFingerprint(fingerprint: string) {
  return fingerprint.toLowerCase();
}

function sourceRevisionsEqual(
  storedFingerprint: string | null,
  scope: OrdinaryEditorDraftScope
) {
  return (
    storedFingerprint !== null &&
    normalizeFingerprint(storedFingerprint) ===
      normalizeFingerprint(scope.sourceRevisionFingerprint)
  );
}

function mapOperationError(
  error: unknown,
  fallback: OrdinaryEditorDraftErrorCode
): OrdinaryEditorDraftErrorCode {
  if (error instanceof OrdinaryEditorDraftOperationError) {
    return error.code;
  }
  if (error instanceof PrivateWorkspaceConflictError) {
    return 'storage-conflict';
  }
  if (error instanceof ProjectDraftRegistryError) {
    switch (error.code) {
      case 'adapter-mismatch':
        return 'adapter-mismatch';
      case 'adapter-version-unsupported':
        return 'adapter-version-unsupported';
      case 'draft-count-limit':
      case 'document-size-limit':
        return 'storage-limit';
      case 'migration-failed':
        return 'migration-failed';
      case 'payload-invalid':
        return 'payload-invalid';
      case 'payload-size-limit':
        return 'payload-too-large';
    }
  }
  return fallback;
}
