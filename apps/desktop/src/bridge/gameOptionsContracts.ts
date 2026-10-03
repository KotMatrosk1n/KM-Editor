/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { apiDiagnosticSchema, editSessionSchema, projectGameSchema, projectPathsSchema, type EditSession } from './contracts';
import { calculatePendingPayloadSha256 } from '../utils/pendingPayloadHash';

export const gameOptionsCatalog = [
  { id: 'textSpeed', choices: ['slow', 'normal', 'fast'], retail: 1 },
  { id: 'characters', choices: ['hiragana', 'kanji'], retail: 0 },
  { id: 'battleEffects', choices: ['on', 'off'], retail: 0 },
  { id: 'battleStyle', choices: ['switch', 'set'], retail: 0 },
  { id: 'sendToBoxes', choices: ['manual', 'automatic'], retail: 0 },
  { id: 'giveNicknames', choices: ['give', 'dontGive'], retail: 0 },
  { id: 'gyroscope', choices: ['use', 'dontUse'], retail: 0 },
  { id: 'verticalCamera', choices: ['regular', 'inverted'], retail: 0 },
  { id: 'horizontalCamera', choices: ['regular', 'inverted'], retail: 0 },
  { id: 'autosave', choices: ['on', 'off'], retail: 0 },
  { id: 'casualControls', choices: ['on', 'off'], retail: 1 },
  { id: 'showNicknames', choices: ['show', 'dontShow'], retail: 0 },
  { id: 'skipMovies', choices: ['on', 'off'], retail: 1 },
  { id: 'music', choices: Array.from({ length: 11 }, (_, value) => String(value)), retail: 8 },
  { id: 'soundEffects', choices: Array.from({ length: 11 }, (_, value) => String(value)), retail: 8 },
  { id: 'cries', choices: Array.from({ length: 11 }, (_, value) => String(value)), retail: 8 }
] as const;
export const defaultGameOptionsSelections = Array<number>(60).fill(0);
export const encodeGameOptionsSelections = (values: readonly number[]) => values.join(',');

export function normalizeGameOptionsSelections(values: readonly number[]): number[] | null {
  if (values.length !== 60 || values.some(value => !Number.isInteger(value) || value < 0 || value > 6 || (value & 3) === 3)) return null;
  const result = [...values];
  let offset = 0;
  for (const option of gameOptionsCatalog) {
    const choices = result.slice(offset, offset + option.choices.length);
    const enabled = choices.flatMap((value, index) => (value & 3) === 1 ? [index] : []);
    if (enabled.length > 1 || enabled.length === 1 && (choices[enabled[0]] & 4) !== 0) return null;
    if (enabled.length === 1) choices.forEach((value, index) => {
      if (index !== enabled[0]) choices[index] = (value & 4) | 2;
    });
    if (!choices.some(value => value === 0 || value === 1)) return null;
    result.splice(offset, choices.length, ...choices);
    offset += choices.length;
  }
  return result;
}

export const gameOptionsSelectionsSchema = z.array(z.number().int().min(0).max(6)).length(60)
  .refine(values => normalizeGameOptionsSelections(values) !== null);
export const gameOptionsWorkflowSchema = z.strictObject({
  canEdit: z.boolean(), detectedGame: projectGameSchema.nullable(), selections: z.array(z.number().int()),
  sourceLayer: z.enum(['base', 'layered', 'missing']), diagnostics: z.array(apiDiagnosticSchema)
}).refine(workflow => (workflow.selections.length === 0 && !workflow.canEdit || gameOptionsSelectionsSchema.safeParse(workflow.selections).success
    && encodeGameOptionsSelections(normalizeGameOptionsSelections(workflow.selections)!) === encodeGameOptionsSelections(workflow.selections))
  && (!workflow.canEdit || (workflow.detectedGame === 'sword' || workflow.detectedGame === 'shield') && workflow.sourceLayer !== 'missing'));
export const loadGameOptionsRequestSchema = z.strictObject({ paths: projectPathsSchema });
export const stageGameOptionsRequestSchema = z.strictObject({ paths: projectPathsSchema,
  selections: gameOptionsSelectionsSchema, session: editSessionSchema.nullable() });
export const loadGameOptionsResponseSchema = z.strictObject({ workflow: gameOptionsWorkflowSchema });
export const stageGameOptionsResponseSchema = z.strictObject({ workflow: gameOptionsWorkflowSchema,
  session: editSessionSchema, diagnostics: z.array(apiDiagnosticSchema) });
export type GameOptionsWorkflow = z.infer<typeof gameOptionsWorkflowSchema>;
export type LoadGameOptionsRequest = z.infer<typeof loadGameOptionsRequestSchema>;
export type LoadGameOptionsResponse = z.infer<typeof loadGameOptionsResponseSchema>;
export type StageGameOptionsRequest = z.infer<typeof stageGameOptionsRequestSchema>;
export type StageGameOptionsResponse = z.infer<typeof stageGameOptionsResponseSchema>;

export function getGameOptionsPendingSelections(session: EditSession | null): number[] | null {
  const edits = session?.pendingEdits.filter(edit => edit.domain === 'workflow.gameOptions') ?? [];
  if (edits.length !== 1 || !session?.hasPendingChanges || !session.sessionId.trim()) return null;
  const edit = edits[0];
  const parsed = edit.newValue?.split(',').map(value => /^[0-6]$/.test(value) ? Number(value) : NaN) ?? [];
  const values = normalizeGameOptionsSelections(parsed);
  if (!values || edit.newValue !== encodeGameOptionsSelections(values) || edit.recordId !== 'game-options' || edit.field !== 'selections'
    || edit.summary !== 'Stage global game option selections.' || edit.sources.length !== 1 || edit.sources[0].layer !== 'pending'
    || edit.sources[0].relativePath !== `pending/game-options/selections/${calculatePendingPayloadSha256(edit.newValue)}`) return null;
  return values;
}
