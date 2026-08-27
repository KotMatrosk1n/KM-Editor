/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import { outputSafetyScopeSchema } from './outputSafetyContracts';

export const inGameSettingsPackageMaximumReturnedTargets = 512;

const uint32Schema = z.number().int().min(0).max(0xffff_ffff);
const titleIdSchema = z.string().regex(/^[0-9A-F]{16}$/u);
const buildIdSchema = z.string().regex(/^[0-9A-F]{64}$/u);
const bundleIdSchema = z.string().regex(/^[0-9A-F]{32}$/u);
const sha256Schema = z.string().regex(/^[a-f0-9]{64}$/u);
const reviewIdSchema = z.string().regex(/^[a-f0-9]{32}$/u);
const gameVersionSchema = z
  .string()
  .regex(/^(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)$/u);
const dateTimeOffsetSchema = z.string().refine(
  (value) =>
    /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/u.test(value) &&
    Number.isFinite(Date.parse(value)),
  { message: 'Expected an ISO 8601 timestamp with an offset.' }
);
const relativeOutputPathSchema = z
  .string()
  .min(1)
  .max(1_024)
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

export const inGameSettingsPackageStateSchema = z.enum([
  'unavailable',
  'notInstalled',
  'installed',
  'upgradeAvailable',
  'coexistenceConflict',
  'incomplete',
  'unmanaged',
  'conflict',
  'corrupt'
]);

export const inGameSettingsPackageOperationSchema = z.enum([
  'install',
  'upgrade',
  'remove'
]);

export const inGameSettingsPackageTargetOperationSchema = z.enum(['write', 'delete']);

export const inGameSettingsPackageVersionSchema = z.strictObject({
  major: uint32Schema,
  minor: uint32Schema,
  patch: uint32Schema
});

export const inGameSettingsPackageDescriptorSchema = z.strictObject({
  archiveSha256: sha256Schema,
  buildId: buildIdSchema,
  bundleId: bundleIdSchema,
  packageVersion: inGameSettingsPackageVersionSchema,
  supportedGameVersion: gameVersionSchema,
  targetCount: z.number().int().min(0).max(4_098),
  titleId: titleIdSchema
});

export const inGameSettingsPackageSnapshotSchema = z.strictObject({
  availablePackage: inGameSettingsPackageDescriptorSchema.nullable(),
  detail: z.string().nullable(),
  installedPackage: inGameSettingsPackageDescriptorSchema.nullable(),
  packageAvailable: z.boolean(),
  revision: sha256Schema,
  state: inGameSettingsPackageStateSchema
});

export const inspectInGameSettingsPackageRequestSchema = z.strictObject({
  scope: outputSafetyScopeSchema
});

export const inspectInGameSettingsPackageResponseSchema = z.strictObject({
  snapshot: inGameSettingsPackageSnapshotSchema
});

export const previewInGameSettingsPackageRequestSchema = z.strictObject({
  expectedRevision: sha256Schema,
  operation: inGameSettingsPackageOperationSchema,
  scope: outputSafetyScopeSchema
});

export const inGameSettingsPackageTargetSchema = z.strictObject({
  operation: inGameSettingsPackageTargetOperationSchema,
  relativePath: relativeOutputPathSchema
});

export const previewInGameSettingsPackageResponseSchema = z.strictObject({
  before: inGameSettingsPackageSnapshotSchema,
  expiresAtUtc: dateTimeOffsetSchema,
  operation: inGameSettingsPackageOperationSchema,
  reviewId: reviewIdSchema,
  targets: z
    .array(inGameSettingsPackageTargetSchema)
    .max(inGameSettingsPackageMaximumReturnedTargets),
  targetsTruncated: z.boolean()
});

export const applyInGameSettingsPackageRequestSchema = z.strictObject({
  reviewId: reviewIdSchema,
  scope: outputSafetyScopeSchema
});

export const inGameSettingsPackageApplyOutcomeSchema = z.enum([
  'committed',
  'rolledBack',
  'recoveryRequired'
]);

export const applyInGameSettingsPackageResponseSchema = z.strictObject({
  outcome: inGameSettingsPackageApplyOutcomeSchema,
  snapshot: inGameSettingsPackageSnapshotSchema.nullable(),
  transactionId: reviewIdSchema
});

export type InGameSettingsPackageState = z.infer<typeof inGameSettingsPackageStateSchema>;
export type InGameSettingsPackageOperation = z.infer<typeof inGameSettingsPackageOperationSchema>;
export type InGameSettingsPackageVersion = z.infer<typeof inGameSettingsPackageVersionSchema>;
export type InGameSettingsPackageDescriptor = z.infer<typeof inGameSettingsPackageDescriptorSchema>;
export type InGameSettingsPackageSnapshot = z.infer<typeof inGameSettingsPackageSnapshotSchema>;
export type InspectInGameSettingsPackageRequest = z.infer<
  typeof inspectInGameSettingsPackageRequestSchema
>;
export type InspectInGameSettingsPackageResponse = z.infer<
  typeof inspectInGameSettingsPackageResponseSchema
>;
export type PreviewInGameSettingsPackageRequest = z.infer<
  typeof previewInGameSettingsPackageRequestSchema
>;
export type PreviewInGameSettingsPackageResponse = z.infer<
  typeof previewInGameSettingsPackageResponseSchema
>;
export type ApplyInGameSettingsPackageRequest = z.infer<
  typeof applyInGameSettingsPackageRequestSchema
>;
export type ApplyInGameSettingsPackageResponse = z.infer<
  typeof applyInGameSettingsPackageResponseSchema
>;
