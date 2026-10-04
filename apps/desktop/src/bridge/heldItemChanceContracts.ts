/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { apiDiagnosticSchema, editSessionSchema, projectGameSchema, projectPathsSchema, type EditSession } from './contracts';
import { calculatePendingPayloadSha256 } from '../utils/pendingPayloadHash';

export const defaultHeldItemRates = [50, 5, 0, 60, 20, 0];
export const heldItemRatesSchema = z.array(z.number().int().min(0).max(100)).length(6).refine(rates =>
  rates.slice(0, 3).reduce((sum, rate) => sum + rate, 0) <= 100 &&
  rates.slice(3).reduce((sum, rate) => sum + rate, 0) <= 100);
export const heldItemChanceUpdateSchema = z.strictObject({ personalId: z.number().int().min(1),
  items: z.array(z.number().int().min(0).max(65535)).length(3), rates: heldItemRatesSchema.nullable() });
export type HeldItemChanceUpdate = z.infer<typeof heldItemChanceUpdateSchema>;
export const heldItemChanceWorkflowSchema = z.strictObject({
  canEdit: z.boolean(), detectedGame: projectGameSchema.nullable(), rates: z.array(z.number().int()),
  sourceLayer: z.enum(['base', 'layered', 'missing']), diagnostics: z.array(apiDiagnosticSchema),
  pokemon: z.array(z.strictObject({ personalId: z.number().int().min(1), species: z.number().int().min(1),
    form: z.number().int().min(0), name: z.string(), formLabel: z.string(), type1: z.string(), type2: z.string(), items: z.array(z.number().int()).length(3),
    rates: heldItemRatesSchema, customRates: z.boolean() })),
  itemOptions: z.array(z.strictObject({ value: z.number().int(), label: z.string() }))
}).refine(workflow => (workflow.rates.length === 0 && !workflow.canEdit || heldItemRatesSchema.safeParse(workflow.rates).success)
  && (!workflow.canEdit || (workflow.detectedGame === 'sword' || workflow.detectedGame === 'shield') && workflow.sourceLayer !== 'missing'));
export const loadHeldItemChanceRequestSchema = z.strictObject({ paths: projectPathsSchema });
export const stageHeldItemChanceRequestSchema = z.strictObject({ paths: projectPathsSchema,
  rates: heldItemRatesSchema.optional(), pokemon: z.array(heldItemChanceUpdateSchema).min(1).max(2048).optional(),
  session: editSessionSchema.nullable() }).refine(request => (request.rates === undefined) !== (request.pokemon === undefined));
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
  const edits = session?.pendingEdits.filter(edit => edit.domain === 'workflow.heldItemChance' && edit.recordId === 'global-held-items') ?? [];
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

export function getHeldItemPokemonState(row: HeldItemChanceWorkflow['pokemon'][number], session: EditSession | null, fallbackRates: number[] = defaultHeldItemRates) {
  const edit = session?.pendingEdits.find(edit => edit.domain === 'workflow.heldItemChance' && edit.recordId === String(row.personalId));
  const values = edit?.newValue?.split(',').map(Number);
  const valid = edit?.field === 'rates' && edit.summary === 'Stage Pokemon held item percentages.' && edit.newValue != null
    && edit.sources.length === 1 && edit.sources[0].layer === 'pending'
    && edit.sources[0].relativePath === `pending/held-item-chance/pokemon/${calculatePendingPayloadSha256(edit.newValue)}`
    && values?.[0] === row.species && values[1] === row.form;
  const rates = valid && values ? parseHeldItemRateDrafts(values.slice(2).map(String)) : null;
  const reset = valid && values?.length === 3 && values[2] === -1;
  const globalRates = getHeldItemPendingRates(session);
  return { ...row, customRates: rates !== null ? true : reset ? false : row.customRates,
    rates: rates ?? (reset || !row.customRates ? globalRates ?? (reset ? fallbackRates : row.rates) : row.rates),
    items: row.items.map((item, index) => {
      const pending = session?.pendingEdits.find(edit => edit.domain === 'workflow.pokemon' && edit.recordId === String(row.personalId)
        && edit.field === `heldItem${index + 1}`);
      return pending?.newValue != null && /^\d+$/.test(pending.newValue) ? Number(pending.newValue) : item;
    }) };
}
