/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import { getSectionWikiUrl } from './wikiLinks';

describe('wiki links', () => {
  it('links shared SwSh sections to their wiki pages', () => {
    expect(getSectionWikiUrl('pokemon', 'sword')).toBe(
      'https://github.com/KotMatrosk1n/KM-Editor/wiki/Pokemon-Editor'
    );
    expect(getSectionWikiUrl('encounters', 'shield')).toBe(
      'https://github.com/KotMatrosk1n/KM-Editor/wiki/Wild-Encounters-Editor'
    );
  });

  it('links Scarlet and Violet sections to S/V wiki pages when available', () => {
    expect(getSectionWikiUrl('pokemon', 'scarlet')).toBe(
      'https://github.com/KotMatrosk1n/KM-Editor/wiki/Scarlet-and-Violet-Pokemon-Editor'
    );
    expect(getSectionWikiUrl('encounters', 'violet')).toBe(
      'https://github.com/KotMatrosk1n/KM-Editor/wiki/Scarlet-and-Violet-Wild-Encounters-Editor'
    );
    expect(getSectionWikiUrl('hyperspaceBypass', 'scarlet')).toBe(
      'https://github.com/KotMatrosk1n/KM-Editor/wiki/Hyperspace-Bypass'
    );
  });

  it('hides the button for sections without a wiki page', () => {
    expect(getSectionWikiUrl('settings', 'sword')).toBeNull();
  });
});
