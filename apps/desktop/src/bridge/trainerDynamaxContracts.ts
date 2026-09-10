/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { apiDiagnosticSchema, applyResultSchema, projectPathsSchema } from './contracts';

export const trainerDynamaxSettingsSchema = z.strictObject({ disablePlayer: z.boolean(), disableOpponents: z.boolean() });
export const loadTrainerDynamaxRequestSchema = z.strictObject({ paths: projectPathsSchema });
export const reviewTrainerDynamaxRequestSchema = z.strictObject({ paths: projectPathsSchema, settings: trainerDynamaxSettingsSchema });
export const applyTrainerDynamaxRequestSchema = reviewTrainerDynamaxRequestSchema.extend({ reviewToken: z.string().regex(/^[A-F0-9]{64}$/u) });
export const trainerDynamaxStatusSchema = z.strictObject({
  canEdit: z.boolean(), settings: trainerDynamaxSettingsSchema, partial: z.boolean(), buildId: z.string().nullable(),
  sourceLayer: z.string(), diagnostics: z.array(apiDiagnosticSchema)
});
export const trainerDynamaxReviewSchema = z.strictObject({ reviewToken: z.string().nullable(), settings: trainerDynamaxSettingsSchema,
  outputAction: z.enum(['none', 'create', 'write', 'delete']), diagnostics: z.array(apiDiagnosticSchema) });
export const loadTrainerDynamaxResponseSchema = z.strictObject({ status: trainerDynamaxStatusSchema });
export const reviewTrainerDynamaxResponseSchema = z.strictObject({ review: trainerDynamaxReviewSchema });
export const applyTrainerDynamaxResponseSchema = z.strictObject({ status: trainerDynamaxStatusSchema, applyResult: applyResultSchema });
export type TrainerDynamaxSettings = z.infer<typeof trainerDynamaxSettingsSchema>;
export type TrainerDynamaxStatus = z.infer<typeof trainerDynamaxStatusSchema>;
export type TrainerDynamaxReview = z.infer<typeof trainerDynamaxReviewSchema>;
export type LoadTrainerDynamaxRequest = z.infer<typeof loadTrainerDynamaxRequestSchema>;
export type LoadTrainerDynamaxResponse = z.infer<typeof loadTrainerDynamaxResponseSchema>;
export type ReviewTrainerDynamaxRequest = z.infer<typeof reviewTrainerDynamaxRequestSchema>;
export type ReviewTrainerDynamaxResponse = z.infer<typeof reviewTrainerDynamaxResponseSchema>;
export type ApplyTrainerDynamaxRequest = z.infer<typeof applyTrainerDynamaxRequestSchema>;
export type ApplyTrainerDynamaxResponse = z.infer<typeof applyTrainerDynamaxResponseSchema>;
