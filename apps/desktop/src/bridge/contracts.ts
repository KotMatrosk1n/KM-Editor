/* SPDX-License-Identifier: GPL-3.0-only */

import { z, type ZodTypeAny } from 'zod';

export const kmCommandNameValues = [
  'project.open',
  'project.validate',
  'project.fileGraph.refresh',
  'workflow.list',
  'items.load',
  'items.field.update',
  'text.load',
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
  listWorkflows: 'workflow.list',
  loadItemsWorkflow: 'items.load',
  loadTextWorkflow: 'text.load',
  openProject: 'project.open',
  refreshFileGraph: 'project.fileGraph.refresh',
  startEditSession: 'editSession.start',
  updateItemField: 'items.field.update',
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

export const validateProjectRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const refreshFileGraphRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const listWorkflowsRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadItemsWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadTextWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const startEditSessionRequestSchema = z.strictObject({
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

export const projectFileGraphEntryStateSchema = z.enum([
  'baseOnly',
  'layeredOverride',
  'layeredOnly'
]);

export const projectFileLayerSchema = z.enum(['base', 'layered', 'pending', 'generated']);

export const projectFileReferenceSchema = z.strictObject({
  layer: projectFileLayerSchema,
  relativePath: z.string()
});

export const pendingEditSchema = z.strictObject({
  domain: z.string(),
  field: z.string().nullable().optional(),
  newValue: z.string().nullable().optional(),
  recordId: z.string().nullable().optional(),
  sources: z.array(projectFileReferenceSchema),
  summary: z.string()
});

export const editSessionSchema = z.strictObject({
  hasPendingChanges: z.boolean(),
  pendingEdits: z.array(pendingEditSchema),
  sessionId: z.string()
});

export const validateEditSessionRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema
});

export const createChangePlanRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema
});

export const applyChangePlanRequestSchema = z.strictObject({
  changePlan: z.lazy(() => changePlanSchema),
  paths: projectPathsSchema,
  session: editSessionSchema
});

export const projectFileGraphEntrySchema = z.strictObject({
  baseFile: projectFileReferenceSchema.nullable(),
  layeredFile: projectFileReferenceSchema.nullable(),
  relativePath: z.string(),
  state: projectFileGraphEntryStateSchema
});

export const projectFileGraphSchema = z.strictObject({
  entries: z.array(projectFileGraphEntrySchema),
  summary: projectFileGraphSummarySchema
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
  fileGraph: projectFileGraphSchema,
  health: projectHealthSchema,
  projectId: z.string()
});

export const validateProjectResponseSchema = z.strictObject({
  health: projectHealthSchema
});

export const refreshFileGraphResponseSchema = z.strictObject({
  fileGraph: projectFileGraphSchema
});

export const workflowAvailabilitySchema = z.enum(['disabled', 'readOnly', 'available']);

export const workflowSummarySchema = z.strictObject({
  availability: workflowAvailabilitySchema,
  description: z.string(),
  diagnostics: z.array(apiDiagnosticSchema),
  id: z.string(),
  label: z.string()
});

export const listWorkflowsResponseSchema = z.strictObject({
  workflows: z.array(workflowSummarySchema)
});

export const itemProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const itemRecordSchema = z.strictObject({
  buyPrice: z.number().int().nonnegative(),
  category: z.string(),
  itemId: z.number().int().nonnegative(),
  name: z.string(),
  provenance: itemProvenanceSchema,
  sellPrice: z.number().int().nonnegative()
});

export const itemEditableFieldSchema = z.strictObject({
  field: z.string(),
  label: z.string(),
  maximumValue: z.number().int().nullable(),
  minimumValue: z.number().int().nullable(),
  valueKind: z.string()
});

export const itemsWorkflowStatsSchema = z.strictObject({
  sourceFileCount: z.number().int().nonnegative(),
  totalItemCount: z.number().int().nonnegative()
});

export const itemsWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  editableFields: z.array(itemEditableFieldSchema),
  items: z.array(itemRecordSchema),
  stats: itemsWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadItemsWorkflowResponseSchema = z.strictObject({
  workflow: itemsWorkflowSchema
});

export const textProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const textEntryRecordSchema = z.strictObject({
  label: z.string(),
  language: z.string(),
  provenance: textProvenanceSchema,
  textId: z.number().int().nonnegative(),
  value: z.string()
});

export const dialogueReferenceRecordSchema = z.strictObject({
  context: z.string(),
  dialogueId: z.string(),
  label: z.string(),
  preview: z.string(),
  provenance: textProvenanceSchema,
  textId: z.number().int().nonnegative()
});

export const textWorkflowStatsSchema = z.strictObject({
  dialogueReferenceCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative(),
  totalTextEntryCount: z.number().int().nonnegative()
});

export const textWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  dialogueReferences: z.array(dialogueReferenceRecordSchema),
  entries: z.array(textEntryRecordSchema),
  stats: textWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadTextWorkflowResponseSchema = z.strictObject({
  workflow: textWorkflowSchema
});

export const updateItemFieldRequestSchema = z.strictObject({
  field: z.string(),
  itemId: z.number().int().nonnegative(),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  value: z.string()
});

export const updateItemFieldResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: itemsWorkflowSchema
});

export const startEditSessionResponseSchema = z.strictObject({
  session: editSessionSchema
});

export const validateEditSessionResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  isValid: z.boolean(),
  session: editSessionSchema
});

export const plannedFileWriteSchema = z.strictObject({
  reason: z.string(),
  replacesExistingOutput: z.boolean(),
  sources: z.array(projectFileReferenceSchema),
  targetRelativePath: z.string()
});

export const changePlanSchema = z.strictObject({
  canApply: z.boolean(),
  diagnostics: z.array(apiDiagnosticSchema),
  sessionId: z.string(),
  writes: z.array(plannedFileWriteSchema)
});

export const createChangePlanResponseSchema = z.strictObject({
  changePlan: changePlanSchema
});

export const applyResultSchema = z.strictObject({
  applyId: z.string(),
  diagnostics: z.array(apiDiagnosticSchema),
  writtenFiles: z.array(z.string())
});

export const applyChangePlanResponseSchema = z.strictObject({
  applyResult: applyResultSchema
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
export type ApplyChangePlanRequest = z.infer<typeof applyChangePlanRequestSchema>;
export type ApplyChangePlanResponse = z.infer<typeof applyChangePlanResponseSchema>;
export type ApplyResult = z.infer<typeof applyResultSchema>;
export type ChangePlan = z.infer<typeof changePlanSchema>;
export type CreateChangePlanRequest = z.infer<typeof createChangePlanRequestSchema>;
export type CreateChangePlanResponse = z.infer<typeof createChangePlanResponseSchema>;
export type EditSession = z.infer<typeof editSessionSchema>;
export type ItemEditableField = z.infer<typeof itemEditableFieldSchema>;
export type ItemRecord = z.infer<typeof itemRecordSchema>;
export type ItemsWorkflow = z.infer<typeof itemsWorkflowSchema>;
export type TextWorkflow = z.infer<typeof textWorkflowSchema>;
export type ListWorkflowsRequest = z.infer<typeof listWorkflowsRequestSchema>;
export type ListWorkflowsResponse = z.infer<typeof listWorkflowsResponseSchema>;
export type LoadItemsWorkflowRequest = z.infer<typeof loadItemsWorkflowRequestSchema>;
export type LoadItemsWorkflowResponse = z.infer<typeof loadItemsWorkflowResponseSchema>;
export type LoadTextWorkflowRequest = z.infer<typeof loadTextWorkflowRequestSchema>;
export type LoadTextWorkflowResponse = z.infer<typeof loadTextWorkflowResponseSchema>;
export type OpenProjectRequest = z.infer<typeof openProjectRequestSchema>;
export type OpenProjectResponse = z.infer<typeof openProjectResponseSchema>;
export type ProjectFileGraph = z.infer<typeof projectFileGraphSchema>;
export type ProjectHealth = z.infer<typeof projectHealthSchema>;
export type ProjectPathRole = z.infer<typeof projectPathRoleSchema>;
export type ProjectPathValidation = z.infer<typeof projectPathValidationSchema>;
export type RefreshFileGraphRequest = z.infer<typeof refreshFileGraphRequestSchema>;
export type RefreshFileGraphResponse = z.infer<typeof refreshFileGraphResponseSchema>;
export type StartEditSessionRequest = z.infer<typeof startEditSessionRequestSchema>;
export type StartEditSessionResponse = z.infer<typeof startEditSessionResponseSchema>;
export type UpdateItemFieldRequest = z.infer<typeof updateItemFieldRequestSchema>;
export type UpdateItemFieldResponse = z.infer<typeof updateItemFieldResponseSchema>;
export type ValidateEditSessionRequest = z.infer<typeof validateEditSessionRequestSchema>;
export type ValidateEditSessionResponse = z.infer<typeof validateEditSessionResponseSchema>;
export type ValidateProjectRequest = z.infer<typeof validateProjectRequestSchema>;
export type ValidateProjectResponse = z.infer<typeof validateProjectResponseSchema>;
export type WorkflowSummary = z.infer<typeof workflowSummarySchema>;
