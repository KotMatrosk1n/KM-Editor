/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { apiDiagnosticSchema, editSessionSchema, projectGameSchema, projectPathsSchema, type EditSession } from './contracts';
import { fairyGymBoostSelectionSchema, type FairyGymBoostSelection } from './fairyGymBoostsContracts';
import { isSupportedFairyGymBoostOutcome } from '../features/fairy-gym-boosts/fairyGymBoostsPending';
import { calculatePendingPayloadSha256 } from '../utils/pendingPayloadHash';

export const marnieBoostIds = [1, 2, 3].flatMap(battle => [1, 2].map(answer => `marnie-${battle}-${answer}`));
const selectionSchema = fairyGymBoostSelectionSchema.refine(value =>
  isSupportedFairyGymBoostOutcome(value.effectId, value.resultKind));
const selectionsSchema = z.array(selectionSchema).length(6).refine(values =>
  values.every((value, index) => value.boostId === marnieBoostIds[index]));
export const marnieBoostsWorkflowSchema = z.strictObject({
  canEdit: z.boolean(), detectedGame: projectGameSchema.nullable(), selections: z.array(selectionSchema),
  sources: z.array(z.strictObject({ path: z.string(), status: z.enum(['available', 'blocked']),
    layer: z.enum(['base', 'layered', 'missing']) })).length(3), diagnostics: z.array(apiDiagnosticSchema)
}).refine(workflow => workflow.sources.every((source, index) =>
  source.path === `romfs/bin/battle/waza/sequence/bk${135 + index}.bseq`) &&
  workflow.selections.every((selection, index, values) => marnieBoostIds.includes(selection.boostId) &&
    values.findIndex(value => value.boostId === selection.boostId) === index) &&
  (!workflow.canEdit || ((workflow.detectedGame === 'sword' || workflow.detectedGame === 'shield') &&
    workflow.sources.every(source => source.status === 'available') && selectionsSchema.safeParse(workflow.selections).success)));
export const loadMarnieBoostsRequestSchema = z.strictObject({ paths: projectPathsSchema });
export const stageMarnieBoostsRequestSchema = z.strictObject({ paths: projectPathsSchema,
  selections: selectionsSchema, session: editSessionSchema.nullable() });
export const loadMarnieBoostsResponseSchema = z.strictObject({ workflow: marnieBoostsWorkflowSchema });
export const stageMarnieBoostsResponseSchema = z.strictObject({ workflow: marnieBoostsWorkflowSchema,
  session: editSessionSchema, diagnostics: z.array(apiDiagnosticSchema) });
export type MarnieBoostsWorkflow = z.infer<typeof marnieBoostsWorkflowSchema>;
export type LoadMarnieBoostsRequest = z.infer<typeof loadMarnieBoostsRequestSchema>;
export type LoadMarnieBoostsResponse = z.infer<typeof loadMarnieBoostsResponseSchema>;
export type StageMarnieBoostsRequest = z.infer<typeof stageMarnieBoostsRequestSchema>;
export type StageMarnieBoostsResponse = z.infer<typeof stageMarnieBoostsResponseSchema>;

export function encodeMarnieBoostSelections(selections: readonly FairyGymBoostSelection[]) {
  return selections.map(value => `${value.boostId}:${value.effectId}:${value.resultKind}`).join(';');
}

export function getMarnieBoostPendingSelections(session: EditSession | null): FairyGymBoostSelection[] | null {
  const edits = session?.pendingEdits.filter(edit => edit.domain === 'workflow.marnieBoosts') ?? [];
  if (edits.length !== 1 || !session?.hasPendingChanges || !session.sessionId.trim()) return null;
  const edit = edits[0];
  const result = selectionsSchema.safeParse(edit.newValue?.split(';').map(entry => {
    const [boostId, effect, resultKind, extra] = entry.split(':');
    return { boostId, effectId: extra === undefined ? Number(effect) : NaN, resultKind };
  }));
  if (!result.success || edit.recordId !== 'marnie-wyndon-boosts' || edit.field !== 'boostSelections' ||
    edit.summary !== 'Stage Marnie Wyndon cheering outcomes.' ||
    edit.newValue !== encodeMarnieBoostSelections(result.data) || edit.sources.length !== 1 ||
    edit.sources[0].layer !== 'pending' || edit.sources[0].relativePath !==
      `pending/marnie-boosts/selections/${calculatePendingPayloadSha256(edit.newValue)}`) return null;
  return result.data;
}
