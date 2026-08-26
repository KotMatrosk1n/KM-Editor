/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectGame } from '../bridge/contracts';
import {
  workspaceDraftDocumentSchema,
  workspaceDraftMaximumCount,
  workspaceDraftMaximumDocumentBytes,
  workspaceDraftMaximumEntityIdLength,
  workspaceDraftMaximumIdentifierLength,
  workspaceDraftMaximumPayloadBytes,
  workspaceDraftMaximumStableIdLength,
  workspaceDraftSchemaVersion,
  type WorkspaceDraftDocument,
  type WorkspaceDraftEntry,
  type WorkspaceDraftKey
} from '../bridge/workspaceDraftContracts';
import type { WorkspaceDraftProjectBridgeApi } from '../bridge/workspaceDraftProjectBridge';
import type { WorkbenchSection } from '../workbenchStore';
import {
  getWorkbenchCapabilityRegistration,
  isRegisteredWorkbenchSection,
  isCapabilityRegisteredForGame
} from './capabilityRegistry';
import {
  createBoundedMemoryWorkspaceStorage,
  createWorkspaceDraftPrivateStorage,
  PrivateWorkspaceConflictError,
  type PrivateWorkspaceStorage
} from './privateWorkspaceStorage';
import {
  projectGameToFamily,
  semanticRecordRefKey,
  validateSemanticRecordRef,
  type JsonValue,
  type SemanticRecordRef
} from './semanticContracts';

export const unassignedDraftChangeSetId = 'unassigned';

export type ProjectDraftKey = {
  changeSetId: string;
  domain: string;
  entity: SemanticRecordRef;
  game: ProjectGame;
  projectId: string;
  section: WorkbenchSection;
};

export type ProjectDraftAdapter<TDraft> = {
  adapterId: string;
  migratePayload?: (
    payload: JsonValue,
    fromVersion: number,
    toVersion: number
  ) => JsonValue;
  parsePayload: (payload: JsonValue) => TDraft;
  schemaVersion: number;
  serializePayload: (draft: TDraft) => JsonValue;
};

export type ProjectDraft<TDraft> = {
  adapterId: string;
  adapterSchemaVersion: number;
  key: ProjectDraftKey;
  payload: TDraft;
  projectSourceRevisionFingerprint: string | null;
  storedAdapterSchemaVersion: number;
  updatedAtUtc: string;
  wasMigrated: boolean;
};

export type ProjectDraftEntryInspection = {
  adapterId: string;
  adapterSchemaVersion: number;
  identity: {
    domain: string;
    entityId: string;
    game: ProjectGame;
    section: WorkbenchSection;
  };
  payloadBytes: number;
  projectId: string;
  projectSourceRevisionFingerprint: string | null;
  updatedAtUtc: string;
};

export type ProjectDraftListOptions = {
  adapterId?: string;
  domain?: string;
  game?: ProjectGame;
  limit?: number;
  section?: WorkbenchSection;
};

export type ProjectDraftReconciliation<TDraft, TResult> =
  | { kind: 'keep'; result: TResult }
  | { kind: 'delete'; result: TResult }
  | {
      kind: 'save';
      payload: TDraft;
      projectSourceRevisionFingerprint: string | null;
      result: TResult;
    };

export type ProjectDraftReconciliationResult<TDraft, TResult> = {
  draft: ProjectDraft<TDraft> | null;
  result: TResult;
};

export type ProjectDraftRegistryOptions = {
  maxConflictRetries?: number;
  maxDraftBytes?: number;
  now?: () => Date;
  storage?: PrivateWorkspaceStorage<WorkspaceDraftDocument>;
};

export type ProjectDraftRegistryErrorCode =
  | 'adapter-mismatch'
  | 'adapter-version-unsupported'
  | 'draft-count-limit'
  | 'document-size-limit'
  | 'migration-failed'
  | 'payload-invalid'
  | 'payload-size-limit';

export class ProjectDraftRegistryError extends Error {
  public constructor(
    public readonly code: ProjectDraftRegistryErrorCode,
    message: string,
    options: ErrorOptions = {}
  ) {
    super(message, options);
    this.name = 'ProjectDraftRegistryError';
  }
}

const defaultMaxDraftBytes = workspaceDraftMaximumPayloadBytes;
const defaultMaxConflictRetries = 3;
const defaultListLimit = 50;

export class ProjectDraftRegistry {
  private readonly maxConflictRetries: number;
  private readonly maxDraftBytes: number;
  private readonly now: () => Date;
  private readonly pendingProjectOperations = new Map<string, Promise<void>>();
  private readonly storage: PrivateWorkspaceStorage<WorkspaceDraftDocument>;

  public constructor(options: ProjectDraftRegistryOptions = {}) {
    this.maxConflictRetries = options.maxConflictRetries ?? defaultMaxConflictRetries;
    this.maxDraftBytes = options.maxDraftBytes ?? defaultMaxDraftBytes;
    assertNonNegativeInteger(this.maxConflictRetries, 'maxConflictRetries');
    assertPositiveInteger(this.maxDraftBytes, 'maxDraftBytes');
    this.now = options.now ?? (() => new Date());
    this.storage =
      options.storage ??
      createBoundedMemoryWorkspaceStorage<WorkspaceDraftDocument>({
        maxDocumentBytes: workspaceDraftMaximumDocumentBytes
      });
  }

  public async load<TDraft>(
    key: ProjectDraftKey,
    adapter: ProjectDraftAdapter<TDraft>
  ): Promise<ProjectDraft<TDraft> | null> {
    validateDraftKey(key);
    validateDraftAdapter(adapter);
    await this.waitForPendingProjectOperation(key.projectId);
    const snapshot = await this.storage.read(key.projectId);
    if (!snapshot.document) {
      return null;
    }

    const validatedDocument = workspaceDraftDocumentSchema.parse(snapshot.document);
    const entry = validatedDocument.drafts.find((candidate) =>
      workspaceDraftKeysEqual(candidate.key, toWorkspaceDraftKey(key))
    );
    if (!entry) {
      return null;
    }

    return decodeProjectDraft(key, entry, adapter);
  }

  public async inspect(key: ProjectDraftKey): Promise<ProjectDraftEntryInspection | null> {
    validateDraftKey(key);
    await this.waitForPendingProjectOperation(key.projectId);
    const snapshot = await this.storage.read(key.projectId);
    if (!snapshot.document) {
      return null;
    }

    const workspaceKey = toWorkspaceDraftKey(key);
    const validatedDocument = workspaceDraftDocumentSchema.parse(snapshot.document);
    const entry = validatedDocument.drafts.find((candidate) =>
      workspaceDraftKeysEqual(candidate.key, workspaceKey)
    );
    return entry ? inspectProjectDraftEntry(key.projectId, entry) : null;
  }

  public async list(
    projectId: string,
    options: ProjectDraftListOptions = {}
  ): Promise<readonly ProjectDraftEntryInspection[]> {
    validateBoundedIdentifier(projectId, 'project id');
    const limit = options.limit ?? defaultListLimit;
    assertPositiveInteger(limit, 'limit');
    if (limit > workspaceDraftMaximumCount) {
      throw new Error(`limit cannot exceed ${workspaceDraftMaximumCount}.`);
    }
    if (options.adapterId !== undefined) {
      validateBoundedIdentifier(options.adapterId, 'draft adapter id');
    }
    if (options.domain !== undefined) {
      validateBoundedIdentifier(options.domain, 'draft domain');
    }

    await this.waitForPendingProjectOperation(projectId);
    const snapshot = await this.storage.read(projectId);
    if (!snapshot.document) {
      return [];
    }

    const validatedDocument = workspaceDraftDocumentSchema.parse(snapshot.document);
    return validatedDocument.drafts
      .filter(
        (entry) =>
          entry.key.changeSetId === unassignedDraftChangeSetId &&
          workspaceDraftEntryHasCanonicalScope(entry) &&
          (options.adapterId === undefined || entry.adapterId === options.adapterId) &&
          (options.domain === undefined || entry.key.domain === options.domain) &&
          (options.game === undefined || entry.key.game === options.game) &&
          (options.section === undefined || entry.key.section === options.section)
      )
      .sort(compareWorkspaceDraftEntriesForInspection)
      .slice(0, limit)
      .map((entry) => inspectProjectDraftEntry(projectId, entry));
  }

  public save<TDraft>(
    key: ProjectDraftKey,
    adapter: ProjectDraftAdapter<TDraft>,
    payload: TDraft,
    projectSourceRevisionFingerprint: string | null = null
  ): Promise<ProjectDraft<TDraft>> {
    validateDraftKey(key);
    validateDraftAdapter(adapter);
    const serializedPayload = serializeProjectDraftPayload(adapter, payload);
    assertPayloadBound(serializedPayload, this.maxDraftBytes);
    const normalizedPayload = parseProjectDraftPayload(adapter, serializedPayload);

    return this.enqueueProjectOperation(key.projectId, async () => {
      let initiallyObservedEntry: WorkspaceDraftEntry | undefined;
      for (let conflictCount = 0; ; conflictCount += 1) {
        const snapshot = await this.storage.read(key.projectId);
        const validatedDocument = snapshot.document
          ? workspaceDraftDocumentSchema.parse(snapshot.document)
          : createEmptyWorkspaceDraftDocument(this.now());
        const workspaceKey = toWorkspaceDraftKey(key);
        const currentlyObservedEntry = validatedDocument.drafts.find((candidate) =>
          workspaceDraftKeysEqual(candidate.key, workspaceKey)
        );
        if (conflictCount === 0) {
          initiallyObservedEntry = currentlyObservedEntry;
        } else if (
          !workspaceDraftEntriesSemanticallyEqual(
            initiallyObservedEntry,
            currentlyObservedEntry
          )
        ) {
          throw new PrivateWorkspaceConflictError();
        }
        const updatedAtUtc = this.now().toISOString();
        const entry: WorkspaceDraftEntry = {
          adapterId: adapter.adapterId,
          adapterSchemaVersion: adapter.schemaVersion,
          key: workspaceKey,
          payload: serializedPayload,
          projectSourceRevisionFingerprint,
          updatedAtUtc
        };
        const nextDrafts = validatedDocument.drafts.filter(
          (candidate) => !workspaceDraftKeysEqual(candidate.key, workspaceKey)
        );
        nextDrafts.push(entry);
        if (nextDrafts.length > workspaceDraftMaximumCount) {
          throw new ProjectDraftRegistryError(
            'draft-count-limit',
            `A project workspace can retain at most ${workspaceDraftMaximumCount} drafts.`
          );
        }

        const document = workspaceDraftDocumentSchema.parse({
          drafts: nextDrafts,
          schemaVersion: workspaceDraftSchemaVersion,
          updatedAtUtc
        });
        assertDocumentBound(document);
        try {
          await this.storage.write(key.projectId, document, snapshot.etag);
          return {
            adapterId: adapter.adapterId,
            adapterSchemaVersion: adapter.schemaVersion,
            key,
            payload: normalizedPayload,
            projectSourceRevisionFingerprint,
            storedAdapterSchemaVersion: adapter.schemaVersion,
            updatedAtUtc,
            wasMigrated: false
          };
        } catch (error) {
          if (!this.shouldRetryConflict(error, conflictCount)) {
            throw error;
          }
        }
      }
    });
  }

  public reconcile<TDraft, TResult>(
    key: ProjectDraftKey,
    adapter: ProjectDraftAdapter<TDraft>,
    reconcile: (
      current: ProjectDraft<TDraft> | null
    ) => ProjectDraftReconciliation<TDraft, TResult>
  ): Promise<ProjectDraftReconciliationResult<TDraft, TResult>> {
    validateDraftKey(key);
    validateDraftAdapter(adapter);
    if (typeof reconcile !== 'function') {
      throw new Error('Project draft reconciliation requires a resolver.');
    }

    return this.enqueueProjectOperation(key.projectId, async () => {
      let initiallyObservedEntry: WorkspaceDraftEntry | undefined;
      let initiallyDecodedDraft: ProjectDraft<TDraft> | null = null;
      let resolution: ProjectDraftReconciliation<TDraft, TResult> | undefined;
      let normalizedPayload: TDraft | undefined;
      let serializedPayload: JsonValue | undefined;
      for (let conflictCount = 0; ; conflictCount += 1) {
        const snapshot = await this.storage.read(key.projectId);
        const validatedDocument = snapshot.document
          ? workspaceDraftDocumentSchema.parse(snapshot.document)
          : createEmptyWorkspaceDraftDocument(this.now());
        const workspaceKey = toWorkspaceDraftKey(key);
        const currentlyObservedEntry = validatedDocument.drafts.find((candidate) =>
          workspaceDraftKeysEqual(candidate.key, workspaceKey)
        );
        if (conflictCount === 0) {
          initiallyObservedEntry = currentlyObservedEntry;
          initiallyDecodedDraft = currentlyObservedEntry
            ? decodeProjectDraft(key, currentlyObservedEntry, adapter)
            : null;
          resolution = reconcile(initiallyDecodedDraft);
          validateReconciliation(resolution);
          if (resolution.kind === 'save') {
            serializedPayload = serializeProjectDraftPayload(
              adapter,
              resolution.payload
            );
            assertPayloadBound(serializedPayload, this.maxDraftBytes);
            normalizedPayload = parseProjectDraftPayload(adapter, serializedPayload);
          }
        } else if (
          !workspaceDraftEntriesSemanticallyEqual(
            initiallyObservedEntry,
            currentlyObservedEntry
          )
        ) {
          throw new PrivateWorkspaceConflictError();
        }

        const currentResolution = resolution!;
        if (currentResolution.kind === 'keep') {
          return {
            draft: initiallyDecodedDraft,
            result: currentResolution.result
          };
        }
        if (currentResolution.kind === 'delete' && !currentlyObservedEntry) {
          return { draft: null, result: currentResolution.result };
        }

        const nextDrafts = validatedDocument.drafts.filter(
          (candidate) => !workspaceDraftKeysEqual(candidate.key, workspaceKey)
        );
        let savedDraft: ProjectDraft<TDraft> | null = null;
        const updatedAtUtc = this.now().toISOString();
        if (currentResolution.kind === 'save') {
          const entry: WorkspaceDraftEntry = {
            adapterId: adapter.adapterId,
            adapterSchemaVersion: adapter.schemaVersion,
            key: workspaceKey,
            payload: serializedPayload!,
            projectSourceRevisionFingerprint:
              currentResolution.projectSourceRevisionFingerprint,
            updatedAtUtc
          };
          nextDrafts.push(entry);
          if (nextDrafts.length > workspaceDraftMaximumCount) {
            throw new ProjectDraftRegistryError(
              'draft-count-limit',
              `A project workspace can retain at most ${workspaceDraftMaximumCount} drafts.`
            );
          }
          savedDraft = {
            adapterId: adapter.adapterId,
            adapterSchemaVersion: adapter.schemaVersion,
            key,
            payload: normalizedPayload!,
            projectSourceRevisionFingerprint:
              currentResolution.projectSourceRevisionFingerprint,
            storedAdapterSchemaVersion: adapter.schemaVersion,
            updatedAtUtc,
            wasMigrated: false
          };
        }

        try {
          if (nextDrafts.length === 0) {
            await this.storage.delete(key.projectId, snapshot.etag);
          } else {
            const document = workspaceDraftDocumentSchema.parse({
              drafts: nextDrafts,
              schemaVersion: workspaceDraftSchemaVersion,
              updatedAtUtc
            });
            assertDocumentBound(document);
            await this.storage.write(
              key.projectId,
              document,
              snapshot.etag
            );
          }
          return { draft: savedDraft, result: currentResolution.result };
        } catch (error) {
          if (!this.shouldRetryConflict(error, conflictCount)) {
            throw error;
          }
        }
      }
    });
  }

  public delete(key: ProjectDraftKey): Promise<boolean> {
    validateDraftKey(key);
    return this.enqueueProjectOperation(key.projectId, async () => {
      let initiallyObservedEntry: WorkspaceDraftEntry | undefined;
      for (let conflictCount = 0; ; conflictCount += 1) {
        const snapshot = await this.storage.read(key.projectId);
        const validatedDocument = snapshot.document
          ? workspaceDraftDocumentSchema.parse(snapshot.document)
          : null;
        const workspaceKey = toWorkspaceDraftKey(key);
        const currentlyObservedEntry = validatedDocument?.drafts.find((candidate) =>
          workspaceDraftKeysEqual(candidate.key, workspaceKey)
        );
        if (conflictCount === 0) {
          initiallyObservedEntry = currentlyObservedEntry;
        } else if (
          !workspaceDraftEntriesSemanticallyEqual(
            initiallyObservedEntry,
            currentlyObservedEntry
          )
        ) {
          throw new PrivateWorkspaceConflictError();
        }
        if (!validatedDocument || !currentlyObservedEntry) {
          return false;
        }

        const nextDrafts = validatedDocument.drafts.filter(
          (candidate) => !workspaceDraftKeysEqual(candidate.key, workspaceKey)
        );

        try {
          if (nextDrafts.length === 0) {
            await this.storage.delete(key.projectId, snapshot.etag);
          } else {
            const document = workspaceDraftDocumentSchema.parse({
              drafts: nextDrafts,
              schemaVersion: workspaceDraftSchemaVersion,
              updatedAtUtc: this.now().toISOString()
            });
            assertDocumentBound(document);
            await this.storage.write(
              key.projectId,
              document,
              snapshot.etag
            );
          }
          return true;
        } catch (error) {
          if (!this.shouldRetryConflict(error, conflictCount)) {
            throw error;
          }
        }
      }
    });
  }

  public deleteProject(projectId: string): Promise<boolean> {
    validateBoundedIdentifier(projectId, 'project id');
    return this.enqueueProjectOperation(projectId, async () => {
      const snapshot = await this.storage.read(projectId);
      if (!snapshot.document) {
        return false;
      }
      // Whole-project deletion cannot safely merge a concurrent writer's new entries.
      // Surface an ETag conflict instead of rereading and widening the original delete.
      return this.storage.delete(projectId, snapshot.etag);
    });
  }

  private shouldRetryConflict(error: unknown, conflictCount: number) {
    return (
      error instanceof PrivateWorkspaceConflictError &&
      conflictCount < this.maxConflictRetries
    );
  }

  private enqueueProjectOperation<T>(projectId: string, operation: () => Promise<T>) {
    const previousOperation = this.pendingProjectOperations
      .get(projectId)
      ?.catch(() => undefined);
    const result = (previousOperation ?? Promise.resolve()).then(operation);
    const completion = result.then(
      () => undefined,
      () => undefined
    );
    this.pendingProjectOperations.set(projectId, completion);
    void completion.finally(() => {
      if (this.pendingProjectOperations.get(projectId) === completion) {
        this.pendingProjectOperations.delete(projectId);
      }
    });
    return result;
  }

  private async waitForPendingProjectOperation(projectId: string) {
    await this.pendingProjectOperations.get(projectId)?.catch(() => undefined);
  }
}

export function createBridgeBackedProjectDraftRegistry(
  bridge: WorkspaceDraftProjectBridgeApi,
  options: Omit<ProjectDraftRegistryOptions, 'storage'> = {}
) {
  return new ProjectDraftRegistry({
    ...options,
    storage: createWorkspaceDraftPrivateStorage(bridge)
  });
}

export function projectDraftKey(key: ProjectDraftKey) {
  validateDraftKey(key);
  return JSON.stringify({
    projectId: key.projectId,
    ...toWorkspaceDraftKey(key)
  });
}

function createEmptyWorkspaceDraftDocument(now: Date): WorkspaceDraftDocument {
  return {
    drafts: [],
    schemaVersion: workspaceDraftSchemaVersion,
    updatedAtUtc: now.toISOString()
  };
}

function decodeProjectDraft<TDraft>(
  key: ProjectDraftKey,
  entry: WorkspaceDraftEntry,
  adapter: ProjectDraftAdapter<TDraft>
): ProjectDraft<TDraft> {
  if (entry.adapterId !== adapter.adapterId) {
    throw new ProjectDraftRegistryError(
      'adapter-mismatch',
      `Draft adapter ${entry.adapterId} cannot be opened by ${adapter.adapterId}.`
    );
  }

  let payload = entry.payload;
  if (entry.adapterSchemaVersion !== adapter.schemaVersion) {
    if (
      entry.adapterSchemaVersion > adapter.schemaVersion ||
      !adapter.migratePayload
    ) {
      throw new ProjectDraftRegistryError(
        'adapter-version-unsupported',
        `Draft adapter schema ${entry.adapterSchemaVersion} is not supported by ${adapter.adapterId}.`
      );
    }
    try {
      payload = adapter.migratePayload(
        payload,
        entry.adapterSchemaVersion,
        adapter.schemaVersion
      );
    } catch (error) {
      throw new ProjectDraftRegistryError(
        'migration-failed',
        'The project draft adapter could not migrate its stored payload.',
        { cause: error }
      );
    }
  }

  return {
    adapterId: adapter.adapterId,
    adapterSchemaVersion: adapter.schemaVersion,
    key,
    payload: parseProjectDraftPayload(adapter, payload),
    projectSourceRevisionFingerprint:
      entry.projectSourceRevisionFingerprint ?? null,
    storedAdapterSchemaVersion: entry.adapterSchemaVersion,
    updatedAtUtc: entry.updatedAtUtc,
    wasMigrated: entry.adapterSchemaVersion !== adapter.schemaVersion
  };
}

function inspectProjectDraftEntry(
  projectId: string,
  entry: WorkspaceDraftEntry
): ProjectDraftEntryInspection {
  return {
    adapterId: entry.adapterId,
    adapterSchemaVersion: entry.adapterSchemaVersion,
    identity: {
      domain: entry.key.domain,
      entityId: entry.key.entityId,
      game: entry.key.game,
      section: entry.key.section as WorkbenchSection
    },
    payloadBytes: new TextEncoder().encode(JSON.stringify(entry.payload)).byteLength,
    projectId,
    projectSourceRevisionFingerprint:
      entry.projectSourceRevisionFingerprint ?? null,
    updatedAtUtc: entry.updatedAtUtc
  };
}

function workspaceDraftEntryHasCanonicalScope(entry: WorkspaceDraftEntry) {
  if (!isRegisteredWorkbenchSection(entry.key.section)) {
    return false;
  }
  const registration = getWorkbenchCapabilityRegistration(entry.key.section);
  return (
    registration.domain === entry.key.domain &&
    isCapabilityRegisteredForGame(entry.key.section, entry.key.game)
  );
}

function compareWorkspaceDraftEntriesForInspection(
  left: WorkspaceDraftEntry,
  right: WorkspaceDraftEntry
) {
  const updatedAtComparison = compareOrdinal(right.updatedAtUtc, left.updatedAtUtc);
  if (updatedAtComparison !== 0) {
    return updatedAtComparison;
  }
  return compareOrdinal(JSON.stringify(left.key), JSON.stringify(right.key));
}

function validateReconciliation<TDraft, TResult>(
  resolution: ProjectDraftReconciliation<TDraft, TResult>
): void {
  if (
    typeof resolution !== 'object' ||
    resolution === null ||
    !('kind' in resolution) ||
    !['keep', 'delete', 'save'].includes(resolution.kind)
  ) {
    throw new Error('Project draft reconciliation returned an invalid resolution.');
  }
}

function toWorkspaceDraftKey(key: ProjectDraftKey): WorkspaceDraftKey {
  return {
    changeSetId: key.changeSetId,
    domain: key.domain,
    entityId: semanticRecordRefKey(key.entity),
    game: key.game,
    section: key.section
  };
}

function workspaceDraftKeysEqual(left: WorkspaceDraftKey, right: WorkspaceDraftKey) {
  return (
    left.changeSetId === right.changeSetId &&
    left.game === right.game &&
    left.domain === right.domain &&
    left.section === right.section &&
    left.entityId === right.entityId
  );
}

function workspaceDraftEntriesSemanticallyEqual(
  left: WorkspaceDraftEntry | undefined,
  right: WorkspaceDraftEntry | undefined
) {
  if (!left || !right) {
    return left === right;
  }

  return (
    workspaceDraftKeysEqual(left.key, right.key) &&
    left.adapterId === right.adapterId &&
    left.adapterSchemaVersion === right.adapterSchemaVersion &&
    (left.projectSourceRevisionFingerprint ?? null) ===
      (right.projectSourceRevisionFingerprint ?? null) &&
    left.updatedAtUtc === right.updatedAtUtc &&
    jsonValuesSemanticallyEqual(left.payload, right.payload)
  );
}

function jsonValuesSemanticallyEqual(left: JsonValue, right: JsonValue) {
  const pending: Array<[JsonValue, JsonValue]> = [[left, right]];
  while (pending.length > 0) {
    const [leftValue, rightValue] = pending.pop()!;
    if (Object.is(leftValue, rightValue)) {
      continue;
    }
    if (
      leftValue === null ||
      rightValue === null ||
      typeof leftValue !== typeof rightValue ||
      typeof leftValue !== 'object' ||
      typeof rightValue !== 'object'
    ) {
      return false;
    }

    if (Array.isArray(leftValue) || Array.isArray(rightValue)) {
      if (
        !Array.isArray(leftValue) ||
        !Array.isArray(rightValue) ||
        leftValue.length !== rightValue.length
      ) {
        return false;
      }
      for (let index = 0; index < leftValue.length; index += 1) {
        pending.push([leftValue[index]!, rightValue[index]!]);
      }
      continue;
    }

    const leftObject = leftValue as { [key: string]: JsonValue };
    const rightObject = rightValue as { [key: string]: JsonValue };
    const leftKeys = Object.keys(leftObject).sort(compareOrdinal);
    const rightKeys = Object.keys(rightObject).sort(compareOrdinal);
    if (
      leftKeys.length !== rightKeys.length ||
      leftKeys.some((key, index) => key !== rightKeys[index])
    ) {
      return false;
    }
    for (const key of leftKeys) {
      pending.push([leftObject[key]!, rightObject[key]!]);
    }
  }

  return true;
}

function compareOrdinal(left: string, right: string) {
  return left < right ? -1 : left > right ? 1 : 0;
}

function validateDraftKey(key: ProjectDraftKey) {
  validateBoundedIdentifier(key.projectId, 'project id');
  validateBoundedIdentifier(
    key.changeSetId,
    'change-set id',
    workspaceDraftMaximumStableIdLength
  );
  validateBoundedIdentifier(key.domain, 'draft domain');
  const registration = getWorkbenchCapabilityRegistration(key.section);
  if (!isCapabilityRegisteredForGame(key.section, key.game)) {
    throw new Error('Project draft section is not available for its game scope.');
  }
  if (registration.domain !== key.domain) {
    throw new Error('Project draft domain must match its canonical section registration.');
  }

  validateSemanticRecordRef(key.entity);
  if (
    key.entity.gameFamily !== projectGameToFamily(key.game) ||
    key.entity.domain !== key.domain
  ) {
    throw new Error('Project draft entity identity must match its game and domain scope.');
  }

  validateBoundedIdentifier(
    semanticRecordRefKey(key.entity),
    'draft entity id',
    workspaceDraftMaximumEntityIdLength
  );
}

function validateDraftAdapter<TDraft>(adapter: ProjectDraftAdapter<TDraft>) {
  validateBoundedIdentifier(adapter.adapterId, 'draft adapter id');
  if (!Number.isInteger(adapter.schemaVersion) || adapter.schemaVersion < 1) {
    throw new Error('Draft adapter schema versions must be positive integers.');
  }
}

function validateBoundedIdentifier(
  value: string,
  label: string,
  maximumLength = workspaceDraftMaximumIdentifierLength
) {
  if (
    value.trim().length === 0 ||
    value.trim() !== value ||
    value.length > maximumLength ||
    /[\u0000-\u001f\u007f-\u009f]/u.test(value)
  ) {
    throw new Error(`The ${label} must be a non-empty bounded identifier.`);
  }
}

function assertPayloadBound(payload: JsonValue, maxBytes: number) {
  const payloadBytes = new TextEncoder().encode(JSON.stringify(payload)).byteLength;
  if (payloadBytes > maxBytes) {
    throw new ProjectDraftRegistryError(
      'payload-size-limit',
      'Project draft exceeds the configured per-draft storage bound.'
    );
  }
}

function assertDocumentBound(document: WorkspaceDraftDocument) {
  const documentBytes = new TextEncoder().encode(JSON.stringify(document)).byteLength;
  if (documentBytes > workspaceDraftMaximumDocumentBytes) {
    throw new ProjectDraftRegistryError(
      'document-size-limit',
      'Project drafts exceed the configured project storage bound.'
    );
  }
}

function serializeProjectDraftPayload<TDraft>(
  adapter: ProjectDraftAdapter<TDraft>,
  payload: TDraft
) {
  try {
    return adapter.serializePayload(payload);
  } catch (error) {
    throw new ProjectDraftRegistryError(
      'payload-invalid',
      'The project draft payload could not be serialized.',
      { cause: error }
    );
  }
}

function parseProjectDraftPayload<TDraft>(
  adapter: ProjectDraftAdapter<TDraft>,
  payload: JsonValue
) {
  try {
    return adapter.parsePayload(payload);
  } catch (error) {
    throw new ProjectDraftRegistryError(
      'payload-invalid',
      'The project draft payload is not valid for its adapter.',
      { cause: error }
    );
  }
}

function assertPositiveInteger(value: number, label: string) {
  if (!Number.isFinite(value) || !Number.isInteger(value) || value <= 0) {
    throw new Error(`${label} must be a positive finite integer.`);
  }
}

function assertNonNegativeInteger(value: number, label: string) {
  if (!Number.isFinite(value) || !Number.isInteger(value) || value < 0) {
    throw new Error(`${label} must be a non-negative finite integer.`);
  }
}
