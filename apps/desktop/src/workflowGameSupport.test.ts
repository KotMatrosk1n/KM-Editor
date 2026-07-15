/* SPDX-License-Identifier: GPL-3.0-only */

import { describe, expect, it } from 'vitest';
import {
  canAccessWorkflowSectionForHealth,
  isWorkflowNavigationVisibleForGame,
  readOnlyViewerSectionIds,
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

  it('routes ExeFS Patches for Sword and Shield and identifies read-only viewers', () => {
    const availableSections = new Set<WorkbenchSection>(['exefsPatches', 'flagworkSave']);
    const advancedGroup = workflowNavigationGroups.find(
      (group) => group.id === 'advancedEditors'
    );

    expect(advancedGroup?.sectionIds).toContain('exefsPatches');
    expect(readOnlyViewerSectionIds).toEqual(new Set(['flagworkSave']));
    expect(canAccessWorkflowSectionForHealth('flagworkSave', true, false)).toBe(true);
    expect(canAccessWorkflowSectionForHealth('items', true, false)).toBe(false);
    expect(canAccessWorkflowSectionForHealth('items', true, true)).toBe(true);
    expect(
      isWorkflowNavigationVisibleForGame('exefsPatches', 'sword', availableSections)
    ).toBe(true);
    expect(
      isWorkflowNavigationVisibleForGame('exefsPatches', 'shield', availableSections)
    ).toBe(true);
    expect(
      isWorkflowNavigationVisibleForGame('exefsPatches', 'scarlet', availableSections)
    ).toBe(false);
  });

  it('routes Starting Items only for Sword and Shield', () => {
    const availableSections = new Set<WorkbenchSection>(['startingItems']);
    const advancedGroup = workflowNavigationGroups.find(
      (group) => group.id === 'advancedEditors'
    );

    expect(advancedGroup?.sectionIds).toContain('startingItems');
    expect(
      isWorkflowNavigationVisibleForGame('startingItems', 'sword', availableSections)
    ).toBe(true);
    expect(
      isWorkflowNavigationVisibleForGame('startingItems', 'shield', availableSections)
    ).toBe(true);
    expect(
      isWorkflowNavigationVisibleForGame('startingItems', 'scarlet', availableSections)
    ).toBe(false);
    expect(
      isWorkflowNavigationVisibleForGame('startingItems', 'violet', availableSections)
    ).toBe(false);
    expect(
      isWorkflowNavigationVisibleForGame('startingItems', 'za', availableSections)
    ).toBe(false);
  });
});
