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

export const habitatCoordinatesCommandNames = {
  load: 'habitatCoordinates.load',
  stageCoordinate: 'habitatCoordinates.coordinate.stage'
} as const;

export const svHabitatCoordinatesDiagnosticCodes = {
  buildUnsupported: 'KM-SV-HABITAT-BUILD-UNSUPPORTED',
  coordinateUnobserved: 'KM-SV-HABITAT-COORDINATE-UNOBSERVED',
  editSessionInvalid: 'KM-SV-HABITAT-EDIT-SESSION-INVALID',
  outputCommitFailed: 'KM-SV-HABITAT-OUTPUT-COMMIT-FAILED',
  outputPreimageCaptureFailed: 'KM-SV-HABITAT-OUTPUT-PREIMAGE-CAPTURE-FAILED',
  outputPreparationFailed: 'KM-SV-HABITAT-OUTPUT-PREPARATION-FAILED',
  outputRollbackFailed: 'KM-SV-HABITAT-OUTPUT-ROLLBACK-FAILED',
  outputRollbackRestored: 'KM-SV-HABITAT-OUTPUT-ROLLBACK-RESTORED',
  outputVerificationFailed: 'KM-SV-HABITAT-OUTPUT-VERIFICATION-FAILED',
  projectUnsupported: 'KM-SV-HABITAT-PROJECT-UNSUPPORTED',
  queryInvalid: 'KM-SV-HABITAT-QUERY-INVALID',
  regionSourceUnavailable: 'KM-SV-HABITAT-REGION-SOURCE-UNAVAILABLE',
  regionSourceUnsupported: 'KM-SV-HABITAT-REGION-SOURCE-UNSUPPORTED',
  reviewedPlanStale: 'KM-SV-HABITAT-REVIEWED-PLAN-STALE',
  rowBindingStale: 'KM-SV-HABITAT-ROW-BINDING-STALE',
  targetResolutionFailed: 'KM-SV-HABITAT-TARGET-RESOLUTION-FAILED'
} as const;

export const habitatRegionSchema = z.enum(['paldea', 'kitakami', 'blueberry']);
const sha256Schema = z.string().regex(/^[a-f0-9]{64}$/);
const buildIdSchema = z.union([z.literal('unknown'), z.string().regex(/^[A-F0-9]{64}$/)]);
const int32Schema = z.number().int().min(-2_147_483_648).max(2_147_483_647);

export const habitatCoordinatesQuerySchema = z.strictObject({
  limit: z.number().int().min(1).max(200),
  offset: z.number().int().nonnegative().max(50_000),
  region: habitatRegionSchema,
  search: z.string().max(80)
});

export const habitatCoordinateChoiceSchema = z.strictObject({
  x: int32Schema,
  y: int32Schema
});

export const habitatRowBindingSchema = z.strictObject({
  currentX: int32Schema,
  currentY: int32Schema,
  devNo: int32Schema,
  formNo: int32Schema,
  outerGroupOccurrence: z.number().int().nonnegative().max(4_095),
  rowOccurrence: z.number().int().nonnegative().max(49_999),
  rowPreimageSha256: sha256Schema,
  sourceFile: z.string().min(1).max(512),
  sourceRevision: sha256Schema,
  versionA: z.boolean(),
  versionB: z.boolean()
});

export const habitatCoordinateRecordSchema = z
  .strictObject({
    binding: habitatRowBindingSchema,
    formName: z.string().min(1).max(160).nullable(),
    isStaged: z.boolean(),
    speciesName: z.string().min(1).max(160),
    stagedCoordinate: habitatCoordinateChoiceSchema.nullable(),
    x: int32Schema,
    y: int32Schema
  })
  .superRefine((record, context) => {
    if (record.x !== record.binding.currentX || record.y !== record.binding.currentY) {
      context.addIssue({
        code: 'custom',
        message: 'Habitat row coordinates do not match their exact binding.',
        path: ['binding']
      });
    }
    if (record.isStaged !== (record.stagedCoordinate !== null)) {
      context.addIssue({
        code: 'custom',
        message: 'Habitat staged state and coordinate metadata disagree.',
        path: ['stagedCoordinate']
      });
    }
  });

export const habitatRegionStateSchema = z
  .strictObject({
    canStage: z.boolean(),
    coordinateChoices: z.array(habitatCoordinateChoiceSchema).max(2_048),
    fileState: projectFileGraphEntryStateSchema.nullable(),
    label: z.string().min(1).max(80),
    outerGroupCount: z.number().int().nonnegative().max(4_096),
    region: habitatRegionSchema,
    rowCount: z.number().int().nonnegative().max(50_000),
    semanticIdentityCount: z.number().int().nonnegative().max(50_000),
    sourceFile: z.string().min(1).max(512),
    sourceLayer: projectFileLayerSchema.nullable(),
    sourceRevision: z.union([sha256Schema, z.literal('')])
  })
  .superRefine((region, context) => {
    if (
      region.canStage &&
      (region.sourceLayer === null ||
        region.fileState === null ||
        region.sourceRevision.length !== 64 ||
        region.coordinateChoices.length === 0 ||
        region.rowCount === 0)
    ) {
      context.addIssue({
        code: 'custom',
        message: 'An editable habitat region requires exact source and coordinate metadata.',
        path: ['canStage']
      });
    }
    const coordinates = region.coordinateChoices.map(({ x, y }) => `${x},${y}`);
    if (new Set(coordinates).size !== coordinates.length) {
      context.addIssue({
        code: 'custom',
        message: 'Habitat coordinate choices must be unique.',
        path: ['coordinateChoices']
      });
    }
  });

export const habitatCoordinatePageSchema = z
  .strictObject({
    limit: z.number().int().min(1).max(200),
    offset: z.number().int().nonnegative().max(50_000),
    records: z.array(habitatCoordinateRecordSchema).max(200),
    region: habitatRegionSchema,
    search: z.string().max(80),
    totalMatches: z.number().int().nonnegative().max(50_000)
  })
  .superRefine((page, context) => {
    if (page.records.length > page.limit || page.offset + page.records.length > page.totalMatches) {
      context.addIssue({
        code: 'custom',
        message: 'Habitat page metadata is inconsistent.',
        path: ['records']
      });
    }
  });

export const habitatCoordinatesWorkflowSchema = z
  .strictObject({
    detectedBuildId: buildIdSchema,
    diagnostics: z.array(apiDiagnosticSchema),
    page: habitatCoordinatePageSchema,
    regions: z.array(habitatRegionStateSchema).length(3),
    stats: z.strictObject({
      readyRegionCount: z.number().int().min(0).max(3),
      regionCount: z.literal(3),
      totalRowCount: z.number().int().nonnegative().max(150_000),
      totalSemanticIdentityCount: z.number().int().nonnegative().max(150_000)
    }),
    summary: workflowSummarySchema,
    supportedBuild: z.literal('Scarlet/Violet 4.0.0')
  })
  .superRefine((workflow, context) => {
    if (
      workflow.summary.id !== 'habitatCoordinates' ||
      workflow.summary.label !== 'Habitat Coordinates'
    ) {
      context.addIssue({
        code: 'custom',
        message: 'Habitat Coordinates workflow identity is not canonical.',
        path: ['summary']
      });
    }
    if (new Set(workflow.regions.map((region) => region.region)).size !== 3) {
      context.addIssue({
        code: 'custom',
        message: 'Habitat Coordinates must include each exact region once.',
        path: ['regions']
      });
    }
    if (!workflow.regions.some((region) => region.region === workflow.page.region)) {
      context.addIssue({
        code: 'custom',
        message: 'Habitat Coordinates page region is unavailable.',
        path: ['page', 'region']
      });
    }
    if (
      workflow.stats.readyRegionCount !== workflow.regions.filter((region) => region.canStage).length ||
      workflow.stats.totalRowCount !== workflow.regions.reduce((sum, region) => sum + region.rowCount, 0) ||
      workflow.stats.totalSemanticIdentityCount !== workflow.regions.reduce(
        (sum, region) => sum + region.semanticIdentityCount,
        0
      )
    ) {
      context.addIssue({
        code: 'custom',
        message: 'Habitat Coordinates statistics do not match region metadata.',
        path: ['stats']
      });
    }
  });

const habitatPathsSchema = projectPathsSchema.superRefine((paths, context) => {
  if (paths.selectedGame !== 'scarlet' && paths.selectedGame !== 'violet') {
    context.addIssue({
      code: 'custom',
      message: 'Habitat Coordinates requires a Scarlet or Violet project.',
      path: ['selectedGame']
    });
  }
});

export const loadHabitatCoordinatesRequestSchema = z.strictObject({
  paths: habitatPathsSchema,
  query: habitatCoordinatesQuerySchema.nullable(),
  session: editSessionSchema.nullable()
});

export const loadHabitatCoordinatesResponseSchema = z.strictObject({
  workflow: habitatCoordinatesWorkflowSchema
});

export const stageHabitatCoordinateRequestSchema = z.strictObject({
  binding: habitatRowBindingSchema,
  coordinate: habitatCoordinateChoiceSchema,
  paths: habitatPathsSchema,
  query: habitatCoordinatesQuerySchema.nullable(),
  region: habitatRegionSchema,
  session: editSessionSchema.nullable()
});

export const stageHabitatCoordinateResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: habitatCoordinatesWorkflowSchema
});

export type HabitatCoordinatesQuery = z.infer<typeof habitatCoordinatesQuerySchema>;
export type HabitatCoordinateChoice = z.infer<typeof habitatCoordinateChoiceSchema>;
export type HabitatRowBinding = z.infer<typeof habitatRowBindingSchema>;
export type HabitatCoordinateRecord = z.infer<typeof habitatCoordinateRecordSchema>;
export type HabitatRegionState = z.infer<typeof habitatRegionStateSchema>;
export type HabitatCoordinatesWorkflow = z.infer<typeof habitatCoordinatesWorkflowSchema>;
export type LoadHabitatCoordinatesRequest = z.infer<typeof loadHabitatCoordinatesRequestSchema>;
export type LoadHabitatCoordinatesResponse = z.infer<typeof loadHabitatCoordinatesResponseSchema>;
export type StageHabitatCoordinateRequest = z.infer<typeof stageHabitatCoordinateRequestSchema>;
export type StageHabitatCoordinateResponse = z.infer<typeof stageHabitatCoordinateResponseSchema>;
