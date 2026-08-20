/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import {
  apiDiagnosticSchema,
  changePlanOutputModeSchema,
  changePlanSchema,
  editSessionSchema,
  pendingEditSchema,
  projectFileReferenceSchema,
  projectGameSchema,
  projectPathsSchema
} from './contracts';
import { workspaceProjectIdSchema } from './workspacePersonalStateContracts';

export const changeSetSchemaVersion = 1 as const;
export const changeSetAssociationVersion = 1 as const;
export const portableChangeSetSchemaVersion = 1 as const;
export const portablePendingEditAdapterId = 'pending-edit.v1' as const;
export const portablePendingEditAdapterSchemaVersion = 1 as const;
export const changeSetMaximumCount = 64;
export const changeSetMaximumOperationCount = 256;
export const changeSetMaximumOperationsPerSet = 128;
export const changeSetMaximumBuildVariantCount = 32;
export const changeSetMaximumHistoryCount = 16;
export const changeSetMaximumTagCount = 32;
export const changeSetMaximumDependencyCount = 32;
export const changeSetMaximumSerializedDocumentBytes = 3 * 1024 * 1024;
export const changeSetMaximumPortablePackageBytes = 2 * 1024 * 1024;

const stableIdSchema = z
  .string()
  .min(1)
  .max(128)
  .regex(/^[A-Za-z0-9][A-Za-z0-9._-]*$/u);
const sha256FingerprintSchema = z.string().regex(/^[A-Fa-f0-9]{64}$/u);
const dateTimeOffsetSchema = z.string().refine(
  (value) => (
    /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/u.test(value) &&
    Number.isFinite(Date.parse(value))
  ),
  { message: 'Expected an ISO 8601 timestamp with an offset.' }
);
const displayNameSchema = z
  .string()
  .min(1)
  .max(128)
  .refine((value) => value.trim() === value, { message: 'Expected trimmed display text.' });
const notesSchema = z.string().max(32 * 1024).nullable();
const tagSchema = z
  .string()
  .min(1)
  .max(64)
  .refine((value) => value.trim() === value, { message: 'Expected trimmed tag text.' });
const distinctStableIdsSchema = (maximum: number) => z
  .array(stableIdSchema)
  .max(maximum)
  .refine((values) => new Set(values).size === values.length, {
    message: 'Expected unique stable identifiers.'
  });
const utf8BoundedStringSchema = (maximumBytes: number) => z.string().refine(
  (value) => new TextEncoder().encode(value).byteLength <= maximumBytes,
  { message: `Expected no more than ${maximumBytes} UTF-8 bytes.` }
);
const windowsReservedDeviceAlias = /^(?:CON|PRN|AUX|NUL|CLOCK\$|CONIN\$|CONOUT\$|(?:COM|LPT)[1-9¹²³])(?:\.|$)/iu;
const canonicalRelativeOutputPathSchema = z
  .string()
  .min(1)
  .max(4096)
  .refine((value) => value.trim() === value && value.normalize('NFC') === value, {
    message: 'Expected a trimmed, Unicode-normalized relative output path.'
  })
  .refine(
    (value) => (
      !value.startsWith('/') &&
      !value.includes('\\') &&
      !/[":<>|?*\p{Cc}]/u.test(value) &&
      value.split('/').every(
        (segment) => segment.length > 0 &&
          segment.length <= 255 &&
          segment !== '.' &&
          segment !== '..' &&
          !segment.endsWith('.') &&
          !segment.endsWith(' ') &&
          !windowsReservedDeviceAlias.test(segment)
      )
    ),
    { message: 'Expected a canonical safe relative output path.' }
  );
const canonicalRelativeOutputPathKey = (value: string) => value.normalize('NFC').toUpperCase();
const portableCanonicalIntegerSchema = z
  .string()
  .min(1)
  .max(20)
  .refine((value) => {
    try {
      const parsed = BigInt(value);
      return parsed >= -9223372036854775808n &&
        parsed <= 9223372036854775807n &&
        parsed.toString() === value;
    } catch {
      return false;
    }
  }, { message: 'Expected a canonical signed 64-bit integer.' });

export const changeSetWorkspaceScopeSchema = z.strictObject({
  paths: projectPathsSchema,
  projectId: workspaceProjectIdSchema
});

export const changeSetOperationStorageKindSchema = z.literal('legacyPendingEdit');
export const changeSetSourceBindingKindSchema = z.enum(['reviewedPlan', 'legacyUnsupported']);

export const changeSetOperationSchema = z
  .strictObject({
    createdAtUtc: dateTimeOffsetSchema,
    kind: changeSetOperationStorageKindSchema,
    operationId: stableIdSchema,
    ownedTargets: z.array(canonicalRelativeOutputPathSchema).max(64),
    pendingEdit: pendingEditSchema,
    sourceBindingKind: changeSetSourceBindingKindSchema,
    sourceFingerprint: sha256FingerprintSchema.nullable(),
    updatedAtUtc: dateTimeOffsetSchema
  })
  .superRefine((operation, context) => {
    if (
      operation.pendingEdit.association?.operationId !== operation.operationId ||
      operation.pendingEdit.association?.version !== changeSetAssociationVersion
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The pending edit association must identify this operation.',
        path: ['pendingEdit', 'association']
      });
    }
    if (
      (operation.sourceBindingKind === 'reviewedPlan') !==
      (operation.sourceFingerprint !== null)
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The source fingerprint does not match the source binding kind.',
        path: ['sourceFingerprint']
      });
    }
    if (
      new Set(operation.ownedTargets.map(canonicalRelativeOutputPathKey)).size !==
      operation.ownedTargets.length
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'Owned output targets must be unique.',
        path: ['ownedTargets']
      });
    }
    if (
      (operation.sourceBindingKind === 'reviewedPlan' && operation.ownedTargets.length === 0) ||
      (operation.sourceBindingKind === 'legacyUnsupported' && operation.ownedTargets.length !== 0)
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'Owned output targets do not match the source binding kind.',
        path: ['ownedTargets']
      });
    }
  });

export const namedChangeSetSchema = z
  .strictObject({
    archived: z.boolean(),
    changeSetId: stableIdSchema,
    createdAtUtc: dateTimeOffsetSchema,
    dependencyIds: distinctStableIdsSchema(changeSetMaximumDependencyCount),
    enabled: z.boolean(),
    name: displayNameSchema,
    notes: notesSchema,
    operations: z.array(changeSetOperationSchema).max(changeSetMaximumOperationsPerSet),
    tags: z.array(tagSchema).max(changeSetMaximumTagCount).refine(
      (values) => new Set(values.map((value) => value.toUpperCase())).size === values.length,
      { message: 'Change-set tags must be unique.' }
    ),
    updatedAtUtc: dateTimeOffsetSchema
  })
  .superRefine((changeSet, context) => {
    const operationIds = new Set<string>();
    for (const [index, operation] of changeSet.operations.entries()) {
      if (operationIds.has(operation.operationId)) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: 'Operation identifiers must be unique.',
          path: ['operations', index, 'operationId']
        });
      }
      operationIds.add(operation.operationId);
      if (operation.pendingEdit.association?.changeSetId !== changeSet.changeSetId) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: 'The pending edit association must identify its owning change set.',
          path: ['operations', index, 'pendingEdit', 'association', 'changeSetId']
        });
      }
    }
  });

export const changeSetMetadataSchema = z.strictObject({
  archived: z.boolean(),
  dependencyIds: distinctStableIdsSchema(changeSetMaximumDependencyCount),
  enabled: z.boolean(),
  name: displayNameSchema,
  notes: notesSchema,
  tags: z.array(tagSchema).max(changeSetMaximumTagCount).refine(
    (values) => new Set(values.map((value) => value.toUpperCase())).size === values.length,
    { message: 'Change-set tags must be unique.' }
  )
});

export const changeSetBuildVariantSchema = z.strictObject({
  changeSetIds: distinctStableIdsSchema(changeSetMaximumCount),
  createdAtUtc: dateTimeOffsetSchema,
  name: displayNameSchema,
  outputMode: changePlanOutputModeSchema.nullable(),
  outputProfileId: stableIdSchema.nullable(),
  updatedAtUtc: dateTimeOffsetSchema,
  variantId: stableIdSchema
});

export const changeSetWorkspaceDocumentSchema = z
  .strictObject({
    activeBuildVariantId: stableIdSchema.nullable(),
    activeChangeSetId: stableIdSchema.nullable(),
    buildVariants: z.array(changeSetBuildVariantSchema).max(changeSetMaximumBuildVariantCount),
    changeSets: z.array(namedChangeSetSchema).max(changeSetMaximumCount),
    game: projectGameSchema,
    schemaVersion: z.literal(changeSetSchemaVersion),
    updatedAtUtc: dateTimeOffsetSchema
  })
  .superRefine((document, context) => {
    const changeSetIds = new Set<string>();
    const operationIds = new Set<string>();
    let operationCount = 0;
    for (const [setIndex, changeSet] of document.changeSets.entries()) {
      if (changeSetIds.has(changeSet.changeSetId)) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: 'Change-set identifiers must be unique.',
          path: ['changeSets', setIndex, 'changeSetId']
        });
      }
      changeSetIds.add(changeSet.changeSetId);
      operationCount += changeSet.operations.length;
      for (const [operationIndex, operation] of changeSet.operations.entries()) {
        if (operationIds.has(operation.operationId)) {
          context.addIssue({
            code: z.ZodIssueCode.custom,
            message: 'Operation identifiers must be unique across the workspace.',
            path: ['changeSets', setIndex, 'operations', operationIndex, 'operationId']
          });
        }
        operationIds.add(operation.operationId);
      }
    }
    if (operationCount > changeSetMaximumOperationCount) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The workspace exceeds the operation limit.',
        path: ['changeSets']
      });
    }
    for (const [setIndex, changeSet] of document.changeSets.entries()) {
      for (const dependencyId of changeSet.dependencyIds) {
        if (!changeSetIds.has(dependencyId)) {
          context.addIssue({
            code: z.ZodIssueCode.custom,
            message: 'A change-set dependency does not exist.',
            path: ['changeSets', setIndex, 'dependencyIds']
          });
        }
      }
    }
    const activeSet = document.changeSets.find(
      (changeSet) => changeSet.changeSetId === document.activeChangeSetId
    );
    if (document.activeChangeSetId !== null && (!activeSet || activeSet.archived)) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The active staging target must be a non-archived change set.',
        path: ['activeChangeSetId']
      });
    }
    const variantIds = new Set<string>();
    for (const [variantIndex, variant] of document.buildVariants.entries()) {
      if (variantIds.has(variant.variantId)) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: 'Build variant identifiers must be unique.',
          path: ['buildVariants', variantIndex, 'variantId']
        });
      }
      variantIds.add(variant.variantId);
      if (variant.changeSetIds.some((id) => !changeSetIds.has(id))) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: 'A build variant references a missing change set.',
          path: ['buildVariants', variantIndex, 'changeSetIds']
        });
      }
    }
    if (
      document.activeBuildVariantId !== null &&
      !variantIds.has(document.activeBuildVariantId)
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The active build variant does not exist.',
        path: ['activeBuildVariantId']
      });
    }
  });

export const changeSetMutationSchema = z.discriminatedUnion('kind', [
  z.strictObject({ kind: z.literal('createSet'), name: displayNameSchema }),
  z.strictObject({
    changeSetId: stableIdSchema,
    kind: z.literal('updateSet'),
    metadata: changeSetMetadataSchema
  }),
  z.strictObject({ changeSetId: stableIdSchema, kind: z.literal('deleteSet') }),
  z.strictObject({
    changeSetId: stableIdSchema,
    kind: z.literal('duplicateSet'),
    name: displayNameSchema
  }),
  z.strictObject({
    kind: z.literal('reorderSets'),
    orderedIds: distinctStableIdsSchema(changeSetMaximumCount)
  }),
  z.strictObject({
    changeSetId: stableIdSchema,
    kind: z.literal('reorderOperations'),
    orderedIds: distinctStableIdsSchema(changeSetMaximumOperationsPerSet)
  }),
  z.strictObject({
    changeSetId: stableIdSchema,
    kind: z.literal('removeOperation'),
    operationId: stableIdSchema
  }),
  z.strictObject({
    changeSetId: stableIdSchema.nullable(),
    kind: z.literal('setActiveSet')
  }),
  z.strictObject({ kind: z.literal('createVariant'), variant: changeSetBuildVariantSchema }),
  z.strictObject({ kind: z.literal('updateVariant'), variant: changeSetBuildVariantSchema }),
  z.strictObject({ kind: z.literal('deleteVariant'), variantId: stableIdSchema }),
  z.strictObject({
    kind: z.literal('setActiveVariant'),
    variantId: stableIdSchema.nullable()
  }),
  z.strictObject({ kind: z.literal('undo') }),
  z.strictObject({ kind: z.literal('redo') })
]);

export const changeSetConflictKindSchema = z.enum([
  'semanticTarget',
  'ownedOutput',
  'missingDependency',
  'disabledDependency',
  'dependencyCycle',
  'dependencyOrder',
  'sessionTarget'
]);

export const changeSetConflictSchema = z.strictObject({
  changeSetIds: distinctStableIdsSchema(changeSetMaximumCount),
  kind: changeSetConflictKindSchema,
  message: z.string().min(1),
  operationIds: distinctStableIdsSchema(changeSetMaximumOperationCount),
  target: z.string().nullable()
});

export const changeSetOperationMaterializationStateSchema = z.enum([
  'fresh',
  'stale',
  'legacyUnsupported',
  'conflict',
  'sessionLocal'
]);

export const changeSetOperationSummarySchema = z.strictObject({
  changeSetId: stableIdSchema.nullable(),
  changeSetName: z.string().nullable(),
  description: z.string(),
  operationId: stableIdSchema,
  state: changeSetOperationMaterializationStateSchema,
  target: z.string(),
  title: z.string()
});

export const changeSetMaterializationSchema = z.strictObject({
  canMaterialize: z.boolean(),
  changePlan: changePlanSchema.nullable(),
  conflicts: z.array(changeSetConflictSchema).max(changeSetMaximumOperationCount),
  diagnostics: z.array(apiDiagnosticSchema),
  operations: z.array(changeSetOperationSummarySchema).max(changeSetMaximumOperationCount),
  outputMode: changePlanOutputModeSchema.nullable(),
  outputProfileId: stableIdSchema.nullable(),
  selectedChangeSetIds: distinctStableIdsSchema(changeSetMaximumCount),
  session: editSessionSchema.nullable(),
  sourceRevisionFingerprint: sha256FingerprintSchema,
  workspaceFingerprint: sha256FingerprintSchema
});

export const changeSetWorkspaceSnapshotSchema = z.strictObject({
  canRedo: z.boolean(),
  canUndo: z.boolean(),
  document: changeSetWorkspaceDocumentSchema,
  effective: changeSetMaterializationSchema,
  etag: sha256FingerprintSchema.nullable(),
  redoLabel: z.string().nullable(),
  undoLabel: z.string().nullable()
});

export const readChangeSetWorkspaceRequestSchema = z.strictObject({
  scope: changeSetWorkspaceScopeSchema,
  session: editSessionSchema.nullable().optional()
});

export const mutateChangeSetWorkspaceRequestSchema = z.strictObject({
  expectedETag: sha256FingerprintSchema.nullable(),
  mutation: changeSetMutationSchema,
  scope: changeSetWorkspaceScopeSchema,
  session: editSessionSchema.nullable().optional()
});

export const captureChangeSetSessionRequestSchema = z.strictObject({
  changeSetId: stableIdSchema,
  expectedETag: sha256FingerprintSchema,
  previousSession: editSessionSchema.nullable(),
  scope: changeSetWorkspaceScopeSchema,
  stagedSession: editSessionSchema
});

export const captureChangeSetSessionResponseSchema = z.strictObject({
  capturedOperationIds: distinctStableIdsSchema(changeSetMaximumOperationCount),
  removedOperationIds: distinctStableIdsSchema(changeSetMaximumOperationCount),
  snapshot: changeSetWorkspaceSnapshotSchema,
  stagedSession: editSessionSchema
});

export const materializeChangeSetWorkspaceRequestSchema = z.strictObject({
  buildVariantId: stableIdSchema.nullable().optional(),
  expectedETag: sha256FingerprintSchema,
  scope: changeSetWorkspaceScopeSchema,
  session: editSessionSchema.nullable().optional()
});

export const exportChangeSetsRequestSchema = z.strictObject({
  changeSetIds: distinctStableIdsSchema(changeSetMaximumCount).min(1),
  expectedETag: sha256FingerprintSchema,
  scope: changeSetWorkspaceScopeSchema
});

export const exportChangeSetsResponseSchema = z
  .strictObject({
    available: z.boolean(),
    diagnostics: z.array(apiDiagnosticSchema),
    packageJson: utf8BoundedStringSchema(changeSetMaximumPortablePackageBytes).nullable()
  })
  .superRefine((response, context) => {
    if (response.available !== (response.packageJson !== null)) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'Portable package availability must match the package payload.',
        path: ['packageJson']
      });
    }
  });

export const importChangeSetsRequestSchema = z.strictObject({
  enableImported: z.boolean(),
  expectedETag: sha256FingerprintSchema.nullable(),
  packageJson: utf8BoundedStringSchema(changeSetMaximumPortablePackageBytes),
  scope: changeSetWorkspaceScopeSchema,
  session: editSessionSchema.nullable().optional()
});

export const importChangeSetsResponseSchema = z.strictObject({
  snapshot: changeSetWorkspaceSnapshotSchema
});

export const portableChangeSetOperationSchema = z.strictObject({
  adapterId: stableIdSchema,
  adapterSchemaVersion: z.number().int().positive(),
  payloadJson: utf8BoundedStringSchema(changeSetMaximumPortablePackageBytes),
  sourceFingerprint: sha256FingerprintSchema
});

const portablePendingEditTextSchema = (maximum: number) => z
  .string()
  .min(1)
  .max(maximum)
  .refine((value) => value.trim() === value, { message: 'Expected trimmed semantic text.' })
  .refine((value) => !/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/u.test(value), {
    message: 'Expected semantic text without unsupported control characters.'
  });

export const portablePendingEditOperationPayloadSchema = z.strictObject({
  domain: z.enum([
    'workflow.items',
    'workflow.moves',
    'workflow.pokemon',
    'workflow.trainers'
  ]),
  field: portablePendingEditTextSchema(512),
  newValue: portableCanonicalIntegerSchema,
  ownedTargets: z.array(canonicalRelativeOutputPathSchema).min(1).max(64).refine(
    (values) => new Set(values.map(canonicalRelativeOutputPathKey)).size === values.length,
    { message: 'Portable owned targets must be unique.' }
  ),
  recordId: portablePendingEditTextSchema(4096),
  sources: z.array(projectFileReferenceSchema.extend({
    layer: z.enum(['base', 'layered']),
    relativePath: canonicalRelativeOutputPathSchema
  })).min(1).max(128).refine(
    (values) => new Set(values.map(
      (value) => `${value.layer}\0${canonicalRelativeOutputPathKey(value.relativePath)}`
    )).size === values.length,
    { message: 'Portable pending-edit sources must be unique.' }
  ),
  summary: portablePendingEditTextSchema(2048)
});

export const portableNamedChangeSetSchema = z.strictObject({
  dependencyIds: distinctStableIdsSchema(changeSetMaximumDependencyCount),
  name: displayNameSchema,
  notes: notesSchema,
  operations: z.array(portableChangeSetOperationSchema).max(changeSetMaximumOperationsPerSet),
  portableId: stableIdSchema,
  tags: z.array(tagSchema).max(changeSetMaximumTagCount)
});

export const portableChangeSetPackageSchema = z.strictObject({
  changeSets: z.array(portableNamedChangeSetSchema).max(changeSetMaximumCount),
  game: projectGameSchema,
  schemaVersion: z.literal(portableChangeSetSchemaVersion)
});

export type ChangeSetWorkspaceScope = z.infer<typeof changeSetWorkspaceScopeSchema>;
export type ChangeSetOperationStorageKind = z.infer<typeof changeSetOperationStorageKindSchema>;
export type ChangeSetSourceBindingKind = z.infer<typeof changeSetSourceBindingKindSchema>;
export type ChangeSetOperation = z.infer<typeof changeSetOperationSchema>;
export type NamedChangeSet = z.infer<typeof namedChangeSetSchema>;
export type ChangeSetMetadata = z.infer<typeof changeSetMetadataSchema>;
export type ChangeSetBuildVariant = z.infer<typeof changeSetBuildVariantSchema>;
export type ChangeSetWorkspaceDocument = z.infer<typeof changeSetWorkspaceDocumentSchema>;
export type ChangeSetWorkspaceMutation = z.infer<typeof changeSetMutationSchema>;
export type ChangeSetConflictKind = z.infer<typeof changeSetConflictKindSchema>;
export type ChangeSetConflict = z.infer<typeof changeSetConflictSchema>;
export type ChangeSetOperationMaterializationState = z.infer<
  typeof changeSetOperationMaterializationStateSchema
>;
export type ChangeSetOperationSummary = z.infer<typeof changeSetOperationSummarySchema>;
export type ChangeSetMaterialization = z.infer<typeof changeSetMaterializationSchema>;
export type ChangeSetWorkspaceSnapshot = z.infer<typeof changeSetWorkspaceSnapshotSchema>;
export type ReadChangeSetWorkspaceRequest = z.infer<typeof readChangeSetWorkspaceRequestSchema>;
export type MutateChangeSetWorkspaceRequest = z.infer<typeof mutateChangeSetWorkspaceRequestSchema>;
export type CaptureChangeSetSessionRequest = z.infer<typeof captureChangeSetSessionRequestSchema>;
export type CaptureChangeSetSessionResponse = z.infer<typeof captureChangeSetSessionResponseSchema>;
export type MaterializeChangeSetWorkspaceRequest = z.infer<
  typeof materializeChangeSetWorkspaceRequestSchema
>;
export type ExportChangeSetsRequest = z.infer<typeof exportChangeSetsRequestSchema>;
export type ExportChangeSetsResponse = z.infer<typeof exportChangeSetsResponseSchema>;
export type ImportChangeSetsRequest = z.infer<typeof importChangeSetsRequestSchema>;
export type ImportChangeSetsResponse = z.infer<typeof importChangeSetsResponseSchema>;
export type PortableChangeSetOperation = z.infer<typeof portableChangeSetOperationSchema>;
export type PortablePendingEditOperationPayload = z.infer<
  typeof portablePendingEditOperationPayloadSchema
>;
export type PortableNamedChangeSet = z.infer<typeof portableNamedChangeSetSchema>;
export type PortableChangeSetPackage = z.infer<typeof portableChangeSetPackageSchema>;
