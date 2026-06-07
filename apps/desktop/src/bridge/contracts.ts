/* SPDX-License-Identifier: GPL-3.0-only */

import { z, type ZodTypeAny } from 'zod';

export const kmCommandNameValues = [
  'project.open',
  'project.validate',
  'project.fileGraph.refresh',
  'editSession.start',
  'editSession.get',
  'editSession.discard',
  'editSession.validate',
  'changePlan.create',
  'changePlan.apply'
] as const;

export const kmCommandNameSchema = z.enum(kmCommandNameValues);
export type KmCommandName = z.infer<typeof kmCommandNameSchema>;

export const kmCommandNames = {
  applyChangePlan: 'changePlan.apply',
  createChangePlan: 'changePlan.create',
  discardEditSession: 'editSession.discard',
  getEditSession: 'editSession.get',
  openProject: 'project.open',
  refreshFileGraph: 'project.fileGraph.refresh',
  startEditSession: 'editSession.start',
  validateEditSession: 'editSession.validate',
  validateProject: 'project.validate'
} as const satisfies Record<string, KmCommandName>;

export const apiDiagnosticSeveritySchema = z.enum(['info', 'warning', 'error']);

export const apiDiagnosticSchema = z.strictObject({
  domain: z.string().nullable().optional(),
  expected: z.string().nullable().optional(),
  field: z.string().nullable().optional(),
  file: z.string().nullable().optional(),
  message: z.string(),
  severity: apiDiagnosticSeveritySchema
});

export const apiErrorSchema = z.strictObject({
  code: z.string(),
  diagnostics: z.array(apiDiagnosticSchema),
  message: z.string()
});

export const projectPathsSchema = z.strictObject({
  baseExeFsPath: z.string().nullable(),
  baseRomFsPath: z.string().nullable(),
  outputRootPath: z.string().nullable()
});

export const openProjectRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const projectHealthStateSchema = z.enum([
  'needsPaths',
  'readOnlyReady',
  'editableReady',
  'blocked'
]);

export const projectPathRoleSchema = z.enum(['baseRomFs', 'baseExeFs', 'outputRoot']);

export const projectPathStatusSchema = z.enum(['notSet', 'missing', 'wrongKind', 'valid', 'unsafe']);

export const projectPathValidationSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  isRequired: z.boolean(),
  path: z.string().nullable(),
  role: projectPathRoleSchema,
  status: projectPathStatusSchema
});

export const projectFileGraphSummarySchema = z.strictObject({
  baseFileCount: z.number().int().nonnegative(),
  layeredFileCount: z.number().int().nonnegative(),
  layeredOnlyCount: z.number().int().nonnegative(),
  overrideCount: z.number().int().nonnegative()
});

export const projectHealthSchema = z.strictObject({
  canOpenEditableWorkflows: z.boolean(),
  canOpenReadOnlyWorkflows: z.boolean(),
  diagnostics: z.array(apiDiagnosticSchema),
  fileGraph: projectFileGraphSummarySchema,
  paths: z.array(projectPathValidationSchema),
  state: projectHealthStateSchema
});

export const openProjectResponseSchema = z.strictObject({
  health: projectHealthSchema,
  projectId: z.string()
});

export function createBridgeRequestSchema<TPayloadSchema extends ZodTypeAny>(
  payloadSchema: TPayloadSchema
) {
  return z.strictObject({
    command: kmCommandNameSchema,
    payload: payloadSchema,
    requestId: z.string().nullable().optional()
  });
}

export function createBridgeResponseSchema<TPayloadSchema extends ZodTypeAny>(
  payloadSchema: TPayloadSchema
) {
  return z
    .strictObject({
      error: apiErrorSchema.nullable().optional(),
      payload: payloadSchema.nullable().optional(),
      requestId: z.string().nullable().optional()
    })
    .superRefine((response, context) => {
      const hasPayload = response.payload !== null && response.payload !== undefined;
      const hasError = response.error !== null && response.error !== undefined;

      // A bridge response must be exactly one of success or failure; never both and never neither.
      if (hasPayload === hasError) {
        context.addIssue({
          code: 'custom',
          message: 'Bridge responses must contain either payload or error.'
        });
      }
    });
}

export type ApiDiagnostic = z.infer<typeof apiDiagnosticSchema>;
export type ApiError = z.infer<typeof apiErrorSchema>;
export type OpenProjectRequest = z.infer<typeof openProjectRequestSchema>;
export type OpenProjectResponse = z.infer<typeof openProjectResponseSchema>;
export type ProjectHealth = z.infer<typeof projectHealthSchema>;
