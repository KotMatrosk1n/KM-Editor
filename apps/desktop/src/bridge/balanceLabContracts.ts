/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import { apiDiagnosticSchema } from './contracts';
import {
  semanticExploreRecordRefSchema,
  semanticExploreRevisionSchema,
  semanticExploreScalarSchema,
  semanticExploreScopeSchema,
  semanticExploreSourceSnapshotSchema
} from './semanticExploreContracts';

export const balanceLabDefaultPageSize = 50;
export const balanceLabMaximumPageSize = 100;
export const balanceLabMaximumAccumulatedResults = 500;
export const balanceLabMaximumFindingsPerPage = 100;
export const balanceLabMaximumContinuationStartCount =
  balanceLabMaximumAccumulatedResults - balanceLabMaximumFindingsPerPage;
export const balanceLabMaximumSearchTextLength = 256;

const contractKeySchema = z
  .string()
  .min(1)
  .max(128)
  .regex(/^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$/u);
const stableIdSchema = z
  .string()
  .min(1)
  .max(1_024)
  .refine((value) => (
    value.trim() === value &&
    !/[\u0000-\u001f\u007f-\u009f\u061c\u200b-\u200f\u202a-\u202e\u2060-\u2064\u2066-\u2069\ufeff]/iu.test(value)
  ));
const displayTextSchema = z
  .string()
  .max(8_192)
  .refine((value) => (
    !/[\u0000-\u001f\u007f-\u009f\u061c\u200b-\u200f\u202a-\u202e\u2060-\u2064\u2066-\u2069\ufeff]/iu.test(value)
  ));
const fingerprintSchema = z.string().regex(/^[a-f0-9]{64}$/u);
const cursorSchema = z
  .string()
  .min(1)
  .max(2_048)
  .refine((value) => !/[\u0000-\u001f\u007f-\u009f]/u.test(value));

export const balanceLabStudySchema = z.enum([
  'trainerProgression',
  'encounterDistribution',
  'moveBalance',
  'economy',
  'pokedexEvolution'
]);

export const balanceLabConfidenceSchema = z.enum(['unknown', 'verified', 'derived']);
export const balanceLabCoverageStateSchema = z.enum(['complete', 'partial', 'unavailable']);

export const balanceLabCapabilitySchema = z.strictObject({
  confidence: balanceLabConfidenceSchema,
  providerId: contractKeySchema,
  reasonCode: contractKeySchema.nullable(),
  state: balanceLabCoverageStateSchema,
  study: balanceLabStudySchema
});

export const balanceLabFactSchema = z.strictObject({
  confidence: balanceLabConfidenceSchema,
  evidence: z.array(semanticExploreRecordRefSchema).max(128),
  factId: stableIdSchema,
  label: displayTextSchema,
  providerId: contractKeySchema,
  unit: displayTextSchema.max(128).nullable(),
  value: semanticExploreScalarSchema
});

export const balanceLabPointSchema = z.strictObject({
  facts: z.array(balanceLabFactSchema).max(128),
  label: displayTextSchema,
  pointId: stableIdSchema,
  record: semanticExploreRecordRefSchema,
  seriesKey: contractKeySchema
});

export const balanceLabFindingSchema = z.strictObject({
  confidence: balanceLabConfidenceSchema,
  facts: z.array(balanceLabFactSchema).max(128),
  findingId: stableIdSchema,
  record: semanticExploreRecordRefSchema,
  relatedRecords: z.array(semanticExploreRecordRefSchema).max(128),
  ruleId: contractKeySchema,
  severity: z.enum(['info', 'warning']),
  summary: displayTextSchema,
  title: displayTextSchema
});

export const balanceLabQueryRequestSchema = z.strictObject({
  searchText: displayTextSchema.max(256).optional(),
  metric: displayTextSchema.max(1_024).optional(),
  catalogOnly: z.boolean().optional(),
  pointId: stableIdSchema.optional(),
  cursor: cursorSchema.optional(),
  expectedRevision: semanticExploreRevisionSchema,
  layer: z.enum(['base', 'layered', 'pending']),
  limit: z.number().int().min(1).max(balanceLabMaximumPageSize),
  scope: semanticExploreScopeSchema,
  study: balanceLabStudySchema
});

export const balanceLabQueryResponseSchema = z.strictObject({
  metrics: z.array(z.strictObject({
    identity: displayTextSchema.max(1_024),
    key: stableIdSchema,
    label: displayTextSchema,
    providerId: contractKeySchema,
    supportCount: z.number().int().nonnegative().max(400_000),
    unit: displayTextSchema.max(128).nullable()
  })).max(512).optional(),
  totalPointCount: z.number().int().nonnegative().max(400_000).optional(),
  capabilities: z.array(balanceLabCapabilitySchema).max(5),
  diagnostics: z.array(apiDiagnosticSchema).max(512),
  findings: z.array(balanceLabFindingSchema).max(balanceLabMaximumFindingsPerPage),
  nextCursor: cursorSchema.nullable(),
  points: z.array(balanceLabPointSchema).max(balanceLabMaximumPageSize),
  queryFingerprint: fingerprintSchema,
  revision: semanticExploreRevisionSchema,
  snapshot: semanticExploreSourceSnapshotSchema
});

export type BalanceLabStudy = z.infer<typeof balanceLabStudySchema>;
export type BalanceLabConfidence = z.infer<typeof balanceLabConfidenceSchema>;
export type BalanceLabCoverageState = z.infer<typeof balanceLabCoverageStateSchema>;
export type BalanceLabCapability = z.infer<typeof balanceLabCapabilitySchema>;
export type BalanceLabFact = z.infer<typeof balanceLabFactSchema>;
export type BalanceLabPoint = z.infer<typeof balanceLabPointSchema>;
export type BalanceLabFinding = z.infer<typeof balanceLabFindingSchema>;
export type BalanceLabQueryRequest = z.infer<typeof balanceLabQueryRequestSchema>;
export type BalanceLabQueryResponse = z.infer<typeof balanceLabQueryResponseSchema>;
