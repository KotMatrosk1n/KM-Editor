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

export const loadShinyRateWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const shinyRateModeSchema = z.enum(['default', 'fixed', 'always']);
export const shinyRateInstallStatusSchema = z.enum([
  'disabled',
  'blocked',
  'available',
  'readOnly',
  'fixed',
  'always'
]);
export const shinyRateRuleModeSchema = z.enum(['default', 'fixed', 'always', 'blocked']);
export const shinyRatePresetModeSchema = z.enum([
  'default',
  'fixed',
  'always',
  'unsupported'
]);
export const shinyRateSourceStatusSchema = z.enum(['available', 'missing']);
const shinyRateGameSchema = z.enum(['sword', 'shield']);
const shinyRateBuildIdSchema = z.union([
  z.literal('unknown'),
  z.string().regex(/^[A-F0-9]{40}$/)
]);
const shinyRateOffsetSchema = z.union([
  z.literal('unknown'),
  z.string().regex(/^main\.text\+0x[A-F0-9]{8}$/)
]);

export const shinyRateProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.literal('exefs/main'),
  sourceLayer: projectFileLayerSchema
});

export const shinyRateSourceRecordSchema = z.strictObject({
  label: z.string(),
  provenance: shinyRateProvenanceSchema,
  relativePath: z.literal('exefs/main'),
  sourceId: z.literal('exefs-main'),
  status: shinyRateSourceStatusSchema
});

const shinyRateRuleConstants = {
  maximumCustomDenominator: z.literal(4096),
  maximumRollCount: z.literal(4091),
  minimumCustomDenominator: z.literal(2),
  minimumRollCount: z.literal(1)
};

const defaultShinyRateRuleSchema = z.strictObject({
  chancePercent: z.null(),
  ...shinyRateRuleConstants,
  mode: z.literal('default'),
  oddsDenominator: z.null(),
  oddsLabel: z.literal('Dynamic'),
  percentLabel: z.literal('Variable'),
  rollCount: z.null(),
  runtimeSummary: z.literal(
    "Default restores the game's original runtime-dependent reroll count calculation."
  )
});

const fixedShinyRateRuleSchema = z.strictObject({
  chancePercent: z.number().positive().lt(100),
  ...shinyRateRuleConstants,
  mode: z.literal('fixed'),
  oddsDenominator: z.number().int().min(2).max(4096),
  oddsLabel: z.string(),
  percentLabel: z.string(),
  rollCount: z.number().int().min(1).max(4091),
  runtimeSummary: z.literal(
    'Fixed writes a global PID roll count for random shiny checks.'
  )
});

const alwaysShinyRateRuleSchema = z.strictObject({
  chancePercent: z.literal(100),
  ...shinyRateRuleConstants,
  mode: z.literal('always'),
  oddsDenominator: z.literal(1),
  oddsLabel: z.literal('1/1'),
  percentLabel: z.literal('100.000%'),
  rollCount: z.null(),
  runtimeSummary: z.literal('Always Shiny NOPs the loop break branch.')
});

const blockedShinyRateRuleSchema = z.strictObject({
  chancePercent: z.null(),
  ...shinyRateRuleConstants,
  mode: z.literal('blocked'),
  oddsDenominator: z.null(),
  oddsLabel: z.literal('Unknown'),
  percentLabel: z.literal('Unknown'),
  rollCount: z.null(),
  runtimeSummary: z.literal(
    'Runtime shiny rate is unavailable until exefs/main can be inspected.'
  )
});

export const shinyRateRuleSchema = z.discriminatedUnion('mode', [
  defaultShinyRateRuleSchema,
  fixedShinyRateRuleSchema,
  alwaysShinyRateRuleSchema,
  blockedShinyRateRuleSchema
]).superRefine((rule, context) => {
  if (rule.mode !== 'fixed') {
    return;
  }

  const chance = rule.chancePercent / 100;
  const expectedChance = 1 - Math.pow(4095 / 4096, rule.rollCount);
  const expectedOddsDenominator = Math.max(1, Math.round(1 / chance));
  const expectedOddsLabel = `1/${formatShinyRateInteger(rule.oddsDenominator)}`;
  const expectedPercentLabel = `${rule.chancePercent.toFixed(3)}%`;

  if (Math.abs(chance - expectedChance) > 1e-12) {
    context.addIssue({
      code: 'custom',
      message: 'Fixed Shiny Rate chance must match its roll count.',
      path: ['chancePercent']
    });
  }

  if (rule.oddsDenominator !== expectedOddsDenominator) {
    context.addIssue({
      code: 'custom',
      message: 'Fixed Shiny Rate odds must match its chance.',
      path: ['oddsDenominator']
    });
  }

  if (rule.oddsLabel !== expectedOddsLabel) {
    context.addIssue({
      code: 'custom',
      message: 'Fixed Shiny Rate odds label must match its denominator.',
      path: ['oddsLabel']
    });
  }

  if (rule.percentLabel !== expectedPercentLabel) {
    context.addIssue({
      code: 'custom',
      message: 'Fixed Shiny Rate percent label must match its chance.',
      path: ['percentLabel']
    });
  }
});

const gen3ShinyRatePresetSchema = z.strictObject({
  description: z.literal(
    'Not available yet. The current patch can make odds more common, but 1/8192 needs a separate shiny-threshold patch.'
  ),
  isEnabled: z.literal(false),
  label: z.literal('Gen 3'),
  mode: z.literal('unsupported'),
  oddsLabel: z.literal('1/8,192'),
  percentLabel: z.literal('0.012%'),
  presetId: z.literal('gen3'),
  rollCount: z.null(),
  targetDenominator: z.literal(8192)
});

const defaultShinyRatePresetSchema = z.strictObject({
  description: z.literal("Restores the game's runtime-dependent shiny reroll logic."),
  isEnabled: z.literal(true),
  label: z.literal('Default'),
  mode: z.literal('default'),
  oddsLabel: z.literal('Dynamic'),
  percentLabel: z.literal('Variable'),
  presetId: z.literal('default'),
  rollCount: z.null(),
  targetDenominator: z.null()
});

const shinyCharmShinyRatePresetSchema = z.strictObject({
  description: z.literal('Writes 3 PID rolls.'),
  isEnabled: z.literal(true),
  label: z.literal('Shiny Charm'),
  mode: z.literal('fixed'),
  oddsLabel: z.literal('1/1,366'),
  percentLabel: z.literal('0.073%'),
  presetId: z.literal('shinyCharm'),
  rollCount: z.literal(3),
  targetDenominator: z.literal(1366)
});

const masudaShinyRatePresetSchema = z.strictObject({
  description: z.literal('Writes 6 PID rolls.'),
  isEnabled: z.literal(true),
  label: z.literal('Masuda'),
  mode: z.literal('fixed'),
  oddsLabel: z.literal('1/683'),
  percentLabel: z.literal('0.146%'),
  presetId: z.literal('masuda'),
  rollCount: z.literal(6),
  targetDenominator: z.literal(683)
});

const masudaCharmShinyRatePresetSchema = z.strictObject({
  description: z.literal('Writes 8 PID rolls.'),
  isEnabled: z.literal(true),
  label: z.literal('Masuda + Shiny Charm'),
  mode: z.literal('fixed'),
  oddsLabel: z.literal('1/512'),
  percentLabel: z.literal('0.195%'),
  presetId: z.literal('masudaCharm'),
  rollCount: z.literal(8),
  targetDenominator: z.literal(512)
});

const alwaysShinyRatePresetSchema = z.strictObject({
  description: z.literal('Forces random shiny checks to resolve as shiny.'),
  isEnabled: z.literal(true),
  label: z.literal('Always Shiny'),
  mode: z.literal('always'),
  oddsLabel: z.literal('1/1'),
  percentLabel: z.literal('100.000%'),
  presetId: z.literal('always'),
  rollCount: z.null(),
  targetDenominator: z.literal(1)
});

export const shinyRatePresetSchema = z.discriminatedUnion('presetId', [
  gen3ShinyRatePresetSchema,
  defaultShinyRatePresetSchema,
  shinyCharmShinyRatePresetSchema,
  masudaShinyRatePresetSchema,
  masudaCharmShinyRatePresetSchema,
  alwaysShinyRatePresetSchema
]);

export const shinyRateWorkflowStatsSchema = z.strictObject({
  outputFileCount: z.number().int().nonnegative(),
  presetCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative()
});

export const shinyRateWorkflowSchema = z.strictObject({
  breakOffsetHex: shinyRateOffsetSchema,
  buildId: shinyRateBuildIdSchema,
  compareOffsetHex: shinyRateOffsetSchema,
  detectedGame: shinyRateGameSchema.nullable(),
  diagnostics: z.array(apiDiagnosticSchema),
  functionOffsetHex: shinyRateOffsetSchema,
  installMessage: z.string(),
  installStatus: shinyRateInstallStatusSchema,
  presets: z.tuple([
    gen3ShinyRatePresetSchema,
    defaultShinyRatePresetSchema,
    shinyCharmShinyRatePresetSchema,
    masudaShinyRatePresetSchema,
    masudaCharmShinyRatePresetSchema,
    alwaysShinyRatePresetSchema
  ]),
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
  rollCount: z.number().int().min(1).max(4091).nullable(),
  session: editSessionSchema.nullable()
}).superRefine((request, context) => {
  if (request.mode === 'fixed' && request.rollCount === null) {
    context.addIssue({
      code: 'custom',
      message: 'Fixed Shiny Rate mode requires a roll count.',
      path: ['rollCount']
    });
  }

  if (request.mode !== 'fixed' && request.rollCount !== null) {
    context.addIssue({
      code: 'custom',
      message: 'Only fixed Shiny Rate mode accepts a roll count.',
      path: ['rollCount']
    });
  }
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

function formatShinyRateInteger(value: number) {
  return value.toString().replace(/\B(?=(\d{3})+(?!\d))/g, ',');
}
