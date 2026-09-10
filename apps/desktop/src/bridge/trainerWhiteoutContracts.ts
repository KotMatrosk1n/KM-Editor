/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { apiDiagnosticSchema, editSessionSchema, projectGameSchema, projectPathsSchema } from './contracts';

export const trainerWhiteoutRecordSchema = z.strictObject({
  trainerId: z.number().int().positive(), name: z.string(),
  enabled: z.boolean(), vanillaEnabled: z.boolean(),
  override: z.boolean().nullable(), mixed: z.boolean()
});
export const trainerWhiteoutWorkflowSchema = z.strictObject({
  canEdit: z.boolean(), trainers: z.array(trainerWhiteoutRecordSchema),
  detectedGame: projectGameSchema.nullable(), diagnostics: z.array(apiDiagnosticSchema)
});
export const trainerWhiteoutChangeSchema = z.strictObject({ trainerId: z.number().int().positive(), enabled: z.boolean().nullable() });
export const loadTrainerWhiteoutRequestSchema = z.strictObject({ paths: projectPathsSchema });
export const stageTrainerWhiteoutRequestSchema = z.strictObject({ paths: projectPathsSchema,
  changes: z.array(trainerWhiteoutChangeSchema).min(1).max(436), session: editSessionSchema.nullable() });
export const loadTrainerWhiteoutResponseSchema = z.strictObject({ workflow: trainerWhiteoutWorkflowSchema });
export const stageTrainerWhiteoutResponseSchema = z.strictObject({ workflow: trainerWhiteoutWorkflowSchema,
  session: editSessionSchema, diagnostics: z.array(apiDiagnosticSchema) });
export type TrainerWhiteoutWorkflow = z.infer<typeof trainerWhiteoutWorkflowSchema>;
export type TrainerWhiteoutChange = z.infer<typeof trainerWhiteoutChangeSchema>;
export type LoadTrainerWhiteoutRequest = z.infer<typeof loadTrainerWhiteoutRequestSchema>;
export type LoadTrainerWhiteoutResponse = z.infer<typeof loadTrainerWhiteoutResponseSchema>;
export type StageTrainerWhiteoutRequest = z.infer<typeof stageTrainerWhiteoutRequestSchema>;
export type StageTrainerWhiteoutResponse = z.infer<typeof stageTrainerWhiteoutResponseSchema>;
