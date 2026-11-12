/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';

const count = z.number().int().nonnegative().max(Number.MAX_SAFE_INTEGER);
const owner = z.enum(['Unknown', 'CurrentUser', 'Administrators', 'System', 'Other']);
export const outputAttemptDiagnosticSchema = z.strictObject({
  sequence: count,
  stage: z.enum(['ValidatePaths', 'CreateWorkspace', 'AcquireWorkspaceLock', 'ClaimLegacyMetadata', 'AcquireLegacyLock', 'CreateGameWorkspace', 'ResumePublication', 'MigrateLegacy', 'ValidateStore', 'CheckOtherGames', 'PrepareWorkingTransactions', 'Ready']),
  lastCompletedStage: z.enum(['ValidatePaths', 'CreateWorkspace', 'AcquireWorkspaceLock', 'ClaimLegacyMetadata', 'AcquireLegacyLock', 'CreateGameWorkspace', 'ResumePublication', 'MigrateLegacy', 'ValidateStore', 'CheckOtherGames', 'PrepareWorkingTransactions', 'Ready']).nullable(),
  objectRole: z.enum(['OutputRoot', 'WorkspaceDirectory', 'WorkspaceLock', 'LegacyMetadataDirectory', 'LegacyLock', 'GameWorkspaceDirectory', 'MetadataStore', 'MetadataDirectory', 'MetadataFile', 'MarkerTemporaryFile', 'WorkingTransactions']),
  elapsedMs: count, lockRetries: count, outcome: z.enum(['success', 'failure']),
  securityFailure: z.enum(['CurrentWindowsAccountUnavailable', 'OwnerMismatch', 'PermissionsMismatch', 'DirectoryPermissionsUnavailable', 'DirectoryCreationUnavailable', 'FilePermissionsUnavailable', 'WorkspaceStorageUnsafe']).nullable(),
  migrationFailure: z.enum(['PreviousFailureUnavailable', 'PortableLegacyOwnership', 'OtherGameTransactions', 'PreservedCopyMissing', 'PublishedStoreMissing', 'MetadataChanged', 'PreservedCopyMismatch', 'InvalidHistory', 'InvalidJson', 'InvalidMetadata', 'IncompatibleGame', 'ConflictingTransactions', 'OutputStateMismatch', 'UnboundWorkingMetadata', 'WorkingBindingMismatch', 'MetadataDocumentTooLarge', 'TransactionMismatch', 'UnexpectedTransactionFiles', 'MetadataDepthLimit', 'MetadataEntryLimit', 'MetadataSizeLimit', 'DestinationExists', 'CopyVerificationFailed']).nullable(),
  errorKind: z.enum(['none', 'security', 'migration', 'lock-timeout', 'access', 'io', 'native', 'cancelled', 'unsafe-path', 'other']),
  hResult: z.string().regex(/^0x[0-9A-F]{8}$/).nullable(), nativeError: z.number().int().nullable(),
  objectOwner: owner, tokenOwner: owner, elevated: z.boolean().nullable(), aclProtected: z.boolean().nullable(), aclRuleCount: count.nullable(),
  storage: z.array(z.strictObject({
    role: z.enum(['output', 'workspace']), status: z.enum(['available', 'partial', 'unavailable', 'unsupported']),
    fileSystem: z.enum(['ntfs', 'refs', 'fat', 'fat32', 'exfat', 'other', 'unknown']),
    persistentAcls: z.boolean().nullable(), freeBytes: count.nullable(), nativeError: z.number().int().nullable()
  })).max(2)
});

export const supportDiagnosticSnapshotSchema = z.strictObject({
  schemaVersion: z.literal(1), backendVersion: z.string().regex(/^\d+(?:\.\d+){1,3}$/),
  backendCommit: z.string().regex(/^[a-f0-9]{40}$/).nullable(), attemptCount: count,
  attempts: z.array(outputAttemptDiagnosticSchema).max(8)
});
