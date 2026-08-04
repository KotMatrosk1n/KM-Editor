/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import { projectPathsSchema } from './contracts';

export const swShCacheModeSchema = z.enum(['minimal', 'balanced', 'performance']);
export type SwShCacheMode = z.infer<typeof swShCacheModeSchema>;

export const swShCacheSettingsSchema = z.strictObject({
  maxCacheSizeBytes: z.number(),
  mode: swShCacheModeSchema
});
export type SwShCacheSettings = z.infer<typeof swShCacheSettingsSchema>;

export const swShCacheStatusSchema = z.strictObject({
  cacheSizeBytes: z.number(),
  isActiveProjectPreserved: z.boolean(),
  message: z.string(),
  phase: z.string(),
  progressPercent: z.number(),
  settings: swShCacheSettingsSchema,
  warmupCompleted: z.number(),
  warmupTotal: z.number()
});
export type SwShCacheStatus = z.infer<typeof swShCacheStatusSchema>;

export const getSwShCacheStatusRequestSchema = z.strictObject({
  paths: projectPathsSchema.nullable().optional()
});
export type GetSwShCacheStatusRequest = z.infer<typeof getSwShCacheStatusRequestSchema>;

export const updateSwShCacheSettingsRequestSchema = z.strictObject({
  maxCacheSizeBytes: z.number(),
  mode: swShCacheModeSchema,
  paths: projectPathsSchema.nullable().optional()
});
export type UpdateSwShCacheSettingsRequest = z.infer<
  typeof updateSwShCacheSettingsRequestSchema
>;

export const clearSwShCacheRequestSchema = z.strictObject({
  activePaths: projectPathsSchema.nullable().optional()
});
export type ClearSwShCacheRequest = z.infer<typeof clearSwShCacheRequestSchema>;

export const warmupSwShCacheStepRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  stepIndex: z.number()
});
export type WarmupSwShCacheStepRequest = z.infer<typeof warmupSwShCacheStepRequestSchema>;

export const swShCacheStatusResponseSchema = z.strictObject({
  status: swShCacheStatusSchema
});
export type SwShCacheStatusResponse = z.infer<typeof swShCacheStatusResponseSchema>;
