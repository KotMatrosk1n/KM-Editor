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

export const battleCafeRewardRowCount = 23;
export const battleCafeRewardMaximumItemOptions = 10_000;

const battleCafeItemIdSchema = z.number().int().min(1).max(65_535);
const battleCafePercentSchema = z.number().int().min(0).max(100);
const battleCafeTextSchema = z.string().min(1).max(256).refine((value) => value.trim().length > 0);

export const battleCafeRewardRowSchema = z.strictObject({
  rowIndex: z.number().int().min(1).max(battleCafeRewardRowCount),
  itemId: battleCafeItemIdSchema,
  itemName: battleCafeTextSchema,
  dwightPercent: battleCafePercentSchema,
  bernardPercent: battleCafePercentSchema,
  richardPercent: battleCafePercentSchema
});

export const battleCafeRewardItemOptionSchema = z.strictObject({
  itemId: battleCafeItemIdSchema,
  name: battleCafeTextSchema,
  category: battleCafeTextSchema
});

export const battleCafeRewardTotalsSchema = z.strictObject({
  dwightPercent: z.number().int().min(0).max(2_300),
  bernardPercent: z.number().int().min(0).max(2_300),
  richardPercent: z.number().int().min(0).max(2_300)
});

export const battleCafeRewardsProvenanceSchema = z.strictObject({
  sourceFile: z.literal('romfs/bin/script/amx/sub_event_011.amx'),
  sourceLayer: projectFileLayerSchema,
  fileState: projectFileGraphEntryStateSchema
});

export const battleCafeRewardsWorkflowSchema = z.strictObject({
  summary: workflowSummarySchema,
  rewards: z.array(battleCafeRewardRowSchema).length(battleCafeRewardRowCount),
  itemOptions: z.array(battleCafeRewardItemOptionSchema).max(battleCafeRewardMaximumItemOptions),
  totals: battleCafeRewardTotalsSchema,
  provenance: battleCafeRewardsProvenanceSchema.nullable(),
  diagnostics: z.array(apiDiagnosticSchema).max(256)
});

export const battleCafeRewardRowEditSchema = z.strictObject({
  rowIndex: z.number().int().min(1).max(battleCafeRewardRowCount),
  expectedItemId: battleCafeItemIdSchema,
  expectedDwightPercent: battleCafePercentSchema,
  expectedBernardPercent: battleCafePercentSchema,
  expectedRichardPercent: battleCafePercentSchema,
  itemId: battleCafeItemIdSchema,
  dwightPercent: battleCafePercentSchema,
  bernardPercent: battleCafePercentSchema,
  richardPercent: battleCafePercentSchema
});

export const loadBattleCafeRewardsWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadBattleCafeRewardsWorkflowResponseSchema = z.strictObject({
  workflow: battleCafeRewardsWorkflowSchema
});

export const stageBattleCafeRewardRowsRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  rows: z.array(battleCafeRewardRowEditSchema).min(1).max(battleCafeRewardRowCount)
}).superRefine((request, context) => {
  const seen = new Set<number>();
  for (const row of request.rows) {
    if (seen.has(row.rowIndex)) {
      context.addIssue({
        code: z.ZodIssueCode.custom,
        message: 'Battle Cafe reward row indexes must be unique.',
        path: ['rows']
      });
      return;
    }
    seen.add(row.rowIndex);
  }
});

export const stageBattleCafeRewardRowsResponseSchema = z.strictObject({
  workflow: battleCafeRewardsWorkflowSchema,
  session: editSessionSchema,
  diagnostics: z.array(apiDiagnosticSchema).max(256)
});

export type BattleCafeRewardItemOption = z.infer<typeof battleCafeRewardItemOptionSchema>;
export type BattleCafeRewardRow = z.infer<typeof battleCafeRewardRowSchema>;
export type BattleCafeRewardRowEdit = z.infer<typeof battleCafeRewardRowEditSchema>;
export type BattleCafeRewardTotals = z.infer<typeof battleCafeRewardTotalsSchema>;
export type BattleCafeRewardsWorkflow = z.infer<typeof battleCafeRewardsWorkflowSchema>;
export type LoadBattleCafeRewardsWorkflowRequest = z.infer<typeof loadBattleCafeRewardsWorkflowRequestSchema>;
export type LoadBattleCafeRewardsWorkflowResponse = z.infer<typeof loadBattleCafeRewardsWorkflowResponseSchema>;
export type StageBattleCafeRewardRowsRequest = z.infer<typeof stageBattleCafeRewardRowsRequestSchema>;
export type StageBattleCafeRewardRowsResponse = z.infer<typeof stageBattleCafeRewardRowsResponseSchema>;
