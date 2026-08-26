/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import {
  apiDiagnosticSchema,
  editSessionSchema,
  projectPathsSchema
} from './contracts';

export const trainerPoolMemberSchema = z.strictObject({
  appearanceAssetId: z.string(),
  displayName: z.string(),
  rawRosterId: z.string(),
  rawTrainerId: z.string(),
  rosterIndex: z.number().int().nonnegative(),
  storedRank: z.number().int(),
  teamSize: z.number().int().nonnegative(),
  weight: z.number().int().nonnegative()
});

export const trainerPoolRecordSchema = z.strictObject({
  compatibilityGroup: z.string().min(1),
  displayLabel: z.string().min(1),
  kind: z.enum(['story', 'infinity']),
  logicalPoolId: z.string(),
  memberCount: z.number().int().nonnegative(),
  members: z.array(trainerPoolMemberSchema),
  physicalTableIds: z.array(z.string()),
  referencedPhysicalTableCount: z.number().int().nonnegative(),
  totalWeight: z.number().int().nonnegative()
});

export const trainerPoolsWorkflowSchema = z.strictObject({
  canStage: z.boolean(),
  diagnostics: z.array(apiDiagnosticSchema),
  pools: z.array(trainerPoolRecordSchema),
  stats: z.strictObject({
    dormantPhysicalMirrorCount: z.number().int().nonnegative(),
    logicalPoolCount: z.number().int().nonnegative(),
    memberReferenceCount: z.number().int().nonnegative(),
    physicalMirrorCount: z.number().int().nonnegative()
  })
});

export const loadTrainerPoolsWorkflowRequestSchema = z.strictObject({
  paths: projectPathsSchema
});

export const loadTrainerPoolsWorkflowResponseSchema = z.strictObject({
  workflow: trainerPoolsWorkflowSchema
});

export const stageTrainerPoolFixedCountSwapRequestSchema = z.strictObject({
  destinationLogicalPoolId: z.string().min(1),
  destinationRawTrainerId: z.string().min(1),
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  sourceLogicalPoolId: z.string().min(1),
  sourceRawTrainerId: z.string().min(1)
});

export const stageTrainerPoolFixedCountSwapResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: trainerPoolsWorkflowSchema
});

export type TrainerPoolMember = z.infer<typeof trainerPoolMemberSchema>;
export type TrainerPoolRecord = z.infer<typeof trainerPoolRecordSchema>;
export type TrainerPoolsWorkflow = z.infer<typeof trainerPoolsWorkflowSchema>;
export type LoadTrainerPoolsWorkflowRequest = z.infer<
  typeof loadTrainerPoolsWorkflowRequestSchema
>;
export type LoadTrainerPoolsWorkflowResponse = z.infer<
  typeof loadTrainerPoolsWorkflowResponseSchema
>;
export type StageTrainerPoolFixedCountSwapRequest = z.infer<
  typeof stageTrainerPoolFixedCountSwapRequestSchema
>;
export type StageTrainerPoolFixedCountSwapResponse = z.infer<
  typeof stageTrainerPoolFixedCountSwapResponseSchema
>;
