/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import { kmErrorCodeSchema } from '../errorCodes';
import {
  apiDiagnosticSchema,
  outputTransactionResultSchema,
  projectHealthSchema,
  projectPathRoleSchema,
  projectPathStatusSchema,
  projectPathsSchema
} from './contracts';

export const outputSafetyMaximumReturnedEntries = 500;
export const outputSafetyMaximumCleanupTargets = 512;
export const outputSafetyMaximumHistoryReceipts = 100;
export const outputSafetyMaximumCheckpoints = 64;
export const outputSafetyMaximumRecoveryTransactions = 1_024;
export const outputSafetyMaximumReturnedRecoveryUnknownTargets = 500;
export const outputSafetyMaximumRecoveryUnknownTargetUtf8Bytes = 2 * 1024 * 1024;
export const outputSafetyMaximumRelativePathLength = 4_096;

const boundedIdentifierSchema = z
  .string()
  .min(1)
  .max(256)
  .refine((value) => value.trim() === value, {
    message: 'Identifiers cannot have surrounding whitespace.'
  })
  .refine((value) => !/[\u0000-\u001f\u007f-\u009f]/u.test(value), {
    message: 'Identifiers cannot contain control characters.'
  });
const boundedStableIdSchema = z
  .string()
  .min(1)
  .max(1_024)
  .refine((value) => value.trim() === value, {
    message: 'Stable IDs cannot have surrounding whitespace.'
  })
  .refine((value) => !/[\u0000-\u001f\u007f-\u009f]/u.test(value), {
    message: 'Stable IDs cannot contain control characters.'
  });
const contractKeySchema = z
  .string()
  .min(1)
  .max(256)
  .regex(/^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$/u);
const sha256Schema = z.string().regex(/^[a-f0-9]{64}$/u);
const transactionIdSchema = z.string().regex(/^[a-f0-9]{32}$/u);
const projectIdSchema = z
  .string()
  .min(1)
  .max(128)
  .refine((value) => value.trim() === value, {
    message: 'Project IDs cannot have surrounding whitespace.'
  })
  .refine((value) => !/[\u0000-\u001f\u007f-\u009f]/u.test(value), {
    message: 'Project IDs cannot contain control characters.'
  });
const checkpointLabelSchema = z
  .string()
  .min(1)
  .max(256)
  .refine((value) => value.trim() === value, {
    message: 'Checkpoint labels cannot have surrounding whitespace.'
  })
  .refine((value) => !/[\u0000-\u001f\u007f-\u009f]/u.test(value), {
    message: 'Checkpoint labels cannot contain control characters.'
  });
const decimalByteCountSchema = z.string().regex(/^(?:0|[1-9]\d*)$/u);
const dateTimeOffsetSchema = z.string().refine(
  (value) =>
    /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/u.test(value) &&
    Number.isFinite(Date.parse(value)),
  { message: 'Expected an ISO 8601 timestamp with an offset.' }
);
const relativeOutputPathSchema = z
  .string()
  .min(1)
  .max(outputSafetyMaximumRelativePathLength)
  .refine((value) => value.trim() === value, {
    message: 'Relative output paths cannot have surrounding whitespace.'
  })
  .refine(
    (value) =>
      !value.startsWith('/') &&
      !value.includes('\\') &&
      !value.includes(':') &&
      !/[\u0000-\u001f\u007f-\u009f]/u.test(value) &&
      value.split('/').every((segment) => segment.length > 0 && segment !== '.' && segment !== '..'),
    { message: 'Expected a normalized safe relative output path.' }
  );

export const outputSafetyScopeSchema = z.strictObject({
  paths: projectPathsSchema,
  projectId: projectIdSchema
});

export const outputTransactionPhaseSchema = z.enum([
  'preparing',
  'prepared',
  'committing',
  'committed',
  'rollingBack',
  'rolledBack',
  'recoveryRequired',
  'finalizing'
]);
export const outputRecoveryDispositionSchema = z.enum([
  'noAction',
  'finalizeCommit',
  'rollBack',
  'recoveryRequired'
]);
export const outputRecoveryTransactionSchema = z.strictObject({
  disposition: outputRecoveryDispositionSchema,
  journalReadable: z.boolean(),
  phase: outputTransactionPhaseSchema,
  transactionId: transactionIdSchema,
  unknownTargetCount: z.number().int().min(0).max(outputSafetyMaximumRecoveryTransactions),
  unknownTargets: z
    .array(relativeOutputPathSchema)
    .max(outputSafetyMaximumReturnedEntries),
  unknownTargetsTruncated: z.boolean()
}).superRefine((value, context) => {
  if (value.unknownTargetCount < value.unknownTargets.length) {
    context.addIssue({
      code: 'custom',
      message: 'Unknown target count cannot be smaller than the returned list.',
      path: ['unknownTargetCount']
    });
  }
  if (value.unknownTargetsTruncated !== (value.unknownTargetCount > value.unknownTargets.length)) {
    context.addIssue({
      code: 'custom',
      message: 'Unknown target truncation must match the returned list.',
      path: ['unknownTargetsTruncated']
    });
  }
});
export const outputRecoveryStatusSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema).max(256),
  pendingReconciliationCount: z
    .number()
    .int()
    .min(0)
    .max(outputSafetyMaximumRecoveryTransactions),
  requiresRecovery: z.boolean(),
  revision: sha256Schema,
  transactionCount: z.number().int().min(0).max(outputSafetyMaximumRecoveryTransactions),
  transactions: z
    .array(outputRecoveryTransactionSchema)
    .max(outputSafetyMaximumReturnedEntries),
  transactionsTruncated: z.boolean()
}).superRefine((value, context) => {
  if (value.transactionCount < value.transactions.length) {
    context.addIssue({
      code: 'custom',
      message: 'Transaction count cannot be smaller than the returned list.',
      path: ['transactionCount']
    });
  }
  if (value.pendingReconciliationCount > value.transactionCount) {
    context.addIssue({
      code: 'custom',
      message: 'Pending reconciliation count cannot exceed the transaction count.',
      path: ['pendingReconciliationCount']
    });
  }
  if (value.transactionsTruncated !== (value.transactionCount > value.transactions.length)) {
    context.addIssue({
      code: 'custom',
      message: 'Transaction truncation must match the returned list.',
      path: ['transactionsTruncated']
    });
  }
  const returnedUnknownTargets = value.transactions.reduce(
    (count, transaction) => count + transaction.unknownTargets.length,
    0
  );
  if (returnedUnknownTargets > outputSafetyMaximumReturnedRecoveryUnknownTargets) {
    context.addIssue({
      code: 'custom',
      message: 'The recovery response returned too many unknown targets.',
      path: ['transactions']
    });
  }
  let returnedUnknownTargetUtf8Bytes = 0;
  for (const transaction of value.transactions) {
    for (const target of transaction.unknownTargets) {
      returnedUnknownTargetUtf8Bytes += new TextEncoder().encode(target).byteLength;
    }
  }
  if (returnedUnknownTargetUtf8Bytes > outputSafetyMaximumRecoveryUnknownTargetUtf8Bytes) {
    context.addIssue({
      code: 'custom',
      message: 'The recovery response exceeded the unknown-target text budget.',
      path: ['transactions']
    });
  }
});

export const getOutputRecoveryStatusRequestSchema = z.strictObject({
  scope: outputSafetyScopeSchema
});
export const getOutputRecoveryStatusResponseSchema = z.strictObject({
  status: outputRecoveryStatusSchema
});
export const reconcileOutputRecoveryRequestSchema = z.strictObject({
  expectedRevision: sha256Schema,
  scope: outputSafetyScopeSchema
});
export const reconcileOutputRecoveryResponseSchema = z.strictObject({
  reconciledCount: z.number().int().nonnegative(),
  status: outputRecoveryStatusSchema
});

export const outputIntegrityClassificationSchema = z.enum([
  'baseEquivalent',
  'kmOwnedCurrent',
  'kmOwnedStale',
  'foreign',
  'conflicted',
  'interrupted',
  'unknown'
]);
export const outputIntegrityEntrySchema = z.strictObject({
  classification: outputIntegrityClassificationSchema,
  cleanupEligible: z.boolean(),
  ownerIds: z.array(contractKeySchema).max(256),
  relativePath: relativeOutputPathSchema,
  sizeBytes: decimalByteCountSchema.nullable(),
  targetId: sha256Schema
});
export const outputIntegrityCountsSchema = z.strictObject({
  baseEquivalent: z.number().int().nonnegative(),
  conflicted: z.number().int().nonnegative(),
  foreign: z.number().int().nonnegative(),
  interrupted: z.number().int().nonnegative(),
  kmOwnedCurrent: z.number().int().nonnegative(),
  kmOwnedStale: z.number().int().nonnegative(),
  unknown: z.number().int().nonnegative()
});
export const scanOutputIntegrityRequestSchema = z.strictObject({
  scope: outputSafetyScopeSchema
});
export const scanOutputIntegrityResponseSchema = z.strictObject({
  counts: outputIntegrityCountsSchema,
  diagnostics: z.array(apiDiagnosticSchema).max(256),
  entries: z.array(outputIntegrityEntrySchema).max(outputSafetyMaximumReturnedEntries),
  revision: sha256Schema,
  scanId: transactionIdSchema,
  scannedAtUtc: dateTimeOffsetSchema,
  truncated: z.boolean()
});

export const previewOutputCleanupRequestSchema = z.strictObject({
  integrityRevision: sha256Schema,
  scanId: transactionIdSchema,
  scope: outputSafetyScopeSchema,
  targetIds: z
    .array(sha256Schema)
    .min(1)
    .max(outputSafetyMaximumCleanupTargets)
});
export const outputCleanupCandidateSchema = z.strictObject({
  relativePath: relativeOutputPathSchema,
  sizeBytes: decimalByteCountSchema.nullable(),
  targetId: sha256Schema
});
export const previewOutputCleanupResponseSchema = z.strictObject({
  candidates: z
    .array(outputCleanupCandidateSchema)
    .max(outputSafetyMaximumCleanupTargets),
  createdAtUtc: dateTimeOffsetSchema,
  diagnostics: z.array(apiDiagnosticSchema).max(256),
  expectedRevision: sha256Schema,
  planId: transactionIdSchema,
  totalBytes: decimalByteCountSchema
});
export const applyOutputCleanupRequestSchema = z.strictObject({
  expectedRevision: sha256Schema,
  planId: transactionIdSchema,
  scope: outputSafetyScopeSchema
});
export const outputCleanupDispositionSchema = z.enum([
  'removed',
  'notOwned',
  'fingerprintMismatch',
  'missing',
  'applyNotCommitted',
  'forgotMissing'
]);
export const applyOutputCleanupResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema).max(256),
  entries: z
    .array(
      z.strictObject({
        disposition: outputCleanupDispositionSchema,
        relativePath: relativeOutputPathSchema,
        targetId: sha256Schema
      })
    )
    .max(outputSafetyMaximumCleanupTargets),
  outputTransaction: outputTransactionResultSchema.nullable(),
  removedCount: z.number().int().nonnegative(),
  skippedCount: z.number().int().nonnegative()
});

export const listOutputHistoryRequestSchema = z.strictObject({
  cursor: transactionIdSchema.nullable(),
  limit: z.number().int().min(1).max(outputSafetyMaximumHistoryReceipts),
  scope: outputSafetyScopeSchema
});
export const outputApplyOriginSchema = z.strictObject({
  id: boundedStableIdSchema,
  kind: boundedIdentifierSchema
});
export const outputHistoryReceiptSchema = z.strictObject({
  completedAtUtc: dateTimeOffsetSchema,
  origins: z.array(outputApplyOriginSchema).max(64),
  outcome: z.enum(['committed', 'rolledBack', 'recoveryRequired']),
  outcomeCode: kmErrorCodeSchema.nullable(),
  outputMode: boundedIdentifierSchema,
  semanticReviewHash: sha256Schema,
  targetCount: z.number().int().nonnegative(),
  transactionId: transactionIdSchema
});
export const listOutputHistoryResponseSchema = z.strictObject({
  nextCursor: transactionIdSchema.nullable(),
  receipts: z
    .array(outputHistoryReceiptSchema)
    .max(outputSafetyMaximumHistoryReceipts),
  truncated: z.boolean()
});

export const outputCheckpointCoverageSchema = z.enum(['fullOutput', 'kmOwnedOnly']);
export const outputCheckpointSchema = z.strictObject({
  checkpointId: transactionIdSchema,
  coverage: outputCheckpointCoverageSchema,
  createdAtUtc: dateTimeOffsetSchema,
  fileCount: z.number().int().nonnegative(),
  label: checkpointLabelSchema.nullable(),
  manifestFingerprint: sha256Schema,
  outputMode: boundedIdentifierSchema,
  totalBytes: decimalByteCountSchema
});
export const listOutputCheckpointsRequestSchema = z.strictObject({
  scope: outputSafetyScopeSchema
});
export const listOutputCheckpointsResponseSchema = z.strictObject({
  checkpoints: z.array(outputCheckpointSchema).max(outputSafetyMaximumCheckpoints),
  outputRevision: sha256Schema,
  revision: sha256Schema
});
export const createOutputCheckpointRequestSchema = z.strictObject({
  expectedOutputRevision: sha256Schema,
  label: checkpointLabelSchema.nullable(),
  scope: outputSafetyScopeSchema
});
export const createOutputCheckpointResponseSchema = z.strictObject({
  checkpoint: outputCheckpointSchema,
  checkpoints: z.array(outputCheckpointSchema).max(outputSafetyMaximumCheckpoints),
  outputRevision: sha256Schema,
  revision: sha256Schema
});
export const previewOutputCheckpointRestoreRequestSchema = z.strictObject({
  checkpointId: transactionIdSchema,
  manifestFingerprint: sha256Schema,
  scope: outputSafetyScopeSchema
});
export const previewOutputCheckpointRestoreResponseSchema = z.strictObject({
  canRestore: z.boolean(),
  diagnostics: z.array(apiDiagnosticSchema).max(256),
  planId: transactionIdSchema,
  targetCount: z.number().int().nonnegative(),
  targets: z.array(relativeOutputPathSchema).max(outputSafetyMaximumReturnedEntries),
  totalBytes: decimalByteCountSchema
});
export const restoreOutputCheckpointRequestSchema = z.strictObject({
  planId: transactionIdSchema,
  scope: outputSafetyScopeSchema
});
export const restoreOutputCheckpointResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema).max(256),
  outputTransaction: outputTransactionResultSchema
});
export const deleteOutputCheckpointRequestSchema = z.strictObject({
  checkpointId: transactionIdSchema,
  expectedRevision: sha256Schema,
  manifestFingerprint: sha256Schema,
  scope: outputSafetyScopeSchema
});
export const deleteOutputCheckpointResponseSchema = z.strictObject({
  deleted: z.boolean(),
  revision: sha256Schema
});

export const projectRelocationDocumentStatusSchema = z.enum(['copy', 'skip', 'conflict']);
export const previewProjectRelocationRequestSchema = z.strictObject({
  candidatePaths: projectPathsSchema,
  source: outputSafetyScopeSchema
});
export const previewProjectRelocationResponseSchema = z.strictObject({
  canApply: z.boolean(),
  destinationProjectId: projectIdSchema.nullable(),
  diagnostics: z.array(apiDiagnosticSchema).max(256),
  reviewToken: sha256Schema,
  roles: z
    .array(
      z.strictObject({
        role: projectPathRoleSchema,
        status: projectPathStatusSchema
      })
    )
    .max(6),
  sourceProjectId: projectIdSchema,
  workspaceDocuments: z
    .array(
      z.strictObject({
        documentId: boundedIdentifierSchema,
        status: projectRelocationDocumentStatusSchema
      })
    )
    .max(64)
});
export const applyProjectRelocationRequestSchema = z.strictObject({
  candidatePaths: projectPathsSchema,
  reviewToken: sha256Schema,
  source: outputSafetyScopeSchema
});
export const applyProjectRelocationResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema).max(256),
  health: projectHealthSchema,
  migratedDocumentIds: z.array(boundedIdentifierSchema).max(64),
  projectId: projectIdSchema
});

export const buildSupportReportRequestSchema = z.strictObject({
  scope: outputSafetyScopeSchema
});
export const outputSupportReportSchema = z.strictObject({
  applicationVersion: z.string().min(1).max(128),
  checkpointCount: z.number().int().nonnegative(),
  createdAtUtc: dateTimeOffsetSchema,
  diagnosticCodes: z.array(kmErrorCodeSchema).max(256),
  gameFamily: z.enum(['swordShield', 'scarletViolet', 'pokemonLegendsZA']),
  historyReceiptCount: z.number().int().nonnegative(),
  integrityCounts: z
    .array(
      z.strictObject({
        classification: outputIntegrityClassificationSchema,
        count: z.number().int().nonnegative()
      })
    )
    .max(7),
  outputMode: boundedIdentifierSchema,
  ownershipFileCount: z.number().int().nonnegative(),
  schemaVersion: z.literal(1),
  transactionPhases: z.array(outputTransactionPhaseSchema).max(8)
});
export const buildSupportReportResponseSchema = z.strictObject({
  report: outputSupportReportSchema
});

export type OutputSafetyScope = z.infer<typeof outputSafetyScopeSchema>;
export type OutputRecoveryStatus = z.infer<typeof outputRecoveryStatusSchema>;
export type GetOutputRecoveryStatusRequest = z.infer<typeof getOutputRecoveryStatusRequestSchema>;
export type GetOutputRecoveryStatusResponse = z.infer<typeof getOutputRecoveryStatusResponseSchema>;
export type ReconcileOutputRecoveryRequest = z.infer<typeof reconcileOutputRecoveryRequestSchema>;
export type ReconcileOutputRecoveryResponse = z.infer<typeof reconcileOutputRecoveryResponseSchema>;
export type OutputIntegrityClassification = z.infer<typeof outputIntegrityClassificationSchema>;
export type OutputIntegrityEntry = z.infer<typeof outputIntegrityEntrySchema>;
export type ScanOutputIntegrityRequest = z.infer<typeof scanOutputIntegrityRequestSchema>;
export type ScanOutputIntegrityResponse = z.infer<typeof scanOutputIntegrityResponseSchema>;
export type PreviewOutputCleanupRequest = z.infer<typeof previewOutputCleanupRequestSchema>;
export type PreviewOutputCleanupResponse = z.infer<typeof previewOutputCleanupResponseSchema>;
export type ApplyOutputCleanupRequest = z.infer<typeof applyOutputCleanupRequestSchema>;
export type ApplyOutputCleanupResponse = z.infer<typeof applyOutputCleanupResponseSchema>;
export type ListOutputHistoryRequest = z.infer<typeof listOutputHistoryRequestSchema>;
export type ListOutputHistoryResponse = z.infer<typeof listOutputHistoryResponseSchema>;
export type OutputHistoryReceipt = z.infer<typeof outputHistoryReceiptSchema>;
export type ListOutputCheckpointsRequest = z.infer<typeof listOutputCheckpointsRequestSchema>;
export type ListOutputCheckpointsResponse = z.infer<typeof listOutputCheckpointsResponseSchema>;
export type OutputCheckpoint = z.infer<typeof outputCheckpointSchema>;
export type CreateOutputCheckpointRequest = z.infer<typeof createOutputCheckpointRequestSchema>;
export type CreateOutputCheckpointResponse = z.infer<typeof createOutputCheckpointResponseSchema>;
export type PreviewOutputCheckpointRestoreRequest = z.infer<typeof previewOutputCheckpointRestoreRequestSchema>;
export type PreviewOutputCheckpointRestoreResponse = z.infer<typeof previewOutputCheckpointRestoreResponseSchema>;
export type RestoreOutputCheckpointRequest = z.infer<typeof restoreOutputCheckpointRequestSchema>;
export type RestoreOutputCheckpointResponse = z.infer<typeof restoreOutputCheckpointResponseSchema>;
export type DeleteOutputCheckpointRequest = z.infer<typeof deleteOutputCheckpointRequestSchema>;
export type DeleteOutputCheckpointResponse = z.infer<typeof deleteOutputCheckpointResponseSchema>;
export type PreviewProjectRelocationRequest = z.infer<typeof previewProjectRelocationRequestSchema>;
export type PreviewProjectRelocationResponse = z.infer<typeof previewProjectRelocationResponseSchema>;
export type ApplyProjectRelocationRequest = z.infer<typeof applyProjectRelocationRequestSchema>;
export type ApplyProjectRelocationResponse = z.infer<typeof applyProjectRelocationResponseSchema>;
export type BuildSupportReportRequest = z.infer<typeof buildSupportReportRequestSchema>;
export type BuildSupportReportResponse = z.infer<typeof buildSupportReportResponseSchema>;
export type OutputSupportReport = z.infer<typeof outputSupportReportSchema>;
