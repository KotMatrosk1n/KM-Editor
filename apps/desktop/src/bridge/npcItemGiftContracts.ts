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

export const npcItemGiftProvenanceSchema = z.strictObject({
  fileState: projectFileGraphEntryStateSchema,
  sourceFile: z.string(),
  sourceLayer: projectFileLayerSchema
});

export const npcItemGiftSourceRecordSchema = z.strictObject({
  label: z.string(),
  provenance: npcItemGiftProvenanceSchema,
  relativePath: z.string(),
  sourceId: z.string(),
  status: z.string()
});

export const npcItemGiftItemOptionRecordSchema = z.strictObject({
  category: z.string(),
  isKeyItem: z.boolean(),
  itemId: z.number().int().nonnegative(),
  name: z.string()
});

export const npcItemGiftItemSlotRecordSchema = z.strictObject({
  itemCell: z.number().int().nonnegative(),
  itemId: z.number().int().nonnegative(),
  itemName: z.string(),
  label: z.string(),
  slotId: z.string(),
  vanillaItemId: z.number().int().nonnegative(),
  vanillaItemName: z.string()
});

export const npcItemGiftRecordSchema = z.strictObject({
  displayOrder: z.number().int(),
  giftId: z.string(),
  items: z.array(npcItemGiftItemSlotRecordSchema),
  label: z.string(),
  location: z.string(),
  npcId: z.string(),
  npcName: z.string(),
  provenance: npcItemGiftProvenanceSchema,
  quantity: z.number().int().positive(),
  quantityCell: z.number().int().nonnegative(),
  relativePath: z.string(),
  vanillaQuantity: z.number().int().positive()
});

export const npcItemGiftNpcGroupSchema = z.strictObject({
  displayOrder: z.number().int(),
  gifts: z.array(npcItemGiftRecordSchema),
  npcId: z.string(),
  npcName: z.string()
});

export const npcItemGiftItemSelectionSchema = z.strictObject({
  itemId: z.number().int().nonnegative(),
  slotId: z.string()
});

export const npcItemGiftSelectionSchema = z.strictObject({
  giftId: z.string(),
  items: z.array(npcItemGiftItemSelectionSchema),
  quantity: z.number().int().positive()
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
  gifts: z.array(npcItemGiftSelectionSchema),
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
