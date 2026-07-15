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

export const loadNpcItemGiftWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

const npcItemGiftIdentifierSchema = z
  .string()
  .min(1)
  .max(160)
  .regex(/^[A-Za-z0-9][A-Za-z0-9._-]*$/);
const npcItemGiftLabelSchema = z
  .string()
  .min(1)
  .max(300)
  .refine((value) => value.trim().length > 0);
const npcItemGiftPathSchema = z
  .string()
  .min(1)
  .max(1024)
  .refine((value) => value.trim().length > 0);
const npcItemGiftSemanticTextSchema = z.string().min(1);
const npcItemGiftStatusSchema = z.enum([
  'available',
  'repairable',
  'damaged',
  'missing'
]);
const npcItemGiftQuantitySchema = z.number().int().min(1).max(999);
const npcItemGiftPackedIntegerSchema = z
  .number()
  .int()
  .min(-2_147_483_648)
  .max(2_147_483_647);

export const npcItemGiftProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: npcItemGiftPathSchema,
  sourceLayer: projectFileLayerSchema
});

export const npcItemGiftSourceRecordSchema = z.strictObject({
  label: npcItemGiftLabelSchema,
  provenance: npcItemGiftProvenanceSchema,
  relativePath: npcItemGiftPathSchema,
  sourceId: npcItemGiftIdentifierSchema,
  status: npcItemGiftStatusSchema
});

export const npcItemGiftItemOptionRecordSchema = z.strictObject({
  category: npcItemGiftSemanticTextSchema,
  isKeyItem: z.boolean(),
  itemId: z.number().int().positive(),
  name: npcItemGiftSemanticTextSchema
});

export const npcItemGiftItemSlotRecordSchema = z.strictObject({
  itemCell: z.number().int().nonnegative(),
  itemId: npcItemGiftPackedIntegerSchema,
  itemName: npcItemGiftSemanticTextSchema,
  label: npcItemGiftLabelSchema,
  slotId: npcItemGiftIdentifierSchema,
  vanillaItemId: z.number().int().positive(),
  vanillaItemName: npcItemGiftSemanticTextSchema
});

export const npcItemGiftRecordSchema = z.strictObject({
  canEditQuantity: z.boolean(),
  displayOrder: z.number().int().nonnegative(),
  giftId: npcItemGiftIdentifierSchema,
  items: z.array(npcItemGiftItemSlotRecordSchema).min(1).max(16),
  label: npcItemGiftLabelSchema,
  location: npcItemGiftLabelSchema,
  npcId: npcItemGiftIdentifierSchema,
  npcName: npcItemGiftLabelSchema,
  provenance: npcItemGiftProvenanceSchema,
  quantity: npcItemGiftPackedIntegerSchema,
  quantityCell: z.number().int().nonnegative().nullable(),
  relativePath: npcItemGiftPathSchema,
  status: npcItemGiftStatusSchema,
  vanillaQuantity: npcItemGiftQuantitySchema
});

export const npcItemGiftNpcGroupSchema = z.strictObject({
  displayOrder: z.number().int().nonnegative(),
  gifts: z.array(npcItemGiftRecordSchema).min(1),
  npcId: npcItemGiftIdentifierSchema,
  npcName: npcItemGiftLabelSchema
});

export const npcItemGiftItemSelectionSchema = z.strictObject({
  itemId: npcItemGiftPackedIntegerSchema,
  slotId: npcItemGiftIdentifierSchema
});

export const npcItemGiftSelectionSchema = z.strictObject({
  giftId: npcItemGiftIdentifierSchema,
  items: z.array(npcItemGiftItemSelectionSchema).min(1).max(16),
  quantity: npcItemGiftPackedIntegerSchema
});

export const npcItemGiftWorkflowStatsSchema = z.strictObject({
  giftCount: z.number().int().nonnegative(),
  itemOptionCount: z.number().int().nonnegative(),
  npcCount: z.number().int().nonnegative(),
  sourceFileCount: z.number().int().nonnegative()
});

export const npcItemGiftWorkflowSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  itemOptions: z.array(npcItemGiftItemOptionRecordSchema),
  npcs: z.array(npcItemGiftNpcGroupSchema),
  sources: z.array(npcItemGiftSourceRecordSchema),
  stats: npcItemGiftWorkflowStatsSchema,
  summary: workflowSummarySchema
});

export const loadNpcItemGiftWorkflowResponseSchema = z.strictObject({
  workflow: npcItemGiftWorkflowSchema
});

export const stageNpcItemGiftRequestSchema = z.strictObject({
  gifts: z.array(npcItemGiftSelectionSchema).min(1),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable()
});

export const stageNpcItemGiftResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: npcItemGiftWorkflowSchema
});

export type LoadNpcItemGiftWorkflowRequest = z.infer<
  typeof loadNpcItemGiftWorkflowRequestSchema
>;
export type LoadNpcItemGiftWorkflowResponse = z.infer<
  typeof loadNpcItemGiftWorkflowResponseSchema
>;
export type NpcItemGiftItemOptionRecord = z.infer<
  typeof npcItemGiftItemOptionRecordSchema
>;
export type NpcItemGiftItemSelection = z.infer<typeof npcItemGiftItemSelectionSchema>;
export type NpcItemGiftItemSlotRecord = z.infer<typeof npcItemGiftItemSlotRecordSchema>;
export type NpcItemGiftNpcGroup = z.infer<typeof npcItemGiftNpcGroupSchema>;
export type NpcItemGiftRecord = z.infer<typeof npcItemGiftRecordSchema>;
export type NpcItemGiftSelection = z.infer<typeof npcItemGiftSelectionSchema>;
export type NpcItemGiftWorkflow = z.infer<typeof npcItemGiftWorkflowSchema>;
export type StageNpcItemGiftRequest = z.infer<typeof stageNpcItemGiftRequestSchema>;
export type StageNpcItemGiftResponse = z.infer<typeof stageNpcItemGiftResponseSchema>;
