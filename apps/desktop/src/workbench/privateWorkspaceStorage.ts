/* SPDX-License-Identifier: GPL-3.0-only */

import type { WorkspaceDraftProjectBridgeApi } from '../bridge/workspaceDraftProjectBridge';
import { ProjectBridgeError } from '../bridge/projectBridgeError';
import {
  workspaceDraftDocumentSchema,
  type WorkspaceDraftDocument
} from '../bridge/workspaceDraftContracts';
import { projectBridgeErrorCodes } from '../errorCodes';

export type VersionedPrivateWorkspaceDocument = {
  schemaVersion: number;
};

export type PrivateWorkspaceStorage<TDocument extends VersionedPrivateWorkspaceDocument> = {
  delete: (projectId: string, expectedETag: string | null) => Promise<boolean>;
  read: (projectId: string) => Promise<PrivateWorkspaceSnapshot<TDocument>>;
  write: (
    projectId: string,
    document: TDocument,
    expectedETag: string | null
  ) => Promise<PrivateWorkspaceWriteResult>;
};

export type PrivateWorkspaceSnapshot<TDocument> = {
  document: TDocument | null;
  etag: string | null;
};

export type PrivateWorkspaceWriteResult = {
  etag: string;
};

export class PrivateWorkspaceConflictError extends Error {
  public constructor(options: ErrorOptions = {}) {
    super('The private workspace document changed concurrently.', options);
    this.name = 'PrivateWorkspaceConflictError';
  }
}

export type PrivateWorkspaceMigration = {
  fromVersion: number;
  migrate: (document: VersionedPrivateWorkspaceDocument) => unknown;
  toVersion: number;
};

export type PrivateWorkspaceDocumentDefinition<
  TDocument extends VersionedPrivateWorkspaceDocument
> = {
  currentVersion: number;
  migrations?: readonly PrivateWorkspaceMigration[];
  parse: (document: unknown) => TDocument;
};

export type BoundedMemoryWorkspaceStorageOptions = {
  maxBytes?: number;
  maxProjects?: number;
};

const defaultMaxWorkspaceBytes = 4 * 1024 * 1024;
const defaultMaxWorkspaceProjects = 16;

// Authored data never falls back to localStorage. This bounded adapter is safe for
// browser/dev contexts; the desktop bridge adapter below owns durable persistence.
export function createBoundedMemoryWorkspaceStorage<
  TDocument extends VersionedPrivateWorkspaceDocument
>(
  options: BoundedMemoryWorkspaceStorageOptions = {}
): PrivateWorkspaceStorage<TDocument> {
  const maxBytes = options.maxBytes ?? defaultMaxWorkspaceBytes;
  const maxProjects = options.maxProjects ?? defaultMaxWorkspaceProjects;
  assertPositiveInteger(maxBytes, 'maxBytes');
  assertPositiveInteger(maxProjects, 'maxProjects');
  const documents = new Map<string, { etag: string; serializedDocument: string }>();
  let storedBytes = 0;
  let etagCounter = 0;

  const touch = (
    projectId: string,
    entry: { etag: string; serializedDocument: string }
  ) => {
    documents.delete(projectId);
    documents.set(projectId, entry);
  };

  const createETag = () => {
    etagCounter += 1;
    if (!Number.isSafeInteger(etagCounter)) {
      etagCounter = 1;
    }
    return etagCounter.toString(16).padStart(64, '0');
  };

  const evictToBounds = () => {
    while (documents.size > maxProjects || storedBytes > maxBytes) {
      const oldestProjectId = documents.keys().next().value as string | undefined;
      if (oldestProjectId === undefined) {
        break;
      }

      const entry = documents.get(oldestProjectId);
      documents.delete(oldestProjectId);
      storedBytes -=
        workspaceStringBytes(oldestProjectId) +
        workspaceStringBytes(entry?.serializedDocument ?? '');
    }
  };

  return {
    delete: async (projectId, expectedETag) => {
      const entry = documents.get(projectId);
      assertExpectedETag(entry?.etag ?? null, expectedETag);
      if (entry === undefined) {
        return false;
      }

      documents.delete(projectId);
      storedBytes -=
        workspaceStringBytes(projectId) + workspaceStringBytes(entry.serializedDocument);
      return true;
    },
    read: async (projectId) => {
      const entry = documents.get(projectId);
      if (entry === undefined) {
        return { document: null, etag: null };
      }

      touch(projectId, entry);
      return {
        document: JSON.parse(entry.serializedDocument) as TDocument,
        etag: entry.etag
      };
    },
    write: async (projectId, document, expectedETag) => {
      const serializedDocument = JSON.stringify(document);
      const entryBytes = workspaceStringBytes(projectId) + workspaceStringBytes(serializedDocument);
      if (entryBytes > maxBytes) {
        throw new Error('Private workspace document exceeds the in-memory storage bound.');
      }

      const currentEntry = documents.get(projectId);
      assertExpectedETag(currentEntry?.etag ?? null, expectedETag);
      if (currentEntry !== undefined) {
        storedBytes -=
          workspaceStringBytes(projectId) +
          workspaceStringBytes(currentEntry.serializedDocument);
      }
      const etag = createETag();
      touch(projectId, { etag, serializedDocument });
      storedBytes += entryBytes;
      evictToBounds();
      return { etag };
    }
  };
}

export function createWorkspaceDraftPrivateStorage(
  bridge: WorkspaceDraftProjectBridgeApi
): PrivateWorkspaceStorage<WorkspaceDraftDocument> {
  return {
    delete: async (projectId, expectedETag) => {
      try {
        const response = await bridge.deleteWorkspaceDrafts({ expectedETag, projectId });
        return response.deleted;
      } catch (error) {
        throw mapWorkspaceStorageError(error);
      }
    },
    read: async (projectId) => {
      const response = await bridge.readWorkspaceDrafts({ projectId });
      return { document: response.document, etag: response.etag };
    },
    write: async (projectId, document, expectedETag) => {
      workspaceDraftDocumentSchema.parse(document);
      try {
        const response = await bridge.writeWorkspaceDrafts({
          document,
          expectedETag,
          projectId
        });
        return { etag: response.etag };
      } catch (error) {
        throw mapWorkspaceStorageError(error);
      }
    }
  };
}

function assertExpectedETag(actualETag: string | null, expectedETag: string | null) {
  if (actualETag !== expectedETag) {
    throw new PrivateWorkspaceConflictError();
  }
}

function mapWorkspaceStorageError(error: unknown) {
  if (
    error instanceof ProjectBridgeError &&
    error.semanticCode === projectBridgeErrorCodes.workspaceConcurrentModification
  ) {
    return new PrivateWorkspaceConflictError({ cause: error });
  }
  return error;
}

export function migratePrivateWorkspaceDocument<
  TDocument extends VersionedPrivateWorkspaceDocument
>(
  document: unknown,
  definition: PrivateWorkspaceDocumentDefinition<TDocument>
) {
  assertPositiveInteger(definition.currentVersion, 'currentVersion');
  let currentDocument = readVersionedDocument(document);
  const migrationDefinitions = definition.migrations ?? [];
  if (migrationDefinitions.length > 64) {
    throw new Error('A private workspace definition cannot contain more than 64 migrations.');
  }

  const migrations = new Map<number, PrivateWorkspaceMigration>();
  for (const migration of migrationDefinitions) {
    assertPositiveInteger(migration.fromVersion, 'migration.fromVersion');
    assertPositiveInteger(migration.toVersion, 'migration.toVersion');
    if (
      migration.toVersion <= migration.fromVersion ||
      migration.toVersion > definition.currentVersion
    ) {
      throw new Error('Private workspace migrations must advance toward the current version.');
    }
    if (migrations.has(migration.fromVersion)) {
      throw new Error(
        `Private workspace schema version ${migration.fromVersion} has multiple migrations.`
      );
    }
    migrations.set(migration.fromVersion, migration);
  }
  const visitedVersions = new Set<number>();

  while (currentDocument.schemaVersion !== definition.currentVersion) {
    if (
      currentDocument.schemaVersion > definition.currentVersion ||
      visitedVersions.has(currentDocument.schemaVersion)
    ) {
      throw new Error(`Unsupported private workspace schema version ${currentDocument.schemaVersion}.`);
    }

    visitedVersions.add(currentDocument.schemaVersion);
    const migration = migrations.get(currentDocument.schemaVersion);
    if (!migration || migration.toVersion <= migration.fromVersion) {
      throw new Error(
        `No safe private workspace migration exists for schema version ${currentDocument.schemaVersion}.`
      );
    }

    const migratedDocument = readVersionedDocument(migration.migrate(currentDocument));
    const migratedVersion = migratedDocument.schemaVersion;
    if (migratedVersion !== migration.toVersion) {
      throw new Error(
        `Private workspace migration from schema version ${migration.fromVersion} ` +
          `produced version ${migratedVersion} instead of ${migration.toVersion}.`
      );
    }
    currentDocument = migratedDocument;
  }

  const parsedDocument = definition.parse(currentDocument);
  if (readVersionedDocument(parsedDocument).schemaVersion !== definition.currentVersion) {
    throw new Error('The parsed private workspace document has an unexpected schema version.');
  }
  return parsedDocument;
}

function readVersionedDocument(document: unknown): VersionedPrivateWorkspaceDocument {
  if (
    typeof document !== 'object' ||
    document === null ||
    !('schemaVersion' in document) ||
    typeof document.schemaVersion !== 'number' ||
    !Number.isSafeInteger(document.schemaVersion) ||
    document.schemaVersion < 1
  ) {
    throw new Error('Private workspace document is missing a valid schema version.');
  }

  return document as VersionedPrivateWorkspaceDocument;
}

function workspaceStringBytes(value: string) {
  return new TextEncoder().encode(value).byteLength;
}

function assertPositiveInteger(value: number, name: string) {
  if (!Number.isFinite(value) || !Number.isInteger(value) || value <= 0) {
    throw new Error(`${name} must be a positive finite integer.`);
  }
}
