/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import {
  isWorkflowNavigationVisibleForGame,
  workflowNavigationGroups
} from './workflowGameSupport';
import type { WorkbenchSection } from './workbenchStore';

describe('workflow game support', () => {
  it('routes Rental Pokemon only for Sword and Shield', () => {
    const availableSections = new Set<WorkbenchSection>(['rentalPokemon']);
    const encounterGroup = workflowNavigationGroups.find(
      (group) => group.id === 'encountersPokemonSources'
    );

    expect(encounterGroup?.sectionIds).toContain('rentalPokemon');
    expect(
      isWorkflowNavigationVisibleForGame('rentalPokemon', 'sword', availableSections)
    ).toBe(true);
    expect(
      isWorkflowNavigationVisibleForGame('rentalPokemon', 'shield', availableSections)
    ).toBe(true);
    expect(
      isWorkflowNavigationVisibleForGame('rentalPokemon', 'scarlet', availableSections)
    ).toBe(false);
    expect(
      isWorkflowNavigationVisibleForGame('rentalPokemon', 'violet', availableSections)
    ).toBe(false);
    expect(
      isWorkflowNavigationVisibleForGame('rentalPokemon', 'za', availableSections)
    ).toBe(false);
  });
});
