/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import { getPokemonSpriteId, getPokemonSpriteIds } from './App';

const bundledStaticSpriteIds = new Set(
  Object.keys(import.meta.glob('../public/sprites/gen5/*.png', { eager: true })).map((filePath) =>
    filePath.replace(/^.*\/([^/]+)\.png$/, '$1')
  )
);

function hasBundledStaticSprite(label: string) {
  return getPokemonSpriteIds(label).some((spriteId) => bundledStaticSpriteIds.has(spriteId));
}

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

  it('maps Hisuian and Paldean region labels to bundled sprite ids', () => {
    expect(getPokemonSpriteId('Growlithe (Hisuian)')).toBe('growlithe-hisui');
    expect(getPokemonSpriteId('Typhlosion (Hisuian)')).toBe('typhlosion-hisui');
    expect(getPokemonSpriteId('Wooper (Paldean)')).toBe('wooper-paldea');
  });

  it('maps Scarlet and Violet form labels to bundled sprite ids', () => {
    expect(getPokemonSpriteId('Great Tusk')).toBe('greattusk');
    expect(getPokemonSpriteId('Scream Tail')).toBe('screamtail');
    expect(getPokemonSpriteId('Brute Bonnet')).toBe('brutebonnet');
    expect(getPokemonSpriteId('Flutter Mane')).toBe('fluttermane');
    expect(getPokemonSpriteId('Slither Wing')).toBe('slitherwing');
    expect(getPokemonSpriteId('Sandy Shocks')).toBe('sandyshocks');
    expect(getPokemonSpriteId('Roaring Moon')).toBe('roaringmoon');
    expect(getPokemonSpriteId('Iron Treads')).toBe('irontreads');
    expect(getPokemonSpriteId('Iron Bundle')).toBe('ironbundle');
    expect(getPokemonSpriteId('Iron Hands')).toBe('ironhands');
    expect(getPokemonSpriteId('Iron Jugulis')).toBe('ironjugulis');
    expect(getPokemonSpriteId('Iron Moth')).toBe('ironmoth');
    expect(getPokemonSpriteId('Iron Thorns')).toBe('ironthorns');
    expect(getPokemonSpriteId('Iron Valiant')).toBe('ironvaliant');
    expect(getPokemonSpriteId('Walking Wake')).toBe('walkingwake');
    expect(getPokemonSpriteId('Iron Leaves')).toBe('ironleaves');
    expect(getPokemonSpriteId('Gouging Fire')).toBe('gougingfire');
    expect(getPokemonSpriteId('Raging Bolt')).toBe('ragingbolt');
    expect(getPokemonSpriteId('Iron Boulder')).toBe('ironboulder');
    expect(getPokemonSpriteId('Iron Crown')).toBe('ironcrown');
    expect(getPokemonSpriteId('Wo-Chien')).toBe('wochien');
    expect(getPokemonSpriteId('Chien-Pao')).toBe('chienpao');
    expect(getPokemonSpriteId('Ting-Lu')).toBe('tinglu');
    expect(getPokemonSpriteId('Chi-Yu')).toBe('chiyu');
    expect(getPokemonSpriteId('Tauros (Paldean Combat Breed)')).toBe('tauros-paldeacombat');
    expect(getPokemonSpriteId('Tauros (Paldean Blaze Breed)')).toBe('tauros-paldeablaze');
    expect(getPokemonSpriteId('Tauros (Paldean Aqua Breed)')).toBe('tauros-paldeaaqua');
    expect(getPokemonSpriteId('Basculegion (Female)')).toBe('basculegion-f');
    expect(getPokemonSpriteId('Dudunsparce (Three-Segment Form)')).toBe(
      'dudunsparce-threesegment'
    );
    expect(getPokemonSpriteId('Maushold (Family of Four)')).toBe('maushold-four');
    expect(getPokemonSpriteId('Maushold (Family of Three)')).toBe('maushold');
    expect(getPokemonSpriteId('Squawkabilly (Blue Plumage)')).toBe('squawkabilly-blue');
    expect(getPokemonSpriteId('Gimmighoul (Roaming Form)')).toBe('gimmighoul-roaming');
    expect(getPokemonSpriteId('Ogerpon (Cornerstone Mask Terastallized)')).toBe(
      'ogerpon-cornerstonetera'
    );
    expect(getPokemonSpriteId('Terapagos (Stellar Form)')).toBe('terapagos-stellar');
    expect(getPokemonSpriteId('Poltchageist (Artisan Form)')).toBe('poltchageist-artisan');
    expect(getPokemonSpriteId('Sinistcha (Masterpiece Form)')).toBe('sinistcha-masterpiece');
  });

  it('resolves Scarlet and Violet form labels used by Pokemon and Wild editors to bundled static sprites', () => {
    const labels = [
      'Growlithe (Hisuian)',
      'Arcanine (Hisuian)',
      'Voltorb (Hisuian)',
      'Electrode (Hisuian)',
      'Tauros (Kantonian)',
      'Tauros (Paldean Combat Breed)',
      'Tauros (Paldean Blaze Breed)',
      'Tauros (Paldean Aqua Breed)',
      'Typhlosion (Hisuian)',
      'Wooper (Johtonian)',
      'Wooper (Paldean)',
      'Qwilfish (Hisuian)',
      'Sneasel (Hisuian)',
      'Samurott (Hisuian)',
      'Lilligant (Hisuian)',
      'Basculin (Red-Striped)',
      'Basculin (Blue-Striped)',
      'Basculin (White-Striped)',
      'Great Tusk',
      'Scream Tail',
      'Brute Bonnet',
      'Flutter Mane',
      'Slither Wing',
      'Sandy Shocks',
      'Roaring Moon',
      'Iron Treads',
      'Iron Bundle',
      'Iron Hands',
      'Iron Jugulis',
      'Iron Moth',
      'Iron Thorns',
      'Iron Valiant',
      'Walking Wake',
      'Iron Leaves',
      'Gouging Fire',
      'Raging Bolt',
      'Iron Boulder',
      'Iron Crown',
      'Wo-Chien',
      'Chien-Pao',
      'Ting-Lu',
      'Chi-Yu',
      'Zorua (Hisuian)',
      'Zoroark (Hisuian)',
      'Braviary (Hisuian)',
      'Sliggoo (Hisuian)',
      'Goodra (Hisuian)',
      'Avalugg (Hisuian)',
      'Decidueye (Hisuian)',
      'Oricorio (Pom-Pom Style)',
      'Oricorio (Pa\'u Style)',
      'Oricorio (Sensu Style)',
      'Minior (Blue Core)',
      'Ursaluna (Bloodmoon)',
      'Basculegion (Female)',
      'Enamorus (Therian Forme)',
      'Oinkologne (Female)',
      'Dudunsparce (Three-Segment Form)',
      'Palafin (Hero Form)',
      'Maushold (Family of Four)',
      'Maushold (Family of Three)',
      'Tatsugiri (Droopy Form)',
      'Tatsugiri (Stretchy Form)',
      'Squawkabilly (Blue Plumage)',
      'Squawkabilly (Yellow Plumage)',
      'Squawkabilly (White Plumage)',
      'Gimmighoul (Roaming Form)',
      'Koraidon (Sprinting Build)',
      'Miraidon (Drive Mode)',
      'Ogerpon (Wellspring Mask)',
      'Ogerpon (Hearthflame Mask)',
      'Ogerpon (Cornerstone Mask)',
      'Ogerpon (Wellspring Mask Terastallized)',
      'Ogerpon (Hearthflame Mask Terastallized)',
      'Ogerpon (Cornerstone Mask Terastallized)',
      'Terapagos (Terastal Form)',
      'Terapagos (Stellar Form)',
      'Poltchageist (Artisan Form)',
      'Sinistcha (Masterpiece Form)'
    ];

    for (const label of labels) {
      expect(hasBundledStaticSprite(label), label).toBe(true);
    }
  });
});
