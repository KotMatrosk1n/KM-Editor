/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from "zod";
import { outputSafetyScopeSchema } from "./outputSafetyContracts";

export const gameplaySettingsMaximumCachedReviews = 16;
export const gameplaySettingsMaximumExperienceRateBasisPoints = 50_000;
export const gameplaySettingsExperienceRateStepBasisPoints = 1_000;

const canonicalGenerationSchema = z.string().regex(/^(?:0|[1-9]\d*)$/u);
const titleIdSchema = z.string().regex(/^[0-9A-F]{16}$/u);
const executableProfileIdSchema = z.string().regex(/^[0-9A-F]{32}$/u);
const supportedGameVersionSchema = z
  .string()
  .regex(/^(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)$/u);
const reviewIdSchema = z.string().regex(/^[a-f0-9]{32}$/u);
const dateTimeOffsetSchema = z
  .string()
  .refine(
    (value) =>
      /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/u.test(
        value,
      ) && Number.isFinite(Date.parse(value)),
    { message: "Expected an ISO 8601 timestamp with an offset." },
  );

export const gameplaySettingsStateSchema = z.enum([
  "missing",
  "ready",
  "incomplete",
  "unmanaged",
  "conflict",
  "unsupported",
  "corrupt",
]);

export const gameplaySettingsValuesSchema = z.strictObject({
  experienceRateBasisPoints: z.number().int().min(0).max(0xffff_ffff),
  experienceShareEnabled: z.boolean(),
  levelCap: z.number().int().min(1).max(100),
  levelCapEnabled: z.boolean(),
});

export const gameplaySettingsCapabilitySchema = z.strictObject({
  available: z.boolean(),
  reasonCode: z.string(),
  scopeCode: z.string(),
});

export const gameplaySettingsSnapshotSchema = z.strictObject({
  executableProfileId: executableProfileIdSchema,
  experienceRateCapability: gameplaySettingsCapabilitySchema,
  experienceShareCapability: gameplaySettingsCapabilitySchema,
  generation: canonicalGenerationSchema,
  hasExperienceRate: z.boolean(),
  hasExperienceShare: z.boolean(),
  hasLevelCap: z.boolean(),
  levelCapCapability: gameplaySettingsCapabilitySchema,
  supportedGameVersion: supportedGameVersionSchema,
  titleId: titleIdSchema,
  values: gameplaySettingsValuesSchema,
});

export const getGameplaySettingsRequestSchema = z.strictObject({
  scope: outputSafetyScopeSchema,
});

export const getGameplaySettingsResponseSchema = z.strictObject({
  detail: z.string().nullable(),
  snapshot: gameplaySettingsSnapshotSchema.nullable(),
  state: gameplaySettingsStateSchema,
});

export const previewGameplaySettingsUpdateRequestSchema = z.strictObject({
  expectedGeneration: canonicalGenerationSchema,
  experienceRateBasisPoints: z
    .number()
    .int()
    .min(0)
    .max(gameplaySettingsMaximumExperienceRateBasisPoints)
    .refine(
      (value) => value % gameplaySettingsExperienceRateStepBasisPoints === 0,
    )
    .nullish(),
  experienceShareEnabled: z.boolean().nullish(),
  levelCap: z.number().int().min(1).max(100).nullish(),
  levelCapEnabled: z.boolean().nullish(),
  scope: outputSafetyScopeSchema,
});

export const previewGameplaySettingsUpdateResponseSchema = z.strictObject({
  after: gameplaySettingsSnapshotSchema,
  before: gameplaySettingsSnapshotSchema,
  expiresAtUtc: dateTimeOffsetSchema,
  reviewId: reviewIdSchema,
});

export const applyGameplaySettingsUpdateRequestSchema = z.strictObject({
  reviewId: reviewIdSchema,
  scope: outputSafetyScopeSchema,
});

export const gameplaySettingsApplyOutcomeSchema = z.enum([
  "committed",
  "rolledBack",
  "recoveryRequired",
]);

export const applyGameplaySettingsUpdateResponseSchema = z.strictObject({
  outcome: gameplaySettingsApplyOutcomeSchema,
  snapshot: gameplaySettingsSnapshotSchema.nullable(),
  transactionId: reviewIdSchema,
});

export type GameplaySettingsState = z.infer<typeof gameplaySettingsStateSchema>;
export type GameplaySettingsCapability = z.infer<
  typeof gameplaySettingsCapabilitySchema
>;
export type GameplaySettingsValues = z.infer<
  typeof gameplaySettingsValuesSchema
>;
export type GameplaySettingsSnapshot = z.infer<
  typeof gameplaySettingsSnapshotSchema
>;
export type GetGameplaySettingsRequest = z.infer<
  typeof getGameplaySettingsRequestSchema
>;
export type GetGameplaySettingsResponse = z.infer<
  typeof getGameplaySettingsResponseSchema
>;
export type PreviewGameplaySettingsUpdateRequest = z.infer<
  typeof previewGameplaySettingsUpdateRequestSchema
>;
export type PreviewGameplaySettingsUpdateResponse = z.infer<
  typeof previewGameplaySettingsUpdateResponseSchema
>;
export type ApplyGameplaySettingsUpdateRequest = z.infer<
  typeof applyGameplaySettingsUpdateRequestSchema
>;
export type ApplyGameplaySettingsUpdateResponse = z.infer<
  typeof applyGameplaySettingsUpdateResponseSchema
>;
