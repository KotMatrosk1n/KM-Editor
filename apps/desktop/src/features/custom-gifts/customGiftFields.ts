/* SPDX-License-Identifier: GPL-3.0-only */
import type { CustomGiftPokemon } from '../../bridge/customGiftContracts';

const ivKeys = { ivHp: 'hp', ivAttack: 'attack', ivDefense: 'defense', ivSpecialAttack: 'specialAttack',
  ivSpecialDefense: 'specialDefense', ivSpeed: 'speed' } as const;
export function customGiftFieldValue(pokemon: CustomGiftPokemon, field: string): number {
  if (field in ivKeys) return pokemon.ivs[ivKeys[field as keyof typeof ivKeys]];
  if (field === 'species') return pokemon.speciesId;
  const value = pokemon[field as keyof CustomGiftPokemon];
  return typeof value === 'boolean' ? Number(value) : typeof value === 'number' ? value : 0;
}
export function updateCustomGiftField(pokemon: CustomGiftPokemon, field: string, value: number): CustomGiftPokemon {
  if (field in ivKeys) return { ...pokemon, ivs: { ...pokemon.ivs, [ivKeys[field as keyof typeof ivKeys]]: value } };
  if (field === 'species') return { ...pokemon, speciesId: value, form: 0, ability: 0, gender: 0 };
  if (field === 'form') return { ...pokemon, form: value, ability: 0, gender: 0 };
  if (field === 'canGigantamax') return { ...pokemon, canGigantamax: value === 1 };
  if (field === 'flawlessIvCount' && value === -9999) return { ...pokemon, flawlessIvCount: 0,
    ivs: { ...pokemon.ivs, hp: pokemon.ivs.hp === -4 ? -1 : pokemon.ivs.hp } };
  if (field === 'flawlessIvCount' && (value === 0 || value === 3 || value === 6)) return { ...pokemon, flawlessIvCount: value,
    ivs: { hp: -1, attack: -1, defense: -1, speed: -1, specialAttack: -1, specialDefense: -1 } };
  return { ...pokemon, [field]: value };
}

export const defaultCustomGiftPokemon = (): CustomGiftPokemon => ({
  speciesId: 25, form: 0, level: 5, isEgg: false, heldItemId: 0, ballItemId: 4, ability: 0, nature: 25,
  gender: 0, shinyLock: 2, dynamaxLevel: 0, canGigantamax: false, specialMoveId: 0,
  ivs: { hp: -1, attack: -1, defense: -1, specialAttack: -1, specialDefense: -1, speed: -1 },
  flawlessIvCount: 0, genderRatio: 127, abilityMask: 3
});
