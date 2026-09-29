/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { apiDiagnosticSchema, editSessionSchema, projectPathsSchema, workflowSummarySchema } from './contracts';

export const titanSwapperFields = ['enabled', 'species', 'level'] as const;
export const titanSwapperUpdateSchema = z.strictObject({
  rowId: z.string().min(1).max(80), field: z.enum(titanSwapperFields), value: z.number().int().min(0).max(65535)
}).refine(update => update.field === 'enabled' ? update.value <= 1
  : update.field === 'level' ? update.value >= 1 && update.value <= 100 : update.value > 0, { path: ['value'] });
export const titanSwapperWorkflowSchema = z.strictObject({
  summary: workflowSummarySchema, sourceRevision: z.string().max(64),
  rows: z.array(z.strictObject({
    id: z.string().min(1).max(80), storySpecies: z.number().int().positive(), phase: z.number().int().min(1).max(3),
    edition: z.enum(['both', 'scarlet', 'violet']),
    values: z.strictObject({ enabled: z.number().int().min(0).max(1), species: z.number().int().positive(), level: z.number().int().min(1).max(100) })
  })).max(13),
  speciesOptions: z.array(z.strictObject({ value: z.number().int().positive(), label: z.string() })).max(65536),
  diagnostics: z.array(apiDiagnosticSchema)
}).refine(workflow => workflow.summary.id === 'titanSwapper');
export const loadTitanSwapperRequestSchema = z.strictObject({ paths: projectPathsSchema, session: editSessionSchema.nullable() });
export const loadTitanSwapperResponseSchema = z.strictObject({ workflow: titanSwapperWorkflowSchema });
export const stageTitanSwapperRequestSchema = loadTitanSwapperRequestSchema.extend({
  sourceRevision: z.string().regex(/^[A-F0-9]{64}$/u), updates: z.array(titanSwapperUpdateSchema).min(1).max(39)
});
export const stageTitanSwapperResponseSchema = z.strictObject({
  workflow: titanSwapperWorkflowSchema, session: editSessionSchema, diagnostics: z.array(apiDiagnosticSchema)
});
export type TitanSwapperWorkflow = z.infer<typeof titanSwapperWorkflowSchema>;
export type TitanSwapperUpdate = z.infer<typeof titanSwapperUpdateSchema>;
export type LoadTitanSwapperRequest = z.infer<typeof loadTitanSwapperRequestSchema>;
export type LoadTitanSwapperResponse = z.infer<typeof loadTitanSwapperResponseSchema>;
export type StageTitanSwapperRequest = z.infer<typeof stageTitanSwapperRequestSchema>;
export type StageTitanSwapperResponse = z.infer<typeof stageTitanSwapperResponseSchema>;
