/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import { apiDiagnosticSchema, projectGameSchema } from './contracts';
import { changeSetWorkspaceDocumentSchema } from './changeSetContracts';
import {
  semanticExploreCoverageSchema,
  semanticExploreRecordRefSchema,
  semanticExploreRevisionSchema,
  semanticExploreScalarSchema,
  semanticExploreScopeSchema,
  semanticExploreSourceSnapshotSchema
} from './semanticExploreContracts';

export const semanticMergeSchemaVersion = 1 as const;
export const semanticMergeDefaultPageSize = 50;
export const semanticMergeMaximumPageSize = 100;
export const semanticMergeMaximumCursorLength = 2_048;
export const semanticMergeMaximumExternalRootLength = 4_096;
export const semanticMergeMaximumTargets = 128;
export const semanticMergeMaximumDomainsPerProposal = 1;
export const semanticMergeMaximumResolutions = 384;
export const semanticMergeMaximumIndexedRows = 50_000;
export const semanticMergeMaximumTargetSelectionWindow = 500;
export const semanticMergeMaximumTargetSearchTextLength = 256;
export const semanticMergeMaximumConflictsPerRow = 3;
export const semanticMergeMaximumReportedConflicts =
  semanticMergeMaximumTargetSelectionWindow * semanticMergeMaximumConflictsPerRow;
export const semanticMergeMaximumDiagnostics = 100;
export const semanticMergeMaximumChangeSetNameLength = 128;
export const kmRecipeMaximumBytes = 2 * 1_024 * 1_024;
export const kmRecipeMaximumOperations = 128;
export const kmRecipeMaximumSteps = 32;
export const kmRecipeMaximumDependencies = 32;
export const kmRecipeMaximumNameLength = 128;
export const kmRecipeMaximumNotesLength = 4_096;
export const kmRecipeMaximumSeedLength = 128;

const fingerprintSchema = z.string().regex(/^[a-f0-9]{64}$/u);
const changeSetETagSchema = z.string().regex(/^[A-Fa-f0-9]{64}$/u);
const contractKeySchema = z
  .string()
  .min(1)
  .max(128)
  .regex(/^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$/u);
const fieldKeySchema = z
  .string()
  .min(1)
  .max(128)
  .regex(/^[a-z][A-Za-z0-9]*$/u);
const stableIdSchema = z
  .string()
  .min(1)
  .max(1_024)
  .refine((value) => (
    value.trim() === value &&
    !/[\u0000-\u001f\u007f-\u009f\u061c\u200b-\u200f\u202a-\u202e\u2060-\u2064\u2066-\u2069\ufeff]/iu.test(value)
  ));
const boundedDisplayTextSchema = z
  .string()
  .max(8_192)
  .refine((value) => (
    !/[\u0000-\u001f\u007f-\u009f\u061c\u200b-\u200f\u202a-\u202e\u2060-\u2064\u2066-\u2069\ufeff]/iu.test(value)
  ))
  .refine((value) => !containsLocalPathSignature(value));
const cursorSchema = z
  .string()
  .min(1)
  .max(semanticMergeMaximumCursorLength)
  .refine((value) => !/[\u0000-\u001f\u007f-\u009f]/u.test(value));
const sourceInstanceIdSchema = z.string().regex(/^merge-src-[0-9a-f]{32}$/u);
const recipeInstanceIdSchema = z.string().regex(/^recipe-[0-9a-f]{32}$/u);
const utf8BoundedStringSchema = (maximumBytes: number) => z.string().refine(
  (value) => new TextEncoder().encode(value).byteLength <= maximumBytes,
  { message: `Expected no more than ${maximumBytes} UTF-8 bytes.` }
);
const normalizedSearchSchema = z
  .string()
  .min(1)
  .max(semanticMergeMaximumTargetSearchTextLength)
  .refine(
    (value) => value.trim() === value && value.normalize('NFC') === value,
    { message: 'Expected trimmed NFC target search text.' }
  );
const safeTextSchema = (maximum: number) => z
  .string()
  .max(maximum)
  .refine((value) => (
    value.normalize('NFC') === value &&
    !/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f\u061c\u200b-\u200f\u202a-\u202e\u2060-\u2064\u2066-\u2069\ufeff]/iu.test(value)
  ))
  .refine((value) => !containsLocalPathSignature(value));
const trimmedNameSchema = z
  .string()
  .min(1)
  .max(semanticMergeMaximumChangeSetNameLength)
  .refine((value) => value.trim() === value && value.normalize('NFC') === value)
  .refine((value) => !containsLocalPathSignature(value));
const distinct = <T>(values: readonly T[], key: (value: T) => string) => (
  new Set(values.map(key)).size === values.length
);
const recordKey = (record: z.infer<typeof semanticExploreRecordRefSchema>) => JSON.stringify([
  record.gameFamily,
  record.domain,
  record.recordKind.key,
  record.recordKind.schemaVersion,
  record.recordId,
  record.subrecordId
]);
const fieldRefKey = (target: z.infer<typeof semanticMergeFieldRefSchema>) => JSON.stringify([
  recordKey(target.record),
  target.fieldKey
]);
const hasSingleDomain = (targets: readonly z.infer<typeof semanticMergeFieldRefSchema>[]) => (
  new Set(targets.map((target) => target.record.domain)).size <=
  semanticMergeMaximumDomainsPerProposal
);
const exactRevisionKey = (revision: z.infer<typeof semanticExploreRevisionSchema>) => (
  JSON.stringify(revision)
);
const exactSnapshotKey = (snapshot: z.infer<typeof semanticExploreSourceSnapshotSchema>) => (
  JSON.stringify(snapshot)
);

function containsLocalPathSignature(value: string) {
  let candidate = value;
  for (let depth = 0; depth <= 3; depth += 1) {
    if (
      candidate.includes('\\') ||
      candidate.split('|').some((part) => (
        part.includes('/') && part !== 'Scarlet/Violet'
      )) ||
      /(?:^|[^A-Za-z0-9])[A-Za-z]:/u.test(candidate) ||
      /(?:^|[^A-Za-z0-9])file:/iu.test(candidate) ||
      /(?:^|[^A-Za-z0-9])~/u.test(candidate)
    ) return true;
    if (depth === 3 || !candidate.includes('%')) break;
    try {
      const decoded = decodeURIComponent(candidate);
      if (decoded === candidate) break;
      candidate = decoded;
    } catch {
      return true;
    }
  }
  return false;
}

export const semanticMergeFeatureSchema = z.enum([
  'threeWayScalarMerge',
  'focusedConflictResolution',
  'stableCollectionMerge',
  'opaqueFileFallback',
  'recipeImport',
  'recipeExport',
  'compatibilityReport',
  'seededReproducibility',
  'headlessAutomation'
]);
export const semanticMergeConflictKindSchema = z.enum([
  'sameField',
  'currentTarget',
  'pendingTarget',
  'deleteVsEdit',
  'reorder',
  'incompatibleLayout',
  'ownership'
]);
export const semanticMergeConflictChoiceSchema = z.enum([
  'sourceA',
  'sourceB',
  'base',
  'keepCurrent'
]);
export const semanticMergeRowStateSchema = z.enum([
  'autoMerged',
  'conflict',
  'alreadyCurrent',
  'unsupported'
]);
export const semanticMergeFallbackKindSchema = z.enum([
  'none',
  'legacyWorkflowOnly',
  'unavailable'
]);
export const semanticMergeFallbackTargetSchema = z.literal('legacyModMerger');
export const kmRecipeCompatibilityStateSchema = z.enum([
  'compatible',
  'alreadyApplied',
  'conflict',
  'unsupported'
]);
const semanticCoverageStateSchema = z.enum(['complete', 'partial', 'unavailable']);
const semanticConfidenceSchema = z.enum(['verified', 'derived', 'unknown']);

export const semanticMergeDomainCapabilitySchema = z.strictObject({
  domain: contractKeySchema,
  fieldKeys: z.array(fieldKeySchema).max(128).refine(
    (values) => new Set(values).size === values.length,
    { message: 'Semantic merge capability fields must be unique.' }
  ),
  recordKind: contractKeySchema
});

export const semanticMergeCapabilitySchema = z.strictObject({
  confidence: semanticConfidenceSchema,
  domains: z.array(semanticMergeDomainCapabilitySchema).max(128).refine(
    (values) => distinct(values, (value) => JSON.stringify([value.domain, value.recordKind])),
    { message: 'Semantic merge capability domains must be unique.' }
  ),
  feature: semanticMergeFeatureSchema,
  providerId: contractKeySchema,
  reasonCode: contractKeySchema.nullable(),
  state: semanticCoverageStateSchema
}).superRefine((capability, context) => {
  if (capability.state === 'unavailable' && capability.domains.length > 0) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'An unavailable semantic merge capability cannot advertise writable domains.',
      path: ['domains']
    });
  }
});

const capabilitiesSchema = z.array(semanticMergeCapabilitySchema).length(9).refine(
  (values) => distinct(values, (value) => value.feature),
  { message: 'All semantic merge feature capabilities must be returned exactly once.' }
);

export const semanticMergeCapabilitiesRequestSchema = z.strictObject({
  scope: semanticExploreScopeSchema
});

export const semanticMergeCapabilitiesResponseSchema = z.strictObject({
  canOpenLegacyMerger: z.literal(false),
  capabilities: capabilitiesSchema,
  revision: semanticExploreRevisionSchema,
  snapshots: z.array(semanticExploreSourceSnapshotSchema).length(3)
}).superRefine((response, context) => {
  const layers = response.snapshots.map((snapshot) => snapshot.layer.kind).sort();
  if (JSON.stringify(layers) !== JSON.stringify(['base', 'layered', 'pending'])) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Semantic merge capabilities require exactly Base, Layered, and Pending snapshots.',
      path: ['snapshots']
    });
  }
  response.snapshots.forEach((snapshot, index) => {
    if (snapshot.layer.instanceId !== null) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'Authoring snapshots cannot carry source instance identifiers.',
        path: ['snapshots', index, 'layer', 'instanceId']
      });
    }
  });
});

export const semanticMergeSourceOpenRequestSchema = z.strictObject({
  expectedRevision: semanticExploreRevisionSchema,
  externalRootPath: z.string().min(1).max(semanticMergeMaximumExternalRootLength),
  scope: semanticExploreScopeSchema
});

export const semanticMergeSourceSchema = z.strictObject({
  coverage: z.array(semanticExploreCoverageSchema).max(128),
  instanceId: sourceInstanceIdSchema,
  snapshot: semanticExploreSourceSnapshotSchema
}).superRefine((source, context) => {
  if (
    source.snapshot.layer.kind !== 'comparedMod' ||
    source.snapshot.layer.instanceId !== source.instanceId
  ) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'A semantic merge source snapshot must carry its public opaque instance.',
      path: ['snapshot', 'layer']
    });
  }
});

export const semanticMergeSourceOpenResponseSchema = z.strictObject({
  revision: semanticExploreRevisionSchema,
  source: semanticMergeSourceSchema
});

export const semanticMergeFieldRefSchema = z.strictObject({
  fieldKey: fieldKeySchema,
  record: semanticExploreRecordRefSchema
});

export const semanticMergeConflictResolutionSchema = z.strictObject({
  choice: semanticMergeConflictChoiceSchema,
  conflictId: stableIdSchema
});

export const semanticMergeConflictSchema = z.strictObject({
  allowedChoices: z.array(semanticMergeConflictChoiceSchema).max(4).refine(
    (values) => new Set(values).size === values.length,
    { message: 'Semantic merge conflict choices must be unique.' }
  ),
  conflictId: stableIdSchema,
  kind: semanticMergeConflictKindSchema,
  reasonCode: contractKeySchema,
  selectedChoice: semanticMergeConflictChoiceSchema.nullable()
}).superRefine((conflict, context) => {
  if (
    conflict.selectedChoice !== null &&
    !conflict.allowedChoices.includes(conflict.selectedChoice)
  ) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'The selected semantic merge choice must be explicitly allowed.',
      path: ['selectedChoice']
    });
  }
  if (
    (conflict.kind === 'currentTarget' || conflict.kind === 'pendingTarget') &&
    (
      conflict.allowedChoices.length !== 1 ||
      conflict.allowedChoices[0] !== 'keepCurrent'
    )
  ) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Current and pending conflicts may only preserve the current authored value.',
      path: ['allowedChoices']
    });
  }
  if (conflict.kind === 'sameField' && (
    JSON.stringify(conflict.allowedChoices) !== JSON.stringify([
      'sourceA',
      'sourceB',
      'base'
    ])
  )) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'A same-field conflict requires an explicit Base, Mod A, or Mod B choice.',
      path: ['allowedChoices']
    });
  }
  const isUnsupportedKind = [
    'deleteVsEdit',
    'reorder',
    'incompatibleLayout',
    'ownership'
  ].includes(conflict.kind);
  if (isUnsupportedKind && (
    conflict.allowedChoices.length !== 0 ||
    conflict.selectedChoice !== null
  )) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'An unsupported semantic merge conflict cannot advertise a resolution.',
      path: ['allowedChoices']
    });
  }
});

export const semanticMergeFallbackSchema = z.strictObject({
  available: z.literal(false),
  kind: z.literal('unavailable'),
  reasonCode: z.literal('legacy-reviewed-transaction-boundary-unavailable'),
  target: z.null()
});

export const semanticMergeRowSchema = z.strictObject({
  baseValue: semanticExploreScalarSchema.nullable(),
  confidence: semanticConfidenceSchema,
  conflicts: z.array(semanticMergeConflictSchema).max(semanticMergeMaximumConflictsPerRow).refine(
    (values) => distinct(values, (value) => value.conflictId),
    { message: 'Semantic merge row conflicts must be unique.' }
  ),
  coverage: semanticCoverageStateSchema,
  currentValue: semanticExploreScalarSchema.nullable(),
  fallback: semanticMergeFallbackSchema,
  fieldLabel: boundedDisplayTextSchema,
  pendingValue: semanticExploreScalarSchema.nullable(),
  providerId: contractKeySchema,
  recordLabel: boundedDisplayTextSchema,
  resultValue: semanticExploreScalarSchema.nullable(),
  rowId: stableIdSchema,
  selected: z.boolean(),
  sourceAValue: semanticExploreScalarSchema.nullable(),
  sourceBValue: semanticExploreScalarSchema.nullable(),
  state: semanticMergeRowStateSchema,
  target: semanticMergeFieldRefSchema
}).superRefine((row, context) => {
  if (row.state === 'conflict' && (
    row.conflicts.length === 0 ||
    !row.conflicts.some((conflict) => (
      conflict.allowedChoices.length > 0 && conflict.selectedChoice === null
    ))
  )) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'A semantic merge conflict row must expose an unresolved focused choice.',
      path: ['conflicts']
    });
  }
  if (
    (row.state === 'autoMerged' || row.state === 'alreadyCurrent') &&
    row.conflicts.some((conflict) => (
    conflict.selectedChoice === null
    ))
  ) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Resolved semantic merge rows cannot retain unresolved conflicts.',
      path: ['conflicts']
    });
  }
  if (row.state === 'unsupported' && (
    row.conflicts.length === 0 ||
    row.conflicts.some((conflict) => (
      conflict.allowedChoices.length !== 0 || conflict.selectedChoice !== null
    ))
  )) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'An unsupported semantic merge row must expose only nonresolvable conflicts.',
      path: ['conflicts']
    });
  }
});

export const semanticMergePreviewRequestSchema = z.strictObject({
  cursor: cursorSchema.nullable(),
  expectedChangeSetETag: changeSetETagSchema.nullable(),
  expectedRevision: semanticExploreRevisionSchema,
  limit: z.number().int().min(1).max(semanticMergeMaximumPageSize),
  proposalFingerprint: fingerprintSchema.nullable(),
  proposalId: fingerprintSchema.nullable(),
  resolutions: z.array(semanticMergeConflictResolutionSchema).max(
    semanticMergeMaximumResolutions
  ).refine(
    (values) => distinct(values, (value) => value.conflictId),
    { message: 'Semantic merge conflict resolutions must be unique.' }
  ),
  scope: semanticExploreScopeSchema,
  sourceAInstanceId: sourceInstanceIdSchema,
  sourceBInstanceId: sourceInstanceIdSchema,
  targetSearchText: normalizedSearchSchema.nullable(),
  targets: z.array(semanticMergeFieldRefSchema).max(semanticMergeMaximumTargets).refine(
    (values) => distinct(values, fieldRefKey),
    { message: 'Semantic merge targets must be unique.' }
  ).refine(
    hasSingleDomain,
    { message: 'A semantic merge proposal may target only one semantic domain.' }
  )
}).superRefine((request, context) => {
  const isContinuation = request.cursor !== null;
  if (
    isContinuation !== (request.proposalId !== null) ||
    isContinuation !== (request.proposalFingerprint !== null)
  ) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'A semantic merge continuation requires its exact proposal identity.',
      path: ['cursor']
    });
  }
  if (request.sourceAInstanceId === request.sourceBInstanceId) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Semantic merge requires two distinct source instances.',
      path: ['sourceBInstanceId']
    });
  }
  if (request.targets.length > 0 && request.targetSearchText !== null) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Target search is only valid during semantic merge discovery.',
      path: ['targetSearchText']
    });
  }
  if (request.targets.length === 0 && request.resolutions.length > 0) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Semantic merge discovery cannot carry conflict resolutions.',
      path: ['resolutions']
    });
  }
});

export const semanticMergePreviewResponseSchema = z.strictObject({
  authoringContextFingerprint: fingerprintSchema,
  baseSnapshot: semanticExploreSourceSnapshotSchema,
  canImport: z.boolean(),
  capabilities: capabilitiesSchema,
  diagnostics: z.array(apiDiagnosticSchema).max(semanticMergeMaximumDiagnostics),
  layeredSnapshot: semanticExploreSourceSnapshotSchema,
  nextCursor: cursorSchema.nullable(),
  normalizedResolutions: z.array(semanticMergeConflictResolutionSchema).max(
    semanticMergeMaximumResolutions
  ).refine(
    (values) => distinct(values, (value) => value.conflictId),
    { message: 'Normalized semantic merge resolutions must be unique.' }
  ),
  normalizedTargetSearchText: normalizedSearchSchema.nullable(),
  normalizedTargets: z.array(semanticMergeFieldRefSchema).max(
    semanticMergeMaximumTargets
  ).refine(
    (values) => distinct(values, fieldRefKey),
    { message: 'Normalized semantic merge targets must be unique.' }
  ),
  pendingSnapshot: semanticExploreSourceSnapshotSchema,
  proposalFingerprint: fingerprintSchema,
  proposalId: fingerprintSchema,
  queryFingerprint: fingerprintSchema,
  revision: semanticExploreRevisionSchema,
  rows: z.array(semanticMergeRowSchema).max(semanticMergeMaximumPageSize).refine(
    (values) => distinct(values, (value) => value.rowId),
    { message: 'Semantic merge rows must be unique.' }
  ).refine(
    (values) => distinct(values, (value) => fieldRefKey(value.target)),
    { message: 'Semantic merge row targets must be unique.' }
  ),
  selectionRequired: z.boolean(),
  sourceASnapshot: semanticExploreSourceSnapshotSchema,
  sourceBSnapshot: semanticExploreSourceSnapshotSchema,
  targetWindowCapped: z.boolean(),
  totalConflictCount: z.number().int().min(0).max(semanticMergeMaximumReportedConflicts),
  totalMatchingTargetCount: z.number().int().min(0).max(semanticMergeMaximumIndexedRows),
  totalMutationCount: z.number().int().min(0).max(semanticMergeMaximumTargets),
  totalRowCount: z.number().int().min(0).max(semanticMergeMaximumTargetSelectionWindow)
}).superRefine((response, context) => {
  const expectedWindowCount = Math.min(
    response.totalMatchingTargetCount,
    semanticMergeMaximumTargetSelectionWindow
  );
  const conflictIds = response.rows.flatMap((row) => (
    row.conflicts.map((conflict) => conflict.conflictId)
  ));
  if (new Set(conflictIds).size !== conflictIds.length) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Semantic merge conflict identifiers must be unique across rows.',
      path: ['rows']
    });
  }
  const normalizedChoices = new Map(response.normalizedResolutions.map((resolution) => [
    resolution.conflictId,
    resolution.choice
  ]));
  response.rows.forEach((row, rowIndex) => {
    row.conflicts.forEach((conflict, conflictIndex) => {
      if ((normalizedChoices.get(conflict.conflictId) ?? null) !== conflict.selectedChoice) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: 'A semantic merge row choice must match its normalized resolution.',
          path: ['rows', rowIndex, 'conflicts', conflictIndex, 'selectedChoice']
        });
      }
    });
  });
  if (response.rows.length > response.totalRowCount) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Semantic merge returned more rows than its advertised total.',
      path: ['rows']
    });
  }
  if (
    response.targetWindowCapped !==
    (response.totalMatchingTargetCount > semanticMergeMaximumTargetSelectionWindow)
  ) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Semantic merge returned an inconsistent target window.',
      path: ['targetWindowCapped']
    });
  }
  if (response.selectionRequired) {
    if (
      response.canImport ||
      response.normalizedTargets.length > 0 ||
      response.normalizedResolutions.length > 0 ||
      response.totalMutationCount > 0 ||
      response.totalRowCount !== expectedWindowCount
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'A semantic merge discovery response cannot contain an importable proposal.',
        path: ['selectionRequired']
      });
    }
  } else {
    if (
      response.normalizedTargets.length === 0 ||
      response.totalRowCount !== response.normalizedTargets.length ||
      response.normalizedTargetSearchText !== null ||
      response.totalMatchingTargetCount !== 0 ||
      response.targetWindowCapped
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'A semantic merge proposal must bind exact targets and no discovery search.',
        path: ['normalizedTargets']
      });
    }
  }
  if (response.canImport && response.totalMutationCount === 0) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'An importable semantic merge proposal requires an effective mutation.',
      path: ['canImport']
    });
  }
  if (response.canImport && response.rows.some((row) => (
    row.state === 'unsupported' ||
    row.conflicts.some((conflict) => conflict.selectedChoice === null)
  ))) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'An importable semantic merge page cannot contain unresolved or unsupported rows.',
      path: ['canImport']
    });
  }
});

export const semanticMergeImportRequestSchema = z.strictObject({
  changeSetName: trimmedNameSchema,
  expectedChangeSetETag: changeSetETagSchema.nullable(),
  expectedRevision: semanticExploreRevisionSchema,
  proposalFingerprint: fingerprintSchema,
  proposalId: fingerprintSchema,
  resolutions: z.array(semanticMergeConflictResolutionSchema).max(
    semanticMergeMaximumResolutions
  ).refine(
    (values) => distinct(values, (value) => value.conflictId),
    { message: 'Semantic merge import resolutions must be unique.' }
  ),
  scope: semanticExploreScopeSchema,
  sourceAInstanceId: sourceInstanceIdSchema,
  sourceBInstanceId: sourceInstanceIdSchema,
  targets: z.array(semanticMergeFieldRefSchema).min(1).max(
    semanticMergeMaximumTargets
  ).refine(
    (values) => distinct(values, fieldRefKey),
    { message: 'Semantic merge import targets must be unique.' }
  ).refine(
    hasSingleDomain,
    { message: 'A semantic merge import may target only one semantic domain.' }
  )
});

export const semanticMergeImportResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema).max(semanticMergeMaximumDiagnostics),
  importedChangeSetId: stableIdSchema,
  proposalFingerprint: fingerprintSchema,
  proposalId: fingerprintSchema,
  receipt: z.strictObject({
    canRedo: z.boolean(),
    canUndo: z.boolean(),
    document: changeSetWorkspaceDocumentSchema,
    etag: changeSetETagSchema,
    redoLabel: safeTextSchema(256).nullable(),
    undoLabel: safeTextSchema(256).nullable()
  }),
  revision: semanticExploreRevisionSchema
});

const canonicalInt32Schema = z.string().min(1).max(11).refine((value) => {
  if (!/^-?(?:0|[1-9][0-9]*)$/u.test(value) || value === '-0') return false;
  const parsed = Number(value);
  return Number.isInteger(parsed) &&
    parsed >= -2_147_483_648 &&
    parsed <= 2_147_483_647 &&
    String(parsed) === value;
}, { message: 'Expected a canonical signed 32-bit integer.' });

export const kmRecipeScalarSchema = z.strictObject({
  canonicalValue: canonicalInt32Schema,
  kind: z.enum(['signedInteger', 'unsignedInteger', 'enum'])
}).superRefine((scalar, context) => {
  if (scalar.kind === 'unsignedInteger' && scalar.canonicalValue.startsWith('-')) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'An unsigned recipe scalar cannot be negative.',
      path: ['canonicalValue']
    });
  }
});

export const kmRecipeMetadataSchema = z.strictObject({
  name: z.string().min(1).max(kmRecipeMaximumNameLength).refine(
    (value) => value.trim() === value && value.normalize('NFC') === value
  ).refine((value) => !containsLocalPathSignature(value)),
  notes: safeTextSchema(kmRecipeMaximumNotesLength).nullable(),
  seed: z.null()
});

export const kmRecipeOperationSchema = z.strictObject({
  afterValue: kmRecipeScalarSchema,
  expectedBaseValue: kmRecipeScalarSchema,
  expectedCurrentValue: kmRecipeScalarSchema,
  fieldKey: fieldKeySchema,
  operationId: stableIdSchema,
  providerId: contractKeySchema,
  record: semanticExploreRecordRefSchema
});

export const kmRecipeStepSchema = z.strictObject({
  dependencyStepIds: z.array(stableIdSchema).max(kmRecipeMaximumDependencies).refine(
    (values) => new Set(values).size === values.length,
    { message: 'Recipe step dependencies must be unique.' }
  ),
  operations: z.array(kmRecipeOperationSchema).min(1).max(kmRecipeMaximumOperations).refine(
    (values) => distinct(values, (value) => value.operationId),
    { message: 'Recipe operation identifiers must be unique within a step.' }
  ),
  order: z.number().int().min(0).max(kmRecipeMaximumSteps - 1),
  stepId: stableIdSchema
});

export const kmRecipePackageSchema = z.strictObject({
  game: z.enum(['sword', 'shield']),
  metadata: kmRecipeMetadataSchema,
  providerSchema: z.literal('km.semantic-scalar.swsh.v1'),
  schemaVersion: z.literal(semanticMergeSchemaVersion),
  sourceCompatibilityFingerprint: fingerprintSchema,
  steps: z.array(kmRecipeStepSchema).min(1).max(kmRecipeMaximumSteps)
}).superRefine((recipe, context) => {
  if (!distinct(recipe.steps, (step) => step.stepId)) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Recipe step identifiers must be unique.',
      path: ['steps']
    });
  }
  if (!distinct(recipe.steps, (step) => String(step.order))) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Recipe step order values must be unique.',
      path: ['steps']
    });
  }
  const stepIds = new Set(recipe.steps.map((step) => step.stepId));
  const operationIds = new Set<string>();
  const operationTargets = new Set<string>();
  const operationDomains = new Set<string>();
  let operationCount = 0;
  recipe.steps.forEach((step, stepIndex) => {
    const expectedStepId = `step-${String(stepIndex + 1).padStart(4, '0')}`;
    if (step.order !== stepIndex || step.stepId !== expectedStepId) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'Recipe steps must use canonical contiguous array order and identifiers.',
        path: ['steps', stepIndex]
      });
    }
    if (JSON.stringify(step.dependencyStepIds) !== JSON.stringify(
      [...step.dependencyStepIds].sort()
    )) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'Recipe step dependencies must use canonical sorted order.',
        path: ['steps', stepIndex, 'dependencyStepIds']
      });
    }
    operationCount += step.operations.length;
    step.dependencyStepIds.forEach((dependency, dependencyListIndex) => {
      const referencedStepIndex = recipe.steps.findIndex((candidate) => (
        candidate.stepId === dependency
      ));
      if (
        !stepIds.has(dependency) ||
        referencedStepIndex < 0 ||
        referencedStepIndex >= stepIndex
      ) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: 'A recipe dependency must reference a different included step.',
          path: ['steps', stepIndex, 'dependencyStepIds', dependencyListIndex]
        });
      }
    });
    let previousTargetKey: string | null = null;
    step.operations.forEach((operation, operationIndex) => {
      const expectedOperationId = `op-${String(operationCount - step.operations.length + operationIndex + 1).padStart(6, '0')}`;
      if (operationIds.has(operation.operationId)) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: 'Recipe operation identifiers must be globally unique.',
          path: ['steps', stepIndex, 'operations', operationIndex, 'operationId']
        });
      }
      operationIds.add(operation.operationId);
      operationDomains.add(operation.record.domain);
      if (operation.operationId !== expectedOperationId) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: 'Recipe operations must use one canonical global identifier sequence.',
          path: ['steps', stepIndex, 'operations', operationIndex, 'operationId']
        });
      }
      const expectedRecordKind = operation.record.domain === 'workflow.items'
        ? 'item'
        : operation.record.domain === 'workflow.pokemon'
          ? 'pokemon-personal'
          : operation.record.domain === 'workflow.moves'
            ? 'move'
            : null;
      const expectedProvider = operation.record.domain === 'workflow.items'
        ? 'swsh.items.semantic'
        : operation.record.domain === 'workflow.pokemon'
          ? 'swsh.pokemon.semantic'
          : operation.record.domain === 'workflow.moves'
            ? 'swsh.moves.semantic'
            : null;
      const recordId = Number(operation.record.recordId);
      const minimumRecordId = operation.record.domain === 'workflow.moves' ? 0 : 1;
      if (
        operation.record.gameFamily !== 'swordShield' ||
        expectedRecordKind === null ||
        operation.record.recordKind.key !== expectedRecordKind ||
        operation.record.recordKind.schemaVersion !== 1 ||
        operation.record.subrecordId !== null ||
        !Number.isInteger(recordId) ||
        recordId < minimumRecordId ||
        recordId > 2_147_483_647 ||
        String(recordId) !== operation.record.recordId ||
        operation.providerId !== expectedProvider
      ) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: 'A recipe operation record or provider is outside the Sword/Shield scalar adapter.',
          path: ['steps', stepIndex, 'operations', operationIndex]
        });
      }
      if (
        operation.expectedBaseValue.kind !== operation.expectedCurrentValue.kind ||
        operation.expectedBaseValue.kind !== operation.afterValue.kind
      ) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: 'Recipe operation scalar kinds must match exactly.',
          path: ['steps', stepIndex, 'operations', operationIndex]
        });
      }
      const targetKey = [
        operation.record.gameFamily,
        operation.record.domain,
        operation.record.recordKind.key,
        operation.record.recordKind.schemaVersion,
        operation.record.recordId,
        '',
        operation.fieldKey
      ].join(':');
      if (operationTargets.has(targetKey)) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: 'Recipe semantic targets must be globally unique.',
          path: ['steps', stepIndex, 'operations', operationIndex]
        });
      }
      operationTargets.add(targetKey);
      if (previousTargetKey !== null && previousTargetKey >= targetKey) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: 'Recipe operations must use canonical target order within each step.',
          path: ['steps', stepIndex, 'operations', operationIndex]
        });
      }
      previousTargetKey = targetKey;
    });
  });
  if (operationCount > kmRecipeMaximumOperations) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Recipe operation count exceeds the portable limit.',
      path: ['steps']
    });
  }
  if (operationCount === 0) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'A recipe must contain at least one semantic operation.',
      path: ['steps']
    });
  }
  if (operationDomains.size > semanticMergeMaximumDomainsPerProposal) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'A recipe may contain operations from only one semantic domain.',
      path: ['steps']
    });
  }
});

export const kmRecipeArtifactSchema = z.strictObject({
  content: utf8BoundedStringSchema(kmRecipeMaximumBytes).superRefine((value, context) => {
    try {
      const parsed = JSON.parse(value) as unknown;
      const result = kmRecipePackageSchema.safeParse(parsed);
      if (!result.success) {
        context.addIssue({
          code: z.ZodIssueCode.custom,
          message: 'The recipe artifact content does not match the portable schema.'
        });
      }
    } catch {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The recipe artifact content must be valid JSON.'
      });
    }
  }),
  mediaType: z.literal('application/vnd.km-editor.recipe+json'),
  schemaVersion: z.literal(semanticMergeSchemaVersion),
  sha256: fingerprintSchema,
  suggestedFileName: z.string().min(1).max(128).refine((value) => (
    value.trim() === value &&
    /^[\x20-\x7e]+$/u.test(value) &&
    !/[\\/:*?"<>|\u0000-\u001f\u007f]/u.test(value) &&
    value.toLowerCase().endsWith('.kmrecipe') &&
    value !== '.' && value !== '..'
  ))
});

export const kmRecipeExportRequestSchema = z.strictObject({
  expectedChangeSetETag: changeSetETagSchema,
  expectedRevision: semanticExploreRevisionSchema,
  name: kmRecipeMetadataSchema.shape.name,
  notes: kmRecipeMetadataSchema.shape.notes,
  scope: semanticExploreScopeSchema,
  seed: kmRecipeMetadataSchema.shape.seed,
  selectedChangeSetIds: z.array(stableIdSchema).min(1).max(kmRecipeMaximumSteps).refine(
    (values) => new Set(values).size === values.length,
    { message: 'Recipe export change sets must be unique.' }
  )
});

export const kmRecipeExportResponseSchema = z.strictObject({
  artifact: kmRecipeArtifactSchema,
  diagnostics: z.array(apiDiagnosticSchema).max(semanticMergeMaximumDiagnostics),
  recipeFingerprint: fingerprintSchema,
  revision: semanticExploreRevisionSchema,
  selectedChangeSetCount: z.number().int().min(1).max(kmRecipeMaximumSteps),
  totalOperationCount: z.number().int().min(1).max(kmRecipeMaximumOperations)
});

export const kmRecipeValidateRequestSchema = z.strictObject({
  content: utf8BoundedStringSchema(kmRecipeMaximumBytes)
});

export const kmRecipeValidateResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema).max(semanticMergeMaximumDiagnostics),
  game: projectGameSchema,
  metadata: kmRecipeMetadataSchema,
  recipeFingerprint: fingerprintSchema,
  recipeInstanceId: recipeInstanceIdSchema,
  totalOperationCount: z.number().int().min(1).max(kmRecipeMaximumOperations),
  totalStepCount: z.number().int().min(1).max(kmRecipeMaximumSteps)
});

export const kmRecipeCompatibilityRowSchema = z.strictObject({
  actualBaseValue: semanticExploreScalarSchema.nullable(),
  afterValue: kmRecipeScalarSchema,
  currentValue: semanticExploreScalarSchema.nullable(),
  expectedBaseValue: kmRecipeScalarSchema,
  expectedCurrentValue: kmRecipeScalarSchema,
  pendingValue: semanticExploreScalarSchema.nullable(),
  providerId: contractKeySchema,
  reasonCode: contractKeySchema.nullable(),
  rowId: stableIdSchema,
  state: kmRecipeCompatibilityStateSchema,
  target: semanticMergeFieldRefSchema
});

export const kmRecipePreviewRequestSchema = z.strictObject({
  cursor: cursorSchema.nullable(),
  expectedChangeSetETag: changeSetETagSchema.nullable(),
  expectedRevision: semanticExploreRevisionSchema,
  limit: z.number().int().min(1).max(semanticMergeMaximumPageSize),
  proposalFingerprint: fingerprintSchema.nullable(),
  proposalId: fingerprintSchema.nullable(),
  recipeFingerprint: fingerprintSchema,
  recipeInstanceId: recipeInstanceIdSchema,
  scope: semanticExploreScopeSchema
}).superRefine((request, context) => {
  const isContinuation = request.cursor !== null;
  if (
    isContinuation !== (request.proposalId !== null) ||
    isContinuation !== (request.proposalFingerprint !== null)
  ) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'A recipe continuation requires its exact proposal identity.',
      path: ['cursor']
    });
  }
});

export const kmRecipePreviewResponseSchema = z.strictObject({
  authoringContextFingerprint: fingerprintSchema,
  baseSnapshot: semanticExploreSourceSnapshotSchema,
  canImport: z.boolean(),
  compatibility: z.array(kmRecipeCompatibilityRowSchema).max(
    semanticMergeMaximumPageSize
  ).refine(
    (values) => distinct(values, (value) => value.rowId),
    { message: 'Recipe compatibility rows must be unique.' }
  ).refine(
    (values) => distinct(values, (value) => fieldRefKey(value.target)),
    { message: 'Recipe compatibility targets must be unique.' }
  ),
  diagnostics: z.array(apiDiagnosticSchema).max(semanticMergeMaximumDiagnostics),
  layeredSnapshot: semanticExploreSourceSnapshotSchema,
  metadata: kmRecipeMetadataSchema,
  nextCursor: cursorSchema.nullable(),
  pendingSnapshot: semanticExploreSourceSnapshotSchema,
  proposalFingerprint: fingerprintSchema,
  proposalId: fingerprintSchema,
  queryFingerprint: fingerprintSchema,
  recipeFingerprint: fingerprintSchema,
  recipeInstanceId: recipeInstanceIdSchema,
  revision: semanticExploreRevisionSchema,
  totalCompatibilityCount: z.number().int().min(0).max(kmRecipeMaximumOperations),
  totalMutationCount: z.number().int().min(0).max(kmRecipeMaximumOperations)
}).superRefine((response, context) => {
  if (response.compatibility.length > response.totalCompatibilityCount) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Recipe preview returned more rows than its advertised total.',
      path: ['compatibility']
    });
  }
  if (response.canImport && response.totalMutationCount === 0) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'An importable recipe must contain an effective semantic mutation.',
      path: ['canImport']
    });
  }
});

export const kmRecipeImportRequestSchema = z.strictObject({
  changeSetName: trimmedNameSchema,
  expectedChangeSetETag: changeSetETagSchema.nullable(),
  expectedRevision: semanticExploreRevisionSchema,
  proposalFingerprint: fingerprintSchema,
  proposalId: fingerprintSchema,
  recipeFingerprint: fingerprintSchema,
  recipeInstanceId: recipeInstanceIdSchema,
  scope: semanticExploreScopeSchema
});

export const kmRecipeImportResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema).max(semanticMergeMaximumDiagnostics),
  importedChangeSetId: stableIdSchema,
  proposalFingerprint: fingerprintSchema,
  proposalId: fingerprintSchema,
  recipeFingerprint: fingerprintSchema,
  recipeInstanceId: recipeInstanceIdSchema,
  receipt: z.strictObject({
    canRedo: z.boolean(),
    canUndo: z.boolean(),
    document: changeSetWorkspaceDocumentSchema,
    etag: changeSetETagSchema,
    redoLabel: safeTextSchema(256).nullable(),
    undoLabel: safeTextSchema(256).nullable()
  }),
  revision: semanticExploreRevisionSchema
});

export type SemanticMergeFeature = z.infer<typeof semanticMergeFeatureSchema>;
export type SemanticMergeConflictChoice = z.infer<typeof semanticMergeConflictChoiceSchema>;
export type SemanticMergeCapability = z.infer<typeof semanticMergeCapabilitySchema>;
export type SemanticMergeCapabilitiesRequest = z.infer<
  typeof semanticMergeCapabilitiesRequestSchema
>;
export type SemanticMergeCapabilitiesResponse = z.infer<
  typeof semanticMergeCapabilitiesResponseSchema
>;
export type SemanticMergeSourceOpenRequest = z.infer<typeof semanticMergeSourceOpenRequestSchema>;
export type SemanticMergeSourceOpenResponse = z.infer<
  typeof semanticMergeSourceOpenResponseSchema
>;
export type SemanticMergeSource = z.infer<typeof semanticMergeSourceSchema>;
export type SemanticMergeFieldRef = z.infer<typeof semanticMergeFieldRefSchema>;
export type SemanticMergeConflictResolution = z.infer<
  typeof semanticMergeConflictResolutionSchema
>;
export type SemanticMergeRow = z.infer<typeof semanticMergeRowSchema>;
export type SemanticMergePreviewRequest = z.infer<typeof semanticMergePreviewRequestSchema>;
export type SemanticMergePreviewResponse = z.infer<typeof semanticMergePreviewResponseSchema>;
export type SemanticMergeImportRequest = z.infer<typeof semanticMergeImportRequestSchema>;
export type SemanticMergeImportResponse = z.infer<typeof semanticMergeImportResponseSchema>;
export type KmRecipeMetadata = z.infer<typeof kmRecipeMetadataSchema>;
export type KmRecipeArtifact = z.infer<typeof kmRecipeArtifactSchema>;
export type KmRecipeExportRequest = z.infer<typeof kmRecipeExportRequestSchema>;
export type KmRecipeExportResponse = z.infer<typeof kmRecipeExportResponseSchema>;
export type KmRecipeValidateRequest = z.infer<typeof kmRecipeValidateRequestSchema>;
export type KmRecipeValidateResponse = z.infer<typeof kmRecipeValidateResponseSchema>;
export type KmRecipeCompatibilityRow = z.infer<typeof kmRecipeCompatibilityRowSchema>;
export type KmRecipePreviewRequest = z.infer<typeof kmRecipePreviewRequestSchema>;
export type KmRecipePreviewResponse = z.infer<typeof kmRecipePreviewResponseSchema>;
export type KmRecipeImportRequest = z.infer<typeof kmRecipeImportRequestSchema>;
export type KmRecipeImportResponse = z.infer<typeof kmRecipeImportResponseSchema>;

export const semanticMergeContractKeys = {
  exactRevisionKey,
  exactSnapshotKey,
  fieldRefKey,
  recordKey
};
