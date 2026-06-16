/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import {
  apiDiagnosticSchema,
  editSessionSchema,
  projectFileGraphEntryStateSchema,
  projectFileLayerSchema,
  projectPathsSchema,
  workflowSummarySchema
} from './contracts';

export const loadFairyGymBoostsWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const fairyGymBoostStatSchema = z.enum(['atk', 'def', 'spAtk', 'spDef', 'speed']);
export const fairyGymBoostResultKindSchema = z.enum(['none', 'increase', 'decrease']);

export const fairyGymBoostSelectionSchema = z.strictObject({
  boostId: z.string(),
  effectId: z.number().int().min(0).max(6),
  resultKind: fairyGymBoostResultKindSchema
});

export const fairyGymBoostsProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const fairyGymBoostsSourceRecordSchema = z.strictObject({
  label: z.string(),
  provenance: fairyGymBoostsProvenanceSchema,
  relativePath: z.string(),
  sourceId: z.string(),
  status: z.string()
});

export const fairyGymBoostRecordSchema = z.strictObject({
  affectedStats: z.array(fairyGymBoostStatSchema),
  answerChoice: z.number().int().positive(),
  answerText: z.string(),
  boostId: z.string(),
  defaultResultKind: fairyGymBoostResultKindSchema,
  effectId: z.number().int().nonnegative(),
  effectLabel: z.string(),
  questionText: z.string(),
  resultKind: fairyGymBoostResultKindSchema,
  sequenceFile: z.string(),
  stageAmount: z.number().int().nonnegative()
});

export const fairyGymBoostTrainerSchema = z.strictObject({
  boosts: z.array(fairyGymBoostRecordSchema),
  displayOrder: z.number().int().nonnegative(),
  npcName: z.string(),
  trainerId: z.number().int().nonnegative()
});

export const fairyGymBoostsWorkflowStatsSchema = z.strictObject({
  boostCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative(),
  trainerCount: z.number().int().nonnegative()
});

export const fairyGymBoostsWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  sources: z.array(fairyGymBoostsSourceRecordSchema),
  stats: fairyGymBoostsWorkflowStatsSchema,
  summary: workflowSummarySchema,
  trainers: z.array(fairyGymBoostTrainerSchema)
});

export const loadFairyGymBoostsWorkflowResponseSchema = z.strictObject({
  workflow: fairyGymBoostsWorkflowSchema
});

export const stageFairyGymBoostsRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  selections: z.array(fairyGymBoostSelectionSchema),
  session: editSessionSchema.nullable()
});

export const stageFairyGymBoostsResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: fairyGymBoostsWorkflowSchema
});

export type FairyGymBoostResultKind = z.infer<typeof fairyGymBoostResultKindSchema>;
export type FairyGymBoostSelection = z.infer<typeof fairyGymBoostSelectionSchema>;
export type FairyGymBoostStat = z.infer<typeof fairyGymBoostStatSchema>;
export type FairyGymBoostRecord = z.infer<typeof fairyGymBoostRecordSchema>;
export type FairyGymBoostTrainer = z.infer<typeof fairyGymBoostTrainerSchema>;
export type FairyGymBoostsWorkflow = z.infer<typeof fairyGymBoostsWorkflowSchema>;
export type LoadFairyGymBoostsWorkflowRequest = z.infer<
  typeof loadFairyGymBoostsWorkflowRequestSchema
>;
export type LoadFairyGymBoostsWorkflowResponse = z.infer<
  typeof loadFairyGymBoostsWorkflowResponseSchema
>;
export type StageFairyGymBoostsRequest = z.infer<typeof stageFairyGymBoostsRequestSchema>;
export type StageFairyGymBoostsResponse = z.infer<typeof stageFairyGymBoostsResponseSchema>;
