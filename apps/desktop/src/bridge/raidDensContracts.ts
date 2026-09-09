/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { apiDiagnosticSchema, editSessionSchema, projectGameSchema, projectPathsSchema } from './contracts';

export const raidDensWorkflowSchema = z.strictObject({
  canEdit: z.boolean(), disabled: z.boolean().nullable(), sourceLayer: z.string(),
  detectedGame: projectGameSchema.nullable(), diagnostics: z.array(apiDiagnosticSchema)
});
export const loadRaidDensRequestSchema = z.strictObject({ paths: projectPathsSchema });
export const stageRaidDensRequestSchema = z.strictObject({ paths: projectPathsSchema,
  disabled: z.boolean(), session: editSessionSchema.nullable() });
export const loadRaidDensResponseSchema = z.strictObject({ workflow: raidDensWorkflowSchema });
export const stageRaidDensResponseSchema = z.strictObject({ workflow: raidDensWorkflowSchema,
  session: editSessionSchema, diagnostics: z.array(apiDiagnosticSchema) });
export type RaidDensWorkflow = z.infer<typeof raidDensWorkflowSchema>;
export type LoadRaidDensRequest = z.infer<typeof loadRaidDensRequestSchema>;
export type LoadRaidDensResponse = z.infer<typeof loadRaidDensResponseSchema>;
export type StageRaidDensRequest = z.infer<typeof stageRaidDensRequestSchema>;
export type StageRaidDensResponse = z.infer<typeof stageRaidDensResponseSchema>;
