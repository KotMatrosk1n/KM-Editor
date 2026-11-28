/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import { apiDiagnosticSchema, editSessionSchema, projectPathsSchema, workflowSummarySchema } from './contracts';

const slotIdSchema = z.enum(['left', 'center', 'right']);
const speciesSchema = z.number().int().min(1).max(65535);
const sourceRevisionSchema = z.string().regex(/^[A-F0-9]{64}$/u);
const updateSchema = z.strictObject({ slotId: slotIdSchema, species: speciesSchema });
const workflowSchema = z.strictObject({
  summary: workflowSummarySchema,
  sourceRevision: z.union([sourceRevisionSchema, z.literal('')]),
  missionTitle: z.string(),
  slots: z.array(z.strictObject({ id: slotIdSchema, species: speciesSchema, originalSpecies: speciesSchema })).max(3),
  speciesOptions: z.array(z.strictObject({ value: speciesSchema, label: z.string() })),
  diagnostics: z.array(apiDiagnosticSchema)
}).refine(workflow => workflow.summary.id === 'mainMission20');

export const loadMainMission20RequestSchema = z.strictObject({ paths: projectPathsSchema });
export const loadMainMission20ResponseSchema = z.strictObject({ workflow: workflowSchema });
export const stageMainMission20RequestSchema = loadMainMission20RequestSchema.extend({
  session: editSessionSchema,
  sourceRevision: sourceRevisionSchema,
  updates: z.array(updateSchema).min(1).max(3)
});
export const stageMainMission20ResponseSchema = z.strictObject({
  session: editSessionSchema,
  diagnostics: z.array(apiDiagnosticSchema)
});

export type MainMission20Workflow = z.infer<typeof workflowSchema>;
export type MainMission20Update = z.infer<typeof updateSchema>;
export type LoadMainMission20Request = z.infer<typeof loadMainMission20RequestSchema>;
export type LoadMainMission20Response = z.infer<typeof loadMainMission20ResponseSchema>;
export type StageMainMission20Request = z.infer<typeof stageMainMission20RequestSchema>;
export type StageMainMission20Response = z.infer<typeof stageMainMission20ResponseSchema>;
