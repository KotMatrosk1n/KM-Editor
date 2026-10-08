/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { apiDiagnosticSchema, editSessionSchema, projectPathsSchema, workflowSummarySchema } from './contracts';

export const blueberryEditors = ['bbqRewards', 'supportBoard', 'snacksworth'] as const;
export const blueberryEligibilityFields = ['soloScarlet', 'soloViolet', 'groupScarlet', 'groupViolet'] as const;
const fields = ['rewardBp', 'costBp', ...blueberryEligibilityFields] as const;
export const blueberryWorkflowSchema = z.strictObject({
  summary: workflowSummarySchema, sourceRevision: z.string().max(64),
  rows: z.array(z.strictObject({ id: z.string().min(1).max(80), label: z.string(), group: z.number().int(),
    difficulty: z.number().int(), goal: z.number().int(), species: z.number().int(),
    values: z.partialRecord(z.enum(fields), z.number().int()), vanillaValues: z.partialRecord(z.enum(fields), z.number().int()) })).max(512),
  diagnostics: z.array(apiDiagnosticSchema)
}).refine(workflow => blueberryEditors.some(id => id === workflow.summary.id));
export const blueberryUpdateSchema = z.strictObject({ rowId: z.string().min(1).max(80), field: z.enum(fields),
  value: z.number().int().min(0).max(9999999) }).refine(update => !blueberryEligibilityFields.some(field => field === update.field) || update.value <= 1);
export const loadBlueberryRequestSchema = z.strictObject({ paths: projectPathsSchema, session: editSessionSchema.nullable(), editor: z.enum(blueberryEditors) });
export const loadBlueberryResponseSchema = z.strictObject({ workflow: blueberryWorkflowSchema });
export const stageBlueberryRequestSchema = loadBlueberryRequestSchema.extend({ sourceRevision: z.string().regex(/^[A-F0-9]{64}$/u),
  updates: z.array(blueberryUpdateSchema).min(1).max(2048) });
export const stageBlueberryResponseSchema = z.strictObject({ workflow: blueberryWorkflowSchema, session: editSessionSchema, diagnostics: z.array(apiDiagnosticSchema) });
export type BlueberryEditor = typeof blueberryEditors[number];
export type BlueberryWorkflow = z.infer<typeof blueberryWorkflowSchema>;
export type BlueberryUpdate = z.infer<typeof blueberryUpdateSchema>;
export type LoadBlueberryRequest = z.infer<typeof loadBlueberryRequestSchema>;
export type LoadBlueberryResponse = z.infer<typeof loadBlueberryResponseSchema>;
export type StageBlueberryRequest = z.infer<typeof stageBlueberryRequestSchema>;
export type StageBlueberryResponse = z.infer<typeof stageBlueberryResponseSchema>;
