/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import {
  apiDiagnosticSchema,
  editSessionSchema,
  encountersWorkflowSchema,
  giftPokemonWorkflowSchema,
  itemsWorkflowSchema,
  movesWorkflowSchema,
  placedObjectRecordSchema,
  placementWorkflowSchema,
  pokemonWorkflowSchema,
  projectPathsSchema,
  tradePokemonWorkflowSchema,
  trainersWorkflowSchema
} from './contracts';

export const pokemonFieldUpdateSchema = z.strictObject({
  field: z.string(),
  personalId: z.number().int().nonnegative(),
  value: z.string()
});

export const updatePokemonFieldsRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  updates: z.array(pokemonFieldUpdateSchema)
});

export const updatePokemonFieldsResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: pokemonWorkflowSchema
});

export const itemFieldUpdateSchema = z.strictObject({
  field: z.string(),
  itemId: z.number().int().nonnegative(),
  value: z.string()
});

export const updateItemFieldsRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  updates: z.array(itemFieldUpdateSchema)
});

export const updateItemFieldsResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: itemsWorkflowSchema
});

export const moveFieldUpdateSchema = z.strictObject({
  field: z.string(),
  moveId: z.number().int().nonnegative(),
  value: z.string()
});

export const updateMoveFieldsRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  updates: z.array(moveFieldUpdateSchema)
});

export const updateMoveFieldsResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: movesWorkflowSchema
});

export const trainerFieldUpdateSchema = z.strictObject({
  field: z.string(),
  slot: z.number().int().nonnegative().nullable(),
  trainerId: z.number().int().nonnegative(),
  value: z.string()
});

export const updateTrainerFieldsRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  updates: z.array(trainerFieldUpdateSchema)
});

export const updateTrainerFieldsResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: trainersWorkflowSchema
});

export const giftPokemonFieldUpdateSchema = z.strictObject({
  field: z.string(),
  giftIndex: z.number().int().nonnegative(),
  value: z.string()
});

export const updateGiftPokemonFieldsRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  updates: z.array(giftPokemonFieldUpdateSchema)
});

export const updateGiftPokemonFieldsResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: giftPokemonWorkflowSchema
});

export const tradePokemonFieldUpdateSchema = z.strictObject({
  field: z.string(),
  tradeIndex: z.number().int().nonnegative(),
  value: z.string()
});

export const updateTradePokemonFieldsRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  updates: z.array(tradePokemonFieldUpdateSchema)
});

export const updateTradePokemonFieldsResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: tradePokemonWorkflowSchema
});

export const encounterSlotFieldUpdateSchema = z.strictObject({
  field: z.string(),
  slot: z.number().int().nonnegative(),
  tableId: z.string(),
  value: z.string()
});

export const updateEncounterSlotFieldsRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  updates: z.array(encounterSlotFieldUpdateSchema)
});

export const updateEncounterSlotFieldsResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  workflow: encountersWorkflowSchema
});

export const placementObjectFieldUpdateSchema = z.strictObject({
  field: z.string(),
  objectId: z.string(),
  value: z.string()
});

export const updatePlacementObjectFieldsRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  session: editSessionSchema.nullable(),
  updates: z.array(placementObjectFieldUpdateSchema)
});

export const updatePlacementObjectFieldsResponseSchema = z.strictObject({
  diagnostics: z.array(apiDiagnosticSchema),
  session: editSessionSchema,
  updatedObjects: z.array(placedObjectRecordSchema).nullable().optional(),
  workflow: placementWorkflowSchema.nullable()
});

export type PokemonFieldUpdate = z.infer<typeof pokemonFieldUpdateSchema>;
export type UpdatePokemonFieldsRequest = z.infer<typeof updatePokemonFieldsRequestSchema>;
export type UpdatePokemonFieldsResponse = z.infer<typeof updatePokemonFieldsResponseSchema>;
export type MoveFieldUpdate = z.infer<typeof moveFieldUpdateSchema>;
export type UpdateMoveFieldsRequest = z.infer<typeof updateMoveFieldsRequestSchema>;
export type UpdateMoveFieldsResponse = z.infer<typeof updateMoveFieldsResponseSchema>;
export type ItemFieldUpdate = z.infer<typeof itemFieldUpdateSchema>;
export type UpdateItemFieldsRequest = z.infer<typeof updateItemFieldsRequestSchema>;
export type UpdateItemFieldsResponse = z.infer<typeof updateItemFieldsResponseSchema>;
export type TrainerFieldUpdate = z.infer<typeof trainerFieldUpdateSchema>;
export type UpdateTrainerFieldsRequest = z.infer<typeof updateTrainerFieldsRequestSchema>;
export type UpdateTrainerFieldsResponse = z.infer<typeof updateTrainerFieldsResponseSchema>;
export type GiftPokemonFieldUpdate = z.infer<typeof giftPokemonFieldUpdateSchema>;
export type UpdateGiftPokemonFieldsRequest = z.infer<
  typeof updateGiftPokemonFieldsRequestSchema
>;
export type UpdateGiftPokemonFieldsResponse = z.infer<
  typeof updateGiftPokemonFieldsResponseSchema
>;
export type TradePokemonFieldUpdate = z.infer<typeof tradePokemonFieldUpdateSchema>;
export type UpdateTradePokemonFieldsRequest = z.infer<
  typeof updateTradePokemonFieldsRequestSchema
>;
export type UpdateTradePokemonFieldsResponse = z.infer<
  typeof updateTradePokemonFieldsResponseSchema
>;
export type EncounterSlotFieldUpdate = z.infer<typeof encounterSlotFieldUpdateSchema>;
export type UpdateEncounterSlotFieldsRequest = z.infer<
  typeof updateEncounterSlotFieldsRequestSchema
>;
export type UpdateEncounterSlotFieldsResponse = z.infer<
  typeof updateEncounterSlotFieldsResponseSchema
>;
export type PlacementObjectFieldUpdate = z.infer<typeof placementObjectFieldUpdateSchema>;
export type UpdatePlacementObjectFieldsRequest = z.infer<
  typeof updatePlacementObjectFieldsRequestSchema
>;
export type UpdatePlacementObjectFieldsResponse = z.infer<
  typeof updatePlacementObjectFieldsResponseSchema
>;
