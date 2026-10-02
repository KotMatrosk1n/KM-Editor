/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { apiDiagnosticSchema, editSessionSchema, projectGameSchema, projectPathsSchema } from './contracts';

export const fixAiFlagsRowSchema = z.strictObject({
  trainerId: z.number().int().positive(), name: z.string(), sourceFile: z.string(),
  currentFlags: z.number().int().min(0).max(8191), proposedFlags: z.number().int().min(0).max(8191).nullable(),
  fingerprint: z.string().regex(/^[A-F0-9]{64}$/), candidate: z.boolean(), previouslyFixed: z.boolean(),
  changedSinceFix: z.boolean(), fixedAtUtc: z.string().nullable(), baseFlags: z.number().int().nullable()
});
export const fixAiFlagsWorkflowSchema = z.strictObject({
  canEdit: z.boolean(), trainers: z.array(fixAiFlagsRowSchema), detectedGame: projectGameSchema.nullable(),
  customAiScripts: z.boolean(), contextFingerprint: z.string(), diagnostics: z.array(apiDiagnosticSchema)
});
export const fixAiFlagsSelectionSchema = z.strictObject({ trainerId: z.number().int().positive(),
  fingerprint: z.string().regex(/^[A-F0-9]{64}$/), acknowledgePreviousFix: z.boolean() });
export const loadFixAiFlagsRequestSchema = z.strictObject({ paths: projectPathsSchema });
export const loadFixAiFlagsResponseSchema = z.strictObject({ workflow: fixAiFlagsWorkflowSchema });
export const stageFixAiFlagsRequestSchema = z.strictObject({ paths: projectPathsSchema, session: editSessionSchema.nullable(),
  selections: z.array(fixAiFlagsSelectionSchema).min(1).max(512), contextFingerprint: z.string(), acknowledgeCustomScripts: z.boolean() });
export const stageFixAiFlagsResponseSchema = z.strictObject({ workflow: fixAiFlagsWorkflowSchema, session: editSessionSchema,
  diagnostics: z.array(apiDiagnosticSchema) });
export type FixAiFlagsWorkflow = z.infer<typeof fixAiFlagsWorkflowSchema>;
export type FixAiFlagsSelection = z.infer<typeof fixAiFlagsSelectionSchema>;
export type LoadFixAiFlagsRequest = z.infer<typeof loadFixAiFlagsRequestSchema>;
export type LoadFixAiFlagsResponse = z.infer<typeof loadFixAiFlagsResponseSchema>;
export type StageFixAiFlagsRequest = z.infer<typeof stageFixAiFlagsRequestSchema>;
export type StageFixAiFlagsResponse = z.infer<typeof stageFixAiFlagsResponseSchema>;
