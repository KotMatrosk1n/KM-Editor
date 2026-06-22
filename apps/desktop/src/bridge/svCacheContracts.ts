/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import { projectPathsSchema } from './contracts';

export const svCacheModeSchema = z.enum(['minimal', 'balanced', 'performance']);
export type SvCacheMode = z.infer<typeof svCacheModeSchema>;

export const svCacheSettingsSchema = z.strictObject({
  maxCacheSizeBytes: z.number(),
  mode: svCacheModeSchema
});
export type SvCacheSettings = z.infer<typeof svCacheSettingsSchema>;

export const svCacheStatusSchema = z.strictObject({
  cacheSizeBytes: z.number(),
  isActiveProjectPreserved: z.boolean(),
  message: z.string(),
  phase: z.string(),
  progressPercent: z.number(),
  settings: svCacheSettingsSchema,
  warmupCompleted: z.number(),
  warmupTotal: z.number()
});
export type SvCacheStatus = z.infer<typeof svCacheStatusSchema>;

export const getSvCacheStatusRequestSchema = z.strictObject({
  paths: projectPathsSchema.nullable().optional()
});
export type GetSvCacheStatusRequest = z.infer<typeof getSvCacheStatusRequestSchema>;

export const updateSvCacheSettingsRequestSchema = z.strictObject({
  maxCacheSizeBytes: z.number(),
  mode: svCacheModeSchema,
  paths: projectPathsSchema.nullable().optional()
});
export type UpdateSvCacheSettingsRequest = z.infer<typeof updateSvCacheSettingsRequestSchema>;

export const clearSvCacheRequestSchema = z.strictObject({
  activePaths: projectPathsSchema.nullable().optional()
});
export type ClearSvCacheRequest = z.infer<typeof clearSvCacheRequestSchema>;

export const warmupSvCacheStepRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  stepIndex: z.number()
});
export type WarmupSvCacheStepRequest = z.infer<typeof warmupSvCacheStepRequestSchema>;

export const svCacheStatusResponseSchema = z.strictObject({
  status: svCacheStatusSchema
});
export type SvCacheStatusResponse = z.infer<typeof svCacheStatusResponseSchema>;
