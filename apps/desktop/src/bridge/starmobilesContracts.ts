/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import { apiDiagnosticSchema, editSessionSchema, projectPathsSchema, workflowSummarySchema } from './contracts';

export const starmobileFields = ['level', 'hp', 'attack', 'defense', 'specialAttack', 'specialDefense', 'hpMultiplier'] as const;
export const starmobileMoveFields = ['move2', 'move3', 'move4'] as const;
export const starmobileTraitFields = ['type1', 'type2', 'ability'] as const;
export { pokemonTypeOptions as starmobileTypeOptions } from '../pokemonTypeOptions';
const editableFields = [...starmobileFields, ...starmobileMoveFields, ...starmobileTraitFields] as const;
export function starmobileFieldMaximum(field: typeof editableFields[number]) {
  return field === 'type1' || field === 'type2' ? 17 : field.startsWith('move') || field === 'ability' ? 65535 : field === 'level' || field === 'hpMultiplier' ? 100 : 255;
}
export const starmobileUpdateSchema = z.strictObject({
  rowId: z.string().min(1).max(80), field: z.enum(editableFields), value: z.number().int().min(0).max(65535)
}).refine(update => update.value <= starmobileFieldMaximum(update.field)
  && (update.field.startsWith('move') ? update.value < 896 || update.value > 900 : starmobileTraitFields.some(field => field === update.field) || update.value >= 1), { path: ['value'] });
export const starmobilesWorkflowSchema = z.strictObject({
  summary: workflowSummarySchema, sourceRevision: z.string().max(64),
  rows: z.array(z.strictObject({
    id: z.string().min(1).max(80), bossType: z.number().int(), difficulty: z.number().int(),
    trainerId: z.string().max(256), eventId: z.string().max(256),
    values: z.partialRecord(z.enum([...editableFields, 'move1', 'speed']), z.number().int()),
    vanillaValues: z.partialRecord(z.enum([...editableFields, 'move1', 'speed']), z.number().int()).nullable()
  })).max(64), diagnostics: z.array(apiDiagnosticSchema),
  moveOptions: z.array(z.strictObject({ value: z.number().int().min(0).max(65535), label: z.string(), canSelect: z.boolean() })).max(65536),
  abilityOptions: z.array(z.strictObject({ value: z.number().int().min(0).max(65535), label: z.string() })).max(65536)
}).refine(workflow => workflow.summary.id === 'starmobiles');
export const loadStarmobilesRequestSchema = z.strictObject({ paths: projectPathsSchema, session: editSessionSchema.nullable() });
export const loadStarmobilesResponseSchema = z.strictObject({ workflow: starmobilesWorkflowSchema });
export const stageStarmobilesRequestSchema = loadStarmobilesRequestSchema.extend({
  sourceRevision: z.string().regex(/^[A-F0-9]{64}$/u), updates: z.array(starmobileUpdateSchema).min(1).max(512)
});
export const stageStarmobilesResponseSchema = z.strictObject({
  workflow: starmobilesWorkflowSchema, session: editSessionSchema, diagnostics: z.array(apiDiagnosticSchema)
});
export type StarmobilesWorkflow = z.infer<typeof starmobilesWorkflowSchema>;
export type StarmobileUpdate = z.infer<typeof starmobileUpdateSchema>;
export type LoadStarmobilesRequest = z.infer<typeof loadStarmobilesRequestSchema>;
export type LoadStarmobilesResponse = z.infer<typeof loadStarmobilesResponseSchema>;
export type StageStarmobilesRequest = z.infer<typeof stageStarmobilesRequestSchema>;
export type StageStarmobilesResponse = z.infer<typeof stageStarmobilesResponseSchema>;
