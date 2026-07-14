/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import { getSectionWikiUrl } from './wikiLinks';
import {
  isWorkflowSupportedForGame,
  workflowNavigationGroups
} from './workflowGameSupport';

const supportedGames = ['sword', 'shield', 'scarlet', 'violet', 'za'] as const;
const workflowNavigationSectionIds = workflowNavigationGroups.flatMap(
  (group) => group.sectionIds
);

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
    expect(getSectionWikiUrl('typeChart', 'violet')).toBe(
      'https://github.com/KotMatrosk1n/KM-Editor/wiki/Scarlet-and-Violet-Type-Chart'
    );
  });

  it('links Legends Z-A sections to Z-A wiki pages when available', () => {
    expect(getSectionWikiUrl('pokemon', 'za')).toBe(
      'https://github.com/KotMatrosk1n/KM-Editor/wiki/Legends-Z-A-Pokemon-Editor'
    );
    expect(getSectionWikiUrl('encounters', 'za')).toBe(
      'https://github.com/KotMatrosk1n/KM-Editor/wiki/Legends-Z-A-Wild-Encounters-Editor'
    );
    expect(getSectionWikiUrl('gameDump', 'za')).toBe(
      'https://github.com/KotMatrosk1n/KM-Editor/wiki/Legends-Z-A-Game-Dump'
    );
    expect(getSectionWikiUrl('spreadsheetImport', 'za')).toBe(
      'https://github.com/KotMatrosk1n/KM-Editor/wiki/Legends-Z-A-Dump-Importer'
    );
    expect(getSectionWikiUrl('text', 'za')).toBe(
      'https://github.com/KotMatrosk1n/KM-Editor/wiki/Text-Viewer'
    );
  });

  it('links shared tool pages to the current wiki page names', () => {
    expect(getSectionWikiUrl('gameDump', 'sword')).toBe(
      'https://github.com/KotMatrosk1n/KM-Editor/wiki/Game-Dump'
    );
    expect(getSectionWikiUrl('spreadsheetImport', 'shield')).toBe(
      'https://github.com/KotMatrosk1n/KM-Editor/wiki/Dump-Importer'
    );
  });

  it('links Settings to its canonical wiki page', () => {
    expect(getSectionWikiUrl('settings', 'sword')).toBe(
      'https://github.com/KotMatrosk1n/KM-Editor/wiki/Settings'
    );
  });

  it.each(supportedGames)('links every workflow supported for %s', (game) => {
    const sectionsWithoutWikiPages = workflowNavigationSectionIds.filter(
      (section) =>
        isWorkflowSupportedForGame(section, game) && getSectionWikiUrl(section, game) === null
    );

    expect(sectionsWithoutWikiPages).toEqual([]);
  });
});
