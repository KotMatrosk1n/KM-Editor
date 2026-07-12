/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import type {
  ItemsWorkflow,
  PokemonEvolutionMethodOption,
  PokemonRecord,
  PokemonWorkflow
} from '../../bridge/contracts';
import { formatPokemonEvolutionPendingValue } from './pokemonPendingEditFormatting';

describe('Pokemon pending evolution formatting', () => {
  it.each([
    ['Z-A', '8|248|26|0|0'],
    ['Scarlet and Violet', '8|248|26|0|0']
  ])('shows a staged custom evolution item for %s pipe payloads', (_, pendingValue) => {
    const context = createContext({
      argumentOptions: [],
      itemId: 248,
      itemName: 'Twisted Spoon'
    });

    expect(
      formatPokemonEvolutionPendingValue(
        pendingValue,
        { action: 'upsert', slot: 1 },
        context
      )
    ).toBe('008 Use Item / Item 248 Twisted Spoon / 026 Raichu / Form 0 / Lv. 0');
  });

  it('keeps Sword and Shield colon payloads and method item options working', () => {
    const context = createContext({
      argumentOptions: [{ label: '83 Thunder Stone', value: 83 }],
      itemId: 83,
      itemName: 'Thunder Stone'
    });

    expect(
      formatPokemonEvolutionPendingValue(
        '8:83:26:0:0',
        { action: 'upsert', slot: 1 },
        context
      )
    ).toBe('008 Use Item / Item 83 Thunder Stone / 026 Raichu / Form 0 / Lv. 0');
  });
});

function createContext({
  argumentOptions,
  itemId,
  itemName
}: {
  argumentOptions: PokemonEvolutionMethodOption['argumentOptions'];
  itemId: number;
  itemName: string;
}) {
  const evolutionMethodOptions: PokemonEvolutionMethodOption[] = [
    {
      argumentKind: 'item',
      argumentLabel: 'Item',
      argumentOptions,
      label: '008 Use Item',
      value: 8
    }
  ];
  const pikachu = {
    evolutions: [
      {
        argument: itemId,
        argumentKind: 'item',
        argumentLabel: 'Item',
        argumentValue: '',
        form: 0,
        level: 0,
        method: 8,
        methodName: 'Use Item',
        slot: 1,
        species: 26
      }
    ],
    name: 'Pikachu',
    personalId: 33,
    speciesId: 25
  } as unknown as PokemonRecord;
  const raichu = {
    evolutions: [],
    name: 'Raichu',
    personalId: 44,
    speciesId: 26
  } as unknown as PokemonRecord;
  const pokemonWorkflow = {
    evolutionMethodOptions,
    pokemon: [pikachu, raichu]
  } as unknown as PokemonWorkflow;
  const itemsWorkflow = {
    items: [{ itemId, name: itemName }]
  } as unknown as ItemsWorkflow;

  return { itemsWorkflow, pokemon: pikachu, pokemonWorkflow };
}
