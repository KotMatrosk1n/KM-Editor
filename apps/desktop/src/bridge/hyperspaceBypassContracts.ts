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

export const loadHyperspaceBypassWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const hyperspaceBypassProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const hyperspaceBypassReservedRegionSchema = z.strictObject({
  label: z.string(),
  length: z.number().int().nullable(),
  offsetLabel: z.string(),
  regionId: z.string(),
  rule: z.string(),
  startOffset: z.number().int().nullable()
});

export const hyperspaceBypassWorkflowStatsSchema = z.strictObject({
  reservedMainTextRegionCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative()
});

export const hyperspaceBypassWorkflowSchema = z.strictObject({
  buildId: z.string(),
  detectedGame: projectGameSchema.nullable(),
  diagnostics: z.array(apiDiagnosticSchema),
  installMessage: z.string(),
  installStatus: z.string(),
  patchOffsetHex: z.string(),
  provenance: hyperspaceBypassProvenanceSchema,
  reservedRegions: z.array(hyperspaceBypassReservedRegionSchema),
  stats: hyperspaceBypassWorkflowStatsSchema,
  stubKind: z.string(),
  summary: workflowSummarySchema
});

export const loadHyperspaceBypassWorkflowResponseSchema = z.strictObject({
  workflow: hyperspaceBypassWorkflowSchema
});

export const stageHyperspaceBypassInstallRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable()
});

export const stageHyperspaceBypassInstallResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: hyperspaceBypassWorkflowSchema
});

export const stageHyperspaceBypassUninstallRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable()
});

export const stageHyperspaceBypassUninstallResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: hyperspaceBypassWorkflowSchema
});

export type HyperspaceBypassReservedRegion = z.infer<
  typeof hyperspaceBypassReservedRegionSchema
>;
export type HyperspaceBypassWorkflow = z.infer<typeof hyperspaceBypassWorkflowSchema>;
export type LoadHyperspaceBypassWorkflowRequest = z.infer<
  typeof loadHyperspaceBypassWorkflowRequestSchema
>;
export type LoadHyperspaceBypassWorkflowResponse = z.infer<
  typeof loadHyperspaceBypassWorkflowResponseSchema
>;
export type StageHyperspaceBypassInstallRequest = z.infer<
  typeof stageHyperspaceBypassInstallRequestSchema
>;
export type StageHyperspaceBypassInstallResponse = z.infer<
  typeof stageHyperspaceBypassInstallResponseSchema
>;
export type StageHyperspaceBypassUninstallRequest = z.infer<
  typeof stageHyperspaceBypassUninstallRequestSchema
>;
export type StageHyperspaceBypassUninstallResponse = z.infer<
  typeof stageHyperspaceBypassUninstallResponseSchema
>;
