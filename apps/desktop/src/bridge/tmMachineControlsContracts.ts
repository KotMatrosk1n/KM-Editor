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

export const svTmMachineControlsDiagnosticCodes = {
  editSessionInvalid: 'KM-SV-TM-MACHINE-EDIT-SESSION-INVALID',
  materialSourceUnsupported: 'KM-SV-TM-MACHINE-MATERIAL-SOURCE-UNSUPPORTED',
  outputCommitFailed: 'KM-SV-TM-MACHINE-OUTPUT-COMMIT-FAILED',
  outputPreimageCaptureFailed: 'KM-SV-TM-MACHINE-OUTPUT-PREIMAGE-CAPTURE-FAILED',
  outputPreparationFailed: 'KM-SV-TM-MACHINE-OUTPUT-PREPARATION-FAILED',
  outputRollbackFailed: 'KM-SV-TM-MACHINE-OUTPUT-ROLLBACK-FAILED',
  outputRollbackRestored: 'KM-SV-TM-MACHINE-OUTPUT-ROLLBACK-RESTORED',
  projectUnsupported: 'KM-SV-TM-MACHINE-PROJECT-UNSUPPORTED',
  recipeSourceUnsupported: 'KM-SV-TM-MACHINE-RECIPE-SOURCE-UNSUPPORTED',
  reviewedPlanStale: 'KM-SV-TM-MACHINE-REVIEWED-PLAN-STALE',
  targetResolutionFailed: 'KM-SV-TM-MACHINE-TARGET-RESOLUTION-FAILED'
} as const;

export const tmMachineControlStateSchema = z.strictObject({
  canStage: z.boolean(),
  matchingRecordCount: z.number().int().nonnegative(),
  message: z.string().min(1),
  policy: z.enum([
    'progressionGated',
    'allAvailable',
    'discoveryGated',
    'alwaysVisible',
    'customized',
    'unknown'
  ]),
  stagedPolicy: z
    .enum(['progressionGated', 'allAvailable', 'discoveryGated', 'alwaysVisible'])
    .nullable(),
  status: z.enum(['standard', 'installed', 'customized', 'blocked']),
  totalRecordCount: z.number().int().positive()
});

export const tmMachineControlProvenanceSchema = z.strictObject({
  control: z.enum(['recipeAvailability', 'materialVisibility']),
  fileState: projectFileGraphEntryStateSchema,
  sha256: z.string().regex(/^[A-F0-9]{64}$/),
  sourceFile: z.string().min(1),
  sourceLayer: projectFileLayerSchema
});

export const tmMachineControlsWorkflowSchema = z
  .strictObject({
    diagnostics: z.array(apiDiagnosticSchema),
    materialVisibility: tmMachineControlStateSchema,
    provenance: z.array(tmMachineControlProvenanceSchema).max(2),
    recipeAvailability: tmMachineControlStateSchema,
    stats: z.strictObject({
      recipeCount: z.literal(229),
      sourceFileCount: z.number().int().min(0).max(2),
      supportedBuildCount: z.literal(2)
    }),
    summary: workflowSummarySchema,
    supportedBuild: z.literal('Scarlet/Violet 4.0.0')
  })
  .superRefine((workflow, context) => {
    if (
      workflow.summary.id !== 'tmMachineControls' ||
      workflow.summary.label !== 'TM Machine Controls'
    ) {
      context.addIssue({
        code: 'custom',
        message: 'TM Machine Controls workflow identity is not canonical.',
        path: ['summary']
      });
    }
    if (workflow.stats.sourceFileCount !== workflow.provenance.length) {
      context.addIssue({
        code: 'custom',
        message: 'TM Machine Controls source count does not match provenance.',
        path: ['stats', 'sourceFileCount']
      });
    }
  });

const tmMachinePathsSchema = projectPathsSchema.superRefine((paths, context) => {
  if (paths.selectedGame !== 'scarlet' && paths.selectedGame !== 'violet') {
    context.addIssue({
      code: 'custom',
      message: 'TM Machine Controls requires a Scarlet or Violet project.',
      path: ['selectedGame']
    });
  }
});

export const loadTmMachineControlsRequestSchema = z.strictObject({
  paths: tmMachinePathsSchema
});

export const loadTmMachineControlsResponseSchema = z.strictObject({
  workflow: tmMachineControlsWorkflowSchema
});

export const stageTmRecipeAvailabilityRequestSchema = z.strictObject({
  allAvailable: z.boolean(),
  paths: tmMachinePathsSchema,
  session: editSessionSchema.nullable()
});

export const stageTmRecipeAvailabilityResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: tmMachineControlsWorkflowSchema
});

export const stageTmMaterialVisibilityRequestSchema = z.strictObject({
  alwaysVisible: z.boolean(),
  paths: tmMachinePathsSchema,
  session: editSessionSchema.nullable()
});

export const stageTmMaterialVisibilityResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: tmMachineControlsWorkflowSchema
});

export type TmMachineControlsWorkflow = z.infer<typeof tmMachineControlsWorkflowSchema>;
export type LoadTmMachineControlsRequest = z.infer<
  typeof loadTmMachineControlsRequestSchema
>;
export type LoadTmMachineControlsResponse = z.infer<
  typeof loadTmMachineControlsResponseSchema
>;
export type StageTmRecipeAvailabilityRequest = z.infer<
  typeof stageTmRecipeAvailabilityRequestSchema
>;
export type StageTmRecipeAvailabilityResponse = z.infer<
  typeof stageTmRecipeAvailabilityResponseSchema
>;
export type StageTmMaterialVisibilityRequest = z.infer<
  typeof stageTmMaterialVisibilityRequestSchema
>;
export type StageTmMaterialVisibilityResponse = z.infer<
  typeof stageTmMaterialVisibilityResponseSchema
>;
