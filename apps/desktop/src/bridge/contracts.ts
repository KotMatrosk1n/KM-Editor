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
  'trainers.load',
  'shops.load',
  'encounters.load',
  'raidRewards.load',
  'placement.load',
  'flagworkSave.load',
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
  loadTrainersWorkflow: 'trainers.load',
  loadShopsWorkflow: 'shops.load',
  loadEncountersWorkflow: 'encounters.load',
  loadRaidRewardsWorkflow: 'raidRewards.load',
  loadPlacementWorkflow: 'placement.load',
  loadFlagworkSaveWorkflow: 'flagworkSave.load',
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

export const loadTrainersWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadShopsWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadEncountersWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadRaidRewardsWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadPlacementWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadFlagworkSaveWorkflowRequestSchema = z.strictObject({
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

export const trainerProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const trainerPokemonRecordSchema = z.strictObject({
  heldItem: z.string().nullable(),
  level: z.number().int().nonnegative(),
  moves: z.array(z.string()),
  slot: z.number().int().nonnegative(),
  species: z.string()
});

export const trainerRecordSchema = z.strictObject({
  battleType: z.string(),
  location: z.string(),
  name: z.string(),
  provenance: trainerProvenanceSchema,
  team: z.array(trainerPokemonRecordSchema),
  trainerClass: z.string(),
  trainerId: z.number().int().nonnegative()
});

export const trainersWorkflowStatsSchema = z.strictObject({
  sourceFileCount: z.number().int().nonnegative(),
  totalPokemonCount: z.number().int().nonnegative(),
  totalTrainerCount: z.number().int().nonnegative()
});

export const trainersWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  stats: trainersWorkflowStatsSchema,
  summary: workflowSummarySchema,
  trainers: z.array(trainerRecordSchema)
});

export const loadTrainersWorkflowResponseSchema = z.strictObject({
  workflow: trainersWorkflowSchema
});

export const shopProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const shopInventoryRecordSchema = z.strictObject({
  itemId: z.number().int().nonnegative(),
  itemName: z.string(),
  price: z.number().int().nonnegative(),
  slot: z.number().int().nonnegative(),
  stockLimit: z.number().int().nonnegative().nullable()
});

export const shopRecordSchema = z.strictObject({
  currency: z.string(),
  inventory: z.array(shopInventoryRecordSchema),
  location: z.string(),
  name: z.string(),
  provenance: shopProvenanceSchema,
  shopId: z.string()
});

export const shopsWorkflowStatsSchema = z.strictObject({
  sourceFileCount: z.number().int().nonnegative(),
  totalInventoryItemCount: z.number().int().nonnegative(),
  totalShopCount: z.number().int().nonnegative()
});

export const shopsWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  shops: z.array(shopRecordSchema),
  stats: shopsWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadShopsWorkflowResponseSchema = z.strictObject({
  workflow: shopsWorkflowSchema
});

export const encounterProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const encounterSlotRecordSchema = z.strictObject({
  levelMax: z.number().int().nonnegative(),
  levelMin: z.number().int().nonnegative(),
  slot: z.number().int().nonnegative(),
  species: z.string(),
  timeOfDay: z.string().nullable(),
  weather: z.string(),
  weight: z.number().int().nonnegative()
});

export const encounterTableRecordSchema = z.strictObject({
  area: z.string(),
  encounterType: z.string(),
  gameVersion: z.string(),
  location: z.string(),
  provenance: encounterProvenanceSchema,
  slots: z.array(encounterSlotRecordSchema),
  tableId: z.string()
});

export const encountersWorkflowStatsSchema = z.strictObject({
  sourceFileCount: z.number().int().nonnegative(),
  totalSlotCount: z.number().int().nonnegative(),
  totalTableCount: z.number().int().nonnegative()
});

export const encountersWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  stats: encountersWorkflowStatsSchema,
  summary: workflowSummarySchema,
  tables: z.array(encounterTableRecordSchema)
});

export const loadEncountersWorkflowResponseSchema = z.strictObject({
  workflow: encountersWorkflowSchema
});

export const raidRewardProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const raidRewardItemRecordSchema = z.strictObject({
  itemId: z.number().int().nonnegative(),
  itemName: z.string(),
  quantity: z.number().int().nonnegative(),
  slot: z.number().int().nonnegative(),
  weight: z.number().int().nonnegative()
});

export const raidRewardTableRecordSchema = z.strictObject({
  denId: z.string(),
  gameVersion: z.string(),
  provenance: raidRewardProvenanceSchema,
  rank: z.number().int().nonnegative(),
  rewards: z.array(raidRewardItemRecordSchema),
  tableId: z.string()
});

export const raidRewardsWorkflowStatsSchema = z.strictObject({
  sourceFileCount: z.number().int().nonnegative(),
  totalRewardItemCount: z.number().int().nonnegative(),
  totalTableCount: z.number().int().nonnegative()
});

export const raidRewardsWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  stats: raidRewardsWorkflowStatsSchema,
  summary: workflowSummarySchema,
  tables: z.array(raidRewardTableRecordSchema)
});

export const loadRaidRewardsWorkflowResponseSchema = z.strictObject({
  workflow: raidRewardsWorkflowSchema
});

export const placementProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const placedObjectRecordSchema = z.strictObject({
  label: z.string(),
  map: z.string(),
  objectId: z.string(),
  objectType: z.string(),
  provenance: placementProvenanceSchema,
  rotationY: z.number(),
  scriptId: z.string().nullable(),
  x: z.number(),
  y: z.number(),
  z: z.number()
});

export const placementWorkflowStatsSchema = z.strictObject({
  sourceFileCount: z.number().int().nonnegative(),
  totalObjectCount: z.number().int().nonnegative()
});

export const placementWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  objects: z.array(placedObjectRecordSchema),
  stats: placementWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadPlacementWorkflowResponseSchema = z.strictObject({
  workflow: placementWorkflowSchema
});

export const flagworkSaveProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const flagRecordSchema = z.strictObject({
  category: z.string(),
  defaultValue: z.string(),
  description: z.string(),
  flagId: z.string(),
  name: z.string(),
  provenance: flagworkSaveProvenanceSchema,
  valueKind: z.string()
});

export const saveBlockRecordSchema = z.strictObject({
  blockId: z.string(),
  description: z.string(),
  length: z.number().int().nonnegative(),
  name: z.string(),
  offset: z.number().int().nonnegative(),
  provenance: flagworkSaveProvenanceSchema
});

export const flagworkSaveWorkflowStatsSchema = z.strictObject({
  sourceFileCount: z.number().int().nonnegative(),
  totalFlagCount: z.number().int().nonnegative(),
  totalSaveBlockCount: z.number().int().nonnegative()
});

export const flagworkSaveWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  flags: z.array(flagRecordSchema),
  saveBlocks: z.array(saveBlockRecordSchema),
  stats: flagworkSaveWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadFlagworkSaveWorkflowResponseSchema = z.strictObject({
  workflow: flagworkSaveWorkflowSchema
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
export type TrainersWorkflow = z.infer<typeof trainersWorkflowSchema>;
export type ShopsWorkflow = z.infer<typeof shopsWorkflowSchema>;
export type EncountersWorkflow = z.infer<typeof encountersWorkflowSchema>;
export type RaidRewardsWorkflow = z.infer<typeof raidRewardsWorkflowSchema>;
export type PlacementWorkflow = z.infer<typeof placementWorkflowSchema>;
export type FlagworkSaveWorkflow = z.infer<typeof flagworkSaveWorkflowSchema>;
export type ListWorkflowsRequest = z.infer<typeof listWorkflowsRequestSchema>;
export type ListWorkflowsResponse = z.infer<typeof listWorkflowsResponseSchema>;
export type LoadItemsWorkflowRequest = z.infer<typeof loadItemsWorkflowRequestSchema>;
export type LoadItemsWorkflowResponse = z.infer<typeof loadItemsWorkflowResponseSchema>;
export type LoadTextWorkflowRequest = z.infer<typeof loadTextWorkflowRequestSchema>;
export type LoadTextWorkflowResponse = z.infer<typeof loadTextWorkflowResponseSchema>;
export type LoadTrainersWorkflowRequest = z.infer<typeof loadTrainersWorkflowRequestSchema>;
export type LoadTrainersWorkflowResponse = z.infer<typeof loadTrainersWorkflowResponseSchema>;
export type LoadShopsWorkflowRequest = z.infer<typeof loadShopsWorkflowRequestSchema>;
export type LoadShopsWorkflowResponse = z.infer<typeof loadShopsWorkflowResponseSchema>;
export type LoadEncountersWorkflowRequest = z.infer<typeof loadEncountersWorkflowRequestSchema>;
export type LoadEncountersWorkflowResponse = z.infer<typeof loadEncountersWorkflowResponseSchema>;
export type LoadRaidRewardsWorkflowRequest = z.infer<typeof loadRaidRewardsWorkflowRequestSchema>;
export type LoadRaidRewardsWorkflowResponse = z.infer<typeof loadRaidRewardsWorkflowResponseSchema>;
export type LoadPlacementWorkflowRequest = z.infer<typeof loadPlacementWorkflowRequestSchema>;
export type LoadPlacementWorkflowResponse = z.infer<typeof loadPlacementWorkflowResponseSchema>;
export type LoadFlagworkSaveWorkflowRequest = z.infer<typeof loadFlagworkSaveWorkflowRequestSchema>;
export type LoadFlagworkSaveWorkflowResponse = z.infer<typeof loadFlagworkSaveWorkflowResponseSchema>;
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
