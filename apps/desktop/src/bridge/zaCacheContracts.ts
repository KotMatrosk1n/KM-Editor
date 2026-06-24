/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import { projectPathsSchema } from './contracts';

export const zaCacheModeSchema = z.enum(['minimal', 'balanced', 'performance']);
export type ZaCacheMode = z.infer<typeof zaCacheModeSchema>;

export const zaCacheSettingsSchema = z.strictObject({
  maxCacheSizeBytes: z.number(),
  mode: zaCacheModeSchema
});
export type ZaCacheSettings = z.infer<typeof zaCacheSettingsSchema>;

export const zaCacheStatusSchema = z.strictObject({
  cacheSizeBytes: z.number(),
  isActiveProjectPreserved: z.boolean(),
  message: z.string(),
  phase: z.string(),
  progressPercent: z.number(),
  settings: zaCacheSettingsSchema,
  warmupCompleted: z.number(),
  warmupTotal: z.number()
});
export type ZaCacheStatus = z.infer<typeof zaCacheStatusSchema>;

export const getZaCacheStatusRequestSchema = z.strictObject({
  paths: projectPathsSchema.nullable().optional()
});
export type GetZaCacheStatusRequest = z.infer<typeof getZaCacheStatusRequestSchema>;

export const updateZaCacheSettingsRequestSchema = z.strictObject({
  maxCacheSizeBytes: z.number(),
  mode: zaCacheModeSchema,
  paths: projectPathsSchema.nullable().optional()
});
export type UpdateZaCacheSettingsRequest = z.infer<typeof updateZaCacheSettingsRequestSchema>;

export const clearZaCacheRequestSchema = z.strictObject({
  activePaths: projectPathsSchema.nullable().optional()
});
export type ClearZaCacheRequest = z.infer<typeof clearZaCacheRequestSchema>;

export const warmupZaCacheStepRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  stepIndex: z.number()
});
export type WarmupZaCacheStepRequest = z.infer<typeof warmupZaCacheStepRequestSchema>;

export const zaCacheStatusResponseSchema = z.strictObject({
  status: zaCacheStatusSchema
});
export type ZaCacheStatusResponse = z.infer<typeof zaCacheStatusResponseSchema>;
