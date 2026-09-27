/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { apiDiagnosticSchema, editSessionSchema, projectGameSchema, projectPathsSchema, type EditSession } from './contracts';
import { calculatePendingPayloadSha256 } from '../utils/pendingPayloadHash';

export const defaultHeldItemRates = [50, 5, 0, 60, 20, 0];
export const heldItemRatesSchema = z.array(z.number().int().min(0).max(100)).length(6).refine(rates =>
  rates.slice(0, 3).reduce((sum, rate) => sum + rate, 0) <= 100 &&
  rates.slice(3).reduce((sum, rate) => sum + rate, 0) <= 100);
export const heldItemChanceWorkflowSchema = z.strictObject({
  canEdit: z.boolean(), detectedGame: projectGameSchema.nullable(), rates: z.array(z.number().int()),
  sourceLayer: z.enum(['base', 'layered', 'missing']), diagnostics: z.array(apiDiagnosticSchema)
}).refine(workflow => (workflow.rates.length === 0 && !workflow.canEdit || heldItemRatesSchema.safeParse(workflow.rates).success)
  && (!workflow.canEdit || (workflow.detectedGame === 'sword' || workflow.detectedGame === 'shield') && workflow.sourceLayer !== 'missing'));
export const loadHeldItemChanceRequestSchema = z.strictObject({ paths: projectPathsSchema });
export const stageHeldItemChanceRequestSchema = z.strictObject({ paths: projectPathsSchema,
  rates: heldItemRatesSchema, session: editSessionSchema.nullable() });
export const loadHeldItemChanceResponseSchema = z.strictObject({ workflow: heldItemChanceWorkflowSchema });
export const stageHeldItemChanceResponseSchema = z.strictObject({ workflow: heldItemChanceWorkflowSchema,
  session: editSessionSchema, diagnostics: z.array(apiDiagnosticSchema) });
export type HeldItemChanceWorkflow = z.infer<typeof heldItemChanceWorkflowSchema>;
export type LoadHeldItemChanceRequest = z.infer<typeof loadHeldItemChanceRequestSchema>;
export type LoadHeldItemChanceResponse = z.infer<typeof loadHeldItemChanceResponseSchema>;
export type StageHeldItemChanceRequest = z.infer<typeof stageHeldItemChanceRequestSchema>;
export type StageHeldItemChanceResponse = z.infer<typeof stageHeldItemChanceResponseSchema>;
export const encodeHeldItemRates = (rates: readonly number[]) => rates.join(',');

export function getHeldItemPendingRates(session: EditSession | null): number[] | null {
  const edits = session?.pendingEdits.filter(edit => edit.domain === 'workflow.heldItemChance') ?? [];
  if (edits.length !== 1 || !session?.hasPendingChanges || !session.sessionId.trim()) return null;
  const edit = edits[0];
  const result = heldItemRatesSchema.safeParse(edit.newValue?.split(',').map(value => /^\d+$/.test(value) ? Number(value) : NaN));
  if (!result.success || edit.recordId !== 'global-held-items' || edit.field !== 'rates' ||
    edit.summary !== 'Stage global held item percentages.' || edit.newValue !== encodeHeldItemRates(result.data) ||
    edit.sources.length !== 1 || edit.sources[0].layer !== 'pending' || edit.sources[0].relativePath !==
      `pending/held-item-chance/rates/${calculatePendingPayloadSha256(edit.newValue)}`) return null;
  return result.data;
}

export function parseHeldItemRateDrafts(values: readonly string[]): number[] | null {
  const result = heldItemRatesSchema.safeParse(values.map(value => /^\d{1,3}$/.test(value) ? Number(value) : NaN));
  return result.success ? result.data : null;
}
