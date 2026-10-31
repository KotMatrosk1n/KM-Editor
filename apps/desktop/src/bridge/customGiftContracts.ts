/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { apiDiagnosticSchema, editSessionSchema, giftPokemonEditableFieldSchema, giftPokemonIvsSchema,
  projectPathsSchema, type EditSession } from './contracts';

export const customGiftPokemonSchema = z.strictObject({
  speciesId: z.int().min(1).max(65535), form: z.int().min(0).max(255), level: z.int().min(1).max(100),
  isEgg: z.boolean(), heldItemId: z.int().min(0).max(65535), ballItemId: z.int().min(0).max(65535),
  ability: z.int().min(0).max(3), nature: z.int().min(0).max(25), gender: z.int().min(0).max(2),
  shinyLock: z.int().min(0).max(2), dynamaxLevel: z.int().min(0).max(10), canGigantamax: z.boolean(),
  specialMoveId: z.int().min(0).max(65535), ivs: giftPokemonIvsSchema, flawlessIvCount: z.union([z.literal(0), z.literal(3), z.literal(6)]),
  genderRatio: z.int().min(0).max(255), abilityMask: z.int().min(1).max(7)
});
export const codeGiftSchema = z.strictObject({
  claimId: z.int().min(1800).max(1999), name: z.string().min(1).max(80).refine(value => value.trim().length > 0 && !/\p{Cc}/u.test(value)),
  code: z.string().regex(/^[A-Z0-9]{5,16}$/).refine(value => /[^0]/.test(value)), oncePerSave: z.boolean(), pokemon: customGiftPokemonSchema
});
export const maximumRoyalGiftPoolSize = 14;
export function royalGiftChanceWeight(chance: number): number | null {
  const weight = Math.round(chance * 100);
  return Number.isFinite(chance) && chance >= 0 && chance <= 100 && chance === weight / 100 ? weight : null;
}
export function parseRoyalGiftChance(raw: string): number | null {
  const normalized = raw.trim().replace(',', '.');
  if (!/^\d+(?:\.\d{1,2})?$/.test(normalized)) return null;
  const chance = Number(normalized);
  return royalGiftChanceWeight(chance) === null ? null : chance;
}
const equalRoyalGiftChances = (count: number) => Array.from({ length: count }, (_, index) =>
  (Math.floor(10000 / count) + (index < 10000 % count ? 1 : 0)) / 100);
export const royalGiftPoolSchema = z.strictObject({ badges: z.int().min(0).max(8), pokemon: z.array(customGiftPokemonSchema).max(128),
  chances: z.array(z.number().min(0).max(100).refine(chance => royalGiftChanceWeight(chance) !== null)).max(128).optional()
}).transform(pool => ({ ...pool, chances: pool.chances ?? equalRoyalGiftChances(pool.pokemon.length) }));
export function royalGiftPoolIsValid(pool: z.infer<typeof royalGiftPoolSchema>): boolean {
  const weights = pool.chances.map(royalGiftChanceWeight);
  return pool.pokemon.length <= maximumRoyalGiftPoolSize && pool.chances.length === pool.pokemon.length
    && weights.every(weight => weight !== null)
    && (pool.pokemon.length === 0 || weights.reduce<number>((total, weight) => total + (weight ?? 0), 0) === 10000);
}
const customGiftCatalogDataSchema = z.strictObject({
  version: z.literal(1), mode: z.enum(['none', 'mystery', 'royal']), nextClaimId: z.int().min(1800).max(2000),
  codeGifts: z.array(codeGiftSchema).max(128), royalPools: z.array(royalGiftPoolSchema).length(9)
}).refine(catalog => new Set(catalog.codeGifts.map(gift => gift.claimId)).size === catalog.codeGifts.length
  && new Set(catalog.codeGifts.map(gift => gift.code)).size === catalog.codeGifts.length
  && catalog.codeGifts.every(gift => gift.claimId < catalog.nextClaimId)
  && new Set(catalog.royalPools.map(pool => pool.badges)).size === 9
  && catalog.royalPools.reduce((sum, pool) => sum + pool.pokemon.length, 0) <= 128
  && catalog.royalPools.every(pool => pool.chances.length === pool.pokemon.length)
  && (catalog.mode !== 'mystery' || catalog.codeGifts.length > 0)
  && (catalog.mode !== 'royal' || catalog.royalPools.some(pool => pool.pokemon.length > 0)));
export const customGiftCatalogSchema = customGiftCatalogDataSchema.refine(catalog => catalog.royalPools.every(royalGiftPoolIsValid));
export const customGiftWorkflowSchema = z.strictObject({
  canEdit: z.boolean(), installedMode: z.enum(['none', 'mystery', 'royal']), needsRepair: z.boolean(),
  catalog: customGiftCatalogDataSchema, editableFields: z.array(giftPokemonEditableFieldSchema), diagnostics: z.array(apiDiagnosticSchema)
});
export const loadCustomGiftsRequestSchema = z.strictObject({ paths: projectPathsSchema });
export const stageCustomGiftsRequestSchema = z.strictObject({ paths: projectPathsSchema, catalog: customGiftCatalogSchema, session: editSessionSchema.nullable() });
export const loadCustomGiftsResponseSchema = z.strictObject({ workflow: customGiftWorkflowSchema });
export const stageCustomGiftsResponseSchema = z.strictObject({ workflow: customGiftWorkflowSchema, session: editSessionSchema, diagnostics: z.array(apiDiagnosticSchema) });
export type CustomGiftPokemon = z.infer<typeof customGiftPokemonSchema>;
export type CustomGiftCatalog = z.infer<typeof customGiftCatalogSchema>;
export type CustomGiftWorkflow = z.infer<typeof customGiftWorkflowSchema>;
export type CodeGift = z.infer<typeof codeGiftSchema>;
export type LoadCustomGiftsRequest = z.infer<typeof loadCustomGiftsRequestSchema>;
export type LoadCustomGiftsResponse = z.infer<typeof loadCustomGiftsResponseSchema>;
export type StageCustomGiftsRequest = z.infer<typeof stageCustomGiftsRequestSchema>;
export type StageCustomGiftsResponse = z.infer<typeof stageCustomGiftsResponseSchema>;

export function getCustomGiftPendingCatalog(session: EditSession | null): CustomGiftCatalog | null {
  const edits = session?.pendingEdits.filter(edit => edit.domain === 'workflow.customGifts') ?? [];
  if (edits.length !== 1 || !session?.hasPendingChanges || !session.sessionId.trim()) return null;
  const edit = edits[0];
  if (edit.recordId !== 'custom-gifts' || edit.field !== 'catalog' || edit.summary !== 'Stage custom gift catalog.'
    || edit.sources.length !== 1 || edit.sources[0].layer !== 'pending'
    || !/^pending\/custom-gifts\/catalog\/[A-F0-9]{64}$/.test(edit.sources[0].relativePath)) return null;
  try { const result = customGiftCatalogSchema.safeParse(JSON.parse(edit.newValue ?? '')); return result.success ? result.data : null; }
  catch { return null; }
}

// The receiver derives these two values from the current personal data when staging.
export function customGiftCatalogMatches(left: CustomGiftCatalog, right: CustomGiftCatalog): boolean {
  const comparable = (catalog: CustomGiftCatalog) => ({ ...catalog,
    codeGifts: catalog.codeGifts.map(gift => ({ ...gift, pokemon: authored(gift.pokemon) })),
    royalPools: catalog.royalPools.map(pool => ({ ...pool, pokemon: pool.pokemon.map(authored) })) });
  const authored = ({ genderRatio: _genderRatio, abilityMask: _abilityMask, ...pokemon }: CustomGiftPokemon) => pokemon;
  return JSON.stringify(comparable(left)) === JSON.stringify(comparable(right));
}
