/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import {
  editSessionSchema,
  projectGameSchema,
  projectPathsSchema
} from './contracts';
import { workspaceProjectIdSchema } from './workspacePersonalStateContracts';

export const semanticExploreMaximumPageSize = 100;
export const semanticExploreDefaultPageSize = 50;
export const semanticExploreMaximumSearchTextLength = 256;
export const semanticExploreMaximumContinuationTokenLength = 2_048;
export const semanticExploreMaximumExternalPathLength = 4_096;

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
  ));
const fingerprintSchema = z.string().regex(/^[a-f0-9]{64}$/u);
const nonnegativeInt64Schema = z
  .string()
  .regex(/^(?:0|[1-9][0-9]{0,18})$/u)
  .refine((value) => BigInt(value) <= 9_223_372_036_854_775_807n);
const cursorSchema = z
  .string()
  .min(1)
  .max(semanticExploreMaximumContinuationTokenLength)
  .refine((value) => !/[\u0000-\u001f\u007f-\u009f]/u.test(value));
const pageLimitSchema = z.number().int().min(1).max(semanticExploreMaximumPageSize);

export const semanticExploreGameFamilySchema = z.enum([
  'swordShield',
  'scarletViolet',
  'legendsZA'
]);

export const semanticExploreRecordRefSchema = z.strictObject({
  domain: contractKeySchema,
  gameFamily: semanticExploreGameFamilySchema,
  recordId: stableIdSchema,
  recordKind: z.strictObject({
    key: contractKeySchema,
    schemaVersion: z.number().int().positive()
  }),
  subrecordId: stableIdSchema.nullable()
});

export const semanticExploreRevisionSchema = z.strictObject({
  fingerprint: fingerprintSchema,
  gameFamily: semanticExploreGameFamilySchema,
  generation: nonnegativeInt64Schema,
  projectId: workspaceProjectIdSchema
});

export const semanticExploreLayerKindSchema = z.enum([
  'base',
  'layered',
  'pending',
  'comparedMod'
]);

export const semanticExploreSourceSnapshotSchema = z
  .strictObject({
    fingerprint: fingerprintSchema,
    layer: z.strictObject({
      instanceId: stableIdSchema.nullable(),
      kind: semanticExploreLayerKindSchema
    }),
    revision: semanticExploreRevisionSchema
  })
  .superRefine((snapshot, context) => {
    const needsInstanceId = snapshot.layer.kind === 'comparedMod';
    if (needsInstanceId !== (snapshot.layer.instanceId !== null)) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'The source-layer instance does not match its layer kind.',
        path: ['layer', 'instanceId']
      });
    }
  });

export const semanticExploreCoverageSchema = z.strictObject({
  confidence: z.enum(['verified', 'derived', 'unknown']),
  domains: z.array(contractKeySchema).max(128),
  providerId: contractKeySchema,
  reasonCode: contractKeySchema.nullable(),
  state: z.enum(['complete', 'partial', 'unavailable'])
});

export const semanticExploreFeatureSchema = z.enum([
  'search',
  'entity',
  'compare',
  'references',
  'impact',
  'ownership',
  'externalCompare',
  'changes'
]);

export const semanticExploreDifferenceKindSchema = z.enum([
  'added',
  'removed',
  'reordered',
  'changed',
  'inherited',
  'unavailable',
  'undecodable'
]);

export const semanticExploreScopeSchema = z.strictObject({
  paths: projectPathsSchema,
  pendingSession: editSessionSchema.nullable().optional(),
  projectId: workspaceProjectIdSchema
});

export const semanticExploreCapabilitiesRequestSchema = z.strictObject({
  scope: semanticExploreScopeSchema
});

export const semanticExploreCapabilitiesSchema = z.strictObject({
  providers: z.array(z.strictObject({
    coverage: semanticExploreCoverageSchema,
    domains: z.array(contractKeySchema).max(128),
    features: z.array(semanticExploreFeatureSchema).max(8),
    providerId: contractKeySchema
  })).max(128),
  revision: semanticExploreRevisionSchema,
  snapshots: z.array(semanticExploreSourceSnapshotSchema).max(4)
});

const semanticExploreRevisionRequestBaseSchema = z.strictObject({
  expectedRevision: semanticExploreRevisionSchema,
  scope: semanticExploreScopeSchema
});

const queryLayerSchema = z.enum(['base', 'layered', 'pending']);
const pagedRequestFields = {
  cursor: cursorSchema.optional(),
  limit: pageLimitSchema
} as const;

export const semanticExploreSearchRequestSchema = z.strictObject({
  ...semanticExploreRevisionRequestBaseSchema.shape,
  ...pagedRequestFields,
  domains: z.array(contractKeySchema).max(16).optional(),
  layer: queryLayerSchema,
  searchText: z.string().min(1).max(semanticExploreMaximumSearchTextLength)
});

export const semanticExploreSearchItemSchema = z.strictObject({
  changeKind: semanticExploreDifferenceKindSchema.nullable(),
  description: boundedDisplayTextSchema.nullable(),
  displayName: boundedDisplayTextSchema,
  domainLabel: boundedDisplayTextSchema,
  record: semanticExploreRecordRefSchema,
  snapshot: semanticExploreSourceSnapshotSchema
});

export const semanticExploreSearchPageSchema = revisionBoundPageSchema(
  semanticExploreSearchItemSchema
);

export const semanticExploreScalarSchema = z.strictObject({
  canonicalValue: z.string().max(32 * 1_024).nullable(),
  displayValue: boundedDisplayTextSchema,
  kind: z.enum([
    'boolean',
    'signedInteger',
    'unsignedInteger',
    'decimal',
    'text',
    'enum',
    'null'
  ])
});

export const semanticExploreEntityRequestSchema = z.strictObject({
  ...semanticExploreRevisionRequestBaseSchema.shape,
  layer: queryLayerSchema,
  record: semanticExploreRecordRefSchema
});

export const semanticExploreEntitySchema = z.strictObject({
  coverage: z.array(semanticExploreCoverageSchema).max(128),
  entity: z.strictObject({
    features: z.strictObject({
      compare: z.boolean(),
      impact: z.boolean(),
      ownership: z.boolean(),
      references: z.boolean()
    }),
    fields: z.array(z.strictObject({
      group: boundedDisplayTextSchema,
      key: fieldKeySchema,
      label: boundedDisplayTextSchema,
      ownerId: contractKeySchema,
      value: semanticExploreScalarSchema
    })).max(512),
    record: semanticExploreRecordRefSchema,
    snapshot: semanticExploreSourceSnapshotSchema,
    summary: boundedDisplayTextSchema.nullable(),
    title: boundedDisplayTextSchema
  }),
  queryFingerprint: fingerprintSchema,
  revision: semanticExploreRevisionSchema
});

export const semanticExploreCompareRequestSchema = z.strictObject({
  ...semanticExploreRevisionRequestBaseSchema.shape,
  ...pagedRequestFields,
  left: queryLayerSchema,
  record: semanticExploreRecordRefSchema.optional(),
  right: queryLayerSchema
});

export const semanticExploreDifferenceSchema = z.strictObject({
  fieldKey: fieldKeySchema,
  kind: semanticExploreDifferenceKindSchema,
  label: boundedDisplayTextSchema,
  left: semanticExploreScalarSchema.nullable(),
  ownerId: contractKeySchema,
  record: semanticExploreRecordRefSchema,
  right: semanticExploreScalarSchema.nullable()
});

export const semanticExploreComparisonPageSchema = revisionBoundPageSchema(
  semanticExploreDifferenceSchema,
  {
    leftSnapshot: semanticExploreSourceSnapshotSchema,
    rightSnapshot: semanticExploreSourceSnapshotSchema
  }
);

export const semanticExploreReferencesRequestSchema = z.strictObject({
  ...semanticExploreRevisionRequestBaseSchema.shape,
  ...pagedRequestFields,
  direction: z.enum(['incoming', 'outgoing']),
  layer: queryLayerSchema,
  record: semanticExploreRecordRefSchema
});

export const semanticExploreReferenceSchema = z.strictObject({
  confidence: z.enum(['verified', 'derived', 'unknown']),
  providerId: contractKeySchema,
  relationshipKey: contractKeySchema,
  relationshipLabel: boundedDisplayTextSchema,
  snapshot: semanticExploreSourceSnapshotSchema,
  source: semanticExploreRecordRefSchema,
  sourceTitle: boundedDisplayTextSchema,
  target: semanticExploreRecordRefSchema,
  targetTitle: boundedDisplayTextSchema
});

export const semanticExploreReferencesPageSchema = revisionBoundPageSchema(
  semanticExploreReferenceSchema
);

export const semanticExploreImpactRequestSchema = z.strictObject({
  ...semanticExploreRevisionRequestBaseSchema.shape,
  ...pagedRequestFields,
  layer: queryLayerSchema,
  record: semanticExploreRecordRefSchema
});

export const semanticExploreImpactSchema = z.strictObject({
  actionability: z.literal('readOnly'),
  count: z.number().int().nonnegative(),
  relationshipKey: contractKeySchema,
  severity: z.enum(['info', 'warning']),
  sourceDomain: contractKeySchema,
  summary: boundedDisplayTextSchema
});

export const semanticExploreImpactPageSchema = revisionBoundPageSchema(
  semanticExploreImpactSchema
);

export const semanticExploreOwnershipRequestSchema = z.strictObject({
  ...semanticExploreRevisionRequestBaseSchema.shape,
  ...pagedRequestFields,
  record: semanticExploreRecordRefSchema.optional()
});

export const semanticExploreOwnershipPageSchema = z.strictObject({
  conflicts: z.array(z.strictObject({
    conflictId: stableIdSchema,
    label: boundedDisplayTextSchema,
    nodeIds: z.array(stableIdSchema).min(2).max(32),
    severity: z.enum(['info', 'warning'])
  })).max(semanticExploreMaximumPageSize),
  coverage: z.array(semanticExploreCoverageSchema).max(128),
  edges: z.array(z.strictObject({
    kind: z.enum(['owns', 'references', 'conflicts', 'targets']),
    sourceNodeId: stableIdSchema,
    targetNodeId: stableIdSchema
  })).max(semanticExploreMaximumPageSize),
  nextCursor: cursorSchema.nullable(),
  nodes: z.array(z.strictObject({
    kind: z.enum(['entity', 'provider', 'file', 'pendingOperation']),
    label: boundedDisplayTextSchema,
    nodeId: stableIdSchema,
    ownerId: contractKeySchema.nullable(),
    record: semanticExploreRecordRefSchema.nullable()
  })).max(semanticExploreMaximumPageSize),
  queryFingerprint: fingerprintSchema,
  revision: semanticExploreRevisionSchema
});

export const semanticExploreExternalCompareRequestSchema = z
  .strictObject({
    ...semanticExploreRevisionRequestBaseSchema.shape,
    ...pagedRequestFields,
    comparedModInstanceId: stableIdSchema.nullable().optional(),
    externalRootPath: z
      .string()
      .min(1)
      .max(semanticExploreMaximumExternalPathLength)
      .nullable()
      .optional(),
    left: queryLayerSchema,
    record: semanticExploreRecordRefSchema.optional()
  })
  .superRefine((request, context) => {
    const hasPath = request.externalRootPath != null;
    const hasInstance = request.comparedModInstanceId != null;
    if (hasPath === hasInstance) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'External comparison requires either a new path or an opaque instance.',
        path: ['externalRootPath']
      });
    }
    if (hasPath === (request.cursor !== undefined)) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'Only an opaque external comparison continuation can carry a cursor.',
        path: ['cursor']
      });
    }
  });

export const semanticExploreChangesRequestSchema = z.strictObject({
  ...semanticExploreRevisionRequestBaseSchema.shape,
  ...pagedRequestFields,
  format: z.enum(['structured', 'canonicalText']),
  from: z.enum(['base', 'layered']),
  to: z.enum(['layered', 'pending'])
});

export const semanticExploreChangeSchema = z.strictObject({
  after: semanticExploreScalarSchema.nullable(),
  before: semanticExploreScalarSchema.nullable(),
  fieldKey: fieldKeySchema,
  kind: semanticExploreDifferenceKindSchema,
  line: z.string().max(32 * 1_024),
  path: z
    .string()
    .min(1)
    .max(4_096)
    .refine((value) => (
      value.trim() === value &&
      !value.includes('\\') &&
      !value.startsWith('/') &&
      value.split('/').every((segment) => segment.length > 0 && segment !== '.' && segment !== '..')
    )),
  record: semanticExploreRecordRefSchema
});

export const semanticExploreChangesPageSchema = revisionBoundPageSchema(
  semanticExploreChangeSchema
);

function revisionBoundPageSchema<TItem extends z.ZodTypeAny, TExtra extends z.ZodRawShape = {}>(
  itemSchema: TItem,
  extra?: TExtra
) {
  return z.strictObject({
    coverage: z.array(semanticExploreCoverageSchema).max(128),
    items: z.array(itemSchema).max(semanticExploreMaximumPageSize),
    nextCursor: cursorSchema.nullable(),
    queryFingerprint: fingerprintSchema,
    revision: semanticExploreRevisionSchema,
    ...(extra ?? {} as TExtra)
  });
}

export type SemanticExploreScope = z.infer<typeof semanticExploreScopeSchema>;
export type SemanticExploreRevision = z.infer<typeof semanticExploreRevisionSchema>;
export type SemanticExploreLayerKind = z.infer<typeof semanticExploreLayerKindSchema>;
export type SemanticExploreSourceSnapshot = z.infer<typeof semanticExploreSourceSnapshotSchema>;
export type SemanticExploreCoverage = z.infer<typeof semanticExploreCoverageSchema>;
export type SemanticExploreFeature = z.infer<typeof semanticExploreFeatureSchema>;
export type SemanticExploreRecordRef = z.infer<typeof semanticExploreRecordRefSchema>;
export type SemanticExploreCapabilitiesRequest = z.infer<typeof semanticExploreCapabilitiesRequestSchema>;
export type SemanticExploreCapabilities = z.infer<typeof semanticExploreCapabilitiesSchema>;
export type SemanticExploreSearchRequest = z.infer<typeof semanticExploreSearchRequestSchema>;
export type SemanticExploreSearchItem = z.infer<typeof semanticExploreSearchItemSchema>;
export type SemanticExploreSearchPage = z.infer<typeof semanticExploreSearchPageSchema>;
export type SemanticExploreScalar = z.infer<typeof semanticExploreScalarSchema>;
export type SemanticExploreEntityRequest = z.infer<typeof semanticExploreEntityRequestSchema>;
export type SemanticExploreEntity = z.infer<typeof semanticExploreEntitySchema>;
export type SemanticExploreCompareRequest = z.infer<typeof semanticExploreCompareRequestSchema>;
export type SemanticExploreDifference = z.infer<typeof semanticExploreDifferenceSchema>;
export type SemanticExploreComparisonPage = z.infer<typeof semanticExploreComparisonPageSchema>;
export type SemanticExploreReferencesRequest = z.infer<typeof semanticExploreReferencesRequestSchema>;
export type SemanticExploreReference = z.infer<typeof semanticExploreReferenceSchema>;
export type SemanticExploreReferencesPage = z.infer<typeof semanticExploreReferencesPageSchema>;
export type SemanticExploreImpactRequest = z.infer<typeof semanticExploreImpactRequestSchema>;
export type SemanticExploreImpact = z.infer<typeof semanticExploreImpactSchema>;
export type SemanticExploreImpactPage = z.infer<typeof semanticExploreImpactPageSchema>;
export type SemanticExploreOwnershipRequest = z.infer<typeof semanticExploreOwnershipRequestSchema>;
export type SemanticExploreOwnershipPage = z.infer<typeof semanticExploreOwnershipPageSchema>;
export type SemanticExploreExternalCompareRequest = z.infer<typeof semanticExploreExternalCompareRequestSchema>;
export type SemanticExploreChangesRequest = z.infer<typeof semanticExploreChangesRequestSchema>;
export type SemanticExploreChange = z.infer<typeof semanticExploreChangeSchema>;
export type SemanticExploreChangesPage = z.infer<typeof semanticExploreChangesPageSchema>;

export function semanticExploreProjectGameFamily(
  game: z.infer<typeof projectGameSchema>
): SemanticExploreRevision['gameFamily'] {
  switch (game) {
    case 'sword':
    case 'shield':
      return 'swordShield';
    case 'scarlet':
    case 'violet':
      return 'scarletViolet';
    case 'za':
      return 'legendsZA';
  }
}
