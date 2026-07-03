/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import {
  apiDiagnosticSchema,
  applyResultSchema,
  projectGameSchema,
  projectPathsSchema
} from './contracts';

export const loadProfanityFilterRequestSchema = z.strictObject({ paths: projectPathsSchema });
export const applyProfanityFilterRequestSchema = z.strictObject({ paths: projectPathsSchema });
export const restoreProfanityFilterRequestSchema = z.strictObject({ paths: projectPathsSchema });

export const profanityFilterStatusSchema = z.strictObject({
  buildId: z.string().nullable(),
  detectedGame: projectGameSchema.nullable(),
  diagnostics: z.array(apiDiagnosticSchema),
  message: z.string(),
  patchOffsetHex: z.string(),
  patchShape: z.string(),
  sourceLayer: z.string(),
  status: z.string()
});

export const loadProfanityFilterResponseSchema = z.strictObject({
  status: profanityFilterStatusSchema
});
export const applyProfanityFilterResponseSchema = z.strictObject({
  applyResult: applyResultSchema,
  status: profanityFilterStatusSchema
});
export const restoreProfanityFilterResponseSchema = z.strictObject({
  applyResult: applyResultSchema,
  status: profanityFilterStatusSchema
});

export type ProfanityFilterStatus = z.infer<typeof profanityFilterStatusSchema>;
export type LoadProfanityFilterRequest = z.infer<typeof loadProfanityFilterRequestSchema>;
export type LoadProfanityFilterResponse = z.infer<typeof loadProfanityFilterResponseSchema>;
export type ApplyProfanityFilterRequest = z.infer<typeof applyProfanityFilterRequestSchema>;
export type ApplyProfanityFilterResponse = z.infer<typeof applyProfanityFilterResponseSchema>;
export type RestoreProfanityFilterRequest = z.infer<typeof restoreProfanityFilterRequestSchema>;
export type RestoreProfanityFilterResponse = z.infer<typeof restoreProfanityFilterResponseSchema>;
