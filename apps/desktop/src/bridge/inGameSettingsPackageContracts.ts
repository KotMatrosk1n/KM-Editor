/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import { outputSafetyScopeSchema } from './outputSafetyContracts';

export const inGameSettingsPackageMaximumReturnedTargets = 512;
export const inGameSettingsPackageMaximumReturnedReadDependencies = 16;

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

export const inGameSettingsExecutableInputSourceSchema = z.enum([
  'none',
  'base',
  'standaloneOutput'
]);

export const inGameSettingsExecutableCompatibilitySchema = z.enum([
  'absent',
  'retailEquivalent',
  'compatiblePreservable',
  'incompatibleOwnedRegion',
  'unsupportedBuild',
  'unreadableOrAmbiguous'
]);

export const inGameSettingsPackageReadDependencyRoleSchema = z.enum([
  'staticExecutableGuard',
  'executableCompositionSource'
]);

export const inGameSettingsExecutableCompositionStrategySchema = z.enum([
  'stockPackage',
  'retailEquivalentStandalone',
  'compatibleStandalone'
]);

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

export const inGameSettingsExecutableInputAssessmentSchema = z.strictObject({
  compatibility: inGameSettingsExecutableCompatibilitySchema,
  reasonCode: z
    .string()
    .min(1)
    .max(96)
    .regex(/^[a-z0-9]+(?:-[a-z0-9]+)*$/u),
  source: inGameSettingsExecutableInputSourceSchema,
  sourceLengthBytes: z.number().int().min(0).max(Number.MAX_SAFE_INTEGER).nullable(),
  sourceRelativePath: relativeOutputPathSchema.nullable(),
  sourceSha256: sha256Schema.nullable()
}).superRefine((assessment, context) => {
  const hasCompleteFingerprint =
    assessment.sourceSha256 !== null && assessment.sourceLengthBytes !== null;
  if ((assessment.sourceSha256 !== null) !== (assessment.sourceLengthBytes !== null)) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Executable input fingerprints must contain both SHA-256 and byte length.'
    });
  }
  if (
    (assessment.compatibility === 'retailEquivalent' ||
      assessment.compatibility === 'compatiblePreservable') &&
    (assessment.source !== 'standaloneOutput' ||
      assessment.sourceRelativePath === null ||
      !hasCompleteFingerprint)
  ) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Compatible standalone inputs require one complete, path-bound fingerprint.'
    });
  }
  if (
    assessment.compatibility === 'incompatibleOwnedRegion' &&
    assessment.source !== 'standaloneOutput'
  ) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Owned-region conflicts require a standalone output source.'
    });
  }
});

export const inGameSettingsPackageSnapshotSchema = z.strictObject({
  availablePackage: inGameSettingsPackageDescriptorSchema.nullable(),
  blocksStaticEditor: z.boolean(),
  detail: z.string().nullable(),
  executableInput: inGameSettingsExecutableInputAssessmentSchema,
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

export const inGameSettingsPackageReadDependencySchema = z.strictObject({
  exists: z.boolean(),
  lengthBytes: z.number().int().min(0).max(Number.MAX_SAFE_INTEGER).nullable(),
  preserved: z.boolean(),
  relativePath: relativeOutputPathSchema,
  role: inGameSettingsPackageReadDependencyRoleSchema,
  sha256: sha256Schema.nullable()
}).superRefine((dependency, context) => {
  if (dependency.exists !== (dependency.sha256 !== null && dependency.lengthBytes !== null)) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Existing read dependencies require one complete fingerprint; missing dependencies require none.'
    });
  }
});

export const inGameSettingsExecutableCompositionSchema = z.strictObject({
  destinationRelativePath: relativeOutputPathSchema,
  ownedRegionCount: z.number().int().min(0).max(4_096),
  preservesBytesOutsideOwnedRegions: z.boolean(),
  sourcePreserved: z.boolean(),
  strategy: inGameSettingsExecutableCompositionStrategySchema
});

export const previewInGameSettingsPackageResponseSchema = z.strictObject({
  before: inGameSettingsPackageSnapshotSchema,
  composition: inGameSettingsExecutableCompositionSchema.nullable(),
  expiresAtUtc: dateTimeOffsetSchema,
  operation: inGameSettingsPackageOperationSchema,
  readDependencies: z
    .array(inGameSettingsPackageReadDependencySchema)
    .max(inGameSettingsPackageMaximumReturnedReadDependencies),
  readDependenciesTruncated: z.boolean(),
  reviewId: reviewIdSchema,
  targets: z
    .array(inGameSettingsPackageTargetSchema)
    .max(inGameSettingsPackageMaximumReturnedTargets),
  targetsTruncated: z.boolean()
}).superRefine((preview, context) => {
  if (preview.operation === 'remove' && preview.composition !== null) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Removal reviews cannot claim an executable composition operation.'
    });
  }
  if (preview.operation !== 'remove' && preview.composition === null) {
    context.addIssue({
      code: z.ZodIssueCode.custom,
      message: 'Install and upgrade reviews require executable composition facts.'
    });
  }
  if (preview.composition?.strategy === 'compatibleStandalone') {
    const source = preview.readDependencies.find(
      (dependency) => dependency.role === 'executableCompositionSource'
    );
    if (
      !preview.composition.sourcePreserved ||
      !preview.composition.preservesBytesOutsideOwnedRegions ||
      preview.composition.ownedRegionCount === 0 ||
      !source?.exists ||
      !source.preserved
    ) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'Compatible composition requires a preserved fingerprinted source and explicit owned-region guarantees.'
      });
    }
  }
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
export type InGameSettingsExecutableInputAssessment = z.infer<
  typeof inGameSettingsExecutableInputAssessmentSchema
>;
export type InGameSettingsPackageReadDependency = z.infer<
  typeof inGameSettingsPackageReadDependencySchema
>;
export type InGameSettingsExecutableComposition = z.infer<
  typeof inGameSettingsExecutableCompositionSchema
>;
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
