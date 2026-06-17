/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import { getPokemonSpriteId, getPokemonSpriteIds } from './App';

describe('Pokemon sprite ids', () => {
  it('normalizes known hyphenated Pokemon sprite ids', () => {
    expect(getPokemonSpriteId('Kommo-o')).toBe('kommoo');
    expect(getPokemonSpriteId('Toxtricity (Low Key) (Gigantamax)')).toBe('toxtricity-gmax');
  });

  it('normalizes gendered Nidoran sprite ids to bundled static sprites', () => {
    expect(getPokemonSpriteId('Nidoran♀')).toBe('nidoranf');
    expect(getPokemonSpriteId('Nidoran♂')).toBe('nidoranm');
    expect(getPokemonSpriteId('Nidoran-F')).toBe('nidoranf');
    expect(getPokemonSpriteId('Nidoran-M')).toBe('nidoranm');
    expect(getPokemonSpriteId('Nidoran (Female)')).toBe('nidoranf');
    expect(getPokemonSpriteId('Nidoran (Male)')).toBe('nidoranm');
    expect(getPokemonSpriteId('Nidorina')).toBe('nidorina');
  });

  it('normalizes punctuation and accent Pokemon names to bundled sprite ids', () => {
    expect(getPokemonSpriteId('Flabébé')).toBe('flabebe');
    expect(getPokemonSpriteId('Ho-Oh')).toBe('hooh');
    expect(getPokemonSpriteId('Mime Jr.')).toBe('mimejr');
    expect(getPokemonSpriteId('Mr. Mime')).toBe('mrmime');
    expect(getPokemonSpriteId('Mr. Mime (Galarian)')).toBe('mrmime-galar');
    expect(getPokemonSpriteId('Mr. Rime')).toBe('mrrime');
    expect(getPokemonSpriteId('Tapu Bulu')).toBe('tapubulu');
    expect(getPokemonSpriteId('Tapu Fini')).toBe('tapufini');
    expect(getPokemonSpriteId('Tapu Koko')).toBe('tapukoko');
    expect(getPokemonSpriteId('Tapu Lele')).toBe('tapulele');
    expect(getPokemonSpriteId('Type: Null')).toBe('typenull');
  });

  it('normalizes legendary form labels to bundled sprite ids', () => {
    expect(getPokemonSpriteId('Necrozma (Dusk Mane)')).toBe('necrozma-duskmane');
    expect(getPokemonSpriteId('Necrozma (Dawn Wings)')).toBe('necrozma-dawnwings');
    expect(getPokemonSpriteId('Necrozma (Ultra Necrozma)')).toBe('necrozma-ultra');
    expect(getPokemonSpriteIds('Giratina (Origin Forme)')).toEqual([
      'giratina-origin-forme',
      'giratina-origin',
      'giratina'
    ]);
  });

  it('falls back from form-specific Pokemon sprite ids to the base species id', () => {
    expect(getPokemonSpriteIds('Tornadus (Therian Forme)')).toEqual([
      'tornadus-therian-forme',
      'tornadus-therian',
      'tornadus'
    ]);
  });

  it('maps gender-form Pokemon names to the bundled gender sprite ids', () => {
    expect(getPokemonSpriteId('Frillish (Male)')).toBe('frillish');
    expect(getPokemonSpriteId('Frillish (Female)')).toBe('frillish-f');
    expect(getPokemonSpriteId('Jellicent (Female)')).toBe('jellicent-f');
    expect(getPokemonSpriteId('Indeedee (Female)')).toBe('indeedee-f');
    expect(getPokemonSpriteId('Meowstic (Female)')).toBe('meowstic-f');
    expect(getPokemonSpriteId('Unfezant (Female)')).toBe('unfezant-f');
  });
});
