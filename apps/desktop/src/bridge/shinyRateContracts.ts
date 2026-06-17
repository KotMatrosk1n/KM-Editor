/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import {
  apiDiagnosticSchema,
  editSessionSchema,
  projectFileGraphEntryStateSchema,
  projectFileLayerSchema,
  projectGameSchema,
  projectPathsSchema,
  workflowSummarySchema
} from './contracts';

export const loadShinyRateWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const shinyRateModeSchema = z.enum(['default', 'fixed', 'always']);

export const shinyRateProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const shinyRateSourceRecordSchema = z.strictObject({
  label: z.string(),
  provenance: shinyRateProvenanceSchema,
  relativePath: z.string(),
  sourceId: z.string(),
  status: z.string()
});

export const shinyRateRuleSchema = z.strictObject({
  chancePercent: z.number(),
  maximumCustomDenominator: z.number().int().positive(),
  maximumRollCount: z.number().int().positive(),
  minimumCustomDenominator: z.number().int().positive(),
  minimumRollCount: z.number().int().positive(),
  mode: z.string(),
  oddsDenominator: z.number().int().positive().nullable(),
  oddsLabel: z.string(),
  percentLabel: z.string(),
  rollCount: z.number().int().positive().nullable(),
  runtimeSummary: z.string()
});

export const shinyRatePresetSchema = z.strictObject({
  description: z.string(),
  isEnabled: z.boolean(),
  label: z.string(),
  mode: z.string(),
  oddsLabel: z.string(),
  percentLabel: z.string(),
  presetId: z.string(),
  rollCount: z.number().int().positive().nullable(),
  targetDenominator: z.number().int().positive().nullable()
});

export const shinyRateWorkflowStatsSchema = z.strictObject({
  outputFileCount: z.number().int().nonnegative(),
  presetCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative()
});

export const shinyRateWorkflowSchema = z.strictObject({
  breakOffsetHex: z.string(),
  buildId: z.string(),
  compareOffsetHex: z.string(),
  detectedGame: projectGameSchema.nullable(),
  diagnostics: z.array(apiDiagnosticSchema),
  functionOffsetHex: z.string(),
  installMessage: z.string(),
  installStatus: z.string(),
  presets: z.array(shinyRatePresetSchema),
  rateRule: shinyRateRuleSchema,
  source: shinyRateSourceRecordSchema.nullable(),
  stats: shinyRateWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadShinyRateWorkflowResponseSchema = z.strictObject({
  workflow: shinyRateWorkflowSchema
});

export const stageShinyRateRequestSchema = z.strictObject({
  mode: shinyRateModeSchema,
  paths: projectPathsSchema,
  rollCount: z.number().int().positive().nullable(),
  session: editSessionSchema.nullable()
});

export const stageShinyRateResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: shinyRateWorkflowSchema
});

export type ShinyRateMode = z.infer<typeof shinyRateModeSchema>;
export type ShinyRatePreset = z.infer<typeof shinyRatePresetSchema>;
export type ShinyRateSourceRecord = z.infer<typeof shinyRateSourceRecordSchema>;
export type ShinyRateWorkflow = z.infer<typeof shinyRateWorkflowSchema>;
export type LoadShinyRateWorkflowRequest = z.infer<
  typeof loadShinyRateWorkflowRequestSchema
>;
export type LoadShinyRateWorkflowResponse = z.infer<
  typeof loadShinyRateWorkflowResponseSchema
>;
export type StageShinyRateRequest = z.infer<typeof stageShinyRateRequestSchema>;
export type StageShinyRateResponse = z.infer<typeof stageShinyRateResponseSchema>;
